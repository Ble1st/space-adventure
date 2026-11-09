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
/// den Dienst „trade“ liefert; Station-Info und Hangar immer; alle übrigen Bereiche als gedämpfte Platzhalter.
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
		list.Add(new DockAreaDef { Id = Repair, Label = LabelFor(Repair), Sub = UiPlaceholders.Soon, Available = false });
		list.Add(new DockAreaDef { Id = Outfitter, Label = LabelFor(Outfitter), Sub = UiPlaceholders.Soon, Available = false });
		list.Add(new DockAreaDef { Id = Shipyard, Label = LabelFor(Shipyard), Sub = UiPlaceholders.Soon, Available = false });
		list.Add(new DockAreaDef { Id = Hangar, Label = LabelFor(Hangar), Sub = "Angedockte Schiffe", Available = true });
		list.Add(new DockAreaDef { Id = Info, Label = LabelFor(Info), Sub = "Dienste & Markt", Available = true });
		list.Add(new DockAreaDef { Id = Taxi, Label = LabelFor(Taxi), Sub = UiPlaceholders.Soon, Available = false });
		list.Add(new DockAreaDef { Id = Missions, Label = LabelFor(Missions), Sub = UiPlaceholders.Soon, Available = false });
		return list;
	}
}
