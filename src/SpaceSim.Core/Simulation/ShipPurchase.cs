using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Bedarf einer Ware beim Schiffskauf (Bausatz oder Modul-Ware).</summary>
public readonly record struct MaterialNeed(GoodId Good, float Units);

/// <summary>
/// Gemeinsame Kaufregel für Schiffe (Phase 5c, SCHIFFE.md „Kauf“): Ein neues Schiff verbraucht den Bausatz seiner Klasse und je
/// eingebautem Modul eine Modul-Ware aus dem Lager der verkaufenden Station; der Preis ist der Marktpreis dieser Waren mal
/// (1 + Montagegebühr des Stationstyps), das Geld bekommt die Station. Spieler (Dienst „shipdealer“) und KI-Flotte
/// (<see cref="FleetSystem"/>) nutzen dieselben Funktionen. Reine Funktionen über das Universum, deterministische Reihenfolge.
/// </summary>
public static class ShipPurchase
{
    /// <summary>Bausatz-Warenschlüssel einer Klasse: <see cref="ShipClass.Kit"/>, sonst die Regel <see cref="EconomyRules.ShipKitGoodKey"/>.</summary>
    public static string KitKeyOf(Universe u, ShipClass? cls) => cls?.Kit is { Length: > 0 } kit ? kit : u.Rules.ShipKitGoodKey;

    /// <summary>
    /// Materialbedarf eines Schiffs mit dieser Klasse und diesem Fit: 1 Bausatz plus je belegtem Slot eine Einheit der Modul-Ware,
    /// zusammengefasst je Ware, sortiert nach GoodId. null, wenn Bausatz oder eine Modul-Ware in diesem Universum fehlt.
    /// </summary>
    public static IReadOnlyList<MaterialNeed>? Materials(Universe u, ShipClass? cls, IReadOnlyList<string?> fit)
    {
        var sum = new Dictionary<GoodId, float>();
        var kit = u.FindGood(KitKeyOf(u, cls));
        if (kit is null) return null;
        sum[kit.Id] = Math.Max(1, cls?.KitCount ?? 1);   // Phase 29: große Klassen brauchen mehrere Bausätze
        foreach (var key in fit)
        {
            if (key is null) continue;
            var g = u.FindGood(key);
            if (g is null) return null;
            sum[g.Id] = sum.GetValueOrDefault(g.Id) + 1f;
        }
        return sum.OrderBy(kv => kv.Key.Value).Select(kv => new MaterialNeed(kv.Key, kv.Value)).ToList();
    }

    /// <summary>Materialbedarf eines Modells (Bausatz der Klasse + Module des Werks-Fits); null wie <see cref="Materials(Universe, ShipClass?, IReadOnlyList{string?})"/>.</summary>
    public static IReadOnlyList<MaterialNeed>? Materials(Universe u, ShipModel model) =>
        Materials(u, u.ShipClasses.GetValueOrDefault(model.ClassKey), model.Fit);

    /// <summary>Was im Lager der Station fehlt (ganze Einheiten; Ware nicht geführt = ganzer Bedarf fehlt).</summary>
    public static List<MaterialNeed> Missing(Station st, IReadOnlyList<MaterialNeed> needs)
    {
        var missing = new List<MaterialNeed>();
        foreach (var n in needs)
        {
            float have = st.Market.TryGetValue(n.Good, out var e) ? MathF.Floor(e.Amount) : 0f;
            if (have < n.Units) missing.Add(new MaterialNeed(n.Good, n.Units - have));
        }
        return missing;
    }

    /// <summary>Kaufpreis: Σ Menge × Marktpreis der Station (Ware ohne Marktpreis: Basispreis) × (1 + Montagegebühr).</summary>
    public static float Price(Universe u, Station st, IReadOnlyList<MaterialNeed> needs, float assembly)
    {
        float sum = 0f;
        foreach (var n in needs)
            sum += n.Units * (st.Market.TryGetValue(n.Good, out var e) && e.Price > 0f ? e.Price : u.Goods[n.Good].BasePrice);
        return sum * (1f + MathF.Max(0f, assembly));
    }

    /// <summary>Verkauft diese Station Schiffe dieser Klasse (Stationstyp mit <see cref="StationType.ShipDealer"/>, Klasse erlaubt)?</summary>
    public static bool Sells(Universe u, Station st, ShipClass cls) =>
        u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.ShipDealer is { } cfg && cfg.Allows(cls);

    /// <summary>
    /// Phase 13: Darf diese Station gerade dieses Modell verkaufen — Klasse erlaubt und das Modell für die Fraktion der Station erforscht
    /// (<see cref="Research.Available"/>)? Gilt für Spieler, KI-Flotte und Polizei gleich.
    /// </summary>
    public static bool SellsModel(Universe u, Station st, ShipModel model) =>
        u.ShipClasses.TryGetValue(model.ClassKey, out var cls) && Sells(u, st, cls) && Research.Available(u, st, UnlockKind.Model, model.Key);

    /// <summary>Montagegebühr der Station (0 ohne Schiffshändler).</summary>
    public static float AssemblyOf(Universe u, Station st) =>
        u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.ShipDealer is { } cfg ? cfg.Assembly : 0f;

    /// <summary>Nimmt den Materialbedarf aus dem Stationslager (Aufrufer hat <see cref="Missing"/> geprüft).</summary>
    public static void Consume(Station st, IReadOnlyList<MaterialNeed> needs)
    {
        foreach (var n in needs) st.Market[n.Good].Amount -= n.Units;
    }

    /// <summary>
    /// Erzeugt ein neues Schiff mit Klasse, Slotliste und Fit (Kopie) am Andockpunkt der Station: Werte aus dem Fit, voller Hülle und vollem
    /// Tank; Rolle/Steuerer und Güteklasse (Phase 7, Slots/Fit enthalten ihre Wirkung schon) wie übergeben. Das Schiff ist noch nicht im Universum (Aufrufer ruft <see cref="Universe.AddShip"/>).
    /// </summary>
    public static Ship Create(Universe u, ShipId id, string modelKey, IReadOnlyList<ClassSlot> slots, IReadOnlyList<string?> fit,
        ShipRole role, Controller controller, ActorId owner, Station at, float credits, ShipQuality quality = ShipQuality.Standard)
    {
        var ship = new Ship
        {
            Id = id, Role = role, Controller = controller, Owner = owner, Sector = at.Sector, Position = at.DockPoint(id), Credits = credits,
            ModelKey = modelKey, Quality = quality, DockedAt = at.Id,   // Phase 12: liegt angedockt an der Werft
            Slots = slots.Select(s => new ClassSlot { Type = s.Type, Size = s.Size, Facing = s.Facing, Arc = s.Arc }).ToList(),
            Fit = new List<string?>(fit),
        };
        if (ship.RecomputeStats(u))
        {
            ship.CargoCapacity = ship.Stats.Cargo;
            ship.Hull = ship.MaxHull;
            ship.FillFuel();   // Neuschiff startet mit vollem Tank (erster zulässiger Treibstoff)
        }
        return ship;
    }
}
