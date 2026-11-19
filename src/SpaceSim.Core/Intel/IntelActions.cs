using SpaceSim.Core.Comms;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;

namespace SpaceSim.Core.Intel;

/// <summary>
/// Ausbau Punkt 16: Comms-Aktion „Intel kaufen“ (Diplomatie-Detail). Schlüssel streng: <c>intel:&lt;F&gt;:&lt;Paket&gt;</c> mit Paket
/// <c>sector:&lt;S&gt;</c>, <c>territory</c> oder <c>stations:&lt;Typ&gt;</c> (<see cref="IntelMarket"/>).
/// </summary>
public sealed class IntelAction : ICommsAction
{
    public const string KeyId = "intel";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    /// <summary>Zerlegt „intel:F:paket“ streng.</summary>
    public static bool TryParse(string offer, out FactionId f, out string package)
    {
        f = default; package = "";
        if (!offer.StartsWith(KeyId + ":", StringComparison.Ordinal)) return false;
        var tail = offer[(KeyId.Length + 1)..];
        int c = tail.IndexOf(':');
        if (c <= 0 || !int.TryParse(tail[..c], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var fv) || fv <= 0) return false;
        f = new FactionId(fv); package = tail[(c + 1)..];
        return package.Length > 0;
    }

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (!Knowledge.Limited(u)) return list;
        foreach (var f in u.NpcFactionIds())
            foreach (var p in IntelMarket.Packages(u, f)) list.Add(ToOffer(f, p));
        return list;
    }

    /// <summary>Angebote einer Fraktion (Diplomatie-Detail).</summary>
    public static IReadOnlyList<CommsOffer> OffersOf(Universe u, FactionId f) => IntelMarket.Packages(u, f).Select(p => ToOffer(f, p)).ToList();

    static CommsOffer ToOffer(FactionId f, IntelPackage p) =>
        new($"{KeyId}:{f.Value}:{p.Key}", $"{p.Text} – {p.Price:0} Cr", $"{p.Detail}. Geld an die Fraktionskasse.", -p.Price, p.Issue is null, p.Issue);

    public CommsOffer Preview(Universe u, string offer)
    {
        if (!TryParse(offer, out var f, out var pkg)) return new(offer, "Intel", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
        if (IntelMarket.FactionIssue(u, f) is { } fi) return new(offer, "Intel", fi, 0f, false, fi);
        return IntelMarket.Preview(u, f, pkg) is { } p ? ToOffer(f, p) : new(offer, "Intel", "Nichts Neues.", 0f, false, "Dieses Paket gibt es nicht (oder es ist nichts Neues darin).");
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (!TryParse(offer, out var f, out var pkg)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
        float price = IntelMarket.Preview(u, f, pkg)?.Price ?? 0f;
        if (!IntelMarket.Buy(u, f, pkg, out var msg)) return new ServiceResult(false, 0f, 0f, msg);
        u.Events.Publish(new CommsActionPerformed(KeyId, f.Value, -price, msg));
        return new ServiceResult(true, 1f, -price, msg);
    }
}

/// <summary>
/// Ausbau Punkt 16: Dienst „intel“ an Behörden (Stationen mit Gericht oder Kopfgeldbüro): Intel-Pakete der Fraktion der Station. Schlüssel streng
/// <c>buy:&lt;Paket&gt;</c>; nur eigene Schiffe des Spielers, nur bei beschränktem Wissen.
/// </summary>
public sealed class IntelService : IStationService
{
    public const string ServiceKey = "intel";
    public string Key => ServiceKey;

    public bool IsAvailable(Universe u, Station station) =>
        Knowledge.Limited(u) && !station.IsBuildSite && u.FactionOf(station) is { } f && u.IsNpcFaction(f)
        && u.StationTypes.TryGetValue(station.TypeKey, out var t) && (t.Services.Contains(CourtService.ServiceKey) || t.Services.Contains(BountyService.ServiceKey));

    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var list = new List<ServiceOffer>();
        if (!ship.IsPlayer || !IsAvailable(u, station) || u.FactionOf(station) is not { } f) return list;
        foreach (var p in IntelMarket.Packages(u, f))
            list.Add(new ServiceOffer($"buy:{p.Key}", $"{p.Text}: {p.Detail}", p.Price, p.Issue is null ? 1f : 0f, default, p.Items, 0f, p.Issue));
        return list;
    }

    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        if (!request.Offer.StartsWith("buy:", StringComparison.Ordinal) || request.Offer.Length <= 4) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{request.Offer}'.");
        if (!ship.IsPlayer) return new ServiceResult(false, 0f, 0f, "Nur für eigene Schiffe.");
        if (!IsAvailable(u, station) || u.FactionOf(station) is not { } f) return new ServiceResult(false, 0f, 0f, "Hier gibt es keine Intel.");
        string pkg = request.Offer[4..];
        float price = IntelMarket.Preview(u, f, pkg)?.Price ?? 0f;
        if (!IntelMarket.Buy(u, f, pkg, out var msg)) return new ServiceResult(false, 0f, 0f, msg);
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, ServiceKey, request.Offer, default, 1f, -price));
        return new ServiceResult(true, 1f, -price, msg);
    }
}
