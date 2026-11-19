using SpaceSim.Core.Combat;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Model
{
    public static partial class CommandKinds
    {
        /// <summary>Ausbau Punkt 16: Erkunden auf Sektorebene (Muster von Punkten abfliegen).</summary>
        public const string ExploreSector = "explore";
        /// <summary>Ausbau Punkt 16: Erkunden auf Universumsebene (durch Tore in noch nicht besuchte Sektoren).</summary>
        public const string ExploreUniverse = "explore_far";
    }

    /// <summary>Erkunden (Sektor): fliegt ein festes Punktmuster im Sektor ab und deckt Stationen, Felder und Tore auf; endet, wenn nichts mehr unbekannt ist.</summary>
    public sealed record ExploreSectorCommand(SectorId Sector) : ShipCommand
    {
        public override string Kind => CommandKinds.ExploreSector;
    }

    /// <summary>Erkunden (Universum): fliegt nacheinander in den nächsten bekannten, noch nicht besuchten Sektor; endet, wenn keiner mehr erreichbar ist.</summary>
    public sealed record ExploreUniverseCommand : ShipCommand
    {
        public override string Kind => CommandKinds.ExploreUniverse;
    }
}

namespace SpaceSim.Core.Intel
{
    /// <summary>Ausbau Punkt 16: Punktmuster und Zielwahl des Erkundens (rein, deterministisch).</summary>
    public static class ExploreRules
    {
        /// <summary>Punktmuster eines Sektors: Mitte, Ring 0,45 × Ausdehnung (6 Richtungen), Ring 0,85 × Ausdehnung (10 Richtungen); aus Hindernissen geschoben.</summary>
        public static List<Vec2> Pattern(Universe u, SectorId s)
        {
            var list = new List<Vec2>();
            if (!u.Sectors.TryGetValue(s, out var sec)) return list;
            void Add(Vec2 p) => list.Add(CombatSpace.Clear(u, s, p));
            Add(new Vec2(0f, 0f));
            for (int k = 0; k < 6; k++) Add(Vec2.FromAngle(k * MathF.Tau / 6f) * (sec.Extent * 0.45f));
            for (int k = 0; k < 10; k++) Add(Vec2.FromAngle(0.31f + k * MathF.Tau / 10f) * (sec.Extent * 0.85f));
            return list;
        }

        /// <summary>Liegt in Sensorreichweite um <paramref name="p"/> etwas Unbekanntes?</summary>
        public static bool Worth(Universe u, SectorId s, Vec2 p)
        {
            if (Knowledge.Of(u) is not { } i || !u.Sectors.TryGetValue(s, out var sec)) return false;
            float r = u.Rules.IntelSensorRange;
            foreach (var id in sec.Stations)
                if (!Knowledge.KnowsStation(u, id) && u.Stations.TryGetValue(id, out var st) && Vec2.Distance(p, st.Position) - st.Radius <= r) return true;
            for (int k = 0; k < sec.Fields.Count; k++) if (!i.Fields.Contains((s.Value, k)) && Vec2.Distance(p, sec.Fields[k].Position) <= r) return true;
            for (int k = 0; k < sec.GasFields.Count; k++) if (!i.GasFields.Contains((s.Value, k)) && Vec2.Distance(p, sec.GasFields[k].Position) - sec.GasFields[k].Radius <= r) return true;
            return false;
        }

