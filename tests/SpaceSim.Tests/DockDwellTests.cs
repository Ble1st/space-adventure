using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Liegezeit an Stationen (Nutzerwunsch: Schiffe bleiben nach dem Andocken etwas im Hangar, statt nach dem Verkauf sofort weiterzufliegen; neu
/// angekommene Schiffe sind im Hangar sichtbar). <see cref="Universe.StartTransit"/> verschiebt die Reise um die Restliegezeit.
/// </summary>
public class DockDwellTests
{
    [Fact]
    public void Schalter_CodeAus_StandardweltAn()
    {
        Assert.Equal(0, new EconomyRules().DockDwellTicks);
        Assert.Equal(0, new EconomyRules().DockDwellTicksPlayer);
        var u = DemoUniverse.Create();
        Assert.True(u.Rules.DockDwellTicks > 0);
        Assert.True(u.Rules.DockDwellTicksPlayer > u.Rules.DockDwellTicks);
    }

    [Fact]
    public void AngekommenesSchiff_LiegtImHangar_BisDieLiegezeitUmIst()
    {
        var u = DemoUniverse.Create();
        u.Rules.DockDwellTicks = 30;
        var r = new SimulationRunner();
        int seen = 0;
        for (int i = 0; i < 3000 && seen < 20; i++)
        {
            r.Run(u, 1);
            foreach (var s in u.ShipsById().Where(s => s.IsEconomyAi && Hangar.LeavingFrom(u, s).Value != 0))
            {
                Assert.Equal(u.PlayerSector, s.Transit!.Legs[0].Sector);   // nur im Sektor der Person
            }
            foreach (var s in u.ShipsById().Where(s => s.IsEconomyAi && Hangar.LeavingFrom(u, s).Value != 0))
            {
                var st = u.Stations[Hangar.LeavingFrom(u, s)];
                Assert.Contains(s, Hangar.DockedShips(u, st));   // im Hangar sichtbar, obwohl die Reise geplant ist
                var snap = Materializer.Snapshot(u, st.Sector, u.Tick);
                Assert.Contains(snap, x => x.Id == s.Id && Vec2.Distance(x.Position, s.Transit!.Legs[0].From) < 0.01f);   // liegt am Andockpunkt
                Assert.True(s.Transit!.Legs[0].StartTick - u.Tick <= 30);
                seen++;
            }
        }
        Assert.True(seen > 0, "kein Schiff lag nach der Ankunft");
    }

    [Fact]
    public void Ausgeschaltet_KeineVerschiebung_KeinFeld()
    {
        var u = DemoUniverse.Create();
        u.Rules.DockDwellTicks = 0; u.Rules.DockDwellTicksPlayer = 0;
        new SimulationRunner().Run(u, 2000);
        Assert.All(u.Ships.Values, s => Assert.Equal(-1, s.DockedSince));
        Assert.DoesNotContain(u.Ships.Values, s => Hangar.LeavingFrom(u, s).Value != 0);
    }

    [Fact]
    public void SpeichernLaden_MitLiegezeit_Bitgleich()
    {
        var u = DemoUniverse.Create();
        var r = new SimulationRunner();
        r.Run(u, 1500);
        Assert.Contains(u.Ships.Values, s => s.DockedSince >= 0 || Hangar.LeavingFrom(u, s).Value != 0);
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var (v, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        r.Run(u, 1500);
        new SimulationRunner().Run(v, 1500);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }

    [Fact]
    public void EigenesSchiffMitBefehl_LiegtLaengerAlsKi()
    {
        var u = DemoUniverse.Create();
        var r = new SimulationRunner(); r.Run(u, 1);
        var me = u.Ships[u.PlayerShip];
        var st = u.StationsById().Where(s => s.Sector == me.Sector && s.Dockable).OrderBy(s => Vec2.Distance(s.Position, me.Position)).First();
        PlayerFleet.Dock(u, me, st);
        Assert.True(PlayerFleet.LeaveShip(u, out _));
        Assert.True(Commands.Give(u, me, new MineCommand(new AutoLimits(u.PlayerSector, 0, 2f, 0f)), out var m), m);
        r.Run(u, 2);
        Assert.Contains(me, Hangar.DockedShips(u, st));   // eben noch angedockt: liegt noch, Reise beginnt später
        r.Run(u, u.Rules.DockDwellTicksPlayer + 2);
        Assert.DoesNotContain(me, Hangar.DockedShips(u, st));   // nach der Liegezeit abgelegt
    }
}
