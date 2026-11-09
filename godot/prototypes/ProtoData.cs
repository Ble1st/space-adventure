using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure.Prototypes;

public sealed record MarketRow(string Name, Color Color, float Price, float Avg, float Amount, float Target, float Held);

/// <summary>Echte Daten aus der Demo-Welt für realistische Entwürfe.</summary>
public sealed class ProtoData
{
	public string Station = "";
	public string Type = "";
	public string Faction = "";
	public float StationCredits;
	public float PlayerCredits = 12480f;
	public float Hull = 82f, Shield = 64f, Fuel = 0.72f;
	public float CargoUsed = 52f, CargoMax = 100f;
	public string Sector = "Alpha";
	public List<MarketRow> Rows = new();

	public static ProtoData Create()
	{
		var u = DemoUniverse.Create();
		new SimulationRunner().Run(u, 1500);
		var d = new ProtoData { Station = "Handelsstation Alpha", Type = "Knotenmarkt (neuer Stationstyp)", Faction = "Terra", Sector = u.Sectors.Values.First().Name };
		d.StationCredits = u.Stations.Values.Max(s => s.Credits);
		var held = new Dictionary<string, float> { ["Stahl"] = 20, ["Wasser"] = 12, ["Elektronik"] = 8, ["Titan"] = 12 };
		foreach (var name in new[] { "Stahl", "Eisenerz", "Wasser", "Elektronik", "Titan", "Hüllenplatten", "Nahrung", "Konsumgüter" })
		{
			var g = u.Goods.Values.First(x => x.Name == name);
			var entry = u.Stations.Values.Where(s => s.Market.ContainsKey(g.Id)).Select(s => s.Market[g.Id]).OrderByDescending(e => e.Amount).First();
			float basePrice = g.BasePrice <= 0 ? entry.Price : g.BasePrice;
			d.Rows.Add(new MarketRow(g.Name, Color.FromHtml(g.Color), entry.Price, basePrice, entry.Amount, entry.Target, held.GetValueOrDefault(g.Name)));
		}
		return d;
	}
}
