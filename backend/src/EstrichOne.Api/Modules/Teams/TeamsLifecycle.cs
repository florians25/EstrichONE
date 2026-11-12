using System.Text.Json.Nodes;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Teams.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Teams;

/// <summary>Person für „Chat in Teams“: Microsoft-Konto (UPN) oder Grund, warum kein Chat möglich ist.</summary>
/// <param name="Upn">Anmeldename bei Microsoft; ohne Kontenzuordnung (keine App-Registrierung) die E-Mail-Adresse.</param>
/// <param name="FromEmail">Nur aus der E-Mail-Adresse abgeleitet (nicht bei Microsoft geprüft).</param>
public sealed record TeamsPerson(Guid UserId, string Name, string? Upn, bool FromEmail, string? Reason);

/// <summary>Grund, warum eine Person nicht in den Teams-Chat aufgenommen wurde.</summary>
public sealed record TeamsChatMissing(Guid UserId, string Name, string Reason);

/// <summary>Link zu einem Einzel- bzw. Gruppenchat in Teams.</summary>
/// <param name="WebUrl">https-Link (öffnet im Browser bzw. die Teams-App).</param>
/// <param name="AppUrl">Link für die Teams-App (<c>msteams:</c>).</param>
/// <param name="ViaGraph">Über Microsoft Graph angelegt (stabiler Chat-Link) statt Teams-Deep-Link.</param>
public sealed record TeamsChatLinkResponse(string WebUrl, string AppUrl, bool ViaGraph, IReadOnlyList<string> Included, IReadOnlyList<TeamsChatMissing> Missing);

/// <summary>Teams-Lebenszyklus: Kanäle archivieren und löschen, Chats löschen, Einzel- und Gruppenchats starten, Rechte prüfen.</summary>
internal sealed partial class TeamsService
{
    /// <summary>Berechtigung → andere Anwendungsberechtigungen, die sie einschließen (Microsoft Graph).</summary>
    private static readonly Dictionary<string, string[]> Covering = new(StringComparer.OrdinalIgnoreCase)
    {
        ["User.Read.All"] = ["User.ReadWrite.All", "Directory.Read.All", "Directory.ReadWrite.All"],
        ["Team.ReadBasic.All"] = ["TeamSettings.Read.All", "TeamSettings.ReadWrite.All", "Group.Read.All", "Group.ReadWrite.All", "Directory.Read.All",
            "Directory.ReadWrite.All"],
        ["Channel.Create"] = ["Group.ReadWrite.All", "Directory.ReadWrite.All"],
        ["TeamMember.ReadWrite.All"] = ["Group.ReadWrite.All", "Directory.ReadWrite.All"],
        ["Chat.Create"] = ["Chat.ReadWrite.All"],
        ["ChatMember.ReadWrite.All"] = ["Chat.ReadWrite.All"],
        ["ChannelSettings.ReadWrite.All"] = ["Group.ReadWrite.All", "Directory.ReadWrite.All"],
        ["Channel.Delete.All"] = ["Group.ReadWrite.All", "Directory.ReadWrite.All"],
        ["Chat.ManageDeletion.All"] = ["Chat.ReadWrite.All"],
    };

    /// <summary>Fehlende Anwendungsberechtigungen laut Zugriffstoken; <c>null</c> = nicht feststellbar.</summary>
    public static IReadOnlyList<string>? MissingPermissions(string token)
    {
        var roles = GraphClient.AppRoles(token);
        return roles is null
            ? null
            : Permissions.Where(p => p.Type == "Anwendung")
                .Where(p => !roles.Contains(p.Name) && !Covering.GetValueOrDefault(p.Name, []).Any(roles.Contains))
                .Select(p => p.Name).ToList();
    }

