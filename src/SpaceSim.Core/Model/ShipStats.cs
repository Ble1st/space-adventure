namespace SpaceSim.Core.Model;

/// <summary>
/// Reine Ableitung aller Schiffs­werte aus Klasse und Fit (<see cref="Compute"/>). Phase 3a: noch ohne
/// Verbraucher — Ship.MaxHull, Ship.CargoCapacity und Travel.ShipSpeed bleiben vorerst maßgeblich.
/// </summary>
public sealed class ShipStats
{
    /// <summary>Höchsttempo; Schub/Gesamtmasse wird darauf begrenzt.</summary>
    public const float MaxSpeed = 12f;

    /// <summary>Leistung aller Reaktoren (stats powerOutput).</summary>
    public float PowerOutput;
    /// <summary>Dauerlast aller Module (Summe powerDraw).</summary>
    public float PowerDraw;
    /// <summary>Spitzenlast aller Module (Summe peakDraw), gepuffert von der Speicherbank.</summary>
    public float PeakDraw;
    /// <summary>Kapazität der Speicherbänke (Summe stats powerStorage).</summary>
    public float PowerStorage;
    /// <summary>Laderaum: Summe der Kapazitäten (stats capacity) aller Frachtmodule; holds any zählt voll, die 70%-Regel steckt in den Modul-Daten.</summary>
    public float Cargo;
    /// <summary>Feststoff-Laderaum: Summe der Kapazitäten der Frachtmodule mit holds solid (0 = keiner).</summary>
    public float CargoSolid;
    /// <summary>Flüssigstoff-Laderaum (Kanister): Summe der Kapazitäten der Frachtmodule mit holds liquid (0 = keiner).</summary>
    public float CargoLiquid;
    /// <summary>Universal-Laderaum: Summe der Kapazitäten der Frachtmodule mit holds any; wird zwischen den Formen geteilt.</summary>
    public float CargoAny;
    /// <summary>Hülle: baseHull der Klasse + Panzerung (Summe stats armor).</summary>
    public float Hull;
    /// <summary>Gesamtmasse: baseMass der Klasse + Summe der Modulmassen.</summary>
    public float Mass;
    /// <summary>Schub: Summe stats thrust.</summary>
    public float Thrust;
    /// <summary>Tempo = Schub / Gesamtmasse, begrenzt auf <see cref="MaxSpeed"/>.</summary>
    public float Speed;
    /// <summary>Unterhalt: Summe upkeep der Module.</summary>
    public float Upkeep;
    /// <summary>Fähigkeiten: Vereinigung aller grants der Module.</summary>
    public Capability Capabilities = Capability.None;
    /// <summary>Laserstufe des Werkzeugmoduls mit Mining (stats chunkChanceMult), sonst 1.</summary>
    public float LaserChunkChanceMult = 1f;
    /// <summary>Laserstufe des Werkzeugmoduls mit Mining (stats chunkSizeMult), sonst 1.</summary>
    public float LaserChunkSizeMult = 1f;
    /// <summary>Tankkapazität: baseFuel der Klasse + Summe stats fuelCapacity der Module (z. B. Tanks).</summary>
    public float FuelCapacity;
    /// <summary>Zulässige Treibstoffe der Antriebsmodule (Ware → efficiency); bei mehreren Antrieben Vereinigung, höchste efficiency je Ware.</summary>
    public List<FuelType> FuelTypes = new();
    /// <summary>Treibstoffverbrauch der Antriebsmodule: Summe stats fuelUse (Einheiten Treibstoff je geflogener Einheit, vor efficiency). 0 = kein Verbrauch.</summary>
    public float FuelUsePerUnit;
    /// <summary>Phase 8: Spaltlaser (Asteroidenbrecher, stats splitPower); 0 = kein Spaltlaser.</summary>
    public float SplitPower;

    /// <summary>
    /// Berechnet die Werte eines Schiffs aus seiner Klasse und dem Fit (ein Eintrag je Slot, null = leer).
    /// Reine Funktion ohne Universum und ohne Nebeneffekte.
    /// </summary>
    public static ShipStats Compute(ShipClass cls, IReadOnlyList<Module?> fit)
    {
        var s = new ShipStats { Mass = cls.BaseMass, FuelCapacity = cls.BaseFuel };
        foreach (var m in fit)
        {
            if (m is null) continue;
            s.Mass += m.Mass;
            s.PowerDraw += m.PowerDraw;
            s.PeakDraw += m.PeakDraw;
            s.Upkeep += m.Upkeep;
            foreach (var c in m.Grants) s.Capabilities |= c;
            if (m.Stats.TryGetValue("powerOutput", out var output)) s.PowerOutput += output;
            if (m.Stats.TryGetValue("powerStorage", out var storage)) s.PowerStorage += storage;
            if (m.Stats.TryGetValue("fuelCapacity", out var fuelCap)) s.FuelCapacity += fuelCap;
            if (m.Stats.TryGetValue("fuelUse", out var fuelUse)) s.FuelUsePerUnit += fuelUse;   // Phase 4b: Verbrauchsrate des Antriebs
            if (m.Stats.TryGetValue("splitPower", out var split)) s.SplitPower += split;      // Phase 8: Spaltlaser
            foreach (var f in m.FuelTypes)
            {
                int idx = s.FuelTypes.FindIndex(x => x.Good == f.Good);
                if (idx < 0) s.FuelTypes.Add(f);
                else if (f.Efficiency > s.FuelTypes[idx].Efficiency) s.FuelTypes[idx] = f;   // mehrere Antriebe: höchste efficiency je Ware
            }
            if (m.Stats.TryGetValue("capacity", out var capacity))
            {
                s.Cargo += capacity;
                switch (m.Holds)
                {
                    case ModuleHolds.Solid: s.CargoSolid += capacity; break;
                    case ModuleHolds.Liquid: s.CargoLiquid += capacity; break;
                    default: s.CargoAny += capacity; break;
                }
            }
            if (m.Stats.TryGetValue("armor", out var armor)) s.Hull += armor;
            if (m.Stats.TryGetValue("thrust", out var thrust)) s.Thrust += thrust;
            if (m.Grants.Contains(Capability.Mining))
            {
                s.LaserChunkChanceMult = m.Stats.TryGetValue("chunkChanceMult", out var ccm) ? ccm : 1f;
                s.LaserChunkSizeMult = m.Stats.TryGetValue("chunkSizeMult", out var csm) ? csm : 1f;
            }
        }
        s.LaserChunkChanceMult *= cls.ToolEfficiency;   // Phase 6: Rumpf mit geteilter Leistung (Mehrzweck) schwächt den Laser
        s.Hull += cls.BaseHull;
        s.Speed = s.Mass > 0f ? MathF.Min(MaxSpeed, s.Thrust / s.Mass) : 0f;
        return s;
    }
}
