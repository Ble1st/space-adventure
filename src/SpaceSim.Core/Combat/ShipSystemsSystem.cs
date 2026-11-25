using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 23 (KAMPF.md Abschnitt 3.2): schreibt Schild und Speicherbank der Schiffe fort, denen etwas fehlt (<see cref="Universe.Recovering"/>), nach der
/// Leistungsregel <see cref="ShipSystems.Advance"/>; skaliert mit <c>dt</c>. Volle Schiffe stehen nicht im Index und kosten nichts. Ist ein Schiff voll
/// (oder entfernt), fällt es heraus.
/// </summary>
public sealed class ShipSystemsSystem : ISimSystem
{
    readonly List<int> _buffer = new();

    public void Step(Universe u, int dt)
    {
        if (u.Recovering.Count == 0) return;
        u.Recovering.CopyTo(_buffer);
        float seconds = ShipSystems.Seconds(dt);
        foreach (int raw in _buffer)
        {
            var id = new ShipId(raw);
            if (!u.Ships.TryGetValue(id, out var ship)) { u.Recovering.Remove(id); continue; }
            bool paused = u.Tick < ship.Systems.ShieldPauseUntil;
            bool burning = ship.Systems.Burning.Count > 0;   // Phase 26: brennende Schiffe bleiben im Index (FireSystem)
            if (ShipSystems.Advance(ship.Stats, ship.Systems, seconds, paused, u.Rules, Nebula.ShieldRegenFactor(u, ship.Sector) * Heat.ShieldRegenFactor(u, ship)))
            {
                // Phase 24 (Prüfung Phase 23): ohne mögliche Erholung (kein Überschuss, Schild ohne Regeneration) aus dem Index — ein Modulwechsel weckt es wieder
                if (!ShipSystems.CanRecover(ship.Stats, ship.Systems) && !burning) u.Recovering.Remove(id);
                continue;
            }
            ship.Systems.ShieldPauseUntil = 0;   // voll: keine Pause mehr nötig (Standardzustand, nichts im Spielstand)
            if (!burning) u.Recovering.Remove(id);
        }
    }
}
