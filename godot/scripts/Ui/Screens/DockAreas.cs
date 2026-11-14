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
/// „outfitter“ bzw. „shipdealer“ liefert (Phase 5c); Station-Info und Hangar immer (Phase 12: mit Dienst „hangar“ samt fremden Schiffen, Wechsel,
/// Verkauf und Leihschiff); Taxi echt, wenn die Station „taxi“ liefert (Phase 12); dazu „Lager“ (Phase 8, „storage“) und „Behörde“ (Phase 11,
/// „court“/„bounty“, statt der Platzhalterkachel „Aufträge“) und „Forschung“ (Phase 13, „research“). Es können neun Kacheln werden — Menü und Leiste rollen dann.
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
	/// <summary>Phase 13: Forschung (Dienst „research“, Forschungsstationen).</summary>
	public const string Research = "research";

	/// <summary>Anzeige-Label je Bereichs-Id (auch für Dienst-Schlüssel der Station-Info).</summary>
	public static string LabelFor(string id, Universe? u = null) => id switch
	{
		Trade => "Handel",
		Repair => u is { Rules.FuelActive: false } ? "Reparatur" : "Reparatur & Tanken",   // Phase 14: ohne Treibstoff nur Reparatur
		Outfitter => "Ausrüster",
		Shipyard => "Werft",
		Hangar => "Hangar",
		Info => "Station-Info",
		Taxi => "Taxi",
		Missions => "Aufträge",
		Storage => "Lager",
		Authority => "Behörde",
		Research => "Forschung",
		CourtService.ServiceKey => "Gericht",          // Dienst-Schlüssel (Station-Info)
		BountyService.ServiceKey => "Kopfgeldbüro",
		_ => id,
	};

	/// <summary>Gerätetest Phase 27: deutscher Name eines Stationsdienstes (Station-Info-Chips; nie der Rohschlüssel).</summary>
	public static string ServiceName(string key, Universe? u = null) => key switch
	{
		"trade" => "Handel", "repair" => "Reparatur", "refuel" => "Tanken", "outfitter" => "Ausrüster", "shipdealer" => "Schiffshändler",
		"upgrade" => "Verbessern", "storage" => "Lager", "court" => "Gericht", "bounty" => "Kopfgeldbüro", "hangar" => "Hangar", "taxi" => "Taxi",
		"rental" => "Verleih", "research" => "Forschung", "rearm" => "Munition",
		_ => LabelFor(key, u),
	};

	/// <summary>Kurzlabel für die schmale Seitenleiste (dort passt „Reparatur & Tanken“ nicht hin).</summary>
	public static string ShortLabelFor(string id) => id == Repair ? "Reparatur" : LabelFor(id);

	/// <summary>Bereichsliste in der Reihenfolge aus UI.md Abschnitt 3, Handel nur wenn verfügbar.</summary>
	public static IReadOnlyList<DockAreaDef> Build(Universe u, Station st, StationServices services, Ship? ship = null)
	{
		var list = new List<DockAreaDef>(10);
		ship ??= u.Ships.GetValueOrDefault(u.PlayerShip);
		bool trade = services.ForStation(u, st).Any(s => s.Key == TradeService.ServiceKey);
		if (trade)
		{
			int wares = st.Market.Count(kv => kv.Value.Price > 0f);
			list.Add(new DockAreaDef { Id = Trade, Label = LabelFor(Trade), Sub = $"{wares} Waren", Available = true });
		}
		// Reparatur & Tanken: verfügbar, sobald die Station „refuel“ oder „repair“ liefert (Phase 4c)
		var offered = services.ForStation(u, st).Select(s => s.Key).ToList();
		bool refuel = offered.Contains(RefuelService.ServiceKey), repair = offered.Contains(RepairService.ServiceKey);
		bool rearm = offered.Contains(RearmService.ServiceKey);   // Phase 25: Munition nachladen als Abschnitt der Reparatur
		// Führt der Stationstyp den Dienst, ist er aber gerade nicht lieferbar (Lager leer), steht das dort statt „Bald“
		bool listed = u.StationTypes.TryGetValue(st.TypeKey, out var type)
			&& type.Services.Any(k => k is RefuelService.ServiceKey or RepairService.ServiceKey or RearmService.ServiceKey);
		string repairSub = refuel && repair ? "Tanken · Reparatur" : refuel ? "Tanken" : repair ? "Reparatur" : rearm ? "Munition"
			: listed ? "Lager leer" : "Nicht an dieser Station";
		if (rearm && (refuel || repair)) repairSub += " · Munition";
		list.Add(new DockAreaDef { Id = Repair, Label = LabelFor(Repair, u), Sub = repairSub, Available = refuel || repair || rearm });
		// Ausrüster und Werft (Phase 5c): echt, sobald ForStation „outfitter“ bzw. „shipdealer“ liefert
		bool outfitter = offered.Contains(OutfitterService.ServiceKey), dealer = offered.Contains(ShipDealerService.ServiceKey);
		bool upgrade = offered.Contains(UpgradeService.ServiceKey);   // Phase 7: Upgrades sind ein Abschnitt im Ausrüster
		int modules = st.Market.Count(kv => kv.Value.Amount >= 1f && u.Goods.TryGetValue(kv.Key, out var g) && u.Modules.ContainsKey(g.Key));
		string outSub = outfitter ? $"{modules} Module{(upgrade ? " · Upgrades" : " auf Lager")}" : upgrade ? "Upgrades" : "Nicht hier";
		list.Add(new DockAreaDef { Id = Outfitter, Label = LabelFor(Outfitter), Sub = outSub, Available = outfitter || upgrade });
		int models = dealer ? ShipDealerService.Models(u, st).Count : 0;
		list.Add(new DockAreaDef { Id = Shipyard, Label = LabelFor(Shipyard), Sub = dealer ? $"{models} Modelle" : "Nicht hier", Available = dealer });
		// Phase 12: Hangar echt (Dienst „hangar“: eigene und fremde Schiffe, Wechsel, Heim schicken, Verkaufen, Leihschiff)
		bool hangar = offered.Contains(HangarService.ServiceKey);
		int docked = SpaceSim.Core.Simulation.Hangar.DockedShips(u, st).Count(s => s.Id != u.PlayerShip) + 1;   // + das aktive Schiff (die Ansicht hält es angedockt)
		list.Add(new DockAreaDef { Id = Hangar, Label = LabelFor(Hangar), Sub = hangar ? $"{docked} Schiff{(docked == 1 ? "" : "e")} angedockt" : "Nur dein Schiff", Available = true });
		list.Add(new DockAreaDef { Id = Info, Label = LabelFor(Info), Sub = "Dienste & Markt", Available = true });
		// Phase 12: Taxi echt (Dienst „taxi“); Lager und Behörde verdrängen keine Kacheln mehr — das Raster rollt bei Bedarf
		bool taxi = offered.Contains(TaxiService.ServiceKey);
		int targets = taxi && ship is not null ? TaxiService.Quotes(u, st, ship).Count(q => q.Bookable) : 0;
		// Runde 23 (Handy-Test: „0 Ziele buchbar“ sah aktiv aus): ohne buchbares Ziel abgeblendet mit Grund
		string taxiSub = !taxi ? "Nicht hier" : targets > 0 ? $"{targets} Ziel{(targets == 1 ? "" : "e")} buchbar" : "Kein Ziel buchbar";
		list.Add(new DockAreaDef { Id = Taxi, Label = LabelFor(Taxi), Sub = taxiSub, Available = taxi && targets > 0 });
		if (offered.Contains(StorageService.ServiceKey))
			list.Add(new DockAreaDef { Id = Storage, Label = LabelFor(Storage), Sub = $"{StorageService.Used(st):0}/{u.Rules.StorageCapacity:0} eingelagert", Available = true });
		// Phase 13: Forschung (Projekte ansehen, beauftragen, mitfinanzieren) an Forschungsstationen
		if (offered.Contains(ResearchService.ServiceKey) && SpaceSim.Core.Simulation.ResearchInfo.ForStation(u, st) is { } rv)
			list.Add(new DockAreaDef { Id = Research, Label = LabelFor(Research), Sub = $"{rv.Completed}/{rv.Total} erforscht" + (rv.Current is not null ? " · läuft" : ""), Available = true });
		// Phase 11: Gericht und Kopfgeldbüro statt der Aufträge-Platzhalterkachel (Missionen gibt es noch nicht)
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
