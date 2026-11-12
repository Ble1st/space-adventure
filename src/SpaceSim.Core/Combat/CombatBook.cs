using SpaceSim.Core.Model;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 24: die laufenden Gefechte eines Universums (gespeichert, in fester Reihenfolge nach Id) und der abgeleitete Index Schiff → Gefecht (nicht
/// gespeichert, beim Laden über <see cref="Rebuild"/> neu aufgebaut). Ein Schiff ist höchstens in einem Gefecht.
/// </summary>
public sealed class CombatBook
{
    readonly Dictionary<ShipId, int> _byShip = new();

    /// <summary>Laufende Gefechte (aufsteigende Id).</summary>
    public List<Encounter> Encounters { get; } = new();
    /// <summary>Nächste freie Gefechts-Id (gespeichert).</summary>
    public int NextId { get; set; } = 1;

    public int Count => Encounters.Count;

    /// <summary>Ist das Schiff aktiver Teilnehmer eines Gefechts?</summary>
    public bool IsEngaged(ShipId ship) => _byShip.ContainsKey(ship);

    /// <summary>Gefecht, an dem das Schiff aktiv teilnimmt (null = keins).</summary>
    public Encounter? Of(ShipId ship)
    {
        if (!_byShip.TryGetValue(ship, out int id)) return null;
        foreach (var e in Encounters) if (e.Id == id) return e;
        return null;
    }

    internal void Mark(ShipId ship, int encounter) => _byShip[ship] = encounter;
    internal void Unmark(ShipId ship) => _byShip.Remove(ship);

    /// <summary>Index aus den gespeicherten Gefechten neu aufbauen (aktive Teilnehmer).</summary>
    public void Rebuild()
    {
        _byShip.Clear();
        foreach (var e in Encounters)
            foreach (var p in e.Participants)
                if (p.Active) _byShip[p.Ship] = e.Id;
    }

    /// <summary>
    /// Ein Schiff verlässt die Welt (<see cref="Universe.RemoveShip"/>, jeder Abgangsweg): Es scheidet aus seinem Gefecht aus. Bleibt dadurch eine Seite leer,
    /// sind auch die übrigen sofort frei (keine Sperre für ein Gefecht ohne Gegner); die Auswertung (<see cref="CombatSystem.Finish"/>) folgt im nächsten Schritt.
    /// </summary>
    public void OnShipRemoved(ShipId ship)
    {
        var e = Of(ship);
        if (e is null) return;
        _byShip.Remove(ship);
        if (e.Of(ship) is { Active: true } p) p.Exit = CombatExit.Lost;
        bool a = false, d = false;
        foreach (var q in e.Participants) if (q.Active) { if (q.Side == 0) a = true; else d = true; }
        if (a && d) return;
        foreach (var q in e.Participants)
            if (q.Active) { q.Exit = CombatExit.Ended; _byShip.Remove(q.Ship); }
    }

    /// <summary>Laufende Gefechte in einem Sektor (Anzeige).</summary>
    public IEnumerable<Encounter> In(SectorId sector)
    {
        foreach (var e in Encounters) if (e.Sector == sector) yield return e;
    }
}
