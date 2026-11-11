using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Besitz eines Akteurs, aus dem die Größenzahl folgt: Schiffe, Stationen (auch Baustellen), kontrollierte Sektoren, Geld.</summary>
public readonly record struct Holdings(int Ships, int Stations, int Sectors, float Money);

/// <summary>
/// Regeln der Akteure (Phase 9, POLITIK.md Abschnitt 6): Größenzahl und Größenklasse aus dem Besitz, Appetit beim Zukauf, gewürfelte
/// Persönlichkeit und der träge Aufstieg freier Einzelschiffe zum eigenen Akteur. Reine Funktionen über dem Universum (außer
/// <see cref="Promote"/>, das einen Akteur anlegt).
/// </summary>
public static class ActorRules
{
    /// <summary>
    /// Besitz je ausdrücklichem Akteur in einem Durchlauf: Schiffe (ohne die freien Einzelschiffe der Sammelgruppe, die je für sich zählen),
    /// Stationen und Baustellen, Sektoren der Fraktion (<see cref="Sector.Owner"/>) und Geld = Kasse + Betriebskassen der eigenen Schiffe und
    /// fertigen Stationen (Treuhandgeld von Baustellen gehört dem Projekt, nicht dem Besitzer). Feste Reihenfolge (Ids).
    /// </summary>
    public static Dictionary<ActorId, Holdings> Index(Universe u)
    {
        var acc = new Dictionary<ActorId, (int ships, int stations, int sectors, float money)>();
        foreach (var a in u.Actors.Values.OrderBy(a => a.Id.Value)) acc[a.Id] = (0, 0, 0, a.Treasury.Credits);
        foreach (var s in u.Ships.Values.OrderBy(s => s.Id.Value))
        {
            if (s.Owner == u.FreeActor || !acc.TryGetValue(s.Owner, out var h)) continue;
            acc[s.Owner] = (h.ships + 1, h.stations, h.sectors, h.money + s.Credits);
        }
        foreach (var st in u.Stations.Values.OrderBy(s => s.Id.Value))
        {
            if (!acc.TryGetValue(st.Owner, out var h)) continue;
            acc[st.Owner] = (h.ships, h.stations + 1, h.sectors, h.money + (st.IsBuildSite ? 0f : st.Credits));
        }
        foreach (var sec in u.Sectors.Values.OrderBy(s => s.Id.Value))
        {
            ActorId owner = sec.Owner;
            if (!acc.TryGetValue(owner, out var h)) continue;
            acc[owner] = (h.ships, h.stations, h.sectors + 1, h.money);
        }
        return acc.ToDictionary(kv => kv.Key, kv => new Holdings(kv.Value.ships, kv.Value.stations, kv.Value.sectors, kv.Value.money));
    }

    /// <summary>Besitz eines freien Einzelschiffs (impliziter Akteur): ein Schiff, seine Betriebskasse.</summary>
    public static Holdings SingleShip(Ship ship) => new(1, 0, 0, ship.Credits);

    /// <summary>Größenzahl = Schiffe × Gewicht + Stationen × Gewicht + Sektoren × Gewicht + Geld / Skala (POLITIK.md Abschnitt 6).</summary>
    public static float Size(EconomyRules r, Holdings h) =>
        h.Ships * r.ActorShipWeight + h.Stations * r.ActorStationWeight + h.Sectors * r.ActorSectorWeight + MathF.Max(0f, h.Money) / MathF.Max(1f, r.ActorMoneyScale);

    /// <summary>Größenklasse zur Größenzahl (Schwellen <see cref="EconomyRules.ActorSizeThresholds"/>).</summary>
    public static ActorSizeClass ClassOf(EconomyRules r, float size)
    {
        int c = 0;
        for (int i = 0; i < r.ActorSizeThresholds.Count; i++) if (size >= r.ActorSizeThresholds[i]) c = i + 1;
        return (ActorSizeClass)Math.Min(c, (int)ActorSizeClass.State);
    }

