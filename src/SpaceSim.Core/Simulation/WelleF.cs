using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Welle F (1): was eine eigene Wirtschaftsstation je Tick an die Spielerkasse abführt — die Vermögensabgabe auf ihre Kasse über dem Betriebskapital
/// (<see cref="UpkeepSystem"/>). Nur echter Absatz füllt diese Kasse (das Grundeinkommen deckt höchstens den Unterhalt), eine Leerlaufstation führt nichts ab.
/// </summary>
public static class PlayerStationSales
{
    public static float IncomePerTick(Universe u, Station st, StationType t)
    {
        if (st.Owner != u.PlayerActor || st.IsBuildSite || t.Government != GovernmentKind.None) return 0f;
        float above = st.Credits - WorkingCapital.Of(u, st);
        return above > 0f ? above * u.Rules.StationWealthTax * Math.Clamp(u.Rules.TaxToTreasuryShare, 0f, 1f) : 0f;
    }
}

/// <summary>Welle F (3): eine Kriegsseite ist seit <paramref name="Since"/> ununterbrochen unter ihrer Rücklage (Diagnose, nicht gespeichert).</summary>
public sealed record WarBrokeNoticed(FactionId Side, FactionId Enemy, float Credits, float Reserve) : SimEvent;

/// <summary>
/// Welle F (3): Frieden wegen leerer Kasse erst nach anhaltender Pleite. Gemessen (Spielerbot, Test 8): die KI-Kasse liegt im Frieden knapp über der
/// Rücklage (jede freiwillige Ausgabe nimmt nur das Verfügbare), ein Pflichtposten drückt sie kurz darunter — der Krieg mit dem Spieler endete nach 16–1 900
/// Ticks „broke“, mitten in Belagerungen. Jetzt: eine Seite gilt erst als pleite, wenn ihre Kasse <see cref="EconomyRules.WarBrokeTicks"/> ununterbrochen
/// unter der Rücklage liegt (<see cref="War.BrokeSinceA"/>/<see cref="War.BrokeSinceB"/>, ein Tick darüber setzt zurück); solange die zahlungsfähige Seite
/// eine Station oder ein Tor der pleiten Seite belagert, wartet der Frieden — höchstens bis <see cref="EconomyRules.WarBrokeMaxTicks"/>. Pflichtausgaben
/// im Krieg kommen ohnehin nur aus dem Verfügbaren (Kriegsschiffe werden unter der Rücklage abgebaut), die Handlungsfähigkeit bleibt also gewahrt.
/// Gilt gleich für den Spieler (Kasse unter 0). Kein Zufall.
/// </summary>
public static class WarBroke
{
    /// <summary>Schreibt die Pleite-Zeitpunkte fort und sagt, ob jetzt Frieden „broke“ fällig ist.</summary>
    public static bool Due(Universe u, War w, bool brokeA, bool brokeB)
    {
        w.BrokeSinceA = Track(u, w.BrokeSinceA, brokeA, w.A, w.B);
        w.BrokeSinceB = Track(u, w.BrokeSinceB, brokeB, w.B, w.A);
        return Ready(u, w, w.A, w.B, w.BrokeSinceA) || Ready(u, w, w.B, w.A, w.BrokeSinceB);
    }

    static long Track(Universe u, long since, bool broke, FactionId side, FactionId enemy)
    {
        if (!broke) return 0;
        if (since != 0) return since;
        if (u.Actors.TryGetValue(side, out var a))
            u.Events.Publish(new WarBrokeNoticed(side, enemy, a.Treasury.Credits, FactionViability.Reserve(u, side)));
        return Math.Max(1, u.Tick);
    }

    static bool Ready(Universe u, War w, FactionId side, FactionId enemy, long since)
    {
        if (since == 0) return false;
        long t = u.Tick - since;
        if (t < u.Rules.WarBrokeTicks) return false;
        if (u.Rules.WarBrokeMaxTicks > 0 && t >= u.Rules.WarBrokeMaxTicks) return true;
        return !SiegeAgainst(u, side, enemy);
    }

    /// <summary>Belagert die Seite <paramref name="attacker"/> gerade eine Station oder ein Tor von <paramref name="target"/> (aktiver Angreifer)?</summary>
    public static bool SiegeAgainst(Universe u, FactionId target, FactionId attacker)
    {
        foreach (var e in u.Fights.Encounters)
        {
            if (e.Cause != EncounterCause.Siege || e.Done) continue;
            if (Combat.SiegeTargets.Of(u, e) is not { } t || u.FactionOf(t.Owner) != target) continue;
            foreach (var p in e.Participants)
                if (p.Side == 0 && p.Active && u.FactionOf(p.Owner) == attacker) return true;
        }
        return false;
    }

