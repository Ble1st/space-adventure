using System.Linq;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Ui;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Runde 38a (Handy-Abschlusstest): Taxi in ein Leihschiff — angezeigter Preis = Abbuchung; Rückgabe des Leihschiffs mit sichtbarem Grund.</summary>
public class Runde38aTests
{
    static Universe World()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        u.PlayerWallet.Credits = 2_000f;
        return u;
    }

    static Station St(Universe u, int id) => u.Stations[new StationId(id)];

    [Fact]
    public void TaxiSteigtOhneSchiffAus_AngezeigterPreisIstDieAbbuchung_AuchNachDerTaeglichenAbrechnung()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var from = u.StationsById().First(s => s.Name == "Werft Delta");
        u.MoveShip(player, from.Sector); u.PlayerSector = from.Sector;
        PlayerFleet.Dock(u, player, from);
        var taxi = new TaxiService();
        var q = TaxiService.Quotes(u, from, player).First(x => x.Bookable && x.WaitingShip is null && x.RentalThere);
        Assert.Contains("Fahrt", q.Breakdown(u.Rules));   // das Leihschiff mietet die Person am Ziel selbst: bezahlt ist nur die Fahrt
        float before = u.PlayerWallet.Credits;
        var res = taxi.Execute(u, from, player, new ServiceRequest($"taxi:{q.To.Id.Value}", default, 0f));
        Assert.True(res.Success, res.Message);
        float other = 0f;   // Liegegebühr/Unterhalt des zurückgelassenen eigenen Schiffs sind eigene Kosten, nicht Teil der Buchung
        u.Events.Raised += e => { if (e is BerthFeePaid b) other += b.Amount; else if (e is FleetUpkeepPaid f) other += f.Amount; };
        new SimulationRunner().Run(u, (int)q.Ticks + 120);   // Fahrt vergeht, die tägliche Abrechnung läuft mindestens einmal
        Assert.Equal(before - q.Total - other, u.PlayerWallet.Credits, 1);
        Assert.Equal(-q.Total, res.Credits, 1);
    }

    [Fact]
    public void Mieten_AngezeigterPreisIstDieAbbuchung()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var st = u.StationsById().First(s => s.Name == "Handelsstation Beta");
        u.MoveShip(player, st.Sector); u.PlayerSector = st.Sector;
        PlayerFleet.Dock(u, player, st);
        var rental = new RentalService();
        var offer = rental.Offers(u, st, player).First(o => o.Key == "rent:1");
        float before = u.PlayerWallet.Credits;
        float other = 0f;
        u.Events.Raised += e => { if (e is BerthFeePaid b) other += b.Amount; else if (e is FleetUpkeepPaid f) other += f.Amount; };
        Assert.True(rental.Execute(u, st, player, new ServiceRequest("rent:1", default, 0f)).Success);
        new SimulationRunner().Run(u, 120);
        Assert.Equal(before - offer.Price - other, u.PlayerWallet.Credits, 1);
    }

    [Fact]
    public void Leihschiff_OhneEigenesSchiffHier_RueckgabeMitSichtbaremGrund()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var st = u.StationsById().First(s => s.Name == "Handelsstation Beta");
        var yard = u.StationsById().First(s => s.Name == "Werft Delta");
        u.MoveShip(player, yard.Sector); u.PlayerSector = yard.Sector;
        PlayerFleet.Dock(u, player, yard);   // das eigene Schiff liegt woanders
        u.MoveShip(player, st.Sector); u.PlayerSector = st.Sector; PlayerFleet.Dock(u, player, st);
        var rental = new RentalService();
        Assert.True(rental.Execute(u, st, player, new ServiceRequest("rent:1", default, 0f)).Success);
        var rented = u.Ships[u.PlayerShip];
        // eigenes Schiff von hier wegbringen: es liegt an der Werft
        u.MoveShip(player, yard.Sector); PlayerFleet.Dock(u, player, yard);
        var ret = rental.Offers(u, st, rented).First(o => o.Key.StartsWith("return:"));
        Assert.Equal(0f, ret.Amount);
        Assert.False(string.IsNullOrEmpty(ret.Note));
        Assert.Contains("eigenes Schiff", ret.Note);
        var res = rental.Execute(u, st, rented, new ServiceRequest(ret.Key, default, 0f));
        Assert.False(res.Success);
        Assert.Equal(ret.Note, res.Message);
        // mit eigenem Schiff an der Station geht es
        u.MoveShip(player, st.Sector); PlayerFleet.Dock(u, player, st);
        var ok = rental.Offers(u, st, rented).First(o => o.Key.StartsWith("return:"));
        Assert.Equal(1f, ok.Amount);
        Assert.Null(ok.Note);
    }

    [Theory]
    [InlineData(CombatExit.Fled, "Gegner geflohen")]
    [InlineData(CombatExit.Destroyed, "Gegner zerstört")]
    [InlineData(CombatExit.Surrendered, "Gegner ergibt sich")]
    [InlineData(CombatExit.Timeout, "Gegner bricht das Gefecht ab")]
    [InlineData(CombatExit.Looted, "Gegner dreht mit Beute ab")]
    public void JederAusgangEinesGegnersInDeinemGefecht_HatEineMeldung(CombatExit exit, string text)
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        u.MoveShip(raider, player.Sector);
        raider.Position = player.Position + new Vec2(30f, 0f);
        var e = Engage.StartRaid(u, raider, player, u.Sectors[player.Sector]);
        string? got = null;
        u.Events.Raised += ev => { if (ev is CombatExited cx && cx.Ship == raider.Id) got = MessageRules.EnemyExit(u, cx); };
        u.Events.Publish(new CombatExited(e.Id, raider.Id, raider.Role, exit));
        Assert.NotNull(got);
        Assert.StartsWith(text, got);
        // das eigene Schiff ist kein Gegner
        Assert.Null(MessageRules.EnemyExit(u, new CombatExited(e.Id, player.Id, player.Role, exit)));
    }

    [Fact]
    public void FremdeFluchtImSektor_IstInfo_EigenesGefechtIstWichtig()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var trader = u.ShipsById().First(s => s.IsEconomyAi && s.Role == ShipRole.Trader);
        Assert.Equal(MessageWeight.Info, MessageRules.FleetWeight(u, new RefugeReached(0, trader.Id, "gate", u.PlayerSector)));
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        u.MoveShip(raider, player.Sector);
        var e = Engage.StartRaid(u, raider, player, u.Sectors[player.Sector]);
        Assert.Equal(MessageWeight.Important, MessageRules.FleetWeight(u, new CombatJoined(e.Id, trader.Id, 1, "pack")));
    }
}
