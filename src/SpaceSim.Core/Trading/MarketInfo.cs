using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Trading;

/// <summary>Marktlage einer Ware an einer Station, abgeleitet aus Bestand gegen Wunschbestand (Preisbildung siehe <see cref="Simulation.MarketSystem"/>).</summary>
public enum MarketPressure
{
    /// <summary>Bestand unter Wunschbestand: Nachfrage, der Preis steigt über den Basispreis.</summary>
    Scarce,
    /// <summary>Bestand genau auf Wunschbestand.</summary>
    Balanced,
    /// <summary>Bestand über Wunschbestand: Überschuss, der Preis fällt unter den Basispreis.</summary>
    Glut,
}

/// <summary>Phase 16: warum eine Station eine Ware nicht oder nur begrenzt annimmt.</summary>
public enum IntakeLimit
{
    /// <summary>Keine Begrenzung außer Bestand an Bord (oder es ist nichts an Bord).</summary>
    None,
    /// <summary>Die Station führt die Ware nicht (Exportsenken: nur Endwaren).</summary>
    NotTraded,
    /// <summary>Die Station stellt die Ware selbst her und verbraucht sie nicht (<see cref="StockEntry.NoIntake"/>).</summary>
    OwnProduct,
    /// <summary>Lager bis zur Annahmegrenze gefüllt (Überhang, Regal bis Wunschbestand, Exportlager).</summary>
    Full,
    /// <summary>Die Zutat braucht nur ein pausiertes Rezept (Produkt ohne Abnehmer): Annahme nur bis Wunschbestand, der ist erreicht.</summary>
    Paused,
    /// <summary>Baustelle: nimmt nur die Fehlmenge (oder nichts mehr: verlassen bzw. vollständig).</summary>
    Site,
    /// <summary>Die Station kann (die ganze Menge) nicht bezahlen.</summary>
    Credits,
}

/// <summary>
/// Phase 16: Annahmestatus einer Ware an einer Station. <see cref="Units"/> = was die Station jetzt annimmt und bezahlen kann (ohne Blick auf die
/// Fracht), <see cref="Limit"/>/<see cref="Reason"/> = was begrenzt (Reason kurz und deutsch, null wenn unbegrenzt).
/// </summary>
public readonly record struct Intake(float Units, IntakeLimit Limit, string? Reason)
{
    /// <summary>
    /// Phase 17: ungerundete Obergrenze eines Verkaufs (min(Annahme, Kasse ÷ Preis), wie <see cref="TradeActions.SaleCap"/>); <see cref="Units"/> ist 0,
    /// sobald sie unter einer Einheit liegt. Damit nimmt die Station einen Rest unter einer Einheit an, wenn er ganz hineinpasst.
    /// </summary>
    public float Cap { get; init; }

    /// <summary>Phase 18: Obergrenze für „alles an Bord“ (Annahme + Resttoleranz, bezahlbar; <see cref="TradeActions.SaleCaps"/>). Standard = <see cref="Cap"/>.</summary>
    public float AllCap { get; init; }

    /// <summary>Nimmt die Station überhaupt etwas (mindestens eine Einheit)?</summary>
    public bool Accepts => Units >= 1f;

    /// <summary>Phase 17: was ein Verkauf von <paramref name="held"/> Einheiten hier bewegen würde (<see cref="TradeActions.SaleUnits"/>: alles oder ganze Einheiten).</summary>
    public float SellableFrom(float held) => TradeActions.SaleUnits(held, held, Cap, MathF.Max(Cap, AllCap));

    /// <summary>
    /// Hinweis für eine Verkaufszeile mit <paramref name="held"/> Einheiten an Bord: der Grund, wenn ein Verkauf nichts bewegen würde; bewegt er weniger, als an
    /// Bord ist, der Grund der Begrenzung („Baustelle: nur noch n“, „kann nur n bezahlen“) oder „nimmt noch n“; sonst null. Phase 17: nach derselben
    /// Mengenregel wie der Verkauf (<see cref="SellableFrom"/>), also auch für einen Bruchteil an Bord.
    /// </summary>
    public string? NoteFor(float held)
    {
        float n = SellableFrom(held);
        if (n <= 0f) return Reason ?? (Cap > 0f ? "Nimmt weniger als eine Einheit an" : "Nimmt nichts an");
        if (n < held - 0.001f) return Reason ?? $"nimmt noch {MathF.Floor(Units):0}";
        return null;
    }
}

