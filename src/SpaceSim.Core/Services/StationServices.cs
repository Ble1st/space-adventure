using System.Diagnostics.CodeAnalysis;

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

    /// <summary>Standard-Registry mit dem Handelsdienst ("trade").</summary>
    public static StationServices Default()
    {
        var registry = new StationServices();
        registry.Register(new TradeService());
        return registry;
    }
}
