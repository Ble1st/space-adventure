using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.PlayerBot;

/// <summary>Testwerkzeug: Baufenster-Katalog und Regeln als Tabelle (nur lesen).</summary>
public static class Catalog
{
    public static void Dump(Universe u)
    {
        Console.WriteLine("== Baufenster (PlayerBuilding.Catalog) ==");
        Console.WriteLine("Typ;Größe;Radius;Baukosten(Treuhand);Unterhalt/Tick;Einkommen/Tick(Spieler);Rezepte;Stückliste");
        foreach (var t in PlayerBuilding.Catalog(u))
        {
            var rec = string.Join(" | ", t.Recipes.Select(r => PlayerBuilding.RecipeText(u, r)));
            var bill = ConstructionRules.Bill(u, t);
            Console.WriteLine($"{t.Key};{t.Size};{t.Radius};{PlayerBuilding.Cost(u, t):0};{t.UpkeepPerTick:0.###};{MathF.Min(t.IncomePerTick, t.UpkeepPerTick):0.###};{rec};{(bill is null ? "-" : PlayerBuilding.BillText(u, bill))}");
        }
        Console.WriteLine("== Nicht baubare Typen ==");
        foreach (var t in u.StationTypes.Values.Where(t => !PlayerBuilding.IsBuildableKind(u, t)).OrderBy(t => t.Key))
            Console.WriteLine($"{t.Key};{t.Government};Baukosten {t.BuildCost:0};Unterhalt/Tick {t.UpkeepPerTick:0.###};Einkommen {t.IncomePerTick:0.###}");
        var r = u.Rules;
        Console.WriteLine($"StationBuildCostFactor {r.StationBuildCostFactor} PlayerMaxSites {r.PlayerMaxSites} PlayerMaxStations {r.PlayerMaxStations} perSector {r.PlayerMaxStationsPerSector} HqMinClass {r.PlayerHqMinClass} SeatMinClass {r.PlayerSeatMinClass} Thresholds {string.Join(",", r.ActorSizeThresholds)} ShipW {r.ActorShipWeight} StationW {r.ActorStationWeight} SectorW {r.ActorSectorWeight} MoneyScale {r.ActorMoneyScale}");
    }

    /// <summary>Schiffspreise gegen Stärke, Fracht, Abbau, Unterhalt; Preis der Werft mit vollem Lager (Quote.Price = Material zu Marktpreisen × (1 + Montage)).</summary>
    public static void ShipTable(Universe u)
    {
        var run = new SimulationRunner(); run.Run(u, 300);
        var yards = u.StationsById().Where(s => s.TypeKey == "shipyard").ToList();
        var yard = yards.First();
        var me = u.Ships[u.PlayerShip];
        Console.WriteLine($"Werft {yard.Name}; Preise = Materialwert zu Marktpreisen × (1+Montage); Unterhalt je Schiff mit Befehl {u.Rules.ShipUpkeepPerTick * 4 * 3600:0} Cr/h flach");
        Console.WriteLine("Modell;Klasse;Größe;Jobs;Preis;Lieferbar;Hülle;Schild;Panzer;Waffenkraft;Stärke(CombatRules);Cargo;Tempo;Abbau/s;Abbau-Stufe;Preis/Stärke;Preis/Cargo;Preis/Abbau;Hängt ab: Kit");
        foreach (var m in ShipDealerService.Models(u, yard).OrderBy(m => u.ShipClasses[m.ClassKey].Size).ThenBy(m => m.Key))
        {
            if (ShipDealerService.Quote(u, yard, me, m.Key) is not { } q) continue;
            var s = q.Stats; float str = CombatModels.Strength(s);
            float mine = s.ContactMining > 0 ? s.ContactMining : s.HasLaser ? s.MiningTier : 0f;
            Console.WriteLine($"{m.Key};{q.Class.Key};{q.Class.Size};{string.Join("+", m.Jobs)};{q.Price:0};{(q.Buyable ? "ja" : "(Lager fehlt) " + string.Join(",", q.Missing.Select(x => x.Good)))};{s.Hull:0};{s.Shield:0};{s.Armor:0};{s.WeaponPower:0.0};{str:0.0};{s.Cargo:0};{s.Speed:0.0};{s.ContactMining:0.0};{s.MiningTier:0.0};{(s.WeaponPower > 0 ? q.Price / str : 0):0};{(s.Cargo > 0 ? q.Price / s.Cargo : 0):0.0};{(mine > 0 ? q.Price / mine : 0):0};{q.Class.Kit}");
        }
    }
}
