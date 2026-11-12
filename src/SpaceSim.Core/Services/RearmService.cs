using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Services;

/// <summary>
/// Phase 25 (KAMPF.md 3.4): Dienst „rearm“ — Munition aus dem Stationslager ins Magazin des Schiffs zum Marktpreis (Buchung wie bei NPCs über
/// <see cref="Rearm.AtStation"/>: Lager, Stationskasse, Kasse des Schiffs über <see cref="Universe.WalletOf"/>). Strikte Schlüssel wie „refuel“:
/// „rearm:fill“ (Magazin voll) und „rearm:10“/„rearm:30“. Verfügbar an Stationen, die Munition führen, solange Munition eingeschaltet ist
/// (<see cref="EconomyRules.AmmoActive"/>). Fehlschläge ändern nichts und lösen kein Ereignis aus; Erfolg veröffentlicht <see cref="ServicePerformed"/>.
/// </summary>
public sealed class RearmService : IStationService
{
    public const string ServiceKey = "rearm";
    public const string Fill = "fill";
    public static readonly int[] Tiers = { 10, 30 };

    public string Key => ServiceKey;

    public bool IsAvailable(Universe u, Station station) =>
        u.Rules.AmmoActive && !station.IsBuildSite && Rearm.AmmoGood(u) is { } g && station.Market.ContainsKey(g.Id);

    static float Price(Universe u, StockEntry e, Good g) => e.Price > 0f ? e.Price : g.BasePrice;

    /// <summary>Was jetzt geladen würde: fehlende Munition, Lagerbestand, bezahlbare Menge (ganze Einheiten, außer dem letzten Rest des Magazins).</summary>
    static float Deliverable(Universe u, Station st, Ship ship, float want)
    {
        if (Rearm.AmmoGood(u) is not { } g || !st.Market.TryGetValue(g.Id, out var e)) return 0f;
        float price = Price(u, e, g);
        float units = MathF.Min(MathF.Min(Rearm.Missing(u, ship), want), e.Amount);
        if (price > 0f) units = MathF.Min(units, MathF.Floor(MathF.Max(0f, u.WalletOf(ship).Credits) / price));
        return MathF.Max(0f, units);
    }

    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var list = new List<ServiceOffer>();
        if (!IsAvailable(u, station) || Rearm.AmmoGood(u) is not { } g || !station.Market.TryGetValue(g.Id, out var e)) return list;
        float price = Price(u, e, g), held = ship.AmmoLoaded;
        string? note = ship.Stats.AmmoCapacity <= 0f ? "Kein Magazin an Bord." : Rearm.Missing(u, ship) <= 0.001f ? "Magazin ist voll." : e.Amount <= 0f ? "Lager leer." : null;
        list.Add(new ServiceOffer($"{ServiceKey}:{Fill}", $"Magazin füllen ({g.Name})", price, Deliverable(u, station, ship, float.MaxValue), g.Id, e.Amount, held) { Note = note });
        foreach (int t in Tiers)
            list.Add(new ServiceOffer($"{ServiceKey}:{t}", $"{t} {g.Name} laden", price, Deliverable(u, station, ship, t), g.Id, e.Amount, held) { Note = note });
        return list;
    }

    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        float want;
        if (request.Offer == $"{ServiceKey}:{Fill}") want = float.MaxValue;
        else if (request.Offer.StartsWith($"{ServiceKey}:", StringComparison.Ordinal) && int.TryParse(request.Offer[(ServiceKey.Length + 1)..], out int v)
                 && Array.IndexOf(Tiers, v) >= 0 && request.Offer[(ServiceKey.Length + 1)..] == v.ToString()) want = v;
        else return Fail($"Unbekanntes Angebot '{request.Offer}'.");
        if (!IsAvailable(u, station)) return Fail($"Dienst '{Key}' ist an dieser Station nicht verfügbar.");
        if (!ship.IsPlayer) return Fail("Nur für den Spieler.");
        if (ship.Stats.AmmoCapacity <= 0f) return Fail("Kein Magazin an Bord.");
        if (Rearm.Missing(u, ship) <= 0.001f) return Fail("Magazin ist voll.");
        float units = Deliverable(u, station, ship, want);
        if (units < 0.001f) return Fail(station.Market[Rearm.AmmoGood(u)!.Id].Amount <= 0f ? "Lager leer." : "Nicht genügend Credits.");
        var wallet = u.WalletOf(ship);
        float before = wallet.Credits;
        float got = Rearm.AtStation(u, ship, station, wallet, ship.Owner, false, units);
        if (got <= 0f) return Fail("Nachladen fehlgeschlagen.");
        float paid = before - wallet.Credits;
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, request.Offer, Rearm.AmmoGood(u)!.Id, got, -paid));
        return new ServiceResult(true, got, -paid, $"{got:0} Munition geladen für {paid:0} Cr (Magazin {ship.AmmoLoaded:0}/{ship.Stats.AmmoCapacity:0}).");
    }

    static ServiceResult Fail(string m) => new(false, 0f, 0f, m);
}
