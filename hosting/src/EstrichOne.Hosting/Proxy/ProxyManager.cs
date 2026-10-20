using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Docker;
using EstrichOne.Hosting.Infrastructure;
using EstrichOne.Hosting.Instances;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Proxy;

/// <summary>Pflegt den gemeinsamen Reverse Proxy: Konfiguration schreiben, neu laden, mit den Instanznetzen verbinden.</summary>
public sealed class ProxyManager(
    IOptions<HostingOptions> options,
    IDockerHost docker,
    IServiceScopeFactory scopes,
    IWebHostEnvironment environment,
    ILogger<ProxyManager> logger) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    private HostingOptions Settings => options.Value;

    public void Dispose() => gate.Dispose();

    public string CaddyfilePath => Path.Combine(Settings.ProxyDirectory, "Caddyfile");

    /// <summary>Letzter Fehler beim Neuladen (für die Übersicht).</summary>
    public string? LastError { get; private set; }

    public DateTime? LastReload { get; private set; }

    /// <summary>Schreibt Caddyfile und Wartungsseite (auch ohne laufenden Proxy, z. B. vor dem ersten Start).</summary>
    public async Task<string> WriteConfigurationAsync(CancellationToken cancellationToken)
    {
        List<ProxyRoute> routes;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostingDbContext>();
            routes = (await db.Tenants.AsNoTracking().Where(t => t.State != TenantState.Deleted).ToListAsync(cancellationToken))
                .Select(t => new ProxyRoute(t.Slug, t.Domain, ProxyConfigBuilder.ShowsMaintenance(t.State)))
                .ToList();
        }

        Directory.CreateDirectory(Settings.ProxyDirectory);
        var maintenance = Path.Combine(Settings.ProxyDirectory, "maintenance");
        Directory.CreateDirectory(maintenance);
        await File.WriteAllTextAsync(Path.Combine(maintenance, "index.html"), MaintenancePage.Build(LogoDataUri()), cancellationToken);

        var content = ProxyConfigBuilder.Build(Settings, routes);
        var temp = CaddyfilePath + ".tmp";
        await File.WriteAllTextAsync(temp, content, cancellationToken);
        File.Move(temp, CaddyfilePath, overwrite: true);
        return content;
    }

    /// <summary>Konfiguration neu schreiben und den Proxy ohne Unterbrechung neu laden.</summary>
    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        if (!Settings.ManagesProxy)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            await WriteConfigurationAsync(cancellationToken);
            var result = await docker.ExecAsync(Settings.ProxyContainer,
                ["caddy", "reload", "--config", "/etc/caddy/Caddyfile", "--adapter", "caddyfile"], TimeSpan.FromSeconds(60), cancellationToken);
            if (!result.Success)
            {
                LastError = result.Summary;
                logger.LogError("Proxy konnte nicht neu geladen werden: {Error}", result.Summary);
                throw new DockerException($"Reverse Proxy konnte nicht neu geladen werden: {result.Summary}");
            }

            LastError = null;
            LastReload = DateTime.UtcNow;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Edge-Netz der Instanz anlegen und Proxy sowie Konsole damit verbinden.</summary>
    public async Task AttachAsync(string slug, CancellationToken cancellationToken)
    {
        var network = InstanceLayout.EdgeNetwork(slug);
        await docker.EnsureNetworkAsync(network, isInternal: true,
            new Dictionary<string, string> { [InstanceLayout.TenantLabel] = slug }, cancellationToken);
        await docker.ConnectNetworkAsync(network, Settings.ProxyContainer, cancellationToken);
        await docker.ConnectNetworkAsync(network, Settings.ConsoleContainer, cancellationToken);
    }

    public async Task DetachAsync(string slug, CancellationToken cancellationToken)
    {
        var network = InstanceLayout.EdgeNetwork(slug);
        await docker.DisconnectNetworkAsync(network, Settings.ProxyContainer, cancellationToken);
        await docker.DisconnectNetworkAsync(network, Settings.ConsoleContainer, cancellationToken);
        await docker.RemoveNetworkAsync(network, cancellationToken);
    }

    private string? LogoDataUri()
    {
        var file = Path.Combine(environment.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), "brand", "logo.svg");
        return File.Exists(file) ? "data:image/svg+xml;base64," + Convert.ToBase64String(File.ReadAllBytes(file)) : null;
    }
}

/// <summary>Beim Start: Konfiguration schreiben, Netzverbindungen wiederherstellen (z. B. nach Neuerstellung des Proxys), neu laden.</summary>
public sealed class ProxyStartup(
    ProxyManager proxy,
    IDockerHost docker,
    IOptions<HostingOptions> options,
    IServiceScopeFactory scopes,
    ILogger<ProxyStartup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await proxy.WriteConfigurationAsync(stoppingToken);
        if (options.Value.IsFake)
        {
            return;
        }

        // Der Proxy startet erst, wenn die Konsole bereit ist – kurz warten und mehrfach versuchen.
        for (var attempt = 1; attempt <= 30 && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await ReconnectAsync(stoppingToken);
                await proxy.ApplyAsync(stoppingToken);
                logger.LogInformation("Reverse Proxy konfiguriert.");
                return;
            }
            catch (Exception exception) when (exception is DockerException or IOException)
            {
                logger.LogWarning("Proxy noch nicht bereit (Versuch {Attempt}): {Message}", attempt, exception.Message);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, attempt * 2)), stoppingToken);
            }
        }
    }

    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostingDbContext>();
        var slugs = await db.Tenants.AsNoTracking()
            .Where(t => t.State != TenantState.Deleted && t.State != TenantState.Provisioning)
            .Select(t => t.Slug).ToListAsync(cancellationToken);
        foreach (var slug in slugs)
        {
            await docker.EnsureNetworkAsync(InstanceLayout.EdgeNetwork(slug), true,
                new Dictionary<string, string> { [InstanceLayout.TenantLabel] = slug }, cancellationToken);
            await docker.ConnectNetworkAsync(InstanceLayout.EdgeNetwork(slug), options.Value.ProxyContainer, cancellationToken);
            await docker.ConnectNetworkAsync(InstanceLayout.EdgeNetwork(slug), options.Value.ConsoleContainer, cancellationToken);
        }
    }
}
