using SpaceSim.Core.Model;

namespace SpaceSim.Core.Help;

/// <summary>Bekannte Auslöser der Kontexthinweise (Daten in <c>help.json</c> dürfen nur diese nennen).</summary>
public static class HintTriggers
{
    public const string Dock = "dock", Trade = "trade", Mining = "mining", RaidWarning = "raid_warning", FuelLow = "fuel_low", Heat = "heat",
        Missile = "missile", Wormhole = "wormhole", Sun = "sun", Command = "command", Fleet = "fleet";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { Dock, Trade, Mining, RaidWarning, FuelLow, Heat, Missile, Wormhole, Sun, Command, Fleet };
}

/// <summary>
/// Was nur die Ansicht weiß: offener Handelsbereich, Kommandozentrale, Flottenbereich und ob der Bohrkopf gerade am Fels arbeitet (Kontaktzustand der
/// Spielerabbau-Mikrosimulation, nicht gespeichert).
/// </summary>
public readonly record struct HintView(bool TradeOpen = false, bool CommandOpen = false, bool FleetOpen = false, bool MiningContact = false);

/// <summary>
/// Kontexthinweise (UI.md „Hilfe und Tutorial“): welche Lagen gerade bestehen (<see cref="Active"/>, rein lesend aus dem Universum plus <see cref="HintView"/>)
/// und welcher noch nicht gesehene Hinweis als nächster dran ist (<see cref="Next"/>). Die „schon gesehen“-Liste hält die Ansicht in den Einstellungen —
/// nichts davon liegt im Spielstand oder im Hash.
/// </summary>
public static class HintRules
{
    /// <summary>Wurmloch gilt als „in der Nähe“ bis zu diesem Abstand (Einheiten).</summary>
    public const float WormholeNear = 300f;
    /// <summary>Hitzehinweis ab diesem Anteil (wie die Nachladebremse).</summary>
    public const float HeatShare = 0.7f;

    public static IReadOnlySet<string> Active(Universe u, HintView v)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (v.TradeOpen) set.Add(HintTriggers.Trade);
        if (v.CommandOpen) set.Add(HintTriggers.Command);
        if (v.FleetOpen) set.Add(HintTriggers.Fleet);
        if (v.MiningContact) set.Add(HintTriggers.Mining);
        if (!u.Ships.TryGetValue(u.PlayerShip, out var me) || u.PlayerOnFoot) return set;
        if (me.DockedAt.Value != 0)
        {
            set.Add(HintTriggers.Dock);
            return set;   // angedockt: keine Fluglagen
        }
        if (me.AnchorRock != 0) set.Add(HintTriggers.Mining);
        if (u.PendingEncounters.Any(p => p.Target == me.Id)) set.Add(HintTriggers.RaidWarning);
        if (u.Rules.FuelActive && Flight.TankInfo.For(u, me).Any(l => l.Low)) set.Add(HintTriggers.FuelLow);
        if (Combat.Heat.Share(u, me) >= HeatShare) set.Add(HintTriggers.Heat);
        if (Combat.Countermeasures.Incoming(u, me).Count > 0) set.Add(HintTriggers.Missile);
        if (Simulation.Suns.ZoneAt(u, me.Sector, me.Position) != Simulation.SunZone.None) set.Add(HintTriggers.Sun);
        if (WormholeNearby(u, me)) set.Add(HintTriggers.Wormhole);
        return set;
    }

    static bool WormholeNearby(Universe u, Ship me)
    {
        foreach (var g in u.Gates.Values)
            if (g.Wormhole && g.Sector == me.Sector && Vec2.Distance(g.Position, me.Position) <= WormholeNear) return true;
        foreach (var w in u.UnstableWormholes)
            if (w.Sector == me.Sector && Vec2.Distance(w.Position, me.Position) <= WormholeNear) return true;
        return false;
    }

    /// <summary>Erster noch nicht gesehener Hinweis (Reihenfolge der Daten), dessen Auslöser gerade besteht; null = keiner.</summary>
    public static HintDef? Next(IReadOnlyList<HintDef> hints, IReadOnlySet<string> active, IReadOnlySet<string> seen)
    {
        foreach (var h in hints)
            if (!seen.Contains(h.Key) && active.Contains(h.Trigger)) return h;
        return null;
    }

    public static HintDef? Next(IReadOnlySet<string> active, IReadOnlySet<string> seen) => Next(HelpData.Default.Hints, active, seen);
}