    /// <summary>Anzeige: „Kasse seit 3:20 unter der Rücklage — Frieden in 6:40“ (leer, wenn keine Seite pleite ist).</summary>
    public static string Text(Universe u, War w, FactionId side)
    {
        long since = w.A == side ? w.BrokeSinceA : w.B == side ? w.BrokeSinceB : 0;
        if (since == 0 || u.Rules.WarBrokeTicks <= 0) return "";
        long left = Math.Max(0, u.Rules.WarBrokeTicks - (u.Tick - since));
        return left > 0 ? $"Kasse unter der Rücklage — Frieden „leere Kasse“ in {Clock(left)}" : "Kasse leer — Frieden fällig, sobald keine Belagerung mehr läuft";
    }

    static string Clock(long ticks) { long s = ticks / SimClock.TicksPerSecond; return $"{s / 60}:{s % 60:00}"; }
}

/// <summary>Welle F (2): ein Spielerschiff kommt nicht weiter (Notkauf unbezahlbar oder Notfall ungelöst) und wartet; Meldung für die Ansicht.</summary>
public sealed record FleetShipStranded(ShipId Ship, string Reason, float Units, float Cost) : SimEvent;

/// <summary>
/// Welle F (2): Treibstoffvorrang der Spielerflotte. Gemessen (Probe fuelwhy/fueltrace, Test 8, Tick 700 000, Kasse ≈ 0): ein Frachter mit kritischer Hülle
/// lag an der Reparaturstation (Notfall-Ebene), konnte nicht zahlen, beendete den Notfall sofort und startete den Handelsbefehl neu — der Händler kaufte
/// 1–2 Einheiten Notbenzin zum doppelten Preis, flog los und wurde im nächsten Tick vom Notfall wieder angehalten. Dazu reichte ein Notkauf nur bis zur
/// Luftlinie (die geplante Strecke ist länger) und das Tanken an der Raffinerie nahm nur 30 % des Bargelds. Regeln: Pflichtmaß beim Tanken auf Schuld
/// (<see cref="EconomyRules.PlayerFuelDebtLimit"/>), Notkauf ganz oder gar nicht mit Aufschlag auf die geplante Strecke, und ein ungelöster Notfall wartet
/// (<see cref="EconomyRules.PlayerStrandedWaitTicks"/>). NPCs unverändert.
/// </summary>
public static class FuelPriority
{
    /// <summary>Aufschlag auf den Treibstoffbedarf bis zur nächsten Tankstelle (Umwege, Bremsen).</summary>
    public const float Margin = 1.25f;

    public static bool Applies(Universe u, Ship ship) =>
        u.Rules.PlayerFuelPriority && u.Rules.FuelActive && u.PlayerActor.Value != 0 && ship.Owner == u.PlayerActor;

    public static float WithMargin(float units) => units <= 0f ? 0f : units * Margin + 2f;

    /// <summary>Was ein Spielerschiff an Station <paramref name="st"/> mindestens zutanken muss: bis zur nächsten anderen Tankstelle (mit Aufschlag) bzw. zur Reserve.</summary>
    public static float MinimumAt(Universe u, Ship ship, Station st, GoodId good)
    {
        float target = u.Rules.PlayerTankReserve ? Refuel.MinFuelReserveFraction * ship.Stats.FuelCapacity : 0f;
        if (Refuel.NearestFuelStation(u, ship, good, float.PositiveInfinity, exclude: st.Id) is { } next)
            target = MathF.Max(target, WithMargin(Refuel.Consumption(ship, Travel.PlannedDistance(u, ship.Sector, ship.Position, next.Sector, next.DockPoint(ship.Id), ship), good)));
        return MathF.Max(0f, MathF.Min(target, ship.Stats.FuelCapacity) - ship.FuelAmount(good));
    }

    /// <summary>Notkauf nicht bezahlbar: Schiff wartet (Denkpause), Meldung; liefert <see cref="Refuel.TripPlan.Stuck"/>.</summary>
    public static Refuel.TripPlan Strand(Universe u, Ship ship, GoodId good, float units)
    {
        float cost = MathF.Ceiling(units) * u.Goods[good].BasePrice * Refuel.EmergencyPriceFactor;
        ship.NextThinkTick = u.Tick + Math.Max(1, u.Rules.PlayerStrandedWaitTicks);
        u.Events.Publish(new FleetShipStranded(ship.Id, "fuel", units, cost));
        return Refuel.TripPlan.Stuck;
    }

    /// <summary>Wartet ein Spielerschiff im ungelösten Notfall (Handel/Schürfen ruhen, bis der Notfall gelöst ist)?</summary>
    public static bool WaitsInEmergency(Universe u, Ship ship) => u.Rules.PlayerFuelPriority && ship.Emergency && ship.Owner == u.PlayerActor;

    /// <summary>Anzeige der Wartemeldung.</summary>
    public static string Text(Universe u, FleetShipStranded e) => e.Reason switch
    {
        "fuel" => $"Tank leer: Notkauf {e.Units:0} E für {e.Cost:0} Cr nicht bezahlbar (Schuldgrenze {u.Rules.PlayerFuelDebtLimit:0} Cr) – wartet",
        _ => $"Notfall ({e.Reason}) nicht lösbar – Kasse reicht nicht, Schiff wartet an der Station",
    };
}
