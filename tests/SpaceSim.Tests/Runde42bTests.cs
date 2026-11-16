using SpaceSim.Core.Comms;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Runde 42b (Handytest-Fixes): (1) Transport-Ablieferung bei JEDEM Andocken des Spielerschiffs an der Zielstation — View-Pfad
/// (Main setzt <see cref="Ship.DockedAt"/> direkt und veröffentlicht <see cref="ShipDocked"/>), <see cref="PlayerFleet.Dock"/> und
/// <see cref="Universe.FinishTransit"/> am Andockpunkt, plus Check-Fallback; (2) Liefervertrag-Reservierung greift in Anzeige
/// (<see cref="MarketInfo.Acceptance"/>) UND Ausführung (<see cref="TradeActions.SaleCaps"/>) — auch bei vollem Lager — und Verträge
/// entstehen nur für Waren, die die Station als Zutat/Regal annimmt; (3) Angebotsverteilung: alle Arten, Kopfgeld/Patrouille an
/// Polizei/HQ/Sektor-HQ, Eskorte an Handels-/Produktionsstationen mit abfliegenden Händlern, Andock-Station wird sofort erzeugt
/// (eigener Missions-RNG, Welt-RNG unberührt); (4) Zeiten als m:ss / „vor X min“. Hash-Neutralität: Angebote und Dock-Erzeugung
/// verbrauchen kein <see cref="Universe.Rng"/>.
/// </summary>
public class Runde42bTests
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Universe, SimulationRunner> Runners = new();

    static Universe World(ulong seed = 1)
    {
        var u = TestWorlds.Default(seed, fuel: false, research: false);
        Run(u, 1);   // Systeme hängen sich an den Bus (Comms, Missionen)
        return u;
    }

    static void Run(Universe u, int ticks) => Runners.GetValue(u, _ => new SimulationRunner()).Run(u, ticks);

    static Ship Player(Universe u) => u.Ships[u.PlayerShip];

    static MissionOffer? FindOffer(Universe u, Func<MissionOffer, bool> pred, int rounds = 250)
    {
        for (int i = 0; i < rounds; i++)
        {
            var o = u.MissionOffers.FirstOrDefault(pred);
            if (o is not null) return o;
            Run(u, 100);
        }
        return u.MissionOffers.FirstOrDefault(pred);
    }

    static List<SimEvent> Capture(Universe u)
    {
        var ev = new List<SimEvent>();
        u.Events.Raised += e => ev.Add(e);
        return ev;
    }

    static MissionOffer TransportOffer(Universe u) =>
        FindOffer(u, o => o.Type == TransportMission.KeyId && MissionRules.AcceptIssue(u, o.Id, Player(u), remote: true) is null)
        ?? throw new Xunit.Sdk.XunitException("kein annehmbares Transportangebot in der Welt gefunden");

    static MissionOffer ContractOffer(Universe u) =>
        FindOffer(u, o => o.Type == DeliveryMission.KeyId && o.Args.TryGetValue("contract", out var c) && c == "1")
        ?? throw new Xunit.Sdk.XunitException("kein Liefervertrag in der Welt gefunden");

    static Mission AcceptDock(Universe u, MissionOffer o)
    {
        var ship = Player(u);
        ship.DockedAt = o.At;
        ship.Position = u.Stations[o.At].DockPoint(ship.Id);
        var (m, issue) = MissionRules.Accept(u, o.Id, ship, remote: false);
        Assert.Null(issue);
        Assert.NotNull(m);
        return m!;
    }

    static (Universe U, MissionOffer O) WorldWithOffer(ulong startSeed, string type)
    {
        for (ulong s = startSeed; s < startSeed + 25; s++)
        {
            var u = World(s);
            var o = FindOffer(u, x => x.Type == type && MissionRules.AcceptIssue(u, x.Id, Player(u), remote: true) is null, rounds: 200);
            if (o is not null) return (u, o);
        }
        throw new Xunit.Sdk.XunitException($"kein annehmbares {type}-Angebot in den Seeds {startSeed}..{startSeed + 24} gefunden");
    }

    // ───────── 1. Transport: Ablieferung bei jedem Andocken ─────────

    [Fact]
    public void Transport_Erfuellt_ViewPfad_DockedAtDirektUndShipDocked()
    {
        var (u, o) = WorldWithOffer(11, TransportMission.KeyId);
        var m = AcceptDock(u, o);
        var ship = Player(u);
        Assert.True(ship.MissionCargo >= m.CargoVolume - 0.001f);
        float wallet0 = u.Player.Treasury.Credits;
        float reserved = m.Reserved;
        var target = u.Stations[TransportMission.TargetOf(o)];

        // falsche Station: Andocken dort erfüllt nicht
        var other = u.StationsById().First(s => s.Id != o.At && s.Id != target.Id && s.Dockable);
        ship.DockedAt = other.Id;
        u.Events.Publish(new ShipDocked(ship.Id, other.Id));
        Assert.NotEmpty(u.Missions);

        // View-Pfad (Main.Dock): DockedAt direkt setzen + ShipDocked veröffentlichen → erfüllt sofort
        ship.DockedAt = target.Id;
        var ev = Capture(u);
        u.Events.Publish(new ShipDocked(ship.Id, target.Id));
        Assert.Contains(ev, e => e is MissionCompleted);
        Assert.Empty(u.Missions);
        Assert.True(u.Player.Treasury.Credits >= wallet0 + reserved - 0.01f, $"Belohnung gezahlt ({wallet0:0} → {u.Player.Treasury.Credits:0}, reserviert {reserved:0})");
        Assert.True(ship.MissionCargo <= 0.001f, "versiegelte Fracht frei");
        Assert.Contains(u.Inbox, x => x.Kind == "mission_completed");
    }

    [Fact]
    public void Transport_Erfuellt_PlayerFleetDock_Pfad()
    {
        var (u, o) = WorldWithOffer(12, TransportMission.KeyId);
        var m = AcceptDock(u, o);
        var ship = Player(u);
        var target = u.Stations[TransportMission.TargetOf(o)];
        var ev = Capture(u);
        PlayerFleet.Dock(u, ship, target);   // Flotten-/Taxi-/Hangar-Pfad
        Assert.Contains(ev, e => e is ShipDocked sd && sd.Ship == ship.Id && sd.Station == target.Id);
        Assert.Empty(u.Missions);
        Assert.True(ship.MissionCargo <= 0.001f);
    }

    [Fact]
    public void Transport_Erfuellt_FinishTransit_Pfad_AnkunftAmAndockpunkt()
    {
        var (u, o) = WorldWithOffer(13, TransportMission.KeyId);
        var m = AcceptDock(u, o);
        var ship = Player(u);
        var target = u.Stations[TransportMission.TargetOf(o)];
        var dock = target.DockPoint(ship.Id);
        var t = new Transit { StartTick = u.Tick, ArrivalTick = u.Tick, ToSector = target.Sector, ToPosition = dock, Legs = { new Leg(target.Sector, ship.Position, dock, u.Tick, u.Tick) } };
        u.StartTransit(ship, t);
        var ev = Capture(u);
        u.FinishTransit(ship);   // NPC-/Flotten-Ankunft am Andockpunkt
        Assert.Contains(ev, e => e is ShipDocked sd && sd.Station == target.Id);
        Assert.Equal(target.Id, ship.DockedAt);
        Assert.Empty(u.Missions);
    }

    [Fact]
    public void Transport_CheckFallback_ErfuelltWennSchiffAnZielAngedocktLiegt()
    {
        var (u, o) = WorldWithOffer(14, TransportMission.KeyId);
        var m = AcceptDock(u, o);
        var ship = Player(u);
        var target = u.Stations[TransportMission.TargetOf(o)];
        // kein Ereignis (z. B. Spielstand direkt angedockt geladen): Check alle 20 Ticks liefert trotzdem ab
        ship.DockedAt = target.Id;
        Run(u, 20);
        Assert.Empty(u.Missions);
        Assert.True(ship.MissionCargo <= 0.001f);
    }

    [Fact]
    public void ShipDocked_WirdPubliziert_VonFinishTransitUndPlayerFleetDock()
    {
        var u = World(21);
        var ship = Player(u);
        var st = u.StationsById().First(s => s.Dockable);
        var ev = Capture(u);
        PlayerFleet.Dock(u, ship, st);
        Assert.Contains(ev, e => e is ShipDocked sd && sd.Ship == ship.Id && sd.Station == st.Id);
        ship.DockedAt = default;
        var dock = st.DockPoint(ship.Id);
        u.StartTransit(ship, new Transit { StartTick = u.Tick, ArrivalTick = u.Tick, ToSector = st.Sector, ToPosition = dock, Legs = { new Leg(st.Sector, ship.Position, dock, u.Tick, u.Tick) } });
        ev.Clear();
        u.FinishTransit(ship);
        Assert.Contains(ev, e => e is ShipArrived);
        Assert.Contains(ev, e => e is ShipDocked sd && sd.Station == st.Id);
    }

    // ───────── 2. Liefervertrag: Reservierung in Anzeige und Ausführung ─────────

    [Fact]
    public void Liefervertrag_Reservierung_GreiftInAcceptanceSaleCapsUndVerkauf_AuchBeiVollemLager()
    {
        var (u, co) = WorldWithOffer(31, DeliveryMission.KeyId);   // Welt mit delivery-Angeboten; Vertrag darunter suchen
        co = ContractOffer(u);
        var st = u.Stations[co.At];
        var good = DeliveryMission.GoodOf(co);
        float units = DeliveryMission.UnitsOf(u, co);
        var ship = Player(u);
        ship.DockedAt = co.At;
        var (m, issue) = MissionRules.Accept(u, co.Id, ship, remote: true);
        Assert.Null(issue);
        Assert.NotNull(m);
        Assert.True(m!.ReservedCash > 0f);
        float cashAfterReserve = st.Credits;

        // Lager voll: ohne Reservierung nimmt die Station nichts mehr an
        var e = st.Market[good];
        e.Amount = e.Target * (e.Overstock > 0f ? e.Overstock : u.Rules.OverstockFactor);
        Assert.True(st.AcceptableUnits(good, u.Rules) <= 0f, "Lager voll (Kontrolle)");

        // Anzeige = Ausführung: Acceptance und SaleCaps kennen die Reservierung (nur Spielerschiff)
        var intake = MarketInfo.Acceptance(u, st, good, ship);
        Assert.True(intake.Accepts, $"Anzeige nimmt trotz vollem Lager an (Grund: {intake.Reason})");
        Assert.NotEqual("Lager voll", intake.Reason);
        Assert.True(intake.Units >= units - 0.01f, $"reservierte Menge in der Anzeige ({intake.Units:0.#} ≥ {units:0.#})");
        var (cap, allCap) = TradeActions.SaleCaps(u, st, good, st.Credits, null, ship);
        Assert.True(cap >= units - 0.01f, $"Verkauf-Cap deckt die reservierte Menge ({cap:0.#} ≥ {units:0.#}, Kasse {st.Credits:0} + reserviert {m.ReservedCash:0})");
        Assert.True(allCap >= cap);
        // NPC-Schiffe sehen die Reservierung nicht (Anzeige für die KI unverändert)
        var npc = u.Ships.Values.First(s => !s.IsPlayer);
        Assert.False(MarketInfo.Acceptance(u, st, good, npc).Accepts);

        // Ausführung: der Verkauf der reservierten Menge gelingt und erfüllt den Auftrag
        ship.Cargo[good] = units + 5f;
        var sr = TradeActions.Sell(u, ship, st, good, units + 5f);
        Assert.True(sr.Sold.TryGetValue(good, out var sold) && sold >= units - 0.01f, $"verkauft {sold:0.#}/{units:0.#} trotz vollem Lager");
        Run(u, 20);
        Assert.Empty(u.Missions);
        Assert.Contains(u.Inbox, x => x.Kind == "mission_completed");
        // Kasse: Reservierung erst bei Erfüllung zurück an die Station — Netto zahlt sie nur den Kaufpreis
        Assert.True(st.Credits <= cashAfterReserve + m.ReservedCash + 0.01f, "Kassenfluss erklärbar (Reservierung zurück bei Erfüllung)");
    }

    [Fact]
    public void Liefervertrag_NurFuerWarenDieStationAlsZutatOderRegalAnnummt()
    {
        var u = World(41);
        // Station mit Produktion: alle Markteinträge voll, außer einem reinen Produkt ohne NoIntake-Flag (alter Code hätte dafür einen Vertrag erzeugt)
        var st = u.StationsById().First(s => !s.IsBuildSite && s.Market.Count > 0 && s.Production.Count > 0);
        var product = st.Production[0].Outputs.Keys.First();
        Assert.False(st.Consumes(product));
        Assert.False(st.IsStockOnly(product));
        foreach (var (g, e) in st.Market)
            if (g != product) e.Amount = MathF.Max(e.Amount, e.Target * (e.Overstock > 0f ? e.Overstock : u.Rules.OverstockFactor));
        var pe = st.Market[product];
        pe.Amount = 0f; pe.Target = 100f; pe.Price = MathF.Max(pe.Price, 5f); pe.NoIntake = false;   // knapp, aber reines Produkt
        Assert.True(st.AcceptableUnits(product, u.Rules) > 0f, "Kontrolle: der alte Check allein würde die Ware zulassen");
        var rng = MissionRng.For(u, st.Id.Value, 0);
        var o = new DeliveryMission().Generate(u, st, rng, 0);
        Assert.True(o is null || DeliveryMission.GoodOf(o) != product, "kein Vertrag für ein reines Produkt der Station");

        // alle Verträge eines Weltlaufs: Ware ist Zutat oder Regal
        var u2 = World(42);
        var seen = new List<MissionOffer>();
        for (int i = 0; i < 30; i++)
        {
            seen.AddRange(u2.MissionOffers.Where(x => x.Args.TryGetValue("contract", out var c) && c == "1"));
            Run(u2, 100);
        }
        Assert.NotEmpty(seen);
        foreach (var c in seen)
        {
            var s2 = u2.Stations[c.At];
            var g2 = DeliveryMission.GoodOf(c);
            Assert.True(s2.Consumes(g2) || s2.IsStockOnly(g2), $"Vertrag nur für Zutat/Regal ({u2.GoodOf(g2).Name} an {s2.Name})");
        }
    }

    // ───────── 3. Angebotsrate und -verteilung ─────────

    [Fact]
    public void Angebote_AlleFuenfArten_VerteilungJeStationstyp()
    {
        var byType = new SortedDictionary<string, int>();
        var escortAtTrade = false;
        var bountyAtPolice = false;
        for (ulong seed = 51; seed < 59; seed++)
        {
            var u = World(seed);
            // Bedrohung im Gebiet aller KI-Fraktionen hochhalten (verfällt pro Tick), damit Kopfgeld/Patrouille entstehen — wie der Selbsttest
            var facs = u.NpcFactionIds().ToList();
            for (int i = 0; i < 50; i++)
            {
                foreach (var sec in u.Sectors.Values.Where(x => facs.Contains(x.Owner))) sec.Threat = MathF.Max(sec.Threat, 30f);
                var raider = u.Ships.Values.FirstOrDefault(ThreatMap.IsRaider);
                if (raider is not null)
                    foreach (var fac in facs)
                        u.Actors[raider.Owner].Wanted[fac] = new WantedRecord { Heat = 4f, Fine = 800f, LastCrimeTick = u.Tick, LastCrime = "piracy" };
                foreach (var o in u.MissionOffers)
                {
                    byType[o.Type] = byType.GetValueOrDefault(o.Type) + 1;
                    var st = u.Stations[o.At];
                    switch (o.Type)
                    {
                        case BountyMission.KeyId:
                        case PatrolMission.KeyId:
                            Assert.True(BountyMission.IsOfferStation(u, st), $"{o.Type} nur an Polizei/HQ/Sektor-HQ ({st.TypeKey})");
                            if (st.TypeKey == "police_station") bountyAtPolice = true;   // Runde 42b: Polizei ist Angebots-Station
                            break;
                        case EscortMission.KeyId:
                            Assert.False(st.IsBuildSite);
                            Assert.True(st.Market.Count > 0, "Eskorte an Handels-/Produktionsstation (Markt)");
                            if (!BountyMission.IsOfferStation(u, st)) escortAtTrade = true;
                            break;
                    }
                }
                Run(u, 100);
            }
        }
        foreach (var t in new[] { DeliveryMission.KeyId, TransportMission.KeyId, BountyMission.KeyId, EscortMission.KeyId, PatrolMission.KeyId })
            Assert.True(byType.TryGetValue(t, out var n) && n > 0, $"Art {t} kam vor ({(byType.TryGetValue(t, out var x) ? x : 0)})");
        Assert.True(escortAtTrade, "Eskorte auch an Nicht-HQ-Handelsstationen");
        Assert.True(bountyAtPolice, "Kopfgeld auch an Polizeiwachen");
    }

    [Fact]
    public void IsOfferStation_PoliceHqUndSektorHq()
    {
        var u = World(61);
        Assert.Contains(u.StationsById(), s => s.TypeKey == "police_station" && BountyMission.IsOfferStation(u, s));
        Assert.Contains(u.StationsById(), s => s.TypeKey == "sector_hq" && BountyMission.IsOfferStation(u, s));
        Assert.Contains(u.StationsById(), s => s.TypeKey == "headquarters" && BountyMission.IsOfferStation(u, s));
        Assert.DoesNotContain(u.StationsById(), s => s.TypeKey == "trade_station" && BountyMission.IsOfferStation(u, s));
    }

    [Fact]
    public void Angebote_AndockStation_WirdSofortErzeugt_WeltRngUnberuehrt()
    {
        var a = World(71);
        var b = World(71);
        Run(a, 2450); Run(b, 2450);
        var st = b.StationsById().First(s => s.Dockable && b.MissionOffers.Any(o => o.At == s.Id));
        b.MissionOffers.RemoveAll(o => o.At == st.Id);   // Bucket lief schon — ohne Dock-Trigger kämen erst beim nächsten Bucket neue
        Player(b).DockedAt = st.Id;
        Run(a, 100); Run(b, 100);
        Assert.Contains(b.MissionOffers, o => o.At == st.Id);   // sofort beim Andocken erzeugt (gleicher Bucket-RNG)
        Assert.Equal(a.Rng.State, b.Rng.State);   // Hash-Neutralität: kein Welt-RNG verbraucht
    }

    [Fact]
    public void MissionOffered_FuerAlleArten_CommsPostNurFuerCommsKanal()
    {
        var u = World(81);
        var ev = Capture(u);
        Run(u, 2500);   // ein Bucket-Wechsel
        Assert.Contains(ev, e => e is MissionOffered mo && mo.Type == TransportMission.KeyId);   // Runde 42b: auch Dock-Arten feuern (Zählung)
        // angedockt an einer Station mit Dock-Angebot (Kanal „station“) → keine Posteingangs-Nachricht für genau dieses Angebot
        var st = u.StationsById().First(s => s.Dockable && u.MissionOffers.Any(o => o.At == s.Id && o.Args.TryGetValue("channel", out var c) && c == "station"));
        var stationChannelIds = u.MissionOffers.Where(o => o.At == st.Id && o.Args.TryGetValue("channel", out var c) && c == "station").Select(o => o.Id.ToString()).ToHashSet();
        Assert.NotEmpty(stationChannelIds);
        Player(u).DockedAt = st.Id;
        Run(u, 40);
        Assert.DoesNotContain(u.Inbox, m => m.Args.TryGetValue("offer", out var os) && stationChannelIds.Contains(os) && m.Kind is "mission_offer" or "contract_offer");
    }

    // ───────── 4. Zeiten ─────────

    [Fact]
    public void Rest_ZeitenAlsMss_StattRohTicks()
    {
        Assert.Equal("0:00", MissionRules.Rest(0));
        Assert.Equal("0:05", MissionRules.Rest(20));
        Assert.Equal("2:30", MissionRules.Rest(600));
        Assert.Equal("65:00", MissionRules.Rest(240 * 65));
        Assert.Equal("0:00", MissionRules.Rest(-100));
        var u = World(91);
        Assert.Equal("3 min", Comms.Ago(u, u.Tick - 240 * 3));
        Assert.Equal("<1 min", Comms.Ago(u, u.Tick - 10));
        Assert.Equal("50 min", Comms.In(u, u.Tick + 240 * 50));
        // Anzeigen der Aufträge nutzen die Restzeit, keine rohen Ticks
        var (u2, o) = WorldWithOffer(15, TransportMission.KeyId);
        var m = AcceptDock(u2, o);
        var text = MissionRules.Types.Get(TransportMission.KeyId)!.Describe(u2, m);
        Assert.DoesNotContain("Tick", text);
        Assert.Contains("noch ", text);
    }
}
