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
using EstrichOne.Api.Modules.Notifications.Exports;
using EstrichOne.Api.Modules.Teams.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Teams;

/// <summary>Was alle Angemeldeten über die Teams-Anbindung wissen müssen (Navigation, Bauakte, Startseite).</summary>
/// <param name="ChatMode">Wo Unterhaltungen stattfinden: <c>estrichOne</c>, <c>teams</c> oder <c>both</c>.</param>
/// <param name="ChatReadOnly">Eingebauter Chat nur noch als Archiv (lesen, exportieren).</param>
/// <param name="Available">Teams-Verknüpfungen nutzbar (Modus nicht „nur EstrichONE-Chat“).</param>
/// <param name="RolloutNotice">Hinweis „Ab sofort chatten wir in Teams“ für Web und Apps (nur wenn eingeschaltet).</param>
/// <param name="CanStartChats">„Chat in Teams“ an Personen anbieten (Teams im Einsatz und Recht <c>chat.use</c>).</param>
/// <param name="CanDelete">Recht <c>teams.delete</c>: Kanäle und Gruppenchats von Bauakten archivieren bzw. löschen.</param>
public sealed record TeamsStatusResponse(
    TeamsChatMode ChatMode, bool ChatReadOnly, bool Available, bool GraphConfigured, TeamsPostVia PostVia, bool CanManage, string? SiteTeamName,
    string? SiteTeamUrl, string? SiteTeamAppUrl, string TeamsWebUrl = "https://teams.microsoft.com", string TeamsAppUrl = "msteams:",
    string? RolloutNotice = null, bool CanStartChats = false, bool CanDelete = false,
    string TeamsDownloadUrl = "https://www.microsoft.com/microsoft-teams/download-app");

public sealed record TeamsSettingsResponse(
    TeamsChatMode ChatMode, string? TenantId, string? ClientId, TeamsAuthKind AuthKind, bool HasClientSecret, string? CertificateThumbprint,
    DateTimeOffset? CertificateExpiresAt, string? SiteTeamId, string? SiteTeamName, string? SiteTeamUrl, TeamsLinkKind DefaultLinkKind, bool AutoLink,
    bool SyncMembers, TeamsPostVia PostVia, bool HasWebhook, string? WebhookHost, bool PostDefects, bool PostAcceptance, bool PostSchedule,
    string? SenderName, DateTimeOffset? SenderConnectedAt, DateTimeOffset? LastTestAt, bool? LastTestOk, string? LastTestError,
    DateTimeOffset? LastPostAt, string? LastPostError, bool GraphConfigured, int MappedUsers, int ActiveUsers, int LinkedBauakten,
    IReadOnlyList<TeamsPermissionInfo> Permissions, long Version, TeamsCloseAction CloseAction, bool ChatsViaGraph, bool RolloutNotice,
    string? RolloutNoticeText);

/// <summary>Geheimnisse: <c>null</c> = unverändert; <c>Clear…</c> entfernt sie. <paramref name="SiteTeam"/>: ID oder Teams-Link, leer = entfernen, <c>null</c> = unverändert.</summary>
public sealed record SaveTeamsSettingsRequest(
    TeamsChatMode ChatMode = TeamsChatMode.EstrichOne, [property: MaxLength(100)] string? TenantId = null, [property: MaxLength(100)] string? ClientId = null,
    TeamsAuthKind AuthKind = TeamsAuthKind.Secret, [property: MaxLength(500)] string? ClientSecret = null, bool ClearClientSecret = false,
    [property: MaxLength(20_000)] string? CertificatePem = null, bool ClearCertificate = false, [property: MaxLength(2000)] string? SiteTeam = null,
    TeamsLinkKind DefaultLinkKind = TeamsLinkKind.Channel, bool AutoLink = false, bool SyncMembers = true, TeamsPostVia PostVia = TeamsPostVia.Off,
    [property: MaxLength(2000)] string? WebhookUrl = null, bool ClearWebhook = false, bool PostDefects = true, bool PostAcceptance = true,
    bool PostSchedule = true, long? Version = null, TeamsCloseAction CloseAction = TeamsCloseAction.Archive, bool ChatsViaGraph = false,
    bool RolloutNotice = false, [property: MaxLength(TeamsSettingsRow.MaxNoticeText)] string? RolloutNoticeText = null);

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
    string? LastPostError, bool HasWebhook, DateTimeOffset LinkedAt, string? LinkedByName, DateTimeOffset? ArchivedAt, string? CloseError);