    /// <summary>Größenklasse eines Akteurs (aus einem vorher gebauten <see cref="Index"/>).</summary>
    public static ActorSizeClass ClassOf(Universe u, Dictionary<ActorId, Holdings> index, ActorId actor) =>
        ClassOf(u.Rules, Size(u.Rules, index.TryGetValue(actor, out var h) ? h : default));

    /// <summary>Appetit beim Zukauf: Wert der Größenklasse (<see cref="EconomyRules.InvestAppetite"/>) × (0,5 + Gier).</summary>
    public static float Appetite(EconomyRules r, ActorSizeClass c, Personality p) =>
        (r.InvestAppetite.Count > (int)c ? r.InvestAppetite[(int)c] : 1f) * (0.5f + p.Greed);

    /// <summary>Persönlichkeit eines Akteurs; Unbekannte sind neutral.</summary>
    public static Personality PersonalityOf(Universe u, ActorId actor) => u.Actors.TryGetValue(actor, out var a) ? a.Personality : Personality.Neutral;

    /// <summary>Würfelt eine Persönlichkeit um die Mittelwerte (je ein Wurf über <see cref="Universe.Rng"/>, Reihenfolge A, G, K, L).</summary>
    public static Personality Roll(Universe u, IReadOnlyList<float> mean, float spread)
    {
        float M(int i) => mean.Count > i ? mean[i] : 0.5f;
        float R(int i) => M(i) + u.Rng.Range(-spread, spread);
        float a = R(0), g = R(1), c = R(2), l = R(3);
        return new Personality(a, g, c, l).Clamped();
    }

    /// <summary>
    /// Träger Aufstieg (POLITIK.md „Einzelschiffe träge anlegen“): Ein freies Einzelschiff wird zum eigenen Akteur, sobald es etwas Besonderes tut
    /// (hier: investieren). Der neue Akteur (Art <see cref="ActorKind.Independent"/>) besitzt das Schiff, ist Mitglied der Fraktion, der der
    /// aktuelle Sektor gehört (dort ist er gemeldet), und würfelt seine Persönlichkeit. Das Schiff behält seine Betriebskasse.
    /// Schiffe, die schon einem ausdrücklichen Akteur gehören, bleiben unverändert. Gibt den Besitzer nach dem Aufruf zurück.
    /// </summary>
    public static ActorId Promote(Universe u, Ship ship)
    {
        if (ship.Owner != u.FreeActor || !u.Rules.PromoteInvestors || ship.IsPlayer) return ship.Owner;
        var id = u.NewActorId();
        FactionId? membership = u.Sectors.TryGetValue(ship.Sector, out var sec) && u.Factions.ContainsKey(sec.Owner) ? sec.Owner : null;
        string label = ship.Role switch { ShipRole.Miner => "Schürfbetrieb", ShipRole.Builder => "Baubetrieb", _ => "Handelskontor" };
        var actor = new Actor
        {
            Id = id, Name = $"{label} {ship.Id.Value}", Kind = ActorKind.Independent, Membership = membership,
            Personality = Roll(u, u.Rules.IndependentPersonality, u.Rules.PersonalitySpread), CreatedTick = u.Tick,
        };
        u.Actors[id] = actor;
        ship.Owner = id;
        u.Events.Publish(new ActorPromoted(id, ship.Id));
        return id;
    }

    /// <summary>Besitzt der Akteur noch etwas (Schiff, Station oder Baustelle)?</summary>
    public static bool HasPossessions(Universe u, ActorId actor) =>
        u.Ships.Values.Any(s => s.Owner == actor) || u.Stations.Values.Any(s => s.Owner == actor);
}

/// <summary>Woher das Geld beim Zukauf kommt.</summary>
public enum InvestorSource { Treasury, Station, Ship }

