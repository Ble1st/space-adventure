using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausbau Punkt 3 (docs/POLITIK.md „Schutzvertrag“): Der Spieler zahlt einer KI-Fraktion <see cref="EconomyRules.ProtectionFeePerDay"/> je Tag (im Voraus,
/// Spielerkasse → Fraktionskasse). Solange er läuft, behandelt ihre Polizei die Schiffe des Spielers in ihrem Gebiet wie die eigenen Händler: Überfälle auf sie
/// bekommen sicher Hilfe, wenn eine Streife im Sektor ist (kein Zufallswurf), und Streifen können ihnen Geleit geben. Er endet bei Krieg, Fehde, feindlicher
/// Beziehung oder wenn die Kasse die Gebühr nicht trägt (Kasse unter 0). Kein Zufall.
/// </summary>
public static class Protection
{
    public static ProtectionContract? Of(Universe u, FactionId f)
    {
        if (u.Protections.Count == 0) return null;
        foreach (var c in u.Protections) if (c.Faction == f) return c;
        return null;
    }

    public static bool Active(Universe u, FactionId f) => u.Rules.ProtectionContracts && Of(u, f) is not null;

    /// <summary>Steht ein Schiff des Spielers im Sektor unter Schutz (Vertrag mit dem Sektorbesitzer)?</summary>
    public static bool Covers(Universe u, Ship ship, SectorId sector) =>
        u.Protections.Count > 0 && ship.IsPlayer && u.Sectors.TryGetValue(sector, out var sec) && Active(u, sec.Owner);

    /// <summary>Warum der Vertrag mit <paramref name="f"/> nicht (mehr) bestehen kann (null = möglich); ohne Geldprüfung.</summary>
    public static string? StandingIssue(Universe u, FactionId f)
    {
        if (!u.Rules.ProtectionContracts) return "Schutzverträge sind abgeschaltet.";
        if (u.PlayerFaction is not { } me) return "Keine eigene Fraktion.";
        if (!u.IsNpcFaction(f)) return "Keine KI-Fraktion.";
        if (Bandits.IsEstablished(u, f) || u.Actors.TryGetValue(new ActorId(f.Value), out var a) && a.Kind == ActorKind.Outlaw) return "Gesetzlose bieten keinen Schutz.";
        if (Wars.AtWar(u, f, me)) return "Krieg.";
        if (Feuds.InFeud(u, f, me)) return "Fehde.";
        if (Relations.Effective(u, f, me) <= u.Rules.HostileRelation) return "Beziehung feindlich.";
        return null;
    }

    public static string? BuyIssue(Universe u, FactionId f)
    {
        if (StandingIssue(u, f) is { } s) return s;
        if (Of(u, f) is not null) return "Vertrag läuft bereits.";
        if (u.PlayerWallet.Credits < u.Rules.ProtectionFeePerDay) return $"Zu wenig Credits ({u.Rules.ProtectionFeePerDay:0} Cr je Tag).";
        return null;
    }

    /// <summary>Schließt den Vertrag und zahlt den ersten Tag.</summary>
    public static ProtectionContract Buy(Universe u, FactionId f)
    {
        var c = new ProtectionContract { Faction = f, Since = u.Tick, PaidUntil = u.Tick };
        u.Protections.Add(c);
        Pay(u, c);
        u.Events.Publish(new ProtectionStarted(f, u.Rules.ProtectionFeePerDay));
        return c;
    }

    static void Pay(Universe u, ProtectionContract c)
    {
        float fee = u.Rules.ProtectionFeePerDay;
        u.PlayerWallet.Credits -= fee;
        if (u.Actors.TryGetValue(new ActorId(c.Faction.Value), out var a)) a.Treasury.Credits += fee;
        c.PaidUntil += Math.Max(1, u.Rules.DayTicks);
        u.Events.Publish(new TreasuryIncome(new ActorId(c.Faction.Value), "protection", fee));
        u.Events.Publish(new ProtectionPaid(c.Faction, fee));
    }

    public static void End(Universe u, FactionId f, string reason)
    {
        int i = u.Protections.FindIndex(c => c.Faction == f);
        if (i < 0) return;
        u.Protections.RemoveAt(i);
        u.Events.Publish(new ProtectionEnded(f, reason));
    }

    /// <summary>Täglicher Schritt (<see cref="ProtectionSystem"/>): Kündigung bei Krieg/Fehde/Feindschaft, Kasse unter 0 oder unbezahlter Gebühr; sonst zahlen.</summary>
    public static void Step(Universe u)
    {
        if (u.Protections.Count == 0) return;
        foreach (var c in u.Protections.ToList())
        {
            if (!u.Rules.ProtectionContracts) { End(u, c.Faction, "rule"); continue; }
            if (u.PlayerFaction is not { } me || !u.IsNpcFaction(c.Faction)) { End(u, c.Faction, "hostile"); continue; }
            if (Wars.AtWar(u, c.Faction, me)) { End(u, c.Faction, "war"); continue; }
            if (Feuds.InFeud(u, c.Faction, me)) { End(u, c.Faction, "feud"); continue; }
            if (Relations.Effective(u, c.Faction, me) <= u.Rules.HostileRelation) { End(u, c.Faction, "hostile"); continue; }
            if (u.PlayerWallet.Credits < 0f) { End(u, c.Faction, "unpaid"); continue; }
            if (u.Tick >= c.PaidUntil)
            {
                if (u.PlayerWallet.Credits < u.Rules.ProtectionFeePerDay) { End(u, c.Faction, "unpaid"); continue; }
                Pay(u, c);
            }
        }
    }

    public static string ReasonText(string reason) => reason switch
    {
        "cancelled" => "gekündigt",
        "war" => "Krieg",
        "feud" => "Fehde",
        "hostile" => "Beziehung feindlich",
        "unpaid" => "Gebühr nicht bezahlt",
        _ => "Regel aus",
    };
}

/// <summary>Ausbau Punkt 3: Schutzverträge abrechnen und prüfen (Politik-Modul, Intervall <see cref="Interval"/>).</summary>
public sealed class ProtectionSystem : ISimSystem
{
    public const int Interval = 50;
    public void Step(Universe u, int dt) => Protection.Step(u);
}
