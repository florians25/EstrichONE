namespace EstrichOne.Api.Modules.Bauakten.Exports;

public enum BauakteHintSeverity
{
    Info,
    Success,
    Warning,
    Danger,
}

/// <summary>Hinweiszeile in der Bauakte („Einsatz heute“, „Erstbegehung offen“ …), serverseitig berechnet (02).</summary>
/// <param name="Kind">Stabiler Schlüssel, z. B. <c>assignment_today</c>; die Apps können darauf reagieren (Reiter öffnen).</param>
public sealed record BauakteHint(string Kind, BauakteHintSeverity Severity, string Text);

/// <summary>Jedes Modul steuert seine Hinweise bei, ohne dass die Bauakte die anderen Module kennen muss.</summary>
public interface IBauakteHintSource
{
    Task<IReadOnlyList<BauakteHint>> GetHintsAsync(Bauakte bauakte, DateOnly today, CancellationToken cancellationToken);
}