/// <summary>Ein möglicher Geldgeber beim Zukauf: Quelle, Akteur dahinter, Größenklasse, verfügbares Geld und Gewicht der Ziehung.</summary>
public sealed record Investor(InvestorSource Source, ActorId Holder, ActorSizeClass Class, float Money, float Weight, Station? Station, Ship? Ship)
{
    /// <summary>Konto, von dem bezahlt wird.</summary>
    public ICreditAccount Account(Universe u) => Source switch
    {
        InvestorSource.Treasury => u.Actors[Holder].Treasury,
        InvestorSource.Station => new StationAccount(Station!),
        _ => Ship!,
    };

    sealed class StationAccount : ICreditAccount
    {
        readonly Station _st;
        public StationAccount(Station st) => _st = st;
        public float Credits { get => _st.Credits; set => _st.Credits = value; }
    }
}

/// <summary>
/// Zukauf mit Erst- und Zweitzugriff (Phase 9, POLITIK.md „Zukauf durch kleinere Akteure“, Entscheidung „weich“): Statt „der Reichste zahlt“
/// zieht eine gewichtete Ziehung über <see cref="Universe.Rng"/> einen Geldgeber. Gewicht = (Geld − Schwelle + 1) × Appetit der Größenklasse
/// × (0,5 + Gier); Große haben den höheren Appetit (Erstzugriff), Kleine springen seltener ein (Zweitzugriff). Kandidaten in fester
/// Reihenfolge: Kassen der Akteure (Id), Betriebskassen der Stationen (Id), der Schiffe (Id).
/// </summary>
public static class Investors
{
    /// <summary>Geldgeber für einen Stationsbau, die <paramref name="need"/> aufbringen (Kassen, Stationen, wirtschaftlich aktive KI-Schiffe).</summary>
    public static List<Investor> ForStation(Universe u, float need)
    {
        var rules = u.Rules;
        var index = ActorRules.Index(u);
        var list = new List<Investor>();
        if (rules.TreasuryInvests)
            foreach (var a in u.Actors.Values.OrderBy(a => a.Id.Value))
            {
                if (a.Kind is ActorKind.Player or ActorKind.Free or ActorKind.Outlaw || a.Treasury.Credits < need) continue;   // Phase 11: Piraten bauen keine Stationen
                list.Add(Make(u, index, InvestorSource.Treasury, a.Id, a.Treasury.Credits, need, null, null));
            }
        foreach (var st in u.Stations.Values.Where(s => !s.IsBuildSite && s.Credits >= need).OrderBy(s => s.Id.Value))
            list.Add(Make(u, index, InvestorSource.Station, st.Owner, st.Credits, need, st, null));
        foreach (var sh in u.Ships.Values.Where(s => s.IsEconomyAi && s.Credits >= need).OrderBy(s => s.Id.Value))
            list.Add(Make(u, index, InvestorSource.Ship, sh.Owner, sh.Credits, need, null, sh));
        return list;
    }

    /// <summary>Käufer für ein neues Schiff einer Aufgabe: KI-Schiffe dieser Rolle, geparkt, mit mindestens <paramref name="threshold"/> Credits.</summary>
    public static List<Investor> ForShip(Universe u, ShipRole role, float threshold)
    {
        var index = ActorRules.Index(u);
        return u.Ships.Values
            .Where(s => s.Role == role && s.Controller == Controller.Ai && s.Transit is null && s.Credits >= threshold)
            .OrderBy(s => s.Id.Value)
            .Select(s => Make(u, index, InvestorSource.Ship, s.Owner, s.Credits, threshold, null, s))
            .ToList();
    }

    static Investor Make(Universe u, Dictionary<ActorId, Holdings> index, InvestorSource src, ActorId holder, float money, float need, Station? st, Ship? sh)
    {
        // Freie Einzelschiffe sind implizite Akteure: Größe aus dem Schiff selbst, Persönlichkeit der Sammelgruppe
        var cls = sh is not null && holder == u.FreeActor
            ? ActorRules.ClassOf(u.Rules, ActorRules.Size(u.Rules, ActorRules.SingleShip(sh)))
            : ActorRules.ClassOf(u, index, holder);
        float weight = (money - need + 1f) * ActorRules.Appetite(u.Rules, cls, ActorRules.PersonalityOf(u, holder));
        return new Investor(src, holder, cls, money, MathF.Max(0f, weight), st, sh);
    }

