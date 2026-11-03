using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using EstrichOne.Hosting.Infrastructure;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Notifications;

public sealed record DeliveryResult(bool Attempted, bool Success, string Summary);

/// <summary>Stellt Benachrichtigungen per E-Mail (SMTP, STARTTLS) und Webhook zu. In der Simulation wird nur protokolliert.</summary>
public sealed partial class NotificationSender(HttpClient http, IOptions<HostingOptions> options, ILogger<NotificationSender> logger)
{
    public async Task<DeliveryResult> SendAsync(NotificationSettings settings, string subject, string text, string severity,
        string? tenantSlug, bool resolved, CancellationToken cancellationToken)
    {
        if (!settings.EmailConfigured && !settings.WebhookConfigured)
        {
            return new DeliveryResult(false, true, "keine Zustellung eingerichtet");
        }

        var parts = new List<string>();
        var success = true;
        if (settings.EmailConfigured)
        {
            try
            {
                if (options.Value.IsFake)
                {
                    LogSimulated(logger, "E-Mail", subject);
                }
                else
                {
                    using var client = new SmtpClient(settings.SmtpHost!, settings.SmtpPort)
                    {
                        EnableSsl = settings.SmtpStartTls,
                        DeliveryMethod = SmtpDeliveryMethod.Network,
                        Timeout = 20000,
                    };
                    if (!string.IsNullOrEmpty(settings.SmtpUser))
                    {
                        client.Credentials = new NetworkCredential(settings.SmtpUser, settings.SmtpPassword);
                    }

                    using var message = new MailMessage { From = new MailAddress(settings.SmtpFrom!, "EstrichONE Hosting-Konsole"), Subject = subject, Body = text };
                    foreach (var to in settings.EmailTo!.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        message.To.Add(to);
                    }

                    await client.SendMailAsync(message, cancellationToken);
                }

                parts.Add($"E-Mail an {settings.EmailTo}");
            }
            catch (Exception exception) when (exception is SmtpException or InvalidOperationException or FormatException or IOException)
            {
                success = false;
                parts.Add($"E-Mail fehlgeschlagen: {exception.Message}");
                logger.LogWarning(exception, "Benachrichtigung per E-Mail fehlgeschlagen.");
            }
        }

        if (settings.WebhookConfigured)
        {
            try
            {
                if (options.Value.IsFake)
                {
                    LogSimulated(logger, "Webhook", subject);
                }
                else
                {
                    using var response = await http.PostAsJsonAsync(new Uri(settings.WebhookUrl!), new
                    {
                        text = $"{subject}\n{text}",
                        title = subject,
                        message = text,
                        severity,
                        tenant = tenantSlug,
                        resolved,
                        console = options.Value.ConsoleDomain,
                        at = DateTime.UtcNow,
                    }, cancellationToken);
                    response.EnsureSuccessStatusCode();
                }

                parts.Add("Webhook");
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                success = false;
                parts.Add($"Webhook fehlgeschlagen: {exception.Message}");
                logger.LogWarning(exception, "Benachrichtigung per Webhook fehlgeschlagen.");
            }
        }

        return new DeliveryResult(true, success, string.Join("; ", parts));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Simulation: {Channel} – {Subject}")]
    private static partial void LogSimulated(ILogger logger, string channel, string subject);
}
