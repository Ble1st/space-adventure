using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Bars;

/// <summary>Ein Gerücht der Bar: Text, Art und — bei kaufbaren Gerüchten — die Station, die der Kauf bekannt macht.</summary>
public readonly record struct BarRumour(string Kind, string Text, StationId Reveal = default)
{
    public bool Paid => Reveal.Value != 0;
}

/// <summary>
/// Regeln der Bars (rein bis auf die Buchungen in <see cref="BuyTicket"/>, <see cref="Reveal"/>, <see cref="Claim"/>, <see cref="BuyRumour"/>; docs/BARS.md).
/// Nur der Spieler handelt hier; ohne Spieleraktion entsteht kein Zustand.
/// </summary>
public static class Bars
{
    public const string ServiceKey = "bar";

    /// <summary>Hat die Station eine Bar? (Schalter, Typliste, andockbar, fertig.)</summary>
    public static bool IsAt(Universe u, Station st) =>
        u.Rules.Bars && st.Dockable && !st.IsBuildSite && u.Rules.BarStationTypes.Contains(st.TypeKey);

    /// <summary>Offenes Los des Spielers; ein Los einer verschwundenen Station gilt als keins.</summary>
    public static BarTicket? OpenTicket(Universe u) =>
        u.Bar?.Ticket is { } t && u.Stations.ContainsKey(t.Station) ? t : null;

    static long Day(Universe u) => u.Tick / Math.Max(1, u.Rules.DayTicks);

    /// <summary>Lose, die an dieser Station heute noch zu haben sind.</summary>
    public static int TicketsLeft(Universe u, Station st)
    {
        int sold = u.Bar is { } b && b.Stations.TryGetValue(st.Id.Value, out var s) && s.Day == Day(u) ? s.Sold : 0;
        return Math.Max(0, u.Rules.BarTicketsPerDay - sold);
    }

    /// <summary>Darf hier ein Los gekauft werden? null = ja, sonst Grund.</summary>
    public static string? TicketIssue(Universe u, Station st, Ship ship)
    {
        if (!IsAt(u, st)) return "Keine Bar an dieser Station.";
        if (!ship.IsPlayer) return "Nur für eigene Schiffe.";
        if (OpenTicket(u) is { } t)
            return t.Station == st.Id ? "Erst das offene Los aufdecken und abgeben." : $"Offenes Los bei {StationNames.Short(u, u.Stations[t.Station])} — dort aufdecken.";
        if (TicketsLeft(u, st) <= 0) return $"Heute keine Lose mehr (höchstens {u.Rules.BarTicketsPerDay} am Tag).";
        if (st.Credits < u.Rules.BarMinStationCash) return "Die Bar verkauft keine Lose: die Stationskasse ist zu knapp.";
        if (u.WalletOf(ship).Credits < u.Rules.BarTicketPrice) return "Nicht genügend Credits.";
        return null;
    }

    /// <summary>Kauft ein Los: Preis an die Station, Ausgang sofort gezogen (eigener Zufall). null bei Sperrgrund.</summary>
    public static BarTicket? BuyTicket(Universe u, Station st, Ship ship, out string message)
    {
        if (TicketIssue(u, st, ship) is { } issue) { message = issue; return null; }
        var bar = u.Bar ??= new BarState();
        if (!bar.Stations.TryGetValue(st.Id.Value, out var s)) bar.Stations[st.Id.Value] = s = new BarStationState();
        long day = Day(u);
        if (s.Day != day) { s.Day = day; s.Sold = 0; }
        var (tier, prize, symbols) = Scratchcard.Draw(Scratchcard.Rng(u, u.Tick, st.Id, s.Counter), u.Rules);
        s.Sold++;
        s.Counter++;
        float price = u.Rules.BarTicketPrice;
        u.WalletOf(ship).Credits -= price;
        st.Credits += price;
        var t = new BarTicket { Station = st.Id, Bought = u.Tick, Price = price, Tier = tier, Prize = prize, Symbols = symbols };
        bar.Ticket = t;
        message = $"Rubbellos gekauft ({price:0} Cr) — Felder antippen zum Aufdecken.";
        return t;
    }

