using EstrichOne.Api.Infrastructure.Hosting;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace EstrichOne.Api.Tests.Hosting;

public sealed class PublicAddressTests
{
    private static HttpRequest Request(string scheme, string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        return context.Request;
    }

    [Fact]
    public void Konfigurierte_Adresse_gilt() =>
        PublicAddress.Resolve(new EstrichOneOptions { PublicUrl = new Uri("https://estrichone.firma.de/") }, Request("http", "10.0.0.5:8181"))
            .ShouldBe("https://estrichone.firma.de");

    [Fact]
    public void Localhost_wird_durch_Anfrageadresse_ersetzt() =>
        PublicAddress.Resolve(new EstrichOneOptions { PublicUrl = new Uri("https://localhost") }, Request("http", "estrichone.firma.de"))
            .ShouldBe("https://estrichone.firma.de");

    [Fact]
    public void Ohne_Konfiguration_gilt_die_Anfrage() =>
        PublicAddress.Resolve(new EstrichOneOptions(), Request("https", "192.168.1.20:8143")).ShouldBe("https://192.168.1.20:8143");

    [Fact]
    public void Lokaler_Aufruf_behaelt_localhost() =>
        PublicAddress.Resolve(new EstrichOneOptions { PublicUrl = new Uri("https://localhost:8143") }, Request("https", "127.0.0.1:8143"))
            .ShouldBe("https://localhost:8143");
}
