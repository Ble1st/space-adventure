using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Trading;

public sealed record SaleResult(float Revenue, IReadOnlyDictionary<GoodId, float> Sold, float Unsold);

public enum BuyOutcome { Ok, NotSold, OutOfStock, NoCredits, NoSpace }

/// <summary>Ergebnis eines Einkaufs: tatsächlich gekaufte Einheiten und Kosten; Outcome nennt den begrenzenden Grund, wenn weniger als gewünscht gekauft wurde.</summary>
public sealed record BuyResult(float Units, float Cost, BuyOutcome Outcome);

/// <summary>Verkauf der Fracht eines Schiffs an eine Station (Spieler und Automatik). Gleiche Regeln wie für NPC-Händler.
/// Geld bucht auf die Kasse des Schiffs (<see cref="Universe.WalletOf(Ship)"/>): Spieler → Spielerkasse, NPC → Schiff.</summary>
public static class TradeActions
{
    /// <summary>
    /// Phase 17: die eine Mengenregel jedes Verkaufs an eine Station (Dienst „trade“, <see cref="Sell"/>, <see cref="SellAll"/>, Mining-Autopilot, NPC-Miner,
    /// Angebotsmenge, Annahmehinweis, <see cref="Quote(Universe, Ship, Station)"/>): Nimmt die Station alles an Bord (<paramref name="cap"/> ≥
    /// <paramref name="have"/>) und ist alles gewünscht, geht alles — auch ein Bruchteil einer Einheit aus dem Bergbau, damit kein unverkäuflicher Rest
    /// hängen bleibt. Sonst werden nur ganze Einheiten bewegt (abgerundet); unter einer Einheit verkauft sie nichts. Vorher verkaufte
    /// <see cref="Sell"/> auch einen Bruchteil, wenn die Station weniger als eine Einheit annahm oder bezahlen konnte, während der Handelsbereich
    /// „Lager voll“ bzw. „Station kann nicht zahlen“ meldete.
    /// </summary>
    /// <param name="have">Bestand an Bord.</param>
    /// <param name="want">gewünschte Menge (≥ <paramref name="have"/> = alles).</param>
    /// <param name="cap">was die Station annimmt und bezahlen kann: min(<see cref="Station.AcceptableUnits(GoodId, EconomyRules)"/>, Kasse ÷ Preis).</param>
    public static float SaleUnits(float have, float want, float cap) => SaleUnits(have, want, cap, cap);

    /// <summary>
    /// Phase 18: wie <see cref="SaleUnits(float, float, float)"/>, aber „alles“ gilt schon, wenn <paramref name="allCap"/> die ganze Menge deckt — die
    /// Annahme plus <see cref="EconomyRules.SaleRestTolerance"/> (ein Überhang unter einer Einheit), bezahlbar (<see cref="SaleCaps"/>).
    /// </summary>
    public static float SaleUnits(float have, float want, float cap, float allCap)
    {
        if (have <= 0f || want <= 0f) return 0f;
        if (want >= have && allCap >= have && allCap > 0f) return have;
        if (cap <= 0f) return 0f;
        return MathF.Floor(MathF.Min(MathF.Min(want, have), cap));
    }

    /// <summary>
    /// Phase 18: beide Obergrenzen eines Verkaufs bei der Kasse <paramref name="credits"/>: <c>Cap</c> = min(Annahme, Kasse ÷ Preis) für ganze Einheiten,
    /// <c>AllCap</c> = min(Annahme + Resttoleranz, Kasse ÷ Preis) für „alles“, wenn die Station die Ware überhaupt annimmt (Annahme &gt; 0) und keine
    /// Baustelle ist (deren Stückliste ist genau). (0, 0) ohne Preis.
    /// </summary>
    public static (float Cap, float AllCap) SaleCaps(Universe u, Station station, GoodId good, float credits)
    {
        if (!station.Market.TryGetValue(good, out var e) || e.Price <= 0f) return (0f, 0f);
        float pay = MathF.Max(0f, credits) / e.Price;
        float accept = station.AcceptableUnits(good, u.Rules);
        float cap = MathF.Min(pay, accept);
        float allCap = accept > 0f && station.Site is null && u.Rules.SaleRestTolerance > 0f ? MathF.Min(pay, accept + u.Rules.SaleRestTolerance) : cap;
        return (MathF.Max(0f, cap), MathF.Max(0f, allCap));
    }

