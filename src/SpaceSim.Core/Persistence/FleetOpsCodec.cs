using SpaceSim.Core.Model;

namespace SpaceSim.Core.Persistence;

/// <summary>Flottenbefehle (Punkt 1/1b) im erweiterten <see cref="OrderSave"/> — additiv, Save bleibt v17. null = kein Flottenbefehl.</summary>
public static class FleetOpsCodec
{
    public static OrderSave? ToSave(ShipCommand c) => c switch
    {
        AttackCommand a => new OrderSave { Kind = CommandKinds.Attack, A = a.Ship.Value, B = a.Station.Value, C = a.Gate.Value, Ids = new List<int> { a.Sector.Value },
            Values = a.War || a.Reported ? new List<float> { a.War ? 1f : 0f, a.Reported ? 1f : 0f } : null },   // Welle A: nur wenn gesetzt
        SalvageCommand s => new OrderSave { Kind = CommandKinds.Salvage, A = s.Sector.Value },
        BlockadeCommand b => new OrderSave { Kind = CommandKinds.Blockade, A = b.Gate.Value, B = b.Faction.Value },
        DeliverCommand d => new OrderSave { Kind = CommandKinds.Deliver, A = d.From.Value, B = d.To.Value, C = d.Good.Value, Values = d.AllowLoss ? new List<float> { d.Amount, d.MaxBuyFactor, 1f } : new List<float> { d.Amount, d.MaxBuyFactor } },   // 1c: dritter Wert nur bei erlaubtem Verlust
        RallyCommand r => new OrderSave { Kind = CommandKinds.Rally, A = r.At.Value, B = r.Leader.Value, C = r.Until },   // 1c: Frist (0 = keine)
        ConvoyCommand v => UniverseSerializer.LimitsSave(CommandKinds.Convoy, v.Limits),
        ExploreSectorCommand e => new OrderSave { Kind = CommandKinds.ExploreSector, A = e.Sector.Value },   // Ausbau Punkt 16
        ExploreUniverseCommand => new OrderSave { Kind = CommandKinds.ExploreUniverse },
        TaxiCommand t => Taxi(t),   // Ausbau Punkt 14
        _ => null,
    };

    public static ShipCommand? FromSave(OrderSave o) => o.Kind switch
    {
        CommandKinds.Attack => new AttackCommand(new ShipId(o.A), new StationId(o.B), new GateId(o.C), new SectorId(o.Ids?.ElementAtOrDefault(0) ?? 0),
            (o.Values?.ElementAtOrDefault(0) ?? 0f) > 0.5f, (o.Values?.ElementAtOrDefault(1) ?? 0f) > 0.5f),
        CommandKinds.Salvage => new SalvageCommand(new SectorId(o.A)),
        CommandKinds.Blockade => new BlockadeCommand(new GateId(o.A), new FactionId(o.B)),
        CommandKinds.Deliver => new DeliverCommand(new StationId(o.A), new StationId(o.B), new GoodId(o.C), o.Values?.ElementAtOrDefault(0) ?? 0f, o.Values?.ElementAtOrDefault(1) ?? 0f, (o.Values?.ElementAtOrDefault(2) ?? 0f) > 0.5f),
        CommandKinds.Rally => new RallyCommand(new StationId(o.A), new ShipId(o.B), o.C),
        CommandKinds.Convoy => new ConvoyCommand(UniverseSerializer.LimitsFrom(o)),
        CommandKinds.ExploreSector => new ExploreSectorCommand(new SectorId(o.A)),
        CommandKinds.ExploreUniverse => new ExploreUniverseCommand(),
        CommandKinds.Taxi => new TaxiCommand(UniverseSerializer.LimitsFrom(o), new StationId(o.Ids?.ElementAtOrDefault(0) ?? 0), new StationId(o.Ids?.ElementAtOrDefault(1) ?? 0), o.Ids?.ElementAtOrDefault(2) ?? 0),
        _ => null,
    };

    static OrderSave Taxi(TaxiCommand t)
    {
        var o = UniverseSerializer.LimitsSave(CommandKinds.Taxi, t.Limits);
        if (t.Dest.Value != 0 || t.From.Value != 0 || t.Pax != 0) o.Ids = new List<int> { t.Dest.Value, t.From.Value, t.Pax };
        return o;
    }
}
