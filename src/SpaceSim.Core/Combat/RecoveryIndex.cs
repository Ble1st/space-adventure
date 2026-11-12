using SpaceSim.Core.Model;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 23: abgeleiteter Index der Schiffe, deren Schild oder Speicherbank nicht voll ist (aufsteigende Id = feste Reihenfolge). Nicht gespeichert:
/// Nach dem Laden baut <see cref="Rebuild"/> ihn aus dem Zustand neu auf. Ein Schiff, das voll ist oder nicht mehr existiert, fällt beim nächsten Schritt von
/// <see cref="ShipSystemsSystem"/> heraus; eine überzählige Eintragung ändert also nie den Zustand (Speichern → Laden → Weiterlaufen bleibt bit-identisch).
/// </summary>
public sealed class RecoveryIndex
{
    readonly SortedSet<int> _ids = new();

    public int Count => _ids.Count;
    public bool Contains(ShipId id) => _ids.Contains(id.Value);
    public void Add(ShipId id) => _ids.Add(id.Value);
    public void Remove(ShipId id) => _ids.Remove(id.Value);

    /// <summary>Kopiert die Ids in <paramref name="into"/> (aufsteigend), damit der Aufrufer den Index beim Durchlaufen ändern darf.</summary>
    public void CopyTo(List<int> into)
    {
        into.Clear();
        into.AddRange(_ids);
    }

    /// <summary>Neu aufbauen: jedes Schiff, dem Schild oder Ladung fehlt.</summary>
    public void Rebuild(Universe u)
    {
        _ids.Clear();
        foreach (var s in u.Ships.Values)
            if (ShipSystems.NeedsRecovery(s)) _ids.Add(s.Id.Value);
    }
}
