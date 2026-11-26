using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Welle E: ein Bauauftrag einer KI-Fraktion für ein Großkampfschiff an einer Werft. <see cref="Need"/> = Material des Modells (Bausätze + Modulwaren),
/// <see cref="Reserved"/> = schon aus dem Werftlager genommen, <see cref="Raised"/> = die zur Zeit auf die Regalziele aufgeschlagene Menge (wird bei jedem
/// Schritt neu gesetzt und beim Ende wieder abgezogen), <see cref="Escrow"/> = noch unbezahlte Treuhand der Fraktion.
/// </summary>
public sealed class CapitalOrder
{
    public int Id;
    public FactionId Faction;
    public StationId Yard;
    public string ModelKey = "";
    public long Placed;
    public long Expires;
    /// <summary>Vertragspreis je Basispreis-Credit (Montagegebühr × Preisfaktor), beim Auftrag festgelegt.</summary>
    public float UnitFactor;
    public float Escrow;
    /// <summary>Beginn der Montage (−1 = Material noch nicht vollständig).</summary>
    public long AssemblyStart = -1;
    public SortedDictionary<int, float> Need = new();
    public SortedDictionary<int, float> Reserved = new();
    public SortedDictionary<int, float> Raised = new();
    /// <summary>Lagereinträge, die der Auftrag erst angelegt hat (werden beim Ende wieder entfernt, wenn leer).</summary>
    public SortedSet<int> Created = new();

    public float NeedTotal => Need.Values.Sum();
    public float ReservedTotal => Reserved.Values.Sum();
    /// <summary>Materialanteil 0..1.</summary>
    public float MaterialShare => NeedTotal <= 0f ? 1f : MathF.Min(1f, ReservedTotal / NeedTotal);
}

/// <summary>Welle E: Zustand aller Bauaufträge (null auf <see cref="Universe.CapitalOrders"/> = nie benutzt).</summary>
public sealed class CapitalOrderState
{
    public List<CapitalOrder> Orders = new();
    /// <summary>Fraktion → Tick des letzten Auftrags (Abklingzeit).</summary>
    public SortedDictionary<int, long> LastOrder = new();
    public int NextId = 1;

    public static CapitalOrderState Of(Universe u) => u.CapitalOrders ??= new CapitalOrderState();
}

/// <summary>Welle E: Auftrag erteilt (Fraktion, Werft, Modell, Treuhand, Grund war/threat/rich).</summary>
public sealed record CapitalOrderPlaced(FactionId Faction, StationId Yard, string Model, float Escrow, string Reason) : SimEvent;
/// <summary>Welle E: Großkampfschiff fertig (Fraktion, Schiff, Werft, Modell, Preis).</summary>
public sealed record CapitalShipCompleted(FactionId Faction, ShipId Ship, StationId Yard, string Model, float Price, string Text) : SimEvent;
/// <summary>Welle E: Auftrag abgebrochen (Verfall, Werft verloren) — Rückerstattung des unbezahlten Rests.</summary>
public sealed record CapitalOrderEnded(FactionId Faction, StationId Yard, string Model, string Reason, float Refund) : SimEvent;

/// <summary>
/// Welle E: Regeln der Großkampfschiff-Aufträge (rein bis auf <see cref="Step"/>). Keine Zufallszahl, feste Reihenfolge (Fraktions-Id, Werft-Id, Waren-Id).
/// Geld fließt nur Kasse → Treuhand → Werft (je reservierter Einheit der Vertragspreis) bzw. Treuhand → Kasse (Rückerstattung); es entsteht keins.
/// </summary>
public static class CapitalOrders
{
    /// <summary>Ist dieses Schiff ein Großkampfschiff (KI-Kriegsschiff eines Modells aus <see cref="EconomyRules.CapitalModels"/>)?</summary>
    public static bool IsCapital(Universe u, Ship s) => Wars.IsWarship(s) && u.Rules.CapitalModels.Contains(s.ModelKey);

    /// <summary>Großkampfschiffe einer Fraktion plus ihre laufenden Aufträge.</summary>
    public static int CountOf(Universe u, FactionId f)
    {
        int n = 0;
        foreach (var s in u.ShipsById()) if (IsCapital(u, s) && u.FactionOf(s) == f) n++;
        if (u.CapitalOrders is { } st) foreach (var o in st.Orders) if (o.Faction == f) n++;
        return n;
    }