        /// <summary>Punkt nahe dem nächsten Unbekannten im Sektor (Station: vor ihrem Rand, Feld: Mitte), null = nichts.</summary>
        public static Vec2? NearestUnknown(Universe u, SectorId s, Vec2 from)
        {
            if (Knowledge.Of(u) is not { } i || !u.Sectors.TryGetValue(s, out var sec)) return null;
            Vec2? best = null; float bd = float.MaxValue;
            void Try(Vec2 p) { float d = Vec2.Distance(from, p); if (d < bd) { bd = d; best = p; } }
            foreach (var id in sec.Stations)
                if (!Knowledge.KnowsStation(u, id) && u.Stations.TryGetValue(id, out var st)) Try(st.Position);
            for (int k = 0; k < sec.Fields.Count; k++) if (!i.Fields.Contains((s.Value, k))) Try(sec.Fields[k].Position);
            for (int k = 0; k < sec.GasFields.Count; k++) if (!i.GasFields.Contains((s.Value, k))) Try(sec.GasFields[k].Position);
            if (best is not { } b) return null;
            var dir = b - from;
            float len = MathF.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
            var p = len <= u.Rules.IntelSensorRange * 0.5f ? from : b - dir * (u.Rules.IntelSensorRange * 0.5f / len);
            return CombatSpace.Clear(u, s, p);
        }

        /// <summary>Nächster bekannter, noch nicht besuchter Sektor, den das Schiff erreicht (wenigste Sprünge, dann Id); null = keiner.</summary>
        public static SectorId? NextSector(Universe u, Ship ship)
        {
            if (Knowledge.Of(u) is not { } i) return null;
            var from = ThreatMap.Where(u, ship).Sector;
            var profile = GateAccess.ProfileOf(u, ship);
            SectorId? best = null; int bestHops = int.MaxValue;
            foreach (var (s, k) in i.Sectors.OrderBy(kv => kv.Key.Value))
            {
                if (k != SectorKnowledge.Seen || s == from) continue;
                var r = u.CachedRoute(from, s, profile);
                if (r is null || r.Count >= bestHops) continue;
                best = s; bestHops = r.Count;
            }
            return best;
        }

        /// <summary>Ankunftspunkt in einem Sektor: das Tor, durch das man hereinkommt (sonst die Mitte), etwas nach innen.</summary>
        public static Vec2 Arrival(Universe u, SectorId from, SectorId to)
        {
            var route = u.FindRoute(from, to);
            if (route is { Count: > 0 } && u.Gates.TryGetValue(route[^1], out var g) && u.Gates.TryGetValue(g.Target, out var far))
                return CombatSpace.Clear(u, to, far.Position * 0.8f);
            return CombatSpace.Clear(u, to, new Vec2(0f, 0f));
        }
    }

    /// <summary>Ausbau Punkt 16: Ausführer „Erkunden: Sektor“.</summary>
    public sealed class ExploreSectorExecutor : IOrderExecutor
    {
        public string Kind => CommandKinds.ExploreSector;
        public Capability Needs => Capability.None;

        public string? Check(Universe u, Ship ship, ShipCommand command)
        {
            if (command is not ExploreSectorCommand c || !u.Sectors.ContainsKey(c.Sector)) return "Sektor unbekannt.";
            if (!ship.IsPlayer) return "Nur für die eigene Flotte.";
            if (!Knowledge.KnowsSector(u, c.Sector)) return "Sektor unbekannt.";
            if (Knowledge.Limited(u) && Knowledge.Visited(u, c.Sector) && Knowledge.UnknownIn(u, c.Sector) == 0) return "Sektor ist schon ganz bekannt.";
            return ship.Sector == c.Sector || OrderMove.Reachable(u, ship, c.Sector) ? null : "Sektor ist nicht erreichbar.";
        }

        public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

