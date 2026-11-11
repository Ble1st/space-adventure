using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Durchsetzung geänderter Gate-Zugänge für laufende Reisen (Phase 10, SCHIFFSSLOTS.md Abschnitt 5 „Schiffe unterwegs“). Reisen sind abstrakte
/// Fahrpläne; ändert sich eine Sperre oder ein Gate-Zustand, prüft <see cref="Revalidate"/> jedes reisende KI-Schiff: Liegt vor ihm ein Gate, das
/// sein Profil jetzt abweist, wird die Reise vom aktuellen Ort aus neu geplant; geht das nicht, hält das Schiff an Ort und Stelle an, verliert seinen
/// Auftrag (keine Geisterreservierung, kein Geisterauftrag hinter einem geschlossenen Gate) und behält seine Fracht.
/// </summary>
public static class AccessEnforcement
{
    /// <summary>Prüft alle laufenden Reisen gegen den aktuellen Zugang (feste Reihenfolge nach Id). Liefert (neu geplant, angehalten).</summary>
    public static (int Rerouted, int Stopped) Revalidate(Universe u)
    {
        int rerouted = 0, stopped = 0;
        foreach (var ship in u.Ships.Values.Where(s => s.Transit is not null && s.Id != u.PlayerShip).OrderBy(s => s.Id.Value).ToList())
        {
            var t = ship.Transit!;
            var profile = GateAccess.ProfileOf(u, ship);
            Gate? blockedAt = null;
            string reason = "";
            for (int i = 0; i + 1 < t.Legs.Count; i++)
            {
                var a = t.Legs[i];
                var b = t.Legs[i + 1];
                if (a.Sector == b.Sector || u.Tick >= a.EndTick) continue;   // kein Sprung oder schon hindurch
                var gate = GateBetween(u, a.Sector, b.Sector, a.To);
                if (gate is null) continue;
                var pass = GateAccess.Evaluate(u, gate, profile, ship);
                if (pass.Allowed) continue;
                blockedAt = gate; reason = pass.Reason;
                break;
            }
            if (blockedAt is null) continue;

            var (sector, pos) = Where(t, u.Tick);
            var legs = Travel.Plan(u, u.Tick, sector, pos, t.ToSector, t.ToPosition, ship);
            if (legs is not null)
            {
                // Umweg: Mehrstrecke kostet Treibstoff (gegenüber dem Rest der alten Reise)
                float extra = legs.Sum(l => Vec2.Distance(l.From, l.To)) - Remaining(t, u.Tick);
                if (extra > 0f) Refuel.Consume(u, ship, extra);
                u.StartTransit(ship, new Transit { StartTick = u.Tick, ArrivalTick = legs[^1].EndTick, ToSector = t.ToSector, ToPosition = t.ToPosition, Legs = legs });
                u.Events.Publish(new ShipRerouted(ship.Id, blockedAt.Id, reason));
                rerouted++;
            }
            else
            {
                Stop(u, ship, sector, pos);
                u.Events.Publish(new RouteBlocked(ship.Id, t.ToSector, reason));
                stopped++;
            }
        }
        return (rerouted, stopped);
    }

    /// <summary>
    /// Hält ein Schiff an seinem aktuellen Ort an (ohne Ankunftslogik der KI-Systeme, Fracht bleibt) und gibt seinen Auftrag frei; es denkt im
    /// nächsten Takt neu.
    /// </summary>
    public static void Stop(Universe u, Ship ship, SectorId sector, Vec2 pos)
    {
        u.StartTransit(ship, new Transit { StartTick = u.Tick, ArrivalTick = u.Tick, ToSector = sector, ToPosition = pos, Legs = { new Leg(sector, pos, pos, u.Tick, u.Tick + 1) } });
        u.FinishTransit(ship);
        Release(u, ship);
    }

    /// <summary>Auftrag und Phasen freigeben (Fracht bleibt an Bord), Leerlauf beginnen, bald neu denken.</summary>
    public static void Release(Universe u, Ship ship)
    {
        ship.Order = null;
        ship.Phase = TradePhase.Idle;
        ship.MinePhase = MinePhase.Idle;
        if (ship.IdleSince < 0) ship.IdleSince = u.Tick;
        ship.NextThinkTick = u.Tick + TraderSystem.ThinkInterval;
    }

    /// <summary>Gate im Sektor <paramref name="from"/>, das nach <paramref name="to"/> führt und bei <paramref name="at"/> liegt.</summary>
    public static Gate? GateBetween(Universe u, SectorId from, SectorId to, Vec2 at)
    {
        Gate? best = null;
        float bestD = float.MaxValue;
        foreach (var gid in u.Sectors[from].Gates)
        {
            var g = u.Gates[gid];
            if (!u.Gates.TryGetValue(g.Target, out var far) || far.Sector != to) continue;
            float d = Vec2.Distance(g.Position, at);
            if (d < bestD) { bestD = d; best = g; }
        }
        return best;
    }

    /// <summary>Ort eines reisenden Schiffs zu einem Tick (aktuelle Teilstrecke interpoliert).</summary>
    public static (SectorId Sector, Vec2 Position) Where(Transit t, long tick)
    {
        if (t.Legs.Count == 0) return (t.ToSector, t.ToPosition);
        if (tick < t.Legs[0].StartTick) return (t.Legs[0].Sector, t.Legs[0].From);
        foreach (var leg in t.Legs)
        {
            if (tick < leg.StartTick || tick >= leg.EndTick) continue;
            float f = (float)((tick - leg.StartTick) / (double)Math.Max(1, leg.EndTick - leg.StartTick));
            return (leg.Sector, Vec2.Lerp(leg.From, leg.To, f));
        }
        return (t.ToSector, t.ToPosition);
    }

    /// <summary>Rest der Strecke einer Reise ab einem Tick (Gate-Sprünge frei).</summary>
    static float Remaining(Transit t, long tick)
    {
        float rest = 0f;
        foreach (var leg in t.Legs)
        {
            if (tick >= leg.EndTick) continue;
            float len = Vec2.Distance(leg.From, leg.To);
            if (tick <= leg.StartTick) rest += len;
            else rest += len * (1f - (float)((tick - leg.StartTick) / (double)Math.Max(1, leg.EndTick - leg.StartTick)));
        }
        return rest;
    }
}
