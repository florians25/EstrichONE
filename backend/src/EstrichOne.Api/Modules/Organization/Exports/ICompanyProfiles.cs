namespace EstrichOne.Api.Modules.Organization.Exports;

public interface ICompanyProfiles
{
    /// <summary>Legt die Firmendaten bei der Ersteinrichtung an. Speichert mit dem nächsten SaveChanges.</summary>
    Task CreateAsync(string companyName, CancellationToken cancellationToken);

    Task<string?> GetNameAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Anzeigename des Betriebs für Kopfzeile, Anmeldung, Apps (server-info), 2FA-Aussteller und Kopplungen:
    /// der gepflegte Firmenname aus den Firmendaten, sonst der Installer-Wert EstrichOne:ServerName (ESTRICHONE_NAME).
    /// Wird bei jedem Aufruf frisch gelesen, eine Änderung der Firmendaten wirkt also sofort.
    /// </summary>
    Task<string> GetDisplayNameAsync(CancellationToken cancellationToken);

    /// <summary>Firmendaten für Briefköpfe und Ausdrucke (null vor der Einrichtung).</summary>
    Task<CompanyInfo?> GetProfileAsync(CancellationToken cancellationToken);
}

public sealed record CompanyInfo(string Name, string? Street, string? PostalCode, string? City, string? Phone, string? Email, string? Website);

/// <summary>Kopf- und Fußzeile der PDFs: Firmendaten, eigenes Logo, Gestaltung aus den Einstellungen.</summary>
public interface IPdfBranding
{
    Task<EstrichOne.Api.Infrastructure.Pdf.PdfCompany> GetAsync(CancellationToken cancellationToken);

    /// <summary>Ist ein eigenes Firmenlogo hinterlegt (GET /company/logo)?</summary>
    Task<bool> HasLogoAsync(CancellationToken cancellationToken);
}