    /// <summary>Phase 17: was die Station von dieser Ware jetzt annimmt und bezahlen kann (Obergrenze für <see cref="SaleUnits(float, float, float)"/>; 0 ohne Preis).</summary>
    public static float SaleCap(Universe u, Station station, GoodId good) =>
        station.Market.TryGetValue(good, out var e) && e.Price > 0f ? MathF.Min(MathF.Max(0f, station.Credits) / e.Price, station.AcceptableUnits(good, u.Rules)) : 0f;

    /// <summary>
    /// Verkauft alle Waren, die die Station führt, zum aktuellen Preis. Die Station zahlt nur, was sie hat; der Rest bleibt an Bord.
    /// Phase 17: je Ware nach <see cref="SaleUnits"/> (alles, wenn die Station alles nimmt, sonst ganze Einheiten).
    /// </summary>
    public static SaleResult SellAll(Universe u, Ship ship, Station station)
    {
        float revenue = 0f;
        var sold = new Dictionary<GoodId, float>();
        var wallet = u.WalletOf(ship);
        // feste Reihenfolge (GoodId), nicht die Einfügereihenfolge des Dictionarys: die geht beim Speichern/Laden nach Entnahmen verloren
        // (Phase 13 gefunden: sonst verkauft ein geladener Stand bei knapper Stationskasse in anderer Reihenfolge)
        foreach (var good in ship.Cargo.Keys.OrderBy(k => k.Value).ToList())
        {
            float have = ship.Cargo[good];
            if (have <= 0f || !station.Market.TryGetValue(good, out var entry) || entry.Price <= 0f) continue;

            var (cap, allCap) = SaleCaps(u, station, good, station.Credits);
            float units = SaleUnits(have, have, cap, allCap);
            if (units <= 0f) continue;
            float pay = units * entry.Price;
            entry.Amount += units;
            station.Credits -= pay;
            wallet.Credits += pay;
            if (have - units <= 0.0001f) ship.Cargo.Remove(good); else ship.Cargo[good] = have - units;
            revenue += pay;
            sold[good] = units;
            if (ship.IsPlayer) u.Events.Publish(new PlayerSold(ship.Id, station.Id, good, units, pay));
            else u.Events.Publish(new MineDelivered(ship.Id, station.Id, good, units, pay));
            if (station.IsBuildSite) ConstructionRules.OnDelivered(u, station, ship, good, units, ship.IsPlayer ? "player" : "trader");
            ExportRules.OnDelivered(u, station, good, units, pay);   // Phase 14: Exportsenke
        }
        return new SaleResult(revenue, sold, ship.Cargo.Values.Sum());
    }