    /// <summary>Deckt ein Feld (oder mit −1 alle) auf. false, wenn es kein offenes Los hier gibt oder das Feld schon offen ist.</summary>
    public static bool Reveal(Universe u, Station st, int field)
    {
        if (OpenTicket(u) is not { } t || t.Station != st.Id) return false;
        if (field < 0) { if (t.AllRevealed) return false; t.Revealed = (1 << Scratchcard.Fields) - 1; return true; }
        if (field >= Scratchcard.Fields || t.IsRevealed(field)) return false;
        t.Revealed |= 1 << field;
        return true;
    }

    /// <summary>Auszahlung, die die Station jetzt leisten kann: der Gewinn, höchstens die Stationskasse (Rest verfällt — so steht es auf dem Los).</summary>
    public static float Payout(Universe u, BarTicket t) =>
        t.Prize <= 0 || !u.Stations.TryGetValue(t.Station, out var st) ? 0f : MathF.Min(t.Prize, MathF.Max(0f, st.Credits));

    /// <summary>Gibt ein ganz aufgedecktes Los ab und zahlt den Gewinn aus der Stationskasse. Rückgabe: ausgezahlte Credits (−1 = nicht möglich).</summary>
    public static float Claim(Universe u, Station st, Ship ship, out string message)
    {
        if (OpenTicket(u) is not { } t || t.Station != st.Id) { message = "Kein offenes Los an dieser Bar."; return -1f; }
        if (!t.AllRevealed) { message = "Erst alle Felder aufdecken."; return -1f; }
        float pay = Payout(u, t);
        st.Credits -= pay;
        u.WalletOf(ship).Credits += pay;
        u.Bar!.Ticket = null;
        message = t.Prize <= 0 ? "Niete — leider kein Dreier."
            : pay + 0.001f < t.Prize ? $"Gewinn 3× {Scratchcard.SymbolName(t.Tier)}: die Bar zahlt nur {pay:0} von {t.Prize} Cr (Kasse leer)."
            : $"Gewinn 3× {Scratchcard.SymbolName(t.Tier)}: {pay:0} Cr ausgezahlt.";
        return pay;
    }

    // ── Gerüchte ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gerüchte an dieser Bar (rein, aus dem Universum berechnet, nur über bekannte Orte): knappe Ware mit gutem Abnehmer, laufendes Universumsereignis,
    /// Jagdsektor einer Piratengruppe und — bei beschränktem Wissen — eine unbekannte Station (kaufbar, macht sie bekannt).
    /// </summary>
    public static List<BarRumour> Rumours(Universe u, Station bar)
    {
        var list = new List<BarRumour>(4);
        if (!IsAt(u, bar)) return list;
        // 1) knappe Ware: höchstes Preis/Basis-Verhältnis bei einer bekannten Station, die die Ware nimmt
        (float ratio, Station? st, GoodId good) best = (1.4f, null, default);
        foreach (var st in u.StationsById())
        {
            if (st.IsBuildSite || !st.Dockable || st.Id == bar.Id || !Knowledge.KnowsStation(u, st)) continue;
            foreach (var (g, e) in st.Market)
            {
                if (!u.Goods.TryGetValue(g, out var good) || good.BasePrice <= 0f || e.Price <= 0f) continue;
                if (!st.Consumes(g) && !st.IsStockOnly(g)) continue;
                if (st.AcceptableUnits(e, u.Rules) < 1f) continue;
                float ratio = e.Price / good.BasePrice;
                if (ratio > best.ratio)
                    best = (ratio, st, g);
            }
        }
        if (best.st is { } bs)
            list.Add(new BarRumour("scarce", $"Ein Frachterkapitän erzählt: {StationNames.Short(u, bs)} ({Knowledge.SectorName(u, bs.Sector)}) zahlt für {u.Goods[best.good].Name} gerade das {best.ratio.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',')}-fache."));
        // 2) laufendes Universumsereignis in einem bekannten Sektor
        foreach (var e in u.UniverseEvents)
            if (Knowledge.KnowsSector(u, e.Sector))
            {
                list.Add(new BarRumour("event", $"Man hört von {UniverseEventInfo.Name(e.Kind)} in {Knowledge.SectorName(u, e.Sector)} — noch {UniverseEventInfo.Rest(u, e)}."));
                break;
            }
        // 3) Jagdsektor einer Piratengruppe (lauernder Kaperer)
        foreach (var s in u.ShipsById())
            if (s.Role == ShipRole.Pirate && s.Command is LurkCommand lurk && Knowledge.KnowsSector(u, lurk.Sector))
            {
                string who = Knowledge.KnowsActor(u, s.Owner) ? ActorNames.Tagged(u, s.Owner) : "Piraten";
                list.Add(new BarRumour("raiders", $"Vorsicht: {who} lauern in {Knowledge.SectorName(u, lurk.Sector)}."));
                break;
            }
        // 4) unbekannte Station (nur bei beschränktem Wissen): die nächste nach Sprüngen, kaufbar
        if (UnknownStation(u, bar) is { } unk)
        {
            string where = Knowledge.KnowsSector(u, unk.Sector) ? $"in {Knowledge.SectorName(u, unk.Sector)}" : "jenseits deiner Karte";
            list.Add(new BarRumour("station", $"Ein Prospektor kennt eine Station {where}, die du noch nicht kennst — für {u.Rules.BarRumourFee:0} Cr verrät er sie.", unk.Id));
        }
        if (list.Count == 0) list.Add(new BarRumour("quiet", "Ruhiger Abend — niemand weiß etwas Neues."));
        return list;
    }

