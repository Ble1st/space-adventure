namespace SpaceSim.Core.Model;

/// <summary>
/// Phase 12 (FLOTTEN.md Abschnitt 4): Befehl an ein Schiff — die mittlere der drei Ebenen Notfall &gt; Befehl &gt; Automatik. Ein Befehl gilt, bis er
/// beendet oder ersetzt wird; ausgeführt wird er von dem <see cref="Simulation.IOrderExecutor"/>, der für seine <see cref="Kind"/> registriert ist.
/// Der Befehl ist gespeicherter Zustand (<see cref="Ship.Command"/>); <see cref="Ship.Order"/> bleibt die Arbeitsanweisung der ausführenden Systeme
/// (Handel, Abbau, Bau) bzw. der laufenden Bewegung. Kein Befehl = Automatik (KI: Aufgaben-Systeme; Spielerschiffe: halten die Position).
/// </summary>
public abstract record ShipCommand
{
    /// <summary>Schlüssel des Befehlstyps (Registry der Ausführer, Speicherformat).</summary>
    public abstract string Kind { get; }
}

/// <summary>Schlüssel der Befehlstypen (Registry, Speicherformat, Oberfläche).</summary>
public static class CommandKinds
{
    public const string GoTo = "goto";
    public const string Hold = "hold";
    public const string Patrol = "patrol";
    public const string Guard = "guard";
    public const string Escort = "escort";
    public const string Follow = "follow";
    public const string Trade = "trade";
    public const string Mine = "mine";
    public const string Build = "build";
    public const string Return = "return";
    /// <summary>Phase 27: Lauern (Kaperschiffe der Piratengruppen; kein Spielerbefehl).</summary>
    public const string Lurk = "lurk";
    /// <summary>Phase 27: Verfolgen (Polizei verfolgt ein geflohenes Kaperschiff in ihrer Zuständigkeit; kein Spielerbefehl).</summary>
    public const string Pursue = "pursue";
    /// <summary>Phase 28: Angriff auf eine Station (Kriegsschiffe einer Fraktion im Krieg; kein Spielerbefehl).</summary>
    public const string Assault = "assault";
}

/// <summary>Fliegen: zu einer Station (andocken) oder zu einem Punkt in einem Sektor (<paramref name="Station"/> default).</summary>
public sealed record GoToCommand(SectorId Sector, Vec2 Position, StationId Station) : ShipCommand
{
    public override string Kind => CommandKinds.GoTo;
}

/// <summary>Position halten (eine laufende Reise endet an Ort und Stelle).</summary>
public sealed record HoldCommand : ShipCommand
{
    public override string Kind => CommandKinds.Hold;
}

/// <summary>Streife über eine Runde von Sektoren (Streifenpunkte wie die Polizei, Verweilzeit), braucht <see cref="Capability.Combat"/>.</summary>
public sealed record PatrolCommand(IReadOnlyList<SectorId> Sectors) : ShipCommand
{
    public override string Kind => CommandKinds.Patrol;
    public bool Equals(PatrolCommand? other) => other is not null && Sectors.SequenceEqual(other.Sectors);
    public override int GetHashCode() => Sectors.Aggregate(17, (h, s) => h * 31 + s.Value);
}

/// <summary>Station bewachen (bleibt dort, verteidigt Schiffe im Sektor), braucht <see cref="Capability.Combat"/>.</summary>
public sealed record GuardCommand(StationId Station) : ShipCommand
{
    public override string Kind => CommandKinds.Guard;
}

/// <summary>Ein Schiff begleiten und verteidigen, braucht <see cref="Capability.Combat"/>.</summary>
public sealed record EscortCommand(ShipId Target) : ShipCommand
{
    public override string Kind => CommandKinds.Escort;
}

/// <summary>Einem Schiff folgen (auch Verbandsmitglieder hinter dem Anführer).</summary>
public sealed record FollowCommand(ShipId Target) : ShipCommand
{
    public override string Kind => CommandKinds.Follow;
}

/// <summary>
/// Grenzen eines Automatikauftrags (FLOTTEN.md Abschnitt 5: „Der Spieler gibt nur Richtung und Grenzen vor“): Mittelpunkt und Reichweite in Sprüngen
/// (−1 = unbegrenzt), höchster Einkaufspreis relativ zum Basispreis (0 = keine Grenze) und Rücklage, die in der Kasse bleiben muss.
/// </summary>
public sealed record AutoLimits(SectorId Center, int RadiusJumps, float MaxBuyFactor, float Reserve)
{
    public static readonly AutoLimits None = new(default, -1, 0f, 0f);
}

/// <summary>Handel automatisch im Rahmen der Grenzen (Ausführer: <see cref="Simulation.TraderSystem"/>), braucht <see cref="Capability.Cargo"/>.</summary>
public sealed record TradeCommand(AutoLimits Limits) : ShipCommand
{
    public override string Kind => CommandKinds.Trade;
}

/// <summary>Schürfen und verkaufen im Rahmen der Grenzen (Ausführer: <see cref="Simulation.MinerSystem"/>), braucht <see cref="Capability.Mining"/>.</summary>
public sealed record MineCommand(AutoLimits Limits) : ShipCommand
{
    public override string Kind => CommandKinds.Mine;
}

