using SpaceSim.Core.Combat;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Ausbau Punkt 17 — zwei Banditenarten (docs/BANDITEN.md): die etablierte gesetzlose Fraktion (Id 4, „Herren der Nebelmark“) mit
/// Sitz (Freistatt), drei Werken, Schutz- und Kaperflotte, Dauerzoll und eigenem Gebiet neben den kleinen Gruppen (Rote Korsaren,
/// Schwarze Klingen). Code-Schalter <see cref="EconomyRules.BanditFactions"/> aus = Standard, Standardwelt an; aus (oder ohne
/// solche Fraktion) rechnet die NPC-Welt bitgleich wie vorher (<see cref="NpcWeltOhneBanditen_Bitgleich"/>).
/// </summary>
public class BanditenTests
{
    static readonly SectorId Nebelmark = new(9), Abgrund = new(11), Alpha = new(1), Delta = new(4);
    static readonly FactionId Bandits = new(4);
    static readonly ActorId BanditActor = new(4);

    /// <summary>
    /// Ausbau Punkt 17 zurücknehmen (Referenz-Hash-Tests; Teil der Rücknahmekette über <see cref="BalanceFixTests.RevertBalanceFix"/>
    /// und <see cref="GasNebulaTests.RevertGas"/>): Fraktion 4, ihre vier Stationen und Schiffsgruppen entfernen, Sektor 11 wieder
    /// herrenlos, Obergrenze zurück, Schalter aus. Der Weltaufbau ließe den Inhalt sonst selbst weg (WorldBuilder) — hier wird die
    /// WorldDef zusätzlich bereinigt, weil Tests sie direkt untersuchen.
    /// </summary>
    internal static void RevertBandits(WorldDef def)
    {
        foreach (var sd in def.Sectors)
        {
            sd.Stations.RemoveAll(s => s.Id is >= 91 and <= 94 || s.Owner == "bandits");
            if (sd.Id == 11) sd.Owner = 0;
        }
        def.Ships.RemoveAll(g => g.Faction == 4);
        def.Factions.RemoveAll(f => f.Id == 4);
        var r = def.Rules ??= new EconomyRules();
        r.BanditFactions = false;
        if (r.MaxStations == 78) r.MaxStations = 75;   // erst hier, nicht im WorldBuilder — sonst zöge die Builder-Regel doppelt ab
    }

    static Universe World(ulong seed = 1) => WorldBuilder.Build(TestWorlds.Def(fuel: false, research: false), seed);

    // ───────── 1: Schalter ─────────

    [Fact]
    public void Schalter_CodeAus_StandardweltAn_RuecknahmeAus()
    {
        var c = new EconomyRules();
        Assert.False(c.BanditFactions);
        Assert.Equal(1, c.BanditTollLevel);
        Assert.Equal(2, c.BanditWarships);
        Assert.Equal(3f, c.BanditMaxRaiderUnits);
        Assert.Empty(c.BanditExpansionSectors);
        Assert.Empty(c.BanditEconomyTypes);
        var u = World();
        Assert.True(u.Rules.BanditFactions);
        Assert.Equal(new[] { 9 }, u.Rules.BanditExpansionSectors);
        Assert.Equal(3, u.Rules.BanditEconomyTypes.Count);
        // Rücknahme: gebaute Welt hat weder Fraktion 4 noch ihre Stationen/Schiffe, Sektor 11 herrenlos, Obergrenze 75
        var def = TestWorlds.Def(fuel: false, research: false);
        RevertBandits(def);
        var off = WorldBuilder.Build(def, 1);
        Assert.False(off.Factions.ContainsKey(Bandits));
        Assert.DoesNotContain(off.Stations.Values, s => s.Id.Value is >= 91 and <= 94);
        Assert.DoesNotContain(off.Ships.Values, s => s.Owner == BanditActor);
        Assert.Equal(75, off.Rules.MaxStations);
        Assert.Equal(0, off.Sectors[Abgrund].Owner.Value);
        var ev = new List<SimEvent>(); off.Events.Raised += ev.Add;
        new SimulationRunner().Run(off, 6000);
        Assert.DoesNotContain(ev, e => e is SectorOwnerChanged);
        Assert.DoesNotContain(ev, e => e is TollPaid t && t.To == Bandits);
        // nur der Schalter aus (Inhalt bleibt in der Datei): WorldBuilder lässt Fraktion/Stationen/Schiffe selbst weg, Obergrenze sinkt um die 3 Werke
        var def2 = TestWorlds.Def(fuel: false, research: false);
        def2.Rules!.BanditFactions = false;
        var off2 = WorldBuilder.Build(def2, 1);
        Assert.False(off2.Factions.ContainsKey(Bandits));
        Assert.False(off2.Actors.ContainsKey(BanditActor));
        Assert.DoesNotContain(off2.Stations.Values, s => s.Id.Value is >= 91 and <= 94);
        Assert.DoesNotContain(off2.Ships.Values, s => s.Owner == BanditActor);
        Assert.Equal(0, off2.Sectors[Abgrund].Owner.Value);
        Assert.Equal(75, off2.Rules.MaxStations);   // 78 − 3 weggelassene Wirtschaftsstationen (der Sitz als Versteck zählt nicht)
    }

