using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Teams.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Teams;

/// <summary>Was alle Angemeldeten über die Teams-Anbindung wissen müssen (Navigation, Bauakte, Startseite).</summary>
/// <param name="ChatMode">Wo Unterhaltungen stattfinden: <c>estrichOne</c>, <c>teams</c> oder <c>both</c>.</param>
/// <param name="ChatReadOnly">Eingebauter Chat nur noch als Archiv (lesen, exportieren).</param>
/// <param name="Available">Teams-Verknüpfungen nutzbar (Modus nicht „nur EstrichONE-Chat“).</param>
public sealed record TeamsStatusResponse(
    TeamsChatMode ChatMode, bool ChatReadOnly, bool Available, bool GraphConfigured, TeamsPostVia PostVia, bool CanManage, string? SiteTeamName,
    string? SiteTeamUrl, string? SiteTeamAppUrl, string TeamsWebUrl = "https://teams.microsoft.com", string TeamsAppUrl = "msteams:");

public sealed record TeamsSettingsResponse(
    TeamsChatMode ChatMode, string? TenantId, string? ClientId, TeamsAuthKind AuthKind, bool HasClientSecret, string? CertificateThumbprint,
    DateTimeOffset? CertificateExpiresAt, string? SiteTeamId, string? SiteTeamName, string? SiteTeamUrl, TeamsLinkKind DefaultLinkKind, bool AutoLink,
    bool SyncMembers, TeamsPostVia PostVia, bool HasWebhook, string? WebhookHost, bool PostDefects, bool PostAcceptance, bool PostSchedule,
    string? SenderName, DateTimeOffset? SenderConnectedAt, DateTimeOffset? LastTestAt, bool? LastTestOk, string? LastTestError,
    DateTimeOffset? LastPostAt, string? LastPostError, bool GraphConfigured, int MappedUsers, int ActiveUsers, int LinkedBauakten,
    IReadOnlyList<TeamsPermissionInfo> Permissions, long Version);

/// <summary>Geheimnisse: <c>null</c> = unverändert; <c>Clear…</c> entfernt sie. <paramref name="SiteTeam"/>: ID oder Teams-Link, leer = entfernen, <c>null</c> = unverändert.</summary>
public sealed record SaveTeamsSettingsRequest(
    TeamsChatMode ChatMode = TeamsChatMode.EstrichOne, [property: MaxLength(100)] string? TenantId = null, [property: MaxLength(100)] string? ClientId = null,
    TeamsAuthKind AuthKind = TeamsAuthKind.Secret, [property: MaxLength(500)] string? ClientSecret = null, bool ClearClientSecret = false,
    [property: MaxLength(20_000)] string? CertificatePem = null, bool ClearCertificate = false, [property: MaxLength(2000)] string? SiteTeam = null,
    TeamsLinkKind DefaultLinkKind = TeamsLinkKind.Channel, bool AutoLink = false, bool SyncMembers = true, TeamsPostVia PostVia = TeamsPostVia.Off,
    [property: MaxLength(2000)] string? WebhookUrl = null, bool ClearWebhook = false, bool PostDefects = true, bool PostAcceptance = true,
    bool PostSchedule = true, long? Version = null);

public sealed record TeamsTestResponse(bool Ok, IReadOnlyList<TeamsTestStep> Steps, DateTimeOffset At);

public sealed record TeamsWebhookTestRequest([property: MaxLength(2000)] string? Url = null);

public sealed record TeamsUserMapping(
    Guid UserId, string Name, string? Email, bool Active, string? AadUserId, string? UserPrincipalName, string? AccountName, bool Manual, string? Error,
    DateTimeOffset? CheckedAt);

public sealed record TeamsMatchResponse(int Matched, int NotFound, int Manual, int Skipped);

/// <param name="Account">UPN, E-Mail oder Objekt-ID; leer = wieder automatisch über die E-Mail-Adresse.</param>
public sealed record MapTeamsUserRequest([property: MaxLength(320)] string? Account = null);

public sealed record TeamsSenderStart(string UserCode, string VerificationUri, DateTimeOffset ExpiresAt, int IntervalSeconds, string? Message);

public sealed record TeamsSenderState(bool Connected, bool Pending, string? SenderName, DateTimeOffset? ConnectedAt);

public sealed record TeamsLinkResponse(
    TeamsLinkKind Kind, bool Created, string? DisplayName, string WebUrl, string AppUrl, string? TeamId, string? ChannelId, string? ChatId,
    bool SyncMembers, DateTimeOffset? LastSyncAt, string? LastSyncError, int SyncedMembers, int UnmappedMembers, DateTimeOffset? LastPostAt,
    string? LastPostError, bool HasWebhook, DateTimeOffset LinkedAt, string? LinkedByName);

