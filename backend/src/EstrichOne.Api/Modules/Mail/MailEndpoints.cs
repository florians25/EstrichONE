using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Mail.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace EstrichOne.Api.Modules.Mail;

public sealed record MailSettingsDto(
    bool Configured, bool Enabled, string Host, int Port, MailSecurity Security, string? UserName, bool HasPassword, string FromAddress,
    string? FromName, string? ReplyTo, DateTimeOffset? LastTestAt, bool? LastTestOk, string? LastTestError);

/// <summary><paramref name="Password"/>: null = unverändert lassen; <paramref name="ClearPassword"/> entfernt es.</summary>
public sealed record UpdateMailSettingsRequest(
    bool Enabled, string Host, int Port, MailSecurity Security, string? UserName, string? Password, bool ClearPassword, string FromAddress,
    string? FromName, string? ReplyTo);

public sealed record TestMailRequest(string To);

public sealed record TestMailResult(bool Ok, string? Error, DateTimeOffset At);

public sealed record MailTemplateDto(string Key, string Name, string Subject, string Body, bool Customized, IReadOnlyList<string> Placeholders);

public sealed record UpdateMailTemplateRequest(string Subject, string Body);

public sealed record MailLogItem(
    Guid Id, DateTimeOffset CreatedAt, string To, string Subject, string? TemplateKey, string? Category, string? RelatedEntity,
    MailStatus Status, int Attempts, DateTimeOffset? SentAt, string? Error, string? AttachmentNames);

