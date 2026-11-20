namespace EstrichOne.Api.Modules.Planning.Exports;

/// <summary>Fahrzeug, Pumpe oder Maschine aus „Geräte &amp; Fahrzeuge“ (für Tankbelege).</summary>
/// <param name="Kind">vehicle, pump oder machine.</param>
/// <param name="TankCapacityLiters">Tankgröße, falls gepflegt (Plausibilitätsprüfung der Tankbelege).</param>
public sealed record VehicleInfo(Guid Id, string Kind, string Name, string? Identifier, string? InventoryNumber, decimal? TankCapacityLiters, bool IsActive);

/// <summary>Geräte und Fahrzeuge der Planung für andere Module (Tankbelege).</summary>
public interface IVehicleDirectory
{
    /// <summary>Alle Geräte, Fahrzeuge zuerst; ohne <paramref name="includeInactive"/> nur aktive.</summary>
    Task<IReadOnlyList<VehicleInfo>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    /// <summary>
    /// Fahrzeug der Person am Tag: das erste Fahrzeug ihrer Einsätze (über die Kolonne oder direkt eingeplant), sonst das
    /// fest der eigenen Kolonne zugeordnete Fahrzeug; <c>null</c>, wenn keines bekannt ist.
    /// </summary>
    Task<Guid?> VehicleOfDayAsync(Guid userId, DateOnly day, CancellationToken cancellationToken);
}
