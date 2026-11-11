using System.Globalization;
using System.Text;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

// Headless-Balancing: lässt die Wirtschaft ohne Spieler laufen und gibt Kennzahlen als Tabelle/CSV aus.
//   dotnet run --project tools/SpaceSim.Balance -- [--ticks 40000] [--every 2000] [--seed N] [--world datei.json] [--csv out.csv] [--security off]

var opts = ParseArgs(args);
int ticks = Int(opts, "ticks", 40_000), every = Int(opts, "every", 2_000);
ulong? seed = opts.TryGetValue("seed", out var s) ? ulong.Parse(s, CultureInfo.InvariantCulture) : null;

var u = opts.TryGetValue("world", out var path) ? WorldBuilder.Build(WorldBuilder.Parse(File.ReadAllText(path)), seed) : WorldBuilder.BuildDefault(seed);
// Phase 11: --security off schaltet Polizei und Piraten ab (Vergleichslauf ohne Phase-11-Wirkung)
if (opts.TryGetValue("security", out var secOpt) && secOpt == "off") u.Rules.SecurityActive = false;
// Phase 11: Rechenzeit der Sicherheits-Systeme (Lagekarte, Piraten, Polizei, Fahndung) messen — das Modul wird in eine Zeitmessung gehüllt
var securityWatch = new System.Diagnostics.Stopwatch();
var runner = new SimulationRunner(SimulationRunner.DefaultModules().Select(m => m is SpaceSim.Core.Modules.SecurityModule ? new TimedModule(m, securityWatch) : m));
var routes = new RouteRecorder(u);
var csv = new StringBuilder();

