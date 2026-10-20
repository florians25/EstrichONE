using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Calls;

public enum CallKind
{
    /// <summary>Gesprächsnotiz zu einem Anruf (ein- oder ausgehend).</summary>
    Call,

    /// <summary>Anfrage (Ticket), die zur Bauakte werden kann.</summary>
    Inquiry,
}

public enum CallDirection
{
    Inbound,
    Outbound,
}

public enum InquiryStatus
{
    Open,
    InProgress,
    Converted,
    Cancelled,
}

public enum CallSource
{
    Phone,
    Email,
    Website,
    Personal,
    Other,
}

public enum CallPriority
{
    Low,
    Normal,
    High,
}

/// <summary>
/// Anruf oder Anfrage im Büro (02: „Anrufe und Anfragen, Umwandlung in Bauakte“). Anfragen haben einen eigenen
/// Status; die Umwandlung legt eine Bauakte an und verknüpft sie.
/// </summary>
public sealed class CallRecord : IVersionedEntity, ISoftDeletable
{
    public Guid Id { get; private set; }
    public CallKind Kind { get; private set; }
    public CallDirection Direction { get; private set; } = CallDirection.Inbound;
    public CallSource Source { get; private set; } = CallSource.Phone;
    public CallPriority Priority { get; private set; } = CallPriority.Normal;
    public string? CallerName { get; private set; }
    public string? CallerPhone { get; private set; }
    public string? CallerEmail { get; private set; }
    public string? Company { get; private set; }
    public Guid? CustomerId { get; private set; }

    /// <summary>Bauakte, um die es im Gespräch ging.</summary>
    public Guid? BauakteId { get; private set; }
    public string? Subject { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Wiedervorlage nötig; mit <see cref="DueDate"/> erscheint sie in der Tagesübersicht.</summary>
    public bool FollowUp { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public string? NextStep { get; private set; }

    // Anfrage
    public InquiryStatus InquiryStatus { get; private set; } = InquiryStatus.Open;
    public decimal? AreaSquareMeters { get; private set; }
    public string? ScreedType { get; private set; }
    public DateOnly? DesiredStart { get; private set; }
    public string? SiteStreet { get; private set; }
    public string? SitePostalCode { get; private set; }
    public string? SiteCity { get; private set; }
    public Guid? ConvertedBauakteId { get; private set; }

    public Guid HandledByUserId { get; private set; }
    public DateTimeOffset CalledAt { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private CallRecord()
    {
    }

    public static CallRecord Create(Guid id, CallKind kind, Guid handledByUserId, DateTimeOffset calledAt) =>
        new() { Id = id, Kind = kind, HandledByUserId = handledByUserId, CalledAt = calledAt };

    public void Update(CallDetails d)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (Blank(d.CallerName) is null && Blank(d.CallerPhone) is null && Blank(d.Company) is null && d.CustomerId is null)
        {
            throw new ArgumentException("Bitte angeben, wer angerufen hat (Name, Firma, Telefon oder Kunde).", nameof(d));
        }

        Direction = d.Direction;
        Source = d.Source;
        Priority = d.Priority;
        CallerName = Blank(d.CallerName);
        CallerPhone = Blank(d.CallerPhone);
        CallerEmail = Blank(d.CallerEmail);
        Company = Blank(d.Company);
        CustomerId = d.CustomerId;
        BauakteId = Kind == CallKind.Call ? d.BauakteId : null;
        Subject = Blank(d.Subject);
        Notes = Blank(d.Notes);
        FollowUp = d.FollowUp || d.DueDate is not null;
        DueDate = d.DueDate;
        NextStep = Blank(d.NextStep);
        AreaSquareMeters = d.AreaSquareMeters;
        ScreedType = Blank(d.ScreedType);
        DesiredStart = d.DesiredStart;
        SiteStreet = Blank(d.SiteStreet);
        SitePostalCode = Blank(d.SitePostalCode);
        SiteCity = Blank(d.SiteCity);
        if (d.CalledAt is { } at)
        {
            CalledAt = at;
        }
    }

    public void SetStatus(InquiryStatus status)
    {
        if (Kind != CallKind.Inquiry)
        {
            throw new InvalidOperationException("Nur Anfragen haben einen Status.");
        }

        if (InquiryStatus == InquiryStatus.Converted && status != InquiryStatus.Converted)
        {
            throw new InvalidOperationException("Die Anfrage ist bereits eine Bauakte.");
        }

        InquiryStatus = status;
    }

    public void MarkConverted(Guid bauakteId)
    {
        InquiryStatus = InquiryStatus.Converted;
        ConvertedBauakteId = bauakteId;
    }

    public void CompleteFollowUp()
    {
        FollowUp = false;
        DueDate = null;
    }

    public void Delete(DateTimeOffset at) => DeletedAt ??= at;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record CallDetails(
    CallDirection Direction, CallSource Source, CallPriority Priority, string? CallerName, string? CallerPhone, string? CallerEmail,
    string? Company, Guid? CustomerId, Guid? BauakteId, string? Subject, string? Notes, bool FollowUp, DateOnly? DueDate, string? NextStep,
    decimal? AreaSquareMeters, string? ScreedType, DateOnly? DesiredStart, string? SiteStreet, string? SitePostalCode, string? SiteCity,
    DateTimeOffset? CalledAt);

internal sealed class CallRecordConfiguration : IEntityTypeConfiguration<CallRecord>
{
    public void Configure(EntityTypeBuilder<CallRecord> builder)
    {
        builder.ToTable("calls");
        builder.ConfigureVersioned();
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Direction).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Priority).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.InquiryStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.CallerName).HasMaxLength(200);
        builder.Property(c => c.CallerPhone).HasMaxLength(80);
        builder.Property(c => c.CallerEmail).HasMaxLength(320);
        builder.Property(c => c.Company).HasMaxLength(200);
        builder.Property(c => c.Subject).HasMaxLength(300);
        builder.Property(c => c.Notes).HasMaxLength(10_000);
        builder.Property(c => c.NextStep).HasMaxLength(300);
        builder.Property(c => c.ScreedType).HasMaxLength(100);
        builder.Property(c => c.SiteStreet).HasMaxLength(200);
        builder.Property(c => c.SitePostalCode).HasMaxLength(10);
        builder.Property(c => c.SiteCity).HasMaxLength(100);
        builder.Property(c => c.AreaSquareMeters).HasPrecision(10, 2);
        builder.HasOne<Contact>().WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(c => c.BauakteId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(c => c.ConvertedBauakteId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.HandledByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.Kind, c.CalledAt });
        builder.HasIndex(c => new { c.Kind, c.InquiryStatus });
        builder.HasIndex(c => c.DueDate).HasFilter("follow_up");
        builder.HasIndex(c => c.CustomerId);
        builder.HasIndex(c => c.BauakteId);
    }
}
