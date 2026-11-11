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
        void Route(FactionId? authority, float tax)
        {
            if (tax <= 0f) return;
            float share = Math.Clamp(u.Rules.TaxToTreasuryShare, 0f, 1f);
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
                if (type.Government != GovernmentKind.None && stUpkeep > 0f)
                {
                    var cur = official.GetValueOrDefault(st.Owner.Value);
                    if (u.Actors.TryGetValue(st.Owner, out var owner) && owner.Treasury.Credits >= stUpkeep)
                    {
                        owner.Treasury.Credits -= stUpkeep;
                        official[st.Owner.Value] = (cur.paid + stUpkeep, cur.unpaid);
                        st.Credits += type.IncomePerTick * dt;
                        continue;
                    }
                    float own = Math.Clamp(st.Credits + type.IncomePerTick * dt, 0f, stUpkeep);
                    official[st.Owner.Value] = (cur.paid, cur.unpaid + stUpkeep - own);
                    st.Credits += type.IncomePerTick * dt - own;
                    continue;
                }
                st.Credits += (type.IncomePerTick - type.UpkeepPerTick) * dt;
            }
        foreach (var (actor, (paid, unpaid)) in official) u.Events.Publish(new TreasuryUpkeep(new ActorId(actor), "government", paid, unpaid));
        foreach (var st in u.Stations.Values)
            if (!st.IsBuildSite && st.Credits > 0f)
            {
                float tax = st.Credits * u.Rules.StationWealthTax * dt;
                st.Credits -= tax;
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
                    Route(TaxAuthority(u, ship), tax);
                }
            }

        foreach (var (f, amount) in collected) u.Events.Publish(new TaxCollected(new ActorId(f), amount));
        if (sunk > 0f) u.Events.Publish(new TaxSunk(sunk));
    }

    /// <summary>Wer die Abgabe eines Schiffs bekommt: die Fraktion seines Besitzers, für freie Einzelschiffe die Fraktion, der der Sektor gehört.</summary>
    public static FactionId? TaxAuthority(Universe u, Ship ship) =>
        u.FactionOf(ship) ?? (u.Sectors.TryGetValue(ship.Sector, out var sec) && u.Factions.ContainsKey(sec.Owner) ? sec.Owner : null);
}
