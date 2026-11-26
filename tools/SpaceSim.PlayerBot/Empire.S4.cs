using SpaceSim.Core.Combat;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.PlayerBot;

/// <summary>
/// Sonnet-Test 4 (--s4): Eroberungskampagne, die die bekannten Lücken bewusst umgeht.
/// Logistik: Kampfschiffe unter 30 % Munition fliegen selbst zu einer eigenen/neutralen Werft/Polizeiwache und laden dort nach (Dienst rearm).
/// Halten: nach einer Eroberung bleiben bis zu 3 Schiffe als Wache (GuardCommand), bis der Sektor kippt.
/// Ziele: tragende Stationen zuerst (Firmensitz, Sektor-HQ, Werft, HQ).
/// Ansehen: nur eine Fraktion im Krieg; die anderen bekommen Spenden/Bündnisse (Diplo läuft auch während der Kampagne).
/// </summary>
public sealed partial class Empire
{
    public static bool S4;
    public static float AmmoReturn = 0.30f;
    public readonly List<string> S4Notes = new();
    readonly Dictionary<ShipId, StationId> _resup = new();
    readonly Dictionary<ShipId, StationId> _garrison = new();
    readonly HashSet<ShipId> _s4Free = new();
    readonly Dictionary<StationId, long> _supFail = new();
    readonly Dictionary<ShipId, long> _dry = new(); public int DryVisits, AmmoTrips; bool Dry(Ship s) => _dry.TryGetValue(s.Id, out var t) && U.Tick < t; long _s4Next; public int ResupplyTrips, Rearms, GuardPosts, GuardsReleased, AttackOrders;
    void S4Note(string s) { S4Notes.Add($"[{U.Tick,8}] {s}"); Note("S4: " + s); }

    /// <summary>Wert der Station als Ziel: kleiner = früher. Firmensitz, Sektor-HQ, Werft, HQ, Rest.</summary>
    int Prio(Station s)
    {
        if (!U.StationTypes.TryGetValue(s.TypeKey, out var t)) return 9;
        if (t.Government == GovernmentKind.CompanySeat) return 0;
        if (t.Government == GovernmentKind.SectorHq) return 1;
        if (s.TypeKey == "shipyard") return 2;
        if (t.Government == GovernmentKind.Headquarters) return 3;
        if (t.Government == GovernmentKind.Police) return 4;
        return 5 + (t.Influence >= 1.5f ? 0 : 1);
    }

    int Guns(Station s) => U.StationTypes.TryGetValue(s.TypeKey, out var t) ? (t.Defense?.Guns.Count ?? 0) : 0;
    float HullOf(Station s) => U.StationTypes.TryGetValue(s.TypeKey, out var t) ? (t.Defense?.Hull ?? 0f) : 0f;

    bool Warship(Ship s) => !s.IsRental() && s.Has(Capability.Combat) && U.ShipModels.TryGetValue(s.ModelKey, out var m) && m.Jobs.Count == 0 && s.Role != ShipRole.Trader && s.Role != ShipRole.Miner;

    float AmmoFrac(Ship s)
    {
        if (s.Stats.AmmoCapacity > 0f) return s.AmmoLoaded / s.Stats.AmmoCapacity;
        if (s.Stats.MissileCapacity > 0f) return Missiles.Loaded(U, s) / s.Stats.MissileCapacity;
        return 1f;
    }

