using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 24: Entscheidung einer Taktik für einen Takt (KAMPF.md 4.2 „Zielwahl und Taktik“): Ziel, Bewegung mit gewünschtem Abstand, Leistungsverteilung,
/// ob gefeuert wird und ob der Teilnehmer aufgibt (Fracht abwirft).
/// </summary>
public readonly record struct TacticDecision(ShipId Target, CombatMove Move, float Distance, PowerMode Mode, bool Fire, bool Surrender);

/// <summary>Phase 24: austauschbare Gefechtstaktik (Kaperer, Polizei, Händler, Begleiter). Rein bis auf das Lesen des Universums.</summary>
public interface ICombatTactic
{
    string Key { get; }
    TacticDecision Decide(Universe u, Encounter e, CombatParticipant self, Ship ship);
}

/// <summary>Phase 24: Registry der Taktiken (Reihenfolge der Registrierung, doppelter Schlüssel wirft).</summary>
public sealed class CombatTactics
{
    public const string Raider = "raider", Police = "police", Trader = "trader", Escort = "escort";
    readonly Dictionary<string, ICombatTactic> _byKey = new(StringComparer.Ordinal);
    readonly List<ICombatTactic> _list = new();

    public void Register(ICombatTactic t)
    {
        if (!_byKey.TryAdd(t.Key, t)) throw new ArgumentException($"Taktik '{t.Key}' doppelt");
        _list.Add(t);
    }

    public ICombatTactic Get(string key) => _byKey.TryGetValue(key, out var t) ? t : _byKey[Trader];
    public bool Has(string key) => _byKey.ContainsKey(key);
    public IReadOnlyList<ICombatTactic> All => _list;

    public static CombatTactics CreateDefault()
    {
        var r = new CombatTactics();
        r.Register(new RaiderTactic());
        r.Register(new PoliceTactic());
        r.Register(new TraderTactic());
        r.Register(new EscortTactic());
        return r;
    }

    public static CombatTactics Default { get; } = CreateDefault();
}

/// <summary>Phase 24: gemeinsame Auswertungen für Taktiken und Gefechtssystem (rein).</summary>
public static class CombatSense
{
    /// <summary>Abstand zweier Teilnehmer auf der Gefechtslinie.</summary>
    public static float Distance(CombatParticipant a, CombatParticipant b) => MathF.Abs(a.X - b.X);

    /// <summary>Aktive Gegner (andere Seite, Schiff existiert), feste Reihenfolge.</summary>
    public static IEnumerable<CombatParticipant> Enemies(Universe u, Encounter e, CombatParticipant self)
    {
        foreach (var p in e.Participants)
            if (p.Active && p.Side != self.Side && u.Ships.ContainsKey(p.Ship)) yield return p;
    }

    /// <summary>Nächster aktiver Gegner (Gleichstand: Reihenfolge); null = keiner.</summary>
    public static CombatParticipant? Nearest(Universe u, Encounter e, CombatParticipant self)
    {
        CombatParticipant? best = null;
        foreach (var p in Enemies(u, e, self))
            if (best is null || Distance(self, p) < Distance(self, best)) best = p;
        return best;
    }

    /// <summary>Stärke der aktiven Teilnehmer einer Seite (<see cref="CombatRules.Strength(Ship)"/>).</summary>
    public static float SideStrength(Universe u, Encounter e, int side)
    {
        float s = 0f;
        foreach (var p in e.Participants)
            if (p.Active && p.Side == side && u.Ships.TryGetValue(p.Ship, out var sh)) s += CombatRules.Strength(sh);
        return s;
    }

    /// <summary>Größte Waffenreichweite eines Schiffs (0 = unbewaffnet).</summary>
    public static float MaxRange(Universe u, Ship ship)
    {
        float r = 0f;
        foreach (var key in ship.Fit)
            if (key is not null && u.Modules.TryGetValue(key, out var m) && m.IsWeapon) r = MathF.Max(r, m.Stat("range"));
        return r;
    }

    /// <summary>Bewaffnet: mindestens eine Waffe mit Waffenwerten.</summary>
    public static bool Armed(Universe u, Ship ship) => MaxRange(u, ship) > 0f;

    /// <summary>Kleinste Waffenreichweite eines Schiffs (0 = unbewaffnet): bis dorthin muss es heran, damit alle Waffen tragen.</summary>
    public static float MinRange(Universe u, Ship ship)
    {
        float r = 0f;
        foreach (var key in ship.Fit)
            if (key is not null && u.Modules.TryGetValue(key, out var m) && m.IsWeapon) r = r <= 0f ? m.Stat("range") : MathF.Min(r, m.Stat("range"));
        return r;
    }