/// <param name="CanManage">Verknüpfen, abgleichen, lösen (bauakten.edit).</param>
/// <param name="CanCreate">Automatisch anlegen möglich (Graph eingerichtet).</param>
/// <param name="CanDelete">Recht <c>teams.delete</c>: archivieren bzw. in Teams löschen (Bestätigung mit <paramref name="Confirmation"/>).</param>
/// <param name="CanPost">„Nachricht in Kanal posten“ möglich (Webhook bzw. Dienstkonto eingerichtet, Recht <c>bauakten.edit</c>).</param>
/// <param name="Confirmation">Zum Löschen einzutippen: Bauaktennummer (ohne Nummer: Titel).</param>
/// <param name="TeamChat">Chat mit dem ganzen Bauakten-Team (ohne den Aufrufer) als Teams-Link; <c>null</c>, wenn niemand ein Microsoft-Konto hat.</param>
/// <param name="CloseAction">Was beim Abschließen der Bauakte mit der Unterhaltung geschieht.</param>
public sealed record BauakteTeamsResponse(
    Guid BauakteId, TeamsChatMode ChatMode, TeamsLinkResponse? Link, bool CanManage, bool CanCreate, bool CanCreateChannel, TeamsLinkKind DefaultLinkKind,
    bool CanDelete, bool CanPost, string Confirmation, TeamsChatLinkResponse? TeamChat, TeamsCloseAction CloseAction);

/// <param name="Confirmation">Bauaktennummer (bzw. Titel ohne Nummer) – Schutz vor versehentlichem Löschen.</param>
public sealed record DeleteBauakteTeamsRequest([property: MaxLength(250)] string? Confirmation = null);

public sealed record PostBauakteTeamsMessageRequest([property: Required, MaxLength(2000)] string Text);

/// <param name="UserIds">Personen für den Chat (der Aufrufer wird nicht mitgezählt): eine = Einzelchat, mehrere = Gruppenchat.</param>
/// <param name="Topic">Thema (nur Gruppenchats).</param>
/// <param name="Message">Vorgeschlagener erster Text.</param>
public sealed record StartTeamsChatRequest(
    [property: Required, MaxLength(100)] IReadOnlyList<Guid> UserIds, [property: MaxLength(250)] string? Topic = null,
    [property: MaxLength(1000)] string? Message = null);

/// <summary>Stand der Umstellung auf Teams (Einstellungen › Integrationen › Microsoft Teams › Umstellung).</summary>
/// <param name="Unchecked">Aktive Benutzer ohne Prüfung (noch nicht abgeglichen – „Chat in Teams“ nutzt dann die E-Mail-Adresse).</param>
public sealed record TeamsRolloutResponse(
    int ActiveUsers, int Mapped, int NotFound, int Unchecked, int NoEmail, int OpenBauakten, int LinkedOpen, int UnlinkedOpen, TeamsLinkJobResponse? Job,
    bool CanLinkAll, string? CannotLinkReason);

/// <param name="Kind">Art der neuen Unterhaltungen; ohne Angabe die Voreinstellung.</param>
public sealed record StartTeamsLinkJobRequest(TeamsLinkKind? Kind = null);

/// <param name="Kind">Automatisch anlegen: Kanal im Team „Baustellen“ oder Gruppenchat. Ohne Angabe: <paramref name="Url"/> verknüpfen.</param>
/// <param name="Url">Vorhandener Kanal oder Chat (Link aus Teams).</param>
/// <param name="WebhookUrl">Optional: Teams-Workflows-Webhook nur für diese Bauakte.</param>
/// <param name="Topic">Name des Gruppenchats (Standard „Bauakte Nummer Titel“).</param>
/// <param name="Participants">Teilnehmer des Gruppenchats: <c>team</c>, <c>crew:{id}</c>, <c>role:{id}</c>, <c>user:{id}</c> (leer = Bauakten-Team);
/// die anlegende Person kommt immer dazu. Der Mitgliederabgleich folgt diesen Teilnehmern.</param>
public sealed record LinkBauakteTeamsRequest(
    TeamsLinkKind? Kind = null, [property: MaxLength(2000)] string? Url = null, [property: MaxLength(2000)] string? WebhookUrl = null, bool? SyncMembers = null,
    [property: MaxLength(250)] string? Topic = null, [property: MaxLength(200)] IReadOnlyList<string>? Participants = null);

/// <summary>Schnellauswahl im Dialog „Teams-Chat anlegen“: Bauakten-Team, Kolonne, Gruppe (Rolle) oder Person.</summary>
/// <param name="Key"><c>team</c>, <c>crew:{id}</c>, <c>role:{id}</c> bzw. <c>user:{id}</c>.</param>
/// <param name="Kind"><c>team</c>, <c>crew</c>, <c>role</c> oder <c>user</c>.</param>
public sealed record TeamsParticipantOption(string Key, string Kind, string Name, IReadOnlyList<Guid> UserIds);

/// <param name="People">Alle aktiven Personen mit Microsoft-Konto (<c>upn</c>) bzw. Grund, warum keines zugeordnet ist.</param>
/// <param name="ViaGraph">Chat wird über Microsoft Graph angelegt und gespeichert; sonst nur Teams-Link (nicht gespeichert).</param>
/// <param name="MinMembers">Mindestzahl Personen mit Microsoft-Konto für einen Gruppenchat (Microsoft: 3).</param>
public sealed record TeamsParticipantsResponse(
    IReadOnlyList<TeamsParticipantOption> Options, IReadOnlyList<TeamsPerson> People, Guid SelfUserId, bool ViaGraph, int MinMembers, string Topic);

/// <summary>Teams-Chat einer Kolonne.</summary>
public sealed record TeamsCrewChatResponse(
    Guid CrewId, string? DisplayName, string WebUrl, string AppUrl, bool Created, int SyncedMembers, int UnmappedMembers, string? LastSyncError,
    DateTimeOffset LinkedAt);

