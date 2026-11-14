using System.Globalization;
using System.Text;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

/// <summary>
/// Runde 25/26: <c>--probe hinweis</c> — Güte der Abnehmer-Auskunft im Messaufbau des Geräts: der Spieler kauft 10 Einheiten beim billigsten Verkäufer
/// (und bei einem zufälligen), der Editor nennt den BESTEN Abnehmer (<see cref="MarketInfo.HonestBuyers"/>, wie die Handelsansicht), die Fahrt dauert die echte
/// Flugzeit des Spielers per Autopilot (<see cref="PlayerTrip"/>), ab Tick 0 (Frühphase getrennt). Bei Ankunft wird gemessen, was der Verkauf der Menge
/// dann brächte (ohne zu verkaufen) und wie viel NPC-Händler unterwegs an diese Station lieferten. Nur lesen — der Lauf bleibt bitgleich.
/// </summary>
sealed class HintProbe
{
    sealed class Trip
    {
        public long Start, Due; public StationId Seller, Buyer; public GoodId Good;
        public float Qty, Buy, Now, Arr, Cons, Fast, Slow, Base, Incoming, Secs, EstTicks;
        public float NpcDelivered, Amount, Target, Jump, HistLo; public string Kind = ""; public TripVerdict Verdict;
        public float Actual, ActualUnits, DepCash, DepAccept, ArrCash, ArrAccept;
    }
    readonly List<Trip> _open = new(), _done = new();
    readonly PriceMemory _mem = new();
    readonly Random _rng = new(26);
    public const int EveryEarly = 40, EveryLate = 200, EarlyTicks = 2000;
    public const float Qty = 10f;

    public HintProbe(Universe u)
    {
        u.Events.Raised += e =>
        {
            if (e is TradeCompleted tc) foreach (var t in _open) if (t.Buyer == tc.To && t.Good == tc.Good) t.NpcDelivered += tc.Amount;
            if (e is MineDelivered md) foreach (var t in _open) if (t.Buyer == md.To && t.Good == md.Good) t.NpcDelivered += md.Amount;
        };
    }

    // Runde 27: Preissprünge je Minute (240 Ticks = 1 min bei ×1): Anteil der Markteinträge, deren Preis sich in einer Minute mehr als verdoppelt bzw. halbiert
    readonly Dictionary<(int, int), float> _snap = new();
    readonly int[] _jumpN = new int[2], _jumpBig = new int[2];
    readonly Dictionary<string, float> _jumpMax = new();
    void Jumps(Universe u)
    {
        int ph = u.Tick < EarlyTicks ? 0 : 1;
        foreach (var st in u.Stations.Values)
            foreach (var (gid, e) in st.Market)
            {
                if (e.Price <= 0f || st.IsBuildSite) continue;
                var k = (st.Id.Value, gid.Value);
                if (_snap.TryGetValue(k, out var old) && old > 0f)
                {
                    float r = MathF.Max(e.Price / old, old / e.Price);
                    _jumpN[ph]++; if (r > 2f) _jumpBig[ph]++;
                    string key = u.Goods[gid].Key;
                    if (key is "water" or "wafers" or "copper" or "energy" && r > _jumpMax.GetValueOrDefault(key)) _jumpMax[key] = r;
                }
                _snap[k] = e.Price;
            }
    }

    public void Observe(Universe u) => _mem.Observe(u);

    public void Sample(Universe u)
    {
        if (u.Tick % 240 == 0) Jumps(u);
        for (int i = _open.Count - 1; i >= 0; i--)
        {
            var t = _open[i];
            if (t.Due > u.Tick) continue;
            _open.RemoveAt(i);
            if (!u.Stations.TryGetValue(t.Buyer, out var st) || !st.Market.TryGetValue(t.Good, out var e)) continue;
            var (cap, allCap) = TradeActions.SaleCaps(u, st, t.Good, st.Credits);
            t.ActualUnits = e.Price > 0f ? TradeActions.SaleUnits(t.Qty, t.Qty, cap, allCap) : 0f;
            t.Actual = e.Price; t.ArrCash = st.Credits; t.ArrAccept = st.AcceptableUnits(t.Good, u.Rules);
            _done.Add(t);
        }
        int every = u.Tick < EarlyTicks ? EveryEarly : EveryLate;
        if (u.Tick % every != 0) return;
        foreach (var g in u.Goods.Values.Where(g => g.Category is "ore" or "refined" or "component" or "end").OrderBy(g => g.Id.Value))
        {
            var sellers = u.StationsById().Where(st => !st.IsBuildSite && !ExportRules.Buys(u, st, g.Id) && st.Market.TryGetValue(g.Id, out var e)
                && e.Amount >= Qty && e.Price > 0f && st.Dockable).ToList();
            if (sellers.Count == 0) continue;
            var cheapest = sellers.OrderBy(s => s.Market[g.Id].Price).ThenBy(s => s.Id.Value).First();
            Add(u, g, cheapest, "billigst");
            var rnd = sellers[_rng.Next(sellers.Count)];
            if (rnd != cheapest) Add(u, g, rnd, "zufällig");
        }
    }

