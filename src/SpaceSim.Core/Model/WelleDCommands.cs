namespace SpaceSim.Core.Model;

/// <summary>Welle D: Schlüssel des Versorgungsbefehls.</summary>
public static partial class CommandKinds
{
    /// <summary>Versorgen: ein Tanker füllt Tank, Magazin und Raketenschacht eigener Schiffe aus seinem Laderaum nach.</summary>
    public const string Supply = "supply";
}

/// <summary>
/// Welle D (Punkt 1): Versorgen. Das Schiff versorgt die eigenen Schiffe seines Verbands (es folgt dem Anführer) bzw. — ohne Verband — die eigenen Schiffe im
/// Sektor <paramref name="Sector"/> aus seinem Laderaum (Treibstoff in den Tank, Munition ins Magazin, Raketen in den Raketenschacht). Fehlt ihm, was die
/// Flotte braucht, holt es es an der nächsten Station, die es verkauft (Spielerkasse, Marktpreis), und kehrt zurück.
/// </summary>
public sealed record SupplyCommand(SectorId Sector) : ShipCommand
{
    public override string Kind => CommandKinds.Supply;
}