    /// <summary>Gewünschter Abstand: Anteil der kleinsten eigenen Reichweite, damit alle Waffen tragen (unbewaffnet: Fluchtabstand).</summary>
    public static float Preferred(Universe u, Ship ship)
    {
        float r = MinRange(u, ship);
        return r > 0f ? r * u.Rules.CombatPreferredRange : u.Rules.CombatMinEscapeDistance;
    }

    /// <summary>Behält das bisherige Ziel, solange es aktiver Gegner ist; sonst <paramref name="fallback"/>.</summary>
    public static CombatParticipant? KeepTarget(Universe u, Encounter e, CombatParticipant self, CombatParticipant? fallback)
    {
        if (self.Target.Value != 0 && e.Of(self.Target) is { Active: true } t && t.Side != self.Side && u.Ships.ContainsKey(t.Ship)) return t;
        return fallback;
    }

    /// <summary>Verhaltensregel (Spielerflotte, Phase 12): im Verband die des Verbands; KI-Schiffe verteidigen sich.</summary>
    public static Engagement RulesOf(Universe u, Ship ship) => ship.IsPlayer ? Squadrons.RulesOf(u, ship) : Engagement.Defend;
}

/// <summary>
/// Kaperer (KAMPF.md 4.2): greift das Opfer mit der wertvollen Fracht an (bleibt bei seinem Ziel), hält sich in Schussweite, Stellung „Waffen“. Flieht
/// (Stellung „Antrieb“) unter <see cref="EconomyRules.RaiderFleeHull"/> oder wenn seine Seite schwächer ist als <see cref="EconomyRules.RaiderFleeRatio"/> × die
/// Gegenseite (Polizei greift ein, Opfer wehrt sich).
/// </summary>
public sealed class RaiderTactic : ICombatTactic
{
    public string Key => CombatTactics.Raider;

    public TacticDecision Decide(Universe u, Encounter e, CombatParticipant self, Ship ship)
    {
        var r = u.Rules;
        var nearest = CombatSense.Nearest(u, e, self);
        if (nearest is null) return new TacticDecision(default, CombatMove.Hold, 0f, PowerMode.Balanced, false, false);
        float own = CombatSense.SideStrength(u, e, self.Side), enemy = CombatSense.SideStrength(u, e, 1 - self.Side);
        bool flee = ship.Hull < r.RaiderFleeHull * ship.MaxHull || own < r.RaiderFleeRatio * enemy;
        if (flee) return new TacticDecision(nearest.Ship, CombatMove.Flee, 0f, PowerMode.Engine, true, false);
        var victim = e.Victim.Value != 0 && e.Of(e.Victim) is { Active: true } v && v.Side != self.Side ? v : null;
        var target = CombatSense.KeepTarget(u, e, self, victim ?? nearest)!;
        float preferred = CombatSense.Preferred(u, ship);
        // Verfolgung: weit hinter dem gewünschten Abstand Stellung „Antrieb“ (holt auf), in Schussweite „Waffen“
        var mode = CombatSense.Distance(self, target) > preferred * 1.15f ? PowerMode.Engine : PowerMode.Weapons;
        return new TacticDecision(target.Ship, CombatMove.Approach, preferred, mode, true, false);
    }
}

/// <summary>
/// Polizei (KAMPF.md 4.2): stellt das nächste Kaperschiff, hält Schussweite, Stellung „Waffen“; zieht sich erst unter <see cref="EconomyRules.PoliceRetreatHull"/>
/// zurück. Verfolgt nicht über die Grenze: Wer außer Reichweite flieht, ist entkommen.
/// </summary>
public sealed class PoliceTactic : ICombatTactic
{
    public string Key => CombatTactics.Police;

    public TacticDecision Decide(Universe u, Encounter e, CombatParticipant self, Ship ship)
    {
        var nearest = CombatSense.Nearest(u, e, self);
        if (nearest is null) return new TacticDecision(default, CombatMove.Hold, 0f, PowerMode.Balanced, false, false);
        if (ship.Hull < u.Rules.PoliceRetreatHull * ship.MaxHull) return new TacticDecision(nearest.Ship, CombatMove.Flee, 0f, PowerMode.Engine, true, false);
        var target = CombatSense.KeepTarget(u, e, self, nearest)!;
        float preferred = CombatSense.Preferred(u, ship);
        var mode = CombatSense.Distance(self, target) > preferred * 1.15f ? PowerMode.Engine : PowerMode.Weapons;   // verfolgt, solange der Gegner in Reichweite ist
        return new TacticDecision(target.Ship, CombatMove.Approach, preferred, mode, true, false);
    }
}

