using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Teams.Exports;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Teams;

/// <summary>Benötigte Microsoft-Graph-Berechtigungen mit Begründung (Oberfläche und Handbuch).</summary>
public sealed record TeamsPermissionInfo(string Name, string Type, bool Required, string Reason);

public sealed record TeamsTestStep(string Name, bool Ok, string Detail);

/// <summary>Teams-Link (Kanal, Chat oder Team) zerlegt.</summary>
public sealed record TeamsLinkTarget(TeamsLinkKind? Kind, string? TeamId, string? ChannelId, string? ChatId, string? TenantId, string? Name, string WebUrl);

/// <summary>Kern der Teams-Anbindung: Geheimnisse, Zuordnung der Konten, Verknüpfung je Bauakte, Mitgliederabgleich, Ereigniskarten.</summary>
internal sealed partial class TeamsService(
    EstrichOneDbContext db, GraphClient graph, IDataProtectionProvider protection, TimeProvider clock, IOptions<EstrichOneOptions> app,
    IBauakteReportSource bauakten, ILogger<TeamsService> logger)
{
    public const string ProtectorPurpose = "EstrichOne.Teams.Secrets.v1";

    public static IReadOnlyList<TeamsPermissionInfo> Permissions { get; } =
    [
        new("User.Read.All", "Anwendung", true, "Benutzer über E-Mail-Adresse bzw. UPN einem Microsoft-Konto zuordnen (liest nur Name, E-Mail, UPN, ID)."),
        new("Team.ReadBasic.All", "Anwendung", true, "Team „Baustellen“ prüfen und dessen Namen und Link anzeigen."),
        new("Channel.Create", "Anwendung", false, "Je Bauakte einen Kanal im Team „Baustellen“ anlegen (nur bei Art „Kanal“)."),
        new("TeamMember.ReadWrite.All", "Anwendung", false,
            "Bauakten-Team ins Team „Baustellen“ aufnehmen, damit es den Kanal sieht (nur bei Art „Kanal“ mit Mitgliederabgleich; entfernt niemanden)."),
        new("Chat.Create", "Anwendung", false, "Je Bauakte einen Gruppenchat mit dem Bauakten-Team anlegen (nur bei Art „Gruppenchat“)."),
        new("ChatMember.ReadWrite.All", "Anwendung", false,
            "Mitglieder des Gruppenchats bei Teamänderungen abgleichen (fügt hinzu, entfernt nur von EstrichONE Hinzugefügte)."),
        new("ChannelMessage.Send", "Delegiert", false, "Ereigniskarten über das Dienstkonto in verknüpfte Kanäle senden (nur bei „Senden über Microsoft Graph“)."),
        new("ChatMessage.Send", "Delegiert", false, "Ereigniskarten über das Dienstkonto in verknüpfte Gruppenchats senden (nur bei „Senden über Microsoft Graph“)."),
        new("offline_access", "Delegiert", false, "Dienstkonto angemeldet halten (Aktualisierungstoken, verschlüsselt gespeichert)."),
    ];

    public EstrichOneDbContext Db => db;

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
        catch (GraphException exception)
        {
            steps.Add(new("Anmeldung bei Microsoft Entra ID", false, exception.Message));
            return steps;
        }

        await StepAsync(steps, "Benutzer lesen (User.Read.All)", async () =>
        {
            await graph.GetAsync(token, "users?$top=1&$select=id", "User.Read.All", cancellationToken);
            return "Benutzerverzeichnis lesbar.";
        });
        if (!string.IsNullOrWhiteSpace(row.SiteTeamId))
        {
            await StepAsync(steps, "Team „Baustellen“ (Team.ReadBasic.All)", async () =>
            {
                var team = await graph.GetAsync(token, $"teams/{Uri.EscapeDataString(row.SiteTeamId)}?$select=id,displayName,webUrl", "Team.ReadBasic.All",
                    cancellationToken);
                return $"Team „{team?["displayName"]?.GetValue<string>() ?? row.SiteTeamName}“ gefunden.";
            });
        }

        if (row.ProtectedSenderToken is not null)
        {
            await StepAsync(steps, "Dienstkonto für Nachrichten", async () =>
            {
                await SenderTokenAsync(row, cancellationToken);
                return $"Angemeldet als {row.SenderName ?? "Dienstkonto"}.";
            });
        }

        return steps;
    }

    private static async Task StepAsync(List<TeamsTestStep> steps, string name, Func<Task<string>> action)
    {
        try
        {
            steps.Add(new(name, true, await action()));
        }
        catch (GraphException exception)
        {
            steps.Add(new(name, false, exception.Message));
        }
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
        return (id, team?["displayName"]?.GetValue<string>() ?? "Team", team?["webUrl"]?.GetValue<string>());
    }

    // ---------- Benutzerzuordnung ----------

    public sealed record MatchSummary(int Matched, int NotFound, int Manual, int Skipped, string? Error);

    public async Task<MatchSummary> MatchUsersAsync(TeamsSettingsRow row, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(row, cancellationToken);
        var users = await db.Set<User>().AsNoTracking().Where(u => u.IsActive).ToListAsync(cancellationToken);
        var links = await db.Set<TeamsUserLink>().ToDictionaryAsync(l => l.UserId, cancellationToken);
        int matched = 0, notFound = 0, manual = 0, skipped = 0;
        string? error = null;
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

            try
            {
                var account = await FindAccountAsync(token, address, cancellationToken);
                Apply(existing, account, manual: false);
                existing.Error = account is null ? $"Kein Microsoft-Konto mit „{address}“ gefunden." : null;
                if (account is null)
                {
                    notFound++;
                }
                else
                {
                    matched++;
                }
            }
            catch (GraphException exception) when (exception.Code is not ("OFFLINE" or "AUTH_FAILED" or "FORBIDDEN"))
            {
                existing.Error = exception.Message;
                notFound++;
            }
            catch (GraphException exception)
            {
                error = exception.Message;
                break;
            }
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

    private async Task<(string Id, string? Upn, string? Name)?> FindAccountAsync(string token, string address, CancellationToken cancellationToken)
    {
        var quoted = address.Replace("'", "''", StringComparison.Ordinal);
        var filter = Uri.EscapeDataString($"mail eq '{quoted}' or userPrincipalName eq '{quoted}'");
        var result = await graph.GetAsync(token, $"users?$filter={filter}&$select=id,displayName,userPrincipalName,mail&$top=2", "User.Read.All",
            cancellationToken);
        var values = result?["value"] as JsonArray;
        return values is { Count: 1 } ? Account(values[0]) : null;
    }

    private static (string Id, string? Upn, string? Name)? Account(JsonNode? node) =>
        node?["id"]?.GetValue<string>() is { } id
            ? (id, node["userPrincipalName"]?.GetValue<string>(), node["displayName"]?.GetValue<string>())
            : null;

    // ---------- Verknüpfung je Bauakte ----------

    public async Task<TeamsBauakteLink> CreateLinkAsync(Guid bauakteId, TeamsLinkKind kind, Guid actor, CancellationToken cancellationToken)
    {
        var row = await SettingsAsync(cancellationToken);
        var bauakte = await db.Set<Bauakte>().AsNoTracking().SingleAsync(b => b.Id == bauakteId, cancellationToken);
        var token = await TokenAsync(row, cancellationToken);
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

            JsonNode? channel;
            try
            {
                channel = await graph.PostAsync(token, $"teams/{Uri.EscapeDataString(row.SiteTeamId)}/channels", new JsonObject
                {
                    ["displayName"] = name, ["description"] = Clip($"EstrichONE-Bauakte {bauakte.Number} {bauakte.Title}".Trim(), 1000),
                    ["membershipType"] = "standard",
                }, "Channel.Create", cancellationToken);
            }
            catch (GraphException exception) when (exception.Code == "CONFLICT"
                || (exception.Code == "GRAPH_REJECTED" && exception.Message.Contains("exist", StringComparison.OrdinalIgnoreCase)))
            {
                throw new GraphException("CONFLICT",
                    $"Im Team „{row.SiteTeamName}“ gibt es bereits einen Kanal „{name}“. Bitte diesen über „Vorhandenen Kanal oder Chat verknüpfen“ per Link verknüpfen.");
            }

            link.TeamId = row.SiteTeamId;
            link.ChannelId = channel?["id"]?.GetValue<string>();
            link.WebUrl = channel?["webUrl"]?.GetValue<string>() ?? ChannelUrl(link.ChannelId!, name, row.SiteTeamId, row.TenantId);
        }
        else
        {
            var desired = await DesiredMembersAsync(row, bauakteId, cancellationToken);
            if (desired.Ids.Count < 2)
            {
                throw new GraphException("TOO_FEW_MEMBERS",
                    "Für einen Gruppenchat müssen mindestens zwei Personen des Bauakten-Teams (Zuständige, Mitglieder) einem Microsoft-Konto zugeordnet sein. "
                    + "Bitte Team ergänzen oder Konten zuordnen (Einstellungen › Integrationen › Microsoft Teams › Benutzer).");
            }

            var members = new JsonArray();
            foreach (var id in desired.Ids)
            {
                members.Add(Member(id, owner: true, history: false));
            }

            var chat = await graph.PostAsync(token, "chats", new JsonObject { ["chatType"] = "group", ["topic"] = name, ["members"] = members },
                "Chat.Create", cancellationToken);
            link.ChatId = chat?["id"]?.GetValue<string>();
            link.WebUrl = chat?["webUrl"]?.GetValue<string>() ?? ChatUrl(link.ChatId!, row.TenantId);
            link.ManagedMemberIds = [.. desired.Ids];
            link.SyncedMembers = desired.Ids.Count;
            link.UnmappedMembers = desired.Unmapped;
            link.LastSyncAt = clock.GetUtcNow();
            link.DesiredHash = desired.Hash;
        }

        db.Add(link);
        return link;
    }

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

    /// <summary>Microsoft-Konten des Bauakten-Teams (Zuständige und Mitglieder) und – falls verbunden – das Dienstkonto.</summary>
    public async Task<DesiredMembers> DesiredMembersAsync(TeamsSettingsRow row, Guid bauakteId, CancellationToken cancellationToken)
    {
        var userIds = await bauakten.TeamUserIdsAsync(bauakteId, cancellationToken);
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
            var desired = await DesiredMembersAsync(row, link.BauakteId, cancellationToken);
            var token = await TokenAsync(row, cancellationToken);
            var problems = new List<string>();
            if (link.Kind == TeamsLinkKind.GroupChat && link.ChatId is { } chatId)
            {
                var current = await MembersAsync(token, $"chats/{Uri.EscapeDataString(chatId)}/members", "ChatMember.ReadWrite.All", cancellationToken);
                foreach (var id in desired.Ids.Where(id => !current.ContainsKey(id)))
                {
                    await TryAsync(problems, () => graph.PostAsync(token, $"chats/{Uri.EscapeDataString(chatId)}/members", Member(id, owner: true, history: true),
                        "ChatMember.ReadWrite.All", cancellationToken));
                    if (!link.ManagedMemberIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                    {
                        link.ManagedMemberIds = [.. link.ManagedMemberIds, id];
                    }
                }

                // Nur Personen entfernen, die EstrichONE selbst hinzugefügt hat und die nicht mehr zum Team gehören.
                foreach (var id in link.ManagedMemberIds.Where(id => !desired.Ids.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList())
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

                    link.ManagedMemberIds = link.ManagedMemberIds.Where(m => !string.Equals(m, id, StringComparison.OrdinalIgnoreCase)).ToList();
                }
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

            if (desired.Unmapped > 0)
            {
                problems.Add(desired.Unmapped == 1
                    ? "1 Person des Bauakten-Teams ist keinem Microsoft-Konto zugeordnet."
                    : $"{desired.Unmapped} Personen des Bauakten-Teams sind keinem Microsoft-Konto zugeordnet.");
            }

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
                if (member["userId"]?.GetValue<string>() is { } userId)
                {
                    result[userId] = member["id"]?.GetValue<string>() ?? userId;
                }
            }

            var nextLink = json?["@odata.nextLink"]?.GetValue<string>();
            next = nextLink is null ? null : nextLink[(nextLink.IndexOf("/v1.0/", StringComparison.Ordinal) + 6)..];
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
