using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Infrastructure.Modules;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Notices;

/// <summary>
/// Systemhinweise: Ankündigungen (Banner) und Wartungsmodus. Lesen ist ohne Anmeldung möglich (Clients zeigen Banner
/// bzw. Wartungsseite auch vor der Anmeldung); gesetzt werden sie von der Hosting-Konsole mit einem Instanz-Schlüssel.
/// </summary>
public sealed partial class NoticesModule : IModule
{
    public const int MaxAnnouncements = 20;
    private static readonly string[] Severities = ["info", "warning", "critical"];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<NoticeSettings>().Bind(configuration.GetSection(NoticeSettings.SectionName));
        services.AddSingleton<NoticeStore>();
        services.AddTransient<IStartupFilter, MaintenanceStartupFilter>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        var notices = api.MapGroup("/system/notices").WithTags("Systemhinweise");

        notices.MapGet("/", (NoticeStore store) => TypedResults.Ok(store.ForClients()))
            .WithName("GetSystemNotices")
            .WithSummary("Aktuelle Ankündigungen und geplante bzw. laufende Wartung (ohne Anmeldung lesbar)")
            .AllowAnonymous();

        notices.MapPut("/", Results<NoContent, NotFound, UnauthorizedHttpResult, ValidationProblem> (
                SystemNoticesDocument document, HttpContext http, NoticeStore store, IOptions<NoticeSettings> options,
                ILogger<NoticesModule> logger) =>
            {
                var token = options.Value.ManagementToken;
                if (string.IsNullOrWhiteSpace(token))
                {
                    return TypedResults.NotFound();
                }

                if (!IsAuthorized(http.Request.Headers.Authorization.ToString(), token))
                {
                    return TypedResults.Unauthorized();
                }

                var errors = Validate(document);
                if (errors.Count > 0)
                {
                    return TypedResults.ValidationProblem(errors);
                }

                store.Replace(document);
                LogReplaced(logger, document.Announcements.Count, document.Maintenance is not null);
                return TypedResults.NoContent();
            })
            .WithName("SetSystemNotices")
            .WithSummary("Systemhinweise vollständig ersetzen – nur Hosting-Konsole (Authorization: Bearer <Instanz-Schlüssel>)")
            .AllowAnonymous();
    }

    private static bool IsAuthorized(string header, string token)
    {
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(header[prefix.Length..].Trim()));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public static Dictionary<string, string[]> Validate(SystemNoticesDocument document)
    {
        var errors = new Dictionary<string, string[]>();
        var announcements = document.Announcements ?? [];
        if (announcements.Count > MaxAnnouncements)
        {
            errors["announcements"] = [$"Höchstens {MaxAnnouncements} Ankündigungen."];
        }

        for (var i = 0; i < announcements.Count; i++)
        {
            var a = announcements[i];
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(a.Id) || a.Id.Length > 64) problems.Add("Id fehlt oder ist zu lang.");
            if (string.IsNullOrWhiteSpace(a.Title) || a.Title.Length > 120) problems.Add("Titel fehlt oder ist länger als 120 Zeichen.");
            if (a.Text is null || a.Text.Length > 2000) problems.Add("Text fehlt oder ist länger als 2000 Zeichen.");
            if (!Severities.Contains(a.Severity)) problems.Add("Schweregrad muss info, warning oder critical sein.");
            if (a.StartsAt is not null && a.EndsAt is not null && a.EndsAt <= a.StartsAt) problems.Add("Ende liegt vor dem Beginn.");
            if (problems.Count > 0)
            {
                errors[$"announcements[{i}]"] = [.. problems];
            }
        }

        if (document.Maintenance is { } window)
        {
            if (window.EndsAt is not null && window.EndsAt <= window.StartsAt) errors["maintenance"] = ["Ende liegt vor dem Beginn."];
            if (window.Message is { Length: > 2000 }) errors["maintenance.message"] = ["Text ist länger als 2000 Zeichen."];
        }

        return errors;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Systemhinweise gesetzt: {Count} Ankündigung(en), Wartung geplant: {Maintenance}.")]
    private static partial void LogReplaced(ILogger logger, int count, bool maintenance);

    private sealed class MaintenanceStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseMiddleware<MaintenanceGate>();
            next(app);
        };
    }
}
