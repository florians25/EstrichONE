using System.Globalization;
using System.Text;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Domain;
using EstrichOne.Hosting.Infrastructure;
using EstrichOne.Hosting.Instances;

namespace EstrichOne.Hosting.Proxy;

/// <summary>Ein Eintrag im gemeinsamen Reverse Proxy.</summary>
public sealed record ProxyRoute(string Slug, string Domain, bool Maintenance);

/// <summary>
/// Erzeugt das Caddyfile des gemeinsamen Reverse Proxys: je Mandant ein Site-Block mit der Domain, Weiterleitung an den
/// Caddy der Instanz im eigenen Edge-Netz – oder die Wartungsseite (gesperrt, angehalten, in Bereitstellung).
/// Alle Domains und Kürzel sind vorher streng geprüft; ungültige Einträge werden zusätzlich hier verworfen.
/// </summary>
public static class ProxyConfigBuilder
{
    public const string MaintenanceRoot = "/etc/caddy/maintenance";

    public static bool ShowsMaintenance(TenantState state) =>
        state is TenantState.Locked or TenantState.Stopped or TenantState.Provisioning or TenantState.Deleting;

    public static string Build(HostingOptions settings, IEnumerable<ProxyRoute> routes)
    {
        var tls = settings.Tls.ToLowerInvariant();
        var scheme = tls == "off" ? "http://" : string.Empty;
        var builder = new StringBuilder();
        builder.AppendLine("# Von der EstrichONE Hosting-Konsole erzeugt – Änderungen werden überschrieben.");
        builder.AppendLine("{");
        if (tls == "acme" && !string.IsNullOrWhiteSpace(settings.AcmeEmail) && Validation.CheckEmail(settings.AcmeEmail, true) is null)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"\temail {settings.AcmeEmail}");
        }

        if (tls == "internal")
        {
            builder.AppendLine("\tlocal_certs");
        }

        if (tls == "off")
        {
            builder.AppendLine("\tauto_https off");
        }

        builder.AppendLine("\tservers {");
        builder.AppendLine("\t\tprotocols h1 h2 h3");
        builder.AppendLine("\t}");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("(gemeinsam) {");
        builder.AppendLine("\tencode zstd gzip");
        builder.AppendLine("\theader -Server");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("(wartung) {");
        builder.AppendLine(CultureInfo.InvariantCulture, $"\troot * {MaintenanceRoot}");
        builder.AppendLine("\trewrite * /index.html");
        builder.AppendLine("\theader Cache-Control \"no-store\"");
        builder.AppendLine("\theader Retry-After \"300\"");
        builder.AppendLine("\theader Content-Security-Policy \"default-src 'none'; style-src 'unsafe-inline'; img-src 'self' data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\"");
        builder.AppendLine("\tfile_server");
        builder.AppendLine("}");
        builder.AppendLine();

        var console = Validation.NormalizeDomain(settings.ConsoleDomain);
        if (Validation.CheckDomain(console) is null)
        {
            builder.AppendLine("# Hosting-Konsole");
            builder.AppendLine(CultureInfo.InvariantCulture, $"{scheme}{console} {{");
            builder.AppendLine("\timport gemeinsam");
            builder.AppendLine(CultureInfo.InvariantCulture, $"\treverse_proxy {settings.ConsoleContainer}:8080");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        foreach (var route in routes.OrderBy(r => r.Slug, StringComparer.Ordinal))
        {
            if (!Validation.IsValidSlug(route.Slug) || Validation.CheckDomain(route.Domain) is not null || route.Domain == console)
            {
                continue;
            }

            builder.AppendLine(CultureInfo.InvariantCulture, $"# Mandant {route.Slug}{(route.Maintenance ? " – Wartungsseite" : string.Empty)}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"{scheme}{route.Domain} {{");
            builder.AppendLine("\timport gemeinsam");
            if (route.Maintenance)
            {
                builder.AppendLine("\thandle {");
                builder.AppendLine("\t\terror 503");
                builder.AppendLine("\t}");
            }
            else
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"\treverse_proxy {InstanceLayout.WebContainer(route.Slug)}:{InstanceLayout.InstanceHttpPort} {{");
                builder.AppendLine("\t\tflush_interval -1");
                builder.AppendLine("\t}");
            }

            // Instanz nicht erreichbar (angehalten, Neustart) oder gesperrt: Wartungsseite statt nackter Fehlermeldung.
            builder.AppendLine("\thandle_errors 502 503 504 {");
            builder.AppendLine("\t\timport wartung");
            builder.AppendLine("\t}");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        return builder.ToString();
    }
}
