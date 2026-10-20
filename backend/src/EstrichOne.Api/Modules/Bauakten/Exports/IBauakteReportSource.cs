namespace EstrichOne.Api.Modules.Bauakten.Exports;

public sealed record RoomReportRow(string Name, string? Floor, decimal? AreaSquareMeters, string? ScreedType, int? ThicknessMillimeters,
    int? InsulationMillimeters, bool UnderfloorHeating, string Status);

public sealed record CustomerReportInfo(string Name, string? CustomerNumber, string? Street, string? PostalCode, string? City, string? Phone, string? Email);

public sealed record BauakteReportInfo(
    Guid Id, string? Number, string Title, BauakteStatus Status, string? Street, string? PostalCode, string? City, string? Description,
    DateOnly? PlannedStart, DateOnly? PlannedEnd, string? SiteContactName, string? SiteContactPhone, string? SiteNotes,
    CustomerReportInfo? Customer, string CreatedByName, DateTimeOffset CreatedAt, string? ResponsibleName, IReadOnlyList<string> Team,
    IReadOnlyList<RoomReportRow> Rooms);

/// <summary>Stammdaten, Kunde, Team und Räume einer Bauakte für Ausdrucke (Bauakte-PDF).</summary>
public interface IBauakteReportSource
{
    Task<BauakteReportInfo?> GetAsync(Guid bauakteId, CancellationToken cancellationToken);
}
