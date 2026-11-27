using SpaceSim.Core.Combat;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.PlayerBot;

/// <summary>
/// Sonnet-Test 3: gezielte Lückenproben auf einem gespeicherten Bot-Stand (--load): "wing" (FleetAttack.OrderWing mit Händlern im Sektor),
/// "shadow" (Sprungantrieb/Tarnfeld gegen Sperren), "supply" (Munitionsnachschub über Fracht/Umladen).
/// Nur Spielerpfade (Dienste, Comms, Befehle, Spielerfunktionen); wo etwas künstlich gesetzt wird, steht es in der Ausgabe.
/// </summary>
public static class Probe3
{
    public static void Run(Universe u, string mode, ulong seed, string outDir)
    {
        var log = new Log(Path.Combine(outDir, "probe3-" + mode + ".log"), () => u.Tick);
        var pilot = new Pilot(u, log, seed);
        pilot.Runner.Run(u, 300);
        pilot.Led = new Ledger(u);
        var w = new StreamWriter(Path.Combine(outDir, "probe3-" + mode + ".txt")) { AutoFlush = true };
        void P(string s) { Console.WriteLine(s); w.WriteLine(s); }
        P($"== Probe {mode} bei Tick {u.Tick}, aktives Schiff #{u.PlayerShip.Value} {pilot.Me.ModelKey} in {u.Sectors[pilot.Me.Sector].Name}, Kasse {pilot.Wallet:0}");
        switch (mode)
        {
            case "wing": Wing(u, pilot, P); break;
            case "shadow": Shadow(u, pilot, P); break;
            case "jumpfit": JumpFit(u, P); break;
            case "supply": Supply(u, pilot, P); break;
            case "fleetammo": FleetAmmo(u, pilot, P); break;
            case "heavy": Heavy(u, pilot, P, false); break;
            case "heavytank": Heavy(u, pilot, P, true); break;
            case "fuelwhy": ProbeFuel.Run(u, pilot, P, 6000); break;
            case "fueltrace": ProbeFuel.Trace(u, pilot, P, 1500); break;
            case "reich": ProbeEmpire.Run(u, pilot, P, 14400); break;
            case "siegefly": ProbeSiege.Run(u, pilot, P, false, 3, 8); break;
            case "siegerally": ProbeSiege.Run(u, pilot, P, true, 3, 8); break;
            case "reach": foreach (var g in u.StationsById().Where(x => !x.IsBuildSite).GroupBy(x => x.TypeKey)) { var st0 = g.First(); var gm = (Guns: StationCombat.DefenseOf(u, st0)?.Guns.Count ?? 0, Range: StationCombat.GunRange(u, st0)); bool rk = StationCombat.DefenseOf(u, st0)?.Guns.Any(k => u.Modules.TryGetValue(k, out var mm) && mm.Stat("missileSpeed") > 0) == true; P($"{g.Key,-22} Radius {st0.Radius:0} Geschütze {gm.Guns} Reichweite {gm.Range:0} (ab Außenhaut), Hülle {StationCombat.MaxHull(u, st0):0} Schild {StationCombat.MaxShield(u, st0):0} Raketen {rk}"); } break;
        }
        w.Dispose();
    }

    static string Cmd(Ship s) => s.Command?.GetType().Name.Replace("Command", "") ?? "-";

