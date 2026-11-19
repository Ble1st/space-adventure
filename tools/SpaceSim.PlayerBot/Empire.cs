using SpaceSim.Core.Combat;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using SpaceSim.Core.Weapons;

namespace SpaceSim.PlayerBot;

/// <summary>
/// Zweiter Spielbogen des Bots (nur Spielerpfade): eigenes Hauptquartier, Baufenster mit eigenen Bauschiffen, Gericht/Kopfgeld mit echten Verbrechen,
/// Krieg (Erklärung, Belagerung, Kapitulation, Tribut/Frieden). Jede Teilfunktion meldet, ob sie Spielzeit verbraucht hat.
/// </summary>
public sealed class Empire
{
    readonly Pilot P; readonly Universe U; readonly Log L; readonly Metrics M; readonly Bot B; readonly CommsActions C;
    public readonly Dictionary<string, long> Mile = new();
    public readonly List<string> Notes = new();
    public readonly StationBook Book;
    public readonly HashSet<StationId> MySites = new();
    public int BuiltCount => MySites.Count(id => U.Stations.TryGetValue(id, out var st) && !st.IsBuildSite && st.Owner == U.PlayerActor);

    public static string Only = "";
    public static double RichGate = 9_999_999;
    bool _rich;
    public bool Rich { get { if (!_rich && (Wallet >= RichGate || U.Tick >= 500_000)) { _rich = true; Note($"Rücklage erreicht (Kasse {Wallet:0}, Tick {U.Tick}): Strukturziele beginnen"); } return _rich; } }
    float AvgRel() => U.NpcFactionIds().Select(f => Relations.Effective(U, U.PlayerActor, new ActorId(f.Value))).DefaultIfEmpty(0f).Average();
    long _lockT = -1; float _lockRel0; double _lockTrade0;

    public Empire(Pilot p, Log l, Metrics m, Bot b, CommsActions c) { P = p; U = p.U; L = l; M = m; B = b; C = c; Book = new StationBook(U); Book.Built = id => MySites.Contains(id); U.Events.Raised += e => { if (e is PlayerSiteStarted ps) MySites.Add(ps.Site); }; }

    void Note(string s) { Notes.Add($"[{U.Tick,8}] {s}"); L.Event("EMPIRE: " + s); }
    void Mark(string k) { if (!Mile.ContainsKey(k)) { Mile[k] = U.Tick; Note($"MEILENSTEIN {k}"); } }
    float Wallet => P.Wallet;
    ActorSizeClass Cls => ActorRules.ClassOf(U, ActorRules.Index(U), U.PlayerActor);

    public bool Step()
    {
        if (U.Tick < 2000) return false;
        if (CampaignActive) { P.Where = "Empire:campaign"; return War(); }
        LockRevert();
        if (!Rich) return false;
        P.Where = "Empire:hq"; if (Hq()) return true;
        if (Only == "court") { P.Where = "Empire:court"; return Court(); }
        if (Only == "bounty") { P.Where = "Empire:bounty"; return HuntRaider() || Bounty(); }
        P.Where = "Empire:build"; if (Build()) return true;
        P.Where = "Empire:court"; if (Court()) return true;
        P.Where = "Empire:war"; if (War()) return true;
        P.Where = "Empire:bounty"; if (HuntRaider() || Bounty()) return true;
        return false;
    }

    // ───────── Hauptquartier ─────────

    bool Hq()
    {
        if (PlayerHq.Current(U) is { } have)
        {
            if (!Mile.ContainsKey("HQ-Wirkung")) Effects(have);
            return false;
        }
        var own = PlayerSeats.OwnSectors(U);
        if (own.Count == 0 || (int)Cls < U.Rules.PlayerHqMinClass) return false;
        var t = PlayerHq.HqType(U); if (t is null) return false;
        float cost = PlayerHq.Cost(U, t);
        if (Wallet < cost + 150000f) return false;
        var me = P.Me;
        var tgt = own.OrderBy(s => U.Hops(me.Sector, s.Id) is var h && h < 0 ? 99 : h).First();
        if (!P.GoToSector(tgt.Id, out var why)) { if (!P.Danger()) Note($"HQ: Weg nach {tgt.Name} verwehrt: {why}"); return true; }
        if (P.Me.DockedAt.Value != 0) P.Undock();
        var issue = PlayerHq.FoundIssue(U, tgt.Id);
        if (issue is not null) { Note($"HQ in {tgt.Name} nicht möglich: {issue}"); P.Run(2000); return true; }
        var r = P.Book("HQ gründen", () => C.Execute(U, $"hq:found:{tgt.Id.Value}"));
        Note($"HQ gründen {tgt.Name} (Kosten {cost:0}, Kasse vorher {Wallet + cost:0}, Klasse {Cls}): {r.Message}");
        if (r.Success) { Mark("HQ"); P.Run(40); }
        return true;
    }

    void Effects(Station hq)
    {
        var sec = U.Sectors[hq.Sector];
        P.Run(100);
        var t = U.StationTypes[hq.TypeKey];
        Note($"HQ-Wirkung: Sektor {sec.Name} Besitzer {(sec.Owner == U.PlayerFaction ? "Spielerfraktion" : sec.Owner.Value.ToString())}, Stufe {sec.Control}, Einfluss {string.Join(" ", sec.Influence.Select(kv => $"{kv.Key.Value}:{kv.Value:0.00}"))}, MaxSperrstufe {SectorControl.MaxLevel(U, sec)}, Unterhalt {t.UpkeepPerTick * 240:0.0} Cr/min, Verteidigung hull {t.Defense?.Hull} Geschütze {t.Defense?.Guns.Count}, Dienste {string.Join(",", t.Services)}");
        Mark("HQ-Wirkung");
    }

