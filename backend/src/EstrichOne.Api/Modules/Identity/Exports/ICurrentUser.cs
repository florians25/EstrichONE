namespace EstrichOne.Api.Modules.Identity.Exports;

/// <summary>Angemeldeter Benutzer der aktuellen Anfrage.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }
    Guid SessionId { get; }
}
