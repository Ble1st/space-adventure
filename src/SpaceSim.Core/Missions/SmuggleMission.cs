using System.Globalization;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Core.Missions;

/// <summary>
/// Ausbau Punkte 10/14: Kontrollen für Aufträge mit verbotener Fracht (Schmuggel) und gesuchten Fahrgästen (Flüchtling). Zwei Stellen: (1) Tor mit Sperre
/// (Eintrittsstufe ≥ 1, durchgesetzt) — der Torsensor (<see cref="EconomyRules.GateSensorStrength"/>) findet ein ungetarntes Schiff sicher; (2) Streife
/// in Reichweite (<see cref="SmuggleSearch.Roll"/>, Sensorstärke der Polizei). Gefunden → Auftrag gescheitert (versiegelte Fracht beschlagnahmt),
/// Verbrechen „smuggling“ mit Strafe bei der Fraktion (<see cref="Wanted.Report"/> → Ansehensverlust über <see cref="CrimeStanding"/>).
/// Tarnfeld und Sprungantrieb (Sprung statt Tor) helfen. Der Sprungantrieb prüft beim Sprung mit seinem Sensor nur echte Fracht.
/// </summary>
public static class MissionContraband
{
    /// <summary>Trägt dieser Auftrag etwas, das die Kontrolle findet?</summary>
    public static bool Risky(Mission m) =>
        m.State == MissionState.Active && (m.Type == SmuggleMission.KeyId || (m.Type == PassengerMission.KeyId && m.Args.GetValueOrDefault("kind") == "refugee"));

    static bool OnShip(Mission m, Ship ship) => m.Args.TryGetValue("ship", out var s) && int.TryParse(s, out var v) && v == ship.Id.Value;

    /// <summary>Gefunden: Auftrag scheitert, Verbrechen bei der Fraktion, Ereignis.</summary>
    public static void Found(Universe u, Mission m, Ship ship, FactionId f, SectorId sector, string where)
    {
        float extra = m.Type == SmuggleMission.KeyId ? u.Rules.MissionSmuggleFine : u.Rules.MissionSmuggleFine * 0.5f;
        u.Events.Publish(new MissionContrabandFound(m.Id, ship.Id, f, sector, where));
        MissionRules.Fail(u, m, where == "gate" ? "am Kontrollpunkt entdeckt" : "von der Polizei durchsucht");
        if (u.Rules.SecurityActive) Wanted.Report(u, u.PlayerActor, f, "smuggling", extra);
    }

    /// <summary>Spieler springt durch ein Tor in einen gesperrten Sektor: Torsensor gegen Tarnfeld.</summary>
    public static void AtGate(Universe u, Ship ship, Gate gate)
    {
        if (u.Missions.Count == 0 || !u.Gates.TryGetValue(gate.Target, out var far) || !u.Sectors.TryGetValue(far.Sector, out var sec)) return;
        if (sec.Access is not { Enforced: true } acc || acc.EntryLevel < 1 || sec.Owner.Value == 0 || !u.IsNpcFaction(sec.Owner)) return;
        if (Cloak.HiddenFrom(u, ship, u.Rules.GateSensorStrength)) return;
        foreach (var m in u.Missions.Where(m => Risky(m) && OnShip(m, ship)).ToList()) Found(u, m, ship, sec.Owner, sec.Id, "gate");
    }

    /// <summary>Streife in Reichweite durchsucht (in <see cref="IMissionType.Check"/>, alle 20 Ticks).</summary>
    public static bool Search(Universe u, Mission m)
    {
        if (!Risky(m) || !m.Args.TryGetValue("ship", out var s) || !int.TryParse(s, out var v) || !u.Ships.TryGetValue(new ShipId(v), out var ship)) return false;
        if (ship.DockedAt.Value != 0) return false;   // angedockt sucht niemand
        if (SmuggleSearch.Roll(u, ship) is not { } f) return false;
        Found(u, m, ship, f, SmuggleSearch.Where(u, ship).Sector, "police");
        return true;
    }
}