    /// <summary>Kauft bis zu <paramref name="units"/> Einheiten einer Ware zum Stationspreis. Phase 14: Exportwaren einer Exportsenke sind reiner Ankauf (nicht käuflich). Begrenzt durch Lagerbestand, Credits und Laderaum; die Station behält nichts zurück, was sie nicht liefern kann.</summary>
    public static BuyResult Buy(Universe u, Ship ship, Station station, GoodId good, float units)
    {
        if (units <= 0f || !station.Market.TryGetValue(good, out var e) || e.Price <= 0f || IsLiveSite(station) || ExportRules.Buys(u, station, good)) return new BuyResult(0f, 0f, BuyOutcome.NotSold);
        var wallet = u.WalletOf(ship);
        float stock = MathF.Floor(e.Amount), afford = MathF.Floor(wallet.Credits / e.Price), space = MathF.Floor(ship.FreeUnits(u, good));
        float n = MathF.Min(MathF.Min(MathF.Floor(units), stock), MathF.Min(afford, space));
        if (n <= 0f)
            return new BuyResult(0f, 0f, stock <= 0f ? BuyOutcome.OutOfStock : afford <= 0f ? BuyOutcome.NoCredits : BuyOutcome.NoSpace);
        float cost = n * e.Price;
        e.Amount -= n;
        station.Credits += cost;
        wallet.Credits -= cost;
        ship.Cargo[good] = ship.Cargo.GetValueOrDefault(good) + n;
        if (ship.IsPlayer) u.Events.Publish(new PlayerBought(ship.Id, station.Id, good, n, cost));
        var outcome = n < MathF.Floor(units) ? (n == stock ? BuyOutcome.OutOfStock : n == afford ? BuyOutcome.NoCredits : BuyOutcome.NoSpace) : BuyOutcome.Ok;
        return new BuyResult(n, cost, outcome);
    }

    /// <summary>Höchstmenge, die <see cref="Buy"/> für diese Ware jetzt liefern würde.</summary>
    public static float MaxBuyable(Universe u, Ship ship, Station station, GoodId good) =>
        !IsLiveSite(station) && !ExportRules.Buys(u, station, good) && station.Market.TryGetValue(good, out var e) && e.Price > 0f
            ? MathF.Max(0f, MathF.Min(MathF.Min(MathF.Floor(e.Amount), MathF.Floor(u.WalletOf(ship).Credits / e.Price)), MathF.Floor(ship.FreeUnits(u, good))))
            : 0f;

    /// <summary>Laufende Baustelle (Phase 8): nimmt nur Material an und verkauft nichts zurück; verlassene Baustellen verkaufen ihr Lager.</summary>
    static bool IsLiveSite(Station station) => station.Site is { Abandoned: false };

    /// <summary>Verkauft bis zu <paramref name="units"/> Einheiten einer einzelnen Ware (gleiche Regeln wie <see cref="SellAll"/>, Menge nach <see cref="SaleUnits"/>).</summary>
    public static SaleResult Sell(Universe u, Ship ship, Station station, GoodId good, float units)
    {
        float have = ship.Cargo.GetValueOrDefault(good);
        var sold = new Dictionary<GoodId, float>();
        if (have <= 0f || units <= 0f || !station.Market.TryGetValue(good, out var e) || e.Price <= 0f)
            return new SaleResult(0f, sold, ship.Cargo.Values.Sum());
        var (cap, allCap) = SaleCaps(u, station, good, station.Credits);
        float n = SaleUnits(have, units, cap, allCap);
        if (n <= 0f) return new SaleResult(0f, sold, ship.Cargo.Values.Sum());
        float pay = n * e.Price;
        e.Amount += n;
        station.Credits -= pay;
        u.WalletOf(ship).Credits += pay;
        if (have - n <= 0.0001f) ship.Cargo.Remove(good); else ship.Cargo[good] = have - n;
        sold[good] = n;
        if (ship.IsPlayer) u.Events.Publish(new PlayerSold(ship.Id, station.Id, good, n, pay));
        if (station.IsBuildSite) ConstructionRules.OnDelivered(u, station, ship, good, n, ship.IsPlayer ? "player" : "trader");
        ExportRules.OnDelivered(u, station, good, n, pay);   // Phase 14: Exportsenke
        return new SaleResult(pay, sold, ship.Cargo.Values.Sum());
    }

    /// <summary>
    /// Erwarteter Erlös der aktuellen Fracht an dieser Station (ohne zu verkaufen): je Ware die Menge nach <see cref="SaleUnits"/> (Phase 17: dieselbe Regel
    /// wie <see cref="SellAll"/>; Phase 16 zählte hier schon nur ganze Einheiten oder einen ganz genommenen Rest, verkaufte aber Bruchteile).
    /// Wie SellAll rechnet sie mit der Kasse, die nach den vorigen Waren übrig bleibt.
    /// </summary>
    public static float Quote(Universe u, Ship ship, Station station) => Sellable(u, ship, station).Revenue;

