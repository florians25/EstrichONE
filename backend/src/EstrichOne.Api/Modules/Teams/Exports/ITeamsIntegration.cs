namespace EstrichOne.Api.Modules.Teams.Exports;

/// <summary>Wo Unterhaltungen stattfinden (Einstellungen › Integrationen › Microsoft Teams).</summary>
public enum TeamsChatMode
{
    /// <summary>Nur der eingebaute Chat (Standard, wie bisher).</summary>
    EstrichOne,

    /// <summary>Nur Microsoft Teams: der eingebaute Chat ist ein Archiv (lesen und exportieren, nicht schreiben).</summary>
    Teams,

    /// <summary>Beides: eingebauter Chat wie bisher und „In Teams öffnen“.</summary>
    Both,
}

/// <summary>Ereignisse, die optional als Karte in die verknüpfte Teams-Unterhaltung einer Bauakte gehen.</summary>
public enum TeamsEventKind
{
    DefectCreated,
    AcceptanceSigned,
    AcceptanceRejected,
    ScheduleChanged,
}

/// <summary>
/// Ein Ereignis zu einer Bauakte. Wie bei Pushnachrichten nur Hinweis und Objekt – keine Kommentare, Preise oder
/// Kundenangaben, denn die Karte liegt danach bei Microsoft.
/// </summary>
/// <param name="Link">Relativer Pfad der Weboberfläche (z. B. <c>/bauakten/{id}</c>); wird mit der öffentlichen Adresse ergänzt.</param>
public sealed record TeamsEvent(Guid BauakteId, TeamsEventKind Kind, string Title, string? Detail = null, string? Link = null);

/// <summary>Microsoft-Teams-Anbindung für andere Module. Ohne Einrichtung wirkungslos.</summary>
public interface ITeamsIntegration
{
    /// <summary>Chat-Modus; ohne Einrichtung <see cref="TeamsChatMode.EstrichOne"/>.</summary>
    Task<TeamsChatMode> ChatModeAsync(CancellationToken cancellationToken);

    /// <summary>Ereignis zum Senden vormerken (im Hintergrund, nie blockierend, wirft nie).</summary>
    void Publish(TeamsEvent teamsEvent);

    /// <summary>Team einer Bauakte geändert: Mitglieder der verknüpften Unterhaltung bald abgleichen.</summary>
    void BauakteTeamChanged(Guid bauakteId);

    /// <summary>Mitglieder einer Kolonne geändert: Teams-Chat der Kolonne (und Bauakten-Chats mit dieser Kolonne) im Hintergrund abgleichen.</summary>
    void CrewChanged(Guid crewId);
}
