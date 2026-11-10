using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Services;

/// <summary>Vorschau einer Umrüstung: Werte vorher/nachher, Regelverstoß (null = gültig) und Frachtproblem (null = Fracht passt).</summary>
public sealed record FitPreview(ShipStats Before, ShipStats After, FitIssue? Issue, string? CargoProblem)
{
    public bool Valid => Issue is null && CargoProblem is null;
}

/// <summary>
/// Dienst „outfitter“ (Ausrüster, Phase 5c, SCHIFFSSLOTS.md „Module sind Waren“): Module ein- und ausbauen.
/// <list type="bullet">
/// <item>Einbau „fit:&lt;Slot&gt;:&lt;Modul&gt;“: Das Modul kommt aus der Fracht des Schiffs (kostenlos), sonst als Modul-Ware aus dem
/// Stationslager zum Marktpreis (Kasse über <see cref="Universe.WalletOf"/>, die Station bekommt das Geld). Steckt im Slot schon ein
/// Modul, wird es getauscht: Das alte geht an die Station (Ankauf zum Marktpreis, soweit sie annimmt und zahlen kann), sonst in die Fracht.</item>
/// <item>Ausbau „remove:&lt;Slot&gt;“: nur für Nicht-Pflichtslots (Reaktor/Antrieb nur per Tausch); das Modul geht wie oben an die Station
/// oder in die Fracht.</item>
/// </list>
/// Jede Änderung muss die Fit-Regeln (<see cref="FitRules.Check"/>: Slotart, Größe, Pflichtslots, Leistungsbilanz — dieselbe Prüfung
/// wie die Content-Validierung) und die Frachtregel (vorhandene Fracht passt in den neuen Laderaum) erfüllen; sonst wird sie ohne
/// Zustandsänderung abgelehnt. Danach <see cref="Ship.RecomputeStats"/>. Nur Schiffe mit Modell.
/// </summary>
public sealed class OutfitterService : IStationService
{
    public const string ServiceKey = "outfitter";
    /// <summary>Angebots-Schlüssel Einbau „fit:&lt;Slot&gt;:&lt;Modulschlüssel&gt;“ (Slot = Index ab 0).</summary>
    public const string Fit = "fit";
    /// <summary>Angebots-Schlüssel Ausbau „remove:&lt;Slot&gt;“.</summary>
    public const string Remove = "remove";

    public string Key => ServiceKey;

    /// <summary>Verfügbar, wenn die Station Modul-Waren im Markt führt (Lager des Ausrüsters).</summary>
    public bool IsAvailable(Universe u, Station station)
    {
        foreach (var g in station.Market.Keys)
            if (u.Goods.TryGetValue(g, out var good) && u.Modules.ContainsKey(good.Key)) return true;
        return false;
    }

    static ShipClass? ClassOf(Universe u, Ship ship) =>
        ship.HasModel && u.ShipModels.TryGetValue(ship.ModelKey, out var m) ? u.ShipClasses.GetValueOrDefault(m.ClassKey) : null;

    static ShipStats StatsOf(Universe u, ShipClass cls, IReadOnlyList<string?> fit) =>
        ShipStats.Compute(cls, fit.Select(k => k is not null && u.Modules.TryGetValue(k, out var m) ? m : null).ToList());

    /// <summary>Kauft die Station ein ausgebautes Modul an? Preis &gt; 0, Platz im Lager, genug Kasse.</summary>
    static bool StationTakes(Universe u, Station st, GoodId good) =>
        st.Market.TryGetValue(good, out var e) && e.Price > 0f && st.AcceptableUnits(good, u.Rules) >= 1f && st.Credits >= e.Price;

