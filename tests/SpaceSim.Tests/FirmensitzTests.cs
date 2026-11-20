using SpaceSim.Core.Comms;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Ausbau Punkt 9 — Firmensitz und Büro (docs/POLITIK.md „Firmensitz und Büro“): KI-Firmen gründen einen Sitz (eigene Kasse: Abschöpfung, Aufstocken, Schiffskauf
/// aus der Kasse, Unterhaltsanteil, Rückstand → Verlust), Spieler-Firmensitz (Befehlsstufe +1, Fehden), Büros der KI und des Spielers (Zustimmung des Gastgebers,
/// Hangar und Auftragsbrett, Schließen bei Feindschaft), Speichern/Laden bitgleich, Schalter aus = keine neue Station.
/// </summary>
public class FirmensitzTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    static readonly SectorId Alpha = new(1), Nebelmark = new(9), Abgrund = new(11);

    /// <summary>Regeln dieser Runde zurücknehmen (Teil der Rücknahmekette über <see cref="BalanceFixTests.RevertBalanceFix"/>): kein Sitz, keine Büros, Büro ohne Hangar.</summary>
    internal static void RevertSeats(WorldDef def)
    {
        var r = def.Rules ??= new EconomyRules();
        r.FirmSeats = false; r.PlayerFirmSeat = false; r.Offices = false; r.PlayerOffices = false;
        if (def.StationTypes.FirstOrDefault(t => t.Key == "office") is { } office) office.Services.Remove("hangar");
    }

    static Universe World(ulong seed = 1) => WorldBuilder.Build(TestWorlds.Def(fuel: false, research: false), seed);

    /// <summary>Eine KI-Firma aus drei Wirtschaftsschiffen in Alpha (Mitglied der Besitzerfraktion), jedes mit <paramref name="credits"/>.</summary>
    static Actor MakeFirm(Universe u, float credits = 9000f, int ships = 3)
    {
        var free = u.ShipsById().Where(s => s.IsEconomyAi && s.Owner == u.FreeActor && s.Transit is null).Take(ships).ToList();
        Assert.Equal(ships, free.Count);
        foreach (var s in free) { if (s.Sector != Alpha) u.MoveShip(s, Alpha); s.Credits = credits; }
        var id = ActorRules.Promote(u, free[0]);
        foreach (var s in free) s.Owner = id;
        var a = u.Actors[id];
        Assert.Equal(u.Sectors[Alpha].Owner, a.Membership!.Value);
        return a;
    }

    [Fact]
    public void Schalter_CodeAus_StandardweltAn_RuecknahmeAus()
    {
        var c = new EconomyRules();
        Assert.False(c.FirmSeats || c.PlayerFirmSeat || c.Offices || c.PlayerOffices);
        Assert.Equal(2, c.FirmSeatMinClass);
        var r = World().Rules;
        Assert.True(r.FirmSeats && r.PlayerFirmSeat && r.Offices && r.PlayerOffices);
        Assert.Equal(1, r.FirmSeatMinClass);   // gemessen: keine Firma erreicht in 300 000 Ticks Klasse Firma (höchstens 4 Schiffe, Größe 4,3)
        Assert.Contains("hangar", World().StationTypes["office"].Services);
        var def = TestWorlds.Def(fuel: false, research: false);
        RevertSeats(def);
        var off = WorldBuilder.Build(def, 1);
        Assert.False(off.Rules.FirmSeats || off.Rules.Offices || off.Rules.PlayerFirmSeat || off.Rules.PlayerOffices);
        Assert.DoesNotContain("hangar", off.StationTypes["office"].Services);
        // aus: nach 6000 Ticks weder Sitz noch Büro
        new SimulationRunner().Run(off, 6000);
        Assert.DoesNotContain(off.Stations.Values, s => FirmSeats.IsSeat(off, s) || Offices.IsOffice(off, s));
    }

    [Fact]
    public void KiFirma_GruendetSitz_ImSektorIhrerFraktion_ZahltAusUeberschuessen()
    {
        var u = World();
        var a = MakeFirm(u);
        var type = FirmSeats.SeatType(u)!;
        float cost = FirmSeats.FirmCost(u, type);
        float before = u.ShipsById().Where(s => s.Owner == a.Id).Sum(s => s.Credits);
        var founded = new List<CompanySeatFounded>();
        u.Events.Raised += e => { if (e is CompanySeatFounded f) founded.Add(f); };
        new FirmSeatSystem().Step(u, FirmSeatSystem.Interval);
        var seat = Assert.Single(founded);
        Assert.Equal(a.Id, seat.Owner);
        Assert.Equal(Alpha, seat.Sector);
        Assert.Equal(cost, seat.Cost, 1);
        Assert.Equal(before - cost, u.ShipsById().Where(s => s.Owner == a.Id).Sum(s => s.Credits), 1);
        Assert.True(u.ShipsById().Where(s => s.Owner == a.Id).All(s => s.Credits >= u.Rules.FirmShipKeep - 0.01f));
        Assert.Same(u.Stations[seat.Station], FirmSeats.SeatOf(u, a.Id));
        Assert.Equal(u.Tick, a.LastFoundTick);
        // höchstens einer: der nächste Schritt gründet nichts mehr
        a.LastFoundTick = -1_000_000;
        new FirmSeatSystem().Step(u, FirmSeatSystem.Interval);
        Assert.Single(founded);
        Assert.Contains("Firmenkasse", FirmSeats.InfoLine(u, u.Stations[seat.Station]));
    }

    [Theory]
    [InlineData("klein")]    // Größenklasse zu klein
    [InlineData("arm")]      // zu wenig Geld
    [InlineData("fremd")]    // keine Präsenz in einem Sektor der eigenen Fraktion
    [InlineData("aus")]      // Regel aus
    public void KiFirma_GruendetNicht_OhneVoraussetzung(string fall)
    {
        var u = World();
        var a = MakeFirm(u, ships: fall == "klein" ? 1 : 3, credits: fall == "arm" ? 1500f : 9000f);
        if (fall == "klein") u.Rules.FirmSeatMinClass = 2;
        if (fall == "fremd") a.Membership = new FactionId(a.Membership!.Value.Value == 1 ? 2 : 1);
        if (fall == "aus") u.Rules.FirmSeats = false;
        new FirmSeatSystem().Step(u, FirmSeatSystem.Interval);
        Assert.Null(FirmSeats.SeatOf(u, a.Id));
    }

    [Fact]
    public void Sitz_SchoepftGewinnAb_StocktAuf_UndKauftAusDerKasse()
    {
        var u = World();
        var a = MakeFirm(u);
        new FirmSeatSystem().Step(u, FirmSeatSystem.Interval);
        Assert.NotNull(FirmSeats.SeatOf(u, a.Id));
        var ships = u.ShipsById().Where(s => s.Owner == a.Id).ToList();
        // Abschöpfung: Anteil des positiven Gewinns, nie unter den Rückhalt
        foreach (var s in ships) { s.Credits = 5000f; s.Profit.Rate = 2f; s.Profit.Since = 0; }
        float t0 = a.Treasury.Credits;
        float worth0 = ships[0].Profit.Worth;
        new FirmSeatSystem().Step(u, 50);
        float expected = ships.Count * 2f * 50 * u.Rules.FirmSkimShare;
        Assert.Equal(t0 + expected, a.Treasury.Credits, 1);
        Assert.Equal(5000f - 2f * 50 * u.Rules.FirmSkimShare, ships[0].Credits, 2);
        Assert.Equal(worth0 - 2f * 50 * u.Rules.FirmSkimShare, ships[0].Profit.Worth, 2);   // Bezug verschoben: neutral für die Rentabilität
        // Kasse am Ziel: keine Abschöpfung mehr
        a.Treasury.Credits = u.Rules.FirmTreasuryTarget + 1f;
        new FirmSeatSystem().Step(u, 50);
        Assert.Equal(u.Rules.FirmTreasuryTarget + 1f, a.Treasury.Credits, 1);
        // Aufstocken eines armen Schiffs aus der Kasse (über der Rücklage)
        ships[1].Credits = 100f;
        float t1 = a.Treasury.Credits;
        foreach (var s in ships) s.Profit.Rate = 0f;
        new FirmSeatSystem().Step(u, 50);
        Assert.Equal(u.Rules.FirmShipKeep, ships[1].Credits, 1);
        Assert.Equal(t1 - (u.Rules.FirmShipKeep - 100f), a.Treasury.Credits, 1);

        // Schiffskauf: die Firmenkasse ist Kandidat der Zukaufziehung (Ort = ihr erstes geparktes Schiff der Rolle)
        a.Treasury.Credits = 60_000f;
        var role = ships[0].Role;
        var cands = Investors.ForShip(u, role, u.Rules.FleetExpandCredits);
        var mine = Assert.Single(cands, c => c.Source == InvestorSource.Treasury);
        Assert.Equal(a.Id, mine.Holder);
        Assert.Equal(a.Id, mine.Ship!.Owner);
        // ohne Sitz kein Kassen-Kandidat
        u.Rules.FirmSeats = false;
        Assert.DoesNotContain(Investors.ForShip(u, role, u.Rules.FleetExpandCredits), c => c.Source == InvestorSource.Treasury);
    }

    [Fact]
    public void FlottenKauf_AusFirmenkasse_GehoertDerFirma()
    {
        var u = World();
        var a = MakeFirm(u);
        new FirmSeatSystem().Step(u, FirmSeatSystem.Interval);
        // nur die Firmenkasse kann kaufen; viel Platz in der Flotte
        foreach (var s in u.ShipsById().Where(s => s.IsEconomyAi)) s.Credits = MathF.Min(s.Credits, 1000f);
        u.Rules.MaxTraders = 1000; u.Rules.MaxMiners = 1000;
        a.Treasury.Credits = 80_000f;
        var chosen = new List<InvestorChosen>();
        u.Events.Raised += e => { if (e is InvestorChosen { Purpose: "ship" } c) chosen.Add(c); };
        int before = u.Ships.Values.Count(s => s.Owner == a.Id);
        new FleetSystem().Step(u, 20);
        var c0 = Assert.Single(chosen, c => c.Source == InvestorSource.Treasury);
        Assert.Equal(a.Id, c0.Holder);
        Assert.True(u.Ships.Values.Count(s => s.Owner == a.Id) > before);
        Assert.Equal(80_000f - chosen.Where(c => c.Source == InvestorSource.Treasury).Sum(c => c.Amount), a.Treasury.Credits, 1);
    }

    [Fact]
    public void Unterhalt_Anteil_AusFirmenkasse_KeinZuschuss_RueckstandKostetDenSitz()
    {
        var u = World();
        var a = MakeFirm(u);
        new FirmSeatSystem().Step(u, FirmSeatSystem.Interval);
        var seat = FirmSeats.SeatOf(u, a.Id)!;
        var type = u.StationTypes[seat.TypeKey];
        Assert.Equal(type.UpkeepPerTick * u.Rules.FirmSeatUpkeepShare, FirmSeats.UpkeepPerTick(u, seat, type), 4);
        Assert.False(SubsidySystem.Eligible(u, seat));   // die Mitgliedsfraktion stützt den Firmensitz nicht
        a.Treasury.Credits = 100f;
        new UpkeepSystem().Step(u, 10);
        Assert.Equal(100f - FirmSeats.UpkeepPerTick(u, seat, type) * 10f, a.Treasury.Credits, 2);
        // leere Kasse: Rückstand, nach der Frist ist der Sitz weg (Grund b)
        a.Treasury.Credits = 0f;
        foreach (var s in u.ShipsById().Where(s => s.Owner == a.Id)) s.Credits = 0f;   // nichts zum Aufstocken/Abschöpfen
        var lost = new List<CompanySeatLost>();
        u.Events.Raised += e => { if (e is CompanySeatLost l) lost.Add(l); };
        new FirmSeatSystem().Step(u, 50);
        Assert.Equal(u.Tick, a.SeatArrearsSince);
        u.Tick += u.Rules.FirmSeatArrearsTicks;
        new FirmSeatSystem().Step(u, 50);
        Assert.Equal("unpaid", Assert.Single(lost).Reason);
        Assert.Null(FirmSeats.SeatOf(u, a.Id));
        Assert.False(u.Stations.ContainsKey(seat.Id));
        Assert.Equal(-1, a.SeatArrearsSince);
    }

    /// <summary>Spieler reich, aktives Schiff abgedockt in <paramref name="sid"/> an einer Stelle, an der <paramref name="ok"/> gilt.</summary>
    static void PlayerAt(Universe u, SectorId sid, Func<bool> ok, float credits = 400_000f)
    {
        u.Player.Treasury.Credits = credits;
        var ship = u.Ships[u.PlayerShip];
        if (ship.DockedAt.Value != 0) PlayerFleet.Undock(u, ship);
        u.MoveShip(ship, sid);
        float half = 150f * MathF.Max(1f, u.Sectors[sid].Scale);
        for (float x = -half; x <= half; x += 37f)
            for (float y = -half; y <= half; y += 37f)
            {
                ship.Position = new Vec2(x, y);
                if (ok()) return;
            }
    }

    [Fact]
    public void SpielerFirmensitz_Bedingungen_Befehlsstufe_Fehde_Aufgeben()
    {
        var u = World();
        var comms = CommsActions.Default();
        var me = u.PlayerFaction!.Value;
        // fremder Sektor verweigert, Piratenversteck verweigert
        PlayerAt(u, Alpha, () => true);
        Assert.Contains("nur eigene oder herrenlose", PlayerSeatsOffices.SeatIssue(u, Alpha));
        PlayerAt(u, Abgrund, () => true);
        Assert.Contains("Piratenversteck", PlayerSeatsOffices.SeatIssue(u, Abgrund));
        // zu klein
        PlayerAt(u, Nebelmark, () => true, credits: 1000f);
        Assert.Contains("Größenklasse", PlayerSeatsOffices.SeatIssue(u, Nebelmark) ?? "");
        // gründen
        PlayerAt(u, Nebelmark, () => PlayerSeatsOffices.SeatIssue(u, Nebelmark) is null);
        var capBefore = Commands.CapacityClass(u, u.PlayerActor);
        float wallet = u.Player.Treasury.Credits;
        Assert.False(PlayerSeatsOffices.FeudUnlocked(u));
        var res = comms.Execute(u, $"firmseat:found:{Nebelmark.Value}");
        Assert.True(res.Success, res.Message);
        var seat = PlayerSeatsOffices.Seat(u)!;
        Assert.Equal(u.PlayerActor, seat.Owner);
        Assert.Equal(wallet - FirmSeats.Cost(u, FirmSeats.SeatType(u)!), u.Player.Treasury.Credits, 1);
        Assert.True(PlayerSeatsOffices.FeudUnlocked(u));
        Assert.True(Commands.CapacityClass(u, u.PlayerActor) > capBefore);
        Assert.Equal(me, u.Sectors[Nebelmark].Owner);   // Einfluss 3 im herrenlosen Sektor
        Assert.Contains("schon einen Firmensitz", PlayerSeatsOffices.SeatIssue(u, Nebelmark));
        Assert.Contains(comms.Offers(u), o => o.Key == $"firmseat:close:{seat.Id.Value}");
        Assert.Contains("Befehlsstufe", FirmSeats.InfoLine(u, seat));
        // aufgeben: Bonus und Fehde weg
        var close = comms.Execute(u, $"firmseat:close:{seat.Id.Value}");
        Assert.True(close.Success, close.Message);
        Assert.Null(PlayerSeatsOffices.Seat(u));
        Assert.False(PlayerSeatsOffices.FeudUnlocked(u));
        Assert.False(comms.Execute(u, $"firmseat:close:{seat.Id.Value}").Success);
        Assert.False(comms.Execute(u, "firmseat:found:x").Success);
    }

    [Fact]
    public void SpielerBuero_Zustimmung_HangarUndAuftraege_SchliesstBeiFeindschaft()
    {
        var u = World();
        var comms = CommsActions.Default();
        // eigener/herrenloser Sektor: kein Büro
        PlayerAt(u, Nebelmark, () => true);
        Assert.Contains("fremde", PlayerSeatsOffices.OfficeIssue(u, Nebelmark));
        // fremder Sektor mit Zustimmung
        PlayerAt(u, Alpha, () => PlayerSeatsOffices.OfficeIssue(u, Alpha) is null);
        var host = u.Sectors[Alpha].Owner;
        float wallet = u.Player.Treasury.Credits;
        var res = comms.Execute(u, $"office:found:{Alpha.Value}");
        Assert.True(res.Success, res.Message);
        var office = Assert.Single(Offices.Of(u, u.PlayerActor));
        Assert.Equal(wallet - Offices.Cost(u, Offices.OfficeType(u)!), u.Player.Treasury.Credits, 1);
        Assert.Contains("schon ein Büro", PlayerSeatsOffices.OfficeIssue(u, Alpha));
        Assert.True(office.Dockable);
        var keys = StationServices.Default().ForStation(u, office).Select(s => s.Key).ToList();
        Assert.Contains("hangar", keys);
        Assert.Contains(MissionService.ServiceKey, keys);
        Assert.Contains("Schutzverträge", FirmSeats.InfoLine(u, office));
        Assert.False(Offices.ProtectionContracts(u, office));   // Hook für Punkt 3
        Assert.Equal(host, u.Sectors[Alpha].Owner);   // 1,5 Einfluss verschiebt den Besitz nicht
        // Gastgeber wird feindlich: das Büro schließt
        Relations.Set(u, host, u.PlayerActor, -0.5f);
        var closed = new List<OfficeClosed>();
        u.Events.Raised += e => { if (e is OfficeClosed c) closed.Add(c); };
        new OfficeSystem().Step(u, OfficeSystem.Interval);
        Assert.Equal("unwelcome", Assert.Single(closed).Reason);
        Assert.Empty(Offices.Of(u, u.PlayerActor));
        Assert.Contains("stimmt nicht zu", PlayerSeatsOffices.OfficeIssue(u, Alpha));
    }

    [Fact]
    public void KiBuero_WoDieFraktionHandelt_OhneStation_EinsJeSchritt()
    {
        var u = World();
        u.Rules.OfficeMinTrade = 1;
        var founded = new List<OfficeFounded>();
        u.Events.Raised += e => { if (e is OfficeFounded f) founded.Add(f); };
        // Fraktionsschiffe mit Aufträgen in einem fremden Sektor
        var f1 = u.NpcFactionIds().First();
        var foreign = u.Sectors.Values.Where(s => u.IsNpcFaction(s.Owner) && s.Owner != f1).OrderBy(s => s.Id.Value)
            .First(s => !Offices.HasPresence(u, s, (ActorId)f1) && Offices.HostIssue(u, s, (ActorId)f1) is null);
        var target = u.Stations.Values.Where(st => st.Sector == foreign.Id && st.Dockable).OrderBy(st => st.Id.Value).First();
        var traders = u.ShipsById().Where(s => s.IsEconomyAi && s.Role == ShipRole.Trader).Take(2).ToList();
        foreach (var s in traders) { s.Owner = f1; s.Order = new TradeOrder(target.Id, target.Id, target.Market.Keys.First()); }
        u.Actors[f1].Treasury.Credits = 1_000_000f;
        new OfficeSystem().Step(u, OfficeSystem.Interval);
        var of = Assert.Single(founded);
        Assert.Equal((ActorId)f1, of.Owner);
        Assert.Equal(foreign.Id, of.Sector);
        Assert.Equal(foreign.Owner, u.Sectors[foreign.Id].Owner);
        // Abklingzeit: im nächsten Schritt kein zweites
        new OfficeSystem().Step(u, OfficeSystem.Interval);
        Assert.Single(founded);
    }

    [Fact]
    public void SpeichernLaden_Bitgleich_MitSitzBueroUndRueckstand()
    {
        var u = World();
        var a = MakeFirm(u);
        new FirmSeatSystem().Step(u, FirmSeatSystem.Interval);
        a.SeatArrearsSince = 42;
        PlayerAt(u, Alpha, () => PlayerSeatsOffices.OfficeIssue(u, Alpha) is null);
        Assert.True(CommsActions.Default().Execute(u, $"office:found:{Alpha.Value}").Success);
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        Assert.Equal(42, loaded.Actors[a.Id].SeatArrearsSince);
        Assert.Equal(a.LastFoundTick, loaded.Actors[a.Id].LastFoundTick);
        var r1 = new SimulationRunner(); var r2 = new SimulationRunner();
        r1.Run(u, 600); r2.Run(loaded, 600);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        // Hash nur, wenn gesetzt
        var b = World();
        ulong h0 = UniverseHash.Compute(b);
        b.Actors[b.FreeActor].SeatArrearsSince = 5;
        Assert.NotEqual(h0, UniverseHash.Compute(b));
    }

    [Fact]
    public void Standardwelt_20000Ticks_SitzeUndBueros_Kassen_PreiseImRahmen()
    {
        var u = DemoUniverse.Create();
        int seats = 0, offices = 0;
        u.Events.Raised += e => { if (e is CompanySeatFounded) seats++; if (e is OfficeFounded) offices++; };
        new SimulationRunner().Run(u, 20_000);
        Assert.True(seats >= 1, $"Sitze {seats}");
        Assert.True(offices >= 1, $"Büros {offices}");
        // Sitze stehen in Sektoren der Mitgliedsfraktion; Büros in fremden Sektoren; kein Sektor wechselt dadurch zur Firma
        foreach (var st in u.Stations.Values.Where(s => FirmSeats.IsSeat(u, s)))
            Assert.Equal(u.Actors[st.Owner].Membership!.Value, u.Sectors[st.Sector].Owner);
        foreach (var st in u.Stations.Values.Where(s => Offices.IsOffice(u, s)))
            Assert.NotEqual(u.FactionOf(st), (FactionId?)u.Sectors[st.Sector].Owner);
        // Firmenkassen bleiben im Rahmen (Ziel + eine Abschöpfung)
        foreach (var a in u.Actors.Values.Where(a => FirmSeats.IsFirm(u, a) && FirmSeats.SeatOf(u, a.Id) is not null))
            Assert.InRange(a.Treasury.Credits, 0f, u.Rules.FirmTreasuryTarget * 2f + 20_000f);
        var snap = EconomyStats.Snapshot(u);
        foreach (var g in snap.Goods.Where(g => g.TotalStock > 0f)) Assert.InRange(g.PriceRatio, 0.1f, 4.01f);
    }
}