    /// <summary>Nächste Station mit Nachladedienst, die das Schiff anfliegen darf und die kein Kriegsgegner besitzt (eigene bevorzugt).</summary>
    Station? SupplyFor(Ship s)
    {
        Station? best = null; double bs = double.MaxValue;
        foreach (var st in U.StationsById())
        {
            if (st.IsBuildSite || (_supFail.TryGetValue(st.Id, out var ft) && U.Tick - ft < 6000)) continue;
            if (!U.StationTypes.TryGetValue(st.TypeKey, out var t) || !t.Services.Contains("rearm")) continue;
            var f = U.FactionOf(st);
            if (f is { } ff && ff != U.PlayerFaction && (Wars.PlayerWar(U, ff) is not null || Relations.Effective(U, U.PlayerActor, new ActorId(ff.Value)) < 0.05f)) continue;
            if (Docking.Issue(U, st, s) is not null) continue;
            var route = U.FindRoute(s.Sector, st.Sector, GateAccess.ProfileOf(U, s));
            if (route is null) continue;
            var ammo = SpaceSim.Core.Combat.Rearm.AmmoGood(U);
            if (ammo is not null && st.Market.TryGetValue(ammo.Id, out var me) && me.Amount < 12f) continue;
            double sc = route.Count * 1000 + Vec2.Distance(s.Position, st.Position) - (st.Owner == U.PlayerActor ? 800 : 0);
            if (sc < bs) { bs = sc; best = st; }
        }
        return best;
    }

    void LoadUp(Ship s, Station st)
    {
        var rs = P.Services.ForStation(U, st).FirstOrDefault(x => x.Key == "rearm");
        var rp = P.Services.ForStation(U, st).FirstOrDefault(x => x.Key == "repair");
        if (rp is not null && RepairNeeded(s)) foreach (var k in new[] { "repair:all", "repair:full" }) { var o = rp.Offers(U, st, s).FirstOrDefault(x => x.Key == k); if (o is { Amount: > 0f }) { P.Book("Nachschub", () => rp.Execute(U, st, s, new SpaceSim.Core.Services.ServiceRequest(k, default, 0f))); break; } }
        if (rs is null) return;
        foreach (var k in new[] { "rearm:fill", "rearm:missiles" })
        {
            var o = rs.Offers(U, st, s).FirstOrDefault(x => x.Key == k);
            if (o is { Amount: > 0f }) { var r = P.Book("Nachschub", () => rs.Execute(U, st, s, new SpaceSim.Core.Services.ServiceRequest(k, default, 0f))); if (r.Success) Rearms++; }
        }
    }
    static bool RepairNeeded(Ship s) => s.Hull < s.MaxHull * 0.8f || s.Systems.Condition.Any(c => c < 0.7f);

