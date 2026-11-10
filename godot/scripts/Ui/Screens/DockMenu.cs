using System;
using Godot;
using SpaceAdventure.Ui.Kit;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kleines Auswahlmenü beim Andocken (UI.md Abschnitt 3, Vorlage prototypes/FinalUi.cs „Menu“): oben die Karten
/// der angedockten Schiffe (der Core kennt nur das Spielerschiff als angedockt, weitere sind Platzhalter), darunter die
/// Bereiche mit Untertitel (Daten aus <see cref="DockAreas.Build"/> – Handel nur mit Dienst „trade“, Rest „Bald“),
/// unten die große Taste „Abdocken“. Als IScreen in der Registry; der Aufrufer setzt <see cref="Ctx"/>.
/// </summary>
public sealed partial class DockMenu : Control, IScreen
{
	public string Id => "dock.menu";
	public string Title => Ctx != null ? Ctx.Station.Name : "Dock";
	public Control Node => this;
	public bool IsOpen => Visible;

	/// <summary>Kontext des Docks (Universum, Station, Schiff, Dienste).</summary>
	public DockContext Ctx { get; set; } = null!;

	/// <summary>Bereich wurde angetippt (Id aus <see cref="DockAreas"/>; Platzhalter öffnen ihre Platzhalterseite).</summary>
	public event Action<string>? AreaSelected;
	/// <summary>Große Taste „Abdocken“ unten.</summary>
	public event Action? UndockPressed;

	long _builtTick = -1;

	public DockMenu()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
	}

	public void Open() { Visible = true; Rebuild(); }
	public void Close() => Visible = false;

	/// <summary>Aktualisiert die Anzeige (z. B. Credits nach einem Verkauf durch den Auto-Miner).</summary>
	public void Refresh()
	{
		if (!Visible || Ctx == null) return;
		if (Ctx.U.Tick != _builtTick) Rebuild();
	}

	public override void _Draw()
	{
		if (Visible) DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.02f, 0.05f, 0.62f));
	}

	void Rebuild()
	{
		_builtTick = Ctx.U.Tick;
		foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }

		var u = Ctx.U; var st = Ctx.Station; var ship = Ctx.Ship;
		string type = u.StationTypes.TryGetValue(st.TypeKey, out var t) ? t.Name : st.TypeKey;
		string faction = u.Factions.TryGetValue(st.Owner, out var f) ? f.Name : "?";
		string sector = u.Sectors.TryGetValue(st.Sector, out var sec) ? sec.Name : "?";

		// Karte mit freier Platzierung (ein PanelContainer würde alle Kinder strecken); Innenrand 20 px
		var card = CardBg(this, 350, 36, 580, 648, PanelSolid, Edge, 16, 2);

		// Titel: Station, Sektor, Fraktion, Typ
		At(card, Text(st.Name, 26, Cyan), 20, 14, 540, 32);
		At(card, Text($"{sector} · {faction} · {type} · Kasse {st.Credits:0} Cr", 15, Dim), 20, 50, 540, 22);

		// Angedockte Schiffe oben als Karten (der Core kennt nur das Spielerschiff als angedockt)
		At(card, Text("ANGEDOCKT", 12, Dim), 20, 80, 540, 18);
		var chips = At(card, new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }, 20, 102, 540, 92);
		chips.AddThemeConstantOverride("separation", 8);
		chips.AddChild(new ShipChip(ship, true));
		chips.AddChild(new ShipChip(null, false));   // weitere Schiffe: Platzhalter

		// Bereiche darunter, je mit Untertitel
		var defs = DockAreas.Build(u, st, Ctx.Services);
		var grid = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
		grid.AddThemeConstantOverride("h_separation", 10);
		grid.AddThemeConstantOverride("v_separation", 10);
		At(card, grid, 20, 204, 540, 332);
		foreach (var def in defs)
		{
			var b = new KitButton(def.Label, new Vector2(262, 76)) { AlignLeft = true, FontSize = 20, Sub = def.Sub };
			string id = def.Id;
			b.Pressed += () => AreaSelected?.Invoke(id);   // auch Platzhalter öffnen ihre „Bald“-Seite
			if (!def.Available) b.Modulate = new Color(1f, 1f, 1f, 0.5f);   // Sub trägt schon „Bald“
			grid.AddChild(b);
		}

		// Große Taste „Abdocken“ unten
		var undock = At(card, new KitButton("Abdocken", new Vector2(540, 64)) { FontSize = 24, State = KitButtonState.Ready }, 20, 556);
		undock.Pressed += () => UndockPressed?.Invoke();
		At(card, Text($"Hülle repariert beim Andocken · {(int)u.WalletOf(ship).Credits} Credits · Liegeplätze/Gebühr: {UiPlaceholders.Tag}", 13, Dim, HorizontalAlignment.Center), 20, 628, 540, 20);
	}

	/// <summary>Karte eines angedockten Schiffs (Hülle echt); null = Platzhalter für weitere Schiffe.</summary>
	sealed partial class ShipChip : Control
	{
		readonly SpaceSim.Core.Model.Ship? _ship;
		readonly bool _sel;

		public ShipChip(SpaceSim.Core.Model.Ship? ship, bool sel)
		{
			_ship = ship; _sel = sel;
			MouseFilter = MouseFilterEnum.Ignore;
			CustomMinimumSize = new Vector2(122, 92);
			Size = CustomMinimumSize;
		}

		public override void _Draw()
		{
			DrawRect(new Rect2(Vector2.Zero, Size), _sel ? new Color(0.14f, 0.26f, 0.46f, 0.98f) : new Color(0.04f, 0.07f, 0.13f, 0.95f));
			DrawRect(new Rect2(Vector2.Zero, Size), _sel ? Cyan : new Color(Dim, 0.4f), false, 2f);
			var font = ThemeDB.FallbackFont;
			if (_ship != null)
			{
				var c = new Vector2(61, 26); float s = 16f;
				var pts = new[]
				{
					c + new Vector2(0, -s), c + new Vector2(s * 0.55f, s * 0.35f), c + new Vector2(s * 0.22f, s * 0.55f),
					c + new Vector2(0, s * 0.32f), c + new Vector2(-s * 0.22f, s * 0.55f), c + new Vector2(-s * 0.55f, s * 0.35f),
				};
				DrawColoredPolygon(pts, new Color(0.25f, 0.33f, 0.5f));
				var closed = new System.Collections.Generic.List<Vector2>(pts) { pts[0] };
				DrawPolyline(closed.ToArray(), Cyan, 1.5f, true);
				DrawString(font, new Vector2(0, 66), $"#{ _ship.Id.Value}", HorizontalAlignment.Center, 122, 15, White);
				DrawString(font, new Vector2(0, 80), "aktiv", HorizontalAlignment.Center, 122, 12, Dim);
				DrawRect(new Rect2(14, 84, 94, 4), new Color(0, 0, 0, 0.5f));
				DrawRect(new Rect2(14, 84, 94 * Mathf.Clamp(_ship.Hull / _ship.MaxHull, 0f, 1f), 4), _ship.Hull > 60f ? Green : Orange);
			}
			else
			{
				DrawString(font, new Vector2(0, 40), "…", HorizontalAlignment.Center, 122, 22, new Color(Dim, 0.7f));
				DrawString(font, new Vector2(0, 62), "weitere", HorizontalAlignment.Center, 122, 12, new Color(Dim, 0.7f));
				DrawString(font, new Vector2(0, 78), UiPlaceholders.Tag, HorizontalAlignment.Center, 122, 11, new Color(Dim, 0.5f));
			}
		}
	}
}