    /// <summary>Laufender Auftrag an dieser Werft (der erste), sonst null.</summary>
    public static CapitalOrder? AtYard(Universe u, StationId yard) => u.CapitalOrders?.Orders.FirstOrDefault(o => o.Yard == yard);

    /// <summary>Höchste Bedrohung im Gebiet der Fraktion.</summary>
    public static float MaxThreat(Universe u, FactionId f)
    {
        if (!u.Factions.TryGetValue(f, out var fac)) return 0f;
        float m = 0f;
        foreach (var s in fac.Territory) if (u.Sectors.TryGetValue(s, out var sec)) m = MathF.Max(m, sec.Threat);
        return m;
    }

    /// <summary>Vertragspreis eines Modells an einer Werft (fester Preis über den Basispreisen).</summary>
    public static float PriceOf(Universe u, Station yard, IReadOnlyList<MaterialNeed> needs) =>
        needs.Sum(n => n.Units * u.Goods[n.Good].BasePrice) * UnitFactorOf(u, yard);

    static float UnitFactorOf(Universe u, Station yard) => (1f + MathF.Max(0f, ShipPurchase.AssemblyOf(u, yard))) * MathF.Max(1f, u.Rules.CapitalPriceFactor);

    /// <summary>Darf die Fraktion an dieser Werft bauen lassen? Eigene oder neutrale (kein Krieg, keine feindliche Beziehung), fertig, nicht außer Gefecht.</summary>
    public static bool YardUsable(Universe u, FactionId f, Station st)
    {
        if (st.IsBuildSite || st.Condition is { Disabled: true }) return false;
        if (u.FactionOf(st) is not { } owner || owner == f) return true;
        if (!u.IsNpcFaction(owner) && !u.IsPlayerFaction(owner)) return false;
        if (Wars.AtWar(u, f, owner)) return false;
        return Relations.Effective(u, new ActorId(f.Value), new ActorId(owner.Value)) > u.Rules.HostileRelation;
    }

    /// <summary>Warum gibt die Fraktion einen Auftrag (war/threat/rich) — null = keiner der Auslöser.</summary>
    public static string? Trigger(Universe u, FactionId f, Actor a, float price)
    {
        var r = u.Rules;
        if (Wars.CountOf(u, f) > 0) return "war";
        if (r.CapitalThreat > 0f && MaxThreat(u, f) >= r.CapitalThreat) return "threat";
        if (r.CapitalRichFactor > 0f && FactionViability.Spendable(u, a) >= price * r.CapitalRichFactor) return "rich";
        return null;
    }

    /// <summary>
    /// Auftrag für eine Fraktion, wenn alle Bedingungen gelten: Schalter, keine laufender Auftrag, Abklingzeit vorbei, unter der Obergrenze, ein Auslöser,
    /// eine erreichbare nutzbare Werft für das stärkste bezahlbare Modell, Treuhand ≤ verfügbares Geld (nie unter die Rücklage). null = keiner.
    /// </summary>
    public static CapitalOrder? TryPlace(Universe u, FactionId f)
    {
        var r = u.Rules;
        if (!r.CapitalOrders || !r.Warfare || r.CapitalModels.Count == 0 || !Wars.CanWage(u, f) || !u.Actors.TryGetValue(f, out var a)) return null;
        if (u.Tick < a.LastForceTick) return null;   // Startszenario-Sperre wie bei der Marine
        var state = u.CapitalOrders;
        if (state is not null && state.Orders.Any(o => o.Faction == f)) return null;
        if (state is not null && state.LastOrder.TryGetValue(f.Value, out var last) && u.Tick - last < r.CapitalOrderCooldownTicks) return null;
        if (CountOf(u, f) >= r.MaxCapitalShipsPerFaction) return null;
        if (SecuritySystem.HomeBaseFor(u, f, null) is not { } home) return null;
        float spend = FactionViability.Spendable(u, a);
        // stärkstes Modell zuerst (Kampfstärke des Werks-Fits), dann Liste
        foreach (var key in r.CapitalModels)
        {
            if (!u.ShipModels.TryGetValue(key, out var model) || ShipPurchase.Materials(u, model) is not { } needs) continue;
            Station? best = null; int bestHops = int.MaxValue; bool bestOwn = false;
            foreach (var st in u.StationsById())
            {
                if (!ShipPurchase.SellsModel(u, st, model) || !YardUsable(u, f, st)) continue;
                int hops = u.Hops(home.Sector, st.Sector);
                if (hops < 0) continue;
                bool own = u.FactionOf(st) == f;
                if (best is null || (own && !bestOwn) || (own == bestOwn && hops < bestHops)) { best = st; bestHops = hops; bestOwn = own; }
            }
            if (best is null) continue;
            float price = PriceOf(u, best, needs);
            if (price > spend) continue;
            if (Trigger(u, f, a, price) is not { } why) return null;
            var o = new CapitalOrder
            {
                Faction = f, Yard = best.Id, ModelKey = key, Placed = u.Tick, Expires = u.Tick + r.CapitalOrderExpiryTicks,
                UnitFactor = UnitFactorOf(u, best), Escrow = price,
            };
            foreach (var n in needs) o.Need[n.Good.Value] = n.Units;
            state = CapitalOrderState.Of(u);
            o.Id = state.NextId++;
            state.Orders.Add(o);
            state.LastOrder[f.Value] = u.Tick;
            a.Treasury.Credits -= price;
            u.Events.Publish(new TreasuryUpkeep(a.Id, "capital_order", price, 0f));
            u.Events.Publish(new CapitalOrderPlaced(f, best.Id, key, price, why));
            Raise(u, best, o);
            return o;
        }
        return null;
    }