    /// <summary>Gewichtete Ziehung (ein Wurf über <see cref="Universe.Rng"/>, nur bei mehr als einem Kandidaten); null ohne Kandidaten.</summary>
    public static Investor? Draw(Universe u, IReadOnlyList<Investor> candidates)
    {
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];
        double total = 0;
        foreach (var c in candidates) total += c.Weight;
        if (total <= 0) return candidates[0];
        double roll = u.Rng.NextDouble() * total;
        foreach (var c in candidates)
        {
            roll -= c.Weight;
            if (roll < 0) return c;
        }
        return candidates[^1];
    }
}

/// <summary>
/// Beziehungen sparsam (Phase 9, POLITIK.md „Beziehungen sparsam speichern“): Gespeichert werden nur Abweichungen — persönliche am Akteur
/// (<see cref="Actor.Relations"/>), Hauptbeziehungen zwischen Fraktionen an der Fraktion (<see cref="Faction.Relations"/>). Alles andere ergibt
/// sich aus Vorgabewerten nach Gruppe (gleiche Fraktion, Staat–Freie, alle–Gesetzlose; <see cref="EconomyRules"/>). Speicher und Rechenzeit
/// bleiben linear; freie Einzelschiffe teilen die Einträge ihrer Sammelgruppe.
/// </summary>
public static class Relations
{
    /// <summary>
    /// Effektive Beziehung von <paramref name="a"/> zu <paramref name="b"/> (−1 … +1): persönliche Abweichung von a (zu b oder zu b's Fraktion),
    /// sonst Hauptbeziehung der Fraktionen (gleiche Fraktion: Vorgabe), sonst Vorgabe nach Art.
    /// </summary>
    public static float Effective(Universe u, ActorId a, ActorId b)
    {
        if (a == b) return 1f;
        var fb = u.FactionOf(b);
        if (u.Actors.TryGetValue(a, out var aa))
        {
            if (aa.Relations.TryGetValue(b, out var p)) return p;
            if (fb is { } fbv && aa.Relations.TryGetValue(fbv, out var pf)) return pf;
        }
        return WithoutPersonal(u, a, b);
    }

    /// <summary>Beziehung eines Schiffs zu einem Akteur (über seinen Besitzer; freie Einzelschiffe über die Sammelgruppe).</summary>
    public static float OfShip(Universe u, Ship ship, ActorId other) => Effective(u, ship.Owner, other);

    /// <summary>Beziehung ohne persönliche Abweichung: Fraktionsebene, sonst Vorgabe.</summary>
    static float WithoutPersonal(Universe u, ActorId a, ActorId b)
    {
        var fa = u.FactionOf(a);
        var fb = u.FactionOf(b);
        if (fa is { } x && fb is { } y)
        {
            if (x == y) return u.Rules.RelationSameFaction;
            if (u.Factions.TryGetValue(x, out var fx) && fx.Relations.TryGetValue(y, out var v)) return v;
        }
        return Default(u, Group(u, a), Group(u, b));
    }

    /// <summary>Art, nach der die Vorgabe gilt: die Art der Fraktion, sonst die eigene Art.</summary>
    static ActorKind Group(Universe u, ActorId id)
    {
        if (u.FactionOf(id) is { } f && u.Actors.TryGetValue(f, out var fa)) return fa.Kind;
        return u.Actors.TryGetValue(id, out var a) ? a.Kind : ActorKind.Free;
    }

