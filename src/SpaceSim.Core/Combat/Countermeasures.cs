using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>Raketenwarnung: eine Rakete fliegt auf ein Schiff zu — Lenkart, Restzeit, Richtung (Weltebene, vom Ziel zur Rakete; 0 = unbekannt).</summary>
public readonly record struct IncomingMissile(int Encounter, MissileGuidance Guidance, float Seconds, Vec2 From, ShipId Shooter, string Module);

/// <summary>
/// Gegenmaßnahmen gegen gelenkte Raketen (KAMPF.md „Raketenlenkung und Gegenmaßnahmen“), reine deterministische Regeln:
/// <list type="bullet">
/// <item><b>Täuschkörper</b> (Modulwert <c>flareCapacity</c>, eigenes Magazin <see cref="Ship.FlaresMissing"/>, Ware der Kategorie <see cref="FlareCategory"/>,
/// nachladbar): ein Ausstoß kostet einen Täuschkörper und wirkt <see cref="EconomyRules.FlareTicks"/>; nur gegen Infrarot.</item>
/// <item><b>Störsender</b> (<c>jamming</c>, <c>jamDraw</c>): Umschalter, kostet Leistung solange er läuft; nur gegen aktives Radar.</item>
/// </list>
/// Je Auflösung höchstens ein Zufallszug für die Gegenmaßnahme (<see cref="Defeats"/>). NPCs (und Flottenschiffe des Spielers) reagieren in
/// <see cref="React"/> nach festen Regeln ohne Zufall; das aktive Spielerschiff steuert die Person.
/// </summary>
public static class Countermeasures
{
    /// <summary>Warenkategorie der Täuschkörper.</summary>
    public const string FlareCategory = "flare";

    public static Good? FlareGood(Universe u)
    {
        Good? best = null;
        foreach (var g in u.Goods.Values)
            if (g.Category == FlareCategory && (best is null || g.Id.Value < best.Id.Value)) best = g;
        return best;
    }

    /// <summary>Ist das Modul mit dieser Kennzahl eingebaut und nicht ausgefallen?</summary>
    static bool Intact(Universe u, Ship ship, string stat)
    {
        for (int i = 0; i < ship.Fit.Count; i++)
            if (ship.Fit[i] is { } k && u.Modules.TryGetValue(k, out var m) && m.Stat(stat) > 0f && ship.ConditionOf(i) >= u.Rules.ModuleFailCondition) return true;
        return false;
    }

    public static bool HasFlares(Ship ship) => ship.Stats.FlareCapacity > 0f;
    public static bool HasJammer(Ship ship) => ship.Stats.Jamming > 0f;

    /// <summary>Täuschkörper an Bord (mit abgeschalteter Munition: voll).</summary>
    public static float FlaresLoaded(Universe u, Ship ship) =>
        !u.Rules.AmmoActive ? ship.Stats.FlareCapacity : MathF.Max(0f, ship.Stats.FlareCapacity - ship.FlaresMissing);

    public static float FlaresMissingNow(Universe u, Ship ship) => u.Rules.AmmoActive ? MathF.Min(ship.FlaresMissing, ship.Stats.FlareCapacity) : 0f;

    public static bool FlaresActive(Universe u, Ship ship) => u.Tick < ship.FlaresUntil;

    /// <summary>Warum kein Täuschkörper ausgestoßen werden kann (null = geht).</summary>
    public static string? FlareIssue(Universe u, Ship ship)
    {
        if (!Guidance.Active(u)) return "Raketenlenkung aus";
        if (!HasFlares(ship)) return "kein Täuschkörperwerfer";
        if (!Intact(u, ship, "flareCapacity")) return "Werfer ausgefallen";
        if (ship.DockedAt.Value != 0) return "angedockt";
        if (FlaresActive(u, ship)) return "wirken noch";
        if (FlaresLoaded(u, ship) < 1f - 1e-4f) return "keine Täuschkörper";
        return null;
    }

    /// <summary>Täuschkörper ausstoßen (ein Stück aus dem Magazin, wirkt <see cref="EconomyRules.FlareTicks"/>). Kein Zufallszug. Liefert den Grund, wenn es nicht geht.</summary>
    public static string? FireFlares(Universe u, Ship ship)
    {
        if (FlareIssue(u, ship) is { } issue) return issue;
        if (u.Rules.AmmoActive) ship.FlaresMissing = MathF.Min(ship.Stats.FlareCapacity, ship.FlaresMissing + 1f);
        ship.FlaresUntil = u.Tick + Math.Max(1, u.Rules.FlareTicks);
        u.Events.Publish(new CountermeasureUsed(ship.Id, ship.Sector, "flares", true));
        return null;
    }

    /// <summary>Wirkt der Störsender gerade (an, Modul heil, Reaktor deckt die Last)?</summary>
    public static bool JammerWorks(Universe u, Ship ship) =>
        ship.JammerOn && HasJammer(ship) && Intact(u, ship, "jamming") && ship.Stats.PowerOutput + 1e-4f >= ship.Stats.PowerDraw;

