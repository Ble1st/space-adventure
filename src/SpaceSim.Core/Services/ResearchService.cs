using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Services;

/// <summary>
/// Dienst „research“ an Forschungsstationen (Phase 13, FORSCHUNG.md „Spieler“): Der Spieler sieht den Forschungsstand der Fraktion der Station
/// (Anzeige über <see cref="ResearchInfo"/>), beauftragt ein Projekt (<c>commission:&lt;Projekt&gt;</c>, Gebühr <see cref="EconomyRules.ResearchCommissionFee"/>
/// an die Station; das Projekt läuft dann als nächstes) oder finanziert das laufende Projekt mit (<c>fund:&lt;Betrag&gt;</c>, Beträge aus
/// <see cref="EconomyRules.ResearchFundAmounts"/>; Credits gehen an die Station, das Projekt bekommt Betrag ÷ <see cref="EconomyRules.ResearchCreditsPerPoint"/> FP
/// — so kommt der Spieler früher an neue Module und Schiffe). Güter liefert er über den normalen Handel. Feindlich gesinnte Fraktionen
/// (Beziehung ≤ <see cref="EconomyRules.HostileRelation"/>) nehmen keine Aufträge an. Schlüssel streng; Event nur bei Erfolg.
/// </summary>
public sealed class ResearchService : IStationService
{
    public const string ServiceKey = "research";
    public const string Commission = "commission";
    public const string Fund = "fund";

    public string Key => ServiceKey;

    /// <summary>Verfügbar an fertigen Forschungsstationen einer Fraktion, wenn das Universum Forschungsprojekte hat.</summary>
    public bool IsAvailable(Universe u, Station station) =>
        u.Rules.ResearchActive && u.Research.Count > 0 && Research.ConfigOf(u, station) is not null && Research.TechOf(u, station) is { } f && u.Factions.ContainsKey(f);

    /// <summary>Grund, warum der Spieler hier keine Aufträge geben kann (feindliche Fraktion), sonst null.</summary>
    public static string? Refusal(Universe u, Station station, Ship ship)
    {
        if (!ship.IsPlayer) return "Nur für eigene Schiffe.";
        if (Research.TechOf(u, station) is not { } f) return "Keine Fraktion forscht hier.";
        return Relations.Effective(u, f, ship.Owner) <= u.Rules.HostileRelation ? $"{u.Factions[f].Name} nimmt von dir keine Aufträge an." : null;
    }

    /// <summary>
    /// Angebote: je noch nicht erforschtem Projekt „commission:&lt;Projekt&gt;“ (Price = Gebühr, Amount 1 nur, wenn die Voraussetzungen erfüllt sind, es
    /// nicht schon läuft, die Kasse reicht und die Fraktion nicht feindlich ist; Stock = erreichte FP, Held = nötige FP) und je Betrag
    /// „fund:&lt;Betrag&gt;“ für das laufende Projekt (Price = Betrag, Stock = FP, die er bringt).
    /// </summary>
    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var offers = new List<ServiceOffer>();
        if (!IsAvailable(u, station) || Research.TechOf(u, station) is not { } f) return offers;
        var state = u.Factions[f].Research;
        bool ok = Refusal(u, station, ship) is null;
        float credits = u.WalletOf(ship).Credits;
        var current = Research.Peek(u, state);
        foreach (var p in u.Research.Projects)
        {
            if (state.IsCompleted(p.Key)) continue;
            bool can = ok && Research.RequirementsMet(state, p) && current?.Key != p.Key && credits >= u.Rules.ResearchCommissionFee;
            offers.Add(new ServiceOffer($"{Commission}:{p.Key}", $"Beauftragen: {p.Name}", u.Rules.ResearchCommissionFee, can ? 1f : 0f,
                default, state.Progress.GetValueOrDefault(p.Key), p.Points));
        }
        if (current is not null)
            foreach (var amount in u.Rules.ResearchFundAmounts.Where(a => a > 0f).Distinct().OrderBy(a => a))
            {
                float points = amount / MathF.Max(0.01f, u.Rules.ResearchCreditsPerPoint);
                offers.Add(new ServiceOffer($"{Fund}:{amount:0}", $"Mitfinanzieren: {amount:0} Cr → {points:0} FP für {current.Name}", amount,
                    ok && credits >= amount ? 1f : 0f, default, points, 0f));
            }
        return offers;
    }

    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        var parts = request.Offer.Split(':');
        if (parts.Length != 2 || parts[1].Length == 0 || (parts[0] != Commission && parts[0] != Fund))
            return Fail($"Unbekanntes Angebot '{request.Offer}'.");
        if (!IsAvailable(u, station) || Research.TechOf(u, station) is not { } f) return Fail("Hier wird nicht geforscht.");
        if (Refusal(u, station, ship) is { } why) return Fail(why);
        var faction = u.Factions[f];
        var state = faction.Research;
        var wallet = u.WalletOf(ship);

        if (parts[0] == Commission)
        {
            if (u.Research.Get(parts[1]) is not { } p) return Fail($"Unbekanntes Projekt '{parts[1]}'.");
            if (state.IsCompleted(p.Key)) return Fail($"{p.Name} ist bereits erforscht.");
            if (!Research.RequirementsMet(state, p))
                return Fail($"{p.Name} braucht zuerst: {string.Join(", ", p.Requires.Where(r => !state.IsCompleted(r)).Select(r => u.Research.Get(r)?.Name ?? r))}.");
            if (Research.Peek(u, state)?.Key == p.Key) return Fail($"{p.Name} läuft bereits.");
            float fee = u.Rules.ResearchCommissionFee;
            if (wallet.Credits < fee) return Fail($"Gebühr {fee:0} Cr — zu wenig Credits.");
            wallet.Credits -= fee;
            station.Credits += fee;
            state.Current = p.Key;
            state.Commissioned = true;
            u.Events.Publish(new ResearchCommissioned(f, p.Key, fee));
            u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Commission, default, 1f, -fee));
            return new ServiceResult(true, 1f, -fee, $"{faction.Name} forscht jetzt an {p.Name} (Gebühr {fee:0} Cr).");
        }

        // fund:<Betrag> — nur die angebotenen Beträge (strenge Ziffern)
        if (!parts[1].All(c => c is >= '0' and <= '9') || !float.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var amount)
            || !u.Rules.ResearchFundAmounts.Any(a => MathF.Abs(a - amount) < 0.5f) || amount <= 0f)
            return Fail($"Unbekanntes Angebot '{request.Offer}'.");
        if (Research.CurrentOf(u, faction) is not { } cur) return Fail("Hier gibt es nichts mehr zu erforschen.");
        if (wallet.Credits < amount) return Fail($"{amount:0} Cr — zu wenig Credits.");
        float pts = amount / MathF.Max(0.01f, u.Rules.ResearchCreditsPerPoint);
        wallet.Credits -= amount;
        station.Credits += amount;
        state.PlayerFunded += amount;
        u.Events.Publish(new ResearchFunded(f, cur.Key, amount, pts));
        bool done = Research.AddPoints(u, faction, cur, pts);
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Fund, default, pts, -amount));
        return new ServiceResult(true, pts, -amount, done
            ? $"{amount:0} Cr beigesteuert — {cur.Name} ist erforscht! Freigeschaltet: {string.Join(", ", cur.Unlocks.Select(x => Research.UnlockName(u, x)))}."
            : $"{amount:0} Cr beigesteuert: +{pts:0} FP für {cur.Name}.");
    }

    static ServiceResult Fail(string message) => new(false, 0f, 0f, message);
}
