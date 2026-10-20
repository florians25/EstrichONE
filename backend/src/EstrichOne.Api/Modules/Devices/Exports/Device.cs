using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Infrastructure.Persistence;

namespace EstrichOne.Api.Modules.Devices.Exports;

public enum DevicePlatform
{
    Ios,
    Android,
    Windows,
    Other,
}

public enum DeviceKind
{
    Personal,
    Pool,
}

public enum DeviceStatus
{
    /// <summary>Registriert, wartet auf Freigabe – kein Zugriff auf Fachdaten.</summary>
    Pending,
    Active,
    /// <summary>Vorübergehend gesperrt, kann wieder entsperrt werden.</summary>
    Locked,
    /// <summary>Aus der Verwaltung entfernt; alle Sitzungen beendet.</summary>
    Retired,
}

/// <summary>
/// Registriertes Gerät (docs/reengineering/11, 13). Der private Schlüssel verlässt das Gerät nie
/// (Secure Enclave / Android Keystore); der Server kennt nur den öffentlichen Schlüssel.
/// </summary>
public sealed class Device : IVersionedEntity
{
    public const string RefreshProofPrefix = "EstrichONE refresh v1\n";
    public const string AttachProofPrefix = "EstrichONE attach v1\n";

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DevicePlatform Platform { get; private set; }
    public string? Model { get; private set; }
    public string? OsVersion { get; private set; }
    public string? AppVersion { get; private set; }
    public DeviceKind Kind { get; private set; }
    public DeviceStatus Status { get; private set; }

    /// <summary>Öffentlicher Schlüssel (P-256) im Format SubjectPublicKeyInfo (DER).</summary>
    public byte[] PublicKey { get; private set; } = [];
    public Guid? AssignedUserId { get; private set; }
    public Guid RegisteredByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private Device()
    {
    }

    public static Device Register(string name, DevicePlatform platform, DeviceKind kind, byte[] publicKey,
        Guid registeredByUserId, string? model, string? osVersion, string? appVersion, DateTimeOffset now)
    {
        if (!IsValidPublicKey(publicKey))
        {
            throw new ArgumentException("Der Geräteschlüssel ist kein gültiger öffentlicher P-256-Schlüssel.", nameof(publicKey));
        }

        return new Device
        {
            Id = Guid.CreateVersion7(now),
            Name = name.Trim(),
            Platform = platform,
            Kind = kind,
            Status = DeviceStatus.Pending,
            PublicKey = publicKey,
            AssignedUserId = kind == DeviceKind.Personal ? registeredByUserId : null,
            RegisteredByUserId = registeredByUserId,
            Model = model,
            OsVersion = osVersion,
            AppVersion = appVersion,
            LastSeenAt = now,
        };
    }

    public void Approve(Guid? approvedByUserId, DateTimeOffset now)
    {
        if (Status is DeviceStatus.Retired)
        {
            throw new InvalidOperationException("Ein entferntes Gerät kann nicht freigegeben werden.");
        }

        Status = DeviceStatus.Active;
        ApprovedAt = now;
        ApprovedByUserId = approvedByUserId;
    }

    public void Lock() => Status = Status == DeviceStatus.Retired ? Status : DeviceStatus.Locked;

    public void Unlock()
    {
        if (Status == DeviceStatus.Locked)
        {
            Status = ApprovedAt is null ? DeviceStatus.Pending : DeviceStatus.Active;
        }
    }

    public void Retire() => Status = DeviceStatus.Retired;

    public void Rename(string name) => Name = name.Trim();

    public void ReportVersions(string? osVersion, string? appVersion, DateTimeOffset now)
    {
        OsVersion = osVersion;
        AppVersion = appVersion;
        LastSeenAt = now;
    }

    /// <summary>Prüft die Signatur (ECDSA P-256, SHA-256, IEEE-P1363 oder DER) über <see cref="RefreshProofPrefix"/> + Refresh-Token.</summary>
    public bool VerifyRefreshProof(string refreshToken, byte[] signature) => Verify(RefreshProofPrefix + refreshToken, signature);

    /// <summary>Prüft die Signatur über <see cref="AttachProofPrefix"/> + Zugriffstoken der Sitzung, die gebunden werden soll.</summary>
    public bool VerifyAttachProof(string accessToken, byte[] signature) => Verify(AttachProofPrefix + accessToken, signature);

    /// <summary>Prüft eine Gerätesignatur über eine vollständige Nachricht mit eigenem Präfix (z. B. Meldung ohne Anmeldung).</summary>
    public bool VerifyProof(string message, byte[] signature) => Verify(message, signature);

    private bool Verify(string message, byte[] signature)
    {
        if (signature.Length == 0)
        {
            return false;
        }

        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(PublicKey, out _);
        var data = Encoding.UTF8.GetBytes(message);
        return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
            || key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    private static bool IsValidPublicKey(byte[] publicKey)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKey, out var read);
            return read == publicKey.Length && key.KeySize == 256;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
