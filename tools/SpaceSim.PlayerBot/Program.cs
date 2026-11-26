using SpaceSim.Core.Combat;
using SpaceSim.Core.Persistence;
using System.Diagnostics;
using SpaceSim.Core.Content;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.PlayerBot;

string? loadFile = null, saveFile = null; long saveAt = -1;
string world = "tools/SpaceSim.PlayerBot/Content/world24.json", start = "miner", outDir = "spielerbot-out";
ulong seed = 11; long ticks = 2_000_000, every = 20_000;
for (int i = 0; i < args.Length; i++)
    switch (args[i])
    {
        case "--world": world = args[++i]; break;
        case "--start": start = args[++i]; break;
        case "--seed": seed = ulong.Parse(args[++i]); break;
        case "--ticks": ticks = long.Parse(args[++i]); break;
        case "--out": outDir = args[++i]; break;
        case "--every": every = long.Parse(args[++i]); break;
        case "--load": loadFile = args[++i]; break;
        case "--save-at": saveAt = long.Parse(args[++i]); saveFile = args[++i]; break;
        case "--richgate": Empire.RichGate = double.Parse(args[++i]); break;
        case "--only": Empire.Only = args[++i]; break;
        case "--dominion": Empire.Dom = true; break;
        case "--domwar": Empire.DomWar = true; break;
        case "--banditfirst": Empire.BanditFirst = true; break;
        case "--domshare": Empire.DomShare = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
        case "--domforemen": Empire.DomForemen = int.Parse(args[++i]); break;
        case "--mixed": Empire.Dom = true; Empire.DomWar = true; Empire.MilOn = true; Empire.Mixed = true; Empire.RetreatHull = 0.5f; break;
        case "--s4": Empire.S4 = true; Empire.Dom = true; Empire.DomWar = true; Empire.MilOn = true; Empire.Mixed = true; Empire.RetreatHull = 0.5f; break;
        case "--supply": Empire.Supply = true; break;
        case "--needbig": Empire.NeedBig = int.Parse(args[++i]); break;
        case "--noflood": Empire.NoFlood = true; break;
        case "--hunter": Hunter.On = true; break;
        case "--mix": Hunter.Mix = true; Hunter.MixAt = long.Parse(args[++i]); break;
    }
