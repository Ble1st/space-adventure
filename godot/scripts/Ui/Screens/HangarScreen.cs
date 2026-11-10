using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Dock-Bereich „Hangar“ (UI.md Abschnitt 3): die angedockten Schiffe, soweit der Core sie kennt – aktuell das
/// Spielerschiff mit Hülle, Fracht, Laderaum und Auftrag (alles echt). Fremde/weitere eigene Schiffe sowie
/// „Steuern“, „Befehle“, „Verkaufen“, „Heim schicken“ sind Platzhalter („Bald“); der Wechsel in ein anderes
/// Schiff kommt erst in Stufe 3 / Phase 12.
/// </summary>
public sealed partial class HangarScreen : DockAreaScreen
{
	public override string Id => "dock.hangar";
	public HangarScreen() => AreaId = DockAreas.Hangar;

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U; var ship = Ctx.Ship;
		float w = inner.Size.X > 0 ? inner.Size.X : 1020f;

		var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		head.AddThemeConstantOverride("separation", 12);
		At(inner, head, 0, 0, w, 36);
		head.AddChild(Text("Angedockte Schiffe", 20, Cyan));
		var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
		head.AddChild(spacer);
		head.AddChild(Text($"Liegeplätze/Gebühr: {UiPlaceholders.Tag}", 14, Dim));

		// Karte des Spielerschiffs (einzige angedockte Schiff, das der Core kennt)
		var card = At(inner, new PanelContainer(), 0, 44, w - 8, 212);
		card.AddThemeStyleboxOverride("panel", Box(new Color(0.12f, 0.22f, 0.4f, 0.95f), Cyan, 10, 2, 14));
		var cv = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		cv.AddThemeConstantOverride("separation", 6);
		card.AddChild(cv);

		var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		top.AddThemeConstantOverride("separation", 12);
		cv.AddChild(top);
		top.AddChild(new ShipSketch { CustomMinimumSize = new Vector2(56, 44), Size = new Vector2(56, 44), MouseFilter = MouseFilterEnum.Ignore });
		var names = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		names.AddThemeConstantOverride("separation", 2);
		names.AddChild(Text($"Dein Schiff #{ship.Id.Value}", 22, White));
		names.AddChild(Text($"Rolle {ship.Role} · Fraktion {FactionName(u, ship.Faction)}", 14, Dim));
		names.AddChild(Text("AKTIV – von dir gesteuert", 14, Cyan));
		top.AddChild(names);
		var sp = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
		top.AddChild(sp);
		// Aktionen in einer Zeile, sonst wird die Karte zu hoch und überlappt den Platzhalter-Hinweis unten
		var act = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		act.AddThemeConstantOverride("separation", 6);
		top.AddChild(act);
		foreach (var label in new[] { "Steuern", "Befehle", "Verkaufen", "Heim schicken" })
		{
			var b = new KitButton(label, new Vector2(118, 44)) { FontSize = 15, State = KitButtonState.Disabled, Sub = UiPlaceholders.Soon };
			act.AddChild(b);
		}

		// Hülle, Fracht/Laderaum, Credits, Auftrag – echt
		BarLine(cv, "Hülle", ship.Hull, ship.MaxHull, ship.Hull > 60f ? Green : Orange, $"{(int)ship.Hull}");
		BarLine(cv, "Fracht", ship.UsedVolume(u), ship.CargoCapacity, Cyan, $"{ship.UsedVolume(u):0.#}/{ship.CargoCapacity:0} m³");
		cv.AddChild(Text($"Credits {(int)u.WalletOf(ship).Credits:0} · Auftrag: {OrderText(u, ship)}", 16, White));

		// Platzhalter-Hinweis für fremde/weitere eigene Schiffe (unterhalb der Karte, ohne Überlappung)
		At(inner, Text(UiPlaceholders.HangarSoon, 14, new Color(Dim, 0.75f)), 0, 268, w - 8, 40);
	}

	static string FactionName(Universe u, FactionId f) => u.Factions.TryGetValue(f, out var x) ? x.Name : "?";

	/// <summary>Auftrag des Schiffs als Text (echt aus <see cref="Ship.Order"/>).</summary>
	static string OrderText(Universe u, Ship ship) => ship.Order switch
	{
		null => "kein Auftrag",
		TradeOrder t => $"Handel {GoodName(u, t.Good)}: {StationName(u, t.Buy)} → {StationName(u, t.Sell)}",
		MineOrder m => $"Abbau in {SectorName(u, m.Sector)}, liefern an {StationName(u, m.DeliverTo)}",
		PatrolOrder p => $"Patrouille in {SectorName(u, p.Sector)}",
		GoToOrder g => $"Flug nach {StationName(u, g.Target)}",
		_ => ship.Order.GetType().Name,
	};

	static string GoodName(Universe u, GoodId g) => u.Goods.TryGetValue(g, out var x) ? x.Name : g.Value.ToString();
	static string StationName(Universe u, StationId id) => u.Stations.TryGetValue(id, out var s) ? s.Name : id.Value.ToString();
	static string SectorName(Universe u, SectorId id) => u.Sectors.TryGetValue(id, out var s) ? s.Name : id.Value.ToString();

	static void BarLine(Control parent, string name, float value, float max, Color fill, string text)
	{
		var r = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		r.AddThemeConstantOverride("separation", 8);
		var l = Text(name, 15, Dim); l.CustomMinimumSize = new Vector2(58, 0);
		r.AddChild(l);
		var bar = new KitBar(420, 12, fill);
		bar.Set(value, fill, max);
		r.AddChild(bar);
		r.AddChild(Text(text, 15, White));
		parent.AddChild(r);
	}
}
