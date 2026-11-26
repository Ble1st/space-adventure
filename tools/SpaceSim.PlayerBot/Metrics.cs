using System.Text;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.PlayerBot;

/// <summary>Ziele und Zeitleiste.</summary>
public sealed class Metrics
{
    readonly Universe U; readonly Pilot P; readonly StreamWriter _csv, _txt; readonly long _every;
    public readonly Dictionary<string, long> Reached = new();
    public readonly Dictionary<string, string> How = new();
    public readonly Dictionary<string, int> Events = new();
    public readonly Dictionary<string, float> Costs = new();
    public string Phase = "";
    public int PlayerCrimes; public float EarnedSales, EarnedTrade; public int Purchases, Seats, Exceptions;
    public bool AllGoals => GoalNames.All(Reached.ContainsKey);
    public Empire? Emp;
    public static readonly string[] GoalNames = { "HQ eigen", "10 Sektoren", "Alle Sektoren", "Polizei", "Militär", "Supermacht", "Krieg", "Handelsnetz", "Gericht", "Geld" };

    public Metrics(Universe u, Pilot p, string csv, string txt, long every)
    {
        U = u; P = p; _every = every; _hourly = new StreamWriter(Path.Combine(Path.GetDirectoryName(csv) ?? ".", "kasse-stunden.txt")) { AutoFlush = true };
        _csv = new StreamWriter(csv) { AutoFlush = true }; _txt = new StreamWriter(txt) { AutoFlush = true };
        _csv.WriteLine("tick;stunden;kasse;schiffe;haendler;schuerfer;polizei;kampfschiffe;gross;stationen;sektoren;groessenklasse;kriege;verbrechen;verluste;exceptions;ms_tick");
        u.Events.Raised += e =>
        {
            string n = e.GetType().Name; Events[n] = Events.GetValueOrDefault(n) + 1;
            switch (e)
            {
                case SpaceSim.Core.Events.CrimeReported c when c.Actor == u.PlayerActor: PlayerCrimes++; P.Log.Event($"Verbrechen {c.Crime} bei Fraktion {c.Faction.Value}: Hitze {c.Heat:0.0} Stufe {c.Level} Ansehen −{c.StandingLoss:0.00}"); break;
                case SpaceSim.Core.Events.TreasuryUpkeep t when t.Actor == u.PlayerActor: Costs[t.Purpose] = Costs.GetValueOrDefault(t.Purpose) + t.Amount; break;
                case SpaceSim.Core.Events.FleetUpkeepPaid fu when fu.Actor == u.PlayerActor: Costs["flotte"] = Costs.GetValueOrDefault("flotte") + fu.Amount; break;
                case SpaceSim.Core.Events.BerthFeePaid bf when bf.Actor == u.PlayerActor: Costs["liegegebuehr"] = Costs.GetValueOrDefault("liegegebuehr") + bf.Amount; break;
                case SpaceSim.Core.Events.PlayerShipLost l: P.Log.Event($"SCHIFF VERLOREN #{l.Lost.Value}: {l.How} — {l.Message}"); break;
                case SpaceSim.Core.Events.WarDeclared w: P.Log.Event($"Krieg {w.A.Value}↔{w.B.Value}: {w.Message}"); break;
                case SpaceSim.Core.Events.ShipDeparted d when d.Owner == u.PlayerActor: P.Log.Event($"Schiff #{d.Ship.Value} ({d.Role}) abgegangen: {d.Reason} {d.Detail}"); break;
                case SpaceSim.Core.Simulation.StandingChanged sc: if (Math.Abs(sc.To - sc.From) >= 0.0049f) P.Log.Event($"Ansehen Fraktion {sc.Faction.Value}: {sc.From:0.00}→{sc.To:0.00} ({sc.Reason})"); break;
            }
        };
    }

    readonly StreamWriter _hourly;
    long _lastMs = 0; long _lastMsTick = 0; readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();

    public void OnTick(Universe u)
    {
        if (u.Tick % 200 == 0) CheckGoals();
        if (u.Tick % 20 == 0) Emp?.Book.Sample();
        if (u.Tick % 14400 == 0 && P.Led is { } led)
        {
            _hourly.WriteLine($"=== Stunde {u.Tick / 14400} (Tick {u.Tick}) Kasse {P.Wallet:0} ===");
            _hourly.Write(led.Report(led.Window, 1.0)); led.Window.Clear(); _hourly.Flush();
        }
        if (u.Tick % _every == 0) Line();
    }

    (int armedPatrol, int combat, int big, int traders, int miners) Counts()
    {
        var own = PlayerFleet.Owned(U);
        var ownSec = PlayerSeats.OwnSectors(U).Select(s => s.Id).ToHashSet();
        int patrol = 0;
        foreach (var s in own)
        {
            if (!s.Has(Capability.Combat) || s.Id == U.PlayerShip) continue;
            ShipCommand? c = s.Command;
            if (c is FollowCommand f && U.Ships.TryGetValue(f.Target, out var lead)) c = lead.Command;
            bool ok = (c is PatrolCommand pc && pc.Sectors.Any(ownSec.Contains)) || (c is GuardCommand gc && U.Stations.TryGetValue(gc.Station, out var gst) && ownSec.Contains(gst.Sector));
            if (ok) patrol++;
        }
        int combat = 0, big = 0, tr = 0, mi = 0;
        foreach (var s in own)
        {
            if (U.ShipModels.TryGetValue(s.ModelKey, out var m) && m.Jobs.Count == 0 && s.Has(Capability.Combat))
            {
                combat++;
                if (U.ShipClasses.TryGetValue(m.ClassKey, out var c) && c.Size >= SlotSize.L) big++;
            }
            if (s.Command is TradeCommand) tr++; else if (s.Command is MineCommand) mi++;
        }
        return (patrol, combat, big, tr, mi);
    }

