using SpaceSim.Core.Model;

namespace SpaceSim.Core.Flight;

/// <summary>Was die Kontexttaste tut: andocken, springen oder nichts.</summary>
public enum ContextKind { None, Dock, Jump }

/// <summary>
/// Runde 20 (Handy-Test: mit einem gewählten Tor blieb „Andocken: &lt;Station&gt;“ stehen): Wahl der Kontexttaste aus dem, was in Reichweite ist, mit Vorrang für das
/// gewählte Ziel — ein gewähltes Tor in Reichweite gewinnt gegen eine nahe Station, eine gewählte Station gegen ein nahes Tor; ohne passende Wahl wie bisher
/// (Station vor Tor). Rein.
/// </summary>
public static class ContextChoice
{
    public static (ContextKind Kind, int Id) Pick(StationId? nearStation, GateId? nearGate, StationId? selectedStation, GateId? selectedGate)
    {
        if (selectedGate is { } sg && nearGate is { } ng && sg == ng) return (ContextKind.Jump, ng.Value);
        if (selectedStation is { } ss && nearStation is { } ns && ss == ns) return (ContextKind.Dock, ns.Value);
        if (selectedGate is not null && nearGate is { } ng2) return (ContextKind.Jump, ng2.Value);   // Tor gewählt: das nahe Tor statt einer Station
        if (nearStation is { } s) return (ContextKind.Dock, s.Value);
        if (nearGate is { } g) return (ContextKind.Jump, g.Value);
        return (ContextKind.None, 0);
    }
}