    /// <summary>Phase 17: was <see cref="SellAll"/> jetzt bewegen würde — Einheiten und Erlös (reine Auswertung, gleiche Reihenfolge und Regel).</summary>
    public static (float Units, float Revenue) Sellable(Universe u, Ship ship, Station station)
    {
        float total = 0f, units = 0f, credits = station.Credits;
        foreach (var (good, have) in ship.Cargo.OrderBy(kv => kv.Key.Value))
            if (have > 0f && station.Market.TryGetValue(good, out var e) && e.Price > 0f)
            {
                var (cap, allCap) = SaleCaps(u, station, good, credits);
                float n = SaleUnits(have, have, cap, allCap);
                if (n <= 0f) continue;
                total += n * e.Price; units += n; credits -= n * e.Price;
            }
        return (units, total);
    }

    /// <summary>
    /// Phase 14: Beste Abnehmerin für eine Teilladung einer Ware (Erlös aus angenommener und bezahlbarer Menge pro Reisezeit, nur erreichbare Stationen),
    /// ohne <paramref name="exclude"/>; null = niemand nimmt sie.
    /// </summary>
    /// <param name="allowed">Phase 21: optionaler Filter (Automatik-Grenzen eines Flottenschiffs); null = alle Stationen.</param>
    public static Station? BestBuyerFor(Universe u, Ship ship, GoodId good, float units, StationId exclude, Func<Station, bool>? allowed = null)
    {
        Station? best = null;
        float bestScore = 0f;
        // Phase 15: ohne Sortierung je Aufruf (keine Allokation); Gleichstand entscheidet die kleinere Id — dasselbe Ergebnis wie die frühere Id-Reihenfolge
        foreach (var st in u.Stations.Values)
        {
            // Baustellen bekommen nur geplante Lieferungen (Reservierung im Händler), keine Restmengen
            if (st.Id == exclude || st.IsBuildSite || !st.Market.TryGetValue(good, out var e) || e.Price <= 0f) continue;
            if (allowed is not null && !allowed(st)) continue;
            float n = MathF.Min(MathF.Min(units, st.AcceptableUnits(good, u.Rules)), MathF.Max(0f, st.Credits) / e.Price);
            if (n < 1f) continue;
            float time = Travel.Estimate(u, ship.Sector, ship.Position, st.Sector, st.DockPoint(ship.Id), ship);
            if (float.IsPositiveInfinity(time)) continue;
            float score = n * e.Price / (time + 10f);
            if (score > bestScore || (score == bestScore && best is not null && st.Id.Value < best.Id.Value)) { bestScore = score; best = st; }
        }
        return best;
    }

    /// <summary>
    /// Beste Verkaufsstation für die Fracht: Erlös pro Reisezeit, nur erreichbare Stationen. Phase 16: zählt je Ware nur, was die Station in mindestens
    /// einer ganzen Einheit annimmt und bezahlen kann — sonst fuhr der Mining-Autopilot (und ein NPC-Miner mit Restfracht) für Bruchteile einer Einheit
    /// zu einer vollen Station und wieder zurück.
    /// </summary>
    public static Station? BestBuyer(Universe u, Ship ship, SectorId fromSector, Vec2 fromPos, StationId? exclude = null)
    {
        Station? best = null;
        float bestScore = 0f;
        foreach (var st in u.Stations.Values)
        {
            if (st.Id == exclude) continue;
            float quote = Quote(u, ship, st);
            if (quote <= 0f) continue;
            float time = Travel.Estimate(u, fromSector, fromPos, st.Sector, st.DockPoint(ship.Id), ship);
            if (float.IsPositiveInfinity(time)) continue;
            float score = quote / (time + 10f);
            if (score > bestScore) { bestScore = score; best = st; }
        }
        return best;
    }
}
