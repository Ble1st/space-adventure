using System.Globalization;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Missions;

/// <summary>
/// Auftragsart <b>Schiff liefern</b> (Lücke gegenüber Interstellar Pilot 2): eine Station mit Schiffshändler (Werft, Handelsstation) will ein Schiff eines
/// bestimmten Modells. Der Spieler bringt ein eigenes passendes Schiff dorthin (angedockt, <b>nicht</b> das aktive — vorher umsteigen; kein Leihschiff, keine
/// Fracht, Hülle mindestens die Hälfte) und liefert es im Auftragsbereich ab (Dienst „missions“, Schlüssel <c>deliver:&lt;id&gt;</c>). Das Schiff geht an die
/// Fraktion (aus der Welt, <see cref="DepartureReason.Sold"/>), die Belohnung = Materialwert des Modells zum Basispreis × <see cref="EconomyRules.MissionShipDeliveryFactor"/>
/// liegt über dem Hangar-Verkaufspreis.
/// </summary>
public sealed class ShipDeliveryMission : IMissionType
{
    public const string KeyId = "shipdelivery";
    public string Key => KeyId;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Gewünschtes Modell (Args „model“).</summary>
    public static string ModelOf(IReadOnlyDictionary<string, string> a) => a.GetValueOrDefault("model") ?? "";

    /// <summary>Materialwert eines Modells (Bausatz + Module zum Basispreis).</summary>
    public static float ModelValue(Universe u, ShipModel model)
    {
        var needs = ShipPurchase.Materials(u, u.ShipClasses.GetValueOrDefault(model.ClassKey), model.Fit);
        return needs?.Sum(n => n.Units * u.Goods[n.Good].BasePrice) ?? 0f;
    }

    public MissionOffer? Generate(Universe u, Station st, DeterministicRng rng, long bucket)
    {
        var r = u.Rules;
        if (r.MissionShipDeliveryChance <= 0f || !u.StationTypes.TryGetValue(st.TypeKey, out var type) || type.ShipDealer is null) return null;
        if (u.FactionOf(st.Owner) is not { } f || !u.IsNpcFaction(f)) return null;
        if (MissionRules.Forbidden(u, st.Owner) || MissionRules.Blocked(u, st.Owner.Value)) return null;
        if (MissionRng.NextFloat(rng) >= r.MissionShipDeliveryChance) return null;
        var models = u.ShipModels.Values.Where(m => m.Jobs.Count > 0 && ShipPurchase.SellsModel(u, st, m)).OrderBy(m => m.Key, StringComparer.Ordinal).ToList();
        if (models.Count == 0) return null;
        var model = models[MissionRng.NextInt(rng, models.Count)];
        float reward = MathF.Round(ModelValue(u, model) * r.MissionShipDeliveryFactor);
        if (reward <= 0f || !u.Actors.TryGetValue(st.Owner, out var client)) return null;
        if (MissionRules.SpentToday(u, st.Owner.Value) + reward > MissionRules.BudgetOf(u, client) + 0.0001f) return null;
        if (FactionViability.Spendable(u, client) < reward - 0.0001f) return null;
        var o = new MissionOffer { Type = KeyId, Reward = reward };
        o.Args["model"] = model.Key;
        o.Args["channel"] = "comms";
        o.Args["contract"] = "0";
        return o;
    }

    public string? AcceptIssue(Universe u, MissionOffer o, Ship ship, bool remote) =>
        u.ShipModels.ContainsKey(ModelOf(o.Args)) ? null : "Unbekanntes Schiffsmodell.";

    public Mission Accept(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        var m = new Mission { Type = KeyId, Client = o.Client, At = o.At, Args = new Dictionary<string, string>(o.Args) };
        m.Deadline = u.Tick + DeadlineOf(u, o, ship);
        return m;
    }

    public void OnEvent(Universe u, Mission m, SimEvent e) { }

    public bool TargetGone(Universe u, Mission m) => !u.Stations.ContainsKey(m.At);

