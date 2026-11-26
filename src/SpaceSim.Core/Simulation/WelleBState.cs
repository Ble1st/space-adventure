using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Welle B: gespeicherter Zustand der Spielerregeln (null auf <see cref="Universe.WelleB"/> = nie benutzt): Flottenabgabe (Saldo der laufenden
/// Periode, Verkäufe je Fraktion des Verkaufssektors) und Spenden (Tag und Ansehensgewinn je Fraktion).
/// </summary>
public sealed class WelleBState
{
    /// <summary>Handelssaldo der Spielerflotte in der laufenden Abrechnungsperiode (Verkäufe − Einkäufe; negativ = Verlust, wird vorgetragen).</summary>
    public float FleetPending;
    /// <summary>Verkaufserlöse der Flotte in der Periode je Fraktion des Verkaufssektors (0 = herrenlos) — Schlüssel für die Aufteilung der Abgabe.</summary>
    public SortedDictionary<int, float> FleetSales { get; } = new();
    /// <summary>Spenden: je Fraktion der Tag (Tick ÷ DayTicks) und der an diesem Tag schon gewonnene Ansehenszuwachs.</summary>
    public SortedDictionary<int, (long Day, float Gain)> Donations { get; } = new();

    public static WelleBState Of(Universe u) => u.WelleB ??= new WelleBState();
}

/// <summary>
/// Welle B (3): Abgabe auf den Handelsgewinn der Spielerflotte. Gewählt statt eines mit der Flottengröße steigenden Unterhalts: die Abgabe trifft
/// genau den Gewinn der Automatik (der Bot verdiente ~500 000 Cr/h) und lässt kleine Flotten und Verlustphasen in Ruhe; ein Unterhalt nach Größe hätte
/// auch unrentable Flotten (Schutz, Bergung) bestraft. Erfasst werden alle Markt-Buchungen (<see cref="Trading.PlayerLedger.BookBuy"/>/<c>BookSale</c>)
/// von Spielerschiffen außer dem aktiven Schiff.
/// </summary>
public static class FleetLevy
{
    /// <summary>Zählt das Schiff als Flottenschiff des Spielers (eigen, nicht das aktive)?</summary>
    public static bool Counts(Universe u, Ship ship) =>
        u.Rules.PlayerFleetLevyShare > 0f && u.PlayerActor.Value != 0 && ship.Owner == u.PlayerActor && ship.Id != u.PlayerShip;

    /// <summary>Bucht einen Verkauf (<paramref name="money"/> &gt; 0) oder Einkauf (&lt; 0) eines Flottenschiffs an <paramref name="st"/> in den Saldo.</summary>
    public static void Note(Universe u, Ship ship, Station st, float money)
    {
        if (!Counts(u, ship) || money == 0f) return;
        var s = WelleBState.Of(u);
        s.FleetPending += money;
        if (money > 0f)
        {
            int f = u.Sectors.TryGetValue(st.Sector, out var sec) && u.IsNpcFaction(sec.Owner) ? sec.Owner.Value : 0;
            s.FleetSales[f] = s.FleetSales.GetValueOrDefault(f) + money;
        }
    }

    /// <summary>
    /// Rechnet die Periode ab: bei positivem Saldo zahlt die Spielerkasse <see cref="EconomyRules.PlayerFleetLevyShare"/> davon (auch in die Schulden, wie
    /// andere Spielergebühren), aufgeteilt nach Verkaufserlösen an die Fraktionen der Verkaufssektoren (herrenlos = versenkt). Ein Verlust wird vorgetragen.
    /// Liefert die Abgabe.
    /// </summary>
    public static float Settle(Universe u)
    {
        if (u.WelleB is not { } s) return 0f;
        float profit = s.FleetPending;
        if (profit <= 0f) { s.FleetSales.Clear(); return 0f; }   // Verlust bleibt als Vortrag stehen
        float levy = profit * MathF.Min(1f, u.Rules.PlayerFleetLevyShare);
        // Welle C (2): die Abgabe nimmt der Kasse nicht den Vorrat der eigenen Behörden — sonst riss die Abrechnung (alle 2400 Ticks ein Klumpen) die Kasse
        // ins Minus und alle Behörden fielen im selben Schritt aus (Spielerbot). Der nicht bezahlte Teil bleibt als Gewinn stehen (gestundet).
        float carry = 0f;
        if (PlayerUpkeepOrder.Active(u))
        {
            float room = MathF.Max(0f, u.Player.Treasury.Credits - PlayerUpkeepOrder.Reserve(u));
            if (levy > room) { carry = (levy - room) / MathF.Max(1e-6f, MathF.Min(1f, u.Rules.PlayerFleetLevyShare)); levy = room; }
            if (levy <= 0f) { s.FleetPending = profit; return 0f; }
        }
        float sales = s.FleetSales.Values.Sum();
        float toFactions = 0f;
        foreach (var (f, v) in s.FleetSales)
        {
            if (f == 0 || sales <= 0f || !u.Actors.TryGetValue(new ActorId(f), out var a)) continue;
            float part = levy * v / sales;
            a.Treasury.Credits += part;
            toFactions += part;
            u.Events.Publish(new TreasuryIncome(a.Id, "fleet_levy", part));
        }
        u.Player.Treasury.Credits -= levy;
        s.FleetPending = carry;
        s.FleetSales.Clear();
        u.Events.Publish(new FleetLevyPaid(profit, levy, toFactions, levy - toFactions,
            $"Flottenabgabe: {levy:0} Cr ({u.Rules.PlayerFleetLevyShare:P0} von {profit:0} Cr Flottengewinn)."));
        return levy;
    }
}

/// <summary>Welle B (3): Abrechnung der Flottenabgabe je <see cref="EconomyRules.PlayerFleetLevyTicks"/> (zustandslos über den Tick).</summary>
public sealed class FleetLevySystem : ISimSystem
{
    public const int Interval = 100;

    public void Step(Universe u, int dt)
    {
        if (u.Rules.PlayerFleetLevyShare <= 0f || u.WelleB is null) return;
        long period = Math.Max(1, u.Rules.PlayerFleetLevyTicks);
        if (u.Tick / period == (u.Tick - dt) / period) return;
        FleetLevy.Settle(u);
    }
}

/// <summary>Welle B (4): Spende an eine KI-Fraktion gegen Ansehen (Tagesgrenze, Obergrenze der Beziehung). Reine Prüfung + Ausführung.</summary>
public static class Donations
{
    static long Today(Universe u) => u.Tick / Math.Max(1, u.Rules.DayTicks);

    /// <summary>Heute aus Spenden schon gewonnenes Ansehen bei dieser Fraktion.</summary>
    public static float GainedToday(Universe u, FactionId f) =>
        u.WelleB is { } s && s.Donations.TryGetValue(f.Value, out var d) && d.Day == Today(u) ? d.Gain : 0f;

    /// <summary>Ansehensgewinn einer Spende dieses Betrags jetzt (gedeckelt durch Tagesgrenze und Obergrenze der Beziehung). Rein.</summary>
    public static float GainFor(Universe u, FactionId f, float amount)
    {
        var r = u.Rules;
        if (u.PlayerFaction is not { } me) return 0f;
        float raw = amount / 1000f * r.DonationPer1000;
        float left = MathF.Max(0f, r.DonationDailyCap - GainedToday(u, f));
        float room = MathF.Max(0f, r.DonationMaxRelation - Relations.Effective(u, f, me));
        return MathF.Max(0f, MathF.Min(raw, MathF.Min(left, room)));
    }

    /// <summary>Warum keine Spende möglich ist (null = möglich). Rein.</summary>
    public static string? Issue(Universe u, FactionId f, float amount)
    {
        var r = u.Rules;
        if (!r.Donations) return "Spenden sind abgeschaltet.";
        if (u.PlayerFaction is not { } me || !u.IsNpcFaction(f)) return "Keine Fraktion für Spenden.";
        if (Bandits.IsEstablished(u, f)) return "Banditen führen keine Diplomatie.";   // Punkt 17: keine Diplomatie mit Banditen
        if (amount <= 0f) return "Betrag fehlt.";
        if (Wars.AtWar(u, me, f)) return "Im Krieg nimmt sie kein Geld an.";
        if (u.Player.Treasury.Credits < amount) return $"Kasse reicht nicht ({amount:0} Cr nötig).";
        if (GainedToday(u, f) >= r.DonationDailyCap - 1e-4f) return $"Heute schon genug gespendet (+{r.DonationDailyCap:0.00} am Tag).";
        if (Relations.Effective(u, f, me) >= r.DonationMaxRelation - 1e-4f) return $"Beziehung schon bei {r.DonationMaxRelation:0.00} — Spenden bringen nicht mehr.";
        return null;
    }

    /// <summary>Spende ausführen (nach <see cref="Issue"/>): Geld Spielerkasse → Fraktionskasse, Ansehen +Gewinn. Liefert den Gewinn.</summary>
    public static float Donate(Universe u, FactionId f, float amount)
    {
        float gain = GainFor(u, f, amount);
        var a = u.Actors[new ActorId(f.Value)];
        u.Player.Treasury.Credits -= amount;
        a.Treasury.Credits += amount;
        u.Events.Publish(new TreasuryIncome(a.Id, "donation", amount));
        var s = WelleBState.Of(u);
        long today = Today(u);
        float before = s.Donations.TryGetValue(f.Value, out var d) && d.Day == today ? d.Gain : 0f;
        s.Donations[f.Value] = (today, before + gain);
        PlayerStanding.Change(u, f, gain, "donation");
        u.Events.Publish(new DonationMade(f, amount, gain, $"Spende an {a.Name}: {amount:0} Cr — Ansehen +{gain:0.00}."));
        return gain;
    }
}

/// <summary>Welle B: Speichern/Laden/Hash des Zustands (additiv, v17 bleibt; gehasht nur mit Spielerpolitik und nur wenn vorhanden).</summary>
public sealed class WelleBSave
{
    public float FleetPending { get; set; }
    public Dictionary<int, float>? FleetSales { get; set; }
    public List<float[]>? Donations { get; set; }   // [Fraktion, Tag, Gewinn]
}

public static class WelleBCodec
{
    public static void Save(Universe u, SaveGame s)
    {
        if (u.WelleB is not { } b) return;
        s.WelleB = new WelleBSave
        {
            FleetPending = b.FleetPending,
            FleetSales = b.FleetSales.Count > 0 ? new Dictionary<int, float>(b.FleetSales) : null,
            Donations = b.Donations.Count > 0 ? b.Donations.Select(kv => new[] { kv.Key, (float)kv.Value.Day, kv.Value.Gain }).ToList() : null,
        };
    }

    public static void Load(SaveGame s, Universe u)
    {
        if (s.WelleB is not { } w) return;
        var b = new WelleBState { FleetPending = w.FleetPending };
        if (w.FleetSales is { } fs) foreach (var (k, v) in fs) b.FleetSales[k] = v;
        if (w.Donations is { } ds) foreach (var d in ds) if (d.Length >= 3) b.Donations[(int)d[0]] = ((long)d[1], d[2]);
        u.WelleB = b;
    }

    public static void Mix(Universe u, Action<ulong> mix, Action<float> mixF)
    {
        if (u.WelleB is not { } b) return;
        mix(0xB0B0u); mixF(b.FleetPending);
        foreach (var (k, v) in b.FleetSales) { mix((ulong)k); mixF(v); }
        foreach (var (k, d) in b.Donations) { mix(0xB0B1u); mix((ulong)k); mix((ulong)d.Day); mixF(d.Gain); }
    }
}