    /// <summary>Prüfschritt „Berechtigungen“: Pflicht fehlt → Fehler; optionale fehlen → Hinweis mit Folgen (z. B. Löschen nicht möglich).</summary>
    private static void PermissionStep(List<TeamsTestStep> steps, string token)
    {
        var missing = MissingPermissions(token);
        if (missing is null)
        {
            return; // kein lesbares Token (z. B. ältere Anmeldung) – die folgenden Schritte prüfen die Pflichtrechte einzeln.
        }

        var required = missing.Where(m => Permissions.Any(p => p.Name == m && p.Required)).ToList();
        var optional = missing.Except(required).ToList();
        if (missing.Count == 0)
        {
            steps.Add(new("Berechtigungen (Anwendung)", true, "Alle Anwendungsberechtigungen sind gewährt – auch Archivieren und Löschen."));
            return;
        }

        var parts = new List<string>();
        if (required.Count > 0)
        {
            parts.Add($"Es fehlt: {string.Join(", ", required)}.");
        }

        if (optional.Count > 0)
        {
            parts.Add("Nicht gewährt (optional): " + string.Join("; ", optional.Select(name => $"{name} – {Consequence(name)}")) + ".");
        }

        parts.Add("Ergänzen in Entra ID › App-Registrierung › API-Berechtigungen, danach „Administratorzustimmung erteilen“.");
        steps.Add(new("Berechtigungen (Anwendung)", required.Count == 0, string.Join(" ", parts), Warning: required.Count == 0));
    }

    private static string Consequence(string permission) => permission switch
    {
        "Channel.Create" => "Kanäle werden nicht automatisch angelegt",
        "TeamMember.ReadWrite.All" => "Bauakten-Team wird nicht ins Team „Baustellen“ aufgenommen",
        "Chat.Create" => "Gruppenchats bzw. Einzelchats über Graph nicht möglich (Teams-Links funktionieren weiter)",
        "ChatMember.ReadWrite.All" => "Mitglieder von Gruppenchats werden nicht abgeglichen",
        "ChannelSettings.ReadWrite.All" => "Kanäle lassen sich nicht archivieren",
        "Channel.Delete.All" => "Kanäle lassen sich nicht löschen",
        "Chat.ManageDeletion.All" => "Gruppenchats lassen sich nicht löschen",
        _ => "eingeschränkt",
    };

    // ---------- Archivieren und Löschen ----------

    private static string ChannelPath(TeamsBauakteLink link) =>
        link is { Kind: TeamsLinkKind.Channel, TeamId: { } team, ChannelId: { } channel }
            ? $"teams/{Uri.EscapeDataString(team)}/channels/{Uri.EscapeDataString(channel)}"
            : throw new GraphException("NOT_SUPPORTED", link.Kind == TeamsLinkKind.Channel
                ? "Zu diesem Kanal kennt EstrichONE die Team- bzw. Kanal-ID nicht (Verknüpfung per Link ohne „groupId“). Bitte in Teams archivieren bzw. löschen."
                : "Gruppenchats lassen sich in Microsoft Teams nicht archivieren. Möglich sind „In Teams löschen“ oder „Verknüpfung lösen“.");

    /// <summary>Kanal archivieren (nur lesen, in Teams wiederherstellbar). Gruppenchats: <c>NOT_SUPPORTED</c>.</summary>
    public async Task ArchiveAsync(TeamsSettingsRow row, TeamsBauakteLink link, CancellationToken cancellationToken)
    {
        var path = ChannelPath(link);
        var token = await TokenAsync(row, cancellationToken);
        await graph.PostAsync(token, $"{path}/archive", new JsonObject(), "ChannelSettings.ReadWrite.All", cancellationToken);
        link.ArchivedAt = clock.GetUtcNow();
    }

    public async Task UnarchiveAsync(TeamsSettingsRow row, TeamsBauakteLink link, CancellationToken cancellationToken)
    {
        var path = ChannelPath(link);
        var token = await TokenAsync(row, cancellationToken);
        await graph.PostAsync(token, $"{path}/unarchive", new JsonObject(), "ChannelSettings.ReadWrite.All", cancellationToken);
        link.ArchivedAt = null;
    }