// Phase 4b: Treibstoff-Kennzahlen zählen (Tankkäufe an Stationen, Notkäufe zum Aufpreis)
int fuelBuys = 0, notk = 0;
float fuelBuyCost = 0f, notkCost = 0f;
// Phase 5a: Modul-Waren — wie viel Händler an Lager (stocks) liefern
float moduleDelivered = 0f, moduleRevenue = 0f;
// Phase 5c: KI-Neubauten mit Modulen (volle Kaufregel) bzw. nur aus dem Bausatz
int buildsWithModules = 0, buildsKitOnly = 0;
// Phase 6: Aufgabenwechsel der Multirole-Schiffe und Modellwahl der KI-Flotte
int jobSwitches = 0;
var chosenModels = new SortedDictionary<string, int>(StringComparer.Ordinal);
// Phase 7: Güteklassen der Neubauten (gewürfelt/erreicht) und Upgrade-Versuche der NPCs
var gradeRolled = new int[4]; var gradeGot = new int[4];
int upgradeTries = 0, upgradeOk = 0; float upgradeCost = 0f;
// Phase 8: Bau über Baustellen — angelegt, fertig (davon Notbau), verfallen; Material nach Lieferweg; Notbau-Kosten
int sitesStarted = 0, sitesDone = 0, sitesEmergency = 0, sitesAbandoned = 0, directBuilds = 0, splits = 0;
var deliveredBy = new SortedDictionary<string, float>(StringComparer.Ordinal);
var deliveredGood = new SortedDictionary<string, float>(StringComparer.Ordinal);
float emergencyCost = 0f, emergencyWaived = 0f, emergencyUnits = 0f;
u.Events.Raised += e =>
{
    if (e is ConstructionStarted) sitesStarted++;
    else if (e is ConstructionCompleted cc) { sitesDone++; if (cc.Emergency) sitesEmergency++; }
    else if (e is ConstructionAbandoned) sitesAbandoned++;
    else if (e is EmergencyConstruction ec) { emergencyCost += ec.Cost; emergencyWaived += ec.Waived; emergencyUnits += ec.Units; }
    else if (e is ConstructionDelivered cd)
    {
        deliveredBy[cd.By] = deliveredBy.GetValueOrDefault(cd.By) + cd.Units;
        deliveredGood[u.Goods[cd.Good].Name] = deliveredGood.GetValueOrDefault(u.Goods[cd.Good].Name) + cd.Units;
    }
    else if (e is AsteroidSplit) splits++;
    else if (e is StationBuilt) directBuilds++;   // abzüglich der fertigen Baustellen = Direktbau
};
// Phase 9: Kassen und Zukauf — Abgaben in Kassen (und ohne Empfänger), Investitionen nach Quelle und Größenklasse, Auf-/Abstiege
double taxIn = 0, taxSunk = 0, treasuryInvested = 0;
var investBy = new SortedDictionary<string, int>(StringComparer.Ordinal);
int promoted = 0, removedActors = 0;
u.Events.Raised += e =>
{
    switch (e)
    {
        case TaxCollected tcol: taxIn += tcol.Amount; break;
        case TaxSunk ts: taxSunk += ts.Amount; break;
        case InvestorChosen ic:
            string key = $"{(ic.Purpose == "station" ? "Station" : "Schiff")} aus {ic.Source switch { InvestorSource.Treasury => "Kasse", InvestorSource.Station => "Stationskasse", _ => "Schiffskasse" }} ({ActorNames.Size(ic.Class)})";
            investBy[key] = investBy.GetValueOrDefault(key) + 1;
            if (ic.Source == InvestorSource.Treasury) treasuryInvested += ic.Amount;
            break;
        case ActorPromoted: promoted++; break;
        case ActorRemoved: removedActors++; break;
    }
};
// Phase 10: Politik — Sperränderungen, Zoll, Umwege/Abbrüche, Kassen-Unterhalt (Regierungsstationen, Sperren), Besitzerwechsel
int accessChanges = 0, rerouted = 0, blocked = 0, ownerChanges = 0, maxLevelSeen = 0;
double tolls = 0, govUpkeep = 0, govUnpaid = 0, lockUpkeep = 0;
u.Events.Raised += e =>
{
    switch (e)
    {
        case SectorAccessChanged sac: accessChanges++; maxLevelSeen = Math.Max(maxLevelSeen, sac.ToEntry); break;
        case TollPaid tp: tolls += tp.Amount; break;
        case ShipRerouted: rerouted++; break;
        case RouteBlocked: blocked++; break;
        case SectorOwnerChanged: ownerChanges++; break;
        case TreasuryUpkeep tu when tu.Purpose == "government": govUpkeep += tu.Amount; govUnpaid += tu.Unpaid; break;
        case TreasuryUpkeep tu when tu.Purpose == "lock": lockUpkeep += tu.Amount; break;
    }
};
// Phase 11: Polizei, Piraten, Fahndung — Überfälle, Verluste, Abfangen, Polizeikosten, Kasseneinnahmen
int raids = 0, raidsWon = 0, raidsOnPlayer = 0, victimsLost = 0, raidersLost = 0, intercepts = 0, interceptsWon = 0, raidersBuilt = 0, policeBought = 0, policeGone = 0, policeHelped = 0;
int crimes = 0, warnings = 0;
double lootValue = 0, fenced = 0, tradeValue = 0, policeCost = 0, policeUnpaid = 0, pirateCost = 0, fines = 0, bounties = 0;
var victimsByRole = new SortedDictionary<string, int>(StringComparer.Ordinal);
u.Events.Raised += e =>
{
    switch (e)
    {
        case RaidResolved rr:
            raids++; if (rr.AttackerWon) raidsWon++; if (rr.Player) raidsOnPlayer++; if (rr.VictimDestroyed) victimsLost++; if (rr.RaiderDestroyed) raidersLost++;
            if (rr.Police.Value != 0) policeHelped++;
            lootValue += rr.LootValue; fenced += rr.Fenced;
            break;
        case RaiderIntercepted ri: intercepts++; if (ri.PoliceWon) interceptsWon++; if (ri.RaiderDestroyed) raidersLost++; break;
        case RaiderBuilt: raidersBuilt++; break;
        case PoliceCommissioned: policeBought++; break;
        case PoliceDecommissioned: policeGone++; break;
        case TreasuryUpkeep { Purpose: "police" } pu: policeCost += pu.Amount; policeUnpaid += pu.Unpaid; break;
        case TreasuryUpkeep { Purpose: "pirates" } pc: pirateCost += pc.Amount; break;
        case TreasuryUpkeep { Purpose: "bounty" } bo: bounties += bo.Amount; break;
        case TreasuryIncome { Purpose: "fine" } fi: fines += fi.Amount; break;
        case CrimeReported: crimes++; break;
        case EncounterWarning: warnings++; break;
        case TradeCompleted tcv: tradeValue += tcv.Revenue; break;
        case MineDelivered md: tradeValue += md.Revenue; break;
    }
};
u.Events.Raised += e =>
{
    if (e is EmergencyRefuel er) { notk++; notkCost += er.Cost; }
    else if (e is FuelBought fb) { fuelBuys++; fuelBuyCost += fb.Cost; }
    else if (e is FleetShipBuilt fsb) { if (fsb.WithModules) buildsWithModules++; else buildsKitOnly++; }
    else if (e is JobChanged) jobSwitches++;
    else if (e is ShipQualityRolled sq) { gradeRolled[(int)sq.Rolled]++; gradeGot[(int)sq.Grade]++; }
    else if (e is UpgradeAttempted ua) { upgradeTries++; if (ua.Success) upgradeOk++; upgradeCost += ua.Price; }
    else if (e is FleetModelChosen fmc) chosenModels[fmc.ModelKey] = chosenModels.GetValueOrDefault(fmc.ModelKey) + 1;
    else if (e is TradeCompleted tc && u.Modules.ContainsKey(u.Goods[tc.Good].Key)) { moduleDelivered += tc.Amount; moduleRevenue += tc.Revenue; }
};

