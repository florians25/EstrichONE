using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using EstrichOne.Client;

namespace EstrichOne.Desktop.ViewModels;

/// <summary>Gemeinsame Basis der Seiten: Ladezustand und verständliche Fehlermeldung statt Ausnahme.</summary>
public abstract partial class PageViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    private int _generation;

    /// <summary>
    /// Laden mit Generationszähler: Startet währenddessen ein neueres Laden (z. B. Filter geändert und „Aktualisieren“),
    /// verwirft <paramref name="apply"/> das ältere Ergebnis, statt die Liste doppelt zu füllen.
    /// </summary>
    protected Task<bool> LoadLatestAsync<T>(Func<Task<T>> fetch, Action<T> apply)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(apply);
        var generation = Interlocked.Increment(ref _generation);
        return RunAsync(async () =>
        {
            var result = await fetch();
            if (generation == Volatile.Read(ref _generation))
            {
                apply(result);
            }
        });
    }

    /// <summary>Führt eine Serveraktion aus; Fehler landen in <see cref="Error"/>. Gibt zurück, ob es geklappt hat.</summary>
    protected async Task<bool> RunAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        IsBusy = true;
        Error = null;
        try
        {
            await action();
            return true;
        }
        catch (Exception exception) when (Describe(exception) is { } message)
        {
            Error = message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Meldung für die Oberfläche; <c>null</c> für Programmfehler, die nicht verschluckt werden dürfen.</summary>
    public static string? Describe(Exception exception) => exception switch
    {
        ApiException { Status: HttpStatusCode.Forbidden } => "Dafür fehlt Ihnen die Berechtigung.",
        ApiException api => api.Message,
        HttpRequestException => "Der Server ist nicht erreichbar. Bitte Netzwerk und Serveradresse prüfen.",
        TaskCanceledException => "Der Server antwortet nicht rechtzeitig.",
        ArgumentException argument => argument.Message.Split(" (Parameter", StringSplitOptions.None)[0],
        InvalidOperationException invalid => invalid.Message,
        _ => null,
    };
}
