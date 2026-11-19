using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using SpaceSim.Core.Weapons;

namespace SpaceSim.PlayerBot;

/// <summary>
/// Spielerpfade ohne Ansicht: Zeit vergeht nur über SimulationRunner-Ticks; die Reisezeit des aktiven Schiffs ist die echte Spielerflugzeit
/// (<see cref="PlayerTrip.Seconds"/> × 4 Ticks/s), Tore laufen durch <see cref="GateAccess.TryPlayerJump"/> (Sperren, Zoll, Verbrechen).
/// Es wird nichts gesetzt, was ein Spieler nicht auch bekäme (kein Geld, keine Besitzer, keine Beziehungen, kein Lager).
/// </summary>
public sealed class Pilot
{
    public readonly Universe U;
    public readonly SimulationRunner Runner = new();
    public readonly OreChunks Chunks;
    public readonly Log Log;
    public Action<Universe>? OnTick;
    public Ledger? Led;
    public T Book<T>(string label, Func<T> f) => Led is null ? f() : Led.Book(label, f);
    public long TicksFlown, TicksMined, TicksDocked, TicksFighting;
    public int Jumps; public float TollsPaid;
    public bool Alive = true;
    public volatile string Where = "";

    public readonly StationServices Services = StationServices.Default();

    public Pilot(Universe u, Log log, ulong seed) { U = u; Log = log; Chunks = new OreChunks(seed: seed + 7); }

    public Ship Me => U.Ships[U.PlayerShip];
    public float Wallet => U.PlayerWallet.Credits;
    public Station? Here => Me.DockedAt.Value != 0 && U.Stations.TryGetValue(Me.DockedAt, out var s) ? s : null;

    // ───────── Zeit ─────────

    /// <summary>Ein Tick der Welt; Ereignisse/Metrik über <see cref="OnTick"/>. Gibt false zurück, wenn der Lauf zu Ende ist.</summary>
    public void Step()
    {
        Led?.BeginSim();
        Runner.Step(U);
        Led?.EndSim();
        OnTick?.Invoke(U);
    }

    public void Run(long ticks) { for (long i = 0; i < ticks; i++) { Step(); if (Danger()) return; } }

    /// <summary>Warnung/Gefecht beim aktiven Schiff: unterbricht lange Wartezeiten.</summary>
    public bool Danger() => U.Fights.Of(U.PlayerShip) is not null || U.PendingEncounters.Any(p => p.Target == U.PlayerShip);

    // ───────── Flug ─────────

    static long TicksOf(float seconds) => float.IsInfinity(seconds) ? 4000 : Math.Max(1, (long)MathF.Ceiling(seconds * SimClock.TicksPerSecond));

    /// <summary>Flug innerhalb des Sektors zu einem Punkt; die Zeit vergeht in Ticks. false = unterbrochen (Gefahr).</summary>
    public bool FlyTo(Vec2 target)
    {
        var me = Me;
        float sec = PlayerTrip.Seconds(U, me.Sector, me.Position, me.Sector, target, me) - PlayerTrip.DockSeconds;
        sec = MathF.Max(0.5f, sec);
        float dist = Vec2.Distance(me.Position, target);
        long n = TicksOf(sec);
        for (long i = 0; i < n; i++)
        {
            Step();
            if (Danger()) { me.Position = Vec2.Lerp(me.Position, target, (i + 1f) / n); return false; }
        }
        PlayerFuel.ConsumeFlown(U, me, dist * 1.1f);
        me.Position = target; TicksFlown += n;
        return true;
    }

    /// <summary>Zu einem Sektor: Tor für Tor mit Kontrollpunkt (Zoll, Sperre). false = abgewiesen/unterbrochen.</summary>
    public bool GoToSector(SectorId target, out string why)
    {
        why = "";
        for (int guard = 0; guard < 40; guard++)
        {
            var me = Me;
            if (me.Sector == target) return true;
            if (me.DockedAt.Value != 0) Undock();
            var route = U.FindRoute(me.Sector, target, GateAccess.ProfileOf(U, me));
            if (route is null || route.Count == 0) { why = GateAccess.BlockReason(U, me, me.Sector, target) ?? "kein Weg"; return false; }
            var gate = U.Gates[route[0]];
            if (!FlyTo(gate.Position)) { why = "Gefahr"; return false; }
            var chk = Book("Zoll/Torsprung", () => GateAccess.TryPlayerJump(U, me, gate));
            if (!chk.Allowed) { why = chk.Message; return false; }
            TollsPaid += chk.Toll;
            var far = U.Gates[gate.Target];
            JumpDrive.Abort(U, me, "moved", "Torsprung");
            U.MoveShip(me, far.Sector); U.PlayerSector = far.Sector;
            var inward = (far.Position * -1f).Normalized();
            me.Position = far.Position + inward * 22f; Jumps++;
            Step();   // 1 s Sprung/Grace ≈ ein Tick
        }
        why = "zu viele Sprünge"; return false;
    }

    public bool GoToStation(Station st, out string why)
    {
        why = "";
        if (Me.DockedAt == st.Id) return true;
        if (!GoToSector(st.Sector, out why)) return false;
        var me = Me;
        if (me.DockedAt.Value != 0) Undock();
        if (!FlyTo(st.DockPoint(me.Id))) { why = "Gefahr"; return false; }
        if (Docking.Issue(U, st, me) is { } iss) { why = iss; return false; }
        PlayerFleet.Dock(U, me, st);
        Step();
        return true;
    }

    public void Undock()
    {
        var me = Me;
        if (me.DockedAt.Value == 0) return;
        PlayerFleet.Undock(U, me);
    }