/// <summary>
/// Reine Core-Funktionen für Marktanzeigen der Oberfläche: beste bekannte Verkaufsstelle je Ware und
/// Nachfrage/Überschuss aus Bestand gegen Ziel. Keine Godot-Abhängigkeit, keine Zustandsänderung.
/// </summary>
public static class MarketInfo
{
    /// <summary>Verhältnis Bestand/Wunschbestand; &lt; 1 = Nachfrage, &gt; 1 = Überschuss (Preisfaktor umgekehrt, siehe MarketSystem).</summary>
    public static float StockRatio(StockEntry e) => e.Target > 0f ? e.Amount / e.Target : 1f;

    /// <summary>Marktlage aus Bestand gegen Wunschbestand.</summary>
    public static MarketPressure Classify(StockEntry e)
    {
        float r = StockRatio(e);
        return r < 1f ? MarketPressure.Scarce : r > 1f ? MarketPressure.Glut : MarketPressure.Balanced;
    }

    /// <summary>
    /// Phase 16: Annahmestatus einer Ware an einer Station — dieselbe Menge wie der Verkauf (<see cref="TradeActions.Sell"/>: min(<see cref="Station.AcceptableUnits"/>,
    /// Kasse ÷ Preis)) und der Grund, wenn sie begrenzt ist: Hersteller nehmen ihr reines Produkt nicht an, Regale nur bis Wunschbestand, Zutaten pausierter
    /// Rezepte nur bis Wunschbestand, Exportsenken nur Endwaren, Baustellen nur die Fehlmenge, und niemand mehr, als er bezahlen kann. Reine Auswertung.
    /// </summary>
    public static Intake Acceptance(Universe u, Station st, GoodId good)
    {
        if (!st.Market.TryGetValue(good, out var e) || e.Price <= 0f)
            return new Intake(0f, IntakeLimit.NotTraded, ExportRules.ConfigOf(u, st) is not null ? "Nimmt nur Endwaren" : "Handelt diese Ware nicht");
        float accept = st.AcceptableUnits(good, u.Rules);
        float pay = MathF.Max(0f, st.Credits) / e.Price;
        float cap = MathF.Max(0f, MathF.Min(pay, accept));   // Phase 17: wie TradeActions.SaleCap
        var (_, allCap) = TradeActions.SaleCaps(u, st, good, st.Credits);   // Phase 18: Rest unter einer Einheit
        return Classify() with { Cap = cap, AllCap = allCap };

        Intake Classify()
        {
            if (st.Site is { } site)
            {
                if (site.Abandoned) return new Intake(0f, IntakeLimit.Site, "Verlassene Baustelle");
                if (accept < 1f) return new Intake(0f, IntakeLimit.Site, accept > 0f ? $"Baustelle: nur noch {accept:0.#}" : "Baustelle: Material vollständig");
                if (pay < accept) return Paid(accept, pay);
                return new Intake(accept, IntakeLimit.Site, $"Baustelle: nur noch {MathF.Floor(accept):0}");
            }
            if (e.NoIntake) return new Intake(0f, IntakeLimit.OwnProduct, "Stellt die Ware selbst her");
            if (accept < 1f)
            {
                bool paused = e.Overstock > 0f && st.Consumes(good) && !st.Produces(good);
                return paused ? new Intake(0f, IntakeLimit.Paused, "Kein Bedarf: Produktion pausiert") : new Intake(0f, IntakeLimit.Full, "Lager voll");
            }
            if (pay < accept) return Paid(accept, pay);
            return new Intake(accept, IntakeLimit.None, null);
        }

        static Intake Paid(float accept, float pay) =>
            pay < 1f ? new Intake(0f, IntakeLimit.Credits, "Station kann nicht zahlen") : new Intake(pay, IntakeLimit.Credits, $"kann nur {MathF.Floor(pay):0} bezahlen");
    }