    bool _lockDone; long _lockNext;
    /// <summary>Sperrstufe-5-Test (ganz am Ende, weil er die Beziehungen aller KI-Fraktionen zerstört): setzen, Wirkung messen, wiederholt aufheben.</summary>
    void LockRevert()
    {
        var hq = PlayerHq.Current(U); if (hq is null || _lockDone || U.Tick < _lockNext) return;
        if (_lockT < 0)
        {
            bool ready = U.Tick >= 1_150_000 || (Mile.ContainsKey("Gericht") && (Mile.ContainsKey("Station erobert") || Mile.ContainsKey("Station zerstört")) && PlayerSeats.OwnSectors(U).Count >= 10);
            if (!ready) return;
            var r = P.Book("Sperre", () => C.Execute(U, $"lock:{hq.Sector.Value}:5"));
            Note($"Sperrstufe 5 setzen: {r.Message}");
            if (r.Success) { _lockT = U.Tick; _lockRel0 = AvgRel(); _lockTrade0 = P.Led?.Sources.GetValueOrDefault("Flotte Handel Verkauf") ?? 0; }
            else { _lockDone = true; }
            _lockNext = U.Tick + 2100; return;
        }
        float rel = AvgRel(); double tr = (P.Led?.Sources.GetValueOrDefault("Flotte Handel Verkauf") ?? 0) - _lockTrade0;
        var r0 = P.Book("Sperre", () => C.Execute(U, $"lock:{hq.Sector.Value}:0"));
        Note($"Sperrstufe-5-Test nach {U.Tick - _lockT} Ticks: mittlere Beziehung {_lockRel0:0.00}→{rel:0.00}, Flottenhandel-Umsatz in der Zeit {tr:0}; Sperre aufheben: {r0.Message}");
        if (r0.Success) _lockDone = true; else _lockNext = U.Tick + 600;
    }

    // ───────── Baufenster ─────────

    static readonly string[] Plan = { "solar_plant", "steelworks", "water_plant", "copper_smelter", "wafer_fab", "electronics_fab", "goods_factory", "farm", "life_support_fab", "titanium_works" };
    int _planIdx; long _nextBuild; readonly HashSet<(SectorId, string)> _noSpot = new(); readonly HashSet<StationId> _funded = new();
    public readonly List<string> BuildLog = new();

    List<Ship> Foremen() => PlayerFleet.Owned(U).Where(s => s.ModelKey == "foreman").ToList();

    bool Build()
    {
        if (!U.Rules.PlayerBuilding) return false;
        // Betriebskapital für fertige eigene Stationen (Umbuchung der Spielerkasse, kein neues Geld)
        foreach (var st in PlayerBuilding.OwnEconomic(U))
        {
            if (st.IsBuildSite) continue;
            Book.Register(st);
            if (st.Credits < 1500f && Wallet > 60000f)
            {
                float amt = MathF.Min(8000f, Wallet * 0.05f);
                if (PlayerBuilding.TransferIssue(U, st.Id, amt) is null)
                {
                    P.Book("Umbuchung Station", () => { PlayerBuilding.Transfer(U, st.Id, amt); return 0; });
                    Book.Transfer(st.Id, amt);
                    if (_funded.Add(st.Id)) Note($"Station {st.TypeKey} {st.Name} fertig, Betriebskapital {amt:0} Cr");
                }
            }
        }
        // Bauschiffe den Baustellen zuweisen
        var sites = PlayerBuilding.OwnSites(U);
        foreach (var sit in sites)
            if (!Foremen().Any(f => f.Command is BuildCommand bc && bc.Site == sit.Id))
                foreach (var f in Foremen().Where(f => f.Command is not BuildCommand && f.Id != U.PlayerShip))
                {
                    bool ok = Commands.Give(U, f, new BuildCommand(sit.Id), out var msg, ignoreCapacity: false);
                    if (!ok && msg.Contains("kapazität") && FreeSlot()) { ok = Commands.Give(U, f, new BuildCommand(sit.Id), out msg, ignoreCapacity: false); if (ok) Note("Einzelbefehl eines Schürfers aufgehoben (Befehlskapazität voll), Platz für das Bauschiff"); }
                    if (ok) Note($"Bauschiff #{f.Id.Value} baut {sit.TypeKey} (Baustelle {sit.Id.Value})"); else Note($"Bauschiff-Befehl: {msg}");
                    break;
                }
        if (U.Tick < _nextBuild || _planIdx >= Plan.Length) return false;
        var type = U.StationTypes[Plan[_planIdx]];
        float cost = PlayerBuilding.Cost(U, type);
        if (Wallet < cost + 60000f) return false;
        var own = PlayerSeats.OwnSectors(U);
        if (own.Count == 0) return false;
        if (Foremen().Count == 0) { BuyForeman(); _nextBuild = U.Tick + 600; return true; }
        if (sites.Count >= Math.Min(U.Rules.PlayerMaxSites, Foremen().Count)) return false;
        // Sektor mit Platz (höchstens 4 eigene je Sektor), bevorzugt wenig belegt
        var perSec = PlayerBuilding.OwnEconomic(U).GroupBy(s => s.Sector).ToDictionary(g => g.Key, g => g.Count());
        var cands = own.Where(s => perSec.GetValueOrDefault(s.Id) < U.Rules.PlayerMaxStationsPerSector && (type.Export is null || s.Planet is not null))
            .OrderBy(s => perSec.GetValueOrDefault(s.Id)).ThenBy(s => U.Hops(P.Me.Sector, s.Id)).ToList();
        if (cands.Count == 0) { Note($"Bau {type.Key}: kein eigener Sektor mit Platz (eigene Stationen {PlayerBuilding.OwnEconomic(U).Count})"); _nextBuild = U.Tick + 20000; return false; }
        var sec = cands.FirstOrDefault(c => !_noSpot.Contains((c.Id, type.Key)));
        if (sec is null) { Note($"Bau {type.Key}: in keinem eigenen Sektor ein freier Platz — überspringe Typ"); _planIdx++; _nextBuild = U.Tick + 300; return false; }
        if (!P.GoToSector(sec.Id, out var why)) { if (!P.Danger()) { Note($"Bau: Weg nach {sec.Name} verwehrt: {why}"); _nextBuild = U.Tick + 3000; } return true; }
        if (P.Me.DockedAt.Value != 0) P.Undock();
        var me = P.Me;
        Vec2? spot = null;
        // Gitter über die ganze Platzierungsfläche, nächster freier Punkt zum Schiff
        float half = StationPlacement.HalfArea(U.Sectors[sec.Id]) - 20f, step = 25f;
        var pts = new List<(float d, Vec2 p)>();
        for (float gx = -half; gx <= half; gx += step) for (float gy = -half; gy <= half; gy += step) { var p = new Vec2(gx, gy); pts.Add((Vec2.Distance(p, me.Position), p)); }
        foreach (var (_, p) in pts.OrderBy(t => t.d)) if (PlayerBuilding.PlaceIssue(U, type, p) is null) { spot = p; break; }
        if (spot is null) { Note($"Bau {type.Key}: kein freier Platz in {sec.Name} (Gitter {pts.Count} Punkte geprüft)"); _noSpot.Add((sec.Id, type.Key)); _nextBuild = U.Tick + 100; return true; }
        var issue = PlayerBuilding.PlaceIssue(U, type, spot.Value);
        if (issue is not null) { Note($"Bau {type.Key}: {issue}"); _nextBuild = U.Tick + 3000; return true; }
        var site = P.Book("Baustelle", () => PlayerBuilding.Start(U, type, spot.Value, 0f));
        Note($"Baustelle {type.Key} in {sec.Name} bei ({spot.Value.X:0},{spot.Value.Y:0}) Treuhand {cost:0}");
        BuildLog.Add($"{type.Key};{sec.Name};{spot.Value.X:0};{spot.Value.Y:0};{U.Tick}");
        _planIdx++; _nextBuild = U.Tick + 300;
        return true;
    }

