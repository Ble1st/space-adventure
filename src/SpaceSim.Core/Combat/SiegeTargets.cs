using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Weapons;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 31 (docs/TORE.md, Abschnitt 7): gemeinsame Naht für belagerbare Ziele. Belagerung (<see cref="Siege"/>), Gefechtsschritt, Taktiken, Raketen und
/// Spielerschüsse arbeiten über diese Schnittstelle — Station und Tor sind Adapter (<see cref="StationTarget"/>, <see cref="GateTarget"/>) über ihre bisherigen
/// Regeln (<see cref="StationCombat"/>, <see cref="GateCombat"/>). Ein Gefecht hat höchstens ein Belagerungsziel (<see cref="Encounter.Target"/>).
/// </summary>
public interface ISiegeTarget
{
    SiegeRef Ref { get; }
    string Name { get; }
    /// <summary>Sektor des Ziels (bei Toren die Hälfte in diesem Sektor).</summary>
    SectorId Sector { get; }
    Vec2 Position { get; }
    /// <summary>Körperradius (Waffen der Angreifer messen bis zur Außenhaut).</summary>
    float Radius { get; }
    /// <summary>Besitzer, dem ein Fall als Verlust gebucht wird.</summary>
    ActorId Owner { get; }
    /// <summary>Fraktionen, die das Ziel verteidigen (Station: ihre Fraktion; Tor: Besitzer beider Endsektoren).</summary>
    IReadOnlyList<FactionId> Defenders { get; }
    float MaxHull { get; }
    float MaxShield { get; }
    float Hull { get; }
    float Shield { get; }
    float PointDefense { get; }
    /// <summary>Außer Gefecht (ausgeschaltet bzw. außer Betrieb) — kein Ziel mehr.</summary>
    bool IsDown { get; }
    /// <summary>Größte Geschützreichweite ab Mitte (0 = keine Geschütze).</summary>
    float GunRange { get; }
    TargetProfile Profile { get; }
    /// <summary>Verbrechen, wenn der Spieler das Ziel angreift.</summary>
    string Crime { get; }
    /// <summary>Treffer anwenden (Schild mit Pause, Hülle; bei 0 fällt das Ziel). Liefert den Hüllenschaden.</summary>
    float Apply(Encounter? e, in HitResult hit, ActorId attacker);
    /// <summary>Verteidigt das Ziel sich noch (steht, nicht erobert)?</summary>
    bool Defends(Encounter e);
}

/// <summary>Adapter Station → <see cref="ISiegeTarget"/> (Regeln unverändert aus <see cref="StationCombat"/>).</summary>
public sealed class StationTarget : ISiegeTarget
{
    readonly Universe _u;
    public Station Station { get; }
    public StationTarget(Universe u, Station st) { _u = u; Station = st; }
    public SiegeRef Ref => SiegeRef.Of(Station.Id);
    public string Name => Station.Name;
    public SectorId Sector => Station.Sector;
    public Vec2 Position => Station.Position;
    public float Radius => Station.Radius;
    public ActorId Owner => Station.Owner;
    public IReadOnlyList<FactionId> Defenders => _u.FactionOf(Station) is { } f ? new[] { f } : Array.Empty<FactionId>();
    public float MaxHull => StationCombat.MaxHull(_u, Station);
    public float MaxShield => StationCombat.MaxShield(_u, Station);
    public float Hull => StationCombat.Hull(_u, Station);
    public float Shield => StationCombat.Shield(_u, Station);
    public float PointDefense => StationCombat.DefenseOf(_u, Station)?.PointDefense ?? 0f;
    public bool IsDown => StationCombat.IsDown(_u, Station);
    public float GunRange => StationCombat.GunRange(_u, Station);
    public TargetProfile Profile => StationCombat.Profile(_u, Station);
    public string Crime => "assault_station";
    public float Apply(Encounter? e, in HitResult hit, ActorId attacker) => StationCombat.Apply(_u, e, Station, hit, attacker);
    public bool Defends(Encounter e) => StationCombat.Defends(_u, e);
}

