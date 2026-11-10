using System.Globalization;
using System.Text;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
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

var header = new List<string> { "tick", "stations", "traders", "miners", "stationCredits", "shipCredits" };
header.AddRange(u.Goods.Values.Select(g => "price_" + g.Name));
csv.AppendLine(string.Join(",", header));
Console.WriteLine($"{"tick",7} {"stat",4} {"trad",4} {"mine",4} {"stCredits",10} {"shCredits",10}  " + string.Join(" ", u.Goods.Values.Select(g => (g.Name.Length > 5 ? g.Name[..5] : g.Name).PadLeft(5))) + "   (Preis/Basis)");

var sw = System.Diagnostics.Stopwatch.StartNew();
var history = new List<EconomySnapshot>();
for (int t = 0; t <= ticks; t += every)
{
    if (t > 0) runner.Run(u, every);
    var snap = EconomyStats.Snapshot(u);
    history.Add(snap);
    Console.WriteLine($"{snap.Tick,7} {snap.Stations,4} {snap.Traders,4} {snap.Miners,4} {snap.StationCredits,10:0} {snap.ShipCredits,10:0}  " +
                      string.Join(" ", snap.Goods.Select(g => g.PriceRatio.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5))));
    var row = new List<string> { snap.Tick.ToString(), snap.Stations.ToString(), snap.Traders.ToString(), snap.Miners.ToString(),
        snap.StationCredits.ToString("0", CultureInfo.InvariantCulture), snap.ShipCredits.ToString("0", CultureInfo.InvariantCulture) };
    row.AddRange(snap.Goods.Select(g => g.AvgPrice.ToString("0.00", CultureInfo.InvariantCulture)));
    csv.AppendLine(string.Join(",", row));
}
sw.Stop();

Console.WriteLine();
Console.WriteLine($"{ticks} Ticks in {sw.ElapsedMilliseconds} ms ({ticks / Math.Max(1.0, sw.ElapsedMilliseconds):0.0} Ticks/ms), Hash {UniverseHash.Compute(u):X16}");
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
