namespace EstrichOne.Api.Infrastructure.Hosting;

/// <summary>Betreiberkonfiguration (Abschnitt <c>EstrichOne</c>, Umgebungsvariablen <c>EstrichOne__…</c>).</summary>
public sealed class EstrichOneOptions
{
    public const string SectionName = "EstrichOne";

    /// <summary>Anzeigename des Servers, z. B. der Firmenname. Erscheint im Einrichtungsassistent der Apps.</summary>
    public string ServerName { get; set; } = "EstrichONE";

    /// <summary>Öffentliche Basisadresse, z. B. <c>https://estrichone.meinefirma.de</c>.</summary>
    public Uri? PublicUrl { get; set; }

    /// <summary>Älteste App-Version, die dieser Server noch akzeptiert.</summary>
    public string MinClientVersion { get; set; } = "0.1.0";

    /// <summary>
    /// <c>X-Forwarded-*</c>-Header auswerten. Nur einschalten, wenn die API ausschließlich hinter dem
    /// mitgelieferten Reverse Proxy erreichbar ist (Standard im Docker-Stack).
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }

    /// <summary>OpenAPI-Beschreibung unter <c>/api/openapi/v1.json</c> ausliefern.</summary>
    public bool ExposeOpenApi { get; set; } = true;

    /// <summary>
    /// Datenbankschema beim Serverstart aktualisieren (statt eines eigenen <c>migrate</c>-Dienstes). Im Docker-Stack an.
    /// </summary>
    public bool MigrateOnStartup { get; set; }
}
