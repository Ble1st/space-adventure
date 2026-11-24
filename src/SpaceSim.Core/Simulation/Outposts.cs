using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 30: Kleinstationen (Satellit, leichte/mittlere/schwere Waffenstellung; Typen mit Regierungsform <c>outpost</c>, Größe XS, nicht andockbar).
/// Gemeinsame Regeln: wer ist eine Kleinstation, welcher Typ hat welche Rolle (aus den Daten: Geschütze = Waffenstellung, sonst Satellit; Stufe nach Geschützzahl),
/// Platzwahl an Toren/Feldern/Stationen.
/// </summary>
public static class Outposts
{
    public enum Role { Satellite, Light, Medium, Heavy }

    public static bool IsOutpost(Universe u, Station st) => StationProtection.KindOf(u, st) == GovernmentKind.Outpost;

    /// <summary>Rolle eines Kleinstationstyps aus seinen Daten: ohne Geschütze Satellit, sonst nach Zahl der Geschütze (≤ 2 leicht, ≤ 3 mittel, mehr schwer).</summary>
    public static Role RoleOf(StationType t)
    {
        int guns = t.Defense?.Guns.Count ?? 0;
        return guns == 0 ? Role.Satellite : guns <= 2 ? Role.Light : guns <= 3 ? Role.Medium : Role.Heavy;
    }

