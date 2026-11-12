using System.Text.Json.Nodes;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 10: Gate-Zustände und Zugang (<see cref="GateAccess"/>, <see cref="IAccessCheck"/>), Mobilitätsprofil in der Routensuche, Sperrstufen mit
/// Richtung, Mindestdauer, Hysterese, Ablauf und Hauptroute (<see cref="FactionPolicySystem"/>), Beziehungen aus Daten und Ereignissen, Sektorkontrolle
/// (<see cref="SectorControl"/>), Regierungsstationen und die übrige Aufschlüsselung von <c>Essential</c>, Durchsetzung für laufende Reisen
/// (<see cref="AccessEnforcement"/>), Zerfall der Karte (<see cref="MapConnectivity"/>), Save v13 mit Migration.
/// </summary>
public class PoliticsTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    static readonly FactionId Union = new(1), Guild = new(2);
    static readonly SectorId Alpha = new(1), Beta = new(2), Gamma = new(3), Delta = new(4);

    /// <summary>
    /// Demotet einen aktuellen Save (v14, über <see cref="SecurityTests.ToV13Json"/>) auf v12: Regierungstypen und ihre Stationen, Gate-Zustand und Hauptroute, Sperren, Einfluss,
    /// Kontrollstufe, Unsterblichkeit und die Phase-10-Regeln entfallen.
    /// </summary>
    internal static string ToV12Json(string json)
    {
        json = SecurityTests.ToV13Json(json);   // Phase 11: erst Polizei und Piraten zurückbauen
        var node = JsonNode.Parse(json)!.AsObject();
        if ((int)node["Version"]! < 13) return json;
        node["Version"] = 12;
        var types = node["StationTypes"]!.AsArray();
        var gov = new HashSet<string>();
        foreach (var t in types.ToList())
        {
            var o = t!.AsObject();
            // Phase 13: Forschungsstationen (Typ mit Forschungsbetrieb) baut ResearchTests.ToV15Json nicht zurück — sie bleiben wie eigener Content stehen
            if (o["Research"] is not null) continue;
            if (o["Government"] is not null) { gov.Add((string)o["Key"]!); types.Remove(t); continue; }
            o.Remove("Influence"); o.Remove("Capturable"); o.Remove("Destructible");
        }
        var stations = node["Stations"]!.AsArray();
        var removed = new HashSet<int>();
        foreach (var s in stations.ToList())
        {
            if (gov.Contains((string)s!["Type"]!)) { removed.Add((int)s["Id"]!); stations.Remove(s); continue; }
            s.AsObject().Remove("Immortal");
        }
        foreach (var sec in node["Sectors"]!.AsArray())
        {
            var o = sec!.AsObject();
            var list = o["Stations"]!.AsArray();
            foreach (var x in list.ToList()) if (removed.Contains((int)x!)) list.Remove(x);
            o.Remove("Access"); o.Remove("Influence"); o.Remove("Control"); o.Remove("Contested");
        }
        foreach (var g in node["Gates"]!.AsArray())
        {
            var o = g!.AsObject();
            string? state = (string?)o["State"];
            o["Open"] = state is null or "Open" or "Restricted";
            o.Remove("State"); o.Remove("MainRoute");
        }
        var rules = node["Rules"]!.AsObject();
        foreach (var key in new[] { "AllowStationCapture", "MainRouteMaxLevel", "GateTollPerLevel", "AllyRelation", "HostileRelation", "LockThresholds",
                     "LockAggressionShift", "LockHysteresis", "LockMinTicks", "LockMaxTicks", "LockCooldownTicks", "LockUpkeepPerLevel", "StageMaxLevel",
                     "ContestedMaxLevel", "ContestedShare", "DominanceShare", "OwnerSwitchMargin", "GateSensorStrength", "Contraband", "RelationRestBase",
                     "RelationCooperationWeight", "RelationAggressionWeight", "RelationTradeWeight", "RelationDriftRate", "RelationStepTicks",
                     "ContestOpinionLoss", "LockOpinionLoss" })
            rules.Remove(key);
        return node.ToJsonString();
    }

    /// <summary>Startwelt; optional ohne Hauptrouten (dann wirken Stufen 4–5 an allen Gates) und mit einem zusätzlichen Gate Alpha–Gamma (Umweg).</summary>
    static Universe World(bool mainRoutes = true, bool shortcut = false, Action<EconomyRules>? rules = null)
    {
        var node = JsonNode.Parse(TestWorlds.ClassicJson())!.AsObject();
        var gates = node["gates"]!.AsArray();
        if (!mainRoutes) foreach (var g in gates) g!.AsObject()["mainRoute"] = false;
        if (shortcut)
            gates.Add(JsonNode.Parse("""{ "a": { "id": 7, "sector": 1, "pos": [-140, -140] }, "b": { "id": 8, "sector": 3, "pos": [140, 140] } }"""));
        var u = WorldBuilder.Build(WorldBuilder.Parse(node.ToJsonString()));
        rules?.Invoke(u.Rules);
        return u;
    }

    static Ship AddShip(Universe u, ActorId owner, SectorId sector, ShipRole role = ShipRole.Trader)
    {
        var ship = WorldBuilder.NewShip(u, u.NewShipId(), role, owner, sector, 5000f, 100f);
        u.AddShip(ship);
        return ship;
    }

    static Gate GateFrom(Universe u, SectorId from, SectorId to) =>
        u.Gates.Values.Where(g => g.Sector == from && u.Gates[g.Target].Sector == to).OrderBy(g => g.Id.Value).First();

    static void Lock(Universe u, SectorId sector, int entry, int exit = 0)
    {
        var acc = u.Sectors[sector].Access;
        acc.EntryLevel = entry; acc.ExitLevel = exit; acc.Enforced = true; acc.SinceTick = u.Tick; acc.ExpiresTick = u.Tick + 100_000;
        GateAccess.SyncRestricted(u);
        u.TouchAccess();
    }

    // ───────── Startwelt, Content, Essential ─────────

    [Fact]
    public void Startwelt_Regierungsstationen_Kontrollstufen_OffeneHauptrouten()
    {
        var u = TestWorlds.Classic();
        // Phase 13: die Forschungsstationen (37, 38) sind ebenfalls offiziell (Unterhalt aus der Kasse), haben aber einen Markt
        var gov = u.Stations.Values.Where(s => StationProtection.IsOfficial(u, s) && StationProtection.KindOf(u, s) != GovernmentKind.Research).OrderBy(s => s.Id.Value).ToList();
        Assert.Equal(new[] { 32, 33, 34, 35 }, gov.Select(s => s.Id.Value));
        Assert.Equal(GovernmentKind.Headquarters, StationProtection.KindOf(u, u.Stations[new StationId(32)]));
        Assert.Equal(GovernmentKind.SectorHq, StationProtection.KindOf(u, u.Stations[new StationId(33)]));
        Assert.All(gov, s => { Assert.True(s.BankruptcyProtected); Assert.Empty(s.Market); Assert.Equal(u.Sectors[s.Sector].Owner.Value, s.Owner.Value); });
        foreach (var t in new[] { "headquarters", "company_seat", "sector_hq", "office", "police_station" }) Assert.True(u.StationTypes.ContainsKey(t));
        Assert.Equal(GovernmentKind.Police, u.StationTypes["police_station"].Government);
        // Regierungsstationen zählen nicht gegen die Obergrenzen; die Startwelt hat dieselbe Bau-Reserve wie vorher
        Assert.Equal(u.Stations.Count - 4 - 1, StationProtection.CountEconomic(u));   // Phase 11: das Piratenversteck zählt ebenfalls nicht; Phase 14: die Forschungsstationen fehlen in der Standardwelt (Forschung aus)
        // Sektorkontrolle: Hauptquartier = Behörde + Polizeizentrale → Kontrolle; Sektor-HQ ohne Wache → Dominanz
        Assert.Equal(ControlStage.Control, u.Sectors[Alpha].Control);
        Assert.Equal(ControlStage.Dominance, u.Sectors[Beta].Control);
        Assert.Equal(ControlStage.Control, u.Sectors[Gamma].Control);
        Assert.Equal(ControlStage.Dominance, u.Sectors[Delta].Control);
        Assert.All(u.Sectors.Values, s => { Assert.False(s.Contested); Assert.Equal(1f, s.Influence[s.Owner], 3); Assert.True(s.Access.IsOpen); });
        Assert.All(u.Gates.Values, g => { Assert.Equal(GateState.Open, g.State); Assert.True(g.MainRoute); });
        Assert.False(u.AccessRestricted);
        Assert.False(MapConnectivity.Physical(u).IsSplit);
        Assert.False(MapConnectivity.Economy(u).IsSplit);
    }

    [Fact]
    public void Content_UngueltigeRegierungsformEinflussUndGateZustand_Werfen()
    {
        string Mutate(Action<JsonObject> f)
        {
            var node = JsonNode.Parse(TestWorlds.ClassicJson())!.AsObject();
            f(node);
            return node.ToJsonString();
        }
        var bad1 = Mutate(n => n["stationTypes"]![0]!.AsObject()["government"] = "kaiserpalast");
        var ex1 = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(WorldBuilder.Parse(bad1)));
        Assert.Contains("Regierungsform", ex1.Message);
        var bad2 = Mutate(n => n["stationTypes"]![0]!.AsObject()["influence"] = -1);
        Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(WorldBuilder.Parse(bad2)));
        var bad3 = Mutate(n => n["gates"]![0]!.AsObject()["state"] = "halboffen");
        Assert.Contains("Zustand", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(WorldBuilder.Parse(bad3))).Message);
        var closed = WorldBuilder.Build(WorldBuilder.Parse(Mutate(n => n["gates"]![0]!.AsObject()["state"] = "closed")));
        Assert.Equal(GateState.Closed, closed.Gates[new GateId(1)].State);
        Assert.Equal(GateState.Closed, closed.Gates[new GateId(2)].State);
        Assert.Null(closed.FindRoute(Alpha, Beta));
    }

    [Fact]
    public void Essential_Aufgeschluesselt_GovernmentCapturableDestructibleImmortal()
    {
        var node = JsonNode.Parse(TestWorlds.ClassicJson())!.AsObject();
        node["sectors"]![0]!["stations"]![0]!.AsObject()["immortal"] = true;   // Stahlwerk Alpha
        var u = WorldBuilder.Build(WorldBuilder.Parse(node.ToJsonString()));
        var hq = u.Stations[new StationId(32)];
        var steel = u.Stations[new StationId(1)];
        var police = StationFactoryPolice(u);
        Assert.True(StationProtection.IsGovernment(u, hq));
        Assert.False(StationProtection.IsGovernment(u, police));   // Wache ist keine Regierung (eigene Rolle ab Phase 11)
        Assert.True(StationProtection.IsOfficial(u, police));
        Assert.False(StationProtection.IsGovernment(u, steel));
        // Eroberung ist abgeschaltet (Regel), Regierungsstationen nie, unsterbliche nie
        Assert.False(StationProtection.IsCapturable(u, u.Stations[new StationId(2)]));
        u.Rules.AllowStationCapture = true;
        Assert.True(StationProtection.IsCapturable(u, u.Stations[new StationId(2)]));
        Assert.False(StationProtection.IsCapturable(u, hq));
        Assert.False(StationProtection.IsCapturable(u, steel));
        Assert.True(steel.Immortal);
        Assert.False(StationProtection.IsDestructible(u, steel));
        Assert.True(StationProtection.IsDestructible(u, hq));
        Assert.False(u.StationTypes["headquarters"].Capturable);
    }

    static Station StationFactoryPolice(Universe u)
    {
        var st = StationFactory.Create(u, u.NewStationId(), u.StationTypes["police_station"], "Wache", Gamma, new Vec2(-140, 120), Guild);
        u.AddStation(st);
        return st;
    }

    // ───────── Kontrollpunkte ─────────

    [Fact]
    public void Kontrollpunkt_StufenWirkenJeFraktion_RichtungUndEmbargo()
    {
        var u = World(mainRoutes: false);
        Relations.Set(u, Guild, Union, -0.6f);   // die Gilde (Gamma) sieht die Union feindlich
        var into = GateFrom(u, Beta, Gamma);
        var outOf = GateFrom(u, Gamma, Beta);
        var free = MobilityProfile.For(u.FreeActor);
        var unionEmpty = MobilityProfile.For(Union);
        var unionLaden = MobilityProfile.For(Union, laden: true);
        var guild = MobilityProfile.For(Guild, laden: true);

        Lock(u, Gamma, 1);
        Assert.Equal(AccessPolicy.Toll, GateAccess.Evaluate(u, into, free).Policy);
        Assert.Equal(u.Rules.GateTollPerLevel, GateAccess.Evaluate(u, into, free).Toll);
        Assert.Equal(Guild, GateAccess.Evaluate(u, into, free).TollTo);
        Assert.Equal(AccessPolicy.Allow, GateAccess.Evaluate(u, into, guild).Policy);         // Mitglieder immer
        Assert.Equal(AccessPolicy.Allow, GateAccess.Evaluate(u, outOf, free).Policy);         // Ausreise frei
        Lock(u, Gamma, 2);
        Assert.Equal(AccessPolicy.Toll, GateAccess.Evaluate(u, into, unionEmpty).Policy);    // leer: Zoll
        var embargo = GateAccess.Evaluate(u, into, unionLaden);
        Assert.Equal(AccessPolicy.Deny, embargo.Policy);                                     // beladen: Embargo gegen Gegner
        Assert.Contains("Embargo", embargo.Reason);
        Lock(u, Gamma, 3);
        Assert.Equal(AccessPolicy.Deny, GateAccess.Evaluate(u, into, unionEmpty).Policy);    // nur Verbündete und Neutrale
        Assert.Equal(AccessPolicy.Toll, GateAccess.Evaluate(u, into, free).Policy);           // Freie sind neutral
        Lock(u, Gamma, 4);
        Assert.Equal(AccessPolicy.Deny, GateAccess.Evaluate(u, into, free).Policy);           // niemand rein …
        Assert.Equal(AccessPolicy.Allow, GateAccess.Evaluate(u, outOf, free).Policy);         // … wer drin ist, darf raus
        Assert.Equal(AccessPolicy.Allow, GateAccess.Evaluate(u, into, guild).Policy);
        Lock(u, Gamma, 5, 5);
        Assert.Equal(AccessPolicy.Deny, GateAccess.Evaluate(u, outOf, free).Policy);          // Abriegelung: auch nicht raus
        Assert.Equal(AccessPolicy.Allow, GateAccess.Evaluate(u, outOf, guild).Policy);
        // Verbündete passieren Stufe 1–3 ohne Zoll
        Lock(u, Gamma, 3);
        Relations.Set(u, Guild, Union, 0.6f);
        Assert.Equal(AccessPolicy.Allow, GateAccess.Evaluate(u, into, unionLaden).Policy);
        // Physisch geschlossen schlägt alles
        GateAccess.SetState(u, into, GateState.Closed);
        Assert.Equal(GateState.Closed, u.Gates[into.Target].State);
        Assert.Equal(AccessPolicy.Deny, GateAccess.Evaluate(u, into, guild).Policy);
        Assert.Equal(AccessPolicy.Deny, GateAccess.Evaluate(u, into, MobilityProfile.Physical).Policy);
    }

    [Fact]
    public void Hauptroute_DeckeltSperre_PapiersperreWirktNicht_EntdeckungUndSchmuggel()
    {
        var u = World();   // Startwelt: alle Gates sind Hauptroute
        var into = GateFrom(u, Beta, Gamma);
        Lock(u, Gamma, 5, 5);
        var free = MobilityProfile.For(u.FreeActor);
        Assert.Equal(3, GateAccess.EffectiveLevel(u, u.Gates[into.Target], u.Sectors[Gamma], AccessDirection.Entry));
        Assert.Equal(AccessPolicy.Toll, GateAccess.Evaluate(u, into, free).Policy);   // Hauptroute: höchstens Stufe 3, Neutrale zahlen nur
        Assert.Equal(AccessPolicy.Allow, GateAccess.Evaluate(u, GateFrom(u, Gamma, Beta), free).Policy);
        Assert.Equal(GateState.Restricted, u.Gates[into.Target].State);
        // Ohne bezahlte Durchsetzung gilt die Sperre nur auf dem Papier
        u.Sectors[Gamma].Access.Enforced = false;
        u.TouchAccess();
        Assert.Equal(AccessPolicy.Allow, GateAccess.Evaluate(u, into, free).Policy);
        u.Sectors[Gamma].Access.Enforced = true;
        u.TouchAccess();
        // Entdeckung: Tarnstärke über der Sensorstärke passiert unbemerkt (ohne Zoll)
        var stealthy = MobilityProfile.For(Union, laden: true, stealth: u.Rules.GateSensorStrength + 1f);
        Relations.Set(u, Guild, Union, -0.9f);
        Assert.Equal(AccessPolicy.Deny, GateAccess.Evaluate(u, into, MobilityProfile.For(Union, laden: true)).Policy);
        var hidden = GateAccess.Evaluate(u, into, stealthy);
        Assert.Equal(AccessPolicy.Allow, hidden.Policy);
        Assert.Equal(0f, hidden.Toll);
        Assert.Contains("unentdeckt", hidden.Reason);
        // Schmuggelware (Regel) wird an der echten Fracht erkannt
        u.Rules.Contraband.Add("luxury_goods");
        var smuggler = AddShip(u, u.FreeActor, Beta);
        smuggler.Cargo[u.FindGood("luxury_goods")!.Id] = 5f;
        var pass = GateAccess.Evaluate(u, into, GateAccess.ProfileOf(u, smuggler), smuggler);
        Assert.Equal(AccessPolicy.Deny, pass.Policy);
        Assert.Contains("Schmuggel", pass.Reason);
        // Prüfungen sind eine Registry (Reihenfolge Politik, Ware, Entdeckung), doppelte Schlüssel werfen
        Assert.Equal(new[] { "policy", "cargo", "detection" }, AccessChecks.Default().All.Select(c => c.Key));
        Assert.Throws<InvalidOperationException>(() => AccessChecks.Default().Register(new PolicyCheck()));
    }

    // ───────── Routensuche mit Mobilitätsprofil ─────────

    [Fact]
    public void FindRoute_MitProfil_UmgehtSperre_CacheNachProfilGetrennt()
    {
        var u = World(mainRoutes: false, shortcut: true);
        Assert.Single(u.FindRoute(Alpha, Gamma)!);    // Abkürzung Alpha–Gamma
        Assert.Equal(2, u.FindRoute(Alpha, Delta)!.Count);
        // Beta für Fremde gesperrt (Stufe 4): Freie fahren nach Delta nicht mehr, nach Gamma über die Abkürzung; die Union selbst fährt durch
        Lock(u, Beta, 4);
        var free = MobilityProfile.For(u.FreeActor);
        Assert.Null(u.CachedRoute(Alpha, Delta, free));
        Assert.Equal(2, u.CachedRoute(Alpha, Delta, MobilityProfile.For(Union))!.Count);
        Assert.Equal(2, u.CachedRoute(Alpha, Delta)!.Count);   // physisch unverändert
        Assert.Null(u.CachedRoute(Gamma, Beta, free));
        Assert.Single(u.CachedRoute(Alpha, Gamma, free)!);
        Assert.False(u.IsReachable(Gamma, Delta, free));
        Assert.True(u.IsReachable(Gamma, Delta, MobilityProfile.For(Union)));
        // Gesperrtes Gate auf direktem Weg: der Umweg wird geplant (Gamma → Alpha über die Abkürzung, nicht über Beta)
        Lock(u, Beta, 0);
        var direct = u.FindRoute(Gamma, Alpha, free)!;
        Lock(u, Alpha, 0);
        GateAccess.SetState(u, GateFrom(u, Alpha, Gamma), GateState.Closed);
        var around = u.FindRoute(Gamma, Alpha, free)!;
        Assert.Single(direct);
        Assert.Equal(2, around.Count);
        Assert.Equal(Beta, u.Gates[u.Gates[around[0]].Target].Sector);
        // Ohne jede Einschränkung ist jedes Profil physisch (gemeinsamer Cache)
        var plain = World();
        Assert.False(plain.AccessRestricted);
        Assert.Same(plain.CachedRoute(Alpha, Gamma, MobilityProfile.For(Union)), plain.CachedRoute(Alpha, Gamma, MobilityProfile.For(Guild)));
    }

    [Fact]
    public void TravelStart_Unerreichbar_KeinTeleport_AuftragFreiFrachtBleibt()
    {
        var u = World(mainRoutes: false);
        var ship = AddShip(u, u.FreeActor, Alpha);
        var steel = u.FindGood("steel")!.Id;
        ship.Cargo[steel] = 10f;
        ship.Order = new TradeOrder(new StationId(1), new StationId(14), steel);
        ship.Phase = TradePhase.GoingToSell;
        Lock(u, Delta, 4);
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        Assert.False(Travel.Start(u, ship, Delta, u.Stations[new StationId(14)].DockPoint(ship.Id)));
        Assert.Null(ship.Transit);
        Assert.Equal(Alpha, ship.Sector);
        Assert.Null(ship.Order);
        Assert.Equal(TradePhase.Idle, ship.Phase);
        Assert.Equal(10f, ship.Cargo[steel]);
        Assert.Single(events.OfType<RouteBlocked>());
    }

    [Fact]
    public void Zoll_BeimReiseantritt_FliesstInDieKasseDerSektorfraktion()
    {
        var u = World();
        var ship = AddShip(u, u.FreeActor, Alpha);
        Lock(u, Gamma, 2);
        float before = ship.Credits, treasury = u.Actors[Guild].Treasury.Credits;
        var events = new List<TollPaid>();
        u.Events.Raised += e => { if (e is TollPaid t) events.Add(t); };
        Assert.True(Travel.Start(u, ship, Gamma, u.Stations[new StationId(9)].DockPoint(ship.Id)));
        float toll = 2 * u.Rules.GateTollPerLevel;
        Assert.Equal(before - toll, ship.Credits, 3);
        Assert.Equal(treasury + toll, u.Actors[Guild].Treasury.Credits, 3);
        Assert.Single(events);
        Assert.Equal(Guild, events[0].To);
    }

    [Fact]
    public void Navigator_SpielerRouteRespektiertSeinProfil()
    {
        var u = World(mainRoutes: false);
        var player = u.Ships[u.PlayerShip];   // Mitglied der Union
        Relations.Set(u, Guild, Union, -0.9f);
        Lock(u, Gamma, 3);
        var target = Navigator.ForSector(u, Gamma, player.Id);
        Assert.Equal(NavStatus.Unreachable, Navigator.Next(u, Alpha, new Vec2(0, 0), target, player).Status);
        Assert.Equal(NavStatus.EnRoute, Navigator.Next(u, Alpha, new Vec2(0, 0), target).Status);   // ohne Profil nur physisch
        var pass = GateAccess.Evaluate(u, GateFrom(u, Beta, Gamma), GateAccess.ProfileOf(u, player), player);
        Assert.False(pass.Allowed);
        Assert.Contains("Verbündete", pass.Reason);
    }

    [Fact]
    public void Spielersprung_AbgewiesenMitGrund_ZollNurWennBezahlbar_AnzeigeEcht()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var into = GateFrom(u, Beta, Gamma);
        Relations.Set(u, Guild, Union, -0.9f);
        Lock(u, Gamma, 3);
        var denied = GateAccess.TryPlayerJump(u, player, into);
        Assert.False(denied.Allowed);
        Assert.StartsWith("Sprung verweigert", denied.Message);
        Assert.Equal("Sektor Gamma: nur Verbündete und Neutrale (Beziehung feindlich)", GateAccess.BlockReason(u, player, Alpha, Gamma));
        // Anzeige (rein): Sektor, Gate, Station
        var sp = AccessInfo.ForSector(u, Gamma, player);
        Assert.True(sp.PlayerDenied);
        Assert.Equal(3, sp.EntryLevel);
        Assert.StartsWith("Stufe 3 · Nur Verbündete", sp.Lock);
        Assert.Equal("Kontrolle", sp.Stage);
        Assert.Equal("Freie Händler 100 %", sp.Influence);
        Assert.True(AccessInfo.ForGate(u, into, player).Denied);
        Assert.Equal("eingeschränkt", AccessInfo.ForGate(u, u.Gates[into.Target], player).State);
        var stp = AccessInfo.ForStation(u, u.Stations[new StationId(10)], player);
        Assert.StartsWith("feindlich", stp.Reputation);
        Assert.StartsWith("gesperrt", stp.Customs);
        Assert.Contains(stp.Notes, n => n.Contains("Einreise für dich gesperrt"));
        Assert.Contains(AccessInfo.ForStation(u, u.Stations[new StationId(34)], player).Notes, n => n.StartsWith("Hauptquartier"));
        // Neutral auf Stufe 1: Zoll. Phase 11 (absichtlich geändert): Reicht die Kasse nicht, springt der Spieler trotzdem — Zollflucht, gezahlt wird,
        // was da ist, der Rest wird Teil der Strafe (Fahndung bei der Gilde); mit abgeschalteter Sicherheit gilt die alte Abweisung
        Relations.Set(u, Guild, Union, 0f);
        Lock(u, Gamma, 1);
        Assert.Equal("Zoll 8 Cr je Einreise", AccessInfo.ForSector(u, Gamma, player).ForPlayer);
        u.Rules.SecurityActive = false;
        u.PlayerWallet.Credits = 5f;
        Assert.Contains("zu wenig Credits", GateAccess.TryPlayerJump(u, player, into).Message);
        Assert.Equal(5f, u.PlayerWallet.Credits);
        u.Rules.SecurityActive = true;
        var evaded = GateAccess.TryPlayerJump(u, player, into);
        Assert.True(evaded.Allowed);
        Assert.StartsWith("Zollflucht", evaded.Message);
        Assert.Equal(0f, u.PlayerWallet.Credits, 3);
        Assert.True(u.Player.Wanted.ContainsKey(Guild));
        Assert.Equal("toll_evasion", u.Player.Wanted[Guild].LastCrime);
        u.Player.Wanted.Clear();
        u.PlayerWallet.Credits = 100f;
        float treasury = u.Actors[Guild].Treasury.Credits;
        var ok = GateAccess.TryPlayerJump(u, player, into);
        Assert.True(ok.Allowed);
        Assert.Equal(8f, ok.Toll);
        Assert.Equal(92f, u.PlayerWallet.Credits, 3);
        Assert.Equal(treasury + 8f, u.Actors[Guild].Treasury.Credits, 3);
        Assert.Equal("Zoll 8 Cr an Freie Händler", ok.Message);
        // offen: frei, ohne Meldung
        Lock(u, Gamma, 0);
        Assert.Equal(new PlayerJump(true, 0f, ""), GateAccess.TryPlayerJump(u, player, into));
        Assert.Equal("frei", AccessInfo.ForSector(u, Gamma, player).ForPlayer);
    }

    // ───────── Sperrstufen der Fraktionspolitik ─────────

    [Fact]
    public void Politik_FeindlicheBeziehung_Sperre_MitDeckelMindestdauerHystereseAblauf()
    {
        var u = World(rules: r => { r.RelationDriftRate = 0f; r.LockMinTicks = 400; r.LockMaxTicks = 2000; r.LockCooldownTicks = 600; });
        var runner = new SimulationRunner();
        var changes = new List<SectorAccessChanged>();
        u.Events.Raised += e => { if (e is SectorAccessChanged c) changes.Add(c); };
        Relations.Set(u, Guild, Union, -0.9f);
        runner.Run(u, 21);
        // Gamma (Kontrolle): Stufe 5 mit Ausreisesperre; Delta (nur Dominanz): höchstens Stufe 2; Union-Sektoren unverändert
        Assert.Equal(5, u.Sectors[Gamma].Access.EntryLevel);
        Assert.Equal(5, u.Sectors[Gamma].Access.ExitLevel);
        Assert.Equal(Union, u.Sectors[Gamma].Access.Against);
        Assert.Equal(2, u.Sectors[Delta].Access.EntryLevel);
        Assert.Equal(0, u.Sectors[Delta].Access.ExitLevel);
        Assert.True(u.Sectors[Alpha].Access.IsOpen);
        Assert.Contains(changes, c => c.Sector == Gamma && c.ToEntry == 5 && c.Reason == "relation");
        Assert.Equal(GateState.Restricted, GateFrom(u, Gamma, Beta).State);
        // Der Spieler (Union) kommt nicht mehr nach Gamma (Hauptroute: Stufe 3 gilt, Gegner bleiben draußen); freie Händler zahlen Zoll
        Assert.False(u.IsReachable(Beta, Gamma, GateAccess.ProfileOf(u, u.Ships[u.PlayerShip])));
        Assert.True(u.IsReachable(Beta, Gamma, MobilityProfile.For(u.FreeActor, laden: true)));

        // Mindestdauer: Beziehung wird gut, die Sperre bleibt bis LockMinTicks
        long since = u.Sectors[Gamma].Access.SinceTick;
        Relations.Set(u, Guild, Union, 0.25f);
        runner.Run(u, 200);
        Assert.Equal(5, u.Sectors[Gamma].Access.EntryLevel);
        runner.Run(u, (int)(since + 400 - u.Tick) + 21);
        Assert.True(u.Sectors[Gamma].Access.IsOpen);
        Assert.True(u.Sectors[Delta].Access.IsOpen);

        // Hysterese (rein): Stufe 2 bleibt bei −0,30 (Schwelle −0,34 + 0,1), sinkt bei −0,20 auf 1
        var gamma = u.Sectors[Gamma];
        gamma.Access.EntryLevel = 2;
        Relations.Set(u, Guild, Union, -0.30f);
        Assert.Equal(2, FactionPolicySystem.Desired(u, gamma).Level);
        Relations.Set(u, Guild, Union, -0.20f);
        Assert.Equal(1, FactionPolicySystem.Desired(u, gamma).Level);
        gamma.Access.EntryLevel = 0;
        Assert.Equal(1, FactionPolicySystem.Desired(u, gamma).Level);
        Assert.Equal(0, FactionPolicySystem.LevelFor(u.Rules, -0.14f, 0.5f));
        Assert.Equal(1, FactionPolicySystem.LevelFor(u.Rules, -0.14f, 1f));   // aggressive Fraktionen sperren früher

        // Ablauf und Abkühlzeit: eine Sperre fällt nach LockMaxTicks, danach entsteht für LockCooldownTicks keine neue
        Relations.Set(u, Guild, Union, -0.9f);
        runner.Run(u, 40);
        Assert.Equal(5, gamma.Access.EntryLevel);
        long expires = gamma.Access.ExpiresTick;
        runner.Run(u, (int)(expires - u.Tick) + 21);
        Assert.True(gamma.Access.IsOpen);
        Assert.Contains(changes, c => c.Sector == Gamma && c.Reason == "expired");
        runner.Run(u, 400);
        Assert.True(gamma.Access.IsOpen);   // Abkühlzeit
        runner.Run(u, 260);
        Assert.Equal(5, gamma.Access.EntryLevel);

        // Ohne Geld für die Durchsetzung: Papier — das Gate ist wieder offen, die Union kommt durch
        u.Rules.LockUpkeepPerLevel = 1e9f;
        runner.Run(u, 21);
        Assert.False(gamma.Access.Enforced);
        Assert.Equal(GateState.Open, GateFrom(u, Gamma, Beta).State);
        Assert.True(u.IsReachable(Beta, Gamma, MobilityProfile.For(Union, laden: true)));
    }

    [Fact]
    public void Politik_UmkaempfteSektoren_SenkenBeziehung_SperreEntstehtAusDerMechanik()
    {
        var u = World();
        // Die Gilde übernimmt je vier Stationen in Alpha und Beta (über 30 % Einfluss): beide Sektoren sind umkämpft
        foreach (var id in new[] { 1, 2, 3, 4, 5, 6, 7, 8 }) u.Stations[new StationId(id)].Owner = Guild;
        var changes = new List<SectorAccessChanged>();
        u.Events.Raised += e => { if (e is SectorAccessChanged c) changes.Add(c); };
        new SimulationRunner().Run(u, 51);
        Assert.True(u.Sectors[Alpha].Contested);
        Assert.True(u.Sectors[Beta].Contested);
        Assert.Equal(Union, u.Sectors[Alpha].Owner);   // 69 % bleiben bei der Union
        Assert.Equal(2, FactionPolicySystem.ContestedBetween(u, Union, Guild));
        Assert.Equal(1, SectorControl.MaxLevel(u, u.Sectors[Alpha]));   // umkämpft: höchstens Stufe 1
        new SimulationRunner().Run(u, 6000);
        float guildView = Relations.Effective(u, Guild, Union);
        Assert.True(guildView < -0.19f, $"Gilde→Union {guildView}");
        // Gamma (Gildenhauptquartier, Kontrolle) sperrt gegen die Union; Union-Sektoren höchstens Stufe 1 (umkämpft)
        Assert.True(u.Sectors[Gamma].Access.EntryLevel >= 1);
        Assert.Contains(changes, c => c.Sector == Gamma && c.Reason == "relation" && c.Against == Union);
        Assert.All(new[] { Alpha, Beta }, s => Assert.True(!u.Sectors[s].Contested || u.Sectors[s].Access.EntryLevel <= 1));
        Assert.Equal(2, Relations.StoredEntries(u));   // nur die beiden Hauptbeziehungen
    }

    [Fact]
    public void Startwelt_20000Ticks_KeineSperre_BeziehungFreundlich_KarteZusammen_KassenErklaerbar()
    {
        var u = TestWorlds.Classic();
        int changes = 0;
        double gov = 0, unpaid = 0;
        u.Events.Raised += e =>
        {
            if (e is SectorAccessChanged) changes++;
            else if (e is TreasuryUpkeep { Purpose: "government" } t) { gov += t.Amount; unpaid += t.Unpaid; }
        };
        new SimulationRunner().Run(u, 20_000);
        Assert.Equal(0, changes);
        Assert.All(u.Sectors.Values, s => Assert.True(s.Access.IsOpen));
        Assert.True(Relations.Effective(u, Union, Guild) >= 0.15f);
        Assert.True(Relations.Effective(u, Guild, Union) >= 0.15f);
        Assert.False(MapConnectivity.Economy(u).IsSplit);
        Assert.Equal(new[] { Union, Union, Guild, Guild }, u.Sectors.Values.OrderBy(s => s.Id.Value).Select(s => s.Owner));
        // Regierungsunterhalt ist eine echte Senke der Kassen (6 + 3 Cr/Tick je Fraktion; Phase 13: + 1 Cr/Tick je Forschungsstation — Phase 14: in der
        // Standardwelt ohne Forschungsstationen), fast vollständig gedeckt
        Assert.InRange(gov, 18.0 * 20_000 * 0.95, 18.0 * 20_000 * 1.01);
        Assert.True(unpaid < 0.01 * gov);
        Assert.True(ShipJobs.UsedUnits(u, ShipJobs.ByKey("trade")!) >= 12f);
        Assert.True(ShipJobs.UsedUnits(u, ShipJobs.ByKey("mine")!) >= 7f);
    }

    // ───────── Durchsetzung für laufende Reisen ─────────

    [Fact]
    public void Durchsetzung_Abriegelung_ReisenUmgeplantOderAngehalten_KeinGeisterauftrag()
    {
        var u = World(mainRoutes: false, rules: r => r.LockMinTicks = 100_000);   // die Politik hebt die Hand-Sperre im Test nicht auf
        var runner = new SimulationRunner();
        runner.Run(u, 400);
        int crossing = u.Ships.Values.Count(s => s.Transit is { } t && t.Legs.Any(l => l.Sector == Gamma) && t.Legs[0].Sector != Gamma);
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        Assert.Equal(5, FactionPolicySystem.SetLevel(u, u.Sectors[Gamma], 5));
        var free = MobilityProfile.For(u.FreeActor);
        Assert.False(u.IsReachable(Beta, Gamma, free));
        Assert.True(MapConnectivity.Economy(u).IsSplit);
        Assert.Contains(Gamma, MapConnectivity.Economy(u).Isolated);
        Assert.False(MapConnectivity.Physical(u).IsSplit);
        if (crossing > 0) Assert.True(events.OfType<ShipRerouted>().Count() + events.OfType<RouteBlocked>().Count() >= 1);

        for (int i = 0; i < 30; i++)
        {
            runner.Run(u, 100);
            foreach (var s in u.Ships.Values.Where(x => x.Controller == Controller.Ai))
            {
                // Keine Reise über ein Gate, das das Schiff abweist
                if (s.Transit is { } t)
                    for (int k = 0; k + 1 < t.Legs.Count; k++)
                    {
                        var a = t.Legs[k]; var b = t.Legs[k + 1];
                        if (a.Sector == b.Sector || u.Tick >= a.EndTick) continue;
                        var gate = AccessEnforcement.GateBetween(u, a.Sector, b.Sector, a.To)!;
                        Assert.True(GateAccess.Evaluate(u, gate, GateAccess.ProfileOf(u, s), s).Allowed, $"Schiff {s.Id.Value} plant über gesperrtes Gate {gate.Id.Value}");
                    }
                // Kein Geisterauftrag: Händler mit Auftrag reisen; Miner mit Auftrag reisen oder schürfen
                if (s.Role == ShipRole.Trader && s.Order is not null) Assert.NotNull(s.Transit);
                if (s.Role == ShipRole.Miner && s.Order is not null) Assert.True(s.Transit is not null || s.MinePhase == MinePhase.Mining, $"Miner {s.Id.Value} {s.MinePhase}");
                // Wer außerhalb von Gamma ist, fliegt nicht hinein; wer in Gamma liegt, bleibt dort
                if (s.Transit is { } tt && tt.ToSector == Gamma && s.Owner == u.FreeActor) Assert.Equal(Gamma, tt.Legs[0].Sector);
            }
        }
        // Die Wirtschaft außerhalb läuft weiter (Handel findet statt)
        Assert.True(u.Ships.Values.Count(s => s.Role == ShipRole.Trader) >= 10);
    }

    // ───────── Sektorkontrolle und Regierungsunterhalt ─────────

    [Fact]
    public void Sektorkontrolle_BesitzerwechselMitHysterese_TerritoriumUndStufen()
    {
        var u = World();
        var delta = u.Sectors[Delta];
        // Delta: 8 Gildenstationen + Sektor-HQ (4) = 12. Union bekommt 5 → 5/12 = 42 % gegen 58 %: Gilde bleibt, umkämpft
        foreach (var id in new[] { 14, 15, 16, 17, 18 }) u.Stations[new StationId(id)].Owner = Union;
        Assert.False(SectorControl.Update(u, delta) && delta.Owner != Guild);
        Assert.Equal(Guild, delta.Owner);
        Assert.True(delta.Contested);
        Assert.Equal(ControlStage.Dominance, delta.Control);
        // Union 7/12 = 58 % gegen 42 %: Vorsprung 17 % > 10 % → Besitzerwechsel, Territorium wandert mit, ohne Behörde nur Einfluss
        foreach (var id in new[] { 19, 23 }) u.Stations[new StationId(id)].Owner = Union;
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        SectorControl.Update(u, delta);
        Assert.Equal(Union, delta.Owner);
        Assert.Contains(Delta, u.Factions[Union].Territory);
        Assert.DoesNotContain(Delta, u.Factions[Guild].Territory);
        Assert.Single(events.OfType<SectorOwnerChanged>());
        Assert.Equal(ControlStage.Influence, delta.Control);   // über 50 %, aber kein Sektor-HQ und keine Wache der Union
        // Hysterese: knapp zurück (Gilde 6/12 gegen Union 6/12) wechselt nicht zurück
        u.Stations[new StationId(19)].Owner = Guild;
        SectorControl.Update(u, delta);
        Assert.Equal(Union, delta.Owner);
        // Präsenz: eine einzelne Station
        var alpha = u.Sectors[Alpha];
        u.Stations[new StationId(1)].Owner = Guild;
        var inf = SectorControl.Compute(u, alpha);
        Assert.Equal(ControlStage.Presence, SectorControl.StageOf(u, alpha, inf, Guild));
        Assert.Equal(ControlStage.Control, SectorControl.StageOf(u, alpha, inf, Union));
    }

    [Fact]
    public void Regierungsunterhalt_AusDerKasse_SonstAusDerStation()
    {
        var u = World();
        var hq = u.Stations[new StationId(32)];
        var upkeep = new List<TreasuryUpkeep>();
        u.Events.Raised += e => { if (e is TreasuryUpkeep t) upkeep.Add(t); };
        u.Actors[Union].Treasury.Credits = 1000f;
        float stationBefore = hq.Credits;
        new UpkeepSystem().Step(u, 10);
        var paid = Assert.Single(upkeep, t => t.Actor == (ActorId)Union);
        Assert.Equal((6f + 3f) * 10, paid.Amount, 3);   // Hauptquartier + Sektor-HQ Beta (Phase 14: Forschungsinstitut nur mit Forschung)
        Assert.Equal(0f, paid.Unpaid);
        Assert.Equal(stationBefore, hq.Credits, 3);
        upkeep.Clear();
        // Kasse leer: die Station zahlt, soweit sie Geld hat; der Rest bleibt unbezahlt (keine Schulden)
        u.Actors[Union].Treasury.Credits = 0f;
        hq.Credits = 40f;
        new UpkeepSystem().Step(u, 10);
        Assert.Equal(0f, hq.Credits, 3);
        Assert.Equal(90f - 40f, upkeep.Single(t => t.Actor == (ActorId)Union).Unpaid, 3);
        // geschützt: keine Pleite
        hq.Credits = -1e6f;
        new StationLifecycleSystem().Step(u, 20);
        Assert.True(u.Stations.ContainsKey(hq.Id));
    }

    // ───────── Persistenz ─────────

    [Fact]
    public void SaveRoundtrip_V13_SperrenGatesEinfluss_BitIdentisch()
    {
        var u = World(mainRoutes: false, rules: r => r.RelationDriftRate = 0f);
        Relations.Set(u, Guild, Union, -0.9f);
        var runner = new SimulationRunner();
        runner.Run(u, 300);
        GateAccess.SetState(u, GateFrom(u, Alpha, Beta), GateState.Destroyed);
        runner.Run(u, 300);
        Assert.False(u.Sectors[Gamma].Access.IsOpen);
        var json = SaveGameJson.Write(u, Now);
        Assert.Contains("\"Version\":17", json);   // Phase 14: v17
        Assert.Contains("\"Destroyed\"", json);
        var (loaded, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        Assert.Equal(u.Sectors[Gamma].Access.EntryLevel, loaded.Sectors[Gamma].Access.EntryLevel);
        Assert.Equal(u.Sectors[Gamma].Access.ExpiresTick, loaded.Sectors[Gamma].Access.ExpiresTick);
        Assert.Equal(GateState.Destroyed, loaded.Gates[GateFrom(u, Alpha, Beta).Id].State);
        Assert.Equal(u.Sectors[Delta].Influence[Guild], loaded.Sectors[Delta].Influence[Guild]);
        runner.Run(u, 2000);
        new SimulationRunner().Run(loaded, 2000);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Migration_V12ToV13_RegierungsstationenHauptrouteUndOffeneGates()
    {
        var u = TestWorlds.Classic();
        var v12 = ToV12Json(SaveGameJson.Write(u, Now));
        Assert.Contains("\"Version\":12", v12);
        Assert.DoesNotContain("headquarters", v12);
        Assert.DoesNotContain("MainRoute", v12);
        var (loaded, _) = SaveGameJson.Read(v12);
        Assert.Equal(4, loaded.Stations.Values.Count(s => StationProtection.IsOfficial(loaded, s) && StationProtection.KindOf(loaded, s) != GovernmentKind.Research));   // Phase 13: Forschungsstationen zählen extra
        Assert.All(loaded.Gates.Values, g => { Assert.True(g.MainRoute); Assert.Equal(GateState.Open, g.State); });
        // Nach dem ersten Schritt der Sektorkontrolle sind frische Welt und migrierter Stand gleich
        new SimulationRunner().Run(u, 2000);
        new SimulationRunner().Run(loaded, 2000);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Migration_AlteKette_LaedtUndLaeuft()
    {
        var u = TestWorlds.Classic();
        var (loaded, _) = SaveGameJson.Read(FuelModelTests.ToV5Json(SaveGameJson.Write(u, Now)));
        Assert.Equal(4, loaded.Stations.Values.Count(s => StationProtection.IsOfficial(loaded, s) && StationProtection.KindOf(loaded, s) != GovernmentKind.Research));   // Phase 13: Forschungsstationen zählen extra
        new SimulationRunner().Run(loaded, 2000);
        Assert.All(loaded.Sectors.Values, s => Assert.NotEqual(ControlStage.None, s.Control));
    }
}