/// <param name="CanManage">Verknüpfen, abgleichen, lösen (bauakten.edit).</param>
/// <param name="CanCreate">Automatisch anlegen möglich (Graph eingerichtet).</param>
public sealed record BauakteTeamsResponse(
    Guid BauakteId, TeamsChatMode ChatMode, TeamsLinkResponse? Link, bool CanManage, bool CanCreate, bool CanCreateChannel, TeamsLinkKind DefaultLinkKind);

/// <param name="Kind">Automatisch anlegen: Kanal im Team „Baustellen“ oder Gruppenchat. Ohne Angabe: <paramref name="Url"/> verknüpfen.</param>
/// <param name="Url">Vorhandener Kanal oder Chat (Link aus Teams).</param>
/// <param name="WebhookUrl">Optional: Teams-Workflows-Webhook nur für diese Bauakte.</param>
public sealed record LinkBauakteTeamsRequest(
    TeamsLinkKind? Kind = null, [property: MaxLength(2000)] string? Url = null, [property: MaxLength(2000)] string? WebhookUrl = null, bool? SyncMembers = null);

internal static partial class TeamsEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/teams/status", StatusAsync).WithTags("Microsoft Teams").WithName("GetTeamsStatus")
            .WithSummary("Chat-Modus und Teams-Anbindung für Navigation, Bauakte und Startseite (alle Angemeldeten)");

        var settings = api.MapGroup("/settings/integrations/teams").WithTags("Microsoft Teams").RequirePermission(Permissions.SettingsIntegrations)
            .AddEndpointFilter(GuardAsync);
        settings.MapGet("/", GetSettingsAsync).WithName("GetTeamsSettings").WithSummary("Einstellungen (Geheimnisse nie im Klartext)");
        settings.MapPut("/", SaveSettingsAsync).WithName("SaveTeamsSettings")
            .WithSummary("Speichern (Schlüssel, Zertifikat und Webhook verschlüsselt; null = unverändert)")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        settings.MapPost("/test", TestAsync).WithName("TestTeamsConnection").WithSummary("Verbindungstest: Anmeldung, Berechtigungen, Team, Dienstkonto")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        settings.MapPost("/webhook/test", TestWebhookAsync).WithName("TestTeamsWebhook").WithSummary("Testkarte an den Workflows-Webhook senden")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status424FailedDependency);
        settings.MapGet("/users", UsersAsync).WithName("ListTeamsUserMappings").WithSummary("Zuordnung Benutzer ↔ Microsoft-Konto");
        settings.MapPost("/users/match", MatchAsync).WithName("MatchTeamsUsers").WithSummary("Alle Benutzer über E-Mail/UPN zuordnen (von Hand gesetzte bleiben)")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status424FailedDependency)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        settings.MapPut("/users/{userId:guid}", MapUserAsync).WithName("MapTeamsUser").WithSummary("Zuordnung von Hand korrigieren")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status424FailedDependency);
        settings.MapPost("/sender/start", StartSenderAsync).WithName("StartTeamsSenderLogin")
            .WithSummary("Dienstkonto verbinden: Gerätecode-Anmeldung starten (für Karten über Microsoft Graph)").ProducesProblem(StatusCodes.Status409Conflict);
        settings.MapPost("/sender/poll", PollSenderAsync).WithName("PollTeamsSenderLogin").WithSummary("Stand der Gerätecode-Anmeldung")
            .ProducesProblem(StatusCodes.Status409Conflict);
        settings.MapDelete("/sender", DisconnectSenderAsync).WithName("DisconnectTeamsSender").WithSummary("Dienstkonto trennen (Token löschen)");

        var bauakte = api.MapGroup("/bauakten/{id:guid}/teams").WithTags("Microsoft Teams").AddEndpointFilter(GuardAsync);
        bauakte.MapGet("/", BauakteAsync).WithName("GetBauakteTeams").WithSummary("Verknüpfte Teams-Unterhaltung der Bauakte")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenRead);
        bauakte.MapPost("/", LinkAsync).WithName("LinkBauakteTeams")
            .WithSummary("Kanal oder Gruppenchat anlegen bzw. vorhandenen per Link verknüpfen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status424FailedDependency)
            .RequirePermission(Permissions.BauaktenEdit);
        bauakte.MapPost("/sync", SyncAsync).WithName("SyncBauakteTeamsMembers").WithSummary("Mitglieder jetzt abgleichen (Ergebnis an der Verknüpfung)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenEdit);
        bauakte.MapDelete("/", UnlinkAsync).WithName("UnlinkBauakteTeams").WithSummary("Verknüpfung lösen (in Teams wird nichts gelöscht)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenEdit);
    }

    // ---------- Status ----------

    private static async Task<Ok<TeamsStatusResponse>> StatusAsync(TeamsService teams, IUserPermissions permissions, CancellationToken cancellationToken)
    {
        var row = await teams.SettingsAsync(cancellationToken);
        return TypedResults.Ok(new TeamsStatusResponse(row.ChatMode, row.ChatMode == TeamsChatMode.Teams, row.ChatMode != TeamsChatMode.EstrichOne,
            row.GraphConfigured, row.PostVia, await permissions.HasAsync(Permissions.SettingsIntegrations, cancellationToken), row.SiteTeamName,
            row.SiteTeamUrl, row.SiteTeamUrl is null ? null : TeamsService.AppUrl(row.SiteTeamUrl)));
    }

    // ---------- Einstellungen ----------

    private static async Task<TeamsSettingsResponse> ToResponseAsync(TeamsService teams, TeamsSettingsRow row, CancellationToken cancellationToken)
    {
        var db = teams.Db;
        var active = await db.Set<User>().AsNoTracking().Where(u => u.IsActive).Select(u => u.Id).ToListAsync(cancellationToken);
        var mapped = await db.Set<TeamsUserLink>().AsNoTracking().CountAsync(l => l.AadUserId != null && active.Contains(l.UserId), cancellationToken);
        var linked = await db.Set<TeamsBauakteLink>().AsNoTracking().CountAsync(cancellationToken);
        var webhook = teams.Unprotect(row.ProtectedWebhookUrl);
        return new TeamsSettingsResponse(row.ChatMode, row.TenantId, row.ClientId, row.AuthKind, row.ProtectedClientSecret is not null, row.CertificateThumbprint,
            row.CertificateExpiresAt, row.SiteTeamId, row.SiteTeamName, row.SiteTeamUrl, row.DefaultLinkKind, row.AutoLink, row.SyncMembers, row.PostVia,
            row.ProtectedWebhookUrl is not null, webhook is null ? null : (Uri.TryCreate(webhook, UriKind.Absolute, out var uri) ? uri.Host : null),
            row.PostDefects, row.PostAcceptance, row.PostSchedule, row.SenderName, row.SenderConnectedAt, row.LastTestAt, row.LastTestOk, row.LastTestError,
            row.LastPostAt, row.LastPostError, row.GraphConfigured, mapped, active.Count, linked, TeamsService.Permissions, row.Version);
    }

    private static async Task<Ok<TeamsSettingsResponse>> GetSettingsAsync(TeamsService teams, CancellationToken cancellationToken) =>
        TypedResults.Ok(await ToResponseAsync(teams, await teams.SettingsAsync(cancellationToken), cancellationToken));

    [GeneratedRegex(@"^([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+)$")]
    private static partial Regex TenantPattern();

    private static async Task<Results<Ok<TeamsSettingsResponse>, ProblemHttpResult>> SaveSettingsAsync(
        SaveTeamsSettingsRequest request, TeamsService teams, GraphClient graph, IAuditLog audit, ICurrentUser user, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var tenant = Blank(request.TenantId);
        var client = Blank(request.ClientId);
        if (tenant is not null && !TenantPattern().IsMatch(tenant))
        {
            return Invalid("Die Tenant-ID ist die Verzeichnis-ID (GUID) oder die Domain des Mandanten (z. B. firma.onmicrosoft.com).");
        }

        if (client is not null && !Guid.TryParse(client, out _))
        {
            return Invalid("Die Client-ID ist die Anwendungs-ID (GUID) aus der App-Registrierung.");
        }

        var row = await teams.SettingsForUpdateAsync(cancellationToken);
        if (request.Version is { } version && row.Version != 0 && version != row.Version)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Geändert",
                detail: "Die Einstellungen wurden inzwischen geändert. Bitte neu laden.", extensions: Code("VERSION_CONFLICT"));
        }

        var before = (row.ChatMode, row.TenantId, row.ClientId, row.PostVia);
        var credentialsChanged = tenant != row.TenantId || client != row.ClientId || request.AuthKind != row.AuthKind
            || !string.IsNullOrEmpty(request.ClientSecret) || !string.IsNullOrEmpty(request.CertificatePem) || request.ClearClientSecret || request.ClearCertificate;
        row.ChatMode = request.ChatMode;
        row.TenantId = tenant;
        row.ClientId = client;
        row.AuthKind = request.AuthKind;
        if (request.ClearClientSecret)
        {
            row.ProtectedClientSecret = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            row.ProtectedClientSecret = teams.Protect(request.ClientSecret.Trim());
        }

        if (request.ClearCertificate)
        {
            row.ProtectedCertificate = null;
            row.CertificateThumbprint = null;
            row.CertificateExpiresAt = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.CertificatePem))
        {
            try
            {
                using var certificate = GraphClient.LoadCertificate(request.CertificatePem);
                if (certificate.GetRSAPrivateKey() is null)
                {
                    return Invalid("Das Zertifikat braucht einen RSA-Schlüssel.");
                }

                row.ProtectedCertificate = teams.Protect(request.CertificatePem.Trim());
                row.CertificateThumbprint = certificate.Thumbprint;
                row.CertificateExpiresAt = certificate.NotAfter.ToUniversalTime();
            }
            catch (GraphException exception)
            {
                return Invalid(exception.Message);
            }
        }

        if (request.ClearWebhook)
        {
            row.ProtectedWebhookUrl = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.WebhookUrl))
        {
            if (graph.ValidateWebhook(request.WebhookUrl) is { } webhookProblem)
            {
                return Invalid(webhookProblem);
            }

            row.ProtectedWebhookUrl = teams.Protect(request.WebhookUrl.Trim());
        }

        if (!row.AutoLink && request.AutoLink)
        {
            row.AutoLinkSince = clock.GetUtcNow();
        }

        row.DefaultLinkKind = request.DefaultLinkKind;
        row.AutoLink = request.AutoLink;
        row.SyncMembers = request.SyncMembers;
        row.PostVia = request.PostVia;
        row.PostDefects = request.PostDefects;
        row.PostAcceptance = request.PostAcceptance;
        row.PostSchedule = request.PostSchedule;
        if (credentialsChanged)
        {
            graph.Forget();
            row.LastTestAt = null;
            row.LastTestOk = null;
            row.LastTestError = null;
        }

        if (request.SiteTeam is { } siteTeam)
        {
            if (string.IsNullOrWhiteSpace(siteTeam))
            {
                row.SiteTeamId = row.SiteTeamName = row.SiteTeamUrl = null;
            }
            else if (!string.Equals(siteTeam.Trim(), row.SiteTeamId, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var (id, name, url) = await teams.ResolveTeamAsync(row, siteTeam, cancellationToken);
                    row.SiteTeamId = id;
                    row.SiteTeamName = name;
                    row.SiteTeamUrl = url ?? TeamsService.ParseLink(siteTeam)?.WebUrl;
                }
                catch (GraphException exception)
                {
                    return Problem(exception);
                }
            }
        }

        audit.Record("teams.settings_saved", user.UserId, "teams_settings", "1", new Dictionary<string, string?>
        {
            ["chatMode"] = row.ChatMode.ToString(), ["tenant"] = row.TenantId, ["client"] = row.ClientId, ["auth"] = row.AuthKind.ToString(),
            ["postVia"] = row.PostVia.ToString(), ["credentialsChanged"] = credentialsChanged ? "true" : "false",
        });
        if (before.ChatMode != row.ChatMode)
        {
            audit.Record("teams.chat_mode_changed", user.UserId, "teams_settings", "1",
                new Dictionary<string, string?> { ["before"] = before.ChatMode.ToString(), ["after"] = row.ChatMode.ToString() });
        }

        await teams.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(teams, row, cancellationToken));
    }

    private static async Task<Results<Ok<TeamsTestResponse>, ProblemHttpResult>> TestAsync(
        TeamsService teams, IAuditLog audit, ICurrentUser user, TimeProvider clock, CancellationToken cancellationToken)
    {
        var row = await teams.SettingsForUpdateAsync(cancellationToken);
        if (!row.GraphConfigured)
        {
            return NotConfigured();
        }

        var steps = await teams.TestAsync(row, cancellationToken);
        var ok = steps.All(s => s.Ok);
        var now = clock.GetUtcNow();
        row.LastTestAt = now;
        row.LastTestOk = ok;
        row.LastTestError = ok ? null : TeamsService.Clip(string.Join(" ", steps.Where(s => !s.Ok).Select(s => $"{s.Name}: {s.Detail}")), 2000);
        audit.Record("teams.connection_tested", user.UserId, "teams_settings", "1", new Dictionary<string, string?> { ["ok"] = ok ? "true" : "false" });
        await teams.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new TeamsTestResponse(ok, steps, now));
    }

    private static async Task<Results<Ok<TeamsTestResponse>, ProblemHttpResult>> TestWebhookAsync(
        TeamsWebhookTestRequest request, TeamsService teams, GraphClient graph, TimeProvider clock, IAuditLog audit, ICurrentUser user,
        CancellationToken cancellationToken)
    {
        var row = await teams.SettingsAsync(cancellationToken);
        var url = Blank(request.Url) ?? teams.Unprotect(row.ProtectedWebhookUrl);
        if (url is null)
        {
            return Invalid("Bitte zuerst die Webhook-URL aus Teams › Workflows eintragen.");
        }

        if (graph.ValidateWebhook(url) is { } problem)
        {
            return Invalid(problem);
        }

        var card = teams.Card(new TeamsEvent(Guid.Empty, TeamsEventKind.ScheduleChanged, "Testnachricht aus EstrichONE",
            "Wenn diese Karte im Kanal erscheint, ist der Webhook richtig eingerichtet.", "/"), null, "Test");
        var now = clock.GetUtcNow();
        try
        {
            await graph.PostWebhookAsync(url, card, cancellationToken);
            audit.Record("teams.webhook_tested", user.UserId, "teams_settings", "1", new Dictionary<string, string?> { ["ok"] = "true" });
            await teams.Db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(new TeamsTestResponse(true, [new("Webhook", true, "Testkarte gesendet.")], now));
        }
        catch (GraphException exception)
        {
            return TypedResults.Ok(new TeamsTestResponse(false, [new("Webhook", false, exception.Message)], now));
        }
    }

    // ---------- Benutzer ----------

    private static async Task<Ok<List<TeamsUserMapping>>> UsersAsync(TeamsService teams, CancellationToken cancellationToken)
    {
        var db = teams.Db;
        var users = await db.Set<User>().AsNoTracking().OrderByDescending(u => u.IsActive).ThenBy(u => u.DisplayName).ToListAsync(cancellationToken);
        var links = await db.Set<TeamsUserLink>().AsNoTracking().ToDictionaryAsync(l => l.UserId, cancellationToken);
        return TypedResults.Ok(users.Select(u =>
        {
            var link = links.GetValueOrDefault(u.Id);
            return new TeamsUserMapping(u.Id, u.DisplayName, u.Email, u.IsActive, link?.AadUserId, link?.UserPrincipalName, link?.DisplayName, link?.Manual ?? false,
                link?.Error, link?.CheckedAt);
        }).ToList());
    }

    private static async Task<Results<Ok<TeamsMatchResponse>, ProblemHttpResult>> MatchAsync(
        TeamsService teams, IAuditLog audit, ICurrentUser user, CancellationToken cancellationToken)
    {
        var row = await teams.SettingsAsync(cancellationToken);
        if (!row.GraphConfigured)
        {
            return NotConfigured();
        }

        try
        {
            var summary = await teams.MatchUsersAsync(row, cancellationToken);
            if (summary.Error is { } error)
            {
                return Problem(error);
            }

            audit.Record("teams.users_matched", user.UserId, "teams_settings", "1", new Dictionary<string, string?>
            {
                ["matched"] = summary.Matched.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["notFound"] = summary.NotFound.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            await teams.Db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(new TeamsMatchResponse(summary.Matched, summary.NotFound, summary.Manual, summary.Skipped));
        }
        catch (GraphException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<Results<Ok<TeamsUserMapping>, ProblemHttpResult>> MapUserAsync(
        Guid userId, MapTeamsUserRequest request, TeamsService teams, IAuditLog audit, ICurrentUser user, CancellationToken cancellationToken)
    {
        var target = await teams.Db.Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (target is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Benutzer nicht gefunden", extensions: Code("NOT_FOUND"));
        }

        var row = await teams.SettingsAsync(cancellationToken);
        if (!row.GraphConfigured && !string.IsNullOrWhiteSpace(request.Account))
        {
            return NotConfigured();
        }

        try
        {
            var link = await teams.MapUserAsync(row, userId, request.Account, cancellationToken);
            audit.Record("teams.user_mapped", user.UserId, "user", userId.ToString(), new Dictionary<string, string?>
            {
                ["manual"] = link.Manual ? "true" : "false", ["account"] = link.UserPrincipalName,
            });
            await teams.Db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(new TeamsUserMapping(target.Id, target.DisplayName, target.Email, target.IsActive, link.AadUserId, link.UserPrincipalName,
                link.DisplayName, link.Manual, link.Error, link.CheckedAt));
        }
        catch (GraphException exception)
        {
            return Problem(exception);
        }
    }

    // ---------- Dienstkonto ----------

    private static async Task<Results<Ok<TeamsSenderStart>, ProblemHttpResult>> StartSenderAsync(
        TeamsService teams, GraphClient graph, TimeProvider clock, CancellationToken cancellationToken)
    {
        var row = await teams.SettingsForUpdateAsync(cancellationToken);
        if (!row.GraphConfigured)
        {
            return NotConfigured();
        }

        try
        {
            var start = await graph.StartDeviceCodeAsync(row.TenantId!.Trim(), row.ClientId!.Trim(), cancellationToken);
            row.ProtectedDeviceCode = teams.Protect(start.DeviceCode);
            row.DeviceCodeExpiresAt = clock.GetUtcNow().AddSeconds(start.ExpiresInSeconds);
            await teams.Db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(new TeamsSenderStart(start.UserCode, start.VerificationUri, row.DeviceCodeExpiresAt.Value, start.IntervalSeconds, start.Message));
        }
        catch (GraphException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<Results<Ok<TeamsSenderState>, ProblemHttpResult>> PollSenderAsync(
        TeamsService teams, GraphClient graph, TimeProvider clock, IAuditLog audit, ICurrentUser user, CancellationToken cancellationToken)
    {
        var row = await teams.SettingsForUpdateAsync(cancellationToken);
        var deviceCode = teams.Unprotect(row.ProtectedDeviceCode);
        if (!row.GraphConfigured || deviceCode is null || row.DeviceCodeExpiresAt < clock.GetUtcNow())
        {
            return TypedResults.Ok(new TeamsSenderState(row.ProtectedSenderToken is not null, false, row.SenderName, row.SenderConnectedAt));
        }

        try
        {
            var token = await graph.PollDeviceCodeAsync(row.TenantId!.Trim(), row.ClientId!.Trim(), deviceCode, cancellationToken);
            if (token is null)
            {
                return TypedResults.Ok(new TeamsSenderState(false, true, null, null));
            }

            if (token.RefreshToken is null)
            {
                return Problem(new GraphException("NO_REFRESH_TOKEN", "Microsoft hat kein Aktualisierungstoken geliefert (Berechtigung offline_access fehlt)."));
            }

            row.ProtectedSenderToken = teams.Protect(token.RefreshToken);
            row.SenderName = TeamsService.Clip(token.UserName ?? "Dienstkonto", TeamsBauakteLink.MaxName);
            row.SenderUserId = token.UserId;
            row.SenderConnectedAt = clock.GetUtcNow();
            row.ProtectedDeviceCode = null;
            row.DeviceCodeExpiresAt = null;
            audit.Record("teams.sender_connected", user.UserId, "teams_settings", "1", new Dictionary<string, string?> { ["account"] = row.SenderName });
            await teams.Db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(new TeamsSenderState(true, false, row.SenderName, row.SenderConnectedAt));
        }
        catch (GraphException exception)
        {
            row.ProtectedDeviceCode = null;
            row.DeviceCodeExpiresAt = null;
            await teams.Db.SaveChangesAsync(cancellationToken);
            return Problem(exception);
        }
    }

    private static async Task<NoContent> DisconnectSenderAsync(TeamsService teams, IAuditLog audit, ICurrentUser user, CancellationToken cancellationToken)
    {
        var row = await teams.SettingsForUpdateAsync(cancellationToken);
        row.ProtectedSenderToken = null;
        row.SenderName = null;
        row.SenderUserId = null;
        row.SenderConnectedAt = null;
        row.ProtectedDeviceCode = null;
        row.DeviceCodeExpiresAt = null;
        audit.Record("teams.sender_disconnected", user.UserId, "teams_settings", "1");
        await teams.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // ---------- Bauakte ----------

    private static async Task<TeamsLinkResponse?> LinkResponseAsync(TeamsService teams, TeamsBauakteLink? link, CancellationToken cancellationToken)
    {
        if (link is null)
        {
            return null;
        }

        var name = await teams.Db.Set<User>().AsNoTracking().Where(u => u.Id == link.LinkedByUserId).Select(u => u.DisplayName)
            .SingleOrDefaultAsync(cancellationToken);
        return new TeamsLinkResponse(link.Kind, link.Created, link.DisplayName, link.WebUrl, TeamsService.AppUrl(link.WebUrl), link.TeamId, link.ChannelId,
            link.ChatId, link.SyncMembers, link.LastSyncAt, link.LastSyncError, link.SyncedMembers, link.UnmappedMembers, link.LastPostAt, link.LastPostError,
            link.ProtectedWebhookUrl is not null, link.LinkedAt, name);
    }

    private static async Task<Results<Ok<BauakteTeamsResponse>, ProblemHttpResult>> BauakteAsync(
        Guid id, TeamsService teams, IBauakteAccess access, IUserPermissions permissions, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return BauakteNotFound();
        }

        var row = await teams.SettingsAsync(cancellationToken);
        var link = await teams.Db.Set<TeamsBauakteLink>().AsNoTracking().SingleOrDefaultAsync(l => l.BauakteId == id, cancellationToken);
        var canManage = await permissions.HasAsync(Permissions.BauaktenEdit, cancellationToken);
        return TypedResults.Ok(new BauakteTeamsResponse(id, row.ChatMode, await LinkResponseAsync(teams, link, cancellationToken), canManage,
            canManage && row.GraphConfigured, canManage && row.GraphConfigured && !string.IsNullOrWhiteSpace(row.SiteTeamId), row.DefaultLinkKind));
    }

    private static async Task<Results<Created<TeamsLinkResponse>, ProblemHttpResult>> LinkAsync(
        Guid id, LinkBauakteTeamsRequest request, TeamsService teams, GraphClient graph, IBauakteAccess access, IAuditLog audit, ICurrentUser user,
        CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return BauakteNotFound();
        }

        var db = teams.Db;
        if (await db.Set<TeamsBauakteLink>().AnyAsync(l => l.BauakteId == id, cancellationToken))
        {
            return TypedResults.Problem(statusCode: 409, title: "Bereits verknüpft",
                detail: "Diese Bauakte ist bereits mit Teams verknüpft. Bitte zuerst die Verknüpfung lösen.", extensions: Code("TEAMS_ALREADY_LINKED"));
        }

        if (Blank(request.WebhookUrl) is { } webhook && graph.ValidateWebhook(webhook) is { } webhookProblem)
        {
            return Invalid(webhookProblem);
        }

        var row = await teams.SettingsAsync(cancellationToken);
        TeamsBauakteLink link;
        try
        {
            if (Blank(request.Url) is { } url)
            {
                link = await teams.LinkByUrlAsync(id, url, user.UserId, cancellationToken);
                db.Add(link);
            }
            else if (request.Kind is { } kind)
            {
                if (!row.GraphConfigured)
                {
                    return NotConfigured();
                }

                link = await teams.CreateLinkAsync(id, kind, user.UserId, cancellationToken);
            }
            else
            {
                return Invalid("Bitte „Kanal anlegen“, „Gruppenchat anlegen“ wählen oder einen Link aus Teams einfügen.");
            }
        }
        catch (GraphException exception)
        {
            return Problem(exception);
        }

        if (request.SyncMembers is { } sync)
        {
            link.SyncMembers = sync && row.GraphConfigured;
        }

        if (Blank(request.WebhookUrl) is { } hook)
        {
            link.ProtectedWebhookUrl = teams.Protect(hook);
        }

        if (link.SyncMembers && (link.Kind == TeamsLinkKind.Channel || !link.Created))
        {
            await teams.SyncMembersAsync(row, link, cancellationToken);
        }

        audit.Record("teams.bauakte_linked", user.UserId, "bauakte", id.ToString(), new Dictionary<string, string?>
        {
            ["kind"] = link.Kind.ToString(), ["created"] = link.Created ? "true" : "false", ["name"] = link.DisplayName,
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/bauakten/{id}/teams", await LinkResponseAsync(teams, link, cancellationToken));
    }

    private static async Task<Results<Ok<TeamsLinkResponse>, ProblemHttpResult>> SyncAsync(
        Guid id, TeamsService teams, IBauakteAccess access, IAuditLog audit, ICurrentUser user, CancellationToken cancellationToken)
    {
        var link = await teams.Db.Set<TeamsBauakteLink>().SingleOrDefaultAsync(l => l.BauakteId == id, cancellationToken);
        if (link is null || !await access.CanReadAsync(id, cancellationToken))
        {
            return NotLinked();
        }

        await teams.SyncMembersAsync(await teams.SettingsAsync(cancellationToken), link, cancellationToken);
        audit.Record("teams.members_synced", user.UserId, "bauakte", id.ToString(), new Dictionary<string, string?>
        {
            ["ok"] = link.LastSyncError is null ? "true" : "false", ["members"] = link.SyncedMembers.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        await teams.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await LinkResponseAsync(teams, link, cancellationToken))!);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UnlinkAsync(
        Guid id, TeamsService teams, IBauakteAccess access, IAuditLog audit, ICurrentUser user, CancellationToken cancellationToken)
    {
        var link = await teams.Db.Set<TeamsBauakteLink>().SingleOrDefaultAsync(l => l.BauakteId == id, cancellationToken);
        if (link is null || !await access.CanReadAsync(id, cancellationToken))
        {
            return NotLinked();
        }

        teams.Db.Remove(link);
        audit.Record("teams.bauakte_unlinked", user.UserId, "bauakte", id.ToString(), new Dictionary<string, string?> { ["name"] = link.DisplayName });
        await teams.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // ---------- Hilfen ----------

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, object?> Code(string code) => new() { ["code"] = code };

    private static ProblemHttpResult Invalid(string detail) =>
        TypedResults.Problem(statusCode: 422, title: "Ungültige Angaben", detail: detail, extensions: Code("VALIDATION_FAILED"));

    private static ProblemHttpResult NotConfigured() => TypedResults.Problem(statusCode: 409, title: "Teams nicht eingerichtet",
        detail: "Bitte zuerst Tenant-ID, Client-ID und Schlüssel bzw. Zertifikat speichern (Einstellungen › Integrationen › Microsoft Teams).",
        extensions: Code("TEAMS_NOT_CONFIGURED"));

    private static ProblemHttpResult BauakteNotFound() =>
        TypedResults.Problem(statusCode: 404, title: "Bauakte nicht gefunden", extensions: Code("NOT_FOUND"));

    private static ProblemHttpResult NotLinked() =>
        TypedResults.Problem(statusCode: 404, title: "Nicht verknüpft", detail: "Diese Bauakte ist nicht mit Teams verknüpft.", extensions: Code("TEAMS_NOT_LINKED"));

    /// <summary>
    /// Microsoft-Fehler als Problem: keine Verbindung, Zeitüberschreitung oder Drosselung 503, Ablehnung durch Microsoft 424, eigene Prüfungen 409/422.
    /// Bewusst nie 502/504: Vorgeschaltete Proxys (Cloudflare) ersetzen solche Antworten des Servers durch eine eigene Fehlerseite – die
    /// Oberfläche sähe dann nur „Bad Gateway“ statt des Grundes.
    /// </summary>
    internal static ProblemHttpResult Problem(GraphException exception)
    {
        var status = exception.Code switch
        {
            "OFFLINE" or "TIMEOUT" or "THROTTLED" => StatusCodes.Status503ServiceUnavailable,
            "NOT_CONFIGURED" or "SECRET_UNREADABLE" or "NO_SITE_TEAM" or "CONFLICT" or "NO_SENDER" => StatusCodes.Status409Conflict,
            "INVALID_LINK" or "INVALID_TEAM" or "TOO_FEW_MEMBERS" or "CERTIFICATE_INVALID" => StatusCodes.Status422UnprocessableEntity,
            "NOT_FOUND" when exception.Status is null => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status424FailedDependency,
        };
        var title = exception.Code switch
        {
            "OFFLINE" => "Keine Verbindung zu Microsoft 365",
            "TIMEOUT" => "Microsoft 365 antwortet nicht rechtzeitig",
            _ => "Microsoft Teams",
        };
        return TypedResults.Problem(statusCode: status, title: title, detail: exception.Message, extensions: Code("TEAMS_" + exception.Code));
    }

    /// <summary>
    /// Jede Teams-Anfrage aus der Oberfläche: kurze Gesamtfrist für Microsoft-Aufrufe (<see cref="TeamsOptions.InteractiveDeadline"/>) und
    /// jede Ausnahme als lesbares Problem mit Eintrag im Server-Log – nie ein Verbindungsabbruch oder eine leere 500.
    /// </summary>
    private static async ValueTask<object?> GuardAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var services = http.RequestServices;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(TeamsEndpoints).FullName!);
        var step = http.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? http.Request.Path.Value ?? "Teams";
        var started = Stopwatch.GetTimestamp();
        using var interactive = services.GetRequiredService<GraphClient>().BeginInteractive();
        try
        {
            var result = await next(context);
            if (result is IStatusCodeHttpResult { StatusCode: >= 400 } failed && result is ProblemHttpResult problem)
            {
                logger.LogWarning("Teams {Step}: HTTP {Status} {Code} nach {Elapsed} ms – {Detail}", step, failed.StatusCode,
                    problem.ProblemDetails.Extensions.TryGetValue("code", out var code) ? code : null, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    problem.ProblemDetails.Detail);
            }

            return result;
        }
        catch (GraphException exception)
        {
            logger.LogWarning("Teams {Step}: {Code} (HTTP {GraphStatus}) nach {Elapsed} ms – {Message}", step, exception.Code, exception.Status,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, exception.Message);
            return Problem(exception);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            throw; // Browser hat abgebrochen – niemand wartet auf eine Antwort.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Teams {Step}: unerwarteter Fehler nach {Elapsed} ms", step, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return Unexpected(exception, Activity.Current?.Id ?? http.TraceIdentifier);
        }
    }

    /// <summary>Unerwartete Ausnahme (kein Microsoft-Fehler) als verständliches Problem; Einzelheiten nur im Server-Log.</summary>
    internal static ProblemHttpResult Unexpected(Exception exception, string traceId)
    {
        var (status, code, title, detail) = exception switch
        {
            OperationCanceledException => (StatusCodes.Status503ServiceUnavailable, "TEAMS_TIMEOUT", "Microsoft 365 antwortet nicht rechtzeitig",
                "Microsoft 365 hat nicht rechtzeitig geantwortet. Bitte in einigen Minuten erneut versuchen; Einzelheiten stehen im Server-Log."),
            HttpRequestException => (StatusCodes.Status503ServiceUnavailable, "TEAMS_OFFLINE", "Keine Verbindung zu Microsoft 365",
                GraphClient.Offline("graph.microsoft.com").Message),
            CryptographicException => (StatusCodes.Status409Conflict, "TEAMS_SECRET_UNREADABLE", "Microsoft Teams",
                "Der gespeicherte Schlüssel bzw. das Zertifikat ist nicht verwendbar. Bitte Schlüssel bzw. Zertifikat neu eingeben und speichern."),
            UriFormatException => (StatusCodes.Status422UnprocessableEntity, "TEAMS_INVALID_ADDRESS", "Microsoft Teams",
                "Eine Adresse ist ungültig (Tenant-ID, Team-Link oder Webhook-URL). Bitte die Angaben prüfen."),
            DbUpdateException => (StatusCodes.Status500InternalServerError, "TEAMS_SAVE_FAILED", "Microsoft Teams",
                "Das Ergebnis konnte nicht gespeichert werden (gleichzeitige Änderung oder Datenbankfehler). Bitte die Seite neu laden und erneut versuchen."),
            System.Text.Json.JsonException or FormatException or InvalidOperationException => (StatusCodes.Status500InternalServerError,
                "TEAMS_UNEXPECTED_RESPONSE", "Microsoft Teams",
                "Die Antwort von Microsoft 365 konnte nicht verarbeitet werden. Bitte erneut versuchen; Einzelheiten stehen im Server-Log."),
            _ => (StatusCodes.Status500InternalServerError, "TEAMS_INTERNAL_ERROR", "Microsoft Teams",
                "Bei der Teams-Anbindung ist ein unerwarteter Fehler aufgetreten. Bitte erneut versuchen; Einzelheiten stehen im Server-Log."),
        };
        return TypedResults.Problem(statusCode: status, title: title, detail: $"{detail} (Fehlerkennung {traceId})",
            extensions: new Dictionary<string, object?> { ["code"] = code, ["traceId"] = traceId });
    }
}
