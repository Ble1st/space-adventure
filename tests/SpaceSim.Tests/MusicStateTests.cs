using SpaceSim.Core.Audio;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>Runde 12: Musikzustand als reine Funktion (Priorität, Grenzgebiet, Gefecht/Belagerung) und Hysterese gegen Flattern.</summary>
public class MusicStateTests
{
    static Universe Standard(string? start = null) => TestWorlds.Default(fuel: false, research: false, startOption: start);

    [Fact]
    public void Prioritaet_MenueVorAllem_DannAngedockt_Bergbau_Flug()
    {
        var u = Standard();
        Assert.Equal(MusicMood.Menu, MusicState.Raw(u, new MusicContext(Menu: true, Docked: true, Mining: true)));
        Assert.Equal(MusicMood.Docked, MusicState.Raw(u, new MusicContext(false, Docked: true, Mining: true)));
        Assert.Equal(MusicMood.Mining, MusicState.Raw(u, new MusicContext(false, false, Mining: true)));
        // Alpha: Unionsgebiet, Hauptquartier — sicher
        Assert.Equal(MusicMood.SafeFlight, MusicState.Raw(u, new MusicContext(false, false, false)));
        Assert.Equal("flug_sicher", MusicState.Key(MusicMood.SafeFlight));
        Assert.Equal("belagerung", MusicState.Key(MusicMood.Siege));
    }

    [Fact]
    public void Grenzgebiet_HerrenlosOderBedrohtOderUnsicher()
    {
        var u = Standard();
        var me = u.Ships[u.PlayerShip];
        var nebel = u.Sectors.Values.First(s => s.Owner.Value == 0);
        u.MoveShip(me, nebel.Id); u.PlayerSector = nebel.Id;
        Assert.Equal(MusicMood.Frontier, MusicState.Raw(u, default));
        var alpha = u.Sectors[new SectorId(1)];
        u.MoveShip(me, alpha.Id); u.PlayerSector = alpha.Id;
        alpha.Threat = MusicState.FrontierThreat + 0.1f;
        Assert.Equal(MusicMood.Frontier, MusicState.Raw(u, default));
        alpha.Threat = 0f; alpha.Security = 0.1f;
        Assert.Equal(MusicMood.Frontier, MusicState.Raw(u, default));
    }

    [Fact]
    public void Warnung_Gefecht_Grosskampf()
    {
        var u = Standard();
        u.Rules.PlayerRaids = false;
        var me = u.Ships[u.PlayerShip];
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        u.PendingEncounters.Add(new PendingEncounter { Kind = EncounterKind.Raid, Source = raider.Id, Target = me.Id, Sector = me.Sector, DueTick = u.Tick + 60 });
        Assert.Equal(MusicMood.Warning, MusicState.Raw(u, new MusicContext(false, Docked: true, false)));   // Warnung vor angedockt
        u.PendingEncounters.Clear();
        if (raider.Transit is not null) u.FinishTransit(raider);
        if (raider.Sector != me.Sector) u.MoveShip(raider, me.Sector);
        raider.Position = me.Position + new Vec2(20f, 0f);
        var e = Engage.StartRaid(u, raider, me, u.Sectors[me.Sector]);
        Assert.Equal(MusicMood.Combat, MusicState.Raw(u, new MusicContext(false, false, Mining: true)));
        // Großkampf: weitere Teilnehmer bis zur Schwelle
        var extra = u.ShipsById().Where(s => s.Id != me.Id && s.Id != raider.Id && !u.Fights.IsEngaged(s.Id) && s.Combat()).Take(MusicState.BigBattleParticipants).ToList();
        foreach (var s in extra) { if (s.Transit is not null) u.FinishTransit(s); if (s.Sector != me.Sector) u.MoveShip(s, me.Sector); Reinforcements.JoinSide(u, e, s, 0, "test", null); }
        Assert.Equal(MusicMood.Siege, MusicState.Raw(u, default));
    }

    [Fact]
    public void Hysterese_SteigerungSofort_BeruhigungErstNachMindestdauer()
    {
        var h = new MusicState.Hysteresis { MinHoldSeconds = 6f, CalmSeconds = 5f };
        Assert.Equal(MusicMood.SafeFlight, h.Update(MusicMood.SafeFlight, 0));
        Assert.Equal(MusicMood.Combat, h.Update(MusicMood.Combat, 1));        // sofort
        Assert.Equal(MusicMood.Combat, h.Update(MusicMood.SafeFlight, 2));    // nicht sofort zurück
        Assert.Equal(MusicMood.Combat, h.Update(MusicMood.Combat, 3));        // kurzes Flattern …
        Assert.Equal(MusicMood.Combat, h.Update(MusicMood.SafeFlight, 4));    // … setzt die Ruhezeit neu
        Assert.Equal(MusicMood.Combat, h.Update(MusicMood.SafeFlight, 8.5));  // erst 4,5 s ruhig
        Assert.Equal(MusicMood.SafeFlight, h.Update(MusicMood.SafeFlight, 9.1));
        Assert.Equal(MusicMood.Docked, h.Update(MusicMood.Docked, 9.2));      // Andocken sofort
        Assert.Equal(MusicMood.SafeFlight, h.Update(MusicMood.SafeFlight, 9.3));   // Abdocken sofort
        Assert.Equal(MusicMood.Menu, h.Update(MusicMood.Menu, 9.4));          // Menü sofort
        // flatternder Rohzustand (Bergbau ↔ Flug je Sekunde) wechselt nicht hin und her
        var f = new MusicState.Hysteresis();
        f.Update(MusicMood.Frontier, 0);
        int changes = 0; var last = f.Current;
        for (int t = 1; t < 60; t++) { var c = f.Update(t % 2 == 0 ? MusicMood.Mining : MusicMood.SafeFlight, t); if (c != last) { changes++; last = c; } }
        Assert.True(changes <= 1, $"{changes} Wechsel");
    }
}

static class MusicTestExt
{
    public static bool Combat(this Ship s) => s.Has(Capability.Combat);
}
