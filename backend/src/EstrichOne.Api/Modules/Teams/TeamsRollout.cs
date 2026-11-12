using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Teams;

/// <summary>Stand des Hintergrundauftrags „Alle Bauakten ohne Teams-Verknüpfung verknüpfen“.</summary>
/// <param name="State"><c>running</c>, <c>completed</c>, <c>cancelled</c> oder <c>failed</c> (grundsätzlicher Fehler, z. B. keine Verbindung).</param>
/// <param name="Errors">Höchstens 50 Einzelfehler (Bauaktennummer: Grund).</param>
public sealed record TeamsLinkJobResponse(
    Guid Id, string State, TeamsLinkKind Kind, int Total, int Processed, int Linked, int Skipped, int Failed, IReadOnlyList<string> Errors,
    string? Error, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string? StartedByName);

/// <summary>
/// Massenverknüpfung im Hintergrund: Die Anfrage startet den Auftrag und kehrt sofort zurück (nie eine lange HTTP-Anfrage hinter
/// Cloudflare); die Oberfläche fragt den Stand ab. Höchstens ein Auftrag gleichzeitig; der Stand liegt im Speicher (nach einem
/// Neustart ist er weg – bereits verknüpfte Bauakten bleiben verknüpft, ein neuer Lauf macht beim Rest weiter).
/// </summary>
internal sealed class TeamsRolloutJobs(IServiceScopeFactory scopes, TimeProvider clock, ILogger<TeamsRolloutJobs> logger)
{
    private readonly Lock _gate = new();
    private Job? _current;

    private sealed class Job
    {
        public Guid Id { get; } = Guid.CreateVersion7();
        public required TeamsLinkKind Kind { get; init; }
        public required DateTimeOffset StartedAt { get; init; }
        public required Guid StartedBy { get; init; }
        public string? StartedByName { get; init; }
        public CancellationTokenSource Cancel { get; } = new();
        public string State { get; set; } = "running";
        public int Total { get; set; }
        public int Processed { get; set; }
        public int Linked { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; } = [];
        public string? Error { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
    }

    public TeamsLinkJobResponse? Current
    {
        get
        {
            lock (_gate)
            {
                return _current is null ? null : Snapshot(_current);
            }
        }
    }

    private static TeamsLinkJobResponse Snapshot(Job job) => new(job.Id, job.State, job.Kind, job.Total, job.Processed, job.Linked, job.Skipped, job.Failed,
        [.. job.Errors], job.Error, job.StartedAt, job.FinishedAt, job.StartedByName);

    /// <summary>Startet den Auftrag; läuft schon einer, kommt dessen Stand zurück (<c>started</c> = false).</summary>
    public (TeamsLinkJobResponse Job, bool Started) Start(TeamsLinkKind kind, Guid actor, string? actorName)
    {
        Job job;
        lock (_gate)
        {
            if (_current is { State: "running" } running)
            {
                return (Snapshot(running), false);
            }

            job = new Job { Kind = kind, StartedAt = clock.GetUtcNow(), StartedBy = actor, StartedByName = actorName };
            _current = job;
        }

        // Ohne Ausführungskontext der Anfrage: keine Frist der Oberfläche (GraphClient.BeginInteractive) für den Hintergrund.
        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(() => RunAsync(job));
        }

        return (Snapshot(job), true);
    }

    public bool CancelCurrent()
    {
        lock (_gate)
        {
            if (_current is not { State: "running" } running)
            {
                return false;
            }

            running.Cancel.Cancel();
            return true;
        }
    }

    private async Task RunAsync(Job job)
    {
        var token = job.Cancel.Token;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<TeamsService>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            var db = service.Db;
            var settings = await service.SettingsAsync(token);
            var linked = db.Set<TeamsBauakteLink>().Select(l => l.BauakteId);
            var open = await db.Set<Bauakte>().AsNoTracking()
                .Where(b => b.Status != BauakteStatus.Completed && b.Status != BauakteStatus.Cancelled && !linked.Contains(b.Id))
                .OrderBy(b => b.CreatedAt).Select(b => new { b.Id, b.Number, b.Title }).ToListAsync(token);
            Update(job, j => j.Total = open.Count);
            foreach (var bauakte in open)
            {
                token.ThrowIfCancellationRequested();
                var label = string.IsNullOrWhiteSpace(bauakte.Number) ? bauakte.Title : bauakte.Number;
                try
                {
                    // Inzwischen von Hand verknüpft?
                    if (await db.Set<TeamsBauakteLink>().AnyAsync(l => l.BauakteId == bauakte.Id, token))
                    {
                        Update(job, j => j.Skipped++);
                        continue;
                    }

                    var link = await service.CreateLinkAsync(bauakte.Id, job.Kind, job.StartedBy, token);
                    if (link.Kind == TeamsLinkKind.Channel && link.SyncMembers)
                    {
                        await service.SyncMembersAsync(settings, link, token);
                    }

                    audit.Record("teams.bauakte_linked", job.StartedBy, "bauakte", bauakte.Id.ToString(), new Dictionary<string, string?>
                    {
                        ["kind"] = link.Kind.ToString(), ["created"] = "true", ["name"] = link.DisplayName, ["trigger"] = "rollout",
                    });
                    await db.SaveChangesAsync(token);
                    Update(job, j => j.Linked++);
                }
                catch (GraphException exception) when (exception.Code is "TOO_FEW_MEMBERS" or "CONFLICT")
                {
                    db.ChangeTracker.Clear();
                    Update(job, j =>
                    {
                        j.Skipped++;
                        AddError(j, $"{label}: {exception.Message}");
                    });
                }
                catch (GraphException exception)
                {
                    db.ChangeTracker.Clear();
                    Update(job, j =>
                    {
                        j.Failed++;
                        AddError(j, $"{label}: {exception.Message}");
                    });
                    if (exception.Code is "OFFLINE" or "AUTH_FAILED" or "FORBIDDEN" or "NOT_CONFIGURED" or "SECRET_UNREADABLE" or "NO_SITE_TEAM")
                    {
                        Update(job, j => j.Error = exception.Message);
                        break;
                    }
                }
                finally
                {
                    Update(job, j => j.Processed++);
                }
            }

            Update(job, j => j.State = j.Error is null ? "completed" : "failed");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Update(job, j => j.State = "cancelled");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Teams: Massenverknüpfung abgebrochen");
            Update(job, j =>
            {
                j.State = "failed";
                j.Error = "Unerwarteter Fehler – Einzelheiten stehen im Server-Log (docker compose logs api).";
            });
        }
        finally
        {
            Update(job, j => j.FinishedAt = clock.GetUtcNow());
            if (logger.IsEnabled(LogLevel.Information))
            {
                var done = Current;
                logger.LogInformation("Teams: Massenverknüpfung {State}: {Linked} verknüpft, {Skipped} übersprungen, {Failed} fehlgeschlagen", done?.State, done?.Linked,
                    done?.Skipped, done?.Failed);
            }
        }
    }

    private static void AddError(Job job, string error)
    {
        if (job.Errors.Count < 50)
        {
            job.Errors.Add(TeamsService.Clip(error, 400));
        }
    }

    private void Update(Job job, Action<Job> change)
    {
        lock (_gate)
        {
            change(job);
        }
    }
}
