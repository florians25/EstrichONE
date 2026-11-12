using System.Globalization;
using System.Text;
using EstrichOne.Hosting.Data;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Hosting.Operations;

/// <summary>Protokoll eines laufenden Vorgangs; wird regelmäßig in die Datenbank geschrieben, damit die Oberfläche live mitliest.</summary>
public sealed class OperationLog(Guid operationId, IServiceScopeFactory scopes, TimeProvider clock)
{
    private const int MaxLength = 256 * 1024;
    private readonly StringBuilder text = new();
    private readonly Lock sync = new();
    private bool dirty;
    private string? phase;
    private DateTime? phaseUntil;

    /// <summary>Phase des Update-Ablaufs für die Konsole (mit dem Protokoll geschrieben).</summary>
    public void SetPhase(string? value, DateTime? until)
    {
        lock (sync)
        {
            phase = value;
            phaseUntil = until;
            dirty = true;
        }
    }

    public (string? Phase, DateTime? Until) Phase
    {
        get
        {
            lock (sync)
            {
                return (phase, phaseUntil);
            }
        }
    }

    public void Write(string line)
    {
        var stamp = clock.GetUtcNow().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        lock (sync)
        {
            if (text.Length > MaxLength)
            {
                // Ältesten Teil verwerfen, Ende behalten (dort stehen Fehlermeldungen).
                text.Remove(0, text.Length - (MaxLength * 3 / 4));
                text.Insert(0, "… (gekürzt)\n");
            }

            text.Append(stamp).Append("  ").AppendLine(line.TrimEnd());
            dirty = true;
        }
    }

    public string Text
    {
        get
        {
            lock (sync)
            {
                return text.ToString();
            }
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        string snapshot;
        string? currentPhase;
        DateTime? until;
        lock (sync)
        {
            if (!dirty)
            {
                return;
            }

            snapshot = text.ToString();
            currentPhase = phase;
            until = phaseUntil;
            dirty = false;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostingDbContext>();
        await db.Operations.Where(o => o.Id == operationId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Log, snapshot).SetProperty(o => o.Phase, currentPhase)
                .SetProperty(o => o.PhaseUntil, until), cancellationToken);
    }

    /// <summary>Schreibt das Protokoll alle zwei Sekunden, solange <paramref name="cancellationToken"/> nicht abgebrochen ist.</summary>
    public async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                await FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Ende des Vorgangs
        }
    }
}
