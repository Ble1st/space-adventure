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
    }
Directory.CreateDirectory(outDir);
var def = WorldBuilder.Parse(File.ReadAllText(world));
var u = loadFile is null ? WorldBuilder.Build(def, seed, start) : SaveGameJson.Read(File.ReadAllText(loadFile)).Universe;
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
if (args.Contains("--catalog")) { Catalog.Dump(u); return; }
if (args.Contains("--shiptable")) { Catalog.ShipTable(u); return; }
var log = new Log(Path.Combine(outDir, "ereignisse.log"), () => u.Tick);
var pilot = new Pilot(u, log, seed);
var metrics = new Metrics(u, pilot, Path.Combine(outDir, "zeitleiste.csv"), Path.Combine(outDir, "zeitleiste.txt"), every);
pilot.OnTick = metrics.OnTick;
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
File.WriteAllText(Path.Combine(outDir, "empire.txt"), string.Join("\n", bot.Emp.Notes) + "\n--- Gericht ---\n" + string.Join("\n", bot.Emp.CourtLog) + "\n--- Krieg ---\n" + string.Join("\n", bot.Emp.WarLog) + "\n--- Bau ---\n" + string.Join("\n", bot.Emp.BuildLog) + $"\nKopfgeld abgeholt {bot.Emp.BountyClaimed:0} in {bot.Emp.BountyClaims} Abholungen\n");
File.WriteAllText(Path.Combine(outDir, "zusammenfassung.txt"), summary);