    // ───────── 2: Inhalt ─────────

    [Fact]
    public void Inhalt_Fraktion_Sitz_Werke_Flotten()
    {
        var u = World();
        Assert.True(u.Factions.ContainsKey(Bandits));
        var a = u.Actors[BanditActor];
        Assert.Equal(ActorKind.Outlaw, a.Kind);
        Assert.Equal(Bandits, a.Membership);
        Assert.Equal(22000f, a.Treasury.Credits);
        Assert.True(Simulation.Bandits.IsEstablished(u, a.Id));
        Assert.True(Simulation.Bandits.IsEstablished(u, Bandits));
        var seatType = u.StationTypes["bandit_seat"];
        Assert.True(seatType.Hideout);
        Assert.Equal(1.2f, seatType.Security);
        Assert.Equal(3f, seatType.Influence);
        Assert.Equal(GovernmentKind.None, seatType.Government);
        Assert.Equal(new[] { "trade", "blackmarket", "hangar" }, seatType.Services);
        Assert.False(seatType.Capturable);
        Assert.Equal(4, seatType.Defense!.Guns.Count);
        // Sitz und Werke in Abgrund, Besitzer = Fraktion 4 (erbt den Sektorherrn)
        var seat = u.Stations[new StationId(91)];
        Assert.Equal("bandit_seat", seat.TypeKey);
        Assert.True(StationProtection.IsHideout(u, seat));
        Assert.Equal(BanditActor, seat.Owner);
        Assert.Equal(Abgrund, seat.Sector);
        foreach (var (id, type) in new[] { (92, "steelworks"), (93, "plating_works"), (94, "electronics_fab") })
        {
            var st = u.Stations[new StationId(id)];
            Assert.Equal(type, st.TypeKey);
            Assert.Equal(BanditActor, st.Owner);
            Assert.Equal(Abgrund, st.Sector);
            Assert.True(st.Production.Count > 0, type);                 // echte Werke mit Rezepten
            Assert.False(StationProtection.IsHideout(u, st));
            Assert.DoesNotContain(st.Production.SelectMany(r => r.Outputs.Keys), g => u.Modules.ContainsKey(u.Goods[g].Key));   // keine Modul-Waren (keine Forschungs-Sperre)
        }
        // Kaper- und Schutzflotte mit Heimatbasis = Sitz
        var mine = u.ShipsById().Where(s => s.Owner == BanditActor).ToList();
        Assert.Equal(2, mine.Count(s => s.Role == ShipRole.Pirate));
        Assert.Equal(2, mine.Count(s => s.Role == ShipRole.Warship && Wars.IsWarship(s)));
        Assert.All(mine, s => Assert.Equal(seat.Id, s.HomeBase));
        Assert.All(mine, s => Assert.Equal(Abgrund, s.Sector));
        // die kleinen Gruppen sind nicht etabliert
        foreach (var o in u.Actors.Values.Where(x => x.Kind == ActorKind.Outlaw && !x.IsFaction(u)))
            Assert.False(Simulation.Bandits.IsEstablished(u, o.Id), o.Name);
        Assert.Equal(2, u.Actors.Values.Count(x => x.Kind == ActorKind.Outlaw && !x.IsFaction(u)));
    }

    // ───────── 3: Platzierung ─────────

    [Fact]
    public void Platzierung_VierStationenFrei_GuertelVoll_SpielraumWieVorher()
    {
        var u = World();
        var sec = u.Sectors[Abgrund];
        foreach (var id in new[] { 91, 92, 93, 94 })
        {
            var st = u.Stations[new StationId(id)];
            var chk = StationPlacement.Check(u, sec, st.Position, st.Radius);
            Assert.True(chk.Ok, $"Station {id}: {chk.Reason}");
        }
        // Gürtel behält seine 12 Asteroiden (Abstand Feldmitte–Station groß genug für jeden Felsen)
        Assert.Equal(12, Belt.AsteroidsOf(sec, sec.Fields[0]).Count());
        // Bau-Spielraum gleich: 78 − wirtschaftliche Stationen = 6 wie vorher 75 − 69
        Assert.Equal(78, u.Rules.MaxStations);
        Assert.Equal(6, u.Rules.MaxStations - StationProtection.CountEconomic(u));
    }