var header = new List<string> { "tick", "stations", "sites", "traders", "miners", "stationCredits", "shipCredits", "playerCredits", "treasuryCredits", "tank", "notk", "locks", "isolated" };
header.AddRange(u.Goods.Values.Select(g => "price_" + g.Name));
csv.AppendLine(string.Join(",", header));
Console.WriteLine($"{"tick",7} {"stat",4} {"bau",3} {"trad",4} {"mine",4} {"stCredits",10} {"shCredits",10} {"plCredits",9} {"kassen",8} {"tank",5} {"notk",5} {"sperr",5} {"iso",3}  " + string.Join(" ", u.Goods.Values.Select(g => (g.Name.Length > 5 ? g.Name[..5] : g.Name).PadLeft(5))) + "   (Preis/Basis)");

var sw = System.Diagnostics.Stopwatch.StartNew();
var history = new List<EconomySnapshot>();
MapSplit? worstSplit = null, finalSplit = null;
for (int t = 0; t <= ticks; t += every)
{
    if (t > 0) runner.Run(u, every);
    var snap = EconomyStats.Snapshot(u);
    history.Add(snap);
    // Phase 10: Sperren und Zerfall der Karte (physisch oder für die freien Händler = die Wirtschaft)
    int locks = u.Sectors.Values.Count(x => !x.Access.IsOpen);
    var phys = MapConnectivity.Physical(u);
    var econ = MapConnectivity.Economy(u);
    var split = phys.Isolated.Count >= econ.Isolated.Count ? phys : econ;
    if (split.IsSplit && (worstSplit is null || split.Isolated.Count > worstSplit.Isolated.Count)) worstSplit = split;
    if (t == ticks) finalSplit = split.IsSplit ? split : null;
    Console.WriteLine($"{snap.Tick,7} {snap.Stations,4} {snap.Sites,3} {snap.Traders,4} {snap.Miners,4} {snap.StationCredits,10:0} {snap.ShipCredits,10:0} {snap.PlayerCredits,9:0} {snap.TreasuryCredits,8:0} {fuelBuys,5} {notk,5} {locks,5} {split.Isolated.Count,3}  " +
                      string.Join(" ", snap.Goods.Select(g => g.PriceRatio.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5))));
    var row = new List<string> { snap.Tick.ToString(), snap.Stations.ToString(), snap.Sites.ToString(), snap.Traders.ToString(), snap.Miners.ToString(),
        snap.StationCredits.ToString("0", CultureInfo.InvariantCulture), snap.ShipCredits.ToString("0", CultureInfo.InvariantCulture),
        snap.PlayerCredits.ToString("0", CultureInfo.InvariantCulture), snap.TreasuryCredits.ToString("0", CultureInfo.InvariantCulture), fuelBuys.ToString(), notk.ToString(),
        locks.ToString(), split.Isolated.Count.ToString() };
    row.AddRange(snap.Goods.Select(g => g.AvgPrice.ToString("0.00", CultureInfo.InvariantCulture)));
    csv.AppendLine(string.Join(",", row));
}
sw.Stop();

