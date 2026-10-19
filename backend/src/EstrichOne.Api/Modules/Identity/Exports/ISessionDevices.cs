namespace EstrichOne.Api.Modules.Identity.Exports;

/// <summary>Verbindet Sitzungen mit Geräten (für das Geräte-Modul).</summary>
public interface ISessionDevices
{
    /// <summary>Gerät der aktuellen Sitzung oder <c>null</c>.</summary>
    Task<Guid?> GetCurrentSessionDeviceAsync(CancellationToken cancellationToken);

    /// <summary>Bindet die aktuelle Sitzung an ein Gerät. Speichert mit dem nächsten SaveChanges.</summary>
    Task BindCurrentSessionAsync(Guid deviceId, CancellationToken cancellationToken);

    /// <summary>Beendet alle Sitzungen eines Geräts. Speichert mit dem nächsten SaveChanges.</summary>
    Task RevokeDeviceSessionsAsync(Guid deviceId, string reason, CancellationToken cancellationToken);
}
