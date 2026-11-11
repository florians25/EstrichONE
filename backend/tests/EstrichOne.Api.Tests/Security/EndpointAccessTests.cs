using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Security;

/// <summary>
/// Zugriffskontrolle über ALLE Endpunkte (aus der Routentabelle, also auch die nicht in OpenAPI beschriebenen):
/// ohne Anmeldung 401, mit einem Konto ohne Rollen 403 – außer den ausdrücklich freigegebenen und begründeten
/// Ausnahmen unten. Neue Endpunkte ohne Recht fallen hier auf. Dazu ein IDOR-Test: Eine Baustellenkraft erreicht über
/// fremde IDs nichts aus Bauakten, Dateien, Chats, Tickets und Benachrichtigungen anderer.
/// </summary>
public sealed class EndpointAccessTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private ApiFactory _factory = null!;

    /// <summary>Ohne Anmeldung erreichbar. „token“: eigenes Geheimnis statt Sitzung (muss ohne es abgelehnt werden).</summary>
    private static readonly Dictionary<string, string> Anonymous = new()
    {
        ["GET /health/live"] = "public", ["GET /health/ready"] = "public", ["GET /api/openapi/{documentName}.json"] = "public",
        ["GET /api/v1/server-info"] = "public",
        // App-Updates: Prüfung, Neuerungen und Android-Paket auch für gesperrte, abgemeldete oder zu alte Apps (docs/app-updates.md).
        ["GET /api/v1/app-updates/check"] = "public", ["GET /api/v1/app-updates/release-notes"] = "public",
        ["GET /api/v1/app-updates/android/package"] = "public", ["HEAD /api/v1/app-updates/android/package"] = "public",
        ["POST /api/v1/auth/login"] = "public", ["POST /api/v1/auth/refresh"] = "token",
        ["POST /api/v1/auth/web/login"] = "public", ["POST /api/v1/auth/web/refresh"] = "token", ["POST /api/v1/auth/web/logout"] = "public",
        ["GET /api/v1/company/logo/"] = "public", // Anmeldeseite zeigt das Firmenlogo
        ["GET /api/v1/public/acceptances/{token}/"] = "token", ["GET /api/v1/public/acceptances/{token}/photos/{fileId:guid}"] = "token",
        ["GET /api/v1/public/acceptances/{token}/signature"] = "token", ["POST /api/v1/public/acceptances/{token}/decision"] = "token",
        ["POST /api/v1/tickets/device"] = "token", // Gerät ohne Konto meldet ein Problem (Geräteschlüssel, Rate-Limit)
        ["GET /api/v1/setup/status"] = "public", ["POST /api/v1/setup/"] = "token", // Einrichtungscode, nur einmal
        ["GET /api/v1/system/notices/"] = "public", ["PUT /api/v1/system/notices/"] = "token", // Hosting-Konsole
        ["POST /api/v1/backup/mirror/pair"] = "token", ["POST /api/v1/backup/mirror/checkin"] = "token", ["POST /api/v1/backup/mirror/report"] = "token",
        ["GET /api/v1/backup/mirror/repo/config"] = "token", ["GET /api/v1/backup/mirror/repo/{type}/"] = "token",
        ["GET /api/v1/backup/mirror/repo/{type}/{name}"] = "token",
    };

    /// <summary>
    /// Angemeldet ohne jedes Recht erreichbar: eigenes Konto, eigene Daten (Benachrichtigungen, Tickets, Sitzungen),
    /// Lesedaten für die Oberfläche – oder der Handler prüft Objektzugriff selbst (Bauakte/Datei: 404 ohne Sichtbarkeit).
    /// </summary>
    private static readonly HashSet<string> AnyUser =
    [
        "GET /api/v1/directory/users", // nur Name und Status (Zuständige auswählen), keine Kontaktdaten
        "GET /api/v1/bauakten/{id:guid}/history", "GET /api/v1/files/{id:guid}", "GET /api/v1/files/{id:guid}/content", // IBauakteAccess
        "GET /api/v1/system/health-banner", "GET /api/v1/settings/", "GET /api/v1/settings/modules/", "GET /api/v1/company/", "GET /api/v1/company/logo/info",
        "POST /api/v1/auth/logout", "GET /api/v1/auth/me", "POST /api/v1/auth/change-password", "GET /api/v1/access/me/permissions",
        "GET /api/v1/auth/two-factor/", "POST /api/v1/auth/two-factor/setup", "POST /api/v1/auth/two-factor/enable",
        "POST /api/v1/auth/two-factor/recovery-codes", "POST /api/v1/auth/two-factor/disable",
        "GET /api/v1/auth/sessions/", "DELETE /api/v1/auth/sessions/{id:guid}", "POST /api/v1/auth/sessions/revoke-others",
        "POST /api/v1/devices/register", "POST /api/v1/devices/attach", "GET /api/v1/devices/me",
        "POST /api/v1/tickets/", "GET /api/v1/tickets/unread", "POST /api/v1/tickets/{id:guid}/seen", "GET /api/v1/tickets/mine",
        "GET /api/v1/tickets/{id:guid}", "POST /api/v1/tickets/{id:guid}/comments", "POST /api/v1/tickets/{id:guid}/reopen",
        "POST /api/v1/tickets/{id:guid}/rating", // nur der Melder des Tickets (sonst 404)
        // Melden mit Unterkategorie und Wissensdatenbank: veröffentlichte Artikel für alle, Entwürfe nur für die Bearbeitung.
        "GET /api/v1/helpdesk/options", "GET /api/v1/helpdesk/articles", "GET /api/v1/helpdesk/articles/suggest", "GET /api/v1/helpdesk/articles/{id:guid}",
        "POST /api/v1/helpdesk/articles/{id:guid}/helpful",
        // Fehler melden / Funktion wünschen: jeder darf einreichen und sieht nur eigene Meldungen (fremde: 404).
        "POST /api/v1/feedback/", "GET /api/v1/feedback/mine", "GET /api/v1/feedback/{id:guid}", "POST /api/v1/feedback/{id:guid}/comments",
        "POST /api/v1/sync/push", "GET /api/v1/sync/pull", "POST /api/v1/sync/snapshot", // je Operation/Objekt geprüft
        "GET /api/v1/notifications/", "GET /api/v1/notifications/unread-count", "POST /api/v1/notifications/{id:guid}/read",
        "POST /api/v1/notifications/{id:guid}/unread", "POST /api/v1/notifications/read-all", "DELETE /api/v1/notifications/{id:guid}",
        "GET /api/v1/notifications/push-config", "GET /api/v1/notifications/devices", "POST /api/v1/notifications/devices",
        "DELETE /api/v1/notifications/devices/{id:guid}", "DELETE /api/v1/notifications/devices", "GET /api/v1/notifications/preferences",
        "PUT /api/v1/notifications/preferences", "POST /api/v1/notifications/test",
        "GET /api/v1/chat/status",
    ];

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
    {
        ["EstrichOne:Authentication:RequestsPerMinutePerAddress"] = "100000",
    });

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private List<(string Key, RouteEndpoint Endpoint, string Method)> Routes() =>
        [.. _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(m => ($"{m} {e.RoutePattern.RawText}", e, m)))];

    [Fact]
    public async Task EveryEndpoint_WithoutLogin_Returns401_ExceptAllowlist()
    {
        using var anonymous = _factory.CreateClient();
        var routes = Routes();
        routes.Count.ShouldBeGreaterThan(300);
        var problems = new List<string>();
        foreach (var (key, endpoint, method) in routes)
        {
            // Neue anonyme Endpunkte müssen hier bewusst eingetragen werden.
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null && !Anonymous.ContainsKey(key))
            {
                problems.Add($"{key}: AllowAnonymous, aber nicht in der Liste");
            }

            using var response = await anonymous.SendAsync(Request(method, endpoint, _ => Guid.CreateVersion7()), Ct);
            var status = (int)response.StatusCode;
            var expected = Anonymous.GetValueOrDefault(key);
            var ok = expected switch
            {
                null => status == 401,
                "token" => status is 400 or 401 or 403 or 404 or 422,
                _ => status < 500,
            };
            if (!ok)
            {
                problems.Add($"{key}: {status}");
            }
        }

        problems.ShouldBeEmpty();
        Anonymous.Keys.Except(routes.Select(r => r.Key)).ShouldBeEmpty("Veraltete Einträge in der Liste");
    }

    [Fact]
    public async Task EveryEndpoint_WithAccountWithoutRoles_Returns403_ExceptAllowlist()
    {
        using var user = await _factory.CreateClientAsAsync("ohne.rechte");
        var routes = Routes();
        var problems = new List<string>();
        foreach (var (key, endpoint, method) in routes.Where(r => !Anonymous.ContainsKey(r.Key)))
        {
            if (key.Contains("logout", StringComparison.Ordinal) || key.Contains("revoke-others", StringComparison.Ordinal))
            {
                continue; // würde die eigene Sitzung beenden
            }

            using var response = await user.SendAsync(Request(method, endpoint, _ => Guid.CreateVersion7()), Ct);
            var status = (int)response.StatusCode;
            if (AnyUser.Contains(key) ? status >= 500 || status == 401 : status != 403)
            {
                problems.Add($"{key}: {status}");
            }
        }

        problems.ShouldBeEmpty();
        AnyUser.Except(routes.Select(r => r.Key)).ShouldBeEmpty("Veraltete Einträge in der Liste");
    }

    [Fact]
    public async Task SiteWorker_WithForeignIds_ReachesNothingOfOthers()
    {
        var setup = await ForeignObjectsAsync();
        using var attacker = await _factory.CreateClientAsAsync("angreifer", StandardRoles.SiteWorkerId);
        var problems = new List<string>();
        foreach (var (key, endpoint, method) in Routes().Where(r => !Anonymous.ContainsKey(r.Key)))
        {
            var ids = setup.IdsFor(endpoint.RoutePattern.RawText!);
            if (ids is null || key.Contains("logout", StringComparison.Ordinal) || key.Contains("revoke-others", StringComparison.Ordinal))
            {
                continue;
            }

            using var request = Request(method, endpoint, ids);
            if (key.EndsWith("/files/archive", StringComparison.Ordinal))
            {
                request.RequestUri = new Uri($"{request.RequestUri}?ids={setup.File}", UriKind.Relative);
            }

            using var response = await attacker.SendAsync(request, Ct);
            var status = (int)response.StatusCode;
            // Lesen: nur 403/404. Schreiben: nie Erfolg (400/422 = Eingabe abgelehnt, ohne Wirkung – geprüft unten).
            var allowed = method == "GET" ? status is 403 or 404 : status is 400 or 403 or 404 or 405 or 409 or 415 or 422;
            if (!allowed)
            {
                problems.Add($"{key}: {status}");
            }
        }

        // Gezielte, gültige Schreibversuche auf fremde Objekte.
        async Task Expect(HttpResponseMessage response, string what)
        {
            using (response)
            {
                if ((int)response.StatusCode is not (403 or 404))
                {
                    problems.Add($"{what}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
                }
            }
        }

        await Expect(await attacker.PutAsJsonAsync($"/api/v1/bauakten/{setup.Bauakte}", new { title = "Übernommen", version = 1 }, Ct), "Bauakte ändern");
        await Expect(await attacker.PostAsync(new Uri($"/api/v1/bauakten/{setup.Bauakte}/members/me", UriKind.Relative), null, Ct), "Selbst zuweisen");
        await Expect(await attacker.PutAsJsonAsync($"/api/v1/bauakten/{setup.Bauakte}/members", new { userIds = new[] { ForeignObjects.AttackerId(_factory) } }, Ct), "Mitglieder setzen");
        await Expect(await attacker.PostAsJsonAsync($"/api/v1/bauakten/{setup.Bauakte}/notes", new { id = Guid.CreateVersion7(), text = "x" }, Ct), "Notiz schreiben");
        await Expect(await attacker.DeleteAsync(new Uri($"/api/v1/files/{setup.File}", UriKind.Relative), Ct), "Datei löschen");
        await Expect(await attacker.PostAsJsonAsync($"/api/v1/chat/conversations/{setup.Conversation}/messages", new { text = "x", clientMessageId = Guid.CreateVersion7() }, Ct), "Chat schreiben");
        await Expect(await attacker.GetAsync(new Uri($"/api/v1/chat/conversations/{setup.Conversation}/messages", UriKind.Relative), Ct), "Chat lesen");
        await Expect(await attacker.PostAsJsonAsync($"/api/v1/tickets/{setup.Ticket}/comments", new { id = Guid.CreateVersion7(), text = "x" }, Ct), "Ticket kommentieren");
        await Expect(await attacker.PostAsJsonAsync($"/api/v1/feedback/{setup.Feedback}/comments", new { text = "x", isInternal = false }, Ct), "Fremde Meldung beantworten");
        await Expect(await attacker.PatchAsJsonAsync($"/api/v1/feedback/{setup.Feedback}", new { status = "rejected" }, Ct), "Fremde Meldung ändern");
        await Expect(await attacker.PostAsync(new Uri($"/api/v1/notifications/{setup.Notification}/read", UriKind.Relative), null, Ct), "Fremde Benachrichtigung");
        await Expect(await attacker.PatchAsJsonAsync($"/api/v1/work-items/{setup.WorkItem}", new { status = "done" }, Ct), "Fremde Aufgabe");

        // Über die Synchronisation kommt nichts Fremdes.
        var pulled = await attacker.GetStringAsync(new Uri("/api/v1/sync/pull?limit=500&includeScope=true", UriKind.Relative), Ct);
        pulled.ShouldNotContain(setup.Bauakte.ToString());
        pulled.ShouldNotContain(setup.File.ToString());

        problems.ShouldBeEmpty();

        // Nichts wurde verändert.
        using var admin = setup.Admin;
        var bauakte = (await admin.GetFromJsonAsync<BauakteDetail>($"/api/v1/bauakten/{setup.Bauakte}", TestJson.Options, Ct))!;
        bauakte.Title.ShouldBe("Fremde Baustelle");
        (await admin.GetAsync(new Uri($"/api/v1/files/{setup.File}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var members = await admin.GetStringAsync(new Uri($"/api/v1/bauakten/{setup.Bauakte}/members", UriKind.Relative), Ct);
        members.ShouldNotContain("angreifer", Case.Insensitive);
    }

    private sealed record ForeignObjects(HttpClient Admin, Guid Bauakte, Guid File, Guid Conversation, Guid Message, Guid Attachment,
        Guid Ticket, Guid Notification, Guid WorkItem, Guid Note, Guid Feedback)
    {
        public static Guid AttackerId(ApiFactory factory)
        {
            using var scope = factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<EstrichOne.Api.Infrastructure.Persistence.EstrichOneDbContext>()
                .Set<EstrichOne.Api.Modules.Identity.Exports.User>().Single(u => u.UserName == "angreifer").Id;
        }

        /// <summary>Fremde IDs je Routenparameter; null = Route ohne Objektbezug (nicht Teil dieses Tests).</summary>
        public Func<string, Guid>? IdsFor(string route)
        {
            Guid? Main() => route switch
            {
                _ when route.Contains("/bauakten/{", StringComparison.Ordinal) => Bauakte,
                _ when route.StartsWith("/api/v1/files/{", StringComparison.Ordinal) => File,
                _ when route.Contains("/chat/conversations/{", StringComparison.Ordinal) => Conversation,
                _ when route.Contains("/chat/messages/{", StringComparison.Ordinal) => Message,
                _ when route.Contains("/chat/attachments/{", StringComparison.Ordinal) => Attachment,
                _ when route.StartsWith("/api/v1/tickets/{", StringComparison.Ordinal) => Ticket,
                _ when route.StartsWith("/api/v1/feedback/{", StringComparison.Ordinal) => Feedback,
                _ when route.StartsWith("/api/v1/notifications/{", StringComparison.Ordinal) => Notification,
                _ when route.StartsWith("/api/v1/work-items/{", StringComparison.Ordinal) => WorkItem,
                _ when route.Contains("{bauakteId", StringComparison.Ordinal) => Bauakte,
                _ when route.Contains("{fileId", StringComparison.Ordinal) => File,
                _ => null,
            };

            return Main() is { } main
                ? name => name switch
                {
                    "noteId" => Note,
                    "bauakteId" => Bauakte,
                    "fileId" => File,
                    "id" => main,
                    _ => Guid.CreateVersion7(),
                }
                : null;
        }
    }

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>Administrator und Büro legen Objekte an, an denen die Baustellenkraft „angreifer“ nicht beteiligt ist.</summary>
    private async Task<ForeignObjects> ForeignObjectsAsync()
    {
        var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        var officeUser = await _factory.CreateUserAsync("buero", roleIds: StandardRoles.OfficeId);
        (await admin.PutAsJsonAsync("/api/v1/chat/admin/settings", new { enabled = true, retentionDays = 90, showPreview = false }, Ct)).EnsureSuccessStatusCode();

        using var created = await admin.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title = "Fremde Baustelle", city = "Landshut" }, Ct);
        created.EnsureSuccessStatusCode();
        var bauakte = (await created.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!.Id;

        var file = Guid.CreateVersion7();
        (await admin.PostAsJsonAsync("/api/v1/files", new
        {
            id = file, bauakteId = bauakte, kind = "photo", originalName = "a.png", contentType = "image/png", sizeBytes = (long)Png.Length,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(Png)),
        }, Ct)).EnsureSuccessStatusCode();
        using (var body = new ByteArrayContent(Png))
        {
            body.Headers.ContentType = new("application/octet-stream");
            (await admin.PutAsync(new Uri($"/api/v1/files/{file}/content", UriKind.Relative), body, Ct)).EnsureSuccessStatusCode();
        }

        var note = Guid.CreateVersion7();
        (await admin.PostAsJsonAsync($"/api/v1/bauakten/{bauakte}/notes", new { id = note, text = "intern" }, Ct)).EnsureSuccessStatusCode();
        var workItem = Guid.CreateVersion7();
        (await admin.PostAsJsonAsync("/api/v1/work-items", new { id = workItem, bauakteId = bauakte, kind = "task", title = "Intern" }, Ct)).EnsureSuccessStatusCode();

        using var direct = await admin.PostAsJsonAsync("/api/v1/chat/conversations/direct", new { userId = officeUser.Id }, Ct);
        direct.EnsureSuccessStatusCode();
        var conversation = (await direct.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>(TestJson.Options, Ct))!["id"]!.GetValue<Guid>();
        using var sent = await admin.PostAsJsonAsync($"/api/v1/chat/conversations/{conversation}/messages", new { text = "vertraulich", clientMessageId = Guid.CreateVersion7() }, Ct);
        sent.EnsureSuccessStatusCode();
        var message = (await sent.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>(TestJson.Options, Ct))!["id"]!.GetValue<Guid>();
        using var jpeg = new ByteArrayContent(NetVips.Image.Black(8, 8).JpegsaveBuffer());
        jpeg.Headers.ContentType = new("image/jpeg");
        using var attached = await admin.PostAsync(new Uri($"/api/v1/chat/conversations/{conversation}/attachments?fileName=a.jpg", UriKind.Relative), jpeg, Ct);
        attached.EnsureSuccessStatusCode();
        var attachment = (await attached.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>(TestJson.Options, Ct))!["id"]!.GetValue<Guid>();

        var ticket = Guid.CreateVersion7();
        (await admin.PostAsJsonAsync("/api/v1/tickets", new { id = ticket, title = "Drucker", category = "officeIt", source = "web" }, Ct)).EnsureSuccessStatusCode();
        var feedback = Guid.CreateVersion7();
        (await admin.PostAsJsonAsync("/api/v1/feedback", new
        {
            id = feedback, kind = "feature", title = "Vertraulicher Wunsch", area = "Planung", problem = "p", desiredSolution = "l", impact = "low",
        }, Ct)).EnsureSuccessStatusCode();
        (await admin.PostAsync(new Uri("/api/v1/notifications/test", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();
        var inbox = await admin.GetFromJsonAsync<System.Text.Json.Nodes.JsonNode>("/api/v1/notifications", TestJson.Options, Ct);
        var notification = (inbox!["items"] ?? inbox).AsArray()[0]!["id"]!.GetValue<Guid>();

        return new ForeignObjects(admin, bauakte, file, conversation, message, attachment, ticket, notification, workItem, note, feedback);
    }

    private static HttpRequestMessage Request(string method, RouteEndpoint endpoint, Func<string, Guid> ids)
    {
        var path = "/" + string.Join('/', endpoint.RoutePattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternSeparatorPart separator => separator.Content,
            RoutePatternParameterPart parameter when parameter.ParameterPolicies.Any(p => p.Content == "guid") => ids(parameter.Name).ToString(),
            RoutePatternParameterPart parameter when parameter.Name is "day" => "2026-11-02",
            RoutePatternParameterPart => "x",
            _ => string.Empty,
        }))));
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is not ("GET" or "HEAD" or "DELETE"))
        {
            var accepts = endpoint.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes.FirstOrDefault(t => !t.Contains('*', StringComparison.Ordinal));
            request.Content = accepts is null or "application/json"
                ? new StringContent("{}", Encoding.UTF8, "application/json")
                : new ByteArrayContent([]) { Headers = { ContentType = new(accepts.Split(';')[0]) } };
        }

        return request;
    }
}
