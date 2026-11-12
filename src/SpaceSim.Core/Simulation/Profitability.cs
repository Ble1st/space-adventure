using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 22: Rentabilität (Grund c) — reine Regeln über <see cref="ProfitMeter"/>. Gemessen werden wirtschaftliche KI-Schiffe (Händler, Schürfer,
/// <see cref="Ship.IsEconomyAi"/>) und alle fertigen Stationen. Vermögen = Kasse + Fracht bzw. Lager zum Basispreis; der gleitende Mittelwert seiner Änderung
/// je Tick ist der Netto-Geldfluss (Erlös − Einkauf − Unterhalt − Abgaben − Verluste durch Überfall oder Abwurf). Kein Geschäftsergebnis und daher über
/// <see cref="Transfer"/> neutralisiert: Investitionen eines Schiffs (Kauf eines Schiffs, Stationsfinanzierung, Upgrade), Kapitalausgleich innerhalb eines
/// Akteurs, Subvention und Grundeinkommen einer Station. Die Instandhaltungs-Erstattung der Kasse zählt (sie gleicht den Materialkauf aus).
/// Dienste der Fraktionen (Polizei, Bauschiffe, Regierungs-, Forschungs-, Export- und Endverbraucherstationen, Verstecke) sind nicht „unrentabel“: Für sie
/// gilt (c) nur als „lange ohne Nutzen und überzählig“ (<see cref="SecuritySystem"/>, <see cref="FactionBuilderSystem"/>); ihre Messung dient der Anzeige.
/// </summary>
public static class Profitability
{
    /// <summary>Wird dieses Schiff gemessen? Wirtschaftliche KI-Schiffe (Händler und Schürfer).</summary>
    public static bool Measured(Ship s) => s.IsEconomyAi;

    /// <summary>Vermögen eines Schiffs: Betriebsgeld + Fracht zum Basispreis.</summary>
    public static float WorthOf(Universe u, Ship s)
    {
        float w = s.Credits;
        foreach (var (g, n) in s.Cargo.OrderBy(k => k.Key.Value)) if (n > 0f && u.Goods.TryGetValue(g, out var good)) w += n * good.BasePrice;
        return w;
    }

    /// <summary>Vermögen einer Station: Kasse + Lager zum Basispreis.</summary>
    public static float WorthOf(Universe u, Station st)
    {
        float w = st.Credits;
        foreach (var (g, e) in st.Market.OrderBy(k => k.Key.Value)) if (e.Amount > 0f && u.Goods.TryGetValue(g, out var good)) w += e.Amount * good.BasePrice;
        return w;
    }

    /// <summary>
    /// Durchgereichter Betrag, der kein Geschäftsergebnis ist: <paramref name="amount"/> &gt; 0 = Geld kam von außen (Subvention, Grundeinkommen, Ausgleich
    /// erhalten), &lt; 0 = Kapital ging hinaus (Investition, Ausgleich abgegeben). Verschiebt nur die Bezugsgröße der nächsten Messung.
    /// </summary>
    public static void Transfer(ProfitMeter m, float amount)
    {
        if (m.Since >= 0) m.Worth += amount;
    }

    /// <summary>
    /// Hauptteil der Karte: bei zerfallener Karte der Teil mit den meisten fertigen Wirtschaftsstationen (Gleichstand: kleinster Schlüssel), sonst 0 (alles
    /// Hauptteil).
    /// </summary>
    public static int MainPart(Universe u)
    {
        var parts = MapParts.Of(u);
        if (!parts.IsSplit) return 0;
        int best = -1, bestCount = -1;
        foreach (var p in parts.Parts)
        {
            int count = 0;
            foreach (var sec in p)
                foreach (var id in u.Sectors[sec].Stations)
                    if (u.Stations.TryGetValue(id, out var st) && !st.IsBuildSite && StationProtection.IsEconomic(u, st)) count++;
            if (count > bestCount) { bestCount = count; best = p[0].Value; }
        }
        return best;
    }

    /// <summary>
    /// Ruht die Uhr? Ja, solange eine physische Torsperre den Sektor vom Hauptteil der Karte trennt (<paramref name="mainPart"/> aus <see cref="MainPart"/>):
    /// Eine Sperre allein macht nichts unrentabel — wer im abgeschnittenen Teil nur wartet, verschwindet nicht deswegen.
    /// </summary>
    public static bool Resting(Universe u, SectorId sector, int mainPart) =>
        mainPart != 0 && MapParts.Of(u).KeyOf(sector) != mainPart;

    /// <summary>
    /// Ein Messschritt über <paramref name="dt"/> Ticks: gleitender Mittelwert des Geldflusses, Verlustzähler mit Hysterese. Beim ersten Aufruf beginnt die
    /// Messung (Schonfrist ab hier). <paramref name="resting"/>: nur die Bezugsgröße nachführen, Mittelwert und Zähler ruhen.
    /// </summary>
    public static void Step(Universe u, ProfitMeter m, float worth, int dt, bool resting)
    {
        var r = u.Rules;
        if (m.Since < 0) { m.Since = u.Tick; m.Worth = worth; m.Rate = 0f; m.LossTicks = 0; return; }
        float flow = dt > 0 ? (worth - m.Worth) / dt : 0f;
        m.Worth = worth;
        if (resting || r.ProfitWindowTicks <= 0 || dt <= 0) return;
        m.Rate += (flow - m.Rate) * MathF.Min(1f, (float)dt / r.ProfitWindowTicks);
        if (m.Rate < 0f) m.LossTicks = (int)Math.Min(int.MaxValue, (long)m.LossTicks + dt);
        else if (m.Rate > r.ProfitableAbove) m.LossTicks = 0;
    }

    /// <summary>
    /// Länger nicht rentabel (Grund c)? Messung an, Schonfrist (<see cref="EconomyRules.ProfitGraceTicks"/>) seit Messbeginn vorbei und der Mittelwert lag
    /// mindestens <see cref="EconomyRules.UnprofitableTicks"/> lang unter null (ohne zwischendurch die Hysterese-Schwelle zu überschreiten).
    /// </summary>
    public static bool Unprofitable(Universe u, ProfitMeter m) =>
        u.Rules.ProfitWindowTicks > 0 && m.Since >= 0 && u.Tick - m.Since >= u.Rules.ProfitGraceTicks && m.LossTicks >= u.Rules.UnprofitableTicks;

    /// <summary>Anzeigezeile: „+1,2 Cr/Tick“ bzw. „−0,4 Cr/Tick · seit 12000 Ticks im Minus“; null, solange nicht gemessen.</summary>
    public static string? Text(Universe u, ProfitMeter m)
    {
        if (m.Since < 0 || u.Rules.ProfitWindowTicks <= 0) return null;
        string rate = (m.Rate >= 0 ? "+" : "−") + MathF.Abs(m.Rate).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + " Cr/Tick";
        if (Unprofitable(u, m)) return rate + " · länger nicht rentabel";
        if (m.LossTicks > 0) return rate + $" · seit {m.LossTicks} Ticks im Minus";
        return rate;
    }
}

/// <summary>
/// Phase 22: Misst die Rentabilität (Viabilitäts-Modul, Intervall <see cref="Interval"/>): je wirtschaftlichem KI-Schiff und fertiger Station ein Schritt von
/// <see cref="Profitability.Step"/>; die Uhr ruht im abgeschnittenen Teil der Karte.
/// </summary>
public sealed class ProfitabilitySystem : ISimSystem
{
    public const int Interval = 50;

    public void Step(Universe u, int dt)
    {
        if (u.Rules.ProfitWindowTicks <= 0) return;
        int main = Profitability.MainPart(u);
        foreach (var s in u.ShipsById())
        {
            if (!Profitability.Measured(s)) continue;
            Profitability.Step(u, s.Profit, Profitability.WorthOf(u, s), dt, Profitability.Resting(u, s.Transit?.ToSector ?? s.Sector, main));
        }
        foreach (var st in u.StationsById())
        {
            if (st.IsBuildSite) continue;
            Profitability.Step(u, st.Profit, Profitability.WorthOf(u, st), dt, Profitability.Resting(u, st.Sector, main));
        }
    }
}