    /// <summary>Kleinstationstyp einer Rolle (der billigste, falls mehrere passen); null = keiner in dieser Welt.</summary>
    public static StationType? TypeFor(Universe u, Role role) =>
        u.StationTypes.Values.Where(t => t.Government == GovernmentKind.Outpost && RoleOf(t) == role).OrderBy(t => t.BuildCost).ThenBy(t => t.Key, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>Kleinstationen eines Besitzers im Sektor (eine Rolle oder alle).</summary>
    public static int Count(Universe u, SectorId sector, ActorId owner, Role? role = null) =>
        u.Sectors.TryGetValue(sector, out var s)
            ? s.Stations.Count(id => u.Stations.TryGetValue(id, out var st) && st.Owner == owner && !st.IsBuildSite && IsOutpost(u, st)
                                     && (role is null || RoleOf(u.StationTypes[st.TypeKey]) == role))
            : 0;

    /// <summary>
    /// Freier Platz nahe <paramref name="anchor"/> (Ringe um den Anker, feste Reihenfolge, kein Zufall): Abstand zu Stationen Summe der Radien + 15, zu Toren
    /// Radius + 25 und nicht auf der Seite der Zollstation, zu Feldmitten Radius + 20, zu Asteroiden Radius + Asteroid + 4.
    /// </summary>
    public static Vec2? FreeSpot(Universe u, Sector s, Vec2 anchor, float radius, float minDist, float maxDist)
    {
        for (float d = minDist; d <= maxDist; d += 6f)
            for (int i = 0; i < 16; i++)
            {
                var p = anchor + Vec2.FromAngle(i * MathF.PI / 8f) * d;
                float bound = 210f * MathF.Max(1f, s.Scale);   // Runde 44: Fläche wächst mit dem Maßstab (fest 210 ließ in vergrößerten Sektoren keine Stellung am Tor zu)
                if (MathF.Abs(p.X) > bound || MathF.Abs(p.Y) > bound) continue;
                if (Fits(u, s, p, radius)) return p;
            }
        return null;
    }

    static bool Fits(Universe u, Sector s, Vec2 p, float r)
    {
        foreach (var id in s.Stations) if (u.Stations.TryGetValue(id, out var st) && Vec2.Distance(p, st.Position) < r + st.Radius + 15f) return false;
        foreach (var gid in s.Gates)
        {
            if (!u.Gates.TryGetValue(gid, out var g)) continue;
            if (Vec2.Distance(p, g.Position) < r + 25f) return false;
            if (Vec2.Distance(p, g.Position) < r + 45f && Dot(p - g.Position, CustomsSide(u, s, g)) > 0f) return false;   // Zollstation des Tormodells
        }
        foreach (var f in s.Fields) if (Vec2.Distance(p, f.Position) < r + 20f) return false;
        foreach (var a in s.Asteroids) if (Vec2.Distance(p, a.Position) < r + a.Radius + 4f) return false;
        return true;
    }

    static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    /// <summary>Seite der Zollstation eines Tors (wie die Ansicht das Tormodell dreht: Durchflugachse von der Sektormitte zum Tor, Station bei Modell +X).</summary>
    public static Vec2 CustomsSide(Universe u, Sector s, Gate g)
    {
        var pts = s.Stations.Where(u.Stations.ContainsKey).Select(id => u.Stations[id].Position).Concat(s.Fields.Select(f => f.Position))
            .Concat(s.Gates.Where(id => id != g.Id && u.Gates.ContainsKey(id)).Select(id => u.Gates[id].Position)).ToList();
        var c = pts.Count == 0 ? new Vec2(0f, 0f) : pts.Aggregate(new Vec2(0f, 0f), (a, b) => a + b) * (1f / pts.Count);
        var outward = g.Position - c;
        if (outward.Length < 1e-3f) outward = g.Position;
        if (outward.Length < 1e-3f) return new Vec2(1f, 0f);
        float yaw = MathF.Atan2(outward.X, outward.Y);
        return new Vec2(MathF.Cos(yaw), -MathF.Sin(yaw));
    }

    /// <summary>Ist das Schiff ein Gegner, auf den die Stellung von sich aus feuert (dieselbe Regel wie Stationsgeschütze: Krieg, feindliche Beziehung, Gesuchter ≥ 3)?</summary>
    public static bool Hostile(Universe u, Station st, Ship ship)
    {
        if (u.FactionOf(st) is not { } f && !u.Actors.ContainsKey(st.Owner)) return false;
        if (ship.Owner == st.Owner) return false;
        // Runde 13b: ein Gesuchter ist auch für Stellungen der eigenen Fraktion ein Ziel (wie für ihre Polizei)
        if (ship.IsPlayer && u.FactionOf(st) is { } fw && Wanted.LevelOf(u, ship.Owner, fw) >= u.Rules.TradeRefusalWantedLevel) return true;
        if (u.FactionOf(ship) is { } sf && u.FactionOf(st) == sf) return false;
        if (Wars.AtWar(u, st.Owner, ship.Owner)) return Wars.Hostile(u, st.Owner, ship.Owner);   // Runde 40: im Krieg zählt nur Hostile (Waffenstillstand sperrt)
        if (Relations.Effective(u, st.Owner, ship.Owner) <= u.Rules.HostileRelation) return true;
        return ship.IsPlayer && u.FactionOf(st) is { } fs && Wanted.LevelOf(u, ship.Owner, fs) >= u.Rules.TradeRefusalWantedLevel;
    }
}

/// <summary>
/// Phase 30: Waffenstellungen eröffnen von sich aus das Feuer (Abwehrgefecht über <see cref="Siege.StartDefense"/>; das Ziel steht auf der Angreiferseite, Polizei
/// und Marine der Fraktion im Sektor helfen wie bei einer Belagerung). Runde 13b (Prüfung: Piratenschwund, Entscheidung Vorwarnung):
/// <list type="bullet">
/// <item>Kriegsgegner in voller Geschützreichweite.</item>
/// <item>Andere Gegner (Kaperer) nur im Nahbereich (<see cref="EconomyRules.OutpostCloseRange"/> ab Rumpf) — sonst schießt die Stellung nur in Überfällen und
/// Gefechten in ihrer Reichweite mit (Stationsgeschütz); ein lauernder Kaperer weiter draußen wird nicht angegriffen.</item>
/// <item>Den gesuchten Spieler (Stufe ≥ 3) warnt sie zuerst wie die Polizei (<see cref="EconomyRules.PlayerWarningTicks"/>, Ergeben möglich); Feuer erst nach
/// Ablauf, wenn er dann noch in Reichweite, nicht angedockt und gesucht ist.</item>
/// </list>
/// Eine Stellung führt höchstens ein Gefecht zugleich.
/// </summary>
public sealed class OutpostDefenseSystem : ISimSystem
{
    public const int Interval = 5;

