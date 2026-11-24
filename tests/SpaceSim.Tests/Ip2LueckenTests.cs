using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Modules;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Lücken gegenüber Interstellar Pilot 2: Fracht umladen, Schiffe und eigene Fraktion umbenennen, Spielerstatistik.</summary>
public class Ip2LueckenTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    static Universe World()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        return u;
    }

    /// <summary>Aktives Schiff an einer Station im eigenen Sektor angedockt, dazu ein zweites eigenes Schiff dort.</summary>
    static (Universe U, Ship A, Ship B, Station St) Docked(string model = "nomad")
    {
        var u = World();
        var a = u.Ships[u.PlayerShip];
        var st = u.StationsById().First(s => s.Sector == a.Sector && s.Dockable && !s.IsBuildSite);
        PlayerFleet.Dock(u, a, st);
        var b = DebugSpawn.SpawnShip(u, model, out var msg)!;
        Assert.NotNull(b);
        Assert.Equal(st.Id, b.DockedAt);
        return (u, a, b, st);
    }

    [Fact]
    public void Umladen_AnDerselbenStation_MengeErhaltenUndEreignis()
    {
        var (u, a, b, _) = Docked();
        var iron = u.FindGood("iron_ore")!.Id;
        a.Cargo.Clear(); b.Cargo.Clear();
        a.Cargo[iron] = 30f;
        CargoTransferred? ev = null;
        u.Events.Raised += e => { if (e is CargoTransferred c) ev = c; };
        Assert.Contains(b, CargoTransfer.Partners(u, a));
        Assert.Equal(30f, CargoTransfer.MaxUnits(u, a, b, iron));
        Assert.Equal(12f, CargoTransfer.Transfer(u, a, b, iron, 12f));
        Assert.Equal(18f, a.Cargo[iron], 3);
        Assert.Equal(12f, b.Cargo[iron], 3);
        Assert.NotNull(ev);
        Assert.Equal(12f, ev!.Units);
        // alles: leert die Quelle
        Assert.Equal(18f, CargoTransfer.Transfer(u, a, b, iron, 0f), 3);
        Assert.False(a.Cargo.ContainsKey(iron));
        Assert.Equal(30f, b.Cargo[iron], 3);
        Assert.Equal("Nichts an Bord", CargoTransfer.Issue(u, a, b, iron));
    }

    [Fact]
    public void Umladen_BegrenztDurchPlatzDesZiels_UndVersiegelteFracht()
    {
        var (u, a, b, _) = Docked();
        var iron = u.FindGood("iron_ore")!.Id;
        a.Cargo.Clear(); b.Cargo.Clear();
        a.Cargo[iron] = 20f;
        float room = CargoTransfer.Room(u, b, iron);
        b.MissionCargo = b.CargoCapacity - 5f * u.Goods[iron].VolumePerUnit;   // versiegelte Fracht belegt fast alles
        Assert.True(CargoTransfer.Room(u, b, iron) <= 5.001f && room > 5f);
        Assert.Equal(5f, CargoTransfer.Transfer(u, a, b, iron, 20f), 2);
        Assert.Equal("Kein Platz im Zielschiff", CargoTransfer.Issue(u, a, b, iron));
        // versiegelte Fracht selbst wird nie bewegt
        float sealedB = b.MissionCargo;
        CargoTransfer.Transfer(u, b, a, iron, 0f);
        Assert.Equal(sealedB, b.MissionCargo);
        Assert.Equal(0f, a.MissionCargo);
    }

    [Fact]
    public void Umladen_Abgelehnt_FremdesSchiffAndereStationZuWeitUnterwegs()
    {
        var (u, a, b, st) = Docked();
        var iron = u.FindGood("iron_ore")!.Id;
        a.Cargo.Clear(); a.Cargo[iron] = 10f;
        var npc = u.ShipsById().First(s => !s.IsPlayer && s.Owner != u.PlayerActor && s.Transit is null);
        Assert.Equal("Nur zwischen eigenen Schiffen", CargoTransfer.PairIssue(u, a, npc));
        Assert.Equal(0f, CargoTransfer.Transfer(u, a, npc, iron, 5f));
        // andere Station
        var other = u.StationsById().First(s => s.Sector == a.Sector && s.Dockable && s.Id != st.Id);
        PlayerFleet.Dock(u, b, other);
        Assert.Equal("Nicht an derselben Station angedockt", CargoTransfer.PairIssue(u, a, b));
        // beide frei im Raum: nah = ja, weit = nein
        PlayerFleet.Undock(u, a); b.DockedAt = default;
        a.Position = new Vec2(500, 500); b.Position = new Vec2(530, 500);
        Assert.Null(CargoTransfer.PairIssue(u, a, b));
        b.Position = new Vec2(545, 500);
        Assert.StartsWith("Zu weit", CargoTransfer.PairIssue(u, a, b));
        Assert.Equal(10f, a.Cargo[iron]);   // nichts geändert
    }

    [Fact]
    public void Schiff_Umbenennen_AnzeigeSpeichernHashUnveraendert()
    {
        var (u, a, b, _) = Docked();
        ulong h = UniverseHash.Compute(u);
        Assert.StartsWith($"#{b.Id.Value} ", ShipNames.Display(u, b));
        Assert.True(ShipNames.Rename(u, b, "  Graue   Gans "));
        Assert.Equal("Graue Gans", b.CustomName);
        Assert.Equal($"Graue Gans (#{b.Id.Value})", ShipNames.Display(u, b));
        Assert.Equal(h, UniverseHash.Compute(u));   // nur Anzeige
        Assert.NotNull(ShipNames.RenameIssue(u, b, new string('x', ShipNames.MaxLength + 1)));
        var npc = u.ShipsById().First(s => s.Owner != u.PlayerActor);
        Assert.Equal("Nur eigene Schiffe", ShipNames.RenameIssue(u, npc, "Meins"));
        Assert.False(ShipNames.Rename(u, npc, "Meins"));
        var (v, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal("Graue Gans", v.Ships[b.Id].CustomName);
        Assert.True(ShipNames.Rename(u, b, ""));   // zurück zum Modellnamen
        Assert.StartsWith($"#{b.Id.Value} ", ShipNames.Display(u, b));
        Assert.DoesNotContain("\"Name\":\"Graue", SaveGameJson.Write(u, Now));
    }

    [Fact]
    public void Fraktion_Umbenennen_KuerzelEindeutigUndGueltig()
    {
        var u = World();
        var other = u.Actors.Values.First(a => a.Id != u.PlayerActor && a.Tag.Length == 3);
        Assert.StartsWith("Kürzel", PlayerIdentity.RenameIssue(u, "Sternenbund", other.Tag));
        Assert.StartsWith("Kürzel", PlayerIdentity.RenameIssue(u, "Sternenbund", "AB"));
        Assert.StartsWith("Kürzel", PlayerIdentity.RenameIssue(u, "Sternenbund", "A-B"));
        Assert.StartsWith("Name", PlayerIdentity.RenameIssue(u, "X", "SB7"));
        Assert.Equal("Name ist schon vergeben", PlayerIdentity.RenameIssue(u, other.Name, "SB7"));
        Assert.True(PlayerIdentity.Rename(u, "Sternenbund", "sb7"));
        Assert.Equal("Sternenbund", u.Player.Name);
        Assert.Equal("SB7", u.Player.Tag);
        if (u.Factions.TryGetValue(new FactionId(u.PlayerActor.Value), out var f)) Assert.Equal("Sternenbund", f.Name);
        Assert.Equal("[SB7] Sternenbund", ActorNames.Tagged(u, u.PlayerActor));
        var (v, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal("SB7", v.Player.Tag);
        Assert.Equal("Sternenbund", v.Player.Name);
        Assert.Null(PlayerIdentity.RenameIssue(u, "Sternenbund", "SB7"));   // eigenes Kürzel bleibt erlaubt
    }

    [Fact]
    public void Statistik_VerkaufUmladenUndKasse_ZaehlenUndSpeichernBitgleich()
    {
        var u = World();
        var me = u.Ships[u.PlayerShip];
        var iron = u.FindGood("iron_ore")!.Id;
        var st = u.StationsById().First(s => s.Dockable && !s.IsBuildSite && s.Market.ContainsKey(iron) && s.Consumes(iron));
        PlayerFleet.Dock(u, me, st);
        var r = new SimulationRunner();
        r.Step(u);
        me.Cargo.Clear(); me.Cargo[iron] = 10f;
        float before = u.PlayerWallet.Credits;
        var sale = TradeActions.Sell(u, me, st, iron, 10f);
        Assert.True(sale.Revenue > 0f, sale.ToString());
        r.Step(u);
        var s = u.Player.Stats!;
        Assert.NotNull(s);
        Assert.Equal(1, s.Sales);
        Assert.True(s.TradeRevenue > 0f);
        Assert.Equal(u.PlayerWallet.Credits - before, s.Earned, 1);
        Assert.True(s.MaxWallet >= u.PlayerWallet.Credits - 0.01);
        var b = DebugSpawn.SpawnShip(u, "nomad", out _)!;
        me.Cargo[iron] = 4f;
        CargoTransfer.Transfer(u, me, b, iron, 4f);
        Assert.Equal(4.0, u.Player.Stats!.Transferred, 3);
        // Speichern → Laden → weiter: bitgleich (Statistik im Hash mit Spielerpolitik)
        var (v, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        var rv = new SimulationRunner();
        for (int k = 0; k < 300; k++) { r.Step(u); rv.Step(v); }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }

    [Fact]
    public void Statistik_NpcWelt_OhneSpielerhandlung_Unberuehrt()
    {
        var a = DemoUniverse.Create();
        var b = DemoUniverse.Create();
        var withStats = new SimulationRunner();
        var without = new SimulationRunner(SimulationRunner.DefaultModules().Where(m => m is not PlayerStatsModule));
        for (int k = 0; k < 2000; k++) { withStats.Step(a); without.Step(b); }
        Assert.Equal(UniverseHash.Compute(a, true, includePlayerPolitics: false), UniverseHash.Compute(b, true, includePlayerPolitics: false));
        if (a.Player.Stats is null) Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
    }
}
