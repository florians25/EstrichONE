using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Fuel;

public enum FuelType
{
    Diesel,
    Petrol,
    AdBlue,
    Other,
}

/// <summary>Wie bezahlt wurde: Tankkarte, Firmenkarte (Kreditkarte des Betriebs) oder privat ausgelegt (Erstattung).</summary>
public enum FuelPaymentMethod
{
    FuelCard,
    CompanyCard,
    Private,
}

public enum FuelReceiptStatus
{
    /// <summary>Erfasst, noch nicht geprüft.</summary>
    New,

    /// <summary>Geprüft und freigegeben.</summary>
    Checked,

    /// <summary>Erstattet (privat ausgelegt) bzw. gebucht (Tank- oder Firmenkarte).</summary>
    Settled,

    /// <summary>Abgelehnt (mit Begründung); der Erfasser kann korrigieren.</summary>
    Rejected,
}

/// <summary>
/// Tankbeleg: in der App fotografiert und erfasst (offline über die Sync-Warteschlange) oder im Büro nachgetragen,
/// dann geprüft, freigegeben und erstattet bzw. gebucht. Die Belegnummer kommt aus dem Nummernkreis „Tankbelege“.
/// Gelöscht wird nur weich (Nachvollziehbarkeit); erstattete bzw. gebuchte Belege sind unveränderlich.
/// </summary>
public sealed class FuelReceipt : IVersionedEntity, ISoftDeletable
{
    public const int MaxStationLength = 200;
    public const int MaxNotesLength = 1000;