    /// <summary>Warum der Störsender nicht eingeschaltet werden kann (null = geht).</summary>
    public static string? JammerIssue(Universe u, Ship ship)
    {
        if (!Guidance.Active(u)) return "Raketenlenkung aus";
        if (!HasJammer(ship)) return "kein Störsender";
        if (!Intact(u, ship, "jamming")) return "Störsender ausgefallen";
        if (ship.DockedAt.Value != 0) return "angedockt";
        if (ShipSystems.Surplus(ship.Stats) + 1e-4f < ship.Stats.JamDraw) return "zu wenig Leistung";
        return null;
    }

    /// <summary>Störsender schalten. Einschalten nur ohne <see cref="JammerIssue"/>; Ausschalten immer. Liefert den Grund, wenn es nicht geht.</summary>
    public static string? SetJammer(Universe u, Ship ship, bool on)
    {
        if (on == ship.JammerOn) return null;
        if (on && JammerIssue(u, ship) is { } issue) return issue;
        ship.JammerOn = on;
        ship.RecomputeStats(u);
        u.Events.Publish(new CountermeasureUsed(ship.Id, ship.Sector, "jammer", on));
        return null;
    }

    /// <summary>
    /// Gegenmaßnahme bei der Auflösung (höchstens ein Zug): Infrarot gegen aktive Täuschkörper (<see cref="EconomyRules.FlareChance"/>), aktives Radar gegen einen
    /// laufenden Störsender (<see cref="EconomyRules.JammerChance"/> × Störstärke, höchstens 1). Liefert „decoyed“/„jammed“ oder null (kein Zug, wenn keine passende
    /// Gegenmaßnahme läuft). Optisch: verborgenes Ziel → „blind“ ohne Zug.
    /// </summary>
    public static string? Defeats(Universe u, MissileGuidance g, Ship target)
    {
        var r = u.Rules;
        switch (g)
        {
            case MissileGuidance.Optical:
                return Guidance.Hidden(u, target) ? "blind" : null;
            case MissileGuidance.Infrared:
                if (!FlaresActive(u, target)) return null;
                return u.Rng.NextDouble() < Math.Clamp(r.FlareChance, 0f, 1f) ? "decoyed" : null;
            case MissileGuidance.ActiveRadar:
                if (!JammerWorks(u, target)) return null;
                return u.Rng.NextDouble() < Math.Clamp(r.JammerChance * MathF.Min(1f, target.Stats.Jamming), 0f, 1f) ? "jammed" : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Feste NPC-Regeln je Gefechtstakt (kein Zufall, Raketen in Abschussreihenfolge): Täuschkörper, wenn eine Infrarotrakete höchstens
    /// <see cref="EconomyRules.NpcFlareLeadTicks"/> vor dem Einschlag ist; Störsender an, solange eine aktive Radarrakete anfliegt, sonst aus.
    /// Das aktive Spielerschiff entscheidet selbst.
    /// </summary>
    public static void React(Universe u, Encounter e)
    {
        if (!Guidance.Active(u)) return;
        foreach (var f in e.Missiles)
        {
            if (!f.Siege.IsNone || f.Target == u.PlayerShip || !u.Ships.TryGetValue(f.Target, out var t)) continue;
            if (!u.Modules.TryGetValue(f.Module, out var m)) continue;
            var g = Guidance.Of(u, m);
            if (g == MissileGuidance.Infrared && f.Left / MathF.Max(1f, f.Speed) * SimClock.TicksPerSecond <= u.Rules.NpcFlareLeadTicks && FlareIssue(u, t) is null)
                FireFlares(u, t);
            else if (g == MissileGuidance.ActiveRadar && !t.JammerOn && JammerIssue(u, t) is null)
                SetJammer(u, t, true);
        }
        foreach (var p in e.Participants)
        {
            if (!p.Active || p.Ship == u.PlayerShip || !u.Ships.TryGetValue(p.Ship, out var s) || !s.JammerOn) continue;
            bool radar = false;
            foreach (var f in e.Missiles)
                if (f.Target == s.Id && u.Modules.TryGetValue(f.Module, out var m) && Guidance.Of(u, m) == MissileGuidance.ActiveRadar) { radar = true; break; }
            if (!radar) SetJammer(u, s, false);
        }
    }

    /// <summary>Beim Verlassen eines Gefechts: der Störsender eines NPC (nicht des aktiven Spielerschiffs) geht aus.</summary>
    public static void OnLeave(Universe u, Ship ship)
    {
        if (ship.JammerOn && ship.Id != u.PlayerShip) SetJammer(u, ship, false);
    }

    /// <summary>Anfliegende Raketen auf ein Schiff (Warnung), nach Restzeit sortiert. Rein.</summary>
    public static List<IncomingMissile> Incoming(Universe u, Ship ship)
    {
        var list = new List<IncomingMissile>();
        if (u.Fights.Of(ship.Id) is not { } e) return list;
        foreach (var f in e.Missiles)
        {
            if (f.Target != ship.Id || !f.Siege.IsNone) continue;
            u.Modules.TryGetValue(f.Module, out var m);
            float secs = f.Left / MathF.Max(1f, f.Speed);
            Vec2 at = e.Spatial ? Missiles.PositionOf(u, e, f) : u.Ships.TryGetValue(f.Shooter, out var sh) ? sh.Position : f.From;
            Vec2 dir = at - ship.Position;
            list.Add(new IncomingMissile(e.Id, Guidance.Of(u, m), secs, dir.Length > 1e-3f ? dir.Normalized() : default, f.Shooter, f.Module));
        }
        list.Sort((a, b) => a.Seconds.CompareTo(b.Seconds));
        return list;
    }

    /// <summary>Kurze Warnzeile: „RAKETE! Infrarot · 2,4 s“ (+ weitere). Richtung macht die Ansicht.</summary>
    public static string WarningText(IReadOnlyList<IncomingMissile> list, string? direction = null)
    {
        if (list.Count == 0) return "";
        var first = list[0];
        string more = list.Count > 1 ? $" (+{list.Count - 1})" : "";
        string dir = direction is { Length: > 0 } ? $" · {direction}" : "";
        string hint = first.Guidance switch
        {
            MissileGuidance.Infrared => " — Täuschkörper!",
            MissileGuidance.ActiveRadar => " — Störsender!",
            MissileGuidance.SemiActive => " — Schütze aus der Reichweite bringen",
            MissileGuidance.Optical => " — tarnen oder wegdrehen",
            _ => "",
        };
        return $"RAKETE{more}! {Guidance.Name(first.Guidance)} · {first.Seconds:0.0} s{dir}{hint}";
    }

    /// <summary>Richtung der Rakete relativ zur Schiffsnase (Peilung <paramref name="headingDeg"/>, wie <see cref="Weapons.FiringArc.Bearing"/>); leer = unbekannt.</summary>
    public static string DirectionText(in IncomingMissile m, float headingDeg)
    {
        if (m.From.Length < 1e-3f) return "";
        float rel = Weapons.FiringArc.Normalize180(Weapons.FiringArc.Bearing(m.From) - headingDeg);
        return rel > 135f || rel < -135f ? "▼ von hinten" : rel > 45f ? "◀ von links" : rel < -45f ? "▶ von rechts" : "▲ von vorn";
    }

    /// <summary>Täuschkörper aus dem Stationslager nachladen (Marktpreis). Liefert die Einheiten.</summary>
    public static float AtStation(Universe u, Ship ship, Station st, ICreditAccount payer, float maxUnits = float.MaxValue)
    {
        float missing = MathF.Min(FlaresMissingNow(u, ship), MathF.Max(0f, maxUnits));
        if (missing <= 0f || FlareGood(u) is not { } good || !st.Market.TryGetValue(good.Id, out var entry) || entry.Amount < 1f) return 0f;
        float price = entry.Price > 0f ? entry.Price : good.BasePrice;
        float units = MathF.Floor(MathF.Min(missing, entry.Amount));
        if (price > 0f) units = MathF.Min(units, MathF.Floor(MathF.Max(0f, payer.Credits) / price));
        if (units < 1f) return 0f;
        if (Trading.PlayerLedger.BuyCost(u, ship, st, good.Id, units, price).Cost > payer.Credits + 0.001f)
        { units = MathF.Floor(Trading.PlayerLedger.Affordable(u, ship, st, good.Id, units, price, payer.Credits)); if (units < 1f) return 0f; }
        Trading.PlayerLedger.BookBuy(u, ship, st, good.Id, units, payer, price);
        ship.FlaresMissing = MathF.Max(0f, ship.FlaresMissing - units);
        return units;
    }

    /// <summary>An der Heimatbasis (Polizei, Piraten, Kriegsschiffe): zum Basispreis × <see cref="EconomyRules.AmmoBaseMarkup"/> aus der Kasse des Besitzers.</summary>
    public static void AtHome(Universe u, Ship ship)
    {
        float missing = FlaresMissingNow(u, ship);
        if (!Guidance.Active(u) || missing <= 0f || FlareGood(u) is not { } good || !u.Actors.TryGetValue(ship.Owner, out var owner)) return;
        float price = good.BasePrice * MathF.Max(0f, u.Rules.AmmoBaseMarkup);
        float units = price > 0f ? MathF.Min(missing, MathF.Max(0f, owner.Treasury.Credits) / price) : missing;
        if (units <= 0.001f) return;
        float cost = units * price;
        owner.Treasury.Credits -= cost;
        ship.FlaresMissing = MathF.Max(0f, ship.FlaresMissing - units);
        if (cost > 0f) u.Events.Publish(new TreasuryUpkeep(owner.Id, "flares", cost, 0f));
    }
}