    /// <summary>Setzt die Regalziele der Werft auf Grundziel + noch fehlenden Bedarf (alter Aufschlag wird erst abgezogen). Idempotent.</summary>
    static void Raise(Universe u, Station yard, CapitalOrder o)
    {
        foreach (var (g, need) in o.Need)
        {
            var gid = new GoodId(g);
            float want = o.AssemblyStart >= 0 ? 0f : MathF.Max(0f, need - o.Reserved.GetValueOrDefault(g));
            float old = o.Raised.GetValueOrDefault(g);
            if (MathF.Abs(want - old) < 1e-4f) continue;
            if (!yard.Market.TryGetValue(gid, out var e))
            {
                if (want <= 0f) continue;
                e = new StockEntry { Amount = 0f, Target = 0f, Price = u.Goods[gid].BasePrice };
                yard.Market[gid] = e;
                o.Created.Add(g);
            }
            e.Target = MathF.Max(0f, e.Target - old + want);
            if (want > 0f) o.Raised[g] = want; else o.Raised.Remove(g);
        }
    }

    /// <summary>Zieht alle Aufschläge ab und entfernt selbst angelegte, leere Einträge.</summary>
    static void Lower(Universe u, Station? yard, CapitalOrder o)
    {
        if (yard is not null)
        {
            foreach (var (g, old) in o.Raised)
                if (yard.Market.TryGetValue(new GoodId(g), out var e))
                    e.Target = MathF.Max(o.Created.Contains(g) ? 0f : IdleShelfSystem.Floor(u, yard, new GoodId(g)), e.Target - old);   // nie unter die Regal-Untergrenze (Ausbau Punkt 12 / Welle D)
            foreach (var g in o.Created)
                if (yard.Market.TryGetValue(new GoodId(g), out var e) && e.Amount <= 0f && e.Target <= 0f) yard.Market.Remove(new GoodId(g));
        }
        o.Raised.Clear();
        o.Created.Clear();
    }

    /// <summary>Beendet einen Auftrag ohne Schiff: reservierte Ware zurück ins Werftlager (bezahlt ist bezahlt), unbezahlter Rest zurück in die Kasse.</summary>
    public static void End(Universe u, CapitalOrder o, string reason)
    {
        var yard = u.Stations.GetValueOrDefault(o.Yard);
        if (yard is not null)
            foreach (var (g, units) in o.Reserved)
                if (units > 0f && yard.Market.TryGetValue(new GoodId(g), out var e)) e.Amount += units;
        Lower(u, yard, o);
        float refund = MathF.Max(0f, o.Escrow);
        o.Escrow = 0f;
        if (refund > 0f && u.Actors.TryGetValue(o.Faction, out var a))
        {
            a.Treasury.Credits += refund;
            u.Events.Publish(new TreasuryIncome(a.Id, "capital_refund", refund));
        }
        u.CapitalOrders?.Orders.Remove(o);
        u.Events.Publish(new CapitalOrderEnded(o.Faction, o.Yard, o.ModelKey, reason, refund));
    }

    /// <summary>Ein Durchlauf: neue Aufträge (Fraktions-Id-Reihenfolge), dann jeder laufende Auftrag (Reservieren, Ziele, Montage, Verfall).</summary>
    public static void Step(Universe u)
    {
        if (!u.Rules.CapitalOrders) return;
        foreach (var f in u.NpcFactionIds()) TryPlace(u, f);
        if (u.CapitalOrders is not { } state) return;
        foreach (var o in state.Orders.ToList()) Advance(u, o);
    }