/// <summary>Baustelle übernehmen (default = nächste freie): Material liefern und montieren (Ausführer: <see cref="Simulation.ConstructionSystem"/>), braucht <see cref="Capability.Construction"/>.</summary>
public sealed record BuildCommand(StationId Site) : ShipCommand
{
    public override string Kind => CommandKinds.Build;
}

/// <summary>Heimkehr: zur Heimatbasis, dort tanken und reparieren, dann angedockt bleiben.</summary>
public sealed record ReturnCommand : ShipCommand
{
    public override string Kind => CommandKinds.Return;
}

/// <summary>
/// Phase 27: Lauern — ein Kaperschiff (oder der Anführer eines Rudels) fliegt zum Lauerposten <paramref name="Spot"/> im Revier <paramref name="Sector"/>
/// und wartet dort bis <paramref name="Until"/> auf Beute (Überfall nach der Regel von <see cref="Simulation.PirateSystem"/>). Vergibt die Piratengruppe, nicht der Spieler.
/// </summary>
public sealed record LurkCommand(SectorId Sector, Vec2 Spot, long Until) : ShipCommand
{
    public override string Kind => CommandKinds.Lurk;
}

/// <summary>
/// Phase 27: Verfolgen — ein Polizeischiff (oder der Anführer einer Streife) folgt einem geflohenen Kaperschiff innerhalb der Zuständigkeit seiner Fraktion
/// bis <paramref name="Until"/>. <paramref name="Lead"/> ist der Vorsprung des Ziels (Einheiten): er schrumpft, wenn der Verfolger im selben Sektor schneller ist
/// oder das Ziel liegt; bei 0 kommt es zum Gefecht. Vergibt die Fraktion, nicht der Spieler.
/// </summary>
public sealed record PursueCommand(ShipId Target, float Lead, long Until) : ShipCommand
{
    public override string Kind => CommandKinds.Pursue;
}

/// <summary>
/// Phase 28: Angriff — ein Kriegsschiff fliegt zur feindlichen Station <paramref name="Station"/> im Sektor <paramref name="Sector"/> und belagert sie
/// (Gefecht mit Anlass <see cref="EncounterCause.Siege"/>). Vergibt die Fraktion im Krieg, nicht der Spieler.
/// </summary>
public sealed record AssaultCommand(SectorId Sector, StationId Station) : ShipCommand
{
    public override string Kind => CommandKinds.Assault;
}

/// <summary>Verhaltensregeln (FLOTTEN.md „Rules of Engagement“).</summary>
public enum Engagement
{
    /// <summary>Feuert nur, wenn es selbst oder ein Schützling angegriffen wird (Standard).</summary>
    Defend,
    /// <summary>Keine Waffen, flieht.</summary>
    Passive,
    /// <summary>Greift Feinde und gesuchte Schiffe an (lauernde Kaperschiffe im Sektor).</summary>
    Attack,
    /// <summary>Nie feuern, auch nicht zur Selbstverteidigung.</summary>
    HoldFire,
}

/// <summary>Formation eines Verbands — nur Darstellung (Versatz der Mitglieder um den Anführer), ohne Wirkung auf die Simulation.</summary>
public enum Formation { Line, Wedge, Star, Box }

public readonly record struct SquadronId(int Value);

/// <summary>
/// Verband (FLOTTEN.md Abschnitte 3/3a): Anführer, höchstens <see cref="EconomyRules.SquadronMaxMembers"/> Mitglieder (Anführer eingeschlossen),
/// Formation (nur Darstellung), Heimatbasis und Verhaltensregel. Der Befehl des Verbands ist der Befehl des Anführers; Bewegungsbefehle plant nur der
/// Anführer (mit dem Tempo des langsamsten Mitglieds), die Mitglieder folgen; Arbeitsbefehle (Handel, Abbau, Bau) führt jedes fähige Mitglied selbst aus.
/// </summary>
public sealed class Squadron
{
    public SquadronId Id;
    public ActorId Owner;
    public string Name = "";
    public ShipId Leader;
    /// <summary>Mitglieder in fester Reihenfolge, der Anführer zuerst.</summary>
    public List<ShipId> Members = new();
    public Formation Formation;
    public StationId HomeBase;
    public Engagement Rules;
}

/// <summary>Deutsche Anzeigenamen der Befehle, Regeln und Formationen.</summary>
public static class CommandNames
{
    public static string Kind(string kind) => kind switch
    {
        CommandKinds.GoTo => "Fliegen",
        CommandKinds.Hold => "Halten",
        CommandKinds.Patrol => "Patrouille",
        CommandKinds.Guard => "Wache",
        CommandKinds.Escort => "Eskorte",
        CommandKinds.Follow => "Folgen",
        CommandKinds.Trade => "Handel",
        CommandKinds.Mine => "Schürfen",
        CommandKinds.Build => "Bauen",
        CommandKinds.Return => "Heimkehr",
        CommandKinds.Lurk => "Lauern",
        CommandKinds.Pursue => "Verfolgen",
        _ => kind,
    };

    public static string Rule(Engagement e) => e switch
    {
        Engagement.Passive => "Passiv",
        Engagement.Attack => "Angriff",
        Engagement.HoldFire => "Feuer halten",
        _ => "Verteidigen",
    };

    public static string Formation(Formation f) => f switch
    {
        Model.Formation.Wedge => "Keil",
        Model.Formation.Star => "Stern",
        Model.Formation.Box => "Block",
        _ => "Linie",
    };
}
