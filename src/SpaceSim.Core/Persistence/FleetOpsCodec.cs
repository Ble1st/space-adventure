using SpaceSim.Core.Model;

namespace SpaceSim.Core.Persistence;

/// <summary>Flottenbefehle (Punkt 1/1b) im erweiterten <see cref="OrderSave"/> — additiv, Save bleibt v17. null = kein Flottenbefehl.</summary>
public static class FleetOpsCodec
{
    public static OrderSave? ToSave(ShipCommand c) => c switch
    {
        AttackCommand a => new OrderSave { Kind = CommandKinds.Attack, A = a.Ship.Value, B = a.Station.Value, C = a.Gate.Value, Ids = new List<int> { a.Sector.Value } },
        SalvageCommand s => new OrderSave { Kind = CommandKinds.Salvage, A = s.Sector.Value },
        BlockadeCommand b => new OrderSave { Kind = CommandKinds.Blockade, A = b.Gate.Value, B = b.Faction.Value },
        DeliverCommand d => new OrderSave { Kind = CommandKinds.Deliver, A = d.From.Value, B = d.To.Value, C = d.Good.Value, Values = new List<float> { d.Amount, d.MaxBuyFactor } },
        RallyCommand r => new OrderSave { Kind = CommandKinds.Rally, A = r.At.Value, B = r.Leader.Value },
        ConvoyCommand v => UniverseSerializer.LimitsSave(CommandKinds.Convoy, v.Limits),
        ExploreSectorCommand e => new OrderSave { Kind = CommandKinds.ExploreSector, A = e.Sector.Value },   // Ausbau Punkt 16
        ExploreUniverseCommand => new OrderSave { Kind = CommandKinds.ExploreUniverse },
        _ => null,
    };

    public static ShipCommand? FromSave(OrderSave o) => o.Kind switch
    {
        CommandKinds.Attack => new AttackCommand(new ShipId(o.A), new StationId(o.B), new GateId(o.C), new SectorId(o.Ids?.ElementAtOrDefault(0) ?? 0)),
        CommandKinds.Salvage => new SalvageCommand(new SectorId(o.A)),
        CommandKinds.Blockade => new BlockadeCommand(new GateId(o.A), new FactionId(o.B)),
        CommandKinds.Deliver => new DeliverCommand(new StationId(o.A), new StationId(o.B), new GoodId(o.C), o.Values?.ElementAtOrDefault(0) ?? 0f, o.Values?.ElementAtOrDefault(1) ?? 0f),
        CommandKinds.Rally => new RallyCommand(new StationId(o.A), new ShipId(o.B)),
        CommandKinds.Convoy => new ConvoyCommand(UniverseSerializer.LimitsFrom(o)),
        CommandKinds.ExploreSector => new ExploreSectorCommand(new SectorId(o.A)),
        CommandKinds.ExploreUniverse => new ExploreUniverseCommand(),
        _ => null,
    };
}
