namespace SpaceSim.Core.Model;

/// <summary>
/// Instabiles Wurmloch (docs/ERKUNDUNG.md „Wurmlöcher“): reißt in einem Sektor auf, führt einseitig an einen Ort in einem zufälligen anderen Sektor und fällt
/// nach seiner Frist zusammen. Nur der Spieler nutzt es (NPCs kennen es nicht, es ist keine Kante im Torgraphen). Gespeichert, gehasht nur wenn je eines lief.
/// Stabile Wurmlöcher sind dagegen Torpaare mit <see cref="Gate.Wormhole"/>.
/// </summary>
public sealed class UnstableWormhole
{
    public int Id;
    public SectorId Sector;
    public Vec2 Position;
    /// <summary>Zielsektor und Austrittsort.</summary>
    public SectorId Target;
    public Vec2 TargetPosition;
    public long StartTick;
    public long EndTick;
    /// <summary>Hat der Spieler es schon durchflogen (dann ist das Ziel bekannt)? Gespeichert, gehasht.</summary>
    public bool Traversed;
}