/// <summary>
/// Begleiter (KAMPF.md 4.2): Eskorten und Wachen schützen ihr Schiff — greifen den nächsten Angreifer an, ziehen sich unter
/// <see cref="EconomyRules.EscortRetreatHull"/> zurück. Verhaltensregeln der Spielerflotte: „Passiv“ flieht ohne zu schießen, „Feuer einstellen“ hält ohne zu schießen.
/// </summary>
public sealed class EscortTactic : ICombatTactic
{
    public string Key => CombatTactics.Escort;

    public TacticDecision Decide(Universe u, Encounter e, CombatParticipant self, Ship ship)
    {
        var nearest = CombatSense.Nearest(u, e, self);
        if (nearest is null) return new TacticDecision(default, CombatMove.Hold, 0f, PowerMode.Balanced, false, false);
        var rules = CombatSense.RulesOf(u, ship);
        if (rules == Engagement.Passive) return new TacticDecision(nearest.Ship, CombatMove.Flee, 0f, PowerMode.Engine, false, false);
        if (rules == Engagement.HoldFire) return new TacticDecision(nearest.Ship, CombatMove.Hold, CombatSense.Preferred(u, ship), PowerMode.Shield, false, false);
        if (ship.Hull < u.Rules.EscortRetreatHull * ship.MaxHull) return new TacticDecision(nearest.Ship, CombatMove.Flee, 0f, PowerMode.Engine, true, false);
        var target = CombatSense.KeepTarget(u, e, self, nearest)!;
        return new TacticDecision(target.Ship, CombatMove.Approach, CombatSense.Preferred(u, ship), PowerMode.Weapons, true, false);
    }
}

/// <summary>
/// Händler und Bergbauschiffe (KAMPF.md 4.2, 4.2a): Bewaffnet und stark genug (eigene Seite ≥ <see cref="EconomyRules.TraderFightRatio"/> × Gegenseite) wehrt er sich
/// aus der Stellung; sonst flieht er (Stellung „Antrieb“) und schießt, wenn er kann, im Rückzug. Fällt die Hülle unter seinen Mut (<see cref="CombatParticipant.Courage"/>)
/// und greift ihn ein Kaperer an, wirft er Fracht ab (Aufgabe). Verhaltensregeln der Spielerflotte: „Passiv“ flieht ohne zu schießen, „Feuer einstellen“ hält
/// ohne zu schießen.
/// </summary>
public sealed class TraderTactic : ICombatTactic
{
    public string Key => CombatTactics.Trader;

    public TacticDecision Decide(Universe u, Encounter e, CombatParticipant self, Ship ship)
    {
        var r = u.Rules;
        var nearest = CombatSense.Nearest(u, e, self);
        if (nearest is null) return new TacticDecision(default, CombatMove.Hold, 0f, PowerMode.Balanced, false, false);
        bool raided = false;
        foreach (var p in CombatSense.Enemies(u, e, self)) if (p.Tactic == CombatTactics.Raider) { raided = true; break; }
        bool surrender = raided && ship.Hull < self.Courage * ship.MaxHull && ThreatMap.CargoValue(u, ship) > 0f;
        var rules = CombatSense.RulesOf(u, ship);
        if (rules == Engagement.Passive) return new TacticDecision(nearest.Ship, CombatMove.Flee, 0f, PowerMode.Engine, false, surrender);
        if (rules == Engagement.HoldFire) return new TacticDecision(nearest.Ship, CombatMove.Hold, 0f, PowerMode.Shield, false, surrender);
        bool armed = CombatSense.Armed(u, ship);
        float own = CombatSense.SideStrength(u, e, self.Side), enemy = CombatSense.SideStrength(u, e, 1 - self.Side);
        var target = CombatSense.KeepTarget(u, e, self, nearest)!;
        if (armed && own >= r.TraderFightRatio * enemy)
            return new TacticDecision(target.Ship, CombatMove.Hold, CombatSense.Preferred(u, ship), PowerMode.Weapons, true, surrender);
        return new TacticDecision(target.Ship, CombatMove.Flee, 0f, PowerMode.Engine, armed, surrender);
    }
}
