using System.Text.Json;
using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Mail.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EstrichOne.Api.Modules.Mail;

/// <summary>Versendet über das eingerichtete SMTP-Konto. Getrennt, damit Tests und Testmail denselben Weg nehmen.</summary>
public sealed class SmtpSender(IDataProtectionProvider protection)
{
    public const string ProtectorPurpose = "EstrichOne.Mail.Password";

    public string Protect(string password) => protection.CreateProtector(ProtectorPurpose).Protect(password);

    public async Task SendAsync(MailAccount account, string to, string subject, string body, IReadOnlyList<MailAttachment> attachments,
        CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(account.FromName ?? string.Empty, account.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        if (account.ReplyTo is not null)
        {
            message.ReplyTo.Add(MailboxAddress.Parse(account.ReplyTo));
        }

        message.Subject = subject;
        var builder = new BodyBuilder { TextBody = body };
        foreach (var attachment in attachments)
        {
            builder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        message.Body = builder.ToMessageBody();
        using var smtp = new SmtpClient { Timeout = 30_000 };
        var security = account.Security switch
        {
            MailSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
            MailSecurity.None => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls,
        };
        await smtp.ConnectAsync(account.Host, account.Port, security, cancellationToken);
        if (account.UserName is not null)
        {
            var password = account.ProtectedPassword is null
                ? string.Empty
                : protection.CreateProtector(ProtectorPurpose).Unprotect(account.ProtectedPassword);
            await smtp.AuthenticateAsync(account.UserName, password, cancellationToken);
        }

        await smtp.SendAsync(message, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);
    }

    /// <summary>Verständliche Ursache statt Ausnahme-Text.</summary>
    public static (string Message, bool Permanent) Describe(Exception exception) => exception switch
    {
        AuthenticationException => ("Anmeldung am Mailserver fehlgeschlagen – Benutzername oder Passwort prüfen.", true),
        SslHandshakeException => ("Verschlüsselung fehlgeschlagen – Sicherheit (STARTTLS/SSL) und Port prüfen.", false),
        SmtpCommandException command when command.ErrorCode == SmtpErrorCode.RecipientNotAccepted =>
            ($"Empfänger abgelehnt: {command.Message}", true),
        SmtpCommandException command when command.ErrorCode == SmtpErrorCode.SenderNotAccepted =>
            ($"Absenderadresse abgelehnt: {command.Message}", true),
        SmtpCommandException command => ($"Mailserver meldet: {command.Message}", false),
        SmtpProtocolException protocol => ($"Protokollfehler: {protocol.Message}", false),
        ParseException => ("Ungültige E-Mail-Adresse.", true),
        System.Net.Sockets.SocketException socket => ($"Mailserver nicht erreichbar ({socket.SocketErrorCode}) – Server und Port prüfen.", false),
        TimeoutException or OperationCanceledException => ("Zeitüberschreitung beim Mailserver.", false),
        System.Security.Cryptography.CryptographicException => ("Gespeichertes Passwort nicht lesbar – bitte neu eingeben.", true),
        IOException io => ($"Verbindung abgebrochen: {io.Message}", false),
        _ => (exception.Message, false),
    };
}

internal sealed record StoredAttachment(string FileName, string ContentType, string Base64);

/// <summary>Öffentliche Schnittstelle für andere Module und Verwaltung der Warteschlange.</summary>
internal sealed class MailService(
    EstrichOneDbContext db, SmtpSender sender, TimeProvider clock, ICompanyProfiles company, IOptions<EstrichOneOptions> options,
    MailSignal signal, ISystemSettings systemSettings) : IMailService
{
    public const long MaxAttachmentBytes = 10 * 1024 * 1024;

    public async Task<MailAccount?> AccountAsync(CancellationToken cancellationToken) =>
        await db.Set<MailAccount>().SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken) =>
        (await AccountAsync(cancellationToken))?.IsUsable == true;

    public async Task<Guid?> QueueAsync(MailRequest request, CancellationToken cancellationToken) =>
        await QueueCoreAsync(request, null, cancellationToken);

    public async Task<Guid?> QueueTemplateAsync(string templateKey, string recipient, IReadOnlyDictionary<string, string?> values,
        string? relatedEntity = null, IReadOnlyList<MailAttachment>? attachments = null, CancellationToken cancellationToken = default)
    {
        if (!await NotificationEnabledAsync(templateKey, cancellationToken))
        {
            return null;
        }

        var (subject, body) = await RenderAsync(templateKey, values, cancellationToken);
        return await QueueCoreAsync(new MailRequest(recipient, subject, body, templateKey, relatedEntity, attachments), templateKey, cancellationToken);
    }

    /// <summary>Schalter aus Einstellungen → Benachrichtigungen (Bereich <c>notifications</c> der Installationseinstellungen).</summary>
    private async Task<bool> NotificationEnabledAsync(string templateKey, CancellationToken cancellationToken)
    {
        var notifications = (await systemSettings.GetAsync(cancellationToken)).Notifications;
        return templateKey switch
        {
            MailTemplateKeys.ApprovalRequested => notifications.ApprovalRequested,
            MailTemplateKeys.ApprovalDecided => notifications.ApprovalDecided,
            MailTemplateKeys.HelpdeskUpdated => notifications.HelpdeskUpdates,
            MailTemplateKeys.UserWelcome or MailTemplateKeys.PasswordReset => notifications.NewUserLetterByEmail,
            _ => true,
        };
    }

    public async Task<(string Subject, string Body)> RenderAsync(string templateKey, IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken)
    {
        var definition = DefaultMailTemplates.Find(templateKey)
            ?? throw new ArgumentException($"Unbekannte Vorlage '{templateKey}'.", nameof(templateKey));
        var custom = await db.Set<MailTemplate>().AsNoTracking().SingleOrDefaultAsync(t => t.Key == templateKey, cancellationToken);
        var all = new Dictionary<string, string?>(values, StringComparer.Ordinal)
        {
            ["firma"] = values.GetValueOrDefault("firma") ?? await company.GetNameAsync(cancellationToken) ?? options.Value.ServerName,
            ["adresse"] = values.GetValueOrDefault("adresse") ?? options.Value.PublicUrl?.ToString().TrimEnd('/') ?? string.Empty,
        };
        return (DefaultMailTemplates.Render(custom?.Subject ?? definition.Subject, all), DefaultMailTemplates.Render(custom?.Body ?? definition.Body, all));
    }

    private async Task<Guid?> QueueCoreAsync(MailRequest request, string? templateKey, CancellationToken cancellationToken)
    {
        if (!await IsConfiguredAsync(cancellationToken))
        {
            return null;
        }

        var attachments = request.Attachments ?? [];
        if (attachments.Sum(a => (long)a.Content.Length) > MaxAttachmentBytes)
        {
            throw new ArgumentException("Anhänge sind zusammen größer als 10 MB.", nameof(request));
        }

        if (!MailboxAddress.TryParse(request.To, out _))
        {
            throw new ArgumentException("Ungültige E-Mail-Adresse.", nameof(request));
        }

        var entry = MailLogEntry.Queue(request.To.Trim(), Limit(request.Subject, 300), request.TextBody, templateKey, request.Category,
            request.RelatedEntity, attachments.Count == 0 ? null : JsonSerializer.Serialize(attachments.Select(a =>
                new StoredAttachment(a.FileName, a.ContentType, Convert.ToBase64String(a.Content)))),
            attachments.Count == 0 ? null : Limit(string.Join(", ", attachments.Select(a => a.FileName)), 1000), clock.GetUtcNow());
        db.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        signal.Wake();
        return entry.Id;
    }

    /// <summary>Fällige Mails versenden (Hintergrunddienst).</summary>
    public async Task<int> SendDueAsync(CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is not { IsUsable: true })
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var due = await db.Set<MailLogEntry>().Where(m => m.Status == MailStatus.Queued && m.NextAttemptAt <= now)
            .OrderBy(m => m.NextAttemptAt).Take(20).ToListAsync(cancellationToken);
        foreach (var mail in due)
        {
            try
            {
                var attachments = mail.AttachmentsJson is null
                    ? []
                    : JsonSerializer.Deserialize<List<StoredAttachment>>(mail.AttachmentsJson)!
                        .Select(a => new MailAttachment(a.FileName, a.ContentType, Convert.FromBase64String(a.Base64))).ToList();
                await sender.SendAsync(account, mail.To, mail.Subject, mail.Body, attachments, cancellationToken);
                mail.Sent(clock.GetUtcNow());
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                var (message, permanent) = SmtpSender.Describe(exception);
                mail.Failed(message, clock.GetUtcNow(), permanent);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }

    private static string Limit(string value, int length) => value.Length > length ? value[..length] : value;
}

/// <summary>Weckt den Versanddienst sofort nach dem Einreihen.</summary>
public sealed class MailSignal : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Dispose() => _signal.Dispose();

    public void Wake()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}

internal sealed partial class MailWorker(IServiceScopeFactory scopes, MailSignal signal, TimeProvider clock, ILogger<MailWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastCleanup = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<MailService>();
                while (await service.SendDueAsync(stoppingToken) > 0)
                {
                }

                if (clock.GetUtcNow() - lastCleanup > TimeSpan.FromDays(1))
                {
                    // Versandprotokoll 180 Tage aufbewahren.
                    var cutoff = clock.GetUtcNow().AddDays(-180);
                    await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<MailLogEntry>()
                        .Where(m => m.CreatedAt < cutoff && m.Status != MailStatus.Queued).ExecuteDeleteAsync(stoppingToken);
                    lastCleanup = clock.GetUtcNow();
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception.Message);
            }

            try
            {
                await signal.WaitAsync(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "E-Mail-Versand: {Error}")]
    private static partial void LogFailed(ILogger logger, string error);
}