    bool FreeSlot()
    {
        var v = PlayerFleet.Owned(U).Where(x => x.Command is MineCommand && x.Id != U.PlayerShip).OrderBy(x => x.Stats.Cargo).FirstOrDefault();
        if (v is null) return false;
        Commands.Cancel(U, v, "Platz für Bauschiff"); return true;
    }

    void BuyForeman()
    {
        var yard = P.NearestWith("shipdealer", st => st.TypeKey == "shipyard");
        if (yard is null || P.Svc(yard, "shipdealer") is not { } svc) return;
        if (!P.GoToStation(yard, out _)) return;
        var q = ShipDealerService.Quote(U, yard, P.Me, "foreman");
        if (q is not { Buyable: true } || q.Price > Wallet - 30000f) { Note($"Bauschiff: nicht kaufbar bei {yard.Name} ({(q is null ? "kein Modell" : q.Locked ?? (q.Missing.Count > 0 ? "fehlt Material" : "zu teuer"))})"); return; }
        var r = P.Do(yard, svc, "buy:foreman");
        Note($"Bauschiff gekauft {q.Price:0} Cr: {(r.Success ? "ok" : r.Message)}");
        if (r.Success) { B.RestoreFlag(); M.Purchases++; }
    }

    // ───────── Gericht ─────────

    int _courtStage; long _nextCourt; readonly HashSet<FactionId> _courtFacs = new();
    public readonly List<string> CourtLog = new();

