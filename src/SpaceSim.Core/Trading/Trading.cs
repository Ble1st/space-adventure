using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Trading;

/// <param name="Capped">Runde 31: davon zum Rückkaufpreis <paramref name="CapPrice"/> gerechnete Einheiten (PlayerLedger).</param>
/// <param name="RevenueByGood">Runde 39: Erlös je verkaufter Ware (für das Handelsjournal); null = nicht einzeln ausgewiesen.</param>
public sealed record SaleResult(float Revenue, IReadOnlyDictionary<GoodId, float> Sold, float Unsold, float Capped = 0f, float CapPrice = 0f, IReadOnlyDictionary<GoodId, float>? RevenueByGood = null);

public enum BuyOutcome { Ok, NotSold, OutOfStock, NoCredits, NoSpace }

/// <summary>Ergebnis eines Einkaufs: tatsächlich gekaufte Einheiten und Kosten; Outcome nennt den begrenzenden Grund, wenn weniger als gewünscht gekauft wurde.</summary>
/// <param name="Floored">Runde 31: davon zum Rückkaufpreis <paramref name="FloorPrice"/> gerechnete Einheiten (PlayerLedger).</param>
public sealed record BuyResult(float Units, float Cost, BuyOutcome Outcome, float Floored = 0f, float FloorPrice = 0f);

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
    public static (float Cap, float AllCap) SaleCaps(Universe u, Station station, GoodId good, float credits, float? acceptOverride = null, Ship? ship = null)
    {
        if (!station.Market.TryGetValue(good, out var e) || e.Price <= 0f) return (0f, 0f);
        float pay = MathF.Max(0f, credits) / e.Price;
        // Phase 22: eine zugesagte Lieferung darf eine größere Annahme haben (TraderSystem, EconomyRules.HonourPlannedDelivery)
        float accept = acceptOverride ?? station.AcceptableUnits(good, u.Rules);
        // Runde 41/42b: Liefervertrag — die reservierte Annahme und Kasse gelten für das Spielerschiff (Anzeige = Ausführung, siehe ContractReserve)
        if (acceptOverride is null)
        {
            var (resAccept, resCash) = ContractReserve(u, station, good, ship);
            accept += resAccept;
            pay += resCash / e.Price;
        }
        float cap = MathF.Min(pay, accept);
        float allCap = accept > 0f && station.Site is null && u.Rules.SaleRestTolerance > 0f ? MathF.Min(pay, accept + u.Rules.SaleRestTolerance) : cap;
        return (MathF.Max(0f, cap), MathF.Max(0f, allCap));
    }

    /// <summary>
    /// Runde 41/42b: Reservierung eines aktiven Liefervertrags des Spielers an dieser Station für diese Ware: noch offene Annahme
    /// (Einheiten = n − geliefert) und die dafür reservierte Kasse (anteilig). Nur für Spielerschiffe, nur solange Aufträge laufen; sonst (0, 0).
    /// Anzeige (<see cref="MarketInfo.Acceptance"/>) und Ausführung (<see cref="SaleCaps"/>) rechnen beide damit — die Station nimmt die
    /// reservierte Menge auch bei vollem Lager an und kann sie bezahlen.
    /// </summary>
    public static (float AcceptLeft, float CashLeft) ContractReserve(Universe u, Station station, GoodId good, Ship? ship)
    {
        if (ship is null || u.Missions.Count == 0 || ship.Owner != u.PlayerActor) return (0f, 0f);
        float accept = 0f, cash = 0f;
        foreach (var m in u.Missions)
        {
            if (m.State != MissionState.Active || m.At != station.Id || m.Type != Missions.DeliveryMission.KeyId) continue;
            if (!m.Args.TryGetValue("contract", out var c) || c != "1") continue;
            if (!m.Args.TryGetValue("good", out var gs) || !int.TryParse(gs, out var gv) || gv != good.Value) continue;
            if (!m.Args.TryGetValue("units", out var us)
                || !float.TryParse(us, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var need) || need <= 0f) continue;
            float left = MathF.Max(0f, need - m.Sold);
            if (left <= 0f) continue;
            accept += left;
            cash += m.ReservedCash * (left / need);
        }
        return (accept, cash);
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
        float revenue = 0f, capped = 0f;
        var sold = new Dictionary<GoodId, float>();
        var byGood = new Dictionary<GoodId, float>();   // Runde 39: Erlös je Ware (Journal)
        if (Refusal(u, ship, station) is not null) return new SaleResult(0f, sold, ship.Cargo.Values.Sum());   // Phase 25: gesucht → kein Handel
        var wallet = u.WalletOf(ship);
        // feste Reihenfolge (GoodId), nicht die Einfügereihenfolge des Dictionarys: die geht beim Speichern/Laden nach Entnahmen verloren
        // (Phase 13 gefunden: sonst verkauft ein geladener Stand bei knapper Stationskasse in anderer Reihenfolge)
        foreach (var good in ship.Cargo.Keys.OrderBy(k => k.Value).ToList())
        {
            float have = ship.Cargo[good];
            if (have <= 0f || !station.Market.TryGetValue(good, out var entry) || entry.Price <= 0f) continue;

            var (cap, allCap) = SaleCaps(u, station, good, station.Credits, null, ship);
            float units = SaleUnits(have, have, cap, allCap);
            if (units <= 0f) continue;
            var bk = PlayerLedger.BookSale(u, ship, station, good, units, wallet);   // Runde 30/31: Rückkaufdeckel über die eine Buchung
            float pay = bk.Money;
            capped += bk.Ledgered;
            if (have - units <= 0.0001f) ship.Cargo.Remove(good); else ship.Cargo[good] = have - units;
            revenue += pay;
            sold[good] = units;
            byGood[good] = pay;
            if (ship.IsPlayer) u.Events.Publish(new PlayerSold(ship.Id, station.Id, good, units, pay));
            else u.Events.Publish(new MineDelivered(ship.Id, station.Id, good, units, pay));
            if (station.IsBuildSite) ConstructionRules.OnDelivered(u, station, ship, good, units, ship.IsPlayer ? "player" : "trader");
            ExportRules.OnDelivered(u, station, good, units, pay);   // Phase 14: Exportsenke
        }
        return new SaleResult(revenue, sold, ship.Cargo.Values.Sum(), capped, 0f, byGood);
    }

    /// <summary>Kauft bis zu <paramref name="units"/> Einheiten einer Ware zum Stationspreis. Phase 14: Exportwaren einer Exportsenke sind reiner Ankauf (nicht käuflich). Begrenzt durch Lagerbestand, Credits und Laderaum; die Station behält nichts zurück, was sie nicht liefern kann.</summary>
    public static BuyResult Buy(Universe u, Ship ship, Station station, GoodId good, float units)
    {
        if (units <= 0f || !station.Market.TryGetValue(good, out var e) || e.Price <= 0f || IsLiveSite(station) || ExportRules.Buys(u, station, good)
            || Refusal(u, ship, station) is not null) return new BuyResult(0f, 0f, BuyOutcome.NotSold);
        var wallet = u.WalletOf(ship);
        float stock = MathF.Floor(e.Amount), space = MathF.Floor(ship.FreeUnits(u, good));
        // Runde 30: Rückkauf eigener Lieferungen höchstens billiger als erhalten (PlayerLedger) — die Menge folgt den wirklichen Kosten
        float afford = PlayerLedger.Affordable(u, ship, station, good, MathF.Max(1f, MathF.Min(MathF.Floor(units), stock)), e.Price, wallet.Credits);
        float n = MathF.Min(MathF.Min(MathF.Floor(units), stock), MathF.Min(afford, space));
        if (n <= 0f)
            return new BuyResult(0f, 0f, stock <= 0f ? BuyOutcome.OutOfStock : afford <= 0f ? BuyOutcome.NoCredits : BuyOutcome.NoSpace);
        var bk = PlayerLedger.BookBuy(u, ship, station, good, n, wallet);   // Runde 30/31: Rückkaufdeckel über die eine Buchung
        float cost = bk.Money;
        ship.Cargo[good] = ship.Cargo.GetValueOrDefault(good) + n;
        if (ship.IsPlayer) u.Events.Publish(new PlayerBought(ship.Id, station.Id, good, n, cost));
        var outcome = n < MathF.Floor(units) ? (n == stock ? BuyOutcome.OutOfStock : n == afford ? BuyOutcome.NoCredits : BuyOutcome.NoSpace) : BuyOutcome.Ok;
        return new BuyResult(n, cost, outcome, bk.Ledgered, bk.LedgerPrice);
    }

    /// <summary>Höchstmenge, die <see cref="Buy"/> für diese Ware jetzt liefern würde.</summary>
    public static float MaxBuyable(Universe u, Ship ship, Station station, GoodId good) =>
        !IsLiveSite(station) && !ExportRules.Buys(u, station, good) && Refusal(u, ship, station) is null && station.Market.TryGetValue(good, out var e) && e.Price > 0f
            ? MathF.Max(0f, PlayerLedger.Affordable(u, ship, station, good, MathF.Min(MathF.Floor(e.Amount), MathF.Floor(ship.FreeUnits(u, good))), e.Price, u.WalletOf(ship).Credits))
            : 0f;

    /// <summary>
    /// Phase 25 (KAMPF.md 4.3 „Überfall durch den Spieler“): Eine Station handelt nicht mit dem aktiven Spielerschiff, wenn die Person bei der Fraktion des
    /// Stationsbesitzers ab <see cref="EconomyRules.TradeRefusalWantedLevel"/> gesucht wird — Beute ist nur dort verkäuflich, wo man nicht gesucht wird.
    /// Liefert den Grund (null = Handel erlaubt). Rein.
    /// </summary>
    public static string? Refusal(Universe u, Ship ship, Station station)
    {
        int min = u.Rules.TradeRefusalWantedLevel;
        if (ship.Id != u.PlayerShip || u.FactionOf(station) is not { } f) return null;
        // Runde 40: im Krieg mit der Stationsfraktion verweigert die Station Handel (auch im Waffenstillstand — AtWar, nicht Hostile)
        if (Simulation.Wars.AtWar(u, u.PlayerActor, f))
            return $"Handel verweigert: Krieg mit {(u.Factions.TryGetValue(f, out var wf) ? wf.Name : "der Fraktion")}.";
        if (min <= 0) return null;
        int level = Wanted.LevelOf(u, u.PlayerActor, f);
        return level >= min ? $"Handel verweigert: du wirst bei {(u.Factions.TryGetValue(f, out var fa) ? fa.Name : "der Fraktion")} gesucht ({SecurityNames.WantedLevel(level)})." : null;
    }

    /// <summary>Laufende Baustelle (Phase 8): nimmt nur Material an und verkauft nichts zurück; verlassene Baustellen verkaufen ihr Lager.</summary>
    static bool IsLiveSite(Station station) => station.Site is { Abandoned: false };

    /// <summary>Verkauft bis zu <paramref name="units"/> Einheiten einer einzelnen Ware (gleiche Regeln wie <see cref="SellAll"/>, Menge nach <see cref="SaleUnits"/>).</summary>
    public static SaleResult Sell(Universe u, Ship ship, Station station, GoodId good, float units)
    {
        float have = ship.Cargo.GetValueOrDefault(good);
        var sold = new Dictionary<GoodId, float>();
        if (have <= 0f || units <= 0f || !station.Market.TryGetValue(good, out var e) || e.Price <= 0f || Refusal(u, ship, station) is not null)
            return new SaleResult(0f, sold, ship.Cargo.Values.Sum());
        var (cap, allCap) = SaleCaps(u, station, good, station.Credits, null, ship);
        float n = SaleUnits(have, units, cap, allCap);
        if (n <= 0f) return new SaleResult(0f, sold, ship.Cargo.Values.Sum());
        var bs = PlayerLedger.BookSale(u, ship, station, good, n, u.WalletOf(ship));   // Runde 30/31: Rückkaufdeckel über die eine Buchung
        float pay = bs.Money;
        if (have - n <= 0.0001f) ship.Cargo.Remove(good); else ship.Cargo[good] = have - n;
        sold[good] = n;
        if (ship.IsPlayer) u.Events.Publish(new PlayerSold(ship.Id, station.Id, good, n, pay));
        if (station.IsBuildSite) ConstructionRules.OnDelivered(u, station, ship, good, n, ship.IsPlayer ? "player" : "trader");
        ExportRules.OnDelivered(u, station, good, n, pay);   // Phase 14: Exportsenke
        return new SaleResult(pay, sold, ship.Cargo.Values.Sum(), bs.Ledgered, bs.LedgerPrice, new Dictionary<GoodId, float> { [good] = pay });   // Runde 39: Erlös je Ware (Journal)
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
        if (Refusal(u, ship, station) is not null) return (0f, 0f);
        foreach (var (good, have) in ship.Cargo.OrderBy(kv => kv.Key.Value))
            if (have > 0f && station.Market.TryGetValue(good, out var e) && e.Price > 0f)
            {
                var (cap, allCap) = SaleCaps(u, station, good, credits, null, ship);
                float n = SaleUnits(have, have, cap, allCap);
                if (n <= 0f) continue;
                float r = PlayerLedger.SaleValue(u, ship, station, good, n, e.Price).Revenue;   // Runde 30
                total += r; units += n; credits -= r;
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
        var shipSize = Simulation.Docking.SizeOf(u, ship);
        // Phase 15: ohne Sortierung je Aufruf (keine Allokation); Gleichstand entscheidet die kleinere Id — dasselbe Ergebnis wie die frühere Id-Reihenfolge
        foreach (var st in u.Stations.Values)
        {
            // Baustellen bekommen nur geplante Lieferungen (Reservierung im Händler), keine Restmengen
            if (st.Id == exclude || st.IsBuildSite || !st.Market.TryGetValue(good, out var e) || e.Price <= 0f) continue;
            if (allowed is not null && !allowed(st)) continue;
            if (!Simulation.Docking.Allows(st, shipSize)) continue;   // Phase 30
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
        var shipSize = Simulation.Docking.SizeOf(u, ship);
        foreach (var st in u.Stations.Values)
        {
            if (st.Id == exclude || !Simulation.Docking.Allows(st, shipSize)) continue;   // Phase 30: Station groß genug
            float quote = Quote(u, ship, st);
            if (quote <= 0f) continue;
            float time = Travel.Estimate(u, fromSector, fromPos, st.Sector, st.DockPoint(ship.Id), ship);
            if (float.IsPositiveInfinity(time)) continue;
            float score = quote / (time + 10f);
            if (score > bestScore) { bestScore = score; best = st; }
        }
        return best;
    }

    // ───────── Runde 39: Rückgängig im Handel ─────────

    /// <summary>
    /// Runde 39: Sperrgrund für die Rücknahme der letzten Buchung (<paramref name="journal"/>) — null = möglich. Rein: liest nur.
    /// Reihenfolge der Gründe: keine Buchung, nicht mehr angedockt, anderes Schiff, Fenster abgelaufen, Ware nicht mehr an Bord /
    /// nicht mehr im Lager, Kasse reicht nicht, Station kann nicht zahlen, Baustelle, Gefecht, Rückkaufeintrag hat sich geändert.
    /// </summary>
    public static string? UndoIssue(Universe u, TradeJournal journal, Ship ship, Station station)
    {
        var e = journal.Last;
        if (e is null) return "Keine offene Buchung";
        if (e.Station != station.Id || ship.DockedAt != station.Id) return "Nicht mehr angedockt";
        if (e.Ship != ship.Id) return "Anderes Schiff";
        if (u.Tick - e.Tick > TradeJournal.WindowTicks) return $"Fenster {TradeJournal.WindowTicks} Ticks abgelaufen";
        var wallet = u.WalletOf(ship);
        foreach (var l in e.Lines)
        {
            if (!station.Market.TryGetValue(l.Good, out var me) || me.Price <= 0f) return "Rückkaufeintrag hat sich geändert";
            if (e.Kind == TradeKind.Buy)
            {
                if (ship.Cargo.GetValueOrDefault(l.Good) < l.Units - 0.0001f) return "Ware nicht mehr an Bord";
                float refund = RefundFor(u, ship, station, l);
                if (station.Credits < refund - 0.0001f) return "Station kann nicht zahlen";
            }
            else
            {
                if (me.Amount < l.Units - 0.0001f) return "Ware nicht mehr im Lager";
                // Laderaum: alle Zeilen derselben Frachtform zusammen müssen wieder hineinpassen (z. B. inzwischen aus dem Lagerhaus geholt)
                var form = u.Goods[l.Good].Form;
                float need = 0f;
                foreach (var o in e.Lines) if (u.Goods[o.Good].Form == form) need += o.Units * u.Goods[o.Good].VolumePerUnit;
                if (ship.FreeVolumeForForm(u, form) < need - 0.0001f) return "Kein Platz im Laderaum";
                float pay = PaymentFor(u, ship, station, l);
                if (wallet.Credits < pay - 0.0001f) return "Kasse reicht nicht";
            }
        }
        if (station.IsBuildSite) return "Baustelle";
        if (u.Fights.IsEngaged(ship.Id)) return "Gefecht";
        foreach (var l in e.Lines)
            if (!TradeJournal.SameOrPruned(l.PostAfter, RawPost(u, station, l.Good))) return "Rückkaufeintrag hat sich geändert";   // Runde 41 (A1): Verfall allein sperrt nicht
        return null;
    }

    /// <summary>
    /// Runde 39: Geldfluss der Rücknahme von <paramref name="e"/> (rein): Kauf zurück = Erstattung min(gezahlt, heutiger Verkaufswert
    /// mit Rückkaufdeckel), Verkauf zurück = Zahlung max(erhalten, heutige Kosten mit Rückkaufboden). Positiv = der Spieler bekommt Geld.
    /// </summary>
    public static float UndoAmount(Universe u, Ship ship, Station station, TradeEntry e)
    {
        float amount = 0f;
        foreach (var l in e.Lines)
            amount += e.Kind == TradeKind.Buy ? RefundFor(u, ship, station, l) : -PaymentFor(u, ship, station, l);
        return amount;
    }

    /// <summary>
    /// Runde 39: bucht die letzte Transaktion zurück (Ware und Geld tauschen zurück, Ledger-Posten auf das Abbild vor der Buchung,
    /// entstandene Exportchargen entfernt) und veröffentlicht <see cref="Events.TradeUndone"/>. Sperrgrund via <see cref="UndoIssue"/>:
    /// bei einem Grund ändert sich nichts und es gibt kein Ereignis.
    /// </summary>
    public static UndoResult Undo(Universe u, TradeJournal journal, Ship ship, Station station)
    {
        if (UndoIssue(u, journal, ship, station) is { } issue) return new UndoResult(false, issue, 0f, 0f);
        var e = journal.Last!;
        var wallet = u.WalletOf(ship);
        float amount = 0f, units = 0f;
        var lines = new List<UndoLine>(e.Lines.Count);
        foreach (var l in e.Lines)
        {
            var me = station.Market[l.Good];
            if (e.Kind == TradeKind.Buy)
            {
                // Kauf zurück: die Station kauft die Ware zurück — nie teurer als gezahlt und nie teurer als der Markt jetzt (Rückkaufdeckel)
                float refund = RefundFor(u, ship, station, l);
                me.Amount += l.Units;
                station.Credits -= refund;
                wallet.Credits += refund;
                float have = ship.Cargo.GetValueOrDefault(l.Good);
                if (have - l.Units <= 0.0001f) ship.Cargo.Remove(l.Good); else ship.Cargo[l.Good] = have - l.Units;
                amount += refund;
                lines.Add(new UndoLine(l.Good, l.Units, refund));
            }
            else
            {
                // Verkauf zurück: der Spieler kauft die Ware zurück — nie weniger als erhalten und nie billiger als der Markt jetzt (Rückkaufboden)
                float pay = PaymentFor(u, ship, station, l);
                me.Amount -= l.Units;
                station.Credits += pay;
                wallet.Credits -= pay;
                ship.Cargo[l.Good] = ship.Cargo.GetValueOrDefault(l.Good) + l.Units;
                amount -= pay;
                lines.Add(new UndoLine(l.Good, l.Units, -pay));
            }
            units += l.Units;
            RestorePost(u, station, l.Good, l.PostBefore);
        }
        foreach (var b in e.Batches) station.Exports.Remove(b);   // entstandene, noch nicht fällige Exportcharge entfernen
        SpaceSim.Core.Missions.MissionRules.RollbackProgress(u, e.Progress);   // Runde 41: Auftragsfortschritt der Buchung zurücknehmen
        journal.Clear();
        u.Events.Publish(new Events.TradeUndone(ship.Id, station.Id, lines, amount));
        return new UndoResult(true, null, amount, units);
    }

    /// <summary>Runde 39: Erstattung für die Rücknahme eines Kaufs — min(gezahlt, heutiger Verkaufswert dieser Menge mit Rückkaufdeckel).</summary>
    static float RefundFor(Universe u, Ship ship, Station station, TradeLine l)
    {
        float price = station.Market[l.Good].Price;
        float now = PlayerLedger.SaleValue(u, ship, station, l.Good, l.Units, price).Revenue;
        return MathF.Min(l.Amount, now);
    }

    /// <summary>Runde 39: Zahlung für die Rücknahme eines Verkaufs — max(erhalten, heutige Kosten dieser Menge mit Rückkaufboden).</summary>
    static float PaymentFor(Universe u, Ship ship, Station station, TradeLine l)
    {
        float price = station.Market[l.Good].Price;
        float now = PlayerLedger.BuyCost(u, ship, station, l.Good, l.Units, price).Cost;
        return MathF.Max(l.Amount, now);
    }

    static LedgerPost? RawPost(Universe u, Station st, GoodId good)
    {
        var key = (u.PlayerActor.Value, st.Id.Value, good.Value);
        return u.Ledger.TryGetValue(key, out var p) ? p : null;
    }

    /// <summary>Runde 39: Ledger-Eintrag auf das Abbild vor der Buchung zurück (null = Eintrag entfernen).</summary>
    static void RestorePost(Universe u, Station st, GoodId good, LedgerPost? before)
    {
        var key = (u.PlayerActor.Value, st.Id.Value, good.Value);
        if (before is null) { u.Ledger.Remove(key); return; }
        if (u.Ledger.TryGetValue(key, out var p)) CopyInto(before, p);
        else u.Ledger[key] = TradeJournal.Copy(before);
    }

    static void CopyInto(LedgerPost from, LedgerPost to)
    {
        to.BoughtQty = from.BoughtQty; to.BoughtPrice = from.BoughtPrice; to.BoughtStockBefore = from.BoughtStockBefore;
        to.SoldQty = from.SoldQty; to.SoldPrice = from.SoldPrice; to.SoldStockBefore = from.SoldStockBefore;
        to.BoughtOpen = from.BoughtOpen; to.SoldOpen = from.SoldOpen; to.LastTick = from.LastTick;
        to.MissingQty = from.MissingQty; to.MissingSeen = from.MissingSeen;
    }
}
