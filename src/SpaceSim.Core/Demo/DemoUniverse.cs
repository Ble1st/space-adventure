using SpaceSim.Core.Model;

namespace SpaceSim.Core.Demo;

/// <summary>Kleines Testuniversum: drei Sektoren in einer Kette (A - B - C), zwei Waren, drei Stationen, drei Händler.</summary>
public static class DemoUniverse
{
    public static readonly GoodId Ore = new(1);
    public static readonly GoodId Metal = new(2);

    public static Universe Create()
    {
        var u = new Universe();
        u.Goods[Ore] = new Good { Id = Ore, Name = "Erz", BasePrice = 10f };
        u.Goods[Metal] = new Good { Id = Metal, Name = "Metall", BasePrice = 25f };

        var union = new Faction { Id = new(1), Name = "Terranische Union" };
        var traders = new Faction { Id = new(2), Name = "Freie Händler" };
        u.Factions[union.Id] = union;
        u.Factions[traders.Id] = traders;

        var a = AddSector(u, 1, "Sektor Alpha", union.Id);
        var b = AddSector(u, 2, "Sektor Beta", union.Id);
        var c = AddSector(u, 3, "Sektor Gamma", traders.Id);

        AddGatePair(u, new GateId(1), a, new Vec2(90, -70), new GateId(2), b, new Vec2(-90, 0));
        AddGatePair(u, new GateId(3), b, new Vec2(90, 30), new GateId(4), c, new Vec2(-90, -30));

        a.Fields.Add(new ResourceField { Good = Ore, Position = new Vec2(60, 60), Amount = 3000, Capacity = 3000, RegenPerTick = 0.5f });
        a.Fields.Add(new ResourceField { Good = Ore, Position = new Vec2(-80, 80), Amount = 3000, Capacity = 3000, RegenPerTick = 0.5f });

        // Mine: erzeugt Erz, verbraucht etwas Metall
        var mine = AddStation(u, 1, "Erzmine Alpha", a, union.Id, new Vec2(-60, -40));
        mine.Production.Add(new Recipe { Outputs = { [Ore] = 2f }, TicksPerCycle = 5 });
        mine.Production.Add(new Recipe { Inputs = { [Metal] = 1f }, TicksPerCycle = 10 });
        Stock(mine, Ore, amount: 300, target: 150);
        Stock(mine, Metal, amount: 20, target: 60);

        // Schmelze: Erz -> Metall
        var smelter = AddStation(u, 2, "Schmelze Beta", b, union.Id, new Vec2(0, -50));
        smelter.Production.Add(new Recipe
        {
            Inputs = { [Ore] = 6f }, Outputs = { [Metal] = 3f }, TicksPerCycle = 5,
        });
        Stock(smelter, Ore, amount: 50, target: 150);
        Stock(smelter, Metal, amount: 10, target: 80);

        // Werft: verbraucht Metall
        var yard = AddStation(u, 3, "Werft Gamma", c, traders.Id, new Vec2(40, 40));
        yard.Production.Add(new Recipe { Inputs = { [Metal] = 2f }, TicksPerCycle = 10 });
        Stock(yard, Ore, amount: 0, target: 20);
        Stock(yard, Metal, amount: 0, target: 100);

        for (int i = 1; i <= 3; i++)
        {
            var home = i == 3 ? c : (i == 2 ? b : a);
            AddShip(u, i, ShipRole.Trader, i == 3 ? traders.Id : union.Id, home, credits: 1000);
        }

        for (int i = 4; i <= 5; i++)
            AddShip(u, i, ShipRole.Miner, union.Id, a, credits: 1000).CargoCapacity = 60f;

        foreach (var s in u.Sectors.Values) AsteroidGenerator.Populate(u, s, seed: s.Id.Value * 7919);

        var player = AddShip(u, 100, ShipRole.Player, union.Id, a, credits: 500);
        u.PlayerShip = player.Id;
        u.PlayerSector = a.Id;
        return u;
    }

    static Sector AddSector(Universe u, int id, string name, FactionId owner)
    {
        var s = new Sector { Id = new(id), Name = name, Owner = owner };
        u.Sectors[s.Id] = s;
        u.Factions[owner].Territory.Add(s.Id);
        return s;
    }

    static void AddGatePair(Universe u, GateId idA, Sector a, Vec2 posA, GateId idB, Sector b, Vec2 posB)
    {
        var ga = new Gate { Id = idA, Sector = a.Id, Position = posA, Target = idB };
        var gb = new Gate { Id = idB, Sector = b.Id, Position = posB, Target = idA };
        u.Gates[ga.Id] = ga; u.Gates[gb.Id] = gb;
        a.Gates.Add(ga.Id); b.Gates.Add(gb.Id);
    }

    static Station AddStation(Universe u, int id, string name, Sector sector, FactionId owner, Vec2 pos)
    {
        var st = new Station { Id = new(id), Name = name, Sector = sector.Id, Owner = owner, Position = pos, Credits = 10_000 };
        u.Stations[st.Id] = st;
        sector.Stations.Add(st.Id);
        return st;
    }

    static void Stock(Station st, GoodId good, float amount, float target)
    {
        var e = st.Stock(good);
        e.Amount = amount;
        e.Target = target;
    }

    static Ship AddShip(Universe u, int id, ShipRole role, FactionId faction, Sector sector, float credits)
    {
        var ship = new Ship
        {
            Id = new(id), Role = role, Faction = faction, Credits = credits, Sector = sector.Id,
            Position = sector.Stations.Count > 0 ? u.Stations[sector.Stations[0]].Position : default,
        };
        u.AddShip(ship);
        return ship;
    }
}