    // ───────── 4: Sektorbesitz, Dominanz, Dauerzoll ─────────

    [Fact]
    public void SektorAbgrund_GehoertDenBanditen_DominanzUndZoll()
    {
        var u = World();
        var sec = u.Sectors[Abgrund];
        Assert.Equal(Bandits, sec.Owner);
        Assert.Equal(ControlStage.Dominance, sec.Control);
        Assert.Equal(2, SectorControl.MaxLevel(u, sec));
        Assert.Contains(sec.Id, u.Factions[Bandits].Territory);
        Assert.Equal(0, u.Sectors[Nebelmark].Owner.Value);   // Nebelmark bleibt herrenlos (kein Spieler-Sitz)
        var ev = new List<SimEvent>(); u.Events.Raised += ev.Add;
        new SimulationRunner().Run(u, 40);
        Assert.Equal(1, sec.Access.EntryLevel);
        Assert.Equal(-1, sec.Access.ExpiresTick);            // Dauerzoll, kein Ablauf
        Assert.True(sec.Access.Enforced);                    // ohne Verwaltungskosten durchgesetzt
        Assert.Contains(ev, e => e is SectorAccessChanged sac && sac.Sector == Abgrund && sac.Reason == "bandit");   // einmal gesetzt
    }

    // ───────── 5: Zoll ─────────

    [Fact]
    public void Zoll_WirdErhoben_UndFliesstInDieKasse_EigenesUndSpielerSchiff()
    {
        var u = World();
        new SimulationRunner().Run(u, 40);
        var sec = u.Sectors[Abgrund];
        var gate = u.Gates[sec.Gates[0]];   // einziges Tor des Abgrunds (Hauptroute)
        Assert.Equal(Delta, u.Sectors[u.Gates[gate.Target].Sector].Id);
        // freier Händler zahlt Zoll an die Banditenkasse
        var trader = u.Ships.Values.First(s => s.Role == ShipRole.Trader && s.Owner == u.FreeActor);
        trader.Credits = 1000f;
        var pass = GateAccess.Evaluate(u, gate, GateAccess.ProfileOf(u, trader), trader);
        Assert.Equal(AccessPolicy.Toll, pass.Policy);
        Assert.Equal(u.Rules.GateTollPerLevel, pass.Toll, 3);
        Assert.Equal(Bandits, pass.TollTo);
        float k0 = u.Actors[BanditActor].Treasury.Credits;
        float paid = GateAccess.PayToll(u, trader, gate, pass);
        Assert.Equal(pass.Toll, paid, 3);
        Assert.Equal(k0 + paid, u.Actors[BanditActor].Treasury.Credits, 3);
        // eigenes Kaperschiff passiert ohne Zoll (Mitglied)
        var raider = u.Ships.Values.First(s => s.Role == ShipRole.Pirate && s.Owner == BanditActor);
        var pass2 = GateAccess.Evaluate(u, gate, GateAccess.ProfileOf(u, raider), raider);
        Assert.True(pass2.Allowed);
        Assert.Equal(0f, pass2.Toll);
        // Spieler mit Fracht wird bei Stufe 1 nicht abgewiesen (Embargo erst ab Stufe 2), zahlt aber Zoll
        var me = u.Ships[u.PlayerShip];
        me.Cargo[u.FindGood("food")!.Id] = 10f;
        var pass3 = GateAccess.Evaluate(u, gate, GateAccess.ProfileOf(u, me), me);
        Assert.True(pass3.Allowed);
        Assert.Equal(u.Rules.GateTollPerLevel, pass3.Toll, 3);
    }

    // ───────── 6: keine Fremdsperren, keine Beziehungsdrift ─────────

