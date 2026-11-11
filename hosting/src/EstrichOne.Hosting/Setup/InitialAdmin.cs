using System.Text.Json;
using EstrichOne.Hosting.Auth;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Domain;
using EstrichOne.Hosting.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Hosting.Setup;

/// <summary>
/// Admin-Konto aus dem Installer: Der Installer legt <c>console/initial-admin.json</c> (nur root lesbar) mit Benutzername,
/// Startpasswort und E-Mail ab; die Konsole legt beim Start das Konto an (bzw. setzt es mit <c>reset</c> zurück – Notfall
/// bei verlorenem Passwort/2FA), verlangt bei der ersten Anmeldung ein neues Passwort und löscht die Datei sofort.
/// </summary>
public static class InitialAdmin
{
    public const string FileName = "initial-admin.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Request(string? UserName, string? Password, string? Email, bool Reset);

    public static async Task<string?> ApplyAsync(IServiceProvider services, HostingOptions options, CancellationToken cancellationToken)
    {
        var path = Path.Combine(options.ConsoleDataDirectory, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        Request? request;
        try
        {
            request = JsonSerializer.Deserialize<Request>(await File.ReadAllTextAsync(path, cancellationToken), Json);
        }
        catch (JsonException)
        {
            request = null;
        }
        finally
        {
            File.Delete(path);
        }

        var userName = request?.UserName?.Trim().ToLowerInvariant();
        if (request is null || Validation.CheckUserName(userName) is not null || Validation.CheckPassword(request.Password) is not null)
        {
            return "Admin-Konto aus dem Installer ungültig – Datei verworfen.";
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostingDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditLog>();
        var admin = await db.Admins.FirstOrDefaultAsync(a => a.UserName == userName, cancellationToken);
        string message;
        if (admin is null)
        {
            if (!request.Reset && await db.Admins.AnyAsync(cancellationToken))
            {
                return "Es gibt bereits Administratoren – Admin-Konto aus dem Installer nicht angelegt (zum Zurücksetzen: reset-admin).";
            }

            admin = new AdminUser { UserName = userName!, DisplayName = "Administrator", PasswordHash = string.Empty, MustChangePassword = true };
            admin.PasswordHash = AuthEndpoints.HashPassword(admin, request.Password!);
            db.Admins.Add(admin);
            message = $"Administrator „{userName}“ aus dem Installer angelegt (Passwort bei der ersten Anmeldung ändern).";
        }
        else
        {
            admin.PasswordHash = AuthEndpoints.HashPassword(admin, request.Password!);
            admin.MustChangePassword = true;
            admin.FailedLogins = 0;
            admin.LockedUntil = null;
            admin.TotpEnabled = false;
            admin.TotpSecret = null;
            // Notfallzugang: das zurückgesetzte Konto darf wieder alles (auch wenn es zuvor „nur lesen“ war).
            admin.Role = AdminRole.Administrator;
            admin.SecurityStamp++;
            message = $"Administrator „{userName}“ per Installer zurückgesetzt (neues Startpasswort, Zwei-Faktor-Anmeldung aus).";
        }

        await db.SaveChangesAsync(cancellationToken);
        if (Validation.CheckEmail(request.Email?.Trim(), required: false) is null && !string.IsNullOrWhiteSpace(request.Email))
        {
            var settings = scope.ServiceProvider.GetRequiredService<ConsoleSettings>();
            if (string.IsNullOrEmpty(await settings.GetAsync("notify.emailTo", cancellationToken)))
            {
                await settings.SetAsync("notify.emailTo", request.Email.Trim(), cancellationToken);
            }
        }

        await audit.WriteAsync(request.Reset ? "Administrator zurückgesetzt" : "Administrator angelegt", details: message, actor: "Installer",
            cancellationToken: cancellationToken);
        return message;
    }
}
