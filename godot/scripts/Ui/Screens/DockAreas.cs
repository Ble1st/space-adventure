using System.Collections.Generic;
using System.Linq;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kontext des Docks: Universum, angedockte Station, Schiff und Dienst-Registry. Die Registry-Instanz hält der
/// Aufrufer (<see cref="SpaceAdventure.Game.GameSession.Services"/>), es gibt keinen statischen globalen Zustand;
/// Main setzt die Felder vor dem Öffnen der Dock-Bildschirme.
/// </summary>
public sealed class DockContext
{
	public Universe U = null!;
	public Station Station = null!;
	public Ship Ship = null!;
	public StationServices Services = null!;
}

/// <summary>Ein Bereich des Andock-Menüs (UI.md Abschnitt 3): Anzeige, Untertitel und ob der Dienst im Core echt verfügbar ist.</summary>
public sealed class DockAreaDef
{
	public required string Id;
	public required string Label;
	public required string Sub;
	/// <summary>True, wenn der Core den Dienst an dieser Station liefert (<see cref="StationServices.ForStation"/>); false = Platzhalter („Bald“).</summary>
	public required bool Available;
}

/// <summary>
/// Baut die Bereichsliste des Andock-Menüs aus den Daten: Handel nur, wenn <see cref="StationServices.ForStation"/>
/// den Dienst „trade“ liefert; „Reparatur &amp; Tanken“, wenn sie „refuel“ oder „repair“ liefert; Ausrüster und Werft, wenn sie
/// „outfitter“ bzw. „shipdealer“ liefert (Phase 5c); Station-Info und Hangar immer; Taxi und Aufträge als gedämpfte Platzhalter; an
/// Stationen mit „storage“ (Lagerhaus, Phase 8) steht statt Taxi der Bereich „Lager“, an Stationen mit „court“/„bounty“ (Phase 11) statt Aufträge die
/// „Behörde“.
/// </summary>
public static class DockAreas
{
	public const string Trade = "trade";
	public const string Repair = "repair";
	public const string Outfitter = "outfitter";
	public const string Shipyard = "shipyard";
	public const string Hangar = "hangar";
	public const string Info = "info";
	public const string Taxi = "taxi";
	public const string Missions = "missions";
	/// <summary>Phase 8: persönliches Lager (Dienst „storage“, Lagerhaus); ersetzt an solchen Stationen die Taxi-Kachel.</summary>
	public const string Storage = "storage";
	/// <summary>Phase 11: Behörde (Dienste „court“ und „bounty“, Regierungsstationen); ersetzt dort die Kachel „Aufträge“.</summary>
	public const string Authority = "authority";

	/// <summary>Anzeige-Label je Bereichs-Id (auch für Dienst-Schlüssel der Station-Info).</summary>
	public static string LabelFor(string id) => id switch
	{
		Trade => "Handel",
		Repair => "Reparatur & Tanken",
		Outfitter => "Ausrüster",
		Shipyard => "Werft",
		Hangar => "Hangar",
		Info => "Station-Info",
		Taxi => "Taxi",
		Missions => "Aufträge",
		Storage => "Lager",
		Authority => "Behörde",
		CourtService.ServiceKey => "Gericht",          // Dienst-Schlüssel (Station-Info)
		BountyService.ServiceKey => "Kopfgeldbüro",
		_ => id,
	};

	/// <summary>Kurzlabel für die schmale Seitenleiste (dort passt „Reparatur & Tanken“ nicht hin).</summary>
	public static string ShortLabelFor(string id) => id == Repair ? "Reparatur" : LabelFor(id);