    static void Advance(Universe u, CapitalOrder o)
    {
        if (!u.Stations.TryGetValue(o.Yard, out var yard) || !YardUsable(u, o.Faction, yard) || !u.Actors.ContainsKey(o.Faction))
        { End(u, o, "yard_lost"); return; }
        if (o.AssemblyStart < 0)
        {
            if (u.Tick >= o.Expires) { End(u, o, "expired"); return; }
            bool complete = true;
            foreach (var (g, need) in o.Need)
            {
                float have = o.Reserved.GetValueOrDefault(g);
                float miss = need - have;
                if (miss <= 1e-4f) continue;
                var gid = new GoodId(g);
                if (yard.Market.TryGetValue(gid, out var e))
                {
                    float take = MathF.Min(miss, MathF.Floor(e.Amount + 1e-4f));
                    if (take > 0f)
                    {
                        e.Amount -= take;
                        o.Reserved[g] = have + take;
                        float pay = MathF.Min(o.Escrow, take * u.Goods[gid].BasePrice * o.UnitFactor);
                        o.Escrow -= pay;
                        yard.Credits += pay;
                        miss -= take;
                    }
                }
                if (miss > 1e-4f) complete = false;
            }
            if (complete) o.AssemblyStart = u.Tick;
            Raise(u, yard, o);
            return;
        }
        if (u.Tick - o.AssemblyStart < u.Rules.CapitalAssemblyTicks) return;
        Complete(u, o, yard);
    }

    static void Complete(Universe u, CapitalOrder o, Station yard)
    {
        Lower(u, yard, o);
        if (o.Escrow > 0f) { yard.Credits += o.Escrow; o.Escrow = 0f; }
        u.CapitalOrders?.Orders.Remove(o);
        if (!u.ShipModels.TryGetValue(o.ModelKey, out var model) || !u.ShipClasses.TryGetValue(model.ClassKey, out var cls)
            || !u.Actors.TryGetValue(o.Faction, out var a)) return;
        var ship = ShipPurchase.Create(u, u.NewShipId(), model.Key, cls.Slots, model.Fit, ShipRole.Warship, Controller.Ai, a.Id, yard, 0f);
        ship.HomeBase = SecuritySystem.HomeBaseFor(u, o.Faction, null)?.Id ?? (u.FactionOf(yard) == o.Faction ? yard.Id : default);
        ship.JobSince = u.Tick;
        u.AddShip(ship);
        if (Combat.Carriers.IsCarrier(ship)) Combat.Carriers.EquipNpc(u, ship, a);
        float price = PriceOf(u, yard, ShipPurchase.Materials(u, model) ?? Array.Empty<MaterialNeed>());
        string text = $"{(model.Name.Length > 0 ? model.Name : model.Key)} für {ActorNames.Tagged(u, a.Id)} an {yard.Name} fertig.";
        u.Events.Publish(new CapitalShipCompleted(o.Faction, ship.Id, yard.Id, model.Key, price, text));
    }

    /// <summary>Stationsinfo-Zeile der Werft: „Bauauftrag: Leviathan für [X] — Material n %“ bzw. „… Montage n %“. null = kein Auftrag.</summary>
    public static string? InfoLine(Universe u, Station st)
    {
        if (AtYard(u, st.Id) is not { } o) return null;
        string name = u.ShipModels.TryGetValue(o.ModelKey, out var m) && m.Name.Length > 0 ? m.Name : o.ModelKey;
        string who = ActorNames.Tagged(u, new ActorId(o.Faction.Value));
        if (o.AssemblyStart >= 0)
        {
            int pct = (int)MathF.Round(100f * MathF.Min(1f, (u.Tick - o.AssemblyStart) / MathF.Max(1f, u.Rules.CapitalAssemblyTicks)));
            return $"Bauauftrag: {name} für {who} — Montage {pct} %";
        }
        return $"Bauauftrag: {name} für {who} — Material {(int)MathF.Floor(100f * o.MaterialShare)} %";
    }
}

/// <summary>Welle E: <see cref="CapitalOrders"/> im Sicherheitsmodul nach der Marine (Intervall 50). Ohne Regel nichts.</summary>
public sealed class CapitalOrderSystem : ISimSystem
{
    public const int Interval = 50;
    public void Step(Universe u, int dt) => CapitalOrders.Step(u);
}

