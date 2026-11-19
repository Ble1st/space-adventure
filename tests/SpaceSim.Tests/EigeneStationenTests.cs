using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Tests;

/// <summary>
/// Eigene Stationen des Spielers (docs/STATIONEN.md „Eigene Stationen bauen“): Baufenster-Katalog, die eine Platzregel (geteilt mit dem KI-Bau), Kosten und
/// Treuhandgeld, Fertigstellung mit eigenem Bauschiff, Lieferung durch NPC-Händler, eigene Obergrenzen, Drehung gespeichert/gehasht, Speichern/Laden mitten
/// im Bau bitgleich, Schalter aus = NPC-Welt gleich, und die Missbrauchsprüfung (keine Geldschleife über eigene Stationen).
/// </summary>
public class EigeneStationenTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    static readonly SectorId Nebelmark = new(9), Alpha = new(1);

    static Universe World(ulong seed = 1) => WorldBuilder.Build(TestWorlds.Def(fuel: false, research: false), seed);

    /// <summary>Spieler reich, aktives Schiff abgedockt in Nebelmark (herrenlos) neben einem freien Bauplatz für <paramref name="type"/>; liefert den Platz.</summary>
    static Vec2 Ready(Universe u, string type, float credits = 300_000f, SectorId? sector = null)
    {
        u.Player.Treasury.Credits = credits;
        var ship = u.Ships[u.PlayerShip];
        if (ship.DockedAt.Value != 0) PlayerFleet.Undock(u, ship);
        var sid = sector ?? Nebelmark;
        u.MoveShip(ship, sid);
        var sec = u.Sectors[sid];
        var t = u.StationTypes[type];
        float half = StationPlacement.HalfArea(sec);
        for (float x = -half + 40f; x < half - 40f; x += 23f)
            for (float y = -half + 40f; y < half - 40f; y += 23f)
            {
                var p = new Vec2(x, y);
                if (!StationPlacement.Fits(u, sec, p, t.Radius)) continue;
                ship.Position = p + new Vec2(t.Radius + 25f, 0f);
                if (PlayerBuilding.PlaceIssue(u, t, p) is null) return p;
            }
        throw new Xunit.Sdk.XunitException($"kein Bauplatz für {type} in {sec.Name}");
    }

    /// <summary>Eigenes Bauschiff (Polier) neben dem Spielerschiff, Befehl „Bauen“ für die Baustelle.</summary>
    static Ship Foreman(Universe u, Station site)
    {
        var m = u.ShipModels["foreman"];
        var at = site;
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        s.DockedAt = default;
        u.AddShip(s);
        Assert.True(Commands.Give(u, s, new BuildCommand(site.Id), out var msg, ignoreCapacity: true), msg);
        return s;
    }

    [Fact]
    public void Schalter_CodeAus_StandardweltAn_KatalogOhneRegierungVersteckForschung()
    {
        Assert.False(new EconomyRules().PlayerBuilding);
        var u = World();
        Assert.True(u.Rules.PlayerBuilding);
        var keys = PlayerBuilding.Catalog(u).Select(t => t.Key).ToHashSet();
        foreach (var k in new[] { "farm", "steelworks", "shipyard", "warehouse", "trade_station", "trade_post", "drive_works", "solar_plant" }) Assert.Contains(k, keys);
        foreach (var k in new[] { "headquarters", "sector_hq", "police_station", "office", "pirate_hideout", "research_station", "build_site", "platform_light", "satellite_sensor" })
            Assert.DoesNotContain(k, keys);
        foreach (var o in PlayerBuilding.Options(u))
        {
            Assert.True(o.Bill.Count > 0, o.Type.Key);
            Assert.Equal(o.Type.BuildCost * u.Rules.StationBuildCostFactor, o.Cost);
        }
    }

    [Fact]
    public void Platzregel_IstDieDesKiBaus_UndNenntDenGrund()
    {
        var u = World();
        var sec = u.Sectors[Alpha];
        // Jeder Platz, den der Lebenszyklus findet, ist für die Platzregel frei (eine Funktion)
        for (int i = 0; i < 20; i++)
            if (StationLifecycleSystem.FindFreeSpot(u, sec, 15f) is { } p) Assert.True(StationPlacement.Check(u, sec, p, 15f).Ok);
        var gate = u.Gates[sec.Gates[0]];
        Assert.Equal(PlacementBlock.Gate, StationPlacement.Check(u, sec, gate.Position + new Vec2(5f, 0f), 15f).Block);
        var st = u.Stations[sec.Stations[0]];
        var c = StationPlacement.Check(u, sec, st.Position + new Vec2(3f, 0f), 15f);
        Assert.True(c.Block is PlacementBlock.Station or PlacementBlock.Gate, c.Reason);
        var field = sec.Fields[0];
        var cf = StationPlacement.Check(u, sec, field.Position, 15f);
        Assert.True(cf.Block is PlacementBlock.Belt or PlacementBlock.Rock or PlacementBlock.Station or PlacementBlock.Gate, cf.Reason);
        Assert.Equal(PlacementBlock.OutsideSector, StationPlacement.Check(u, sec, new Vec2(StationPlacement.HalfArea(sec) + 1f, 0f), 15f).Block);
        if (sec.Asteroids.FirstOrDefault(a => sec.Fields.All(f => Vec2.Distance(f.Position, a.Position) > 60f)
                && sec.Gates.All(g => Vec2.Distance(u.Gates[g].Position, a.Position) > 60f) && sec.Stations.All(s => Vec2.Distance(u.Stations[s].Position, a.Position) > 60f)) is { } rock)
            Assert.Equal(PlacementBlock.Rock, StationPlacement.Check(u, sec, rock.Position, 5f).Block);
    }

    [Fact]
    public void Bedingungen_FremderSektor_Geld_Schiffsabstand_Abgedockt()
    {
        var u = World();
        var t = u.StationTypes["farm"];
        var p = Ready(u, "farm");
        Assert.Null(PlayerBuilding.PlaceIssue(u, t, p));
        var ship = u.Ships[u.PlayerShip];
        Assert.Equal("zu nah an deinem Schiff", PlayerBuilding.PlaceIssue(u, t, ship.Position));
        u.Player.Treasury.Credits = 10f;
        Assert.Contains("Kasse reicht nicht", PlayerBuilding.PlaceIssue(u, t, p));
        u.Player.Treasury.Credits = 300_000f;
        u.MoveShip(ship, Alpha);
        // Nutzerentscheidung „Bau auch in Sektoren anderer Fraktionen“: in der Standardwelt erlaubt (Regel an); mit der Regel aus der alte Grund
        Assert.True(u.Rules.PlayerBuildForeignSectors);
        Assert.Null(PlayerBuilding.SectorIssue(u, Alpha));
        u.Rules.PlayerBuildForeignSectors = false;
        Assert.Contains("nur eigene oder herrenlose", PlayerBuilding.TypeIssue(u, t));
        u.Rules.PlayerBuildForeignSectors = true;
        ship.DockedAt = u.Sectors[Alpha].Stations[0];
        Assert.Contains("abdocken", PlayerBuilding.TypeIssue(u, t));
        u.Rules.PlayerBuilding = false;
        Assert.Contains("abgeschaltet", PlayerBuilding.TypeIssue(u, t));
        // Abgrund hat ein Piratenversteck — Nutzerentscheidung „auch in Sektoren mit Piratenversteck“: erlaubt (Regel an); Regel aus = alter Grund
        Assert.Null(PlayerBuilding.SectorIssue(u, new SectorId(11)));
        u.Rules.PlayerBuildHideoutSectors = false;
        Assert.Contains("Piratenversteck", PlayerBuilding.SectorIssue(u, new SectorId(11)));
        u.Rules.PlayerBuildHideoutSectors = true;
        // Exportstation ohne Planet
        u.Rules.PlayerBuilding = true;
        ship.DockedAt = default;
        Ready(u, "farm");
        Assert.Contains("Planet", PlayerBuilding.TypeIssue(u, u.StationTypes["trade_post"]));
    }

    [Fact]
    public void Bauen_KostenAlsTreuhandgeld_ObergrenzenEigen_ZaehltNichtGegenKi()
    {
        var u = World();
        var t = u.StationTypes["farm"];
        var p = Ready(u, "farm");
        int kiBefore = StationProtection.CountEconomic(u);
        float wallet = u.Player.Treasury.Credits;
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        var site = PlayerBuilding.Start(u, t, p, 37.5f);
        float cost = PlayerBuilding.Cost(u, t);
        Assert.Equal(wallet - cost, u.Player.Treasury.Credits, 2);
        Assert.Equal(cost, site.Credits, 2);
        Assert.Equal(cost, site.Site!.Budget, 2);
        Assert.Equal(u.PlayerActor, site.Owner);
        Assert.Equal(u.PlayerActor.Value, site.Site.InvestorActor);
        Assert.Equal("farm", site.Site.TypeKey);
        Assert.Equal(p, site.Position);
        Assert.Equal(37.5f, site.Rotation);
        Assert.Equal(t.Radius, site.Radius);
        Assert.Contains(events, e => e is PlayerSiteStarted ps && ps.Site == site.Id && Math.Abs(ps.Cost - cost) < 0.01f);
        Assert.Contains(events, e => e is ConstructionStarted);
        Assert.Equal(kiBefore, StationProtection.CountEconomic(u));   // zählt nicht gegen die KI-Obergrenze
        // zweite Baustelle geht; die dritte auch — Nutzerentscheidung „Spieler soll kein Baulimit haben“ (Standardwelt playerMaxSites 0 = unbegrenzt);
        // mit einer gesetzten Obergrenze 2 greift die alte Regel weiter
        var p2 = Ready(u, "solar_plant");
        PlayerBuilding.Start(u, u.StationTypes["solar_plant"], p2, 0f);
        Assert.Equal(0, u.Rules.PlayerMaxSites);
        Assert.DoesNotContain("Baustellen gleichzeitig", PlayerBuilding.TypeIssue(u, u.StationTypes["water_plant"]) ?? "");
        u.Rules.PlayerMaxSites = 2;
        Assert.Contains("Baustellen gleichzeitig", PlayerBuilding.TypeIssue(u, u.StationTypes["water_plant"]));
        u.Rules.PlayerMaxSites = 0;
        // Aufgeben: Treuhandgeld zurück, Baustelle weg
        float before = u.Player.Treasury.Credits;
        Assert.Null(PlayerBuilding.CancelIssue(u, site.Id));
        float refund = PlayerBuilding.Cancel(u, site.Id);
        Assert.Equal(cost, refund, 2);
        Assert.Equal(before + cost, u.Player.Treasury.Credits, 2);
        Assert.False(u.Stations.ContainsKey(site.Id));
    }

    [Fact]
    public void Fertigstellung_MitEigenemBauschiff_GehoertDemSpieler_OhneStartkapital_Drehung()
    {
        var u = World(2);
        var t = u.StationTypes["solar_plant"];
        var p = Ready(u, "solar_plant");
        var site = PlayerBuilding.Start(u, t, p, 45f);
        var foreman = Foreman(u, site);
        var runner = new SimulationRunner();
        for (int i = 0; i < 30000 && u.Stations.TryGetValue(site.Id, out var s) && s.IsBuildSite; i++) runner.Step(u);
        var st = u.Stations[site.Id];
        Assert.False(st.IsBuildSite);
        Assert.Equal("solar_plant", st.TypeKey);
        Assert.Equal(u.PlayerActor, st.Owner);
        Assert.Equal(45f, st.Rotation);
        Assert.True(foreman.Has(Capability.Construction));
        // KI-Obergrenzen unberührt, eigene Station nie als pleite entfernt
        u.Player.Treasury.Credits = 0f;
        st.Credits = 0f;
        for (int i = 0; i < 6000; i++) runner.Step(u);
        Assert.True(u.Stations.ContainsKey(st.Id));
    }

    [Fact]
    public void KiBauschiffe_UebernehmenKeineSpielerbaustelle_KeinNotbau_NpcHaendlerLiefern()
    {
        var u = World(4);
        var t = u.StationTypes["farm"];
        var p = Ready(u, "farm");
        var site = PlayerBuilding.Start(u, t, p, 0f);
        u.Player.Treasury.Credits = 0f;
        float traderUnits = 0f;
        bool emergency = false;
        u.Events.Raised += e =>
        {
            if (e is ConstructionDelivered cd && cd.Site == site.Id && cd.By == "trader") traderUnits += cd.Units;
            if (e is EmergencyConstruction ec && ec.Site == site.Id) emergency = true;
        };
        var runner = new SimulationRunner();
        for (int i = 0; i < 20000; i++)
        {
            runner.Step(u);
            var s = u.Stations[site.Id];
            Assert.True(s.Site!.Builder.Value == 0 || u.Ships[s.Site.Builder].IsPlayer, "KI-Bauschiff an Spielerbaustelle");
        }
        Assert.False(emergency);
        Assert.True(u.Stations.ContainsKey(site.Id) && u.Stations[site.Id].IsBuildSite, "Spielerbaustelle verfällt nicht und wird nicht ohne Bauschiff montiert");
        Assert.True(traderUnits > 0f, "NPC-Händler liefern Material über den normalen Handel");
        Assert.True(u.Stations[site.Id].Credits < PlayerBuilding.Cost(u, t), "Händler wurden aus dem Treuhandgeld bezahlt");
    }

    [Fact]
    public void Drehung_GespeichertUndGehashtNurWennGesetzt()
    {
        var u = World();
        var p = Ready(u, "farm");
        var site = PlayerBuilding.Start(u, u.StationTypes["farm"], p, 30f);
        ulong h30 = UniverseHash.Compute(u);
        site.Rotation = 0f;
        ulong h0 = UniverseHash.Compute(u);
        Assert.NotEqual(h30, h0);
        site.Rotation = 30f;
        var json = SaveGameJson.Write(u, Now);
        Assert.Contains("\"Rotation\"", json);
        var (copy, _) = SaveGameJson.Read(json);
        Assert.Equal(30f, copy.Stations[site.Id].Rotation);
        Assert.Equal(h30, UniverseHash.Compute(copy));
        Assert.Equal(-15f + 360f, PlayerBuilding.NormalizeRotation(-15f));
        Assert.Equal(0f, PlayerBuilding.NormalizeRotation(720f));
        // eine Welt ohne gedrehte Station schreibt kein Feld
        Assert.DoesNotContain("\"Rotation\"", SaveGameJson.Write(World(), Now));
    }

    [Fact]
    public void SpeichernLaden_MittenImBau_Bitgleich()
    {
        var u = World(5);
        var p = Ready(u, "farm");
        var site = PlayerBuilding.Start(u, u.StationTypes["farm"], p, 15f);
        Foreman(u, site);
        var runner = new SimulationRunner();
        for (int i = 0; i < 400; i++) runner.Step(u);
        var (copy, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        var r1 = new SimulationRunner(); var r2 = new SimulationRunner();
        for (int i = 0; i < 800; i++) { r1.Step(u); r2.Step(copy); }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
    }

    [Fact]
    public void SchalterAus_OhneBau_NpcWeltGleich()
    {
        var on = World(3);
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.PlayerBuilding = false;
        var off = WorldBuilder.Build(def, 3);
        new SimulationRunner().Run(on, 6000);
        new SimulationRunner().Run(off, 6000);
        on.Rules.PlayerBuilding = false;   // der Regelschalter selbst gehört zum Hash der Regeln nicht; die Welt muss gleich sein
        Assert.Equal(UniverseHash.Compute(off), UniverseHash.Compute(on));
    }

    [Fact]
    public void Umbuchung_Nullsumme_NurFertigeEigeneStation()
    {
        var u = World();
        var p = Ready(u, "solar_plant");
        var site = PlayerBuilding.Start(u, u.StationTypes["solar_plant"], p, 0f);
        Assert.Contains("Treuhandgeld", PlayerBuilding.TransferIssue(u, site.Id, 100f));
        ConstructionRules.Complete(u, site);
        var st = u.Stations[site.Id];
        Assert.Equal(0f, st.Credits);   // kein Startkapital
        float sum = u.Player.Treasury.Credits + st.Credits;
        Assert.Null(PlayerBuilding.TransferIssue(u, st.Id, 5000f));
        PlayerBuilding.Transfer(u, st.Id, 5000f);
        Assert.Equal(sum, u.Player.Treasury.Credits + st.Credits, 1);
        Assert.Contains("Stationskasse", PlayerBuilding.TransferIssue(u, st.Id, -6000f));
        PlayerBuilding.Transfer(u, st.Id, -5000f);
        Assert.Equal(sum, u.Player.Treasury.Credits + st.Credits, 1);
        var npc = u.Stations[u.Sectors[Alpha].Stations[0]];
        Assert.Contains("Keine eigene", PlayerBuilding.TransferIssue(u, npc.Id, 100f));
    }

    /// <summary>
    /// Missbrauch: (a) Handel mit der eigenen Station (kaufen, zurückverkaufen, im selben Tick und über Ticks) — Spielerkasse + Kassen der eigenen Stationen
    /// steigen nie über den Wert, den Waren von außen bringen; (b) eine ruhende eigene Station schafft kein Geld (Grundeinkommen ≤ Unterhalt);
    /// (c) selbst geliefertes Baumaterial + Aufgeben bringt nichts; (d) eigene Exportsenke ohne Aufschlag.
    /// </summary>
    [Fact]
    public void Missbrauch_KeineGeldschleifeUeberEigeneStationen()
    {
        var u = World(6);
        var p = Ready(u, "steelworks");
        var site = PlayerBuilding.Start(u, u.StationTypes["steelworks"], p, 0f);
        ConstructionRules.Complete(u, site);
        var st = u.Stations[site.Id];
        var ship = u.Ships[u.PlayerShip];
        ship.Position = st.DockPoint(ship.Id);
        ship.DockedAt = st.Id;
        PlayerBuilding.Transfer(u, st.Id, 50_000f);
        var steel = u.FindGood("steel")!.Id;
        st.Stock(steel).Amount = 200f;
        float Money() => u.Player.Treasury.Credits + PlayerBuilding.OwnEconomic(u).Sum(s => s.Credits);
        float start = Money();
        var runner = new SimulationRunner();
        // (a) kaufen / zurückverkaufen in verschiedenen Takten: nur Umbuchungen zwischen Kasse und Stationskasse
        for (int round = 0; round < 30; round++)
        {
            float m0 = Money();
            TradeActions.Buy(u, ship, st, steel, 20f);
            Assert.Equal(m0, Money(), 0);
            if (round % 3 == 0) runner.Step(u);
            float m1 = Money();
            TradeActions.Sell(u, ship, st, steel, 20f);
            Assert.True(Money() <= m1 + 0.5f, $"Rückverkauf an die eigene Station schafft Geld ({m1} → {Money()})");
        }
        // (b) ruhende eigene Station: ohne Handel wächst das Geld nicht (Unterhalt ≥ Grundeinkommen)
        foreach (var e in st.Market.Values) e.Amount = 0f;
        ship.Cargo.Clear();
        u.Rules.MaintenanceShare = 0f;   // Instandhaltung lassen wir weg (Ware von außen)
        st.Production.Clear();
        float idle0 = Money();
        for (int i = 0; i < 3000; i++) runner.Step(u);
        // die Station kauft nichts (keine Rezepte, kein Lager), also bleibt nur Grundeinkommen − Unterhalt
        Assert.True(Money() <= idle0 + 0.5f, $"ruhende Station schafft Geld: {idle0} → {Money()}");
        // (c) Baumaterial selbst liefern und aufgeben bringt nichts
        var p2 = Ready(u, "farm");
        float before = u.Player.Treasury.Credits;
        var site2 = PlayerBuilding.Start(u, u.StationTypes["farm"], p2, 0f);
        float delivered = 0f;
        foreach (var (g, need) in site2.Site!.Required)
        {
            ship.Cargo[g] = need;
            ship.Position = site2.DockPoint(ship.Id);
            delivered += TradeActions.Sell(u, ship, site2, g, need).Revenue;
            ship.Cargo.Remove(g);
        }
        Assert.True(delivered > 0f);
        PlayerBuilding.Cancel(u, site2.Id);
        Assert.True(u.Player.Treasury.Credits <= before + 0.5f, $"Selbstlieferung + Aufgeben schafft Geld ({before} → {u.Player.Treasury.Credits})");
        // (d) eigene Exportsenke: Planet zahlt nur, was sie bezahlt hat (kein Aufschlag)
        var sink = u.Stations.Values.First(s => u.StationTypes[s.TypeKey].Export is not null && !s.IsBuildSite);
        sink.Owner = u.PlayerActor;
        var food = u.FindGood("food")!.Id;
        sink.Exports.Clear();
        sink.Exports.Add(new ExportBatch { Good = food, Amount = 10f, Paid = 200f, DueTick = u.Tick });
        sink.Stock(food).Amount = 10f;
        float c0 = sink.Credits;
        ExportRules.ExportDue(u, sink);
        Assert.Equal(c0 + 200f, sink.Credits, 1);
    }

    [Fact]
    public void Unterhalt_AusStationskasse_RestAusSpielerkasse_GrundeinkommenGedeckelt()
    {
        var u = World();
        var p = Ready(u, "colony");
        var site = PlayerBuilding.Start(u, u.StationTypes["colony"], p, 0f);
        ConstructionRules.Complete(u, site);
        var st = u.Stations[site.Id];
        var type = u.StationTypes["colony"];
        Assert.Equal(type.UpkeepPerTick, PlayerBuilding.IncomePerTick(u, st, type));   // Kolonie: Einkommen 30 wird auf den Unterhalt gedeckelt
        var npc = u.Stations.Values.First(s => s.TypeKey == "colony" && s.Owner != u.PlayerActor);
        Assert.Equal(type.IncomePerTick, PlayerBuilding.IncomePerTick(u, npc, type));
    }
}
