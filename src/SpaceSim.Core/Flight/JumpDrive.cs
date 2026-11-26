using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Flight;

/// <summary>Ergebnis eines ausgeführten Sprungs (Zoll, Meldung) bzw. der Grund der Ablehnung.</summary>
public readonly record struct JumpResult(bool Jumped, SectorId To, Vec2 Position, float Toll, string Message);

/// <summary>
/// Sprungantrieb (SCHIFFSSLOTS.md Abschnitt 4, Fähigkeit <see cref="Capability.Jump"/>): ein Utility-Modul, mit dem ein Schiff ohne Gate in einen anderen
/// Sektor springt. Eine Regel, rein und ohne Zufall (kein <c>Universe.Rng</c>, kein <c>System.Random</c>):
/// <list type="bullet">
/// <item><b>Reichweite</b> in Gate-Hops (<see cref="ShipStats.JumpRange"/>) über das ganze Gate-Netz, auch über geschlossene oder zerstörte Tore
/// (<see cref="Hops"/>; entschieden statt der Kartenlage, die reine Darstellung bleibt).</item>
/// <item><b>Aufladen</b> <see cref="ShipStats.JumpChargeTicks"/> ohne Bewegung (<see cref="EconomyRules.JumpMoveTolerance"/>), dann der Sprung, danach
/// <see cref="ShipStats.JumpCooldownTicks"/> Abklingzeit. Laden kostet <see cref="ShipStats.JumpDraw"/> Leistung aus dem Überschuss; Treibstoff
/// (<see cref="ShipStats.JumpFuel"/>) nur mit Schalter <c>fuelActive</c>.</item>
/// <item><b>Massenschatten</b> (<see cref="ShadowAt"/>): nicht nahe Stationen, Gates und großen Felsen — laden und ankommen.</item>
/// <item><b>Politik</b> wie am Gate, aber ohne Gate (<see cref="GateAccess.EvaluateJump"/>): Abriegelung der Ausreise und gesperrte Einreise weisen ab,
/// Zoll wird gezahlt (sonst Zollflucht), Schmuggelware an der Ankunft ist Schmuggel. Nur ein Tarnfeld stärker als die Sprung-Sensoren
/// (<see cref="EconomyRules.JumpSensorStrength"/>) kommt unbemerkt hinein — die Sperre ist rechtlich, also undicht wie am Gate.</item>
/// <item><b>Ankunft</b> an einem festen Punkt am Sektorrand (<see cref="ArrivalSpot"/>): auf der Seite des Tores, das zum Herkunftssektor führt, sonst in
/// einer aus Schiff und Sektoren abgeleiteten Richtung; die erste Stelle ohne Massenschatten. Die Schonfrist gibt die Ansicht (wie nach einem Gate-Sprung).</item>
/// <item><b>Abbruch</b>: bewegt, angedockt, unterwegs, Gefecht, Massenschatten, Modul aus, Ziel nicht mehr erlaubt, Leistung fehlt.</item>
/// </list>
/// Zustand am Schiff (<see cref="Ship.JumpTarget"/>, <see cref="Ship.JumpCharge"/>, <see cref="Ship.JumpFrom"/>, <see cref="Ship.JumpCooldownUntil"/>),
/// gespeichert und gehasht nur wenn gesetzt. Fortgeschrieben von <see cref="JumpCloakSystem"/>. Nur der Spieler nutzt ihn (die KI nicht).
/// </summary>
public static class JumpDrive
{
    /// <summary>Hat das Schiff einen funktionierenden Sprungantrieb (und ist der Schalter an)?</summary>
    public static bool Fitted(Universe u, Ship ship) => u.Rules.JumpDrives && ship.HasModel && ship.Stats.JumpRange > 0;

    /// <summary>Lädt der Antrieb gerade?</summary>
    public static bool Charging(Ship ship) => ship.JumpTarget.Value != 0;

    /// <summary>Ladefortschritt 0..1.</summary>
    public static float Progress(Ship ship) =>
        !Charging(ship) ? 0f : ship.Stats.JumpChargeTicks <= 0f ? 1f : Math.Clamp(ship.JumpCharge / ship.Stats.JumpChargeTicks, 0f, 1f);

    /// <summary>Restliche Abklingzeit in Ticks (0 = bereit).</summary>
    public static long CooldownLeft(Universe u, Ship ship) => Math.Max(0L, ship.JumpCooldownUntil - u.Tick);

