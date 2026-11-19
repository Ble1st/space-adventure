using SpaceSim.Core.Model;

namespace SpaceSim.Core.Intel;

/// <summary>Wissensstand über einen Sektor: unbekannt, von fern gesehen (Name und Lage, Inhalt unbekannt), besucht.</summary>
public enum SectorKnowledge : byte { Unknown = 0, Seen = 1, Visited = 2 }

/// <summary>
/// Ausbau Punkt 16: was der Spieler-Akteur weiß (<see cref="Actor.Intel"/>; null = alles bekannt). Reiner Zustand — Regeln in <see cref="Knowledge"/>.
/// Gespeichert additiv (<c>ActorSave.Intel</c>), gehasht nur mit Spielerpolitik; die NPC-Welt liest es nie.
/// </summary>
public sealed class PlayerIntel
{
    /// <summary>Sektoren mit Stand (fehlt = unbekannt).</summary>
    public Dictionary<SectorId, SectorKnowledge> Sectors = new();
    public HashSet<StationId> Stations = new();
    public HashSet<GateId> Gates = new();
    /// <summary>Rohstofffelder je Sektor und Index in <see cref="Sector.Fields"/>.</summary>
    public HashSet<(int Sector, int Index)> Fields = new();
    /// <summary>Gasfelder je Sektor und Index in <see cref="Sector.GasFields"/>.</summary>
    public HashSet<(int Sector, int Index)> GasFields = new();
    public HashSet<FactionId> Factions = new();

    public SectorKnowledge StateOf(SectorId s) => Sectors.TryGetValue(s, out var k) ? k : SectorKnowledge.Unknown;

    /// <summary>Hebt den Stand an (nie zurück). true = etwas Neues.</summary>
    public bool Raise(SectorId s, SectorKnowledge k)
    {
        if (StateOf(s) >= k) return false;
        Sectors[s] = k;
        return true;
    }

    /// <summary>Anzahl aller Einträge (Anzeige, Tests).</summary>
    public int Count => Sectors.Count + Stations.Count + Gates.Count + Fields.Count + GasFields.Count + Factions.Count;
}
