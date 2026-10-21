using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Sync.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Documentation;

public enum AcceptanceResult
{
    Accepted,
    AcceptedWithReservations,
    Refused,
}

public enum AcceptanceState
{
    /// <summary>Erfasst, Unterschrift des Auftraggebers fehlt noch.</summary>
    Recorded,

    /// <summary>Link an den Kunden verschickt, Freigabe ausstehend.</summary>
    AwaitingCustomer,

    /// <summary>Unterschrieben bzw. freigegeben, PDF abgelegt.</summary>
    Completed,
}

public enum CustomerDecision
{
    Approved,
    Rejected,
}

/// <summary>
/// Digitales Abnahmeprotokoll (VOB/B § 12): Teilnehmer, abgenommene Leistung und Räume, Ergebnis, Mängel mit Frist,
/// Fotos, Notizen und Unterschriften beider Seiten – alternativ Freigabe durch den Kunden über einen Link.
/// Abgeschlossene Protokolle werden nicht mehr geändert; das PDF liegt in der Bauakte unter „Abnahmen“.
/// </summary>
public sealed class AcceptanceProtocol : IVersionedEntity
{
    public const int MaxSignatureBytes = 300_000;
    public const int MaxNotesLength = 100_000;

    public Guid Id { get; private set; }
    public Guid BauakteId { get; private set; }
    public Guid PerformedByUserId { get; private set; }
    public DateTimeOffset PerformedAt { get; private set; }
    public string? Location { get; private set; }
    public string? ClientName { get; private set; }
    public string? ContractorName { get; private set; }
    public string? Scope { get; private set; }
    public List<Guid> RoomIds { get; private set; } = [];
    public AcceptanceResult Result { get; private set; }
    public string? Notes { get; private set; }
    public List<Guid> PhotoFileIds { get; private set; } = [];
    public List<Guid> DefectWorkItemIds { get; private set; } = [];
    public byte[]? ClientSignature { get; private set; }
    public DateTimeOffset? ClientSignedAt { get; private set; }
    public byte[]? ContractorSignature { get; private set; }
    public DateTimeOffset? ContractorSignedAt { get; private set; }
    public AcceptanceState State { get; private set; }
    public byte[]? CustomerTokenHash { get; private set; }
    public DateTimeOffset? CustomerLinkExpiresAt { get; private set; }
    public string? CustomerEmail { get; private set; }
    public CustomerDecision? CustomerDecision { get; private set; }
    public DateTimeOffset? CustomerDecidedAt { get; private set; }
    public string? CustomerDecisionName { get; private set; }
    public string? CustomerComment { get; private set; }
    public string? CustomerIp { get; private set; }
    public Guid? PdfFileId { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private AcceptanceProtocol()
    {
    }

    public static AcceptanceProtocol Create(Guid id, Guid bauakteId, Guid userId, DateTimeOffset performedAt, string? location, string? clientName,
        string? contractorName, string? scope, IEnumerable<Guid> roomIds, AcceptanceResult result, string? notes, IEnumerable<Guid> photoFileIds,
        IEnumerable<Guid> defectWorkItemIds, byte[]? clientSignature, DateTimeOffset? clientSignedAt, byte[]? contractorSignature,
        DateTimeOffset? contractorSignedAt)
    {
        foreach (var signature in new[] { clientSignature, contractorSignature })
        {
            if (signature is { Length: > MaxSignatureBytes } || (signature is not null && !IsPng(signature)))
            {
                throw new ArgumentException("Die Unterschrift muss ein PNG-Bild bis 300 KB sein.");
            }
        }

        var cleanNotes = Clean(notes, MaxNotesLength);
        return new AcceptanceProtocol
        {
            Id = id, BauakteId = bauakteId, PerformedByUserId = userId, PerformedAt = performedAt, Location = Clean(location, 200),
            ClientName = Clean(clientName, 200), ContractorName = Clean(contractorName, 200), Scope = Clean(scope, 4000),
            RoomIds = roomIds.Distinct().Take(500).ToList(), Result = result, Notes = cleanNotes,
            PhotoFileIds = photoFileIds.Distinct().Take(50).ToList(), DefectWorkItemIds = defectWorkItemIds.Distinct().Take(200).ToList(),
            ClientSignature = clientSignature, ClientSignedAt = clientSignature is null ? null : clientSignedAt ?? performedAt,
            ContractorSignature = contractorSignature, ContractorSignedAt = contractorSignature is null ? null : contractorSignedAt ?? performedAt,
            State = AcceptanceState.Recorded,
        };
    }

    public bool IsSigned => ClientSignature is not null && ContractorSignature is not null;

    /// <summary>Neuer Freigabelink; liefert den Klartext-Token (nur einmal sichtbar, gespeichert wird der Hash).</summary>
    public string IssueCustomerLink(DateTimeOffset expiresAt, string? email)
    {
        if (State == AcceptanceState.Completed)
        {
            throw new InvalidOperationException("Das Protokoll ist bereits abgeschlossen.");
        }

        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        CustomerTokenHash = HashToken(token);
        CustomerLinkExpiresAt = expiresAt;
        CustomerEmail = Clean(email, 320);
        State = AcceptanceState.AwaitingCustomer;
        CustomerDecision = null;
        return token;
    }

    public void Decide(bool approve, string name, string? comment, string? ip, DateTimeOffset now, byte[]? signature)
    {
        if (State != AcceptanceState.AwaitingCustomer || CustomerLinkExpiresAt is not { } expires || expires <= now)
        {
            throw new InvalidOperationException("Der Freigabelink ist nicht mehr gültig.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Bitte den Namen angeben.", nameof(name));
        }

        if (!approve && string.IsNullOrWhiteSpace(comment))
        {
            throw new ArgumentException("Bitte begründen, warum die Abnahme abgelehnt wird.", nameof(comment));
        }

        if (signature is { Length: > MaxSignatureBytes } || (signature is not null && !IsPng(signature)))
        {
            throw new ArgumentException("Die Unterschrift muss ein PNG-Bild bis 300 KB sein.", nameof(signature));
        }

        CustomerDecision = approve ? Documentation.CustomerDecision.Approved : Documentation.CustomerDecision.Rejected;
        CustomerDecidedAt = now;
        CustomerDecisionName = Clean(name, 200);
        CustomerComment = Clean(comment, 2000);
        CustomerIp = Clean(ip, 64);
        if (approve && signature is not null)
        {
            ClientSignature = signature;
            ClientSignedAt = now;
        }

        // Nach der Entscheidung ist der Link verbraucht.
        CustomerTokenHash = null;
        if (!approve)
        {
            State = AcceptanceState.Recorded;
        }
    }

    public void Complete(Guid pdfFileId)
    {
        State = AcceptanceState.Completed;
        PdfFileId = pdfFileId;
        CustomerTokenHash = null;
    }

    public static byte[] HashToken(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));

