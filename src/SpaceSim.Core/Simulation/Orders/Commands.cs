using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Befehle geben und beenden (Phase 12, FLOTTEN.md Abschnitt 4): Prüfung (aktives Schiff, Fähigkeit, Ziel, Befehlskapazität), Ablösen des alten
/// Befehls, Start des neuen Ausführers, Ereignisse. Spielerschiffe und KI teilen dieselbe Maschine; die Oberfläche ruft nur hier auf.
/// </summary>
public static class Commands
{
    /// <summary>Kann dieses Schiff den Befehl annehmen? null = ja, sonst der Grund (ohne Kapazität, siehe <see cref="CapacityIssue"/>).</summary>
    public static string? Check(Universe u, Ship ship, ShipCommand command, OrderExecutors? executors = null)
    {
        executors ??= OrderExecutors.Default;
        if (ship.Id == u.PlayerShip) return "Das aktive Schiff steuerst du selbst.";
        if (!executors.TryGet(command.Kind, out var ex)) return $"Unbekannter Befehl '{command.Kind}'.";
        if (ex.Needs != Capability.None && !ship.Has(ex.Needs)) return $"Dafür fehlt die Fähigkeit ({CapabilityName(ex.Needs)}).";
        return ex.Check(u, ship, command);
    }

    /// <summary>Befehlskapazität eines Akteurs nach Größenklasse: (Schiffe mit Einzelbefehl, Verbände).</summary>
    public static (int Ships, int Squadrons) Capacity(Universe u, ActorId actor)
    {
        // Runde 24: der Spieler nach seiner Befehlsstufe (eigene Schiffe zählen wenig); Fraktionen und Piraten (Streifen, Rudel) nach der Größenklasse wie bisher
        var cls = actor == u.PlayerActor ? CapacityClass(u, actor) : ActorRules.ClassOf(u, ActorRules.Index(u), actor);
        int i = (int)cls;
        int ships = u.Rules.CommandShips.Count == 0 ? int.MaxValue : u.Rules.CommandShips[Math.Min(i, u.Rules.CommandShips.Count - 1)];
        int squads = u.Rules.CommandSquadrons.Count == 0 ? int.MaxValue : u.Rules.CommandSquadrons[Math.Min(i, u.Rules.CommandSquadrons.Count - 1)];
        return (ships, squads);
    }

    /// <summary>
    /// Runde 24: <b>Befehlsstufe</b> — die Größenklasse für die Befehlskapazität, in der die eigenen Schiffe nur mit <see cref="EconomyRules.CommandShipWeight"/>
    /// zählen (Stationen, Sektoren und Geld wie in der Größenklasse). Vorher hob jedes gekaufte Schiff die Klasse schneller, als es Befehle brauchte.
    /// </summary>
    public static ActorSizeClass CapacityClass(Universe u, ActorId actor)
    {
        var h = ActorRules.Index(u).GetValueOrDefault(actor);
        var r = u.Rules;
        float size = h.Ships * r.CommandShipWeight + h.Stations * r.ActorStationWeight + h.Sectors * r.ActorSectorWeight + MathF.Max(0f, h.Money) / MathF.Max(1f, r.ActorMoneyScale);
        // Ausbau Punkt 9: ein eigener Firmensitz hebt die Befehlsstufe um FirmSeatCommandBonus Klassen (höchstens Staat)
        int cls = Math.Min((int)ActorRules.ClassOf(r, size) + PlayerSeatsOffices.CommandBonus(u, actor), (int)ActorSizeClass.State);
        return (ActorSizeClass)cls;
    }

    /// <summary>Runde 24: „1 Verband“/„3 Verbände“, „1 Schiff“/„4 Schiffe“.</summary>
    public static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    /// <summary>Schiffe eines Akteurs mit Einzelbefehl (außerhalb von Verbänden; Verbände zählen gegen ihre eigene Grenze).</summary>
    public static int Commanded(Universe u, ActorId actor) =>
        u.Ships.Values.Count(s => s.Owner == actor && s.Command is not null && s.Squadron.Value == 0);