    /// <summary>Logistik und Wachen; wird vom Bot in jedem Schritt und in der Belagerungsschleife aufgerufen.</summary>
    void S4Tick()
    {
        if (!S4 || U.Tick < _s4Next || U.PlayerFaction is not { } me) return; _s4Next = U.Tick + 50;
        // 1) Nachschubfahrten
        foreach (var kv in _resup.ToList())
        {
            if (!U.Ships.TryGetValue(kv.Key, out var s) || !U.Stations.TryGetValue(kv.Value, out var st)) { _resup.Remove(kv.Key); continue; }
            if (s.DockedAt == st.Id)
            {
                LoadUp(s, st);
                if (AmmoFrac(s) < AmmoReturn) { _dry[s.Id] = U.Tick + 8000; DryVisits++; if (DryVisits <= 25) S4Note($"Nachschub leer bei {st.Name}: #{s.Id.Value} {s.ModelKey} bleibt bei {AmmoFrac(s):0.00} (Regal {AmmoStock(st):0})"); _resup.Remove(kv.Key); if (s.Command is not null) Commands.Cancel(U, s, "Regal leer"); }
                else if (AmmoFrac(s) > 0.6f || U.Tick - (s.CommandSince) > 40000) { _resup.Remove(kv.Key); if (s.Command is not null) Commands.Cancel(U, s, "nachgeladen"); }
                else { _supFail[st.Id] = U.Tick; var alt = SupplyFor(s); if (alt is not null && alt.Id != st.Id) { _resup[kv.Key] = alt.Id; Commands.Give(U, s, new GoToCommand(alt.Sector, alt.DockPoint(s.Id), alt.Id), out _, ignoreCapacity: true); } else _resup.Remove(kv.Key); }
            }
            else if (s.Command is null && s.Transit is null) Commands.Give(U, s, new GoToCommand(st.Sector, st.DockPoint(s.Id), st.Id), out _, ignoreCapacity: true);
        }
        // 2) Kampfschiffe mit leerem Magazin zur Versorgung schicken (Wache und Nachschub ausgenommen)
        foreach (var s in PlayerFleet.Owned(U))
        {
            if (s.Id == U.PlayerShip || _resup.ContainsKey(s.Id) || Dry(s) || !Warship(s) || s.Stats.AmmoCapacity + s.Stats.MissileCapacity <= 0f) continue;
            if (s.DockedAt.Value != 0 && s.Command is null) continue;
            if (AmmoFrac(s) >= AmmoReturn) continue;
            var sup = SupplyFor(s);
            if (sup is null) { _dry[s.Id] = U.Tick + 8000; continue; }
            if (s.Command is not null) Commands.Cancel(U, s, "Munition unter 30 %");
            if (Commands.Give(U, s, new GoToCommand(sup.Sector, sup.DockPoint(s.Id), sup.Id), out _, ignoreCapacity: true)) { _resup[s.Id] = sup.Id; ResupplyTrips++; if (ResupplyTrips <= 30 || ResupplyTrips % 20 == 0) S4Note($"Nachschubfahrt #{s.Id.Value} {s.ModelKey} ({AmmoFrac(s):0.00} Munition) → {sup.Name} [{U.Sectors[sup.Sector].Name}] (Fahrt Nr. {ResupplyTrips})"); }
        }
        // 3) Wachen: nur solange die Station mein ist und der Sektor nicht mir gehört
        foreach (var kv in _garrison.ToList())
        {
            if (!U.Ships.TryGetValue(kv.Key, out var g)) { _garrison.Remove(kv.Key); continue; }
            if (!U.Stations.TryGetValue(kv.Value, out var st) || st.Owner != U.PlayerActor) { _garrison.Remove(kv.Key); if (g.Command is GuardCommand) Commands.Cancel(U, g, "Station nicht mehr mein"); continue; }
            if (U.Sectors[st.Sector].Owner == me) { _garrison.Remove(kv.Key); GuardsReleased++; if (g.Command is GuardCommand) Commands.Cancel(U, g, "Sektor gekippt"); S4Note($"Wache #{g.Id.Value} entlassen: Sektor {U.Sectors[st.Sector].Name} gehört mir"); }
        }
    }

    /// <summary>Nach einer Eroberung bis zu 3 Schiffe der Kampfgruppe als Wache an die Station.</summary>
    void Garrison(Station st)
    {
        if (!S4) return;
        if (Prio(st) > 3 || _garrison.Count > 14) return;
        int have = _garrison.Count(kv => kv.Value == st.Id), want = 2 - have;
        if (want <= 0) return;
        var cand = PlayerFleet.Owned(U).Where(s => s.Id != U.PlayerShip && Warship(s) && !_resup.ContainsKey(s.Id) && !_garrison.ContainsKey(s.Id) && s.Sector == st.Sector && s.Hull > s.MaxHull * 0.5f && AmmoFrac(s) > 0.4f)
            .OrderByDescending(s => s.Stats.WeaponPower + s.Stats.Hull * 0.02f).ToList();
        if (cand.Count < want) cand.AddRange(PlayerFleet.Owned(U).Where(s => s.Id != U.PlayerShip && Warship(s) && !_resup.ContainsKey(s.Id) && !_garrison.ContainsKey(s.Id) && s.Sector != st.Sector && s.Command is null or PatrolCommand or GuardCommand).Take(want - cand.Count));
        int n = 0;
        foreach (var s in cand.Take(want))
        {
            if (s.Command is not null) Commands.Cancel(U, s, "Wache");
            if (Commands.Give(U, s, new GuardCommand(st.Id), out _, ignoreCapacity: true)) { _garrison[s.Id] = st.Id; n++; GuardPosts++; }
        }
        S4Note($"Wache an {st.Name} ({U.Sectors[st.Sector].Name}): {n} Schiffe (jetzt {_garrison.Count(kv => kv.Value == st.Id)})");
    }

