// Integrationstests laufen unter Linux (CI); kurze Aufrufe ohne Abbruch-Token.
#pragma warning disable xUnit1051, CA1416
using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Mail;
using EstrichOne.Api.Modules.Mail.Exports;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Mail;

public sealed class MailTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "geheim-smtp-123";
    private ApiFactory _factory = null!;
    private SmtpSink _smtp = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        _smtp = new SmtpSink("buero@firma.de", Password);
        _admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _smtp.DisposeAsync();
        await _factory.DisposeAsync();
    }

    private Task<HttpResponseMessage> SaveAsync(string? password = Password, int? port = null) =>
        _admin.PutAsJsonAsync("/api/v1/mail/settings", new
        {
            enabled = true, host = "127.0.0.1", port = port ?? _smtp.Port, security = "none", userName = "buero@firma.de", password,
            clearPassword = false, fromAddress = "buero@firma.de", fromName = "Estrichbau Test", replyTo = (string?)null,
        });

    [Fact]
    public async Task Settings_StorePasswordEncrypted_TestMailArrives()
    {
        using var saved = await SaveAsync();
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = (await saved.Content.ReadFromJsonAsync<MailSettingsDto>(TestJson.Options))!;
        settings.HasPassword.ShouldBeTrue();
        settings.Configured.ShouldBeTrue();
        (await saved.Content.ReadAsStringAsync()).ShouldNotContain(Password);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var account = await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<MailAccount>().SingleAsync();
            account.ProtectedPassword.ShouldNotBeNull();
            account.ProtectedPassword.ShouldNotContain(Password);
        }

        // Speichern ohne Passwort behält es.
        using var again = await SaveAsync(password: null);
        (await again.Content.ReadFromJsonAsync<MailSettingsDto>(TestJson.Options))!.HasPassword.ShouldBeTrue();

        using var test = await _admin.PostAsJsonAsync("/api/v1/mail/test", new { to = "chef@firma.de" });
        var result = (await test.Content.ReadFromJsonAsync<TestMailResult>(TestJson.Options))!;
        result.Error.ShouldBeNull();
        result.Ok.ShouldBeTrue();
        _smtp.Messages.Single().To.ShouldBe(["chef@firma.de"]);
        _smtp.Messages.Single().Data.ShouldContain("Subject: Testmail von EstrichONE");

        var log = (await _admin.GetFromJsonAsync<List<MailLogItem>>("/api/v1/mail/log", TestJson.Options))!;
        log.Single().Status.ShouldBe(MailStatus.Sent);
    }

    [Fact]
    public async Task WrongPassword_OrUnreachableServer_GiveClearReason()
    {
        using var _ = await SaveAsync(password: "falsch");
        using var test = await _admin.PostAsJsonAsync("/api/v1/mail/test", new { to = "chef@firma.de" });
        var result = (await test.Content.ReadFromJsonAsync<TestMailResult>(TestJson.Options))!;
        result.Ok.ShouldBeFalse();
        result.Error!.ShouldContain("Anmeldung am Mailserver fehlgeschlagen");

        using var __ = await SaveAsync(port: 1);
        using var unreachable = await _admin.PostAsJsonAsync("/api/v1/mail/test", new { to = "chef@firma.de" });
        (await unreachable.Content.ReadFromJsonAsync<TestMailResult>(TestJson.Options))!.Error!.ShouldContain("nicht erreichbar");
    }

    [Fact]
    public async Task OtherModules_QueueTemplates_WorkerSends_WithAttachment()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var mail = scope.ServiceProvider.GetRequiredService<IMailService>();
        (await mail.QueueTemplateAsync(MailTemplateKeys.UserWelcome, "neu@firma.de", new Dictionary<string, string?>())).ShouldBeNull(); // nicht eingerichtet

        using var _ = await SaveAsync();
        // Benutzerbrief per Mail ist in den Benachrichtigungs-Einstellungen standardmäßig aus.
        (await mail.QueueTemplateAsync(MailTemplateKeys.UserWelcome, "neu@firma.de", new Dictionary<string, string?>())).ShouldBeNull();

        using var custom = await _admin.PutAsJsonAsync($"/api/v1/mail/templates/{MailTemplateKeys.ApprovalRequested}",
            new { subject = "Willkommen {{name}}", body = "Dokument {{dokument}} bei {{firma}}" });
        custom.StatusCode.ShouldBe(HttpStatusCode.OK);

        var id = await mail.QueueTemplateAsync(MailTemplateKeys.ApprovalRequested, "neu@firma.de",
            new Dictionary<string, string?> { ["name"] = "Max Huber", ["dokument"] = "Plan EG" }, "user:1",
            [new MailAttachment("Zugangsbrief.pdf", "application/pdf", "%PDF-1.4"u8.ToArray())]);
        id.ShouldNotBeNull();

        (await _smtp.WaitForAsync(1, TimeSpan.FromSeconds(20))).ShouldBe(1);
        var message = _smtp.Messages.Single().Data;
        message.ShouldContain("Subject: Willkommen Max Huber");
        message.ShouldContain("Zugangsbrief.pdf");

        var log = await WaitForStatusAsync(id.Value, MailStatus.Sent);
        log.AttachmentNames.ShouldBe("Zugangsbrief.pdf");
        log.RelatedEntity.ShouldBe("user:1");

        using var reset = await _admin.DeleteAsync(new Uri($"/api/v1/mail/templates/{MailTemplateKeys.ApprovalRequested}", UriKind.Relative));
        (await reset.Content.ReadFromJsonAsync<MailTemplateDto>(TestJson.Options))!.Customized.ShouldBeFalse();
    }

    private async Task<MailLogItem> WaitForStatusAsync(Guid id, MailStatus status)
    {
        for (var i = 0; i < 100; i++)
        {
            var log = (await _admin.GetFromJsonAsync<List<MailLogItem>>("/api/v1/mail/log", TestJson.Options))!.Single(m => m.Id == id);
            if (log.Status == status)
            {
                return log;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Mail {id} nicht im Status {status}.");
    }
}
