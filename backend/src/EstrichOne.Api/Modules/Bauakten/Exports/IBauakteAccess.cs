using Microsoft.AspNetCore.Http.HttpResults;

namespace EstrichOne.Api.Modules.Bauakten.Exports;

/// <summary>
/// Sichtbarkeit und Bearbeitbarkeit von Bauakten für den angemeldeten Benutzer (docs/reengineering/03, Abschnitt 6).
/// <list type="bullet">
/// <item><b>Lesen</b>: Wer Bauakten sehen darf (<c>bauakten.read</c>), sieht alle Bauakten – Liste, Suche, Detail.</item>
/// <item><b>Zugeteilt</b>: zuständig, als Beteiligter eingetragen oder über Einsatz/Kolonne eingeplant.</item>
/// <item><b>Schreiben</b>: nur zugeteilt – oder mit <c>bauakten.edit_all</c> („Alle Bauakten bearbeiten“, Büro).</item>
/// <item><b>Offline-Abgleich</b>: zugeteilte Bauakten (mit <c>bauakten.read_all</c> alle) – damit die Apps nicht jede
/// Bauakte des Betriebs speichern müssen.</item>
/// </list>
/// Dateien, Notizen und Aufgaben erben Lese- und Schreibrecht ihrer Bauakte – andere Module prüfen hierüber.
/// </summary>
public interface IBauakteAccess
{
    Task<bool> CanReadAsync(Guid bauakteId, CancellationToken cancellationToken);

    /// <summary>Filtert eine Abfrage auf die für den Benutzer sichtbaren Bauakten (mit Lesezugriff: alle).</summary>
    Task<IQueryable<Bauakte>> VisibleAsync(IQueryable<Bauakte> query, CancellationToken cancellationToken);

    /// <summary>Nur Bauakten, denen der Benutzer zugeteilt ist (zuständig, Beteiligter oder eingeplant).</summary>
    Task<IQueryable<Bauakte>> AssignedAsync(IQueryable<Bauakte> query, CancellationToken cancellationToken);

    /// <summary>Bauakten, die in die Apps synchronisiert werden: zugeteilte, mit <c>bauakten.read_all</c> alle.</summary>
    Task<IQueryable<Bauakte>> SyncScopeAsync(IQueryable<Bauakte> query, CancellationToken cancellationToken);

    /// <summary>Bauakten, die der Benutzer ändern darf: zugeteilte, mit <c>bauakten.edit_all</c> alle sichtbaren.</summary>
    Task<IQueryable<Bauakte>> WritableAsync(IQueryable<Bauakte> query, CancellationToken cancellationToken);

    /// <summary>Darf der Benutzer die Bauakte (samt Inhalt) ändern?</summary>
    Task<BauakteWriteCheck> CheckWriteAsync(Guid bauakteId, CancellationToken cancellationToken);

    async Task<bool> CanWriteAsync(Guid bauakteId, CancellationToken cancellationToken) =>
        await CheckWriteAsync(bauakteId, cancellationToken) == BauakteWriteCheck.Allowed;

    /// <summary>Hat der Benutzer „Alle Bauakten bearbeiten“?</summary>
    Task<bool> CanEditAllAsync(CancellationToken cancellationToken);
}

public enum BauakteWriteCheck
{
    Allowed,

    /// <summary>Gibt es nicht (oder ohne Lesezugriff) – wie bisher 404.</summary>
    NotFound,

    /// <summary>Sichtbar, aber nicht zugeteilt – 403 <see cref="BauakteWriteProblems.NotAssignedCode"/>.</summary>
    NotAssigned,
}

/// <summary>Einheitliche Antwort, wenn eine Änderung an einer nicht zugeteilten Bauakte abgelehnt wird.</summary>
public static class BauakteWriteProblems
{
    public const string NotAssignedCode = "BAUAKTE_NOT_ASSIGNED";

    public const string NotAssignedMessage =
        "Sie sind dieser Bauakte nicht zugeteilt und können sie nur ansehen. Tragen Sie sich als Beteiligter ein, um sie zu bearbeiten.";

    public static ProblemHttpResult NotAssigned() => TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
        title: "Nur ansehen", detail: NotAssignedMessage,
        extensions: new Dictionary<string, object?> { ["code"] = NotAssignedCode });

    public static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound,
        title: "Bauakte nicht gefunden", extensions: new Dictionary<string, object?> { ["code"] = "BAUAKTE_NOT_FOUND" });

    /// <summary>Antwort für eine abgelehnte Prüfung; <c>null</c> bei <see cref="BauakteWriteCheck.Allowed"/>.</summary>
    public static ProblemHttpResult? For(BauakteWriteCheck check) => check switch
    {
        BauakteWriteCheck.NotFound => NotFound(),
        BauakteWriteCheck.NotAssigned => NotAssigned(),
        _ => null,
    };
}