internal static class MailEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var mail = api.MapGroup("/mail").WithTags("E-Mail").RequirePermission(Permissions.SettingsManage);
        mail.MapGet("/settings", GetSettingsAsync).WithName("GetMailSettings");
        mail.MapPut("/settings", UpdateSettingsAsync).WithName("UpdateMailSettings");
        mail.MapPost("/test", TestAsync).WithName("SendTestMail").WithSummary("Testmail sofort senden (ohne Warteschlange), Ergebnis mit Ursache");
        mail.MapGet("/templates", GetTemplatesAsync).WithName("GetMailTemplates");
        mail.MapPut("/templates/{key}", UpdateTemplateAsync).WithName("UpdateMailTemplate");
        mail.MapDelete("/templates/{key}", ResetTemplateAsync).WithName("ResetMailTemplate").WithSummary("Eingebauten Text wiederherstellen");
        mail.MapGet("/log", GetLogAsync).WithName("GetMailLog");
        mail.MapPost("/log/{id:guid}/retry", RetryAsync).WithName("RetryMail");
    }

    private static MailSettingsDto ToDto(MailAccount? a) => a is null
        ? new MailSettingsDto(false, false, string.Empty, 587, MailSecurity.StartTls, null, false, string.Empty, null, null, null, null, null)
        : new MailSettingsDto(a.IsUsable, a.Enabled, a.Host, a.Port, a.Security, a.UserName, a.ProtectedPassword is not null, a.FromAddress,
            a.FromName, a.ReplyTo, a.LastTestAt, a.LastTestOk, a.LastTestError);

    private static async Task<Ok<MailSettingsDto>> GetSettingsAsync(MailService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(ToDto(await service.AccountAsync(cancellationToken)));

    private static async Task<Results<Ok<MailSettingsDto>, ProblemHttpResult>> UpdateSettingsAsync(
        UpdateMailSettingsRequest request, EstrichOneDbContext db, SmtpSender sender, IAuditLog audit, ICurrentUser user,
        CancellationToken cancellationToken)
    {
        var host = request.Host?.Trim() ?? string.Empty;
        if (request.Enabled && (host.Length == 0 || host.Contains(' ', StringComparison.Ordinal)))
        {
            return Invalid("Bitte den SMTP-Server angeben (z. B. smtp.ionos.de).");
        }

        if (request.Port is < 1 or > 65535)
        {
            return Invalid("Der Port muss zwischen 1 und 65535 liegen (üblich: 587 STARTTLS, 465 SSL).");
        }

        if ((request.Enabled || !string.IsNullOrWhiteSpace(request.FromAddress)) && !MailboxAddress.TryParse(request.FromAddress?.Trim() ?? string.Empty, out _))
        {
            return Invalid("Bitte eine gültige Absenderadresse angeben.");
        }

        if (!string.IsNullOrWhiteSpace(request.ReplyTo) && !MailboxAddress.TryParse(request.ReplyTo.Trim(), out _))
        {
            return Invalid("Die Antwortadresse ist ungültig.");
        }

        var account = await db.Set<MailAccount>().SingleOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            account = MailAccount.Create();
            db.Add(account);
        }

        account.Update(request.Enabled, host, request.Port, request.Security, request.UserName, request.FromAddress ?? string.Empty,
            request.FromName, request.ReplyTo);
        if (request.ClearPassword)
        {
            account.SetProtectedPassword(null);
        }
        else if (!string.IsNullOrEmpty(request.Password))
        {
            account.SetProtectedPassword(sender.Protect(request.Password));
        }

        audit.Record("mail.settings.changed", user.UserId, "mail_account", account.Id.ToString(),
            new Dictionary<string, string?> { ["host"] = account.Host, ["enabled"] = account.Enabled ? "true" : "false" });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToDto(account));
    }

    private static async Task<Results<Ok<TestMailResult>, ProblemHttpResult>> TestAsync(
        TestMailRequest request, EstrichOneDbContext db, MailService service, SmtpSender sender, TimeProvider clock, IAuditLog audit,
        ICurrentUser user, CancellationToken cancellationToken)
    {
        var account = await db.Set<MailAccount>().SingleOrDefaultAsync(cancellationToken);
        if (account is null || account.Host.Length == 0 || account.FromAddress.Length == 0)
        {
            return TypedResults.Problem(statusCode: 409, title: "E-Mail nicht eingerichtet", detail: "Bitte zuerst Server und Absender speichern.",
                extensions: new Dictionary<string, object?> { ["code"] = "MAIL_NOT_CONFIGURED" });
        }

        if (!MailboxAddress.TryParse(request.To?.Trim() ?? string.Empty, out _))
        {
            return Invalid("Bitte eine gültige Empfängeradresse angeben.");
        }

        var now = clock.GetUtcNow();
        var (subject, body) = await service.RenderAsync(MailTemplateKeys.Test,
            new Dictionary<string, string?> { ["zeitpunkt"] = now.ToOffset(TimeSpan.FromHours(1)).ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) },
            cancellationToken);
        var log = MailLogEntry.Queue(request.To!.Trim(), subject, body, MailTemplateKeys.Test, "test", null, null, null, now);
        string? error = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            await sender.SendAsync(account, log.To, subject, body, [], timeout.Token);
            log.Sent(clock.GetUtcNow());
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            error = SmtpSender.Describe(exception).Message;
            log.Failed(error, clock.GetUtcNow(), permanent: true);
        }

        account.Tested(now, error);
        db.Add(log);
        audit.Record("mail.test", user.UserId, "mail_account", account.Id.ToString(),
            new Dictionary<string, string?> { ["ok"] = error is null ? "true" : "false" });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new TestMailResult(error is null, error, now));
    }

    private static async Task<Ok<List<MailTemplateDto>>> GetTemplatesAsync(EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var custom = await db.Set<MailTemplate>().AsNoTracking().ToDictionaryAsync(t => t.Key, cancellationToken);
        return TypedResults.Ok(DefaultMailTemplates.All.Select(d => custom.TryGetValue(d.Key, out var c)
            ? new MailTemplateDto(d.Key, d.Name, c.Subject, c.Body, true, d.Placeholders)
            : new MailTemplateDto(d.Key, d.Name, d.Subject, d.Body, false, d.Placeholders)).ToList());
    }

    private static async Task<Results<Ok<MailTemplateDto>, ProblemHttpResult>> UpdateTemplateAsync(
        string key, UpdateMailTemplateRequest request, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (DefaultMailTemplates.Find(key) is not { } definition)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 300 || string.IsNullOrWhiteSpace(request.Body)
            || request.Body.Length > 20_000)
        {
            return Invalid("Betreff (bis 300 Zeichen) und Text (bis 20 000 Zeichen) sind Pflicht.");
        }

        var template = await db.Set<MailTemplate>().SingleOrDefaultAsync(t => t.Key == key, cancellationToken);
        if (template is null)
        {
            template = MailTemplate.Create(key);
            db.Add(template);
        }

        template.Update(request.Subject, request.Body);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new MailTemplateDto(key, definition.Name, template.Subject, template.Body, true, definition.Placeholders));
    }

    private static async Task<Results<Ok<MailTemplateDto>, ProblemHttpResult>> ResetTemplateAsync(
        string key, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (DefaultMailTemplates.Find(key) is not { } definition)
        {
            return NotFound();
        }

        await db.Set<MailTemplate>().Where(t => t.Key == key).ExecuteDeleteAsync(cancellationToken);
        return TypedResults.Ok(new MailTemplateDto(key, definition.Name, definition.Subject, definition.Body, false, definition.Placeholders));
    }

    private static async Task<Ok<List<MailLogItem>>> GetLogAsync(EstrichOneDbContext db, MailStatus? status, int? take,
        CancellationToken cancellationToken)
    {
        var query = db.Set<MailLogEntry>().AsNoTracking();
        if (status is not null)
        {
            query = query.Where(m => m.Status == status);
        }

        return TypedResults.Ok(await query.OrderByDescending(m => m.CreatedAt).Take(Math.Clamp(take ?? 200, 1, 1000))
            .Select(m => new MailLogItem(m.Id, m.CreatedAt, m.To, m.Subject, m.TemplateKey, m.Category, m.RelatedEntity, m.Status, m.Attempts,
                m.SentAt, m.Error, m.AttachmentNames))
            .ToListAsync(cancellationToken));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RetryAsync(Guid id, EstrichOneDbContext db, TimeProvider clock,
        MailSignal signal, CancellationToken cancellationToken)
    {
        var entry = await db.Set<MailLogEntry>().SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (entry is null)
        {
            return NotFound();
        }

        if (entry.Status == MailStatus.Failed && entry.Body.Length == 0)
        {
            return Invalid("Diese Mail kann nicht erneut gesendet werden (Inhalt nicht mehr vorhanden).");
        }

        entry.Retry(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        signal.Wake();
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult Invalid(string detail) =>
        TypedResults.Problem(statusCode: 422, title: "Ungültige Angaben", detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });

    private static ProblemHttpResult NotFound() =>
        TypedResults.Problem(statusCode: 404, title: "Nicht gefunden", extensions: new Dictionary<string, object?> { ["code"] = "NOT_FOUND" });
}
