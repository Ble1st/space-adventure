using SpaceSim.Core.Mining;

namespace SpaceSim.Core.Weapons;

/// <summary>Ausrichtung eines Waffenschachts am Schiff (siehe SCHIFFSSLOTS.md: Bug, Backbord, Steuerbord, Heck).</summary>
public enum Facing { Bow, Port, Starboard, Stern }

/// <summary>
/// Datensatz eines Waffenschachts: Ausrichtung <see cref="Facing"/>, halber Öffnungswinkel <see cref="ArcDegrees"/>
/// (>= 360 = Turm mit Vollkreis) und Reichweite. Reine Daten, die Winkelprüfung liegt in <see cref="FiringArc"/>.
/// </summary>
public sealed class WeaponBay
{
    public string Key = "";
    public string Name = "";
    public Facing Facing = Facing.Bow;
    /// <summary>Halber Öffnungswinkel in Grad; 360 oder mehr bedeutet Turm (jede Richtung liegt im Winkel).</summary>
    public float ArcDegrees = 360f;
    /// <summary>Reichweite in Einheiten (Abstand Schiff–Ziel minus Zielradius, siehe <see cref="FiringArc.State"/>).</summary>
    public float Range;

    public bool IsTurret => ArcDegrees >= 360f;
}

/// <summary>Anzeigbarer Zustand einer Waffenschacht-Taste (UI.md Abschnitt 2).</summary>
public enum BayState
{
    /// <summary>Ziel im Winkel und Waffe bereit – feuert sofort (grün, „FEUER“).</summary>
    Ready,
    /// <summary>Ziel in Reichweite, aber außerhalb des Winkels – gesperrt mit Drehhinweis (orange).</summary>
    OutsideArc,
    /// <summary>Lädt nach dem Schuss nach – grau mit Balken.</summary>
    Reloading,
    /// <summary>Kein Ziel in Reichweite (gedämpft).</summary>
    NoTarget,
}

/// <summary>Standard-Schächte des Spielerschiffs. Der Mining-Laser sitzt als 360°-Turm, damit sich das Spielverhalten nicht ändert.</summary>
public static class WeaponBays
{
    public static IReadOnlyList<WeaponBay> PlayerDefault { get; } = new[]
    {
        new WeaponBay { Key = "laser", Name = "Laser", Facing = Facing.Bow, ArcDegrees = 360f, Range = MiningRules.LaserRange },
        new WeaponBay { Key = "tractor", Name = "Traktor", Facing = Facing.Bow, ArcDegrees = 360f, Range = MiningRules.TractorRange },
    };

    /// <summary>Phase 8: Spaltlaser (Asteroidenbrecher) als eigener Schacht, Reichweite wie der Mining-Laser.</summary>
    public static WeaponBay Splitter { get; } = new() { Key = "split", Name = "Spalter", Facing = Facing.Bow, ArcDegrees = 360f, Range = MiningRules.LaserRange };

    /// <summary>Nachladezeit des Spaltlasers in Sekunden (Spielerschacht).</summary>
    public const float SplitReloadSeconds = 2f;

    /// <summary>Schächte eines Schiffs: Laser und Traktor wie bisher, dazu der Spalter, wenn der Fit einen Spaltlaser hat.</summary>
    public static IReadOnlyList<WeaponBay> For(SpaceSim.Core.Model.ShipStats stats) =>
        stats.SplitPower > 0f ? PlayerDefault.Append(Splitter).ToList() : PlayerDefault;
}