    public void Step(Universe u, int dt)
    {
        if (!u.Rules.OutpostsEngage || !u.Rules.Warfare || !u.Rules.StationGunsActive) return;
        ResolveWarnings(u);
        foreach (var st in u.StationsById())
        {
            if (st.IsBuildSite || !Outposts.IsOutpost(u, st) || StationCombat.IsDown(u, st) || Siege.Of(u, st.Id) is not null) continue;
            float reach = StationCombat.GunRange(u, st);
            if (reach <= 0f || !u.Sectors.TryGetValue(st.Sector, out var sector)) continue;
            float close = st.Radius + u.Rules.OutpostCloseRange;
            Ship? best = null; float bd = reach;
            foreach (var id in u.Tracker.ParkedIn(sector.Id))
            {
                if (!u.Ships.TryGetValue(id, out var s) || s.DockedAt.Value != 0 || u.Fights.IsEngaged(s.Id) || s.IsDerelict || !s.Has(Capability.Combat) && !s.IsPlayer) continue;
                if (s.IsPlayer && s.Id != u.PlayerShip) continue;   // geparkte eigene Flottenschiffe nicht von sich aus
                if (Flight.Cloak.HiddenFrom(u, s, u.Rules.StationSensorStrength)) continue;   // Tarnfeld: die Plattform bemerkt das Schiff nicht
                if (PirateSystem.AtHome(u, s) || !Outposts.Hostile(u, st, s)) continue;
                float d = Vec2.Distance(s.Position, st.Position);
                if (d > bd) continue;
                if (s.IsPlayer)
                {
                    if (!Wars.AtWar(u, st.Owner, s.Owner)) { Warn(u, st, s); continue; }   // Gesuchter: erst warnen
                }
                else if (!Wars.AtWar(u, st.Owner, s.Owner) && d > close) continue;   // Kaperer: nur im Nahbereich
                bd = d; best = s;
            }
            if (best is not null) Siege.StartDefense(u, st, best);
        }
    }

    /// <summary>Warnung an den gesuchten Spieler (einmal; nicht während einer anderen Ankündigung, nicht in der Festnahme-Sperre).</summary>
    static void Warn(Universe u, Station st, Ship player)
    {
        if (u.PendingEncounters.Any(e => e.Target == player.Id)) return;
        if (u.Actors.TryGetValue(player.Owner, out var a) && u.Rules.ArrestCooldownTicks > 0 && u.Tick < a.ArrestCooldownUntil) return;
        var pending = new PendingEncounter { Kind = EncounterKind.Intercept, Target = player.Id, Sector = st.Sector, Station = st.Id, DueTick = u.Tick + u.Rules.PlayerWarningTicks };
        u.PendingEncounters.Add(pending);
        u.Events.Publish(new EncounterWarning(EncounterKind.Intercept, st.Sector, default, player.Id, pending.DueTick,
            $"{st.Name}: Du wirst gesucht — verlasse den Geschützbereich ({StationCombat.GunRange(u, st):0} m) oder ergib dich ({u.Rules.PlayerWarningTicks / SimClock.TicksPerSecond:0} s), sonst eröffnet sie das Feuer."));
    }

    /// <summary>Fällige Warnungen der Stellungen: noch in Reichweite, frei und gesucht → Abwehrgefecht; sonst entkommen (mit Festnahme-Sperre gegen Schleifen).</summary>
    static void ResolveWarnings(Universe u)
    {
        if (u.PendingEncounters.Count == 0) return;
        foreach (var e in u.PendingEncounters.Where(e => e.Station.Value != 0 && u.Tick >= e.DueTick).ToList())
        {
            u.PendingEncounters.Remove(e);
            if (!u.Stations.TryGetValue(e.Station, out var st) || !u.Ships.TryGetValue(e.Target, out var player)) continue;
            bool fire = player.Id == u.PlayerShip && player.Sector == st.Sector && player.DockedAt.Value == 0 && !u.Fights.IsEngaged(player.Id)
                        && !StationCombat.IsDown(u, st) && Siege.Of(u, st.Id) is null && Outposts.Hostile(u, st, player)
                        && Vec2.Distance(player.Position, st.Position) <= StationCombat.GunRange(u, st);
            SecuritySystem.ArrestCooldown(u, player.Owner);
            if (fire) Siege.StartDefense(u, st, player);
            else u.Events.Publish(new EncounterEscaped(EncounterKind.Intercept, default, player.Id, $"{st.Name}: Du hast den Geschützbereich verlassen."));
        }
    }
}

/// <summary>
/// Phase 30: Fraktionen (Staat, Gilde) und Piratengruppen stellen Kleinstationen auf — aus <see cref="FactionViability.Spendable"/> (nie unter die Rücklage),
/// Bau direkt als Fertigmodul (Begründung: kleine Einheiten ohne Markt; über die Baustellen würden sie die wenigen Bauplätze der Wirtschaft belegen —
/// der Kaufpreis ist eine Geldsenke wie ein Polizeikauf). Regeln: Satellit in Gebietssektoren ohne Abdeckung; leichte Stellung an Toren/Feldern bei anhaltender
/// Bedrohung; mittlere an Hauptroutentoren bei hoher Bedrohung; schwere nur im Krieg an Toren zum Gegner und am Hauptquartier; Piraten leicht/mittel am Versteck.
/// Obergrenzen je Sektor und Fraktion. Abbau einer selbst gebauten Waffenstellung nach langer ruhiger Lage (Grund c, „ruhig“).
/// </summary>
public sealed class OutpostBuilderSystem : ISimSystem
{
    public const int Interval = 100;

    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        if (!r.OutpostsBuild) return;
        Dismantle(u, dt);
        foreach (var a in u.Actors.Values.OrderBy(x => x.Id.Value).ToList())
        {
            if (a.Kind is not (ActorKind.State or ActorKind.Guild or ActorKind.Outlaw)) continue;
            if (u.Tick - a.LastOutpostTick < r.OutpostBuildCooldown && a.LastOutpostTick != 0) continue;
            if (a.Kind == ActorKind.Outlaw) { if (r.OutpostPirates) Pirates(u, a); continue; }
            if (!a.IsNpcFaction(u)) continue;
            var f = new FactionId(a.Id.Value);
            if (TryBuild(u, a, f)) a.LastOutpostTick = u.Tick;
        }
    }

