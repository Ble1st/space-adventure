using SpaceSim.Core.Combat;
using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Flight;

/// <summary>UI-Umbau: Art eines Eintrags der Scanliste („☰ Sektor“).</summary>
public enum ScanKind { Station, Ship, Gate, Rock, Wreck }

/// <summary>Ein Objekt der Scanliste: Art, Id (Station-/Schiffs-/Tor-/Asteroiden-/Wrack-Id), Name, Infozeile, Entfernung, feindlich, andockbar.</summary>
public sealed record ScanContact(ScanKind Kind, int Id, string Name, string Info, float Distance, bool Hostile, bool Dockable);

/// <summary>
/// UI-Umbau (Nutzerwunsch nach Interstellar Pilot 2: „Hamburgermenü unten links für Scans — was im Sektor sichtbar ist, mit Auswahl durch Antippen als
/// Ziel“): reine Liste der sichtbaren Objekte im Sektor des Spielers, nach Entfernung sortiert. Stationen nur, wenn bekannt (Erkundung); Felsen nur die
/// nächsten <see cref="MaxRocks"/> mit Erz in <see cref="RockRange"/>; das eigene aktive Schiff nicht.
/// </summary>
public static class SectorScan
{
    public const int MaxRocks = 8;
    public const float RockRange = 1500f;

    public static List<ScanContact> For(Universe u, ShipId me, Vec2 at)
    {
        var list = new List<ScanContact>();
        if (!u.Ships.TryGetValue(me, out var self) || !u.Sectors.TryGetValue(u.PlayerSector, out var sec)) return list;
        foreach (var st in u.StationsById())
        {
            if (st.Sector != sec.Id || !Knowledge.KnowsStation(u, st)) continue;
            string kind = st.Site is not null ? "Baustelle" : u.StationTypes.TryGetValue(st.TypeKey, out var ty) ? ty.Name : "Station";
            string owner = u.Actors.ContainsKey(st.Owner) ? u.ActorName(st.Owner) : "";
            bool hostile = st.Owner != u.PlayerActor && u.Actors.TryGetValue(st.Owner, out var oa) && oa.Kind == ActorKind.Outlaw;
            string info = kind + (owner.Length > 0 ? " · " + owner : "") + (st.Dockable ? " · andockbar" : "");
            list.Add(new ScanContact(ScanKind.Station, st.Id.Value, StationNames.Short(u, st), info, MathF.Max(0f, Vec2.Distance(at, st.Position) - st.Radius), hostile, st.Dockable));
        }
        foreach (var gid in sec.Gates)
        {
            if (!u.Gates.TryGetValue(gid, out var g) || !Knowledge.KnowsGate(u, gid)) continue;
            string to = u.Gates.TryGetValue(g.Target, out var t2) && u.Sectors.TryGetValue(t2.Sector, out var ts) ? ts.Name : "?";
            string state = g.State switch { GateState.Open => "offen", GateState.Restricted => "eingeschränkt", GateState.Closed => "geschlossen", _ => "zerstört" };
            list.Add(new ScanContact(ScanKind.Gate, gid.Value, "Tor → " + to, state + (g.MainRoute ? " · Hauptroute" : ""), Vec2.Distance(at, g.Position), false, false));
        }
        foreach (var s in u.ShipsById())
        {
            if (s.Id == me || s.DockedAt.Value != 0) continue;
            if (PlayerCombat.PositionOf(u, s.Id) is not { } p || p.Sector != sec.Id) continue;
            if (s.Cloaked && s.Owner != u.PlayerActor) continue;
            bool own = s.Owner == u.PlayerActor;
            bool hostile = !own && PlayerCombat.IsHostile(u, self, s.Id);
            string role = s.Role switch
            {
                ShipRole.Pirate => "Pirat", ShipRole.Police => "Polizei", ShipRole.Warship => "Kriegsschiff", ShipRole.Trader => "Händler", ShipRole.Miner => "Schürfer",
                ShipRole.Builder => "Bauschiff", ShipRole.Salvager => "Bergung", ShipRole.Collector => "Gassammler", ShipRole.Smuggler => "Frachter", _ => own ? "eigenes Schiff" : "Schiff",
            };
            string owner = own ? "Du" : u.Actors.ContainsKey(s.Owner) ? u.ActorName(s.Owner) : "";
            string name = ActorNames.Tagged(u, s.Owner, OrderMove.ShipName(u, s.Id));
            list.Add(new ScanContact(ScanKind.Ship, s.Id.Value, name, role + (owner.Length > 0 && !own ? " · " + owner : "") + (hostile ? " · feindlich" : ""), Vec2.Distance(at, p.Pos), hostile, false));
        }
        foreach (var w in u.Wrecks)
        {
            if (w.Sector != sec.Id) continue;
            float units = 0f; foreach (var v in w.Cargo.Values) units += v;
            string what = w.Kind == WreckKind.Container ? "Frachtbehälter" : "Wrack";
            string info = (units > 0.01f ? $"{units:0} Einheiten Fracht" : "keine Fracht") + (w.Modules.Count > 0 ? $" · {w.Modules.Count} Modul{(w.Modules.Count == 1 ? "" : "e")}" : "");
            list.Add(new ScanContact(ScanKind.Wreck, w.Id, what, info, Vec2.Distance(at, w.Position), false, false));
        }
        var rocks = new List<(Asteroid A, float D)>();
        foreach (var a in sec.Asteroids)
        {
            if (!a.Minable || a.ClassKey.Length == 0) continue;
            float d = MathF.Max(0f, Vec2.Distance(at, a.Position) - a.Radius);
            if (d <= RockRange) rocks.Add((a, d));
        }
        foreach (var (a, d) in rocks.OrderBy(r => r.D).ThenBy(r => r.A.Id).Take(MaxRocks))
        {
            string name = u.AsteroidClasses.TryGetValue(a.ClassKey, out var cls) && cls.Name.Length > 0 ? cls.Name : "Fels";
            list.Add(new ScanContact(ScanKind.Rock, a.Id, name, $"{a.Ore:0} Erz", d, false, false));
        }
        return list.OrderBy(c => c.Distance).ThenBy(c => (int)c.Kind).ThenBy(c => c.Id).ToList();
    }
}