    [Fact]
    public void KeineFremdsperren_UndKeineBeziehungsdrift_20000Ticks()
    {
        var u = World();
        var locks = new List<SectorAccessChanged>();
        u.Events.Raised += e => { if (e is SectorAccessChanged sac) locks.Add(sac); };
        new SimulationRunner().Run(u, 20_000);
        // Sperren nur in Bandiensektoren (Abgrund dauerhaft Stufe 1)
        foreach (var s in u.Sectors.Values)
            if (s.Access.EntryLevel > 0) Assert.True(Simulation.Bandits.HoldsSector(u, s), s.Name);
        Assert.All(locks, l => Assert.True(Simulation.Bandits.HoldsSector(u, l.Sector), $"Sperre in {l.Sector}"));
        // WorstRelation der Staaten sieht nicht die Banditen
        foreach (var f in u.NpcFactionIds().Where(f => !Simulation.Bandits.IsEstablished(u, f)))
        {
            Assert.NotEqual(Bandits, FactionPolicySystem.WorstRelation(u, f).Against);
            Assert.Equal(u.Rules.RelationOutlaw, Relations.Effective(u, f, BanditActor), 3);   // keine Drift, auch nach 20000 Ticks
        }
        Assert.Equal(u.Rules.RelationOutlaw, Relations.Effective(u, BanditActor, new FactionId(1)), 3);
        // Gegenprobe: ohne die Ausnahme wäre die Vorgabe −0,6 Sperrstufe 3 für jede KI-Fraktion
        Assert.Equal(3, FactionPolicySystem.LevelFor(u.Rules, u.Rules.RelationOutlaw, ActorRules.PersonalityOf(u, new FactionId(1)).Aggression));
        Assert.False(MapParts.Of(u).IsSplit);
    }

    // ───────── 7: keine Diplomatie, kein Krieg, keine Bündnisse ─────────

    [Fact]
    public void KeineDiplomatie_KeinKrieg_KeineBuendnisse()
    {
        var u = World();
        Assert.False(Wars.CanWage(u, Bandits));
        Assert.NotNull(Wars.DeclareIssue(u, new FactionId(1), Bandits));
        Assert.NotNull(Wars.PlayerDeclareIssue(u, Bandits));
        Assert.NotNull(Alliances.FormIssue(u, u.PlayerFaction!.Value, Bandits));
        var offers = CommsActions.Default().Offers(u);
        Assert.DoesNotContain(offers, o => o.Key.Contains($":{Bandits.Value}"));
        Assert.DoesNotContain(u.Wars, w => w.Involves(Bandits));
        // die Schutzflotte steht unter der Rolle, nicht als Kriegspartei
        Assert.Equal(2, Wars.WarshipsOf(u, Bandits).Count);
    }

    // ───────── 8: Schutzflotte ─────────

    [Fact]
    public void Schutzflotte_Kauf_Unterhalt_UndRuecklage()
    {
        var u = World();
        var a = u.Actors[BanditActor];
        foreach (var s in u.ShipsById().Where(s => Wars.IsWarship(s) && s.Owner == BanditActor).ToList())
            Departures.RemoveShip(u, s, DepartureReason.Destroyed, "test");
        u.Rules.BanditFleetCooldownTicks = 0;   // beide Käufe in aufeinanderfolgenden Schritten
        u.Rules.BanditHoardShare = 0f;           // Senke aus, damit die Kassenarithmetik exakt prüfbar ist
        a.Treasury.Credits = 50_000f;
        a.LastForceTick = -1_000_000;
        var ev = new List<SimEvent>(); u.Events.Raised += ev.Add;
        float cost = CombatModels.MaterialValue(u, u.ShipModels[u.Rules.BanditWarshipModel]) * u.Rules.StationBuildCostFactor;
        new BanditSystem().Step(u, BanditSystem.Interval);
        new BanditSystem().Step(u, BanditSystem.Interval);
        var warships = u.ShipsById().Where(s => Wars.IsWarship(s) && s.Owner == BanditActor).ToList();
        Assert.Equal(2, warships.Count);
        Assert.All(warships, s => Assert.Equal(u.Stations[new StationId(91)].Id, s.HomeBase));
        Assert.Equal(2, ev.OfType<TreasuryUpkeep>().Count(k => k.Purpose == "bandit_build" && MathF.Abs(k.Amount - cost) < 0.01f));
        // Kasse: 2 Käufe + Unterhalt des einen vorhandenen Schiffs im zweiten Schritt
        Assert.Equal(50_000f - 2f * cost - u.Rules.BanditWarshipUpkeepPerTick * BanditSystem.Interval, a.Treasury.Credits, 1);
        // Unterhalt mindert die Kasse und wird gebucht
        float k0 = a.Treasury.Credits;
        ev.Clear();
        new BanditSystem().Step(u, BanditSystem.Interval);
        float upkeep = u.Rules.BanditWarshipUpkeepPerTick * 2 * BanditSystem.Interval;
        Assert.Equal(k0 - upkeep, a.Treasury.Credits, 3);
        Assert.Contains(ev, e => e is TreasuryUpkeep k && k.Purpose == "bandit_fleet" && MathF.Abs(k.Amount - upkeep) < 0.01f);
        // leere Kasse: Unterhalt wird als unbezahlt gemeldet
        a.Treasury.Credits = 0f;
        ev.Clear();
        new BanditSystem().Step(u, BanditSystem.Interval);
        Assert.Contains(ev, e => e is TreasuryUpkeep k && k.Purpose == "bandit_fleet" && k.Unpaid > 0f);
        // unter der Rücklage wird nicht gekauft
        foreach (var s in warships) Departures.RemoveShip(u, s, DepartureReason.Destroyed, "test");
        a.Treasury.Credits = u.Rules.BanditWarshipReserve + 10f;
        a.LastForceTick = -1_000_000;
        new BanditSystem().Step(u, BanditSystem.Interval);
        Assert.DoesNotContain(u.ShipsById(), s => Wars.IsWarship(s) && s.Owner == BanditActor);
    }