    /// <summary>
    /// Runde 25 (Gerätetest „Befehle 5/4“ nach Auflösen eines Verbands): Einzelbefehle über der Kapazität. Regel: laufende Befehle bleiben (Auflösen oder
    /// Verlassen eines Verbands nimmt keinem Schiff seinen Arbeitsbefehl, eine kleinere Befehlsstufe auch nicht), ändern darf man sie; neue Einzelbefehle sind
    /// gesperrt, bis die Zahl unter die Kapazität fällt (<see cref="CapacityIssue"/>). 0 = nicht darüber.
    /// </summary>
    public static int OverCapacity(Universe u, ActorId actor) => Math.Max(0, Commanded(u, actor) - Capacity(u, actor).Ships);

    /// <summary>Runde 25: Hinweis, wenn mehr Einzelbefehle laufen als die Kapazität erlaubt (siehe <see cref="OverCapacity"/>); "" = nicht darüber.</summary>
    public static string OverCapacityText(Universe u, ActorId actor)
    {
        int over = OverCapacity(u, actor);
        if (over == 0) return "";
        return $"Befehle {Commanded(u, actor)}/{Capacity(u, actor).Ships}: laufende Befehle bleiben, neue Einzelbefehle gesperrt, bis {Count(over + 1, "Befehl endet", "Befehle enden")}.";
    }

    /// <summary>Würde ein neuer Einzelbefehl die Befehlskapazität des Besitzers überschreiten? null = nein, sonst der Grund. Gilt für Spielerschiffe.</summary>
    public static string? CapacityIssue(Universe u, Ship ship)
    {
        if (!ship.IsPlayer || ship.Command is not null || ship.Squadron.Value != 0) return null;
        var (cap, _) = Capacity(u, ship.Owner);
        return Commanded(u, ship.Owner) >= cap ? $"Befehlskapazität erreicht ({Count(cap, "Schiff", "Schiffe")} mit Einzelbefehl) – Schiffe in einen Verband aufnehmen oder mehr Besitz (Stationen, Geld) hebt die Befehlsstufe." : null;
    }

    /// <summary>
    /// Gibt einem Schiff einen Befehl: prüft ihn (und ohne <paramref name="ignoreCapacity"/> die Befehlskapazität), beendet den alten Ausführer,
    /// startet den neuen und veröffentlicht <see cref="CommandGiven"/>. false (mit Grund) = nichts geändert.
    /// </summary>
    public static bool Give(Universe u, Ship ship, ShipCommand command, out string message, bool ignoreCapacity = false, OrderExecutors? executors = null)
    {
        executors ??= OrderExecutors.Default;
        var issue = Check(u, ship, command, executors) ?? (ignoreCapacity ? null : CapacityIssue(u, ship));
        if (issue is not null) { message = issue; return false; }
        if (ship.Command is { } old && executors.TryGet(old.Kind, out var oldEx))
        {
            if (!ship.Emergency) oldEx.Stop(u, ship, old);
            u.Events.Publish(new CommandEnded(ship.Id, old.Kind, "cancelled", "ersetzt"));
        }
        ship.Emergency = false;
        var ex = executors.TryGet(command.Kind, out var e) ? e : throw new InvalidOperationException();
        ship.Command = command;
        ship.CommandSince = u.Tick;
        ship.CommandStep = 0;
        ex.Start(u, ship, command);
        message = ex.Describe(u, ship, command);
        u.Events.Publish(new CommandGiven(ship.Id, command.Kind, message));
        return true;
    }

    /// <summary>Beendet den Befehl eines Schiffs (Ausführer gibt frei, eine laufende Reise endet an Ort und Stelle); danach gilt die Automatik.</summary>
    public static void Cancel(Universe u, Ship ship, string reason = "abgebrochen", OrderExecutors? executors = null)
    {
        executors ??= OrderExecutors.Default;
        if (ship.Command is not { } cmd) return;
        if (executors.TryGet(cmd.Kind, out var ex) && !ship.Emergency) ex.Stop(u, ship, cmd);
        else OrderMove.Release(u, ship);
        ship.Command = null;
        ship.Emergency = false;
        ship.CommandStep = 0;
        if (ship.IsPlayer && ship.Role != ShipRole.Player) ship.Role = ShipRole.Player;
        u.Events.Publish(new CommandEnded(ship.Id, cmd.Kind, "cancelled", reason));
    }

