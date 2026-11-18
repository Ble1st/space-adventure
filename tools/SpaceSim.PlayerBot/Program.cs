using System.Diagnostics;
using SpaceSim.Core.Content;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.PlayerBot;

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
    }
Directory.CreateDirectory(outDir);
var def = WorldBuilder.Parse(File.ReadAllText(world));
var u = WorldBuilder.Build(def, seed, start);
var log = new Log(Path.Combine(outDir, "ereignisse.log"), () => u.Tick);
var pilot = new Pilot(u, log, seed);
var metrics = new Metrics(u, pilot, Path.Combine(outDir, "zeitleiste.csv"), Path.Combine(outDir, "zeitleiste.txt"), every);
pilot.OnTick = metrics.OnTick;
var bot = new Bot(pilot, log, metrics);
pilot.Runner.Run(u, 200);   // Preise stehen erst nach dem ersten Takt
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
    try { bot.Step(); }
    catch (Exception ex) { log.Event("AUSNAHME: " + ex); metrics.Exceptions++; pilot.Run(100); }
    if (u.Tick == lastTick) { if (++stuck > 20) { pilot.Run(50); stuck = 0; } } else { stuck = 0; lastTick = u.Tick; }
    metrics.Phase = bot.Phase;
}
metrics.Final(sw.Elapsed.TotalMilliseconds);
var summary = metrics.Summary(sw.Elapsed.TotalMilliseconds);
Console.WriteLine(summary);
File.WriteAllText(Path.Combine(outDir, "zusammenfassung.txt"), summary);
