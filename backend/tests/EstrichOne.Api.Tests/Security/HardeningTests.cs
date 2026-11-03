using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Http;
using EstrichOne.Api.Infrastructure.Media;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstrichOne.Api.Tests.Security;

/// <summary>Bildbomben, gesperrte Bildformate, Quellenangabe der Karte (XSS), Client-IP hinter Proxys, Log-Maskierung.</summary>
public sealed class HardeningTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>PNG-Kopf mit beliebiger Größe (gültige CRCs), ohne Bilddaten – wie eine Dekompressionsbombe.</summary>
    internal static byte[] PngHeader(int width, int height)
    {
        static byte[] Chunk(string type, byte[] data)
        {
            var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            var crc = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
            return [.. BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(data.Length)), .. typed, .. crc];
        }

        static uint Crc32(byte[] data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in data)
            {
                crc ^= b;
                for (var k = 0; k < 8; k++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            }

            return ~crc;
        }

        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; // Bittiefe, Graustufen
        return [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, .. Chunk("IHDR", ihdr), .. Chunk("IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]), .. Chunk("IEND", [])];
    }

    [Fact]
    public void ImageSafety_RejectsBombsAndBlockedFormats_AcceptsPhotos()
    {
        ImageSafety.EnsureHardened().ShouldBeTrue();
        ImageSafety.IsWithinLimits(PngHeader(50_000, 50_000)).ShouldBeFalse();
        ImageSafety.IsWithinLimits(PngHeader(12_000, 9_000)).ShouldBeFalse(); // 108 MP
        ImageSafety.IsWithinLimits(PngHeader(8_064, 6_048)).ShouldBeTrue(); // 48-MP-Handyfoto
        ImageSafety.IsWithinLimits(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"/>")).ShouldBeFalse();
        using var image = NetVips.Image.Black(16, 16);
        ImageSafety.IsWithinLimits(image.JpegsaveBuffer()).ShouldBeTrue();
        ImageSafety.IsWithinLimits(image.PngsaveBuffer()).ShouldBeTrue();
        ImageSafety.IsWithinLimits(image.WebpsaveBuffer()).ShouldBeTrue();
        Should.Throw<NetVips.VipsException>(() => NetVips.Image.NewFromBuffer(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"/>")).Avg());
    }

    [Fact]
    public async Task Uploads_RejectImageBombs_AndSvgLogos()
    {
        await using var factory = await ApiFactory.CreateMigratedAsync(postgres);
        using var admin = await factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);

        var bomb = PngHeader(40_000, 40_000);
        var id = Guid.CreateVersion7();
        (await admin.PostAsJsonAsync("/api/v1/files", new
        {
            id, kind = "photo", originalName = "bombe.png", contentType = "image/png", sizeBytes = (long)bomb.Length,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(bomb)),
        }, Ct)).EnsureSuccessStatusCode();
        using var body = new ByteArrayContent(bomb);
        body.Headers.ContentType = new("application/octet-stream");
        using var upload = await admin.PutAsync(new Uri($"/api/v1/files/{id}/content", UriKind.Relative), body, Ct);
        upload.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await upload.ReadProblemAsync()).Code.ShouldBe("IMAGE_TOO_LARGE");

        using var svg = new ByteArrayContent(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\" width=\"10\" height=\"10\"/>"));
        svg.Headers.ContentType = new("image/png"); // falscher Typ im Header: entscheidend ist der Inhalt
        using var logo = await admin.PutAsync(new Uri("/api/v1/company/logo", UriKind.Relative), svg, Ct);
        logo.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData("© <a href=\"https://www.openstreetmap.org/copyright\">OpenStreetMap</a>-Mitwirkende", "© <a href=\"https://www.openstreetmap.org/copyright\">OpenStreetMap</a>-Mitwirkende")]
    [InlineData("<img src=x onerror=alert(1)>Karte", "&lt;img src=x onerror=alert(1)&gt;Karte")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "&lt;a href=&quot;javascript:alert(1)&quot;&gt;x&lt;/a&gt;")]
    [InlineData("<a href=\"https://x.de\" onclick=\"alert(1)\">x</a>", "&lt;a href=&quot;https://x.de&quot; onclick=&quot;alert(1)&quot;&gt;x&lt;/a&gt;")]
    [InlineData("&copy; Geo &amp; Co", "&copy; Geo &amp; Co")]
    public void MapAttribution_OnlyTextAndHttpsLinks(string input, string expected)
    {
        var safe = MapSettings.SafeAttribution(input);
        safe.ShouldBe(expected);
        MapSettings.SafeAttribution(safe).ShouldBe(expected); // idempotent (Einstellungen werden oft gespeichert)
    }

    private static async Task<string?> ClientIpAsync(string remote, string forwardedFor, string? extraProxies = null)
    {
        var options = new ForwardedHeadersOptions();
        ForwardedHeadersSetup.Configure(options, new EstrichOneOptions { TrustedProxies = extraProxies });
        string? seen = null;
        var middleware = new ForwardedHeadersMiddleware(context =>
        {
            seen = context.Connection.RemoteIpAddress?.ToString();
            return Task.CompletedTask;
        }, NullLoggerFactory.Instance, Microsoft.Extensions.Options.Options.Create(options));
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        http.Request.Headers["X-Forwarded-For"] = forwardedFor;
        await middleware.Invoke(http);
        return seen;
    }

    [Fact]
    public async Task ForwardedFor_OnlyFromTrustedProxies()
    {
        // Direktbetrieb: Caddy im Docker-Netz setzt die Client-Adresse.
        (await ClientIpAsync("172.18.0.3", "203.0.113.7")).ShouldBe("203.0.113.7");
        (await ClientIpAsync("::ffff:172.18.0.3", "203.0.113.7")).ShouldBe("203.0.113.7");
        // Hinter Nginx Proxy Manager: „gefälscht, echt, NPM“ → echte Adresse, die Fälschung wird ignoriert.
        (await ClientIpAsync("172.18.0.3", "198.51.100.66, 203.0.113.7, 192.168.1.10")).ShouldBe("203.0.113.7");
        // Direkt aus dem Internet (falsch veröffentlichter Port): Header wird ignoriert.
        (await ClientIpAsync("198.51.100.20", "10.0.0.1")).ShouldBe("198.51.100.20");
        // Proxy mit öffentlicher Adresse nur, wenn eingetragen.
        (await ClientIpAsync("172.18.0.3", "203.0.113.7, 198.51.100.5")).ShouldBe("198.51.100.5");
        (await ClientIpAsync("172.18.0.3", "203.0.113.7, 198.51.100.5", "198.51.100.5")).ShouldBe("203.0.113.7");
        // Dieselbe Schreibweise wie für Caddy (ESTRICHONE_TRUSTED_PROXIES aus der .env, Compose gibt sie an beide).
        (await ClientIpAsync("172.18.0.3", "203.0.113.7, 198.51.100.5", "private_ranges 198.51.100.5")).ShouldBe("203.0.113.7");
    }

    [Fact]
    public void RequestLog_MasksPublicAcceptanceTokens()
    {
        LogRedaction.Path("/api/v1/public/acceptances/AbC123geheim/signature").ShouldBe("/api/v1/public/acceptances/***/signature");
        LogRedaction.Path("/api/v1/bauakten/123").ShouldBe("/api/v1/bauakten/123");
    }
}