        public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
        {
            var c = (ExploreSectorCommand)command;
            var progress = OrderMove.StepTransit(u, ship);
            if (progress == OrderMove.Progress.Moving) return CommandStatus.Running;
            if (u.Tick < ship.NextThinkTick) return CommandStatus.Running;
            if (Knowledge.Limited(u) && Knowledge.Visited(u, c.Sector) && Knowledge.UnknownIn(u, c.Sector) == 0) return CommandStatus.Done;
            var pts = ExploreRules.Pattern(u, c.Sector);
            // nächster Musterpunkt, in dessen Sensorkreis noch etwas Unbekanntes liegt (bekannte Gegenden werden übersprungen)
            while (ship.CommandStep < pts.Count && Knowledge.Limited(u) && !(ship.CommandStep == 0 && !Knowledge.Visited(u, c.Sector))
                   && !ExploreRules.Worth(u, c.Sector, pts[ship.CommandStep])) ship.CommandStep++;
            if (ship.CommandStep >= pts.Count)
            {
                // Muster durch, aber noch etwas unbekannt (Ecke zwischen den Ringen): direkt zum nächsten Unbekannten, höchstens ein paar Versuche
                if (!Knowledge.Limited(u) || ship.CommandStep >= pts.Count + OrderMove.MaxRetries * 2 || ExploreRules.NearestUnknown(u, c.Sector, ship.Position) is not { } q) return CommandStatus.Done;
                if (!OrderMove.Go(u, ship, c.Sector, q)) ship.NextThinkTick = u.Tick + OrderMove.RetryTicks;
                ship.CommandStep++;
                return CommandStatus.Running;
            }
            var p = pts[ship.CommandStep];
            if (OrderMove.Near(ship, c.Sector, p, 5f)) { ship.CommandStep++; return CommandStatus.Running; }
            if (!OrderMove.Go(u, ship, c.Sector, p)) { ship.CommandStep++; ship.NextThinkTick = u.Tick + OrderMove.RetryTicks; }
            return CommandStatus.Running;
        }

        public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);

        public string Describe(Universe u, Ship ship, ShipCommand command)
        {
            var s = ((ExploreSectorCommand)command).Sector;
            int n = Knowledge.UnknownIn(u, s);
            return $"Erkundet {OrderMove.SectorName(u, s)}" + (Knowledge.Limited(u) ? (n > 0 ? $" ({n} unbekannt)" : " (fertig)") : "");
        }
    }

    /// <summary>Ausbau Punkt 16: Ausführer „Erkunden: Universum“.</summary>
    public sealed class ExploreUniverseExecutor : IOrderExecutor
    {
        public string Kind => CommandKinds.ExploreUniverse;
        public Capability Needs => Capability.None;
        /// <summary>Im Befehlsraster steht nur eine Taste „Erkunden“; die Universumsebene ist ihr erstes Ziel (<see cref="FleetTargets.ExploreSector"/>).</summary>
        public bool ForPlayer => false;

        public string? Check(Universe u, Ship ship, ShipCommand command)
        {
            if (command is not ExploreUniverseCommand) return "Befehl unbekannt.";
            if (!ship.IsPlayer) return "Nur für die eigene Flotte.";
            if (!Knowledge.Limited(u)) return "Alles bekannt.";
            return ExploreRules.NextSector(u, ship) is null ? "Kein unerkundeter Sektor erreichbar." : null;
        }

        public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

        public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
        {
            var progress = OrderMove.StepTransit(u, ship);
            if (progress == OrderMove.Progress.Moving) return CommandStatus.Running;
            if (progress == OrderMove.Progress.Arrived) Knowledge.Publish(u, ship.Sector, Knowledge.Scan(u, ship.Sector, ship.Position), "sensor");
            if (u.Tick < ship.NextThinkTick) return CommandStatus.Running;
            if (ExploreRules.NextSector(u, ship) is not { } next) return CommandStatus.Done;
            if (OrderMove.Go(u, ship, next, ExploreRules.Arrival(u, ship.Sector, next))) { ship.CommandStep = 0; return CommandStatus.Running; }
            ship.NextThinkTick = u.Tick + OrderMove.RetryTicks;
            return ++ship.CommandStep > OrderMove.MaxRetries ? CommandStatus.Failed : CommandStatus.Running;
        }

        public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);

        public string Describe(Universe u, Ship ship, ShipCommand command)
        {
            int unvisited = Knowledge.Of(u) is { } i ? u.Sectors.Keys.Count(s => i.StateOf(s) < SectorKnowledge.Visited) : 0;
            return ship.Transit is { } t ? $"Erkundet das Universum → {OrderMove.SectorName(u, t.ToSector)}" : $"Erkundet das Universum ({unvisited} Sektoren unbesucht)";
        }
    }
}
