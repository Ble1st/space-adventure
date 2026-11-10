using System.Diagnostics.CodeAnalysis;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Services;

/// <summary>
/// Registry aller Stationsdienste. Die Reihenfolge ist die Registrierungsreihenfolge —
/// deterministisch, ohne Abhängigkeit von Hash-Reihenfolgen.
/// </summary>
public sealed class StationServices
{
    readonly List<IStationService> _services = new();
    readonly Dictionary<string, IStationService> _byKey = new();

    /// <summary>Registriert einen Dienst; wirft <see cref="ArgumentException"/> bei doppeltem Schlüssel.</summary>
    public void Register(IStationService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_byKey.ContainsKey(service.Key))
            throw new ArgumentException($"Dienst '{service.Key}' ist bereits registriert.", nameof(service));
        _byKey.Add(service.Key, service);
        _services.Add(service);
    }

    /// <summary>Dienst zum Schlüssel; false, wenn unbekannt.</summary>
    public bool TryGet(string key, [MaybeNullWhen(false)] out IStationService service) => _byKey.TryGetValue(key, out service);

    /// <summary>Alle registrierten Dienst-Schlüssel in Registrierungsreihenfolge.</summary>
    public IReadOnlyList<string> Keys => _services.Select(s => s.Key).ToArray();

    /// <summary>
    /// Die verfügbaren Dienste einer Station in der Reihenfolge ihrer Typ-Liste (<see cref="StationType.Services"/>).
    /// Unbekannte Schlüssel und Dienste, die an dieser Station nicht verfügbar sind (<see cref="IStationService.IsAvailable"/>), bleiben weg.
    /// </summary>
    public IReadOnlyList<IStationService> ForStation(Universe u, Station station)
    {
        if (!u.StationTypes.TryGetValue(station.TypeKey, out var type)) return Array.Empty<IStationService>();
        var list = new List<IStationService>(type.Services.Count);
        foreach (var key in type.Services)
            if (_byKey.TryGetValue(key, out var service) && service.IsAvailable(u, station))
                list.Add(service);
        return list;
    }

    /// <summary>Standard-Registry mit allen Diensten in fester Reihenfolge: trade, refuel, repair (Phase 4c), outfitter, shipdealer (Phase 5c).</summary>
    public static StationServices Default()
    {
        var registry = new StationServices();
        registry.Register(new TradeService());
        registry.Register(new RefuelService());
        registry.Register(new RepairService());
        registry.Register(new OutfitterService());
        registry.Register(new ShipDealerService());
        return registry;
    }
}
