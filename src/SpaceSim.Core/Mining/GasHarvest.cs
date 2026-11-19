using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Mining;

/// <summary>Zustand eines Gassammlers an einer Stelle (Anzeige der Bucht und Entscheidung der KI).</summary>
public enum GasState
{
    /// <summary>Kein Gassammler eingebaut (oder ausgefallen) bzw. Welt ohne Gas.</summary>
    NoTool,
    /// <summary>Kein Flüssigraum (Kanister/Universalraum) an Bord.</summary>
    NoHold,
    /// <summary>Außerhalb jedes Gasfelds.</summary>
    NoField,
    /// <summary>Flüssigraum voll.</summary>
    Full,
    /// <summary>Im Feld, Platz frei: es wird geschöpft.</summary>
    Collecting,
}

/// <summary>
/// Gasernte (SCHIFFSSLOTS.md „Gasernte und Nebel“): die eine Regel für Spieler und KI. Ein Schiff mit Gassammler (<see cref="ShipStats.GasCollect"/> Gas je
/// Sekunde) schöpft, solange es in einem Gasfeld steht, die Rohware <c>gas</c> in seinen Flüssigraum (Form liquid: Kanister oder Universalraum, Regel
/// <see cref="Ship.FreeUnits"/>). Rate × Felddichte, begrenzt durch den freien Raum; keine Zufallszahl, kein Verbrauch am Feld (Nebelgas strömt nach).
/// </summary>
public static class GasHarvest
{
    /// <summary>Die Gasware der Welt (Regel <see cref="EconomyRules.GasGoodKey"/>), null = keine.</summary>
    public static Good? GasGood(Universe u) => u.FindGood(u.Rules.GasGoodKey);

    /// <summary>Hat das Schiff einen arbeitsfähigen Gassammler?</summary>
    public static bool HasTool(Ship ship) => ship.Stats.GasCollect > 0f && ship.Has(Capability.GasHarvest);

    /// <summary>Zustand an der Position <paramref name="pos"/> im Sektor des Schiffs.</summary>
    public static GasState State(Universe u, Ship ship, Vec2 pos) => State(u, ship, pos, out _);

    static GasState State(Universe u, Ship ship, Vec2 pos, out GasField? field)
    {
        field = null;
        if (!HasTool(ship) || GasGood(u) is not { } gas) return GasState.NoTool;
        if (ship.CapacityVolumeFor(GoodForm.Liquid) <= 0f && ship.HasSeparatedHolds) return GasState.NoHold;
        if (!u.Sectors.TryGetValue(ship.Sector, out var sector) || (field = sector.GasFieldAt(pos)) is null) return GasState.NoField;
        if (ship.FreeUnits(u, gas.Id) < 0.01f) return GasState.Full;
        return GasState.Collecting;
    }

    /// <summary>Gas je Sekunde dieses Schiffs in diesem Feld.</summary>
    public static float Rate(Ship ship, GasField field) => ship.Stats.GasCollect * MathF.Max(0f, field.Density);

    /// <summary>
    /// Schöpft <paramref name="seconds"/> lang an <paramref name="pos"/>: lädt höchstens den freien Flüssigraum und veröffentlicht <see cref="GasCollected"/>.
    /// Liefert die geladene Menge (0 außerhalb eines Feldes, ohne Sammler oder mit vollem Raum).
    /// </summary>
    public static float Step(Universe u, Ship ship, Vec2 pos, float seconds)
    {
        if (seconds <= 0f || State(u, ship, pos, out var field) != GasState.Collecting || field is null) return 0f;
        var gas = GasGood(u)!;
        float units = MathF.Min(Rate(ship, field) * seconds, ship.FreeUnits(u, gas.Id));
        if (units <= 0f) return 0f;
        ship.Cargo[gas.Id] = ship.Cargo.GetValueOrDefault(gas.Id) + units;
        u.Events.Publish(new GasCollected(ship.Sector, ship.Id, gas.Id, units));
        return units;
    }

    /// <summary>Kurzer deutscher Zustandstext für die Bucht im Flug-HUD.</summary>
    public static string Text(GasState s) => s switch
    {
        GasState.NoTool => "kein Sammler",
        GasState.NoHold => "kein Kanister",
        GasState.NoField => "kein Gasfeld",
        GasState.Full => "Kanister voll",
        _ => "sammelt",
    };
}
