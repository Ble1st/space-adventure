using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Geldkreislauf: Stationen haben Unterhalt (Sink) und optional Einnahmen aus Verkäufen außerhalb der Welt (Faucet);
/// Handels- und Minenschiffe zahlen Wartung (Sink). Ohne diese Ströme würden Credits nur im Kreis laufen oder endlos wachsen.
/// Phase 9: Die Vermögensabgaben verschwinden nicht mehr, sondern fließen (Anteil <see cref="EconomyRules.TaxToTreasuryShare"/>) in die Kasse der
/// zuständigen Fraktion — Mitglieder zahlen an ihre Fraktion, freie Einzelschiffe an die Hoheit des Sektors, in dem sie liegen
/// (<see cref="TaxAuthority"/>). Für die Zahler ist die Abgabe dieselbe wie vorher; Ereignisse <see cref="TaxCollected"/>/<see cref="TaxSunk"/>
/// machen die Geldmenge erklärbar.
/// </summary>
public sealed class UpkeepSystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        var collected = new SortedDictionary<int, float>();
        float sunk = 0f;
        // Phase 15: Steuerbremse — je größer der Anteil einer Fraktionskasse an der Geldmenge, desto weniger der Abgabe behält sie (FiscalFactors)
        var factors = FiscalFactors(u);
        float FactorOf(FactionId? authority) => authority is { } fa && factors.TryGetValue(fa.Value, out var x) ? x : 1f;
        void Route(FactionId? authority, float tax)
        {
            if (tax <= 0f) return;
            // Phase 15: Steuerbremse — eine volle Kasse nimmt nur noch einen Teil der Abgabe auf, der Rest verlässt die Welt (wie vor Phase 9)
            float share = Math.Clamp(u.Rules.TaxToTreasuryShare, 0f, 1f) * FactorOf(authority);
            if (authority is { } f && share > 0f && u.Actors.TryGetValue(f, out var actor))
            {
                float paid = tax * share;
                actor.Treasury.Credits += paid;
                collected[f.Value] = collected.GetValueOrDefault(f.Value) + paid;
                sunk += tax - paid;
            }
            else sunk += tax;
        }

        // Phase 8: Baustellen (Treuhandgeld des Investors) zahlen weder Unterhalt noch Vermögensabgabe
        // Phase 10: Regierungsstationen und Wachen zahlen ihren Unterhalt aus der Kasse des Besitzers (Senke für die Fraktionskassen);
        // reicht die Kasse nicht, zahlt die Station, soweit sie Geld hat — der Rest bleibt unbezahlt (gemeldet, keine Schulden)
        var official = new SortedDictionary<int, (float paid, float unpaid)>();
        foreach (var st in u.Stations.Values)
            if (!st.IsBuildSite && u.StationTypes.TryGetValue(st.TypeKey, out var type))
            {
                float stUpkeep = type.UpkeepPerTick * dt;
                if (type.Government != GovernmentKind.None && stUpkeep > 0f && FirmSeats.IsFirmGovernment(u, st))
                    stUpkeep = FirmSeats.UpkeepPerTick(u, st, type) * dt;   // Ausbau Punkt 9: KI-Firmensitz trägt einen Anteil aus der Firmenkasse
                if (type.Government != GovernmentKind.None && stUpkeep > 0f)
                {
                    var cur = official.GetValueOrDefault(st.Owner.Value);
                    // Ausgleich (Punkt 2): Spieler-Regierungsstationen zahlen nur einen Anteil; mit der Untergrenze wird eine Station unbezahlt statt Schulden zu machen
                    if (PlayerUpkeep.IsPlayerGovernment(u, st, type))
                    {
                        stUpkeep = PlayerUpkeep.GovernmentPerTick(u, st, type) * dt;
                        if (u.Rules.PlayerUpkeepFloorActive && u.Actors.TryGetValue(st.Owner, out var po))
                        {
                            bool ok = PlayerUpkeep.Charge(u, st, po, stUpkeep);
                            official[st.Owner.Value] = ok ? (cur.paid + stUpkeep, cur.unpaid) : (cur.paid, cur.unpaid + stUpkeep);
                            st.Credits += type.IncomePerTick * dt;
                            Profitability.Transfer(st.Profit, type.IncomePerTick * dt);
                            continue;
                        }
                    }
                    // Eigenes Hauptquartier: Regierungsstationen des Spielers buchen immer aus der Spielerkasse, notfalls ins Minus (sonst hielt eine leere
                    // Kasse Behörde und Hauptquartier umsonst); KI-Fraktionen unverändert
                    if (u.Actors.TryGetValue(st.Owner, out var owner)
                        && (owner.Treasury.Credits >= stUpkeep || (st.Owner == u.PlayerActor && u.Rules.PlayerGovernmentDebt)))
                    {
                        owner.Treasury.Credits -= stUpkeep;
                        official[st.Owner.Value] = (cur.paid + stUpkeep, cur.unpaid);
                        st.Credits += type.IncomePerTick * dt;
                        Profitability.Transfer(st.Profit, type.IncomePerTick * dt);   // Phase 22: Grundeinkommen ist kein Geschäftsergebnis
                        continue;
                    }
                    float own = Math.Clamp(st.Credits + type.IncomePerTick * dt, 0f, stUpkeep);
                    official[st.Owner.Value] = (cur.paid, cur.unpaid + stUpkeep - own);
                    st.Credits += type.IncomePerTick * dt - own;
                    Profitability.Transfer(st.Profit, type.IncomePerTick * dt);
                    continue;
                }
                float income = PlayerBuilding.IncomePerTick(u, st, type);   // eigene Stationen: Grundeinkommen höchstens der Unterhalt
                st.Credits += (income - type.UpkeepPerTick) * dt;
                // Kapitulation: eine übernommene Station des Spielers zahlt ihren Unterhalt aus ihrer Kasse, was fehlt aus der Spielerkasse (soweit vorhanden)
                if (st.Credits < 0f && st.Owner == u.PlayerActor && u.Actors.TryGetValue(st.Owner, out var pa) && pa.Treasury.Credits > 0f)
                {
                    float take = MathF.Min(-st.Credits, pa.Treasury.Credits);
                    pa.Treasury.Credits -= take;
                    st.Credits += take;
                }
                // Phase 22: das Grundeinkommen des Typs (Geldquelle) zählt nicht zur Rentabilität — eine Station, die nichts verkauft, trägt nur ihren Unterhalt
                Profitability.Transfer(st.Profit, income * dt);
            }
        foreach (var (actor, (paid, unpaid)) in official) u.Events.Publish(new TreasuryUpkeep(new ActorId(actor), "government", paid, unpaid));
        foreach (var st in u.Stations.Values)
            if (!st.IsBuildSite && st.Credits > 0f)
            {
                // Phase 15: das Betriebskapital (WorkingCapital) bleibt steuerfrei — vorher verloren Einkäufer (Exportsenken, Kolonie, Werft) ihre Kasse
                // an die Fraktionskassen, die ≈ 90 % des Geldes horteten
                float taxable = st.Credits - WorkingCapital.Of(u, st);
                if (taxable <= 0f) continue;
                float tax = taxable * u.Rules.StationWealthTax * dt;
                st.Credits -= tax;
                Profitability.Transfer(st.Profit, -tax);   // Phase 22: Vermögensabgabe zählt nicht zum Geschäftsergebnis (sie trifft Reichtum, nicht Verlust)
                Route(u.FactionOf(st), tax);
            }

        float upkeep = u.Rules.ShipUpkeepPerTick * dt;
        foreach (var ship in u.Ships.Values)
            if (ship.IsEconomyAi)
            {
                ship.Credits -= MathF.Min(ship.Credits, upkeep);
                if (ship.Credits > u.Rules.WealthTaxFree)
                {
                    float tax = (ship.Credits - u.Rules.WealthTaxFree) * u.Rules.ShipWealthTax * dt;
                    ship.Credits -= tax;
                    Profitability.Transfer(ship.Profit, -tax);   // Phase 22: Rentabilität = Erlös − Einkauf − Unterhalt, ohne Vermögensabgabe
                    Route(TaxAuthority(u, ship), tax);
                }
            }

        foreach (var (f, amount) in collected) u.Events.Publish(new TaxCollected(new ActorId(f), amount));
        if (sunk > 0f) u.Events.Publish(new TaxSunk(sunk));
    }

    /// <summary>
    /// Phase 15: Steuerbremse je Fraktion (Fraktions-Id → Anteil 0..1 der Vermögensabgabe, den die Kasse behält). Ziel der Kasse ist ihr Anteil
    /// <see cref="EconomyRules.TreasuryTargetShare"/> ÷ Anzahl Fraktionen an der Geldmenge (Stationen, wirtschaftliche KI-Schiffe, Kassen aller Akteure außer
    /// dem Spieler); Faktor = 1 − Kasse ÷ Ziel, begrenzt auf 0..1. Die Zahler zahlen dieselbe Abgabe wie immer; was eine volle Kasse nicht aufnimmt,
    /// verlässt die Welt (<see cref="TaxSunk"/>, die Senke vor Phase 9). Ohne Bremse (Regel 0) horteten die Kassen ≈ 90 % des Geldes, weil ihre Ausgaben
    /// (Regierungsunterhalt, Polizei, Instandhaltung, Subventionen) die Abgaben nie erreichten, und die Geldmenge wuchs linear. Leere Liste = überall 1.
    /// </summary>
    public static Dictionary<int, float> FiscalFactors(Universe u)
    {
        var result = new Dictionary<int, float>();
        var npc = u.NpcFactionIds();   // Runde 38: nur KI-Fraktionen (die Spielerfraktion hat keine Steuern und zählt nicht zur Fraktionszahl)
        if (u.Rules.TreasuryTargetShare <= 0f || npc.Count == 0) return result;
        double money = 0;
        foreach (var st in u.Stations.Values) money += Math.Max(0f, st.Credits);
        foreach (var sh in u.Ships.Values) if (sh.IsEconomyAi) money += Math.Max(0f, sh.Credits);
        foreach (var a in u.Actors.Values) if (a.Id != u.PlayerActor) money += Math.Max(0f, a.Treasury.Credits);
        float target = (float)(money * u.Rules.TreasuryTargetShare / npc.Count);
        foreach (var f in npc)
        {
            float t = u.Actors.TryGetValue(f, out var a) ? a.Treasury.Credits : 0f;
            result[f.Value] = target <= 0f ? 0f : Math.Clamp(1f - t / target, 0f, 1f);
        }
        return result;
    }

    /// <summary>Wer die Abgabe eines Schiffs bekommt: die Fraktion seines Besitzers, für freie Einzelschiffe die Fraktion, der der Sektor gehört.</summary>
    public static FactionId? TaxAuthority(Universe u, Ship ship) =>
        u.FactionOf(ship) is { } own && !u.IsPlayerFaction(own) ? own : (u.Sectors.TryGetValue(ship.Sector, out var sec) && u.IsNpcFaction(sec.Owner) ? sec.Owner : null);
}
