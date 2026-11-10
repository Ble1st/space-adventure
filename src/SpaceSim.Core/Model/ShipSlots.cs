namespace SpaceSim.Core.Model;

/// <summary>Slot-/Modulgrößen; ein Modul passt, wenn seine Größe höchstens der Slotgröße entspricht.</summary>
public enum SlotSize { S, M, L, XL }

/// <summary>Welche Warenform ein Frachtmodul aufnehmen kann.</summary>
public enum ModuleHolds { Solid, Liquid, Any }

/// <summary>Slotart (Reaktor, Antrieb, …): was in einen Slot passt. Content aus der Weltdatei.</summary>
public sealed class SlotType
{
    public string Key = "";
    public string Name = "";
    /// <summary>Pflichtslot: ein Fit ohne Modul in diesem Slot ist ungültig (Reaktor, Antrieb).</summary>
    public bool Required;
    /// <summary>Anzeigefarbe (#RRGGBB) der Modul-Waren dieser Slotart (Phase 5a); null = Standardfarbe.</summary>
    public string? Color;
}

/// <summary>Ein zulässiger Treibstoff eines Antriebsmoduls: Ware + efficiency (Phase 4).</summary>
public sealed record FuelType(GoodId Good, float Efficiency);

/// <summary>Modul (Schiffsbauteil): sitzt in genau einem Slot und bestimmt mit seinen Stats die Schiffs­werte.</summary>
public sealed class Module
{
    public string Key = "";
    /// <summary>Anzeigename (auch Name der Modul-Ware, Phase 5a); leer = Schlüssel.</summary>
    public string Name = "";
    /// <summary>Slotart-Schlüssel, in die dieses Modul passt.</summary>
    public string Slot = "";
    public SlotSize Size = SlotSize.S;
    /// <summary>Stufe Mk1–Mk3.</summary>
    public int Tier = 1;
    /// <summary>Fähigkeiten, die dieses Modul dem Schiff verleiht.</summary>
    public List<Capability> Grants = new();
    /// <summary>Freie Kennzahlen des Moduls (z.B. powerOutput, capacity, thrust, armor, chunkChanceMult).</summary>
    public Dictionary<string, float> Stats = new();
    /// <summary>Dauer-Leistungsaufnahme.</summary>
    public float PowerDraw;
    /// <summary>Kurzzeitige Spitzenlast (z.B. Waffen), gepuffert von der Speicherbank.</summary>
    public float PeakDraw;
    public float Mass;
    /// <summary>Credits-Unterhalt pro Tick.</summary>
    public float Upkeep;
    /// <summary>Welche Warenform das Frachtmodul fasst; Standard Any (bei Nicht-Frachtmodulen ohne Bedeutung).</summary>
    public ModuleHolds Holds = ModuleHolds.Any;
    /// <summary>Herstellkosten: Warenschlüssel → Menge. Seit Phase 5a das Rezept der Modulfabrik (Eingänge; Ausgang 1 Modul-Ware gleichen Schlüssels).</summary>
    public Dictionary<string, float> Cost = new();
    /// <summary>Zulässige Treibstoffe des Antriebs (Ware → efficiency); leer bei Nicht-Antrieben. Verbrauch erst ab Phase 4b.</summary>
    public List<FuelType> FuelTypes = new();
}

/// <summary>Ein Slot einer Schiffsklasse, in fester Reihenfolge; der Fit füllt sie per Index.</summary>
public sealed class ClassSlot
{
    /// <summary>Slotart-Schlüssel.</summary>
    public string Type = "";
    public SlotSize Size = SlotSize.S;
    /// <summary>Ausrichtung des Slots (bow/port/starboard/stern), nur Waffen- und Werkzeug-Slots.</summary>
    public string? Facing;
    /// <summary>Halber Öffnungswinkel in Grad, nur Waffen- und Werkzeug-Slots.</summary>
    public float? Arc;
}

/// <summary>Schiffsklasse: Rumpf mit Größe, Basiswerten und fester Slotliste.</summary>
public sealed class ShipClass
{
    public string Key = "";
    public string Name = "";
    public SlotSize Size = SlotSize.S;
    public float BaseHull = 100f;
    public float BaseMass = 10f;
    /// <summary>Tankkapazität des Rumpfs; Standard 0. Zusammen mit fuelCapacity-Modulen ergibt sich <see cref="ShipStats.FuelCapacity"/>.</summary>
    public float BaseFuel;
    public List<ClassSlot> Slots = new();
    /// <summary>Warenschlüssel des Bausatzes, aus dem die Werft Schiffe dieser Klasse baut (Phase 5a: ship_kit_light/medium/heavy); null = Regel <see cref="EconomyRules.ShipKitGoodKey"/>.</summary>
    public string? Kit;
}

/// <summary>Schiffsmodell: eine Klasse mit festem Fit (Modulschlüssel pro Slot, null = leerer Slot).</summary>
public sealed class ShipModel
{
    public string Key = "";
    /// <summary>Klassen-Schlüssel.</summary>
    public string ClassKey = "";
    public string Name = "";
    /// <summary>Modulschlüssel je Slot der Klasse, null erlaubt; Länge = Slotanzahl der Klasse.</summary>
    public List<string?> Fit = new();
}
