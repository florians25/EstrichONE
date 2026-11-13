using System.Threading.Channels;
using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Teams.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Teams;

/// <summary>
/// Microsoft-Teams-Anbindung: App-Registrierung des Betriebs, Kontenzuordnung, Teams-Unterhaltung je Bauakte,
/// Mitgliederabgleich, Ereigniskarten und Chat-Modus. Ohne Einrichtung wirkungslos (docs/handbuch/microsoft-teams.md).
/// </summary>
public sealed class TeamsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TeamsOptions>().Bind(configuration.GetSection(TeamsOptions.SectionName));
        services.AddHttpClient(GraphClient.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(2)); // Obergrenze; die Fristen je Versuch setzt GraphClient
        services.AddSingleton<GraphClient>();
        services.AddSingleton<TeamsQueue>();
        services.AddSingleton<TeamsRolloutJobs>();
        services.AddScoped<TeamsService>();
        services.AddScoped<ITeamsIntegration, TeamsIntegration>();
        services.AddHostedService<TeamsWorker>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => TeamsEndpoints.Map(api);
}

/// <summary>Arbeit für den Hintergrund: Ereignisse und geänderte Bauakten-Teams.</summary>
internal sealed class TeamsQueue
{
    private readonly Channel<object> _channel = Channel.CreateBounded<object>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true,
    });

    public ChannelReader<object> Reader => _channel.Reader;

    public void Write(object item) => _channel.Writer.TryWrite(item);
}

internal sealed record TeamChanged(Guid BauakteId);

internal sealed record CrewChanged(Guid CrewId);

internal sealed class TeamsIntegration(EstrichOneDbContext db, TeamsQueue queue) : ITeamsIntegration
{
    public async Task<TeamsChatMode> ChatModeAsync(CancellationToken cancellationToken) =>
        await db.Set<TeamsSettingsRow>().AsNoTracking().Select(s => (TeamsChatMode?)s.ChatMode).SingleOrDefaultAsync(cancellationToken) ?? TeamsChatMode.EstrichOne;

    public void Publish(TeamsEvent teamsEvent) => queue.Write(teamsEvent);

    public void BauakteTeamChanged(Guid bauakteId) => queue.Write(new TeamChanged(bauakteId));

    public void CrewChanged(Guid crewId) => queue.Write(new CrewChanged(crewId));
}

