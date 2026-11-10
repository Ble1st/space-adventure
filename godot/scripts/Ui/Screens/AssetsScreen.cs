using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Besitz“ (UI.md Abschnitt 4): Kasse, Zahl Schiffe/Stationen und Gesamtwert aus echten
/// Daten (Credits + Fracht zu Basispreisen über <see cref="SectorEconomy"/>, klar beschriftet). Tabs Schiffe,
/// Stationen, Verträge, Lager. Schiffe: das Spielerschiff mit Standort, Auftrag, Hülle, Fracht (alles echt).
/// Tippen auf ein Schiff öffnet ein Detailfenster mit „In dieses Schiff wechseln“: für das aktive Schiff
/// „Bereits aktiv“, sonst gesperrt „Bald (Phase 12)“, weil der Spieler nur ein Schiff besitzt. Befehle,
/// Heim schicken, Verkaufen: Platzhalter. Echtes Schiffwechseln braucht Besitz mehrerer Schiffe im Core
/// und gehört nicht in diese Stufe.
/// </summary>
public sealed partial class AssetsScreen : CommandScreen
{
	public override string Id => "cc.assets";

	string _tab = "ships";
	ShipId? _detailShip;

	/// <summary>Test-Hook: Detailfenster eines Schiffs öffnen.</summary>
	public void TestOpenShipDetail() { _detailShip = Ctx.Player.Id; Invalidate(); }

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		var ship = Ctx.Player;
		int myShips = u.Ships.Values.Count(s => s.IsPlayer);
		int myStations = 0;   // Spielerbesitz an Stationen gibt es im Core noch nicht
		float total = SectorEconomy.TotalValue(u, ship);

		// ── Kopf: Kasse, Zahlen, Gesamtwert (echt) ──
		float x = 20;
		foreach (var (label, val, col) in new[] { ("Kasse", $"{(int)ship.Credits} Cr", Orange), ("Schiffe", myShips.ToString(), White), ("Stationen", myStations.ToString(), White), ("Gesamtwert", $"≈ {total:0} Cr", White) })
		{
			At(inner, Text(val, 22, col), x, 8, 190, 28);
			At(inner, Text(label, 13, Dim), x, 38, 190, 18);
			x += 200;
		}
		At(inner, Text("Credits + Fracht (Basis)", 11, new Color(Dim, 0.75f)), 620, 58, 190, 16);

		// ── Tabs ──
		float tx = 12;
		foreach (var (id, label) in new[] { ("ships", "Schiffe"), ("stations", "Stationen"), ("contracts", "Verträge"), ("storage", "Lager") })
		{
			var b = At(inner, new KitButton(label, new Vector2(130, 40)) { FontSize = 17, State = _tab == id ? KitButtonState.On : KitButtonState.Normal }, tx, 76);
			string t = id;
			b.Pressed += () => { _tab = t; _detailShip = null; Invalidate(); };
			tx += 136;
		}

		// ── Inhalt je Tab ──
		switch (_tab)
		{
			case "ships": BuildShips(inner, u, ship, myShips); break;
			case "stations": EmptyTab(inner, "Du besitzt noch keine Stationen."); break;
			case "contracts": EmptyTab(inner, "Du besitzt noch keine Verträge."); break;
			case "storage": EmptyTab(inner, "Du besitzt noch kein Lager."); break;
		}

