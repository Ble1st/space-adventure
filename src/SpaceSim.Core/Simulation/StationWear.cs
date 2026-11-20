using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Ausbau Punkt 8: eine Station hat eine Zustandsschwelle überschritten (langsamer / Stillstand / wieder normal). Anzeige, Balance; nicht gespeichert.</summary>
public sealed record StationWearChanged(StationId Station, float Integrity, StationWearLevel From, StationWearLevel To) : SimEvent;

/// <summary>Ausbau Punkt 8: Reparaturmaterial verbraucht (Balance: zusätzliche Instandhaltungsnachfrage). Nicht gespeichert.</summary>
public sealed record StationWearRepaired(StationId Station, float Points, float MaterialValue) : SimEvent;

/// <summary>Ausbau Punkt 8: Notinstandhaltung — die Fraktionskasse hat den offenen Rückstand zum Aufpreis gekauft (Geldsenke). Nicht gespeichert.</summary>
public sealed record StationWearEmergency(StationId Station, ActorId Payer, float Cost) : SimEvent;

/// <summary>Wirkungsstufe des Zustands.</summary>
public enum StationWearLevel { Normal, Slow, Stopped }

/// <summary>
/// Ausbau Punkt 8 — Stationsverschleiß (docs/STATIONEN.md „Stationsverschleiß“). Aufgerufen aus <see cref="MaintenanceSystem"/> (Intervall 50, skaliert mit dt),
/// rein, ohne Zufall:
/// <list type="bullet">
/// <item>Liegt die offene Instandhaltung (Basiswert) über <see cref="EconomyRules.WearBacklogShare"/> des Tagesbedarfs, sinkt der Zustand um
/// <see cref="EconomyRules.WearLossPerDay"/> je Tag — Regierungsstationen und geschützte Startstationen höchstens bis <see cref="EconomyRules.WearProtectedFloor"/>.</item>
/// <item>Liegt sie unter <see cref="EconomyRules.WearRecoverBelowShare"/>, repariert die Station mit Material aus ihrem Instandhaltungslager: je 1 % Zustand
/// <see cref="EconomyRules.WearRepairNeedPerPoint"/> Tagesbedarfe, höchstens <see cref="EconomyRules.WearRecoverPerDay"/> je Tag; fehlt Material, nur anteilig.</item>
/// <item>Wirkung: unter <see cref="EconomyRules.WearSlowBelow"/> laufen Rezepte mit Faktor Zustand ÷ Schwelle (Zutaten und Ausstoß je Takt — gleiche Wirkung wie
/// eine um den Kehrwert längere Taktzeit, ohne neuen Zustand); unter <see cref="EconomyRules.WearStopBelow"/> steht die Produktion. Handel, Andocken und Dienste bleiben.</item>
/// <item>Notinstandhaltung (<see cref="MaintenanceSystem"/>): liegt eine Station einer KI-Fraktion unter <see cref="EconomyRules.WearEmergencyBelow"/> und ist der
/// Rückstand über der Verfallsschwelle, kauft die Kasse (nur aus <see cref="FactionViability.Spendable"/>) den Rückstand zu Basispreis ×
/// <see cref="EconomyRules.WearEmergencyMarkup"/> — das Material wird nicht aus der Wirtschaft gezogen (Geldsenke wie der Notbau). Der Zustand bleibt so knapp
/// unter der Schwelle stehen; erholen kann sich die Station nur mit echtem Material.</item>
/// </list>
/// </summary>
public static class StationWear
{
    /// <summary>Unterste Grenze des Zustands für diese Station (geschützt/Regierung → Regel, sonst 0).</summary>
    public static float Floor(Universe u, Station st) =>
        st.BankruptcyProtected || StationProtection.IsOfficial(u, st) ? Math.Clamp(u.Rules.WearProtectedFloor, 0f, 1f) : 0f;

    /// <summary>Offene Instandhaltung als Anteil des Tagesbedarfs (Basiswerte; 0 ohne Bedarf).</summary>
    public static float BacklogShare(Universe u, Station st, List<(GoodId Good, float PerDay)> need)
    {
        float needValue = 0f, backlogValue = 0f;
        foreach (var (g, perDay) in need)
        {
            float bp = u.Goods[g].BasePrice;
            needValue += perDay * bp;
            backlogValue += st.MaintenanceBacklog.GetValueOrDefault(g) * bp;
        }
        return needValue > 0f ? backlogValue / needValue : 0f;
    }

    /// <summary>Ist eine Notinstandhaltung fällig (Regel an, Zustand unter der Schwelle, Rückstand über der Verfallsschwelle)?</summary>
    public static bool EmergencyDue(Universe u, Station st, List<(GoodId Good, float PerDay)> need) =>
        u.Rules.StationWear && u.Rules.WearEmergencyBelow > 0f && st.Integrity < u.Rules.WearEmergencyBelow && need.Count > 0
        && BacklogShare(u, st, need) > u.Rules.WearBacklogShare;

    /// <summary>Preis der Notinstandhaltung: offener Rückstand × Basispreis × <see cref="EconomyRules.WearEmergencyMarkup"/>.</summary>
    public static float EmergencyCost(Universe u, Station st) =>
        Maintenance.BacklogValue(u, st) * MathF.Max(1f, u.Rules.WearEmergencyMarkup);