/// <summary>
/// Sendet Ereigniskarten, gleicht Mitglieder ab (bei Teamänderung sofort, sonst regelmäßig, wenn sich das Soll geändert hat
/// oder der letzte Abgleich älter als ein Tag ist) und verknüpft neue Bauakten automatisch, wenn eingestellt.
/// </summary>
internal sealed class TeamsWorker(IServiceScopeFactory scopes, TeamsQueue queue, IOptions<TeamsOptions> options, TimeProvider clock,
    ILogger<TeamsWorker> logger) : BackgroundService
{
    private readonly HashSet<Guid> _autoLinkFailed = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var next = clock.GetUtcNow().AddMinutes(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = next - clock.GetUtcNow();
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
            {
                wait.CancelAfter(delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
                try
                {
                    await queue.Reader.WaitToReadAsync(wait.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Zeit für den regelmäßigen Abgleich.
                }
            }

            while (queue.Reader.TryRead(out var item))
            {
                await RunAsync(service => item switch
                {
                    TeamsEvent teamsEvent => service.PostAsync(teamsEvent, stoppingToken),
                    TeamChanged changed => SyncOneAsync(service, changed.BauakteId, stoppingToken),
                    CrewChanged crew => SyncCrewAsync(service, crew.CrewId, stoppingToken),
                    _ => Task.CompletedTask,
                });
            }

            if (clock.GetUtcNow() >= next)
            {
                await RunAsync(service => SweepAsync(service, stoppingToken));
                next = clock.GetUtcNow().Add(options.Value.SyncInterval);
            }
        }
    }

    private async Task RunAsync(Func<TeamsService, Task> action)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<TeamsService>());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Teams-Hintergrundarbeit fehlgeschlagen");
        }
    }

    private static async Task SyncOneAsync(TeamsService service, Guid bauakteId, CancellationToken cancellationToken)
    {
        var db = service.Db;
        var link = await db.Set<TeamsBauakteLink>().SingleOrDefaultAsync(l => l.BauakteId == bauakteId, cancellationToken);
        if (link is null || !link.SyncMembers)
        {
            return;
        }

        await service.SyncMembersAsync(await service.SettingsAsync(cancellationToken), link, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Kolonne geändert: ihr Teams-Chat und Bauakten-Chats, zu denen die Kolonne gehört.</summary>
    private static async Task SyncCrewAsync(TeamsService service, Guid crewId, CancellationToken cancellationToken)
    {
        var db = service.Db;
        var settings = await service.SettingsAsync(cancellationToken);
        if (!settings.GraphConfigured)
        {
            return;
        }

        if (await db.Set<TeamsCrewChat>().SingleOrDefaultAsync(c => c.CrewId == crewId, cancellationToken) is { } chat)
        {
            await service.SyncCrewChatAsync(settings, chat, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        var source = TeamsMemberSources.Crew(crewId);
        foreach (var link in await db.Set<TeamsBauakteLink>().Where(l => l.SyncMembers && l.MemberSources.Contains(source)).ToListAsync(cancellationToken))
        {
            await service.SyncMembersAsync(settings, link, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SweepAsync(TeamsService service, CancellationToken cancellationToken)
    {
        var db = service.Db;
        var settings = await service.SettingsAsync(cancellationToken);
        if (!settings.GraphConfigured)
        {
            return;
        }

        if (settings.AutoLink && settings.AutoLinkSince is { } since
            && (settings.DefaultLinkKind == TeamsLinkKind.GroupChat || !string.IsNullOrWhiteSpace(settings.SiteTeamId)))
        {
            var linked = db.Set<TeamsBauakteLink>().Select(l => l.BauakteId);
            var fresh = await db.Set<Bauakte>().AsNoTracking()
                .Where(b => b.CreatedAt >= since && b.Status != BauakteStatus.Cancelled && !linked.Contains(b.Id))
                .OrderBy(b => b.CreatedAt).Take(20).Select(b => new { b.Id, b.CreatedByUserId }).ToListAsync(cancellationToken);
            foreach (var bauakte in fresh.Where(b => !_autoLinkFailed.Contains(b.Id)))
            {
                try
                {
                    var link = await service.CreateLinkAsync(bauakte.Id, settings.DefaultLinkKind, bauakte.CreatedByUserId, cancellationToken);
                    if (link.Kind == TeamsLinkKind.Channel && link.SyncMembers)
                    {
                        await service.SyncMembersAsync(settings, link, cancellationToken);
                    }

                    await db.SaveChangesAsync(cancellationToken);
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation("Bauakte {BauakteId} automatisch mit Teams verknüpft", bauakte.Id);
                    }
                }
                catch (GraphException exception)
                {
                    // Später erneut nur nach Neustart oder von Hand – sonst jede Runde dieselbe Fehlermeldung.
                    _autoLinkFailed.Add(bauakte.Id);
                    logger.LogWarning("Bauakte {BauakteId} nicht automatisch mit Teams verknüpft: {Error}", bauakte.Id, exception.Message);
                    db.ChangeTracker.Clear();
                    if (exception.Code is "OFFLINE" or "AUTH_FAILED")
                    {
                        break;
                    }
                }
            }
        }

        // Abgeschlossene bzw. stornierte Bauakten: Kanal archivieren oder löschen (Einstellung „Beim Abschließen“).
        await service.HandleClosedAsync(settings, cancellationToken);

        var stale = clock.GetUtcNow().AddDays(-1);
        var links = await db.Set<TeamsBauakteLink>().Where(l => l.SyncMembers).ToListAsync(cancellationToken);
        foreach (var link in links)
        {
            var due = link.SyncPending || link.LastSyncAt is null || link.LastSyncAt < stale
                || (await service.DesiredMembersAsync(settings, link, cancellationToken)).Hash != link.DesiredHash;
            if (!due)
            {
                continue;
            }

            await service.SyncMembersAsync(settings, link, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (link.LastSyncError?.StartsWith("Microsoft 365 ist vom Server aus nicht erreichbar", StringComparison.Ordinal) == true)
            {
                break; // ohne Internet nicht jede Bauakte einzeln versuchen
            }
        }

        foreach (var chat in await db.Set<TeamsCrewChat>().ToListAsync(cancellationToken))
        {
            var due = chat.SyncPending || chat.LastSyncAt is null || chat.LastSyncAt < stale
                || (await service.CrewDesiredAsync(settings, chat.CrewId, chat.LinkedByUserId, cancellationToken)).Hash != chat.DesiredHash;
            if (!due)
            {
                continue;
            }

            await service.SyncCrewChatAsync(settings, chat, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (chat.LastSyncError?.StartsWith("Microsoft 365 ist vom Server aus nicht erreichbar", StringComparison.Ordinal) == true)
            {
                break;
            }
        }
    }
}
