using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Basis der Vollbild-Dock-Bereiche (UI.md Abschnitt 3, Vorlage prototypes/FinalUi.cs „Shell“): oben Stationsname,
/// Typ, Fraktion und Credits, links die Leiste zum Wechsel zwischen den Bereichen (Daten aus
/// <see cref="DockAreas.Build"/>), unten „← Menü“ und „Abdocken“ immer sichtbar. Konkrete Bereiche bauen den
/// Mittelteil in <see cref="BuildContent"/>; bei <see cref="ShowShip"/> erscheint rechts die Schiff-Spalte.
/// </summary>
public abstract partial class DockAreaScreen : Control, IScreen
{
	public abstract string Id { get; }
	/// <summary>Id des angezeigten Bereichs; setzt der Aufrufer vor dem Öffnen (<see cref="DockAreas"/>).</summary>
	public string AreaId { get; set; } = "";
	public string Title => DockAreas.LabelFor(AreaId);
	public Control Node => this;
	public bool IsOpen => Visible;

	/// <summary>Kontext (Universum, Station, Schiff, Dienste); der Aufrufer setzt ihn vor dem Öffnen.</summary>
	public DockContext Ctx { get; set; } = null!;

	/// <summary>Wechsel in einen anderen Bereich (Id aus <see cref="DockAreas"/>).</summary>
	public event Action<string>? AreaSelected;
	/// <summary>„← Menü“: zurück zum Andock-Menü.</summary>
	public event Action? BackToMenu;
	/// <summary>„Abdocken“ – funktioniert aus jedem Bereich.</summary>
	public event Action? UndockPressed;

	/// <summary>Rechte Spalte mit dem Schiff (Fracht, Laderaum, Credits) – Handel und später Reparatur/Tanken.</summary>
	protected virtual bool ShowShip => false;

	long _builtTick = -1;
	bool _dirty = true;