    // ───────── 9: Schutzflotte verteidigt ─────────

    [Fact]
    public void Schutzflotte_VerteidigtEigenesWerk()
    {
        var u = World();
        var work = u.Stations[new StationId(92)];
        var warships = u.ShipsById().Where(s => Wars.IsWarship(s) && s.Owner == BanditActor).ToList();
        Assert.Equal(2, warships.Count);
        // fremder Angreifer (freie Gruppe) belagert das Werk
        var atk = ShipPurchase.Create(u, u.NewShipId(), "corsair", u.ShipClasses[u.ShipModels["corsair"].ClassKey].Slots,
            u.ShipModels["corsair"].Fit, ShipRole.Pirate, Controller.Ai, u.FreeActor, work, 0f);
        u.AddShip(atk);
        var e = Siege.Start(u, atk, work);
        Siege.Reinforce(u, e);
        var defenders = e.Participants.Where(p => p.Active && p.Side == 1).Select(p => p.Ship).ToList();
        Assert.Equal(2, defenders.Count(id => warships.Any(w => w.Id == id)));
    }

    // ───────── 10: Kaperflotte ─────────

    [Fact]
    public void Kaperflotte_NutztDieHoehereGrenze_UndBautImSitzNach()
    {
        var u = World();
        var a = u.Actors[BanditActor];
        var small = u.Actors.Values.First(x => x.Kind == ActorKind.Outlaw && !Simulation.Bandits.IsEstablished(u, x.Id));
        Assert.Equal(u.Rules.BanditMaxRaiderUnits, Simulation.Bandits.RaiderUnitsCap(u, a));
        Assert.Equal(u.Rules.MaxRaiderUnitsPerActor, Simulation.Bandits.RaiderUnitsCap(u, small));
        // die Grenze lässt 3 Einheiten zu (zwei Korsaren + Platz für einen dritten)
        Assert.NotNull(CombatModels.ForPirates(u, 100_000f, Simulation.Bandits.RaiderUnitsCap(u, a), 0f));
        // Nachbau am Sitz nach Verlust
        foreach (var r in u.ShipsById().Where(s => ThreatMap.IsRaider(s) && s.Owner == BanditActor).ToList())
            Departures.RemoveShip(u, r, DepartureReason.Destroyed, "test");
        a.Treasury.Credits = 50_000f;
        a.LastForceTick = -1_000_000;
        var ev = new List<SimEvent>(); u.Events.Raised += ev.Add;
        var runner = new SimulationRunner();
        runner.Run(u, 60);   // Book läuft alle 50 Ticks
        var built = ev.OfType<RaiderBuilt>().FirstOrDefault(b => b.Actor == BanditActor);
        Assert.NotNull(built);
        var ship = u.Ships[built!.Ship];
        Assert.Equal(ShipRole.Pirate, ship.Role);
        Assert.Equal(u.Stations[new StationId(91)].Id, ship.HomeBase);
        float units = u.ShipsById().Where(s => ThreatMap.IsRaider(s) && s.Owner == BanditActor)
            .Sum(s => CombatModels.Units(u.ShipClasses[u.ShipModels[s.ModelKey].ClassKey]));
        Assert.True(units <= u.Rules.BanditMaxRaiderUnits + 1e-3f, $"Kaperflotte {units} Einheiten");
    }

    // ───────── 11: Stationsgeschütze ─────────