/// <summary>Adapter Tor-Hälfte → <see cref="ISiegeTarget"/> (Zustand des Paars über <see cref="GateCombat"/>).</summary>
public sealed class GateTarget : ISiegeTarget
{
    readonly Universe _u;
    public Gate Gate { get; }
    public GateTarget(Universe u, Gate g) { _u = u; Gate = g; }
    public SiegeRef Ref => SiegeRef.Of(Gate.Id);
    public string Name => GateCombat.NameOf(_u, Gate);
    public SectorId Sector => Gate.Sector;
    public Vec2 Position => Gate.Position;
    public float Radius => GateCombat.BodyRadius;
    public ActorId Owner => GateCombat.OwnerOf(_u, Gate);
    public IReadOnlyList<FactionId> Defenders => GateCombat.DefendersOf(_u, Gate);
    public float MaxHull => GateCombat.MaxHull(_u, Gate);
    public float MaxShield => GateCombat.MaxShield(_u, Gate);
    public float Hull => GateCombat.Hull(_u, Gate);
    public float Shield => GateCombat.Shield(_u, Gate);
    public float PointDefense => GateCombat.TypeOf(_u, Gate)?.Defense.PointDefense ?? 0f;
    public bool IsDown => GateCombat.IsDown(_u, Gate) || GateCombat.InGrace(_u, Gate);   // Runde 14b: in der Schonfrist kein Ziel
    public float GunRange => GateCombat.GunRange(_u, Gate);
    public TargetProfile Profile => GateCombat.Profile(_u, Gate);
    public string Crime => "assault_gate";
    public float Apply(Encounter? e, in HitResult hit, ActorId attacker) => GateCombat.Apply(_u, Gate, hit, attacker);
    public bool Defends(Encounter e) => e.Cause == EncounterCause.Siege && !GateCombat.IsDown(_u, Gate);
}

/// <summary>Phase 31: Auflösen und gemeinsame Rechnungen über <see cref="ISiegeTarget"/>.</summary>
public static class SiegeTargets
{
    /// <summary>Ziel eines Verweises (null = keins oder nicht mehr da).</summary>
    public static ISiegeTarget? Resolve(Universe u, SiegeRef r) => r.Kind switch
    {
        SiegeKind.Station when u.Stations.TryGetValue(r.Station, out var st) => new StationTarget(u, st),
        SiegeKind.Gate when u.Gates.TryGetValue(r.Gate, out var g) => new GateTarget(u, g),
        _ => null,
    };

    /// <summary>Belagerungsziel eines Gefechts (null = keins).</summary>
    public static ISiegeTarget? Of(Universe u, Encounter e) => e.Target.IsNone ? null : Resolve(u, e.Target);

    /// <summary>
    /// Abstand eines Teilnehmers zum Ziel (Mitte): im räumlichen Gefecht echt; das belagerte Ziel steht auf der Linie bei <see cref="Encounter.StationX"/>;
    /// sonst die liegende Position des Schiffs.
    /// </summary>
    public static float Distance(Universe u, Encounter e, CombatParticipant p, ISiegeTarget t)
    {
        if (e.Spatial && p.InSpace) return Vec2.Distance(p.Pos, t.Position);
        if (t.Ref == e.Target) return MathF.Abs(p.X - e.StationX);
        return u.Ships.TryGetValue(p.Ship, out var s) ? Vec2.Distance(s.Position, t.Position) : float.MaxValue;
    }

    /// <summary>Verteidigt das Belagerungsziel des Gefechts sich noch?</summary>
    public static bool Defends(Universe u, Encounter e) => e.Cause == EncounterCause.Siege && Of(u, e) is { } t && t.Defends(e);

    /// <summary>Laufende Belagerung genau dieses Ziels (bei Toren: dieser Hälfte, im Sektor dieser Hälfte; null = keine).</summary>
    public static Encounter? SiegeOf(Universe u, SiegeRef r)
    {
        if (r.IsNone) return null;
        foreach (var e in u.Fights.Encounters) if (!e.Done && e.Target == r) return e;
        return null;
    }
}

/// <summary>Phase 31: ein Schiff schießt auf ein Belagerungsziel (gemeinsam für Station und Tor; Stationen über <see cref="StationCombat.Shoot"/>).</summary>
public static class SiegeShot
{
    public static HitResult Shoot(Universe u, Encounter e, CombatParticipant shooterP, Ship shooter, int slot, Module m, in WeaponProfile w, ISiegeTarget t, float distance, float factor, float reload)
    {
        if (t is StationTarget st) return StationCombat.Shoot(u, e, shooterP, shooter, slot, m, w, st.Station, distance, factor, reload);
        var r = u.Rules;
        if (w.AmmoPerShot > 0f && r.AmmoActive) shooter.Systems.AmmoMissing = MathF.Min(shooter.Stats.AmmoCapacity, shooter.Systems.AmmoMissing + w.AmmoPerShot);
        var hit = Hit.Roll(u.Rng, w, t.Profile, distance, factor, r);
        while (shooterP.Timers.Count <= slot) shooterP.Timers.Add(0f);
        shooterP.Timers[slot] = reload;
        e.Shots++;
        float hull = t.Apply(e, hit, shooter.Owner);
        if (hit.Hit) e.Hits++;
        u.Events.Publish(new ShotAtGate(e.Id, e.Sector, shooter.Id, t.Ref.Gate, m.Key, m.DamageType, hit.Hit, hit.ShieldDamage, hull, slot));
        return hit;
    }
}
