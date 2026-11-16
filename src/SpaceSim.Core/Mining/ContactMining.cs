using SpaceSim.Core.Model;

namespace SpaceSim.Core.Mining;

/// <summary>Zustand des Kontaktabbaus (Anzeige der Werkzeugtaste).</summary>
public enum ContactState { NoTool, NoContact, Mining, HoldFull, Anchoring, Released }   // Runde 20: Released = von Hand gelöst, Bug noch am Fels (kein Neuanker)

/// <summary>
/// Phase 29 (Wunsch des Nutzers „Abbau durch frontalen Kontakt mit Asteroiden, mit Drehen der Bauteile“): die eine Regel des mechanischen Abbaus — Spieler
/// (<see cref="OreChunks.Contact"/>) und NPC-Schürfer (<c>MinerSystem</c>) benutzen sie gleich. Abbau läuft, solange die Werkzeugfront den Asteroiden berührt:
/// der Bug zeigt höchstens <see cref="FrontArcDeg"/> neben die Asteroidenmitte und der Bug ist höchstens <see cref="ContactRange"/> von der Oberfläche entfernt.
/// Erz geht direkt in den Laderaum (kein Brocken, kein Traktor, kein Rückstoß), stetig mit <see cref="OrePerSecond"/>, in Portionen zu einer Einheit; die
/// Erzsorte jeder Portion wird nach den Klassengewichten gewürfelt (ein Zug je Portion, höchstens <see cref="MaxPortionsPerStep"/> je Schritt). Die Rate
/// entspricht im Mittel einem Laser derselben Stufe (Erzfluss wie bisher), dafür ohne Verlust (keine verfallenden Brocken) — der Preis ist der Weg an den Fels.
/// </summary>
public static class ContactMining
{
    /// <summary>Höchster Abstand Bug–Asteroidenoberfläche für Kontakt.</summary>
    public const float ContactRange = 2.5f;
    /// <summary>Höchster Winkel zwischen Bug und Richtung zur Asteroidenmitte.</summary>
    public const float FrontArcDeg = 35f;
    /// <summary>Abstand des Bugs von der Schiffsmitte (Schiffe ohne Modell).</summary>
    public const float BowOffset = 2f;
    /// <summary>Höchstens so viele Portionen je Schritt (gegen Sprünge bei großen Schritten).</summary>
    public const int MaxPortionsPerStep = 4;

    /// <summary>
    /// Runde 43: Abstand Schiffsmitte → Werkzeugfront (Bug mit Werkzeug) je Größenklasse — Halblänge der Klasse
    /// (S 2,4 · M 3,6 · L 5,76 · XL 8,64; ohne Modell <see cref="BowOffset"/>). Die Ansicht misst die echte Spitze des Modells
    /// genauer; der Core rechnet mit der Klassen-Halblänge, damit große Schürfer (L) mit ihrem langen Werkzeug Kontakt halten.
    /// </summary>
    public static float ToolReach(Ship ship, Universe? u = null)
    {
        var size = u is not null ? Simulation.Docking.SizeOf(u, ship) : null;
        return size switch
        {
            Model.SlotSize.S => 2.4f,
            Model.SlotSize.M => 3.6f,
            Model.SlotSize.L => 5.76f,
            Model.SlotSize.XL => 8.64f,
            _ => BowOffset,
        };
    }

    /// <summary>
    /// Erz je Sekunde eines Schiffs an einem Asteroiden dieser Klasse — Laser (Brockenchance × mittlere Brockengröße ÷ Nachladezeit) oder Kontaktwerkzeug
    /// (Rate × Klassenreichtum: Brockenchance der Klasse ÷ 0,5); das bessere zählt.
    /// </summary>
    public static float OrePerSecond(ShipStats s, AsteroidClass cls)
    {
        float laser = s.HasLaser ? MathF.Min(1f, cls.ChunkChance * s.LaserChunkChanceMult) * MiningRules.MeanChunkUnits * s.LaserChunkSizeMult / MiningRules.ShotInterval : 0f;
        float contact = ContactRate(s, cls);
        return MathF.Max(laser, contact);
    }

    /// <summary>Erz je Sekunde des Kontaktwerkzeugs an dieser Klasse (0 = keins).</summary>
    public static float ContactRate(ShipStats s, AsteroidClass cls) => s.ContactMining * cls.ChunkChance / MiningRules.ChunkChance;

    /// <summary>Berührt die Werkzeugfront den Asteroiden? <paramref name="dir"/> = Blickrichtung (Einheitsvektor).</summary>
    /// <param name="bowOffset">Abstand Schiffsmitte → Werkzeugfront; Standard <see cref="BowOffset"/>, mit Modell <see cref="ToolReach"/>.</param>
    public static bool InContact(Vec2 pos, Vec2 dir, Asteroid rock, float bowOffset = BowOffset)
    {
        var to = rock.Position - pos;
        float len = to.Length;
        if (len < 1e-4f) return true;
        float cos = (to.X * dir.X + to.Y * dir.Y) / len;
        if (cos < MathF.Cos(FrontArcDeg * MathF.PI / 180f)) return false;
        var bow = pos + dir * bowOffset;
        return Vec2.Distance(bow, rock.Position) - rock.Radius <= ContactRange;
    }

    /// <summary>Drehzahl des Werkzeugs 0..1 für die Ansicht: im Kontakt voll, sonst Leerlauf (0,25), ohne Werkzeug 0.</summary>
    public static float Spin(ShipStats s, ContactState state) => s.ContactMining <= 0f ? 0f : state == ContactState.Mining ? 1f : state == ContactState.Anchoring ? 0.6f : 0.25f;   // Runde 18: anlaufen beim Verankern

    /// <summary>
    /// Ein Abbauschritt: Fortschritt <paramref name="progress"/> wächst um Rate × Zeit; je volle Einheit eine Portion (Erzsorte nach Klassengewichten über
    /// <paramref name="rng"/>), höchstens so viel, wie der Asteroid hat und der Laderaum für die Sorte fasst. Liefert die Portionen (Ware, Einheiten).
    /// </summary>
    public static List<(GoodId Good, float Units)> Step(Universe u, DeterministicRng rng, Ship ship, Sector sector, Asteroid rock, AsteroidClass cls, ref float progress, float seconds)
    {
        var got = new List<(GoodId, float)>();
        // Phase 32: mit Ankern gleicht AnchorRateFactor die Verankerungszeit aus (Erzfluss wie vorher)
        float factor = u.Rules.MiningAnchors ? u.Rules.AnchorRateFactor : 1f;
        progress += ContactRate(ship.Stats, cls) * factor * MathF.Max(0f, seconds);
        int n = 0;
        while (progress >= 1f && rock.Ore > 0f && n < MaxPortionsPerStep)
        {
            var good = cls.RollGood(rng);
            float units = MathF.Min(1f, MathF.Min(rock.Ore, ship.FreeUnits(u, good)));
            progress -= 1f;
            n++;
            if (units <= 0f) continue;
            Belt.Remove(u, sector, rock, units);
            ship.Cargo[good] = ship.Cargo.GetValueOrDefault(good) + units;
            got.Add((good, units));
        }
        if (n >= MaxPortionsPerStep) progress = MathF.Min(progress, 1f);
        return got;
    }

    /// <summary>Ist der feste Laderaum voll (Erz ist fest)?</summary>
    public static bool HoldFull(Universe u, Ship ship) => ship.FreeVolumeForForm(u, GoodForm.Solid) < MiningRules.MinChunkUnits;
}
