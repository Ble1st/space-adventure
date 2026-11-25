namespace SpaceSim.Core.Content;

/// <summary>
/// Träger (KAMPF.md „Träger und Bordjäger“): Inhalte, die nur mit <see cref="Model.EconomyRules.Carriers"/> in die Welt kommen — die Trägerklasse und das
/// Trägermodell sowie Startpakete mit diesem Modell. Mit abgeschaltetem Schalter nimmt <see cref="WorldBuilder.Build"/> sie heraus, damit die Welt bitgleich
/// zur Welt davor bleibt.
/// </summary>
public static class CarrierContent
{
    public const string ClassKey = "carrier", ModelKey = "carrier";

    /// <summary>Nimmt die Trägerinhalte aus der Weltbeschreibung (idempotent; ändert <paramref name="def"/>).</summary>
    public static void Strip(WorldDef def)
    {
        def.ShipModels.RemoveAll(m => m.Key == ModelKey || m.Class == ClassKey);
        def.ShipClasses.RemoveAll(c => c.Key == ClassKey);
        def.Player.StartOptions.RemoveAll(o => o.Model == ModelKey);
    }
}
