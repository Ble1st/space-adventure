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
        // Gerätetest (Phase 26): nur Brocken zählen, die noch in den Laderaum passen — ein Brocken, dessen Form voll ist (Pip: fester Raum 30), prallte
        // ewig ab, und der Automodus wartete 30+ s „Brocken einsammeln“, statt verkaufen zu fliegen
        bool chunksNear = chunks.Chunks.Any(c => Vec2.Distance(c.Position, pos) <= MiningRules.TractorRange && ship.FreeUnits(u, c.Good) > 0.001f);

        // Zielasteroid prüfen
        if (Current == Phase.Mine && !chunks.IsMinable(sector, _target)) { _target = null; Current = Phase.Seek; }
        // Runde 20 (Gerätetest: Auto-Mining an während der Verankerung zeigte kurz „Anker fliegt“): ein bestehender Anker an einem abbaubaren Fels wird übernommen,
        // statt ihn in der Suchphase zu lösen und neu zu schießen
        if (Current == Phase.Seek && ship.AnchorRock != 0 && !NearlyFull(u, ship)
            && sector.Asteroids.FirstOrDefault(x => x.Id == ship.AnchorRock) is { Minable: true } kept && chunks.IsMinable(sector, kept))
        { _target = kept; Current = Phase.Mine; Status = "Abbau"; }
        // Phase 32: die Harpunen hängen an einem anderen (abbaubaren) Fels als dem Anflugziel — dann ist der das Ziel (sonst lösen und neu schießen im Wechsel)
        if (Current == Phase.Mine && ship.AnchorRock != 0 && _target?.Id != ship.AnchorRock && sector.Asteroids.FirstOrDefault(x => x.Id == ship.AnchorRock) is { Minable: true } held) _target = held;
        // Phase 32: am falschen Fels verankert (Ziel gewechselt) oder nicht mehr im Abbau → Anker lösen, bevor weitergeflogen wird
        if (ship.AnchorRock != 0 && (Current != Phase.Mine || _target is null || _target.Id != ship.AnchorRock)) MiningAnchor.Release(u, ship, sector.Id, "released");

        switch (Current)
        {
            case Phase.Seek:
            {
                if (ship.Cargo.Values.Sum() > 0f && NearlyFull(u, ship)) { Current = Phase.Sell; goto case Phase.Sell; }
                var rock = chunks.Nearest(sector, pos);
                if (rock != null) { _target = rock; Current = Phase.Mine; Status = "Abbau"; goto case Phase.Mine; }

                // Nichts mehr im Sektor: Ladung verkaufen oder anderes Feld suchen (Phase 18: ein Rest unter RestUnits zählt nicht als Ladung)
                if (HasLoad(ship)) { Current = Phase.Sell; goto case Phase.Sell; }
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

                if (fullNow && !chunksNear) { Current = Phase.Sell; Status = "Laderaum voll"; MiningAnchor.Release(u, ship, sector.Id, "hold_full"); goto case Phase.Sell; }

                // Phase 29: Kontaktabbau — anfliegen, mit dem Bug auf die Asteroidenmitte ausrichten und sanft andrücken (der Kontakt hält das Schiff an der
                // Oberfläche; unter dem Rammschwellwert kein Schaden); abgetragen wird über OreChunks.Contact
                if (ship.Stats.UsesContact)
                {
                    if (edge > ContactApproachEdge)
                    {
                        var standC = a.Position + (pos - a.Position).Normalized() * (a.Radius + ContactApproachEdge * 0.5f);
                        var ptsC = PathPlanner.Plan(pos, standC, u.GetObstacles(sector.Id));
                        Status = "Anflug";
                        return none with { Waypoint = ptsC.Count > 1 ? ptsC[1] : standC, Throttle = 1f };
                    }
                    // Phase 32: ansetzen → verankern → abbauen (die Trossen halten das Schiff; der sanfte Schub bleibt unter der Abrissschwelle)
                    Status = MiningAnchor.Active(u)
                        ? MiningAnchor.State(u, ship) switch { AnchorState.Held => "Abbau (verankert)", AnchorState.Flying => "Anker fliegt", _ => "Ansetzen" }
                        : "Abbau (Kontakt)";
                    return none with { Waypoint = a.Position, Throttle = ContactPush, Aim = a };
                }

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
        // Konnte nichts verkauft werden (Station pleite o.ä.), nicht endlos weiterfahren. Phase 18: ein Rest unter RestUnits stoppt nicht
        if (result.Revenue <= 0f && HasLoad(ship)) Stop("Station kauft nicht – Automatik gestoppt");
        return result;
    }

    /// <summary>
    /// Phase 18: Fracht unter dieser Menge (in Einheiten, alle Waren zusammen) gilt dem Automodus als Rest, nicht als Ladung: Er fliegt dafür keinen Käufer
    /// an und stoppt nicht, wenn eine Station ihn nicht nimmt — er sucht weiter Erz. Abwerfen bleibt Sache des Spielers (Handelsbereich, Schiffsdetail);
    /// der Rest belegt weniger als eine Einheit Laderaum und wird beim nächsten Verkauf meist mitgenommen (Resttoleranz, <see cref="EconomyRules.SaleRestTolerance"/>).
    /// </summary>
    public const float RestUnits = 1f;

    /// <summary>Phase 29: ab diesem Abstand zur Oberfläche fliegt der Kontaktmodus noch an (darunter: ausrichten und andrücken).</summary>
    public const float ContactApproachEdge = 10f;
    /// <summary>Phase 29: Schub beim Andrücken (Anteil der Höchstgeschwindigkeit; weit unter dem Rammschwellwert).</summary>
    public const float ContactPush = 0.12f;

    /// <summary>Phase 18: Ist mehr als ein Rest an Bord?</summary>
    public static bool HasLoad(Ship ship) => ship.Cargo.Values.Sum() >= RestUnits;

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
