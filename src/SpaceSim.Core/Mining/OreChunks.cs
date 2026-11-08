using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Mining;

public sealed class OreChunk
{
    public Vec2 Position, Velocity;
    public GoodId Good;
    public float Units;
    public float Age;
    public ResourceField Field = null!;
}

public enum MineResult { Ok, OutOfRange, NotMinable, Depleted, CargoFull }

/// <summary>
/// Echtzeit-Mikrosimulation des Spielerabbaus im aktuellen Sektor: Laser löst Erzbrocken aus dem Asteroiden,
/// der Traktorstrahl zieht sie ins Schiff. Flüchtig (nicht gespeichert); erst beim Einfangen wird der Bestand
/// des Rohstofffelds verringert und die Fracht erhöht, damit unabgeholte Brocken nichts verbrauchen.
/// Engine-unabhängig und testbar; die Darstellung zeigt nur Brocken und Strahlen.
/// </summary>
public sealed class OreChunks
{
    public List<OreChunk> Chunks { get; } = new();
    readonly Dictionary<int, float> _progress = new();
    readonly DeterministicRng _rng = new(0xC0FFEE);

    /// <summary>Beim Sektorwechsel/Laden: schwebende Brocken verschwinden.</summary>
    public void Clear() { Chunks.Clear(); _progress.Clear(); }

    float Reserved(ResourceField f) => Chunks.Where(c => c.Field == f).Sum(c => c.Units);
    float ReservedGood(GoodId g) => Chunks.Where(c => c.Good == g).Sum(c => c.Units);

    public bool IsMinable(Sector sector, int asteroid)
    {
        if (asteroid < 0 || asteroid >= sector.Asteroids.Count) return false;
        var f = sector.FieldFor(sector.Asteroids[asteroid].Position);
        return f != null && f.Amount - Reserved(f) >= MiningRules.ChunkUnits;
    }

    /// <summary>Nächster abbaubarer Asteroid (Abstand Schiff–Oberfläche), optional nur innerhalb von <paramref name="maxEdge"/>; -1 wenn keiner.</summary>
    public int Nearest(Sector sector, Vec2 shipPos, float maxEdge = float.MaxValue)
    {
        int best = -1;
        float bestEdge = maxEdge;
        for (int i = 0; i < sector.Asteroids.Count; i++)
        {
            var a = sector.Asteroids[i];
            float edge = Vec2.Distance(shipPos, a.Position) - a.Radius;
            if (edge <= bestEdge && IsMinable(sector, i)) { best = i; bestEdge = edge; }
        }
        return best;
    }

    /// <summary>Beschießt einen Asteroiden für <paramref name="dt"/> Sekunden; erzeugt bei genug Fortschritt Erzbrocken.</summary>
    public MineResult Mine(Universe u, Ship ship, Sector sector, int asteroid, Vec2 shipPos, float dt)
    {
        if (asteroid < 0 || asteroid >= sector.Asteroids.Count) return MineResult.NotMinable;
        var a = sector.Asteroids[asteroid];
        var field = sector.FieldFor(a.Position);
        if (field is null) return MineResult.NotMinable;
        if (Vec2.Distance(shipPos, a.Position) - a.Radius > MiningRules.LaserRange) return MineResult.OutOfRange;
        if (field.Amount - Reserved(field) < MiningRules.ChunkUnits) return MineResult.Depleted;
        if (ship.FreeUnits(u, field.Good) - ReservedGood(field.Good) < MiningRules.ChunkUnits) return MineResult.CargoFull;

        float p = _progress.GetValueOrDefault(asteroid) + MiningRules.OrePerSecond * dt;
        while (p >= MiningRules.ChunkUnits
               && field.Amount - Reserved(field) >= MiningRules.ChunkUnits
               && ship.FreeUnits(u, field.Good) - ReservedGood(field.Good) >= MiningRules.ChunkUnits)
        {
            p -= MiningRules.ChunkUnits;
            var toShip = (shipPos - a.Position).Normalized();
            var side = new Vec2(-toShip.Y, toShip.X);
            Chunks.Add(new OreChunk
            {
                Good = field.Good, Units = MiningRules.ChunkUnits, Field = field,
                Position = a.Position + toShip * a.Radius,
                Velocity = toShip * _rng.Range(3f, 8f) + side * _rng.Range(-5f, 5f),
            });
        }
        _progress[asteroid] = Math.Min(p, MiningRules.ChunkUnits);
        return MineResult.Ok;
    }

    /// <summary>Bewegt die Brocken; mit Traktorstrahl werden sie angezogen und bei Berührung in den Laderaum geladen.</summary>
    /// <returns>Eingesammelte Einheiten in diesem Schritt.</returns>
    public float Update(Universe u, Ship ship, Vec2 shipPos, bool tractor, float dt)
    {
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
                var dir = to * (1f / MathF.Max(dist, 0.001f));
                c.Velocity += dir * MiningRules.TractorAccel * dt;
                if (c.Velocity.Length > MiningRules.TractorSpeed) c.Velocity = c.Velocity.Normalized() * MiningRules.TractorSpeed;
            }
            else c.Velocity *= MathF.Exp(-0.9f * dt);   // ohne Strahl driften sie langsam aus
            c.Position += c.Velocity * dt;

            if (Vec2.Distance(c.Position, shipPos) > MiningRules.CollectRadius) continue;
            if (ship.FreeUnits(u, c.Good) < c.Units) { c.Velocity = (c.Position - shipPos).Normalized() * 6f; continue; }   // Laderaum voll: abprallen

            ship.Cargo[c.Good] = ship.Cargo.GetValueOrDefault(c.Good) + c.Units;
            c.Field.Amount = MathF.Max(0f, c.Field.Amount - c.Units);
            collected += c.Units;
            u.Events.Publish(new OreCollected(ship.Id, c.Good, c.Units));
            Chunks.RemoveAt(i);
        }
        return collected;
    }
}