    void Add(Universe u, Good g, Station seller, string kind)
    {
        var dock = seller.DockPoint(u.PlayerShip);
        var list = MarketInfo.HonestBuyers(u, g.Id, Qty, seller.Sector, dock, seller.Id, u.PlayerShip, _mem);
        if (list.Count == 0) return;
        var b = list[0];
        float secs = PlayerTrip.SecondsTo(u, seller.Sector, dock, b.Station);
        if (float.IsInfinity(secs)) return;
        _open.Add(new Trip
        {
            Start = u.Tick, Due = u.Tick + (long)MathF.Ceiling(secs * SimClock.TicksPerSecond), Seller = seller.Id, Buyer = b.Station.Id, Good = g.Id, Qty = b.Units,
            Buy = seller.Market[g.Id].Price, Now = b.PriceNow, Arr = b.PriceArrival,
            Cons = b.Price, Fast = b.PriceHigh, Slow = b.HasHistory ? 1f : 0f, Base = g.BasePrice, Incoming = b.Incoming, Secs = secs, EstTicks = b.TravelTicks, Kind = kind, Verdict = MarketInfo.Verdict(b, seller.Market[g.Id].Price, b.Units), Amount = b.Station.Market[g.Id].Amount, Jump = _mem.MaxDelivery(b.Station.Id, g.Id), HistLo = b.HistoryLow, Target = b.Station.Market[g.Id].Target,
            DepCash = b.Station.Credits, DepAccept = b.Station.AcceptableUnits(g.Id, u.Rules) - b.Incoming,
        });
    }

    static double Q(List<double> l, double p) { if (l.Count == 0) return double.NaN; var s = l.OrderBy(x => x).ToList(); return s[(int)Math.Clamp(Math.Round(p * (s.Count - 1)), 0, s.Count - 1)]; }

