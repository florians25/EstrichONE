// Integrationstests laufen unter Linux (CI); kurze Aufrufe ohne Abbruch-Token.
#pragma warning disable xUnit1051, CA1416
using System.Net;
using System.Net.Sockets;
using EstrichOne.Archive;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Support;

/// <summary>Echter Archivserver bzw. Backup-Ziel (backend/src/EstrichOne.Archive) mit HTTPS auf einem freien Port.</summary>
public sealed class ArchiveServerHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private ArchiveServerHost(WebApplication app, ArchiveOptions options)
    {
        _app = app;
        Options = options;
    }

    public ArchiveOptions Options { get; }

    public string Address => $"127.0.0.1:{Options.Port}";

    public IServiceProvider Services => _app.Services;

    public static async Task<ArchiveServerHost> StartAsync(ServerRole role = ServerRole.Archive, int minRetentionYears = 0)
    {
        var options = new ArchiveOptions
        {
            DataPath = Path.Combine(Path.GetTempPath(), "estrichone-archive-tests", Guid.NewGuid().ToString("N")),
            Role = role,
            Port = FreePort(),
            Name = role == ServerRole.Archive ? "Archiv Test" : "Backup-Ziel Test",
            MinRetentionYears = minRetentionYears,
            RestServerPath = "/nicht/vorhanden/rest-server",
        };
        Directory.CreateDirectory(options.DataPath);
        var app = ArchiveServer.Build(options, []);
        await app.StartAsync();
        return new ArchiveServerHost(app, options);
    }

    public string NewCode() => Services.GetRequiredService<ClientRegistry>().NewPairingCode().Code;

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        if (Directory.Exists(Options.DataPath))
        {
            foreach (var file in Directory.EnumerateFiles(Options.DataPath, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }

            Directory.Delete(Options.DataPath, recursive: true);
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
