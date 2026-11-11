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
    /// <summary>Nimmt die Station überhaupt etwas (mindestens eine Einheit)?</summary>
    public bool Accepts => Units >= 1f;

    /// <summary>
    /// Hinweis für eine Verkaufszeile mit <paramref name="held"/> Einheiten an Bord: der Grund, wenn die Station nichts nimmt; nimmt sie weniger, als an Bord
    /// ist, der Grund der Begrenzung („Baustelle: nur noch n“, „kann nur n bezahlen“) oder „nimmt noch n“; sonst null.
    /// </summary>
    public string? NoteFor(float held)
    {
        if (!Accepts) return Reason ?? "Nimmt nichts an";
        if (held > Units + 0.001f) return Reason ?? $"nimmt noch {MathF.Floor(Units):0}";
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
        if (st.Site is { } site)
        {
            if (site.Abandoned) return new Intake(0f, IntakeLimit.Site, "Verlassene Baustelle");
            if (accept < 1f) return new Intake(0f, IntakeLimit.Site, "Baustelle: Material vollständig");
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
}