	protected DockAreaScreen()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
	}

	public void Open() { Visible = true; Rebuild(); }
	public void Close() => Visible = false;

	/// <summary>Aktualisiert die Anzeige, wenn sich der Simulations-Tick geändert hat (Marktpreise bewegen sich).</summary>
	public void Refresh()
	{
		if (!Visible || Ctx == null) return;
		if (Ctx.U.Tick != _builtTick || _dirty) Rebuild();
	}

	/// <summary>Setzt die Anzeige nach einer Aktion (z. B. Kauf) sofort neu auf.</summary>
	protected void Invalidate() { _dirty = true; Rebuild(); }

	public override void _Draw()
	{
		if (Visible) DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.02f, 0.05f, 0.82f));
	}

	/// <summary>Baut das gesamte Gerüst neu auf (Top-Leiste, Seitenleiste, Mittelteil, Fußleiste).</summary>
	protected virtual void Rebuild()
	{
		_builtTick = Ctx.U.Tick;
		_dirty = false;
		foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }

		var u = Ctx.U; var st = Ctx.Station; var ship = Ctx.Ship;
		string type = u.StationTypes.TryGetValue(st.TypeKey, out var t) ? t.Name : st.TypeKey;
		string faction = u.Factions.TryGetValue(st.Owner, out var f) ? f.Name : "?";
		string sector = u.Sectors.TryGetValue(st.Sector, out var sec) ? sec.Name : "?";

		// ── Top-Leiste: Station links, Credits und Stationskasse rechts ──
		var top = At(this, new Control { MouseFilter = MouseFilterEnum.Ignore }, 20, 10, 1240, 64);
		At(top, Text(st.Name, 28, Cyan), 0, 6);
		At(top, Text($"{type} · {faction} · {sector}", 16, Dim), 0, 42);
		At(top, Text($"{(int)ship.Credits} Credits", 26, Orange, HorizontalAlignment.Right), 940, 0, 300, 32);
		At(top, Text($"Kasse der Station {st.Credits:0} Cr", 16, Dim, HorizontalAlignment.Right), 940, 38, 300, 22);

		// ── Linke Leiste: Wechsel zwischen den Bereichen (Daten aus DockAreas) ──
		var rail = At(this, new PanelContainer { MouseFilter = MouseFilterEnum.Stop }, 14, 84, 200, 540);
		rail.AddThemeStyleboxOverride("panel", Box(PanelBg, null, 10, 0, 8));
		var rv = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		rv.AddThemeConstantOverride("separation", 6);
		rail.AddChild(rv);
		foreach (var def in DockAreas.Build(u, st, Ctx.Services))
		{
			var b = new KitButton(DockAreas.ShortLabelFor(def.Id), new Vector2(184, 56)) { AlignLeft = true, FontSize = 16, Sub = def.Sub };
			string id = def.Id;
			b.Pressed += () => AreaSelected?.Invoke(id);   // auch Platzhalter-Bereiche öffnen ihre „Bald“-Seite
			if (!def.Available) b.Modulate = new Color(1f, 1f, 1f, 0.5f);   // Sub trägt schon „Bald“
			else if (def.Id == AreaId)
				b.State = KitButtonState.On;
			rv.AddChild(b);
		}
		rv.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
		rv.AddChild(Text($"Schiff: #{ship.Id.Value} (aktiv)", 13, Dim));

		// ── Mittelteil ──
		float cw = ShowShip ? 690f : 1040f;
		var center = At(this, new PanelContainer(), 224, 84, cw, 540);
		center.AddThemeStyleboxOverride("panel", Box(PanelBg, Edge, 10, 2, 10));
		var inner = new Control { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(cw - 20, 520), Size = new Vector2(cw - 20, 520) };
		center.AddChild(inner);
		BuildContent(inner);

		// ── Rechte Spalte: das Schiff (Fracht, Laderaum, Credits – echt) ──
		if (ShowShip) BuildShipPanel(224 + cw + 10, 84, 1266 - (224 + cw + 10), 540);

		// ── Fußleiste: „← Menü“ und „Abdocken“ immer sichtbar ──
		var back = At(this, new KitButton("← Menü", new Vector2(160, 58)) { FontSize = 20 }, 14, 648);
		back.Pressed += () => BackToMenu?.Invoke();
		var undock = At(this, new KitButton("Abdocken", new Vector2(200, 58)) { FontSize = 22, State = KitButtonState.Ready }, 1066, 648);
		undock.Pressed += () => UndockPressed?.Invoke();
	}

	/// <summary>Baut den Inhalt des Mittelteils (Innenbereich ca. 670/1020 × 520 px).</summary>
	protected abstract void BuildContent(Control inner);

	/// <summary>
	/// Rechte Spalte: Schiffskarte mit Hülle (echt), Schild/Sprit (Platzhalter), Laderaum, Credits und Fracht.
	/// </summary>
	protected void BuildShipPanel(float x, float y, float w, float h)
	{
		var u = Ctx.U; var ship = Ctx.Ship;
		var p = At(this, new PanelContainer(), x, y, w, h);
		p.AddThemeStyleboxOverride("panel", Box(PanelBg, null, 10, 0, 12));
		var sv = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		sv.AddThemeConstantOverride("separation", 7);
		p.AddChild(sv);
		sv.AddChild(Text("Dein Schiff", 20, Cyan));
		var sketch = new ShipSketch { CustomMinimumSize = new Vector2(w - 28, 84), Size = new Vector2(w - 28, 84), MouseFilter = MouseFilterEnum.Ignore };
		sv.AddChild(sketch);

		var hullRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		hullRow.AddThemeConstantOverride("separation", 8);
		var hl = Text("Hülle", 15, Dim); hl.CustomMinimumSize = new Vector2(58, 0);
		var hullBar = new KitBar(w - 150, 12, Green);
		hullBar.Set(ship.Hull, ship.Hull > 60f ? Green : Orange, Ship.MaxHull);
		hullRow.AddChild(hl); hullRow.AddChild(hullBar);
		hullRow.AddChild(Text($"{(int)ship.Hull}", 15, White));
		sv.AddChild(hullRow);

		var phRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		phRow.AddThemeConstantOverride("separation", 8);
		var pl = Text("Schild · Sprit", 15, new Color(Dim, 0.7f)); pl.CustomMinimumSize = new Vector2(88, 0);
		phRow.AddChild(pl);
		phRow.AddChild(Text(UiPlaceholders.Tag, 12, new Color(Dim, 0.55f)));
		sv.AddChild(phRow);

		sv.AddChild(Text("Laderaum", 15, Dim));
		var cargoBar = new KitBar(w - 28, 14, Cyan);
		cargoBar.Set(ship.UsedVolume(u), Cyan, ship.CargoCapacity);
		sv.AddChild(cargoBar);
		sv.AddChild(Text($"{ship.UsedVolume(u):0.#} von {ship.CargoCapacity:0} m³ · {(int)ship.Credits} Credits", 14, White));

		sv.AddChild(Text("An Bord", 15, Dim));
		foreach (var (gid, amount) in ship.Cargo)
		{
			if (amount <= 0f) continue;
			var r = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
			r.AddThemeConstantOverride("separation", 8);
			var chip = new ColorRect { Color = GoodColorOf(u, gid), CustomMinimumSize = new Vector2(14, 14), Size = new Vector2(14, 14), MouseFilter = MouseFilterEnum.Ignore };
			r.AddChild(chip);
			r.AddChild(Text($"{u.Goods[gid].Name}  {amount:0.#}", 15, White));
			sv.AddChild(r);
		}
		if (ship.Cargo.Count == 0) sv.AddChild(Text("leer", 14, new Color(Dim, 0.7f)));
	}

	internal static Color GoodColorOf(Universe u, GoodId gid) =>
		u.Goods.TryGetValue(gid, out var g) ? Color.FromHtml(g.Color) : new Color(1f, 0.75f, 0.3f);

	/// <summary>Einfache Schiffs-Silhouette (Dart) als Platzhalter für ein 3D-Modell.</summary>
	public sealed partial class ShipSketch : Control
	{
		public override void _Draw()
		{
			var c = Size / 2f;
			float s = Mathf.Min(Size.X, Size.Y) * 0.42f;
			var pts = new[]
			{
				c + new Vector2(0, -s),
				c + new Vector2(s * 0.55f, s * 0.35f),
				c + new Vector2(s * 0.22f, s * 0.55f),
				c + new Vector2(0, s * 0.32f),
				c + new Vector2(-s * 0.22f, s * 0.55f),
				c + new Vector2(-s * 0.55f, s * 0.35f),
			};
			DrawColoredPolygon(pts, new Color(0.25f, 0.33f, 0.5f));
			var closed = pts.ToList(); closed.Add(pts[0]);
			DrawPolyline(closed.ToArray(), Cyan, 2f, true);
			DrawCircle(c, 3.5f, White);
		}
	}
}
