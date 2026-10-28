using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace EstrichOne.Archive;

public sealed record HelloResponse(string Product, string Role, string Name, string Version, string CaCertificatePem, string CaFingerprint,
    bool PairingOpen);

public sealed record PairRequest(string ClientName, string Nonce, string Proof, string? Host);

public sealed record PairResponse(string ClientId, string Token, string ServerProof, string Role, string Name, string? RestPath,
    string? RestUsername, string? RestPassword);

public sealed record StatusResponse(string Role, string Name, string Version, DateTimeOffset Now, string ClientId, string ClientName,
    DateTimeOffset PairedAt, ArchiveStatistics? Archive, VerificationReport? LastVerification, long? DiskFreeBytes, long? DiskTotalBytes,
    BackupRepositoryStatus? Backup, UpdateState? Update = null);

public sealed record BackupRepositoryStatus(string Repository, long Bytes, int Snapshots, DateTimeOffset? LastSnapshotAt, bool AppendOnly,
    bool ServerRunning);

public sealed record RetentionRequest(DateOnly RetainUntil, string? Reason);

public static class ArchiveServer
{
    public static string Version { get; } =
        typeof(ArchiveServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public static WebApplication Build(ArchiveOptions options, string[] args, bool useHttps = true)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        });
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<TlsCertificates>();
        builder.Services.AddSingleton<ClientRegistry>();
        builder.Services.AddSingleton<AdminToken>();
        builder.Services.AddSingleton<RemoteUpdater>();
        builder.Services.AddHttpClient("rest-server", http => http.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false });
        if (options.Role == ServerRole.Archive)
        {
            builder.Services.AddSingleton<ArchiveStore>();
            builder.Services.AddHostedService<Maintenance>();
        }
        else
        {
            builder.Services.AddSingleton<RestServerHost>();
            builder.Services.AddHostedService(provider => provider.GetRequiredService<RestServerHost>());
            // „Ziel zieht“: zuhause hinter NAT – verbindet sich ausgehend mit dem Server und spiegelt dessen Repository.
            if (options.PullFrom is not null || File.Exists(options.PathOf(MirrorPuller.ConfigFile)))
            {
                builder.Services.AddSingleton<MirrorPuller>();
                builder.Services.AddHostedService(provider => provider.GetRequiredService<MirrorPuller>());
            }
        }

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = null;
            kestrel.Limits.MinRequestBodyDataRate = null;
            if (useHttps)
            {
                kestrel.ListenAnyIP(options.Port, listen =>
                {
                    listen.Protocols = HttpProtocols.Http1AndHttp2;
                    listen.UseHttps(https =>
                    {
                        var tls = kestrel.ApplicationServices.GetRequiredService<TlsCertificates>();
                        https.ServerCertificateSelector = (_, _) => tls.Current;
                    });
                });
            }
        });

        var app = builder.Build();
        Map(app, options);
        app.Lifetime.ApplicationStarted.Register(() => Banner(app.Services, options));
        return app;
    }

    private static void Map(WebApplication app, ArchiveOptions options)
    {
        app.MapGet("/health", () => Results.Text("ok"));

        app.MapGet("/pair/hello", (TlsCertificates tls, ClientRegistry clients) => Results.Json(
            new HelloResponse("EstrichONE", options.RoleName, options.Name, Version, tls.CaPem, tls.CaFingerprint, clients.OpenPairing() is not null)));

        app.MapPost("/pair/complete", (PairRequest request, TlsCertificates tls, ClientRegistry clients, ILoggerFactory logs) =>
        {
            var log = logs.CreateLogger("Kopplung");
            if (string.IsNullOrWhiteSpace(request.Nonce) || request.Nonce.Length is < 16 or > 128)
            {
                return Problem(400, "PAIRING_INVALID", "Ungültige Anfrage.");
            }

            if (clients.OpenPairing() is null)
            {
                return Problem(410, "PAIRING_CLOSED",
                    "Kein gültiger Kopplungscode. Neuen Code erzeugen: docker exec <container> estrichone-archive pair");
            }

            var result = clients.Complete(request.ClientName ?? string.Empty, tls.CaFingerprint, request.Nonce, request.Proof, out var serverProof);
            if (result is null)
            {
                log.LogWarning("Kopplung abgelehnt: falscher Code (höchstens {Max} Versuche je Code).", ClientRegistry.MaxFailedAttempts);
                return Problem(403, "PAIRING_CODE_WRONG", "Der Kopplungscode stimmt nicht.");
            }

            tls.EnsureName(request.Host);
            log.LogInformation("Gekoppelt mit „{Client}“ (Kennung {Id}).", result.Client.Name, result.Client.Id);
            return Results.Json(new PairResponse(result.Client.Id, result.Token, serverProof!, options.RoleName, options.Name,
                result.Client.RestUser is null ? null : $"/restic/{result.Client.RestUser}/", result.Client.RestUser, result.RestPassword));
        });

        var api = app.MapGroup("/api/v1").AddEndpointFilter(async (context, next) =>
        {
            var registry = context.HttpContext.RequestServices.GetRequiredService<ClientRegistry>();
            var header = context.HttpContext.Request.Headers.Authorization.ToString();
            var client = header.StartsWith("Bearer ", StringComparison.Ordinal) ? registry.Authenticate(header[7..].Trim()) : null;
            if (client is null)
            {
                return Problem(401, "ARCHIVE_UNAUTHORIZED", "Nicht gekoppelt oder Kopplung aufgehoben.");
            }

            context.HttpContext.Items["client"] = client;
            return await next(context);
        });

        api.MapGet("/status", (HttpContext http, IServiceProvider services, TimeProvider clock, RemoteUpdater updater) =>
        {
            var client = Client(http);
            var drive = TryDrive(options.DataPath);
            var store = services.GetService<ArchiveStore>();
            BackupRepositoryStatus? backup = null;
            if (client.RestUser is { } user)
            {
                var (bytes, last, count) = ResticProxy.RepositoryInfo(options, user);
                backup = new BackupRepositoryStatus($"/restic/{user}/", bytes, count, last, options.AppendOnly,
                    services.GetService<RestServerHost>()?.Running ?? false);
            }

            return Results.Json(new StatusResponse(options.RoleName, options.Name, Version, clock.GetUtcNow(), client.Id, client.Name, client.PairedAt,
                store?.Statistics(client.Id), store?.LatestVerification(), drive?.AvailableFreeSpace, drive?.TotalSize, backup, updater.Status()));
        });

        // Fernaktualisierung: signierter Auftrag des gekoppelten Servers (nur Version), ausgeführt vom Updater-Container.
        api.MapGet("/update", (RemoteUpdater updater) => Results.Json(updater.Status()));
        api.MapPost("/update", (UpdateOrder order, HttpContext http, RemoteUpdater updater, ILoggerFactory logs) =>
        {
            var client = Client(http);
            if (updater.Accept(order, client.TokenSha256, client.Id) is { } rejected)
            {
                logs.CreateLogger("Aktualisierung").LogWarning("Auftrag von „{Client}“ abgelehnt: {Code}", client.Name, rejected.Code);
                return Problem(rejected.Status, rejected.Code, rejected.Message);
            }

            logs.CreateLogger("Aktualisierung").LogInformation("Auftrag von „{Client}“: Aktualisierung auf {Version}.", client.Name, order.Version);
            return Results.Json(updater.Status(), statusCode: 202);
        });

        api.MapDelete("/pairing", (HttpContext http, ClientRegistry clients) =>
        {
            clients.Remove(Client(http).Id);
            return Results.NoContent();
        });

        if (options.Role == ServerRole.Archive)
        {
            MapArchive(api);
        }
        else
        {
            app.Map("/restic/{**path}", (HttpContext http, string path, ClientRegistry clients, IHttpClientFactory factory) =>
                ResticProxy.HandleAsync(http, path, clients, options, factory));
        }

        // Nur für den Befehl im Container (docker exec … estrichone-archive verify|export|status): Schlüssel liegt im Datenverzeichnis.
        var admin = app.MapGroup("/admin").AddEndpointFilter(async (context, next) =>
        {
            var token = context.HttpContext.RequestServices.GetRequiredService<AdminToken>();
            var remote = context.HttpContext.Connection.RemoteIpAddress;
            return remote is not null && System.Net.IPAddress.IsLoopback(remote)
                && token.Matches(context.HttpContext.Request.Headers["X-Admin-Token"].ToString())
                ? await next(context)
                : Results.StatusCode(403);
        });
        admin.MapGet("/status", (ClientRegistry clients, IServiceProvider services) => Results.Json(new
        {
            role = options.RoleName,
            name = options.Name,
            version = Version,
            clients = clients.Clients.Select(c => new { c.Id, c.Name, c.PairedAt }),
            archive = services.GetService<ArchiveStore>()?.Statistics(),
            lastVerification = services.GetService<ArchiveStore>()?.LatestVerification(),
        }));
        admin.MapPost("/verify", async (IServiceProvider services, CancellationToken ct) =>
            services.GetService<ArchiveStore>() is { } store ? Results.Json(await store.VerifyAsync(ct)) : Results.NotFound());
        admin.MapGet("/export", async (IServiceProvider services, string? client, CancellationToken ct) =>
            services.GetService<ArchiveStore>() is { } store
                ? Results.Stream(new FileStream(await store.ExportAsync(client, null, null, ct), FileMode.Open, FileAccess.Read, FileShare.None,
                    81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous), "application/zip")
                : Results.NotFound());
    }

    private static void MapArchive(RouteGroupBuilder api)
    {
        var objects = api.MapGroup("/objects/{id}");

        objects.MapPost(string.Empty, async (HttpContext http, string id, ArchiveStore store, CancellationToken ct) =>
        {
            http.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize = null;
            if (ArchiveStore.NormalizeId(id) is not { } objectId)
            {
                return Problem(400, "ARCHIVE_INVALID_ID", "Objekt-ID muss eine UUID sein.");
            }

            ObjectDescription? description;
            try
            {
                var raw = http.Request.Headers["X-Archive-Metadata"].ToString();
                description = JsonSerializer.Deserialize<ObjectDescription>(Convert.FromBase64String(raw), ArchiveStore.Json);
            }
            catch (Exception exception) when (exception is FormatException or JsonException)
            {
                description = null;
            }

            if (description is null)
            {
                return Problem(400, "ARCHIVE_INVALID_METADATA", "Kopfzeile X-Archive-Metadata fehlt oder ist ungültig.");
            }

            var (outcome, receipt) = await store.StoreAsync(Client(http).Id, objectId, http.Request.Headers["X-Archive-Sha256"].ToString(),
                description, http.Request.Body, ct);
            return outcome switch
            {
                StoreOutcome.Created => Results.Json(receipt, statusCode: 201),
                StoreOutcome.AlreadyArchived => Results.Json(receipt),
                StoreOutcome.Immutable => Problem(409, "ARCHIVE_IMMUTABLE", "Unter dieser ID ist bereits ein anderes Dokument archiviert – archivierte Dokumente sind unveränderbar."),
                StoreOutcome.ChecksumMismatch => Problem(422, "ARCHIVE_CHECKSUM_MISMATCH", "Die Prüfsumme des empfangenen Inhalts stimmt nicht."),
                StoreOutcome.TooLarge => Problem(413, "ARCHIVE_TOO_LARGE", "Das Dokument ist zu groß."),
                StoreOutcome.RetentionTooShort => Problem(422, "ARCHIVE_RETENTION_TOO_SHORT", "Die Aufbewahrungsfrist ist kürzer als die Mindestfrist des Archivs."),
                _ => Problem(400, "ARCHIVE_INVALID", "Angaben unvollständig (SHA-256, Name, Frist 1–30 Jahre)."),
            };
        });

        // Unveränderbarkeit ausdrücklich: Überschreiben gibt es nicht.
        objects.MapPut(string.Empty, () => Problem(409, "ARCHIVE_IMMUTABLE", "Archivierte Dokumente sind unveränderbar."));
        objects.MapPatch(string.Empty, () => Problem(409, "ARCHIVE_IMMUTABLE", "Archivierte Dokumente sind unveränderbar."));

        objects.MapGet(string.Empty, (HttpContext http, string id, ArchiveStore store) =>
        {
            var objectId = ArchiveStore.NormalizeId(id);
            var receipt = objectId is null ? null : store.Receipt(Client(http).Id, objectId);
            var stream = receipt is null ? null : store.OpenContent(Client(http).Id, objectId!);
            return stream is null
                ? Problem(404, "ARCHIVE_NOT_FOUND", "Nicht im Archiv (oder nach Fristablauf gelöscht).")
                : Results.Stream(stream, "application/octet-stream", receipt!.Name);
        });

        objects.MapGet("/receipt", (HttpContext http, string id, ArchiveStore store) =>
            ArchiveStore.NormalizeId(id) is { } objectId && store.Receipt(Client(http).Id, objectId) is { } receipt
                ? Results.Json(receipt)
                : Problem(404, "ARCHIVE_NOT_FOUND", "Nicht im Archiv."));

        objects.MapPost("/retention", async (HttpContext http, string id, RetentionRequest request, ArchiveStore store, CancellationToken ct) =>
            ArchiveStore.NormalizeId(id) is { } objectId
            && await store.ExtendRetentionAsync(Client(http).Id, objectId, request.RetainUntil, request.Reason, ct) is { } receipt
                ? Results.Json(receipt)
                : Problem(404, "ARCHIVE_NOT_FOUND", "Nicht im Archiv."));

        objects.MapDelete(string.Empty, async (HttpContext http, string id, string? reason, ArchiveStore store, CancellationToken ct) =>
        {
            if (ArchiveStore.NormalizeId(id) is not { } objectId)
            {
                return Problem(404, "ARCHIVE_NOT_FOUND", "Nicht im Archiv.");
            }

            var (outcome, retainUntil) = await store.DeleteAsync(Client(http).Id, objectId, reason, ct);
            return outcome switch
            {
                DeleteOutcome.Deleted => Results.NoContent(),
                DeleteOutcome.RetentionActive => Problem(409, "ARCHIVE_RETENTION_ACTIVE",
                    $"Löschsperre: Aufbewahrungsfrist läuft bis {retainUntil:dd.MM.yyyy}."),
                DeleteOutcome.AlreadyDeleted => Problem(410, "ARCHIVE_DELETED", "Bereits nach Fristablauf gelöscht."),
                _ => Problem(404, "ARCHIVE_NOT_FOUND", "Nicht im Archiv."),
            };
        });

        api.MapPost("/verify", async (ArchiveStore store, CancellationToken ct) => Results.Json(await store.VerifyAsync(ct)));
        api.MapGet("/verify/latest", (ArchiveStore store) =>
            store.LatestVerification() is { } report ? Results.Json(report) : Problem(404, "ARCHIVE_NOT_VERIFIED", "Noch keine Prüfung."));
        api.MapGet("/export", async (HttpContext http, DateOnly? from, DateOnly? to, ArchiveStore store, CancellationToken ct) =>
        {
            var file = await store.ExportAsync(Client(http).Id, from, to, ct);
            return Results.Stream(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None, 81920,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous), "application/zip", $"archiv-export-{DateTime.UtcNow:yyyyMMdd}.zip");
        });
    }

    private static PairedClient Client(HttpContext http) => (PairedClient)http.Items["client"]!;

    private static IResult Problem(int status, string code, string title) =>
        Results.Json(new { status, code, title }, statusCode: status, contentType: "application/problem+json");

    private static DriveInfo? TryDrive(string path)
    {
        try
        {
            return new DriveInfo(Path.GetFullPath(path));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static void Banner(IServiceProvider services, ArchiveOptions options)
    {
        var tls = services.GetRequiredService<TlsCertificates>();
        services.GetRequiredService<AdminToken>();
        var clients = services.GetRequiredService<ClientRegistry>();
        var open = clients.OpenPairing();
        if (open is null && clients.Clients.Count == 0)
        {
            open = clients.NewPairingCode();
        }

        var text = new StringBuilder();
        text.AppendLine();
        text.AppendLine("======================================================================");
        text.AppendLine($" EstrichONE {options.RoleTitle} {Version} – „{options.Name}“ läuft auf Port {options.Port} (HTTPS)");
        text.AppendLine($" Fingerabdruck: {TlsCertificates.FormatFingerprint(tls.CaFingerprint)}");
        if (open is { } pairing)
        {
            text.AppendLine();
            text.AppendLine($" KOPPLUNGSCODE:  {pairing.Code}      (gültig bis {pairing.ExpiresAt.ToLocalTime():dd.MM.yyyy HH:mm})");
            text.AppendLine();
            text.AppendLine(options.Role == ServerRole.Archive
                ? " In EstrichONE: System → Archiv → „Archivserver koppeln“: Adresse dieses Rechners und Code eingeben."
                : " In EstrichONE: System → Backup-Ziele → „Backup-Ziel koppeln“: Adresse dieses Rechners und Code eingeben.");
        }
        else
        {
            text.AppendLine($" Gekoppelt mit {clients.Clients.Count} Server(n). Weiteren Code erzeugen: docker exec <container> estrichone-archive pair");
        }

        text.AppendLine("======================================================================");
        Console.WriteLine(text.ToString());
    }
}

/// <summary>Schlüssel für die Befehle im Container (nur über 127.0.0.1).</summary>
public sealed class AdminToken
{
    private readonly string _value;

    public AdminToken(ArchiveOptions options)
    {
        var path = options.PathOf("admin.token");
        _value = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(path, _value);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public bool Matches(string? candidate) => PairingProof.Matches(_value, candidate);
}

/// <summary>Tägliche Prüfung (Hash-Kette und alle Prüfsummen) und rechtzeitige Zertifikatserneuerung.</summary>
internal sealed class Maintenance(ArchiveStore store, TlsCertificates tls, ArchiveOptions options, TimeProvider clock, ILogger<Maintenance> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                tls.RenewIfDue(clock.GetUtcNow());
                var last = store.LatestVerification();
                if (last is null || clock.GetUtcNow() - last.At >= options.VerificationInterval)
                {
                    var report = await store.VerifyAsync(stoppingToken);
                    if (report.Ok)
                    {
                        logger.LogInformation("Prüfung in Ordnung: {Objects} Dokumente, Journal {Entries} Einträge.", report.ObjectsChecked, report.LedgerEntries);
                    }
                    else
                    {
                        logger.LogError("PRÜFUNG MIT FEHLERN: {Errors}", string.Join(" | ", report.Errors.Take(10)));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogError(exception, "Prüfung fehlgeschlagen.");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
