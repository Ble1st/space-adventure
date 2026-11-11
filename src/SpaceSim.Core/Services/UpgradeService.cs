using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Services;

/// <summary>
/// Dienst „upgrade“ (Phase 7, SCHIFFSSLOTS.md Abschnitt 8): Module eine Stufe verbessern, Slots vergrößern, Bonusslots anbauen — nach der
/// gemeinsamen Regel <see cref="ShipUpgrade"/> (auch das NPC-<see cref="UpgradeSystem"/> nutzt sie). Angebots-Schlüssel streng
/// „module:&lt;Slot&gt;“, „slot:&lt;Slot&gt;“, „bonus:&lt;Slotart&gt;“. Ein Versuch verbraucht Material aus dem Stationslager und Credits (auch bei
/// Misserfolg); die Erfolgschance steigt mit jedem Fehlschlag am selben Slot (Mitleidszähler). Ergebnis: <see cref="ServiceResult.Success"/> =
/// der Versuch wurde ausgeführt, <see cref="ServiceResult.Units"/> = 1 bei gelungenem Upgrade, 0 bei Fehlschlag.
/// </summary>
public sealed class UpgradeService : IStationService
{
    public const string ServiceKey = "upgrade";
    public string Key => ServiceKey;

    /// <summary>Verfügbar an jeder Station, deren Typ den Dienst führt (Material kann fehlen; die Angebote nennen es).</summary>
    public bool IsAvailable(Universe u, Station station) => true;

    /// <summary>Anzeigetext eines Upgrades: was sich ändert und die Chance (inklusive Mitleid).</summary>
    public static string Describe(Universe u, Ship ship, UpgradeQuote q)
    {
        string Name(string? key) => key is not null && u.Modules.TryGetValue(key, out var m) && m.Name.Length > 0 ? m.Name : key ?? "–";
        string type = u.SlotTypes.TryGetValue(q.SlotType, out var t) ? t.Name : q.SlotType;
        string what = q.Kind switch
        {
            UpgradeKind.Module => $"{Name(q.FromModule)} → {Name(q.ToModule)}",
            UpgradeKind.Slot => $"Slot {q.Slot + 1} ({type}) {ship.Slots[q.Slot].Size} → {q.NewSize}",
            _ => $"Bonusslot {type} {q.NewSize}",
        };
        return $"{what} · {q.Chance * 100f:0} %";
    }

    /// <summary>
    /// Je mögliches Upgrade ein Angebot: Text (Änderung und Chance), Price = Preis des Versuchs, Amount = 1, wenn er jetzt möglich ist (Material
    /// vorrätig, bezahlbar, Fit bleibt gültig, Slotgrenze frei), sonst 0; Good/Stock = erstes Material und sein Bestand; Held = Mitleidszähler.
    /// </summary>
    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var offers = new List<ServiceOffer>();
        foreach (var q in ShipUpgrade.Quotes(u, station, ship))
        {
            var good = q.Materials.Count > 0 ? q.Materials[0].Good : default;
            float stock = q.Materials.Count > 0 && station.Market.TryGetValue(good, out var e) ? e.Amount : 0f;
            offers.Add(new ServiceOffer(q.Key, Describe(u, ship, q), q.Price, q.Possible ? 1f : 0f, good, stock, q.Pity));
        }
        return offers;
    }

    /// <summary>
    /// Versucht das Upgrade. Fehlschlag ohne Zustandsänderung und ohne Event: unbekannter Schlüssel, Schiff ohne Modell, Hinderungsgrund
    /// (Slotgrenze, Leistung), Material fehlt, zu wenig Credits. Sonst wird gewürfelt (<see cref="ShipUpgrade.Attempt"/>) und
    /// <see cref="ServicePerformed"/> veröffentlicht — auch bei misslungenem Wurf, denn Material und Credits sind verbraucht.
    /// </summary>
    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        if (ShipUpgrade.Quote(u, station, ship, request.Offer) is not { } q) return Fail($"Unbekanntes Angebot '{request.Offer}'.");
        if (q.Issue is { } issue) return Fail(issue + ".");
        if (q.Missing.Count > 0)
            return Fail("Im Lager fehlt: " + string.Join(", ", q.Missing.Select(m => $"{m.Units:0}× {u.Goods[m.Good].Name}")) + ".");
        if (!q.Affordable) return Fail($"Nicht genügend Credits ({q.Price:0} Cr nötig).");

        string text = Describe(u, ship, q);
        bool ok = ShipUpgrade.Attempt(u, station, ship, q);
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, q.Key.Split(':')[0], q.Materials.Count > 0 ? q.Materials[0].Good : default, ok ? 1f : 0f, -q.Price));
        if (ok) return new ServiceResult(true, 1f, -q.Price, $"Upgrade gelungen: {text.Split(" · ")[0]} ({q.Price:0} Cr).");
        int pity = ship.UpgradePity.GetValueOrDefault(q.Kind == UpgradeKind.Bonus ? ShipUpgrade.BonusPityKey : q.Slot);
        float next = ShipUpgrade.Quote(u, station, ship, q.Key)?.Chance ?? q.Chance;
        return new ServiceResult(true, 0f, -q.Price,
            $"Fehlschlag ({q.Chance * 100f:0} %): Material und {q.Price:0} Cr verbraucht. Nächster Versuch {next * 100f:0} % (Mitleidszähler {pity}).");
    }

    static ServiceResult Fail(string message) => new(false, 0f, 0f, message);
}
