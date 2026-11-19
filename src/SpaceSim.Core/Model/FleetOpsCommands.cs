namespace SpaceSim.Core.Model;

/// <summary>Flottenbefehle des Spielers (Ausbau Punkt 1/1b): Schlüssel der neuen Befehlstypen.</summary>
public static partial class CommandKinds
{
    /// <summary>Angreifen: Schiff, Station oder Tor (Spielerflotte).</summary>
    public const string Attack = "attack";
    /// <summary>Bergen: Wracks und Behälter eines Sektors leeren, Beute verkaufen.</summary>
    public const string Salvage = "salvage";
    /// <summary>Tor blockieren: am Tor halten und Schiffe einer Kriegsgegner-Fraktion angreifen.</summary>
    public const string Blockade = "blockade";
    /// <summary>Liefern (Pendel): feste Ware von A nach B, wiederholt.</summary>
    public const string Deliver = "deliver";
    /// <summary>Sammelpunkt: Schiffe treffen sich an einer Station und bilden einen Verband.</summary>
    public const string Rally = "rally";
    /// <summary>Konvoi: Händler fahren gemeinsam (Anführer = langsamster), Kampfschiffe begleiten.</summary>
    public const string Convoy = "convoy";
}

/// <summary>
/// Angreifen (Spielerflotte): genau ein Ziel — ein Schiff (<paramref name="Ship"/>), eine Station (<paramref name="Station"/>) oder eine Tor-Hälfte
/// (<paramref name="Gate"/>). <paramref name="Sector"/> ist der Sektor, in dem das Ziel beim Befehl stand (ein Schiffsziel, das ihn verlässt, ist entkommen).
/// Außerhalb eines Kriegs ist der Angriff ein Verbrechen wie ein eigener Schuss.
/// </summary>
public sealed record AttackCommand(ShipId Ship, StationId Station, GateId Gate, SectorId Sector) : ShipCommand
{
    public override string Kind => CommandKinds.Attack;
}

/// <summary>Bergen: Wracks/Behälter im Sektor leeren (Fracht, mit Greifer auch Module), Beute beim besten Abnehmer verkaufen, wiederholen.</summary>
public sealed record SalvageCommand(SectorId Sector) : ShipCommand
{
    public override string Kind => CommandKinds.Salvage;
}

/// <summary>Tor blockieren: am Tor <paramref name="Gate"/> halten und Schiffe der Fraktion <paramref name="Faction"/> angreifen (nur im Krieg mit ihr).</summary>
public sealed record BlockadeCommand(GateId Gate, FactionId Faction) : ShipCommand
{
    public override string Kind => CommandKinds.Blockade;
}

/// <summary>
/// Liefern (Pendel): kauft an <paramref name="From"/> die Ware <paramref name="Good"/> (je Fahrt höchstens <paramref name="Amount"/> Einheiten, 0 = voller
/// Laderaum; Einkauf höchstens <paramref name="MaxBuyFactor"/> × Basispreis, 0 = frei) und liefert sie an <paramref name="To"/>; dann von vorn.
/// Nachtrag 1c: <paramref name="AllowLoss"/> false (Standard) = Verlustschutz wie die Handelsautomatik — an A wird nicht gekauft, wenn B je Einheit
/// nicht mehr zahlt, als der Einkauf an A kostet.
/// </summary>
public sealed record DeliverCommand(StationId From, StationId To, GoodId Good, float Amount, float MaxBuyFactor, bool AllowLoss = false) : ShipCommand
{
    public override string Kind => CommandKinds.Deliver;
}

/// <summary>
/// Sammelpunkt: zur Station <paramref name="At"/> fliegen (nicht andocken); sind alle Schiffe der Gruppe da, bildet <paramref name="Leader"/> den Verband.
/// Nachtrag 1c: <paramref name="Until"/> = Frist (Tick; 0 = keine, alte Befehle) — danach bilden die angekommenen Schiffe den Verband, die übrigen verlieren
/// ihren Sammelbefehl.
/// </summary>
public sealed record RallyCommand(StationId At, ShipId Leader, int Until = 0) : ShipCommand
{
    public override string Kind => CommandKinds.Rally;
}

/// <summary>Konvoi: Händler im Verband; der Anführer (langsamster Händler) handelt automatisch in den Grenzen, die übrigen Händler fahren seine Route mit.</summary>
public sealed record ConvoyCommand(AutoLimits Limits) : ShipCommand
{
    public override string Kind => CommandKinds.Convoy;
}

/// <summary>Deutsche Namen der Flottenbefehle (null = kein Flottenbefehl).</summary>
public static class FleetOpsNames
{
    public static string? Kind(string kind) => kind switch
    {
        CommandKinds.Attack => "Angreifen",
        CommandKinds.Salvage => "Bergen",
        CommandKinds.Blockade => "Tor blockieren",
        CommandKinds.Deliver => "Liefern",
        CommandKinds.Rally => "Sammelpunkt",
        CommandKinds.Convoy => "Konvoi",
        CommandKinds.ExploreSector => "Erkunden: Sektor",
        CommandKinds.ExploreUniverse => "Erkunden: Universum",
        _ => null,
    };
}
