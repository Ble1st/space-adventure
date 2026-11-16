using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Runde 42: Auftragsarten Kopfgeldjagd, Eskorte, Patrouille auf der Infrastruktur von Runde 41. Bindend: Angebote mit eigenem
/// Missions-RNG (nie Universe.Rng) und ohne Hash-Einfluss; angenommene Aufträge gehasht; Kopfgeld nur bei Zerstörung NACH Annahme
/// durch den Spieler (aktives Schiff oder eigene Flotte); Eskorte rein beobachtend mit Stichproben alle 20 Ticks (Anteil ≥
/// EscortMinShare) und Erfüllung bei Ankunft; Patrouille mit Wegpunkten in Reihenfolge; Geldfluss über Ereignisse erklärbar;
/// Speichern/Laden bitgleich; NPC-Welt unverändert (Hash-Vergleich ohne Spielerhandlung).
/// </summary>
public class Runde42Tests
{
    static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    // ein Runner je Universum — ein zweiter Runner würde seine Systeme ein zweites Mal am Ereignisbus anmelden (Events kämen doppelt)
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Universe, SimulationRunner> Runners = new();

    static Universe World(ulong seed = 1)
    {
        var u = TestWorlds.Default(seed, fuel: false, research: false);
        Run(u, 1);   // Systeme hängen sich an den Bus (Comms, Missionen)
        return u;
    }

    static void Run(Universe u, int ticks) => Runners.GetValue(u, _ => new SimulationRunner()).Run(u, ticks);

    /// <summary>Wartet, bis ein Angebot die Bedingung erfüllt (Board erneuert alle 2400 Ticks).</summary>
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

    static Ship Player(Universe u) => u.Ships[u.PlayerShip];

    static List<SimEvent> Capture(Universe u)
    {
        var ev = new List<SimEvent>();
        u.Events.Raised += e => ev.Add(e);
        return ev;
    }

    static MissionOffer BountyOffer(Universe u) =>
        FindOffer(u, o => o.Type == BountyMission.KeyId && MissionRules.AcceptIssue(u, o.Id, Player(u), remote: true) is null)
        ?? throw new Xunit.Sdk.XunitException("kein annehmbares Kopfgeld-Angebot in der Welt gefunden");

    static MissionOffer EscortOffer(Universe u) =>
        FindOffer(u, o => o.Type == EscortMission.KeyId && MissionRules.AcceptIssue(u, o.Id, Player(u), remote: true) is null)
        ?? throw new Xunit.Sdk.XunitException("kein annehmbares Eskorte-Angebot in der Welt gefunden");

    static MissionOffer PatrolOffer(Universe u) =>
        FindOffer(u, o => o.Type == PatrolMission.KeyId && MissionRules.AcceptIssue(u, o.Id, Player(u), remote: true) is null)
        ?? throw new Xunit.Sdk.XunitException("kein annehmbares Patrouille-Angebot in der Welt gefunden");

    /// <summary>Welt mit aufsteigenden Seeds, bis ein annehmbares Angebot der Art existiert (deterministische Seedfolge).</summary>
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

    static Mission AcceptRemote(Universe u, MissionOffer o)
    {
        var (m, issue) = MissionRules.Accept(u, o.Id, Player(u), remote: true);
        Assert.Null(issue);
        Assert.NotNull(m);
        return m!;
    }

    /// <summary>Ziel in den Spielersektor legen und ein Gefecht beginnen (Ziel Seite 1); das aktive Schiff nur, wenn <paramref name="withPlayer"/>.</summary>
    static Encounter AssaultSetup(Universe u, Ship target, bool withPlayer)
    {
        var sector = u.PlayerSector;
        var all = withPlayer ? new[] { target, Player(u) } : new[] { target };
        foreach (var s in all)
        {
            if (s.Transit is not null) u.FinishTransit(s);
            s.Order = null; s.Phase = TradePhase.Idle; s.MinePhase = MinePhase.Idle;
            if (s.Sector != sector) u.MoveShip(s, sector);
            s.DockedAt = default;
        }
        if (withPlayer) Player(u).Position = new Vec2(0f, 0f);
        target.Position = new Vec2(50f, 0f);
        var e = Engage.Begin(u, EncounterCause.Assault, sector);
        if (withPlayer) Engage.Join(u, e, Player(u), 0, CombatTactics.Player, 0f);
        Engage.Join(u, e, target, 1, CombatTactics.Raider, 50f);
        return e;
    }

