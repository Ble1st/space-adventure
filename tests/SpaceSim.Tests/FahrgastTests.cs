using System.Globalization;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Ausbau Punkt 14 — Fahrgäste (docs/FLOTTEN.md „Fahrgäste“): Passagierkabine S/M, Auftragsart „Fahrgast“ (Plätze, Lohn nach Flugzeit, Eilfahrt, VIP,
/// Trinkgeld sinkt bei Treffern, Schiffsverlust = Ansehen, Flüchtling mit Kontrollrisiko, Diplomat), Flottenbefehl „Taxi fahren“, Mauersegler mit Kabine.
/// </summary>
public class FahrgastTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    static Universe World(ulong seed = 1) => SchmuggelForschungAuftragTests.World(seed: seed);
    static MissionOffer Offer(Universe u, Station st, Func<MissionOffer, bool>? want = null) => SchmuggelForschungAuftragTests.Offer(u, PassengerMission.KeyId, st, want);

    /// <summary>Kabine M in einen zusätzlichen Frachtslot des aktiven Schiffs (6 Plätze).</summary>
    static Ship WithCabin(Universe u, Ship ship, string module = "passenger_cabin_m")
    {
        var size = u.Modules[module].Size;
        ship.Slots.Add(new ClassSlot { Type = u.Modules[module].Slot, Size = size });
        ship.Fit.Add(module);
        ship.RecomputeStats(u);
        return ship;
    }

    static Station Stand(Universe u) => u.StationsById().First(s => TaxiExecutor.IsStand(u, s) && u.FactionOf(s) is { } f && u.IsNpcFaction(f));

    [Fact]
    public void Kabinen_Inhalt_MauerseglerHatPlaetze_WerftRegale()
    {
        var u = World();
        Assert.Equal(2f, u.Modules["passenger_cabin_s"].Stats["passengers"]);
        Assert.Equal(6f, u.Modules["passenger_cabin_m"].Stats["passengers"]);
        Assert.Contains("passenger_cabin_s", u.ShipModels["swift"].Fit);
        var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
        Assert.True(yard.Market.ContainsKey(u.FindGood("passenger_cabin_s")!.Id) && yard.Market.ContainsKey(u.FindGood("passenger_cabin_m")!.Id));
        var swift = ShipPurchase.Create(u, new ShipId(99998), "swift", u.ShipClasses["courier"].Slots, u.ShipModels["swift"].Fit, ShipRole.Player, Controller.Player, u.PlayerActor, yard, 0f);
        Assert.Equal(2, swift.Stats.Passengers);
        Assert.True(swift.Stats.Speed > 10f);   // schnell: Eilfahrten schaffbar
    }

    [Fact]
    public void Annahme_BrauchtPlaetze_Lieferung_ZahltLohnMitTrinkgeld()
    {
        var u = World();
        var st = Stand(u);
        var o = Offer(u, st, x => int.Parse(x.Args["pax"], CultureInfo.InvariantCulture) <= 6 && x.Args["kind"] == "normal");
        var ship = u.Ships[u.PlayerShip];
        Assert.Contains("Passagierkabine", MissionRules.AcceptIssue(u, o.Id, ship, true));
        WithCabin(u, ship);
        float w0 = u.Player.Treasury.Credits;
        var (m, issue) = MissionRules.Accept(u, o.Id, ship, remote: true);
        Assert.Null(issue);
        Assert.Equal(6 - PassengerMission.PaxOf(m!), PassengerMission.FreeSeats(u, ship));
        var target = u.Stations[new StationId(int.Parse(m!.Args["target"], CultureInfo.InvariantCulture))];
        u.MoveShip(ship, target.Sector); ship.Position = target.DockPoint(ship.Id); ship.DockedAt = target.Id;
        MissionSystem.OnEvent(u, new ShipDocked(ship.Id, target.Id));
        Assert.Equal(MissionState.Completed, m.State);
        Assert.Equal(w0 + o.Reward, u.Player.Treasury.Credits, 1);
    }

    [Fact]
    public void Treffer_SenkenDasTrinkgeld_BetragZurueckAnDenAuftraggeber()
    {
        var u = World();
        var st = Stand(u);
        var o = Offer(u, st, x => x.Args["kind"] == "normal");
        var ship = WithCabin(u, u.Ships[u.PlayerShip]);
        var (m, _) = MissionRules.Accept(u, o.Id, ship, remote: true);
        var client = u.Actors[m!.Client];
        float c0 = client.Treasury.Credits, r0 = m.Reserved;
        float tip = float.Parse(m.Args["tip"], CultureInfo.InvariantCulture);
        Assert.True(tip > 0f);
        for (int i = 0; i < 6; i++) MissionSystem.OnEvent(u, new ShotFired(1, ship.Sector, new ShipId(1), ship.Id, "laser", DamageType.Energy, true, 0f, 5f));
        Assert.Equal(r0 - tip, m.Reserved, 1);   // Trinkgeld ganz weg, nie mehr
        Assert.Equal(c0 + tip, client.Treasury.Credits, 1);
        // Fehlschüsse und Schildtreffer kosten nichts
        MissionSystem.OnEvent(u, new ShotFired(1, ship.Sector, new ShipId(1), ship.Id, "laser", DamageType.Energy, false, 0f, 5f));
        Assert.Equal(r0 - tip, m.Reserved, 1);
    }

    [Fact]
    public void Schiffsverlust_Gescheitert_ZusaetzlicherAnsehensverlust()
    {
        var u = World();
        var st = Stand(u);
        var f = u.FactionOf(st)!.Value;
        var o = Offer(u, st, x => x.Args["kind"] == "normal");
        var ship = WithCabin(u, u.Ships[u.PlayerShip]);
        var (m, _) = MissionRules.Accept(u, o.Id, ship, remote: true);
        float rel0 = Relations.Effective(u, f, u.PlayerFaction!.Value);
        MissionSystem.OnEvent(u, new ShipRemoved(ship.Id, "destroyed"));
        Assert.Equal(MissionState.Failed, m!.State);
        float drop = rel0 - Relations.Effective(u, f, u.PlayerFaction!.Value);
        Assert.True(drop >= u.Rules.MissionRelationLoss + u.Rules.PassengerLossStanding - 0.005f, $"Verlust {drop}");
    }

    [Fact]
    public void Diplomat_Ansehen_BeiBeidenFraktionen_VipBeiDerEigenen()
    {
        var u = World();
        Station? st = null; MissionOffer? o = null;
        foreach (var s in u.StationsById().Where(s => TaxiExecutor.IsStand(u, s) && u.FactionOf(s) is { } f && u.IsNpcFaction(f)))
        {
            try { o = Offer(u, s, x => x.Args["kind"] == "diplomat"); st = s; break; } catch (Xunit.Sdk.XunitException) { }
        }
        Assert.NotNull(o);
        var ship = WithCabin(u, u.Ships[u.PlayerShip]);
        var (m, _) = MissionRules.Accept(u, o!.Id, ship, remote: true);
        var target = u.Stations[new StationId(int.Parse(m!.Args["target"], CultureInfo.InvariantCulture))];
        var a = u.FactionOf(st!)!.Value; var b = u.FactionOf(target)!.Value;
        Assert.NotEqual(a, b);
        var me = u.PlayerFaction!.Value;
        float ra = Relations.Effective(u, a, me), rb = Relations.Effective(u, b, me);
        u.MoveShip(ship, target.Sector); ship.Position = target.DockPoint(ship.Id); ship.DockedAt = target.Id;
        MissionSystem.OnEvent(u, new ShipDocked(ship.Id, target.Id));
        Assert.Equal(MissionState.Completed, m.State);
        Assert.True(Relations.Effective(u, a, me) > ra && Relations.Effective(u, b, me) > rb);
    }

    [Fact]
    public void Fluechtling_PolizeiFindetIhn_Verbrechen()
    {
        var u = World();
        Station? st = null; MissionOffer? o = null;
        foreach (var s in u.StationsById().Where(s => TaxiExecutor.IsStand(u, s) && !(u.FactionOf(s) is { } f && u.IsNpcFaction(f))))
        {
            try { o = Offer(u, s, x => x.Args["kind"] == "refugee"); st = s; break; } catch (Xunit.Sdk.XunitException) { }
        }
        if (o is null) return;   // keine Station ohne Staatsfraktion mit Taxi/Hangar in dieser Welt
        var ship = WithCabin(u, u.Ships[u.PlayerShip]);
        var (m, _) = MissionRules.Accept(u, o.Id, ship, remote: true);
        var police = u.ShipsById().First(s => s.Role == ShipRole.Police && SmuggleSearch.Where(u, s) is var w && u.Sectors[w.Sector].Owner.Value != 0 && u.FactionOf(s) == u.Sectors[w.Sector].Owner);
        var (psec, ppos) = SmuggleSearch.Where(u, police);
        if (ship.DockedAt.Value != 0) PlayerFleet.Undock(u, ship);
        u.MoveShip(ship, psec); ship.Position = ppos + new Vec2(10f, 0f);
        u.Rules.SmugglerSearchChance = 1f;
        MissionRules.Types.Get(PassengerMission.KeyId)!.Check(u, m!);
        Assert.Equal(MissionState.Failed, m!.State);
        Assert.True(Wanted.LevelOf(u, u.PlayerActor, u.Sectors[psec].Owner) >= 1);
    }

    [Fact]
    public void Taxibefehl_FaehrtEchteFahrgastangebote_LohnAusDemAuftraggeber_OhneKabineGesperrt()
    {
        var u = World();
        var stand = Stand(u);
        var swift = ShipPurchase.Create(u, u.NewShipId(), "swift", u.ShipClasses["courier"].Slots, u.ShipModels["swift"].Fit, ShipRole.Player, Controller.Player, u.PlayerActor, stand, 0f);
        u.AddShip(swift);
        var limits = new AutoLimits(stand.Sector, 2, 0f, 0f);
        Assert.Null(Commands.Check(u, swift, new TaxiCommand(limits)));
        var noCabin = ShipPurchase.Create(u, u.NewShipId(), "mule", u.ShipClasses["light_freighter"].Slots, u.ShipModels["mule"].Fit, ShipRole.Player, Controller.Player, u.PlayerActor, stand, 0f);
        u.AddShip(noCabin);
        Assert.Contains("Passagierkabine", Commands.Check(u, noCabin, new TaxiCommand(limits)));
        Assert.True(Commands.Give(u, swift, new TaxiCommand(limits), out var gm, ignoreCapacity: true), gm);
        float fares = 0f, paid = 0f, reserved = 0f;
        int trips = 0;
        var fleetMissions = new HashSet<int>();
        u.Events.Raised += e =>
        {
            if (e is PassengerFarePaid p && p.Ship == swift.Id) { fares += p.Fare; trips++; }
            if (e is MissionAccepted ma && u.Missions.FirstOrDefault(m => m.Id == ma.Mission) is { } mm && MissionRules.IsFleet(mm)) { fleetMissions.Add(mm.Id); reserved += mm.Reserved; }
            if (e is MissionPaid mp && fleetMissions.Contains(mp.Mission)) paid += mp.Amount;
        };
        new SimulationRunner().Run(u, 12000);
        Assert.True(trips >= 1, $"Fahrten {trips}");
        Assert.Equal(paid, fares, 1);                  // Fahrgeld = ausgezahlte Auftragslöhne, kein neues Geld
        Assert.True(paid <= reserved + 0.5f);           // nie mehr als bei der Annahme aus der Kasse des Auftraggebers reserviert
        Assert.IsType<TaxiCommand>(swift.Command);
        Assert.Equal(0, MissionRules.ActiveCount(u));   // Flottenaufträge zählen nicht gegen die Höchstzahl des Spielers
        var (v, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        Assert.Equal(swift.Command, v.Ships[swift.Id].Command);
        new SimulationRunner().Run(u, 400);
        new SimulationRunner().Run(v, 400);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }

    [Fact]
    public void Taxi_PingPongZwischenNahenStaenden_VerdientHoechstensDieAngebote()
    {
        // zwei Stände im selben Sektor, Umkreis 0: das Taxi darf nur, was die Fahrgastangebote dieser Stände hergeben (Mindeststrecke, Budget)
        var u = World();
        var sector = u.Sectors.Values.Where(s => u.StationsById().Count(st => st.Sector == s.Id && TaxiExecutor.IsStand(u, st)) >= 2).OrderBy(s => s.Id.Value).First();
        var stands = u.StationsById().Where(st => st.Sector == sector.Id && TaxiExecutor.IsStand(u, st)).ToList();
        var ships = new List<Ship>();
        for (int i = 0; i < 3; i++)
        {
            var cabin = u.Modules["passenger_cabin_m"];
            var s = ShipPurchase.Create(u, u.NewShipId(), "mule", u.ShipClasses["light_freighter"].Slots, u.ShipModels["mule"].Fit, ShipRole.Player, Controller.Player, u.PlayerActor, stands[0], 0f);
            s.Slots.Add(new ClassSlot { Type = cabin.Slot, Size = cabin.Size }); s.Fit.Add(cabin.Key);
            u.AddShip(s); s.RecomputeStats(u);
            Assert.True(Commands.Give(u, s, new TaxiCommand(new AutoLimits(sector.Id, 0, 0f, 0f)), out var gm, ignoreCapacity: true), gm);
            ships.Add(s);
        }
        var ids = ships.Select(s => s.Id).ToHashSet();
        var standIds = stands.Select(s => s.Id).ToHashSet();
        float fares = 0f, offered = 0f;
        u.Events.Raised += e =>
        {
            if (e is PassengerFarePaid p && ids.Contains(p.Ship)) fares += p.Fare;
            if (e is MissionOffered mo && mo.Type == PassengerMission.KeyId && u.MissionOffers.FirstOrDefault(o => o.Id == mo.Offer) is { } o && standIds.Contains(o.At)
                && int.TryParse(o.Args["target"], out var tv) && u.Stations[new StationId(tv)].Sector == sector.Id) offered += o.Reward;
        };
        new SimulationRunner().Run(u, 20000);
        Assert.True(fares <= offered + 0.5f, $"Fahrgeld {fares} ≤ angeboten {offered}");
        // die Mindeststrecke verbietet Kurzstrecken: nur Ziele weiter als passengerMinDistance
        Assert.All(u.MissionOffers.Where(o => o.Type == PassengerMission.KeyId), o =>
        {
            var at = u.Stations[o.At]; var to = u.Stations[new StationId(int.Parse(o.Args["target"]))];
            Assert.True(at.Sector != to.Sector || Vec2.Distance(at.Position, to.Position) >= u.Rules.PassengerMinDistance);
        });
    }
}
