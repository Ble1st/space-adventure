using System.Collections.Generic;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Weapons;

namespace SpaceAdventure.Game;

/// <summary>
/// Phase 25: Spielerkampf in der Ansicht (KAMPF.md 4.3, 5) — eigene Komponente statt Logik in <c>Main</c>. Hält nur Bedienzustand (gewähltes Ziel,
/// Dauerfeuer, Beschusswarnung); jeder Schuss geht über <see cref="PlayerGunnery.TryFire"/> (Core-Regel, in der Simulation gebucht), jede Zielwahl über
/// <see cref="PlayerCombat"/>. Abgeworfene Fracht eines überfallenen Schiffs (<see cref="CargoDropped"/>) wird zu Brocken für den Traktorstrahl.
/// </summary>
public sealed class CombatController
{
    /// <summary>Gewähltes Schiffsziel (default = keins).</summary>
    public ShipId Target { get; private set; }
    /// <summary>Dauerfeuer: jede bereite Waffe im Winkel schießt, sobald sie nachgeladen ist.</summary>
    public bool AutoFire { get; set; }
    /// <summary>Letzter Schütze, der das Spielerschiff beschossen hat, und wann (Sekunden Spielzeit der Ansicht).</summary>
    public ShipId LastAttacker { get; private set; }
    public double LastHitAt { get; private set; } = -99;
    /// <summary>Letzte Meldung eines Schusses (Selbsttest).</summary>
    public string LastMessage { get; private set; } = "";
    public GunResult LastResult { get; private set; }
    /// <summary>Zahl der Spielerschüsse (Selbsttest).</summary>
    public int Shots { get; private set; }

    public void SetTarget(ShipId id) => Target = id;
    public void ClearTarget() => Target = default;

    /// <summary>Ziel noch gültig (existiert, im Sektor)? Sonst wird es verworfen.</summary>
    public void Validate(Universe u)
    {
        if (Target.Value == 0) return;
        if (!u.Ships.ContainsKey(Target) || PlayerCombat.PositionOf(u, Target) is not { } p || p.Sector != u.PlayerSector || Target == u.PlayerShip) Target = default;
    }

    /// <summary>„Nächster Gegner“ (Core-Regel <see cref="PlayerCombat.NextEnemy"/>); false = keiner da.</summary>
    public bool NextEnemy(Universe u, Ship player)
    {
        var next = PlayerCombat.NextEnemy(u, player, Target);
        if (next.Value == 0) return false;
        Target = next;
        return true;
    }

    /// <summary>Ein Schuss der Waffe in <paramref name="slot"/> auf das gewählte Ziel.</summary>
    public GunShot Fire(Universe u, Ship player, int slot, float headingDeg)
    {
        var r = PlayerGunnery.TryFire(u, player, slot, Target, headingDeg);
        LastResult = r.Result;
        LastMessage = r.Message;
        if (r.Result == GunResult.Fired) Shots++;
        return r;
    }

    /// <summary>Schuss auf das Spielerschiff (für die Beschusswarnung mit Richtung).</summary>
    public void OnShot(Universe u, ShotFired e, double now)
    {
        if (e.Target != u.PlayerShip) return;
        LastAttacker = e.Shooter;
        LastHitAt = now;
    }

    /// <summary>Warnzeile „Beschuss!“ mit Richtung relativ zur Schiffsnase (leer = keine Warnung); 3 s nach dem letzten Schuss.</summary>
    public string Warning(Universe u, Vec2 pos, float headingDeg, double now)
    {
        if (now - LastHitAt > 3.0 || PlayerCombat.PositionOf(u, LastAttacker) is not { } a) return "";
        float rel = FiringArc.Normalize180(FiringArc.Bearing(a.Pos - pos) - headingDeg);
        string dir = rel > 135f || rel < -135f ? "▼ von hinten" : rel > 45f ? "◀ von links" : rel < -45f ? "▶ von rechts" : "▲ von vorn";
        return $"BESCHUSS! {dir}";
    }

    /// <summary>Abgeworfene Fracht (vom Spieler überfallen) → Brocken nahe dem Opfer, einsammelbar mit dem Traktorstrahl.</summary>
    public static void SpawnDrops(OreChunks chunks, CargoDropped d, int seq)
    {
        // in Teilstücke bis 10 Einheiten, leicht gestreut (Darstellung; nicht gespeichert, wie die Erzbrocken)
        float left = d.Units;
        int i = 0;
        while (left > 0.001f && i < 12)
        {
            float part = System.MathF.Min(10f, left);
            float a = (seq * 37 + i * 71) % 360 * System.MathF.PI / 180f;
            chunks.Chunks.Add(new OreChunk
            {
                Good = d.Good, Units = part, Position = d.Position + new Vec2(System.MathF.Cos(a), System.MathF.Sin(a)) * 3f,
                Velocity = new Vec2(System.MathF.Cos(a), System.MathF.Sin(a)) * 2f,
            });
            left -= part;
            i++;
        }
    }

    /// <summary>Zustände der Waffenschächte (Kampfwaffen) für die Anzeige.</summary>
    public static BayState StateOf(Universe u, Ship player, WeaponBay bay, float headingDeg, ShipId target) =>
        PlayerGunnery.State(u, player, PlayerGunnery.SlotOf(bay.Key), headingDeg, target);

    public void Reset() { AutoFire = false; Target = default; LastAttacker = default; LastHitAt = -99; }

    /// <summary>Kurztext Schild/Hülle des Ziels für die Zielkarte.</summary>
    public static string TargetLine(Universe u, Ship target)
    {
        string shield = target.Stats.Shield > 0f ? $"Schild {target.ShieldNow:0}/{target.Stats.Shield:0}" : "kein Schild";
        return $"{shield} · Hülle {target.Hull:0}/{target.MaxHull:0}";
    }

    /// <summary>Schiffs-Ids, deren Marker in der Nähe eines Bodenpunkts liegen (für das Antippen), nächstes zuerst.</summary>
    public static ShipId? Pick(IEnumerable<(ShipId Id, Vec2 Pos)> ships, Vec2 ground, float radius)
    {
        ShipId? best = null; float bd = radius;
        foreach (var (id, p) in ships)
        {
            float d = Vec2.Distance(p, ground);
            if (d < bd) { bd = d; best = id; }
        }
        return best;
    }
}
