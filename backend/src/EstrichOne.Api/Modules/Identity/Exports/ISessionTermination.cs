namespace EstrichOne.Api.Modules.Identity.Exports;

/// <summary>Beendet Sitzungen systemweit (z. B. vor dem Einspielen eines Updates).</summary>
public interface ISessionTermination
{
    /// <summary>Beendet alle aktiven Sitzungen aller Benutzer sofort (Zugriffs- und Erneuerungstoken ungültig).</summary>
    /// <returns>Anzahl beendeter Sitzungen.</returns>
    Task<int> RevokeAllAsync(string reason, CancellationToken cancellationToken);
}