    /// <summary>
    /// Beste bekannte Verkaufsstelle für eine Ware aus Sicht eines Verkäufers: die Station mit dem höchsten
    /// aktuellen Ankaufspreis unter allen Stationen, die die Ware handeln und jetzt annehmen (Phase 16: <see cref="Acceptance"/> mindestens eine Einheit;
    /// vorher nannte die Anzeige z. B. das Stahlwerk als besten Käufer für Stahl), ohne <paramref name="exclude"/>.
    /// null, wenn außer der Ausschlussstation niemand die Ware kauft.
    /// </summary>
    public static Station? BestBuyerForGood(Universe u, GoodId good, StationId? exclude = null)
    {
        Station? best = null;
        float bestPrice = 0f;
        foreach (var st in u.StationsTrading(good))
        {
            if (st.Id == exclude) continue;
            if (!st.Market.TryGetValue(good, out var e) || e.Price <= 0f) continue;
            if (!Acceptance(u, st, good).Accepts) continue;
            if (e.Price > bestPrice) { bestPrice = e.Price; best = st; }
        }
        return best;
    }

    /// <summary>
    /// Runde 23 (Handy-Test: „bester Abnehmer“ Gamma mit 66,4 genannt, 49,8 gezahlt, nur 3 von 22 verkauft): was andere Händler schon zu dieser Station bringen —
    /// dieselbe Rechnung wie die Planung der Händler (<see cref="TraderSystem"/>, Regel ReserveIncoming): unterwegs zum Verkauf ihre Ladung, unterwegs zum
    /// Einkauf ihr Laderaum für die Ware. Nur lesend.
    /// </summary>
    public static float IncomingTo(Universe u, StationId station, GoodId good, ShipId? except = null)
    {
        float sum = 0f;
        foreach (var o in u.Ships.Values)
            if (o.Id != except && o.Order is TradeOrder io && io.Sell == station && io.Good == good)
                sum += o.Phase == TradePhase.GoingToSell ? o.Cargo.GetValueOrDefault(good) : o.CapacityVolumeFor(u, good) / u.Goods[good].VolumePerUnit;
        return sum;
    }

    /// <summary>
    /// Runde 23: ehrliche Auskunft über einen Abnehmer für <paramref name="qty"/> Einheiten — was eine Lieferung JETZT dort einbrächte: angenommene Menge nach
    /// derselben Verkaufsregel (<see cref="TradeActions.SaleCaps"/>/<see cref="TradeActions.SaleUnits(float, float, float, float)"/>: Annahme, Kasse ÷ Preis)
    /// abzüglich dessen, was andere Händler schon bringen (<see cref="IncomingTo"/>), Erlös = Menge × Preis, dazu Sprünge und Reisezeit vom Standort.
    /// null = die Station nimmt jetzt nichts an oder kann nichts zahlen.
    /// </summary>
    public static BuyerQuote? QuoteBuyer(Universe u, Station st, GoodId good, float qty, SectorId fromSector, Vec2 fromPos, ShipId? self = null, PriceMemory? mem = null)
    {
        if (qty <= 0f || st.IsBuildSite && st.Site!.Abandoned) return null;
        if (!st.Market.TryGetValue(good, out var e) || e.Price <= 0f) return null;
        var intake = Acceptance(u, st, good);
        if (!intake.Accepts) return null;
        float incoming = u.Rules.ReserveIncoming || st.IsBuildSite ? IncomingTo(u, st.Id, good, self) : 0f;
        float accept = MathF.Max(0f, st.AcceptableUnits(good, u.Rules) - incoming);
        var (cap, allCap) = TradeActions.SaleCaps(u, st, good, st.Credits, accept);
        float units = TradeActions.SaleUnits(qty, qty, cap, allCap);
        if (units <= 0f) return null;
        var route = st.Sector == fromSector ? new List<GateId>() : u.FindRoute(fromSector, st.Sector);
        if (route is null) return null;
        float ticks = Travel.Estimate(u, fromSector, fromPos, st.Sector, st.DockPoint(default));
        // Runde 25 (Gerätetest: „zahlt 12,4 · Erwartet +628“, bei Ankunft 4,4 → −221 Cr): abgerechnet wird zum Preis bei Ankunft — ein Preis für die ganze
        // Menge (Verkauf und Händler buchen Menge × Stückpreis des Augenblicks); der Bestand ist dann um das gestiegen, was andere schon hinbringen
        float arrival = ArrivalPrice(u, st, good, incoming);
        // Runde 26 (Gerätetest: 3 von 3 Fahrten 40–70 % unter dem genannten Preis): vorsichtige Schätzung — gemessen (--probe hinweis) kommen die Verluste
        // von Preisspitzen, die NPC-Händler und Schürfer in den nächsten Ticks anfahren (noch nicht unterwegs, also nicht in incoming), und von Abnehmern,
        // deren Kasse bei Ankunft leer ist. Darum: eine Spitze über dem schnellen Mittel der Station wird nicht geglaubt, über dem Basispreis nur zur Hälfte.
        float careful = CarefulPrice(u, st, good, arrival, incoming, ticks * PlayerTimeFactor, mem);
        return new BuyerQuote(st, careful, units, units * careful, incoming, route.Count, ticks, e.Price, arrival,
            st.Credits / MathF.Max(0.01f, e.Price * units), accept / MathF.Max(0.01f, units));
    }