    public string Report(Universe u, string? csv)
    {
        var ci = CultureInfo.GetCultureInfo("de-DE");
        var sb = new StringBuilder("HINWEISGÜTE (Spieler kauft 10 E., bester Abnehmer wie im Editor, echte Flugzeit; Fehler = (Schätzung − tatsächlich) ÷ Schätzung, + = zu optimistisch;\n"
            + "  „grün, aber Verlust“ = Schätzung zeigt Gewinn, die Fahrt bringt Verlust — Anteil an den grün gezeigten)\n");
        foreach (var (phase, sel) in new[] { ("früh (< 2000 Ticks)", (Func<Trip, bool>)(t => t.Start < EarlyTicks)), ("spät", t => t.Start >= EarlyTicks) })
        {
            var ts = _done.Where(sel).ToList();
            sb.AppendLine($" {phase}: {ts.Count} Fahrten, Flugzeit Median {Q(ts.Select(t => (double)t.Secs).ToList(), 0.5).ToString("0", ci)} s (NPC-Schätzung {Q(ts.Select(t => (double)t.EstTicks / SimClock.TicksPerSecond).ToList(), 0.5).ToString("0", ci)} s), "
                + $"NPC-Lieferungen an den Abnehmer unterwegs Median {Q(ts.Select(t => (double)t.NpcDelivered).ToList(), 0.5).ToString("0", ci)} E. (Anteil mit Lieferung {(ts.Count == 0 ? 0 : 100.0 * ts.Count(t => t.NpcDelivered > 0) / ts.Count).ToString("0", ci)} %)");
            foreach (var (name, f) in new (string, Func<Trip, float>)[] { ("heutiger Preis", t => t.Now), ("Ankunft (Runde 25)", t => t.Arr), ("schlechter Fall (R. 27)", t => t.Cons) })
            {
                var err = ts.Select(t => (double)((f(t) - t.Actual) / MathF.Max(0.01f, f(t)))).ToList();
                var green = ts.Where(t => (f(t) - t.Buy) * t.Qty > 0.5f).ToList();
                int greenLoss = green.Count(t => (t.Actual * t.ActualUnits - t.Buy * t.Qty) < 0f);
                sb.AppendLine($"   {name,-22} Median {(100 * Q(err, 0.5)).ToString("0.0", ci),6} %  90-%-Q. {(100 * Q(err, 0.9)).ToString("0.0", ci),6} %  zu optimistisch (>5 %) {(ts.Count == 0 ? 0 : 100.0 * err.Count(x => x > 0.05) / ts.Count).ToString("0", ci),3} %  "
                    + $"grün {green.Count,4}, davon Verlust {(green.Count == 0 ? 0 : 100.0 * greenLoss / green.Count).ToString("0", ci),3} %");
            }
            {
                var green = ts.Where(t => t.Verdict == TripVerdict.Good).ToList();
                int greenLoss = green.Count(t => (t.Actual * t.ActualUnits - t.Buy * t.Qty) < 0f);
                var good = ts.Where(t => t.Actual * t.ActualUnits - t.Buy * t.Qty > 0f).ToList();
                string Sh(TripVerdict v) => (good.Count == 0 ? 0 : 100.0 * good.Count(t => t.Verdict == v) / good.Count).ToString("0", ci);
                sb.AppendLine($"   tatsächlich lohnende Fahrten {good.Count}: grün {Sh(TripVerdict.Good)} %, gelb {Sh(TripVerdict.Unsure)} %, rot {Sh(TripVerdict.Loss)} %; Anteil grün an allen Fahrten {(ts.Count == 0 ? 0 : 100.0 * green.Count / ts.Count).ToString("0.0", ci)} %");
                sb.AppendLine($"   Ampel (Runde 27): grün {green.Count}, davon Verlust {(green.Count == 0 ? 0 : 100.0 * greenLoss / green.Count).ToString("0", ci)} %; gelb {ts.Count(t => t.Verdict == TripVerdict.Unsure)}, rot {ts.Count(t => t.Verdict == TripVerdict.Loss)}; "
                    + $"Gewinn der grünen Fahrten {green.Sum(t => t.Actual * t.ActualUnits - t.Buy * t.Qty).ToString("0", ci)} Cr");
            }
        }
        sb.AppendLine($" Preissprünge je Minute (240 Ticks): Einträge mit mehr als ×2 bzw. ÷2 früh {(_jumpN[0] == 0 ? 0 : 100.0 * _jumpBig[0] / _jumpN[0]).ToString("0.0", ci)} %, spät {(_jumpN[1] == 0 ? 0 : 100.0 * _jumpBig[1] / _jumpN[1]).ToString("0.0", ci)} %; größter Sprung "
            + string.Join(", ", _jumpMax.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} ×{kv.Value.ToString("0.0", ci)}")));
        if (csv is not null)
        {
            var c = new StringBuilder("start;secs;good;kind;seller;buyer;qty;buy;now;arr;lo;hi;history;base;incoming;npcdelivered;actual;actualunits;depcash;depaccept;arrcash;arraccept;verdict;amount;target;jump;histlo\n");
            foreach (var t in _done)
                c.AppendLine(string.Join(";", t.Start, t.Secs.ToString("0.0", CultureInfo.InvariantCulture), u.Goods[t.Good].Key, t.Kind, t.Seller.Value, t.Buyer.Value,
                    t.Qty.ToString(CultureInfo.InvariantCulture), F(t.Buy), F(t.Now), F(t.Arr), F(t.Cons), F(t.Fast), F(t.Slow), F(t.Base), F(t.Incoming), F(t.NpcDelivered), F(t.Actual), F(t.ActualUnits), F(t.DepCash), F(t.DepAccept), F(t.ArrCash), F(t.ArrAccept), t.Verdict, F(t.Amount), F(t.Target), F(t.Jump), F(t.HistLo)));
            File.WriteAllText(csv, c.ToString());
        }
        return sb.ToString();
        static string F(float x) => x.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
