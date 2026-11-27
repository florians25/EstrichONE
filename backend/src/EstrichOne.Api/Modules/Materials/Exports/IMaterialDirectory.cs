namespace EstrichOne.Api.Modules.Materials.Exports;

/// <summary>Artikel des Materialstamms in Kurzform (für Verknüpfungen anderer Module, z. B. Artikel und Leistungen).</summary>
public sealed record MaterialSummary(Guid Id, string Name, string? Number, string Unit, bool IsActive);

/// <summary>Lesender Zugriff anderer Module auf den Materialstamm.</summary>
public interface IMaterialDirectory
{
    /// <summary>Artikel des Materialstamms; ohne <paramref name="ids"/> alle aktiven, mit <paramref name="ids"/> genau diese (auch inaktive).</summary>
    Task<IReadOnlyList<MaterialSummary>> ListAsync(IReadOnlyCollection<Guid>? ids, CancellationToken cancellationToken);
}
