namespace SpaceSim.Core.Model;

/// <summary>Phase 26: Art eines Objekts im All, das Bergungsgut trägt.</summary>
public enum WreckKind
{
    /// <summary>Wrack eines zerstörten Schiffs: Teil der Fracht und einzelne Module mit Restzustand.</summary>
    Wreck,
    /// <summary>Frachtbehälter: abgeworfene Fracht (Aufgabe im Gefecht) oder überzählige Fracht eines ausgefallenen Frachtmoduls.</summary>
    Container,
}

/// <summary>Phase 26: ein Modul im Wrack mit seinem Restzustand (0..1).</summary>
public sealed class WreckModule
{
    public string Key = "";
    public float Condition;
}

/// <summary>
/// Phase 26 (KAMPF.md 4.4): ein Wrack oder Frachtbehälter im Sektor — eigenes, gespeichertes und gehashtes Objekt (kein Hindernis). Fracht kann jeder mit
/// Traktorstrahl aufnehmen, Module nur ein Schiff mit der Fähigkeit <see cref="Capability.Salvage"/> (Bergungsgreifer). Verfällt bei
/// <see cref="ExpiresTick"/> oder sobald es leer ist (<see cref="Simulation.WreckSystem"/>).
/// </summary>
public sealed class Wreck
{
    public int Id;
    public WreckKind Kind;
    public SectorId Sector;
    public Vec2 Position;
    /// <summary>Anzeigename (z. B. „Wrack: Hauler #1012“).</summary>
    public string Name = "";
    /// <summary>Herkunft (Schiff), 0 = unbekannt.</summary>
    public ShipId From;
    public long CreatedTick;
    public long ExpiresTick;
    public Dictionary<GoodId, float> Cargo = new();
    public List<WreckModule> Modules = new();

    /// <summary>Nichts mehr zu holen (Fracht leer, keine Module)?</summary>
    public bool IsEmpty
    {
        get
        {
            if (Modules.Count > 0) return false;
            foreach (var v in Cargo.Values) if (v > 0.001f) return false;
            return true;
        }
    }

    /// <summary>Fracht gesamt (Einheiten).</summary>
    public float CargoUnits
    {
        get
        {
            float sum = 0f;
            foreach (var g in Cargo.Keys.OrderBy(k => k.Value)) sum += Cargo[g];
            return sum;
        }
    }
}