    // ───────── Dienste ─────────

    public ServiceResult Do(Station st, IStationService svc, string offer, GoodId good = default, float units = 0f) =>
        Book(svc.Key + ":" + (offer.Contains(':') ? offer[..offer.IndexOf(':')] : offer), () => svc.Execute(U, st, Me, new ServiceRequest(offer, good, units)));

    public ServiceOffer? Offer(Station st, IStationService svc, string key)
    {
        foreach (var o in svc.Offers(U, st, Me)) if (o.Key == key) return o;
        return null;
    }

    public IStationService? Svc(Station st, string key) => Services.ForStation(U, st).FirstOrDefault(s => s.Key == key);

    /// <summary>Alles Verkaufbare beim aktuellen Dock verkaufen (sell_all), den Rest ggf. abwerfen. Liefert Erlös.</summary>
    public float SellAllHere()
    {
        var st = Here; if (st is null) return 0f;
        float before = Wallet;
        var svc = Svc(st, "trade"); if (svc is null) return 0f;
        if (Offer(st, svc, "sell_all") is { Amount: > 0f }) Do(st, svc, "sell_all");
        return Wallet - before;
    }

    public void Refuel()
    {
        var st = Here; var me = Me;
        if (st is null || !U.Rules.FuelActive || me.Stats.FuelCapacity <= 0f) return;
        if (me.TotalFuel > me.Stats.FuelCapacity * 0.6f) return;
        if (Svc(st, "refuel") is { } svc && Offer(st, svc, "refuel:fill") is { Amount: > 0f }) Do(st, svc, "refuel:fill");
    }

    public void RepairHere()
    {
        var st = Here; var me = Me;
        if (st is null) return;
        if (Svc(st, "repair") is not { } svc) return;
        if (me.Hull < me.MaxHull * 0.8f || me.Systems.Burning.Count > 0 || Damaged(me))
            foreach (var key in new[] { "repair:all", "repair:full", "repair:emergency" })
                if (Offer(st, svc, key) is { Amount: > 0f }) { Do(st, svc, key); break; }
        if (Svc(st, "rearm") is { } rs)
            foreach (var key in new[] { "rearm:fill", "rearm:missiles" })
                if (Offer(st, rs, key) is { Amount: > 0f }) Do(st, rs, key);
    }

    static bool Damaged(Ship s) => s.Systems.Condition.Any(c => c < 0.7f);

    /// <summary>Nächstgelegene Station mit Dienst (Sprünge, dann Entfernung), erreichbar mit dem Profil des Schiffs.</summary>
    public Station? NearestWith(string service, Func<Station, bool>? extra = null)
    {
        var me = Me;
        Station? best = null; double bestScore = double.MaxValue;
        foreach (var st in U.StationsById())
        {
            if (st.IsBuildSite || (extra is not null && !extra(st))) continue;
            if (!Services.ForStation(U, st).Any(s => s.Key == service)) continue;
            if (Docking.Issue(U, st, me) is not null) continue;
            var route = U.FindRoute(me.Sector, st.Sector, GateAccess.ProfileOf(U, me));
            if (route is null) continue;
            double sc = route.Count * 1000 + Vec2.Distance(me.Position, st.Position);
            if (sc < bestScore) { bestScore = sc; best = st; }
        }
        return best;
    }

    // ───────── Gefecht ─────────

    /// <summary>Spielergefecht: feuert jede Runde alle Waffen auf den nächsten Gegner; ergibt sich unbewaffnet oder bei kritischer Hülle.</summary>
    public void Fight(int maxTicks = 400)
    {
        var me = Me; int t = 0;
        while (Danger() && t++ < maxTicks && Alive)
        {
            me = Me;
            var enemies = PlayerCombat.Enemies(U, me);
            var slots = PlayerGunnery.WeaponSlots(U, me);
            bool armed = slots.Count > 0;
            if ((!armed || me.Hull < me.MaxHull * 0.2f) && PlayerSurrender.Issue(U) is null)
            {
                var r = Book("Ergeben (Strafe)", () => PlayerSurrender.Surrender(U));
                Log.Event($"ergibt sich: {r?.ToString() ?? "-"}");
                Step(); continue;
            }
            if (enemies.Count > 0 && armed)
            {
                var tgt = PlayerCombat.NextEnemy(U, me, default);
                if (tgt.Value != 0 && PlayerCombat.PositionOf(U, tgt) is { } tp)
                {
                    var dir = (tp.Pos - me.Position); float dist = dir.Length;
                    float maxRange = slots.Select(s => Gunnery.WeaponAt(U, me, s)).Where(m => m is not null).Select(m => WeaponProfile.Of(m!).Range).DefaultIfEmpty(40f).Min();
                    if (dist > maxRange * 0.7f && dist > 1f) me.Position += dir * (MathF.Min(dist - maxRange * 0.6f, 11f) / dist);   // 45 m/s × 0,25 s
                    float bearing = FiringArc.Bearing(dir.Length > 0.01f ? dir.Normalized() : new Vec2(0, -1));
                    foreach (var s in slots) { PlayerGunnery.TryFire(U, me, s, tgt, bearing); }
                }
            }
            Step(); TicksFighting++;
        }
    }

    public override string ToString() => $"Tick {U.Tick}";
}

public sealed class Log
{
    readonly StreamWriter _w; readonly Func<long> _tick;
    public Log(string path, Func<long> tick) { _w = new StreamWriter(path) { AutoFlush = true }; _tick = tick; }
    public void Event(string s) => _w.WriteLine($"[{_tick(),8}] {s}");
}
