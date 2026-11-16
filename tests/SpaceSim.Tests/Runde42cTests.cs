using SpaceSim.Core.Comms;
using SpaceSim.Core.Events;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Runde 42c (Handytest-Restmängel): (1) Posteingang — Zeiten werden beim Anzeigen gerechnet (Rest/„vor …“, nicht beim Erzeugen),
/// abgelaufene/unbekannte Angebote klar „abgelaufen“ mit leeren Antworten; (2) angenommene Angebote werden nicht erneut erzeugt
/// (dasselbe Bucket, Andock-Station sofort) und sind nach der Annahme aus der Liste; (3) Lieferangebote nie über der
/// annehmbaren Menge (kein „nicht annehmbar“ direkt nach der Erzeugung); (4) Vertragsvorschau: Festpreis × Menge, Pfand,
/// genau eine Frist, Ziel, geschätzte Flugzeit; (5) Fristen aus der realen Spielerflugzeit (PlayerTrip bzw. Sprünge × Fallback)
/// mit Faktor ≥ 2 und Mindestfrist. Hash-Neutralität: Angebote/Meldungen verbrauchen kein <see cref="Universe.Rng"/>.
/// </summary>
public class Runde42cTests
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

    static MissionOffer TransportOffer(Universe u, Func<MissionOffer, bool>? extra = null) =>
        FindOffer(u, o => o.Type == TransportMission.KeyId && MissionRules.AcceptIssue(u, o.Id, Player(u), remote: true) is null && (extra is null || extra(o)))
        ?? throw new Xunit.Sdk.XunitException("kein annehmbares Transportangebot gefunden");

    static MissionOffer ContractOffer(Universe u) =>
        FindOffer(u, o => o.Type == DeliveryMission.KeyId && o.Args.TryGetValue("contract", out var c) && c == "1")
        ?? throw new Xunit.Sdk.XunitException("kein Liefervertrag gefunden");

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

    // ───────── 1. Angenommene Angebote nicht erneut angeboten ─────────

    [Fact]
    public void Annahme_EntferntAngebotUndEsWirdNichtNeuErzeugt_SolangeAuftragLaeuft()
    {
        var u = World(101);
        var o = TransportOffer(u);
        var at = o.At;
        var target = TransportMission.TargetOf(o);
        int id = o.Id;
        float volume = TransportMission.VolumeOf(o);
        var m = AcceptDock(u, o);
        // aus der Liste entfernt
        Assert.DoesNotContain(u.MissionOffers, x => x.Id == id);
        // selbes Bucket: Andock-Station erzeugt sofort neu — das angenommene Angebot darf nicht wieder auftauchen
        Run(u, 100);
        Run(u, 100);
        Assert.DoesNotContain(u.MissionOffers, x => x.At == at && x.Type == TransportMission.KeyId
            && x.Args.TryGetValue("target", out var t) && t == target.Value.ToString()
            && x.Args.TryGetValue("volume", out var v) && MathF.Abs(float.Parse(v, System.Globalization.CultureInfo.InvariantCulture) - volume) < 0.01f);
        Assert.Contains(u.Missions, x => x.Id == m.Id && x.State == MissionState.Active);
        // Bucket-Wechsel: exakt dasselbe Angebot (gleiche Args) darf ebenfalls nicht neu entstehen, solange der Auftrag läuft
        // (Frist künstlich verlängert, damit der Wechsel die Mission nicht vorher ablaufen lässt)
        m.Deadline += 6000;
        long bucket0 = u.Tick / Math.Max(100, u.Rules.MissionRefreshTicks);
        while (u.Tick / Math.Max(100, u.Rules.MissionRefreshTicks) == bucket0) Run(u, 100);
        Assert.DoesNotContain(u.MissionOffers, x => x.At == at && x.Type == TransportMission.KeyId
            && x.Args.TryGetValue("target", out var t) && t == target.Value.ToString()
            && x.Args.TryGetValue("volume", out var v) && MathF.Abs(float.Parse(v, System.Globalization.CultureInfo.InvariantCulture) - volume) < 0.01f);
        Assert.Contains(u.Missions, x => x.Id == m.Id && x.State == MissionState.Active);
        // nach Abbruch ist der Auftrag weg (die Station darf danach wieder anbieten)
        MissionRules.Abandon(u, m.Id, Player(u));
        Assert.DoesNotContain(u.Missions, x => x.Id == m.Id && x.State == MissionState.Active);
    }

    // ───────── 2. Lieferangebote nie über der annehmbaren Menge ─────────

    [Fact]
    public void Lieferangebot_MengeLiegtNieUeberDerAnnehmbarenMenge()
    {
        // gezwungen: knappestes Gut mit annehmbarer Menge < 1 (Overstock < 1) → gar kein Angebot
        // (alter Code rundete auf 1 Einheit auf → Angebot sofort „nicht annehmbar“, s. Handytest a05)
        var u = World(111);
        var st = u.StationsById().First(s => !s.IsBuildSite && s.Market.Count > 2);
        GoodId scarce = default;
        foreach (var (g, e) in st.Market.OrderBy(kv => kv.Key.Value))
        {
            if (e.Price <= 0f || (!st.Consumes(g) && !st.IsStockOnly(g))) { e.Amount = e.Target; continue; }
            if (scarce == default)
            {
                scarce = g;
                e.Target = 10f; e.Overstock = 0.65f; e.Amount = 5.9f; e.Price = MathF.Max(e.Price, 5f);   // ratio 0,59 < 0,6, annehmbar 0,6
            }
            else e.Amount = e.Target;   // ratio 1 → kein Kandidat
        }
        Assert.True(st.AcceptableUnits(scarce, u.Rules) is > 0f and < 1f, $"Kontrolle: annehmbare Menge < 1 ({st.AcceptableUnits(scarce, u.Rules):0.##})");
        var rng = MissionRng.For(u, st.Id.Value, 0);
        var o = new DeliveryMission().Generate(u, st, rng, 0);
        Assert.True(o is null || DeliveryMission.GoodOf(o) != scarce, "kein Angebot mit Menge über der annehmbaren Menge");

        // Scan über Welten, geprüft beim Erzeugen (Event MissionOffered): jedes Lieferangebot (Dock) liegt in der annehmbaren Menge
        for (ulong seed = 121; seed < 127; seed++)
        {
            var w = World(seed);
            var seen = new List<(MissionOffer O, float Acceptable)>();
            w.Events.Raised += e =>
            {
                if (e is MissionOffered mo && mo.Type == DeliveryMission.KeyId)
                {
                    var x = w.MissionOffers.FirstOrDefault(y => y.Id == mo.Offer);
                    if (x is not null && !(x.Args.TryGetValue("contract", out var c) && c == "1"))
                        seen.Add((x, w.Stations[x.At].AcceptableUnits(DeliveryMission.GoodOf(x), w.Rules)));
                }
            };
            Run(w, 5000);   // zwei Bucket-Wechsel
            Assert.NotEmpty(seen);
            foreach (var (x, acc) in seen)
                Assert.True(acc >= DeliveryMission.UnitsOf(w, x) - 0.001f,
                    $"Angebot {DeliveryMission.UnitsOf(w, x):0.#} Einheiten liegt über der annehmbaren Menge ({acc:0.##}) an {w.Stations[x.At].Name}");
        }
    }

    // ───────── 3. Vertragsvorschau: Festpreis, Pfand, eine Frist, Flugzeit ─────────

    [Fact]
    public void Vertrag_Vorschau_EineFristFestpreisPfandFlug_OhneBelohnung0()
    {
        var u = World(131);
        var co = ContractOffer(u);
        var ship = Player(u);
        ship.Position = u.Stations[co.At].DockPoint(ship.Id);
        // Titel des Angebots: keine „Frist“ mehr (die Angebots-Lebensdauer steht in der Posteingangszeile)
        var title = MissionRules.DescribeOfferText(u, co);
        Assert.DoesNotContain("Frist", title);
        Assert.Contains("Festpreis", title);
        Assert.Contains("Pfand", title);
        // Vorschauzeile der Comms-Aktion: Festpreis × Menge (reserviert), Pfand, genau eine Frist, Ziel, Flugzeit
        string line = MissionAcceptAction.PreviewLine(u, co, ship, null);
        Assert.Equal(1, Count(line, "Frist"));
        Assert.Contains("Festpreis", line);
        Assert.Contains("Cr (Kasse reserviert)", line);
        Assert.Contains("Pfand", line);
        Assert.Contains("Ziel", line);
        Assert.Contains("Flug ~", line);
        Assert.DoesNotContain("Belohnung 0", line);
        // Registry-Vorschau liefert dieselbe Zeile
        var prev = CommsActions.Default().Preview(u, $"mission:accept:{co.Id}");
        Assert.Contains("Frist", prev.Preview);
        Assert.Equal(1, Count(prev.Preview, "Frist"));
    }

    static int Count(string s, string what)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
        return n;
    }

    // ───────── 4. Fristen aus der realen Spielerflugzeit ─────────

    [Fact]
    public void Frist_TransportMehrereSpruenge_AusRealerFlugzeit_MitFaktorUndMinimum()
    {
        // Transport über mindestens 3 Sprünge suchen
        MissionOffer? far = null;
        Universe? fu = null;
        for (ulong seed = 141; seed < 181 && far is null; seed++)
        {
            var u = World(seed);
            far = FindOffer(u, o => o.Type == TransportMission.KeyId
                && MissionRules.AcceptIssue(u, o.Id, Player(u), remote: true) is null
                && u.Hops(u.Stations[o.At].Sector, u.Stations[TransportMission.TargetOf(o)].Sector) >= 3, rounds: 120);
            if (far is not null) fu = u;
        }
        Assert.NotNull(far);
        var u2 = fu!;
        var ship = Player(u2);
        var target = u2.Stations[TransportMission.TargetOf(far)];
        ship.DockedAt = far.At;
        ship.Sector = u2.Stations[far.At].Sector;   // angedockt: das Schiff liegt im Sektor der Station
        ship.Position = u2.Stations[far.At].DockPoint(ship.Id);
        long trip = MissionRules.TripTicks(u2, ship.Sector, ship.Position, target.Sector, target.DockPoint(ship.Id), ship);
        Assert.True(trip > 0, "Flugzeit-Schätzung positiv (nicht wie früher ≈ 0 beim angedockten Schiff)");
        int hops = u2.Hops(ship.Sector, target.Sector);
        Assert.True(hops >= 3);
        var m = AcceptDock(u2, far);
        long frist = m.Deadline - m.AcceptedTick;
        Assert.True(frist >= Math.Max(u2.Rules.MissionMinDeadlineTicks, trip * 2), $"Frist {frist} ≥ max(min, Flug {trip} × 2) — Faktor ≥ 2");
        Assert.True(frist >= u2.Rules.MissionMinDeadlineTicks, "Mindestfrist eingehalten");
        // Vorschau zeigt die geschätzte Flugzeit und dieselbe Frist
        var type = MissionRules.Types.Get(TransportMission.KeyId)!;
        string prev = type.DescribeOffer(u2, far);
        Assert.Contains("Flugzeit ~", prev);
        Assert.Contains("min", MissionRules.TripText(u2, ship.Sector, ship.Position, target.Sector, target.DockPoint(ship.Id), ship));
        long fristOf = type.DeadlineOf(u2, far, ship);
        Assert.True(Math.Abs(fristOf - frist) <= 20, $"Vorschau-Frist {fristOf} ≈ Annahme-Frist {frist}");
    }

    [Fact]
    public void Frist_AuchKurzeStrecke_MindestensMindestfrist()
    {
        var u = World(191);
        var o = TransportOffer(u, x => u.Hops(u.Stations[x.At].Sector, u.Stations[TransportMission.TargetOf(x)].Sector) <= 1);
        var m = AcceptDock(u, o);
        Assert.True(m.Deadline - m.AcceptedTick >= u.Rules.MissionMinDeadlineTicks, "Mindestfrist auch bei kurzer Strecke");
    }

    // ───────── 5. Posteingang: Zeiten zur Anzeigezeit, abgelaufen markiert, Antworten weg ─────────

    [Fact]
    public void Posteingang_Angebotstext_Abgelaufen_WennAngebotWeg_AntwortenLeer()
    {
        var u = World(201);
        // Vertragsnachricht im Posteingang (Spieler an der Angebots-Station angedockt)
        var co = ContractOffer(u);
        Player(u).DockedAt = co.At;
        Run(u, 40);   // SyncContractOffers zieht die Nachricht
        var msg = u.Inbox.FirstOrDefault(m => m.Kind == "contract_offer" && m.Args.TryGetValue("offer", out var os) && os == co.Id.ToString());
        Assert.NotNull(msg);
        // lebend: Text frisch aus dem Angebot, Antworten vorhanden
        var (s1, b1) = CommsText.Describe(u, msg!);
        Assert.DoesNotContain("Tick", b1);
        Assert.Contains("Festpreis", b1);
        Assert.NotEmpty(CommsText.Answers(u, msg!));
        // Angebot weg (z. B. Bucket-Wechsel hat es ersetzt): klar „abgelaufen“, keine Antworten
        u.MissionOffers.RemoveAll(x => x.Id == co.Id);
        var (s2, b2) = CommsText.Describe(u, msg!);
        Assert.Contains("abgelaufen", b2);
        Assert.Empty(CommsText.Answers(u, msg!));
        // abgelaufen (Expires erreicht): ebenso
        var u2 = World(202);
        var co2 = ContractOffer(u2);
        Player(u2).DockedAt = co2.At;
        Run(u2, 40);
        var msg2 = u2.Inbox.First(m => m.Kind == "contract_offer" && m.Args.TryGetValue("offer", out var os) && os == co2.Id.ToString());
        var o2 = u2.MissionOffers.First(x => x.Id == co2.Id);
        o2.Expires = u2.Tick;   // gerade abgelaufen
        var (_, b3) = CommsText.Describe(u2, msg2);
        Assert.Contains("abgelaufen", b3);
        Assert.Empty(CommsText.Answers(u2, msg2));
    }

    [Fact]
    public void Posteingang_ZeitenWerdenZurAnzeigezeitGerendert_NichtBeimErzeugen()
    {
        var u = World(211);
        // roher Tick im Text → Rest/„vor …“ zur Anzeigezeit
        var m1 = Comms.Add(u, u.PlayerActor, "ceasefire_concluded", new() { ["faction"] = "1", ["until"] = (u.Tick + 240 * 30).ToString(), ["message"] = $"Waffenstillstand zwischen A und B bis Tick {u.Tick + 240 * 30}." }, 0);
        var (_, b1) = CommsText.Describe(u, m1);
        Assert.DoesNotContain("Tick", b1);
        Assert.Contains("bis in 30 min", b1);
        Run(u, 240 * 40);   // 40 min vergangen → Frist abgelaufen
        var (_, b2) = CommsText.Describe(u, m1);
        Assert.Contains("abgelaufen vor 10 min", b2);
        // Runde 42d (Handytest): eine angenommene/erledigte/gescheiterte Meldung trägt KEINE weiterlaufende Restzeit mehr —
        // „ · noch m:ss“ wird gestrichen (nur offene Angebote haben eine Restzeit); die Frist bleibt als Arg „deadline“ gespeichert
        var ship = Player(u);
        var o = TransportOffer(u);
        var m = AcceptDock(u, o);
        var msg = u.Inbox.First(x => x.Kind == "mission_accepted");
        Assert.Equal(m.Deadline.ToString(), msg.Args["deadline"]);
        var (_, ba) = CommsText.Describe(u, msg);
        Assert.DoesNotContain("Tick", ba);
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(ba, @"noch \d+:\d\d"), $"keine Restzeit im Detailtext: {ba}");
        Run(u, 240);   // 1 min vergangen
        var (_, bb) = CommsText.Describe(u, msg);
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(bb, @"noch \d+:\d\d"), $"auch später keine Restzeit: {bb}");
    }
}