    bool Court()
    {
        if (_courtStage >= 4 || U.Tick < _nextCourt || CampaignActive || PlayerHq.Current(U) is not { } hq) return false;
        if (Wallet < 120000f) return false;
        // Opfer-Fraktion je Stufe: 0 fremdes Gericht, 1 eigenes Hauptquartier-Gericht, 2 Fern-Zahlung per Comms
        var facs = U.NpcFactionIds().Where(f => !Wars.AtWar(U, U.PlayerFaction!.Value, f)).ToList();
        if (facs.Count <= _courtStage) { _courtStage = 4; return false; }
        var warship = PlayerFleet.Owned(U).Where(s => s.Has(Capability.Combat) && PlayerGunnery.WeaponSlots(U, s).Count > 0 && s.DockedAt.Value == 0 || s.Has(Capability.Combat) && s.DockedAt.Value != 0)
            .OrderByDescending(s => s.Stats.WeaponPower).FirstOrDefault();
        if (warship is null) { _nextCourt = U.Tick + 4000; return false; }
        // Zielfraktion: die mit den meisten freien Händlern im Sektor, nicht die stärkste Beziehung verbrennen
        var used = _courtFacs;
        var pool = facs.Where(f => !used.Contains(f)).ToList();
        if (_courtStage == 1) pool = pool.Where(f => Relations.Effective(U, f, U.PlayerFaction!.Value) >= 0f).ToList();   // eigenes Gericht nimmt nur Fraktionen mit Beziehung ≥ 0
        if (pool.Count == 0) { Note($"Gerichtstest {_courtStage}: keine Fraktion mit passender Beziehung (Rechtshilfe im eigenen Gericht braucht Beziehung ≥ {U.Rules.ExtraditionRelation:0.00}); überspringe"); _courtStage++; _nextCourt = U.Tick + 3000; return false; }
        var fid = _courtStage == 3 ? pool.OrderBy(f => Relations.Effective(U, f, U.PlayerFaction!.Value)).First() : pool.OrderByDescending(f => Relations.Effective(U, f, U.PlayerFaction!.Value)).First();
        used.Add(fid);
        string tag = _courtStage switch { 0 => "fremdes Gericht", 1 => "eigenes HQ-Gericht (Fraktion mit hoher Beziehung)", 2 => "Comms-Fernzahlung", _ => "eigenes HQ-Gericht (Gegenprobe: Fraktion mit niedriger Beziehung)" };
        var line = new System.Text.StringBuilder($"Gerichtstest {_courtStage} ({tag}) gegen Fraktion {fid.Value}: ");
        B.UseFlag(warship.Id);
        try
        {
            var vict = FindVictim(fid);
            if (vict is null) { Note($"Gerichtstest: kein Opfer in Gebiet von Fraktion {fid.Value}"); _nextCourt = U.Tick + 4000; return false; }
            float relBefore = Relations.Effective(U, U.PlayerActor, new ActorId(fid.Value));
            float treasBefore = U.Actors[new ActorId(fid.Value)].Treasury.Credits;
            float wBefore = Wallet;
            if (!P.GoToSector(vict.Value.Sector, out var why)) { Note($"Gerichtstest: Weg verwehrt {why}"); _nextCourt = U.Tick + 4000; return true; }
            var me = P.Me; if (me.DockedAt.Value != 0) P.Undock();
            // Angriff: ein Schuss auf den Händler reicht (Überfall: Stufe 3 sofort); danach Gefecht ausfechten
            ShipId tgt = vict.Value.Ship;
            for (int tr = 0; tr < 6; tr++)
            {
                var pp = PlayerCombat.PositionOf(U, tgt);
                if (pp is { } q0 && q0.Sector == me.Sector && PlayerCombat.TargetIssue(U, me, U.Ships[tgt]) is null) break;
                var alt = U.ShipsById().FirstOrDefault(s => s.Sector == me.Sector && s.Role == ShipRole.Trader && !s.IsPlayer && s.Owner == U.FreeActor && U.Fights.Of(s.Id) is null && PlayerCombat.PositionOf(U, s.Id) is { } pq && pq.Sector == me.Sector && PlayerCombat.TargetIssue(U, me, s) is null);
                if (alt is not null) { tgt = alt.Id; break; }
                P.Run(80);
            }
            var pos = PlayerCombat.PositionOf(U, tgt);
            if (pos is null) { _nextCourt = U.Tick + 2000; return true; }
            me.Position = pos.Value.Pos - new Vec2(0f, 22f);
            var slots = PlayerGunnery.WeaponSlots(U, me);
            int fired = 0; string lastMsg = "";
            for (int k = 0; k < 40 && fired == 0; k++)
            {
                var p2 = PlayerCombat.PositionOf(U, tgt); if (p2 is null) break;
                var dir = p2.Value.Pos - me.Position;
                me.Position = p2.Value.Pos - dir.Normalized() * 20f;
                foreach (var s in slots) { var gs = PlayerGunnery.TryFire(U, me, s, tgt, FiringArc.Bearing(dir.Length > 0.01f ? dir.Normalized() : new Vec2(0, -1))); lastMsg = $"{gs.Result}:{gs.Message}"; if (gs.Result == GunResult.Fired) fired++; }
                P.Step();
            }
            line.Append($"Schüsse {fired} (Schiff {me.ModelKey} #{me.Id.Value}, Waffen {slots.Count}, zuletzt {lastMsg}); ");
            P.Fight(200);
            var actor = U.Actors[U.PlayerActor];
            if (!actor.Wanted.TryGetValue(fid, out var w)) { line.Append($"keine Fahndung entstanden oder schon gelöscht (Festnahme/Ergeben zahlte automatisch?) Kasse {wBefore:0}→{Wallet:0}"); CourtLog.Add(line.ToString()); Note(line.ToString()); _courtStage++; _nextCourt = U.Tick + 6000; return true; }
            float fine = Wanted.FineOf(U, w); int level = Wanted.Level(U.Rules, w.Heat);
            line.Append($"Fahndung Stufe {level}, Strafe {fine:0} Cr, Beziehung {relBefore:0.00}→{Relations.Effective(U, U.PlayerActor, new ActorId(fid.Value)):0.00}; ");
            float wallet1 = Wallet, treas1 = U.Actors[new ActorId(fid.Value)].Treasury.Credits;
            string result;
            if (_courtStage == 2)
            {
                var r = P.Book("Comms Strafe", () => C.Execute(U, $"fine:{fid.Value}"));
                result = $"Comms fine: {r.Message}";
            }
            else
            {
                Station? court = (_courtStage == 1 || _courtStage == 3) ? hq : P.NearestWith("court", st => U.FactionOf(st) == fid);
                if (court is null) { line.Append("kein Gericht erreichbar; "); result = "—"; }
                else if (!GoRetry(court, out var cw)) { result = $"Anflug {cw}"; }
                else
                {
                    var svc = P.Svc(court, "court");
                    var off = svc is null ? null : P.Offer(court, svc, $"pay:{fid.Value}");
                    if (off is null) result = $"{court.Name}: Gericht bietet keine Zahlung für Fraktion {fid.Value} an (Akzeptanz: Beziehung {Relations.Effective(U, fid, U.FactionOf(court) ?? fid):0.00} < {U.Rules.ExtraditionRelation:0.00}?)";
                    else { var r = P.Do(court, svc!, off.Value.Key); result = $"{court.Name}: {r.Message}"; }
                }
            }
            bool cleared = !U.Actors[U.PlayerActor].Wanted.ContainsKey(fid);
            float treas2 = U.Actors[new ActorId(fid.Value)].Treasury.Credits;
            line.Append($"{result} | Kasse {wallet1:0}→{Wallet:0} (Δ {Wallet - wallet1:0}), Fraktionskasse {treas1:0}→{treas2:0} (Δ {treas2 - treas1:0}), Eintrag gelöscht {cleared}");
            CourtLog.Add(line.ToString()); Note(line.ToString());
            _courtStage++; _nextCourt = U.Tick + 12000;
            if (_courtStage >= 2 && CourtLog.Count(x => x.Contains("gelöscht True")) >= 1) Mark("Gericht");
        }
        finally { B.RestoreFlag(); }
        return true;
    }

