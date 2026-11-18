using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Spielersperre (Wunsch „Spieler sperren wenn Beziehung schlecht“): ein gemeldetes Verbrechen des Spielers (<see cref="Wanted.Report"/>) senkt außer der
/// Fahndung auch sein Ansehen bei der verfolgenden KI-Fraktion um <see cref="EconomyRules.CrimeStandingLoss"/> des Verbrechens (über
/// <see cref="PlayerStanding.Change"/>, Grund „crime:&lt;Schlüssel&gt;“). Je Fraktion und Tag höchstens <see cref="EconomyRules.CrimeStandingCapPerDay"/>.
/// Die Sperren folgen über die bestehende Politikregel (<see cref="FactionPolicySystem"/>: schlechteste Beziehung gegen Schwellen, Stufendeckel nach
/// Kontrolle); eine hohe Fahndungsstufe allein sperrt nicht — eine Regel erklärt alles. Erholung (<see cref="Recover"/>): solange die Fahndungsstufe bei der
/// Fraktion 0 ist und kein Krieg herrscht, um <see cref="EconomyRules.CrimeStandingRecoveryPerDay"/> je Tag, höchstens bis zur Beziehung vor dem ersten
/// Verbrechen; bezahlte Strafe gibt sofort <see cref="EconomyRules.CrimeStandingFineBonus"/>. Nur der Spielerakteur, nur KI-Fraktionen, kein Zufall.
/// </summary>
public static class CrimeStanding
{
    /// <summary>Grundschlüssel der Ansehensänderung durch ein Verbrechen.</summary>
    public const string ReasonPrefix = "crime:";
    /// <summary>Grund der Erholung.</summary>
    public const string RecoveryReason = "crime_recovery";
    /// <summary>Grund der Erholung durch bezahlte Strafe.</summary>
    public const string FineReason = "crime_fine_paid";

    /// <summary>Senkt das Ansehen nach dem Verbrechen; liefert den tatsächlichen Verlust (0 = nichts, z. B. Regel aus, Tagesdeckel erreicht, kein Spieler).</summary>
    public static float Apply(Universe u, ActorId actor, FactionId faction, string crime)
    {
        var r = u.Rules;
        if (actor != u.PlayerActor || u.PlayerFaction is null || !u.IsNpcFaction(faction)) return 0f;
        float loss = r.CrimeStandingLoss.GetValueOrDefault(crime, 0f);
        if (loss <= 0f) return 0f;
        var me = u.Player;
        if (!me.CrimeStanding.TryGetValue(faction, out var rec))
            rec = new CrimeStandingRecord { WindowStart = u.Tick, Base = Relations.Effective(u, faction, u.PlayerActor) };
        else if (Open(u, faction, rec) <= 0f) { rec.Base = Relations.Effective(u, faction, u.PlayerActor); rec.Debt = 0f; }
        if (u.Tick - rec.WindowStart >= r.DayTicks) { rec.WindowStart = u.Tick; rec.WindowLoss = 0f; }
        if (r.CrimeStandingCapPerDay > 0f) loss = MathF.Min(loss, r.CrimeStandingCapPerDay - rec.WindowLoss);
        if (loss <= 1e-6f) { if (me.CrimeStanding.ContainsKey(faction)) { rec.LastReason = crime; rec.LastTick = u.Tick; rec.LastLoss = 0f; } return 0f; }
        float from = Relations.Effective(u, faction, u.PlayerActor);
        float actual = from - MathF.Max(-1f, from - loss);
        me.CrimeStanding[faction] = rec;
        rec.WindowLoss += loss;
        rec.Debt += actual;
        rec.LastReason = crime; rec.LastTick = u.Tick; rec.LastLoss = actual;
        if (actual > 0f) PlayerStanding.Change(u, faction, -actual, ReasonPrefix + crime);
        return actual;
    }

