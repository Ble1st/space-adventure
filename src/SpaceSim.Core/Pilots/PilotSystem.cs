using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Pilots;

/// <summary>
/// Piloten: zählt gewichtete Abschüsse und Verbrechen des Spielers (in <see cref="PlayerStats"/>), Abschüsse benannter NPC-Kapitäne
/// (<see cref="Universe.Captains"/>) und meldet Rangwechsel des Spielers (<see cref="PilotRankChanged"/>). Liest nur Ereignisse, schreibt nur diese
/// Statistik — kein Zufall, kein Einfluss auf die Simulation (die NPC-Welt bleibt bitgleich). Schalter <see cref="EconomyRules.PilotRanks"/>.
/// Schütze eines Abschusses = letzter Treffer auf das Ziel (flüchtig, nach dem Laden leer), sonst der erste aktive Gegner im Gefecht.
/// </summary>
public sealed class PilotSystem : ISimSystem, ISimEventListener
{
    public const int Interval = 1;

    Universe? _sub;
    Action<SimEvent>? _handler;
    readonly Dictionary<ShipId, ShipId> _lastHit = new();
    long _crimeTick = -1;
    readonly HashSet<string> _crimesThisTick = new();
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Universe, PilotSystem> Owner = new();

    public void Attach(Universe u)
    {
        if (ReferenceEquals(_sub, u)) return;
        if (_sub is not null && _handler is not null) _sub.Events.Raised -= _handler;
        _handler = e => OnEvent(u, e);
        u.Events.Raised += _handler;
        _sub = u;
        _lastHit.Clear();
        Owner.AddOrUpdate(u, this);
    }

    bool IsOwner(Universe u) => Owner.TryGetValue(u, out var o) && ReferenceEquals(o, this);

    public void Step(Universe u, int dt)
    {
        Attach(u);
        if (!u.Rules.PilotRanks || !IsOwner(u)) return;
        if (!u.Actors.TryGetValue(u.PlayerActor, out var me) || me.Stats is not { } st) return;
        float rating = PilotBook.PlayerRating(u);
        int rank = PilotBook.RankOf(rating);
        if (rank == st.RankSeen) return;
        int old = st.RankSeen;
        st.RankSeen = rank;
        u.Events.Publish(new PilotRankChanged(old, rank, rating));
    }

    void OnEvent(Universe u, SimEvent e)
    {
        if (!u.Rules.PilotRanks || !IsOwner(u)) return;
        switch (e)
        {
            case ShotFired { Hit: true } sf: _lastHit[sf.Target] = sf.Shooter; break;
            case CombatExited { Exit: CombatExit.Destroyed } ce: OnDestroyed(u, ce); break;
            case CrimeReported cr when cr.Actor == u.PlayerActor && u.Actors.ContainsKey(u.PlayerActor):
            {
                // ein Vergehen meldet oft mehrere Behörden — je Tick und Art nur einmal zählen
                if (_crimeTick != u.Tick) { _crimeTick = u.Tick; _crimesThisTick.Clear(); }
                if (_crimesThisTick.Add(cr.Crime)) PlayerStatsSystem.EnsureStats(u).Crimes++;
                break;
            }
        }
    }

    void OnDestroyed(Universe u, CombatExited ce)
    {
        _lastHit.Remove(ce.Ship, out var hitBy);
        if (!u.Ships.TryGetValue(ce.Ship, out var dead)) return;
        float value = PilotBook.KillValue(u, dead);
        bool deadIsPlayers = dead.Owner == u.PlayerActor;
        if (!deadIsPlayers && u.Actors.ContainsKey(u.PlayerActor) && Missions.BountyMission.PlayerInvolved(u, ce.Ship))
            PlayerStatsSystem.EnsureStats(u).KillPoints += value;

        // Schütze bestimmen: letzter Treffer, wenn er auf der Gegenseite steht, sonst erster aktiver Gegner
        var enc = u.Fights.Encounters.FirstOrDefault(x => x.Id == ce.Encounter);
        var dp = enc?.Participants.FirstOrDefault(p => p.Ship == ce.Ship);
        Ship? killer = null;
        if (enc is not null && dp is not null)
        {
            if (hitBy.Value != 0 && enc.Participants.FirstOrDefault(p => p.Ship == hitBy) is { } hp && hp.Side != dp.Side && u.Ships.TryGetValue(hitBy, out var hs)) killer = hs;
            else if (enc.Participants.FirstOrDefault(p => p.Side != dp.Side && p.Active && u.Ships.ContainsKey(p.Ship)) is { } fp) killer = u.Ships[fp.Ship];
        }
        if (killer is not null && PilotBook.HasCaptain(killer))
        {
            if (!u.Captains.TryGetValue(killer.Id, out var rec)) u.Captains[killer.Id] = rec = new CaptainRecord();
            rec.Kills++;
            rec.Rating += value;
            rec.Role = killer.Role;
            rec.Owner = killer.Owner;
        }
        if (PilotBook.HasCaptain(dead) && u.Captains.TryGetValue(dead.Id, out var drec)) { drec.Fallen = true; Prune(u); }
    }

    /// <summary>Nicht mehr aktive Kapitäne begrenzen (die schwächsten fallen heraus; Reihenfolge fest: Wertung, dann Id).</summary>
    static void Prune(Universe u)
    {
        var gone = u.Captains.Where(x => !u.Ships.ContainsKey(x.Key) || x.Value.Fallen).OrderBy(x => x.Value.Rating).ThenBy(x => x.Key.Value).ToList();
        for (int i = 0; i < gone.Count - PilotBook.MaxRetiredRecords; i++) u.Captains.Remove(gone[i].Key);
    }
}
