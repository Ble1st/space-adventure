using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Core.Mining;

/// <summary>Was die Darstellung in diesem Frame tun soll. Genau eines von: Nav (Autopilot zu einem Ziel), Waypoint (lokal fliegen), nichts.</summary>
public readonly record struct MiningStep(
    NavTarget? Nav, StationId? DockAt, Vec2? Waypoint, float Throttle, Asteroid? Aim, bool Fire, bool Tractor);

/// <summary>
/// Automodus für den Spieler: Asteroid suchen → anfliegen → beschießen und Brocken einsammeln → bei vollem Laderaum
/// beste Station anfliegen, andocken, verkaufen → weiter. Reine Entscheidungslogik; Bewegung, Strahlen und Andocken
/// führt die Darstellung aus (wie beim normalen Autopiloten). Enthält keine Zeit/Zufall, daher testbar.
/// </summary>
public sealed class MiningAutopilot
{
    public enum Phase { Seek, Mine, Sell }

    public bool Enabled { get; private set; }
    public Phase Current { get; private set; } = Phase.Seek;
    public string Status { get; private set; } = "";

    Asteroid? _target;

    public void Start() { Enabled = true; Current = Phase.Seek; _target = null; Status = "Suche Asteroid …"; }
    public void Stop(string status = "") { Enabled = false; Status = status; _target = null; }

    public MiningStep Step(Universe u, OreChunks chunks, Ship ship, Vec2 pos)
    {
        var none = new MiningStep(null, null, null, 0f, null, false, false);
        if (!Enabled) return none;
        if (!ship.Has(Capability.Mining)) { Stop("Kein Mining-Laser eingebaut"); return none; }   // Phase 5c: Fit ohne Laser

        var sector = u.Sectors[u.PlayerSector];
        bool chunksNear = chunks.Chunks.Any(c => Vec2.Distance(c.Position, pos) <= MiningRules.TractorRange);

        // Zielasteroid prüfen
        if (Current == Phase.Mine && !chunks.IsMinable(sector, _target)) { _target = null; Current = Phase.Seek; }

        switch (Current)
        {
            case Phase.Seek:
            {
                if (ship.Cargo.Values.Sum() > 0f && NearlyFull(u, ship)) { Current = Phase.Sell; goto case Phase.Sell; }
                var rock = chunks.Nearest(sector, pos);
                if (rock != null) { _target = rock; Current = Phase.Mine; Status = "Abbau"; goto case Phase.Mine; }

                // Nichts mehr im Sektor: Ladung verkaufen oder anderes Feld suchen
                if (ship.Cargo.Values.Sum() > 0f) { Current = Phase.Sell; goto case Phase.Sell; }
                var target = FindOtherSector(u, ship, pos);
                if (target is { } nav) { Status = $"Fliege zu {nav.Label}"; return none with { Nav = nav }; }
                Stop("Kein abbaubares Erz erreichbar");
                return none;
            }

            case Phase.Mine:
            {
                if (_target is not { } a) { Current = Phase.Seek; return none; }
                float edge = Vec2.Distance(pos, a.Position) - a.Radius;
                bool fullNow = NearlyFull(u, ship);

                if (fullNow && !chunksNear) { Current = Phase.Sell; Status = "Laderaum voll"; goto case Phase.Sell; }

                if (edge > MiningRules.LaserRange * 0.85f && !fullNow)
                {
                    // Anflug auf einen Punkt kurz vor dem Asteroiden (Hindernisse umfliegen)
                    var stand = a.Position + (pos - a.Position).Normalized() * (a.Radius + MiningRules.LaserRange * 0.6f);
                    var pts = PathPlanner.Plan(pos, stand, u.GetObstacles(sector.Id));
                    Status = "Anflug";
                    return none with { Waypoint = pts.Count > 1 ? pts[1] : stand, Throttle = 1f, Tractor = chunksNear };
                }
                Status = fullNow ? "Brocken einsammeln" : "Abbau";
                return none with { Aim = a, Fire = !fullNow, Tractor = true };
            }

            case Phase.Sell:
            {
                var buyer = TradeActions.BestBuyer(u, ship, u.PlayerSector, pos);
                if (buyer is null) { Stop("Kein Käufer erreichbar"); return none; }
                Status = $"Verkaufe bei {buyer.Name}";
                return none with { Nav = Navigator.ForStation(u, buyer.Id, ship.Id), DockAt = buyer.Id, Tractor = chunksNear };
            }
        }
        return none;
    }

    /// <summary>Nach dem Andocken: Fracht verkaufen und zurück zum Abbau.</summary>
    public SaleResult? OnDocked(Universe u, Ship ship, Station station)
    {
        if (!Enabled) return null;
        var result = TradeActions.SellAll(u, ship, station);
        Current = Phase.Seek; _target = null;
        Status = result.Revenue > 0 ? $"Verkauft für {result.Revenue:0}" : "Nichts verkauft";
        // Konnte nichts verkauft werden (Station pleite o.ä.), nicht endlos weiterfahren
        if (result.Revenue <= 0f && ship.Cargo.Values.Sum() > 0f) Stop("Station kauft nicht – Automatik gestoppt");
        return result;
    }

    static bool NearlyFull(Universe u, Ship ship) =>
        ship.FreeVolumeForForm(u, GoodForm.Solid) < MiningRules.MinChunkUnits * 1.01f;   // Erz ist fest

    /// <summary>Nächster erreichbarer Sektor mit Erzvorrat im Feld.</summary>
    static NavTarget? FindOtherSector(Universe u, Ship ship, Vec2 pos)
    {
        NavTarget? best = null;
        float bestTime = float.MaxValue;
        foreach (var s in u.Sectors.Values)
        {
            if (s.Id == u.PlayerSector) continue;
            foreach (var f in s.Fields.Where(f => Belt.OreIn(s, f) >= MiningRules.MinChunkUnits))
            {
                float t = Travel.Estimate(u, u.PlayerSector, pos, s.Id, f.WorkPoint(ship.Id), ship);
                if (t < bestTime) { bestTime = t; best = new NavTarget(s.Id, f.WorkPoint(ship.Id), $"Feld in {s.Name}"); }
            }
        }
        return best;
    }
}