    /// <summary>
    /// Runde 26: vorsichtiger Stückpreis bei Ankunft = min(Preis bei Ankunft nach Bestandsformel, schnelles gleitendes Mittel der Station
    /// (<see cref="PriceMemory"/>, falls bekannt), Mitte zwischen Ankunftspreis und Basispreis), dazu der Zufluss: fließt der Station laufend Ware zu
    /// (Händler, Schürfer — gemessen im Preisgedächtnis), wird er über die Flugzeit fortgeschrieben (Verbrauch nicht abgezogen: vorsichtig). Reine Auskunft.
    /// </summary>
    public static float CarefulPrice(Universe u, Station st, GoodId good, float arrival, float incoming = 0f, float travelTicks = 0f, PriceMemory? mem = null)
    {
        float p = arrival;
        if (mem?.Of(st.Id, good) is { } m) p = MathF.Min(p, m.Fast);
        var g = u.Goods[good];
        if (arrival > g.BasePrice) p = MathF.Min(p, 0.5f * (arrival + g.BasePrice));
        if (mem is not null && travelTicks > 0f && !st.IsBuildSite && st.Market.TryGetValue(good, out var e) && mem.InflowOf(st.Id, good) is var flow and > 0f)
        {
            float before = MarketSystem.StockPrice(g, e, e.Amount + incoming);
            float after = MarketSystem.StockPrice(g, e, e.Amount + incoming + flow * travelTicks);
            if (before > 0f) p *= MathF.Min(1f, after / before);
        }
        return p;
    }

    /// <summary>Runde 26: Flugzeit des Spielers per Autopilot ÷ NPC-Schätzung (<see cref="Travel.Estimate"/>), gemessen mit <c>--probe hinweis</c> (Median 23 ÷ 12 s).</summary>
    public const float PlayerTimeFactor = 1.9f;

    /// <summary>Runde 26: Mindestabstand des vorsichtigen Erlöses über dem Einkauf für „grün“ (Anteil des Einkaufs).</summary>
    public const float GreenMargin = 0.15f;
    /// <summary>Runde 26: so viel mal die Ladung muss die Kasse bzw. die Annahme des Abnehmers decken, sonst „unsicher“.</summary>
    public const float GreenCover = 2f;

    /// <summary>
    /// Runde 26: ehrliche Ampel für eine Fahrt (Einkauf <paramref name="buyPrice"/> je Einheit, <paramref name="units"/> Einheiten): grün nur, wenn auch die
    /// vorsichtige Schätzung <see cref="GreenMargin"/> über dem Einkauf liegt und Kasse und Annahme des Abnehmers die Ladung <see cref="GreenCover"/>-mal decken;
    /// rot, wenn schon die vorsichtige Schätzung Verlust zeigt oder der Abnehmer nicht alles nimmt; sonst gelb („unsicher“).
    /// Gemessen (--probe hinweis, Start „Händler“, Seeds 11–15, 8000 Ticks): „grün, aber Verlust“ 30 % → 14 % (früh) bzw. 25 % → 12 % (spät).
    /// </summary>
    public static TripVerdict Verdict(BuyerQuote q, float buyPrice, float units)
    {
        float result = q.Revenue - buyPrice * units;
        if (q.Units < units - 0.001f || result < 0f) return TripVerdict.Loss;
        if (q.Price - buyPrice > GreenMargin * buyPrice && result > 0.5f && q.CashCover >= GreenCover && q.AcceptCover >= GreenCover) return TripVerdict.Good;
        return TripVerdict.Unsure;
    }

