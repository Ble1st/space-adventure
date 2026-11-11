using System.Globalization;
using System.Text;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

// Headless-Balancing: lässt die Wirtschaft ohne Spieler laufen und gibt Kennzahlen als Tabelle/CSV aus.
//   dotnet run --project tools/SpaceSim.Balance -- [--ticks 40000] [--every 2000] [--seed N] [--world datei.json] [--csv out.csv]

var opts = ParseArgs(args);
int ticks = Int(opts, "ticks", 40_000), every = Int(opts, "every", 2_000);
ulong? seed = opts.TryGetValue("seed", out var s) ? ulong.Parse(s, CultureInfo.InvariantCulture) : null;

var u = opts.TryGetValue("world", out var path) ? WorldBuilder.Build(WorldBuilder.Parse(File.ReadAllText(path)), seed) : WorldBuilder.BuildDefault(seed);
var runner = new SimulationRunner();
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

var header = new List<string> { "tick", "stations", "sites", "traders", "miners", "stationCredits", "shipCredits", "playerCredits", "treasuryCredits", "tank", "notk" };
header.AddRange(u.Goods.Values.Select(g => "price_" + g.Name));
csv.AppendLine(string.Join(",", header));
Console.WriteLine($"{"tick",7} {"stat",4} {"bau",3} {"trad",4} {"mine",4} {"stCredits",10} {"shCredits",10} {"plCredits",9} {"kassen",8} {"tank",5} {"notk",5}  " + string.Join(" ", u.Goods.Values.Select(g => (g.Name.Length > 5 ? g.Name[..5] : g.Name).PadLeft(5))) + "   (Preis/Basis)");

var sw = System.Diagnostics.Stopwatch.StartNew();
var history = new List<EconomySnapshot>();
for (int t = 0; t <= ticks; t += every)
{
    if (t > 0) runner.Run(u, every);
    var snap = EconomyStats.Snapshot(u);
    history.Add(snap);
    Console.WriteLine($"{snap.Tick,7} {snap.Stations,4} {snap.Sites,3} {snap.Traders,4} {snap.Miners,4} {snap.StationCredits,10:0} {snap.ShipCredits,10:0} {snap.PlayerCredits,9:0} {snap.TreasuryCredits,8:0} {fuelBuys,5} {notk,5}  " +
                      string.Join(" ", snap.Goods.Select(g => g.PriceRatio.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5))));
    var row = new List<string> { snap.Tick.ToString(), snap.Stations.ToString(), snap.Sites.ToString(), snap.Traders.ToString(), snap.Miners.ToString(),
        snap.StationCredits.ToString("0", CultureInfo.InvariantCulture), snap.ShipCredits.ToString("0", CultureInfo.InvariantCulture),
        snap.PlayerCredits.ToString("0", CultureInfo.InvariantCulture), snap.TreasuryCredits.ToString("0", CultureInfo.InvariantCulture), fuelBuys.ToString(), notk.ToString() };
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

if (opts.TryGetValue("csv", out var csvPath)) { File.WriteAllText(csvPath, csv.ToString()); Console.WriteLine("CSV: " + csvPath); }
return bad ? 1 : 0;

static Dictionary<string, string> ParseArgs(string[] a)
{
    var d = new Dictionary<string, string>();
    for (int i = 0; i + 1 < a.Length; i += 2) d[a[i].TrimStart('-')] = a[i + 1];
    return d;
}
static int Int(Dictionary<string, string> d, string k, int def) => d.TryGetValue(k, out var v) ? int.Parse(v) : def;
