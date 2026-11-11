namespace SpaceSim.Core.Model;

/// <summary>
/// Phase 14: Planet eines Sektors — Kulisse der Ansicht und Abnehmer der Exportsenke (<see cref="ExportConfig"/>). Nur Daten aus der Weltdatei,
/// gespeichert und im Hash; Stationen mit Exportsenke gibt es nur in Sektoren mit Planet.
/// </summary>
public sealed class Planet
{
    public string Name = "";
    /// <summary>Farbe der Kulissen-Kugel (#RRGGBB).</summary>
    public string Color = "#5a7fa8";
    /// <summary>Radius der Kulissen-Kugel in Welt-Einheiten (nur Darstellung, keine Kollision).</summary>
    public float Radius = 400f;
}

/// <summary>
/// Phase 14: Exportsenke eines Stationstyps (Handelsstation an einem Planeten). Die Station kauft die <see cref="Goods"/> über den normalen Handel
/// an und bezahlt sofort aus ihrer Kasse; jede Lieferung wird eine <see cref="ExportBatch"/>, die nach der Verzögerung an den Planeten geht
/// (Ware verschwindet, die Station erhält bezahlten Betrag × (1 + Aufschlag)). Händler kaufen dort nicht ein (Lager ohne Rezept), der Spieler auch nicht.
/// </summary>
public sealed class ExportConfig
{
    /// <summary>Exportwaren in GoodId-Reihenfolge (aus Kategorien und Einzelwaren der Weltdatei aufgelöst).</summary>
    public List<GoodId> Goods = new();
    /// <summary>Kategorien, aus denen <see cref="Goods"/> entstand (für Anzeige und Speicherung).</summary>
    public List<string> Categories = new();
    /// <summary>Ticks bis zum Export einer Charge; null = Regel <see cref="EconomyRules.ExportDelayTicks"/>.</summary>
    public int? DelayTicks;
    /// <summary>Aufschlag auf den bezahlten Ankaufpreis; null = Regel <see cref="EconomyRules.ExportMarkup"/>.</summary>
    public float? Markup;
    /// <summary>Wunschbestand je Exportware (Preisformel über den Bestand einschließlich wartender Chargen).</summary>
    public float Target = 20f;
    /// <summary>Wunschbestand als Warenwert (Credits zum Basispreis); &gt; 0 = je Ware max(1, round(TargetValue ÷ Basispreis)) statt <see cref="Target"/>.</summary>
    public float TargetValue;

    /// <summary>Wunschbestand einer Exportware nach dieser Konfiguration.</summary>
    public float TargetFor(Good good) => TargetValue > 0f && good.BasePrice > 0f ? MathF.Max(1f, MathF.Round(TargetValue / good.BasePrice)) : Target;

    /// <summary>
    /// Phase 17: höchstes Gebot je Exportware als Vielfaches des Basispreises (aus <c>export.bids</c> der Weltdatei, je Ware aufgelöst); fehlt eine Ware,
    /// gilt <see cref="EconomyRules.ExportMaxBidFactor"/>.
    /// </summary>
    public Dictionary<GoodId, float> Bids = new();

    /// <summary>Höchstgebot (× Basispreis) für diese Exportware.</summary>
    public float BidOf(GoodId good, EconomyRules r) => Bids.TryGetValue(good, out var f) ? f : r.ExportMaxBidFactor;

    public int Delay(EconomyRules r) => Math.Max(1, DelayTicks ?? r.ExportDelayTicks);
    public float MarkupOf(EconomyRules r) => MathF.Max(0f, Markup ?? r.ExportMarkup);
}

/// <summary>Phase 14: eine angekaufte Charge, die auf den Export an den Planeten wartet (Ware, Menge, bezahlter Betrag, Fälligkeit). Gespeichert.</summary>
public sealed class ExportBatch
{
    public GoodId Good;
    public float Amount;
    /// <summary>Was die Station beim Ankauf bezahlt hat (unbezahlte Teile einer Ladung zählen 0).</summary>
    public float Paid;
    public long DueTick;
}