Console.WriteLine();
Console.WriteLine($"{ticks} Ticks in {sw.ElapsedMilliseconds} ms ({ticks / Math.Max(1.0, sw.ElapsedMilliseconds):0.0} Ticks/ms), Hash {UniverseHash.Compute(u):X16}");
var prop = u.FindGood("propellant");
if (prop is not null)
{
    var tailProp = history.Skip(history.Count * 3 / 4).Select(h => h.Goods.First(x => x.Good == prop.Id).PriceRatio).ToList();
    float propAvg = tailProp.Count > 0 ? tailProp.Average() : 0f;
    Console.WriteLine($"Treibstoff (propellant): {fuelBuys} Tankkäufe an Stationen ({fuelBuyCost:0} Cr), {notk} Notkäufe ({notkCost:0} Cr), Ø Preis/Basis letzte 25%: {propAvg:0.00}");
}
if (u.Modules.Count > 0)
{
    var moduleGoods = u.Goods.Values.Where(g => u.Modules.ContainsKey(g.Key)).Select(g => g.Id).ToHashSet();
    float atFactories = u.Stations.Values.SelectMany(st => st.Market.Where(kv => moduleGoods.Contains(kv.Key) && !st.IsStockOnly(kv.Key))).Sum(kv => kv.Value.Amount);
    float inStocks = u.Stations.Values.SelectMany(st => st.Market.Where(kv => moduleGoods.Contains(kv.Key) && st.IsStockOnly(kv.Key))).Sum(kv => kv.Value.Amount);
    Console.WriteLine($"Modul-Waren: {moduleDelivered:0} Einheiten von Händlern geliefert ({moduleRevenue:0} Cr); Bestand in Fabriken {atFactories:0}, in Lagern ohne Rezept {inStocks:0}");
    Console.WriteLine($"KI-Neubauten: {buildsWithModules} mit Modulen aus dem Werftlager, {buildsKitOnly} nur aus dem Bausatz");
}
// Phase 6: Flotte nach Modell und Aufgabe, belegte Kapazität je Aufgabe, Aufgabenwechsel, Modellwahl, Engpass-Module
{
    var ai = u.Ships.Values.Where(x => x.IsEconomyAi).ToList();
    var byModel = ai.GroupBy(x => (x.ModelKey.Length > 0 ? x.ModelKey : "-") + "/" + x.Role).OrderBy(g => g.Key, StringComparer.Ordinal)
        .Select(g => $"{g.Key} {g.Count()}");
    Console.WriteLine("Flotte (Modell/Aufgabe): " + string.Join(", ", byModel));
    Console.WriteLine("Kapazität: " + string.Join(", ", ShipJobs.Default.Select(j => $"{j.Key} {ShipJobs.UsedUnits(u, j):0.0}/{j.Limit(u.Rules):0}"))
        + $"; Aufgabenwechsel: {jobSwitches}; Multirole-Schiffe: {ai.Count(x => ShipJobs.IsMultirole(u, x))}");
    Console.WriteLine("Modellwahl der KI: " + (chosenModels.Count == 0 ? "keine" : string.Join(", ", chosenModels.Select(kv => $"{kv.Key} {kv.Value}"))));
    var tailH = history.Skip(history.Count * 3 / 4).ToList();
    string Ratio(string key) => u.FindGood(key) is { } gg ? tailH.Average(h => h.Goods.First(x => x.Good == gg.Id).PriceRatio).ToString("0.00", CultureInfo.InvariantCulture) : "-";
    string Grades(int[] g) => $"{g[0]}/{g[1]}/{g[2]}/{g[3]}";
    var founders = u.Ships.Values.Where(x => x.IsEconomyAi).GroupBy(x => x.Quality).OrderBy(g => g.Key).Select(g => $"{ShipQualityNames.Name(g.Key)} {g.Count()}");
    Console.WriteLine($"Güteklassen Neubauten (Standard/Verbessert/Elite/Meisterwerk): gewürfelt {Grades(gradeRolled)}, erreicht {Grades(gradeGot)}; Flotte jetzt: {string.Join(", ", founders)}");
    Console.WriteLine($"Upgrades (NPC): {upgradeTries} Versuche, {upgradeOk} gelungen, {upgradeCost:0} Cr");
    Console.WriteLine($"Engpass-Module (Ø Preis/Basis letzte 25%): Reaktor {Ratio("reactor_mk1")}, Triebwerk {Ratio("chem_engine_s")}, Laser {Ratio("mining_laser_1")}");
}
// Phase 8: Bau über Baustellen und Notbau-Anteil
{
    int direct = directBuilds - sitesDone;
    float share = sitesDone > 0 ? 100f * sitesEmergency / sitesDone : 0f;
    Console.WriteLine($"Bau: {sitesStarted} Baustellen angelegt, {sitesDone} fertig (davon Notbau {sitesEmergency} = {share:0}%), {sitesAbandoned} verfallen, "
        + $"{u.Stations.Values.Count(x => x.Site is { Abandoned: false })} laufen, {direct} Direktbau; Bauschiffe {u.Ships.Values.Count(x => x.IsAiBuilder)}");
    Console.WriteLine("Baumaterial geliefert: " + (deliveredBy.Count == 0 ? "nichts" : string.Join(", ", deliveredBy.Select(kv => $"{kv.Key} {kv.Value:0}")))
        + (deliveredGood.Count == 0 ? "" : " | " + string.Join(", ", deliveredGood.Select(kv => $"{kv.Key} {kv.Value:0}")))
        + $"; Notbau {emergencyUnits:0} Einheiten für {emergencyCost:0} Cr (erlassen {emergencyWaived:0}); Spaltungen {splits}");
}
// Phase 9: Akteure, Kassen, Zukauf, Beziehungen
{
    var snap = history[^1];
    Console.WriteLine($"Geldmenge: Stationen {snap.StationCredits:0} + Schiffe {snap.ShipCredits:0} + Spieler {snap.PlayerCredits:0} + Kassen {snap.TreasuryCredits:0} = {snap.TotalCredits:0} Cr; "
        + $"Abgaben in Kassen {taxIn:0}, ohne Empfänger {taxSunk:0}, aus Kassen investiert {treasuryInvested:0}");
    var actors = snap.Actors ?? Array.Empty<ActorStat>();
    Console.WriteLine($"Akteure: {actors.Count} ({promoted} aufgestiegen, {removedActors} abgestiegen); Beziehungseinträge {Relations.StoredEntries(u)}; "
        + "Größenklassen " + string.Join(", ", actors.GroupBy(a => a.Class).OrderBy(g => g.Key).Select(g => $"{ActorNames.Size(g.Key)} {g.Count()}")));
    foreach (var a in actors.Where(a => a.Kind != ActorKind.Independent || a.Ships + a.Stations > 1).Take(12))
        Console.WriteLine($"  {a.Name,-26} {ActorNames.Kind(a.Kind),-20} {ActorNames.Size(a.Class),-12} Kasse {a.Treasury,9:0}  {a.Ships,3} Schiffe {a.Stations,3} Stationen  Größe {a.Size:0.0}");
    Console.WriteLine("Zukauf: " + (investBy.Count == 0 ? "keiner" : string.Join(", ", investBy.Select(kv => $"{kv.Key} {kv.Value}"))));
}
// Phase 10: Gate-Zugang, Sektorkontrolle, Beziehungen, Regierungsstationen
{
    Console.WriteLine($"Politik: {accessChanges} Sperränderungen (höchste Stufe {maxLevelSeen}), {u.Sectors.Values.Count(x => !x.Access.IsOpen)} Sektoren gesperrt; "
        + $"Zoll {tolls:0} Cr; Reisen umgeplant {rerouted}, angehalten/nicht angetreten {blocked}; Besitzerwechsel {ownerChanges}");
    Console.WriteLine($"Kassen-Unterhalt: Regierungsstationen {govUpkeep:0} Cr (ungedeckt {govUnpaid:0}), Sperren {lockUpkeep:0} Cr — "
        + $"{(taxIn > 0 ? 100.0 * (govUpkeep + lockUpkeep) / taxIn : 0):0}% der Abgaben in Kassen");
    foreach (var sec in u.Sectors.Values.OrderBy(x => x.Id.Value))
    {
        string inf = string.Join(" ", sec.Influence.OrderByDescending(kv => kv.Value).Select(kv => $"{(u.Factions.TryGetValue(kv.Key, out var f) ? f.Name : "?")} {kv.Value:P0}"));
        Console.WriteLine($"  {sec.Name,-14} {(u.Factions.TryGetValue(sec.Owner, out var of) ? of.Name : "?"),-20} {AccessNames.Stage(sec.Control),-10}{(sec.Contested ? " umkämpft" : "")}  "
            + $"Sperre {sec.Access.EntryLevel}/{sec.Access.ExitLevel}{(sec.Access.Enforced ? "" : " (Papier)")}  Einfluss {inf}");
    }
    var fs = u.Factions.Keys.OrderBy(k => k.Value).ToList();
    Console.WriteLine("Beziehungen: " + string.Join(", ", fs.SelectMany(a => fs.Where(b => b != a).Select(b => $"{u.Factions[a].Name}→{u.Factions[b].Name} {Relations.Effective(u, a, b):0.00}"))));
    foreach (var fsplit in MapConnectivity.Factions(u).Where(x => x.IsSplit))
        Console.WriteLine($"  Für {fsplit.Profile} gesperrt: {string.Join(", ", fsplit.Isolated.Select(id => u.Sectors[id].Name))} (Politik)");
}
// Phase 11: Lagekarte, Polizei, Piraten — Verlustquote, Polizeianteil am Haushalt, Rechenzeit der Scans
{
    double income = taxIn + tolls + fines;
    Console.WriteLine($"Sicherheit: {raids} Überfälle ({raidsWon} erfolgreich, {raids - raidsWon} abgewehrt, Polizei griff {policeHelped}× ein, {raidsOnPlayer} auf den Spieler), "
        + $"{warnings} Vorwarnungen; Abfangen {intercepts} ({interceptsWon} von der Polizei gewonnen); Kaperschiffe verloren {raidersLost}, neu {raidersBuilt}; Opferschiffe verloren {victimsLost}");
    Console.WriteLine($"Verlustquote: Beute {lootValue:0} Cr (Basiswert) = {(tradeValue > 0 ? 100.0 * lootValue / tradeValue : 0):0.00}% des Handelsumsatzes ({tradeValue:0} Cr); "
        + $"Hehlerei {fenced:0} Cr; Schiffsverluste {victimsLost}");
    Console.WriteLine($"Polizei: {u.Factions.Keys.OrderBy(k => k.Value).Select(f => $"{u.Factions[f].Name} {SecuritySystem.PoliceOf(u, f).Count}/{SecuritySystem.Desired(u, f)}").Aggregate((a, b) => a + ", " + b)} (Ist/Soll); "
        + $"gekauft {policeBought}, abgebaut {policeGone}; Kosten {policeCost:0} Cr (ungedeckt {policeUnpaid:0}) = {(income > 0 ? 100.0 * policeCost / income : 0):0.0}% der Kasseneinnahmen "
        + $"(Abgaben {taxIn:0} + Zoll {tolls:0} + Strafen {fines:0}); Kopfgeld ausgezahlt {bounties:0}");
    foreach (var a in u.Actors.Values.Where(a => a.Kind == ActorKind.Outlaw).OrderBy(a => a.Id.Value))
        Console.WriteLine($"  Piraten {a.Name}: {u.Ships.Values.Count(x => ThreatMap.IsRaider(x) && x.Owner == a.Id)} Kaperschiffe, Kasse {a.Treasury.Credits:0} Cr, Kosten {pirateCost:0} Cr; Fahndung "
            + string.Join(", ", a.Wanted.OrderBy(kv => kv.Key.Value).Select(kv => $"{u.Factions[kv.Key].Name} {SecurityNames.WantedLevel(Wanted.Level(u.Rules, kv.Value.Heat))}")));
    Console.WriteLine("Lagekarte: " + string.Join(", ", u.Sectors.Values.OrderBy(x => x.Id.Value)
        .Select(x => $"{x.Name} Bedrohung {x.Threat:0.00} Sicherheit {x.Security:0.0} Verkehr {x.Traffic:0}")) + $"; Verbrechen gemeldet {crimes}");
    Console.WriteLine($"Rechenzeit Sicherheit (Lagekarte, Piraten, Polizei, Fahndung): {securityWatch.Elapsed.TotalMilliseconds:0} ms = "
        + $"{(sw.ElapsedMilliseconds > 0 ? 100.0 * securityWatch.Elapsed.TotalMilliseconds / sw.ElapsedMilliseconds : 0):0.0}% der Laufzeit");
}
Console.WriteLine("Top-Handelsrouten:");
foreach (var r in routes.Top(5))
{
    string from = r.Route.From.Value < 0 ? $"Miner {-r.Route.From.Value}" : u.Stations.GetValueOrDefault(r.Route.From)?.Name ?? $"#{r.Route.From.Value}";
    string to = u.Stations.GetValueOrDefault(r.Route.To)?.Name ?? $"#{r.Route.To.Value}";
    Console.WriteLine($"  {u.Goods[r.Route.Good].Name,-9} {from} → {to}: {r.Volume:0} in {r.Trips} Fahrten");
}