    /// <summary>Nächste (wenigste Sprünge, dann Id) andockbare, fertige Station, die der Spieler nicht kennt; null ohne beschränktes Wissen.</summary>
    public static Station? UnknownStation(Universe u, Station bar)
    {
        if (!Knowledge.Limited(u)) return null;
        Station? best = null;
        int bestHops = int.MaxValue;
        foreach (var st in u.StationsById())
        {
            if (st.IsBuildSite || !st.Dockable || Knowledge.KnowsStation(u, st)) continue;
            int hops = st.Sector == bar.Sector ? 0 : u.Hops(bar.Sector, st.Sector);
            if (hops < 0) continue;
            if (hops < bestHops) { bestHops = hops; best = st; }
        }
        return best;
    }

    /// <summary>Darf das Gerücht über diese Station gekauft werden? null = ja.</summary>
    public static string? RumourIssue(Universe u, Station bar, Ship ship, StationId target)
    {
        if (!IsAt(u, bar)) return "Keine Bar an dieser Station.";
        if (!ship.IsPlayer) return "Nur für eigene Schiffe.";
        if (UnknownStation(u, bar) is not { } unk || unk.Id != target) return "Dieses Gerücht gibt es hier nicht (mehr).";
        if (u.WalletOf(ship).Credits < u.Rules.BarRumourFee) return "Nicht genügend Credits.";
        return null;
    }

    /// <summary>Kauft das Gerücht: Gebühr an die Station, die Station wird bekannt (Intel-Ereignis „bar“).</summary>
    public static bool BuyRumour(Universe u, Station bar, Ship ship, StationId target, out string message)
    {
        if (RumourIssue(u, bar, ship, target) is { } issue) { message = issue; return false; }
        float fee = u.Rules.BarRumourFee;
        u.WalletOf(ship).Credits -= fee;
        bar.Credits += fee;
        var st = u.Stations[target];
        Knowledge.Publish(u, st.Sector, Knowledge.RevealStation(u, target), "bar");
        message = $"Gerücht gekauft ({fee:0} Cr): {StationNames.Short(u, st)} in {Knowledge.SectorName(u, st.Sector)} ist jetzt bekannt.";
        return true;
    }

    /// <summary>Söldneranbieter der Bar: der Besitzer der Station, wenn er Söldner stellt (Gilde oder Unabhängiger), sonst null.</summary>
    public static ActorId? MercProvider(Universe u, Station bar) =>
        u.Rules.Mercenaries && Mercenaries.Providers(u).Any(p => p.Id == bar.Owner) ? bar.Owner : null;
}
