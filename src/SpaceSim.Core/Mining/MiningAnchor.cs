using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Mining;

/// <summary>Anzeigezustand der Verankerung.</summary>
public enum AnchorState { None, Flying, Held }

/// <summary>
/// Phase 32 (Runde 17, Entscheidung des Nutzers „mit Anker mit Trossen / Harpune“): Kontaktabbau im Leeren wird glaubhaft, weil sich der Schürfer erst mit
/// Harpunen am Fels festmacht. Eine Regel für Spieler und NPC-Schürfer, rein und ohne Zufall:
/// <list type="bullet">
/// <item>Bei Bugkontakt (<see cref="ContactMining.InContact"/>) fliegen die Harpunen (<see cref="Attach"/>, Ereignis <see cref="AnchorFired"/>); nach
/// <see cref="EconomyRules.AnchorTicks"/> halten sie (<see cref="AnchorHeld"/>). Abgebaut wird nur mit haltendem Anker (<see cref="Holds"/>).</item>
/// <item>Ein verankertes Schiff driftet nicht: es liegt an der Oberfläche (<see cref="HoldPosition"/>); der Fels behält beim Abbau seinen Radius (Runde 43).</item>
/// <item>Abreißen (<see cref="Release"/>): eigener Schub/Wenden (<see cref="BreaksFromSteering"/>) oder Taste „Anker lösen“, harter Stoß, Hüllentreffer
/// (<see cref="OnHullHit"/>), Gefechtsbeginn (<see cref="OnCombat"/>, dazu Trossen kappen: <see cref="EconomyRules.AnchorCastOffTicks"/>), Fels erschöpft oder
/// gespalten, Kontakt verloren, Laderaum voll, Abflug.</item>
/// </list>
/// Zustand am Schiff (<see cref="Ship.AnchorRock"/>, <see cref="Ship.AnchorProgress"/>), gespeichert und gehasht nur wenn gesetzt. Schalter
/// <see cref="EconomyRules.MiningAnchors"/>: aus = Abbau bei Kontakt wie bis Phase 31.
/// </summary>
public static class MiningAnchor
{
    /// <summary>Anker aktiv (Schalter)?</summary>
    public static bool Active(Universe u) => u.Rules.MiningAnchors;

    /// <summary>Zustand der Verankerung eines Schiffs.</summary>
    public static AnchorState State(Universe u, Ship ship) =>
        ship.AnchorRock == 0 ? AnchorState.None : ship.AnchorProgress >= u.Rules.AnchorTicks ? AnchorState.Held : AnchorState.Flying;

    /// <summary>Darf an diesem Fels abgebaut werden? Ohne Anker-Regel immer; sonst nur mit haltendem Anker an genau diesem Fels.</summary>
    public static bool Holds(Universe u, Ship ship, int rock) => !Active(u) || (ship.AnchorRock == rock && ship.AnchorProgress >= u.Rules.AnchorTicks);

    /// <summary>
    /// Harpunen auf den Fels schießen (oder weiter verankern): hängt das Schiff an einem anderen Fels, reißt das zuerst („lost_contact“). Liefert true, wenn neu geschossen wurde.
    /// </summary>
    public static bool Attach(Universe u, Ship ship, SectorId sector, Asteroid rock)
    {
        if (ship.AnchorRock == rock.Id) return false;
        if (ship.AnchorRock != 0) Release(u, ship, sector, "lost_contact");
        ship.AnchorRock = rock.Id;
        ship.AnchorProgress = 0f;
        u.Events.Publish(new AnchorFired(sector, ship.Id, rock.Id, rock.Position, rock.Radius));
        return true;
    }

    /// <summary>Verankerung voranbringen (<paramref name="ticks"/> Spielzeit in Ticks); liefert true, sobald der Anker hält.</summary>
    public static bool Advance(Universe u, Ship ship, SectorId sector, float ticks)
    {
        if (ship.AnchorRock == 0) return false;
        int need = u.Rules.AnchorTicks;
        if (ship.AnchorProgress >= need) return true;
        ship.AnchorProgress = MathF.Min(need, ship.AnchorProgress + MathF.Max(0f, ticks));
        if (ship.AnchorProgress >= need) { u.Events.Publish(new AnchorHeld(sector, ship.Id, ship.AnchorRock)); return true; }
        return false;
    }

    /// <summary>Anker lösen bzw. reißen lassen (nichts, wenn keiner gesetzt ist).</summary>
    public static void Release(Universe u, Ship ship, SectorId sector, string reason)
    {
        if (ship.AnchorRock == 0) return;
        int rock = ship.AnchorRock;
        ship.AnchorRock = 0;
        ship.AnchorProgress = 0f;
        u.Events.Publish(new AnchorReleased(sector, ship.Id, rock, reason));
    }

