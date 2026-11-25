using SpaceSim.Core.Combat;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Pilots;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Piloten: Rang, Kampfwertung, NPC-Kapitäne, Rangliste (reine Anzeige/Statistik; NPC-Welt bitgleich).</summary>
public class PilotenTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    static Universe World(bool on = true)
    {
        var def = TestWorlds.Def();
        def.Rules!.PilotRanks = on;
        var u = WorldBuilder.Build(def, null, null);
        new SimulationRunner().Run(u, 1);
        return u;
    }

    /// <summary>Ein Kaperer neben dem aktiven Schiff (nicht angedockt, ohne Auftrag).</summary>
    static Ship RaiderNext(Universe u, Ship me)
    {
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        if (raider.Transit is not null) u.FinishTransit(raider);
        raider.Order = null; raider.Command = null; raider.DockedAt = default;
        if (raider.Sector != me.Sector) u.MoveShip(raider, me.Sector);
        raider.Position = me.Position + new Vec2(30f, 0f);
        return raider;
    }

    static Ship Undocked(Universe u)
    {
        var me = u.Ships[u.PlayerShip];
        me.DockedAt = default;
        return me;
    }

    [Fact]
    public void Schalter_CodeAus_StandardweltAn()
    {
        Assert.False(new EconomyRules().PilotRanks);
        Assert.True(WorldBuilder.Parse(WorldBuilder.DefaultWorldJson()).Rules!.PilotRanks);
    }

    [Fact]
    public void Raenge_FolgenAllein_AusDerWertung()
    {
        Assert.Equal(10, PilotBook.Ranks.Length);
        Assert.Equal(0, PilotBook.RankOf(0f));
        Assert.Equal(0, PilotBook.RankOf(24.9f));
        Assert.Equal(1, PilotBook.RankOf(25f));
        Assert.Equal(9, PilotBook.RankOf(99999f));
        for (int i = 1; i < PilotBook.Ranks.Length; i++) Assert.True(PilotBook.Ranks[i].Min > PilotBook.Ranks[i - 1].Min);
        var (share, next) = PilotBook.Progress(50f);
        Assert.Equal(75f, next);
        Assert.Equal(0.5f, share, 3);
        Assert.Equal((1f, 0f), PilotBook.Progress(5000f));
    }

    [Fact]
    public void Abschuss_DesSpielers_GibtGewichtetePunkte_UndRangAufstieg()
    {
        var u = World();
        var me = Undocked(u);
        var raider = RaiderNext(u, me);
        float value = PilotBook.KillValue(u, raider);
        Assert.True(value > 0f);
        // Gesetzlose zählen mit dem Faktor
        Assert.Equal(MathF.Round((raider.Stats.WeaponPower + raider.Stats.Shield / 20f + raider.MaxHull / 50f + 0.5f) * 2f * 1.5f, 1), value, 1);
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        var (e, _, them) = PlayerCombat.Engage(u, me, raider);
        Assert.NotNull(e);
        float before = PilotBook.PlayerRating(u);
        Engage.Destroy(u, e!, them!, raider);
        var st = u.Player.Stats!;
        Assert.Equal(value, (float)st.KillPoints, 1);
        Assert.Equal(1, st.Kills);
        Assert.True(PilotBook.PlayerRating(u) > before);
        // Rangwechsel meldet der nächste Tick (nur einmal)
        st.KillPoints = 400;
        new SimulationRunner().Run(u, 2);
        var rc = events.OfType<PilotRankChanged>().ToList();
        Assert.Single(rc);
        Assert.True(rc[0].NewRank > rc[0].OldRank);
        Assert.Equal(PilotBook.RankOf(PilotBook.PlayerRating(u)), st.RankSeen);
    }

    [Fact]
    public void Verbrechen_Tod_UndAuftraege_VerändernDieWertung()
    {
        var u = World();
        var st = u.Player.Stats ??= new PlayerStats();
        st.KillPoints = 200;
        float a = PilotBook.PlayerRating(u);
        // ein Vergehen bei zwei Behörden im selben Tick zählt einmal
        u.Events.Publish(new CrimeReported(u.PlayerActor, new FactionId(1), "assault", 3f, 3));
        u.Events.Publish(new CrimeReported(u.PlayerActor, new FactionId(2), "assault", 3f, 3));
        Assert.Equal(1, st.Crimes);
        Assert.Equal(a - u.Rules.PilotCrimePenalty, PilotBook.PlayerRating(u), 2);
        st.ShipsLost = 1; st.MissionsDone = 2; st.Bounties = 1;
        Assert.Equal(200f - 15f - 20f + 10f + 10f, PilotBook.PlayerRating(u), 2);
        st.Crimes = 100;
        Assert.Equal(0f, PilotBook.PlayerRating(u));   // nie unter 0
    }

    [Fact]
    public void NpcKapitaen_ZaehltAbschuesse_UndFaellt()
    {
        var u = World();
        var police = u.ShipsById().First(ThreatMap.IsPolice);
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        if (raider.Transit is not null) u.FinishTransit(raider);
        if (police.Transit is not null) u.FinishTransit(police);
        if (raider.Sector != police.Sector) u.MoveShip(raider, police.Sector);
        raider.Position = police.Position + new Vec2(20f, 0f);
        Assert.True(PilotBook.HasCaptain(police) && PilotBook.HasCaptain(raider));
        Assert.False(PilotBook.HasCaptain(u.Ships[u.PlayerShip]));
        ulong rngBefore = u.Rng.State;
        string name = PilotBook.CaptainName(u, police.Id);
        Assert.Equal(name, PilotBook.CaptainName(u, police.Id));
        Assert.Equal(rngBefore, u.Rng.State);   // Namen ohne Universe.Rng
        float r0 = PilotBook.CaptainRating(u, police.Id, police.Role);
        var e = Engage.StartIntercept(u, police, raider);
        var p = e.Of(raider.Id)!;
        float value = PilotBook.KillValue(u, raider);
        Engage.Destroy(u, e, p, raider);
        Assert.True(u.Captains.TryGetValue(police.Id, out var rec));
        Assert.Equal(1, rec!.Kills);
        Assert.Equal(value, rec.Rating, 2);
        Assert.Equal(r0 + value, PilotBook.CaptainRating(u, police.Id, police.Role), 2);
        Assert.Contains(PilotBook.Ranking(u), x => !x.IsPlayer && x.Ship == police.Id && x.Kills == 1);
        // Kapitän mit Bilanz fällt: bleibt als gefallen in der Rangliste
        var raider2 = u.ShipsById().First(s => ThreatMap.IsRaider(s) && s.Id != raider.Id);
        if (raider2.Transit is not null) u.FinishTransit(raider2);
        if (raider2.Sector != police.Sector) u.MoveShip(raider2, police.Sector);
        var e2 = Engage.StartIntercept(u, police, raider2);
        Engage.Destroy(u, e2, e2.Of(police.Id)!, police);
        Assert.True(rec.Fallen);
        Assert.Contains(PilotBook.Ranking(u), x => x.Ship == police.Id && x.Status == "gefallen" && x.Deaths == 1);
    }

    [Fact]
    public void Funk_UndZielkarte_NennenDenKapitaen()
    {
        var u = World();
        var me = Undocked(u);
        var raider = RaiderNext(u, me);
        var v = Hail.View(u, new HailTarget(raider.Id, default));
        Assert.NotNull(v.Captain);
        Assert.StartsWith("Kpt. ", v.Captain![0]);
        Assert.Contains(PilotBook.CaptainName(u, raider.Id), v.Captain[0]);
        Assert.Equal(3, PilotBook.CaptainLineOptions(u, raider).Count);
        // aus: kein Kapitän
        u.Rules.PilotRanks = false;
        Assert.Null(Hail.View(u, new HailTarget(raider.Id, default)).Captain);
        Assert.Empty(PilotBook.CaptainLineOptions(u, raider));
    }

    [Fact]
    public void Rangliste_SortiertUndEnthaeltDenSpieler()
    {
        var u = World();
        var list = PilotBook.Ranking(u);
        Assert.Single(list, x => x.IsPlayer);
        Assert.True(list.Count > 5);
        for (int i = 1; i < list.Count; i++) Assert.True(list[i - 1].Rating >= list[i].Rating);
        Assert.Equal(PilotBook.Ranking(u).Select(x => x.Name), list.Select(x => x.Name));   // deterministisch
        // Pilotenname aus dem Seed, eigener Name gewinnt
        string n = PilotBook.PlayerName(u);
        Assert.Contains(' ', n);
        (u.Player.Stats ??= new PlayerStats()).PilotName = "Ada Kestrel";
        Assert.Equal("Ada Kestrel", PilotBook.PlayerName(u));
        // aus: nur der Spieler
        u.Rules.PilotRanks = false;
        Assert.Single(PilotBook.Ranking(u));
        // Porträt rein aus dem Seed
        Assert.Equal(PilotBook.PlayerPortrait(u), PilotBook.PlayerPortrait(u));
    }

    [Fact]
    public void NpcWelt_Bitgleich_MitUndOhneRaenge()
    {
        var a = World(on: true);
        var b = World(on: false);
        new SimulationRunner().Run(a, 4000);
        new SimulationRunner().Run(b, 4000);
        Assert.Equal(UniverseHash.Compute(b), UniverseHash.Compute(a));
    }

    [Fact]
    public void SpeichernLaden_KapitaeneUndPilotenwerte_OhneHashEinfluss()
    {
        var u = World();
        var police = u.ShipsById().First(ThreatMap.IsPolice);
        u.Captains[police.Id] = new CaptainRecord { Kills = 3, Rating = 120f, Role = ShipRole.Police, Owner = police.Owner };
        var st = u.Player.Stats ??= new PlayerStats();
        st.KillPoints = 77; st.Crimes = 2; st.RankSeen = 1; st.PilotName = "Ada Kestrel";
        ulong h = UniverseHash.Compute(u);
        u.Captains[police.Id].Kills = 9;
        Assert.Equal(h, UniverseHash.Compute(u));   // Kapitäne nicht im Hash
        u.Captains[police.Id].Kills = 3;
        var (v, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(h, UniverseHash.Compute(v));
        Assert.Equal(3, v.Captains[police.Id].Kills);
        Assert.Equal(120f, v.Captains[police.Id].Rating);
        var vs = v.Player.Stats!;
        Assert.Equal(77, vs.KillPoints);
        Assert.Equal(2, vs.Crimes);
        Assert.Equal(1, vs.RankSeen);
        Assert.Equal("Ada Kestrel", vs.PilotName);
    }
}