    internal static bool IsPng(byte[] bytes) =>
        bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Clean(string? value, int max)
    {
        var trimmed = value?.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= max ? trimmed : throw new ArgumentException($"Text länger als {max} Zeichen.");
    }
}

internal sealed class AcceptanceProtocolConfiguration : IEntityTypeConfiguration<AcceptanceProtocol>
{
    public void Configure(EntityTypeBuilder<AcceptanceProtocol> builder)
    {
        builder.ToTable("acceptance_protocols");
        builder.ConfigureVersioned();
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Result).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.State).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.CustomerDecision).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Location).HasMaxLength(200);
        builder.Property(a => a.ClientName).HasMaxLength(200);
        builder.Property(a => a.ContractorName).HasMaxLength(200);
        builder.Property(a => a.Scope).HasMaxLength(4000);
        builder.Property(a => a.CustomerEmail).HasMaxLength(320);
        builder.Property(a => a.CustomerDecisionName).HasMaxLength(200);
        builder.Property(a => a.CustomerComment).HasMaxLength(2000);
        builder.Property(a => a.CustomerIp).HasMaxLength(64);
        builder.Property(a => a.RoomIds).HasColumnType("uuid[]");
        builder.Property(a => a.PhotoFileIds).HasColumnType("uuid[]");
        builder.Property(a => a.DefectWorkItemIds).HasColumnType("uuid[]");
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(a => a.BauakteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.PerformedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.BauakteId);
        builder.HasIndex(a => a.CustomerTokenHash).IsUnique().HasFilter("customer_token_hash IS NOT NULL");
    }
}

/// <summary>Abnahmeprotokoll für Web und Apps (ohne Unterschriftsbilder; die gibt es einzeln als PNG).</summary>
public sealed record AcceptanceDto(
    Guid Id, Guid BauakteId, Guid PerformedByUserId, DateTimeOffset PerformedAt, string? Location, string? ClientName, string? ContractorName,
    string? Scope, IReadOnlyList<Guid> RoomIds, AcceptanceResult Result, string? Notes, IReadOnlyList<Guid> PhotoFileIds,
    IReadOnlyList<Guid> DefectWorkItemIds, bool HasClientSignature, DateTimeOffset? ClientSignedAt, bool HasContractorSignature,
    DateTimeOffset? ContractorSignedAt, AcceptanceState State, DateTimeOffset? CustomerLinkExpiresAt, string? CustomerEmail,
    CustomerDecision? CustomerDecision, DateTimeOffset? CustomerDecidedAt, string? CustomerDecisionName, string? CustomerComment,
    Guid? PdfFileId, long Version)
{
    public static AcceptanceDto From(AcceptanceProtocol a) => new(a.Id, a.BauakteId, a.PerformedByUserId, a.PerformedAt, a.Location, a.ClientName,
        a.ContractorName, a.Scope, a.RoomIds, a.Result, a.Notes, a.PhotoFileIds, a.DefectWorkItemIds, a.ClientSignature is not null, a.ClientSignedAt,
        a.ContractorSignature is not null, a.ContractorSignedAt, a.State, a.CustomerLinkExpiresAt, a.CustomerEmail, a.CustomerDecision,
        a.CustomerDecidedAt, a.CustomerDecisionName, a.CustomerComment, a.PdfFileId, a.Version);
}