    /// <summary>Wirkungsstufe des Zustands (ohne Regel immer Normal).</summary>
    public static StationWearLevel Level(Universe u, Station st)
    {
        if (!u.Rules.StationWear || st.Integrity >= 1f) return StationWearLevel.Normal;
        if (st.Integrity < u.Rules.WearStopBelow) return StationWearLevel.Stopped;
        return st.Integrity < u.Rules.WearSlowBelow ? StationWearLevel.Slow : StationWearLevel.Normal;
    }

    /// <summary>Faktor auf Zutaten und Ausstoß je Rezepttakt: 1 (normal), Zustand ÷ Schwelle (langsamer), 0 (Stillstand).</summary>
    public static float ProductionFactor(Universe u, Station st) => Level(u, st) switch
    {
        StationWearLevel.Stopped => 0f,
        StationWearLevel.Slow => st.Integrity / MathF.Max(1e-4f, u.Rules.WearSlowBelow),
        _ => 1f,
    };

    /// <summary>Anzeige: „Zustand 64 % · Produktion 91 %“ / „· Stillstand“; null ohne Regel oder ohne Instandhaltung.</summary>
    public static string? Text(Universe u, Station st) =>
        Percent(u, st) is not { } p ? null : Effect(u, st) is { } e ? $"Zustand {p} · {e}" : $"Zustand {p}";

    /// <summary>Zustand in Prozent („64 %“); null ohne Regel oder ohne Instandhaltung.</summary>
    public static string? Percent(Universe u, Station st) =>
        !u.Rules.StationWear || !Maintenance.Applies(u, st) ? null : $"{MathF.Floor(st.Integrity * 100f + 1e-3f):0} %";

    /// <summary>Wirkung des Zustands („Produktion 91 %“, „Stillstand (Produktion steht)“, „erholt sich mit Material“, „verfällt: Instandhaltung offen“); null = einwandfrei.</summary>
    public static string? Effect(Universe u, Station st)
    {
        if (Percent(u, st) is null) return null;
        string? trend = Decaying(u, st) ? "verfällt: Instandhaltung offen" : st.Integrity < 1f ? "erholt sich mit Material" : null;
        return Level(u, st) switch
        {
            StationWearLevel.Stopped => "Stillstand (Produktion steht)",
            StationWearLevel.Slow => $"Produktion {ProductionFactor(u, st) * 100f:0} %" + (trend is null ? "" : $", {trend.Split(':')[0]}"),
            _ => trend,
        };
    }

    /// <summary>Liegt der Rückstand über der Verfallsschwelle (Anzeige; gleiche Rechnung wie <see cref="Step"/>)?</summary>
    public static bool Decaying(Universe u, Station st)
    {
        if (!u.Rules.StationWear || !Maintenance.Applies(u, st) || !u.StationTypes.TryGetValue(st.TypeKey, out var t)) return false;
        return BacklogShare(u, st, Maintenance.NeedPerDay(u, t)) > u.Rules.WearBacklogShare && st.Integrity > Floor(u, st);
    }

    /// <summary>
    /// Ein Schritt für eine instand gehaltene Station nach dem Verbrauch der Instandhaltung (<paramref name="need"/> = Tagesbedarf je Ware). Gibt den Wert des
    /// verbrauchten Reparaturmaterials zurück (die Kasse erstattet ihn wie die Instandhaltung).
    /// </summary>
    internal static float Step(Universe u, Station st, List<(GoodId Good, float PerDay)> need, int dt)
    {
        var r = u.Rules;
        if (!r.StationWear || need.Count == 0) return 0f;
        float day = MathF.Max(1f, r.DayTicks);
        float share = BacklogShare(u, st, need);
        var before = Level(u, st);
        float value = 0f;
        if (share > r.WearBacklogShare)
        {
            float floor = Floor(u, st);
            if (st.Integrity > floor) st.Integrity = MathF.Max(floor, st.Integrity - r.WearLossPerDay * dt / day);
        }
        else if (share < r.WearRecoverBelowShare && st.Integrity < 1f && r.WearRecoverPerDay > 0f)
        {
            float want = MathF.Min(1f - st.Integrity, r.WearRecoverPerDay * dt / day);
            float perPoint = MathF.Max(0f, r.WearRepairNeedPerPoint) * 100f;   // Tagesbedarfe je voller Zustandseinheit (1 = 100 %)
            float frac = 1f;
            if (perPoint > 0f)
                foreach (var (g, perDay) in need)
                {
                    float req = perDay * perPoint * want;
                    if (req <= 0f) continue;
                    float have = st.Market.TryGetValue(g, out var e) ? MathF.Max(0f, e.Amount) : 0f;
                    frac = MathF.Min(frac, have / req);
                }
            if (frac > 0f)
            {
                float got = want * frac;
                if (perPoint > 0f)
                    foreach (var (g, perDay) in need)
                    {
                        float take = perDay * perPoint * got;
                        if (take <= 0f || !st.Market.TryGetValue(g, out var e)) continue;
                        take = MathF.Min(take, MathF.Max(0f, e.Amount));
                        e.Amount -= take;
                        float price = e.Price > 0f ? e.Price : u.Goods[g].BasePrice;
                        value += take * MathF.Min(price, u.Goods[g].BasePrice * MathF.Max(1f, Maintenance.MaxBidFactor(u, st)));
                    }
                st.Integrity = MathF.Min(1f, st.Integrity + got);
                if (st.Integrity > 0.99999f) st.Integrity = 1f;
                u.Events.Publish(new StationWearRepaired(st.Id, got, value));
            }
        }
        var after = Level(u, st);
        if (after != before) u.Events.Publish(new StationWearChanged(st.Id, st.Integrity, before, after));
        return value;
    }
}