    /// <summary>
    /// Gate-Hops von <paramref name="from"/> nach <paramref name="to"/> über alle Gates, egal ob offen, geschlossen oder zerstört (der Antrieb braucht das
    /// Tor nicht, nur die Nachbarschaft); −1 = nicht verbunden. Breitensuche in Id-Reihenfolge.
    /// </summary>
    public static int Hops(Universe u, SectorId from, SectorId to)
    {
        if (from == to) return 0;
        foreach (var (s, h) in Reachable(u, from, int.MaxValue)) if (s == to) return h;
        return -1;
    }

    /// <summary>Sektoren innerhalb von <paramref name="range"/> Hops (ohne den Startsektor), sortiert nach Hops, dann Id.</summary>
    public static List<(SectorId Sector, int Hops)> Reachable(Universe u, SectorId from, int range)
    {
        var seen = new Dictionary<SectorId, int> { [from] = 0 };
        var queue = new Queue<SectorId>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var s = queue.Dequeue();
            int h = seen[s];
            if (h >= range || !u.Sectors.TryGetValue(s, out var sec)) continue;
            foreach (var gid in sec.Gates.OrderBy(g => g.Value))
                if (u.Gates.TryGetValue(gid, out var g) && u.Gates.TryGetValue(g.Target, out var far) && !seen.ContainsKey(far.Sector))
                {
                    seen[far.Sector] = h + 1;
                    queue.Enqueue(far.Sector);
                }
        }
        return seen.Where(kv => kv.Key != from).Select(kv => (kv.Key, kv.Value)).OrderBy(x => x.Value).ThenBy(x => x.Key.Value).ToList();
    }

    /// <summary>Ziele des Schiffs in Reichweite seines Antriebs (leer ohne Antrieb).</summary>
    public static List<(SectorId Sector, int Hops)> Targets(Universe u, Ship ship) =>
        Fitted(u, ship) ? Reachable(u, ship.Sector, ship.Stats.JumpRange) : new List<(SectorId, int)>();

    /// <summary>Massenschatten an einer Stelle: Grund (deutsch, kurz) oder null = frei.</summary>
    public static string? ShadowAt(Universe u, SectorId sector, Vec2 pos)
    {
        if (!u.Sectors.TryGetValue(sector, out var sec)) return "Sektor unbekannt";
        var r = u.Rules;
        foreach (var sid in sec.Stations)
            if (u.Stations.TryGetValue(sid, out var st) && Vec2.Distance(pos, st.Position) - st.Radius < r.JumpShadowStation) return "Station zu nah";
        foreach (var gid in sec.Gates)
            if (u.Gates.TryGetValue(gid, out var g) && Vec2.Distance(pos, g.Position) < r.JumpShadowGate) return "Tor zu nah";
        foreach (var a in sec.Asteroids)
            if (a.Radius >= r.JumpShadowRockRadius && Vec2.Distance(pos, a.Position) - a.Radius < r.JumpShadowRock) return "Fels zu nah";
        return null;
    }

    /// <summary>Warum kann das Schiff jetzt nicht zum Ziel laden? null = möglich (Politik eingeschlossen).</summary>
    public static string? StartIssue(Universe u, Ship ship, SectorId target)
    {
        if (ReadyIssue(u, ship) is { } ready) return ready;
        if (!u.Sectors.ContainsKey(target)) return "Ziel unbekannt";
        if (target == ship.Sector) return "Ziel ist dieser Sektor";
        int hops = Hops(u, ship.Sector, target);
        if (hops < 0 || hops > ship.Stats.JumpRange) return hops < 0 ? "Ziel nicht verbunden" : $"außer Reichweite ({hops} Sprünge, Antrieb {ship.Stats.JumpRange})";
        var pass = GateAccess.EvaluateJump(u, ship.Sector, target, GateAccess.ProfileOf(u, ship), ship);
        if (!pass.Allowed) return $"verweigert: {pass.Reason}";
        return null;
    }

    /// <summary>
    /// Was hindert den Antrieb gerade unabhängig vom Ziel (Anzeige der Taste)? Abgeschaltet, kein Modul, lädt, kühlt ab, angedockt, unterwegs, Gefecht,
    /// Massenschatten, Treibstoff, Leistung; null = bereit.
    /// </summary>
    public static string? ReadyIssue(Universe u, Ship ship)
    {
        if (!u.Rules.JumpDrives) return "Sprungantrieb abgeschaltet";
        if (!Fitted(u, ship)) return "kein Sprungantrieb eingebaut";
        if (Charging(ship)) return "lädt bereits";
        long cd = CooldownLeft(u, ship);
        if (cd > 0) return $"kühlt ab ({Clock(cd)})";
        if (ship.DockedAt.Value != 0) return "angedockt";
        if (ship.Transit is not null) return "unterwegs";
        if (u.Fights.IsEngaged(ship.Id)) return "im Gefecht";
        if (ShadowAt(u, ship.Sector, ship.Position) is { } shadow) return $"Massenschatten: {shadow}";
        if (FuelIssue(u, ship) is { } fuel) return fuel;
        float surplus = ship.Stats.PowerOutput - ship.Stats.PowerDraw;
        if (surplus + 1e-4f < ship.Stats.JumpDraw) return $"zu wenig Leistung ({MathF.Max(0f, surplus):0.#} von {ship.Stats.JumpDraw:0.#})";
        return null;
    }

    static string? FuelIssue(Universe u, Ship ship) =>
        u.Rules.FuelActive && ship.Stats.JumpFuel > 0f && ship.TotalFuel + 1e-4f < ship.Stats.JumpFuel ? $"zu wenig Treibstoff ({ship.TotalFuel:0} von {ship.Stats.JumpFuel:0})" : null;

    /// <summary>Aufladen zum Ziel beginnen; liefert den Grund der Ablehnung oder null.</summary>
    public static string? Start(Universe u, Ship ship, SectorId target)
    {
        if (StartIssue(u, ship, target) is { } issue) return issue;
        ship.JumpTarget = target;
        ship.JumpCharge = 0f;
        ship.JumpFrom = ship.Position;
        ship.RecomputeStats(u);   // Ladeleistung zählt jetzt in der Dauerlast
        u.Events.Publish(new JumpChargeStarted(ship.Id, ship.Sector, target, ship.Stats.JumpChargeTicks));
        return null;
    }

    /// <summary>Aufladen abbrechen (nichts, wenn nicht geladen wird).</summary>
    public static void Abort(Universe u, Ship ship, string key, string reason)
    {
        if (!Charging(ship)) return;
        var to = ship.JumpTarget;
        ship.JumpTarget = default;
        ship.JumpCharge = 0f;
        ship.JumpFrom = default;
        ship.RecomputeStats(u);
        u.Events.Publish(new JumpChargeAborted(ship.Id, to, key, reason));
    }

    /// <summary>
    /// Ein Schritt des Aufladens (<paramref name="ticks"/> Spielzeit): prüft die Abbruchgründe, zählt hoch und springt, sobald voll geladen. Liefert das
    /// Ergebnis eines ausgeführten Sprungs, sonst null.
    /// </summary>
    public static JumpResult? Step(Universe u, Ship ship, float ticks)
    {
        if (!Charging(ship)) return null;
        var (key, why) = !u.Rules.JumpDrives || ship.Stats.JumpRange <= 0 ? ("module", "Sprungantrieb ausgefallen")
            : ship.DockedAt.Value != 0 ? ("docked", "angedockt")
            : ship.Transit is not null ? ("moved", "Schiff fliegt")
            : u.Fights.IsEngaged(ship.Id) ? ("combat", "Gefecht")
            : Vec2.Distance(ship.Position, ship.JumpFrom) > u.Rules.JumpMoveTolerance ? ("moved", "Schiff bewegt")
            : ShadowAt(u, ship.Sector, ship.Position) is { } sh ? ("shadow", $"Massenschatten: {sh}")
            : ship.Stats.PowerOutput + 1e-4f < ship.Stats.PowerDraw ? ("power", "zu wenig Leistung")
            : ((string?)null, "");
        if (key is not null) { Abort(u, ship, key, why); return null; }
        ship.JumpCharge += MathF.Max(0f, ticks);
        if (ship.JumpCharge + 1e-4f < ship.Stats.JumpChargeTicks) return null;
        return Execute(u, ship);
    }

    /// <summary>Der Sprung selbst (nach vollem Laden): Politik, Treibstoff, Zoll, Ankunftsort, Sektorwechsel, Abklingzeit, Ereignis.</summary>
    static JumpResult Execute(Universe u, Ship ship)
    {
        var from = ship.Sector;
        var to = ship.JumpTarget;
        var pass = GateAccess.EvaluateJump(u, from, to, GateAccess.ProfileOf(u, ship), ship);
        if (!pass.Allowed)
        {
            string msg = $"Sprung verweigert: {pass.Reason}";
            if (pass.Crime is { } crime && pass.Authority is { } auth && u.Rules.SecurityActive)
            {
                Wanted.Report(u, ship.Owner, auth, crime);
                ThreatMap.Report(u, to, u.Rules.SmugglingThreat);
                msg += $" — {SecurityNames.Crime(crime)} gemeldet";
            }
            Abort(u, ship, "denied", msg);
            return new JumpResult(false, to, ship.Position, 0f, msg);
        }
        if (FuelIssue(u, ship) is { } fuelIssue) { Abort(u, ship, "fuel", fuelIssue); return new JumpResult(false, to, ship.Position, 0f, fuelIssue); }
        float paid = 0f;
        string message = $"Sprung nach {u.Sectors[to].Name}";
        if (pass.Toll > 0f)
        {
            float due = pass.Toll;
            string who = pass.TollTo is { } f && u.Factions.TryGetValue(f, out var fac) ? fac.Name : "Sektor";
            if ((!u.Rules.SecurityActive && u.WalletOf(ship).Credits < due) || GateAccess.BrokeAtToll(u, ship))
            {
                Abort(u, ship, "denied", $"Zoll {due:0} Cr, zu wenig Credits");
                return new JumpResult(false, to, ship.Position, 0f, $"Sprung verweigert: Zoll {due:0} Cr, zu wenig Credits");
            }
            paid = GateAccess.PayToll(u, ship, default(GateId), pass);
            if (paid + 0.01f < due && pass.TollTo is { } authority && u.Rules.SecurityActive)
            {
                Wanted.Report(u, ship.Owner, authority, "toll_evasion", extraFine: due - paid);
                message += $" — Zollflucht: {paid:0} von {due:0} Cr an {who}";
            }
            else message += $" — Zoll {paid:0} Cr an {who}";
        }
        if (u.Rules.FuelActive && ship.Stats.JumpFuel > 0f) ConsumeFuel(ship, ship.Stats.JumpFuel);
        var pos = ArrivalSpot(u, ship, from, to);
        Mining.MiningAnchor.Release(u, ship, from, "left");
        u.MoveShip(ship, to);
        ship.Position = pos;
        if (ship.Id == u.PlayerShip) u.PlayerSector = to;
        ship.JumpTarget = default;
        ship.JumpCharge = 0f;
        ship.JumpFrom = default;
        ship.JumpCooldownUntil = u.Tick + (long)MathF.Ceiling(ship.Stats.JumpCooldownTicks);
        ship.RecomputeStats(u);
        u.Events.Publish(new ShipDriveJumped(ship.Id, from, to, pos, paid, message));
        return new JumpResult(true, to, pos, paid, message);
    }

    static void ConsumeFuel(Ship ship, float amount)
    {
        foreach (var g in ship.Fuel.Keys.OrderBy(k => k.Value).ToList())
        {
            if (amount <= 0f) break;
            float take = MathF.Min(ship.Fuel[g], amount);
            ship.Fuel[g] -= take;
            amount -= take;
            if (ship.Fuel[g] <= 1e-5f) ship.Fuel.Remove(g);
        }
    }

    /// <summary>
    /// Ankunftsort im Zielsektor, rein: Ring mit <see cref="EconomyRules.JumpArrivalRing"/> × Sektorausdehnung um die Mitte; Startrichtung zum Tor, das zum
    /// Herkunftssektor führt (sonst aus Schiffs- und Sektor-Ids abgeleitet), dann 16 Richtungen und vier Ringgrößen — die erste Stelle ohne Massenschatten.
    /// </summary>
    public static Vec2 ArrivalSpot(Universe u, Ship ship, SectorId from, SectorId to)
    {
        var sec = u.Sectors[to];
        float baseR = MathF.Max(80f, sec.Extent * u.Rules.JumpArrivalRing);
        float start;
        var gate = sec.Gates.OrderBy(g => g.Value).Select(g => u.Gates.GetValueOrDefault(g)).FirstOrDefault(g => g is not null && u.Gates.TryGetValue(g.Target, out var far) && far.Sector == from);
        if (gate is not null && gate.Position.Length > 1f) start = MathF.Atan2(gate.Position.Y, gate.Position.X);
        else start = ((ship.Id.Value * 7 + from.Value * 3 + to.Value) % 16) * MathF.PI / 8f;
        foreach (float ring in new[] { 1f, 0.85f, 1.15f, 0.55f })
            for (int k = 0; k < 16; k++)
            {
                // abwechselnd links und rechts der Startrichtung
                int step = (k + 1) / 2 * (k % 2 == 0 ? 1 : -1);
                var p = Vec2.FromAngle(start + step * MathF.PI / 8f) * (baseR * ring);
                if (ShadowAt(u, to, p) is null) return p;
            }
        return Vec2.FromAngle(start) * baseR;
    }

    /// <summary>Zeitangabe m:ss aus Ticks.</summary>
    public static string Clock(long ticks)
    {
        long s = (long)MathF.Ceiling(ticks / (float)SimClock.TicksPerSecond);
        return $"{s / 60}:{s % 60:00}";
    }
}