    /// <summary>
    /// Passt die Fracht in den Laderaum der neuen Werte? Ohne getrennte Räume Gesamtvolumen ≤ Laderaum, sonst die Frachtform-Regel
    /// (fest ≤ solid+any, flüssig ≤ liquid+any, Summe ≤ alles). null = passt, sonst Grund.
    /// </summary>
    public static string? CargoProblem(Universe u, ShipStats stats, IReadOnlyDictionary<GoodId, float> cargo)
    {
        float solid = 0f, liquid = 0f;
        foreach (var (g, a) in cargo)
        {
            if (a <= 0f) continue;
            float v = a * u.Goods[g].VolumePerUnit;
            if (u.Goods[g].Form == GoodForm.Liquid) liquid += v; else solid += v;
        }
        const float eps = 0.0001f;
        if (stats.CargoSolid > 0f || stats.CargoLiquid > 0f)
        {
            if (solid > stats.CargoSolid + stats.CargoAny + eps) return "Feste Fracht passt nicht mehr in den Laderaum";
            if (liquid > stats.CargoLiquid + stats.CargoAny + eps) return "Flüssige Fracht passt nicht mehr in den Laderaum";
        }
        if (solid + liquid > stats.Cargo + eps) return "Fracht passt nicht mehr in den Laderaum";
        return null;
    }

    /// <summary>
    /// Vorschau (rein, ändert nichts): Werte vor und nach dem Ein-/Ausbau von <paramref name="moduleKey"/> (null = Ausbau) in Slot
    /// <paramref name="slot"/>, mit Regelprüfung. Für die Oberfläche (Vorher/Nachher, Leistungsbilanz) und für <see cref="Execute"/>.
    /// Ein Modul, das aus der Fracht kommt, verlässt die Fracht; das ausgebaute Modul geht in die Fracht, wenn die Station es nicht nimmt.
    /// </summary>
    public static FitPreview? Preview(Universe u, Station? station, Ship ship, int slot, string? moduleKey)
    {
        var cls = ClassOf(u, ship);
        if (cls is null || slot < 0 || slot >= ship.Slots.Count || slot >= ship.Fit.Count) return null;
        var before = StatsOf(u, cls, ship.Fit);
        var fit = new List<string?>(ship.Fit) { [slot] = moduleKey };
        var issue = FitRules.Check(u.SlotTypes, u.Modules, cls, ship.Slots, fit);
        var after = StatsOf(u, cls, fit);
        // Fracht danach: eingebautes Modul aus der Fracht heraus, ausgebautes hinein (wenn die Station es nicht ankauft)
        var cargo = new Dictionary<GoodId, float>(ship.Cargo);
        if (moduleKey is not null && u.FindGood(moduleKey) is { } inGood && cargo.GetValueOrDefault(inGood.Id) >= 1f) cargo[inGood.Id] -= 1f;
        if (ship.Fit[slot] is { } oldKey && u.FindGood(oldKey) is { } outGood && (station is null || !StationTakes(u, station, outGood.Id)))
            cargo[outGood.Id] = cargo.GetValueOrDefault(outGood.Id) + 1f;
        return new FitPreview(before, after, issue, CargoProblem(u, after, cargo));
    }