Directory.CreateDirectory(outDir); Empire.S4Out = outDir;
var def = WorldBuilder.Parse(File.ReadAllText(world));
var u = loadFile is null ? WorldBuilder.Build(def, seed, start) : SaveGameJson.Read(File.ReadAllText(loadFile)).Universe;
// Welle C: ein älterer Spielstand trägt seine gespeicherten Regeln (ohne Welle C) — mit --wellec gelten die Welle-C-Regeln der Welt-Datei
if (args.Contains("--wellec") && def.Rules is { } wr)
{
    var ur = u.Rules;
    ur.PlayerSectorTaxes = wr.PlayerSectorTaxes; ur.PlayerSectorTaxShare = wr.PlayerSectorTaxShare; ur.PlayerUpkeepPriority = wr.PlayerUpkeepPriority; ur.PlayerUpkeepReserveTicks = wr.PlayerUpkeepReserveTicks;
    ur.PlayerUpkeepWarnDays = wr.PlayerUpkeepWarnDays; ur.PlayerStandingFloor = wr.PlayerStandingFloor; ur.PlayerStandingRecoveryPerDay = wr.PlayerStandingRecoveryPerDay;
    ur.PlayerStandingSoftReasons = new List<string>(wr.PlayerStandingSoftReasons); ur.PlayerSeatContested = wr.PlayerSeatContested;
}
// Welle D: wie --wellec die Welle-D-Regeln der Welt-Datei auf einen älteren Spielstand
if (args.Contains("--welled") && def.Rules is { } wdr)
{
    var ur = u.Rules;
    ur.IdleShelfKeepLargeYard = wdr.IdleShelfKeepLargeYard; ur.IdleShelfKeepKeys = new List<string>(wdr.IdleShelfKeepKeys);
    ur.PlayerStationFunding = wdr.PlayerStationFunding; ur.PlayerStationFundPerDay = wdr.PlayerStationFundPerDay;
    ur.PlayerStationAmmoTarget = wdr.PlayerStationAmmoTarget; ur.PlayerStationMissileTarget = wdr.PlayerStationMissileTarget;
    ur.FleetSupply = wdr.FleetSupply; ur.FleetSupplyRange = wdr.FleetSupplyRange; ur.FleetSupplyTicks = wdr.FleetSupplyTicks;
    ur.PlayerMinerFuelCost = wdr.PlayerMinerFuelCost; ur.PlayerTankReserve = wdr.PlayerTankReserve; ur.PlayerWaitForSupplier = wdr.PlayerWaitForSupplier;
}
if (Array.IndexOf(args, "--probe3") is var p3 && p3 >= 0) { Probe3.Run(u, args[p3 + 1], seed, outDir); return; }
if (args.Contains("--sites"))
{
    if (args.Contains("--run")) new SimulationRunner().Run(u, 60);
    foreach (var st in PlayerBuilding.OwnSites(u))
    {
        var sec = u.Sectors[st.Sector]; var site = st.Site!;
        Console.WriteLine($"Baustelle {st.Id.Value} {site.TypeKey} in {sec.Name} (Besitzer {sec.Owner.Value}) seit {site.StartedTick}, Fortschritt {site.LastProgressTick}, blockiert seit {site.BlockedSince}, Bauschiff {site.Builder.Value}, Montage {site.AssemblyTicks} Treuhand {st.Credits:0.0}");
        foreach (var kv in site.Required) Console.WriteLine($"   {u.Goods[kv.Key].Name}: {st.Stock(kv.Key).Amount:0.0000}/{kv.Value:0.0000} fehlt {site.Missing(st, kv.Key):0.0000}");
    }
    foreach (var f in PlayerFleet.Owned(u).Where(x => x.ModelKey == "foreman")) Console.WriteLine($"Bauschiff {f.Id.Value} in {u.Sectors[f.Sector].Name} Befehl {f.Command?.GetType().Name} Ladung {f.UsedVolume(u):0} Kasse {f.Credits:0} Tick {u.Tick} Pos {f.Position.X:0},{f.Position.Y:0} Transit {(f.Transit is null ? "nein" : "ja")} Order {f.Order?.GetType().Name} Treibstoff {f.TotalFuel:0}/{f.Stats.FuelCapacity:0} Bauplatz-Abstand {PlayerBuilding.OwnSites(u).Select(x => Vec2.Distance(f.Position, x.Position)).DefaultIfEmpty(-1).Min():0}");
    return;
}
if (args.Contains("--fuelreport"))
{
    foreach (var g in u.Goods.Values.Where(g => g.Category == "fuel"))
    {
        Console.WriteLine($"Treibstoff {g.Name} (id {g.Id.Value}, Basis {g.BasePrice})");
        foreach (var st in u.StationsById().Where(s => s.Market.ContainsKey(g.Id)))
        { var e = st.Market[g.Id]; Console.WriteLine($"  {st.Name} [{u.Sectors[st.Sector].Name}] Bestand {e.Amount:0}/{e.Target:0} Preis {e.Price:0.0} Kasse {st.Credits:0} verkauft:{st.Produces(g.Id)}"); }
    }
    foreach (var sh in PlayerFleet.Owned(u).OrderBy(s => s.Id.Value))
        Console.WriteLine($"Schiff #{sh.Id.Value} {sh.ModelKey} {sh.Role} Befehl {sh.Command?.GetType().Name} Sektor {u.Sectors[sh.Sector].Name} Tank {sh.TotalFuel:0}/{sh.Stats.FuelCapacity:0} Kasse {sh.Credits:0} angedockt {sh.DockedAt.Value}");
    return;
}
if (args.Contains("--fightreport"))
{
    Console.WriteLine($"Tick {u.Tick} Aktives Schiff #{u.PlayerShip.Value} Kriege: {string.Join(", ", u.Wars.Select(w => w.A.Value + "-" + w.B.Value))}");
    foreach (var f in u.Fights.Encounters) { Console.WriteLine($"Gefecht {f.Id} {f.Cause} Sektor {u.Sectors[f.Sector].Name} seit {f.StartTick} Station {f.Station.Value} Teilnehmer {string.Join(",", f.Participants.Select(p => (p.Side == 0 ? "A" : "B") + p.Ship.Value + (p.Active ? "" : "(raus)")))}"); }
    foreach (var p in u.PendingEncounters) Console.WriteLine($"Warnung gegen {p.Target.Value}");
    return;
}
if (args.Contains("--shelfreport"))   // Welle D: Regalwaren eigener Stationen (Bestand/Ziel/Preis/Kasse)
{
    if (args.Contains("--run")) new SimulationRunner().Run(u, 2000);
    foreach (var st in u.StationsById().Where(x => !x.IsBuildSite && x.Owner == u.PlayerActor))
    {
        var shelves = st.Market.Where(kv => st.IsStockOnly(kv.Key)).Select(kv => $"{u.Goods[kv.Key].Key} {kv.Value.Amount:0.#}/{kv.Value.Target:0.#} @{kv.Value.Price:0.0}");
        Console.WriteLine($"{st.Name} [{st.TypeKey}] {u.Sectors[st.Sector].Name} Kasse {st.Credits:0} Dienste {string.Join(",", u.StationTypes[st.TypeKey].Services)} | {string.Join("; ", shelves)}");
    }
    Console.WriteLine($"Kasse Spieler {u.PlayerWallet.Credits:0}");
    return;
}
if (args.Contains("--sectorreport"))
{
    foreach (var sec in u.Sectors.Values.OrderBy(x => x.Id.Value))
        Console.WriteLine($"Sektor {sec.Id.Value} {sec.Name} Besitzer {(sec.Owner == u.PlayerFaction ? "SPIELER" : sec.Owner.Value.ToString())} Stufe {sec.Control} Sperrstufe {sec.Access.EntryLevel} gegen {sec.Access.Against?.Value} Einfluss {string.Join(" ", sec.Influence.Select(kv => $"{kv.Key.Value}:{kv.Value:0.00}"))} Stationen {sec.Stations.Count(id => u.Stations.TryGetValue(id, out var st) && st.Owner == u.PlayerActor)}/{sec.Stations.Count}");
    var own = PlayerFleet.Owned(u);
    Console.WriteLine($"Schiffe {own.Count}; ohne Befehl {own.Count(x => x.Command is null)}; nach Modell/Befehl:");
    foreach (var g in own.GroupBy(x => x.ModelKey + " / " + (x.Command?.GetType().Name ?? "-")).OrderByDescending(g => g.Count())) Console.WriteLine($"  {g.Key}: {g.Count()}");
    Console.WriteLine($"Befehlskapazität {Commands.Capacity(u, u.PlayerActor)}");
    return;
}
if (args.Contains("--atkprobe"))
{
    var comms = SpaceSim.Core.Comms.CommsActions.Default();
    var runner = new SimulationRunner(); runner.Run(u, 10);
    var f = u.NpcFactionIds().First(x => x.Value == 3);
    var st = u.StationsById().First(x => !x.IsBuildSite && u.FactionOf(x) == f && u.Sectors[x.Sector].Owner == f && u.StationTypes[x.TypeKey].Government == GovernmentKind.None && !StationProtection.IsHideout(u, x));
    var ships = PlayerFleet.Owned(u).Where(x => x.Id != u.PlayerShip && x.Has(Capability.Combat) && u.ShipModels.TryGetValue(x.ModelKey, out var m) && m.Jobs.Count == 0).OrderByDescending(x => x.Stats.WeaponPower).Take(3).ToList();
    Console.WriteLine($"Ziel {st.Name} ({u.Sectors[st.Sector].Name}), Schiffe {string.Join(",", ships.Select(x => x.ModelKey))}");
    var crimes = new List<string>();
    u.Events.Raised += e => { if (e is SpaceSim.Core.Events.CrimeReported c && c.Actor == u.PlayerActor) crimes.Add($"[{u.Tick}] {c.Crime} F{c.Faction.Value}"); if (e is SpaceSim.Core.Combat.FleetAttackStarted fa) crimes.Add($"[{u.Tick}] FleetAttackStarted crime={fa.Crime}"); };
    Console.WriteLine("Krieg: " + comms.Execute(u, "war:3").Message);
    foreach (var sh in ships) { var ok = Commands.Give(u, sh, new AttackCommand(default, st.Id, default, st.Sector), out var msg, ignoreCapacity: true); Console.WriteLine($"Befehl #{sh.Id.Value}: {ok} {msg}"); }
    for (int i = 0; i < 6000; i++) { runner.Step(u); if (i % 1000 == 999) Console.WriteLine($"[{u.Tick}] Hülle Ziel {StationCombat.Hull(u, u.Stations[st.Id]):0}; Schiffe {string.Join(" ", ships.Where(x => u.Ships.ContainsKey(x.Id)).Select(x => $"{x.ModelKey}:{u.Sectors[x.Sector].Name}:{x.Command?.GetType().Name}:{x.Hull:0}"))}"); }
    Console.WriteLine("Waffenstillstand: " + comms.Execute(u, "ceasefire:3").Message + " | im Krieg: " + (SpaceSim.Core.Simulation.Wars.PlayerWar(u, f) is not null));
    int c0 = crimes.Count;
    for (int i = 0; i < 6000; i++) { runner.Step(u); if (i % 1000 == 999) Console.WriteLine($"[{u.Tick}] nach Waffenstillstand: Hülle Ziel {StationCombat.Hull(u, u.Stations[st.Id]):0}; Krieg {(SpaceSim.Core.Simulation.Wars.PlayerWar(u, f) is not null)}; Befehle {string.Join(" ", ships.Where(x => u.Ships.ContainsKey(x.Id)).Select(x => x.Command?.GetType().Name ?? "-"))}; Verbrechen seit Waffenstillstand {crimes.Count - c0}; Beziehung {SpaceSim.Core.Simulation.Relations.Effective(u, u.PlayerActor, new ActorId(3)):0.00}"); }
    Console.WriteLine("Frieden: " + comms.Execute(u, "peace:3").Message + " | im Krieg: " + (SpaceSim.Core.Simulation.Wars.PlayerWar(u, f) is not null));
    int c1 = crimes.Count(x => !x.Contains("FleetAttackStarted"));
    for (int i = 0; i < 4000; i++) { runner.Step(u); if (i % 1000 == 999) Console.WriteLine($"[{u.Tick}] nach Frieden: Hülle Ziel {StationCombat.Hull(u, u.Stations[st.Id]):0}; Krieg {(SpaceSim.Core.Simulation.Wars.PlayerWar(u, f) is not null)}; Befehle {string.Join(" ", ships.Where(x => u.Ships.ContainsKey(x.Id)).Select(x => (x.Command?.GetType().Name ?? "-") + "@" + u.Sectors[x.Sector].Name))}; Verbrechen {crimes.Count(x => !x.Contains("FleetAttackStarted")) - c1}; Beziehung {SpaceSim.Core.Simulation.Relations.Effective(u, u.PlayerActor, new ActorId(3)):0.00}; Fahndung {u.Actors[u.PlayerActor].Wanted.GetValueOrDefault(new FactionId(3))?.Heat:0.0}"); }
    foreach (var c in crimes.Where(x => !x.Contains("FleetAttackStarted")).Take(20)) Console.WriteLine(c);
    return;
}
if (args.Contains("--yardprobe"))
{
    var me2 = u.Ships[u.PlayerShip];
    foreach (var yard in u.StationsById().Where(x => x.TypeKey == "shipyard"))
        foreach (var g in u.Goods.Values.Where(g => g.Category == "module" && new[] { "reactor_m", "shield_m", "weapon_laser_m", "fusion_drive_l", "weapon_massdriver_s", "armor_plating_m" }.Contains(g.Key)))
            Console.WriteLine($"  Regal {yard.Name} {g.Key}: {(yard.Market.TryGetValue(g.Id, out var me3) ? $"Bestand {me3.Amount:0.0} Ziel {me3.Target:0.0} akzeptiert {yard.AcceptableUnits(g.Id, u.Rules):0.0} Preis {me3.Price:0.0}" : "KEIN Marktplatz")}");
    foreach (var yard in u.StationsById().Where(x => x.TypeKey == "shipyard"))
        foreach (var key in new[] { "battlecruiser", "destroyer", "bastion", "carrier", "gunboat" })
            if (SpaceSim.Core.Services.ShipDealerService.Quote(u, yard, me2, key) is { } q)
                Console.WriteLine($"{yard.Name}: {key} Preis {q.Price:0} {(q.Buyable ? "LIEFERBAR" : "fehlt: " + string.Join(", ", q.Missing.Select(m => u.Goods[m.Good].Name + " " + m.Units.ToString("0.#"))))}");
    return;
}
if (args.Contains("--warprobe"))
{
    foreach (var f in u.NpcFactionIds())
        Console.WriteLine($"Fraktion {f.Value}: Beziehung zum Spieler {SpaceSim.Core.Simulation.Relations.Effective(u, new ActorId(f.Value), u.PlayerActor):0.00}/{SpaceSim.Core.Simulation.Relations.Effective(u, u.PlayerActor, new ActorId(f.Value)):0.00}; Schwelle {SpaceSim.Core.Simulation.Wars.DeclareThreshold(u, f):0.00}; DeclareIssue(KI→Spieler) = {SpaceSim.Core.Simulation.Wars.DeclareIssue(u, f, u.PlayerFaction!.Value) ?? "ja"}; Stärke KI {SpaceSim.Core.Simulation.Wars.Strength(u, f):0.0} Spieler {SpaceSim.Core.Simulation.Wars.Strength(u, u.PlayerFaction!.Value):0.0}; Kasse spendable {SpaceSim.Core.Simulation.FactionViability.Spendable(u, u.Actors[new ActorId(f.Value)]):0}");
    foreach (var f in u.NpcFactionIds()) SpaceSim.Core.Simulation.Relations.Set(u, new ActorId(f.Value), u.PlayerActor, -1f);
    foreach (var f in u.NpcFactionIds()) Console.WriteLine($"[Probe, Beziehung künstlich -1,0 nur zur Diagnose] Fraktion {f.Value}: {SpaceSim.Core.Simulation.Wars.DeclareIssue(u, f, u.PlayerFaction!.Value) ?? "würde erklären"}");
    Console.WriteLine($"WarChest {u.Rules.WarChest} MinStrengthRatio {u.Rules.WarMinStrengthRatio}");
    return;
}
if (args.Contains("--probe"))
{
    new SimulationRunner().Run(u, 400);
    var me = u.Ships[u.PlayerShip];
    Console.WriteLine($"Schiff {me.ModelKey} Sektor {u.Sectors[me.Sector].Name} Station {me.DockedAt.Value} Hülle {me.Hull}/{me.MaxHull} Cargo {me.Stats.Cargo} Speed {me.Stats.Speed} WP {me.Stats.WeaponPower} Shield {me.Stats.Shield} Fuel {me.Stats.FuelCapacity} Kasse {u.PlayerWallet.Credits}");
    foreach (var s in u.Ships.Values.Where(s => s.Role == ShipRole.Pirate)) Console.WriteLine($"Pirat #{s.Id.Value} {s.ModelKey} {u.Sectors[s.Sector].Name} owner {s.Owner.Value} hull {s.Hull}");
    foreach (var o in u.MissionOffers.OrderBy(o => o.Id)) Console.WriteLine($"Offer {o.Id} {o.Type} at {u.Stations[o.At].Name} reward {o.Reward}");
    foreach (var f in u.NpcFactionIds()) Console.WriteLine($"Fraktion {f.Value} {u.Actors[new ActorId(f.Value)].Name} rel {SpaceSim.Core.Simulation.Relations.Effective(u, u.PlayerActor, new ActorId(f.Value)):0.00}");
    return;
}
if (args.Contains("--catalog")) { Catalog.Dump(u); return; }
if (args.Contains("--shiptable")) { Catalog.ShipTable(u); return; }
var emerg = new Dictionary<string, (int n, float units, float cost)>();
u.Events.Raised += e => { if (e is SpaceSim.Core.Events.EmergencyRefuel er && u.Ships.TryGetValue(er.Ship, out var es) && es.Owner == u.PlayerActor) { var k = $"{es.ModelKey} {u.Goods[er.Good].Name} cmd={es.Command?.GetType().Name} in {u.Sectors[es.Sector].Name}"; var o = emerg.GetValueOrDefault(k); emerg[k] = (o.n + 1, o.units + er.Units, o.cost + er.Cost); } };
// Welle D: Treibstoffkäufe der Spielerflotte nach Modell, Befehl, Sorte (Messung der Flotten-Treibstoffkosten)
var fuelBuys = new Dictionary<string, (int n, float units, float cost)>();
var fuelHour = new Dictionary<long, (float cost, float units, float emerg)>();
u.Events.Raised += e =>
{
    if (e is SpaceSim.Core.Events.FuelBought fb && u.Ships.TryGetValue(fb.Ship, out var fs) && fs.Owner == u.PlayerActor)
    { var k = $"{fs.ModelKey} {u.Goods[fb.Good].Name} cmd={fs.Command?.GetType().Name} role={fs.Role}"; var o = fuelBuys.GetValueOrDefault(k); fuelBuys[k] = (o.n + 1, o.units + fb.Units, o.cost + fb.Cost); var h = u.Tick / 14400; var oh = fuelHour.GetValueOrDefault(h); fuelHour[h] = (oh.cost + fb.Cost, oh.units + fb.Units, oh.emerg); }
    if (e is SpaceSim.Core.Events.EmergencyRefuel er2 && u.Ships.TryGetValue(er2.Ship, out var es2) && es2.Owner == u.PlayerActor)
    { var h = u.Tick / 14400; var oh = fuelHour.GetValueOrDefault(h); fuelHour[h] = (oh.cost, oh.units, oh.emerg + er2.Cost); }
};
var log = new Log(Path.Combine(outDir, "ereignisse.log"), () => u.Tick);
if (args.Contains("--evlog")) { var evNames = new HashSet<string> { "StationFell", "StationDeparted", "WarDeclared", "PeaceConcluded", "SectorOwnerChanged", "PlayerGovernmentUnpaidChanged", "AllianceFormed", "TributePaid", "SupplyPanicRaised", "SectorControlChanged", "SectorAccessChanged", "StationCapitulationRolled", "CapitalOrderPlaced", "CapitalShipCompleted", "CapitalOrderEnded" }; u.Events.Raised += e => { if (evNames.Contains(e.GetType().Name)) log.Event("KERN " + e.GetType().Name + " " + e); }; }
var pilot = new Pilot(u, log, seed);
var metrics = new Metrics(u, pilot, Path.Combine(outDir, "zeitleiste.csv"), Path.Combine(outDir, "zeitleiste.txt"), every);
var levOut = new StreamWriter(Path.Combine(outDir, "leviathane.csv")) { AutoFlush = true }; levOut.WriteLine("tick;eigene_leviathane;npc_leviathane_je_fraktion;eigene_grosse;krieg_paare;sektoren_eigen;sperren");
long nextLev = 0;
pilot.OnTick = uu => { metrics.OnTick(uu); if (u.Tick >= nextLev) { nextLev = u.Tick + 10000; var all = u.Ships.Values.Where(s => s.ModelKey == "battlecruiser" || s.ModelKey == "carrier"); levOut.WriteLine($"{u.Tick};{all.Count(s => s.Owner == u.PlayerActor && s.ModelKey == "battlecruiser")};{string.Join(" ", all.Where(s => s.Owner != u.PlayerActor && s.ModelKey == "battlecruiser").GroupBy(s => s.Owner.Value).Select(g => g.Key + ":" + g.Count()))};{all.Count(s => s.Owner == u.PlayerActor)};{string.Join(" ", u.Wars.Select(w => w.A.Value + "-" + w.B.Value))};{u.Sectors.Values.Count(x => x.Owner == u.PlayerFaction)};{u.Sectors.Values.Count(x => x.Access.EntryLevel > 0)}"); } };
var bot = new Bot(pilot, log, metrics);
pilot.Runner.Run(u, 200);   // Preise stehen erst nach dem ersten Takt
pilot.Led = new Ledger(u);
var sw = Stopwatch.StartNew();
var wd = new Thread(() =>
{
    long last = -1; int same = 0;
    while (true)
    {
        Thread.Sleep(20000);
        long t = u.Tick;
        if (t == last) { if (++same >= 2) log.Event($"WACHHUND: Tick steht seit {same * 20} s bei {t}, Ort: {pilot.Where}"); } else same = 0;
        last = t;
    }
}) { IsBackground = true };
wd.Start();
long lastTick = -1; int stuck = 0;
while (u.Tick < ticks && !metrics.AllGoals)
{
    if (saveAt >= 0 && u.Tick >= saveAt && saveFile is not null) { File.WriteAllText(saveFile, SaveGameJson.Write(u, DateTimeOffset.UtcNow)); saveAt = -1; log.Event("Spielstand gespeichert " + saveFile); }
    try { bot.Step(); }
    catch (Exception ex) { log.Event("AUSNAHME: " + ex); metrics.Exceptions++; pilot.Run(100); }
    if (u.Tick == lastTick) { if (++stuck > 20) { pilot.Run(50); stuck = 0; } } else { stuck = 0; lastTick = u.Tick; }
    metrics.Phase = bot.Phase;
}
metrics.Final(sw.Elapsed.TotalMilliseconds);
var summary = metrics.Summary(sw.Elapsed.TotalMilliseconds);
Console.WriteLine(summary);
if (pilot.Led is { } led2)
{
    double hrs = u.Tick / 4.0 / 3600.0;
    var sb = new System.Text.StringBuilder();
    sb.AppendLine(led2.Check()); sb.AppendLine(led2.Report(led2.Sources, hrs)); sb.AppendLine("Takte mit unerklärtem Rest nach Ereignisart:"); sb.AppendLine(led2.ResidReport());
    File.WriteAllText(Path.Combine(outDir, "kasse.txt"), sb.ToString());
    Console.WriteLine(sb.ToString());
}
{
    var sbT = new System.Text.StringBuilder("Baufenster-Typen am Ende des Laufs (Grund aus PlayerBuilding.TypeIssue für das aktive Schiff):\n");
    foreach (var t in PlayerBuilding.Catalog(u)) sbT.AppendLine($"  {t.Key}: {(PlayerBuilding.TypeIssue(u, t) ?? "baubar")} | eigene {PlayerBuilding.OwnEconomic(u).Count(s => s.TypeKey == t.Key)}");
    sbT.AppendLine($"Eigene Wirtschaftsstationen {PlayerBuilding.OwnEconomic(u).Count} von {u.Rules.PlayerMaxStations}; Sektoren: " + string.Join(", ", PlayerBuilding.OwnEconomic(u).GroupBy(s => u.Sectors[s.Sector].Name).Select(g => $"{g.Key} {g.Count()}")));
    File.WriteAllText(Path.Combine(outDir, "baufenster.txt"), sbT.ToString());
}
File.WriteAllText(Path.Combine(outDir, "stationen.csv"), bot.Emp.Book.Report());
File.WriteAllText(Path.Combine(outDir, "empire.txt"), string.Join("\n", bot.Emp.Notes) + "\n--- Gericht ---\n" + string.Join("\n", bot.Emp.CourtLog) + "\n--- Krieg ---\n" + string.Join("\n", bot.Emp.WarLog) + "\n--- Nachschub (S5) ---\n" + string.Join("\n", bot.Emp.SupNotes) + "\n--- Bau ---\n" + string.Join("\n", bot.Emp.BuildLog) + $"\nKopfgeld abgeholt {bot.Emp.BountyClaimed:0} in {bot.Emp.BountyClaims} Abholungen\n");
File.WriteAllText(Path.Combine(outDir, "zusammenfassung.txt"), summary);
File.WriteAllText(Path.Combine(outDir, "treibstoff.txt"), string.Join("\n", fuelBuys.OrderByDescending(k => k.Value.cost).Select(k => $"{k.Key}: {k.Value.n}x {k.Value.units:0} E {k.Value.cost:0} Cr (Ø {k.Value.cost / Math.Max(1, k.Value.units):0.0} Cr/E)")) + "\n--- je Stunde ---\n" + string.Join("\n", fuelHour.OrderBy(k => k.Key).Select(k => $"Stunde {k.Key}: Treibstoff {k.Value.cost:0} Cr ({k.Value.units:0} E, Ø {k.Value.cost / Math.Max(1, k.Value.units):0.0}) Notbetankung {k.Value.emerg:0} Cr")));
File.WriteAllText(Path.Combine(outDir, "notbetankung.txt"), string.Join("\n", emerg.OrderByDescending(k => k.Value.cost).Select(k => $"{k.Key}: {k.Value.n}x {k.Value.units:0} E {k.Value.cost:0} Cr")));