    [Fact]
    public void Geschuetze_TreffenKeineUnbeteiligten_NurBeiEigenerBelagerung()
    {
        var u = World();
        var sector = u.Sectors[Abgrund];
        var seat = u.Stations[new StationId(91)];
        // Kaperschiff der Klingen und freier Händler im Gefecht in Abgrund, nahe dem Sitz
        var raider = u.Ships.Values.First(s => ThreatMap.IsRaider(s) && s.Owner != BanditActor && s.Sector == Abgrund);
        var victim = u.Ships.Values.First(s => s.Role == ShipRole.Trader);
        victim.Position = seat.Position + new Vec2(30f, 0f);
        raider.Position = seat.Position + new Vec2(-30f, 0f);
        var e = new Encounter { Id = 1, Sector = Abgrund, Cause = EncounterCause.Raid, Raider = raider.Id, Victim = victim.Id };
        e.Participants.Add(new CombatParticipant { Ship = raider.Id, Side = 0, Active = true });
        e.Participants.Add(new CombatParticipant { Ship = victim.Id, Side = 1, Active = true });
        var ev = new List<SimEvent>(); u.Events.Raised += ev.Add;
        StationCombat.Fire(u, e, 10f);
        Assert.DoesNotContain(ev, x => x is StationShot ss && ss.Station.Value is 91 or 92 or 93 or 94);   // kein Flächenbrand
        // Belagerung des Sitzes: die Angreifer werden beschossen
        var atk = ShipPurchase.Create(u, u.NewShipId(), "corsair", u.ShipClasses[u.ShipModels["corsair"].ClassKey].Slots,
            u.ShipModels["corsair"].Fit, ShipRole.Pirate, Controller.Ai, u.FreeActor, seat, 0f);
        u.AddShip(atk);
        var e2 = new Encounter { Id = 2, Sector = Abgrund, Cause = EncounterCause.Siege, Station = seat.Id };
        e2.Participants.Add(new CombatParticipant { Ship = atk.Id, Side = 0, Active = true });
        ev.Clear();
        StationCombat.Fire(u, e2, 10f);
        Assert.Contains(ev, x => x is StationShot ss && ss.Station == seat.Id && ss.Target == atk.Id);
    }

    // ───────── 12: Nachbau der Werke ─────────

    [Fact]
    public void Wirtschaft_WirdNachgebaut_KostetGeld_UndBrauchtZeit()
    {
        var u = World();
        var a = u.Actors[BanditActor];
        var steelType = u.StationTypes["steelworks"];
        float cost = steelType.BuildCost * u.Rules.BanditRebuildMoneyFactor;
        Departures.RemoveStation(u, u.Stations[new StationId(92)], DepartureReason.Destroyed, "test");
        a.Treasury.Credits = 100_000f;
        a.LastBanditTick = -1_000_000;
        var ev = new List<SimEvent>(); u.Events.Raised += ev.Add;
        float kBefore = a.Treasury.Credits;
        new BanditSystem().Step(u, BanditSystem.Interval);
        var rebuilt = u.StationsById().FirstOrDefault(s => s.TypeKey == "steelworks" && s.Owner == BanditActor && s.Id.Value != 92);
        Assert.NotNull(rebuilt);
        Assert.Equal(Abgrund, rebuilt!.Sector);
        Assert.Equal(u.Tick, a.LastBanditTick);
        Assert.True(kBefore - a.Treasury.Credits >= cost - 0.01f, $"Kasse {kBefore} → {a.Treasury.Credits}, Kosten {cost} (plus Unterhalt)");
        Assert.Contains(ev, e => e is TreasuryUpkeep k && k.Purpose == "bandit_build" && MathF.Abs(k.Amount - cost) < 0.01f);
        Assert.Contains(ev, e => e is StationBuilt b && b.Station == rebuilt.Id);
        // Cooldown: nächster Schritt baut nichts (auch nicht die entfernte Nr. 93)
        Departures.RemoveStation(u, u.Stations[new StationId(93)], DepartureReason.Destroyed, "test");
        int n = u.Stations.Count;
        new BanditSystem().Step(u, BanditSystem.Interval);
        Assert.Equal(n, u.Stations.Count);
        // ohne Geld kein Bau
        a.Treasury.Credits = 0f;
        a.LastBanditTick = -1_000_000;
        Departures.RemoveStation(u, u.Stations[new StationId(94)], DepartureReason.Destroyed, "test");
        n = u.Stations.Count;
        new BanditSystem().Step(u, BanditSystem.Interval);
        Assert.Equal(n, u.Stations.Count);
    }

    // ───────── 13: Vorposten (Sektor erobern) ─────────