    // ───────── FleetAttack.OrderWing mit Händlern ─────────
    static void Wing(Universe u, Pilot p, Action<string> P)
    {
        var own = PlayerFleet.Owned(u).Where(s => s.Id != u.PlayerShip).ToList();
        var traders = own.Where(s => s.Command is TradeCommand).ToList();
        P($"Eigene Schiffe {own.Count + 1}, davon mit TradeCommand {traders.Count}, MineCommand {own.Count(s => s.Command is MineCommand)}");
        // Sektor mit den meisten Händlern (Position über ThreatMap.Where wie Wing())
        var bySec = traders.GroupBy(s => ThreatMap.Where(u, s).Sector).OrderByDescending(g => g.Count()).ToList();
        foreach (var g in bySec.Take(5)) P($"  Händler in {u.Sectors[g.Key].Name}: {g.Count()}  (Modelle {string.Join(",", g.Select(x => x.ModelKey).Distinct())}, bewaffnet {g.Count(x => CombatSense.Armed(u, x))})");
        var sec = bySec[0].Key;
        // Das aktive Schiff in diesen Sektor stellen (Spielerfunktion „Übernehmen aus der Ferne“ bleibt beim Flaggschiff; hier einfach versetzen und andocken lösen)
        var me = p.Me; if (me.DockedAt.Value != 0) p.Undock();
        u.MoveShip(me, sec); u.PlayerSector = sec; me.Position = new Vec2(0, 0);
        P($"[künstlich versetzt] aktives Schiff nach {u.Sectors[sec].Name}");
        var st = u.StationsById().First(x => !x.IsBuildSite && u.Sectors[x.Sector].Id == sec && u.FactionOf(x) is { } fo0 && u.IsNpcFaction(fo0) && u.StationTypes[x.TypeKey].Government == GovernmentKind.None && !StationProtection.IsHideout(u, x));
        var f = u.FactionOf(st)!.Value;
        P($"Ziel {st.Name} (Fraktion {f.Value}) — Krieg: {SpaceSim.Core.Simulation.Wars.PlayerWar(u, f) is not null}");
        var comms = CommsActions.Default();
        P("Krieg erklären: " + comms.Execute(u, $"war:{f.Value}").Message);
        var target = new AttackCommand(default, st.Id, default, st.Sector);
        var wing = FleetAttack.Wing(u);
        P($"Wing(): {wing.Count} Schiffe: " + string.Join(" ", wing.Select(s => $"#{s.Id.Value}:{s.ModelKey}:{s.Role}:{Cmd(s)}:{(s.Squadron.Value != 0 ? "V" : "frei")}")));
        P($"davon Händler/Schürfer-Rolle: {wing.Count(s => s.Role is ShipRole.Trader or ShipRole.Miner)}, mit TradeCommand: {wing.Count(s => s.Command is TradeCommand)}, Ladung > 0: {wing.Count(s => s.UsedVolume(u) > 0.1f)}");
        P($"WingLabel: {FleetAttack.WingLabel(u, target)}");
        var before = wing.ToDictionary(s => s.Id, s => Cmd(s));
        var cargoVal = wing.Where(s => s.Command is TradeCommand).Sum(s => s.UsedVolume(u));
        int n = FleetAttack.OrderWing(u, target, out var msg);
        P($"OrderWing → {n}: {msg}");
        P("Danach: " + string.Join(" ", wing.Select(s => $"#{s.Id.Value}:{before[s.Id]}→{Cmd(s)}")));
        int taken = wing.Count(s => before[s.Id] == "Trade" && s.Command is AttackCommand);
        P($"=> {taken} Händler mit TradeCommand wurden auf Angriff umgestellt (Frachtvolumen an Bord {cargoVal:0}).");
        // 3000 Ticks weiterlaufen: was passiert mit den Händlern?
        long lost0 = u.Ships.Count;
        for (int i = 0; i < 3000; i++) p.Step();
        var after = wing.Where(s => u.Ships.ContainsKey(s.Id)).ToList();
        P($"Nach 3000 Ticks: Händler-Befehle: {string.Join(" ", after.Where(s => before[s.Id] == "Trade").Select(s => $"#{s.Id.Value}:{Cmd(s)}@{u.Sectors[s.Sector].Name}"))}; verloren {wing.Count - after.Count}");
    }

    // ───────── Sprungantrieb + Tarnfeld gegen Sperren ─────────
    static void Shadow(Universe u, Pilot p, Action<string> P)
    {
        var yards = u.StationsById().Where(x => x.TypeKey == "shipyard").ToList();
        foreach (var y in yards)
        {
            string[] mods = { "jump_drive_s", "cloak_s", "cloak_s_mk2" };
            P($"{y.Name} [{u.Sectors[y.Sector].Name}]: " + string.Join(", ", mods.Select(m => { var g = u.Goods.Values.FirstOrDefault(x => x.Key == m); return g is not null && y.Market.TryGetValue(g.Id, out var e) ? $"{m} {e.Amount:0.0}/{e.Target:0.0}" : m + " -"; })));
            foreach (var key in new[] { "shadow", "bastion" })
                if (ShipDealerService.Quote(u, y, p.Me, key) is { } q) P($"   {key}: Preis {q.Price:0} {(q.Buyable ? "LIEFERBAR" : "fehlt: " + string.Join(", ", q.Missing.Select(m => u.Goods[m.Good].Name + " " + m.Units.ToString("0.#"))) + (q.Locked is { } lk ? " GESPERRT " + lk : ""))}");
                else P($"   {key}: nicht im Angebot");
        }
        var pick = Environment.GetEnvironmentVariable("PROBE_YARD") ?? "";
        var yard = yards.Where(y => y.Name.Contains(pick)).OrderBy(y => u.Hops(p.Me.Sector, y.Sector)).First(y => ShipDealerService.Quote(u, y, p.Me, "shadow") is { Buyable: true } || false);
        P($"Wähle Werft {yard.Name}");
        if (!p.GoToStation(yard, out var why)) { P("Weg verwehrt: " + why); return; }
        var svc = p.Svc(yard, "shipdealer")!;
        var r = p.Do(yard, svc, "buy:shadow");
        P($"Kauf Schatten: {r.Success} {r.Message}");
        var sh = PlayerFleet.Owned(u).Where(s => s.ModelKey == "shadow").OrderByDescending(s => s.Id.Value).FirstOrDefault();
        if (sh is null) return;
        P($"Schatten #{sh.Id.Value}: Sprungreichweite {sh.Stats.JumpRange}, Ladezeit {sh.Stats.JumpChargeTicks}, Abkühlung {sh.Stats.JumpCooldownTicks}, Tarnung {sh.Stats.Cloak}, Fracht {sh.Stats.Cargo}, Leistung {sh.Stats.PowerOutput}/{sh.Stats.PowerDraw}, Tank {sh.TotalFuel:0}/{sh.Stats.FuelCapacity:0} Hülle {sh.MaxHull}");
        bool sw = PlayerFleet.SwitchTo(u, yard, sh.Id, out var smsg); P($"Umsteigen: {sw} {smsg}");
        var comms = CommsActions.Default();
        if (Environment.GetEnvironmentVariable("PROBE_MK2") == "1")
        {
            var ow = p.Svc(yard, "outfitter")!;
            float w0 = p.Wallet; var rr = p.Do(yard, ow, "fit:5:cloak_s_mk2");
            P($"Tarnfeld-Tausch Mk1→Mk2 (outfitter fit:5:cloak_s_mk2): {rr.Success} {rr.Message} Kosten {w0 - p.Wallet:0} Cr; Stärke jetzt {p.Me.Stats.Cloak}");
        }
        // Krieg: Fraktion 3 (Hephaistos/Aurelia) — Sperren gegen den Spieler entstehen ehrlich über die Beziehung
        var enemy = u.NpcFactionIds().First(x => x.Value == 3);
        P("Krieg erklären: " + comms.Execute(u, "war:3").Message);
        for (int i = 0; i < 2600; i++) p.Step();
        foreach (var sec in u.Sectors.Values.OrderBy(s => s.Id.Value))
            P($"  {sec.Name}: Besitzer {sec.Owner.Value} Sperrstufe {sec.Access.EntryLevel} gegen {sec.Access.Against?.Value} Exit {sec.Access.ExitLevel} erzwungen {sec.Access.Enforced}");
        foreach (var f in u.NpcFactionIds()) P($"  Beziehung {f.Value}: {Relations.Effective(u, u.PlayerActor, new ActorId(f.Value)):0.00}/{Relations.Effective(u, new ActorId(f.Value), u.PlayerActor):0.00}");
        var me = p.Me;
        P($"Schatten in {u.Sectors[me.Sector].Name}, andocken {me.DockedAt.Value}");
        if (me.DockedAt.Value != 0) p.Undock();
        me = p.Me;
        { // freien Punkt ohne Massenschatten anfliegen (echter Flug)
            Vec2? best = null; float bd = -1;
            var sec0 = u.Sectors[me.Sector]; float half = StationPlacement.HalfArea(sec0);
            for (float gx = -half; gx <= half; gx += 25f) for (float gy = -half; gy <= half; gy += 25f)
            { var q = new Vec2(gx, gy); if (JumpDrive.ShadowAt(u, me.Sector, q) is not null) continue; float d = Vec2.Distance(q, me.Position); if (best is null || d < bd) { best = q; bd = d; } }
            if (best is { } b) { p.FlyTo(b); P($"Freier Punkt für den Sprung: {b.X:0},{b.Y:0} (Flug {bd:0} m)"); }
        }
        var hephaistos = u.Sectors.Values.First(s => s.Name.Contains("Hephaistos"));
        P($"Route zu Hephaistos: {(u.FindRoute(me.Sector, hephaistos.Id, GateAccess.ProfileOf(u, me)) is { } rt ? rt.Count + " Tore" : "KEINE")}; Grund: {GateAccess.BlockReason(u, me, me.Sector, hephaistos.Id)}");
        foreach (var tgt in u.Sectors.Values.Where(s => JumpDrive.Hops(u, me.Sector, s.Id) is > 0 and var h && h <= me.Stats.JumpRange).OrderBy(s => s.Id.Value))
        {
            var ev = GateAccess.EvaluateJump(u, me.Sector, tgt.Id, GateAccess.ProfileOf(u, me), me);
            P($"  Sprungziel {tgt.Name} ({JumpDrive.Hops(u, me.Sector, tgt.Id)} Hops, Stufe {tgt.Access.EntryLevel} gegen {tgt.Access.Against?.Value}): StartIssue ohne Tarnung = {JumpDrive.StartIssue(u, me, tgt.Id) ?? "möglich"}; Auswertung erlaubt {ev.Allowed} Grund {ev.Reason} Maut {ev.Toll}");
        }
        // Tarnung ein
        P("Tarnung ein: " + (Cloak.TurnOn(u, me) ?? "ok") + $" (Stärke {me.Stats.Cloak}, aktiv {Cloak.ActiveStrength(u, me)}, Leistung {me.Stats.PowerOutput}/{me.Stats.PowerDraw})");
        foreach (var tgt in u.Sectors.Values.Where(s => JumpDrive.Hops(u, me.Sector, s.Id) is > 0 and var h && h <= me.Stats.JumpRange).OrderBy(s => s.Id.Value))
        {
            var ev = GateAccess.EvaluateJump(u, me.Sector, tgt.Id, GateAccess.ProfileOf(u, me), me);
            P($"  [Tarnfeld {me.Stats.Cloak}] Sprungziel {tgt.Name}: StartIssue = {JumpDrive.StartIssue(u, me, tgt.Id) ?? "möglich"}; erlaubt {ev.Allowed} Grund {ev.Reason}");
        }
        // Echter Sprung nach Hephaistos (oder erstes erlaubtes Feindziel)
        var enemySecs = u.Sectors.Values.Where(s => s.Owner == enemy && JumpDrive.Hops(u, me.Sector, s.Id) is > 0 and var h && h <= me.Stats.JumpRange).ToList();
        P($"Feind-Sektoren in Sprungreichweite: {string.Join(", ", enemySecs.Select(s => s.Name))}");
        foreach (var tgt in enemySecs)
        {
            var wallet0 = p.Wallet; var fuel0 = me.TotalFuel;
            var ri = JumpDrive.Start(u, me, tgt.Id);
            P($"Start Sprung → {tgt.Name}: {ri ?? "lädt"}");
            if (ri is not null) continue;
            string? res = null; u.Events.Raised += EvH; void EvH(object e) { if (e is ShipDriveJumped j && j.Ship == me.Id) res = "gesprungen: " + j.Message; if (e is JumpChargeAborted a && a.Ship == me.Id) res = "abgebrochen: " + a.Reason; if (e is CrimeReported c && c.Actor == u.PlayerActor) res += $" [Verbrechen {c.Crime} F{c.Faction.Value}]"; }
            for (int i = 0; i < 200 && JumpDrive.Charging(me); i++) p.Step();
            u.Events.Raised -= EvH;
            P($"  Ergebnis: {res ?? "?"}; jetzt in {u.Sectors[me.Sector].Name}, Tarnung {me.Cloaked}, Kasse {wallet0:0}→{p.Wallet:0}, Tank {fuel0:0}→{me.TotalFuel:0}");
            if (me.Sector == tgt.Id) break;
            // Mk2? Upgrade-Angebot an einer Werft prüfen
        }
        // Zweiter Krieg: Fraktion 2 (Gamma/Delta/Kalypso, kein Nebel) — reicht Tarnfeld Mk1 (1,5) gegen Fühler 2?
        P("Krieg erklären 2: " + comms.Execute(u, "war:2").Message);
        for (int i = 0; i < 2600; i++) p.Step();
        me = p.Me;
        foreach (var sec in u.Sectors.Values.Where(s2 => s2.Owner.Value == 2)) P($"  {sec.Name}: Sperrstufe {sec.Access.EntryLevel} gegen {sec.Access.Against?.Value}");
        foreach (var tgt in u.Sectors.Values.Where(s2 => s2.Owner.Value == 2 && JumpDrive.Hops(u, me.Sector, s2.Id) is > 0 and var h && h <= me.Stats.JumpRange))
        { var ev = GateAccess.EvaluateJump(u, me.Sector, tgt.Id, GateAccess.ProfileOf(u, me), me); P($"  [Mk1, Tarnung {me.Cloaked}, aktiv {Cloak.ActiveStrength(u, me)}] Sprung {tgt.Name}: erlaubt {ev.Allowed} {ev.Reason}"); }
        P($"Beziehung zu 3 nach dem Sprung: {Relations.Effective(u, u.PlayerActor, new ActorId(3)):0.00}; Fahndung: {u.Actors[u.PlayerActor].Wanted.GetValueOrDefault(new FactionId(3))?.Heat:0.0}");
        // Landung im Feindsektor: Station andocken (Handel/Reparatur), Sensor der Station?
        var near = u.StationsById().Where(s => s.Sector == me.Sector && !s.IsBuildSite).OrderBy(s => Vec2.Distance(s.Position, me.Position)).Take(2).ToList();
        foreach (var s in near) P($"  nächste Station {s.Name}: Docking {Docking.Issue(u, s, me) ?? "ok"}, Handel verweigert: {TradeActions.Refusal(u, me, s) ?? "nein"}");
        // Wiederholung: Rücksprung
        P($"Abkühlung {JumpDrive.CooldownLeft(u, me)} Ticks");
    }

    // ───────── Munitionsnachschub ─────────
    static void Supply(Universe u, Pilot p, Action<string> P)
    {
        var ammo = u.Goods.Values.First(g => g.Key == "ammo");
        var warships = PlayerFleet.Owned(u).Where(s => s.Id != u.PlayerShip && s.Stats.AmmoCapacity > 0 && s.Has(Capability.Combat) && s.Role != ShipRole.Trader && s.Role != ShipRole.Miner).OrderByDescending(s => s.Stats.AmmoCapacity).ToList();
        var ws = warships.First();
        var carriers = PlayerFleet.Owned(u).Where(s => s.Stats.Cargo >= 100 && s.Id != ws.Id && s.Id != u.PlayerShip).ToList();
        P($"Kriegsschiff #{ws.Id.Value} {ws.ModelKey} Magazin {ws.AmmoLoaded:0}/{ws.Stats.AmmoCapacity:0}, Sektor {u.Sectors[ws.Sector].Name}, Befehl {Cmd(ws)}");
        // Magazin leeren (Gefechtsverbrauch nachgestellt), Frachter in dieselbe Stelle
        ws.Systems.AmmoMissing = ws.Stats.AmmoCapacity;
        var tanker = carriers.FirstOrDefault(s => s.ModelKey == "cistern") ?? carriers.First();
        P($"Versorger: #{tanker.Id.Value} {tanker.ModelKey} Fracht {tanker.Stats.Cargo:0} (Befehl {Cmd(tanker)})");
        // Munition kaufen (Spielerpfad) mit dem aktiven Schiff an einer Werft, dann umladen an einen Versorger im selben Raum
        var yard = u.StationsById().Where(x => x.TypeKey == "shipyard").OrderBy(y => u.Hops(p.Me.Sector, y.Sector)).First();
        p.GoToStation(yard, out _);
        var trade = p.Svc(yard, "trade")!;
        var buy = p.Do(yard, trade, "buy:" + ammo.Id.Value, ammo.Id, 60f); P($"Munition kaufen: {buy.Success} {buy.Message}");
        var me = p.Me;
        P($"aktives Schiff Munition {me.Cargo.GetValueOrDefault(ammo.Id):0}");
        // Kriegsschiff und aktives Schiff nebeneinander im Raum
        p.Undock(); me = p.Me;
        u.MoveShip(ws, me.Sector); ws.Position = me.Position + new Vec2(10, 0); ws.Transit = null; ws.Command = null; ws.DockedAt = default;
        P($"[künstlich] Kriegsschiff neben das aktive Schiff gestellt; Partner: {CargoTransfer.Partners(u, me).Count}; Problem: {CargoTransfer.Issue(u, me, ws, ammo.Id)}");
        P($"FromCargoIssue(Kriegsschiff, ohne Munition) = {Rearm.FromCargoIssue(u, ws)}");
        float moved = CargoTransfer.Transfer(u, me, ws, ammo.Id, 60f);
        P($"Umladen: {moved:0} Munition ins Kriegsschiff; FromCargoIssue = {Rearm.FromCargoIssue(u, ws) ?? "ok"}");
        float into = Rearm.FromCargo(u, ws);
        P($"Ins Magazin: {into:0}; Magazin {ws.AmmoLoaded:0}/{ws.Stats.AmmoCapacity:0}");
        // Raketen und Treibstoff
        P($"Raketenmagazin {ws.Stats.MissileCapacity:0}, Nachschub per Fracht: keine Funktion (Missiles.AtStation/AtHome nur Station)");
        var fuel = u.Goods.Values.Where(g => g.Category == "fuel").ToList();
        P($"Treibstoff-Waren: {string.Join(", ", fuel.Select(g => g.Key))}; Tank {ws.TotalFuel:0}/{ws.Stats.FuelCapacity:0}; Tank füllen aus Fracht: nur über Station (Refuel.BuyFuel)");
        // Fleet automatics: leert ein Kriegsschiff im Befehl 'attack' sein Magazin → Notfall?
        P($"OrderSystem.EmergencyReason bei leerem Magazin: {OrderSystem.EmergencyReason(u, ws) ?? "keiner (Munition zählt nicht als Notfall)"}");
        ws.Systems.AmmoMissing = ws.Stats.AmmoCapacity;
        P($"Magazin leer → Emergency = {OrderSystem.EmergencyReason(u, ws) ?? "keiner"}");
    }

    // ───────── Verbandsangriff: Munition/Rückzug ohne Nachschub ─────────
    static void FleetAmmo(Universe u, Pilot p, Action<string> P)
    {
        var ws = PlayerFleet.Owned(u).Where(s => s.Id != u.PlayerShip && s.Has(Capability.Combat) && s.Stats.AmmoCapacity > 0 && s.Role != ShipRole.Trader && s.Role != ShipRole.Miner && s.Stats.WeaponPower > 8).OrderByDescending(s => s.Stats.WeaponPower).Take(8).ToList();
        P($"Verband: {string.Join(" ", ws.Select(s => $"#{s.Id.Value}:{s.ModelKey}:Mag{s.AmmoLoaded:0}/{s.Stats.AmmoCapacity:0}:Rak{(s.Stats.MissileCapacity - s.Systems.MissilesMissing):0}"))}");
        var beta = u.Sectors.Values.First(x => x.Name.Contains("Beta"));
        var st = u.StationsById().First(x => x.Sector == beta.Id && x.TypeKey == "wafer_fab");
        foreach (var s in ws) { u.MoveShip(s, beta.Id); s.Position = st.Position + new Vec2(-80, 0); s.Transit = null; s.DockedAt = default; s.Command = null; }
        P($"[künstlich] {ws.Count} Kampfschiffe nach Beta gestellt, Ziel {st.Name} Hülle {StationCombat.Hull(u, st):0}");
        var comms = CommsActions.Default();
        P("Krieg: " + comms.Execute(u, "war:1").Message);
        foreach (var s in ws) Commands.Give(u, s, new AttackCommand(default, st.Id, default, st.Sector), out _, ignoreCapacity: true);
        var seen = new Dictionary<string, int>();
        u.Events.Raised += e => { var n = e.GetType().Name; if (n is "ShotFired" or "ShotAtStation" or "StationShot" or "MissileLaunched" or "MissileResolved" or "CargoDropped" or "ModuleDamaged") return; seen[n] = seen.GetValueOrDefault(n) + 1; if (n.Contains("Fell") || n.Contains("Capitul") || n.Contains("Emergency") || n.Contains("Siege") || n.Contains("CommandEnded") || n.Contains("Alarm") || n.Contains("Peace") || n.Contains("War") || n.Contains("Tribut")) P($"   [{u.Tick}] {n}: {e}"); };
        for (int t = 0; t <= 4000; t++)
        {
            if (t % 500 == 0) P($"[{u.Tick}] Hülle Ziel {StationCombat.Hull(u, u.Stations[st.Id]):0}; " + string.Join(" ", ws.Where(s => u.Ships.ContainsKey(s.Id)).Select(s => $"#{s.Id.Value}:H{s.Hull / s.MaxHull:0.00}:Mag{s.AmmoLoaded:0}:{Cmd(s)}{(s.Emergency ? "!" : "")}:{u.Sectors[s.Sector].Name[7..]}")) + $" | verloren {ws.Count(s => !u.Ships.ContainsKey(s.Id))}");
            p.Step();
        }
        P("Ereignisse: " + string.Join(", ", seen.OrderByDescending(k => k.Value).Take(25).Select(k => k.Key + " " + k.Value)));
    }

    // ───────── Sprungantrieb/Tarnfeld in den Nutzplätzen der Großkampfschiffe (reine Rechnung mit FitRules/ShipStats) ─────────
    static void JumpFit(Universe u, Action<string> P)
    {
        foreach (var gk in new[] { "ammo", "missile", "hull_plates" })
        {
            var g = u.Goods.Values.FirstOrDefault(x => x.Key == gk); if (g is null) continue;
            foreach (var st in u.StationsById().Where(x => !x.IsBuildSite && x.Market.ContainsKey(g.Id) && (u.StationTypes[x.TypeKey].Services.Contains("rearm") || u.StationTypes[x.TypeKey].Services.Contains("repair") || x.Produces(g.Id))))
            { var e = st.Market[g.Id]; P($"Regal {gk} {st.Name} [{u.Sectors[st.Sector].Name}] Besitzer {(st.Owner == u.PlayerActor ? "ICH" : (u.FactionOf(st)?.Value.ToString() ?? "-"))}: {e.Amount:0.0}/{e.Target:0.0} {(st.Produces(g.Id) ? "(Hersteller)" : "")} Dienste {string.Join(",", u.StationTypes[st.TypeKey].Services.Where(x => x is "rearm" or "repair"))}"); }
        }
        foreach (var key in new[] { "destroyer", "carrier", "bastion", "frigate", "battlecruiser", "corvette", "gunboat" })
        {
            if (!u.ShipModels.TryGetValue(key, out var m) || !u.ShipClasses.TryGetValue(m.ClassKey, out var cls)) continue;
            var util = Enumerable.Range(0, cls.Slots.Count).Where(i => u.SlotTypes.TryGetValue(cls.Slots[i].Type, out var st) && cls.Slots[i].Type == "utility").ToList();
            foreach (var combo in new[] { new[] { "jump_drive_s" }, new[] { "jump_drive_s", "cloak_s_mk2" }, new[] { "jump_drive_s", "cloak_s" } })
            {
                var fit = m.Fit.Select(x => x is null ? null : u.Modules[x]).ToList();
                var names = new List<string>();
                for (int k = 0; k < combo.Length && k < util.Count; k++) { fit[util[k]] = u.Modules[combo[k]]; names.Add($"{m.Fit[util[k]]}->{combo[k]}"); }
                var issue = FitRules.Check(u.SlotTypes, u.Modules, cls, cls.Slots.ToList(), fit.Select(x => x?.Key).ToList());
                var st2 = ShipStats.Compute(cls, fit);
                P($"{key}: Nutzplätze {util.Count}; {string.Join(" + ", names)}: {(issue is null ? "gültig" : "UNGÜLTIG: " + FitRules.Describe(issue))}; Leistung {st2.PowerOutput:0.#}/{st2.PowerDraw:0.#} Sprung {st2.JumpRange} Tarnung {st2.Cloak}");
            }
        }
    }

    // ───────── Test 7: schwere Belagerung — Großkampfschiffe allein gegen Großkampfschiffe + Versorger (SupplyCommand) ─────────
    static void Heavy(Universe u, Pilot p, Action<string> P, bool tanker)
    {
        var comms = CommsActions.Default();
        var st = u.StationsById().Where(x => !x.IsBuildSite && u.FactionOf(x) is { } ff && u.IsNpcFaction(ff) && !Bandits.IsEstablished(u, ff) && !StationProtection.IsHideout(u, x) && u.StationTypes[x.TypeKey].Government != GovernmentKind.Outpost && Wars.PlayerDeclareIssue(u, ff) is null)
            .OrderByDescending(x => StationCombat.MaxHull(u, x)).First();
        var f = u.FactionOf(st)!.Value;
        var ws = PlayerFleet.Owned(u).Where(s => s.Id != u.PlayerShip && s.Has(Capability.Combat) && u.ShipModels.TryGetValue(s.ModelKey, out var m) && m.Jobs.Count == 0 && !s.IsRental() && s.Stats.Hull >= 600f).OrderByDescending(s => s.Stats.Hull * s.Stats.WeaponPower).Take(8).ToList();
        P($"Ziel {st.Name} [{st.TypeKey}] Sektor {u.Sectors[st.Sector].Name} Fraktion {f.Value} Hülle {StationCombat.Hull(u, st):0}/{StationCombat.MaxHull(u, st):0} Schild {StationCombat.MaxShield(u, st):0} Geschütze {StationCombat.DefenseOf(u, st)?.Guns.Count}");
        P($"Verband {ws.Count}: " + string.Join(" ", ws.Select(s => $"#{s.Id.Value}:{s.ModelKey}:H{s.MaxHull:0}:WP{s.Stats.WeaponPower:0}:Mag{s.AmmoLoaded:0}/{s.Stats.AmmoCapacity:0}:Rak{s.Stats.MissileCapacity - s.Systems.MissilesMissing:0}")));
        foreach (var s in ws) { u.MoveShip(s, st.Sector); s.Position = st.Position + new Vec2(-(st.Radius + 55f), 0); s.Transit = null; s.DockedAt = default; s.Command = null; if (s.Squadron.Value != 0) { var sq0 = Squadrons.Get(u, s); if (sq0 is not null) Squadrons.Disband(u, sq0, out _); } }
        var tankers = new List<Ship>();
        if (tanker)
        {
            foreach (var t in PlayerFleet.Owned(u).Where(s => s.Id != u.PlayerShip && !s.Has(Capability.Combat) | s.Stats.WeaponPower <= 6f).Where(s => s.Stats.Cargo >= 250f && s.Command is TradeCommand or MineCommand && !ws.Contains(s)).Take(2))
            { u.MoveShip(t, st.Sector); t.Position = st.Position + new Vec2(-(st.Radius + 140f), 30f); t.Transit = null; t.DockedAt = default; Commands.Cancel(u, t); tankers.Add(t); }
            foreach (var t in tankers) { bool ok = Commands.Give(u, t, new SupplyCommand(st.Sector), out var msg, ignoreCapacity: true); P($"Versorger #{t.Id.Value} {t.ModelKey} Fracht {t.Stats.Cargo:0}: Versorgen {ok} {msg}"); }
        }
        P($"[künstlich] {ws.Count} Kampfschiffe{(tankers.Count > 0 ? $" + {tankers.Count} Versorger" : "")} in {u.Sectors[st.Sector].Name} gestellt");
        P("Krieg: " + comms.Execute(u, $"war:{f.Value}").Message);
        foreach (var s in ws) Commands.Give(u, s, new AttackCommand(default, st.Id, default, st.Sector), out _, ignoreCapacity: true);
        int supplied = 0, fell = 0; u.Events.Raised += e => { if (e is FleetSupplied) supplied++; if (e is StationFell sf && sf.Station == st.Id) fell++; };
        long t0 = u.Tick; bool taken = false;
        for (int i = 0; i <= 12000; i++)
        {
            if (i % 600 == 0) P($"[+{u.Tick - t0}] Hülle Ziel {(u.Stations.TryGetValue(st.Id, out var c) ? StationCombat.Hull(u, c) : 0):0} {(u.Stations.TryGetValue(st.Id, out var c2) && StationCombat.IsDown(u, c2) ? "AUS" : "")} Besitzer {(u.Stations.TryGetValue(st.Id, out var c3) ? (c3.Owner == u.PlayerActor ? "ICH" : c3.Owner.Value.ToString()) : "weg")}; Versorgt {supplied}; " + string.Join(" ", ws.Select(s => u.Ships.ContainsKey(s.Id) ? $"#{s.Id.Value}:{s.ModelKey[..Math.Min(4, s.ModelKey.Length)]}:H{s.Hull / s.MaxHull:0.00}:M{s.AmmoLoaded:0}:R{s.Stats.MissileCapacity - s.Systems.MissilesMissing:0}:{Cmd(s)}" : $"#{s.Id.Value}:tot")));
            p.Step();
            if (!taken && (!u.Stations.TryGetValue(st.Id, out var cc) || cc.Owner == u.PlayerActor || StationCombat.IsDown(u, cc))) { taken = true; P($"[+{u.Tick - t0}] Ziel gefallen/ausgeschaltet (Fell-Ereignisse {fell})"); break; }
        }
        P($"Ende nach {u.Tick - t0} Ticks: Ziel gefallen {taken}; Verband lebt {ws.Count(s => u.Ships.ContainsKey(s.Id))}/{ws.Count}; Versorgungen {supplied}");
    }
}
