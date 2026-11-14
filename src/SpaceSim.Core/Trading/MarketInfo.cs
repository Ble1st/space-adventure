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
    public static BuyerQuote? QuoteBuyer(Universe u, Station st, GoodId good, float qty, SectorId fromSector, Vec2 fromPos, ShipId? self = null)
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
        return new BuyerQuote(st, e.Price, units, units * e.Price, incoming, route.Count, ticks);
    }

    /// <summary>
    /// Runde 23: alle Abnehmer, die jetzt etwas von <paramref name="qty"/> Einheiten annehmen und bezahlen, nach erwartetem Erlös für diese Menge (dann
    /// höherer Preis, weniger Sprünge, Stations-Id) — die Rangfolge der Handelsansicht. Ohne <paramref name="exclude"/>.
    /// </summary>
    public static List<BuyerQuote> HonestBuyers(Universe u, GoodId good, float qty, SectorId fromSector, Vec2 fromPos, StationId? exclude = null, ShipId? self = null)
    {
        var list = new List<BuyerQuote>();
        foreach (var st in u.StationsTrading(good))
            if (st.Id != exclude && QuoteBuyer(u, st, good, qty, fromSector, fromPos, self) is { } q) list.Add(q);
        list.Sort((a, b) => b.Revenue != a.Revenue ? b.Revenue.CompareTo(a.Revenue) : b.Price != a.Price ? b.Price.CompareTo(a.Price)
            : a.Jumps != b.Jumps ? a.Jumps.CompareTo(b.Jumps) : a.Station.Id.Value.CompareTo(b.Station.Id.Value));
        return list;
    }

    /// <summary>Runde 23: der beste ehrliche Abnehmer für <paramref name="qty"/> Einheiten (siehe <see cref="HonestBuyers"/>), null = derzeit keiner.</summary>
    public static BuyerQuote? BestHonestBuyer(Universe u, GoodId good, float qty, SectorId fromSector, Vec2 fromPos, StationId? exclude = null, ShipId? self = null)
    {
        var l = HonestBuyers(u, good, qty, fromSector, fromPos, exclude, self);
        return l.Count > 0 ? l[0] : null;
    }
}

/// <summary>Runde 23: ehrliche Abnehmer-Auskunft — Station, Preis, angenommene Menge, erwarteter Erlös, schon unterwegs von anderen, Sprünge, Reisezeit (Ticks).</summary>
public readonly record struct BuyerQuote(Station Station, float Price, float Units, float Revenue, float Incoming, int Jumps, float TravelTicks);