    /// <summary>
    /// Angebote je Slot: „remove:&lt;i&gt;“ für belegte Nicht-Pflichtslots (Price = Ankaufpreis der Station, 0 = Modul geht in die Fracht)
    /// und „fit:&lt;i&gt;:&lt;Modul&gt;“ für jedes Modul passender Slotart und Größe, das in der Fracht liegt oder im Stationslager vorrätig ist
    /// (Price = Marktpreis, 0 aus der Fracht). Amount = 1, wenn die Änderung jetzt gültig und bezahlbar ist, sonst 0 (Grund über
    /// <see cref="Preview"/>). Good = Modul-Ware, Stock = Stationsbestand, Held = Menge in der Fracht. Reihenfolge: Slot, dann GoodId.
    /// </summary>
    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var offers = new List<ServiceOffer>();
        if (ClassOf(u, ship) is null) return offers;
        var wallet = u.WalletOf(ship);
        var moduleGoods = u.Modules.Values.Select(m => (m, g: u.FindGood(m.Key))).Where(x => x.g is not null)
            .OrderBy(x => x.g!.Id.Value).ToList();
        for (int i = 0; i < ship.Slots.Count && i < ship.Fit.Count; i++)
        {
            var slot = ship.Slots[i];
            string slotName = u.SlotTypes.TryGetValue(slot.Type, out var stype) ? stype.Name : slot.Type;
            if (ship.Fit[i] is { } cur && u.FindGood(cur) is { } curGood && !(stype?.Required ?? false))
            {
                var p = Preview(u, station, ship, i, null);
                float price = StationTakes(u, station, curGood.Id) ? station.Market[curGood.Id].Price : 0f;
                offers.Add(new ServiceOffer($"{Remove}:{i}", $"Ausbauen: {curGood.Name} (Slot {i + 1} {slotName})", price,
                    p is { Valid: true } ? 1f : 0f, curGood.Id, station.Market.TryGetValue(curGood.Id, out var ce) ? ce.Amount : 0f,
                    ship.Cargo.GetValueOrDefault(curGood.Id)));
            }
            foreach (var (m, g) in moduleGoods)
            {
                if (m.Key == ship.Fit[i] || FitRules.SlotIssue(m, slot, i) is not null) continue;
                float held = ship.Cargo.GetValueOrDefault(g!.Id);
                bool inStock = station.Market.TryGetValue(g.Id, out var e) && e.Amount >= 1f && e.Price > 0f;
                if (held < 1f && !inStock) continue;
                float price = held >= 1f ? 0f : e!.Price;
                var p = Preview(u, station, ship, i, m.Key);
                float revenue = ship.Fit[i] is { } old && u.FindGood(old) is { } og && StationTakes(u, station, og.Id) ? station.Market[og.Id].Price : 0f;
                bool ok = p is { Valid: true } && wallet.Credits + revenue >= price;
                offers.Add(new ServiceOffer($"{Fit}:{i}:{m.Key}", $"Einbauen: {g.Name} (Slot {i + 1} {slotName})", price, ok ? 1f : 0f,
                    g.Id, e?.Amount ?? 0f, held));
            }
        }
        return offers;
    }

    /// <summary>
    /// Führt „fit:&lt;i&gt;:&lt;Modul&gt;“ oder „remove:&lt;i&gt;“ aus. Bei jedem Verstoß (unbekannter Schlüssel, Dienst nicht verfügbar, Schiff
    /// ohne Modell, falsche Slotart/Größe, Pflichtslot, Leistungsbilanz, Fracht passt nicht, Modul nicht vorrätig, zu wenig Credits)
    /// bleibt der Zustand unverändert und es gibt kein Event. Bei Erfolg: <see cref="ServicePerformed"/> und <see cref="ShipRefitted"/>.
    /// </summary>
    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        if (!TryParseOffer(u, request.Offer, out int slot, out string? moduleKey))
            return Fail($"Unbekanntes Angebot '{request.Offer}'.");
        if (!IsAvailable(u, station)) return Fail($"Dienst '{Key}' ist an dieser Station nicht verfügbar.");
        var cls = ClassOf(u, ship);
        if (cls is null) return Fail("Dieses Schiff hat kein Modell und kann nicht umgerüstet werden.");
        if (slot >= ship.Slots.Count || slot >= ship.Fit.Count) return Fail("Diesen Slot hat das Schiff nicht.");
        var slotDef = ship.Slots[slot];
        string? oldKey = ship.Fit[slot];
        if (moduleKey is null && oldKey is null) return Fail("Der Slot ist bereits leer.");
        if (moduleKey is not null && moduleKey == oldKey) return Fail("Dieses Modul ist dort bereits eingebaut.");
        if (moduleKey is null && u.SlotTypes.TryGetValue(slotDef.Type, out var st) && st.Required)
            return Fail($"Pflichtslot '{st.Name}' darf nicht leer bleiben – nur Tausch möglich.");

        Good? inGood = null;
        bool fromCargo = false;
        float price = 0f;
        if (moduleKey is not null)
        {
            var module = u.Modules[moduleKey];
            if (FitRules.SlotIssue(module, slotDef, slot) is { } slotIssue) return Fail(FitRules.Describe(slotIssue) + ".");
            inGood = u.FindGood(moduleKey);
            if (inGood is null) return Fail("Für dieses Modul gibt es keine Ware.");
            fromCargo = ship.Cargo.GetValueOrDefault(inGood.Id) >= 1f;
            if (!fromCargo)
            {
                if (!station.Market.TryGetValue(inGood.Id, out var e) || e.Price <= 0f || e.Amount < 1f)
                    return Fail($"{inGood.Name} ist hier nicht auf Lager.");
                price = e.Price;
            }
        }

        var preview = Preview(u, station, ship, slot, moduleKey)!;
        if (preview.Issue is { } issue) return Fail($"{FitRules.Describe(issue)}: {issue.Message}.");
        if (preview.CargoProblem is { } cp) return Fail(cp + ".");

        Good? outGood = oldKey is null ? null : u.FindGood(oldKey);
        bool sellOld = outGood is not null && StationTakes(u, station, outGood.Id);
        float revenue = sellOld ? station.Market[outGood!.Id].Price : 0f;
        var wallet = u.WalletOf(ship);
        if (wallet.Credits + revenue < price) return Fail("Nicht genügend Credits.");

        // ── ab hier gültig: buchen ──
        if (inGood is not null)
        {
            if (fromCargo) { ship.Cargo[inGood.Id] -= 1f; if (ship.Cargo[inGood.Id] <= 0.0001f) ship.Cargo.Remove(inGood.Id); }
            else { station.Market[inGood.Id].Amount -= 1f; station.Credits += price; wallet.Credits -= price; }
        }
        if (outGood is not null)
        {
            if (sellOld) { station.Market[outGood.Id].Amount += 1f; station.Credits -= revenue; wallet.Credits += revenue; }
            else ship.Cargo[outGood.Id] = ship.Cargo.GetValueOrDefault(outGood.Id) + 1f;
        }
        ship.Fit[slot] = moduleKey;
        ship.RecomputeStats(u);
        ship.CargoCapacity = ship.Stats.Cargo;
        ship.Hull = MathF.Min(ship.Hull, ship.MaxHull);

        float credits = revenue - price;
        var good = inGood?.Id ?? outGood!.Id;
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, moduleKey is null ? Remove : Fit, good, 1f, credits));
        u.Events.Publish(new ShipRefitted(ship.Id, station.Id, slot, moduleKey, oldKey));
        string text = moduleKey is null
            ? $"{outGood!.Name} ausgebaut" + (sellOld ? $" und für {revenue:0} Cr an die Station verkauft." : " – liegt jetzt in der Fracht.")
            : $"{inGood!.Name} eingebaut" + (fromCargo ? " (aus der Fracht)" : $" für {price:0} Cr")
              + (outGood is null ? "." : sellOld ? $"; {outGood.Name} für {revenue:0} Cr verkauft." : $"; {outGood.Name} liegt in der Fracht.");
        return new ServiceResult(true, 1f, credits, text);
    }

    static ServiceResult Fail(string message) => new(false, 0f, 0f, message);

    /// <summary>
    /// Zerlegt einen Angebots-Schlüssel streng: „fit:&lt;Slot&gt;:&lt;Modulschlüssel&gt;“ (Slot nur Ziffern, Modul bekannt) oder
    /// „remove:&lt;Slot&gt;“. Alles andere gilt als unbekannt.
    /// </summary>
    static bool TryParseOffer(Universe u, string offer, out int slot, out string? moduleKey)
    {
        slot = -1;
        moduleKey = null;
        var parts = offer.Split(':');
        if (parts.Length < 2 || !Digits(parts[1]) || !int.TryParse(parts[1], out slot)) return false;
        if (parts[0] == Remove && parts.Length == 2) return true;
        if (parts[0] == Fit && parts.Length == 3 && u.Modules.ContainsKey(parts[2])) { moduleKey = parts[2]; return true; }
        slot = -1;
        return false;
    }

    static bool Digits(string s) => s.Length > 0 && s.All(c => c is >= '0' and <= '9');
}
