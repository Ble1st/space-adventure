namespace SpaceSim.Core.Model;

/// <summary>
/// Phase 23 (KAMPF.md Abschnitt 3.2): Leistungsverteilung eines Schiffs. Die Stellung verschiebt den Reaktorüberschuss zwischen Waffen (lädt die
/// Speicherbank), Schild (Regeneration) und Antrieb. <see cref="Balanced"/> teilt gleichmäßig. Der Spieler wählt sie über drei Tasten, NPCs später über
/// ihre Taktik (Phase 24).
/// </summary>
public enum PowerMode { Balanced, Weapons, Shield, Engine }

/// <summary>
/// Phase 23 (KAMPF.md Abschnitt 3.1): Laufzeitzustand der Bordsysteme eines Schiffs, getrennt von Laderaum und Tank. Gespeichert werden die
/// <b>Fehlbeträge</b> gegenüber dem Höchstwert aus <see cref="ShipStats"/> (Schild, Speicherbank, Magazin): 0 = voll. So ist jedes neue Schiff ohne
/// weiteres Zutun voll, und ein unbeschädigtes Schiff hat nur Standardwerte (nichts im Spielstand, keine Rechenzeit).
/// </summary>
public sealed class ShipSystemsState
{
    /// <summary>Fehlender Schild (0 = voll); höchstens <see cref="ShipStats.Shield"/>.</summary>
    public float ShieldMissing;
    /// <summary>Fehlende Ladung der Speicherbank (0 = voll); höchstens <see cref="ShipStats.PowerStorage"/>.</summary>
    public float ChargeMissing;
    /// <summary>Fehlende Munition im Magazin (0 = voll); höchstens <see cref="ShipStats.AmmoCapacity"/>. Mit abgeschaltetem Schalter <c>ammoActive</c> gilt das Magazin als voll.</summary>
    public float AmmoMissing;
    /// <summary>
    /// Zustand je Slot 0..1 (Index = Slot). Fehlende Einträge (auch eine leere Liste) gelten als 1. Unter <see cref="EconomyRules.ModuleFailCondition"/>
    /// fällt das Modul aus (<see cref="ShipStats.Compute(ShipClass, IReadOnlyList{Module?}, IReadOnlyList{float}?, float)"/>). In Phase 23 senkt noch nichts
    /// den Zustand (Modulschaden kommt mit Phase 26); Modell, Spielstand, Hash und Wertberechnung sind vorbereitet.
    /// </summary>
    public List<float> Condition = new();
    /// <summary>Leistungsverteilung (Standard: ausgeglichen).</summary>
    public PowerMode Mode;
    /// <summary>Bis zu diesem Tick (ausschließlich) lädt der Schild nach einem Treffer nicht nach (KAMPF.md 3.3 „nach einer kurzen Pause“); 0 = keine Pause.</summary>
    public long ShieldPauseUntil;

    /// <summary>Zustand eines Slots (fehlender Eintrag = 1).</summary>
    public float ConditionOf(int slot) => slot >= 0 && slot < Condition.Count ? Condition[slot] : 1f;

    /// <summary>Alles auf Standard (voll, ausgeglichen, unbeschädigt)? Dann wird nichts gespeichert.</summary>
    public bool IsDefault =>
        ShieldMissing == 0f && ChargeMissing == 0f && AmmoMissing == 0f && Mode == PowerMode.Balanced && ShieldPauseUntil == 0 && AllIntact;

    /// <summary>Alle Slots im Zustand 1?</summary>
    public bool AllIntact
    {
        get
        {
            foreach (var c in Condition) if (c != 1f) return false;
            return true;
        }
    }
}
