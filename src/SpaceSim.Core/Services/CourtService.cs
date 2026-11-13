using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Services;

/// <summary>
/// Gericht (Phase 11, STATIONEN.md „Gericht / Haftanstalt“), Dienst „court“ an Regierungsstationen: Der Spieler zahlt dort seine Strafe und löscht
/// damit den Fahndungseintrag (<see cref="Wanted.FineOf"/>, Geld an die Kasse der Fraktion). Angenommen werden Strafen der eigenen Fraktion der Station
/// und — Rechtshilfe — von Fraktionen, deren Beziehung zu ihr mindestens <see cref="EconomyRules.ExtraditionRelation"/> ist. Angebots-Schlüssel streng:
/// <c>pay:&lt;FactionId&gt;</c>.
/// </summary>
public sealed class CourtService : IStationService
{
    public const string ServiceKey = "court";
    public const string Pay = "pay";

    public string Key => ServiceKey;

    /// <summary>Verfügbar an fertigen Stationen, deren Besitzer zu einer Fraktion gehört.</summary>
    public bool IsAvailable(Universe u, Station station) => !station.IsBuildSite && u.FactionOf(station) is { } f && u.Factions.ContainsKey(f);

    /// <summary>
    /// Gerätetest Phase 28 (Runde 4): nächste Station mit Kopfgeldbüro der Fraktion <paramref name="faction"/> (der Anspruch gilt dort), gezählt in Sprüngen
    /// ab <paramref name="from"/>, dann nach Id; null = keine. Für die Meldung „Kopfgeld abholen: Behörde in …“.
    /// </summary>
    public static Station? NearestOffice(Universe u, SectorId from, FactionId faction)
    {
        Station? best = null; int bestHops = int.MaxValue;
        foreach (var st in u.StationsById())
        {
            if (st.IsBuildSite || u.FactionOf(st) != faction || !u.StationTypes.TryGetValue(st.TypeKey, out var t) || !t.Services.Contains(BountyService.ServiceKey)) continue;
            int hops = st.Sector == from ? 0 : u.FindRoute(from, st.Sector)?.Count ?? int.MaxValue;
            if (hops < bestHops) { bestHops = hops; best = st; }
        }
        return best;
    }

    /// <summary>Meldungstext zum Kopfgeld mit der nächsten Behörde („Kopfgeld 150 Cr abholen: Behörde in Sektor-HQ Delta.“).</summary>
    public static string Hint(Universe u, SectorId from, FactionId faction, float bounty) =>
        NearestOffice(u, from, faction) is { } st ? $" Kopfgeld {bounty:0} Cr abholen: Behörde in {st.Name}." : $" Kopfgeld {bounty:0} Cr beim Kopfgeldbüro abholen.";

    /// <summary>Nimmt das Gericht dieser Station Strafen der Fraktion an?</summary>
    public static bool Accepts(Universe u, Station station, FactionId faction) =>
        u.FactionOf(station) is { } own && (own == faction || Relations.Effective(u, faction, own) >= u.Rules.ExtraditionRelation);

    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var offers = new List<ServiceOffer>();
        if (!u.Actors.TryGetValue(ship.Owner, out var actor)) return offers;
        float credits = u.WalletOf(ship).Credits;
        foreach (var (f, w) in actor.Wanted.OrderBy(kv => kv.Key.Value))
        {
            if (!Accepts(u, station, f)) continue;
            float fine = Wanted.FineOf(u, w);
            string name = u.Factions.TryGetValue(f, out var fac) ? fac.Name : $"Fraktion {f.Value}";
            int level = Wanted.Level(u.Rules, w.Heat);
            offers.Add(new ServiceOffer($"{Pay}:{f.Value}", $"{name}: {SecurityNames.WantedLevel(level)} ({SecurityNames.Crime(w.LastCrime)})",
                fine, credits >= fine ? 1f : 0f, default, level, w.Heat));
        }
        return offers;
    }

    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        if (!TryParse(request.Offer, out var faction)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{request.Offer}'.");
        if (!IsAvailable(u, station)) return new ServiceResult(false, 0f, 0f, "Kein Gericht an dieser Station.");
        if (!ship.IsPlayer) return new ServiceResult(false, 0f, 0f, "Nur für eigene Schiffe.");
        if (!u.Actors.TryGetValue(ship.Owner, out var actor) || !actor.Wanted.TryGetValue(faction, out var w))
            return new ServiceResult(false, 0f, 0f, "Keine Strafe offen.");
        if (!Accepts(u, station, faction)) return new ServiceResult(false, 0f, 0f, "Dieses Gericht ist nicht zuständig.");
        float fine = Wanted.FineOf(u, w);
        var wallet = u.WalletOf(ship);
        if (wallet.Credits < fine) return new ServiceResult(false, 0f, 0f, $"Strafe {fine:0} Cr — zu wenig Credits.");
        wallet.Credits -= fine;
        if (u.Actors.TryGetValue(faction, out var authority))
        {
            authority.Treasury.Credits += fine;
            u.Events.Publish(new TreasuryIncome(authority.Id, "fine", fine));
        }
        Wanted.SetHeat(u, actor, faction, 0f, 0f);
        u.Events.Publish(new FinePaid(actor.Id, faction, fine));
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Pay, default, 1f, -fine));
        string name = u.Factions.TryGetValue(faction, out var fac) ? fac.Name : "Fraktion";
        return new ServiceResult(true, 1f, -fine, $"Strafe {fine:0} Cr an {name} bezahlt — Fahndung gelöscht.");
    }

    static bool TryParse(string offer, out FactionId faction)
    {
        faction = default;
        if (!offer.StartsWith(Pay + ":", StringComparison.Ordinal)) return false;
        string tail = offer[(Pay.Length + 1)..];
        if (tail.Length == 0 || tail.Any(c => c is < '0' or > '9') || !int.TryParse(tail, out int v) || v <= 0) return false;
        faction = new FactionId(v);
        return true;
    }
}