    static Ship NewNpcShip(Universe u, string model, ShipRole role, ActorId owner)
    {
        var m = u.ShipModels[model];
        var cls = u.ShipClasses[m.ClassKey];
        var yard = u.StationsById().First(s => !s.IsBuildSite);
        var ship = ShipPurchase.Create(u, u.NewShipId(), model, cls.Slots, m.Fit, role, Controller.Ai, owner, yard, 2000f);
        u.AddShip(ship);
        return ship;
    }

    /// <summary>Zielschiff im Gefecht zerstören; <paramref name="byPlayer"/> false = nur NPCs beteiligt (Seite 0 = NPC-Schütze).</summary>
    static void DestroyInFight(Universe u, Ship target, bool byPlayer)
    {
        var e = AssaultSetup(u, target, byPlayer);
        if (!byPlayer)
        {
            var faction = u.NpcFactionIds().First();
            var shooter = NewNpcShip(u, "bastion", ShipRole.Police, new ActorId(faction.Value));
            shooter.Position = new Vec2(80f, 0f);
            Engage.Join(u, e, shooter, 0, CombatTactics.Police, 80f);
        }
        var p = e.Of(target.Id)!;
        Engage.Destroy(u, e, p, target, byPlayer ? "Du" : "Polizei");
    }

    // ───────── Angebote: deterministisch, eigener RNG, nicht gehasht ─────────

