using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Missions;

/// <summary>
/// Runde 41: Auftragsart <b>Lieferung</b> — eine Station mit knapper Ware (Bestand &lt; <see cref="EconomyRules.MissionScarceShare"/> des
/// Ziels → Liefervertrag über Comms mit Kassen-Reservierung und Pfand; bis <see cref="EconomyRules.MissionMildScarceShare"/> →
/// normales Angebot am Dock) oder eine Baustelle mit fehlendem Material bietet an: „Liefere n Einheiten Ware bis Tick T an diese
/// Station.“ Erfüllt, wenn netto n Einheiten dort verkauft wurden (Verkauf minus Einkauf derselben Ware an derselben Station seit
/// Annahme — Kaufen-und-zurückverkaufen zählt nicht). Die Ware zahlt die Station zum normalen Marktpreis (Handel bleibt, wie er
/// ist); der Bonus (<see cref="EconomyRules.MissionBonusShare"/> des Basiswerts) kommt aus der Kasse der Auftraggeber-Fraktion.
/// Liefervertrag (PLAN 2.3): kein Bonus, dafür reserviert die Station Annahme und Kasse für n Einheiten zum Festpreis
/// (schlechter Fall; einmal +5 % nachverhandelbar, <see cref="MissionRules.Haggle"/>), Pfand <see cref="EconomyRules.ContractDepositShare"/>
/// des Vertragswerts.
/// </summary>
public sealed class DeliveryMission : IMissionType
{
    public const string KeyId = "delivery";
    public string Key => KeyId;

    static bool Contract(Universe u, MissionOffer o) => o.Args.TryGetValue("contract", out var c) && c == "1";
    static bool Channel(Universe u, MissionOffer o, string ch) => o.Args.TryGetValue("channel", out var c) && c == ch;

    /// <summary>Gute Id des Angebots/Auftrags (Args „good“).</summary>
    public static GoodId GoodOf(MissionOffer o) => new(int.Parse(o.Args["good"]));
    /// <summary>Gelieferte Soll-Menge (Args „units“).</summary>
    public static float UnitsOf(Universe u, MissionOffer o) => float.Parse(o.Args["units"], System.Globalization.CultureInfo.InvariantCulture);
    /// <summary>Festpreis des Liefervertrags (Args „price“; 0 = keiner).</summary>
    public static float PriceOf(MissionOffer o) => o.Args.TryGetValue("price", out var p) ? float.Parse(p, System.Globalization.CultureInfo.InvariantCulture) : 0f;

    public MissionOffer? Generate(Universe u, Station st, DeterministicRng rng, long bucket)
    {
        var r = u.Rules;
        if (MissionRules.Forbidden(u, st.Owner) || MissionRules.Blocked(u, st.Owner.Value)) return null;
        // Kandidaten: Baustelle mit fehlendem Material oder knappes Marktgut
        List<(GoodId Good, float Need, float Ratio)>? candidates = null;
        if (st.IsBuildSite && st.Site is { } site)
        {
            foreach (var (g, req) in site.Required.OrderBy(kv => kv.Key.Value))
            {
                if (!st.Market.TryGetValue(g, out var e) || e.Price <= 0f) continue;
                float need = req - e.Amount;
                if (need >= 1f) (candidates ??= new()).Add((g, need, 0f));
            }
        }
        if (candidates is null || candidates.Count == 0)
        {
            foreach (var (g, e) in st.Market.OrderBy(kv => kv.Key.Value))
            {
                if (e.Price <= 0f || st.AcceptableUnits(g, r) <= 0f) continue;
                // Runde 42b: Verträge nur für Waren, die die Station als Zutat oder Regal wirklich annimmt (nicht ihr reines Produkt o. Ä.)
                if (!st.Consumes(g) && !st.IsStockOnly(g)) continue;
                float ratio = e.Amount / MathF.Max(1f, e.Target);
                if (ratio < r.MissionMildScarceShare) (candidates ??= new()).Add((g, MathF.Max(1f, e.Target - e.Amount), ratio));
            }
        }
        if (candidates is null || candidates.Count == 0) return null;
        // einer je Station und Kanal: knappestes Gut als Vertrag (Comms), sonst Dock-Angebot
        candidates.Sort((a, b) => a.Ratio.CompareTo(b.Ratio));
        var pick = candidates[0];
        bool contract = pick.Ratio < r.MissionScarceShare && !st.IsBuildSite;
        // die Station muss die Menge auch annehmen (Overstock-Deckel) — sonst wäre der Auftrag nie erfüllbar
        float acceptable = st.AcceptableUnits(pick.Good, r);
        float units = Math.Clamp(MathF.Ceiling(MathF.Min(pick.Need, MathF.Max(acceptable, 1f))), 1f, r.MissionMaxUnits);
        if (units < 1f) return null;
        var o = new MissionOffer { Type = KeyId };
        o.Args["good"] = pick.Good.Value.ToString();
        o.Args["units"] = units.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        o.Args["target"] = st.Id.Value.ToString();
        if (contract)
        {
            o.Args["contract"] = "1";
            o.Args["channel"] = "comms";
            float price = st.Market[pick.Good].Price;
            o.Args["price"] = price.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            o.Deposit = r.ContractDepositShare * units * price;
            // Kassen-Reservierung muss möglich sein
            if (st.Credits < units * price) return null;
        }
        else
        {
            o.Args["contract"] = "0";
            o.Args["channel"] = "station";
            float reward = r.MissionBonusShare * units * u.GoodOf(pick.Good).BasePrice;
            o.Reward = MathF.Round(reward);
            if (o.Reward > 0f)
            {
                if (!u.Actors.TryGetValue(st.Owner, out var a)) return null;
                if (MissionRules.SpentToday(u, st.Owner.Value) + o.Reward > MissionRules.BudgetOf(u, a) + 0.0001f) return null;
                if (FactionViability.Spendable(u, a) < o.Reward - 0.0001f) return null;
            }
        }
        return o;
    }

