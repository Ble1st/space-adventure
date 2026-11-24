using SpaceSim.Core.Comms;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Funk Stufe 1 (<see cref="Hail"/>): Themen je Gegenüber, strenge Ausführung, Wirkung auf Warnung, Kassen und Befehle.</summary>
public class HailTests
{
    static readonly SectorId Nebelmark = new(9);

    static Universe Standard() => TestWorlds.Default(null, fuel: false, research: false);

    static void Place(Universe u, Ship s, SectorId sector, Vec2 pos)
    {
        if (s.Transit is not null) u.FinishTransit(s);
        s.Order = null; s.Phase = TradePhase.Idle; s.MinePhase = MinePhase.Idle;
        if (s.Sector != sector) u.MoveShip(s, sector);
        s.Position = pos;
        s.DockedAt = default;
    }

    /// <summary>Spieler mit Fracht in Nebelmark, ein Kaperer kündigt einen Überfall an.</summary>
    static (Universe u, Ship me, Ship raider, PendingEncounter p) Raid()
    {
        var u = Standard();
        var me = u.Ships[u.PlayerShip];
        Place(u, me, Nebelmark, new Vec2(0f, 0f));
        u.PlayerSector = Nebelmark;
        me.Cargo[u.FindGood("food")!.Id] = 40f;
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        Place(u, raider, Nebelmark, new Vec2(50f, 0f));
        var p = new PendingEncounter { Kind = EncounterKind.Raid, Source = raider.Id, Target = me.Id, Sector = Nebelmark, DueTick = u.Tick + 60 };
        u.PendingEncounters.Add(p);
        return (u, me, raider, p);
    }

    [Fact]
    public void Kaperer_Themen_UndUnbekannterSchluesselAendertNichts()
    {
        var (u, _, raider, p) = Raid();
        var v = Hail.View(u, HailTarget.OfShip(raider.Id));
        Assert.Equal(new[] { "bribe", "threaten", "stall", "drop" }, v.Topics.Select(t => t.Key));
        ulong before = UniverseHash.Compute(u);
        var r = Hail.Execute(u, HailTarget.OfShip(raider.Id), "quatsch");
        Assert.False(r.Success);
        Assert.Equal(before, UniverseHash.Compute(u));
        Assert.Contains(p, u.PendingEncounters);
    }

    [Fact]
    public void Schutzgeld_ZahltAnPiratenkasse_WarnungWeg()
    {
        var (u, me, raider, p) = Raid();
        u.Player.Treasury.Credits = 5000f;
        var pirates = u.Actors[raider.Owner];
        pirates.Treasury.Credits = 0f;
        float bribe = Hail.Bribe(u, me);
        var r = Hail.Execute(u, HailTarget.OfShip(raider.Id), "bribe");
        Assert.True(r.Success, r.Message);
        Assert.Equal(5000f - bribe, u.Player.Treasury.Credits, 2);
        Assert.Equal(bribe, pirates.Treasury.Credits, 2);
        Assert.DoesNotContain(p, u.PendingEncounters);
        Assert.Equal(40f, me.Cargo[u.FindGood("food")!.Id], 2);   // Fracht bleibt
    }

    [Fact]
    public void Schutzgeld_OhneGeld_Gesperrt()
    {
        var (u, _, raider, p) = Raid();
        u.Player.Treasury.Credits = 10f;
        var t = Hail.View(u, HailTarget.OfShip(raider.Id)).Topics.First(x => x.Key == "bribe");
        Assert.False(t.Possible);
        Assert.False(Hail.Execute(u, HailTarget.OfShip(raider.Id), "bribe").Success);
        Assert.Contains(p, u.PendingEncounters);
        Assert.Equal(10f, u.Player.Treasury.Credits);
    }

    [Fact]
    public void Hinhalten_NurEinmal_FristWaechst()
    {
        var (u, _, raider, p) = Raid();
        long due = p.DueTick;
        Assert.True(Hail.Execute(u, HailTarget.OfShip(raider.Id), "stall").Success);
        Assert.Equal(due + u.Rules.PlayerWarningTicks / 2, p.DueTick);
        Assert.True(p.Stalled);
        Assert.False(Hail.Execute(u, HailTarget.OfShip(raider.Id), "stall").Success);
        Assert.Equal(due + u.Rules.PlayerWarningTicks / 2, p.DueTick);
    }