    static bool TryBuild(Universe u, Actor a, FactionId f)
    {
        var r = u.Rules;
        var territory = u.Sectors.Values.Where(s => s.Owner == f).OrderBy(s => s.Id.Value).ToList();
        int Total(Outposts.Role role) => u.Stations.Values.Count(st => st.Owner == a.Id && !st.IsBuildSite && Outposts.IsOutpost(u, st) && Outposts.RoleOf(u.StationTypes[st.TypeKey]) == role);
        bool atWar = u.Wars.Any(w => w.A == f || w.B == f);
        // schwer: nur im Krieg — an Toren zum Gegner, sonst am Hauptquartier
        if (atWar && Outposts.TypeFor(u, Outposts.Role.Heavy) is { } heavy && Total(Outposts.Role.Heavy) < r.OutpostHeavyPerFaction)
            foreach (var s in territory)
            {
                if (Outposts.Count(u, s.Id, a.Id, Outposts.Role.Heavy) >= 1) continue;
                var gate = s.Gates.Select(id => u.Gates[id]).FirstOrDefault(g => u.Sectors[u.Gates[g.Target].Sector].Owner is var o && o.Value != 0 && Wars.Between(u, f, o) is not null);
                var anchor = gate?.Position ?? HqPosition(u, s, f);
                if (anchor is { } an && Build(u, a, heavy, s, an)) return true;
            }
        // mittel: Hauptroutentore bei hoher Bedrohung
        if (Outposts.TypeFor(u, Outposts.Role.Medium) is { } medium && Total(Outposts.Role.Medium) < r.OutpostMediumPerFaction)
            foreach (var s in territory.Where(s => s.Threat >= r.OutpostMediumThreat).OrderByDescending(s => s.Threat).ThenBy(s => s.Id.Value))
            {
                if (Outposts.Count(u, s.Id, a.Id, Outposts.Role.Medium) >= 1) continue;
                var gate = s.Gates.Select(id => u.Gates[id]).Where(g => g.MainRoute).OrderBy(g => g.Id.Value).FirstOrDefault();
                if (gate is not null && Build(u, a, medium, s, gate.Position)) return true;
            }
        // leicht: Tore/Felder bei anhaltender Bedrohung
        if (Outposts.TypeFor(u, Outposts.Role.Light) is { } light && Total(Outposts.Role.Light) < r.OutpostLightPerFaction)
            foreach (var s in territory.Where(s => s.Threat >= r.OutpostLightThreat).OrderByDescending(s => s.Threat).ThenBy(s => s.Id.Value))
            {
                if (Outposts.Count(u, s.Id, a.Id, Outposts.Role.Light) >= r.OutpostLightPerSector) continue;
                var anchors = s.Gates.Select(id => u.Gates[id].Position).Concat(s.Fields.Select(fl => fl.Position)).ToList();
                foreach (var an in anchors) if (Build(u, a, light, s, an)) return true;
            }
        // Satellit: Gebietssektoren ohne Abdeckung
        if (Outposts.TypeFor(u, Outposts.Role.Satellite) is { } sat && Total(Outposts.Role.Satellite) < r.OutpostSatellitesPerFaction)
            foreach (var s in territory.Where(s => !ThreatMap.HasCoverage(u, s, f)))
            {
                if (Outposts.Count(u, s.Id, a.Id, Outposts.Role.Satellite) >= 1) continue;
                var anchor = HqPosition(u, s, f) ?? (s.Stations.Count > 0 ? u.Stations[s.Stations[0]].Position : new Vec2(0f, 0f));
                if (Build(u, a, sat, s, anchor)) return true;
            }
        return false;
    }

