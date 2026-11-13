using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 24: Entscheidung einer Taktik für einen Takt (KAMPF.md 4.2 „Zielwahl und Taktik“): Ziel, Bewegung mit gewünschtem Abstand, Leistungsverteilung,
/// ob gefeuert wird und ob der Teilnehmer aufgibt (Fracht abwirft).
/// </summary>
/// Phase 28: <paramref name="Station"/> = Station als Ziel (Belagerung), wenn kein Schiffsziel gesetzt ist.
public readonly record struct TacticDecision(ShipId Target, CombatMove Move, float Distance, PowerMode Mode, bool Fire, bool Surrender, StationId Station = default);

/// <summary>Phase 24: austauschbare Gefechtstaktik (Kaperer, Polizei, Händler, Begleiter). Rein bis auf das Lesen des Universums.</summary>
public interface ICombatTactic
{
    string Key { get; }
    TacticDecision Decide(Universe u, Encounter e, CombatParticipant self, Ship ship);
}

/// <summary>Phase 24: Registry der Taktiken (Reihenfolge der Registrierung, doppelter Schlüssel wirft).</summary>
public sealed class CombatTactics
{
    public const string Raider = "raider", Police = "police", Trader = "trader", Escort = "escort", Player = "player", Siege = "siege";
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
        r.Register(new PlayerTactic());
        r.Register(new SiegeTactic());   // Phase 28: Kriegsschiffe belagern Stationen
        return r;
    }

    public static CombatTactics Default { get; } = CreateDefault();
}

/// <summary>Phase 24: gemeinsame Auswertungen für Taktiken und Gefechtssystem (rein).</summary>
public static class CombatSense
{
    /// <summary>Abstand zweier Teilnehmer: auf der Gefechtslinie, im räumlichen Gefecht (Phase 25, Spielersektor) aus den echten Positionen.</summary>
    public static float Distance(CombatParticipant a, CombatParticipant b) => a.InSpace && b.InSpace ? Vec2.Distance(a.Pos, b.Pos) : MathF.Abs(a.X - b.X);

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