    [Fact]
    public void Angebote_Runde42Arten_DeterministischAusWeltSeed_GleicherHash()
    {
        var a = World(7);
        var b = World(7);
        Run(a, 8000); Run(b, 8000);
        Assert.NotEmpty(a.MissionOffers);
        Assert.Equal(a.MissionOffers.Count, b.MissionOffers.Count);
        for (int i = 0; i < a.MissionOffers.Count; i++)
        {
            Assert.Equal(a.MissionOffers[i].Type, b.MissionOffers[i].Type);
            Assert.Equal(a.MissionOffers[i].At, b.MissionOffers[i].At);
            Assert.Equal(a.MissionOffers[i].Args.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value),
                b.MissionOffers[i].Args.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value));
            Assert.Equal(a.MissionOffers[i].Reward, b.MissionOffers[i].Reward);
        }
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));   // Angebote im Hash nicht sichtbar
    }

    [Fact]
    public void Angebote_BeeinflussenNpcHashNicht_MissionsActiveAusGibtGleichenHash()
    {
        var on = TestWorlds.Default(7, fuel: false, research: false);   // beide ohne Run(1): gleiche Tick-Zahl
        var off = TestWorlds.Default(7, fuel: false, research: false);
        off.Rules.MissionsActive = false;
        Run(on, 8000); Run(off, 8000);
        Assert.NotEmpty(on.MissionOffers);
        Assert.Empty(off.MissionOffers);
        Assert.Empty(on.Missions);
        Assert.Equal(UniverseHash.Compute(off), UniverseHash.Compute(on));
        Assert.Equal(UniverseHash.Compute(off, true, includePlayerPolitics: false), UniverseHash.Compute(on, true, includePlayerPolitics: false));
    }

    [Fact]
    public void Angebote_Runde42Arten_EntstehenInDerWelt_CommsKanal()
    {
        var (u, bounty) = WorldWithOffer(31, BountyMission.KeyId);
        Assert.Equal("comms", bounty.Args["channel"]);
        Assert.True(bounty.Reward > 0f);
        Assert.True(u.Ships.ContainsKey(BountyMission.TargetOf(new Mission { Args = bounty.Args })));
        // über Comms annehmbar (remote) und im Comms-Angebotsliste sichtbar
        var comms = SpaceSim.Core.Comms.CommsActions.Default();
        Assert.Contains(comms.Offers(u), o => o.Key == $"mission:accept:{bounty.Id}");
        Assert.True(comms.Preview(u, $"mission:accept:{bounty.Id}").Possible);

        var (u2, escort) = WorldWithOffer(51, EscortMission.KeyId);
        Assert.True(EscortMission.SamplesOf(new Mission { Args = escort.Args }) == 0);
        Assert.Contains(comms.Offers(u2), o => o.Key == $"mission:accept:{escort.Id}");

        var (u3, patrol) = WorldWithOffer(61, PatrolMission.KeyId);
        Assert.True(PatrolMission.WaypointsOf(new Mission { Args = patrol.Args }).Count is >= 2 and <= 3);
        Assert.Contains(comms.Offers(u3), o => o.Key == $"mission:accept:{patrol.Id}");
    }

    // ───────── Kopfgeldjagd ─────────

    [Fact]
    public void Kopfgeld_Erfuellt_WennZielNachAnnahmeVomSpielerZerstoert_Wird_GeldflussErklaerbar()
    {
        var (u, o) = WorldWithOffer(41, BountyMission.KeyId);
        var target = u.Ships[BountyMission.TargetOf(new Mission { Args = o.Args })];
        var ship = Player(u);
        u.WalletOf(ship).Credits = 100_000f;
        float wallet0 = u.WalletOf(ship).Credits;
        float client0 = u.Actors[o.Client].Treasury.Credits;
        var ev = Capture(u);
        ulong hashBefore = UniverseHash.Compute(u);

        var m = AcceptRemote(u, o);
        Assert.NotEqual(hashBefore, UniverseHash.Compute(u));   //angenommen = gehasht
        Assert.Contains(ev.OfType<TreasuryUpkeep>(), t => t.Purpose == "mission_reserve" && MathF.Abs(t.Amount - m.Reserved) < 0.001f);
        Assert.True(MathF.Abs(u.Actors[o.Client].Treasury.Credits - (client0 - m.Reserved)) < 0.01f, "Belohnung aus Spendable reserviert");
        Assert.True(m.Reserved > 0f);
        var mk = Assert.Single(MissionMarkers.For(u));
        Assert.Equal(MissionMarkerKind.Ship, mk.Kind);
        Assert.Equal(m.Id, mk.Mission);

        // Ziel NACH Annahme vom Spieler zerstört (aktives Schiff auf Seite 0 des Gefechts)
        DestroyInFight(u, target, byPlayer: true);

        Assert.Empty(u.Missions);
        Assert.Contains(ev.OfType<MissionCompleted>(), e => e.Mission == m.Id);
        Assert.Contains(ev.OfType<MissionPaid>(), e => e.Mission == m.Id && MathF.Abs(e.Amount - m.Reserved) < 0.001f);
        Assert.True(MathF.Abs(u.WalletOf(ship).Credits - (wallet0 + m.Reserved)) < 0.01f,
            $"Belohnung als eigener Posten in der Spielerkasse ({wallet0:0} → {u.WalletOf(ship).Credits:0}, reserviert {m.Reserved:0})");
        Assert.True(MathF.Abs(u.Actors[o.Client].Treasury.Credits - (client0 - m.Reserved)) < 0.01f, "Auftraggeber-Kasse: Reserve ging an den Spieler, keine Doppelzahlung");
        Assert.Empty(MissionMarkers.For(u));   // Marker weg nach Erfüllung
    }

    [Fact]
    public void Kopfgeld_Missbrauch_ZielVorAnnahmeZerstoert_ZaehltNicht_AnnahmeSperrt()
    {
        var (u, o) = WorldWithOffer(42, BountyMission.KeyId);
        var target = u.Ships[BountyMission.TargetOf(new Mission { Args = o.Args })];
        float wallet0 = u.WalletOf(Player(u)).Credits;

        // Ziel vor Annahme zerstört (nur NPCs im Gefecht — kein Spieler-Schiff auf der Gegenseite)
        DestroyInFight(u, target, byPlayer: false);
        Assert.DoesNotContain(u.Ships, kv => kv.Key == target.Id);

        // Annahme danach ist gesperrt — die Zerstörung vor Annahme zählt nicht
        Assert.NotNull(MissionRules.AcceptIssue(u, o.Id, Player(u), remote: true));
        var (m, issue) = MissionRules.Accept(u, o.Id, Player(u), remote: true);
        Assert.Null(m);
        Assert.NotNull(issue);
        Assert.Empty(u.Missions);
        Assert.True(MathF.Abs(u.WalletOf(Player(u)).Credits - wallet0) < 0.001f, "kein Geld ohne Auftrag");
    }

    [Fact]
    public void Kopfgeld_Scheitern_WennZielNachAnnahmeNichtVomSpielerZerstoert_KeineBelohnung()
    {
        var (u, o) = WorldWithOffer(43, BountyMission.KeyId);
        var target = u.Ships[BountyMission.TargetOf(new Mission { Args = o.Args })];
        var ev = Capture(u);
        float client0 = u.Actors[o.Client].Treasury.Credits;
        float wallet0 = u.WalletOf(Player(u)).Credits;

        var m = AcceptRemote(u, o);
        // Ziel wird von der Polizei zerstört (kein Spieler-Schiff im Gefecht) → „anders zerstört“
        DestroyInFight(u, target, byPlayer: false);

        Assert.Empty(u.Missions);
        Assert.Contains(ev.OfType<MissionFailed>(), e => e.Mission == m.Id && e.Reason == "Ziel anders zerstört");
        Assert.DoesNotContain(ev.OfType<MissionPaid>(), e => e.Mission == m.Id);
        Assert.Contains(ev.OfType<TreasuryIncome>(), t => t.Purpose == "mission_return" && MathF.Abs(t.Amount - m.Reserved) < 0.001f);
        Assert.True(MathF.Abs(u.Actors[o.Client].Treasury.Credits - client0) < 0.01f, "Reserve zurück in die Fraktionskasse");
        Assert.True(MathF.Abs(u.WalletOf(Player(u)).Credits - wallet0) < 0.01f, "keine Belohnung");
    }

    [Fact]
    public void Kopfgeld_Scheitern_FristAbgelaufen_OderZielVerschwindetOhneGefecht()
    {
        var (u, o) = WorldWithOffer(44, BountyMission.KeyId);
        var m = AcceptRemote(u, o);
        var ev = Capture(u);

        u.Tick = m.Deadline;
        MissionRules.Types.Get(m.Type)!.Check(u, m);
        Assert.Empty(u.Missions);
        Assert.Contains(ev.OfType<MissionFailed>(), e => e.Mission == m.Id && e.Reason == "Frist abgelaufen");

        // Ziel verschwindet ohne Gefecht (z. B. aufgegeben) → Scheitern „Ziel verschwunden“
        var (u2, o2) = WorldWithOffer(45, BountyMission.KeyId);
        var m2 = AcceptRemote(u2, o2);
        var target2 = u2.Ships[BountyMission.TargetOf(new Mission { Args = o2.Args })];
        var ev2 = Capture(u2);
        Departures.RemoveShip(u2, target2, DepartureReason.Unprofitable, "test");
        Assert.Empty(u2.Missions);
        Assert.Contains(ev2.OfType<MissionFailed>(), e => e.Mission == m2.Id && e.Reason == "Ziel verschwunden");
    }

    [Fact]
    public void Kopfgeld_Marker_ZuletztBekanntePosition_NurSektorWennZielWeg()
    {
        var (u, o) = WorldWithOffer(46, BountyMission.KeyId);
        var m = AcceptRemote(u, o);
        var target = u.Ships[BountyMission.TargetOf(m)];
        var (sec, pos) = ThreatMap.Where(u, target);
        var mk = Assert.Single(MissionMarkers.For(u));
        Assert.Equal(sec, mk.Sector);
        Assert.Equal(pos, mk.Position);
        Assert.Equal(MissionMarkerKind.Ship, mk.Kind);
        Assert.Contains("Kopfgeld", mk.Label);

        // Ziel weg (entfernt ohne Gefecht): Mission scheitert, MissionMarkers.For liefert nichts mehr
        Departures.RemoveShip(u, target, DepartureReason.Unprofitable, "test");
        Assert.Empty(MissionMarkers.For(u));
        // Marker-Logik mit frischem Auftrag: nach Check steht die letzte bekannte Position im Args,
        // nach Entfernen des Ziels zeigt Marker nur noch Sektor + letzte Position (Point)
        var (u2, o2) = WorldWithOffer(47, BountyMission.KeyId);
        var m2 = AcceptRemote(u2, o2);
        var t2 = u2.Ships[BountyMission.TargetOf(m2)];
        MissionRules.Types.Get(m2.Type)!.Check(u2, m2);   // merkt letzte Position
        Assert.True(m2.Args.ContainsKey("lastSector"));
        var before = MissionRules.Types.Get(m2.Type)!.Marker(u2, m2);
        Assert.Equal(MissionMarkerKind.Ship, before!.Value.Kind);
        u2.RemoveShip(t2.Id, "test");   // Mission scheitert dadurch, das Mission-Objekt bleibt für den Marker-Test bestehen
        var mk2 = MissionRules.Types.Get(m2.Type)!.Marker(u2, m2);
        Assert.NotNull(mk2);
        Assert.Equal(MissionMarkerKind.Point, mk2!.Value.Kind);
        Assert.Equal(before.Value.Sector, mk2.Value.Sector);   // nur noch der (letzte) Sektor mit letzter Position
    }

    // ───────── Eskorte ─────────

    /// <summary>Spieler folgt dem Schützling (Sektor + Position), bis die Mission entschieden ist (Ankunft) oder maxTicks erreicht.</summary>
    static void FollowEscort(Universe u, Ship escort, Mission m, int maxTicks)
    {
        var me = Player(u);
        for (int t = 0; t < maxTicks && u.Missions.Contains(m); t += 5)
        {
            var (sec, pos) = ThreatMap.Where(u, escort);
            if (me.Sector != sec) u.MoveShip(me, sec);
            me.Position = pos;
            Run(u, 5);   // feine Schritte: der Schützling driftet sonst in 20 Ticks über EscortRange hinaus
        }
    }

    [Fact]
    public void Eskorte_Erfuellt_BeiAnkunftWennGrossteilInReichweite_GeldflussErklaerbar()
    {
        var (u, o) = WorldWithOffer(51, EscortMission.KeyId);
        var escort = u.Ships[EscortMission.EscortOf(new Mission { Args = o.Args })];
        var ship = Player(u);
        u.WalletOf(ship).Credits = 100_000f;
        float wallet0 = u.WalletOf(ship).Credits;
        float clientBefore = u.Actors[o.Client].Treasury.Credits;
        var ev = Capture(u);

        var m = AcceptRemote(u, o);
        Assert.True(m.Reserved > 0f);
        Assert.True(MathF.Abs(u.Actors[o.Client].Treasury.Credits - (clientBefore - m.Reserved)) < 0.01f, "Belohnung aus Spendable reserviert");
        Assert.Contains(ev.OfType<TreasuryUpkeep>(), t => t.Purpose == "mission_reserve" && MathF.Abs(t.Amount - m.Reserved) < 0.001f);
        var mk = Assert.Single(MissionMarkers.For(u));
        Assert.Equal(MissionMarkerKind.Ship, mk.Kind);
        Assert.Equal(ThreatMap.Where(u, escort).Sector, mk.Sector);
        Assert.Contains("Eskorte", mk.Label);

        FollowEscort(u, escort, m, 20000);   // Spieler bleibt die ganze Strecke am Schützling (bis die Mission entschieden ist)
        Assert.Empty(u.Missions);          // bei Ankunft erfüllt (Schützling kann danach weiterfliegen — egal)
        Assert.True(EscortMission.SamplesOf(m) > 0, "Stichproben gelaufen");
        Assert.True(EscortMission.ShareOf(m) >= u.Rules.EscortMinShare - 0.0001f,
            $"Anteil {EscortMission.ShareOf(m):0.##} ({EscortMission.HitsOf(m)}/{EscortMission.SamplesOf(m)}) ≥ {u.Rules.EscortMinShare}");

        Assert.Contains(ev.OfType<ShipArrived>(), e => e.Ship == escort.Id);
        Assert.Contains(ev.OfType<MissionCompleted>(), e => e.Mission == m.Id);
        Assert.Contains(ev.OfType<MissionPaid>(), e => e.Mission == m.Id && MathF.Abs(e.Amount - m.Reserved) < 0.001f);
        Assert.True(MathF.Abs(u.WalletOf(ship).Credits - (wallet0 + m.Reserved)) < 0.01f, "Belohnung in der Spielerkasse");
    }

    [Fact]
    public void Eskorte_Missbrauch_NurKurzInDerNaeheReichtNicht_KeineBelohnung()
    {
        var (u, o) = WorldWithOffer(52, EscortMission.KeyId);
        var escort = u.Ships[EscortMission.EscortOf(new Mission { Args = o.Args })];
        var ship = Player(u);
        float wallet0 = u.WalletOf(ship).Credits;
        float clientBefore = u.Actors[o.Client].Treasury.Credits;
        var ev = Capture(u);

        var m = AcceptRemote(u, o);
        float clientAfterAccept = u.Actors[o.Client].Treasury.Credits;
        Assert.True(MathF.Abs(clientAfterAccept - (clientBefore - m.Reserved)) < 0.01f, "reserviert bei Annahme");
        // Runde 42b: Eskorte wird schon ab EscortMinRemainingTicks (40) Reststrecke angeboten — für den Test die Ankunft
        // weit genug weg schieben, damit nach den 2 nahen Stichproben genug entfernte dazukommen
        escort.Transit!.ArrivalTick = u.Tick + 120;   // genug entfernte Stichproben, aber kurz genug, dass die Fraktionskasse nicht anderweitig schrumpft
        m.Deadline = u.Tick + 140;
        // nur die ersten 40 Ticks in der Nähe (2 Stichproben), dann weg
        FollowEscort(u, escort, m, 40);
        var me = Player(u);
        var far = u.Sectors.Keys.OrderBy(k => k.Value).Last();
        if (me.Sector != far) u.MoveShip(me, far);
        me.Position = new Vec2(9000f, 9000f);
        for (int t = 0; t < 20000 && u.Missions.Contains(m); t += 20) Run(u, 20);   // bis Ankunft entschieden

        int samples = EscortMission.SamplesOf(m), hits = EscortMission.HitsOf(m);
        Assert.True(samples > 2, $"mehr Stichproben als die 2 nahen ({samples})");
        Assert.True(EscortMission.ShareOf(m) < u.Rules.EscortMinShare, $"Anteil {hits}/{samples} < {u.Rules.EscortMinShare}");
        Assert.Empty(u.Missions);   // gescheitert
        Assert.Contains(ev.OfType<MissionFailed>(), e => e.Mission == m.Id && e.Reason == "zu wenig begleitet");
        Assert.DoesNotContain(ev.OfType<MissionPaid>(), e => e.Mission == m.Id);
        Assert.Contains(ev.OfType<TreasuryIncome>(), t => t.Purpose == "mission_return" && MathF.Abs(t.Amount - m.Reserved) < 0.001f);
        Assert.True(MathF.Abs(u.WalletOf(ship).Credits - wallet0) < 0.01f, "keine Belohnung");
        Assert.True(u.Actors[o.Client].Treasury.Credits >= clientAfterAccept + m.Reserved - 0.01f, "Reserve zurück in die Fraktionskasse (Event mission_return, exakter Betrag)");
    }

    [Fact]
    public void Eskorte_Scheitern_WennSchuetzlingZerstoert_OderFrist()
    {
        var (u, o) = WorldWithOffer(53, EscortMission.KeyId);
        var escort = u.Ships[EscortMission.EscortOf(new Mission { Args = o.Args })];
        var ev = Capture(u);
        var m = AcceptRemote(u, o);

        Departures.RemoveShip(u, escort, DepartureReason.Destroyed, "raid");
        Assert.Empty(u.Missions);
        Assert.Contains(ev.OfType<MissionFailed>(), e => e.Mission == m.Id && e.Reason == "Schützling zerstört");

        // Frist
        var (u2, o2) = WorldWithOffer(54, EscortMission.KeyId);
        var m2 = AcceptRemote(u2, o2);
        var ev2 = Capture(u2);
        u2.Tick = m2.Deadline;
        MissionRules.Types.Get(m2.Type)!.Check(u2, m2);
        Assert.Empty(u2.Missions);
        Assert.Contains(ev2.OfType<MissionFailed>(), e => e.Mission == m2.Id && e.Reason == "Frist abgelaufen");
    }

    [Fact]
    public void Eskorte_Beobachtend_SchuetzlingVerhaltenUnveraendert()
    {
        var (u, o) = WorldWithOffer(55, EscortMission.KeyId);
        var escort = u.Ships[EscortMission.EscortOf(new Mission { Args = o.Args })];
        var route = escort.Transit;
        var order = escort.Order;
        var m = AcceptRemote(u, o);
        Run(u, 25);   // MissionSystem prüft (Stichprobe läuft) — ohne den Schützling zu berühren
        Assert.True(EscortMission.SamplesOf(m) >= 1, "Stichprobe lief");
        // Eskorte ändert weder Route noch Auftrag des Schützlings (Ankunft während der 25 Ticks wäre normales NPC-Verhalten)
        Assert.True(escort.Transit is null || ReferenceEquals(route, escort.Transit), "Route des Schützlings unverändert");
        Assert.Equal(order, escort.Order);
    }

    // ───────── Patrouille ─────────

    static void FlyTo(Universe u, SectorId sector, Vec2 pos)
    {
        var me = Player(u);
        if (me.Transit is not null) u.FinishTransit(me);
        if (me.Sector != sector) u.MoveShip(me, sector);
        me.Position = pos;
        me.DockedAt = default;
        Run(u, 25);   // MissionSystem prüft alle 20 Ticks
    }

    [Fact]
    public void Patrouille_Erfuellt_WennAlleWegpunkteInReihenfolgeAngeflogen_GeldflussErklaerbar()
    {
        var (u, o) = WorldWithOffer(61, PatrolMission.KeyId);
        var wps = PatrolMission.WaypointsOf(new Mission { Args = o.Args });
        Assert.True(wps.Count is >= 2 and <= 3);
        var ship = Player(u);
        u.WalletOf(ship).Credits = 100_000f;
        float wallet0 = u.WalletOf(ship).Credits;
        float client0 = u.Actors[o.Client].Treasury.Credits;
        var ev = Capture(u);

        var m = AcceptRemote(u, o);
        Assert.Equal(0, PatrolMission.CurrentOf(m));
        var mk = Assert.Single(MissionMarkers.For(u));
        Assert.Equal(MissionMarkerKind.Point, mk.Kind);
        Assert.Equal(wps[0].Sector, mk.Sector);
        Assert.Contains("Patrouille", mk.Label);

        foreach (var (sec, pos) in wps) FlyTo(u, sec, pos);

        Assert.Empty(u.Missions);
        Assert.Contains(ev.OfType<MissionCompleted>(), e => e.Mission == m.Id);
        Assert.Contains(ev.OfType<MissionPaid>(), e => e.Mission == m.Id && MathF.Abs(e.Amount - m.Reserved) < 0.001f);
        Assert.True(MathF.Abs(u.WalletOf(ship).Credits - (wallet0 + m.Reserved)) < 0.01f, "Belohnung in der Spielerkasse");
        Assert.True(m.Reserved <= u.Rules.MissionPatrolBaseReward + wps.Count * u.Rules.MissionPatrolRewardPerWaypoint + 0.01f, "niedrig bezahlt");
    }

    [Fact]
    public void Patrouille_Missbrauch_WegpunkteNurInReihenfolge()
    {
        var (u, o) = WorldWithOffer(62, PatrolMission.KeyId);
        var wps = PatrolMission.WaypointsOf(new Mission { Args = o.Args });
        Assert.True(wps.Count >= 2);
        var m = AcceptRemote(u, o);

        // zweiter Wegpunkt zuerst: zählt nicht
        FlyTo(u, wps[1].Sector, wps[1].Pos);
        Assert.Equal(0, PatrolMission.CurrentOf(m));
        Assert.Single(u.Missions);

        // erster Wegpunkt: zählt
        FlyTo(u, wps[0].Sector, wps[0].Pos);
        Assert.Equal(1, PatrolMission.CurrentOf(m));

        // Rest in Reihenfolge
        for (int i = 1; i < wps.Count; i++) FlyTo(u, wps[i].Sector, wps[i].Pos);
        Assert.Empty(u.Missions);   // erfüllt
    }

    [Fact]
    public void Patrouille_Scheitern_FristAbgelaufen()
    {
        var (u, o) = WorldWithOffer(63, PatrolMission.KeyId);
        var m = AcceptRemote(u, o);
        var ev = Capture(u);
        u.Tick = m.Deadline;
        MissionRules.Types.Get(m.Type)!.Check(u, m);
        Assert.Empty(u.Missions);
        Assert.Contains(ev.OfType<MissionFailed>(), e => e.Mission == m.Id && e.Reason == "Frist abgelaufen");
        Assert.Contains(ev.OfType<TreasuryIncome>(), t => t.Purpose == "mission_return" && MathF.Abs(t.Amount - m.Reserved) < 0.001f);
    }

    // ───────── Annahme über den Dock-Dienst (UI-Pfad) ─────────

    [Fact]
    public void Annahme_UeberDockDienst_MissionsService_ListetNeueArten()
    {
        var (u, o) = WorldWithOffer(64, PatrolMission.KeyId);
        var st = u.Stations[o.At];
        var ship = Player(u);
        ship.DockedAt = st.Id;
        ship.Position = st.DockPoint(ship.Id);
        var svc = new MissionService();
        var offers = svc.Offers(u, st, ship);
        Assert.Contains(offers, x => x.Key == $"{MissionService.Accept}:{o.Id}");
        var r = svc.Execute(u, st, ship, new ServiceRequest($"{MissionService.Accept}:{o.Id}", default, 0f));
        Assert.True(r.Success, r.Message);
        var m = Assert.Single(u.Missions);
        Assert.Equal(PatrolMission.KeyId, m.Type);
        Assert.Contains("Patrouille", MissionRules.Types.Get(m.Type)!.Describe(u, m));
    }

    // ───────── Speichern / Laden bitgleich (je Art) ─────────

    /// <summary>Speichern → Laden → gleicher Hash, gleicher Zustand; danach 500 Ticks in beiden Welten → gleicher Hash.</summary>
    static void SaveLoadRoundtrip(Universe u)
    {
        ulong hashBefore = UniverseHash.Compute(u);
        var save = UniverseSerializer.ToSave(u, Now);
        var loaded = UniverseSerializer.FromSave(save);
        Assert.Equal(hashBefore, UniverseHash.Compute(loaded));   // bitgleich inkl. Missionen
        Run(u, 500); Run(loaded, 500);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void SpeichernLaden_Bitgleich_Kopfgeldjagd_MitMarkerArgs()
    {
        var (u, o) = WorldWithOffer(41, BountyMission.KeyId);
        var m = AcceptRemote(u, o);
        MissionRules.Types.Get(m.Type)!.Check(u, m);   // letzte bekannte Position in die Args
        Assert.True(m.Args.ContainsKey("lastSector"));
        SaveLoadRoundtrip(u);
        var save = UniverseSerializer.ToSave(u, Now);
        var loaded = UniverseSerializer.FromSave(save);
        var lm = Assert.Single(loaded.Missions);
        Assert.Equal(m.Args.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value),
            lm.Args.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value));
        Assert.Equal(m.Deadline, lm.Deadline);
    }

    [Fact]
    public void SpeichernLaden_Bitgleich_Eskorte_MitStichproben()
    {
        var (u, o) = WorldWithOffer(51, EscortMission.KeyId);
        var m = AcceptRemote(u, o);
        Run(u, 40);   // Stichproben laufen mit (MissionSystem alle 20 Ticks)
        Assert.True(EscortMission.SamplesOf(m) > 0, "Stichproben gezählt");
        var save = UniverseSerializer.ToSave(u, Now);
        var loaded = UniverseSerializer.FromSave(save);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        var lm = Assert.Single(loaded.Missions);
        Assert.Equal(EscortMission.SamplesOf(m), EscortMission.SamplesOf(lm));
        Assert.Equal(EscortMission.HitsOf(m), EscortMission.HitsOf(lm));
        Assert.Equal(m.Args["arr"], lm.Args["arr"]);
        Run(u, 500); Run(loaded, 500);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void SpeichernLaden_Bitgleich_Patrouille_MitWegpunktFortschritt()
    {
        var (u, o) = WorldWithOffer(61, PatrolMission.KeyId);
        var m = AcceptRemote(u, o);
        var wps = PatrolMission.WaypointsOf(m);
        FlyTo(u, wps[0].Sector, wps[0].Pos);   // erster Wegpunkt angeflogen
        Assert.Equal(1, PatrolMission.CurrentOf(m));
        var wpsBefore = string.Join(";", wps.Select(w => $"{w.Sector.Value}:{w.Pos.X:0.###}:{w.Pos.Y:0.###}"));
        var save = UniverseSerializer.ToSave(u, Now);
        var loaded = UniverseSerializer.FromSave(save);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        var lm = Assert.Single(loaded.Missions);
        Assert.Equal(wpsBefore, string.Join(";", PatrolMission.WaypointsOf(lm).Select(w => $"{w.Sector.Value}:{w.Pos.X:0.###}:{w.Pos.Y:0.###}")));
        Assert.Equal(1, PatrolMission.CurrentOf(lm));
        Assert.Equal(m.Deadline, lm.Deadline);
        Run(u, 500); Run(loaded, 500);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }
}
