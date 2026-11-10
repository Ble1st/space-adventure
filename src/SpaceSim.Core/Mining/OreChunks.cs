using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Mining;

public sealed class OreChunk
{
    public Vec2 Position, Velocity;
    public GoodId Good;
    public float Units;
    public float Age;
}

/// <summary>Ergebnis eines Laserschusses; <see cref="NoLaser"/> (Phase 5c): Das Schiff hat kein Schürfwerkzeug (keine Fähigkeit Mining, z. B. Nomad).</summary>
public enum MineResult { Fired, Cooldown, OutOfRange, NotMinable, CargoFull, NoLaser }

/// <summary>
/// Echtzeit-Mikrosimulation des Spielerabbaus im aktuellen Sektor. Der Laser wird <b>einzeln</b> abgefeuert
/// (<see cref="TryFire"/>, mit Nachladezeit <see cref="MiningRules.ShotInterval"/>); jeder Schuss löst nach der gemeinsamen
/// Regel <see cref="LaserShot"/> mit Wahrscheinlichkeit einen Brocken zufälliger Größe aus dem Asteroiden. Das Erz verlässt den
/// Asteroiden beim Lösen (er schrumpft, bei 0 verschwindet er und wächst später per Respawn nach); der Traktorstrahl
/// zieht die Brocken ein. Nicht eingesammelte Brocken verfallen. Flüchtig (nicht gespeichert), engine-unabhängig, testbar.
/// </summary>
public sealed class OreChunks
{
    public List<OreChunk> Chunks { get; } = new();
    readonly DeterministicRng _rng;
    float _cooldown;

    public OreChunks(ulong seed = 0xC0FFEE) => _rng = new DeterministicRng(seed);

    /// <summary>Ein Schuss ist gefallen: Asteroid und freigesetzte Einheiten (0 = Fehlschuss ohne Brocken).</summary>
    public event Action<Asteroid, LaserShot.Result>? Shot;

    /// <summary>Der Laser ist nachgeladen und kann feuern.</summary>
    public bool Ready => _cooldown <= 0f;

    /// <summary>Nachlade-Fortschritt 0..1 (0 = direkt nach dem Schuss, 1 = bereit) – für den Balken der Waffentaste.</summary>
    public float ReloadProgress => 1f - Math.Clamp(_cooldown / MiningRules.ShotInterval, 0f, 1f);

    /// <summary>Beim Sektorwechsel/Laden: schwebende Brocken verschwinden.</summary>
    public void Clear() { Chunks.Clear(); _cooldown = 0f; }

    /// <summary>Volumen der noch schwebenden Brocken (reservierter Laderaum, damit der Laser nicht über die Kapazität hinaus feuert).</summary>
    float InFlightVolume(Universe u) => Chunks.Sum(c => c.Units * u.Goods[c.Good].VolumePerUnit);

    /// <summary>Volumen der noch schwebenden Brocken einer Warenform.</summary>
    float InFlightVolume(Universe u, GoodForm form) => Chunks.Where(c => u.Goods[c.Good].Form == form).Sum(c => c.Units * u.Goods[c.Good].VolumePerUnit);

    public bool IsMinable(Sector sector, Asteroid? a) => a != null && a.Minable && sector.Asteroids.Contains(a);

    static bool HasClass(Universe u, Asteroid a) => u.AsteroidClasses.ContainsKey(a.ClassKey);

    /// <summary>Nächster abbaubarer Asteroid (Abstand Schiff–Oberfläche), optional nur innerhalb von <paramref name="maxEdge"/>.</summary>
    public Asteroid? Nearest(Sector sector, Vec2 shipPos, float maxEdge = float.MaxValue)
    {
        Asteroid? best = null;
        float bestEdge = maxEdge;
        foreach (var a in sector.Asteroids)
        {
            if (!a.Minable) continue;
            float edge = Vec2.Distance(shipPos, a.Position) - a.Radius;
            if (edge <= bestEdge) { best = a; bestEdge = edge; }
        }
        return best;
    }

