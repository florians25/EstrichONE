using System.Security.Claims;
using EstrichOne.Hosting.Api;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Domain;
using EstrichOne.Hosting.Infrastructure;
using EstrichOne.Hosting.Notifications;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Setup;

public sealed record SetupWizardInput(string? DefaultVersion, string? SecondaryRepository, NotificationSettings.Input? Notifications);

/// <summary>
/// Einrichtungsassistent der Konsole nach der ersten Anmeldung: Passwort ändern (Pflicht bei Konten aus dem Installer),
/// Zwei-Faktor optional (vorhandene Konto-Endpunkte), Standardversion, zweites Sicherungsziel, Benachrichtigungen.
/// </summary>
public static class SetupWizardEndpoints
{
    /// <summary>restic-Repository-Arten, die der Backup-Dienst der Suite kennt.</summary>
    private static readonly string[] RepositoryPrefixes = ["sftp:", "rest:http://", "rest:https://", "s3:", "b2:", "azure:", "gs:", "rclone:", "/"];

    public static string? CheckRepository(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= 500 && RepositoryPrefixes.Any(p => value.StartsWith(p, StringComparison.Ordinal))
            && !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '"' or '\'' or '`' or '$' or '\\')
            ? null
            : "Bitte ein restic-Repository angeben, z. B. sftp:backup@sicherung.example.de:/estrichone oder rest:https://…";
    }

    public static void Map(IEndpointRouteBuilder app)
    {
        var wizard = app.MapGroup("/api/setup-wizard").WithTags("Einrichtung").RequireAuthorization();
        wizard.MapGet("/", async (HttpContext context, HostingDbContext db, ConsoleSettings settings, IOptions<HostingOptions> options,
            CancellationToken ct) =>
        {
            var admin = await CurrentAsync(context, db, ct);
            var notifications = await NotificationSettings.LoadAsync(settings, ct);
            return Results.Ok(new
            {
                completed = await settings.GetAsync(ConsoleSettings.WizardCompletedKey, ct) == "true",
                mustChangePassword = admin?.MustChangePassword ?? false,
                totpEnabled = admin?.TotpEnabled ?? false,
                userName = admin?.UserName,
                defaultVersion = await settings.GetDefaultVersionAsync(ct),
                secondaryRepository = await settings.GetAsync(ConsoleSettings.SecondaryRepositoryKey, ct),
                backupRoot = string.IsNullOrWhiteSpace(options.Value.BackupRoot) ? $"{options.Value.InstancesDirectory}/<kürzel>/backup-repository" : options.Value.BackupRoot,
                archiveDirectory = options.Value.ArchiveDirectory,
                baseDomain = options.Value.BaseDomain,
                consoleDomain = options.Value.ConsoleDomain,
                proxy = options.Value.Proxy,
                tls = options.Value.Tls,
                notifications = notifications.ToView(),
            });
        });
        wizard.MapPost("/complete", async (SetupWizardInput input, HttpContext context, HostingDbContext db, ConsoleSettings settings,
            AuditLog audit, CancellationToken ct) =>
        {
            var admin = await CurrentAsync(context, db, ct);
            if (admin?.MustChangePassword == true)
            {
                return ApiErrors.BadRequest("Bitte zuerst ein eigenes Passwort festlegen.");
            }

            var errors = new Dictionary<string, string>();
            var version = input.DefaultVersion?.Trim();
            if (!string.IsNullOrEmpty(version) && Validation.CheckVersion(version) is { } versionError) errors["defaultVersion"] = versionError;
            var repository = input.SecondaryRepository?.Trim();
            if (CheckRepository(repository) is { } repositoryError) errors["secondaryRepository"] = repositoryError;
            NotificationSettings? notifications = null;
            if (input.Notifications is not null)
            {
                var (value, notificationErrors) = NotificationSettings.Validate(input.Notifications, await NotificationSettings.LoadAsync(settings, ct));
                foreach (var (key, message) in notificationErrors) errors[key] = message;
                notifications = value;
            }

            if (errors.Count > 0)
            {
                return ApiErrors.Validation(errors);
            }

            if (!string.IsNullOrEmpty(version)) await settings.SetDefaultVersionAsync(version, ct);
            await settings.SetAsync(ConsoleSettings.SecondaryRepositoryKey, repository ?? string.Empty, ct);
            if (notifications is not null) await notifications.SaveAsync(settings, ct);
            await settings.SetAsync(ConsoleSettings.WizardCompletedKey, "true", ct);
            await audit.WriteAsync("Einrichtungsassistent abgeschlossen",
                details: $"Standardversion {version ?? "unverändert"}, zweites Sicherungsziel {(string.IsNullOrEmpty(repository) ? "keins" : "eingerichtet")}, "
                    + $"Benachrichtigungen {(notifications is { EmailConfigured: true } or { WebhookConfigured: true } ? "an" : "aus")}",
                cancellationToken: ct);
            return Results.NoContent();
        });
        app.MapPut("/api/settings/backup", async (SetupWizardInput input, ConsoleSettings settings, AuditLog audit, CancellationToken ct) =>
        {
            var repository = input.SecondaryRepository?.Trim();
            if (CheckRepository(repository) is { } error)
            {
                return ApiErrors.Validation(new Dictionary<string, string> { ["secondaryRepository"] = error });
            }

            await settings.SetAsync(ConsoleSettings.SecondaryRepositoryKey, repository ?? string.Empty, ct);
            await audit.WriteAsync("Zweites Sicherungsziel geändert", details: string.IsNullOrEmpty(repository) ? "entfernt" : "eingerichtet (gilt ab dem nächsten Vorgang je Instanz)",
                cancellationToken: ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Einstellungen");
    }

    private static async Task<AdminUser?> CurrentAsync(HttpContext context, HostingDbContext db, CancellationToken cancellationToken)
    {
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(id, out var guid) ? await db.Admins.FindAsync([guid], cancellationToken) : null;
    }
}
