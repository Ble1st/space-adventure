using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Lücken gegenüber Interstellar Pilot 2: führt die Spielerstatistik (<see cref="PlayerStats"/> auf dem Spielerakteur). Liest nur Spielerereignisse
/// (eigene Verkäufe/Käufe, eigenes Erz, Abschüsse mit eigener Beteiligung, eigene Verluste, Aufträge, Kopfgelder, Umladen) und vergleicht je Tick Kasse
/// und Sektor des Spielers. Die Statistik entsteht erst, wenn sich wirklich etwas ändert — eine Welt, in der der Spieler nichts tut, bleibt unberührt.
/// Keine Zufallszahl, kein Schreibzugriff außerhalb der Statistik.
/// </summary>
public sealed class PlayerStatsSystem : ISimSystem, ISimEventListener
{
    public const int Interval = 1;

    Universe? _sub;
    Action<SimEvent>? _handler;
    /// <summary>Grundlage, solange es noch keine Statistik gibt (nicht gespeichert; nach dem Laden der Stand beim ersten Tick).</summary>
    double? _wallet;
    int? _sector;
    /// <summary>Je Universum zählt nur der zuletzt angemeldete Läufer (mehrere Läufer auf derselben Welt dürfen ein Ereignis nicht doppelt zählen).</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Universe, PlayerStatsSystem> Owner = new();

    public void Attach(Universe u)
    {
        if (ReferenceEquals(_sub, u)) return;
        if (_sub is not null && _handler is not null) _sub.Events.Raised -= _handler;
        _handler = e => OnEvent(u, e);
        u.Events.Raised += _handler;
        _sub = u;
        Owner.AddOrUpdate(u, this);
        _wallet = u.Actors.ContainsKey(u.PlayerActor) ? u.PlayerWallet.Credits : null;
        _sector = u.PlayerSector.Value;
    }

    /// <summary>Piloten: Statistik anlegen bzw. holen — über den angemeldeten Läufer (gleiche Kassen-/Sektor-Grundlage), sonst mit dem aktuellen Stand.</summary>
    public static PlayerStats EnsureStats(Universe u)
    {
        if (Owner.TryGetValue(u, out var o)) return o.Ensure(u);
        var me = u.Player;
        return me.Stats ??= new PlayerStats { LastWallet = me.Treasury.Credits, MaxWallet = me.Treasury.Credits, LastSector = u.PlayerSector.Value };
    }

    PlayerStats Ensure(Universe u)
    {
        var me = u.Player;
        if (me.Stats is { } s) return s;
        double w = _wallet ?? u.PlayerWallet.Credits;
        return me.Stats = new PlayerStats { LastWallet = w, MaxWallet = w, LastSector = _sector ?? u.PlayerSector.Value };
    }

    public void Step(Universe u, int dt)
    {
        Attach(u);
        if (!Owner.TryGetValue(u, out var owner) || !ReferenceEquals(owner, this)) return;
        if (!u.Actors.TryGetValue(u.PlayerActor, out var me)) return;
        double wallet = me.Treasury.Credits;
        int sector = u.PlayerSector.Value;
        var st = me.Stats;
        double baseW = st?.LastWallet ?? _wallet ?? wallet;
        int baseS = st?.LastSector ?? _sector ?? sector;
        if (wallet != baseW)
        {
            st = Ensure(u);
            if (wallet > baseW) st.Earned += wallet - baseW; else st.Spent += baseW - wallet;
            st.LastWallet = wallet;
            if (wallet > st.MaxWallet) st.MaxWallet = wallet;
        }
        if (sector != baseS && sector != 0)
        {
            st = Ensure(u);
            st.Jumps++;
            st.LastSector = sector;
        }
        _wallet = wallet;
        _sector = sector;
    }

    bool Mine(Universe u, ShipId ship) => u.Ships.TryGetValue(ship, out var s) && s.Owner == u.PlayerActor;

    void OnEvent(Universe u, SimEvent e)
    {
        if (!Owner.TryGetValue(u, out var owner) || !ReferenceEquals(owner, this)) return;
        if (!u.Actors.ContainsKey(u.PlayerActor)) return;
        switch (e)
        {
            case PlayerSold ps: { var s = Ensure(u); s.TradeRevenue += ps.Revenue; s.TradeUnits += ps.Units; s.Sales++; break; }
            case PlayerBought pb: { var s = Ensure(u); s.TradeSpent += pb.Cost; s.TradeUnits += pb.Units; s.Purchases++; break; }
            case OreCollected oc when Mine(u, oc.Ship): Ensure(u).OreMined += oc.Units; break;
            case ContactMined cm when Mine(u, cm.Ship): Ensure(u).OreMined += cm.Units; break;
            case CombatExited { Exit: CombatExit.Destroyed } ce when !Mine(u, ce.Ship) && Missions.BountyMission.PlayerInvolved(u, ce.Ship):
                Ensure(u).Kills++; break;
            case ShipDeparted { Reason: DepartureReason.Destroyed } sd when sd.Owner == u.PlayerActor: Ensure(u).ShipsLost++; break;
            case MissionCompleted: Ensure(u).MissionsDone++; break;
            case MissionFailed: Ensure(u).MissionsFailed++; break;
            case BountyEarned be when be.Hunter == u.PlayerActor: { var s = Ensure(u); s.Bounties++; s.BountyCredits += be.Amount; break; }
            case CargoTransferred ct: Ensure(u).Transferred += ct.Units; break;
            case PlayerSectorTaxCollected tax: Ensure(u).TaxCredits += tax.Amount; break;   // Welle C (1)
        }
    }
}
