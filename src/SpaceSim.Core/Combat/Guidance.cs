using SpaceSim.Core.Content;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Raketenlenkung (KAMPF.md „Raketenlenkung und Gegenmaßnahmen“): reine Regeln je Lenkart eines Raketenwerfers. Alles hinter
/// <see cref="EconomyRules.MissileGuidance"/> (aus = Phase-28-Verhalten: jede Rakete gleich).
/// <list type="bullet">
/// <item><b>Optisch</b>: Start nur mit Sichtlinie (Ziel nicht getarnt, kein Nebel, im räumlichen Gefecht in <see cref="EconomyRules.OpticalArcDeg"/> vor dem Bug);
/// wird das Ziel während des Flugs unsichtbar (Tarnung), verfällt die Rakete. Billig.</item>
/// <item><b>Infrarot</b>: folgt der Wärme — Treffsicherheit × Wärmefaktor (Schubanteil, Antriebsmodus); Täuschkörper lenken sie mit <see cref="EconomyRules.FlareChance"/> ab.</item>
/// <item><b>Halbaktives Radar</b>: der Schütze muss das Ziel bis zum Einschlag in Reichweite und vor dem Bug halten; verliert er es oder fällt aus, verfällt die Rakete.</item>
/// <item><b>Aktives Radar</b>: abfeuern und vergessen; ein laufender Störsender lenkt sie mit <see cref="EconomyRules.JammerChance"/> ab.</item>
/// </list>
/// Zufallszüge je Raketenauflösung: höchstens einer für die Gegenmaßnahme (nur wenn eine passende läuft), dann wie bisher einer für die Punktabwehr
/// und zwei für <see cref="Hit.Roll"/> — höchstens vier. Start- und Zielerfassungsprüfungen ziehen nie.
/// </summary>
public static class Guidance
{
    /// <summary>Lenkung wirkt (Schalter an und Kriegsführung an).</summary>
    public static bool Active(Universe u) => u.Rules.MissileGuidance && u.Rules.Warfare;

    public static MissileGuidance Parse(string? key, string who) => key is null ? MissileGuidance.None
        : TryParse(key) is var g && g != MissileGuidance.None ? g
        : throw new WorldFormatException($"{who}: unbekannte guidance '{key}' (optical, infrared, radar_semi, radar_active)");

    public static MissileGuidance TryParse(string? key) => key?.ToLowerInvariant() switch
    {
        "optical" => MissileGuidance.Optical,
        "infrared" => MissileGuidance.Infrared,
        "radar_semi" => MissileGuidance.SemiActive,
        "radar_active" => MissileGuidance.ActiveRadar,
        _ => MissileGuidance.None,
    };

    public static string Key(MissileGuidance g) => g switch
    {
        MissileGuidance.Optical => "optical",
        MissileGuidance.Infrared => "infrared",
        MissileGuidance.SemiActive => "radar_semi",
        MissileGuidance.ActiveRadar => "radar_active",
        _ => "",
    };

    /// <summary>Anzeigename der Lenkart.</summary>
    public static string Name(MissileGuidance g) => g switch
    {
        MissileGuidance.Optical => "optisch",
        MissileGuidance.Infrared => "Infrarot",
        MissileGuidance.SemiActive => "Radar (halbaktiv)",
        MissileGuidance.ActiveRadar => "Radar (aktiv)",
        _ => "ungelenkt",
    };

    /// <summary>Wirksame Lenkart eines Moduls (Schalter aus = keine).</summary>
    public static MissileGuidance Of(Universe u, Module? m) => m is null || !Active(u) ? MissileGuidance.None : m.Guidance;

    /// <summary>Ist das Ziel für eine optische Rakete unsichtbar (Tarnung an oder Nebelsektor)?</summary>
    public static bool Hidden(Universe u, Ship target) =>
        (target.Cloaked && target.Stats.Cloak > 0f) || (u.Sectors.TryGetValue(target.Sector, out var s) && s.Nebula);

    /// <summary>Liegt <paramref name="to"/> innerhalb des halben Winkels <paramref name="halfDeg"/> vor <paramref name="dir"/> (von <paramref name="from"/>)?</summary>
    public static bool InFront(Vec2 from, Vec2 dir, Vec2 to, float halfDeg)
    {
        var d = to - from;
        float len = d.Length, dl = dir.Length;
        if (len < 1e-4f || dl < 1e-4f) return true;
        float cos = (d.X * dir.X + d.Y * dir.Y) / (len * dl);
        return cos >= MathF.Cos(halfDeg * MathF.PI / 180f) - 1e-5f;
    }

