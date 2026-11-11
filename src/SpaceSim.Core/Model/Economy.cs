namespace SpaceSim.Core.Model;

/// <summary>Warenform: fest oder flüssig (Gase verdichtet). Flüssigstoffe reisen in Kanistern (Frachtmodul mit holds liquid).</summary>
public enum GoodForm { Solid, Liquid }

public sealed class Good
{
    public GoodId Id;
    /// <summary>Stabiler Schlüssel aus den Inhaltsdaten ("iron_ore"); Verweise zwischen Daten und Regeln laufen darüber.</summary>
    public string Key = "";
    public string Name = "";
    /// <summary>Farbe für Brocken und Anzeige (#RRGGBB).</summary>
    public string Color = "#c8c8c8";
    public float BasePrice;
    public float VolumePerUnit = 1f;
    /// <summary>Warenform aus den Inhaltsdaten; wird als eigener (optionaler) Wert im Save mitgespeichert (alte Spielstände ohne das Feld laden als <see cref="GoodForm.Solid"/>).</summary>
    public GoodForm Form = GoodForm.Solid;
}

public sealed class Recipe
{
    public Dictionary<GoodId, float> Inputs = new();
    public Dictionary<GoodId, float> Outputs = new();
    public int TicksPerCycle = 10;
}

public sealed class StockEntry
{
    public float Amount;
    /// <summary>Wunschbestand. Amount unter Target erhöht den Preis, darüber senkt er ihn.</summary>
    public float Target = 100f;
    public float Price;
}

public sealed class StationType
{
    public string Key = "";
    public string Name = "";
    public List<Recipe> Recipes = new();
    public float BuildCost = 5000f;
    public float UpkeepPerTick = 0.05f;
    /// <summary>Einnahmen aus Verkäufen außerhalb der simulierten Welt (Faucet).</summary>
    public float IncomePerTick;
    /// <summary>Dienst-Schlüssel, die Stationen dieses Typs anbieten, in Reihenfolge; Standard ["trade"] (Handel).</summary>
    public List<string> Services = new() { "trade" };
    /// <summary>
    /// Lager ohne Rezept (Phase 5a): Waren mit Anfangs- und Wunschbestand, für die eine neue Station dieses Typs zusätzlich zu den
    /// Rezepten einen Markteintrag bekommt (z. B. Module im Werftlager). Steht die Ware auch in einem Rezept, legt der Eintrag
    /// deren Anfangs-/Wunschbestand fest. Händler beliefern solche Lager über den normalen Handel.
    /// </summary>
    public List<StationStock> Stocks = new();
    /// <summary>
    /// Schiffshändler (Phase 5c, Dienst „shipdealer“): welche Schiffsklassen die Station aus ihrem Lager montiert und verkauft;
    /// null = Station verkauft keine Schiffe.
    /// </summary>
    public ShipDealerConfig? ShipDealer;
    /// <summary>
    /// Phase 8: eigene Stückliste für den Bau einer Station dieses Typs (Ware → Einheiten); null = Regel
    /// <see cref="EconomyRules.ConstructionMaterials"/> × <see cref="EconomyRules.ConstructionMaterialShare"/> der Bausumme.
    /// </summary>
    public Dictionary<GoodId, float>? Construction;
    /// <summary>
    /// Phase 10: Regierungsform (Hauptquartier, Firmensitz, Sektor-HQ, Büro, Polizeiwache; JSON <c>government</c>). Regierungsstationen zahlen
    /// ihren Unterhalt aus der Kasse des Besitzers, sind vor Pleite geschützt und zählen nicht gegen die Bau-Obergrenzen.
    /// </summary>
    public GovernmentKind Government;
    /// <summary>Phase 10: Einflussgewicht einer Station dieses Typs in der Sektorkontrolle (JSON <c>influence</c>, Standard 1; Baustellen 0).</summary>
    public float Influence = 1f;
    /// <summary>Phase 10: Kann eine Station dieses Typs erobert werden (sofern die Regel <see cref="EconomyRules.AllowStationCapture"/> es erlaubt)? Standard ja.</summary>
    public bool Capturable = true;
    /// <summary>Phase 10: Kann eine Station dieses Typs zerstört werden (erst mit Kampf relevant)? Standard ja.</summary>
    public bool Destructible = true;
    /// <summary>
    /// Phase 11: Grundschutz einer Station dieses Typs auf der Lagekarte (JSON <c>security</c>, Standard 0): Wache, Hauptquartier und Sektor-HQ
    /// sichern ihren Sektor auch ohne Streife ein Stück ab (zählt nur für die Fraktion des Besitzers, die den Sektor hält).
    /// </summary>
    public float Security;
    /// <summary>
    /// Phase 11: Piratenversteck (JSON <c>hideout</c>): Heimat der Kaperschiffe eines gesetzlosen Akteurs. Zählt nicht gegen die Bau-Obergrenzen, geht
    /// nicht pleite und hat keinen Einfluss auf die Sektorkontrolle (Einfluss 0 in der Weltdatei).
    /// </summary>
    public bool Hideout;
}

