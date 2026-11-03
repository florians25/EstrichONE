using System.Net;
using EstrichOne.Hosting.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Hosting.Tests;

/// <summary>Optionale IP-Allowlist der Konsole (docs/sicherheit.md, Betrieb).</summary>
public sealed class IpAllowlistTests
{
    [Fact]
    public void Leer_ErlaubtAlles()
    {
        var list = IpAllowlist.Parse("  ");
        list.Enabled.ShouldBeFalse();
        list.IsAllowed(IPAddress.Parse("198.51.100.9")).ShouldBeTrue();
    }

    [Fact]
    public void Netze_Einzeladressen_Loopback_UndIPv4ImIPv6()
    {
        var list = IpAllowlist.Parse("203.0.113.7, 10.0.0.0/8; 2001:db8::/32");
        list.IsAllowed(IPAddress.Parse("203.0.113.7")).ShouldBeTrue();
        list.IsAllowed(IPAddress.Parse("203.0.113.8")).ShouldBeFalse();
        list.IsAllowed(IPAddress.Parse("10.20.30.40")).ShouldBeTrue();
        list.IsAllowed(IPAddress.Parse("::ffff:10.1.2.3")).ShouldBeTrue();
        list.IsAllowed(IPAddress.Parse("2001:db8::1")).ShouldBeTrue();
        list.IsAllowed(IPAddress.Parse("2001:db9::1")).ShouldBeFalse();
        list.IsAllowed(IPAddress.Loopback).ShouldBeTrue();
        list.IsAllowed(null).ShouldBeFalse();
    }

    [Fact]
    public void UngueltigerEintrag_StopptDenStart()
    {
        Should.Throw<FormatException>(() => IpAllowlist.Parse("10.0.0.0/8, intern"));
    }

    [Theory]
    [InlineData("198.51.100.9", HttpStatusCode.Forbidden)]
    [InlineData("203.0.113.7", HttpStatusCode.OK)]
    public async Task Konsole_WeistFremdeAdressenAb_HealthBleibtErreichbar(string client, HttpStatusCode expected)
    {
        using var temp = new TempDirectory();
        var args = new[]
        {
            "--Hosting:Docker=fake", $"--Hosting:Root={temp.Path}", $"--Hosting:TemplateDirectory={TestPaths.Templates}",
            "--Hosting:BaseDomain=hosting.test", "--Hosting:ConsoleDomain=konsole.hosting.test", "--Hosting:DisableStatusCollector=true",
            "--Hosting:AllowedNetworks=203.0.113.0/24", "--Logging:LogLevel:Default=Warning",
        };
        await using var app = HostingApp.Build(args, builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<IStartupFilter>(new RemoteAddress(IPAddress.Parse(client)));
        });
        await DataSeeder.InitializeAsync(app.Services, CancellationToken.None);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var http = app.GetTestClient();
        http.BaseAddress = new Uri("https://konsole.hosting.test");

        var state = await http.GetAsync("/api/auth/state", TestContext.Current.CancellationToken);
        state.StatusCode.ShouldBe(expected);
        var page = await http.GetAsync("/", TestContext.Current.CancellationToken);
        (page.StatusCode == HttpStatusCode.Forbidden).ShouldBe(expected == HttpStatusCode.Forbidden);
        var health = await http.GetAsync("/health", TestContext.Current.CancellationToken);
        health.StatusCode.ShouldBe(HttpStatusCode.OK);
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>TestServer setzt keine Client-Adresse – hier wie eine echte Verbindung vorgeben.</summary>
    private sealed class RemoteAddress(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, Func<Task> inner) =>
            {
                context.Connection.RemoteIpAddress = address;
                return inner();
            });
            next(app);
        };
    }
}