    [Fact]
    public void Vorposten_NimmtNebelmark_NurEinmal_UndNieMitSpielerbesitz()
    {
        var u = World();
        var a = u.Actors[BanditActor];
        a.Treasury.Credits = 200_000f;
        a.LastBanditTick = -1_000_000;
        var ev = new List<SimEvent>(); u.Events.Raised += ev.Add;
        new BanditSystem().Step(u, BanditSystem.Interval);
        var outpost = u.StationsById().FirstOrDefault(s => s.Sector == Nebelmark && s.Owner == BanditActor);
        Assert.NotNull(outpost);
        Assert.Equal(u.Rules.BanditExpansionType, outpost!.TypeKey);
        Assert.Equal(Bandits, u.Sectors[Nebelmark].Owner);
        Assert.Contains(Nebelmark, u.Factions[Bandits].Territory);
        Assert.Contains(ev, e => e is SectorOwnerChanged sc && sc.Sector == Nebelmark && sc.To == Bandits);
        // nur ein Vorposten je Sektor
        int n = u.Sectors[Nebelmark].Stations.Count;
        a.LastBanditTick = -1_000_000;
        new BanditSystem().Step(u, BanditSystem.Interval);
        Assert.Equal(n, u.Sectors[Nebelmark].Stations.Count);
        // mit Spielerbesitz in Nebelmark bleibt der Sektor herrenlos
        var u2 = World();
        var sec2 = u2.Sectors[Nebelmark];
        var spot = Outposts.FreeSpot(u2, sec2, new Vec2(0f, 0f), 10f, 28f, 70f)!.Value;
        var pst = StationFactory.Create(u2, u2.NewStationId(), u2.StationTypes["solar_plant"], "Teststation", Nebelmark, spot, u2.PlayerActor);
        u2.AddStation(pst);
        u2.Actors[BanditActor].Treasury.Credits = 200_000f;
        u2.Actors[BanditActor].LastBanditTick = -1_000_000;
        new BanditSystem().Step(u2, BanditSystem.Interval);
        Assert.Equal(0, u2.Sectors[Nebelmark].Owner.Value);
        Assert.DoesNotContain(u2.Stations.Values, s => s.Sector == Nebelmark && s.Owner == BanditActor);
    }

    // ───────── 14: Sektor verlieren und zurückgewinnen ─────────

    [Fact]
    public void SektorVerlieren_AusgeschalteterSitz_UndZurueckgewinn()
    {
        var u = World();
        var a = u.Actors[BanditActor];
        var sec = u.Sectors[Abgrund];
        var seat = u.Stations[new StationId(91)];
        var ev = new List<SimEvent>(); u.Events.Raised += ev.Add;
        // Werke weg, Sitz ausgeschaltet (Versteck: nie zerstört, nur AUS) → keine eigene aktive Station → Sektor fällt herrenlos
        foreach (var id in new[] { 92, 93, 94 })
            Departures.RemoveStation(u, u.Stations[new StationId(id)], DepartureReason.Destroyed, "test");
        seat.Condition = new StationCondition { Disabled = true };
        SectorControl.Update(u, sec);
        Assert.Equal(0, sec.Owner.Value);
        Assert.DoesNotContain(sec.Id, u.Factions[Bandits].Territory);
        Assert.Contains(ev, e => e is SectorOwnerChanged sc && sc.Sector == Abgrund && sc.To.Value == 0);
        // die Fraktion bleibt handlungsfähig (das Versteck zählt als Sitz, auch ausgeschaltet)
        Assert.Empty(FactionViability.Check(u, a.Id));
        // Sitz wieder in Betrieb → Einfluss kehrt zurück → nächster Kontrollschritt holt den Sektor (Besitzer war 0 → sofortiger Wechsel)
        seat.Condition.Disabled = false;
        SectorControl.Update(u, sec);
        Assert.Equal(Bandits, sec.Owner);
        Assert.Contains(ev, e => e is SectorOwnerChanged sc && sc.Sector == Abgrund && sc.To == Bandits);
    }

    // ───────── 15: Kasse erklärbar ─────────

    [Fact]
    public void Kasse_BleibtErklaerbar_20000Ticks()
    {
        var u = World();
        var purposes = new HashSet<string>();
        u.Events.Raised += e =>
        {
            if (e is TreasuryUpkeep k && k.Actor == BanditActor) purposes.Add(k.Purpose);
            if (e is TreasuryIncome i && i.Actor == BanditActor) purposes.Add(i.Purpose);
        };
        new SimulationRunner().Run(u, 20_000);
        Assert.True(u.Actors[BanditActor].Treasury.Credits >= 0f);
        var known = new HashSet<string> { "bandit_fleet", "bandit_build", "bandit_share", "fence", "blackmarket", "maintenance", "wear_emergency", "government", "lock", "police", "fleet", "outpost", "subsidy" };
        Assert.Subset(purposes, known);   // keine unbekannten Zwecke
        Assert.Contains("bandit_fleet", purposes);   // die Schutzflotte kostet Unterhalt
    }