    /// <summary>Anzeige des Befehls (oder der Automatik) eines Schiffs.</summary>
    public static string Describe(Universe u, Ship ship, OrderExecutors? executors = null)
    {
        executors ??= OrderExecutors.Default;
        if (ship.Command is not { } cmd) return ship.IsPlayer ? (ship.Id == u.PlayerShip ? "von dir gesteuert" : "kein Befehl — hält die Position") : "Automatik";
        string text = executors.TryGet(cmd.Kind, out var ex) ? ex.Describe(u, ship, cmd) : cmd.Kind;
        return ship.Emergency ? "NOTFALL: zur Reparatur — danach " + text : text;
    }

    public static string CapabilityName(Capability c) => c switch
    {
        Capability.Cargo => "Frachtraum",
        Capability.Mining => "Mining-Laser",
        Capability.Combat => "Waffen",
        Capability.Construction => "Bauausleger",
        Capability.Salvage => "Bergung",
        _ => c.ToString(),
    };
}

/// <summary>
/// Grenzen der Automatikaufträge (Phase 12, FLOTTEN.md Abschnitt 5 „Richtung und Grenzen“): reine Regeln, die <see cref="TraderSystem"/> und
/// <see cref="MinerSystem"/> für Schiffe mit <see cref="TradeCommand"/>/<see cref="MineCommand"/> anwenden. KI-Schiffe ohne Befehl haben keine Grenzen.
/// </summary>
public static class AutoLimitRules
{
    /// <summary>Grenzen des laufenden Automatikauftrags (null = keine).</summary>
    public static AutoLimits? Of(Ship ship) => ship.Command switch { TradeCommand t => t.Limits, MineCommand m => m.Limits, ConvoyCommand c => c.Limits, _ => null };   // Punkt 1b: Konvoi handelt in seinen Grenzen

    /// <summary>Liegt der Sektor in der Reichweite (Sprünge vom Mittelpunkt)?</summary>
    public static bool InRange(Universe u, AutoLimits l, SectorId sector)
    {
        if (l.RadiusJumps < 0 || l.Center.Value == 0) return true;
        return u.CachedRoute(l.Center, sector) is { } r && r.Count <= l.RadiusJumps;
    }

    /// <summary>Ist der Einkaufspreis erlaubt (höchstens Basispreis × Faktor)?</summary>
    public static bool PriceOk(AutoLimits l, Good g, float price) => l.MaxBuyFactor <= 0f || price <= g.BasePrice * l.MaxBuyFactor + 1e-4f;

    /// <summary>Verfügbares Geld für Einkäufe: Kasse minus Rücklage (nie negativ).</summary>
    public static float Spendable(Universe u, Ship ship, AutoLimits? l) => MathF.Max(0f, u.WalletOf(ship).Credits - (l?.Reserve ?? 0f));

    public static string Describe(Universe u, AutoLimits l, bool trade)
    {
        var parts = new List<string>();
        if (l.RadiusJumps >= 0 && l.Center.Value != 0) parts.Add($"{l.RadiusJumps} {(l.RadiusJumps == 1 ? "Sprung" : "Sprünge")} um {OrderMove.SectorName(u, l.Center)}");
        if (trade && l.MaxBuyFactor > 0f) parts.Add($"Einkauf ≤ {l.MaxBuyFactor:0.##}× Basis");
        if (trade && l.Reserve > 0f) parts.Add($"Rücklage {l.Reserve:0} Cr");
        if (trade && l.AllowLoss) parts.Add("Verlustkäufe erlaubt");   // Runde 24
        return parts.Count == 0 ? "" : " (" + string.Join(", ", parts) + ")";
    }
}
