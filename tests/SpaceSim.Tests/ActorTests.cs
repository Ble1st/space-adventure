using System.Text.Json.Nodes;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 9: Akteure und Fraktionen — Fraktion als Akteur, Besitzer statt Fraktion am Schiff, Spieler als Einzelakteur, Sammelgruppe „Freie“,
/// träger Aufstieg, Größe/Größenklassen, Zukauf (gewichtete Ziehung), Kassen aus Vermögensabgaben, sparsame Beziehungen, Persönlichkeit, Save v12.
/// </summary>
public class ActorTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Demotiert einen v12-Spielstand zu v11 (Testhilfe): Akteure entfallen, die Kasse des Spielerakteurs wird wieder <c>PlayerWallet</c>,
    /// <c>Ship.Owner</c> wird wieder <c>Ship.Faction</c> (Mitgliedschaft des Besitzers; freie Einzelschiffe: Hoheit ihres Sektors).
    /// Stände älter als v12 bleiben unverändert.
    /// </summary>
    internal static string ToV11Json(string v12Json)
    {
        v12Json = PoliticsTests.ToV12Json(v12Json);   // Phase 10: erst auf v12 zurück
        var node = JsonNode.Parse(v12Json)!.AsObject();
        if ((int)node["Version"]! < 12) return v12Json;
        node["Version"] = 11;
        var actors = node["Actors"]!.AsArray().ToDictionary(a => (int)a!["Id"]!, a => a!.AsObject());
        int playerActor = (int)node["PlayerActor"]!;
        int? Membership(int id) => actors.TryGetValue(id, out var a) && a["Membership"] is JsonNode m ? (int)m : null;
        var sectorOwner = node["Sectors"]!.AsArray().ToDictionary(s => (int)s!["Id"]!, s => (int)s!["Owner"]!);
        node["PlayerWallet"] = new JsonObject
        {
            ["Credits"] = (float)actors[playerActor]["Credits"]!,
            ["Faction"] = Membership(playerActor) ?? 0,
        };
        foreach (var sh in node["Ships"]!.AsArray())
        {
            var o = sh!.AsObject();
            int owner = (int)o["Owner"]!;
            o["Faction"] = Membership(owner) ?? sectorOwner.GetValueOrDefault((int)o["Sector"]!);
            o.Remove("Owner");
        }
        foreach (var st in node["Stations"]!.AsArray())
            if (st!["Site"] is JsonObject site) site.Remove("InvestorActor");
        node.Remove("Actors"); node.Remove("PlayerActor"); node.Remove("FreeActor"); node.Remove("NextActorId");
        return node.ToJsonString();
    }

    static Universe Fresh() => DemoUniverse.Create();
    /// <summary>Runde 38: Weltbeschreibung mit dem Spieler als Mitglied der Startfraktion (Verhalten bis Runde 37).</summary>
    static WorldDef Runde38Off(WorldDef def) { Runde38Tests.RevertRunde38(def); return def; }

    // ───────── Akteure der Startwelt ─────────

    [Fact]
    public void Startwelt_FraktionenSindAkteure_SpielerUndFreieGibtEs()
    {
        var u = Fresh();
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        foreach (var f in u.Factions.Values)
        {
            var a = u.Actors[f.Id];   // gleiche Id-Zahl
            Assert.Equal(f.Name, a.Name);
            Assert.Equal(f.Id, a.Membership);
            Assert.True(a.IsFaction(u));
            // Runde 38: die Spielerfraktion bucht ihre Schiffe auf die Spielerkasse; KI-Fraktionen betreiben Eigenbetriebe
            Assert.Equal(u.IsPlayerFaction(f.Id), a.ShipsUseTreasury);
        }
        Assert.Equal(ActorKind.State, u.Actors[new ActorId(1)].Kind);
        Assert.Equal(ActorKind.Guild, u.Actors[new ActorId(2)].Kind);
        Assert.Equal(0.8f, u.Actors[new ActorId(1)].Personality.Lawfulness, 3);
        Assert.Equal(0.7f, u.Actors[new ActorId(2)].Personality.Greed, 3);
        // Spieler: Einzelakteur, seine Kasse ist die Spielerkasse; Runde 38: Mitglied seiner eigenen Fraktion (vorher der Fraktion des Spielereintrags)
        Assert.Equal(ActorKind.Player, u.Player.Kind);
        Assert.Equal(u.PlayerFaction, u.Player.Membership);
        Assert.Equal(new FactionId(def.Player.Faction), WorldBuilder.Build(Runde38Off(def)).Player.Membership);
        Assert.Same(u.Player.Treasury, u.PlayerWallet);
        Assert.Equal(def.Player.Credits, u.PlayerWallet.Credits);
        Assert.Equal(ActorKind.Free, u.Actors[u.FreeActor].Kind);
        // Besitzer: Spielerschiff → Spieler, Händler/Miner → Sammelgruppe „Freie“, Bauschiffe → Fraktionsakteur; Stationen → Fraktionsakteur
        foreach (var s in u.Ships.Values)
        {
            if (s.IsPlayer) Assert.Equal(u.PlayerActor, s.Owner);
            else if (s.Role is ShipRole.Trader or ShipRole.Miner or ShipRole.Collector) Assert.Equal(u.FreeActor, s.Owner);   // Gasernte: freie Gasschöpfer
            else if (s.Role == ShipRole.Pirate) Assert.Equal(ActorKind.Outlaw, u.Actors[s.Owner].Kind);   // Phase 11: Kaperschiffe gehören dem gesetzlosen Akteur
            else Assert.True(u.Factions.ContainsKey(new FactionId(s.Owner.Value)));
        }
        // Phase 11: das Piratenversteck gehört dem gesetzlosen Akteur (ohne Fraktion), alle übrigen Stationen der Sektorfraktion
        foreach (var st in u.Stations.Values)
            if (StationProtection.IsHideout(u, st)) Assert.Null(u.FactionOf(st));
            else Assert.Equal(u.Sectors[st.Sector].Owner, u.FactionOf(st));
        Assert.Null(u.FactionOf(u.Ships.Values.First(s => s.Role == ShipRole.Trader)));
    }

    [Fact]
    public void WalletOf_IstDieKasseDesBesitzers_NpcSchiffeBleibenIhreBetriebskasse()
    {
        var u = Fresh();
        var player = u.Ships[u.PlayerShip];
        Assert.Same(u.Player.Treasury, u.WalletOf(player));
        foreach (var s in u.Ships.Values.Where(s => !s.IsPlayer)) Assert.Same(s, u.WalletOf(s));
        // Ein Akteur mit gemeinsamer Kasse: seine Schiffe buchen darauf
        var trader = u.Ships.Values.First(s => s.Role == ShipRole.Trader);
        var firm = new Actor { Id = u.NewActorId(), Name = "Testfirma", Kind = ActorKind.Independent, ShipsUseTreasury = true };
        u.Actors[firm.Id] = firm;
        trader.Owner = firm.Id;
        Assert.Same(firm.Treasury, u.WalletOf(trader));
    }

    [Fact]
    public void Weltdatei_UngueltigeFraktionsangabenWerfen()
    {
        WorldDef Def() => WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        var d1 = Def(); d1.Factions[0].Kind = "empire";
        Assert.Contains("unbekannte Art", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(d1)).Message);
        var d2 = Def(); d2.Factions[0].Relations = new Dictionary<int, float> { [7] = 0.5f };
        Assert.Contains("unbekannter Fraktion", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(d2)).Message);
        var d3 = Def(); d3.Factions[0].Id = 1000;
        Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(d3));
        var d4 = Def(); d4.Ships[0].Owner = "smugglers";   // Phase 11: „pirates“ ist jetzt ein Akteur-Schlüssel der Standardwelt
        Assert.Contains("unbekannter Besitzer", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(d4)).Message);
    }

    // ───────── Größe ─────────

    [Fact]
    public void Groesse_UndGroessenklassen_AusDemBesitz()
    {
        var r = new EconomyRules();
        Assert.Equal(ActorSizeClass.Single, ActorRules.ClassOf(r, ActorRules.Size(r, new Holdings(1, 0, 0, 2000f))));
        Assert.Equal(ActorSizeClass.SmallFirm, ActorRules.ClassOf(r, ActorRules.Size(r, new Holdings(3, 0, 0, 0f))));
        Assert.Equal(ActorSizeClass.SmallFirm, ActorRules.ClassOf(r, ActorRules.Size(r, new Holdings(0, 1, 0, 0f))));
        Assert.Equal(ActorSizeClass.Firm, ActorRules.ClassOf(r, ActorRules.Size(r, new Holdings(6, 1, 0, 0f))));
        Assert.Equal(ActorSizeClass.Corporation, ActorRules.ClassOf(r, ActorRules.Size(r, new Holdings(6, 4, 0, 0f))));
        Assert.Equal(ActorSizeClass.State, ActorRules.ClassOf(r, ActorRules.Size(r, new Holdings(2, 8, 2, 0f))));
        // Geld zählt über die Skala: ein sehr reiches Einzelschiff ist eine Kleinfirma
        Assert.Equal(ActorSizeClass.SmallFirm, ActorRules.ClassOf(r, ActorRules.Size(r, new Holdings(1, 0, 0, 60_000f))));

        var u = Fresh();
        var stats = EconomyStats.Actors(u);
        foreach (var f in u.NpcFactionIds())   // Runde 38: die Spielerfraktion ist so groß wie ihr Besitz (Einzelakteur)
            Assert.Equal(ActorSizeClass.State, stats.Single(a => a.Id == f).Class);
        Assert.Equal(ActorSizeClass.Single, stats.Single(a => a.Id == u.PlayerActor).Class);
        Assert.Equal(u.Ships.Values.Count(s => s.Owner == u.FreeActor), stats.Single(a => a.Id == u.FreeActor).Ships);
    }

    // ───────── Zukauf ─────────

    [Fact]
    public void Zukauf_GewichteteZiehung_GrosseBevorzugt_KleineKommenZumZug()
    {
        var u = Fresh();
        u.Rules.FactionKeepsReserve = false;   // Phase 22: die Betriebsreserve der Kasse ist eigener Gegenstand (Phase22Tests); hier zählt nur die Ziehung
        foreach (var st in u.Stations.Values) st.Credits = 0f;
        foreach (var s in u.Ships.Values) s.Credits = 0f;
        // Gleich viel Geld über der Schwelle: Staatskasse (Appetit 1) gegen freies Einzelschiff (Appetit 0,25)
        u.Actors[new ActorId(1)].Treasury.Credits = 20_000f;
        var trader = u.Ships.Values.Where(s => s.Role == ShipRole.Trader).OrderBy(s => s.Id.Value).First();
        trader.Credits = 20_000f;
        var c = Investors.ForStation(u, 10_000f);
        Assert.Equal(2, c.Count);
        var state = c.Single(x => x.Source == InvestorSource.Treasury);
        var ship = c.Single(x => x.Source == InvestorSource.Ship);
        Assert.Equal(ActorSizeClass.State, state.Class);
        Assert.Equal(ActorSizeClass.Single, ship.Class);
        float expected = 1f * (0.5f + 0.4f) / (0.25f * (0.5f + 0.5f));   // Gier der Union 0,4, Sammelgruppe neutral
        Assert.Equal(expected, state.Weight / ship.Weight, 3);

        int stateWins = 0;
        for (int i = 0; i < 4000; i++) if (Investors.Draw(u, c)!.Source == InvestorSource.Treasury) stateWins++;
        float share = stateWins / 4000f;
        Assert.InRange(share, expected / (expected + 1f) - 0.03f, expected / (expected + 1f) + 0.03f);
        Assert.True(4000 - stateWins > 300, "Kleine springen ein (Zweitzugriff)");
        // Ohne Kandidaten keine Ziehung, ein Kandidat ohne Würfel
        ulong rng = u.Rng.State;
        Assert.Null(Investors.Draw(u, new List<Investor>()));
        Assert.Same(c[0], Investors.Draw(u, new[] { c[0] }));
        Assert.Equal(rng, u.Rng.State);
    }

    [Fact]
    public void Stationsbau_AusDerStaatskasse_TreuhandUndRueckzahlungAnDieKasse()
    {
        var u = Fresh();
        u.Rules.BuildCooldown = 0;
        foreach (var st in u.Stations.Values) st.Credits = 100f;
        foreach (var s in u.Ships.Values) s.Credits = 100f;
        var union = u.Actors[new ActorId(1)];
        union.Treasury.Credits = 1_000_000f;
        // Knappheit erzwingen: Lebensmittel leer. Phase 18: alles andere auf Wunschbestand — die Startlager stehen bei der Hälfte (Verhältnis 2 über
        // der Baugrenze), und der Bau folgt der Kette (Farm → Wasser → Eis); hier geht es um die Kasse als Investor, nicht um die Kette
        foreach (var st in u.Stations.Values) foreach (var (g, e) in st.Market) e.Amount = g == DemoUniverse.Food ? 0f : e.Target;
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        new StationLifecycleSystem().Step(u, 20);
        var chosen = Assert.Single(events.OfType<InvestorChosen>());
        Assert.Equal(InvestorSource.Treasury, chosen.Source);
        Assert.Equal(union.Id, chosen.Holder);
        var site = u.Stations.Values.Single(s => s.Site is { Abandoned: false });
        Assert.Equal(union.Id, site.Owner);
        Assert.Equal(union.Id.Value, site.Site!.InvestorActor);
        Assert.Equal(1_000_000f - site.Site.Budget, union.Treasury.Credits, 1);
        // Verfall: der Rest des Treuhandgelds geht an die Kasse zurück
        float escrow = site.Credits;
        ConstructionRules.Expire(u, site);
        Assert.Equal(1_000_000f - site.Site!.Budget + escrow, union.Treasury.Credits, 1);
    }

    [Fact]
    public void Aufstieg_FreiesSchiffKauftSchiff_WirdEinzelakteurUndBesitztBeide()
    {
        var u = Fresh();
        // Runde 44: Platz für ein weiteres Schiff über der (verdreifachten) Gründerflotte — vorher fest 30 bei 17 Gründer-Händlern
        u.Rules.MaxTraders = (int)MathF.Ceiling(ShipJobs.UsedUnits(u, ShipJobs.Default.First(j => j.Key == "trade"))) + 4;
        foreach (var s in u.Ships.Values) s.Credits = 100f;
        var rich = u.Ships.Values.Where(s => s.Role == ShipRole.Trader).OrderBy(s => s.Id.Value).First();
        rich.Credits = 50_000f;
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        int shipsBefore = u.Ships.Count;
        new FleetSystem().Step(u, 20);
        Assert.Equal(shipsBefore + 1, u.Ships.Count);
        var promoted = Assert.Single(events.OfType<ActorPromoted>());
        Assert.Equal(rich.Id, promoted.Ship);
        var actor = u.Actors[promoted.Actor];
        Assert.Equal(ActorKind.Independent, actor.Kind);
        Assert.Equal(actor.Id, rich.Owner);
        Assert.Equal(u.Sectors[rich.Sector].Owner, actor.Membership);   // gemeldet bei der Hoheit seines Sektors
        var child = u.Ships.Values.Single(s => s.Id.Value >= 1000 && !events.OfType<ActorPromoted>().Any(p => p.Ship == s.Id) && s.Owner == actor.Id);
        Assert.Same(child, u.WalletOf(child));   // Eigenbetrieb: eigene Betriebskasse
        Assert.InRange(actor.Personality.Greed, 0f, 1f);
        Assert.Equal(ActorSizeClass.SmallFirm, EconomyStats.Actors(u).Single(a => a.Id == actor.Id).Class);
        // Kauft er erneut, bleibt es derselbe Akteur
        rich.Credits = 50_000f;
        foreach (var s in u.Ships.Values.Where(s => s != rich)) s.Credits = 100f;
        new FleetSystem().Step(u, 20);
        Assert.Single(events.OfType<ActorPromoted>());
        Assert.Equal(2, u.Ships.Values.Count(s => s.Owner == actor.Id && s != rich));
    }

    [Fact]
    public void Abstieg_AkteurOhneBesitzVerschwindet_FraktionenBleiben()
    {
        var u = Fresh();
        var ship = u.Ships.Values.First(s => s.Role == ShipRole.Miner);
        var id = ActorRules.Promote(u, ship);
        Assert.NotEqual(u.FreeActor, id);
        new ActorSystem().Step(u, 20);
        Assert.True(u.Actors.ContainsKey(id));   // besitzt noch sein Schiff
        u.RemoveShip(ship.Id, "test");
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        new ActorSystem().Step(u, 20);
        Assert.False(u.Actors.ContainsKey(id));
        Assert.Single(events.OfType<ActorRemoved>());
        foreach (var f in u.Factions.Keys) Assert.True(u.Actors.ContainsKey(f));
        Assert.True(u.Actors.ContainsKey(u.PlayerActor));
        Assert.True(u.Actors.ContainsKey(u.FreeActor));
    }

    // ───────── Kasse und Geldmenge ─────────

    [Fact]
    public void Vermoegensabgabe_FliesstInDieFraktionskasse_ZahlerUnveraendert()
    {
        var a = Fresh();
        var b = Fresh();
        a.Rules.TreasuryInvests = b.Rules.TreasuryInvests = false;   // Kassen nur füllen, nicht ausgeben: sonst zahlt ein anderer Investor
        // Phase 13: Forschungsstationen leben aus der Kasse (Budget, Unterhalt) — ohne Kasse liefen sie anders; für diesen Vergleich ruhen sie ohne Unterhalt
        a.Rules.ResearchActive = b.Rules.ResearchActive = false;
        a.StationTypes["research_station"].UpkeepPerTick = b.StationTypes["research_station"].UpkeepPerTick = 0f;
        b.Rules.TaxToTreasuryShare = 0f;   // alte Geldsenke
        // Phase 15: Kassenausgaben (Instandhaltungserstattung, Subvention) und die Steuerbremse hängen vom Kassenstand ab — für den Zahlervergleich aus
        foreach (var w in new[] { a, b }) { w.Rules.MaintenanceShare = 0f; w.Rules.SubsidyPerDay = 0f; w.Rules.TreasuryTargetShare = 0f; }
        // Phase 16: Polizei und Piraten hängen an der Kasse (Polizeikäufe zahlen aus ihr an die Werft, Unterhalt der Wachen) und wirken über Überfälle auf
        // die Zahler zurück — seit der Kupferkette hat die Werft die Module öfter auf Lager, und Welt a kauft schon in den ersten 3000 Ticks Polizei.
        // Für den Zahlervergleich ruht die Sicherheit in beiden Welten.
        foreach (var w in new[] { a, b }) w.Rules.SecurityActive = false;
        foreach (var w in new[] { a, b }) w.Rules.OutpostsBuild = false;   // Phase 30: Kleinstationen werden aus der Kasse gekauft
        // Phase 26: die Reparaturregale der Regierungsstationen (Hüllenplatten, Elektronik) kaufen aus der Stationskasse — die hängt ohne Fraktionskasse
        // am Unterhalt und wirkte über den Handel auf die Zahler zurück; für den Zahlervergleich ohne Regale (wie bis Phase 25)
        foreach (var w in new[] { a, b })
            foreach (var st in w.Stations.Values.Where(s => StationProtection.IsOfficial(w, s)))
                foreach (var key in new[] { "hull_plates", "electronics" })
                    if (w.FindGood(key) is { } g && w.StationTypes[st.TypeKey].Stocks.Any(x => x.Good == g.Id)) st.Market.Remove(g.Id);
        var taxes = new List<TaxCollected>();
        double upkeep = 0;   // Phase 10: Unterhalt der Regierungsstationen und Sperren aus den Kassen
        double income = 0;   // Phase 11: Strafen fließen in Fraktionskassen; Polizei und Kopfgeld zahlen aus ihnen (Piratenkassen zählen hier nicht)
        a.Events.Raised += e =>
        {
            if (e is TaxCollected t) taxes.Add(t);
            else if (e is TreasuryUpkeep k && a.Factions.ContainsKey(new FactionId(k.Actor.Value))) upkeep += k.Amount;
            else if (e is TreasuryIncome ti && a.Factions.ContainsKey(new FactionId(ti.Actor.Value))) income += ti.Amount;
        };
        new SimulationRunner().Run(a, 3000);
        new SimulationRunner().Run(b, 3000);
        // Für alle Zahler ist die Abgabe dieselbe: Stationen und Schiffe identisch, nur die Kassen unterscheiden sich
        // (Phase 10: Regierungsstationen zahlen ihren Unterhalt aus der Kasse — ohne Kasse aus der eigenen; sie sind keine Abgabenzahler)
        foreach (var st in a.Stations.Values.Where(s => !StationProtection.IsOfficial(a, s))) Assert.Equal(b.Stations[st.Id].Credits, st.Credits);
        foreach (var s in a.Ships.Values) Assert.Equal(b.Ships[s.Id].Credits, s.Credits);
        float treasuries = a.NpcFactionIds().Sum(f => a.Actors[f].Treasury.Credits);   // Runde 38: Kassen der KI-Fraktionen (die Spielerkasse ist keine Abgabenkasse)
        Assert.True(treasuries > 0f);
        Assert.Equal(0f, b.NpcFactionIds().Sum(f => b.Actors[f].Treasury.Credits));
        // erklärbar: Kasse = Summe der gemeldeten Abgaben (Gleitkomma-Summen über viele kleine Buchungen: relative Toleranz)
        double net = taxes.Sum(t => (double)t.Amount) - upkeep + income;
        Assert.InRange(treasuries, net - 1e-4 * net - 1, net + 1e-4 * net + 1);
        var snap = EconomyStats.Snapshot(a);
        // Phase 11: die Kassenspalte enthält auch die Kasse des gesetzlosen Akteurs (Piraten)
        Assert.Equal(treasuries + a.Actors.Values.Where(x => x.Kind == ActorKind.Outlaw).Sum(x => x.Treasury.Credits), snap.TreasuryCredits, 1);
        Assert.Equal(snap.StationCredits + snap.ShipCredits + snap.PlayerCredits + snap.TreasuryCredits, snap.TotalCredits, 1);
    }

    [Fact]
    public void Geldmenge_Kassen_SindAusEreignissenErklaerbar_20000Ticks()
    {
        var u = Fresh();
        double taxed = 0, invested = 0, refunded = 0, upkeep = 0, tolls = 0;
        double start = u.Actors.Values.Where(a => a.Id != u.PlayerActor).Sum(a => (double)a.Treasury.Credits);   // Phase 11: Startkasse der Piraten
        u.Events.Raised += e =>
        {
            switch (e)
            {
                case TaxCollected t: taxed += t.Amount; break;
                case TreasuryUpkeep k: upkeep += k.Amount; break;   // Phase 10: Regierungsstationen und Sperren zahlen aus der Kasse
                case TollPaid p: tolls += p.Amount; break;           // Phase 10: Zoll fließt in die Kasse der Sektorfraktion
                case TreasuryIncome ti: tolls += ti.Amount; break;   // Phase 11: Hehlerei der Piraten, Strafen
                case InvestorChosen { Source: InvestorSource.Treasury } c: invested += c.Amount; break;
                case ConstructionAbandoned ab when u.Stations.TryGetValue(ab.Site, out var st) && st.Site is { InvestorActor: > 0 }: refunded += ab.Refund; break;
            }
        };
        new SimulationRunner().Run(u, 20_000);
        // Kassen der Nicht-Spieler-Akteure = Abgaben − Investitionen aus Kassen + Rückzahlungen verfallener Baustellen (relative Toleranz für
        // Gleitkomma-Summen über Tausende kleiner Buchungen)
        double treasuries = u.Actors.Values.Where(a => a.Id != u.PlayerActor).Sum(a => (double)a.Treasury.Credits);
        double expected = start + taxed - invested + refunded - upkeep + tolls;
        Assert.InRange(treasuries, expected - 1e-4 * taxed - 1, expected + 1e-4 * taxed + 1);
        Assert.True(taxed > 0);
    }

    // ───────── Beziehungen ─────────

    [Fact]
    public void Beziehungen_Vorgaben_Abweichungen_Sparsam()
    {
        // Runde 38: dieser Test prüft die Vererbung über die Mitgliedschaft (Spieler als Unionsmitglied) — Regel aus; die eigene Fraktion prüft Runde38Tests
        var u = WorldBuilder.Build(Runde38Off(WorldBuilder.Parse(WorldBuilder.DefaultWorldJson())));
        var union = new ActorId(1);
        var guild = new ActorId(2);
        Assert.Equal(0.25f, Relations.Effective(u, union, guild), 3);                     // Hauptbeziehung aus der Weltdatei
        Assert.Equal(u.Rules.RelationSameFaction, Relations.Effective(u, u.PlayerActor, union), 3);   // Spieler ist Mitglied der Union
        Assert.Equal(0.25f, Relations.Effective(u, u.PlayerActor, guild), 3);                          // erbt die Beziehung seiner Fraktion
        Assert.Equal(u.Rules.RelationFree, Relations.Effective(u, union, u.FreeActor), 3);            // Staat–Freie
        var trader = u.Ships.Values.First(s => s.Role == ShipRole.Trader);
        Assert.Equal(u.Rules.RelationFree, Relations.OfShip(u, trader, union), 3);                    // freie Schiffe teilen die Sammelgruppe
        int stored = Relations.StoredEntries(u);
        Assert.Equal(u.Factions.Count * (u.Factions.Count - 1), stored);   // Phase 21: drei Fraktionen, jede nennt die beiden anderen (vorher 2)

        // persönliche Abweichung des Spielers zur Gilde, dann zurück auf die Vorgabe → Eintrag verschwindet wieder
        Relations.Set(u, u.PlayerActor, guild, -0.4f);
        Assert.Equal(-0.4f, Relations.Effective(u, u.PlayerActor, guild), 3);
        Assert.Equal(stored + 1, Relations.StoredEntries(u));
        Relations.Set(u, u.PlayerActor, guild, 0.25f);
        Assert.Equal(stored, Relations.StoredEntries(u));
        // Hauptbeziehung zwischen Fraktionen auf die Vorgabe → Eintrag entfällt
        Relations.Set(u, union, guild, u.Rules.RelationDefault);
        Assert.Equal(stored - 1, Relations.StoredEntries(u));
        // Gesetzlose: Vorgabe aller zu ihnen
        Assert.Equal(u.Rules.RelationOutlaw, Relations.Default(u, ActorKind.State, ActorKind.Outlaw));
    }

    [Fact]
    public void Beziehungen_KeineExplosion_20000Ticks()
    {
        var u = Fresh();
        int before = Relations.StoredEntries(u);
        new SimulationRunner().Run(u, 20_000);
        // Akteure kommen und gehen, aber gespeichert werden nur Abweichungen (keine N×N-Tabelle)
        Assert.Equal(before, Relations.StoredEntries(u));
        Assert.True(u.Actors.Count < 4 * u.Ships.Count);
        // jede Paarung ist trotzdem auswertbar
        foreach (var a in u.Actors.Keys) foreach (var b in u.Actors.Keys) Assert.InRange(Relations.Effective(u, a, b), -1f, 1f);
    }

    // ───────── Determinismus, Save, Migration ─────────

    [Fact]
    public void Determinismus_UndSaveRoundtrip_MitAkteuren()
    {
        var a = Fresh(); var b = Fresh();
        new SimulationRunner().Run(a, 8000);
        new SimulationRunner().Run(b, 8000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
        // eine persönliche Beziehung und ein Akteur mit Kasse sollen den Roundtrip überstehen
        Relations.Set(a, a.PlayerActor, new ActorId(2), -0.3f);
        var json = SaveGameJson.Write(a, Now);
        Assert.Contains("\"Version\":17", json);   // Phase 14: v17
        Assert.DoesNotContain("PlayerWallet", json);
        var (loaded, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(loaded));
        Assert.Equal(a.Actors.Count, loaded.Actors.Count);
        foreach (var x in a.Actors.Values)
        {
            var y = loaded.Actors[x.Id];
            Assert.Equal((x.Name, x.Kind, x.Membership, x.Treasury.Credits, x.Personality, x.ShipsUseTreasury), (y.Name, y.Kind, y.Membership, y.Treasury.Credits, y.Personality, y.ShipsUseTreasury));
        }
        Assert.Equal(-0.3f, Relations.Effective(loaded, loaded.PlayerActor, new ActorId(2)), 3);
        foreach (var s in a.Ships.Values) Assert.Equal(s.Owner, loaded.Ships[s.Id].Owner);
        new SimulationRunner().Run(a, 3000);
        new SimulationRunner().Run(loaded, 3000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Migration_V11ToV12_FraktionenWerdenAkteure_SpielerkasseBleibt()
    {
        var u = TestWorlds.Classic();
        u.PlayerWallet.Credits = 4321f;
        var v11 = ToV11Json(SaveGameJson.Write(u, Now));
        Assert.Contains("\"Version\":11", v11);
        Assert.Contains("PlayerWallet", v11);
        Assert.DoesNotContain("\"Actors\"", v11);
        var (loaded, _) = SaveGameJson.Read(v11);
        Assert.Equal(4321f, loaded.PlayerWallet.Credits);
        Assert.Equal(u.Player.Membership, loaded.Player.Membership);
        // frische Welt und migrierter Stand sind gleich (Akteure, Besitzer, Persönlichkeit aus der Default-Welt)
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        new SimulationRunner().Run(u, 2000);
        new SimulationRunner().Run(loaded, 2000);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Migration_AlteKette_SchiffeBekommenBesitzer()
    {
        var u = TestWorlds.Classic();   // Phase 23: die eingefrorene Migrationskette kennt die Munition der Standardwelt nicht
        u.PlayerWallet.Credits = 999f;
        var (loaded, _) = SaveGameJson.Read(FuelModelTests.ToV5Json(SaveGameJson.Write(u, Now)));
        Assert.Equal(999f, loaded.PlayerWallet.Credits);
        foreach (var s in loaded.Ships.Values)
        {
            if (s.IsPlayer) Assert.Equal(loaded.PlayerActor, s.Owner);
            else if (s.Role is ShipRole.Trader or ShipRole.Miner) Assert.Equal(loaded.FreeActor, s.Owner);
            else if (s.Role == ShipRole.Pirate) Assert.Equal(ActorKind.Outlaw, loaded.Actors[s.Owner].Kind);   // Phase 11: Kaperschiffe des gesetzlosen Akteurs
            else Assert.True(loaded.Factions.ContainsKey(new FactionId(s.Owner.Value)));
        }
        foreach (var f in loaded.Factions.Keys) Assert.True(loaded.Actors.ContainsKey(f));
        new SimulationRunner().Run(loaded, 2000);
    }
}
