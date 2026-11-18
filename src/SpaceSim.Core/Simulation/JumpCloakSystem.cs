using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Sprungantrieb und Tarnfeld (SCHIFFSSLOTS.md 4 und 6): je Tick das Aufladen fortschreiben (<see cref="JumpDrive.Step"/>) und die Tarnung prüfen
/// (<see cref="Cloak.Check"/>). Nur Schiffe mit gesetztem Zustand kosten Rechenzeit — heute nur Spielerschiffe (die KI nutzt beides nicht), daher bleibt
/// die NPC-Welt unberührt. Skaliert mit <c>dt</c>.
/// </summary>
public sealed class JumpCloakSystem : ISimSystem
{
    readonly List<Ship> _work = new();

    public void Step(Universe u, int dt)
    {
        _work.Clear();
        foreach (var s in u.Ships.Values)
            if (s.Cloaked || s.JumpTarget.Value != 0) _work.Add(s);
        if (_work.Count == 0) return;
        _work.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
        foreach (var s in _work)
        {
            Cloak.Check(u, s);
            if (s.JumpTarget.Value != 0 && s.Id != u.PlayerShip && s.IsPlayer) JumpDrive.Abort(u, s, "ship", "Schiffswechsel");
            JumpDrive.Step(u, s, dt);
        }
    }
}