/// <summary>
/// Kopfgeldbüro (Phase 11, STATIONEN.md „Kopfgeldbüro“), Dienst „bounty“ an Regierungsstationen: zahlt dem Spieler sein verdientes Kopfgeld
/// (<see cref="Actor.BountyClaims"/>, aus besiegten Kaperschiffen) aus der Kasse der Fraktion der Station aus (höchstens, was die Kasse hat) und zeigt die
/// Fahndungsliste der Fraktion (Gesuchte mit Kopfgeld je Schiff; reine Anzeige). Angebots-Schlüssel streng: <c>claim:&lt;FactionId&gt;</c>.
/// </summary>
public sealed class BountyService : IStationService
{
    public const string ServiceKey = "bounty";
    public const string Claim = "claim";
    public const string Info = "wanted";

    public string Key => ServiceKey;

    public bool IsAvailable(Universe u, Station station) => !station.IsBuildSite && u.FactionOf(station) is { } f && u.Factions.ContainsKey(f);

    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var offers = new List<ServiceOffer>();
        if (u.FactionOf(station) is not { } f) return offers;
        float treasury = u.Actors.TryGetValue(f, out var authority) ? authority.Treasury.Credits : 0f;
        if (u.Actors.TryGetValue(ship.Owner, out var hunter) && hunter.BountyClaims.TryGetValue(f, out var claim) && claim > 0f)
            offers.Add(new ServiceOffer($"{Claim}:{f.Value}", "Kopfgeld abholen", claim, treasury >= 1f ? 1f : 0f, default, 0f, 0f));
        // Fahndungsliste: Akteure mit Eintrag bei dieser Fraktion (ohne den Spieler selbst), Kopfgeld je Schiff
        foreach (var a in u.Actors.Values.Where(a => a.Id != ship.Owner && a.Wanted.ContainsKey(f)).OrderByDescending(a => a.Wanted[f].Heat).ThenBy(a => a.Id.Value))
        {
            int level = Wanted.Level(u.Rules, a.Wanted[f].Heat);
            offers.Add(new ServiceOffer($"{Info}:{a.Id.Value}", $"Gesucht: {a.Name} — {SecurityNames.WantedLevel(level)}", Wanted.BountyOn(u, a.Id, f), 0f, default, level, a.Wanted[f].Heat));
        }
        return offers;
    }

    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        if (!request.Offer.StartsWith(Claim + ":", StringComparison.Ordinal)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{request.Offer}'.");
        string tail = request.Offer[(Claim.Length + 1)..];
        if (tail.Length == 0 || tail.Any(c => c is < '0' or > '9') || !int.TryParse(tail, out int v) || v <= 0)
            return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{request.Offer}'.");
        var f = new FactionId(v);
        if (!IsAvailable(u, station) || u.FactionOf(station) != f) return new ServiceResult(false, 0f, 0f, "Dieses Büro zahlt nur Kopfgeld seiner Fraktion.");
        if (!ship.IsPlayer) return new ServiceResult(false, 0f, 0f, "Nur für eigene Schiffe.");
        if (!u.Actors.TryGetValue(ship.Owner, out var hunter) || !hunter.BountyClaims.TryGetValue(f, out var claim) || claim <= 0f)
            return new ServiceResult(false, 0f, 0f, "Kein Kopfgeld offen.");
        if (!u.Actors.TryGetValue(f, out var authority) || authority.Treasury.Credits < 1f) return new ServiceResult(false, 0f, 0f, "Die Kasse ist leer.");
        float pay = MathF.Min(claim, authority.Treasury.Credits);
        authority.Treasury.Credits -= pay;
        u.WalletOf(ship).Credits += pay;
        if (claim - pay <= 0.01f) hunter.BountyClaims.Remove(f); else hunter.BountyClaims[f] = claim - pay;
        u.Events.Publish(new TreasuryUpkeep(authority.Id, "bounty", pay, 0f));
        u.Events.Publish(new BountyPaid(hunter.Id, f, pay));
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Claim, default, 1f, pay));
        return new ServiceResult(true, 1f, pay, $"Kopfgeld {pay:0} Cr ausgezahlt.");
    }
}