    /// <summary>
    /// Feuert genau einen Schuss, falls nachgeladen. Dauerfeuer entsteht nur, wenn der Aufrufer jedes Mal neu auslöst
    /// (die Automatik tut das im Takt der Nachladezeit, der Spieler tippt).
    /// </summary>
    public MineResult TryFire(Universe u, Ship ship, Sector sector, Asteroid a, Vec2 shipPos)
    {
        if (!ship.Has(Capability.Mining)) return MineResult.NoLaser;   // Phase 5c: ohne Mining-Laser im Fit kein Abbau
        if (_cooldown > 0f) return MineResult.Cooldown;
        if (!IsMinable(sector, a) || !HasClass(u, a)) return MineResult.NotMinable;
        if (Vec2.Distance(shipPos, a.Position) - a.Radius > MiningRules.LaserRange) return MineResult.OutOfRange;
        // Laderaum voll? Ohne getrennte Räume zählt alles zusammen, sonst pro Form (die Brockensorte steht erst nach dem Schuss fest)
        bool cargoFull = ship.HasSeparatedHolds
            ? MathF.Max(ship.FreeVolumeForForm(u, GoodForm.Solid) - InFlightVolume(u, GoodForm.Solid),
                        ship.FreeVolumeForForm(u, GoodForm.Liquid) - InFlightVolume(u, GoodForm.Liquid)) < MiningRules.MinChunkUnits
            : ship.CargoCapacity - ship.UsedVolume(u) - InFlightVolume(u) < MiningRules.MinChunkUnits;
        if (cargoFull) return MineResult.CargoFull;

        _cooldown = MiningRules.ShotInterval;
        var shot = LaserShot.Roll(_rng, u.AsteroidClasses[a.ClassKey], a, ship.Stats.LaserChunkChanceMult, ship.Stats.LaserChunkSizeMult);
        if (shot.Hit)
        {
            var toShip = (shipPos - a.Position).Normalized();
            var side = new Vec2(-toShip.Y, toShip.X);
            Chunks.Add(new OreChunk
            {
                Good = shot.Good, Units = shot.Units,
                Position = a.Position + toShip * a.Radius,
                Velocity = toShip * _rng.Range(3f, 8f) + side * _rng.Range(-5f, 5f),
            });
            Belt.Remove(u, sector, a, shot.Units);   // Erz verlässt den Asteroiden (schrumpft, bei 0 weg)
        }
        Shot?.Invoke(a, shot);
        return MineResult.Fired;
    }

    /// <summary>Bewegt die Brocken; mit Traktorstrahl werden sie angezogen und bei Berührung (soweit Platz ist) in den Laderaum geladen.</summary>
    /// <returns>Eingesammelte Einheiten in diesem Schritt.</returns>
    public float Update(Universe u, Ship ship, Vec2 shipPos, bool tractor, float dt)
    {
        _cooldown = MathF.Max(0f, _cooldown - dt);

        float collected = 0f;
        for (int i = Chunks.Count - 1; i >= 0; i--)
        {
            var c = Chunks[i];
            c.Age += dt;
            if (c.Age > MiningRules.ChunkLifetime) { Chunks.RemoveAt(i); continue; }

            var to = shipPos - c.Position;
            float dist = to.Length;
            if (tractor && dist <= MiningRules.TractorRange)
            {
                c.Velocity += to * (1f / MathF.Max(dist, 0.001f)) * MiningRules.TractorAccel * dt;
                if (c.Velocity.Length > MiningRules.TractorSpeed) c.Velocity = c.Velocity.Normalized() * MiningRules.TractorSpeed;
            }
            else c.Velocity *= MathF.Exp(-0.9f * dt);   // ohne Strahl driften sie langsam aus
            c.Position += c.Velocity * dt;

            if (Vec2.Distance(c.Position, shipPos) > MiningRules.CollectRadius) continue;
            float take = MathF.Min(ship.FreeUnits(u, c.Good), c.Units);
            if (take < 0.001f) { c.Velocity = (c.Position - shipPos).Normalized() * 6f; continue; }   // Laderaum voll: abprallen

            ship.Cargo[c.Good] = ship.Cargo.GetValueOrDefault(c.Good) + take;
            c.Units -= take;
            collected += take;
            u.Events.Publish(new OreCollected(ship.Id, c.Good, take));
            if (c.Units < 0.001f) Chunks.RemoveAt(i);
            else c.Velocity = (c.Position - shipPos).Normalized() * 6f;   // Rest schwebt weiter
        }
        return collected;
    }
}