    [Fact]
    public void Drohen_GenauEineZufallszahl_ErfolgOderSofortAngriff()
    {
        int won = 0, lost = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var (u, me, raider, p) = Raid();
            u.Rng.State = seed * 0x9E3779B97F4A7C15UL;
            var copy = new DeterministicRng(1) { State = u.Rng.State };
            copy.NextDouble();
            var r = Hail.Execute(u, HailTarget.OfShip(raider.Id), "threaten");
            Assert.True(r.Success);
            Assert.Equal(copy.NextUInt64(), u.Rng.NextUInt64());   // genau eine Zahl gezogen
            if (u.PendingEncounters.Contains(p)) { lost++; Assert.Equal(u.Tick, p.DueTick); }
            else won++;
        }
        Assert.True(won > 0 && lost > 0, $"gewonnen {won}, verloren {lost}");
    }

    [Fact]
    public void FrachtAbwerfen_IstErgeben()
    {
        var (u, me, raider, p) = Raid();
        var r = Hail.Execute(u, HailTarget.OfShip(raider.Id), "drop");
        Assert.True(r.Success, r.Message);
        Assert.DoesNotContain(p, u.PendingEncounters);
        Assert.True(me.Cargo.GetValueOrDefault(u.FindGood("food")!.Id) < 40f);
    }

    [Fact]
    public void Stillstand_GespeichertUndGehasht()
    {
        var (u, _, raider, p) = Raid();
        ulong h0 = UniverseHash.Compute(u);
        p.Stalled = true;
        Assert.NotEqual(h0, UniverseHash.Compute(u));
        var back = UniverseSerializer.FromSave(UniverseSerializer.ToSave(u, DateTimeOffset.UnixEpoch));
        Assert.True(back.PendingEncounters.Single(x => x.Source == raider.Id).Stalled);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(back));
    }

    [Fact]
    public void EigenesSchiff_FolgeMir_UndMenue()
    {
        var u = Standard();
        var me = u.Ships[u.PlayerShip];
        var other = DebugSpawnHelper(u, me);
        var v = Hail.View(u, HailTarget.OfShip(other.Id));
        Assert.Equal("menu", v.Topics[0].Key);
        Assert.True(Hail.Execute(u, HailTarget.OfShip(other.Id), "menu").OpenShipMenu);
        var r = Hail.Execute(u, HailTarget.OfShip(other.Id), "follow");
        Assert.True(r.Success, r.Message);
        Assert.IsType<FollowCommand>(other.Command);
    }

    static Ship DebugSpawnHelper(Universe u, Ship me)
    {
        var s = DebugSpawn.SpawnShip(u, "pip", out var msg);
        Assert.True(s is not null, msg);
        Assert.Equal(me.Sector, s!.Sector);
        return s!;
    }

    [Fact]
    public void Station_Andocken_Lage_UndStrafe()
    {
        var u = Standard();
        var me = u.Ships[u.PlayerShip];
        var st = u.Sectors[me.Sector].Stations.Select(id => u.Stations[id]).First(s => s.Dockable && u.FactionOf(s) is not null);
        var v = Hail.View(u, HailTarget.OfStation(st.Id));
        Assert.Equal(new[] { "dock", "news" }, v.Topics.Select(t => t.Key));
        Assert.True(Hail.Execute(u, HailTarget.OfStation(st.Id), "news").Success);
        var f = u.FactionOf(st)!.Value;
        Wanted.Report(u, u.PlayerActor, f, "smuggling");
        u.Player.Treasury.Credits = 100000f;
        v = Hail.View(u, HailTarget.OfStation(st.Id));
        Assert.Contains(v.Topics, t => t.Key == "fine" && t.Possible);
        Assert.True(Hail.Execute(u, HailTarget.OfStation(st.Id), "fine").Success);
        Assert.False(u.Player.Wanted.ContainsKey(f));
    }
}
