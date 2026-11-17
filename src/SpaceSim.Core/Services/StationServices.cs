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
    public IReadOnlyList<IStationService> ForStation(Universe u, Station station) => ForStation(u, station, null);

    /// <summary>
    /// Wie oben, aber mit Schiff: Runde 40 verweigert eine Station im Krieg mit dem Spielerschiff alle Dienste
    /// (<see cref="Simulation.Wars.ServiceRefusal"/> — Handel, Andocken und Dienste sind im Krieg gesperrt).
    /// </summary>
    public IReadOnlyList<IStationService> ForStation(Universe u, Station station, Ship? ship)
    {
        if (ship is not null && Simulation.Wars.ServiceRefusal(u, station, ship) is not null) return Array.Empty<IStationService>();
        if (!u.StationTypes.TryGetValue(station.TypeKey, out var type)) return Array.Empty<IStationService>();
        var list = new List<IStationService>(type.Services.Count + 1);
        foreach (var key in type.Services)
            if (_byKey.TryGetValue(key, out var service) && service.IsAvailable(u, station))
                list.Add(service);
        // Runde 41: der Dienst „missions“ hängt an jeder andockbaren Station (nicht typgebunden; im Krieg oben schon leer)
        if (_byKey.TryGetValue(MissionService.ServiceKey, out var ms) && ms.IsAvailable(u, station) && !list.Contains(ms))
            list.Add(ms);
        return list;
    }

    /// <summary>Standard-Registry mit allen Diensten in fester Reihenfolge: trade, refuel, repair (Phase 4c), outfitter, shipdealer (Phase 5c), upgrade (Phase 7), storage (Phase 8), court und bounty (Phase 11), hangar, taxi und rental (Phase 12), research (Phase 13), rearm (Phase 25), missions (Runde 41), blackmarket (Schwarzmarkt).</summary>
    public static StationServices Default()
    {
        var registry = new StationServices();
        registry.Register(new TradeService());
        registry.Register(new RefuelService());
        registry.Register(new RepairService());
        registry.Register(new OutfitterService());
        registry.Register(new ShipDealerService());
        registry.Register(new UpgradeService());
        registry.Register(new StorageService());
        registry.Register(new CourtService());
        registry.Register(new BountyService());
        registry.Register(new HangarService());
        registry.Register(new TaxiService());
        registry.Register(new RentalService());
        registry.Register(new ResearchService());   // Phase 13
        registry.Register(new RearmService());      // Phase 25
        registry.Register(new MissionService());    // Runde 41: Aufträge (Dock-Bereich „Aufträge“)
        registry.Register(new BlackMarketService()); // Schwarzmarkt (Hehler im Piratenversteck)
        return registry;
    }
}