	/// <summary>Bereichsliste in der Reihenfolge aus UI.md Abschnitt 3, Handel nur wenn verfügbar.</summary>
	public static IReadOnlyList<DockAreaDef> Build(Universe u, Station st, StationServices services)
	{
		var list = new List<DockAreaDef>(8);
		bool trade = services.ForStation(u, st).Any(s => s.Key == TradeService.ServiceKey);
		if (trade)
		{
			int wares = st.Market.Count(kv => kv.Value.Price > 0f);
			list.Add(new DockAreaDef { Id = Trade, Label = LabelFor(Trade), Sub = $"{wares} Waren", Available = true });
		}
		// Reparatur & Tanken: verfügbar, sobald die Station „refuel“ oder „repair“ liefert (Phase 4c)
		var offered = services.ForStation(u, st).Select(s => s.Key).ToList();
		bool refuel = offered.Contains(RefuelService.ServiceKey), repair = offered.Contains(RepairService.ServiceKey);
		// Führt der Stationstyp den Dienst, ist er aber gerade nicht lieferbar (Lager leer), steht das dort statt „Bald“
		bool listed = u.StationTypes.TryGetValue(st.TypeKey, out var type)
			&& type.Services.Any(k => k is RefuelService.ServiceKey or RepairService.ServiceKey);
		string repairSub = refuel && repair ? "Tanken · Reparatur" : refuel ? "Tanken" : repair ? "Reparatur"
			: listed ? "Lager leer" : "Nicht an dieser Station";
		list.Add(new DockAreaDef { Id = Repair, Label = LabelFor(Repair), Sub = repairSub, Available = refuel || repair });
		// Ausrüster und Werft (Phase 5c): echt, sobald ForStation „outfitter“ bzw. „shipdealer“ liefert
		bool outfitter = offered.Contains(OutfitterService.ServiceKey), dealer = offered.Contains(ShipDealerService.ServiceKey);
		bool upgrade = offered.Contains(UpgradeService.ServiceKey);   // Phase 7: Upgrades sind ein Abschnitt im Ausrüster
		int modules = st.Market.Count(kv => kv.Value.Amount >= 1f && u.Goods.TryGetValue(kv.Key, out var g) && u.Modules.ContainsKey(g.Key));
		string outSub = outfitter ? $"{modules} Module{(upgrade ? " · Upgrades" : " auf Lager")}" : upgrade ? "Upgrades" : "Nicht hier";
		list.Add(new DockAreaDef { Id = Outfitter, Label = LabelFor(Outfitter), Sub = outSub, Available = outfitter || upgrade });
		int models = dealer ? ShipDealerService.Models(u, st).Count : 0;
		list.Add(new DockAreaDef { Id = Shipyard, Label = LabelFor(Shipyard), Sub = dealer ? $"{models} Modelle" : "Nicht hier", Available = dealer });
		list.Add(new DockAreaDef { Id = Hangar, Label = LabelFor(Hangar), Sub = "Angedockte Schiffe", Available = true });
		list.Add(new DockAreaDef { Id = Info, Label = LabelFor(Info), Sub = "Dienste & Markt", Available = true });
		// Phase 8: Lagerhaus-Dienst statt der Taxi-Platzhalterkachel (das Menü hat Platz für acht Kacheln)
		if (offered.Contains(StorageService.ServiceKey))
			list.Add(new DockAreaDef { Id = Storage, Label = LabelFor(Storage), Sub = $"{StorageService.Used(st):0}/{u.Rules.StorageCapacity:0} eingelagert", Available = true });
		else
			list.Add(new DockAreaDef { Id = Taxi, Label = LabelFor(Taxi), Sub = UiPlaceholders.Soon, Available = false });
		// Phase 11: Gericht und Kopfgeldbüro statt der Aufträge-Platzhalterkachel
		if (offered.Contains(CourtService.ServiceKey) || offered.Contains(BountyService.ServiceKey))
		{
			int open = u.Player.Wanted.Count;
			list.Add(new DockAreaDef { Id = Authority, Label = LabelFor(Authority), Sub = open > 0 ? $"{open} Strafe{(open == 1 ? "" : "n")} offen" : "Gericht · Kopfgeld", Available = true });
		}
		else
			list.Add(new DockAreaDef { Id = Missions, Label = LabelFor(Missions), Sub = UiPlaceholders.Soon, Available = false });
		return list;
	}
}
