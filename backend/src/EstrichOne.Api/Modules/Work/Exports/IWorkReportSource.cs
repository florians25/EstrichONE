namespace EstrichOne.Api.Modules.Work.Exports;

public sealed record WorkReportRow(bool IsDefect, string Title, string? Description, string Status, DateOnly? DueDate, string? AssigneeName,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, Guid Id = default)
{
    public bool IsOpen => Status != "erledigt";
}

/// <summary>Aufgaben und Mängel einer Bauakte für Ausdrucke.</summary>
public interface IWorkReportSource
{
    Task<IReadOnlyList<WorkReportRow>> ListAsync(Guid bauakteId, CancellationToken cancellationToken);
}
