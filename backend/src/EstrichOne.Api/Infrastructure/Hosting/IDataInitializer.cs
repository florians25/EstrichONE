namespace EstrichOne.Api.Infrastructure.Hosting;

/// <summary>
/// Legt nach den Migrationen benötigte Grunddaten an bzw. hält sie aktuell (z. B. Systemzustand, Standardrollen).
/// Läuft bei jedem <c>migrate</c> und muss daher wiederholbar sein.
/// </summary>
public interface IDataInitializer
{
    /// <summary>Reihenfolge; kleinere Werte zuerst.</summary>
    int Order { get; }

    Task InitializeAsync(CancellationToken cancellationToken);
}
