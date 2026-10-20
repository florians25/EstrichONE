using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Infrastructure;
using EstrichOne.Hosting.Proxy;

namespace EstrichOne.Hosting.Tests;

public sealed class ProxyConfigTests
{
    private static HostingOptions Options(string tls = "acme") => new()
    {
        ConsoleDomain = "konsole.hosting.de",
        Tls = tls,
        AcmeEmail = "admin@hosting.de",
    };

    [Fact]
    public void Build_LeitetMandantenUndKonsoleWeiter()
    {
        var config = ProxyConfigBuilder.Build(Options(), [new ProxyRoute("mueller", "mueller.hosting.de", false)]);
        config.ShouldContain("email admin@hosting.de");
        config.ShouldContain("konsole.hosting.de {");
        config.ShouldContain("reverse_proxy estrichone-hosting-console:8080");
        config.ShouldContain("mueller.hosting.de {");
        config.ShouldContain("reverse_proxy estrichone-mueller-caddy-1:8181");
        config.ShouldContain("handle_errors 502 503 504");
    }

    [Fact]
    public void Build_GesperrterMandant_ZeigtWartungsseite()
    {
        var config = ProxyConfigBuilder.Build(Options(), [new ProxyRoute("mueller", "mueller.hosting.de", true)]);
        config.ShouldContain("Wartungsseite");
        config.ShouldContain("error 503");
        config.ShouldNotContain("estrichone-mueller-caddy-1");
    }

    [Fact]
    public void Build_VerwirftUngueltigeEintraege()
    {
        var config = ProxyConfigBuilder.Build(Options(),
        [
            new ProxyRoute("ok-firma", "ok.hosting.de", false),
            new ProxyRoute("../boese", "boese.de", false),
            new ProxyRoute("boese", "boese.de {\n\treverse_proxy evil:80\n}", false),
            new ProxyRoute("konsole-klau", "konsole.hosting.de", false),
        ]);
        config.ShouldContain("ok.hosting.de {");
        config.ShouldNotContain("evil");
        config.ShouldNotContain("boese");
        config.ShouldNotContain("konsole-klau");
    }

    [Theory]
    [InlineData("internal", "local_certs", "konsole.hosting.de {")]
    [InlineData("off", "auto_https off", "http://konsole.hosting.de {")]
    public void Build_TlsModi(string tls, string global, string site)
    {
        var config = ProxyConfigBuilder.Build(Options(tls), []);
        config.ShouldContain(global);
        config.ShouldContain(site);
    }

    [Theory]
    [InlineData(TenantState.Running, false)]
    [InlineData(TenantState.Failed, false)]
    [InlineData(TenantState.Locked, true)]
    [InlineData(TenantState.Stopped, true)]
    [InlineData(TenantState.Provisioning, true)]
    public void ShowsMaintenance_JeZustand(TenantState state, bool maintenance) =>
        ProxyConfigBuilder.ShowsMaintenance(state).ShouldBe(maintenance);

    [Fact]
    public void MaintenancePage_IstDeutschUndOhneSkripte()
    {
        var html = MaintenancePage.Build("data:image/svg+xml;base64,AAA=");
        html.ShouldContain("lang=\"de\"");
        html.ShouldContain("Vorübergehend nicht erreichbar");
        html.ShouldNotContain("<script");
    }
}
