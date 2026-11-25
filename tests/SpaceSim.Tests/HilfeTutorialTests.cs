using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Help;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Hilfe und Tutorial (UI.md „Hilfe und Tutorial“): Hilfetexte als Daten, Kodex-Suche, Hinweis-Auslöser und der Tutorial-Schrittprüfer.</summary>
public class HilfeTutorialTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Hilfetexte_GeladenGeprueft_AlleAuslöserUndThemenDa()
    {
        var d = HelpData.Default;
        Assert.True(d.Hints.Count >= 10);
        Assert.True(d.Topics.Count >= 10);
        // jede im Auftrag genannte Lage hat einen Hinweis
        foreach (var trig in new[] { "dock", "trade", "raid_warning", "mining", "fuel_low", "heat", "missile", "wormhole", "sun", "command", "fleet" })
            Assert.Contains(d.Hints, h => h.Trigger == trig);
        foreach (var key in new[] { "steuerung", "handel", "abbau", "kampf", "flotte", "politik", "auftraege", "hitze", "raketen", "wurmloecher", "sonnen" })
            Assert.NotNull(HelpBook.Find(key));
        Assert.All(d.Topics, t => Assert.NotEmpty(t.Paragraphs));
        Assert.Equal(new[] { "near", "dock", "buy", "undock", "mine", "sell", "command", "help" }, d.Tutorial.Select(s => s.Check));
    }

    [Theory]
    [InlineData("{\"hints\":[{\"key\":\"a\",\"trigger\":\"gibtsnicht\",\"text\":\"x\"}]}", "Auslöser")]
    [InlineData("{\"hints\":[{\"key\":\"a\",\"trigger\":\"dock\",\"text\":\"x\"},{\"key\":\"a\",\"trigger\":\"dock\",\"text\":\"y\"}]}", "doppelt")]
    [InlineData("{\"tutorial\":[{\"key\":\"s\",\"check\":\"fliegen\",\"goal\":\"x\"}]}", "Prüfung")]
    [InlineData("{\"topics\":[{\"key\":\"t\",\"title\":\"\",\"text\":\"x\"}]}", "Titel")]
    public void Hilfetexte_Fehlerhaft_Werfen(string json, string part)
    {
        var ex = Assert.Throws<WorldFormatException>(() => HelpData.Parse(json));
        Assert.Contains(part, ex.Message);
    }

    [Fact]
    public void Kodex_Suche_TitelZuerst_UmlauteGleichwertig()
    {
        Assert.Equal(HelpData.Default.Topics.Count, HelpBook.Search("").Count);
        Assert.Equal("handel", HelpBook.Search("Handel")[0].Key);
        Assert.Equal("hitze", HelpBook.Search("HITZE")[0].Key);
        Assert.Equal("wurmloecher", HelpBook.Search("wurmloecher")[0].Key);   // „ö“ = „oe“
        Assert.Equal("wurmloecher", HelpBook.Search("Wurmlöcher")[0].Key);
        Assert.Contains(HelpBook.Search("täuschkörper"), t => t.Key == "raketen");   // Suchwort im Text
        Assert.Contains(HelpBook.Search("rakete stoersender"), t => t.Key == "raketen");   // alle Wörter müssen passen
        Assert.Empty(HelpBook.Search("quantenbanane"));
    }

    [Fact]
    public void Hinweise_Ausloeser_AusUniversumUndAnsicht_NächsterUngesehen()
    {
        var u = WorldBuilder.BuildDefault(startOption: "miner");
        var me = u.Ships[u.PlayerShip];
        Assert.Empty(HintRules.Active(u, default));   // ruhige Startlage: nichts
        Assert.Contains(HintTriggers.Trade, HintRules.Active(u, new HintView(TradeOpen: true)));
        Assert.Contains(HintTriggers.Command, HintRules.Active(u, new HintView(CommandOpen: true)));
        Assert.Contains(HintTriggers.Fleet, HintRules.Active(u, new HintView(FleetOpen: true)));
        Assert.Contains(HintTriggers.Mining, HintRules.Active(u, new HintView(MiningContact: true)));
        // Überfallwarnung
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        u.PendingEncounters.Add(new PendingEncounter { Kind = EncounterKind.Raid, Source = raider.Id, Target = me.Id, Sector = me.Sector, DueTick = u.Tick + 60 });
        Assert.Contains(HintTriggers.RaidWarning, HintRules.Active(u, default));
        u.PendingEncounters.Clear();
        // Hitze
        me.Systems.Heat = SpaceSim.Core.Combat.Heat.Capacity(u, me) * 0.8f;
        Assert.Contains(HintTriggers.Heat, HintRules.Active(u, default));
        me.Systems.Heat = 0f;
        // Tank fast leer
        me.Fuel.Clear();
        Assert.Contains(HintTriggers.FuelLow, HintRules.Active(u, default));
        me.FillFuel();
        // Sonne: in die Atmosphäre gesetzt
        var sun = Suns.Of(u, me.Sector)!;
        var saved = me.Position;
        me.Position = sun.Position + new Vec2(Suns.CoronaRadius(u.Rules, sun) + 5f, 0f);
        Assert.Contains(HintTriggers.Sun, HintRules.Active(u, default));
        me.Position = saved;
        // Wurmloch in der Nähe
        var wh = u.Gates.Values.First(g => g.Wormhole);
        if (wh.Sector != me.Sector) u.MoveShip(me, wh.Sector);
        me.Position = wh.Position + new Vec2(100f, 0f);
        Assert.Contains(HintTriggers.Wormhole, HintRules.Active(u, default));
        // angedockt: nur „dock“, keine Fluglagen
        var st = u.StationsById().First(s => s.Sector == me.Sector && s.Dockable);
        PlayerFleet.Dock(u, me, st);
        var docked = HintRules.Active(u, default);
        Assert.Contains(HintTriggers.Dock, docked);
        Assert.DoesNotContain(HintTriggers.Wormhole, docked);
        // nächster ungesehener Hinweis in Datenreihenfolge
        var active = HintRules.Active(u, new HintView(TradeOpen: true));
        var seen = new HashSet<string>();
        var first = HintRules.Next(active, seen)!;
        Assert.Equal("first_dock", first.Key);
        seen.Add(first.Key);
        Assert.Equal("first_trade", HintRules.Next(active, seen)!.Key);
        seen.Add("first_trade");
        Assert.Null(HintRules.Next(active, seen));   // einmal gezeigt, nie wieder
    }

    static (Universe U, SimulationRunner R) Tutorial()
    {
        var u = WorldBuilder.BuildDefault(startOption: "tutorial");
        var r = new SimulationRunner();
        r.Step(u);   // Statistiksystem hängt am Bus
        return (u, r);
    }

    [Fact]
    public void Tutorial_Startpaket_StartetMitStationUndZiel()
    {
        var opt = WorldBuilder.DefaultStartOptions().Single(o => o.Key == "tutorial");
        Assert.True(opt.Scenario!.Tutorial);
        var (u, _) = Tutorial();
        var t = u.Tutorial!;
        Assert.Equal(0, t.Step);
        var st = u.Stations[t.Station];
        Assert.Equal("steelworks", st.TypeKey);
        Assert.Equal(u.PlayerSector, st.Sector);
        Assert.True(TutorialRules.Active(u));
        string line = TutorialRules.HudLine(u);
        Assert.StartsWith($"Tutorial 1/{TutorialRules.Steps.Count}: ", line);
        Assert.Contains("Stahlwerk", line);
        Assert.DoesNotContain("{station}", line);
        Assert.Null(TutorialRules.Update(u, default));   // Start 140 vom Andockpunkt: noch nicht da
        // andere Pakete starten kein Tutorial
        Assert.Null(WorldBuilder.BuildDefault(startOption: "miner").Tutorial);
        Assert.Null(WorldBuilder.BuildDefault().Tutorial);
    }

    [Fact]
    public void Tutorial_Schritte_AusZustandUndEreignissen_ErkanntJeEinzeln()
    {
        var (u, r) = Tutorial();
        var me = u.Ships[u.PlayerShip];
        var st = u.Stations[u.Tutorial!.Station];
        // 1: hinfliegen
        me.Position = st.DockPoint(me.Id);
        Assert.Equal(new TutorialAdvance(0, 1, false), TutorialRules.Update(u, default));
        Assert.Null(TutorialRules.Update(u, default));   // je Aufruf höchstens ein Schritt; Andocken fehlt noch
        // 2: andocken
        PlayerFleet.Dock(u, me, st);
        Assert.Equal(1, TutorialRules.Update(u, default)!.Value.Completed);
        // 3: kaufen — über den Handelsdienst (wie die Ansicht)
        var steel = u.FindGood("steel")!.Id;
        var trade = new TradeService();
        var buy = trade.Offers(u, st, me).First(o => o.Key == "buy:" + steel.Value);
        Assert.True(trade.Execute(u, st, me, new ServiceRequest(buy.Key, steel, 1f)).Success);
        r.Step(u);
        Assert.Equal(2, TutorialRules.Update(u, default)!.Value.Completed);
        // 4: ablegen
        PlayerFleet.Undock(u, me);
        Assert.Equal(3, TutorialRules.Update(u, default)!.Value.Completed);
        // 5: Erz abbauen (Ereignis wie beim Kontaktabbau)
        var iron = u.FindGood("iron_ore")!.Id;
        u.Events.Publish(new OreCollected(me.Id, iron, 2f));
        r.Step(u);
        Assert.Null(TutorialRules.Update(u, default));   // erst 2 von 3
        u.Events.Publish(new OreCollected(me.Id, iron, 1f));
        me.Cargo[iron] = me.Cargo.GetValueOrDefault(iron) + 3f;
        r.Step(u);
        Assert.Equal(4, TutorialRules.Update(u, default)!.Value.Completed);
        // 6: verkaufen
        PlayerFleet.Dock(u, me, st);
        var sell = trade.Offers(u, st, me).First(o => o.Key == "sell:" + iron.Value);
        Assert.True(trade.Execute(u, st, me, new ServiceRequest(sell.Key, iron, 3f)).Success);
        r.Step(u);
        Assert.Equal(5, TutorialRules.Update(u, default)!.Value.Completed);
        // 7, 8: Kommandozentrale und Hilfe (nur die Ansicht weiß es)
        Assert.Null(TutorialRules.Update(u, new TutorialView(HelpOpen: true)));
        Assert.Equal(6, TutorialRules.Update(u, new TutorialView(CommandOpen: true))!.Value.Completed);
        var last = TutorialRules.Update(u, new TutorialView(HelpOpen: true))!.Value;
        Assert.True(last.Finished);
        Assert.True(u.Tutorial!.Done);
        Assert.False(TutorialRules.Active(u));
        Assert.Equal("", TutorialRules.HudLine(u));
        Assert.Null(TutorialRules.Update(u, new TutorialView(true, true)));
    }

    [Fact]
    public void Tutorial_SpeichernLaden_ErhaltenUndNichtImHash()
    {
        var (u, r) = Tutorial();
        var me = u.Ships[u.PlayerShip];
        me.Position = u.Stations[u.Tutorial!.Station].DockPoint(me.Id);
        TutorialRules.Update(u, default);
        u.Tutorial.BaseOre = 1.5;
        var (v, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.NotNull(v.Tutorial);
        Assert.Equal(1, v.Tutorial!.Step);
        Assert.Equal(u.Tutorial.Station, v.Tutorial.Station);
        Assert.Equal(1.5, v.Tutorial.BaseOre);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        // nie im Hash: ohne Tutorial derselbe Wert
        var h = UniverseHash.Compute(u);
        u.Tutorial.Step = 5; u.Tutorial.Done = true;
        Assert.Equal(h, UniverseHash.Compute(u));
        u.Tutorial = null;
        Assert.Equal(h, UniverseHash.Compute(u));
        // weiterlaufen bitgleich
        for (int k = 0; k < 200; k++) { r.Step(u); }
        var r2 = new SimulationRunner();
        v.Tutorial = null;
        for (int k = 0; k < 200; k++) r2.Step(v);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }
}
