using SpaceSim.Core.Events;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Intel;

/// <summary>
/// Ausbau Punkt 16: Aufdeckung. Ohne Wissensstand (<see cref="Actor.Intel"/> null) tut es nichts. Alle <see cref="Interval"/> Ticks scannen die eigenen
/// Schiffe ihre Umgebung (<see cref="Knowledge.Scan"/>: Sektor betreten, Stationen/Tore/Felder in Sensorreichweite); die Inbox macht Absender bekannt.
/// Über den Bus: Andocken (Station + Fraktion), Taxifahrt (Ziel sofort), Gefechtsbeginn (Gegner), angenommener Auftrag (Ziel). Kein Zufall; liest nur
/// Spielerdaten — die NPC-Welt merkt nichts.
/// </summary>
public sealed class IntelSystem : ISimSystem, ISimEventListener
{
    public const int Interval = 5;

    Universe? _sub;
    Action<SimEvent>? _handler;
    int _inboxSeen;

    public void Attach(Universe u)
    {
        if (ReferenceEquals(_sub, u)) return;
        if (_sub is not null && _handler is not null) _sub.Events.Raised -= _handler;
        _handler = e => OnEvent(u, e);
        u.Events.Raised += _handler;
        _sub = u;
        _inboxSeen = 0;
    }

    public void Step(Universe u, int dt)
    {
        Attach(u);
        if (Knowledge.Of(u) is null) return;
        foreach (var s in u.ShipsById())
        {
            if (s.Owner != u.PlayerActor) continue;
            var (sec, pos) = ThreatMap.Where(u, s);
            Knowledge.Publish(u, sec, Knowledge.Scan(u, sec, pos), "sensor");
        }
        // Comms: wer schreibt, ist begegnet (Inbox nach Id; nach dem Laden wird erneut gelesen — Aufdecken ist idempotent)
        if (u.Inbox.Count != _inboxSeen)
        {
            foreach (var m in u.Inbox.OrderBy(m => m.Id))
                if (m.From.Value != 0) Knowledge.Publish(u, u.PlayerSector, Knowledge.Meet(u, m.From), "comms");
            _inboxSeen = u.Inbox.Count;
        }
    }

    /// <summary>Ereignis → Aufdeckung (auch direkt aufrufbar).</summary>
    public static void OnEvent(Universe u, SimEvent e)
    {
        if (Knowledge.Of(u) is null) return;
        switch (e)
        {
            case ShipDocked sd when Mine(u, sd.Ship) && u.Stations.TryGetValue(sd.Station, out var st):
                Knowledge.Publish(u, st.Sector, Knowledge.Dock(u, sd.Station), "dock");
                break;
            case TaxiRide tr when u.Stations.TryGetValue(tr.To, out var to):
            {
                var d = Knowledge.Visit(u, to.Sector);
                d.Add(Knowledge.Dock(u, tr.To));
                Knowledge.Publish(u, to.Sector, d, "taxi");
                break;
            }
            case EncounterStarted es:
            {
                bool a = Mine(u, es.Attacker), b = Mine(u, es.Defender);
                if (a == b) break;
                var other = a ? es.Defender : es.Attacker;
                if (u.Ships.TryGetValue(other, out var os)) Knowledge.Publish(u, es.Sector, Knowledge.Meet(u, os.Owner), "contact");
                break;
            }
            case MissionAccepted ma:
                RevealMission(u, ma.Mission);
                break;
        }
    }

    static bool Mine(Universe u, ShipId id) => u.Ships.TryGetValue(id, out var s) && s.Owner == u.PlayerActor;

    /// <summary>Angenommener Auftrag: Zielsektor (dem Namen nach), Zielstation und Auftraggeber werden bekannt.</summary>
    public static void RevealMission(Universe u, int missionId)
    {
        if (Knowledge.Of(u) is null) return;
        var m = u.Missions.FirstOrDefault(x => x.Id == missionId);
        if (m is null) return;
        var d = Knowledge.Meet(u, m.Client);
        foreach (var mk in MissionMarkers.For(u).Where(x => x.Mission == missionId))
        {
            d.Add(Knowledge.See(u, mk.Sector));
            if (mk.Kind == MissionMarkerKind.Station && u.Sectors.TryGetValue(mk.Sector, out var sec))
                foreach (var sid in sec.Stations)
                    if (u.Stations.TryGetValue(sid, out var st) && Vec2.Distance(st.Position, mk.Position) <= st.Radius + 40f) d.Add(Knowledge.RevealStation(u, sid));
            Knowledge.Publish(u, mk.Sector, d, "mission");
            d = default;
        }
        // Zielstation (Lieferung, Transport) und Zielsektoren (Eskorte, Patrouillen-Wegpunkte) — unabhängig vom Marker
        if (m.Type is "delivery" or "transport" or "smuggle" or "passenger" && m.Args.TryGetValue("target", out var v) && int.TryParse(v, out var id) && u.Stations.TryGetValue(new StationId(id), out var ts))
            Knowledge.Publish(u, ts.Sector, Knowledge.RevealStation(u, ts.Id), "mission");
        if (m.Args.TryGetValue("dest", out var ds) && int.TryParse(ds, out var dsec))
            Knowledge.Publish(u, new SectorId(dsec), Knowledge.See(u, new SectorId(dsec)), "mission");
        if (m.Args.TryGetValue("wps", out var wps))
            foreach (var w in wps.Split(';', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(w.Split(':')[0], out var ws)) Knowledge.Publish(u, new SectorId(ws), Knowledge.See(u, new SectorId(ws)), "mission");
    }
}