/// <summary>
/// Ausbau Punkt 10: Auftragsart <b>Schmuggel</b> — der Hehler im Versteck (Dienst „blackmarket“) oder die Station einer gesetzlosen Firma bietet an:
/// „Bringe versiegelte Ware (belegt Laderaum, <see cref="Ship.MissionCargo"/>) zu einer Station in einem gesperrten Sektor“ (Eintrittssperre; ohne Sperre
/// irgendwo: ein Sektor unter Polizeischutz). Hohe Belohnung (Grundlohn + je Flugminute + je Sperrstufe), kein Pfand. Kontrolle am Tor oder durch
/// die Polizei (<see cref="MissionContraband"/>): Beschlagnahme, Strafe, Ansehensverlust. Ablieferung durch Andocken an der Zielstation.
/// </summary>
public sealed class SmuggleMission : IMissionType
{
    public const string KeyId = "smuggle";
    public string Key => KeyId;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Bietet diese Station Schmuggel an (Hehler, Station einer gesetzlosen Firma)?</summary>
    public static bool IsOfferStation(Universe u, Station st) =>
        BlackMarket.IsAt(u, st) || (u.Actors.TryGetValue(st.Owner, out var a) && SmugglerSystem.LawlessFirm(u, a));

    /// <summary>Sperrstufe eines Sektors (durchgesetzt), 0 = keine.</summary>
    public static int LockOf(Sector sec) => sec.Access is { Enforced: true } a ? a.EntryLevel : 0;

    public MissionOffer? Generate(Universe u, Station st, DeterministicRng rng, long bucket)
    {
        var r = u.Rules;
        if (!r.MissionSmuggle || !IsOfferStation(u, st)) return null;
        if (MissionRules.Forbidden(u, st.Owner) || MissionRules.Blocked(u, st.Owner.Value)) return null;
        if (MissionRng.NextFloat(rng) >= r.MissionSmuggleChance) return null;
        var cands = new List<Station>();
        bool locked = false;
        foreach (var s in u.StationsById())
        {
            if (s.Id == st.Id || !s.Dockable || s.IsBuildSite || BlackMarket.IsAt(u, s) || !u.Sectors.TryGetValue(s.Sector, out var sec)) continue;
            if (sec.Owner.Value == 0 || !u.IsNpcFaction(sec.Owner) || !u.IsReachable(st.Sector, s.Sector)) continue;
            bool l = LockOf(sec) >= 1;
            if (l && !locked) { cands.Clear(); locked = true; }
            if (l == locked && (locked || sec.Security >= 1f)) cands.Add(s);
        }
        if (cands.Count == 0) return null;
        var target = cands[MissionRng.NextInt(rng, cands.Count)];
        int level = LockOf(u.Sectors[target.Sector]);
        long ticks = MissionRules.TripTicks(u, st.Sector, st.DockPoint(default), target.Sector, target.DockPoint(default), null);
        float reward = MathF.Round(r.MissionSmuggleBaseReward + r.MissionSmuggleRewardPerMinute * ticks / (60f * SimClock.TicksPerSecond) + level * r.MissionSmuggleRewardPerLevel);
        if (!u.Actors.TryGetValue(st.Owner, out var client)) return null;
        if (MissionRules.SpentToday(u, st.Owner.Value) + reward > MissionRules.BudgetOf(u, client) + 0.0001f) return null;
        if (FactionViability.Spendable(u, client) < reward - 0.0001f) return null;
        var o = new MissionOffer { Type = KeyId, Reward = reward };
        o.Args["target"] = target.Id.Value.ToString(Inv);
        o.Args["volume"] = r.MissionSmuggleUnits.ToString("0.##", Inv);
        o.Args["level"] = level.ToString(Inv);
        o.Args["channel"] = "station";   // nur am Hehler, nicht über Comms
        o.Args["contract"] = "0";
        return o;
    }

    public string? AcceptIssue(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        if (!u.Stations.ContainsKey(TransportMission.TargetOf(o))) return "Zielstation existiert nicht mehr.";
        float volume = TransportMission.VolumeOf(o);
        if (ship.CargoCapacity - ship.UsedVolume(u) - ship.MissionCargo < volume - 0.0001f)
            return $"Zu wenig freier Laderaum für die Schmuggelware ({volume:0.#} m³ nötig).";
        return null;
    }

