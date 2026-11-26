using SpaceSim.Core.Comms;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Welle C (fünf Spieltests „Supermacht“): Steuern aus eigenem Gebiet, gestaffelter Regierungsunterhalt mit Ruhen lassen und Vorwarnung, Boden und
/// Erholung weicher Ansehensverluste, Behörde im umkämpften Fremdsektor.
/// </summary>
public class WelleCTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Referenzhash-Tests: alle Schalter der Welle C aus (alte Regeln); erste Zeile in <see cref="SchutzKonvoiTests.RevertSchutz"/>.</summary>
    internal static void RevertWelleC(WorldDef def)
    {
        var r = def.Rules ??= new EconomyRules();
        r.PlayerSectorTaxes = false;
        r.PlayerUpkeepPriority = false;
        r.PlayerUpkeepWarnDays = 0f;
        r.PlayerStandingFloor = 0f;
        r.PlayerStandingRecoveryPerDay = 0f;
        r.PlayerStandingSoftReasons = new List<string>();
        r.PlayerSeatContested = false;
    }

    static Universe World(Action<EconomyRules>? tweak = null)
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        tweak?.Invoke(def.Rules!);
        var u = WorldBuilder.Build(def);
        new SimulationRunner().Run(u, 1);
        u.PlayerWallet.Credits = 500_000f;
        return u;
    }

    static Sector Nebelmark(Universe u) => u.Sectors.Values.First(s => s.Name.Contains("Nebelmark"));

    static Station Place(Universe u, Sector sec, string type, ActorId owner)
    {
        var t = u.StationTypes[type];
        var id = u.NewStationId();
        var st = StationFactory.Create(u, id, t, $"Test {id.Value}", sec.Id, new Vec2(4000f + id.Value * 60f, 4000f), owner);
        st.Credits = 0f;
        u.AddStation(st);
        SectorControl.Update(u, sec);
        return st;
    }

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    [Fact]
    public void Schalter_CodeAus_WeltAn_RevertAus()
    {
        var c = new EconomyRules();
        Assert.False(c.PlayerSectorTaxes || c.PlayerUpkeepPriority || c.PlayerSeatContested);
        Assert.Equal(0f, c.PlayerStandingFloor); Assert.Empty(c.PlayerStandingSoftReasons); Assert.Equal(0f, c.PlayerUpkeepWarnDays);
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        var r = def.Rules!;
        Assert.True(r.PlayerSectorTaxes && r.PlayerUpkeepPriority && r.PlayerSeatContested && r.PlayerStandingFloor < 0f && r.PlayerStandingRecoveryPerDay > 0f
                    && r.PlayerStandingSoftReasons.Contains("player_contest") && r.PlayerUpkeepWarnDays > 0f);
        RevertWelleC(def);
        Assert.False(r.PlayerSectorTaxes || r.PlayerUpkeepPriority || r.PlayerSeatContested);
    }

    /// <summary>Ohne Spielerreich ändert Welle C nichts: Standardwelt mit und ohne die Schalter nach 3000 Ticks hashgleich (alter Hash).</summary>
    [Fact]
    public void SchalterAus_GleicherHash_OhneSpielerreich()
    {
        ulong Run(bool on)
        {
            var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
            if (!on) RevertWelleC(def);
            var u = WorldBuilder.Build(def);
            new SimulationRunner().Run(u, 3000);
            return UniverseHash.Compute(u);
        }
        Assert.Equal(Run(false), Run(true));
    }

    // ───────── 1: Steuern ─────────

    [Fact]
    public void Steuern_FreieSchiffeImSpielersektor_ZahlenAnDenSpieler()
    {
        var u = World();
        new SimulationRunner().Run(u, 300);   // Schürfer liegen dann am Feld (ohne Fahrt)
        var sec = Nebelmark(u);
        Place(u, sec, "sector_hq", u.PlayerActor);
        Assert.Equal(u.PlayerFaction, sec.Owner);
        var ship = u.ShipsById().First(s => s.IsEconomyAi && u.FactionOf(s) is null && s.Transit is null);
        u.MoveShip(ship, sec.Id);
        ship.Credits = 200_000f;
        var member = u.ShipsById().First(s => s.IsEconomyAi && u.FactionOf(s) is { } f && u.IsNpcFaction(f) && s.Transit is null);
        Assert.Equal(u.FactionOf(member), UpkeepSystem.TaxAuthority(u, member));
        Assert.Equal(u.PlayerFaction, UpkeepSystem.TaxAuthority(u, ship));
        var free = Place(u, sec, "solar_plant", u.FreeActor);
        Assert.Equal(u.PlayerFaction, UpkeepSystem.StationTaxAuthority(u, free));

        var income = new PlayerIncome(u);
        var ev = Capture(u);
        float before = u.PlayerWallet.Credits;
        new UpkeepSystem().Step(u, 10);
        var tax = ev.OfType<PlayerSectorTaxCollected>().Single();
        Assert.True(tax.Amount > 0f);
        float gov = ev.OfType<TreasuryUpkeep>().Where(t => t.Actor == u.PlayerActor).Sum(t => t.Amount);
        Assert.Equal(tax.Amount - gov, u.PlayerWallet.Credits - before, 0);   // Steuer rein, eigener Regierungsunterhalt raus
        Assert.Equal(tax.Amount, income.Sources["Steuern"], 2);
        // Steuerbremse zählt den Spieler nicht als Fraktion
        Assert.False(UpkeepSystem.FiscalFactors(u).ContainsKey(u.PlayerFaction!.Value.Value));
        // Schalter aus: freie Schiffe zahlen niemandem (alte Regel, herrenlos = versenkt)
        u.Rules.PlayerSectorTaxes = false;
        Assert.Null(UpkeepSystem.TaxAuthority(u, ship));
        Assert.Null(UpkeepSystem.StationTaxAuthority(u, free));
    }

    [Fact]
    public void Steuern_ImKiSektor_UnveraendertAnDieKi()
    {
        var u = World();
        new SimulationRunner().Run(u, 300);
        var ship = u.ShipsById().First(s => s.IsEconomyAi && u.FactionOf(s) is null && u.IsNpcFaction(u.Sectors[s.Sector].Owner));
        Assert.Equal(u.Sectors[ship.Sector].Owner, UpkeepSystem.TaxAuthority(u, ship));
    }

    // ───────── 2: Rangfolge, Ruhen, Vorwarnung ─────────

    static (Station Hq, List<Station> Seats) Realm(Universe u)
    {
        var sec = Nebelmark(u);
        var hq = Place(u, sec, "headquarters", u.PlayerActor);
        var seats = new List<Station> { Place(u, sec, "sector_hq", u.PlayerActor), Place(u, sec, "sector_hq", u.PlayerActor), Place(u, sec, "sector_hq", u.PlayerActor) };
        return (hq, seats);
    }

    [Fact]
    public void Rangfolge_HqZuerst_UnwichtigsteFallenZuerst()
    {
        var u = World();
        var (hq, seats) = Realm(u);
        var order = PlayerUpkeepOrder.Order(u);
        Assert.Equal(hq.Id, order[0].Id);
        float hqDay = PlayerUpkeep.GovernmentPerTick(u, hq, u.StationTypes[hq.TypeKey]) * u.Rules.PlayerUpkeepReserveTicks;
        float seatDay = PlayerUpkeep.GovernmentPerTick(u, seats[0], u.StationTypes["sector_hq"]) * u.Rules.PlayerUpkeepReserveTicks;
        // Kasse trägt HQ und zwei Behörden mit Vorrat, nicht die dritte
        u.PlayerWallet.Credits = hqDay + seatDay + 60f;
        var ev = Capture(u);
        new UpkeepSystem().Step(u, 10);
        Assert.False(hq.Unpaid);
        Assert.Equal(1, seats.Count(s => s.Unpaid));
        Assert.True(order[^1].Unpaid, "die unwichtigste fällt");
        Assert.Single(ev.OfType<PlayerGovernmentUnpaidChanged>());

        // alte Regel: dieselbe Kasse bezahlt alle (Kaskade erst bei 0)
        var o = World(r => r.PlayerUpkeepPriority = false);
        var (ohq, oseats) = Realm(o);
        o.PlayerWallet.Credits = hqDay + seatDay + 60f;
        new UpkeepSystem().Step(o, 10);
        Assert.False(ohq.Unpaid || oseats.Any(s => s.Unpaid));
    }

    [Fact]
    public void Rang_BehoerdeOhneDieDerSektorVerlorenGeht_VorUebrigen()
    {
        var u = World();
        var sec = u.Sectors.Values.OrderBy(s => s.Id.Value).First(s => u.IsNpcFaction(s.Owner) && s.Stations.Count >= 6);
        // Spieler besitzt den Sektor knapp: sein Einfluss gerade so vorn
        var me = u.PlayerFaction!.Value;
        var seat = Place(u, sec, "sector_hq", u.PlayerActor);
        for (int i = 0; i < 8 && sec.Owner != me; i++) Place(u, sec, "police_station", u.PlayerActor);
        if (sec.Owner != me) return;   // Welt gibt keinen knappen Besitz her
        int rank = PlayerUpkeepOrder.Rank(u, seat);
        Assert.InRange(rank, 1, 2);
        var other = Place(u, Nebelmark(u), "sector_hq", u.PlayerActor);
        Assert.Equal(2, PlayerUpkeepOrder.Rank(u, other));
    }

    [Fact]
    public void RuhenLassen_KeinUnterhaltKeinEinfluss_WiederAn()
    {
        var u = World();
        var (_, seats) = Realm(u);
        var st = seats[0];
        var comms = CommsActions.Default();
        Assert.False(comms.Execute(u, "rest:on:999999").Success);
        Assert.False(comms.Execute(u, "rest:off:" + st.Id.Value).Success);   // läuft schon
        Assert.False(comms.Execute(u, "rest:on:" + u.StationsById().First(s => s.Owner != u.PlayerActor).Id.Value).Success);
        var r = comms.Execute(u, "rest:on:" + st.Id.Value);
        Assert.True(r.Success, r.Message);
        Assert.True(st.Dormant && PlayerUpkeep.Unpaid(u, st));
        Assert.DoesNotContain(PlayerUpkeepOrder.Order(u), s => s.Id == st.Id);
        float w0 = SectorControl.Compute(u, u.Sectors[st.Sector]).Total;
        float before = u.PlayerWallet.Credits;
        var gov = new SortedDictionary<int, (float paid, float unpaid)>();
        PlayerUpkeepOrder.ChargeAll(u, 10, gov);
        float paid = gov.Values.Sum(v => v.paid);
        float expect = (PlayerUpkeepOrder.Governments(u).Where(s => !s.Dormant).Sum(s => PlayerUpkeep.GovernmentPerTick(u, s, u.StationTypes[s.TypeKey]))) * 10f;
        Assert.Equal(expect, paid, 2);
        Assert.Equal(before - paid, u.PlayerWallet.Credits, 1);
        Assert.False(comms.Execute(u, "rest:on:" + st.Id.Value).Success);   // ruht schon
        Assert.True(comms.Execute(u, "rest:off:" + st.Id.Value).Success);
        Assert.False(st.Dormant || PlayerUpkeep.Unpaid(u, st));
        Assert.True(SectorControl.Compute(u, u.Sectors[st.Sector]).Total > w0);
        // Schalter aus: kein Ruhen
        u.Rules.PlayerUpkeepPriority = false;
        Assert.False(comms.Execute(u, "rest:on:" + st.Id.Value).Success);
    }

    [Fact]
    public void Vorwarnung_UnterDreiTagen()
    {
        var u = World();
        Realm(u);
        float day = PlayerUpkeepOrder.DailyUpkeep(u);
        Assert.True(day > 0f);
        u.PlayerWallet.Credits = day * 10f;
        Assert.False(PlayerUpkeepOrder.Warn(u));
        Assert.Contains("10,0 Tage", PlayerUpkeepOrder.StatusLine(u));
        u.PlayerWallet.Credits = day * 2f;
        Assert.True(PlayerUpkeepOrder.Warn(u));
        Assert.Contains("WARNUNG", PlayerUpkeepOrder.StatusLine(u));
        u.Rules.PlayerUpkeepWarnDays = 0f;
        Assert.False(PlayerUpkeepOrder.Warn(u));
    }

    [Fact]
    public void Flottenabgabe_NimmtDenBehoerdenvorratNicht_RestGestundet()
    {
        foreach (bool on in new[] { true, false })
        {
            var u = World(r => r.PlayerUpkeepPriority = on);
            Realm(u);
            float reserve = PlayerUpkeepOrder.Reserve(u);
            Assert.True(reserve > 0f);
            u.PlayerWallet.Credits = reserve + 100f;
            var s = WelleBState.Of(u);
            s.FleetPending = 100_000f;
            float levy = FleetLevy.Settle(u);
            if (on)
            {
                Assert.Equal(100f, levy, 1);
                Assert.True(u.PlayerWallet.Credits >= reserve - 0.5f, "Vorrat bleibt");
                Assert.Equal(100_000f - 100f / u.Rules.PlayerFleetLevyShare, s.FleetPending, 0);   // gestundet
            }
            else
            {
                Assert.Equal(100_000f * u.Rules.PlayerFleetLevyShare, levy, 0);
                Assert.True(u.PlayerWallet.Credits < 0f);
            }
        }
    }

    // ───────── 3: Ansehen ─────────

    [Fact]
    public void Ansehen_WeicherVerlustHatBoden_VerbrechenGehtTiefer()
    {
        var u = World();
        var f = u.NpcFactionIds().First(x => !Bandits.IsEstablished(u, x));
        float start = Relations.Effective(u, f, u.PlayerActor);
        for (int i = 0; i < 10; i++) PlayerStanding.Change(u, f, -0.2f, "player_contest");
        Assert.Equal(u.Rules.PlayerStandingFloor, Relations.Effective(u, f, u.PlayerActor), 3);
        Assert.Equal(start - u.Rules.PlayerStandingFloor, u.WelleC!.Soft[f.Value].Debt, 3);
        // kein Kriegsgrund allein aus weichen Verlusten
        Assert.Equal("Kein Kriegsgrund.", Wars.DeclareIssue(u, f, u.PlayerFaction!.Value));
        PlayerStanding.Change(u, f, -0.3f, "crime:assault");
        Assert.True(Relations.Effective(u, f, u.PlayerActor) < u.Rules.PlayerStandingFloor - 0.2f);
        // Schalter aus: weiter bis −1
        var o = World(r => r.PlayerStandingFloor = 0f);
        for (int i = 0; i < 10; i++) PlayerStanding.Change(o, f, -0.2f, "player_contest");
        Assert.Equal(-1f, Relations.Effective(o, f, o.PlayerActor), 3);
    }

    [Fact]
    public void Ansehen_ErholtSich_NichtUeberBasis_FehdeHaeltAuf()
    {
        var u = World();
        var f = u.NpcFactionIds().First(x => !Bandits.IsEstablished(u, x));
        var me = u.PlayerFaction!.Value;
        float start = Relations.Effective(u, f, u.PlayerActor);
        for (int i = 0; i < 10; i++) PlayerStanding.Change(u, f, -0.2f, "player_contest");
        var sys = new WelleCSystem();
        sys.Step(u, u.Rules.DayTicks);
        Assert.Equal(u.Rules.PlayerStandingFloor + u.Rules.PlayerStandingRecoveryPerDay, Relations.Effective(u, f, u.PlayerActor), 3);
        Assert.Contains("erholt sich", PlayerSoftStanding.Text(u, f));
        // Fehde hält die Erholung auf
        Feuds.Start(u, f, me);
        float r0 = Relations.Effective(u, f, u.PlayerActor);
        sys.Step(u, u.Rules.DayTicks);
        Assert.Equal(r0, Relations.Effective(u, f, u.PlayerActor), 4);
        Assert.Contains("Fehde", PlayerSoftStanding.Text(u, f));
        u.Feuds.Clear();
        for (int i = 0; i < 60; i++) sys.Step(u, u.Rules.DayTicks);
        Assert.Equal(start, Relations.Effective(u, f, u.PlayerActor), 3);
        Assert.False(u.WelleC!.Soft.ContainsKey(f.Value));
        sys.Step(u, u.Rules.DayTicks);
        Assert.Equal(start, Relations.Effective(u, f, u.PlayerActor), 3);
    }

    [Fact]
    public void Ansehen_KriegHaeltErholungAuf_FriedenDanachMoeglich()
    {
        var u = World();
        var f = u.NpcFactionIds().First(x => !Bandits.IsEstablished(u, x));
        for (int i = 0; i < 10; i++) PlayerStanding.Change(u, f, -0.2f, "player_contest");
        Assert.Null(Wars.PlayerDeclareIssue(u, f));
        Wars.PlayerDeclare(u, f);
        Assert.Equal("Krieg", PlayerSoftStanding.Blocked(u, f));
        // Frieden: Vorschau nennt nur Kriegsbedingungen, keine Beziehung
        var pre = CommsActions.Default().Preview(u, "peace:" + f.Value);
        Assert.DoesNotContain("Beziehung", pre.Reason ?? "");
    }

    // ───────── 4: Behörde im umkämpften Sektor ─────────

    static (Sector Sec, Ship Me) Contested(Universe u)
    {
        var sec = u.Sectors.Values.OrderBy(s => s.Id.Value).First(s => u.IsNpcFaction(s.Owner) && s.Stations.Count >= 6 && !Bandits.IsEstablished(u, s.Owner));
        var me = u.PlayerFaction!.Value;
        foreach (var t in new[] { "solar_plant", "steelworks", "water_plant", "farm", "electronics_fab", "copper_smelter", "plating_works" })
        {
            if (sec.Influence.GetValueOrDefault(me) > u.Rules.ContestedShare) break;
            Place(u, sec, t, u.PlayerActor); Place(u, sec, t, u.PlayerActor);
        }
        var ship = u.Ships[u.PlayerShip];
        u.MoveShip(ship, sec.Id);
        u.PlayerWallet.Credits = 20_000_000f;
        return (sec, ship);
    }

    static bool FindSpot(Universe u, Sector sec, Ship ship, Func<string?> issue)
    {
        for (int x = -300; x <= 300; x += 50)
            for (int y = -300; y <= 300; y += 50)
            {
                ship.Position = new Vec2(x * sec.Scale, y * sec.Scale);
                if (issue() is null) return true;
            }
        return false;
    }

    [Fact]
    public void Behoerde_ImUmkaempftenFremdsektor_Erlaubt_ImKriegNicht()
    {
        var u = World();
        var (sec, ship) = Contested(u);
        Assert.True(sec.Influence[u.PlayerFaction!.Value] > u.Rules.ContestedShare);
        Assert.True(PlayerSeatContest.Allows(u, sec));
        Assert.True(FindSpot(u, sec, ship, () => PlayerSeats.FoundIssue(u, sec.Id)), PlayerSeats.FoundIssue(u, sec.Id));
        Assert.DoesNotContain("gehört", PlayerSeatsOffices.SeatIssue(u, sec.Id) ?? "");   // Firmensitz: dieselbe Regel
        var st = PlayerSeats.Found(u, sec.Id);
        Assert.Equal(u.PlayerActor, st.Owner);
        // Schalter aus: nur herrenlose
        u.Rules.PlayerSeatContested = false;
        Assert.Contains("nur herrenlose", PlayerSeats.FoundIssue(u, sec.Id));
        u.Rules.PlayerSeatContested = true;
        // im Krieg mit dem Besitzer: nicht
        Wars.PlayerDeclare(u, sec.Owner);
        Assert.False(PlayerSeatContest.Allows(u, sec));
    }

    [Fact]
    public void Behoerde_NichtUmkaempft_Abgelehnt()
    {
        var u = World();
        var sec = u.Sectors.Values.OrderBy(s => s.Id.Value).First(s => u.IsNpcFaction(s.Owner) && s.Stations.Count >= 6);
        u.MoveShip(u.Ships[u.PlayerShip], sec.Id);
        Assert.False(PlayerSeatContest.Allows(u, sec));
        Assert.Contains("gehört", PlayerSeats.FoundIssue(u, sec.Id));
    }

    // ───────── Speichern ─────────

    [Fact]
    public void Speichern_Laden_Bitgleich()
    {
        var u = World();
        var (_, seats) = Realm(u);
        CommsActions.Default().Execute(u, "rest:on:" + seats[1].Id.Value);
        var f = u.NpcFactionIds().First(x => !Bandits.IsEstablished(u, x));
        PlayerStanding.Change(u, f, -0.9f, "player_contest");
        var sec = Nebelmark(u);
        new SimulationRunner().Run(u, 300);
        var ship = u.ShipsById().First(s => s.IsEconomyAi && u.FactionOf(s) is null && s.Transit is null);
        u.MoveShip(ship, sec.Id);
        ship.Credits = 100_000f;
        new SimulationRunner().Run(u, 50);
        Assert.True(u.Player.Stats is { TaxCredits: > 0 }, "Statistik zählt die Steuern");
        var a = SaveGameJson.Read(SaveGameJson.Write(u, Now)).Universe;
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(a));
        Assert.True(a.Stations[seats[1].Id].Dormant);
        Assert.Equal(u.WelleC!.Soft[f.Value], a.WelleC!.Soft[f.Value]);
        new SimulationRunner().Run(u, 2500); new SimulationRunner().Run(a, 2500);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(a));
    }
}