    void Mark(string goal, string how) { if (!Reached.ContainsKey(goal)) { Reached[goal] = U.Tick; How[goal] = how; P.Log.Event($"ZIEL ERREICHT: {goal} — {how}"); } }

    void CheckGoals()
    {
        if (PlayerHq.Current(U) is { } hq) Mark("HQ eigen", $"{hq.TypeKey} „{hq.Name}“");
        int secs = PlayerSeats.OwnSectors(U).Count;
        if (secs >= 10) Mark("10 Sektoren", $"{secs} Sektoren mit Besitzer Spielerfraktion");
        if (secs >= U.Sectors.Count) Mark("Alle Sektoren", $"{secs} von {U.Sectors.Count} Sektoren");
        var (patrol, combat, big, _, _) = Counts();
        if (patrol >= 6) Mark("Polizei", $"{patrol} bewaffnete Eigenschiffe auf Streife/Wache");
        if (P.Wallet >= 9_999_999f) Mark("Geld", $"Kasse {P.Wallet:0}");
        if (combat >= 10 && big >= 3) Mark("Militär", $"{combat} Kampfschiffe, davon {big} L/XL");
        var cls = ActorRules.ClassOf(U, ActorRules.Index(U), U.PlayerActor);
        if (cls == ActorSizeClass.State) Mark("Supermacht", "Größenklasse Staat");
        if (Emp is { } e)
        {
            if (e.Mile.ContainsKey("Krieg erklärt") && (e.Mile.ContainsKey("Station erobert") || e.Mile.ContainsKey("Station zerstört"))) Mark("Krieg", string.Join(", ", e.Mile.Where(k => k.Key.StartsWith("Krieg") || k.Key.StartsWith("Station") || k.Key.StartsWith("Frieden")).Select(k => k.Key)));
            int econ = e.BuiltCount;
            if (econ >= 25) Mark("Handelsnetz", $"{econ} selbst gebaute Handelsstationen (fertig, je Typ höchstens 2)");
            if (e.Mile.ContainsKey("Gericht")) Mark("Gericht", "eigenes + fremdes Gericht getestet");
        }
    }

    void Line()
    {
        var (patrol, combat, big, tr, mi) = Counts();
        var own = PlayerFleet.Owned(U);
        var cls = ActorRules.ClassOf(U, ActorRules.Index(U), U.PlayerActor);
        int stations = U.Stations.Values.Count(s => s.Owner == U.PlayerActor);
        int secs = PlayerSeats.OwnSectors(U).Count;
        int wars = U.Wars.Count(w => U.PlayerFaction is { } f && w.Involves(f));
        int crimes = PlayerCrimes;
        int lost = Events.GetValueOrDefault("PlayerShipLost");
        long ms = _sw.ElapsedMilliseconds; double msTick = (double)(ms - _lastMs) / Math.Max(1, U.Tick - _lastMsTick); _lastMs = ms; _lastMsTick = U.Tick;
        _csv.WriteLine($"{U.Tick};{U.Tick / 4.0 / 3600:0.0};{P.Wallet:0};{own.Count};{tr};{mi};{patrol};{combat};{big};{stations};{secs};{cls};{wars};{crimes};{lost};{Exceptions};{msTick:0.000}");
        var rel = string.Join(" ", U.NpcFactionIds().Select(n => $"{n.Value}:{Relations.Effective(U, U.PlayerActor, new ActorId(n.Value)):0.00}"));
        _txt.WriteLine($"[{U.Tick,8}] {U.Tick / 4.0 / 3600,5:0.0} h | Kasse {P.Wallet,9:0} | Schiffe {own.Count,3} (H{tr} S{mi} Streife{patrol} Kampf{combat}/{big}) | Stationen {stations} Sektoren {secs} | {cls} | Kriege {wars} Weltstationen {U.Stations.Count} (NPC-Wirtschaft {U.Stations.Values.Count(x => x.Owner != U.PlayerActor && !x.IsBuildSite)}) Verbrechen {crimes} Verluste {lost} | Bez {rel} | {msTick:0.00} ms/Tick | {Phase} | {P.Where}");
    }

    public void Final(double ms) { CheckGoals(); Line(); }

    public string Summary(double ms)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Laufende Kosten (Summe über den Lauf): " + string.Join(", ", Costs.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value:0}")));
        sb.AppendLine($"Lauf: {U.Tick} Ticks ({U.Tick / 4.0 / 3600:0.0} Spielstunden) in {ms / 1000:0} s = {ms / Math.Max(1, U.Tick):0.000} ms/Tick; Kasse {P.Wallet:0}");
        foreach (var g in GoalNames) sb.AppendLine($"  {g}: {(Reached.TryGetValue(g, out var t) ? $"JA bei Tick {t} ({t / 4.0 / 3600:0.0} h) — {How[g]}" : "nein")}");
        return sb.ToString();
    }
}