/// <summary>Teams-Chat einer eigenen Kolonne (Apps).</summary>
public sealed record TeamsMyCrewChatResponse(Guid CrewId, string CrewName, string? DisplayName, string WebUrl, string AppUrl);

/// <param name="ViaGraph">Anlegen über Microsoft Graph (gespeichert, Mitglieder folgen der Kolonne); sonst öffnet „Teams-Chat“ einen Teams-Link.</param>
/// <param name="CanCreate">Recht <c>planning.manage</c>.</param>
/// <param name="CanDelete">Recht <c>teams.delete</c> und App-Registrierung.</param>
public sealed record TeamsCrewChatsResponse(bool Available, bool ViaGraph, bool CanCreate, bool CanDelete, IReadOnlyList<TeamsCrewChatResponse> Chats);

internal static partial class TeamsEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/teams/status", StatusAsync).WithTags("Microsoft Teams").WithName("GetTeamsStatus")
            .WithSummary("Chat-Modus und Teams-Anbindung für Navigation, Bauakte und Startseite (alle Angemeldeten)");

        var people = api.MapGroup("/teams").WithTags("Microsoft Teams").RequirePermission(Permissions.ChatUse).AddEndpointFilter(GuardAsync);
        people.MapGet("/people", PeopleAsync).WithName("ListTeamsPeople")
            .WithSummary("Microsoft-Konten (UPN) der Kollegen für „Chat in Teams“ – mit Grund, wenn kein Chat möglich ist");
        people.MapPost("/chats", StartChatAsync).WithName("StartTeamsChat")
            .WithSummary("Einzel- oder Gruppenchat in Teams: Teams-Link (Deep Link, optional über Microsoft Graph angelegt)")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable);

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
        settings.MapGet("/rollout", RolloutAsync).WithName("GetTeamsRollout").WithSummary("Stand der Umstellung: Konten, Bauakten mit und ohne Verknüpfung");
        settings.MapPost("/rollout/link-all", StartLinkJobAsync).WithName("StartTeamsLinkAll")
            .WithSummary("Alle offenen Bauakten ohne Teams-Verknüpfung im Hintergrund verknüpfen (Stand über GET …/rollout/job)")
            .Produces<TeamsLinkJobResponse>(StatusCodes.Status202Accepted).ProducesProblem(StatusCodes.Status409Conflict);
        settings.MapGet("/rollout/job", LinkJobAsync).WithName("GetTeamsLinkJob").WithSummary("Stand der Massenverknüpfung")
            .ProducesProblem(StatusCodes.Status404NotFound);
        settings.MapDelete("/rollout/job", CancelLinkJobAsync).WithName("CancelTeamsLinkJob").WithSummary("Massenverknüpfung abbrechen")
            .ProducesProblem(StatusCodes.Status404NotFound);

        var bauakte = api.MapGroup("/bauakten/{id:guid}/teams").WithTags("Microsoft Teams").AddEndpointFilter(GuardAsync);
        bauakte.MapGet("/", BauakteAsync).WithName("GetBauakteTeams").WithSummary("Verknüpfte Teams-Unterhaltung der Bauakte")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenRead);
        bauakte.MapPost("/", LinkAsync).WithName("LinkBauakteTeams")
            .WithSummary("Kanal oder Gruppenchat anlegen bzw. vorhandenen per Link verknüpfen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status424FailedDependency)
            .RequirePermission(Permissions.BauaktenEdit);
        bauakte.MapGet("/participants", ParticipantsAsync).WithName("ListBauakteTeamsParticipants")
            .WithSummary("Auswahl für „Teams-Chat anlegen“: Bauakten-Team, Kolonnen, Gruppen (Rollen), Personen mit Microsoft-Konto")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenEdit);
        bauakte.MapPost("/sync", SyncAsync).WithName("SyncBauakteTeamsMembers").WithSummary("Mitglieder jetzt abgleichen (Ergebnis an der Verknüpfung)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenEdit);
        bauakte.MapDelete("/", UnlinkAsync).WithName("UnlinkBauakteTeams").WithSummary("Verknüpfung lösen (in Teams wird nichts gelöscht)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenEdit);
        bauakte.MapPost("/archive", ArchiveAsync).WithName("ArchiveBauakteTeams")
            .WithSummary("Kanal in Teams archivieren (nur lesen, wiederherstellbar; Gruppenchats: nicht möglich)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status424FailedDependency)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable).RequirePermission(Permissions.TeamsDelete);
        bauakte.MapPost("/unarchive", UnarchiveAsync).WithName("UnarchiveBauakteTeams").WithSummary("Archivierten Kanal wiederherstellen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status424FailedDependency)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable).RequirePermission(Permissions.TeamsDelete);
        bauakte.MapPost("/delete", DeleteRemoteAsync).WithName("DeleteBauakteTeams")
            .WithSummary("Kanal bzw. Gruppenchat in Teams löschen und Verknüpfung lösen (Bestätigung mit Bauaktennummer)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.TeamsDelete);
        var crews = api.MapGroup("/teams/crews").WithTags("Microsoft Teams").AddEndpointFilter(GuardAsync);
        crews.MapGet("/", CrewChatsAsync).WithName("ListTeamsCrewChats").WithSummary("Teams-Chats der Kolonnen (einer je Kolonne)")
            .RequirePermission(Permissions.PlanningRead);
        api.MapGet("/teams/crews/mine", MyCrewChatsAsync).WithTags("Microsoft Teams").WithName("ListMyTeamsCrewChats").AddEndpointFilter(GuardAsync)
            .WithSummary("Teams-Chats der eigenen Kolonnen (Apps: „In Teams öffnen“); leer ohne Teams")
            .Produces<List<TeamsMyCrewChatResponse>>();
        crews.MapPost("/{crewId:guid}/chat", CreateCrewChatAsync).WithName("CreateTeamsCrewChat")
            .WithSummary("Teams-Chat der Kolonne anlegen (Microsoft Graph; Mitglieder folgen der Kolonne)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.PlanningManage);
        crews.MapPost("/{crewId:guid}/chat/delete", DeleteCrewChatAsync).WithName("DeleteTeamsCrewChat")
            .WithSummary("Teams-Chat der Kolonne in Teams löschen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status424FailedDependency)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable).RequirePermission(Permissions.TeamsDelete);

        bauakte.MapPost("/message", PostMessageAsync).WithName("PostBauakteTeamsMessage")
            .WithSummary("Kurze Nachricht in den verknüpften Kanal bzw. Chat (Webhook oder Dienstkonto)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status424FailedDependency)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable).RequirePermission(Permissions.BauaktenEdit);
    }

    // ---------- Status ----------

    private static async Task<Ok<TeamsStatusResponse>> StatusAsync(TeamsService teams, IUserPermissions permissions, CancellationToken cancellationToken)
    {
        var row = await teams.SettingsAsync(cancellationToken);
        var granted = await permissions.GetAsync(cancellationToken);
        var available = row.ChatMode != TeamsChatMode.EstrichOne;
        var notice = available && row.RolloutNotice
            ? Blank(row.RolloutNoticeText) ?? DefaultNotice
            : null;
        return TypedResults.Ok(new TeamsStatusResponse(row.ChatMode, row.ChatMode == TeamsChatMode.Teams, available,
            row.GraphConfigured, row.PostVia, granted.Contains(Permissions.SettingsIntegrations), row.SiteTeamName,
            row.SiteTeamUrl, row.SiteTeamUrl is null ? null : TeamsService.AppUrl(row.SiteTeamUrl), RolloutNotice: notice,
            CanStartChats: available && granted.Contains(Permissions.ChatUse), CanDelete: available && granted.Contains(Permissions.TeamsDelete)));
    }

    internal const string DefaultNotice =
        "Ab sofort chatten wir in Microsoft Teams. Bitte die Teams-App auf Smartphone und PC installieren und mit dem Firmenkonto anmelden. "
        + "Der bisherige EstrichONE-Chat bleibt als Archiv lesbar.";

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
            row.LastPostAt, row.LastPostError, row.GraphConfigured, mapped, active.Count, linked, TeamsService.Permissions, row.Version, row.CloseAction,
            row.ChatsViaGraph, row.RolloutNotice, row.RolloutNoticeText);
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
        row.CloseAction = request.CloseAction;
        row.ChatsViaGraph = request.ChatsViaGraph;
        row.RolloutNotice = request.RolloutNotice;
        row.RolloutNoticeText = Blank(request.RolloutNoticeText);
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
            ["postVia"] = row.PostVia.ToString(), ["credentialsChanged"] = credentialsChanged ? "true" : "false", ["closeAction"] = row.CloseAction.ToString(),
            ["rolloutNotice"] = row.RolloutNotice ? "true" : "false",
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
            link.ProtectedWebhookUrl is not null, link.LinkedAt, name, link.ArchivedAt, link.CloseError);
    }

    private static async Task<Results<Ok<BauakteTeamsResponse>, ProblemHttpResult>> BauakteAsync(
        Guid id, TeamsService teams, IBauakteAccess access, IUserPermissions permissions, ICurrentUser user, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return BauakteNotFound();
        }

        return TypedResults.Ok(await BauakteResponseAsync(id, teams, permissions, user, cancellationToken));
    }

    private static async Task<BauakteTeamsResponse> BauakteResponseAsync(Guid id, TeamsService teams, IUserPermissions permissions, ICurrentUser user,
        CancellationToken cancellationToken)
    {
        var row = await teams.SettingsAsync(cancellationToken);
        var link = await teams.Db.Set<TeamsBauakteLink>().AsNoTracking().SingleOrDefaultAsync(l => l.BauakteId == id, cancellationToken);
        var granted = await permissions.GetAsync(cancellationToken);
        var canManage = granted.Contains(Permissions.BauaktenEdit);
        var bauakte = await teams.Db.Set<Bauakte>().AsNoTracking().Where(b => b.Id == id).Select(b => new { b.Number, b.Title }).SingleAsync(cancellationToken);
        TeamsChatLinkResponse? teamChat = null;
        if (row.ChatMode != TeamsChatMode.EstrichOne)
        {
            var members = await teams.Bauakten.TeamUserIdsAsync(id, cancellationToken);
            if (members.Any(m => m != user.UserId))
            {
                // Nur Deep Link (kein Graph-Aufruf beim Öffnen der Bauakte).
                var people = await teams.PeopleAsync(row, members.Where(m => m != user.UserId).ToList(), cancellationToken);
                var included = people.Where(p => p.Upn is not null).ToList();
                if (included.Count > 0)
                {
                    var url = TeamsService.ChatDeepLink(included.Select(p => p.Upn!), TeamsService.BauakteTopic(bauakte.Number, bauakte.Title), null);
                    teamChat = new TeamsChatLinkResponse(url, TeamsService.AppUrl(url), false, included.Select(p => p.Name).ToList(),
                        people.Where(p => p.Upn is null).Select(p => new TeamsChatMissing(p.UserId, p.Name, p.Reason ?? "Kein Microsoft-Konto.")).ToList());
                }
            }
        }

        var canPost = canManage && link is not null && row.PostVia switch
        {
            TeamsPostVia.Webhook => row.ProtectedWebhookUrl is not null || link.ProtectedWebhookUrl is not null,
            TeamsPostVia.Graph => row.ProtectedSenderToken is not null,
            _ => false,
        };
        return new BauakteTeamsResponse(id, row.ChatMode, await LinkResponseAsync(teams, link, cancellationToken), canManage,
            canManage && row.GraphConfigured, canManage && row.GraphConfigured && !string.IsNullOrWhiteSpace(row.SiteTeamId), row.DefaultLinkKind,
            granted.Contains(Permissions.TeamsDelete) && row.GraphConfigured, canPost, Confirmation(bauakte.Number, bauakte.Title), teamChat, row.CloseAction);
    }

    private static string Confirmation(string? number, string title) => string.IsNullOrWhiteSpace(number) ? title.Trim() : number.Trim();

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

                link = await teams.CreateLinkAsync(id, kind, user.UserId, cancellationToken, request.Participants, request.Topic);
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

    private static async Task<Results<Ok<TeamsParticipantsResponse>, ProblemHttpResult>> ParticipantsAsync(
        Guid id, TeamsService teams, IBauakteAccess access, IEnumerable<INotificationAudience> audiences, IRoleAssignments roles, ICurrentUser user,
        CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return BauakteNotFound();
        }

        var row = await teams.SettingsAsync(cancellationToken);
        var people = await teams.PeopleAsync(row, null, cancellationToken);
        var active = people.Select(p => p.UserId).ToHashSet();
        var options = new List<TeamsParticipantOption>
        {
            new(TeamsMemberSources.Team, "team", "Bauakten-Team (Zuständige und Mitarbeiter)",
                (await teams.Bauakten.TeamUserIdsAsync(id, cancellationToken)).Where(active.Contains).ToList()),
        };
        foreach (var audience in audiences)
        {
            foreach (var crew in await audience.CrewsAsync(cancellationToken))
            {
                var members = (await audience.CrewMembersAsync([crew.Id], cancellationToken)).Where(active.Contains).ToList();
                options.Add(new(TeamsMemberSources.Crew(crew.Id), "crew", TeamsService.CrewLabel(crew.Name), members));
            }
        }

        foreach (var (roleId, name) in (await roles.GetRoleNamesAsync(cancellationToken)).OrderBy(r => r.Value, StringComparer.CurrentCultureIgnoreCase))
        {
            var members = (await roles.GetUserIdsInRoleAsync(roleId, cancellationToken)).Where(active.Contains).ToList();
            if (members.Count > 0)
            {
                options.Add(new(TeamsMemberSources.Role(roleId), "role", $"Gruppe {name}", members));
            }
        }

        options.AddRange(people.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new TeamsParticipantOption(TeamsMemberSources.User(p.UserId), "user", p.Name, [p.UserId])));
        var bauakte = await teams.Db.Set<Bauakte>().AsNoTracking().Where(b => b.Id == id).Select(b => new { b.Number, b.Title }).SingleAsync(cancellationToken);
        var topic = string.IsNullOrWhiteSpace(bauakte.Number) ? bauakte.Title.Trim() : $"{bauakte.Number.Trim()} · {bauakte.Title.Trim()}";
        return TypedResults.Ok(new TeamsParticipantsResponse(options, people, user.UserId, row.GraphConfigured, TeamsService.MinGroupChatMembers,
            TeamsService.Clip(topic, 250)));
    }

    // ---------- Kolonnen ----------

    private static TeamsCrewChatResponse CrewChatResponse(TeamsCrewChat chat) => new(chat.CrewId, chat.DisplayName, chat.WebUrl,
        TeamsService.AppUrl(chat.WebUrl), chat.Created, chat.SyncedMembers, chat.UnmappedMembers, chat.LastSyncError, chat.LinkedAt);

    private static async Task<Ok<TeamsCrewChatsResponse>> CrewChatsAsync(TeamsService teams, IUserPermissions permissions, CancellationToken cancellationToken)
    {
        var row = await teams.SettingsAsync(cancellationToken);
        var granted = await permissions.GetAsync(cancellationToken);
        var available = row.ChatMode != TeamsChatMode.EstrichOne;
        var chats = available ? await teams.Db.Set<TeamsCrewChat>().AsNoTracking().ToListAsync(cancellationToken) : [];
        return TypedResults.Ok(new TeamsCrewChatsResponse(available, row.GraphConfigured, available && granted.Contains(Permissions.PlanningManage),
            available && row.GraphConfigured && granted.Contains(Permissions.TeamsDelete), chats.Select(CrewChatResponse).ToList()));
    }

    private static async Task<Ok<List<TeamsMyCrewChatResponse>>> MyCrewChatsAsync(TeamsService teams, IEnumerable<INotificationAudience> audiences,
        ICurrentUser user, CancellationToken cancellationToken)
    {
        var row = await teams.SettingsAsync(cancellationToken);
        var result = new List<TeamsMyCrewChatResponse>();
        if (row.ChatMode == TeamsChatMode.EstrichOne)
        {
            return TypedResults.Ok(result);
        }

        var chats = await teams.Db.Set<TeamsCrewChat>().AsNoTracking().ToListAsync(cancellationToken);
        foreach (var audience in audiences)
        {
            var names = (await audience.CrewsAsync(cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
            foreach (var chat in chats.Where(c => names.ContainsKey(c.CrewId)))
            {
                if ((await audience.CrewMembersAsync([chat.CrewId], cancellationToken)).Contains(user.UserId))
                {
                    result.Add(new TeamsMyCrewChatResponse(chat.CrewId, names[chat.CrewId], chat.DisplayName, chat.WebUrl, TeamsService.AppUrl(chat.WebUrl)));
                }
            }
        }

        return TypedResults.Ok(result);
    }

    private static async Task<Results<Created<TeamsCrewChatResponse>, ProblemHttpResult>> CreateCrewChatAsync(
        Guid crewId, TeamsService teams, IEnumerable<INotificationAudience> audiences, IAuditLog audit, ICurrentUser user, CancellationToken cancellationToken)
    {
        var row = await teams.SettingsAsync(cancellationToken);
        if (row.ChatMode == TeamsChatMode.EstrichOne || !row.GraphConfigured)
        {
            return NotConfigured();
        }

        AudienceOption? crew = null;
        foreach (var audience in audiences)
        {
            crew ??= (await audience.CrewsAsync(cancellationToken)).FirstOrDefault(c => c.Id == crewId);
        }

        if (crew is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Kolonne nicht gefunden", detail: "Die Kolonne gibt es nicht oder sie ist inaktiv.",
                extensions: Code("NOT_FOUND"));
        }

        if (await teams.Db.Set<TeamsCrewChat>().AnyAsync(c => c.CrewId == crewId, cancellationToken))
        {
            return TypedResults.Problem(statusCode: 409, title: "Schon vorhanden", detail: "Diese Kolonne hat bereits einen Teams-Chat.",
                extensions: Code("TEAMS_ALREADY_LINKED"));
        }

        var chat = await teams.CreateCrewChatAsync(crewId, crew.Name, user.UserId, cancellationToken);
        audit.Record("teams.crew_chat_created", user.UserId, "crew", crewId.ToString(), new Dictionary<string, string?> { ["name"] = chat.DisplayName });
        await teams.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/teams/crews/{crewId}/chat", CrewChatResponse(chat));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteCrewChatAsync(
        Guid crewId, TeamsService teams, IAuditLog audit, ICurrentUser user, CancellationToken cancellationToken)
    {
        var chat = await teams.Db.Set<TeamsCrewChat>().SingleOrDefaultAsync(c => c.CrewId == crewId, cancellationToken);
        if (chat is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Kein Teams-Chat", detail: "Diese Kolonne hat keinen Teams-Chat.", extensions: Code("TEAMS_NOT_LINKED"));
        }

        var row = await teams.SettingsAsync(cancellationToken);
        if (!row.GraphConfigured)
        {
            return NotConfigured();
        }

        await teams.DeleteCrewChatAsync(row, chat, cancellationToken);
        teams.Db.Remove(chat);
        audit.Record("teams.crew_chat_deleted", user.UserId, "crew", crewId.ToString(), new Dictionary<string, string?> { ["name"] = chat.DisplayName });
        await teams.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // ---------- Archivieren, Löschen, Nachricht ----------

    private static async Task<(TeamsBauakteLink? Link, ProblemHttpResult? Problem)> LinkForAsync(Guid id, TeamsService teams, IBauakteAccess access,
        CancellationToken cancellationToken)
    {
        var link = await teams.Db.Set<TeamsBauakteLink>().SingleOrDefaultAsync(l => l.BauakteId == id, cancellationToken);
        return link is null || !await access.CanReadAsync(id, cancellationToken) ? (null, NotLinked()) : (link, null);
    }

    private static async Task<Results<Ok<TeamsLinkResponse>, ProblemHttpResult>> ArchiveAsync(
        Guid id, TeamsService teams, IBauakteAccess access, IAuditLog audit, ICurrentUser user, CancellationToken cancellationToken)
    {
        var (link, problem) = await LinkForAsync(id, teams, access, cancellationToken);
        if (link is null)
        {
            return problem!;
        }

        var row = await teams.SettingsAsync(cancellationToken);
        if (!row.GraphConfigured)
        {
            return NotConfigured();
        }

        await teams.ArchiveAsync(row, link, cancellationToken);
        audit.Record("teams.bauakte_archived_in_teams", user.UserId, "bauakte", id.ToString(), new Dictionary<string, string?> { ["name"] = link.DisplayName });
        await teams.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await LinkResponseAsync(teams, link, cancellationToken))!);
    }

    private static async Task<Results<Ok<TeamsLinkResponse>, ProblemHttpResult>> UnarchiveAsync(
        Guid id, TeamsService teams, IBauakteAccess access, IAuditLog audit, ICurrentUser user, CancellationToken cancellationToken)
    {
        var (link, problem) = await LinkForAsync(id, teams, access, cancellationToken);
        if (link is null)
        {
            return problem!;
        }

        var row = await teams.SettingsAsync(cancellationToken);
        if (!row.GraphConfigured)
        {
            return NotConfigured();
        }

        await teams.UnarchiveAsync(row, link, cancellationToken);
        audit.Record("teams.bauakte_unarchived_in_teams", user.UserId, "bauakte", id.ToString(), new Dictionary<string, string?> { ["name"] = link.DisplayName });
        await teams.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await LinkResponseAsync(teams, link, cancellationToken))!);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteRemoteAsync(
        Guid id, DeleteBauakteTeamsRequest request, TeamsService teams, IBauakteAccess access, IAuditLog audit, ICurrentUser user,
        CancellationToken cancellationToken)
    {
        var (link, problem) = await LinkForAsync(id, teams, access, cancellationToken);
        if (link is null)
        {
            return problem!;
        }

        var bauakte = await teams.Db.Set<Bauakte>().AsNoTracking().Where(b => b.Id == id).Select(b => new { b.Number, b.Title }).SingleAsync(cancellationToken);
        var expected = Confirmation(bauakte.Number, bauakte.Title);
        if (!string.Equals(Blank(request.Confirmation), expected, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(statusCode: 422, title: "Bestätigung falsch",
                detail: $"Zum Löschen bitte die Bauaktennummer „{expected}“ genau eintippen.", extensions: Code("TEAMS_CONFIRMATION_MISMATCH"));
        }

        var row = await teams.SettingsAsync(cancellationToken);
        if (!row.GraphConfigured)
        {
            return NotConfigured();
        }

        await teams.DeleteRemoteAsync(row, link, cancellationToken);
        teams.Db.Remove(link);
        audit.Record("teams.bauakte_deleted_in_teams", user.UserId, "bauakte", id.ToString(), new Dictionary<string, string?>
        {
            ["kind"] = link.Kind.ToString(), ["name"] = link.DisplayName, ["created"] = link.Created ? "true" : "false", ["trigger"] = "manual",
        });
        await teams.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<TeamsLinkResponse>, ProblemHttpResult>> PostMessageAsync(
        Guid id, PostBauakteTeamsMessageRequest request, TeamsService teams, IBauakteAccess access, IAuditLog audit, ICurrentUser user,
        CancellationToken cancellationToken)
    {
        var (link, problem) = await LinkForAsync(id, teams, access, cancellationToken);
        if (link is null)
        {
            return problem!;
        }

        var text = Blank(request.Text);
        if (text is null)
        {
            return Invalid("Bitte einen Text eingeben.");
        }

        var bauakte = await teams.Db.Set<Bauakte>().AsNoTracking().SingleAsync(b => b.Id == id, cancellationToken);
        var author = await teams.Db.Set<User>().AsNoTracking().Where(u => u.Id == user.UserId).Select(u => u.DisplayName).SingleOrDefaultAsync(cancellationToken)
            ?? "EstrichONE";
        await teams.PostMessageAsync(await teams.SettingsAsync(cancellationToken), link, bauakte, author, text, cancellationToken);
        audit.Record("teams.message_posted", user.UserId, "bauakte", id.ToString(), new Dictionary<string, string?> { ["length"] = text.Length.ToString(
            System.Globalization.CultureInfo.InvariantCulture) });
        await teams.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await LinkResponseAsync(teams, link, cancellationToken))!);
    }

    // ---------- Einzel- und Gruppenchats ----------

    private static async Task<Ok<List<TeamsPerson>>> PeopleAsync(TeamsService teams, CancellationToken cancellationToken)
    {
        var row = await teams.SettingsAsync(cancellationToken);
        return TypedResults.Ok(row.ChatMode == TeamsChatMode.EstrichOne ? [] : await teams.PeopleAsync(row, null, cancellationToken));
    }

    private static async Task<Results<Ok<TeamsChatLinkResponse>, ProblemHttpResult>> StartChatAsync(
        StartTeamsChatRequest request, TeamsService teams, ICurrentUser user, CancellationToken cancellationToken)
    {
        var row = await teams.SettingsAsync(cancellationToken);
        if (row.ChatMode == TeamsChatMode.EstrichOne)
        {
            return TypedResults.Problem(statusCode: 409, title: "Teams nicht im Einsatz",
                detail: "Microsoft Teams ist nicht eingeschaltet (Einstellungen › Integrationen › Microsoft Teams › Chat-Modus).", extensions: Code("TEAMS_NOT_ACTIVE"));
        }

        if (request.UserIds is not { Count: > 0 } || request.UserIds.All(u => u == user.UserId))
        {
            return Invalid("Bitte mindestens eine andere Person auswählen.");
        }

        return TypedResults.Ok(await teams.ChatLinkAsync(row, user.UserId, request.UserIds, request.Topic, request.Message, cancellationToken));
    }

    // ---------- Umstellung ----------

    private static async Task<Ok<TeamsRolloutResponse>> RolloutAsync(TeamsService teams, TeamsRolloutJobs jobs, CancellationToken cancellationToken)
    {
        var db = teams.Db;
        var row = await teams.SettingsAsync(cancellationToken);
        var people = await teams.PeopleAsync(row, null, cancellationToken);
        var links = await db.Set<TeamsUserLink>().AsNoTracking().ToDictionaryAsync(l => l.UserId, cancellationToken);
        var mapped = people.Count(p => links.GetValueOrDefault(p.UserId)?.AadUserId is not null);
        var notFound = people.Count(p => links.GetValueOrDefault(p.UserId) is { AadUserId: null, CheckedAt: var at } && at != default);
        var noEmail = people.Count(p => p.Upn is null && !links.ContainsKey(p.UserId));
        var linkedIds = db.Set<TeamsBauakteLink>().Select(l => l.BauakteId);
        var open = db.Set<Bauakte>().AsNoTracking().Where(b => b.Status != BauakteStatus.Completed && b.Status != BauakteStatus.Cancelled);
        var openCount = await open.CountAsync(cancellationToken);
        var linkedOpen = await open.CountAsync(b => linkedIds.Contains(b.Id), cancellationToken);
        var reason = !row.GraphConfigured ? "Dafür ist die App-Registrierung nötig (Reiter „Verbindung“)."
            : row.DefaultLinkKind == TeamsLinkKind.Channel && string.IsNullOrWhiteSpace(row.SiteTeamId) ? "Bitte zuerst das Team „Baustellen“ festlegen."
            : null;
        return TypedResults.Ok(new TeamsRolloutResponse(people.Count, mapped, notFound, people.Count - mapped - notFound - noEmail, noEmail, openCount, linkedOpen,
            openCount - linkedOpen, jobs.Current, reason is null, reason));
    }

    private static async Task<Results<Accepted<TeamsLinkJobResponse>, ProblemHttpResult>> StartLinkJobAsync(
        StartTeamsLinkJobRequest request, TeamsService teams, TeamsRolloutJobs jobs, IAuditLog audit, ICurrentUser user, CancellationToken cancellationToken)
    {
        var row = await teams.SettingsAsync(cancellationToken);
        if (!row.GraphConfigured)
        {
            return NotConfigured();
        }

        var kind = request.Kind ?? row.DefaultLinkKind;
        if (kind == TeamsLinkKind.Channel && string.IsNullOrWhiteSpace(row.SiteTeamId))
        {
            return Problem(new GraphException("NO_SITE_TEAM", "Bitte zuerst unter „Verbindung“ das Team „Baustellen“ festlegen."));
        }

        var name = await teams.Db.Set<User>().AsNoTracking().Where(u => u.Id == user.UserId).Select(u => u.DisplayName).SingleOrDefaultAsync(cancellationToken);
        var (job, started) = jobs.Start(kind, user.UserId, name);
        if (!started)
        {
            return TypedResults.Problem(statusCode: 409, title: "Läuft bereits",
                detail: $"Die Verknüpfung läuft bereits ({job.Processed} von {job.Total} Bauakten).", extensions: Code("TEAMS_JOB_RUNNING"));
        }

        audit.Record("teams.link_all_started", user.UserId, "teams_settings", "1", new Dictionary<string, string?> { ["kind"] = kind.ToString() });
        await teams.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.Accepted("/api/v1/settings/integrations/teams/rollout/job", job);
    }

    private static Results<Ok<TeamsLinkJobResponse>, ProblemHttpResult> LinkJobAsync(TeamsRolloutJobs jobs) =>
        jobs.Current is { } job ? TypedResults.Ok(job) : NoJob();

    private static Results<NoContent, ProblemHttpResult> CancelLinkJobAsync(TeamsRolloutJobs jobs) =>
        jobs.CancelCurrent() ? TypedResults.NoContent() : NoJob();

    private static ProblemHttpResult NoJob() =>
        TypedResults.Problem(statusCode: 404, title: "Kein Auftrag", detail: "Es läuft keine Massenverknüpfung.", extensions: Code("TEAMS_NO_JOB"));

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
            "NOT_CONFIGURED" or "SECRET_UNREADABLE" or "NO_SITE_TEAM" or "CONFLICT" or "NO_SENDER" or "NOT_SUPPORTED" or "NO_POST"
                => StatusCodes.Status409Conflict,
            "INVALID_LINK" or "INVALID_TEAM" or "TOO_FEW_MEMBERS" or "CERTIFICATE_INVALID" or "NO_PARTICIPANTS" => StatusCodes.Status422UnprocessableEntity,
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
