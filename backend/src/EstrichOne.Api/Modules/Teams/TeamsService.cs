using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Notifications.Exports;
using EstrichOne.Api.Modules.Teams.Exports;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Teams;

/// <summary>Benötigte Microsoft-Graph-Berechtigungen mit Begründung (Oberfläche und Handbuch).</summary>
public sealed record TeamsPermissionInfo(string Name, string Type, bool Required, string Reason);

/// <param name="Warning">Bestanden, aber mit Hinweis (z. B. optionale Berechtigung fehlt).</param>
public sealed record TeamsTestStep(string Name, bool Ok, string Detail, bool Warning = false);

/// <summary>Teams-Link (Kanal, Chat oder Team) zerlegt.</summary>
public sealed record TeamsLinkTarget(TeamsLinkKind? Kind, string? TeamId, string? ChannelId, string? ChatId, string? TenantId, string? Name, string WebUrl);

/// <summary>Kern der Teams-Anbindung: Geheimnisse, Zuordnung der Konten, Verknüpfung je Bauakte, Mitgliederabgleich, Ereigniskarten.</summary>
internal sealed partial class TeamsService(
    EstrichOneDbContext db, GraphClient graph, IDataProtectionProvider protection, TimeProvider clock, IOptions<EstrichOneOptions> app,
    IBauakteReportSource bauakten, Audit.Exports.IAuditLog audit, ILogger<TeamsService> logger, IEnumerable<INotificationAudience> audiences,
    IRoleAssignments roles)
{
    /// <summary>Gruppenchat mit Microsoft Graph: Microsoft legt Gruppenchats erst ab drei Personen an (zwei = Einzelchat).</summary>
    public const int MinGroupChatMembers = 3;

    public const string ProtectorPurpose = "EstrichOne.Teams.Secrets.v1";

    public static IReadOnlyList<TeamsPermissionInfo> Permissions { get; } =
    [
        new("User.Read.All", "Anwendung", true, "Benutzer über E-Mail-Adresse bzw. UPN einem Microsoft-Konto zuordnen (liest nur Name, E-Mail, UPN, ID)."),
        new("Team.ReadBasic.All", "Anwendung", true, "Team „Baustellen“ prüfen und dessen Namen und Link anzeigen."),
        new("Channel.Create", "Anwendung", false, "Je Bauakte einen Kanal im Team „Baustellen“ anlegen (nur bei Art „Kanal“)."),
        new("TeamMember.ReadWrite.All", "Anwendung", false,
            "Bauakten-Team ins Team „Baustellen“ aufnehmen, damit es den Kanal sieht (nur bei Art „Kanal“ mit Mitgliederabgleich; entfernt niemanden)."),
        new("Chat.Create", "Anwendung", false,
            "„Teams-Chat anlegen“ an Bauakte und Kolonne (Gruppenchat mit den gewählten Personen) und – falls eingeschaltet – Einzelchats mit stabilem Link."),
        new("ChatMember.ReadWrite.All", "Anwendung", false,
            "Mitglieder der Gruppenchats bei Änderungen an Bauakten-Team bzw. Kolonne abgleichen (fügt hinzu, entfernt nur von EstrichONE Hinzugefügte)."),
        new("ChannelSettings.ReadWrite.All", "Anwendung", false,
            "Kanal einer Bauakte archivieren bzw. wiederherstellen (von Hand oder beim Abschließen der Bauakte, Einstellung „Beim Abschließen“)."),
        new("Channel.Delete.All", "Anwendung", false, "Kanal einer Bauakte in Teams löschen (Recht „teams.delete“, Bestätigung mit Bauaktennummer)."),
        new("Chat.ManageDeletion.All", "Anwendung", false,
            "Teams-Chat einer Bauakte bzw. Kolonne löschen (alternativ Chat.ReadWrite.All). Ohne diese Berechtigung bleibt nur „Verknüpfung lösen“."),
        new("ChannelMessage.Send", "Delegiert", false, "Ereigniskarten über das Dienstkonto in verknüpfte Kanäle senden (nur bei „Senden über Microsoft Graph“)."),
        new("ChatMessage.Send", "Delegiert", false, "Ereigniskarten über das Dienstkonto in verknüpfte Gruppenchats senden (nur bei „Senden über Microsoft Graph“)."),
        new("offline_access", "Delegiert", false, "Dienstkonto angemeldet halten (Aktualisierungstoken, verschlüsselt gespeichert)."),
    ];

    public EstrichOneDbContext Db => db;

    internal GraphClient Graph => graph;

    internal TimeProvider Clock => clock;

    internal ILogger Logger => logger;

    internal IBauakteReportSource Bauakten => bauakten;

    internal Audit.Exports.IAuditLog Audit => audit;

    internal Uri? PublicUrl => app.Value.PublicUrl;

    private IDataProtector Protector => protection.CreateProtector(ProtectorPurpose);

    public string Protect(string value) => Protector.Protect(value);

    public string? Unprotect(string? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return Protector.Unprotect(value);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public async Task<TeamsSettingsRow> SettingsAsync(CancellationToken cancellationToken) =>
        await db.Set<TeamsSettingsRow>().SingleOrDefaultAsync(cancellationToken) ?? new TeamsSettingsRow();

    public async Task<TeamsSettingsRow> SettingsForUpdateAsync(CancellationToken cancellationToken)
    {
        var row = await db.Set<TeamsSettingsRow>().SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            row = new TeamsSettingsRow();
            db.Add(row);
        }

        return row;
    }

    public GraphCredentials Credentials(TeamsSettingsRow row)
    {
        if (!row.GraphConfigured)
        {
            throw new GraphException("NOT_CONFIGURED", "Microsoft Teams ist nicht eingerichtet (Tenant-ID, Client-ID und Schlüssel bzw. Zertifikat fehlen).");
        }

        var secret = row.AuthKind == TeamsAuthKind.Secret ? Unprotect(row.ProtectedClientSecret) : null;
        var certificate = row.AuthKind == TeamsAuthKind.Certificate ? Unprotect(row.ProtectedCertificate) : null;
        if (secret is null && certificate is null)
        {
            throw new GraphException("SECRET_UNREADABLE",
                "Der gespeicherte Schlüssel ist nicht lesbar (z. B. nach Wiederherstellung ohne Schlüsselring). Bitte Schlüssel bzw. Zertifikat neu eingeben.");
        }

        return new GraphCredentials(row.TenantId!.Trim(), row.ClientId!.Trim(), secret, certificate);
    }

    public async Task<string> TokenAsync(TeamsSettingsRow row, CancellationToken cancellationToken) =>
        await graph.AppTokenAsync(Credentials(row), cancellationToken);

    // ---------- Verbindungstest ----------

    public async Task<List<TeamsTestStep>> TestAsync(TeamsSettingsRow row, CancellationToken cancellationToken)
    {
        var steps = new List<TeamsTestStep>();
        string token;
        try
        {
            token = await TokenAsync(row, cancellationToken);
            steps.Add(new("Anmeldung bei Microsoft Entra ID", true, $"App {row.ClientId} im Mandanten {row.TenantId} angemeldet."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            steps.Add(new("Anmeldung bei Microsoft Entra ID", false, StepFailure(exception, "Anmeldung bei Microsoft Entra ID")));
            return steps;
        }

        PermissionStep(steps, token);
        await StepAsync(steps, "Benutzer lesen (User.Read.All)", async () =>
        {
            await graph.GetAsync(token, "users?$top=1&$select=id", "User.Read.All", cancellationToken);
            return "Benutzerverzeichnis lesbar.";
        }, cancellationToken);
        if (!string.IsNullOrWhiteSpace(row.SiteTeamId))
        {
            await StepAsync(steps, "Team „Baustellen“ (Team.ReadBasic.All)", async () =>
            {
                var team = await graph.GetAsync(token, $"teams/{Uri.EscapeDataString(row.SiteTeamId)}?$select=id,displayName,webUrl", "Team.ReadBasic.All",
                    cancellationToken);
                return $"Team „{GraphClient.Str(team?["displayName"]) ?? row.SiteTeamName}“ gefunden.";
            }, cancellationToken);
        }

        if (row.ProtectedSenderToken is not null)
        {
            await StepAsync(steps, "Dienstkonto für Nachrichten", async () =>
            {
                await SenderTokenAsync(row, cancellationToken);
                return $"Angemeldet als {row.SenderName ?? "Dienstkonto"}.";
            }, cancellationToken);
        }

        return steps;
    }

    private async Task StepAsync(List<TeamsTestStep> steps, string name, Func<Task<string>> action, CancellationToken cancellationToken)
    {
        try
        {
            steps.Add(new(name, true, await action()));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            steps.Add(new(name, false, StepFailure(exception, name)));
        }
    }

    /// <summary>Fehler eines Prüfschritts lesbar; Unerwartetes (kein Microsoft-Fehler) mit Ausnahme ins Log.</summary>
    private string StepFailure(Exception exception, string step)
    {
        if (exception is GraphException graphException)
        {
            logger.LogWarning("Teams-Verbindungstest, Schritt „{Step}“: {Code} – {Message}", step, graphException.Code, graphException.Message);
            return graphException.Message;
        }

        logger.LogError(exception, "Teams-Verbindungstest, Schritt „{Step}“: unerwarteter Fehler", step);
        return $"Unerwarteter Fehler ({exception.GetType().Name}). Bitte erneut versuchen; Einzelheiten stehen im Server-Log (docker compose logs api).";
    }

    /// <summary>Team „Baustellen“ über ID oder „Link zum Team abrufen“.</summary>
    public async Task<(string Id, string Name, string? WebUrl)> ResolveTeamAsync(TeamsSettingsRow row, string input, CancellationToken cancellationToken)
    {
        var value = input.Trim();
        var id = Guid.TryParse(value, out var guid) ? guid.ToString() : ParseLink(value)?.TeamId;
        if (id is null)
        {
            throw new GraphException("INVALID_TEAM", "Bitte die Gruppen-ID des Teams oder den Link aus Teams („Link zum Team abrufen“) einfügen.");
        }

        var token = await TokenAsync(row, cancellationToken);
        var team = await graph.GetAsync(token, $"teams/{Uri.EscapeDataString(id)}?$select=id,displayName,webUrl", "Team.ReadBasic.All", cancellationToken);
        return (id, GraphClient.Str(team?["displayName"]) ?? "Team", GraphClient.Str(team?["webUrl"]));
    }

    // ---------- Benutzerzuordnung ----------

    /// <param name="Error">Grundsätzlicher Fehler (keine Verbindung, Anmeldung, Berechtigung, Frist) – Zuordnung abgebrochen.</param>
    public sealed record MatchSummary(int Matched, int NotFound, int Manual, int Skipped, GraphException? Error);

    /// <summary>Fehler, bei denen weitere Benutzer nicht zu prüfen sind (betrifft alle gleich).</summary>
    private static bool Fatal(GraphException exception) =>
        exception.Code is "OFFLINE" or "AUTH_FAILED" or "FORBIDDEN" or "TIMEOUT" or "THROTTLED" or "UNEXPECTED_RESPONSE";

    /// <summary>Alle aktiven Benutzer über E-Mail/UPN zuordnen – gebündelt (Graph-$batch, 20 je Aufruf) statt eines Aufrufs je Benutzer.</summary>
    public async Task<MatchSummary> MatchUsersAsync(TeamsSettingsRow row, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(row, cancellationToken);
        var users = await db.Set<User>().AsNoTracking().Where(u => u.IsActive).ToListAsync(cancellationToken);
        var links = await db.Set<TeamsUserLink>().ToDictionaryAsync(l => l.UserId, cancellationToken);
        int matched = 0, notFound = 0, manual = 0, skipped = 0;
        var candidates = new List<(TeamsUserLink Link, string Address)>();
        foreach (var user in users)
        {
            if (links.TryGetValue(user.Id, out var existing) && existing.Manual)
            {
                manual++;
                continue;
            }

            var address = user.Email?.Trim();
            if (string.IsNullOrEmpty(address))
            {
                skipped++;
                continue;
            }

            if (existing is null)
            {
                existing = new TeamsUserLink { UserId = user.Id };
                db.Add(existing);
                links[user.Id] = existing;
            }

            candidates.Add((existing, address));
        }

        GraphException? error = null;
        if (candidates.Count > 0)
        {
            try
            {
                var results = await graph.BatchGetAsync(token, candidates.Select(c => UserLookupPath(c.Address)).ToList(), "User.Read.All", cancellationToken);
                error = results.Select(r => r.Error).FirstOrDefault(e => e is not null && Fatal(e));
                for (var i = 0; i < candidates.Count && error is null; i++)
                {
                    var (link, address) = candidates[i];
                    if (results[i].Error is { } failure)
                    {
                        link.Error = failure.Message;
                        link.CheckedAt = clock.GetUtcNow();
                        notFound++;
                        continue;
                    }

                    var account = SingleAccount(results[i].Body);
                    Apply(link, account, manual: false);
                    link.Error = account is null ? $"Kein Microsoft-Konto mit „{address}“ gefunden." : null;
                    if (account is null)
                    {
                        notFound++;
                    }
                    else
                    {
                        matched++;
                    }
                }
            }
            catch (GraphException exception)
            {
                error = exception;
            }
        }

        if (error is not null)
        {
            logger.LogWarning("Teams-Kontenzuordnung abgebrochen: {Code} – {Message}", error.Code, error.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new MatchSummary(matched, notFound, manual, skipped, error);
    }

    /// <summary>Zuordnung von Hand (UPN, E-Mail oder Objekt-ID); <paramref name="account"/> leer = zurück auf automatisch.</summary>
    public async Task<TeamsUserLink> MapUserAsync(TeamsSettingsRow row, Guid userId, string? account, CancellationToken cancellationToken)
    {
        var link = await db.Set<TeamsUserLink>().SingleOrDefaultAsync(l => l.UserId == userId, cancellationToken);
        if (link is null)
        {
            link = new TeamsUserLink { UserId = userId };
            db.Add(link);
        }

        if (string.IsNullOrWhiteSpace(account))
        {
            link.Manual = false;
            link.AadUserId = null;
            link.UserPrincipalName = null;
            link.DisplayName = null;
            link.Error = null;
            link.CheckedAt = clock.GetUtcNow();
            var user = await db.Set<User>().AsNoTracking().SingleAsync(u => u.Id == userId, cancellationToken);
            if (row.GraphConfigured && !string.IsNullOrWhiteSpace(user.Email))
            {
                var found = await FindAccountAsync(await TokenAsync(row, cancellationToken), user.Email.Trim(), cancellationToken);
                Apply(link, found, manual: false);
                link.Error = found is null ? $"Kein Microsoft-Konto mit „{user.Email}“ gefunden." : null;
            }

            return link;
        }

        var token = await TokenAsync(row, cancellationToken);
        var value = account.Trim();
        var result = Guid.TryParse(value, out _)
            ? Account(await graph.GetAsync(token, $"users/{value}?$select=id,displayName,userPrincipalName,mail", "User.Read.All", cancellationToken))
            : await FindAccountAsync(token, value, cancellationToken);
        if (result is null)
        {
            throw new GraphException("NOT_FOUND", $"Kein Microsoft-Konto „{value}“ gefunden.");
        }

        Apply(link, result, manual: true);
        link.Error = null;
        return link;
    }

    private void Apply(TeamsUserLink link, (string Id, string? Upn, string? Name)? account, bool manual)
    {
        link.AadUserId = account?.Id;
        link.UserPrincipalName = account?.Upn;
        link.DisplayName = account?.Name;
        link.Manual = manual;
        link.CheckedAt = clock.GetUtcNow();
    }

    private async Task<(string Id, string? Upn, string? Name)?> FindAccountAsync(string token, string address, CancellationToken cancellationToken) =>
        SingleAccount(await graph.GetAsync(token, UserLookupPath(address), "User.Read.All", cancellationToken));

    /// <summary>Suche nach E-Mail oder UPN (höchstens zwei Treffer – zwei heißt: nicht eindeutig).</summary>
    internal static string UserLookupPath(string address)
    {
        var quoted = address.Replace("'", "''", StringComparison.Ordinal);
        var filter = Uri.EscapeDataString($"mail eq '{quoted}' or userPrincipalName eq '{quoted}'");
        return $"users?$filter={filter}&$select=id,displayName,userPrincipalName,mail&$top=2";
    }

    private static (string Id, string? Upn, string? Name)? SingleAccount(JsonNode? result) =>
        result?["value"] is JsonArray { Count: 1 } values ? Account(values[0]) : null;

    private static (string Id, string? Upn, string? Name)? Account(JsonNode? node) =>
        GraphClient.Str(node?["id"]) is { } id
            ? (id, GraphClient.Str(node?["userPrincipalName"]), GraphClient.Str(node?["displayName"]))
            : null;

    // ---------- Verknüpfung je Bauakte ----------

    /// <param name="sources">Teilnehmer (<see cref="TeamsMemberSources"/>); <c>null</c> = Bauakten-Team (Massenverknüpfung). Angegeben (aus dem Dialog):
    /// leer = Bauakten-Team, die anlegende Person kommt immer dazu.</param>
    public async Task<TeamsBauakteLink> CreateLinkAsync(Guid bauakteId, TeamsLinkKind kind, Guid actor, CancellationToken cancellationToken,
        IReadOnlyList<string>? sources = null, string? topic = null)
    {
        var row = await SettingsAsync(cancellationToken);
        var bauakte = await db.Set<Bauakte>().AsNoTracking().SingleAsync(b => b.Id == bauakteId, cancellationToken);
        var name = ChannelName(bauakte.Number, bauakte.Title);
        var link = new TeamsBauakteLink
        {
            BauakteId = bauakteId, Kind = kind, Created = true, TenantId = row.TenantId, DisplayName = name, LinkedByUserId = actor,
            LinkedAt = clock.GetUtcNow(), SyncMembers = row.SyncMembers,
        };
        if (kind == TeamsLinkKind.Channel)
        {
            if (string.IsNullOrWhiteSpace(row.SiteTeamId))
            {
                throw new GraphException("NO_SITE_TEAM", "Bitte zuerst unter Einstellungen › Integrationen › Microsoft Teams das Team „Baustellen“ festlegen.");
            }

            var token = await TokenAsync(row, cancellationToken);
            JsonNode? channel;
            try
            {
                channel = await graph.CreateAsync(token, $"teams/{Uri.EscapeDataString(row.SiteTeamId)}/channels", new JsonObject
                {
                    ["displayName"] = name, ["description"] = Clip($"EstrichONE-Bauakte {bauakte.Number} {bauakte.Title}".Trim(), 1000),
                    ["membershipType"] = "standard",
                }, "Channel.Create", "Kanal", cancellationToken);
            }
            catch (GraphException exception) when (exception.Code == "CONFLICT"
                || (exception.Code == "GRAPH_REJECTED" && exception.Message.Contains("exist", StringComparison.OrdinalIgnoreCase)))
            {
                throw new GraphException("CONFLICT",
                    $"Im Team „{row.SiteTeamName}“ gibt es bereits einen Kanal „{name}“ (auch gelöschte Kanäle blockieren ihren Namen 30 Tage). "
                    + "Bitte den vorhandenen Kanal über „Per Link verknüpfen …“ verknüpfen oder in Teams endgültig löschen.");
            }

            link.TeamId = row.SiteTeamId;
            link.ChannelId = GraphClient.Str(channel?["id"]) ?? throw MissingId("Kanal");
            link.WebUrl = GraphClient.Str(channel?["webUrl"]) ?? ChannelUrl(link.ChannelId, name, row.SiteTeamId, row.TenantId);
        }
        else
        {
            link.MemberSources = sources is null ? [] : WithActor(sources, actor); // Massenverknüpfung: nur Bauakten-Team
            link.DisplayName = Clip(Blank(topic) ?? BauakteTopic(bauakte.Number, bauakte.Title), TeamsBauakteLink.MaxName);
            var desired = await DesiredMembersAsync(row, link, cancellationToken);
            var chat = await CreateGroupChatAsync(row, link.DisplayName, desired, cancellationToken);
            link.ChatId = chat.ChatId;
            link.WebUrl = chat.WebUrl;
            link.ManagedMemberIds = [.. desired.Ids];
            link.SyncedMembers = desired.Ids.Count;
            link.UnmappedMembers = desired.Unmapped;
            link.LastSyncAt = clock.GetUtcNow();
            link.DesiredHash = desired.Hash;
        }

        db.Add(link);
        return link;
    }

    /// <summary>Teilnehmer ohne Doppelte, Bauakten-Team als Standard, anlegende Person immer dabei.</summary>
    internal static List<string> WithActor(IReadOnlyList<string>? sources, Guid actor)
    {
        var list = (sources ?? []).Select(s => s.Trim()).Where(TeamsMemberSources.IsValid).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0)
        {
            list.Add(TeamsMemberSources.Team);
        }

        var self = TeamsMemberSources.User(actor);
        if (actor != Guid.Empty && !list.Contains(self, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(self);
        }

        return list;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Gruppenchat über Microsoft Graph (<c>POST /chats</c>, alle als Besitzer). Weniger als drei Konten: verständlicher Fehler statt
    /// Ablehnung durch Microsoft. Ist ein Dienstkonto verbunden, schreibt es eine erste Nachricht – sonst erscheint ein neuer Chat in Teams
    /// erst nach der ersten Nachricht in der Chatliste (über den Link ist er sofort erreichbar).
    /// </summary>
    public async Task<(string ChatId, string WebUrl)> CreateGroupChatAsync(TeamsSettingsRow row, string topic, DesiredMembers desired,
        CancellationToken cancellationToken)
    {
        if (desired.Ids.Count < MinGroupChatMembers)
        {
            throw new GraphException("TOO_FEW_MEMBERS",
                $"Ein Gruppenchat in Teams braucht mindestens {MinGroupChatMembers} Personen mit Microsoft-Konto – gefunden: {desired.Ids.Count}"
                + (desired.Unmapped > 0 ? $" ({desired.Unmapped} weitere ohne Microsoft-Konto)" : string.Empty)
                + ". Bitte weitere Personen auswählen bzw. Konten zuordnen (Einstellungen › Integrationen › Microsoft Teams › Benutzer); "
                + "für zwei Personen genügt ein Einzelchat.");
        }

        var token = await TokenAsync(row, cancellationToken);
        var members = new JsonArray();
        foreach (var id in desired.Ids)
        {
            members.Add(Member(id, owner: true, history: false));
        }

        var chat = await graph.CreateAsync(token, "chats", new JsonObject { ["chatType"] = "group", ["topic"] = Clip(topic, 250), ["members"] = members },
            "Chat.Create", "Gruppenchat", cancellationToken);
        var chatId = GraphClient.Str(chat?["id"]) ?? throw MissingId("Gruppenchat");
        var webUrl = GraphClient.Str(chat?["webUrl"]) ?? ChatUrl(chatId, row.TenantId);
        if (row.ProtectedSenderToken is not null && row.SenderUserId is { } sender && desired.Ids.Contains(sender, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var senderToken = await SenderTokenAsync(row, cancellationToken);
                await graph.PostAsync(senderToken, $"chats/{Uri.EscapeDataString(chatId)}/messages", new JsonObject
                {
                    ["body"] = new JsonObject { ["contentType"] = "text", ["content"] = Clip($"Chat „{topic}“ wurde aus EstrichONE angelegt.", 500) },
                }, "ChatMessage.Send (delegiert)", cancellationToken);
            }
            catch (GraphException exception)
            {
                logger.LogWarning("Erste Nachricht im neuen Teams-Chat nicht gesendet: {Message}", exception.Message);
            }
        }

        return (chatId, webUrl);
    }

    private static GraphException MissingId(string what) =>
        new("UNEXPECTED_RESPONSE", $"Microsoft hat den {what} angelegt, aber keine ID zurückgemeldet. Bitte in Teams prüfen und ggf. per Link verknüpfen.");

    public async Task<TeamsBauakteLink> LinkByUrlAsync(Guid bauakteId, string url, Guid actor, CancellationToken cancellationToken)
    {
        var target = ParseLink(url);
        if (target?.Kind is null)
        {
            throw new GraphException("INVALID_LINK",
                "Das ist kein Link auf einen Teams-Kanal oder Chat. In Teams beim Kanal „…“ › „Link zum Kanal abrufen“ bzw. beim Chat „Link kopieren“ wählen.");
        }

        var row = await SettingsAsync(cancellationToken);
        return new TeamsBauakteLink
        {
            BauakteId = bauakteId, Kind = target.Kind.Value, Created = false, TeamId = target.TeamId, ChannelId = target.ChannelId, ChatId = target.ChatId,
            TenantId = target.TenantId ?? row.TenantId, WebUrl = target.WebUrl, DisplayName = Clip(target.Name, TeamsBauakteLink.MaxName), LinkedByUserId = actor,
            LinkedAt = clock.GetUtcNow(), SyncMembers = row.SyncMembers && row.GraphConfigured,
        };
    }

    public sealed record DesiredMembers(List<string> Ids, int Unmapped, string Hash);

    /// <summary>Soll-Mitglieder einer Bauakten-Verknüpfung: Teilnehmer (<see cref="TeamsBauakteLink.MemberSources"/>, leer = Bauakten-Team).</summary>
    public Task<DesiredMembers> DesiredMembersAsync(TeamsSettingsRow row, TeamsBauakteLink link, CancellationToken cancellationToken) =>
        DesiredMembersAsync(row, link.MemberSources.Count == 0 ? [TeamsMemberSources.Team] : link.MemberSources, link.BauakteId, cancellationToken);

    /// <summary>Microsoft-Konten der Teilnehmer und – falls verbunden – das Dienstkonto.</summary>
    public async Task<DesiredMembers> DesiredMembersAsync(TeamsSettingsRow row, IReadOnlyList<string> sources, Guid? bauakteId,
        CancellationToken cancellationToken) =>
        await DesiredForUsersAsync(row, await ResolveUsersAsync(sources, bauakteId, cancellationToken), cancellationToken);

    /// <summary>Benutzer-IDs zu Teilnehmern (Bauakten-Team, Kolonne, Rolle, Person).</summary>
    public async Task<List<Guid>> ResolveUsersAsync(IReadOnlyList<string> sources, Guid? bauakteId, CancellationToken cancellationToken)
    {
        var users = new HashSet<Guid>();
        foreach (var source in sources)
        {
            var (kind, id) = TeamsMemberSources.Parse(source);
            switch (kind)
            {
                case TeamsMemberSources.Team when bauakteId is { } bauakte:
                    users.UnionWith(await bauakten.TeamUserIdsAsync(bauakte, cancellationToken));
                    break;
                case TeamsMemberSources.CrewPrefix:
                    foreach (var audience in audiences)
                    {
                        users.UnionWith(await audience.CrewMembersAsync([id], cancellationToken));
                    }

                    break;
                case TeamsMemberSources.RolePrefix:
                    users.UnionWith(await roles.GetUserIdsInRoleAsync(id, cancellationToken));
                    break;
                case TeamsMemberSources.UserPrefix:
                    users.Add(id);
                    break;
            }
        }

        return [.. users];
    }

    public async Task<DesiredMembers> DesiredForUsersAsync(TeamsSettingsRow row, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var active = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync(cancellationToken);
        var mapped = await db.Set<TeamsUserLink>().AsNoTracking().Where(l => active.Contains(l.UserId) && l.AadUserId != null)
            .Select(l => l.AadUserId!).ToListAsync(cancellationToken);
        var ids = mapped.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (row.SenderUserId is { } sender && row.PostVia == TeamsPostVia.Graph && !ids.Contains(sender, StringComparer.OrdinalIgnoreCase))
        {
            ids.Add(sender);
        }

        ids.Sort(StringComparer.OrdinalIgnoreCase);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", ids))))[..32];
        return new DesiredMembers(ids, active.Count - mapped.Count, hash);
    }

    /// <summary>Mitglieder abgleichen (best effort). Ergebnis und Fehler stehen danach an der Verknüpfung; wirft nie.</summary>
    public async Task SyncMembersAsync(TeamsSettingsRow row, TeamsBauakteLink link, CancellationToken cancellationToken)
    {
        link.SyncPending = false;
        if (!row.GraphConfigured)
        {
            link.LastSyncError = "Microsoft Graph ist nicht eingerichtet – Mitglieder werden nicht abgeglichen.";
            return;
        }

        try
        {
            var desired = await DesiredMembersAsync(row, link, cancellationToken);
            var token = await TokenAsync(row, cancellationToken);
            var problems = new List<string>();
            if (link.Kind == TeamsLinkKind.GroupChat && link.ChatId is { } chatId)
            {
                link.ManagedMemberIds = await SyncChatMembersAsync(token, chatId, desired, link.ManagedMemberIds, problems, cancellationToken);
            }
            else if (link.Kind == TeamsLinkKind.Channel && link.TeamId is { } teamId)
            {
                var current = await MembersAsync(token, $"teams/{Uri.EscapeDataString(teamId)}/members", "TeamMember.ReadWrite.All", cancellationToken);
                foreach (var id in desired.Ids.Where(id => !current.ContainsKey(id)))
                {
                    await TryAsync(problems, () => graph.PostAsync(token, $"teams/{Uri.EscapeDataString(teamId)}/members", Member(id, owner: false, history: false),
                        "TeamMember.ReadWrite.All", cancellationToken));
                    if (!link.ManagedMemberIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                    {
                        link.ManagedMemberIds = [.. link.ManagedMemberIds, id];
                    }
                }
            }
            else
            {
                problems.Add("Die Verknüpfung enthält keine Kanal- bzw. Chat-ID – Mitglieder lassen sich nicht abgleichen.");
            }

            AddUnmapped(problems, desired.Unmapped);
            link.SyncedMembers = desired.Ids.Count;
            link.UnmappedMembers = desired.Unmapped;
            link.DesiredHash = desired.Hash;
            link.LastSyncError = problems.Count == 0 ? null : Clip(string.Join(" ", problems.Distinct()), 2000);
        }
        catch (GraphException exception)
        {
            link.LastSyncError = exception.Message;
        }

        link.LastSyncAt = clock.GetUtcNow();
    }

    private static void AddUnmapped(List<string> problems, int unmapped)
    {
        if (unmapped > 0)
        {
            problems.Add(unmapped == 1
                ? "1 Person ist keinem Microsoft-Konto zugeordnet."
                : $"{unmapped} Personen sind keinem Microsoft-Konto zugeordnet.");
        }
    }

    /// <summary>
    /// Gruppenchat abgleichen: fehlende Konten hinzufügen (mit Verlauf), nur von EstrichONE Hinzugefügte wieder entfernen.
    /// Liefert die neue Liste der von EstrichONE verwalteten Konten.
    /// </summary>
    private async Task<List<string>> SyncChatMembersAsync(string token, string chatId, DesiredMembers desired, List<string> managedIds, List<string> problems,
        CancellationToken cancellationToken)
    {
        var managed = managedIds.ToList();
        var current = await MembersAsync(token, $"chats/{Uri.EscapeDataString(chatId)}/members", "ChatMember.ReadWrite.All", cancellationToken);
        foreach (var id in desired.Ids.Where(id => !current.ContainsKey(id)))
        {
            await TryAsync(problems, () => graph.PostAsync(token, $"chats/{Uri.EscapeDataString(chatId)}/members", Member(id, owner: true, history: true),
                "ChatMember.ReadWrite.All", cancellationToken));
            if (!managed.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                managed.Add(id);
            }
        }

        foreach (var id in managed.Where(id => !desired.Ids.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            if (current.TryGetValue(id, out var membership))
            {
                var removed = await TryAsync(problems, () => graph.DeleteAsync(token,
                    $"chats/{Uri.EscapeDataString(chatId)}/members/{Uri.EscapeDataString(membership)}", "ChatMember.ReadWrite.All", cancellationToken));
                if (!removed)
                {
                    continue;
                }
            }

            managed.RemoveAll(m => string.Equals(m, id, StringComparison.OrdinalIgnoreCase));
        }

        return managed;
    }

    // ---------- Kolonnen ----------

    /// <summary>Teams-Chat einer Kolonne anlegen (Mitglieder = Kolonne mit Microsoft-Konto, dazu die anlegende Person).</summary>
    public async Task<TeamsCrewChat> CreateCrewChatAsync(Guid crewId, string crewName, Guid actor, CancellationToken cancellationToken)
    {
        var row = await SettingsAsync(cancellationToken);
        var desired = await CrewDesiredAsync(row, crewId, actor, cancellationToken);
        var topic = Clip(CrewLabel(crewName), TeamsBauakteLink.MaxName);
        var (chatId, webUrl) = await CreateGroupChatAsync(row, topic, desired, cancellationToken);
        var chat = new TeamsCrewChat
        {
            CrewId = crewId, ChatId = chatId, WebUrl = webUrl, DisplayName = topic, Created = true, ManagedMemberIds = [.. desired.Ids], LinkedByUserId = actor,
            LinkedAt = clock.GetUtcNow(), LastSyncAt = clock.GetUtcNow(), SyncedMembers = desired.Ids.Count, UnmappedMembers = desired.Unmapped,
            DesiredHash = desired.Hash,
        };
        db.Add(chat);
        return chat;
    }

    /// <summary>„Kolonne Nord“ – ohne doppeltes „Kolonne“, wenn der Name schon so beginnt.</summary>
    public static string CrewLabel(string name)
    {
        var trimmed = name.Trim();
        return trimmed.StartsWith("Kolonne", StringComparison.OrdinalIgnoreCase) ? trimmed : $"Kolonne {trimmed}";
    }

    /// <summary>Soll der Kolonne; die anlegende Person (z. B. Disponent) bleibt Mitglied.</summary>
    public Task<DesiredMembers> CrewDesiredAsync(TeamsSettingsRow row, Guid crewId, Guid creator, CancellationToken cancellationToken) =>
        DesiredMembersAsync(row, [TeamsMemberSources.Crew(crewId), TeamsMemberSources.User(creator)], null, cancellationToken);

    /// <summary>Kolonnen-Chat abgleichen (best effort, wirft nie).</summary>
    public async Task SyncCrewChatAsync(TeamsSettingsRow row, TeamsCrewChat chat, CancellationToken cancellationToken)
    {
        chat.SyncPending = false;
        if (!row.GraphConfigured)
        {
            return;
        }

        try
        {
            var desired = await CrewDesiredAsync(row, chat.CrewId, chat.LinkedByUserId, cancellationToken);
            var problems = new List<string>();
            chat.ManagedMemberIds = await SyncChatMembersAsync(await TokenAsync(row, cancellationToken), chat.ChatId, desired, chat.ManagedMemberIds, problems,
                cancellationToken);
            AddUnmapped(problems, desired.Unmapped);
            chat.SyncedMembers = desired.Ids.Count;
            chat.UnmappedMembers = desired.Unmapped;
            chat.DesiredHash = desired.Hash;
            chat.LastSyncError = problems.Count == 0 ? null : Clip(string.Join(" ", problems.Distinct()), 2000);
        }
        catch (GraphException exception)
        {
            chat.LastSyncError = exception.Message;
        }

        chat.LastSyncAt = clock.GetUtcNow();
    }

    /// <summary>Kolonnen-Chat in Teams löschen (bei Microsoft schon gelöscht = erledigt).</summary>
    public async Task DeleteCrewChatAsync(TeamsSettingsRow row, TeamsCrewChat chat, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(row, cancellationToken);
        try
        {
            await graph.DeleteAsync(token, $"chats/{Uri.EscapeDataString(chat.ChatId)}", "Chat.ManageDeletion.All", cancellationToken);
        }
        catch (GraphException exception) when (exception.Status == 404)
        {
            logger.LogWarning("Teams-Chat der Kolonne {CrewId} war bei Microsoft schon gelöscht", chat.CrewId);
        }
    }

    private static async Task<bool> TryAsync(List<string> problems, Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (GraphException exception)
        {
            problems.Add(exception.Message);
            return false;
        }
    }

    /// <summary>Mitglieder als Konto-ID → Mitgliedschafts-ID.</summary>
    private async Task<Dictionary<string, string>> MembersAsync(string token, string path, string permission, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var next = path;
        for (var page = 0; next is not null && page < 20; page++)
        {
            var json = await graph.GetAsync(token, next, permission, cancellationToken);
            foreach (var member in (json?["value"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (GraphClient.Str(member["userId"]) is { } userId)
                {
                    result[userId] = GraphClient.Str(member["id"]) ?? userId;
                }
            }

            var nextLink = GraphClient.Str(json?["@odata.nextLink"]);
            var at = nextLink?.IndexOf("/v1.0/", StringComparison.Ordinal) ?? -1;
            next = at < 0 ? null : nextLink![(at + 6)..];
        }

        return result;
    }

    private static JsonObject Member(string aadUserId, bool owner, bool history)
    {
        var member = new JsonObject
        {
            ["@odata.type"] = "#microsoft.graph.aadUserConversationMember",
            ["roles"] = owner ? new JsonArray("owner") : new JsonArray(),
            ["user@odata.bind"] = $"https://graph.microsoft.com/v1.0/users('{aadUserId}')",
        };
        if (history)
        {
            member["visibleHistoryStartDateTime"] = "0001-01-01T00:00:00Z";
        }

        return member;
    }

    // ---------- Ereigniskarten ----------

    public static bool Wants(TeamsSettingsRow row, TeamsEventKind kind) => kind switch
    {
        TeamsEventKind.DefectCreated => row.PostDefects,
        TeamsEventKind.AcceptanceSigned or TeamsEventKind.AcceptanceRejected => row.PostAcceptance,
        TeamsEventKind.ScheduleChanged => row.PostSchedule,
        _ => false,
    };

    /// <summary>Ereignis als Adaptive Card senden (Webhook oder Dienstkonto). Fehler stehen an Einstellung bzw. Verknüpfung; wirft nie.</summary>
    public async Task PostAsync(TeamsEvent teamsEvent, CancellationToken cancellationToken)
    {
        var row = await db.Set<TeamsSettingsRow>().SingleOrDefaultAsync(cancellationToken);
        if (row is null || row.PostVia == TeamsPostVia.Off || !Wants(row, teamsEvent.Kind))
        {
            return;
        }

        var link = await db.Set<TeamsBauakteLink>().SingleOrDefaultAsync(l => l.BauakteId == teamsEvent.BauakteId, cancellationToken);
        var bauakte = await db.Set<Bauakte>().AsNoTracking().IgnoreQueryFilters().SingleOrDefaultAsync(b => b.Id == teamsEvent.BauakteId, cancellationToken);
        if (bauakte is null)
        {
            return;
        }

        var card = Card(teamsEvent, bauakte.Number, bauakte.Title);
        string? error = null;
        var sent = false;
        try
        {
            if (row.PostVia == TeamsPostVia.Webhook)
            {
                var url = Unprotect(link?.ProtectedWebhookUrl) ?? Unprotect(row.ProtectedWebhookUrl);
                if (url is not null)
                {
                    await graph.PostWebhookAsync(url, card, cancellationToken);
                    sent = true;
                }
            }
            else if (link is not null)
            {
                await PostViaGraphAsync(row, link, card, cancellationToken);
                sent = true;
            }
        }
        catch (GraphException exception)
        {
            error = exception.Message;
            logger.LogWarning("Teams-Karte für Bauakte {BauakteId} nicht gesendet: {Error}", teamsEvent.BauakteId, exception.Message);
        }

        var now = clock.GetUtcNow();
        if (sent || error is not null)
        {
            row.LastPostAt = now;
            row.LastPostError = error;
            if (link is not null)
            {
                link.LastPostAt = now;
                link.LastPostError = error;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task PostViaGraphAsync(TeamsSettingsRow row, TeamsBauakteLink link, JsonNode card, CancellationToken cancellationToken)
    {
        var token = await SenderTokenAsync(row, cancellationToken);
        var body = new JsonObject
        {
            ["body"] = new JsonObject { ["contentType"] = "html", ["content"] = "<attachment id=\"estrichone\"></attachment>" },
            ["attachments"] = new JsonArray(new JsonObject
            {
                ["id"] = "estrichone", ["contentType"] = "application/vnd.microsoft.card.adaptive", ["content"] = card.ToJsonString(),
            }),
        };
        if (link.Kind == TeamsLinkKind.Channel && link.TeamId is { } teamId && link.ChannelId is { } channelId)
        {
            await graph.PostAsync(token, $"teams/{Uri.EscapeDataString(teamId)}/channels/{Uri.EscapeDataString(channelId)}/messages", body,
                "ChannelMessage.Send (delegiert)", cancellationToken);
        }
        else if (link.ChatId is { } chatId)
        {
            await graph.PostAsync(token, $"chats/{Uri.EscapeDataString(chatId)}/messages", body, "ChatMessage.Send (delegiert)", cancellationToken);
        }
        else
        {
            throw new GraphException("NO_TARGET", "Die Verknüpfung enthält keine Kanal- bzw. Chat-ID.");
        }
    }

    /// <summary>Zugriffstoken des Dienstkontos; speichert das erneuerte Aktualisierungstoken (Entra ID rotiert es).</summary>
    public async Task<string> SenderTokenAsync(TeamsSettingsRow row, CancellationToken cancellationToken)
    {
        var refresh = Unprotect(row.ProtectedSenderToken)
            ?? throw new GraphException("NO_SENDER", "Für „Senden über Microsoft Graph“ ist kein Dienstkonto verbunden (Einstellungen › Microsoft Teams).");
        var token = await graph.RefreshAsync(Credentials(row), refresh, cancellationToken);
        if (token.RefreshToken is { } rotated && rotated != refresh)
        {
            row.ProtectedSenderToken = Protect(rotated);
        }

        return token.AccessToken;
    }

    public JsonObject Card(TeamsEvent teamsEvent, string? number, string title)
    {
        var heading = teamsEvent.Kind switch
        {
            TeamsEventKind.DefectCreated => "Neuer Mangel",
            TeamsEventKind.AcceptanceSigned => "Abnahme unterschrieben",
            TeamsEventKind.AcceptanceRejected => "Abnahme abgelehnt",
            TeamsEventKind.ScheduleChanged => "Termin geändert",
            _ => "Hinweis",
        };
        var body = new JsonArray
        {
            new JsonObject { ["type"] = "TextBlock", ["text"] = heading, ["weight"] = "Bolder", ["size"] = "Medium", ["wrap"] = true },
            new JsonObject { ["type"] = "TextBlock", ["text"] = Clip(teamsEvent.Title, 300), ["wrap"] = true },
        };
        if (!string.IsNullOrWhiteSpace(teamsEvent.Detail))
        {
            body.Add(new JsonObject { ["type"] = "TextBlock", ["text"] = Clip(teamsEvent.Detail, 500), ["wrap"] = true, ["isSubtle"] = true });
        }

        body.Add(new JsonObject
        {
            ["type"] = "FactSet",
            ["facts"] = new JsonArray(new JsonObject { ["title"] = "Bauakte", ["value"] = Clip($"{number} {title}".Trim(), 200) }),
        });
        var card = new JsonObject
        {
            ["type"] = "AdaptiveCard", ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json", ["version"] = "1.4", ["body"] = body,
        };
        if (app.Value.PublicUrl is { } publicUrl)
        {
            card["actions"] = new JsonArray(new JsonObject
            {
                ["type"] = "Action.OpenUrl", ["title"] = "In EstrichONE öffnen",
                ["url"] = new Uri(publicUrl, teamsEvent.Link ?? $"/bauakten/{teamsEvent.BauakteId}").ToString(),
            });
        }

        return card;
    }

    // ---------- Links ----------

    [GeneratedRegex(@"[~#%&*{}+/\\:<>?|'""\u0000-\u001f]")]
    private static partial Regex ForbiddenChannelCharacters();

    /// <summary>Kanalname nach den Regeln von Teams (höchstens 50 Zeichen, keine Sonderzeichen wie # % & : / ?).</summary>
    public static string ChannelName(string? number, string title)
    {
        var name = ForbiddenChannelCharacters().Replace($"{number} {title}".Trim(), " ");
        name = Regex.Replace(name, @"\s+", " ").Trim().TrimStart('_', '.', ' ');
        if (name.Length > 50)
        {
            name = name[..50].TrimEnd(' ', '.');
        }

        return name.Length == 0 ? "Bauakte" : name.TrimEnd('.');
    }

    public static string ChannelUrl(string channelId, string name, string teamId, string? tenantId) =>
        $"https://teams.microsoft.com/l/channel/{Uri.EscapeDataString(channelId)}/{Uri.EscapeDataString(name)}?groupId={Uri.EscapeDataString(teamId)}"
        + (tenantId is null ? string.Empty : $"&tenantId={Uri.EscapeDataString(tenantId)}");

    public static string ChatUrl(string chatId, string? tenantId) =>
        $"https://teams.microsoft.com/l/chat/{Uri.EscapeDataString(chatId)}/0" + (tenantId is null ? string.Empty : $"?tenantId={Uri.EscapeDataString(tenantId)}");

    /// <summary>Link zum Öffnen in der Teams-App (<c>msteams:</c>); Browser nehmen den https-Link.</summary>
    public static string AppUrl(string webUrl) =>
        Uri.TryCreate(webUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? "msteams:" + uri.PathAndQuery
            : webUrl;

    /// <summary>Teams-Link zerlegen: Kanal, Chat oder Team (teams.microsoft.com, teams.cloud.microsoft, msteams:).</summary>
    public static TeamsLinkTarget? ParseLink(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Trim();
        if (text.StartsWith("msteams:", StringComparison.OrdinalIgnoreCase))
        {
            text = "https://teams.microsoft.com" + text["msteams:".Length..].TrimStart('/').Insert(0, "/");
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !(uri.Host.Equals("teams.microsoft.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("teams.cloud.microsoft", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("teams.live.com", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        var groupId = query.TryGetValue("groupId", out var g) ? g.ToString() : null;
        var tenantId = query.TryGetValue("tenantId", out var t) ? t.ToString() : null;
        var webUrl = $"https://teams.microsoft.com{uri.PathAndQuery}";
        if (segments.Length >= 3 && segments[0] == "l" && segments[1] == "channel")
        {
            var name = segments.Length >= 4 ? segments[3] : null;
            return new TeamsLinkTarget(TeamsLinkKind.Channel, groupId, segments[2], null, tenantId, name, webUrl);
        }

        if (segments.Length >= 3 && segments[0] == "l" && segments[1] == "chat" && segments[2].StartsWith("19:", StringComparison.Ordinal))
        {
            return new TeamsLinkTarget(TeamsLinkKind.GroupChat, null, null, segments[2], tenantId, query.TryGetValue("topicName", out var n) ? n.ToString() : null,
                webUrl);
        }

        if (segments.Length >= 2 && segments[0] == "l" && segments[1] == "team" && groupId is not null)
        {
            return new TeamsLinkTarget(null, groupId, null, null, tenantId, null, webUrl);
        }

        return null;
    }

    internal static string Clip(string? text, int max) => GraphClient.Clip(text, max);

    internal static string Format(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}

/// <summary>Teilnehmer eines Teams-Chats als Schlüssel (gespeichert an der Verknüpfung, Mitgliederabgleich folgt ihnen).</summary>
public static class TeamsMemberSources
{
    public const string Team = "team";
    public const string CrewPrefix = "crew";
    public const string RolePrefix = "role";
    public const string UserPrefix = "user";

    public static string Crew(Guid id) => $"{CrewPrefix}:{id}";

    public static string Role(Guid id) => $"{RolePrefix}:{id}";

    public static string User(Guid id) => $"{UserPrefix}:{id}";

    public static (string Kind, Guid Id) Parse(string source)
    {
        if (string.Equals(source, Team, StringComparison.OrdinalIgnoreCase))
        {
            return (Team, Guid.Empty);
        }

        var at = source.IndexOf(':', StringComparison.Ordinal);
        return at > 0 && Guid.TryParse(source[(at + 1)..], out var id) ? (source[..at].ToLowerInvariant(), id) : (string.Empty, Guid.Empty);
    }

    public static bool IsValid(string source) => Parse(source).Kind is Team or CrewPrefix or RolePrefix or UserPrefix;
}