// Urteil: Preise in den letzten 25 % der Messpunkte dauerhaft am Anschlag deuten auf eine kippende Kette hin
var tail = history.Skip(history.Count * 3 / 4).ToList();
bool bad = false;
foreach (var g in u.Goods.Values)
{
    // Waren ohne Markt (noch ohne Erzeuger, z. B. propellant bis Phase 4b) haben keinen Preis und keine Stabilität
    if (!u.Stations.Values.Any(st => st.Market.ContainsKey(g.Id))) continue;
    float avgRatio = tail.Average(h => h.Goods.First(x => x.Good == g.Id).PriceRatio);
    if (avgRatio > 3.0f || avgRatio < 0.3f) { Console.WriteLine($"WARNUNG: {g.Name} im Schnitt bei {avgRatio:0.00}× Basispreis (instabil)"); bad = true; }
}
if (!bad) Console.WriteLine("Urteil: Preise im Zielbereich.");
// Phase 10: zerfallende Karte (physisch oder für die freien Händler) ist ein eigener Befund mit eigenem Exit-Code
bool splitBad = worstSplit is not null;
if (worstSplit is not null)
    Console.WriteLine($"WARNUNG: Karte zerfallen ({worstSplit.Profile}): isolierte Sektoren {string.Join(", ", worstSplit.Isolated.Select(id => u.Sectors[id].Name))} "
        + $"(größter Block {worstSplit.Largest}/{worstSplit.Sectors}){(finalSplit is null ? ", am Ende wieder verbunden" : ", am Ende noch zerfallen")}");