    bool GoRetry(Station st, out string why)
    {
        why = "";
        for (int k = 0; k < 8; k++)
        {
            if (P.GoToStation(st, out why)) return true;
            if (P.Danger()) P.Fight(300);
            P.Run(60);
        }
        return false;
    }

    (ShipId Ship, SectorId Sector)? FindVictim(FactionId f)
    {
        foreach (var sec in U.Sectors.Values.OrderBy(s => U.Hops(P.Me.Sector, s.Id) is var h && h < 0 ? 99 : h))
        {
            if (sec.Owner != f || U.Hops(P.Me.Sector, sec.Id) is < 0 or > 5) continue;
            foreach (var s in U.ShipsById())
                if (s.Sector == sec.Id && s.Role == ShipRole.Trader && !s.IsPlayer && s.Owner == U.FreeActor && s.Id != U.PlayerShip && U.Fights.Of(s.Id) is null && PlayerCombat.TargetIssue(U, P.Me, s) is null && s.Hull > 0)
                    return (s.Id, sec.Id);
        }
        return null;
    }

    // ───────── Kopfgeld (aus echten Kämpfen der Flotte/Spielers; abholen) ─────────

    public float BountyClaimed; public int BountyClaims;
    long _nextBounty;
    bool Bounty()
    {
        if (U.Tick < _nextBounty) return false;
        _nextBounty = U.Tick + 1500;
        var a = U.Actors[U.PlayerActor];
        foreach (var (f, amt) in a.BountyClaims.Where(kv => kv.Value > 1f).ToList())
        {
            var office = P.NearestWith("bounty", st => U.FactionOf(st) == f);
            if (office is null || !P.GoToStation(office, out _)) continue;
            var svc = P.Svc(office, "bounty"); if (svc is null) continue;
            float before = Wallet; float tb = U.Actors[new ActorId(f.Value)].Treasury.Credits;
            var ownOff = PlayerHq.Current(U) is { } oh && P.Svc(oh, "bounty") is { } os ? P.Offer(oh, os, $"claim:{f.Value}") : null;
            var r = P.Do(office, svc, $"claim:{f.Value}");
            Note($"Kopfgeld abholen Fraktion {f.Value} (Anspruch {amt:0}) bei {office.Name}: {r.Message} Kasse {before:0}→{Wallet:0}; Fraktionskasse {tb:0}→{U.Actors[new ActorId(f.Value)].Treasury.Credits:0}; Angebot am eigenen HQ für diese Fraktion: {(ownOff is null ? "keins" : "ja")}");
            if (r.Success) { BountyClaimed += Wallet - before; BountyClaims++; Mark("Kopfgeld"); }
            return true;
        }
        return false;
    }

