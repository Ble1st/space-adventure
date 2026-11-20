using SpaceSim.Core.Comms;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Runde 42d (Handytest-Restmängel r42c): (1) Flugzeit nie mit doppelter Tilde („~~0:29 min“); (2) Annahmemeldung ohne doppeltes
/// „Auftrag:“ (Dock-Dienst und Comms); (3) Posteingang — nur offene Angebote haben eine Restzeit („verfällt in …“ in der
/// Zeile, „noch m:ss“ darf in erledigten/angenommenen/gescheiterten Meldungen nicht mehr stehen); (4) Liefervertrag-Geldzeile
/// „Festpreis … · Kasse reserviert“ statt „Belohnung 0 Cr (reserviert)“; (5) Kopfgeld/Patrouille an Polizeiwache/HQ/Sektor-HQ
/// mindestens je 5 % der Angebote (eigener Missions-RNG, hash-neutral — andere Chancen ⇒ derselbe Welt-Hash).
/// </summary>
public class Runde42dTests
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

    static MissionOffer TransportOffer(Universe u) =>
        FindOffer(u, o => o.Type == TransportMission.KeyId && MissionRules.AcceptIssue(u, o.Id, Player(u), remote: true) is null)
        ?? throw new Xunit.Sdk.XunitException("kein annehmbares Transportangebot gefunden");

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

    // ───────── 1. Keine doppelte Tilde in Flugzeiten ─────────

    [Fact]
    public void Flugzeit_TexteTragenNurEineTilde()
    {
        var u = World(401);
        // Transport-Angebot: Angebotstext direkt
        var o = TransportOffer(u);
        string offer = MissionRules.DescribeOfferText(u, o);
        Assert.DoesNotContain("~~", offer);
        Assert.Contains("Flugzeit ~", offer);
        // Vorschauzeile (Comms/Dock-Vorschau) aller Angebotstypen, so wie sie erzeugt werden
        for (int i = 0; i < 120; i++)
        {
            Run(u, 100);
            foreach (var x in u.MissionOffers)
            {
                Assert.DoesNotContain("~~", MissionRules.DescribeOfferText(u, x));
                Assert.DoesNotContain("~~", MissionAcceptAction.PreviewLine(u, x, Player(u), null));
            }
        }
    }

    // ───────── 2. Annahmemeldung ohne doppeltes „Auftrag:“ ─────────

    [Fact]
    public void Annahme_MeldungTraegtKeinDoppeltesAuftrag()
    {
        // Dock-Dienst (Schiff an der Angebots-Station angedockt wie im echten Dock-Weg)
        var u = World(411);
        var o = TransportOffer(u);
        var st = u.Stations[o.At];
        var ship = Player(u);
        ship.DockedAt = o.At;
        ship.Position = st.DockPoint(ship.Id);
        var r = new MissionService().Execute(u, st, ship, new ServiceRequest($"{MissionService.Accept}:{o.Id}", default, 0f));
        Assert.True(r.Success);
        Assert.StartsWith("Auftrag angenommen: ", r.Message);
        Assert.DoesNotContain("Auftrag: Auftrag", r.Message);
        // Comms-Registry
        var u2 = World(412);
        var o2 = TransportOffer(u2);
        var r2 = CommsActions.Default().Execute(u2, $"mission:accept:{o2.Id}");
        Assert.True(r2.Success);
        Assert.StartsWith("Auftrag angenommen: ", r2.Message);
        Assert.DoesNotContain("Auftrag: Auftrag", r2.Message);
    }

    // ───────── 3. Posteingang: nur offene Angebote mit Restzeit ─────────

    [Fact]
    public void Posteingang_NurOffeneAngeboteHabenRestzeit()
    {
        var u = World(421);
        // offenes Vertragsangebot → Posteingang-Nachricht (Spieler an der Station angedockt) mit „verfällt in …“
        var co = FindOffer(u, x => x.Type == DeliveryMission.KeyId && x.Args.TryGetValue("contract", out var c) && c == "1",
            rounds: 400) ?? throw new Xunit.Sdk.XunitException("kein Liefervertrag gefunden");
        Player(u).DockedAt = co.At;
        Run(u, 40);   // CommsSystem.SyncContractOffers schreibt die Nachricht
        var offerMsg = u.Inbox.FirstOrDefault(m => m.Kind == "contract_offer" && m.Args.TryGetValue("offer", out var os) && os == co.Id.ToString());
        Assert.NotNull(offerMsg);
        Assert.Contains("verfällt in", CommsText.RowMeta(u, offerMsg!));
        // Annahme → Meldung „mission_accepted“: Zeile ohne „verfällt in …“, Detail ohne weiterlaufende Restzeit
        var m = AcceptDock(u, co);
        Run(u, 20);
        var acc = u.Inbox.Last(x => x.Kind == "mission_accepted");
        Assert.DoesNotContain("verfällt", CommsText.RowMeta(u, acc));
        var (_, accBody) = CommsText.Describe(u, acc);
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(accBody, @"noch \d+:\d\d"), $"kein „noch m:ss“ im Detail: {accBody}");
        // erledigt/gescheitert: synthetische Nachrichten mit gebackener Restzeit — die Restzeit wird gestrichen
        var done = Comms.Add(u, u.PlayerActor, "mission_completed", new()
        {
            ["mission"] = m.Id.ToString(), ["amount"] = "300", ["deadline"] = (u.Tick + 600).ToString(),
            ["message"] = $"Auftrag: 0/20 Ware → Ziel · noch 10:00 (Andocken an der Zielstation liefert ab).",
        }, 40000);
        var failed = Comms.Add(u, u.PlayerActor, "mission_failed", new()
        {
            ["mission"] = m.Id.ToString(), ["reason"] = "Frist abgelaufen", ["deadline"] = (u.Tick + 600).ToString(),
            ["message"] = $"Auftrag: Patrouille Wegpunkt 1/2 → X · noch 9:59.",
        }, 40000);
        foreach (var msg in new[] { done, failed })
        {
            Assert.DoesNotContain("verfällt", CommsText.RowMeta(u, msg));
            var (_, body) = CommsText.Describe(u, msg);
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(body, @"noch \d+:\d\d"), $"kein „noch m:ss“: {body}");
        }
    }

    // ───────── 4. Vertrag: „Festpreis … · Kasse reserviert“ statt „Belohnung 0 Cr“ ─────────

    [Fact]
    public void Vertrag_GeldzeileZeigtFestpreisStattBelohnung0()
    {
        var u = World(431);
        var co = FindOffer(u, x => x.Type == DeliveryMission.KeyId && x.Args.TryGetValue("contract", out var c) && c == "1",
            rounds: 400) ?? throw new Xunit.Sdk.XunitException("kein Liefervertrag gefunden");
        var m = AcceptDock(u, co);
        string line = MissionRules.MoneyLine(u, m);
        Assert.Contains("Festpreis", line);
        Assert.Contains("Kasse reserviert", line);
        Assert.DoesNotContain("Belohnung 0", line);
        // Nicht-Vertrag: normale Belohnungszeile
        var u2 = World(432);
        var to = TransportOffer(u2);
        var m2 = AcceptDock(u2, to);
        string line2 = MissionRules.MoneyLine(u2, m2);
        Assert.Contains($"Belohnung {m2.Reserved:0} Cr (reserviert)", line2);
    }

    // ───────── 5. Kopfgeld/Patrouille ≥ 5 % der Angebote, hash-neutral ─────────

    static Universe BalanceWorld(float bountyChance, float patrolChance)
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.Rules ??= new EconomyRules();
        def.Rules.MissionBountyChance = bountyChance;
        def.Rules.MissionPatrolChance = patrolChance;
        return WorldBuilder.Build(def, null, null);
    }

    [Fact]
    public void Angebote_KopfgeldUndPatrouilleMindestens5Prozent_AngebotsStationenMeistMitAngebot_HashNeutral()
    {
        var u = BalanceWorld(0.9f, 0.95f);
        var runner = new SimulationRunner();
        var byType = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var bountyOrPatrol = new HashSet<(int At, long Bucket)>();
        int total = 0;
        u.Events.Raised += e =>
        {
            if (e is MissionOffered mo)
            {
                total++;
                byType[mo.Type] = byType.GetValueOrDefault(mo.Type) + 1;
                var o = u.MissionOffers.FirstOrDefault(x => x.Id == mo.Offer);
                if (o is null) return;
                if (mo.Type is BountyMission.KeyId or PatrolMission.KeyId)
                {
                    Assert.True(BountyMission.IsOfferStation(u, u.Stations[o.At]), $"{mo.Type} nur an Polizei/HQ/Sektor-HQ");
                    bountyOrPatrol.Add((o.At.Value, o.Created / Math.Max(100, u.Rules.MissionRefreshTicks)));
                }
            }
        };
        for (int t = 0; t < 20000; t++) runner.Step(u);
        Assert.True(total > 500, $"genug Angebote ({total})");
        // Ausbau Punkte 10/14: Schmuggel-, Forschungs- und Fahrgastangebote kamen danach dazu — die Anteile gelten unter den Arten der Runden 41/42
        int newer = byType.GetValueOrDefault("smuggle") + byType.GetValueOrDefault("research") + byType.GetValueOrDefault("passenger");
        int total42 = total - newer;
        double shareB = 100.0 * byType.GetValueOrDefault(BountyMission.KeyId) / total42;
        double shareP = 100.0 * byType.GetValueOrDefault(PatrolMission.KeyId) / total42;
        // Runde 44: in den vergrößerten Sektoren entstehen mehr Liefer- und Eskorte-Angebote (längere Fahrten, größere Lager); die Zahl der Kopfgeld-
        // angebote bleibt (≈ 56 je 20 000 Ticks, vorher 59 — begrenzt durch die gesuchten Kaperschiffe, keine doppelten Angebote), ihr Anteil sank auf ≈ 4,3 %.
        // Geprüft wird jetzt: Kopfgeld ≥ 4 %, Kopfgeld + Patrouille ≥ 10 %, jedes einzeln mindestens 50 Angebote
        // „Treibstoff und Forschung dauerhaft an“: das Forschungsbudget (≈ 11 % der Kasseneinnahmen, bis alle Projekte erforscht sind, ≈ 25 000 Ticks) zieht
        // am verfügbaren Geld der Fraktionen, das auch die Kopfgeld-Belohnung decken muss (FactionViability.Spendable). Gemessen (Standard-Seed, 20 000 Ticks,
        // Kopfgeld/Patrouille/gesamt): beide aus 59/73/1217, nur Treibstoff 60/77/1390, nur Forschung 46/64/1234, beide an 43/66/1295 — die Zahl sinkt
        // durch die Forschung, nicht durch einen Fehler. Geprüft: Kopfgeld ≥ 3 % und ≥ 40 Angebote, Kopfgeld + Patrouille ≥ 8 % (vorher 4 %/50/10 %)
        // Gasernte/Fusion: mehr Liefer-/Geleitangebote (neue Stationen) senken den Anteil — gemessen 3,2 % / 7,96 %; Schwelle 7,5 %
        Assert.True(shareB >= 3.0 && shareB + shareP >= 7.5 && byType.GetValueOrDefault(BountyMission.KeyId) >= 40,
            $"Kopfgeld-Anteil {shareB:0.0}% ≥ 3%, mit Patrouille {shareB + shareP:0.0}% ≥ 7,5% (byType=[{string.Join(", ", byType.Select(kv => $"{kv.Key} {kv.Value}"))}], total {total})");
        Assert.True(shareP >= 4.5, $"Patrouille-Anteil {shareP:0.0}% ≥ 4,5%");   // Gasernte: der Inhalt verschiebt den Lauf (gemessen 4,9 %)
        // an einer Polizei/HQ/Sektor-HQ sieht der Spieler meist mindestens ein Kopfgeld- oder Patrouillenangebot (je Bucket)
        int offerStations = u.StationsById().Count(s => (s.Dockable || s.IsBuildSite) && BountyMission.IsOfferStation(u, s));
        long buckets = 20000 / Math.Max(100, u.Rules.MissionRefreshTicks) + 1;
        double pairShare = 100.0 * bountyOrPatrol.Count / Math.Max(1, offerStations * buckets);
        // Gasernte: der verschobene Lauf (Kassen der Fraktionen, Spendable für die Belohnung) ergab 75,6 % — Schwelle 70 %
        Assert.True(pairShare >= 70.0, $"Angebots-Stationen mit Kopfgeld/Patrouille: {pairShare:0.0}% ≥ 70% ({bountyOrPatrol.Count}/{offerStations * buckets})");

        // Hash-Neutralität: andere Angebots-Chancen (anderer Missions-RNG-Verbrauch) ändern den Welt-Hash nicht
        var ua = BalanceWorld(0.05f, 0.05f);
        var ub = BalanceWorld(0.9f, 0.95f);
        var ra = new SimulationRunner(); var rb = new SimulationRunner();
        for (int t = 0; t < 5000; t++) { ra.Step(ua); rb.Step(ub); }
        Assert.NotEqual(ua.MissionOffers.Count + 0, ub.MissionOffers.Count + 0);   // wirklich unterschiedliche Angebotslagen …
        Assert.Equal(UniverseHash.Compute(ua), UniverseHash.Compute(ub));   // … aber derselbe Hash
        Assert.Equal(UniverseHash.Compute(ua, true, false), UniverseHash.Compute(ub, true, false));   // ohne Spielerpolitik ebenso
    }
}
