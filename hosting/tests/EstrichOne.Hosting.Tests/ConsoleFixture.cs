using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EstrichOne.Hosting.Auth;
using EstrichOne.Hosting.Docker;
using EstrichOne.Hosting.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Hosting.Tests;

/// <summary>Startet die echte Konsole (TestServer, SQLite in einem Temp-Verzeichnis) mit simuliertem Docker.</summary>
public sealed class ConsoleApp : IAsyncDisposable
{
    public const string Password = "sehr-geheimes-passwort-1";
    private readonly TempDirectory temp;
    private readonly WebApplication app;

    private ConsoleApp(TempDirectory temp, WebApplication app)
    {
        this.temp = temp;
        this.app = app;
    }

    public string Root => temp.Path;

    public IServiceProvider Services => app.Services;

    public FakeDockerHost Docker => app.Services.GetRequiredService<FakeDockerHost>();

    public static async Task<ConsoleApp> StartAsync(string proxy = "caddy")
    {
        var temp = new TempDirectory();
        var args = new[]
        {
            "--Hosting:Docker=fake",
            $"--Hosting:Root={temp.Path}",
            $"--Hosting:TemplateDirectory={TestPaths.Templates}",
            "--Hosting:BaseDomain=hosting.test",
            "--Hosting:ConsoleDomain=konsole.hosting.test",
            $"--Hosting:Proxy={proxy}",
            "--Hosting:DisableStatusCollector=true",
            "--Logging:LogLevel:Default=Warning",
        };
        var app = HostingApp.Build(args, builder => builder.WebHost.UseTestServer());
        await DataSeeder.InitializeAsync(app.Services, CancellationToken.None);
        await app.StartAsync();
        var console = new ConsoleApp(temp, app);
        console.Docker.StepDelay = TimeSpan.Zero;
        return console;
    }

    /// <summary>Client mit Cookie-Speicher; Basisadresse https, damit das Secure-Cookie wie im Betrieb gilt.</summary>
    public HttpClient Client()
    {
        var handler = new CookieContainerHandler { InnerHandler = app.GetTestServer().CreateHandler() };
        return new HttpClient(handler) { BaseAddress = new Uri("https://konsole.hosting.test") };
    }

    public async Task<string> IssueSetupCodeAsync()
    {
        await using var scope = app.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<SetupCodeService>().IssueIfRequiredAsync(CancellationToken.None))!;
    }

    /// <summary>Ersteinrichtung und angemeldeter Client mit CSRF-Header.</summary>
    public async Task<HttpClient> AdminClientAsync()
    {
        var client = Client();
        var code = await IssueSetupCodeAsync();
        var response = await client.PostAsJsonAsync("/api/auth/setup", new { setupCode = code, userName = "admin", displayName = "Anna Admin", password = Password });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await AddCsrfAsync(client);
        return client;
    }

    public static async Task AddCsrfAsync(HttpClient client)
    {
        var state = await client.GetFromJsonAsync<JsonElement>("/api/auth/state");
        client.DefaultRequestHeaders.Remove(AuthEndpoints.CsrfHeader);
        client.DefaultRequestHeaders.Add(AuthEndpoints.CsrfHeader, state.GetProperty("csrfToken").GetString());
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        temp.Dispose();
    }
}
