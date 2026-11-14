using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Runde 21: Countdown der Vorwarnung, Kräfteschätzung beim Stationsangriff, Zielgrund als Enum, Liegegebühr eines angedockten Verbands.</summary>
public class Runde21Tests
{
    static Universe War(ulong? seed = null) => TestWorlds.Default(seed, fuel: false, research: false, startOption: "war");
    static List<SimEvent> Capture(Universe u) { var l = new List<SimEvent>(); u.Events.Raised += l.Add; return l; }

    [Fact]
    public void Vorwarnung_ZaehltHerunter()
    {
        Assert.Equal(15, WarningCountdown.SecondsLeft(100 + 60, 100));
        Assert.Equal(15, WarningCountdown.SecondsLeft(100 + 59, 100));   // aufgerundet
        Assert.Equal(1, WarningCountdown.SecondsLeft(101, 100));
        Assert.Equal(0, WarningCountdown.SecondsLeft(90, 100));
        Assert.Equal("Piraten! X nimmt Kurs auf dich — Angriff in 7 s. Andocken.", WarningCountdown.Apply("Piraten! X nimmt Kurs auf dich — Angriff in 15 s. Andocken.", 7));
        Assert.Equal("Polizei: … den Sektor (3 s), sonst …", WarningCountdown.Apply("Polizei: … den Sektor (15 s), sonst …", 3));
        Assert.Equal("Zeitraffer aus: Überfall droht — Piraten! … verlassen (9 s).", WarningCountdown.Apply("Zeitraffer aus: Überfall droht — Piraten! … verlassen (15 s).", 9));
        Assert.Equal("ohne Zeitangabe", WarningCountdown.Apply("ohne Zeitangabe", 4));
    }

    [Fact]
    public void Vorwarnung_RestzeitAusDerAnkuendigung()
    {
        var u = War();
        var me = u.Ships[u.PlayerShip];
        var p = new PendingEncounter { Kind = EncounterKind.Raid, Target = me.Id, Sector = me.Sector, DueTick = u.Tick + u.Rules.PlayerWarningTicks };
        u.PendingEncounters.Add(p);
        Assert.Same(p, WarningCountdown.Pending(u, me.Id, p.DueTick));
        Assert.Equal(u.Rules.PlayerWarningTicks / SimClock.TicksPerSecond, WarningCountdown.SecondsLeft(p.DueTick, u.Tick));
        u.PendingEncounters.Remove(p);
        Assert.Null(WarningCountdown.Pending(u, me.Id, p.DueTick));
    }

    [Fact]
    public void Zielgrund_LangUndKurzAusDemselbenGrund()
    {
        foreach (var b in Enum.GetValues<TargetBlock>())
            Assert.Equal(b == TargetBlock.None, PlayerCombat.TargetIssueText(b) is null && PlayerCombat.TargetIssueShortText(b) is null);
        var u = War();
        var me = u.Ships[u.PlayerShip];
        var wing = u.Ships.Values.First(s => s.IsPlayer && s.Id != me.Id);
        Assert.Equal(TargetBlock.Own, PlayerCombat.TargetBlockOf(u, me, wing));
        Assert.Equal(PlayerCombat.TargetIssueText(TargetBlock.Own), PlayerCombat.TargetIssue(u, me, wing));
        Assert.Equal("eigenes Schiff", PlayerCombat.TargetIssueShort(u, me, wing));
        var docked = u.ShipsById().First(s => !s.IsPlayer && s.DockedAt.Value != 0 && !u.Fights.IsEngaged(s.Id));
        Assert.Equal(TargetBlock.Docked, PlayerCombat.TargetBlockOf(u, me, docked));
        Assert.Equal("Ziel angedockt", PlayerCombat.TargetIssueShort(u, me, docked));
    }