/// <summary>Welle E: Speichern/Laden/Hash (additiv, v17 bleibt; gehasht nur wenn vorhanden).</summary>
public sealed class CapitalOrderSave
{
    public int NextId { get; set; }
    public Dictionary<int, long>? LastOrder { get; set; }
    public List<CapitalOrderEntrySave>? Orders { get; set; }
}

public sealed class CapitalOrderEntrySave
{
    public int Id { get; set; }
    public int Faction { get; set; }
    public int Yard { get; set; }
    public string Model { get; set; } = "";
    public long Placed { get; set; }
    public long Expires { get; set; }
    public float UnitFactor { get; set; }
    public float Escrow { get; set; }
    public long AssemblyStart { get; set; } = -1;
    public Dictionary<int, float>? Need { get; set; }
    public Dictionary<int, float>? Reserved { get; set; }
    public Dictionary<int, float>? Raised { get; set; }
    public List<int>? Created { get; set; }
}

public static class CapitalOrderCodec
{
    public static void Save(Universe u, Persistence.SaveGame s)
    {
        if (u.CapitalOrders is not { } st) return;
        s.CapitalOrders = new CapitalOrderSave
        {
            NextId = st.NextId,
            LastOrder = st.LastOrder.Count > 0 ? new Dictionary<int, long>(st.LastOrder) : null,
            Orders = st.Orders.Count == 0 ? null : st.Orders.Select(o => new CapitalOrderEntrySave
            {
                Id = o.Id, Faction = o.Faction.Value, Yard = o.Yard.Value, Model = o.ModelKey, Placed = o.Placed, Expires = o.Expires,
                UnitFactor = o.UnitFactor, Escrow = o.Escrow, AssemblyStart = o.AssemblyStart,
                Need = new Dictionary<int, float>(o.Need), Reserved = o.Reserved.Count > 0 ? new Dictionary<int, float>(o.Reserved) : null,
                Raised = o.Raised.Count > 0 ? new Dictionary<int, float>(o.Raised) : null, Created = o.Created.Count > 0 ? o.Created.ToList() : null,
            }).ToList(),
        };
    }

    public static void Load(Persistence.SaveGame s, Universe u)
    {
        if (s.CapitalOrders is not { } w) return;
        var st = new CapitalOrderState { NextId = Math.Max(1, w.NextId) };
        if (w.LastOrder is { } lo) foreach (var (k, v) in lo) st.LastOrder[k] = v;
        if (w.Orders is { } os)
            foreach (var e in os)
            {
                var o = new CapitalOrder
                {
                    Id = e.Id, Faction = new FactionId(e.Faction), Yard = new StationId(e.Yard), ModelKey = e.Model, Placed = e.Placed, Expires = e.Expires,
                    UnitFactor = e.UnitFactor, Escrow = e.Escrow, AssemblyStart = e.AssemblyStart,
                };
                if (e.Need is { } n) foreach (var (k, v) in n) o.Need[k] = v;
                if (e.Reserved is { } r) foreach (var (k, v) in r) o.Reserved[k] = v;
                if (e.Raised is { } ra) foreach (var (k, v) in ra) o.Raised[k] = v;
                if (e.Created is { } c) foreach (var g in c) o.Created.Add(g);
                st.Orders.Add(o);
            }
        u.CapitalOrders = st;
    }

    public static void Mix(Universe u, Action<ulong> mix, Action<float> mixF)
    {
        if (u.CapitalOrders is not { } st) return;
        mix(0xE001u); mix((ulong)st.NextId);
        foreach (var (k, v) in st.LastOrder) { mix(0xE002u); mix((ulong)k); mix((ulong)v); }
        foreach (var o in st.Orders)
        {
            mix(0xE003u); mix((ulong)o.Id); mix((ulong)o.Faction.Value); mix((ulong)o.Yard.Value); mix((ulong)o.Placed); mix((ulong)o.Expires);
            mixF(o.UnitFactor); mixF(o.Escrow); mix((ulong)o.AssemblyStart);
            foreach (var (k, v) in o.Need) { mix((ulong)k); mixF(v); }
            mix(0xE004u);
            foreach (var (k, v) in o.Reserved) { mix((ulong)k); mixF(v); }
            mix(0xE005u);
            foreach (var (k, v) in o.Raised) { mix((ulong)k); mixF(v); }
            mix(0xE006u);
            foreach (var g in o.Created) mix((ulong)g);
        }
    }
}
