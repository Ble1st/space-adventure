using SpaceSim.Core.Events;
using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Wurmlöcher (docs/ERKUNDUNG.md „Wurmlöcher“).
/// <list type="bullet">
/// <item><b>Stabil:</b> Torpaare mit <see cref="Gate.Wormhole"/> aus der Weltdatei (<c>wormholes[]</c>). Technisch Kanten im Torgraphen, damit Routensuche
/// (<see cref="Universe.FindRoute(SectorId, SectorId)"/>), Reiseplanung, Kartenteile (<see cref="MapParts"/>) und der Torsprung des Spielers ohne Sonderweg
/// funktionieren. Aber kein Tor: kein Kontrollpunkt, kein Zoll, keine Sperre (<see cref="GateAccess.Evaluate"/>), nicht angreifbar (keine Torklasse), nie
/// geschlossen. NPCs nutzen sie wie Tore.</item>
/// <item><b>Instabil:</b> <see cref="UnstableWormhole"/> aus einem eigenen RNG (Welt-Seed ⊕ Takt, nie <see cref="Universe.Rng"/>), befristet, einseitig in
/// einen zufälligen anderen Sektor. Nur der Spieler fliegt hinein (<see cref="Traverse"/>); das Ziel wird beim Durchflug aufgedeckt.</item>
/// </list>
/// </summary>
public static class Wormholes
{
    /// <summary>Eigener RNG für einen Takt (derselbe Seed + Takt ⇒ dieselben Wurmlöcher).</summary>
    public static DeterministicRng Rng(Universe u, long tick, int salt = 0) =>
        new(u.Seed ^ 0x574F524D_484F4C45UL ^ (0x9E3779B97F4A7C15UL * (ulong)(tick + 1)) ^ (0xC2B2AE3D27D4EB4FUL * (ulong)(uint)salt));

    /// <summary>Ist das Tor die Hälfte eines stabilen Wurmlochs?</summary>
    public static bool IsWormhole(Universe u, GateId id) => u.Gates.TryGetValue(id, out var g) && g.Wormhole;

    /// <summary>„Wurmloch Borealis–Kalypso“ (Kurznamen der beiden Sektoren, die Hälfte zuerst).</summary>
    public static string NameOf(Universe u, Gate g)
    {
        string Short(SectorId s) => u.Sectors.TryGetValue(s, out var sec) ? sec.Name.Split(' ').Last() : "?";
        var far = u.Gates.GetValueOrDefault(g.Target);
        return $"Wurmloch {Short(g.Sector)}–{(far is null ? "?" : Short(far.Sector))}";
    }

    /// <summary>Instabiles Wurmloch nach Id (null = keins/zusammengefallen).</summary>
    public static UnstableWormhole? Of(Universe u, int id) => u.UnstableWormholes.FirstOrDefault(w => w.Id == id);

    /// <summary>Instabile Wurmlöcher eines Sektors (Reihenfolge des Aufreißens).</summary>
    public static IEnumerable<UnstableWormhole> In(Universe u, SectorId s) => u.UnstableWormholes.Where(w => w.Sector == s);

    /// <summary>Anzeige-Ziel eines instabilen Wurmlochs: Name, wenn durchflogen oder der Sektor schon besucht, sonst „?“.</summary>
    public static string TargetText(Universe u, UnstableWormhole w) =>
        w.Traversed || Knowledge.Visited(u, w.Target) ? Knowledge.SectorName(u, w.Target) : "?";

    /// <summary>„Instabiles Wurmloch → ? · noch 4:10“.</summary>
    public static string Label(Universe u, UnstableWormhole w) =>
        $"Instabiles Wurmloch → {TargetText(u, w)} · noch {Clock(w.EndTick - u.Tick)}";