    int _hunts; long _nextHunt;
    public readonly List<string> HuntLog = new();
    /// <summary>Kopfgeldjagd (echte Spielerpfade): ein Kaperschiff finden, das bei der Sektorfraktion Fahndung hat, abschießen; der Anspruch entsteht im Gefecht, abgeholt wird beim Büro dieser Fraktion.</summary>
    bool HuntRaider()
    {
        if (_hunts >= 3 || U.Tick < _nextHunt || CampaignActive) return false;
        var warship = PlayerFleet.Owned(U).Where(s => s.Has(Capability.Combat) && s.Id != U.PlayerShip || s.Id == U.PlayerShip && s.Has(Capability.Combat)).OrderByDescending(s => s.Stats.WeaponPower).FirstOrDefault();
        if (warship is null) return false;
        Ship? prey = null; FactionId pf = default; int bestHops = 99;
        foreach (var s in U.ShipsById())
        {
            if (s.Role != ShipRole.Pirate || s.IsPlayer || s.Sector.Value == 0 || U.Fights.Of(s.Id) is not null || s.Hull <= 0) continue;
            var sec = U.Sectors[s.Sector]; if (!U.IsNpcFaction(sec.Owner)) continue;
            if (!U.Actors.TryGetValue(s.Owner, out var oa) || !oa.Wanted.ContainsKey(sec.Owner)) continue;
            int h = U.Hops(P.Me.Sector, s.Sector); if (h < 0 || h >= bestHops) continue;
            prey = s; pf = sec.Owner; bestHops = h;
        }
        _nextHunt = U.Tick + 2500;
        if (prey is null) return false;
        B.UseFlag(warship.Id);
        try
        {
            float bounty = Wanted.BountyOn(U, prey.Owner, pf); float before = U.Actors[U.PlayerActor].BountyClaims.GetValueOrDefault(pf);
            string line = $"Jagd {_hunts + 1}: Kaperschiff #{prey.Id.Value} ({prey.ModelKey}) in {U.Sectors[prey.Sector].Name}, Fraktion {pf.Value}, Kopfgeld laut Fahndung {bounty:0}; ";
            int nf = 0; string hm = "";
            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (!P.GoToSector(prey.Sector, out var why)) { hm = $"Weg verwehrt {why}"; break; }
                var me0 = P.Me; if (me0.DockedAt.Value != 0) P.Undock();
                var here = U.ShipsById().Where(x => x.Role == ShipRole.Pirate && !x.IsPlayer && x.Sector == me0.Sector && U.Fights.Of(x.Id) is null && x.Hull > 0).FirstOrDefault();
                if (here is not null) { prey = here; break; }
                // das Kaperschiff ist weitergezogen: das nächste Ziel suchen
                Ship? nxt = null; int bh = 99;
                foreach (var x in U.ShipsById())
                {
                    if (x.Role != ShipRole.Pirate || x.IsPlayer || U.Fights.Of(x.Id) is not null || x.Hull <= 0) continue;
                    int h = U.Hops(P.Me.Sector, x.Sector); if (h < 0 || h >= bh) continue; nxt = x; bh = h;
                }
                if (nxt is null) break; prey = nxt;
            }
            var me = P.Me; if (me.DockedAt.Value != 0) P.Undock();
            for (int round = 0; round < 12 && U.Ships.ContainsKey(prey.Id); round++)
            {
                var pos = PlayerCombat.PositionOf(U, prey.Id);
                if (pos is null || pos.Value.Sector != me.Sector) { hm = $"kein Ziel: pos {(pos is null ? "null" : U.Sectors[pos.Value.Sector].Name)} ich {U.Sectors[me.Sector].Name}"; P.Run(100); continue; }
                var dir = pos.Value.Pos - me.Position;
                me.Position = pos.Value.Pos - dir.Normalized() * 22f;
                foreach (var sl in PlayerGunnery.WeaponSlots(U, me)) { var gs = PlayerGunnery.TryFire(U, me, sl, prey.Id, FiringArc.Bearing(dir.Length > 0.01f ? dir.Normalized() : new Vec2(0, -1))); hm = $"{gs.Result}:{gs.Message}"; if (gs.Result == GunResult.Fired) nf++; }
                P.Step(); P.Fight(300);
            }
            bool dead = !U.Ships.ContainsKey(prey.Id); line += $"[Schüsse {nf}, zuletzt {hm}, Raider-Hülle {(U.Ships.TryGetValue(prey.Id, out var pr) ? pr.Hull : 0):0}, eigene Hülle {P.Me.Hull:0}/{P.Me.MaxHull:0}] ";
            float after = U.Actors[U.PlayerActor].BountyClaims.GetValueOrDefault(pf);
            line += $"Raider {(dead ? "zerstört" : "nicht erledigt")}; Anspruch bei Fraktion {pf.Value} {before:0}→{after:0}";
            HuntLog.Add(line); Note(line); _hunts++; _nextBounty = 0;
            if (dead && after > before) Mark("Kopfgeld-Anspruch");
        }
        finally { B.RestoreFlag(); }
        return true;
    }

    // ───────── Krieg ─────────

    public bool CampaignActive => _ws is WarState.Declared or WarState.Marching or WarState.Siege;
    enum WarState { Idle, Declared, Marching, Siege, Done }
    WarState _ws; FactionId _enemy; StationId _target; long _wsSince; int _warTry; public readonly List<string> WarLog = new();
    long _nextWar; int _wars, _chain; SectorId _targetSector;

    bool War()
    {
        if (_ws == WarState.Done || U.Tick < _nextWar || PlayerHq.Current(U) is null) return false;
        var mil = PlayerFleet.Owned(U).Where(s => s.Has(Capability.Combat) && U.ShipModels.TryGetValue(s.ModelKey, out var m) && m.Jobs.Count == 0).ToList();
        int big = mil.Count(s => U.ShipClasses.TryGetValue(s.ClassKeyOf(U), out var c) && c.Size >= SlotSize.L);
        if (mil.Count < 10 || big < 3 || Wallet < 150000f) return false;
        if (_planIdx < Plan.Length && BuiltCount < 10 && U.Tick < 900_000) return false;   // erst die eigene Kette bauen (erobert Stationen zählen gegen die Obergrenze von 10)
        switch (_ws)
        {
            case WarState.Idle:
            {
                // Ziel: schwächste kriegsfähige Fraktion mit einer fangbaren Station (Sektor-HQ/Wirtschaftsstation), nicht im Bündnis
                var me = U.PlayerFaction!.Value;
                var cands = U.NpcFactionIds().Where(f => Wars.PlayerDeclareIssue(U, f) is null).ToList();
                if (cands.Count == 0) { _nextWar = U.Tick + 6000; return false; }
                var f0 = cands.OrderBy(f => Wars.Strength(U, f)).First();
                _enemy = f0;
                var st = U.StationsById().Where(s => !s.IsBuildSite && U.FactionOf(s) == f0 && U.Sectors[s.Sector].Owner == f0 && !StationProtection.IsHideout(U, s))
                    .OrderBy(s => IsHq(s) ? 1 : 0).ThenBy(s => U.StationTypes[s.TypeKey].Defense?.Hull ?? 0f)
                    .ThenBy(s => U.Hops(P.Me.Sector, s.Sector) is var h && h < 0 ? 99 : h).FirstOrDefault();
                if (st is null) { _nextWar = U.Tick + 6000; return false; }
                _target = st.Id; _targetSector = st.Sector; _chain = 0;
                Note($"Kriegsplan gegen Fraktion {f0.Value} (Stärke {Wars.Strength(U, f0):0.0}): Ziel {st.Name} ({st.TypeKey}) in {U.Sectors[st.Sector].Name} — erst in Friedenszeit in den Sektor einfliegen (im Krieg gelten dort sofort ihre Sperren), dann erklären");
                _ws = WarState.Declared; _wsSince = U.Tick; return true;
            }
            case WarState.Declared:
            case WarState.Marching:
            case WarState.Siege:
                return Campaign(mil);
        }
        return false;
    }

    bool IsHq(Station s) => U.StationTypes.TryGetValue(s.TypeKey, out var t) && t.Government == GovernmentKind.Headquarters;

    bool Campaign(List<Ship> mil)
    {
        if (!U.Stations.TryGetValue(_target, out var st) || U.FactionOf(st) != _enemy)
        {
            bool mine = U.Stations.TryGetValue(_target, out var s2) && s2.Owner == U.PlayerActor;
            var secId = _targetSector;
            Note(mine ? $"Ziel gefallen und erobert: {s2!.Name} — Sektor {U.Sectors[secId].Name} Besitzer jetzt {(U.Sectors[secId].Owner == U.PlayerFaction ? "Spielerfraktion" : U.Sectors[secId].Owner.Value.ToString())}, Einfluss {string.Join(" ", U.Sectors[secId].Influence.Select(kv => $"{kv.Key.Value}:{kv.Value:0.00}"))}" : "Ziel nicht mehr beim Gegner (zerstört?)");
            WarLog.Add($"[{U.Tick}] Ziel {(mine ? "erobert" : "weg")}");
            Mark(mine ? "Station erobert" : "Station zerstört");
            var next = U.StationsById().Where(x => !x.IsBuildSite && x.Sector == secId && U.FactionOf(x) == _enemy && !StationProtection.IsHideout(U, x) && x.Owner != U.PlayerActor
                && U.StationTypes[x.TypeKey].Government != GovernmentKind.Outpost && !StationCombat.IsDown(U, x))
                .OrderBy(x => U.StationTypes[x.TypeKey].Defense?.Guns.Count ?? 0).ThenBy(x => U.StationTypes[x.TypeKey].Defense?.Hull ?? 0f).FirstOrDefault();
            if (next is not null && U.Sectors[secId].Owner != U.PlayerFaction && _chain < 12) { _target = next.Id; _chain++; _siegeStart = -1; _siegeRounds = 0; Note($"Nächstes Ziel im Sektor: {next.Name} ({next.TypeKey})"); return true; }
            EndWar(); return true;
        }
        // Verband: stärkstes Kampfschiff als Anführer
        var sorted = mil.Where(s => !s.IsRental()).OrderByDescending(s => (s.Hull >= s.MaxHull * 0.9f ? 1000f : 0f) + s.Stats.WeaponPower + s.Stats.Hull * 0.02f).ToList();
        var leader = sorted.First();
        if (_ws == WarState.Declared)
        {
            B.UseFlag(leader.Id);
            if (P.Me.Id != leader.Id) { P.Run(20); if (P.Me.Id != leader.Id) { Note("Verband: Wechsel ins Führungsschiff fehlgeschlagen"); _nextWar = U.Tick + 3000; return true; } }
            Heal();
            var lead = P.Me;
            Squadron? sq = Squadrons.Get(U, lead);
            if (sq is null) { sq = Squadrons.Create(U, lead, "Kriegsflotte", out var msg); if (sq is null) { Note($"Verband gründen: {msg}"); _ws = WarState.Marching; return true; } }
            int joined = 0;
            foreach (var s in sorted.Where(s => s.Id != lead.Id && s.Squadron.Value == 0).Take(U.Rules.SquadronMaxMembers - sq.Members.Count))
                if (Squadrons.Join(U, sq, s, out _)) joined++;
            Squadrons.SetRules(U, sq, Engagement.Attack);
            Note($"Verband {sq.Members.Count} Schiffe (Anführer #{lead.Id.Value}, {lead.ModelKey}, Waffenkraft {lead.Stats.WeaponPower:0})");
            _ws = WarState.Marching; _wsSince = U.Tick; return true;
        }
        if (P.Me.Id != _leaderId(leader)) B.UseFlag(P.Me.Id);
        var me = P.Me;
        if (_ws == WarState.Marching)
        {
            if (me.Sector != st.Sector)
            {
                if (!P.GoToSector(st.Sector, out var why)) { if (!P.Danger()) { Note($"Marsch zu {U.Sectors[st.Sector].Name}: {why}"); _warTry++; P.Run(600); if (_warTry > 6) { Note("Marsch aufgegeben"); EndWar(); } } return true; }
            }
            P.Run(1200);   // Verband sammelt sich (Folgeflug der Mitglieder)
            if (Wars.PlayerWar(U, _enemy) is null)
            {
                int near = Squadrons.Get(U, P.Me) is { } sq0 ? sq0.Members.Count(id => id != P.Me.Id && U.Ships.TryGetValue(id, out var ms) && ms.Sector == st.Sector) : 0;
                var r = P.Book("Krieg erklären", () => C.Execute(U, $"war:{_enemy.Value}"));
                Note($"Krieg erklären Fraktion {_enemy.Value}: {r.Message} (Verband im Zielsektor: {near} von {Squadrons.Get(U, P.Me)?.Members.Count - 1})");
                WarLog.Add($"[{U.Tick}] Krieg erklärt Fraktion {_enemy.Value}: {r.Message}");
                if (!r.Success) { EndWar(); return true; }
                Mark("Krieg erklärt");
            }
            _ws = WarState.Siege; _wsSince = U.Tick; return true;
        }
        // Belagerung: anfliegen, alle Waffen auf die Station
        return Siege(st);
    }

    ShipId _leaderId(Ship l) => l.Id;

    /// <summary>Führungsschiff reparieren lassen (eigenes Hauptquartier zuerst, sonst die nächste Werkstatt), bevor es in den Kampf geht.</summary>
    void Heal()
    {
        var me = P.Me;
        if (me.Hull >= me.MaxHull * 0.9f && !me.Systems.Condition.Any(c => c < 0.7f)) return;
        var st = P.NearestWith("repair", x => x.Owner == U.PlayerActor) ?? P.NearestWith("repair");
        if (st is null) return;
        if (P.GoToStation(st, out _))
        {
            float before = me.Hull; P.RepairHere();
            if (P.Wallet > 0) { var svc = P.Svc(st, "repair"); if (svc is not null && P.Offer(st, svc, "repair:all") is { Amount: > 0f }) P.Do(st, svc, "repair:all"); }
            Note($"Führungsschiff repariert bei {st.Name}: Hülle {before:0}→{P.Me.Hull:0}/{P.Me.MaxHull:0}");
            if (P.Me.DockedAt.Value != 0) P.Undock();
        }
    }

    long _siegeStart = -1; int _siegeRounds;
    bool Siege(Station st)
    {
        var me = P.Me;
        if (_siegeStart < 0) { _siegeStart = U.Tick; Note($"Belagerung beginnt: {st.Name} Hülle {StationCombat.Hull(U, st):0}"); }
        var slots = PlayerGunnery.WeaponSlots(U, me);
        if (slots.Count == 0) { Note("Flaggschiff ohne Waffen"); EndWar(); return true; }
        float reach = slots.Select(s => Gunnery.WeaponAt(U, me, s)).Where(m => m is not null).Select(m => WeaponProfile.Of(m!).Range).DefaultIfEmpty(40f).Min();
        int ticks = 0; long start = U.Tick; var cnt = new Dictionary<string, int>(); string lastMsg = ""; float hull0 = StationCombat.Hull(U, st);
        while (ticks++ < 800 && U.Stations.TryGetValue(st.Id, out var cur) && cur.Owner != U.PlayerActor && U.FactionOf(cur) == _enemy)
        {
            me = P.Me;
            if (me.Hull < me.MaxHull * 0.25f) { Note($"Belagerung abgebrochen: Hülle {me.Hull:0}/{me.MaxHull:0}"); WarLog.Add($"[{U.Tick}] Rückzug Hülle niedrig"); _siegeRounds += 99; break; }
            var dir = st.Position - me.Position; float d = dir.Length;
            float want = reach * 0.7f + st.Radius;
            if (d > want) me.Position += dir.Normalized() * MathF.Min(d - want, 11f);
            float bearing = FiringArc.Bearing(dir.Length > 0.01f ? dir.Normalized() : new Vec2(0, -1));
            foreach (var s in slots) { var gs = PlayerGunnery.TryFireAtStation(U, me, s, st.Id, bearing); cnt[gs.Result.ToString()] = cnt.GetValueOrDefault(gs.Result.ToString()) + 1; if (gs.Result != GunResult.Fired) lastMsg = gs.Message; }
            P.Step(); P.TicksFighting++;
            if (!U.Ships.ContainsKey(me.Id)) break;
        }
        _siegeRounds++;
        Note($"Belagerungsrunde {_siegeRounds}: Schüsse {string.Join(" ", cnt.Select(k => $"{k.Key}={k.Value}"))}; Stationshülle {hull0:0}→{(U.Stations.TryGetValue(st.Id, out var sc) ? StationCombat.Hull(U, sc) : 0):0}; Abstand {Vec2.Distance(P.Me.Position, st.Position):0} (Radius {st.Radius}); Schiff #{P.Me.Id.Value} {P.Me.ModelKey} Sektor {U.Sectors[P.Me.Sector].Name} (PlayerSector {U.Sectors[U.PlayerSector].Name}), Station in {U.Sectors[st.Sector].Name}, andocken {P.Me.DockedAt.Value}, Transit {(P.Me.Transit is null ? "nein" : "ja")}; eigene Hülle {P.Me.Hull:0}/{P.Me.MaxHull:0}; letzte Weigerung: {lastMsg}");
        if (U.Stations.TryGetValue(st.Id, out var after) && (after.Owner == U.PlayerActor || U.FactionOf(after) != _enemy)) return true;
        float hp = U.Stations.TryGetValue(st.Id, out var a2) ? StationCombat.Hull(U, a2) : 0;
        if (_siegeRounds > 12) { Note($"Belagerung ohne Erfolg nach {_siegeRounds} Runden (Stationshülle {hp:0}) — Frieden/Tribut"); EndWar(); }
        return true;
    }

    void EndWar()
    {
        // Frieden oder Tribut
        var w = Wars.PlayerWar(U, _enemy);
        if (w is not null)
        {
            var d = P.Book("Tribut fordern", () => C.Execute(U, $"demand:{_enemy.Value}"));
            Note($"Tribut fordern: {d.Message}");
            if (Wars.PlayerWar(U, _enemy) is not null)
            {
                var c = P.Book("Waffenstillstand", () => C.Execute(U, $"ceasefire:{_enemy.Value}"));
                Note($"Waffenstillstand: {c.Message}");
                var pz = P.Book("Frieden", () => C.Execute(U, $"peace:{_enemy.Value}"));
                Note($"Frieden: {pz.Message}");
            }
            if (Wars.PlayerWar(U, _enemy) is null) Mark("Frieden/Tribut");
        }
        var lead = PlayerFleet.Owned(U).Where(s => s.Squadron.Value != 0 && Squadrons.Get(U, s)?.Leader == s.Id).FirstOrDefault();
        if (lead is not null && Squadrons.Get(U, lead) is { } sq) Squadrons.Disband(U, sq, out _);
        Heal();
        B.RestoreFlag();
        _siegeStart = -1; _siegeRounds = 0; _warTry = 0; _wars++; _chain = 0;
        _ws = (PlayerSeats.OwnSectors(U).Count >= 10 || _wars >= 8) ? WarState.Done : WarState.Idle; _nextWar = U.Tick + 14000;
    }
}