else Console.WriteLine("Karte: zusammenhängend (physisch und für freie Händler, an allen Messpunkten).");

if (opts.TryGetValue("csv", out var csvPath)) { File.WriteAllText(csvPath, csv.ToString()); Console.WriteLine("CSV: " + csvPath); }
return bad ? 1 : splitBad ? 2 : 0;

static Dictionary<string, string> ParseArgs(string[] a)
{
    var d = new Dictionary<string, string>();
    for (int i = 0; i + 1 < a.Length; i += 2) d[a[i].TrimStart('-')] = a[i + 1];
    return d;
}
static int Int(Dictionary<string, string> d, string k, int def) => d.TryGetValue(k, out var v) ? int.Parse(v) : def;

/// <summary>Hüllt die Systeme eines Moduls in eine gemeinsame Zeitmessung (Phase 11: Rechenzeit der Scans).</summary>
sealed class TimedModule(ISimModule inner, System.Diagnostics.Stopwatch watch) : ISimModule
{
    public string Name => inner.Name;
    public void Register(SimulationBuilder b)
    {
        var tmp = new SimulationBuilder();
        inner.Register(tmp);
        foreach (var (system, interval) in tmp.Registered) b.Add(new Timed(system, watch), interval);
    }

    sealed class Timed(ISimSystem system, System.Diagnostics.Stopwatch watch) : ISimSystem
    {
        public void Step(Universe u, int dt) { watch.Start(); system.Step(u, dt); watch.Stop(); }
    }
}