    /// <summary>Freie (nicht im Verband liegende) Kampfschiffe, die im Zielsektor angekommen sind, greifen mit an.</summary>
    int OrderFreeWing(Station st)
    {
        int n = 0;
        foreach (var id in _s4Free.ToList())
        {
            if (!U.Ships.TryGetValue(id, out var s)) { _s4Free.Remove(id); continue; }
            if (_garrison.ContainsKey(id) || _resup.ContainsKey(id) || s.Command is AttackCommand || s.Sector != st.Sector || s.Hull < s.MaxHull * RetreatHull || (AmmoFrac(s) < 0.4f && !Dry(s))) continue;
            if (s.Command is not null && s.Command is not GoToCommand) continue;
            if (Commands.Give(U, s, new AttackCommand(default, st.Id, default, st.Sector), out _, ignoreCapacity: true)) { n++; AttackOrders++; }
        }
        return n;
    }

    // ───────── Messreihe ─────────
    StreamWriter? _s4csv; long _s4csvNext;
    public void S4Csv()
    {
        if (!S4 || U.PlayerFaction is not { } me) return;
        if (_s4csv is null)
        {
            _s4csv = new StreamWriter(Path.Combine(S4Out, "s4zeit.csv")) { AutoFlush = true };
            _s4csv.WriteLine("tick;kasse;schiffe;kampf;gross;sektoren;eigeneStationen;kriege;feind;rel1;rel2;rel3;rel4;sperren;nachschub_unterwegs;wachen;nachschubfahrten;nachgeladen;trocken;regal_munition;mun_mittel;verluste;einfluss_je_sektor");
        }
        if (U.Tick < _s4csvNext) return; _s4csvNext = U.Tick + 10000;
        float shelf = U.StationsById().Where(x => !x.IsBuildSite && U.StationTypes[x.TypeKey].Services.Contains("rearm")).Sum(AmmoStock);
        var own = PlayerFleet.Owned(U);
        var wars = U.Wars.Where(w => w.Involves(me)).Select(w => w.Other(me).Value).ToList();
        var mil = own.Where(Warship).ToList();
        float am = mil.Where(s => s.Stats.AmmoCapacity + s.Stats.MissileCapacity > 0).Select(AmmoFrac).DefaultIfEmpty(1f).Average();
        string rel(int f) => U.Actors.ContainsKey(new ActorId(f)) ? Math.Min(Relations.Effective(U, U.PlayerActor, new ActorId(f)), Relations.Effective(U, new ActorId(f), U.PlayerActor)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "";
        int locks = U.Sectors.Values.Count(s => !s.Access.IsOpen && s.Access.EntryLevel >= 3);
        var infl = string.Join(" ", U.Sectors.Values.OrderBy(s => s.Id.Value).Select(s => { var (pw, o, t) = Shares(s); return $"{s.Name.Replace("Sektor ", "")}:{(s.Owner == me ? "ICH" : s.Owner.Value.ToString())}:{pw:0.0}/{t:0.0}:{PlayerOwnIn(s)}"; }));
        _s4csv.WriteLine(string.Join(";", U.Tick, (long)Wallet, own.Count, mil.Count, mil.Count(s => U.ShipClasses.TryGetValue(s.ClassKeyOf(U), out var c) && c.Size >= SlotSize.L), U.Sectors.Values.Count(s => s.Owner == me), PlayerBuilding.OwnEconomic(U).Count + PlayerSeats.Seats(U).Count,
            string.Join("+", wars), _enemy.Value, rel(1), rel(2), rel(3), rel(4), locks, _resup.Count, _garrison.Count, ResupplyTrips, Rearms, DryVisits, (long)shelf, am.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Lost, infl));
    }
    float AmmoStock(Station st) { var a = SpaceSim.Core.Combat.Rearm.AmmoGood(U); return a is not null && st.Market.TryGetValue(a.Id, out var e) ? e.Amount : 0f; }
    float PlatesAt(Station st) { var g = U.Goods.Values.FirstOrDefault(x => x.Key == "hull_plates"); return g is not null && st.Market.TryGetValue(g.Id, out var e) ? e.Amount : 0f; }
    int PlayerOwnIn(Sector s) => s.Stations.Count(id => U.Stations.TryGetValue(id, out var st) && st.Owner == U.PlayerActor);
    public static string S4Out = ".";
    public int Lost;
}
