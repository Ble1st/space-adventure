using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Bars;

/// <summary>
/// Dienst „bar“ (docs/BARS.md) an bewohnten Stationen: Rubbellos, kaufbares Gerücht, Söldner-Abkürzung. Angebots-Schlüssel streng:
/// <c>ticket</c>, <c>reveal:&lt;0..8&gt;</c>, <c>reveal:all</c>, <c>claim</c>, <c>rumour:&lt;StationId&gt;</c>, <c>merc:&lt;Tage&gt;</c>.
/// Angebotsmenge &gt; 0 ⇔ <see cref="Execute"/> gelingt; <see cref="ServicePerformed"/> nur bei Erfolg. Nur der Spieler.
/// </summary>
public sealed class BarService : IStationService
{
    public const string ServiceKey = Bars.ServiceKey;
    public const string Ticket = "ticket";
    public const string RevealKey = "reveal";
    public const string All = "all";
    public const string Claim = "claim";
    public const string Rumour = "rumour";
    public const string Merc = "merc";

    public string Key => ServiceKey;

    public bool IsAvailable(Universe u, Station station) => Bars.IsAt(u, station);

    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var offers = new List<ServiceOffer>();
        if (!ship.IsPlayer || !Bars.IsAt(u, station)) return offers;
        var r = u.Rules;
        var t = Bars.OpenTicket(u);
        if (t is not null && t.Station == station.Id)
        {
            for (int i = 0; i < Scratchcard.Fields; i++)
                if (!t.IsRevealed(i)) offers.Add(new ServiceOffer($"{RevealKey}:{i}", $"Feld {i + 1} aufdecken", 0f, 1f, default, 0f, 0f));
            if (!t.AllRevealed) offers.Add(new ServiceOffer($"{RevealKey}:{All}", "Alle aufdecken", 0f, 1f, default, 0f, 0f));
            float pay = Bars.Payout(u, t);
            offers.Add(new ServiceOffer(Claim, t.Prize > 0 ? "Gewinn abholen" : "Los abgeben", pay, t.AllRevealed ? 1f : 0f, default, 0f, 0f,
                t.AllRevealed ? null : "Erst alle Felder aufdecken"));
        }
        else
        {
            string? issue = Bars.TicketIssue(u, station, ship);
            offers.Add(new ServiceOffer(Ticket, "Rubbellos kaufen", r.BarTicketPrice, issue is null ? 1f : 0f, default, Bars.TicketsLeft(u, station), 0f, issue));
        }
        foreach (var rm in Bars.Rumours(u, station))
        {
            if (!rm.Paid) continue;
            string? issue = Bars.RumourIssue(u, station, ship, rm.Reveal);
            offers.Add(new ServiceOffer($"{Rumour}:{rm.Reveal.Value}", "Gerücht kaufen", r.BarRumourFee, issue is null ? 1f : 0f, default, 0f, 0f, issue));
        }
        if (Bars.MercProvider(u, station) is { } provider)
            foreach (int days in r.MercenaryDays)
            {
                string? issue = Mercenaries.HireIssue(u, provider, days);
                offers.Add(new ServiceOffer($"{Merc}:{days}", $"Söldner anheuern: {days} Tag{(days == 1 ? "" : "e")}", r.MercenaryFeePerDay, issue is null ? 1f : 0f, default, 0f, 0f, issue));
            }
        return offers;
    }

    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        string key = request.Offer ?? "";
        if (!Valid(key)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{key}'.");
        var offer = Offers(u, station, ship).FirstOrDefault(o => o.Key == key);
        if (offer.Key is null) return new ServiceResult(false, 0f, 0f, ship.IsPlayer && Bars.IsAt(u, station) ? "Dieses Angebot gibt es hier gerade nicht." : "Keine Bar an dieser Station.");
        if (offer.Amount <= 0f) return new ServiceResult(false, 0f, 0f, offer.Note ?? "Nicht möglich");
        string message;
        float credits;
        if (key == Ticket)
        {
            if (Bars.BuyTicket(u, station, ship, out message) is null) return new ServiceResult(false, 0f, 0f, message);
            credits = -u.Rules.BarTicketPrice;
        }
        else if (key.StartsWith(RevealKey + ":"))
        {
            string tail = key[(RevealKey.Length + 1)..];
            int field = tail == All ? -1 : int.Parse(tail);
            if (!Bars.Reveal(u, station, field)) return new ServiceResult(false, 0f, 0f, "Feld schon offen.");
            var t = Bars.OpenTicket(u)!;
            message = t.AllRevealed ? (t.Prize > 0 ? $"Drei gleiche: {Scratchcard.SymbolName(t.Tier)} — {t.Prize} Cr! „Gewinn abholen“." : "Alles aufgedeckt — leider kein Dreier.")
                : $"{t.RevealedCount} von {Scratchcard.Fields} Feldern offen.";
            credits = 0f;
        }
        else if (key == Claim)
        {
            credits = Bars.Claim(u, station, ship, out message);
            if (credits < 0f) return new ServiceResult(false, 0f, 0f, message);
        }
        else if (key.StartsWith(Rumour + ":"))
        {
            if (!Bars.BuyRumour(u, station, ship, new StationId(int.Parse(key[(Rumour.Length + 1)..])), out message)) return new ServiceResult(false, 0f, 0f, message);
            credits = -u.Rules.BarRumourFee;
        }
        else
        {
            int days = int.Parse(key[(Merc.Length + 1)..]);
            if (Bars.MercProvider(u, station) is not { } provider || Mercenaries.Hire(u, provider, days, out message) is null)
                return new ServiceResult(false, 0f, 0f, "Kein Söldner verfügbar.");
            credits = -u.Rules.MercenaryFeePerDay;
        }
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, key, default, 1f, credits));
        return new ServiceResult(true, 1f, credits, message);
    }

    /// <summary>Strenge Schlüsselprüfung (nur die oben genannten Formen, Ziffern ohne Vorzeichen).</summary>
    public static bool Valid(string key)
    {
        if (key == Ticket || key == Claim || key == $"{RevealKey}:{All}") return true;
        static bool Digits(string s) => s.Length is > 0 and < 9 && s.All(c => c is >= '0' and <= '9');
        if (key.StartsWith(RevealKey + ":")) { string t = key[(RevealKey.Length + 1)..]; return t.Length == 1 && Digits(t) && t[0] - '0' < Scratchcard.Fields; }
        if (key.StartsWith(Rumour + ":")) { string t = key[(Rumour.Length + 1)..]; return Digits(t) && int.Parse(t) > 0; }
        if (key.StartsWith(Merc + ":")) { string t = key[(Merc.Length + 1)..]; return Digits(t) && int.Parse(t) > 0; }
        return false;
    }
}