/// <summary>
/// Einschränkung des Schiffshändlers eines Stationstyps (SCHIFFSSLOTS.md, „Kleine Schiffe an anderen Stationen“): erlaubte Klassen
/// (null = alle), größte Klassengröße und Montagegebühr als Anteil auf Bausatz- und Materialpreis.
/// </summary>
public sealed class ShipDealerConfig
{
    /// <summary>Erlaubte Klassen-Schlüssel; null = alle Klassen (Werft).</summary>
    public List<string>? Classes;
    /// <summary>Größte Klassengröße, die die Station verkauft.</summary>
    public SlotSize MaxSize = SlotSize.XL;
    /// <summary>Montagegebühr: Kaufpreis = (Bausatz + Module zum Marktpreis) × (1 + Assembly).</summary>
    public float Assembly;

    /// <summary>Darf diese Station Schiffe dieser Klasse verkaufen?</summary>
    public bool Allows(ShipClass cls) => cls.Size <= MaxSize && (Classes is null ? !cls.Restricted : Classes.Contains(cls.Key));
}

/// <summary>Lagerposten eines Stationstyps: Ware, Anfangsbestand, Wunschbestand (Phase 5a, <see cref="StationType.Stocks"/>).</summary>
public sealed record StationStock(GoodId Good, float Amount, float Target);

public sealed class Station
{
    public StationId Id;
    public string TypeKey = "";
    /// <summary>
    /// Vor Pleite geschützt (Phase 8, STATIONEN.md „Essential aufgedröselt“; früher <c>Essential</c>): die Station wird bei Pleite nie
    /// aufgegeben. Quelle: JSON <c>essential</c> (Alias) bzw. <c>bankruptcyProtected</c>, Startwelt. Ob eine Station die letzte ihrer Kette ist
    /// (<c>Critical</c>), wird dagegen abgeleitet (<see cref="Simulation.StationCriticality"/>) und nicht gespeichert.
    /// </summary>
    public bool BankruptcyProtected;
    /// <summary>Baustelle (Phase 8): gesetzt, solange die Station im Bau ist (oder als verlassene Baustelle Material lagert); null = fertige Station.</summary>
    public BuildSite? Site;
    /// <summary>
    /// Phase 10 (STATIONEN.md „Essential aufgedröselt“): unzerstörbar und nicht eroberbar (Tutorial, Szenario); nur aus der Weltdatei
    /// (<c>immortal</c>), gespeichert. Die übrigen Eigenschaften (<c>Government</c>, <c>Capturable</c>, <c>Destructible</c>) folgen aus dem Typ
    /// (<see cref="Simulation.StationProtection"/>).
    /// </summary>
    public bool Immortal;
    /// <summary>Ist die Station eine Baustelle (auch verlassen)? Baustellen produzieren nicht, zahlen keinen Unterhalt und gehen nicht pleite.</summary>
    public bool IsBuildSite => Site is not null;
    /// <summary>
    /// Persönliches Lager des Spielers an dieser Station (Phase 8, Dienst „storage“ am Lagerhaus): Ware → Menge. Getrennt vom Markt; gespeichert.
    /// </summary>
    public Dictionary<GoodId, float> PlayerStorage = new();
    public long BelowLimitSince = -1;
    public string Name = "";
    public SectorId Sector;
    /// <summary>Besitzer (Phase 9: ein Akteur; die Startstationen gehören den Fraktionsakteuren, Id = Fraktions-Id).</summary>
    public ActorId Owner;
    public Vec2 Position;
    public float Credits;

    /// <summary>Kollisionsradius des Stationskörpers (inkl. Ring).</summary>
    public const float BodyRadius = 15f;

    /// <summary>Parkposition eines Schiffs an der Station, knapp außerhalb des Körpers.</summary>
    public Vec2 DockPoint(ShipId ship) => Position + Vec2.FromAngle(ship.Value * 2.4f) * 22f;
    public List<Recipe> Production = new();
    public Dictionary<GoodId, StockEntry> Market = new();

    /// <summary>
    /// Wie viele Einheiten die Station noch annimmt, bevor ihr Lager voll ist (Wunschbestand × <see cref="EconomyRules.OverstockFactor"/>).
    /// Eine volle Station kauft nicht mehr – sonst würde Beifang einer Erzmischung ewig auf Halde landen.
    /// </summary>
    public float AcceptableUnits(GoodId good, EconomyRules rules) =>
        !Market.TryGetValue(good, out var e) ? 0f
        // Phase 8: Eine Baustelle nimmt genau das noch fehlende Material an (verlangt − geliefert); verlassene Baustellen nichts mehr
        : Site is { } site ? (site.Abandoned ? 0f : MathF.Max(0f, e.Target - e.Amount))
        : MathF.Max(0f, e.Target * rules.OverstockFactor - e.Amount);

    /// <summary>
    /// Lagerware ohne Rezept (Phase 5a, <see cref="StationType.Stocks"/>): Die Station führt die Ware, aber kein Rezept verwendet oder
    /// erzeugt sie (z. B. Module im Werftlager). KI-Händler beliefern solche Lager, kaufen dort aber nicht ein — sonst pendelt die Ware
    /// zwischen Lager und Fabrik (die Fabrik kauft ihr leeres Produkt teuer zurück). Spieler und spätere Dienste dürfen dort kaufen.
    /// </summary>
    public bool IsStockOnly(GoodId good)
    {
        if (!Market.ContainsKey(good)) return false;
        foreach (var r in Production)
            if (r.Inputs.ContainsKey(good) || r.Outputs.ContainsKey(good)) return false;
        return true;
    }

    public StockEntry Stock(GoodId good)
    {
        if (!Market.TryGetValue(good, out var e)) Market[good] = e = new StockEntry();
        return e;
    }
}