    /// <summary>Ticks als m:ss (4 Ticks je Sekunde).</summary>
    public static string Clock(long ticks)
    {
        long s = Math.Max(0, ticks) / SimClock.TicksPerSecond;
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>Ein Schritt (aus <see cref="WormholeSystem"/>): Ablauf prüfen, zum Prüftakt ggf. ein neues instabiles Wurmloch würfeln.</summary>
    public static void Step(Universe u)
    {
        var r = u.Rules;
        if (!r.Wormholes) return;
        Expire(u);
        if (r.WormholeCheckTicks <= 0) return;
        int every = Math.Max(WormholeSystem.Interval, r.WormholeCheckTicks);
        if (u.Tick <= 0 || u.Tick % every != 0) return;
        if (u.UnstableWormholes.Count >= r.WormholeMaxUnstable) return;
        var rng = Rng(u, u.Tick);
        if (rng.NextDouble() >= r.WormholeChance) return;
        var sectors = u.Sectors.Values.OrderBy(s => s.Id.Value).Where(s => !In(u, s.Id).Any()).ToList();
        if (sectors.Count < 2) return;
        var from = sectors[rng.NextInt(sectors.Count)];
        var targets = u.Sectors.Values.OrderBy(s => s.Id.Value).Where(s => s.Id != from.Id).ToList();
        var to = targets[rng.NextInt(targets.Count)];
        Open(u, from.Id, to.Id, rng, null);
    }

    /// <summary>Öffnet ein instabiles Wurmloch (Test/Selbsttest): Sektor, Ziel, optional Ort. null = Sektor unbekannt oder kein Platz.</summary>
    public static UnstableWormhole? Force(Universe u, SectorId sector, SectorId target, Vec2? at = null) =>
        sector == target || !u.Sectors.ContainsKey(sector) || !u.Sectors.ContainsKey(target) ? null : Open(u, sector, target, Rng(u, u.Tick, u.NextWormholeId), at);

    static UnstableWormhole? Open(Universe u, SectorId sector, SectorId target, DeterministicRng rng, Vec2? at)
    {
        var r = u.Rules;
        int lo = Math.Max(1, r.WormholeMinTicks), hi = Math.Max(lo, r.WormholeMaxTicks);
        long duration = lo + rng.NextInt(hi - lo + 1);
        var pos = at ?? UniverseEvents.FreeSpot(u, u.Sectors[sector], rng, 25f);
        var exit = UniverseEvents.FreeSpot(u, u.Sectors[target], rng, 25f);
        if (pos is not { } p || exit is not { } x) return null;
        var w = new UnstableWormhole { Id = u.NextWormholeId++, Sector = sector, Position = p, Target = target, TargetPosition = x, StartTick = u.Tick, EndTick = u.Tick + duration };
        u.UnstableWormholes.Add(w);
        u.Events.Publish(new WormholeOpened(w.Id, w.Sector, w.Position, w.Target));
        return w;
    }

    /// <summary>Schließt abgelaufene instabile Wurmlöcher (Reihenfolge des Aufreißens).</summary>
    public static void Expire(Universe u)
    {
        for (int i = 0; i < u.UnstableWormholes.Count; i++)
        {
            var w = u.UnstableWormholes[i];
            if (u.Tick < w.EndTick) continue;
            u.UnstableWormholes.RemoveAt(i--);
            u.Events.Publish(new WormholeCollapsed(w.Id, w.Sector));
        }
    }

    /// <summary>Warum kann das Schiff (jetzt) nicht durch? null = es kann. Nur das aktive Spielerschiff, im Sektor, im Einflugradius, frei.</summary>
    public static string? TraverseIssue(Universe u, Ship ship, UnstableWormhole w)
    {
        if (!u.Rules.Wormholes) return "Wurmlöcher sind abgeschaltet";
        if (ship.Id != u.PlayerShip || !ship.IsPlayer) return "nur das aktive Spielerschiff";
        if (u.PlayerOnFoot) return "du bist nicht an Bord";
        if (ship.Sector != w.Sector || ship.Transit is not null) return "nicht im Sektor des Wurmlochs";
        if (ship.DockedAt.Value != 0) return "angedockt";
        if (u.Fights.IsEngaged(ship.Id)) return "im Gefecht";
        if (Vec2.Distance(ship.Position, w.Position) > u.Rules.WormholeRadius + 0.5f) return "zu weit weg";
        return null;
    }

    /// <summary>
    /// Durchflug durch ein instabiles Wurmloch: das Schiff landet im Zielsektor am Austrittsort, der Spielersektor folgt, ein laufendes Laden des
    /// Sprungantriebs bricht ab, der Zielsektor wird besucht (Aufklärung). Liefert den Grund, wenn es nicht geht (dann ändert sich nichts).
    /// </summary>
    public static string? Traverse(Universe u, Ship ship, int id)
    {
        if (Of(u, id) is not { } w) return "Wurmloch ist zusammengefallen";
        if (TraverseIssue(u, ship, w) is { } issue) return issue;
        var from = ship.Sector;
        Flight.JumpDrive.Abort(u, ship, "moved", "Wurmloch");
        u.MoveShip(ship, w.Target);
        ship.Position = w.TargetPosition;
        u.PlayerSector = w.Target;
        w.Traversed = true;
        Knowledge.Publish(u, w.Target, Knowledge.Visit(u, w.Target), "wormhole");
        u.Events.Publish(new WormholeTraversed(ship.Id, from, w.Target, w.Id));
        return null;
    }
}

/// <summary>Wurmlöcher: Ablauf und neue instabile (eigenes Modul, am Ende; ohne Schalter nichts).</summary>
public sealed class WormholeSystem : ISimSystem
{
    public const int Interval = 50;
    public void Step(Universe u, int dt) => Wormholes.Step(u);
}