    public Mission Accept(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        float volume = TransportMission.VolumeOf(o);
        ship.MissionCargo += volume;
        var target = u.Stations[TransportMission.TargetOf(o)];
        return new Mission
        {
            Type = KeyId, Client = o.Client, At = o.At,
            Args = new Dictionary<string, string>(o.Args) { ["ship"] = ship.Id.Value.ToString(Inv) },
            Deadline = MissionRules.DeadlineFrom(u, ship, target),
            CargoVolume = volume,
        };
    }

    public void OnEvent(Universe u, Mission m, SimEvent e)
    {
        if (e is not ShipDocked sd || !Mine(m, sd.Ship) || sd.Station != Target(m)) return;
        if (u.Ships.TryGetValue(sd.Ship, out var ship) && u.Stations.TryGetValue(sd.Station, out var st)) Deliver(u, m, ship, st);
    }

    static bool Mine(Mission m, ShipId s) => m.Args.TryGetValue("ship", out var v) && int.TryParse(v, out var id) && id == s.Value;
    static StationId Target(Mission m) => m.Args.TryGetValue("target", out var t) && int.TryParse(t, out var v) ? new StationId(v) : default;

    public bool TargetGone(Universe u, Mission m) => !u.Stations.ContainsKey(Target(m));

    public (SectorId Sector, Vec2 Pos, string Label, MissionMarkerKind Kind)? Marker(Universe u, Mission m) =>
        u.Stations.TryGetValue(Target(m), out var st) ? (st.Sector, st.Position, Describe(u, m), MissionMarkerKind.Station) : null;

    public string TargetText(Universe u, MissionOffer o) => u.Stations.TryGetValue(TransportMission.TargetOf(o), out var st) ? st.Name : "";

    public long DeadlineOf(Universe u, MissionOffer o, Ship ship) =>
        u.Stations.TryGetValue(TransportMission.TargetOf(o), out var st) ? MissionRules.DeadlineFrom(u, ship, st) - u.Tick : u.Rules.MissionMinDeadlineTicks;

    public string DescribeOffer(Universe u, MissionOffer o)
    {
        var target = u.Stations[TransportMission.TargetOf(o)];
        int level = int.TryParse(o.Args.GetValueOrDefault("level"), out var l) ? l : 0;
        string lockText = level > 0 ? $"Sperrstufe {level}" : "Polizeigebiet";
        return $"Schmuggel: versiegelte Ware ({TransportMission.VolumeOf(o):0.#} m³) nach {target.Name} ({lockText}) — Belohnung {o.Reward:0} Cr. Kontrolle = Beschlagnahme, Strafe, Ansehen; Tarnfeld und Sprungantrieb helfen.";
    }

    public string Describe(Universe u, Mission m)
    {
        if (!u.Stations.TryGetValue(Target(m), out var st)) return $"Auftrag #{m.Id}: Ziel weg";
        return $"Auftrag: Schmuggelware ({m.CargoVolume:0.#} m³) → {st.Name} · noch {MissionRules.Rest(Math.Max(0L, m.Deadline - u.Tick))} (Andocken liefert ab; Polizei meiden)";
    }

    public void Check(Universe u, Mission m)
    {
        if (u.Tick >= m.Deadline) { MissionRules.Fail(u, m, "Frist abgelaufen"); return; }
        if (TargetGone(u, m)) { MissionRules.Fail(u, m, "Zielstation weg"); return; }
        if (MissionContraband.Search(u, m)) return;
        if (m.Args.TryGetValue("ship", out var ss) && int.TryParse(ss, out var sid) && u.Ships.TryGetValue(new ShipId(sid), out var sh)
            && sh.DockedAt == Target(m) && u.Stations.TryGetValue(sh.DockedAt, out var st)) Deliver(u, m, sh, st);
    }

    public string? DeliverIssue(Universe u, Mission m, Ship ship, Station st)
    {
        if (st.Id != Target(m)) return "Falsche Station — die Ware geht an die Zielstation.";
        if (ship.DockedAt != st.Id) return "Dafür musst du an der Zielstation andocken.";
        return null;
    }

    public void Deliver(Universe u, Mission m, Ship ship, Station st)
    {
        ship.MissionCargo = MathF.Max(0f, ship.MissionCargo - m.CargoVolume);
        MissionRules.Complete(u, m);
    }
}