    public (SectorId Sector, Vec2 Pos, string Label, MissionMarkerKind Kind)? Marker(Universe u, Mission m) =>
        u.Stations.TryGetValue(m.At, out var st) ? (st.Sector, st.Position, Describe(u, m), MissionMarkerKind.Station) : null;

    static string ModelName(Universe u, string key) => u.ShipModels.TryGetValue(key, out var md) ? md.Name : key;

    public string DescribeOffer(Universe u, MissionOffer o)
    {
        string home = u.Stations.TryGetValue(o.At, out var st) ? st.Name : "die Station";
        return $"Schiff liefern: ein {ModelName(u, ModelOf(o.Args))} an {home} — Belohnung {o.Reward:0} Cr (über dem Verkaufspreis).";
    }

    public string TargetText(Universe u, MissionOffer o) => u.Stations.TryGetValue(o.At, out var st) ? st.Name : "";

    public long DeadlineOf(Universe u, MissionOffer o, Ship ship) => Math.Max(u.Rules.MissionMinDeadlineTicks, u.Rules.MissionShipDeliveryTicks);

    public string Describe(Universe u, Mission m)
    {
        long left = Math.Max(0L, m.Deadline - u.Tick);
        string home = u.Stations.TryGetValue(m.At, out var st) ? st.Name : "?";
        return $"Auftrag: Schiff liefern — {ModelName(u, ModelOf(m.Args))} an {home} · noch {MissionRules.Rest(left)}";
    }

    public void Check(Universe u, Mission m)
    {
        if (u.Tick >= m.Deadline) { MissionRules.Fail(u, m, "Frist abgelaufen"); return; }
        if (TargetGone(u, m)) MissionRules.Fail(u, m, "Zielstation weg");
    }

    /// <summary>Eigenes, hier angedocktes Schiff, das den Auftrag erfüllt (kleinste Id), oder null.</summary>
    public static Ship? Candidate(Universe u, Mission m, Station st) =>
        u.ShipsById().FirstOrDefault(s => Fits(u, m, s, st) is null);

    /// <summary>Warum dieses Schiff nicht passt (null = passt).</summary>
    static string? Fits(Universe u, Mission m, Ship s, Station st)
    {
        if (!s.IsPlayer || s.DockedAt != st.Id || s.Transit is not null) return "nicht hier";
        if (s.ModelKey != ModelOf(m.Args)) return "falsches Modell";
        if (s.Id == u.PlayerShip) return "aktiv";
        if (s.IsRental) return "Leihschiff";
        if (Derelicts.BreakdownOf(u, s.Id) is not null) return "Pannenschiff";
        if (s.Cargo.Values.Sum() > 0.001f || s.MissionCargo > 0f) return "Fracht";
        if (s.Hull < s.MaxHull * 0.5f) return "beschädigt";
        return null;
    }

    public string? DeliverIssue(Universe u, Mission m, Ship ship, Station st)
    {
        if (st.Id != m.At) return "Falsche Station.";
        if (Candidate(u, m, st) is not null) return null;
        string model = ModelName(u, ModelOf(m.Args));
        var here = u.ShipsById().Where(s => s.IsPlayer && s.DockedAt == st.Id && s.ModelKey == ModelOf(m.Args)).ToList();
        if (here.Count == 0) return $"Kein eigener {model} hier angedockt.";
        var why = Fits(u, m, here[0], st);
        return why switch
        {
            "aktiv" => $"Du sitzt im {model} — erst in ein anderes Schiff umsteigen (Hangar).",
            "Leihschiff" => "Leihschiffe gehören dir nicht.",
            "Pannenschiff" => "Das ist ein Pannenschiff der Fraktion.",
            "Fracht" => "Erst die Fracht verkaufen oder einlagern.",
            "beschädigt" => "Erst reparieren (mindestens halbe Hülle).",
            _ => $"Kein passender {model} hier.",
        };
    }

    public void Deliver(Universe u, Mission m, Ship ship, Station st)
    {
        if (Candidate(u, m, st) is not { } s) return;
        MissionRules.Complete(u, m);
        Departures.RemoveShip(u, s, DepartureReason.Sold, "delivered");
    }
}