		// ── Detailfenster eines Schiffs (Overlay) ──
		if (_detailShip is { } did && u.Ships.TryGetValue(did, out var dship)) BuildShipDetail(inner, u, dship);
	}

	void BuildShips(Control inner, Universe u, Ship ship, int myShips)
	{
		var list = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		list.AddThemeConstantOverride("separation", 8);
		At(inner, list, 12, 128, 998, 380);
		foreach (var s in u.Ships.Values.Where(s => s.IsPlayer).OrderBy(s => s.Id.Value))
		{
			bool active = s.Id == u.PlayerShip;
			var row = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
			row.AddThemeStyleboxOverride("panel", Box(active ? new Color(0.12f, 0.22f, 0.4f, 0.95f) : Card, active ? Cyan : Edge, 10, 2, 12));
			row.CustomMinimumSize = new Vector2(978, 92);
			var s2 = s;
			row.GuiInput += e =>
			{
				if (Pointer.TryRead(e, out _, out _, out bool pressed, out bool motion) && pressed && !motion) { _detailShip = s2.Id; Invalidate(); row.AcceptEvent(); }
			};
			row.AddChild(new AssetRow(u, s2, active, Ctx.Docked));
			list.AddChild(row);
		}
		if (myShips == 0) At(inner, Text("Du besitzt noch keine Schiffe.", 15, Dim), 24, 140, 600, 22);
		At(inner, Text("Tippen auf ein Schiff öffnet das Detailfenster.", 13, new Color(Dim, 0.75f)), 20, 500, 600, 18);
	}

	static void EmptyTab(Control inner, string text)
	{
		At(inner, Text(text, 18, Dim), 24, 150, 700, 26);
		At(inner, Text(UiPlaceholders.Tag, 13, new Color(Dim, 0.6f)), 24, 184, 400, 20);
	}

	void BuildShipDetail(Control inner, Universe u, Ship s)
	{
		// abgedunkeltes Overlay + Fenster (wie prototypes/FinalUi2.cs „AssetDetail“)
		var dim = At(inner, new ColorRect { Color = new Color(0.01f, 0.02f, 0.05f, 0.55f), MouseFilter = MouseFilterEnum.Stop }, 0, 0, 1022, 520);
		var w = CardBg(inner, 61, 10, 900, 500, PanelSolid, Cyan, 16, 2);
		bool active = s.Id == u.PlayerShip;

		// Schiffskarte links
		At(w, new DockAreaScreen.ShipSketch { CustomMinimumSize = new Vector2(120, 100), Size = new Vector2(120, 100), MouseFilter = MouseFilterEnum.Ignore }, 30, 40);
		At(w, Text($"Schiff #{s.Id.Value}{(active ? " (aktiv)" : "")}", 30, White), 170, 24, 400, 36);
		At(w, Text($"Rolle {s.Role} · Fraktion {FactionName(u, s.Faction)}", 15, Dim), 170, 62, 400, 20);
		string ort = Ctx.Docked is { } dock && active && u.Stations.TryGetValue(dock, out var dst)
			? $"angedockt an {dst.Name}"
			: $"{SectorName(u, s.Sector)} · ({s.Position.X:0}/{s.Position.Y:0})";
		At(w, Text(ort, 16, active ? Cyan : Orange), 170, 88, 500, 22);

		// Zustand (Hülle echt, Schild/Sprit Platzhalter)
		At(w, Text("Zustand", 17, Cyan), 30, 160, 200, 22);
		Bar(w, 30, 188, "Hülle", s.Hull, s.MaxHull, s.Hull > 60f ? Green : Orange, $"{(int)s.Hull}");
		Bar(w, 30, 216, "Schild · Sprit", 0f, 1f, new Color(Dim, 0.5f), UiPlaceholders.Tag);

		// Auftrag und Standort (echt)
		At(w, Text("Auftrag", 17, Cyan), 30, 256, 200, 22);
		At(w, Text(OrderText(u, s), 15, White), 30, 282, 560, 22);
		At(w, Text("Standort", 17, Cyan), 30, 316, 200, 22);
		At(w, Text(ort + (s.Transit != null ? " · im Transit" : ""), 15, White), 30, 342, 560, 22);
		At(w, Text($"{UiPlaceholders.ShipUpkeepText} · {UiPlaceholders.ShipValueText}", 14, new Color(Dim, 0.8f)), 30, 376, 560, 20);

		// Knöpfe unten (linksbündig, damit der Zustandstext rechts nicht über dem Text klebt)
		var bSwitch = At(w, new KitButton("In dieses Schiff wechseln", new Vector2(380, 58)) { FontSize = 19, AlignLeft = true, State = KitButtonState.Disabled }, 490, 428);
		bSwitch.Detail = active ? "Bereits aktiv" : UiPlaceholders.ShipSwitchSoon;
		At(w, new KitButton("Befehle", new Vector2(120, 48)) { FontSize = 16, AlignLeft = true, State = KitButtonState.Disabled, Detail = UiPlaceholders.Soon }, 30, 428);
		At(w, new KitButton("Heim schicken", new Vector2(160, 48)) { FontSize = 16, AlignLeft = true, State = KitButtonState.Disabled, Detail = UiPlaceholders.Soon }, 160, 428);
		At(w, new KitButton("Verkaufen", new Vector2(140, 48)) { FontSize = 16, AlignLeft = true, State = KitButtonState.Disabled, Detail = UiPlaceholders.Soon }, 330, 428);
		var bClose = At(w, new KitButton("Schließen", new Vector2(150, 44)) { FontSize = 15 }, 730, 16);
		bClose.Pressed += () => { _detailShip = null; Invalidate(); };
	}

	static void Bar(Control parent, float x, float y, string name, float value, float max, Color fill, string text)
	{
		At(parent, Text(name, 14, Dim), x, y, 110, 18);
		var bar = new KitBar(170, 11, fill);
		bar.Set(value, fill, max);
		At(parent, bar, x + 110, y + 3, 170, 11);
		At(parent, Text(text, 14, White), x + 292, y, 200, 18);
	}

	/// <summary>Eine Schiffzeile: Skizze, Name, Standort, Auftrag, Hülle, Fracht (alles echt).</summary>
	sealed partial class AssetRow : Control
	{
		readonly Universe _u; readonly Ship _s; readonly bool _active; readonly StationId? _docked;
		public AssetRow(Universe u, Ship s, bool active, StationId? docked)
		{
			_u = u; _s = s; _active = active; _docked = docked;
			MouseFilter = MouseFilterEnum.Ignore;
			CustomMinimumSize = new Vector2(954, 68);
			Size = CustomMinimumSize;
		}

		public override void _Draw()
		{
			var font = ThemeDB.FallbackFont;
			var c = new Vector2(44, 36); float sc = 20f;
			var pts = new[]
			{
				c + new Vector2(0, -sc), c + new Vector2(sc * 0.55f, sc * 0.35f), c + new Vector2(0, sc * 0.55f), c + new Vector2(-sc * 0.55f, sc * 0.35f),
			};
			DrawColoredPolygon(pts, new Color(0.25f, 0.33f, 0.5f));
			var closed = new System.Collections.Generic.List<Vector2>(pts) { pts[0] };
			DrawPolyline(closed.ToArray(), Cyan, 1.5f, true);

			string ort = _docked is { } d && _active && _u.Stations.TryGetValue(d, out var st)
				? $"angedockt an {st.Name}"
				: $"{(_u.Sectors.TryGetValue(_s.Sector, out var sec) ? sec.Name : "?")} · ({_s.Position.X:0}/{_s.Position.Y:0})";
			DrawString(font, new Vector2(96, 26), $"Schiff #{_s.Id.Value}", HorizontalAlignment.Left, -1, 20, White);
			DrawString(font, new Vector2(96, 48), $"{ort} · Fracht {_s.UsedVolume(_u):0.#}/{_s.CargoCapacity:0} m³ · {(int)_s.Credits} Cr", HorizontalAlignment.Left, -1, 13, Dim);
			DrawString(font, new Vector2(420, 26), OrderText(_u, _s), HorizontalAlignment.Left, -1, 14, White);
			DrawString(font, new Vector2(420, 48), _s.Transit != null ? "im Transit" : "geparkt", HorizontalAlignment.Left, -1, 12, Dim);

			DrawString(font, new Vector2(660, 22), "Hülle", HorizontalAlignment.Left, -1, 12, Dim);
			DrawRect(new Rect2(706, 14, 120, 9), new Color(0, 0, 0, 0.5f));
			DrawRect(new Rect2(706, 14, 120 * Mathf.Clamp(_s.Hull / _s.MaxHull, 0f, 1f), 9), _s.Hull > 60f ? Green : Orange);
			DrawString(font, new Vector2(836, 22), $"{(int)_s.Hull}", HorizontalAlignment.Left, -1, 13, White);
			DrawString(font, new Vector2(660, 44), "Fracht", HorizontalAlignment.Left, -1, 12, Dim);
			DrawRect(new Rect2(706, 36, 120, 9), new Color(0, 0, 0, 0.5f));
			DrawRect(new Rect2(706, 36, 120 * Mathf.Clamp(_s.UsedVolume(_u) / _s.CargoCapacity, 0f, 1f), 9), Cyan);
			DrawString(font, new Vector2(836, 44), $"{_s.UsedVolume(_u):0.#}", HorizontalAlignment.Left, -1, 13, White);

			DrawString(font, new Vector2(954, 26), _active ? "AKTIV" : "Tippen ▸", HorizontalAlignment.Right, -1, 14, _active ? Cyan : Dim);
		}
	}
}