    public Guid Id { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public Guid VehicleId { get; private set; }

    /// <summary>Wer getankt hat (Erfasser; im Büro nachgetragen: die gewählte Person).</summary>
    public Guid UserId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateOnly Date { get; private set; }
    public int? OdometerKm { get; private set; }
    public decimal Liters { get; private set; }

    /// <summary>Betrag brutto in Euro.</summary>
    public decimal AmountGross { get; private set; }
    public FuelType FuelType { get; private set; }
    public FuelPaymentMethod PaymentMethod { get; private set; }
    public string? Station { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Belegfoto (Datei ohne Bauakte, eingeordnet als Tankbeleg für die Archivregel).</summary>
    public Guid? ImageFileId { get; private set; }
    public FuelReceiptStatus Status { get; private set; } = FuelReceiptStatus.New;
    public string? RejectionReason { get; private set; }
    public Guid? CheckedByUserId { get; private set; }
    public DateTimeOffset? CheckedAt { get; private set; }
    public Guid? SettledByUserId { get; private set; }
    public DateTimeOffset? SettledAt { get; private set; }

    /// <summary>Herkunft: ios, android oder web.</summary>
    public string Source { get; private set; } = "web";

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private FuelReceipt()
    {
    }

    public static FuelReceipt Create(Guid id, string number, Guid userId, Guid createdByUserId, string source) => new()
    {
        Id = id, Number = number, UserId = userId, CreatedByUserId = createdByUserId,
        Source = source is "ios" or "android" ? source : "web",
    };

    public bool IsFinal => Status == FuelReceiptStatus.Settled;

    /// <summary>Angaben setzen; eine Korrektur nach der Ablehnung legt den Beleg wieder zur Prüfung vor.</summary>
    public void Update(Guid vehicleId, DateOnly date, int? odometerKm, decimal liters, decimal amountGross, FuelType fuelType,
        FuelPaymentMethod paymentMethod, string? station, string? notes, Guid? imageFileId, DateOnly today)
    {
        if (IsFinal)
        {
            throw new InvalidOperationException("Erstattete bzw. gebuchte Tankbelege können nicht mehr geändert werden.");
        }

        if (vehicleId == Guid.Empty)
        {
            throw new ArgumentException("Bitte das Fahrzeug wählen.", nameof(vehicleId));
        }

        if (date > today.AddDays(1) || date < today.AddYears(-3))
        {
            throw new ArgumentException("Das Datum des Belegs ist nicht plausibel.", nameof(date));
        }

        if (odometerKm is < 0 or > 5_000_000)
        {
            throw new ArgumentException("Der Kilometerstand muss zwischen 0 und 5 000 000 liegen.", nameof(odometerKm));
        }

        if (liters is <= 0 or > 5000)
        {
            throw new ArgumentException("Bitte die getankte Menge in Litern angeben (größer als 0).", nameof(liters));
        }

        if (amountGross is <= 0 or > 50_000)
        {
            throw new ArgumentException("Bitte den Betrag brutto angeben (größer als 0).", nameof(amountGross));
        }

        VehicleId = vehicleId;
        Date = date;
        OdometerKm = odometerKm;
        Liters = Math.Round(liters, 2);
        AmountGross = Math.Round(amountGross, 2);
        FuelType = fuelType;
        PaymentMethod = paymentMethod;
        Station = Text(station, MaxStationLength);
        Notes = Text(notes, MaxNotesLength);
        ImageFileId = imageFileId;
        if (Status == FuelReceiptStatus.Rejected)
        {
            Status = FuelReceiptStatus.New;
        }
    }

    public void ChangeUser(Guid userId) => UserId = userId;

    /// <summary>Prüfen und freigeben (aus Neu oder Abgelehnt).</summary>
    public void Check(Guid by, DateTimeOffset at)
    {
        if (Status is not (FuelReceiptStatus.New or FuelReceiptStatus.Rejected))
        {
            throw new InvalidOperationException("Nur neue oder abgelehnte Belege können freigegeben werden.");
        }

        Status = FuelReceiptStatus.Checked;
        RejectionReason = null;
        CheckedByUserId = by;
        CheckedAt = at;
    }

    public void Reject(Guid by, DateTimeOffset at, string? reason)
    {
        if (Status is not (FuelReceiptStatus.New or FuelReceiptStatus.Checked))
        {
            throw new InvalidOperationException("Nur neue oder freigegebene Belege können abgelehnt werden.");
        }

        RejectionReason = Text(reason, 500) ?? throw new ArgumentException("Bitte kurz begründen, warum der Beleg abgelehnt wird.", nameof(reason));
        Status = FuelReceiptStatus.Rejected;
        CheckedByUserId = by;
        CheckedAt = at;
    }

    /// <summary>Erstattet (privat) bzw. gebucht (Karte) – nur freigegebene Belege.</summary>
    public void Settle(Guid by, DateTimeOffset at)
    {
        if (Status != FuelReceiptStatus.Checked)
        {
            throw new InvalidOperationException("Nur freigegebene Belege können als erstattet bzw. gebucht markiert werden.");
        }

        Status = FuelReceiptStatus.Settled;
        SettledByUserId = by;
        SettledAt = at;
    }

    /// <summary>Freigabe oder Ablehnung zurücknehmen (wieder „Neu“).</summary>
    public void Reopen()
    {
        if (Status is not (FuelReceiptStatus.Checked or FuelReceiptStatus.Rejected))
        {
            throw new InvalidOperationException("Nur freigegebene oder abgelehnte Belege können wieder geöffnet werden.");
        }

        Status = FuelReceiptStatus.New;
        RejectionReason = null;
        CheckedByUserId = null;
        CheckedAt = null;
    }

    public void Delete(DateTimeOffset at)
    {
        if (IsFinal)
        {
            throw new InvalidOperationException("Erstattete bzw. gebuchte Tankbelege können nicht gelöscht werden.");
        }

        DeletedAt ??= at;
    }

    /// <summary>Preis je Liter (gerundet auf drei Stellen, wie an der Zapfsäule).</summary>
    public decimal PricePerLiter => Liters > 0 ? Math.Round(AmountGross / Liters, 3) : 0;

    private static string? Text(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= max ? trimmed : throw new ArgumentException($"Höchstens {max} Zeichen.", nameof(value));
    }
}

internal sealed class FuelReceiptConfiguration : IEntityTypeConfiguration<FuelReceipt>
{
    public void Configure(EntityTypeBuilder<FuelReceipt> builder)
    {
        builder.ToTable("fuel_receipts");
        builder.ConfigureVersioned();
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Number).HasMaxLength(30);
        builder.HasIndex(r => r.Number).IsUnique();
        builder.Property(r => r.Liters).HasPrecision(9, 2);
        builder.Property(r => r.AmountGross).HasPrecision(10, 2);
        builder.Property(r => r.FuelType).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Station).HasMaxLength(FuelReceipt.MaxStationLength);
        builder.Property(r => r.Notes).HasMaxLength(FuelReceipt.MaxNotesLength);
        builder.Property(r => r.RejectionReason).HasMaxLength(500);
        builder.Property(r => r.Source).HasMaxLength(20);
        builder.HasIndex(r => new { r.VehicleId, r.Date });
        builder.HasIndex(r => new { r.UserId, r.Date });
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.ImageFileId);
    }
}
