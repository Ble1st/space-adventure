using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 30 (Runde 13): Stationsgrößen (S/M/L/XL wie Schiffe, Radius, Grundhülle, Geschützzahl, Andockregel) und Kleinstationen (Satellit, leichte/mittlere/
/// schwere Waffenstellung, Größe XS, nicht andockbar), dazu die offenen Tests aus Runde 12 (Stationsnamen mit römischer Nummer, Formationsabstand,
/// Verfolgung endet vor den Versteckgeschützen).
/// </summary>
public class Phase30Tests
{
    static Universe Standard(ulong? seed = null) => TestWorlds.Default(seed, fuel: false, research: false);

    static void Place(Universe u, Ship s, SectorId sector, Vec2 pos)
    {
        if (s.Transit is not null) u.FinishTransit(s);
        if (s.Sector != sector) u.MoveShip(s, sector);
        s.Position = pos;
        s.DockedAt = default;
        s.Order = null;
        s.Command = null;
    }

    // ───────── Größen ─────────

    [Fact]
    public void Groessen_JederTypHatGroesse_RadiusUndAndockregelPassen()
    {
        var u = Standard();
        foreach (var t in u.StationTypes.Values)
        {
            if (t.Size == StationSize.XS) { Assert.False(t.Dockable, t.Key); Assert.True(t.Radius < 10f, t.Key); continue; }
            Assert.True(t.Dockable, t.Key);
            Assert.Equal(StationSizes.Radius(t.Size), t.Radius);
        }
        Assert.Equal(StationSize.XL, u.StationTypes["headquarters"].Size);
        Assert.Equal(StationSize.L, u.StationTypes["shipyard"].Size);
        Assert.Equal(StationSize.S, u.StationTypes["water_plant"].Size);
        Assert.Equal(StationSize.M, u.StationTypes["steelworks"].Size);
        foreach (var st in u.Stations.Values)
        {
            var t = u.StationTypes[st.TypeKey];
            Assert.Equal(t.Radius, st.Radius);
            Assert.Equal(t.Dockable, st.Dockable);
            Assert.Equal(st.Radius + 7f, Vec2.Distance(st.DockPoint(new ShipId(1)), st.Position), 2);
        }
        // Baukosten nach Größe (S 0,9 · L 1,1 · XL 1,2 aus den Daten)
        var raw = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson()).StationTypes.First(x => x.Key == "shipyard");
        Assert.Equal(raw.BuildCost * 1.1f, u.StationTypes["shipyard"].BuildCost, 1);
    }

    [Theory]
    [InlineData("water_plant", "\"size\"", "\"XXL\"", "unbekannte Stationsgröße")]
    [InlineData("satellite_sensor", "\"services\"", "[\"trade\"]", "nicht andockbar")]
    [InlineData("satellite_sensor", "\"radius\"", "null", "radius")]
    public void Groessen_UngueltigWirft(string type, string field, string value, string message)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(WorldBuilder.DefaultWorldJson())!;
        var t = node["stationTypes"]!.AsArray().First(x => (string?)x!["key"] == type)!;
        string f = field.Trim('"');
        if (value == "null") t.AsObject().Remove(f); else t[f] = System.Text.Json.Nodes.JsonNode.Parse(value);
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(WorldBuilder.Parse(node.ToJsonString())));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void Groessen_ZuVieleGeschuetzeFuerDieGroesseWirft()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(WorldBuilder.DefaultWorldJson())!;
        var t = node["stationTypes"]!.AsArray().First(x => (string?)x!["key"] == "headquarters")!;   // XL mit 6 Geschützen
        t["size"] = "S";   // S: höchstens 2
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(WorldBuilder.Parse(node.ToJsonString())));
        Assert.Contains("höchstens 2", ex.Message);
    }

    [Fact]
    public void Andocken_GrosseSchiffeNurAnGrossenStationen_KleinstationGarNicht()
    {
        var u = Standard();
        var me = u.Ships[u.PlayerShip];
        var water = u.Stations.Values.First(s => s.TypeKey == "water_plant");
        var yard = u.Stations.Values.First(s => s.TypeKey == "shipyard");
        var steel = u.Stations.Values.First(s => s.TypeKey == "steelworks");
        var sat = u.Stations.Values.First(s => s.TypeKey == "satellite_sensor");
        // Spieler-Skiff/Jäger (S): überall außer an der Kleinstation
        Assert.True(Docking.Allows(u, water, me));
        Assert.False(Docking.Allows(u, sat, me));
        Assert.Contains("Kleinstation", Docking.Issue(u, sat, me));
        // Fregatte (L): nicht an S (bis M), aber an M (bis L) und L
        me.ModelKey = "bastion"; me.Slots = new(); me.Fit = new();
        Assert.Equal(SlotSize.L, Docking.SizeOf(u, me));
        Assert.False(Docking.Allows(u, water, me));
        Assert.Contains("zu klein", Docking.Issue(u, water, me));
        Assert.True(Docking.Allows(u, steel, me));
        Assert.True(Docking.Allows(u, yard, me));
        // Kleinstation: kein Liegeplatz, keine Schutzzone
        Assert.Equal(0f, sat.SafeRadius(u.Rules));
        Assert.Equal(default, u.StationAt(sat.Sector, sat.DockPoint(me.Id)));
        Assert.True(yard.SafeRadius(u.Rules) > water.SafeRadius(u.Rules));
    }

    [Fact]
    public void Platzierung_KleinstationenUndGrosseStationen_UeberlappenNicht()
    {
        var u = Standard();
        foreach (var s in u.Sectors.Values)
        {
            var list = s.Stations.Select(id => u.Stations[id]).ToList();
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    Assert.True(Vec2.Distance(list[i].Position, list[j].Position) >= list[i].Radius + list[j].Radius + 10f,
                        $"{list[i].Name} ↔ {list[j].Name}");
            foreach (var st in list)
                foreach (var a in s.Asteroids)
                    Assert.True(Vec2.Distance(st.Position, a.Position) >= st.Radius + a.Radius, $"{st.Name} im Asteroiden {a.Id}");
        }
    }

    // ───────── Kleinstationen ─────────

    [Fact]
    public void Kleinstationen_Startwelt_RollenAusDenDaten()
    {
        var u = Standard();
        Assert.Equal(Outposts.Role.Satellite, Outposts.RoleOf(u.StationTypes["satellite_sensor"]));
        Assert.Equal(Outposts.Role.Light, Outposts.RoleOf(u.StationTypes["platform_light"]));
        Assert.Equal(Outposts.Role.Medium, Outposts.RoleOf(u.StationTypes["platform_medium"]));
        Assert.Equal(Outposts.Role.Heavy, Outposts.RoleOf(u.StationTypes["platform_heavy"]));
        var outposts = u.Stations.Values.Where(s => Outposts.IsOutpost(u, s)).ToList();
        Assert.Equal(3, outposts.Count(s => s.TypeKey == "satellite_sensor"));
        Assert.Equal(3, outposts.Count(s => s.TypeKey == "platform_light"));
        Assert.All(outposts, s => { Assert.False(s.Dockable); Assert.False(StationProtection.IsEconomic(u, s)); });
        // Kleinstationen sind keine Heimatbasis der Polizei
        Assert.DoesNotContain(u.Ships.Values.Where(s => s.Role == ShipRole.Police), p => outposts.Any(o => o.Id == p.HomeBase));
    }

    [Fact]
    public void Waffenstellung_KapererNurImNahbereich_HaendlerNie()
    {
        var u = Standard();
        var plat = u.Stations.Values.First(s => s.TypeKey == "platform_light");
        var raider = u.Ships.Values.Where(ThreatMap.IsRaider).OrderBy(s => s.Id.Value).First();
        float reach = StationCombat.GunRange(u, plat);
        Assert.True(reach > plat.Radius + u.Rules.OutpostCloseRange + 10f);
        // Runde 13b: ein lauernder Kaperer in Reichweite, aber außerhalb des Nahbereichs — kein Abwehrgefecht (Piraten sterben sonst an den Stellungen aus)
        Place(u, raider, plat.Sector, plat.Position + new Vec2(reach - 5f, 0f));
        new OutpostDefenseSystem().Step(u, OutpostDefenseSystem.Interval);
        Assert.Null(Siege.Of(u, plat.Id));
        // im Nahbereich: Abwehrgefecht
        raider.Position = plat.Position + new Vec2(plat.Radius + u.Rules.OutpostCloseRange - 3f, 0f);
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        new OutpostDefenseSystem().Step(u, OutpostDefenseSystem.Interval);
        var fight = Assert.IsType<Encounter>(Siege.Of(u, plat.Id));
        Assert.True(fight.Of(raider.Id) is not null, "der Kaperer steht im Abwehrgefecht");
        Assert.Contains(events, e => e is OutpostEngaged oe && oe.Station == plat.Id && oe.Target == raider.Id);
        // ein Händler (nicht feindlich) daneben löst nichts aus
        var u2 = Standard();
        var p2 = u2.Stations[plat.Id];
        var trader = u2.Ships.Values.First(s => s.Role == ShipRole.Trader && s.Controller == Controller.Ai);
        Place(u2, trader, p2.Sector, p2.Position + new Vec2(p2.Radius + 8f, 0f));
        new OutpostDefenseSystem().Step(u2, OutpostDefenseSystem.Interval);
        Assert.Null(Siege.Of(u2, p2.Id));
        // Schalter aus: keine Abwehr
        var u3 = Standard();
        u3.Rules.OutpostsEngage = false;
        var p3 = u3.Stations[plat.Id];
        Place(u3, u3.Ships[raider.Id], p3.Sector, p3.Position + new Vec2(p3.Radius + 8f, 0f));
        new OutpostDefenseSystem().Step(u3, OutpostDefenseSystem.Interval);
        Assert.Null(Siege.Of(u3, p3.Id));
    }

    static (Universe U, Station Plat, Ship Me) WantedNearPlatform()
    {
        var u = Standard();
        var plat = u.Stations.Values.First(s => s.TypeKey == "platform_light");
        var me = u.Ships[u.PlayerShip];
        Place(u, me, plat.Sector, plat.Position + new Vec2(plat.Radius + 25f, 0f));
        u.PlayerSector = plat.Sector;
        var f = u.FactionOf(plat)!.Value;
        Wanted.Report(u, u.PlayerActor, f, "assault");
        Assert.True(Wanted.LevelOf(u, u.PlayerActor, f) >= 3);
        u.Player.Treasury.Credits = 50_000f;
        return (u, plat, me);
    }

    [Fact]
    public void Waffenstellung_WarntGesuchtenSpielerErst_FeuerNachAblauf()
    {
        var (u, plat, me) = WantedNearPlatform();
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        new OutpostDefenseSystem().Step(u, OutpostDefenseSystem.Interval);
        Assert.Null(Siege.Of(u, plat.Id));
        var pending = Assert.Single(u.PendingEncounters, e => e.Target == me.Id);
        Assert.Equal(plat.Id, pending.Station);
        Assert.Contains(events, e => e is EncounterWarning w && w.Message.Contains(plat.Name));
        Assert.Null(PlayerSurrender.Issue(u));   // Ergeben möglich
        // Speichern mitten in der Warnung: bitgleich
        var (copy, _) = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        Assert.Equal(plat.Id, copy.PendingEncounters.Single().Station);
        // vor Ablauf kein Feuer
        u.Tick = pending.DueTick - 1;
        new OutpostDefenseSystem().Step(u, OutpostDefenseSystem.Interval);
        Assert.Null(Siege.Of(u, plat.Id));
        // nach Ablauf, noch in Reichweite: Abwehrgefecht gegen den Spieler
        u.Tick = pending.DueTick;
        new OutpostDefenseSystem().Step(u, OutpostDefenseSystem.Interval);
        var fight = Assert.IsType<Encounter>(Siege.Of(u, plat.Id));
        Assert.True(fight.Of(me.Id) is not null);
        // die Polizei-Kontrolle greift die Stellungs-Warnung nicht auf
        Assert.Empty(u.PendingEncounters);
    }

    [Fact]
    public void Waffenstellung_Warnung_AusDerReichweite_Entkommen_OderErgeben()
    {
        var (u, plat, me) = WantedNearPlatform();
        new OutpostDefenseSystem().Step(u, OutpostDefenseSystem.Interval);
        var pending = u.PendingEncounters.Single();
        me.Position = plat.Position + new Vec2(StationCombat.GunRange(u, plat) + 30f, 0f);
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        u.Tick = pending.DueTick;
        new OutpostDefenseSystem().Step(u, OutpostDefenseSystem.Interval);
        Assert.Null(Siege.Of(u, plat.Id));
        Assert.Contains(events, e => e is EncounterEscaped);
        // Ergeben: Strafe an die Fraktion der Stellung, Fahndung gelöscht
        var (u2, plat2, _) = WantedNearPlatform();
        new OutpostDefenseSystem().Step(u2, OutpostDefenseSystem.Interval);
        var f = u2.FactionOf(plat2)!.Value;
        float before = u2.Player.Treasury.Credits;
        var ev = PlayerSurrender.Surrender(u2);
        Assert.NotNull(ev);
        Assert.True(ev!.ToPolice);
        Assert.Empty(u2.PendingEncounters);
        Assert.False(u2.Player.Wanted.ContainsKey(f));
        Assert.True(u2.Player.Treasury.Credits < before);
    }

    [Fact]
    public void Zuflucht_NurAndockbareStationen()
    {
        var u = Standard();
        var plat = u.Stations.Values.First(s => s.TypeKey == "platform_light");
        var trader = u.Ships.Values.First(s => s.Role == ShipRole.Trader && s.Controller == Controller.Ai);
        var raider = u.Ships.Values.Where(ThreatMap.IsRaider).OrderBy(s => s.Id.Value).First();
        Place(u, trader, plat.Sector, plat.Position + new Vec2(plat.Radius + 6f, 0f));
        Place(u, raider, plat.Sector, plat.Position + new Vec2(plat.Radius + 60f, 0f));
        var e = Engage.StartRaid(u, raider, trader, u.Sectors[plat.Sector]);
        var p = e.Of(trader.Id)!;
        Assert.NotEqual(plat.Id.Value, p.RefugeId);
        // eine zu kleine Station (Wasserwerk, S) ist für eine Fregatte keine Zuflucht
        var water = u.Stations.Values.First(s => s.TypeKey == "water_plant");
        var u2 = Standard();
        var w2 = u2.Stations[water.Id];
        var t2 = u2.Ships[trader.Id];
        t2.ModelKey = "bastion"; t2.Slots = new(); t2.Fit = new();
        Place(u2, t2, w2.Sector, w2.Position + new Vec2(w2.Radius + 6f, 0f));
        var r2 = u2.Ships[raider.Id];
        Place(u2, r2, w2.Sector, w2.Position + new Vec2(w2.Radius + 60f, 0f));
        var e2 = Engage.StartRaid(u2, r2, t2, u2.Sectors[w2.Sector]);
        Assert.NotEqual(w2.Id.Value, e2.Of(t2.Id)!.RefugeId);
    }

    [Fact]
    public void Kaperer_LauernNichtInStellungsreichweite_PiratenBauenKeineStellungen()
    {
        var u = Standard();
        var raider = u.Ships.Values.Where(ThreatMap.IsRaider).OrderBy(s => s.Id.Value).First();
        var platSectors = u.Stations.Values.Where(s => s.TypeKey == "platform_light").Select(s => s.Sector).ToHashSet();
        int checkedSpots = 0;
        for (int i = 0; i < 400; i++)
        {
            if (PirateSystem.PickHunt(u, raider) is not { } h || !platSectors.Contains(h.Sector.Id)) continue;
            checkedSpots++;
            foreach (var st in h.Sector.Stations.Select(id => u.Stations[id]).Where(s => s.TypeKey == "platform_light"))
                Assert.True(Vec2.Distance(h.Spot, st.Position) > StationCombat.GunRange(u, st), $"Lauerposten in Reichweite von {st.Name}");
        }
        Assert.True(checkedSpots > 0);
        // Piraten kaufen keine Stellungen (Regel aus), auch mit voller Kasse
        foreach (var a in u.Actors.Values.Where(a => a.Kind == ActorKind.Outlaw)) a.Treasury.Credits = 1_000_000f;
        int n = u.Stations.Count;
        new OutpostBuilderSystem().Step(u, OutpostBuilderSystem.Interval);
        Assert.DoesNotContain(u.Stations.Values, s => Outposts.IsOutpost(u, s) && u.Actors[s.Owner].Kind == ActorKind.Outlaw);
    }

    [Fact]
    public void Fraktion_BautStellungBeiBedrohung_UndBautSieNachLangerRuheAb()
    {
        var u = Standard();
        u.Tick = 100;
        var union = u.Actors[new ActorId(1)];
        union.Treasury.Credits = 1_000_000f;
        var sector = u.Sectors.Values.Where(s => s.Owner == new FactionId(1)).OrderBy(s => s.Id.Value).First();
        sector.Threat = 1.5f;   // leichte Stellung (≥ 1,2), keine mittlere (< 2,2)
        int before = Outposts.Count(u, sector.Id, union.Id, Outposts.Role.Light);
        var built = new List<OutpostBuilt>();
        u.Events.Raised += e => { if (e is OutpostBuilt ob) built.Add(ob); };
        float money = union.Treasury.Credits;
        new OutpostBuilderSystem().Step(u, OutpostBuilderSystem.Interval);
        var b = Assert.Single(built);
        Assert.Equal("platform_light", b.TypeKey);
        Assert.Equal(sector.Id, b.Sector);
        Assert.Equal(before + 1, Outposts.Count(u, sector.Id, union.Id, Outposts.Role.Light));
        Assert.Equal(money - b.Cost, union.Treasury.Credits, 1);
        var st = u.Stations[b.Station];
        Assert.False(st.Dockable);
        Assert.Equal(union.Id, st.Owner);
        // Abklingzeit: sofort kein zweiter Bau
        new OutpostBuilderSystem().Step(u, OutpostBuilderSystem.Interval);
        Assert.Single(built);
        // lange Ruhe → Abbau (Grund c, „ruhig“); Startwelt-Stellungen bleiben
        sector.Threat = 0f;
        var gone = new List<StationDeparted>();
        u.Events.Raised += e => { if (e is StationDeparted d) gone.Add(d); };
        u.Tick += 200; new OutpostBuilderSystem().Step(u, 100);
        Assert.Equal(u.Tick, st.CalmSince);
        u.Tick += u.Rules.OutpostCalmTicks; new OutpostBuilderSystem().Step(u, 100);
        Assert.False(u.Stations.ContainsKey(st.Id));
        var d = Assert.Single(gone, x => x.Station == st.Id);
        Assert.Equal(DepartureReason.Unprofitable, d.Reason);
        Assert.Contains(u.Stations.Values, s => s.TypeKey == "platform_light" && s.BuiltTick == 0);
    }

    [Fact]
    public void Fraktion_BautNieUnterDieRuecklage()
    {
        var u = Standard();
        var union = u.Actors[new ActorId(1)];
        union.Treasury.Credits = FactionViability.ReserveOf(u, union) + 100f;
        foreach (var s in u.Sectors.Values.Where(s => s.Owner == new FactionId(1))) s.Threat = 3f;
        int n = u.Stations.Count;
        new OutpostBuilderSystem().Step(u, OutpostBuilderSystem.Interval);
        Assert.Equal(n, u.Stations.Count);
    }

    [Fact]
    public void Speichern_MitKleinstationUndRuhezeit_Bitgleich()
    {
        var u = Standard();
        u.Tick = 100;
        var union = u.Actors[new ActorId(1)];
        union.Treasury.Credits = 1_000_000f;
        u.Sectors.Values.Where(s => s.Owner == new FactionId(1)).OrderBy(s => s.Id.Value).First().Threat = 1.5f;
        new OutpostBuilderSystem().Step(u, OutpostBuilderSystem.Interval);
        var built = u.Stations.Values.Single(s => Outposts.IsOutpost(u, s) && s.BuiltTick > 0);
        built.CalmSince = 50;
        var runner = new SimulationRunner();
        runner.Run(u, 200);
        var (copy, _) = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        Assert.Equal(built.Radius, copy.Stations[built.Id].Radius);
        Assert.False(copy.Stations[built.Id].Dockable);
        var r2 = new SimulationRunner();
        for (int i = 0; i < 400; i++) { runner.Step(u); r2.Step(copy); }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
    }

    // ───────── Runde 12 nachgereicht ─────────

    [Fact]
    public void Stationsname_MitRoemischerNummer()
    {
        Assert.Equal("II", StationFactory.Roman(2));
        Assert.Equal("IV", StationFactory.Roman(4));
        Assert.Equal("IX", StationFactory.Roman(9));
        Assert.Equal("XIV", StationFactory.Roman(14));
        Assert.Equal("MCMXCIX", StationFactory.Roman(1999));
        var u = Standard();
        var type = u.StationTypes["electronics_fab"];
        var sector = u.Sectors.Values.First(s => s.Name.EndsWith("Kalypso"));
        string first = StationFactory.BuiltName(u, type, sector.Id);
        Assert.Equal($"{type.Name} Kalypso", first);
        var a = StationFactory.Create(u, u.NewStationId(), type, first, sector.Id, new Vec2(0f, 0f), new ActorId(2));
        u.AddStation(a);
        Assert.Equal($"{type.Name} Kalypso II", StationFactory.BuiltName(u, type, sector.Id));
        // die Station selbst (Umbau einer Baustelle) zählt nicht
        Assert.Equal(first, StationFactory.BuiltName(u, type, sector.Id, a.Id));
    }

    [Fact]
    public void Formation_AbstandNachGroessterKlasse()
    {
        var u = Standard();
        var police = u.Ships.Values.Where(s => s.Role == ShipRole.Police).OrderBy(s => s.Id.Value).Take(2).ToList();
        var sq = Squadrons.Create(u, police[0], "Test", out _)!;
        Squadrons.Join(u, sq, police[1], out _);
        var size = u.ShipClasses[u.ShipModels[police[0].ModelKey].ClassKey].Size;
        float expect = size switch { SlotSize.S => 7f, SlotSize.M => 10f, SlotSize.L => 16f, _ => 24f };
        Assert.Equal(expect, Squadrons.FormationGap(u, sq));
        police[1].ModelKey = "bastion";   // Fregatte (L) im Verband → 16
        Assert.Equal(16f, Squadrons.FormationGap(u, sq));
        var dir = new Vec2(0f, -1f);
        Assert.Equal(16f, Squadrons.Offset(Formation.Line, 1, dir, 16f).Length, 2);
        Assert.Equal(7f, Squadrons.Offset(Formation.Line, 1, dir).Length, 2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Verfolgung_EndetVorDenVersteckgeschuetzen(bool rule)
    {
        var u = Standard();
        u.Rules.PursuitStopsAtHideoutGuns = rule;
        var raider = u.Ships.Values.Where(ThreatMap.IsRaider).OrderBy(s => s.Id.Value)
            .First(r => PirateSystem.Home(u, r) is { } h && StationCombat.GunRange(u, h) > 0f && u.Sectors[h.Sector].Owner.Value != 0);
        var home = PirateSystem.Home(u, raider)!;
        var owner = u.Sectors[home.Sector].Owner;
        var police = u.Ships.Values.Where(s => s.Role == ShipRole.Police && u.FactionOf(s) == owner).OrderBy(s => s.Id.Value).First();
        float reach = StationCombat.GunRange(u, home);
        float safe = home.Radius + (u.Rules.StationSafeRadius - Station.BodyRadius);
        float d = MathF.Max(safe + 3f, reach + u.Rules.HideoutGunMargin - 2f);
        var dirOut = (new Vec2(0f, 0f) - home.Position).Normalized();
        Place(u, raider, home.Sector, home.Position + dirOut * d);
        Place(u, police, home.Sector, home.Position + dirOut * (d + 120f));
        Assert.False(PirateSystem.AtHome(u, raider));
        var cmd = new PursueCommand(raider.Id, 40f, u.Tick + 1200);
        police.Command = cmd;
        var ended = new List<PursuitEnded>();
        u.Events.Raised += e => { if (e is PursuitEnded p) ended.Add(p); };
        var status = new PursueExecutor().Step(u, police, cmd, 5);
        if (rule)
        {
            Assert.Equal(CommandStatus.Done, status);
            Assert.Equal("safe", Assert.Single(ended).How);
        }
        else Assert.DoesNotContain(ended, p => p.How == "safe");
    }
}