    /// <summary>
    /// Runde 25: erwarteter Stückpreis einer Station für eine Ware, wenn die Ladungen anderer Händler (<paramref name="incoming"/>) schon angekommen sind —
    /// dieselbe Bestandsformel wie der Markt (<see cref="MarketSystem.StockPrice"/>); die Deckel der Station (Zahlungsbereitschaft, Endverbraucher-, Export-,
    /// Instandhaltungs-, Baustellenpreis) bleiben, wie sie jetzt wirken: liegt der heutige Preis unter der Formel, ist er gedeckelt und der Deckel gilt weiter.
    /// Verbrauch bis zur Ankunft bleibt unberücksichtigt (vorsichtig).
    /// </summary>
    public static float ArrivalPrice(Universe u, Station st, GoodId good, float incoming)
    {
        if (!st.Market.TryGetValue(good, out var e) || e.Price <= 0f) return 0f;
        if (st.IsBuildSite || incoming <= 0f) return e.Price;
        var g = u.Goods[good];
        float now = MarketSystem.StockPrice(g, e, e.Amount);
        float later = MarketSystem.StockPrice(g, e, e.Amount + incoming);
        return e.Price < now - 0.001f ? MathF.Min(e.Price, later) : later;
    }

    /// <summary>
    /// Runde 23: alle Abnehmer, die jetzt etwas von <paramref name="qty"/> Einheiten annehmen und bezahlen, nach erwartetem Erlös für diese Menge (dann
    /// höherer Preis, weniger Sprünge, Stations-Id) — die Rangfolge der Handelsansicht. Ohne <paramref name="exclude"/>.
    /// </summary>
    public static List<BuyerQuote> HonestBuyers(Universe u, GoodId good, float qty, SectorId fromSector, Vec2 fromPos, StationId? exclude = null, ShipId? self = null, PriceMemory? mem = null)
    {
        var list = new List<BuyerQuote>();
        foreach (var st in u.StationsTrading(good))
            if (st.Id != exclude && QuoteBuyer(u, st, good, qty, fromSector, fromPos, self, mem) is { } q) list.Add(q);
        list.Sort((a, b) => b.Revenue != a.Revenue ? b.Revenue.CompareTo(a.Revenue) : b.Price != a.Price ? b.Price.CompareTo(a.Price)
            : a.Jumps != b.Jumps ? a.Jumps.CompareTo(b.Jumps) : a.Station.Id.Value.CompareTo(b.Station.Id.Value));
        return list;
    }

    /// <summary>Runde 23: der beste ehrliche Abnehmer für <paramref name="qty"/> Einheiten (siehe <see cref="HonestBuyers"/>), null = derzeit keiner.</summary>
    public static BuyerQuote? BestHonestBuyer(Universe u, GoodId good, float qty, SectorId fromSector, Vec2 fromPos, StationId? exclude = null, ShipId? self = null, PriceMemory? mem = null)
    {
        var l = HonestBuyers(u, good, qty, fromSector, fromPos, exclude, self, mem);
        return l.Count > 0 ? l[0] : null;
    }
}

/// <summary>Runde 23: ehrliche Abnehmer-Auskunft — Station, Preis, angenommene Menge, erwarteter Erlös, schon unterwegs von anderen, Sprünge, Reisezeit (Ticks).</summary>
/// <param name="Price">Runde 25: erwarteter Stückpreis bei Ankunft (<see cref="MarketInfo.ArrivalPrice"/>); <paramref name="PriceNow"/> = heutiger Preis.</param>
/// <param name="PriceArrival">Runde 26: Preis bei Ankunft nach der Bestandsformel; <paramref name="Price"/> ist seit Runde 26 die vorsichtige Schätzung
/// (<see cref="MarketInfo.CarefulPrice"/>). <paramref name="CashCover"/>/<paramref name="AcceptCover"/>: wie oft Kasse bzw. Annahme die Ladung decken.</param>
public readonly record struct BuyerQuote(Station Station, float Price, float Units, float Revenue, float Incoming, int Jumps, float TravelTicks, float PriceNow = 0f,
    float PriceArrival = 0f, float CashCover = 0f, float AcceptCover = 0f);

/// <summary>Runde 26: Ampel der Abnehmer-Auskunft (siehe <see cref="MarketInfo.Verdict"/>).</summary>
public enum TripVerdict { Good, Unsure, Loss }