    /// <summary>
    /// Kanal bzw. Gruppenchat bei Microsoft löschen (vorläufig gelöscht: Kanäle 30 Tage im Teams-Client wiederherstellbar).
    /// Schon gelöscht (404) gilt als erledigt.
    /// </summary>
    public async Task DeleteRemoteAsync(TeamsSettingsRow row, TeamsBauakteLink link, CancellationToken cancellationToken)
    {
        string path;
        string permission;
        if (link.Kind == TeamsLinkKind.Channel)
        {
            path = ChannelPath(link);
            permission = "Channel.Delete.All";
        }
        else if (link.ChatId is { } chatId)
        {
            path = $"chats/{Uri.EscapeDataString(chatId)}";
            permission = "Chat.ManageDeletion.All";
        }
        else
        {
            throw new GraphException("NOT_SUPPORTED", "Zu diesem Chat kennt EstrichONE keine Chat-ID. Bitte in Teams löschen und hier „Verknüpfung lösen“ wählen.");
        }

        var token = await TokenAsync(row, cancellationToken);
        try
        {
            await graph.DeleteAsync(token, path, permission, cancellationToken);
        }
        catch (GraphException exception) when (exception.Code == "NOT_FOUND" && exception.Status == 404)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Teams-Unterhaltung der Bauakte {BauakteId} war bei Microsoft schon gelöscht", link.BauakteId);
            }
        }
        catch (GraphException exception) when (exception.Code == "GRAPH_REJECTED" && link.Kind == TeamsLinkKind.Channel
            && exception.Message.Contains("general", StringComparison.OrdinalIgnoreCase))
        {
            throw new GraphException("NOT_SUPPORTED", "Der Standardkanal „Allgemein“ eines Teams lässt sich nicht löschen.");
        }
    }

    /// <summary>
    /// Abschluss-Aktion für abgeschlossene bzw. stornierte Bauakten (nur von EstrichONE angelegte Unterhaltungen). Wieder geöffnete
    /// Bauakten: Archivierung aufheben. Fehler stehen an der Verknüpfung; erneuter Versuch frühestens nach einem Tag.
    /// </summary>
    public async Task HandleClosedAsync(TeamsSettingsRow row, CancellationToken cancellationToken)
    {
        if (!row.GraphConfigured)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var retry = now.AddDays(-1);
        var closed = new[] { BauakteStatus.Completed, BauakteStatus.Cancelled };
        var candidates = await (
            from link in db.Set<TeamsBauakteLink>()
            join bauakte in db.Set<Bauakte>().IgnoreQueryFilters() on link.BauakteId equals bauakte.Id
            where link.Created
            select new { Link = link, bauakte.Status, bauakte.Number, Deleted = bauakte.DeletedAt != null }).ToListAsync(cancellationToken);
        foreach (var item in candidates)
        {
            var link = item.Link;
            var isClosed = closed.Contains(item.Status) || item.Deleted;
            if (!isClosed)
            {
                if (link.CloseHandledAt is not null)
                {
                    // Wieder geöffnet: von EstrichONE archivierten Kanal wiederherstellen.
                    link.CloseHandledAt = null;
                    link.CloseError = null;
                    if (link.ArchivedAt is not null)
                    {
                        try
                        {
                            await UnarchiveAsync(row, link, cancellationToken);
                        }
                        catch (GraphException exception)
                        {
                            link.CloseError = exception.Message;
                        }
                    }

                    await db.SaveChangesAsync(cancellationToken);
                }

                continue;
            }

            if (row.CloseAction == TeamsCloseAction.Nothing || (link.CloseHandledAt is { } handled && (link.CloseError is null || handled > retry)))
            {
                continue;
            }

            link.CloseHandledAt = now;
            link.CloseError = null;
            try
            {
                if (row.CloseAction == TeamsCloseAction.Delete)
                {
                    await DeleteRemoteAsync(row, link, cancellationToken);
                    db.Remove(link);
                    Audit.Record("teams.bauakte_deleted_in_teams", null, "bauakte", link.BauakteId.ToString(),
                        new Dictionary<string, string?> { ["kind"] = link.Kind.ToString(), ["name"] = link.DisplayName, ["trigger"] = "closed" });
                }
                else if (link.Kind == TeamsLinkKind.Channel && link.ArchivedAt is null)
                {
                    await ArchiveAsync(row, link, cancellationToken);
                    Audit.Record("teams.bauakte_archived_in_teams", null, "bauakte", link.BauakteId.ToString(),
                        new Dictionary<string, string?> { ["name"] = link.DisplayName, ["trigger"] = "closed" });
                }
            }
            catch (GraphException exception)
            {
                link.CloseError = exception.Message;
                logger.LogWarning("Teams-Abschluss für Bauakte {Number} ({BauakteId}) nicht ausgeführt: {Code} – {Message}", item.Number, link.BauakteId,
                    exception.Code, exception.Message);
            }

            await db.SaveChangesAsync(cancellationToken);
            if (link.CloseError?.StartsWith("Microsoft 365 ist vom Server aus nicht erreichbar", StringComparison.Ordinal) == true)
            {
                break;
            }
        }
    }

    // ---------- Einzel- und Gruppenchats ----------

    /// <summary>Microsoft-Konten aller aktiven Benutzer (bzw. der angegebenen) für „Chat in Teams“.</summary>
    public async Task<List<TeamsPerson>> PeopleAsync(TeamsSettingsRow row, IReadOnlyCollection<Guid>? userIds, CancellationToken cancellationToken)
    {
        var query = db.Set<User>().AsNoTracking().Where(u => u.IsActive);
        if (userIds is not null)
        {
            query = query.Where(u => userIds.Contains(u.Id));
        }

        var users = await query.OrderBy(u => u.DisplayName).Select(u => new { u.Id, u.DisplayName, u.Email }).ToListAsync(cancellationToken);
        var ids = users.Select(u => u.Id).ToList();
        var links = await db.Set<TeamsUserLink>().AsNoTracking().Where(l => ids.Contains(l.UserId)).ToDictionaryAsync(l => l.UserId, cancellationToken);
        return users.Select(u =>
        {
            var link = links.GetValueOrDefault(u.Id);
            if (!string.IsNullOrWhiteSpace(link?.UserPrincipalName))
            {
                return new TeamsPerson(u.Id, u.DisplayName, link.UserPrincipalName, false, null);
            }

            if (link is not null && link.CheckedAt != default && link.AadUserId is null && row.GraphConfigured)
            {
                return new TeamsPerson(u.Id, u.DisplayName, null, false,
                    link.Error ?? "Kein Microsoft-Konto zugeordnet (Einstellungen › Integrationen › Microsoft Teams › Benutzer).");
            }

            var email = u.Email?.Trim();
            return string.IsNullOrEmpty(email) || !email.Contains('@', StringComparison.Ordinal)
                ? new TeamsPerson(u.Id, u.DisplayName, null, false, "Keine E-Mail-Adresse bzw. kein Microsoft-Konto hinterlegt.")
                : new TeamsPerson(u.Id, u.DisplayName, email, true, null);
        }).ToList();
    }

    /// <summary>
    /// Einzel- bzw. Gruppenchat mit den angegebenen Personen (ohne den Aufrufer). Standard: Teams-Deep-Link
    /// (<c>/l/chat/0/0?users=…</c>, keine Graph-Rechte nötig). Mit „Chats über Microsoft Graph“ werden Einzelchats über
    /// <c>POST /chats</c> angelegt (Microsoft liefert einen vorhandenen 1:1-Chat zurück) – das ergibt einen stabilen Link.
    /// </summary>
    public async Task<TeamsChatLinkResponse> ChatLinkAsync(TeamsSettingsRow row, Guid caller, IReadOnlyCollection<Guid> userIds, string? topic, string? message,
        CancellationToken cancellationToken)
    {
        var wanted = userIds.Where(id => id != caller).Distinct().ToList();
        var people = await PeopleAsync(row, wanted, cancellationToken);
        var missing = people.Where(p => p.Upn is null).Select(p => new TeamsChatMissing(p.UserId, p.Name, p.Reason ?? "Kein Microsoft-Konto.")).ToList();
        foreach (var id in wanted.Where(id => people.All(p => p.UserId != id)))
        {
            missing.Add(new TeamsChatMissing(id, "Unbekannt", "Benutzer nicht gefunden oder deaktiviert."));
        }

        var included = people.Where(p => p.Upn is not null).ToList();
        if (included.Count == 0)
        {
            throw new GraphException("NO_PARTICIPANTS", missing.Count == 1
                ? $"Für {missing[0].Name} ist kein Microsoft-Konto bekannt: {missing[0].Reason}"
                : "Für keine der ausgewählten Personen ist ein Microsoft-Konto bekannt. Bitte Konten zuordnen (Einstellungen › Integrationen › Microsoft Teams › Benutzer).");
        }

        var cleanTopic = included.Count > 1 && !string.IsNullOrWhiteSpace(topic) ? Clip(topic.Trim(), 250) : null;
        if (row.ChatsViaGraph && row.GraphConfigured && included.Count == 1)
        {
            var me = await db.Set<TeamsUserLink>().AsNoTracking().Where(l => l.UserId == caller).Select(l => l.AadUserId).SingleOrDefaultAsync(cancellationToken);
            var other = await db.Set<TeamsUserLink>().AsNoTracking().Where(l => l.UserId == included[0].UserId).Select(l => l.AadUserId)
                .SingleOrDefaultAsync(cancellationToken);
            if (me is not null && other is not null)
            {
                var token = await TokenAsync(row, cancellationToken);
                var chat = await graph.PostAsync(token, "chats", new JsonObject
                {
                    ["chatType"] = "oneOnOne",
                    ["members"] = new JsonArray(Member(me, owner: true, history: false), Member(other, owner: true, history: false)),
                }, "Chat.Create", cancellationToken);
                if (GraphClient.Str(chat?["id"]) is { } chatId)
                {
                    var url = GraphClient.Str(chat?["webUrl"]) ?? ChatUrl(chatId, row.TenantId);
                    return new TeamsChatLinkResponse(url, AppUrl(url), true, included.Select(p => p.Name).ToList(), missing);
                }
            }
        }

        var deepLink = ChatDeepLink(included.Select(p => p.Upn!), cleanTopic, message);
        return new TeamsChatLinkResponse(deepLink, AppUrl(deepLink), false, included.Select(p => p.Name).ToList(), missing);
    }

    /// <summary>
    /// Teams-Deep-Link für einen neuen bzw. vorhandenen Chat: <c>https://teams.microsoft.com/l/chat/0/0?users=a@x,b@x&amp;topicName=…&amp;message=…</c>.
    /// Ein Empfänger: Einzelchat (vorhandener wird geöffnet); mehrere: Gruppenchat (Thema nur dann).
    /// </summary>
    public static string ChatDeepLink(IEnumerable<string> upns, string? topic, string? message)
    {
        var users = string.Join(",", upns.Select(u => Uri.EscapeDataString(u.Trim()).Replace("%40", "@", StringComparison.Ordinal)));
        var url = $"https://teams.microsoft.com/l/chat/0/0?users={users}";
        if (!string.IsNullOrWhiteSpace(topic) && users.Contains(',', StringComparison.Ordinal))
        {
            url += "&topicName=" + Uri.EscapeDataString(Clip(topic.Trim(), 250));
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            url += "&message=" + Uri.EscapeDataString(Clip(message.Trim(), 1000));
        }

        return url;
    }

    /// <summary>Thema des Teamchats einer Bauakte.</summary>
    public static string BauakteTopic(string? number, string title) => Clip($"Bauakte {number} {title}".Replace("  ", " ", StringComparison.Ordinal).Trim(), 250);

    // ---------- Nachricht in die Unterhaltung ----------

    /// <summary>Kurze Nachricht aus EstrichONE in den verknüpften Kanal bzw. Chat (Dienstkonto oder Webhook).</summary>
    public async Task PostMessageAsync(TeamsSettingsRow row, TeamsBauakteLink link, Bauakte bauakte, string author, string text,
        CancellationToken cancellationToken)
    {
        var card = new JsonObject
        {
            ["type"] = "AdaptiveCard", ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json", ["version"] = "1.4",
            ["body"] = new JsonArray(
                new JsonObject { ["type"] = "TextBlock", ["text"] = $"{author} schreibt aus EstrichONE", ["weight"] = "Bolder", ["wrap"] = true },
                new JsonObject { ["type"] = "TextBlock", ["text"] = Clip(text, 2000), ["wrap"] = true },
                new JsonObject
                {
                    ["type"] = "FactSet",
                    ["facts"] = new JsonArray(new JsonObject { ["title"] = "Bauakte", ["value"] = Clip($"{bauakte.Number} {bauakte.Title}".Trim(), 200) }),
                }),
        };
        if (PublicUrl is { } publicUrl)
        {
            card["actions"] = new JsonArray(new JsonObject
            {
                ["type"] = "Action.OpenUrl", ["title"] = "In EstrichONE öffnen", ["url"] = new Uri(publicUrl, $"/bauakten/{bauakte.Id}").ToString(),
            });
        }

        switch (row.PostVia)
        {
            case TeamsPostVia.Webhook:
                var url = Unprotect(link.ProtectedWebhookUrl) ?? Unprotect(row.ProtectedWebhookUrl)
                    ?? throw new GraphException("NO_POST", "Es ist keine Webhook-URL hinterlegt (Einstellungen › Microsoft Teams › Nachrichten).");
                await graph.PostWebhookAsync(url, card, cancellationToken);
                break;
            case TeamsPostVia.Graph:
                await PostViaGraphAsync(row, link, card, cancellationToken);
                break;
            default:
                throw new GraphException("NO_POST",
                    "Nachrichten aus EstrichONE sind nicht eingerichtet (Einstellungen › Microsoft Teams › Nachrichten: Webhook oder Dienstkonto).");
        }

        link.LastPostAt = clock.GetUtcNow();
        link.LastPostError = null;
    }
}