    public string? AcceptIssue(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        if (!u.Stations.TryGetValue(o.At, out var st)) return "Station existiert nicht mehr.";
        // die Station muss die Ware jetzt noch handeln und annehmen — sonst wäre der Auftrag nicht erfüllbar
        if (o.Args.TryGetValue("good", out var gs) && int.TryParse(gs, out var gv))
        {
            var good = new GoodId(gv);
            if (!st.Market.TryGetValue(good, out var e) || e.Price <= 0f) return "Die Station handelt diese Ware gerade nicht mehr.";
            if (!Contract(u, o))
            {
                float want = UnitsOf(u, o);
                if (st.AcceptableUnits(e, u.Rules) < want - 0.0001f) return "Die Station nimmt so viel dieser Ware gerade nicht mehr an.";
            }
        }
        if (Contract(u, o))
        {
            float units = UnitsOf(u, o), price = PriceOf(o);
            if (st.Credits < units * price - 0.0001f) return "Die Station kann die Kasse nicht mehr reservieren.";
        }
        return null;
    }

    public Mission Accept(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        var st = u.Stations[o.At];
        var m = new Mission
        {
            Type = KeyId, Client = o.Client, At = o.At,
            Args = new Dictionary<string, string>(o.Args),
            Deadline = MissionRules.DeadlineFrom(u, ship, st),
        };
        if (Contract(u, o))
        {
            // Reservierung von Annahme und Kasse: n × Festpreis held die Station bis zum Abschluss
            float cash = UnitsOf(u, o) * PriceOf(o);
            st.Credits -= cash;
            m.ReservedCash = cash;
        }
        return m;
    }

    /// <summary>Fortschritt: Verkäufe der Ware an der Zielstation zählen, Käufe derselben Ware dort mindern (netto).</summary>
    public void OnEvent(Universe u, Mission m, SimEvent e)
    {
        if (m.Args.TryGetValue("good", out var gs) is false || !int.TryParse(gs, out var gv)) return;
        var good = new GoodId(gv);
        switch (e)
        {
            case PlayerSold ps when ps.Station == m.At && ps.Good == good:
                m.Sold += ps.Units; break;
            case PlayerBought pb when pb.Station == m.At && pb.Good == good:
                m.Bought += pb.Units; break;
            case TraderBought tb when tb.Station == m.At && tb.Good == good:
                m.Bought += tb.Units; break;   // Flottenschiff des Spielers kaufte dort
        }
    }

    public bool TargetGone(Universe u, Mission m) => !u.Stations.ContainsKey(m.At);

    public (SectorId Sector, Vec2 Pos, string Label, MissionMarkerKind Kind)? Marker(Universe u, Mission m)
    {
        if (!u.Stations.TryGetValue(m.At, out var st)) return null;
        return (st.Sector, st.Position, Describe(u, m), MissionMarkerKind.Station);
    }

    public string TargetText(Universe u, MissionOffer o) =>
        u.Stations.TryGetValue(o.At, out var st) ? st.Name : "";

    public long DeadlineOf(Universe u, MissionOffer o, Ship ship) =>
        u.Stations.TryGetValue(o.At, out var st) ? MissionRules.DeadlineFrom(u, ship, st) - u.Tick : u.Rules.MissionMinDeadlineTicks;

    public string DescribeOffer(Universe u, MissionOffer o)
    {
        var st = u.Stations[o.At];
        string g = u.GoodOf(GoodOf(o)).Name;
        float units = UnitsOf(u, o);
        string base_ = Contract(u, o)
            ? $"Liefervertrag: {units:0.#} {g} an {st.Name} zum Festpreis {PriceOf(o):0.0} Cr — Annahme und Kasse reserviert, Frist {MissionRules.Rest(u.Rules.MissionOfferTicks)}, Pfand {o.Deposit:0} Cr."
            : $"Lieferung: {units:0.#} {g} an {st.Name} — Belohnung {o.Reward:0} Cr, Frist {MissionRules.Rest(MissionRules.DeadlineFrom(u, u.Ships[u.PlayerShip], st) - u.Tick)}.";
        return base_;
    }

    public string Describe(Universe u, Mission m)
    {
        if (!u.Stations.TryGetValue(m.At, out var st)) return $"Auftrag #{m.Id}: Ziel weg";
        float units = float.Parse(m.Args["units"], System.Globalization.CultureInfo.InvariantCulture);
        string g = u.GoodOf(new GoodId(int.Parse(m.Args["good"]))).Name;
        long left = Math.Max(0L, m.Deadline - u.Tick);
        return $"Auftrag: {m.Progress:0.#}/{units:0.#} {g} → {st.Name} · noch {MissionRules.Rest(left)}";
    }

    public void Check(Universe u, Mission m)
    {
        if (u.Tick >= m.Deadline) { MissionRules.Fail(u, m, "Frist abgelaufen"); return; }
        if (TargetGone(u, m)) { MissionRules.Fail(u, m, "Zielstation weg"); return; }
        float units = float.Parse(m.Args["units"], System.Globalization.CultureInfo.InvariantCulture);
        if (m.Progress >= units - 0.0001f) MissionRules.Complete(u, m);
    }

    public string? DeliverIssue(Universe u, Mission m, Ship ship, Station st) => "Lieferaufträge erfüllen sich durch den Verkauf an der Station.";
    public void Deliver(Universe u, Mission m, Ship ship, Station st) => throw new InvalidOperationException("Lieferung braucht keine Ablieferung.");
}
