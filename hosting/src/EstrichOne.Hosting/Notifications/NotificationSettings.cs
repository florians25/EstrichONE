using EstrichOne.Hosting.Domain;
using EstrichOne.Hosting.Infrastructure;

namespace EstrichOne.Hosting.Notifications;

/// <summary>Zustellung von Benachrichtigungen: E-Mail (SMTP) und/oder Webhook (JSON, z. B. Slack/Mattermost/Teams-kompatibel mit „text“).</summary>
public sealed record NotificationSettings(
    string? EmailTo,
    string? SmtpHost,
    int SmtpPort,
    string? SmtpUser,
    string? SmtpPassword,
    string? SmtpFrom,
    bool SmtpStartTls,
    string? WebhookUrl)
{
    public bool EmailConfigured => !string.IsNullOrWhiteSpace(EmailTo) && !string.IsNullOrWhiteSpace(SmtpHost) && !string.IsNullOrWhiteSpace(SmtpFrom);

    public bool WebhookConfigured => !string.IsNullOrWhiteSpace(WebhookUrl);

    private const string Prefix = "notify.";

    public static async Task<NotificationSettings> LoadAsync(ConsoleSettings settings, CancellationToken cancellationToken)
    {
        async Task<string?> Get(string key) => await settings.GetAsync(Prefix + key, cancellationToken) is { Length: > 0 } value ? value : null;
        return new NotificationSettings(
            await Get("emailTo"), await Get("smtpHost"),
            int.TryParse(await Get("smtpPort"), System.Globalization.CultureInfo.InvariantCulture, out var port) ? port : 587,
            await Get("smtpUser"), await Get("smtpPassword"), await Get("smtpFrom"),
            await Get("smtpStartTls") != "false", await Get("webhookUrl"));
    }

    public async Task SaveAsync(ConsoleSettings settings, CancellationToken cancellationToken)
    {
        await settings.SetAsync(Prefix + "emailTo", EmailTo ?? string.Empty, cancellationToken);
        await settings.SetAsync(Prefix + "smtpHost", SmtpHost ?? string.Empty, cancellationToken);
        await settings.SetAsync(Prefix + "smtpPort", SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
        await settings.SetAsync(Prefix + "smtpUser", SmtpUser ?? string.Empty, cancellationToken);
        await settings.SetAsync(Prefix + "smtpPassword", SmtpPassword ?? string.Empty, cancellationToken);
        await settings.SetAsync(Prefix + "smtpFrom", SmtpFrom ?? string.Empty, cancellationToken);
        await settings.SetAsync(Prefix + "smtpStartTls", SmtpStartTls ? "true" : "false", cancellationToken);
        await settings.SetAsync(Prefix + "webhookUrl", WebhookUrl ?? string.Empty, cancellationToken);
    }

    /// <summary>Eingabe aus der Oberfläche; ein leeres Passwort behält das gespeicherte.</summary>
    public sealed record Input(string? EmailTo, string? SmtpHost, int? SmtpPort, string? SmtpUser, string? SmtpPassword, string? SmtpFrom,
        bool? SmtpStartTls, string? WebhookUrl, bool? ClearPassword);

    public static (NotificationSettings? Value, Dictionary<string, string> Errors) Validate(Input input, NotificationSettings current)
    {
        var errors = new Dictionary<string, string>();
        static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var to = Blank(input.EmailTo);
        var from = Blank(input.SmtpFrom);
        var host = Blank(input.SmtpHost);
        foreach (var address in (to ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Validation.CheckEmail(address, required: false) is { } error) errors["emailTo"] = error;
        }

        if (Validation.CheckEmail(from, required: false) is { } fromError) errors["smtpFrom"] = fromError;
        if (host is not null && (host.Length > 253 || host.Any(c => char.IsWhiteSpace(c) || c is '/' or '"' or '\'')))
        {
            errors["smtpHost"] = "Ungültiger SMTP-Server.";
        }

        if (to is not null && (host is null || from is null))
        {
            errors["smtpHost"] = "Für E-Mails bitte SMTP-Server und Absender angeben.";
        }

        var port = input.SmtpPort ?? 587;
        if (port is < 1 or > 65535) errors["smtpPort"] = "Ungültiger Port.";
        var webhook = Blank(input.WebhookUrl);
        if (webhook is not null && (!Uri.TryCreate(webhook, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || webhook.Length > 500))
        {
            errors["webhookUrl"] = "Bitte eine vollständige http(s)-Adresse angeben.";
        }

        if (Validation.CheckText(Blank(input.SmtpUser), "Der SMTP-Benutzer", 200, false) is { } userError) errors["smtpUser"] = userError;
        var password = input.ClearPassword == true ? null : Blank(input.SmtpPassword) ?? current.SmtpPassword;
        return errors.Count > 0
            ? (null, errors)
            : (new NotificationSettings(to, host, port, Blank(input.SmtpUser), password, from, input.SmtpStartTls != false, webhook), errors);
    }

    /// <summary>Für die Oberfläche: Passwort nie zurückgeben.</summary>
    public object ToView() => new
    {
        emailTo = EmailTo, smtpHost = SmtpHost, smtpPort = SmtpPort, smtpUser = SmtpUser, hasSmtpPassword = !string.IsNullOrEmpty(SmtpPassword),
        smtpFrom = SmtpFrom, smtpStartTls = SmtpStartTls, webhookUrl = WebhookUrl, emailConfigured = EmailConfigured, webhookConfigured = WebhookConfigured,
    };
}
