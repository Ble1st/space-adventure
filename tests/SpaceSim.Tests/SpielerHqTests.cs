using SpaceSim.Core.Combat;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Eigenes Hauptquartier der Spielerfraktion (docs/POLITIK.md „Eigenes Hauptquartier“): Bedingungen (eigener Sektor, aktives Schiff, Konzern, Kasse, Platz),
/// Kosten aus der Kasse (Senke), höchstens eines (auch ein übernommenes zählt), Wirkung (Kontrolle → Sperrstufe bis 5, Heimatbasis bewaffneter Schiffe,
/// Gericht/Kopfgeld), Unterhalt notfalls als Schulden, Aufgeben ohne Erlös, Speichern/Laden bitgleich, Schalter aus = NPC-Welt gleich.
/// </summary>
public class SpielerHqTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    static readonly SectorId Nebelmark = new(9), Alpha = new(1);

    static Universe World(ulong seed = 1, string? start = null) =>
        start is null ? WorldBuilder.Build(TestWorlds.Def(fuel: false, research: false), seed)
            : WorldBuilder.Build(TestWorlds.Def(fuel: false, research: false), seed, start);

    static FactionId Me(Universe u) => u.PlayerFaction ?? throw new Xunit.Sdk.XunitException("keine Spielerfraktion");

    /// <summary>Spieler reich, aktives Schiff an einen freien Platz in Nebelmark, Sektorbehörde gegründet (Nebelmark gehört dann der Spielerfraktion).</summary>
    static void WithSeat(Universe u, float credits = 400_000f)
    {
        u.Player.Treasury.Credits = credits;
        var ship = u.Ships[u.PlayerShip];
        if (ship.DockedAt.Value != 0) PlayerFleet.Undock(u, ship);
        u.MoveShip(ship, Nebelmark);
        foreach (var cand in new[] { new Vec2(0, 0), new Vec2(200, 200), new Vec2(-200, 200), new Vec2(200, -200), new Vec2(-200, -200), new Vec2(400, 0), new Vec2(-400, 0) })
        {
            ship.Position = cand;
            if (PlayerSeats.FoundIssue(u, Nebelmark) is null && PlayerSeatsAndHqFit(u)) break;
        }
        var res = CommsActions.Default().Execute(u, $"seat:found:{Nebelmark.Value}");
        Assert.True(res.Success, res.Message);
        Assert.Equal(Me(u), u.Sectors[Nebelmark].Owner);
        u.Player.Treasury.Credits = credits;
    }

    /// <summary>Platz für Behörde und danach das XL-Hauptquartier neben dem Schiff (Probe an einer Kopie wäre teuer — grob: zweimal frei).</summary>
    static bool PlayerSeatsAndHqFit(Universe u)
    {
        var t = PlayerHq.HqType(u)!;
        var sec = u.Sectors[Nebelmark];
        var ship = u.Ships[u.PlayerShip];
        return Outposts.FreeSpot(u, sec, ship.Position, t.Radius, t.Radius + 60f, 150f) is not null;
    }

    [Fact]
    public void Standardwelt_SchalterAn_CodeAus()
    {
        Assert.False(new EconomyRules().PlayerHq);
        Assert.False(new EconomyRules().PlayerGovernmentDebt);
        var u = World();
        Assert.True(u.Rules.PlayerHq);
        Assert.True(u.Rules.PlayerGovernmentDebt);
        var t = PlayerHq.HqType(u)!;
        Assert.Equal(StationSize.XL, t.Size);
        Assert.Equal(GovernmentKind.Headquarters, t.Government);
        Assert.Contains("court", t.Services);
        Assert.Contains("bounty", t.Services);
    }

    [Fact]
    public void Bedingungen_EigenerSektor_AktivesSchiff_Konzern_Kasse()
    {
        var u = World();
        u.Player.Treasury.Credits = 400_000f;
        var ship = u.Ships[u.PlayerShip];
        if (ship.DockedAt.Value != 0) PlayerFleet.Undock(u, ship);
        u.MoveShip(ship, Alpha);
        Assert.Contains("gehört dir nicht", PlayerHq.FoundIssue(u, Alpha));
        u.MoveShip(ship, Nebelmark);
        Assert.Contains("gehört dir nicht", PlayerHq.FoundIssue(u, Nebelmark));   // herrenlos: erst eine Sektorbehörde
        WithSeat(u);
        Assert.Null(PlayerHq.FoundIssue(u, Nebelmark));
        u.MoveShip(ship, Alpha);
        Assert.Contains("aktives Schiff", PlayerHq.FoundIssue(u, Nebelmark));
        u.MoveShip(ship, Nebelmark);
        // Größenklasse Konzern: mit 20 000 Cr reicht die Größe nicht (Sektor 10 + Station 3 + Schiff + Kasse ÷ 50000 < 15)
        u.Player.Treasury.Credits = 20_000f;
        Assert.Contains("Größenklasse", PlayerHq.FoundIssue(u, Nebelmark));
        // Konzern, aber Kasse unter den Kosten: Größe über weitere Schiffe ist hier nicht nötig — Schwelle Konzern absenken
        u.Rules.PlayerHqMinClass = 2;
        Assert.Contains("Kasse reicht nicht", PlayerHq.FoundIssue(u, Nebelmark));
        u.Rules.PlayerHq = false;
        Assert.Contains("abgeschaltet", PlayerHq.FoundIssue(u, Nebelmark));
        Assert.Empty(new HqAction().Offers(u));
    }

    [Fact]
    public void Gruendung_KostetBaukosten_KontrolleBisStufe5_HeimatbasisFuerBewaffnete()
    {
        var u = World(1, "fighter");   // Falke: bewaffnet
        var me = Me(u);
        WithSeat(u);
        var ship = u.Ships[u.PlayerShip];
        Assert.True(ship.Has(Capability.Combat));
        var sec = u.Sectors[Nebelmark];
        Assert.Equal(ControlStage.Dominance, sec.Control);
        Assert.Equal(2, SectorControl.MaxLevel(u, sec));
        var t = PlayerHq.HqType(u)!;
        float expected = t.BuildCost * u.Rules.StationBuildCostFactor;
        Assert.Equal(48_000f, expected, 1);   // 40 000 × Größenfaktor XL 1,2
        float w0 = u.Player.Treasury.Credits;
        var ev = new List<SimEvent>(); u.Events.Raised += ev.Add;
        var actions = CommsActions.Default();
        var offer = actions.Preview(u, $"hq:found:{Nebelmark.Value}");
        Assert.True(offer.Possible, offer.Reason);
        Assert.Equal(-expected, offer.Cost, 1);
        var res = actions.Execute(u, $"hq:found:{Nebelmark.Value}");
        Assert.True(res.Success, res.Message);
        Assert.Equal(w0 - expected, u.Player.Treasury.Credits, 1);
        var hq = PlayerHq.Current(u)!;
        Assert.Equal("headquarters", hq.TypeKey);
        Assert.Equal(u.PlayerActor, hq.Owner);
        Assert.Equal(Nebelmark, hq.Sector);
        Assert.Contains(ev.OfType<PlayerHqFounded>(), e => e.Station == hq.Id && Math.Abs(e.Cost - expected) < 1f);
        Assert.Equal(ControlStage.Control, sec.Control);
        Assert.Equal(5, SectorControl.MaxLevel(u, sec));
        Assert.Null(PlayerSeats.LockIssue(u, Nebelmark, 4));
        Assert.Equal(hq.Id, ship.HomeBase);
        // keine Überschneidung mit Stationen/Toren
        foreach (var id in sec.Stations.Where(id => id != hq.Id))
            Assert.True(Vec2.Distance(hq.Position, u.Stations[id].Position) >= hq.Radius + u.Stations[id].Radius);
        // Gericht und Kopfgeld laufen am eigenen HQ: Gericht nimmt nach der Rechtshilferegel an, Kopfgeld gegen die eigene Fraktion gibt es nie
        Assert.True(new CourtService().IsAvailable(u, hq));
        Assert.True(new BountyService().IsAvailable(u, hq));
        Assert.DoesNotContain(new BountyService().Offers(u, hq, ship), o => o.Key.StartsWith("claim:"));
        var union = new FactionId(1);
        Wanted.Report(u, u.PlayerActor, union, "smuggling");
        bool accepts = Relations.Effective(u, union, me) >= u.Rules.ExtraditionRelation;
        Assert.Equal(accepts, new CourtService().Offers(u, hq, ship).Any(o => o.Key == $"pay:{union.Value}"));
    }

    [Fact]
    public void HoechstensEins_AuchEinUebernommenesZaehlt_AufgebenOhneErloes()
    {
        var u = World();
        WithSeat(u);
        var actions = CommsActions.Default();
        Assert.True(actions.Execute(u, $"hq:found:{Nebelmark.Value}").Success);
        var hq = PlayerHq.Current(u)!;
        Assert.Contains("schon ein Hauptquartier", PlayerHq.FoundIssue(u, Nebelmark));
        Assert.False(actions.Execute(u, $"hq:found:{Nebelmark.Value}").Success);
        Assert.Single(PlayerHq.All(u));
        // Stufe 4 setzen, dann aufgeben: kein Erlös, Kontrolle fällt, Stufe sinkt auf den Deckel der Sektorbehörde
        var sec = u.Sectors[Nebelmark];
        Assert.True(actions.Execute(u, $"lock:{Nebelmark.Value}:4").Success);
        float w = u.Player.Treasury.Credits;
        Assert.True(actions.Execute(u, $"hq:close:{hq.Id.Value}").Success);
        Assert.Equal(w, u.Player.Treasury.Credits, 1);
        Assert.False(u.Stations.ContainsKey(hq.Id));
        Assert.Null(PlayerHq.Current(u));
        Assert.Equal(2, SectorControl.MaxLevel(u, sec));
        Assert.True(sec.Access.EntryLevel <= 2);
        Assert.Equal(Me(u), sec.Owner);   // die Behörde hält den Sektor
        Assert.False(actions.Execute(u, $"hq:close:{hq.Id.Value}").Success);
        // übernommenes KI-Hauptquartier (Kapitulation) zählt als sein Hauptquartier: keine Gründung, aber Aufgeben geht
        var aiHq = u.StationsById().First(s => PlayerHq.IsHq(u, s) && s.Owner != u.PlayerActor);
        Capitulation.Capitulate(u, aiHq, u.PlayerActor, u.PlayerActor);
        Assert.Equal(aiHq.Id, PlayerHq.Current(u)!.Id);
        Assert.Contains("schon ein Hauptquartier", PlayerHq.FoundIssue(u, Nebelmark));
        Assert.Contains(new HqAction().Offers(u), o => o.Key == $"hq:close:{aiHq.Id.Value}");
        Assert.DoesNotContain(new HqAction().Offers(u), o => o.Key.StartsWith("hq:found:"));
        Assert.True(actions.Execute(u, $"hq:close:{aiHq.Id.Value}").Success);
        Assert.Null(PlayerHq.FoundIssue(u, Nebelmark));
        // strenge Schlüssel
        Assert.False(actions.Execute(u, "hq:found:x").Success);
        Assert.False(actions.Execute(u, "hq:found:0").Success);
        Assert.False(actions.Execute(u, "hq:close:").Success);
    }

    [Fact]
    public void Unterhalt_AusDerSpielerkasse_NotfallsSchulden_RegelAusBleibtUnbezahlt()
    {
        foreach (bool debt in new[] { true, false })
        {
            var u = World();
            WithSeat(u);
            Assert.True(CommsActions.Default().Execute(u, $"hq:found:{Nebelmark.Value}").Success);
            u.Rules.PlayerGovernmentDebt = debt;
            var t = PlayerHq.HqType(u)!;
            var seat = PlayerSeats.SeatType(u)!;
            u.Player.Treasury.Credits = 1f;
            var ev = new List<SimEvent>(); u.Events.Raised += ev.Add;
            new UpkeepSystem().Step(u, 10);
            float due = (t.UpkeepPerTick + seat.UpkeepPerTick) * 10f;
            if (debt) Assert.Equal(1f - due, u.Player.Treasury.Credits, 1);
            else Assert.True(u.Player.Treasury.Credits >= 0f);
            var gov = ev.OfType<TreasuryUpkeep>().Single(e => e.Actor == u.PlayerActor && e.Purpose == "government");
            if (debt) Assert.Equal(due, gov.Amount, 1); else Assert.True(gov.Unpaid > 0f);
        }
    }

    [Fact]
    public void SpeichernLaden_Bitgleich_MitHauptquartier()
    {
        var u = World(3, "fighter");
        WithSeat(u);
        Assert.True(CommsActions.Default().Execute(u, $"hq:found:{Nebelmark.Value}").Success);
        Assert.True(CommsActions.Default().Execute(u, $"lock:{Nebelmark.Value}:3").Success);
        var (copy, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        Assert.Equal(PlayerHq.Current(u)!.Id, PlayerHq.Current(copy)!.Id);
        Assert.Equal(u.Rules.PlayerHq, copy.Rules.PlayerHq);
        var r1 = new SimulationRunner(); var r2 = new SimulationRunner();
        for (int i = 0; i < 600; i++) { r1.Step(u); r2.Step(copy); }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
    }

    [Fact]
    public void SchalterAus_OhneGruendung_NpcWeltGleich()
    {
        var on = World(3);
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.PlayerHq = false; def.Rules.PlayerGovernmentDebt = false;
        var off = WorldBuilder.Build(def, 3);
        new SimulationRunner().Run(on, 6000);
        new SimulationRunner().Run(off, 6000);
        Assert.Null(PlayerHq.Current(on));
        Assert.Equal(UniverseHash.Compute(off), UniverseHash.Compute(on));
    }
}