    /// <summary>Vorgabewert nach Art (Staat–Staat, Staat–Freie, alle–Gesetzlose).</summary>
    public static float Default(Universe u, ActorKind a, ActorKind b)
    {
        var r = u.Rules;
        if (a == ActorKind.Outlaw || b == ActorKind.Outlaw) return a == b ? r.RelationSameFaction : r.RelationOutlaw;
        bool freeA = a is ActorKind.Free or ActorKind.Independent, freeB = b is ActorKind.Free or ActorKind.Independent;
        if (freeA != freeB) return r.RelationFree;
        return r.RelationDefault;
    }

    /// <summary>
    /// Setzt die Beziehung von <paramref name="a"/> zu <paramref name="b"/> (auf −1 … +1 begrenzt). Zwischen zwei Fraktionsakteuren ist das die
    /// Hauptbeziehung, sonst eine persönliche Abweichung. Entspricht der Wert dem, was ohne Eintrag gälte, wird der Eintrag entfernt (sparsam).
    /// </summary>
    public static void Set(Universe u, ActorId a, ActorId b, float value)
    {
        value = Math.Clamp(value, -1f, 1f);
        var fa = new FactionId(a.Value);
        var fb = new FactionId(b.Value);
        if (u.Factions.TryGetValue(fa, out var faction) && u.Factions.ContainsKey(fb))
        {
            faction.Relations.Remove(fb);
            if (MathF.Abs(WithoutPersonal(u, a, b) - value) > 1e-4f) faction.Relations[fb] = value;
            return;
        }
        if (!u.Actors.TryGetValue(a, out var actor)) return;
        actor.Relations.Remove(b);
        if (MathF.Abs(Effective(u, a, b) - value) > 1e-4f) actor.Relations[b] = value;
    }

    /// <summary>Anzeigetext einer Beziehung (verbündet, freundlich, neutral, kühl, feindlich).</summary>
    public static string Label(float v) => v >= 0.5f ? "verbündet" : v >= 0.15f ? "freundlich" : v > -0.15f ? "neutral" : v > -0.5f ? "kühl" : "feindlich";

    /// <summary>Ist die Beziehung von a zu b als Abweichung gespeichert (sonst gilt eine Vorgabe)?</summary>
    public static bool IsStored(Universe u, ActorId a, ActorId b)
    {
        if (u.Actors.TryGetValue(a, out var aa) && (aa.Relations.ContainsKey(b) || (u.FactionOf(b) is { } fb && aa.Relations.ContainsKey(fb)))) return true;
        return u.FactionOf(a) is { } x && u.FactionOf(b) is { } y && x != y && u.Factions.TryGetValue(x, out var fx) && fx.Relations.ContainsKey(y);
    }

    /// <summary>Anzahl gespeicherter Beziehungseinträge (Fraktionen plus Akteure) — muss linear in der Zahl der Akteure bleiben.</summary>
    public static int StoredEntries(Universe u) => u.Factions.Values.Sum(f => f.Relations.Count) + u.Actors.Values.Sum(a => a.Relations.Count);
}

/// <summary>
/// Abstieg (Phase 9, POLITIK.md „Einzelschiffe träge anlegen“): Aufgestiegene Einzelakteure ohne Besitz (kein Schiff, keine Station, keine
/// Baustelle) und ohne Geld in der Kasse werden entfernt, wie eine bankrotte Station. Fraktionen, Spieler und Sammelgruppe bleiben immer.
/// </summary>
public sealed class ActorSystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        List<ActorId>? gone = null;
        foreach (var a in u.Actors.Values.OrderBy(a => a.Id.Value))
        {
            if (a.Kind != ActorKind.Independent || a.IsFaction(u) || a.Treasury.Credits >= 1f) continue;
            if (ActorRules.HasPossessions(u, a.Id)) continue;
            (gone ??= new()).Add(a.Id);
        }
        if (gone is null) return;
        foreach (var id in gone)
        {
            u.Actors.Remove(id);
            // Beziehungen anderer zu ihm verfallen (sparsam bleiben)
            foreach (var other in u.Actors.Values) other.Relations.Remove(id);
            u.Events.Publish(new ActorRemoved(id, "no_possessions"));
        }
    }
}