    /// <summary>Phase 25: nächster aktiver Gegner nach echter Position (räumliches Gefecht; ohne Blick auf das Schiff).</summary>
    public static CombatParticipant? NearestInSpace(Encounter e, CombatParticipant self)
    {
        CombatParticipant? best = null;
        float bd = float.MaxValue;
        foreach (var p in e.Participants)
        {
            if (!p.Active || p.Side == self.Side) continue;
            float d = Vec2.Distance(p.Pos, self.Pos);
            if (d < bd) { bd = d; best = p; }
        }
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

    /// <summary>
    /// Gerätetest Phase 28 (Runde 5): schießt der Teilnehmer noch — eine seiner Waffen lädt nach einem Schuss nach oder eine seiner Raketen fliegt?
    /// </summary>
    public static bool StillShooting(Encounter e, CombatParticipant p) => StillShooting(null, e, p);

    /// <summary>Wie oben; Runde 6: mit Universum zählt auch ein eigener Schuss in den letzten <see cref="EconomyRules.PlayerFireHoldTicks"/> Ticks.</summary>
    public static bool StillShooting(Universe? u, Encounter e, CombatParticipant p)
    {
        if (u is not null && p.LastShotTick != 0 && u.Tick - p.LastShotTick <= u.Rules.PlayerFireHoldTicks) return true;
        foreach (var t in p.Timers) if (t > 0f) return true;
        foreach (var f in e.Missiles) if (f.Shooter == p.Ship) return true;
        return false;
    }

    /// <summary>
    /// Fluchtregel (rein, eine Regel für Simulation und Anzeige): null = der Teilnehmer ist außer Reichweite aller aktiven Gegner (größte Waffenreichweite, mindestens
    /// <see cref="EconomyRules.CombatMinEscapeDistance"/>) und — in einer Belagerung — der Stationsgeschütze (Abstand zur Außenhaut wie beim Schuss auf die Station);
    /// sonst der Grund. Phase 28: das aktive Spielerschiff bleibt auch im Gefecht, solange seine eigenen Waffen den Gegner erreichen — Runde 5: aber nur, solange es
    /// noch schießt (<see cref="StillShooting"/>); vorher hielt ein Falke mit Raketen (110 m) jede verfolgende Streife im Gefecht, die Flucht auf 211 m gelang nie.
    /// </summary>
    public static string? EscapeBlock(Universe u, Encounter e, CombatParticipant p)
    {
        var r = u.Rules;
        float own = r.Warfare && p.Ship == u.PlayerShip && u.Ships.TryGetValue(p.Ship, out var pShip) && StillShooting(u, e, p) ? MaxRange(u, pShip) : 0f;
        foreach (var q in Enemies(u, e, p))
        {
            float theirs = MathF.Max(r.CombatMinEscapeDistance, MaxRange(u, u.Ships[q.Ship]));
            float d = Distance(p, q);
            if (d <= MathF.Max(theirs, own))
                return d <= theirs ? $"{Who(u, u.Ships[q.Ship])} in Reichweite ({d:0} von {theirs:0} m)" : $"eigene Waffen feuern noch ({d:0} m)";
        }
        if (r.Warfare && p.Side == 0 && StationCombat.Defends(u, e) && u.Stations.TryGetValue(e.Station, out var besieged))
        {
            // Geschütze messen ab Stationsmitte (ihre Schussregel und die Anzeige „in Geschützreichweite“), eigene Waffen bis zur Außenhaut (TryFireAtStation)
            float centre = StationCombat.Distance(u, e, p, besieged);
            float guns = MathF.Max(r.CombatMinEscapeDistance, StationCombat.GunRange(u, besieged));
            if (centre <= guns) return $"Stationsgeschütze in Reichweite ({centre:0} von {guns:0} m)";
            if (centre - Station.BodyRadius <= own) return $"eigene Waffen feuern noch ({centre:0} m)";
        }
        return null;
    }

    /// <summary>Kurzname eines Schützen für Meldungen (Polizei, Piraten, Kriegsschiff, sonst der Besitzer).</summary>
    public static string Who(Universe u, Ship s) =>
        ThreatMap.IsPolice(s) ? "Polizei" : ThreatMap.IsRaider(s) ? "Piraten" : Wars.IsWarship(s) ? "Kriegsschiff" : u.ActorName(s.Owner);

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

    /// <summary>
    /// Phase 27 (Fokusfeuer, Taktik-Baustein): der schwächste aktive Gegner (Schild + Hülle) in eigener Waffenreichweite — weil alle Mitglieder einer Seite
    /// dieselbe Regel anwenden, konzentrieren sie ihr Feuer. Ohne Flottenkampf/Fokusfeuer oder ohne Gegner in Reichweite: <paramref name="fallback"/>. Rein.
    /// </summary>
    public static CombatParticipant? Focus(Universe u, Encounter e, CombatParticipant self, Ship ship, CombatParticipant? fallback)
    {
        if (!u.Rules.FleetCombat || !u.Rules.CombatFocusFire) return fallback;
        float reach = MaxRange(u, ship);
        CombatParticipant? best = null;
        float bestHp = float.MaxValue;
        foreach (var q in Enemies(u, e, self))
        {
            if (reach > 0f && Distance(self, q) > reach) continue;
            var s = u.Ships[q.Ship];
            float hp = s.ShieldNow + s.Hull;
            if (hp < bestHp) { bestHp = hp; best = q; }
        }
        return best ?? fallback;
    }

    /// <summary>
    /// Phase 27 (Zielverteilung der Begleiter): der Gegner, der gerade einen Schützling dieser Seite angreift (ein Mitglied derselben Seite, das selbst kein Begleiter
    /// und keine Polizei ist); null = keiner. Ohne Flottenkampf immer null. Rein.
    /// </summary>
    public static CombatParticipant? Attacker(Universe u, Encounter e, CombatParticipant self)
    {
        if (!u.Rules.FleetCombat) return null;
        foreach (var q in Enemies(u, e, self))
            if (q.Target.Value != 0 && e.Of(q.Target) is { Active: true } ward && ward.Side == self.Side && ward.Ship != self.Ship
                && ward.Tactic is not (CombatTactics.Escort or CombatTactics.Police)) return q;
        return null;
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
        // zweiter Gerätetest Phase 28: ein Kaperer mit Einsatzregel „Angriff“ (nur im Gefechtstest gesetzt; NPC-Kaperer haben sie nie) kämpft bis zum Ende
        bool flee = ship.Engagement != Engagement.Attack && (ship.Hull < r.RaiderFleeHull * ship.MaxHull || own < r.RaiderFleeRatio * enemy);
        if (flee) return new TacticDecision(nearest.Ship, CombatMove.Flee, 0f, PowerMode.Engine, true, false);
        var victim = e.Victim.Value != 0 && e.Of(e.Victim) is { Active: true } v && v.Side != self.Side ? v : null;
        var target = victim ?? CombatSense.Focus(u, e, self, ship, null) ?? CombatSense.KeepTarget(u, e, self, nearest)!;
        if (!u.Rules.FleetCombat) target = CombatSense.KeepTarget(u, e, self, victim ?? nearest)!;
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
        var target = CombatSense.Focus(u, e, self, ship, null) ?? CombatSense.KeepTarget(u, e, self, nearest)!;   // Phase 27: Fokusfeuer
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
        // Phase 27: zuerst wer den Schützling angreift, sonst Fokusfeuer
        var target = CombatSense.Attacker(u, e, self) ?? CombatSense.Focus(u, e, self, ship, null) ?? CombatSense.KeepTarget(u, e, self, nearest)!;
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
        bool raided = false;   // Phase 25: auch der Spieler als Angreifer (Überfall durch den Spieler) bringt Händler zum Abwerfen
        foreach (var p in CombatSense.Enemies(u, e, self)) if (p.Tactic is CombatTactics.Raider or CombatTactics.Player) { raided = true; break; }
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

/// <summary>
/// Phase 25: das aktive Spielerschiff. Die Person steuert selbst: Bewegung kommt aus der Ansicht (die Position des Schiffs), geschossen wird in Echtzeit
/// über <see cref="PlayerGunnery.TryFire"/>, die Leistungsverteilung stellt sie über die Tasten. Die Taktik hält nur das gewählte Ziel und schießt nie von
/// selbst. Ist das Schiff nicht (mehr) das aktive, verhält es sich wie ein Händler.
/// </summary>
public sealed class PlayerTactic : ICombatTactic
{
    static readonly TraderTactic Fallback = new();
    public string Key => CombatTactics.Player;

    public TacticDecision Decide(Universe u, Encounter e, CombatParticipant self, Ship ship)
    {
        if (ship.Id != u.PlayerShip) return Fallback.Decide(u, e, self, ship);
        var target = CombatSense.KeepTarget(u, e, self, CombatSense.Nearest(u, e, self));
        return new TacticDecision(target?.Ship ?? default, CombatMove.Hold, 0f, ship.Systems.Mode, false, false);
    }
}

/// <summary>
/// Phase 28: Belagerer (Kriegsschiffe). Zieht sich unter <see cref="EconomyRules.WarshipRetreatHull"/> zurück. Feindliche Schiffe in eigener Reichweite haben
/// Vorrang (Fokusfeuer auf das schwächste); sonst nimmt er die belagerte Station unter Feuer und hält dafür den bevorzugten Abstand. Ohne Station und Gegner hält er.
/// </summary>
public sealed class SiegeTactic : ICombatTactic
{
    public string Key => CombatTactics.Siege;

    public TacticDecision Decide(Universe u, Encounter e, CombatParticipant self, Ship ship)
    {
        var nearest = CombatSense.Nearest(u, e, self);
        float preferred = CombatSense.Preferred(u, ship);
        bool station = e.Station.Value != 0 && StationCombat.Defends(u, e) && self.Side == 0;
        if (ship.Hull < u.Rules.WarshipRetreatHull * ship.MaxHull)
            return new TacticDecision(nearest?.Ship ?? default, CombatMove.Flee, 0f, PowerMode.Engine, true, false, nearest is null && station ? e.Station : default);
        float reach = CombatSense.MaxRange(u, ship);
        if (nearest is not null && CombatSense.Distance(self, nearest) <= reach)
        {
            var target = CombatSense.Focus(u, e, self, ship, null) ?? CombatSense.KeepTarget(u, e, self, nearest)!;
            return new TacticDecision(target.Ship, CombatMove.Approach, preferred, PowerMode.Weapons, true, false);
        }
        if (station) return new TacticDecision(default, CombatMove.Approach, preferred, PowerMode.Weapons, true, false, e.Station);
        if (nearest is not null) return new TacticDecision(nearest.Ship, CombatMove.Approach, preferred, PowerMode.Engine, true, false);
        return new TacticDecision(default, CombatMove.Hold, 0f, PowerMode.Balanced, false, false);
    }
}
