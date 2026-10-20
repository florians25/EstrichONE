namespace EstrichOne.Api.Modules.Organization.Exports;

public interface ICompanyProfiles
{
    /// <summary>Legt die Firmendaten bei der Ersteinrichtung an. Speichert mit dem nächsten SaveChanges.</summary>
    Task CreateAsync(string companyName, CancellationToken cancellationToken);

    Task<string?> GetNameAsync(CancellationToken cancellationToken);

    /// <summary>Firmendaten für Briefköpfe und Ausdrucke (null vor der Einrichtung).</summary>
    Task<CompanyInfo?> GetProfileAsync(CancellationToken cancellationToken);
}

public sealed record CompanyInfo(string Name, string? Street, string? PostalCode, string? City, string? Phone, string? Email, string? Website);