    // ───────── 16: Speichern/Laden ─────────

    [Fact]
    public void SpeichernLaden_BitIdentisch_LastBanditTickErhalten()
    {
        var u = World(3);
        new SimulationRunner().Run(u, 3000);
        u.Actors[BanditActor].LastBanditTick = 12345;
        var (copy, _) = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        Assert.Equal(12345, copy.Actors[BanditActor].LastBanditTick);
        new SimulationRunner().Run(u, 600);
        new SimulationRunner().Run(copy, 600);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
    }

    // ───────── 17: Schwarzmarkt und Fahndung ─────────

    [Fact]
    public void Schwarzmarkt_AmSitz_UndFahndungNichtBeiSichSelbst()
    {
        var u = World();
        var seat = u.Stations[new StationId(91)];
        Assert.True(BlackMarket.IsAt(u, seat));
        var food = u.FindGood("food")!.Id;
        BlackMarket.Stash(u, BanditActor, food, 5f);
        Assert.Equal(5f, seat.BlackStock[food]);
        // Überfall der Banditen in Alpha (Besitzer Union): Fahndung bei der Union
        var raider = u.Ships.Values.First(s => s.Role == ShipRole.Pirate && s.Owner == BanditActor);
        var victim = u.Ships.Values.First(s => s.Role == ShipRole.Trader && s.Owner == u.FreeActor);
        victim.Cargo[food] = 20f;
        PirateSystem.ResolveRaid(u, raider, victim, u.Sectors[Alpha]);
        Assert.True(u.Actors[BanditActor].Wanted.ContainsKey(new FactionId(1)));
        // Überfall im eigenen Sektor (Abgrund, Besitzer = 4): keine Selbstanzeige
        Assert.False(u.Actors[BanditActor].Wanted.ContainsKey(Bandits));
    }

    // ───────── 18: NPC-Welt ohne Banditen bitgleich ─────────

    [Fact]
    public void NpcWeltOhneBanditen_Bitgleich()
    {
        // Referenzwert des Stands davor (wie GasNebulaTests: volle Rücknahmekette inkl. RevertBandits, 20000 Ticks)
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        GasNebulaTests.RevertGas(def);
        var u = WorldBuilder.Build(def);
        new SimulationRunner().Run(u, 20_000);
        Assert.Equal(0x8EEEBEA81B038A74UL, UniverseHash.Compute(u));
        // derselbe Stand, aber nur der Schalter aus (Inhalt bleibt in der Datei — der WorldBuilder lässt ihn weg): bitgleich
        var def2 = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        GasNebulaTests.RevertGas(def2);
        def2.Rules!.BanditFactions = false;
        var u2 = WorldBuilder.Build(def2);
        new SimulationRunner().Run(u2, 20_000);
        Assert.Equal(0x8EEEBEA81B038A74UL, UniverseHash.Compute(u2));
    }

    // ───────── 19: kleine Gruppen unverändert ─────────

    [Fact]
    public void NurDieKleinenGruppen_VerhaltenUnveraendert_SchalterAlleinTutNichts()
    {
        var u = World();
        var small = u.Actors.Values.Where(x => x.Kind == ActorKind.Outlaw && !Simulation.Bandits.IsEstablished(u, x.Id)).ToList();
        Assert.Equal(2, small.Count);
        foreach (var a in small)
        {
            Assert.Equal(u.Rules.MaxRaiderUnitsPerActor, Simulation.Bandits.RaiderUnitsCap(u, a));
            Assert.Equal(u.Rules.PirateTreasuryCap, Simulation.Bandits.FenceCap(u, a));
        }
        Assert.Equal(0, u.Sectors[Delta].Access.EntryLevel);   // kein Zoll im Korsaren-Sektor Delta
        // in einer Welt ohne Fraktion 4 ändert der Schalter allein nichts (Hash gleich an/aus)
        var defA = TestWorlds.Def(fuel: false, research: false);
        RevertBandits(defA);
        defA.Rules!.BanditFactions = true;
        var defB = TestWorlds.Def(fuel: false, research: false);
        RevertBandits(defB);
        var a = WorldBuilder.Build(defA, 1);
        var b = WorldBuilder.Build(defB, 1);
        new SimulationRunner().Run(a, 6000);
        new SimulationRunner().Run(b, 6000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
    }
}
