using System.Text.Json.Nodes;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Spielerperson zu Fuß in einer Station (2026-11-20): „Schiff verlassen“ (nur angedockt), Befehl ans eigene Schiff (an Bord pausiert er,
/// zu Fuß/abwesend läuft er weiter, Boarden beendet ihn nicht), Werftkauf betritt das Schiff nicht, Taxi steigt ohne Schiff aus
/// (kein automatisches Übernehmen, kein automatisches Leihschiff), ParkedAt/Hangar nehmen das geparkte aktive Schiff auf,
/// Speicherung des Zustands (<see cref="SaveGame.PlayerAtStation"/>, additiv, ältere Stände = an Bord).
/// </summary>
public class PersonZuFussTests
{
    static Universe World()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        u.PlayerWallet.Credits = 50_000f;
        return u;
    }

    static Station St(Universe u, int id) => u.Stations[new StationId(id)];

    static Ship Own(Universe u, string model, Station at)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        s.HomeBase = at.Id;
        u.AddShip(s);
        return s;
    }

    static void Run(Universe u, int ticks) => new SimulationRunner().Run(u, ticks);

    // ───────────────────────────── Schiff verlassen / zu Fuß ─────────────────────────────

    [Fact]
    public void Schiff_Verlassen_NurAngedockt_Dann_ZuFuss_ParkedAt_NimmtDasAktiveSchiffAuf()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var yard = St(u, 5);
        u.MoveShip(player, yard.Sector); u.PlayerSector = yard.Sector;
        PlayerFleet.Dock(u, player, yard);

        // unterwegs/nicht angedockt geht nicht
        PlayerFleet.Undock(u, player);
        player.DockedAt = default;
        Assert.False(PlayerFleet.LeaveShip(u, out var flying));
        Assert.Contains("angedockt", flying);
        Assert.False(u.PlayerOnFoot);

        PlayerFleet.Dock(u, player, yard);
        Assert.True(PlayerFleet.LeaveShip(u, out var gone));   // Zu-Fuß-Station gesetzt, Schiff bleibt liegen
        Assert.True(u.PlayerOnFoot);
        Assert.Equal(yard.Id, u.PlayerAtStation);
        Assert.Equal(yard.Id, player.DockedAt);
        Assert.Equal(player.Id, u.PlayerShip);                 // das aktive Schiff bleibt die Person-Referenz
        Assert.Equal(player.Id, u.PlayerShip);
        // nochmal verlassen: schon zu Fuß
        Assert.False(PlayerFleet.LeaveShip(u, out var again));
        Assert.Contains("schon zu Fuß", again);

        // das geparkte aktive Schiff zählt jetzt mit (Hangar „Übernehmen“)
        Assert.Contains(player, PlayerFleet.ParkedAt(u, yard));
        var other = Own(u, "nomad", yard);
        Assert.Contains(other, PlayerFleet.ParkedAt(u, yard));
        // Momentaufnahme zeigt es (nicht mehr „das aktive Schiff”)
        var snap = Materializer.Snapshot(u, yard.Sector, u.Tick);
        Assert.Contains(snap, s => s.Id == player.Id);
    }

    [Fact]
    public void ZuFuss_ImHangar_Nochmal_AnBordDesEigenenAktivenSchiffs()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var yard = St(u, 5);
        u.MoveShip(player, yard.Sector); u.PlayerSector = yard.Sector;
        PlayerFleet.Dock(u, player, yard);
        Assert.True(PlayerFleet.LeaveShip(u, out _));

        // während das eigene aktive Schiff hier liegt, „Übernehmen“ desselben = wieder an Bord
        Assert.True(PlayerFleet.SwitchTo(u, yard, player.Id, out var back), back);
        Assert.False(u.PlayerOnFoot);
        Assert.Equal(player.Id, u.PlayerShip);
        // und nochmal: bereits aktiv
        Assert.False(PlayerFleet.SwitchTo(u, yard, player.Id, out _));
    }

    // ───────────────────────────── Befehl ans eigene Schiff ─────────────────────────────

    [Fact]
    public void Befehl_AnsAktiveSchiff_AngedocktLegtAb_ImFlugPausiert()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var beta = St(u, 30);
        u.MoveShip(player, beta.Sector); u.PlayerSector = beta.Sector;
        PlayerFleet.Dock(u, player, beta);
        var target = St(u, 18);   // Werft Delta, 1 Sprung weg

        // Befehl ans Schiff, in dem man sitzt: erlaubt (früher „Das aktive Schiff steuerst du selbst.")
        Assert.Null(Commands.Check(u, player, new GoToCommand(default, default, target.Id)));
        Assert.True(Commands.Give(u, player, new GoToCommand(default, default, target.Id), out var given), given);
        Assert.DoesNotContain("pausiert", Commands.Describe(u, player));   // Anzeige: angedockt läuft der Befehl sofort

        // angedockt läuft das Spiel normal weiter: das Schiff legt mit der Person an Bord ab
        Run(u, 100);
        Assert.True(player.Transit is not null || player.DockedAt == target.Id || player.Sector == target.Sector,
            $"aktives Schiff ist losgeflogen (DockedAt {player.DockedAt.Value}, Sektor {player.Sector.Value}, Reise {player.Transit is not null})");
        Assert.NotNull(player.Command);

        // im Flug endet beim Ersetzen die laufende Reise an Ort und Stelle (StopHere-Regeln); der neue Befehl pausiert an Bord
        if (player.Transit is not null && player.DockedAt.Value == 0)
        {
            Assert.True(Commands.Give(u, player, new GoToCommand(default, default, St(u, 30).Id), out var mid), mid);
            Run(u, 60);
            Assert.Null(player.Transit);   // kein neuer Start: die Person sitzt am Steuer
            Assert.Contains("pausiert", Commands.Describe(u, player));

            // angedockt weiter: der Befehl läuft auch an Bord sofort wieder los
            PlayerFleet.Dock(u, player, target);
            Run(u, 60);
            Assert.True(player.Transit is not null, "angedockt an Bord führt das Order-System den Befehl weiter aus");
        }
    }

    [Fact]
    public void Werftkauf_Bleibt_Geparkt_KeinAktivwechsel()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
        u.MoveShip(player, yard.Sector); u.PlayerSector = yard.Sector;
        PlayerFleet.Dock(u, player, yard);
        var oldId = u.PlayerShip;
        foreach (var n in ShipPurchase.Materials(u, u.ShipModels["prospector"])!)
            yard.Stock(n.Good).Amount = MathF.Max(yard.Stock(n.Good).Amount, 5f);

        var dealer = new ShipDealerService();
        Assert.True(dealer.Execute(u, yard, player, new ServiceRequest("buy:prospector", default, 0f)).Success);
        Assert.Equal(oldId, u.PlayerShip);   // der Kauf steigt nicht ein
        var bought = PlayerFleet.Owned(u).Single(s => s.Id != oldId);
        Assert.Equal(yard.Id, bought.DockedAt);
        Assert.Contains(bought, PlayerFleet.ParkedAt(u, yard));
        Assert.False(u.PlayerOnFoot);

        // zu Fuß gilt dasselbe: Der Kauf ändert nichts am Zustand der Person
        Assert.True(PlayerFleet.LeaveShip(u, out _));
        var second = PlayerFleet.Owned(u).Count;
        Assert.True(dealer.Execute(u, yard, player, new ServiceRequest("buy:prospector", default, 0f)).Success);
        Assert.Equal(yard.Id, u.PlayerAtStation);
        Assert.Equal(oldId, u.PlayerShip);
        Assert.Equal(second + 1, PlayerFleet.Owned(u).Count);
    }

    [Fact]
    public void Taxi_Endet_ZuFuss_Leihschiff_AmZielSelbst()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var from = St(u, 30);
        u.MoveShip(player, from.Sector); u.PlayerSector = from.Sector;
        PlayerFleet.Dock(u, player, from);
        var to = St(u, 1);   // Werk Alpha: weder wartendes Schiff noch Leihdienst

        var taxi = new TaxiService();
        var q = TaxiService.Quote(u, from, player, to)!;
        Assert.True(q.Bookable);   // die Person steigt zu Fuß aus — warten/leihen entscheidet sie am Ziel
        float wallet = u.PlayerWallet.Credits;
        Assert.True(taxi.Execute(u, from, player, new ServiceRequest($"taxi:{to.Id.Value}", default, 0f)).Success);
        Assert.Equal(to.Id, u.PlayerAtStation);
        Assert.Equal(to.Sector, u.PlayerSector);
        Assert.Equal(player.Id, u.PlayerShip);
        Assert.Equal(from.Id, player.DockedAt);   // das Schiff bleibt abgestellt
        Assert.Equal(wallet - q.Total, u.PlayerWallet.Credits, 1);   // bezahlt ist nur die Fahrt
        Assert.DoesNotContain("Leihschiff", q.Breakdown(u.Rules));   // Total = Fahrpreis (das Leihschiff zahlt die Person am Ziel selbst)

        // zurück an Bord: Taxi zurück zur Abstellstation, dort „Übernehmen“ des abgestellten (weiterhin aktiven) Schiffs
        Assert.True(taxi.Execute(u, to, player, new ServiceRequest($"taxi:{from.Id.Value}", default, 0f)).Success);
        Assert.Equal(from.Id, u.PlayerAtStation);
        Assert.True(PlayerFleet.SwitchTo(u, from, player.Id, out var back), back);
        Assert.False(u.PlayerOnFoot);
        Assert.Equal(player.Id, u.PlayerShip);
    }

    [Fact]
    public void ZuFuss_Gespeichert_Additiv_AlterStandOhneFeld_IstAnBord()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var beta = St(u, 30);
        u.MoveShip(player, beta.Sector); u.PlayerSector = beta.Sector;
        PlayerFleet.Dock(u, player, beta);
        Assert.True(PlayerFleet.LeaveShip(u, out _));
        Run(u, 10);

        var json = SaveGameJson.Write(u, new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        Assert.Contains("\"Version\":17", json);
        var (loaded, _) = SaveGameJson.Read(json);
        Assert.True(loaded.PlayerOnFoot);
        Assert.Equal(beta.Id, loaded.PlayerAtStation);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));

        // älterer Stand ohne das Feld: Person ist an Bord
        var node = JsonNode.Parse(json)!.AsObject();
        node.Remove("PlayerAtStation");
        var (oldLoaded, _) = SaveGameJson.Read(node.ToJsonString());
        Assert.False(oldLoaded.PlayerOnFoot);
    }

    [Fact]
    public void Schiffverlust_ZuFuss_PersonBleibtInStation_AktivesSchiffWirdNeuGewaehlt()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var alpha = St(u, 1);
        u.MoveShip(player, alpha.Sector); u.PlayerSector = alpha.Sector;
        PlayerFleet.Dock(u, player, alpha);
        Assert.True(PlayerFleet.LeaveShip(u, out _));
        var spare = Own(u, "nomad", alpha);

        var next = ShipLoss.LoseActive(u, player, "raid");
        Assert.NotNull(next);
        Assert.False(u.Ships.ContainsKey(player.Id));   // das geparkte aktive Schiff ist weg
        Assert.True(u.PlayerOnFoot);                    // die Person bleibt zu Fuß in der Station
        Assert.Equal(alpha.Id, u.PlayerAtStation);
        Assert.Equal(next!.Id, u.PlayerShip);           // das neue aktive Schiff liegt bereit (hier: das geparkte eigene)
        Assert.Equal(spare.Id, next.Id);
    }

    [Fact]
    public void ZuFuss_FernwechselInsEigeneSchiff_BoardetWieder()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var alpha = St(u, 1);
        u.MoveShip(player, alpha.Sector); u.PlayerSector = alpha.Sector;
        PlayerFleet.Dock(u, player, alpha);
        Assert.True(PlayerFleet.LeaveShip(u, out _));

        Assert.True(PlayerFleet.SwitchRemote(u, player.Id, out var msg), msg);
        Assert.False(u.PlayerOnFoot);
        Assert.Equal(player.Id, u.PlayerShip);
    }

    [Fact]
    public void ZuFuss_Schuerfen_AktivesGeparktesSchiffDocktAb()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var alpha = St(u, 1);
        u.MoveShip(player, alpha.Sector); u.PlayerSector = alpha.Sector;
        PlayerFleet.Dock(u, player, alpha);
        var miner = Own(u, "prospector", alpha);
        Assert.True(PlayerFleet.SwitchTo(u, alpha, miner.Id, out var board), board);
        Assert.True(PlayerFleet.LeaveShip(u, out _));

        Assert.True(Commands.Give(u, miner, new MineCommand(new AutoLimits(new SectorId(1), 1, 0f, 0f)), out var give), give);
        Run(u, 300);
        Assert.True(miner.DockedAt.Value == 0 || miner.Transit is not null, $"MinePhase={miner.MinePhase}, Order={miner.Order?.GetType().Name}, DockedAt={miner.DockedAt.Value}");
    }
}