    /// <summary>
    /// Handy-Test b07: Kriegstest-Verband las „Aussichtslos (121 : 119)“ gegen Sektor-HQ Beta, obwohl er im Core in 2–4 min gewinnt (zwei Streifen in der Nähe,
    /// eine davon im Gefecht mit einem Korsaren). Mit zwei Streifen in der Nähe nicht „Aussichtslos“; allein bleibt es „Aussichtslos“; eine Streife in einem
    /// fremden Gefecht zählt nicht.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void Belagerungschance_KriegstestVerband_NichtAussichtslos(ulong? seed)
    {
        var u = War(seed);
        var r = new SimulationRunner();
        for (int i = 0; i < 40; i++) r.Step(u);
        var me = u.Ships[u.PlayerShip];
        var hq = u.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
        var fac = u.FactionOf(hq)!.Value;
        // zwei Streifen der Fraktion dicht am HQ (wie im Handy-Bild)
        var police = u.ShipsById().Where(s => ThreatMap.IsPolice(s) && u.FactionOf(s) == fac && !u.Fights.IsEngaged(s.Id)).Take(2).ToList();
        Assert.Equal(2, police.Count);
        foreach (var p in police)
        {
            if (p.Transit is not null) u.FinishTransit(p);
            if (p.Sector != hq.Sector) u.MoveShip(p, hq.Sector);
            p.Position = hq.Position + new Vec2(40f, 0f);
        }
        var (own, def, ships) = WarInfo.SiegeOdds(u, hq, me);
        Assert.Equal(3, ships);
        Assert.False(WarInfo.SiegeHopeless(u, hq, me), $"{WarInfo.SiegeOddsText(u, hq, me)} ({own:0} : {def:0})");
        // allein (Verband verlässt den Sektor) weiterhin aussichtslos
        foreach (var w in u.Ships.Values.Where(s => s.IsPlayer && s.Id != me.Id).ToList()) u.MoveShip(w, u.Gates[u.Sectors[me.Sector].Gates[0]].Target is var g ? u.Gates[g].Sector : me.Sector);
        Assert.StartsWith("Aussichtslos allein", WarInfo.SiegeOddsText(u, hq, me));
    }

    [Fact]
    public void Belagerungschance_StreifeImFremdenGefecht_ZaehltNicht()
    {
        var u = War();
        var me = u.Ships[u.PlayerShip];
        var hq = u.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
        var fac = u.FactionOf(hq)!.Value;
        var p = u.ShipsById().First(s => ThreatMap.IsPolice(s) && u.FactionOf(s) == fac);
        if (p.Transit is not null) u.FinishTransit(p);
        if (p.Sector != hq.Sector) u.MoveShip(p, hq.Sector);
        p.Position = hq.Position + new Vec2(40f, 0f); p.DockedAt = default;
        float with = WarInfo.SiegeOdds(u, hq, me).Defence;
        var raider = u.ShipsById().First(s => ThreatMap.IsRaider(s) && !u.Fights.IsEngaged(s.Id));
        if (raider.Transit is not null) u.FinishTransit(raider);
        if (raider.Sector != hq.Sector) u.MoveShip(raider, hq.Sector);
        raider.Position = p.Position + new Vec2(20f, 0f);
        Engage.StartIntercept(u, p, raider);
        Assert.True(u.Fights.IsEngaged(p.Id));
        float busy = WarInfo.SiegeOdds(u, hq, me).Defence;
        Assert.True(busy < with - 1f, $"{with:0.0} → {busy:0.0}");
    }

    /// <summary>Prüferhinweis Runde 20 (Runde 18 + 20 zusammen): liegt der Führer an einer fremden Station, docken die Folgenden dort und zahlen Liegegebühr.</summary>
    [Fact]
    public void Verband_FuehrerAngedockt_FolgerAngedocktUndZahlen()
    {
        var u = War();
        var me = u.Ships[u.PlayerShip];
        var farm = u.Stations.Values.First(s => s.Sector == me.Sector && s.Dockable && s.Owner != me.Owner && s.Name.StartsWith("Farm"));
        me.Position = farm.DockPoint(me.Id); me.DockedAt = farm.Id;
        var ev = Capture(u);
        var r = new SimulationRunner();
        var wing = u.Ships.Values.Where(s => s.IsPlayer && s.Id != me.Id).ToList();
        for (int i = 0; i < 400; i++) r.Step(u);
        foreach (var w in wing)
        {
            Assert.Equal(farm.Id, w.DockedAt);
            Assert.False(SpaceSim.Core.Simulation.Hangar.InFormation(u, w));
            Assert.True(SpaceSim.Core.Simulation.Hangar.BerthFeeNow(u, w) > 0f);
        }
        for (int i = 0; i < u.Rules.DayTicks + 100; i++) r.Step(u);
        foreach (var w in wing) Assert.Contains(ev, e => e is BerthFeePaid b && b.Ship == w.Id && b.Station == farm.Id);
    }
}