    /// <summary>Erholung um <paramref name="amount"/> (höchstens Schuld und Abstand zum Ausgangswert); löscht den Eintrag, wenn nichts mehr offen ist.</summary>
    static void Restore(Universe u, FactionId faction, CrimeStandingRecord rec, float amount, string reason)
    {
        float now = Relations.Effective(u, faction, u.PlayerActor);
        float step = MathF.Min(amount, MathF.Min(rec.Debt, rec.Base - now));
        if (step > 0f) { PlayerStanding.Change(u, faction, step, reason); rec.Debt -= step; }
        if (rec.Debt <= 1e-5f || Relations.Effective(u, faction, u.PlayerActor) >= rec.Base - 1e-5f) u.Player.CrimeStanding.Remove(faction);
    }

    /// <summary>Noch offener Verlust: Schuld, höchstens der Abstand der heutigen Beziehung zum Ausgangswert (rein).</summary>
    public static float Open(Universe u, FactionId faction, CrimeStandingRecord rec) =>
        MathF.Max(0f, MathF.Min(rec.Debt, rec.Base - Relations.Effective(u, faction, u.PlayerActor)));

    /// <summary>Langsame Erholung (aus <see cref="WantedSystem"/>, Intervall): nur bei Fahndungsstufe 0 und ohne Krieg mit der Fraktion. Feste Reihenfolge.</summary>
    public static void Recover(Universe u, int dt)
    {
        var me = u.Player;
        if (me.CrimeStanding.Count == 0 || u.PlayerFaction is not { } pf) return;
        float rate = u.Rules.CrimeStandingRecoveryPerDay * dt / MathF.Max(1, u.Rules.DayTicks);
        foreach (var f in me.CrimeStanding.Keys.OrderBy(k => k.Value).ToList())
        {
            var rec = me.CrimeStanding[f];
            if (!u.IsNpcFaction(f)) { me.CrimeStanding.Remove(f); continue; }
            // Frieden, Aufträge oder Bündnis haben die Beziehung schon gehoben: offen ist höchstens der Abstand zum Ausgangswert
            float open = Open(u, f, rec);
            if (open <= 1e-5f) { me.CrimeStanding.Remove(f); continue; }
            rec.Debt = open;
            if (Wanted.LevelOf(u, u.PlayerActor, f) > 0 || Wars.AtWar(u, f, pf) || rate <= 0f) continue;
            Restore(u, f, rec, rate, RecoveryReason);
        }
    }

    /// <summary>Strafe bei der Fraktion bezahlt: sofortige Erholung um <see cref="EconomyRules.CrimeStandingFineBonus"/>.</summary>
    public static void OnFinePaid(Universe u, ActorId actor, FactionId faction)
    {
        if (actor != u.PlayerActor || u.Rules.CrimeStandingFineBonus <= 0f || !u.Player.CrimeStanding.TryGetValue(faction, out var rec)) return;
        if (u.PlayerFaction is { } pf && Wars.AtWar(u, faction, pf)) return;
        Restore(u, faction, rec, u.Rules.CrimeStandingFineBonus, FineReason);
    }

    /// <summary>Anzeige (Fraktionsdetail, Comms): „Ansehen −0,35 durch Angriff (vor 3 min)“ oder null.</summary>
    public static string? Text(Universe u, FactionId faction)
    {
        if (!u.Player.CrimeStanding.TryGetValue(faction, out var rec) || Open(u, faction, rec) <= 0.005f) return null;
        long ago = Math.Max(0, u.Tick - rec.LastTick);
        int min = (int)(ago / 240);
        string when = min < 1 ? "gerade eben" : min < 120 ? $"vor {min} min" : $"vor {min / 60} h";
        return $"Ansehen −{Num(Open(u, faction, rec))} durch {SecurityNames.Crime(rec.LastReason)} ({when})";
    }

    /// <summary>Zahl mit Komma (zwei Stellen), für Toasts und Anzeige.</summary>
    public static string Num(float v) => v.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
}
