using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Flight;

/// <summary>
/// Tarnfeld (SCHIFFSSLOTS.md Abschnitt 6, Fähigkeit <see cref="Capability.Stealth"/>): ein Utility-Modul, das eingeschaltet die Entdeckung erschwert —
/// kein Unsichtbarkeitsmantel für den Kampf. Eine Regel, rein und ohne Zufall:
/// <list type="bullet">
/// <item><b>Wirkung:</b> Tarnstärke (<see cref="ShipStats.Cloak"/>) gegen die Sensorstärke des Entdeckers: Kontrollpunkte am Gate
/// (<see cref="EconomyRules.GateSensorStrength"/>, über das Mobilitätsprofil und <c>DetectionCheck</c>), Sprung-Ankunft
/// (<see cref="EconomyRules.JumpSensorStrength"/>), Kaperer bei der Opferwahl (<see cref="EconomyRules.RaiderSensorStrength"/>), Polizei bei Festnahme
/// und Kriegsangriff (<see cref="EconomyRules.PoliceSensorStrength"/>), Kleinstationen (<see cref="EconomyRules.StationSensorStrength"/>). Stärker getarnt
/// = unentdeckt (<see cref="HiddenFrom"/>).</item>
/// <item><b>Kosten:</b> eingeschaltet zieht es <see cref="ShipStats.CloakDraw"/> aus dem Reaktorüberschuss (über <see cref="Ship.RecomputeStats"/>: Schild,
/// Bank und Antrieb bekommen weniger); einschalten nur mit genug Überschuss.</item>
/// <item><b>Bricht</b> (<see cref="Break"/>): eigener Schuss oder Abbau (Laser, Spalter, Bohrer), Andocken, Gefechtsbeginn, Hüllentreffer, Modulausfall,
/// Schiffswechsel.</item>
/// <item><b>Nicht einschaltbar</b> (<see cref="ToggleIssue"/>): angedockt, im Gefecht, solange eine Warnung (Überfall, Festnahme, Krieg, Plattform) auf das
/// Schiff läuft, ohne Leistung.</item>
/// </list>
/// Zustand <see cref="Ship.Cloaked"/>, gespeichert und gehasht nur wenn gesetzt. Nur der Spieler nutzt es (die KI nicht, siehe SCHIFFSSLOTS.md).
/// </summary>
public static class Cloak
{
    /// <summary>Wirksame Tarnstärke (0, wenn aus).</summary>
    public static float ActiveStrength(Ship ship) => ship.Cloaked ? ship.Stats.Cloak : 0f;

    /// <summary>Wirksame Tarnstärke mit Nebelzuschlag des aktuellen Sektors (0, wenn aus).</summary>
    public static float ActiveStrength(Universe u, Ship ship) => ship.Cloaked ? ship.Stats.Cloak + Simulation.Nebula.CloakBonus(u, ship.Sector) : 0f;

    /// <summary>Ist das Schiff vor einem Entdecker dieser Sensorstärke verborgen?</summary>
    public static bool HiddenFrom(Ship ship, float sensor) => ship.Cloaked && ship.Stats.Cloak > sensor;

    /// <summary>Wie <see cref="HiddenFrom(Ship, float)"/>, mit dem Nebelzuschlag des Sektors (<see cref="Simulation.Nebula.CloakBonus"/>): im Nebel wirkt die Tarnung stärker.</summary>
    public static bool HiddenFrom(Universe u, Ship ship, float sensor) => ship.Cloaked && ship.Stats.Cloak + Simulation.Nebula.CloakBonus(u, ship.Sector) > sensor;

    /// <summary>Kann das Schiff ein Tarnfeld überhaupt schalten (Modul eingebaut und funktionsfähig, Schalter an)?</summary>
    public static bool Fitted(Universe u, Ship ship) => u.Rules.Cloaking && ship.HasModel && ship.Stats.Cloak > 0f;

    /// <summary>Warum lässt sich die Tarnung jetzt nicht einschalten? null = möglich.</summary>
    public static string? ToggleIssue(Universe u, Ship ship)
    {
        if (!u.Rules.Cloaking) return "Tarnfeld abgeschaltet";
        if (!ship.HasModel || ship.Stats.Cloak <= 0f) return "kein Tarnfeld eingebaut";
        if (ship.DockedAt.Value != 0) return "angedockt";
        if (ship.Transit is not null) return "unterwegs";
        if (u.Fights.IsEngaged(ship.Id)) return "im Gefecht";
        if (u.PendingEncounters.Any(e => e.Target == ship.Id)) return "Warnung läuft";
        float surplus = ship.Stats.PowerOutput - ship.Stats.PowerDraw;
        if (surplus + 1e-4f < ship.Stats.CloakDraw) return $"zu wenig Leistung ({MathF.Max(0f, surplus):0.#} von {ship.Stats.CloakDraw:0.#})";
        return null;
    }

    /// <summary>Tarnung einschalten (prüft <see cref="ToggleIssue"/>); liefert den Grund der Ablehnung oder null.</summary>
    public static string? TurnOn(Universe u, Ship ship)
    {
        if (ship.Cloaked) return null;
        if (ToggleIssue(u, ship) is { } issue) return issue;
        ship.Cloaked = true;
        ship.RecomputeStats(u);
        u.Events.Publish(new CloakChanged(ship.Id, true, "on"));
        return null;
    }

    /// <summary>Tarnung brechen bzw. ausschalten (nichts, wenn aus). Grund: off, fired, docked, mining, combat, hit, module, power, ship.</summary>
    public static void Break(Universe u, Ship ship, string reason)
    {
        if (!ship.Cloaked) return;
        ship.Cloaked = false;
        ship.RecomputeStats(u);
        u.Events.Publish(new CloakChanged(ship.Id, false, reason));
    }

    /// <summary>Kurzer deutscher Grund für die Anzeige.</summary>
    public static string ReasonText(string reason) => reason switch
    {
        "off" => "ausgeschaltet",
        "fired" => "Schuss",
        "docked" => "angedockt",
        "mining" => "Abbau",
        "combat" => "Gefecht",
        "hit" => "Treffer",
        "module" => "Modul aus",
        "power" => "Leistung fehlt",
        "ship" => "Schiffswechsel",
        "jump" => "Sprung",
        _ => reason,
    };

    /// <summary>
    /// Laufende Prüfung (je Tick aus <see cref="Simulation.JumpCloakSystem"/>): bricht die Tarnung, wenn das Schiff angedockt, verankert (Abbau), im Gefecht,
    /// ohne Modul oder ohne Leistung ist oder nicht mehr das aktive Spielerschiff.
    /// </summary>
    public static void Check(Universe u, Ship ship)
    {
        if (!ship.Cloaked) return;
        string? why = !u.Rules.Cloaking || ship.Stats.Cloak <= 0f ? "module"
            : ship.IsPlayer && ship.Id != u.PlayerShip ? "ship"   // Ausbau Punkt 5: KI-Schmuggler tarnen sich selbst
            : ship.DockedAt.Value != 0 ? "docked"
            : u.Fights.IsEngaged(ship.Id) ? "combat"
            : ship.AnchorRock != 0 ? "mining"
            : ship.Stats.PowerOutput + 1e-4f < ship.Stats.PowerDraw ? "power"
            : null;
        if (why is not null) Break(u, ship, why);
    }
}