    /// <summary>Reißt Steuerung die Verankerung? Regler/Rückwärts über <see cref="EconomyRules.AnchorBreakThrottle"/> oder jede Wendeeingabe.</summary>
    public static bool BreaksFromSteering(EconomyRules r, float throttle, float turn) => SteeringBreak(r, throttle, turn) is not null;

    /// <summary>Runde 18: Grund eines Abrisses durch Steuerung — „turn“ (Wendetaste) vor „thrust“ (Regler/Rückwärts über der Schwelle); null = hält.</summary>
    public static string? SteeringBreak(EconomyRules r, float throttle, float turn) =>
        turn != 0f ? "turn" : MathF.Abs(throttle) > r.AnchorBreakThrottle ? "thrust" : null;

    /// <summary>Hüllentreffer: die Trossen reißen.</summary>
    public static void OnHullHit(Universe u, Ship ship) { if (ship.AnchorRock != 0) Release(u, ship, ship.Sector, "hit"); }

    /// <summary>Gefechtsbeginn: Anker lösen; war das Schiff verankert, liefert es den Tick, bis zu dem es die Trossen kappt (0 = sofort frei).</summary>
    public static long OnCombat(Universe u, Ship ship)
    {
        if (ship.AnchorRock == 0) return 0;
        Release(u, ship, ship.Sector, "combat");
        return u.Tick + Math.Max(0, u.Rules.AnchorCastOffTicks);
    }

    /// <summary>Gespaltener Fels: alle daran verankerten Schiffe des Sektors lösen sich („split“).</summary>
    public static void OnSplit(Universe u, SectorId sector, int rock)
    {
        foreach (var s in u.ShipsById())
            if (s.AnchorRock == rock && s.Sector == sector) Release(u, s, sector, "split");
    }

    /// <summary>
    /// Lage eines verankerten Schiffs: auf der Linie Felsmitte → Schiff, Schiffsmitte <paramref name="standoff"/> über der Oberfläche (konstanter Radius,
    /// Runde 43) — die Trossen halten den Bug im Kontakt. <paramref name="standoff"/> = Werkzeugreichweite (<see cref="ContactMining.ToolReach"/>) im Core;
    /// die Ansicht setzt stattdessen die Bohrerspitze des Modells auf die Oberfläche.
    /// </summary>
    public static Vec2 HoldPosition(Vec2 shipPos, Asteroid rock, float standoff)
    {
        var d = shipPos - rock.Position;
        var n = d.Length < 1e-4f ? new Vec2(0f, 1f) : d.Normalized();
        return rock.Position + n * (rock.Radius + standoff);
    }

    /// <summary>Anzeige für Werkzeugtaste/Zielkarte: „verankert“, „Anker fliegt“, sonst leer.</summary>
    public static string Text(Universe u, Ship ship) => State(u, ship) switch
    {
        AnchorState.Held => "verankert",
        AnchorState.Flying => "Anker fliegt",
        _ => "",
    };

    /// <summary>Kurzform für die Werkzeugtaste („Riss: Treffer“).</summary>
    public static string ShortReason(string reason) => reason switch
    {
        "released" => "Anker gelöst",
        "thrust" => "Riss: Schub",
        "turn" => "Riss: Wenden",
        "impact" => "Riss: Stoß",
        "hit" => "Riss: Treffer",
        "combat" => "gekappt: Gefecht",
        "depleted" => "Fels leer",
        "split" => "Fels gespalten",
        "lost_contact" => "Riss: Kontakt",
        "hold_full" => "Laderaum voll",
        _ => "Anker gelöst",
    };

    /// <summary>Text zum Grund eines Abrisses („Anker gerissen: Treffer“).</summary>
    public static string ReasonText(string reason) => reason switch
    {
        "released" => "Anker gelöst",
        "thrust" => "Anker gerissen: Schub",
        "turn" => "Anker gerissen: Wenden",
        "impact" => "Anker gerissen: Stoß",
        "hit" => "Anker gerissen: Treffer",
        "combat" => "Anker gekappt: Gefecht",
        "depleted" => "Fels erschöpft – Anker gelöst",
        "split" => "Fels gespalten – Anker gerissen",
        "lost_contact" => "Anker gerissen: kein Kontakt",
        "hold_full" => "Laderaum voll – Anker gelöst",
        _ => "Anker gelöst",
    };
}
