using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>Phase 27: eine Verbindung zwischen zwei Schiffen für die Anzeige — Verbandsmitglied → Anführer, Begleiter → Schützling, Verfolger → Ziel.</summary>
/// <param name="Kind">„squadron“ (Streife/Verband), „pack“ (Rudel), „escort“ (Geleit/Eskorte), „pursuit“ (Verfolgung).</param>
public readonly record struct FleetLink(ShipId From, ShipId To, string Kind);

/// <summary>
/// Phase 27: reine Anzeige des Flottenkampfs (keine Zustandsänderung) — Meldungen für den Spielersektor, Verbindungen zwischen Schiffen (Sektoransicht der
/// Kommandozentrale) und eine Zeile je Schiff (Verband, Geleit, Verfolgung). Die Ansicht erfindet nichts: alles aus Befehlen und Verbänden des Core.
/// </summary>
public static class FleetInfo
{
    static string Name(Universe u, ShipId id) => u.Ships.TryGetValue(id, out var s) ? PlayerCombat.Describe(u, s) : $"Schiff #{id.Value}";

    /// <summary>Meldung zu einem Ereignis im Spielersektor (null = keine).</summary>
    public static string? Toast(Universe u, SimEvent e)
    {
        switch (e)
        {
            case CombatJoined cj:
            {
                var fight = u.Fights.Encounters.FirstOrDefault(x => x.Id == cj.Encounter);
                if (fight is null || fight.Sector != u.PlayerSector) return null;
                string why = cj.Reason switch { "escort" => "Geleit", "guard" => "Wache", "squadron" => "Verband", "pack" => "Rudel", "pursuit" => "Verfolger", _ => "Streife" };
                return $"Verstärkung ({why}): {Name(u, cj.Ship)} greift ein";
            }
            case RefugeReached rr:
            {
                if (rr.Kind == "station" && rr.Sector == u.PlayerSector) return $"{Name(u, rr.Ship)} rettet sich an eine Station";
                if (rr.Kind == "gate" && rr.Sector == u.PlayerSector) return $"{Name(u, rr.Ship)} springt auf der Flucht in diesen Sektor";
                var fight = u.Fights.Encounters.FirstOrDefault(x => x.Id == rr.Encounter);
                return rr.Kind == "gate" && fight?.Sector == u.PlayerSector ? $"{Name(u, rr.Ship)} flieht durch das Tor" : null;
            }
            case PursuitStarted ps when ps.Sector == u.PlayerSector:
                return $"Polizei verfolgt {Name(u, ps.Target)}";
            case EscortAssigned ea when ea.Sector == u.PlayerSector:
                return $"Polizei gibt {Name(u, ea.Ward)} Geleit";
            case ForceSquadron fs when fs.Formed && u.Squadrons.TryGetValue(fs.Squadron, out var sq) && u.Ships.TryGetValue(sq.Leader, out var lead)
                                       && ThreatMap.Where(u, lead).Sector == u.PlayerSector:
                return fs.Kind == "pack" ? $"{u.ActorName(fs.Owner)}: Rudel aus {fs.Members} Kaperschiffen bricht auf" : $"{u.ActorName(fs.Owner)}: Streife fliegt als Verband ({fs.Members} Schiffe)";
        }
        return null;
    }

    /// <summary>Verbindungen der Schiffe, die gerade im Sektor sind (feste Reihenfolge nach Id).</summary>
    public static List<FleetLink> Links(Universe u, SectorId sector)
    {
        var list = new List<FleetLink>();
        foreach (var s in u.ShipsById())
        {
            if (ThreatMap.Where(u, s).Sector != sector) continue;
            switch (s.Command)
            {
                case FollowCommand f when Squadrons.Get(u, s) is { } sq && sq.Leader == f.Target:
                    list.Add(new FleetLink(s.Id, f.Target, ThreatMap.IsRaider(s) ? "pack" : "squadron")); break;
                case EscortCommand ec: list.Add(new FleetLink(s.Id, ec.Target, "escort")); break;
                case PursueCommand pc: list.Add(new FleetLink(s.Id, pc.Target, "pursuit")); break;
            }
        }
        return list;
    }

    /// <summary>Eine Zeile zum Verband/Auftrag eines Schiffs (null = nichts Besonderes).</summary>
    public static string? Describe(Universe u, Ship s)
    {
        string? squad = Squadrons.Get(u, s) is { } sq ? $"{sq.Name} ({sq.Members.Count} Schiffe{(sq.Leader == s.Id ? ", Anführer" : "")})" : null;
        string? task = s.Command switch
        {
            EscortCommand ec => $"Geleit für {Name(u, ec.Target)}",
            PursueCommand pc => $"verfolgt {Name(u, pc.Target)}",
            LurkCommand lc => $"lauert in {OrderMove.SectorName(u, lc.Sector)}",
            _ => null,
        };
        return squad is null ? task : task is null ? squad : $"{squad} · {task}";
    }
}
