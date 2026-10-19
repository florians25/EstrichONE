namespace EstrichOne.Api.Modules.Setup.Exports;

public interface ISetupState
{
    /// <summary><c>true</c>, solange noch kein Benutzer existiert (frische Installation).</summary>
    Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken);
}