    /// <summary>
    /// Startbedingung (rein, kein Zug): null = darf starten, sonst Grund. Optisch braucht Sichtkontakt (ein Schiffsziel nicht verborgen, im räumlichen Gefecht vor dem Bug);
    /// in einem Nebelsektor startet keine optische Rakete. Andere Lenkarten haben keine Startbedingung.
    /// </summary>
    public static string? LaunchIssue(Universe u, Encounter e, CombatParticipant shooter, Module m, Ship? target, Vec2 targetPos) =>
        LaunchIssue(u, e.Sector, m, target, e.Spatial && shooter.InSpace, shooter.Pos, shooter.Dir, targetPos);

    /// <summary>Startbedingung mit ausdrücklicher Position und Blickrichtung (Spieler: Bug aus der Ansicht).</summary>
    public static string? LaunchIssue(Universe u, SectorId sector, Module m, Ship? target, bool checkArc, Vec2 from, Vec2 dir, Vec2 targetPos)
    {
        if (Of(u, m) != MissileGuidance.Optical) return null;
        if (u.Sectors.TryGetValue(sector, out var s) && s.Nebula) return "Nebel – kein Sichtkontakt";
        if (target is not null && Hidden(u, target)) return "Ziel getarnt – kein Sichtkontakt";
        if (checkArc && !InFront(from, dir, targetPos, u.Rules.OpticalArcDeg)) return "Ziel nicht vor dem Bug";
        return null;
    }

    /// <summary>
    /// Halbaktives Radar: hält der Schütze die Zielerfassung? (rein) Schiffsschütze: aktiv im Gefecht, Ziel in Waffenreichweite und — räumlich — in
    /// <see cref="EconomyRules.SemiActiveArcDeg"/> vor dem Bug. Station als Schütze: besteht noch und ist nicht ausgeschaltet. Andere Lenkarten: immer.
    /// </summary>
    public static bool HoldsLock(Universe u, Encounter e, MissileFlight f, Module m)
    {
        if (Of(u, m) != MissileGuidance.SemiActive) return true;
        if (f.FromStation.Value != 0)
            return u.Stations.TryGetValue(f.FromStation, out var st) && st.Sector == e.Sector && st.Condition is not { Disabled: true };
        if (e.Of(f.Shooter) is not { Active: true } sp || !u.Ships.ContainsKey(f.Shooter)) return false;
        float range = m.Stat("range");
        if (!f.Siege.IsNone)
        {
            if (SiegeTargets.Resolve(u, f.Siege) is not { } st2 || st2.IsDown) return false;
            return SiegeTargets.Distance(u, e, sp, st2) <= range
                && (!e.Spatial || !sp.InSpace || InFront(sp.Pos, sp.Dir, st2.Position, u.Rules.SemiActiveArcDeg));
        }
        if (e.Of(f.Target) is not { Active: true } tp) return false;
        if (CombatSense.Distance(sp, tp) > range) return false;
        return !e.Spatial || !sp.InSpace || !tp.InSpace || InFront(sp.Pos, sp.Dir, tp.Pos, u.Rules.SemiActiveArcDeg);
    }

    /// <summary>
    /// Infrarot: Wärmefaktor des Ziels (rein) = <see cref="EconomyRules.HeatMin"/> + (1 − HeatMin) × Tempoanteil, + <see cref="EconomyRules.HeatEngineBonus"/> im
    /// Antriebsmodus; höchstens 1 + Bonus. Ein stillstehendes, kaltes Schiff ist schwerer zu treffen.
    /// </summary>
    public static float HeatFactor(Universe u, Ship target, float speed)
    {
        var r = u.Rules;
        float max = MathF.Max(0.1f, target.Stats.Speed > 0f ? target.Stats.Speed : 8f);
        float share = Math.Clamp(speed / max, 0f, 1f);
        float heat = r.HeatMin + (1f - r.HeatMin) * share;
        if (target.PowerMode == PowerMode.Engine) heat += r.HeatEngineBonus;
        return Math.Clamp(heat, 0.05f, 1f + r.HeatEngineBonus);
    }
}