    static void Pirates(Universe u, Actor a)
    {
        var r = u.Rules;
        var hideout = u.StationsById().FirstOrDefault(st => st.Owner == a.Id && StationProtection.IsHideout(u, st));
        if (hideout is null || !u.Sectors.TryGetValue(hideout.Sector, out var s)) return;
        if (Outposts.Count(u, s.Id, a.Id) >= r.OutpostPiratePerHideout) return;
        var type = (Outposts.TypeFor(u, Outposts.Role.Medium) is { } m && a.Treasury.Credits >= m.BuildCost * r.StationBuildCostFactor + r.OutpostPirateReserve) ? m
            : Outposts.TypeFor(u, Outposts.Role.Light);
        if (type is null || a.Treasury.Credits < type.BuildCost * r.StationBuildCostFactor + r.OutpostPirateReserve) return;
        if (Build(u, a, type, s, hideout.Position)) a.LastOutpostTick = u.Tick;
    }

    static Vec2? HqPosition(Universe u, Sector s, FactionId f) =>
        s.Stations.Select(id => u.Stations[id]).Where(st => st.Owner == (ActorId)f && StationProtection.IsGovernment(u, st)).Select(st => (Vec2?)st.Position).FirstOrDefault();

    /// <summary>Kleinstation kaufen und aufstellen (Fertigmodul): Kasse zahlt Baukosten × Baukostenfaktor, wenn <see cref="FactionViability.Spendable"/> reicht.</summary>
    public static bool Build(Universe u, Actor a, StationType type, Sector s, Vec2 anchor)
    {
        float cost = type.BuildCost * u.Rules.StationBuildCostFactor;
        if (FactionViability.Spendable(u, a) < cost) return false;
        if (Outposts.FreeSpot(u, s, anchor, type.Radius, type.Radius + 18f, 70f) is not { } spot) return false;
        a.Treasury.Credits -= cost;
        u.Events.Publish(new TreasuryUpkeep(a.Id, "outpost", cost, 0f));
        var id = u.NewStationId();
        var st = StationFactory.Create(u, id, type, StationFactory.BuiltName(u, type, s.Id), s.Id, spot, a.Id);
        st.Credits = 0f;
        st.CalmSince = -1;
        u.AddStation(st);
        u.Events.Publish(new StationBuilt(id, s.Id, type.Key));
        u.Events.Publish(new OutpostBuilt(id, s.Id, a.Id, type.Key, cost));
        return true;
    }

    /// <summary>Abbau einer selbst gebauten Waffenstellung (Startwelt-Stellungen bleiben), wenn ihr Sektor lange ruhig ist und kein Krieg herrscht (Grund c).</summary>
    static void Dismantle(Universe u, int dt)
    {
        var r = u.Rules;
        foreach (var st in u.StationsById().ToList())
        {
            if (st.IsBuildSite || !Outposts.IsOutpost(u, st) || st.BuiltTick <= 0 || !u.StationTypes.TryGetValue(st.TypeKey, out var t)) continue;
            if (Outposts.RoleOf(t) == Outposts.Role.Satellite || !u.Sectors.TryGetValue(st.Sector, out var s)) continue;
            bool war = u.FactionOf(st) is { } f && u.Wars.Any(w => w.A == f || w.B == f);
            bool calm = s.Threat < r.OutpostCalmThreat && !war && Siege.Of(u, st.Id) is null;
            if (!calm) { st.CalmSince = -1; continue; }
            if (st.CalmSince < 0) { st.CalmSince = u.Tick; continue; }
            if (u.Tick - st.CalmSince >= r.OutpostCalmTicks) Departures.RemoveStation(u, st, DepartureReason.Unprofitable, "calm");
        }
    }
}