/// <summary>
/// Sync-Operation „acceptance.create“ (Apps und Web): Payload siehe docs/sync-protokoll.md bzw. AcceptanceDto;
/// Unterschriften als Base64-PNG in clientSignature/contractorSignature { png, signedAt }.
/// </summary>
internal sealed class AcceptanceSync(EstrichOneDbContext db, IBauakteAccess access, IUserPermissions permissions, ICurrentUser currentUser,
    TimeProvider clock, AcceptanceService service) : ISyncOperationHandler
{
    public IReadOnlyCollection<string> OperationTypes { get; } = ["acceptance.create"];

    public async Task<SyncOperationResult> ApplyAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(Permissions.InspectionsWrite, cancellationToken))
        {
            return SyncOperationResult.Rejected(op.OpId, "PERMISSION_DENIED", "Für diese Aktion fehlt die Berechtigung.");
        }

        var p = op.Payload;
        var bauakteId = SyncPayload.Read<Guid>(p, "bauakteId");
        if (!await access.CanReadAsync(bauakteId, cancellationToken))
        {
            return SyncOperationResult.Rejected(op.OpId, "BAUAKTE_NOT_FOUND", "Die Bauakte existiert nicht (mehr) oder ist nicht sichtbar.");
        }

        var existing = await db.Set<AcceptanceProtocol>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == op.EntityId, cancellationToken);
        if (existing is not null)
        {
            return existing.PerformedByUserId == currentUser.UserId
                ? new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: existing.Version, Entity: SyncPayload.ToNode(AcceptanceDto.From(existing)))
                : SyncOperationResult.Rejected(op.OpId, "ID_CONFLICT", "Unter dieser ID existiert bereits ein anderes Protokoll.");
        }

        try
        {
            var now = clock.GetUtcNow();
            var performedAt = SyncPayload.Read<DateTimeOffset?>(p, "performedAt") ?? now;
            if (performedAt > now.AddMinutes(10))
            {
                throw new SyncValidationException("Der Zeitpunkt der Abnahme liegt in der Zukunft.");
            }

            var (clientPng, clientAt) = Signature(p, "clientSignature");
            var (contractorPng, contractorAt) = Signature(p, "contractorSignature");
            var protocol = AcceptanceProtocol.Create(op.EntityId, bauakteId, currentUser.UserId, performedAt, SyncPayload.Read<string>(p, "location"),
                SyncPayload.Read<string>(p, "clientName"), SyncPayload.Read<string>(p, "contractorName"), SyncPayload.Read<string>(p, "scope"),
                SyncPayload.Read<List<Guid>>(p, "roomIds") ?? [], SyncPayload.Read<AcceptanceResult?>(p, "result")
                    ?? throw new SyncValidationException("Bitte das Ergebnis der Abnahme angeben."),
                SyncPayload.Read<string>(p, "notes"), SyncPayload.Read<List<Guid>>(p, "photoFileIds") ?? [],
                SyncPayload.Read<List<Guid>>(p, "defectWorkItemIds") ?? [], clientPng, clientAt, contractorPng, contractorAt);
            db.Add(protocol);
            await service.RecordCreatedAsync(protocol, currentUser.UserId, cancellationToken);
            return new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: protocol.Version, Entity: SyncPayload.ToNode(AcceptanceDto.From(protocol)));
        }
        catch (ArgumentException exception)
        {
            throw new SyncValidationException(exception.Message);
        }
    }

    private static (byte[]? Png, DateTimeOffset? At) Signature(JsonObject payload, string name)
    {
        if (payload[name] is not JsonObject signature)
        {
            return (null, null);
        }

        var base64 = signature["png"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(base64))
        {
            return (null, null);
        }

        var comma = base64.IndexOf(',', StringComparison.Ordinal);
        try
        {
            var bytes = Convert.FromBase64String(comma >= 0 ? base64[(comma + 1)..] : base64);
            var at = signature["signedAt"]?.GetValue<string>() is { } text
                ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                : (DateTimeOffset?)null;
            return (bytes, at);
        }
        catch (FormatException)
        {
            throw new SyncValidationException("Die Unterschrift ist kein gültiges Base64-PNG.");
        }
    }
}

internal sealed class AcceptanceSource(EstrichOneDbContext db) : ISyncEntitySource
{
    public string EntityType => "acceptance";

    public string? RequiredPermission => null;

    public async Task<IReadOnlyDictionary<Guid, JsonNode>> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Set<AcceptanceProtocol>().AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken))
            .ToDictionary(a => a.Id, a => SyncPayload.ToNode(AcceptanceDto.From(a))!);
}
