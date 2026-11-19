using System.Globalization;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Flight;

/// <summary>Eine Tankzeile: ein Treibstoff, den die Antriebe des Schiffs annehmen, mit Inhalt und Restreichweite.</summary>
/// <param name="Range">Restreichweite in Welteinheiten (m) bei der jetzigen Verbrauchsrate; +∞ ohne Verbrauch.</param>
/// <param name="Jumps">Restreichweite grob in Gate-Sprüngen (<see cref="TankInfo.UnitsPerJump"/> Strecke je Sprung).</param>
public sealed record TankLine(GoodId Good, string Name, string Short, float Amount, float Capacity, float Efficiency, float Range, float Jumps, bool Low, bool Active);

/// <summary>
/// Tankanzeige (rein): je Treibstoffart, die ein eingebauter Antrieb verbrennt (<see cref="ShipStats.FuelTypes"/>: Treibstoff, Ionengas, Fusionsbrennstoff), Inhalt,
/// Tankgröße und geschätzte Restreichweite (Inhalt × Wirkungsgrad ÷ Verbrauch je Einheit, dieselbe Regel wie <see cref="Refuel.Consumption"/>). Der Tank ist
/// einer für alle Arten (<see cref="ShipStats.FuelCapacity"/>); „knapp“ unter <see cref="LowShare"/> der Tankgröße.
/// </summary>
public static class TankInfo
{
    /// <summary>Knapp unter diesem Anteil der Tankgröße (rote Warnung).</summary>
    public const float LowShare = 0.2f;
    /// <summary>Strecke im Sektor je Gate-Sprung für die grobe Sprung-Schätzung (gemessen: Drei-Sektoren-Flug ≈ 2 500 m).</summary>
    public const float UnitsPerJump = 850f;
    static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Kurzname einer Treibstoffart für knappe Anzeigen.</summary>
    public static string ShortName(Good g) => g.Key switch
    {
        "propellant" => "Treibst.",
        "ion_gas" => "Ionengas",
        "fusion_fuel" => "Fusion",
        _ => g.Name.Length <= 9 ? g.Name : g.Name[..8] + ".",
    };

    /// <summary>Zeilen in der Reihenfolge der Antriebe (zuerst die verbrannte Art); leer ohne Treibstoffantrieb oder mit Schalter aus.</summary>
    public static IReadOnlyList<TankLine> For(Universe u, Ship ship)
    {
        var list = new List<TankLine>();
        if (!u.Rules.FuelActive || !ship.HasModel) return list;
        var active = Refuel.ActiveFuel(ship);
        float cap = ship.Stats.FuelCapacity;
        foreach (var f in ship.Stats.FuelTypes.OrderByDescending(f => active is { } a && a == f.Good))
        {
            if (!u.Goods.TryGetValue(f.Good, out var g)) continue;
            float amount = ship.FuelAmount(f.Good);
            float perUnit = ship.Stats.FuelUsePerUnit / MathF.Max(0.0001f, f.Efficiency);
            float range = perUnit > 0f ? amount / perUnit : float.PositiveInfinity;
            list.Add(new TankLine(f.Good, g.Name, ShortName(g), amount, cap, f.Efficiency, range,
                float.IsPositiveInfinity(range) ? float.PositiveInfinity : range / UnitsPerJump, cap > 0f && amount < LowShare * cap, active == f.Good));
        }
        return list;
    }

    /// <summary>Reichweitentext „~42 km · ≈ 49 Spr.“ (ohne Verbrauch „unbegrenzt“).</summary>
    public static string RangeText(TankLine l) => float.IsPositiveInfinity(l.Range) ? "unbegrenzt"
        : $"~{DistanceText.Format(l.Range)} · ≈{(l.Jumps >= 1000f ? "999+" : MathF.Floor(l.Jumps).ToString("0", De))} Spr.";

    /// <summary>Kurze Zeile „Ionengas 50/200 · ~42 km · ≈ 49 Spr.“.</summary>
    public static string Line(TankLine l) => $"{l.Name} {l.Amount:0}/{l.Capacity:0} · {RangeText(l)}";

    /// <summary>Knappe Zeile für das Flug-HUD: „42 km“ bzw. „999+ km“.</summary>
    public static string HudValue(TankLine l) => float.IsPositiveInfinity(l.Range) ? $"{l.Amount:0}" : $"{l.Amount:0} · {DistanceText.Format(l.Range)}";

    /// <summary>Infozeile des Flug-HUD: „120/200 · ~42 km · ≈49 Spr.“ für die verbrannte Art, weitere Arten kurz dahinter („+ Treibst. 30“).</summary>
    public static string HudLine(Universe u, Ship ship)
    {
        var l = For(u, ship);
        if (l.Count == 0) return "";
        var first = $"{l[0].Amount:0}/{l[0].Capacity:0} · {RangeText(l[0])}";
        return l.Count == 1 ? first : first + string.Concat(l.Skip(1).Select(x => $" + {x.Short} {x.Amount:0}"));
    }

    /// <summary>Reichweite eines vollen Tanks mit den Werten <paramref name="s"/> (beste verbrannte Art); +∞ ohne Verbrauch, 0 ohne Tank/Antrieb.</summary>
    public static float FullRange(ShipStats s)
    {
        if (s.FuelCapacity <= 0f || s.FuelTypes.Count == 0) return 0f;
        if (s.FuelUsePerUnit <= 0f) return float.PositiveInfinity;
        return s.FuelCapacity * s.FuelTypes.Max(f => f.Efficiency) / s.FuelUsePerUnit;
    }

    /// <summary>Kurznamen der Treibstoffarten, die die Antriebe mit diesen Werten verbrennen („Treibst.“, „Ionengas / Fusion“).</summary>
    public static string FuelNames(Universe u, ShipStats s) =>
        string.Join(" / ", s.FuelTypes.Where(f => u.Goods.ContainsKey(f.Good)).Select(f => ShortName(u.Goods[f.Good])));

    /// <summary>Kurze Zeile „Treibst. 120/200 (~42 km)“ für Listen (Hangar, Flotte); leer ohne Zeile.</summary>
    public static string ShortLine(Universe u, Ship ship)
    {
        var l = For(u, ship);
        if (l.Count == 0) return "";
        return $"{l[0].Short} {l[0].Amount:0}/{l[0].Capacity:0}" + (float.IsPositiveInfinity(l[0].Range) ? "" : $" (~{DistanceText.Format(l[0].Range)})");
    }

    /// <summary>Nächste erreichbare Station, die die verbrannte Art des Schiffs verkauft (Lager &gt; 0), sonst null.</summary>
    public static Station? NearestSeller(Universe u, Ship ship)
    {
        if (Refuel.ActiveFuel(ship) is not { } fuel) return null;
        Station? best = null; float bestT = float.PositiveInfinity;
        foreach (var st in Refuel.FuelStations(u, fuel))
        {
            float t = Travel.Estimate(u, ship.Sector, ship.Position, st.Sector, st.DockPoint(ship.Id), ship);
            if (t < bestT) { bestT = t; best = st; }
        }
        return best;
    }

    /// <summary>Alle Zeilen als Text (Mehrfachantriebe getrennt durch „ / “); „kein Treibstoffantrieb“ ohne Zeile.</summary>
    public static string Summary(Universe u, Ship ship)
    {
        var l = For(u, ship);
        return l.Count == 0 ? "kein Treibstoffantrieb" : string.Join(" / ", l.Select(Line));
    }
}
