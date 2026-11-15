using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Ui;
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
	public string Title => DockAreas.LabelFor(AreaId, Ctx?.U);
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

	public void Open() { Visible = true; OnOpen(); Rebuild(); }
	/// <summary>Runde 25: beim Öffnen des Bereichs (Andocken, Wechsel aus dem Dock-Menü) — z. B. eine alte Meldung verwerfen.</summary>
	protected virtual void OnOpen() { }
	/// <summary>Runde 17: Inhalt beim Sitzungswechsel abbauen (Schließungen halten das alte Universum); Open baut neu.</summary>
	public void DropContent() { foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); } _dirty = true; }
	public void Close() => Visible = false;

	/// <summary>Aktualisiert die Anzeige, wenn sich der Simulations-Tick geändert hat (Marktpreise bewegen sich).</summary>
	public void Refresh()
	{
		if (!Visible || Ctx == null) return;
		if (Ctx.U.Tick != _builtTick || _dirty) SafeRebuild();
	}

	/// <summary>
	/// Gerätetest Phase 27: Neuaufbau ohne die Rollposition zu verlieren (die Bereiche bauen sich je Tick neu auf — vorher sprang jede Liste viermal je Sekunde
	/// nach oben) und nie mitten in einem Wisch (der Rollbereich würde unter dem Finger ersetzt).
	/// </summary>
	void SafeRebuild()
	{
		if ((KitScroll.Dragging || KitButton.AnyHeld > 0) && GetChildCount() > 0) { _dirty = true; return; }   // zweiter Gerätetest Phase 28: auch nicht bei gedrückter Taste
		var keep = KitScroll.Capture(this);
		Rebuild();
		KitScroll.Restore(this, keep);
	}

	/// <summary>Setzt die Anzeige nach einer Aktion (z. B. Kauf) sofort neu auf.</summary>
	protected void Invalidate() { _dirty = true; SafeRebuild(); }

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
		string faction = SpaceSim.Core.Model.ActorNames.Tagged(u, st.Owner);   // Phase 29: mit Kürzel   // Phase 9: Besitzer ist ein Akteur (Fraktionen tragen ihren Namen)
		string sector = u.Sectors.TryGetValue(st.Sector, out var sec) ? sec.Name : "?";

		// Runde 34 (UI-ANPASSUNG.md E3): Gerüst aus der Fläche der Dock-Schicht (Core: DockLayout.Shell) — bei 1280 × 720 die bisherigen Lagen
		var L = Shell = DockLayout.Shell(Size.X, Size.Y, UiScale.Div(UiScale.SafeInsets1(), Scale0()), ShowShip);

		// ── Top-Leiste: Station links, Credits und Stationskasse rechts ──
		var top = At(this, new Control { MouseFilter = MouseFilterEnum.Ignore }, L.Top.X, L.Top.Y, L.Top.W, L.Top.H);
		float cx = L.Credits.X - L.Top.X;
		At(top, Text(st.Name, L.Compact ? 24 : 28, Cyan), 0, L.Compact ? 2 : 6, cx - 10, 34);
		At(top, Text($"{type} · {faction} · {sector}", L.Compact ? 14 : 16, Dim), 0, L.Compact ? 36 : 42, cx - 10, 22);
		At(top, Text($"{(int)u.WalletOf(ship).Credits} Credits", L.Compact ? 22 : 26, Orange, HorizontalAlignment.Right), cx, 0, L.Credits.W, 32);
		At(top, Text($"Kasse der Station {st.Credits:0} Cr", L.Compact ? 14 : 16, Dim, HorizontalAlignment.Right), cx, L.Compact ? 34 : 38, L.Credits.W, 22);

		// ── Linke Leiste: Wechsel zwischen den Bereichen (Daten aus DockAreas) ──
		var rail = At(this, new PanelContainer { MouseFilter = MouseFilterEnum.Stop }, L.Rail.X, L.Rail.Y, L.Rail.W, L.Rail.H);
		rail.AddThemeStyleboxOverride("panel", Box(PanelBg, null, 10, 0, 8));
		// Phase 12: bis zu neun Bereiche — die Leiste rollt senkrecht, statt Bereiche zu verdrängen
		var scroll = new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass };
		rail.AddChild(scroll);
		var rv = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		rv.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(rv);
		foreach (var def in DockAreas.Build(u, st, Ctx.Services))
		{
			var b = new KitButton(DockAreas.ShortLabelFor(def.Id), new Vector2(L.Rail.W - 16, 56)) { AlignLeft = true, FontSize = 16, Sub = def.Sub };
			string id = def.Id;
			b.Pressed += () => AreaSelected?.Invoke(id);   // auch Platzhalter-Bereiche öffnen ihre „Bald“-Seite
			if (!def.Available) b.Modulate = new Color(1f, 1f, 1f, 0.5f);   // Sub trägt schon „Bald“
			else if (def.Id == AreaId)
				b.State = KitButtonState.On;
			rv.AddChild(b);
		}
		rv.AddChild(Text($"Schiff: #{ship.Id.Value} (aktiv)", 13, Dim));

		// ── Mittelteil ──
		float cw = L.Center.W;
		var center = At(this, new PanelContainer(), L.Center.X, L.Center.Y, cw, L.Center.H);
		// Gerätetest: seitlicher Innenabstand 16 (Texte klebten am Rahmen)
		var centerBox = Box(PanelBg, Edge, 10, 2, 10);
		centerBox.ContentMarginLeft = 16; centerBox.ContentMarginRight = 16;
		center.AddThemeStyleboxOverride("panel", centerBox);
		var inner = new Control { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(L.InnerW, L.InnerH), Size = new Vector2(L.InnerW, L.InnerH) };
		// Runde 34: auf dem Handy (UI-Faktor > 1) ist der Mittelteil niedriger als im Entwurf — Bereiche, deren Inhalt dann über den Rand reicht (Hangar,
		// Lager, Behörde …), rollen als Ganzes; Bereiche mit eigener Liste (Handel, Werft, Ausrüster) passen und rollen nur innen
		var areaScroll = new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass };
		center.AddChild(areaScroll);
		areaScroll.AddChild(inner);
		BuildContent(inner);
		float bottom = 0f;
		foreach (var ch in inner.GetChildren()) if (ch is Control cc && cc.Visible) bottom = MathF.Max(bottom, cc.Position.Y + MathF.Max(cc.Size.Y, cc.GetCombinedMinimumSize().Y));
		if (bottom > L.InnerH + 1f) inner.CustomMinimumSize = new Vector2(L.InnerW, bottom + 8f);

		// ── Rechte Spalte: das Schiff (Fracht, Laderaum, Credits – echt) ──
		if (ShowShip) BuildShipPanel(L.Ship.X, L.Ship.Y, L.Ship.W, L.Ship.H);

		// ── Fußleiste: „← Menü“ und „Abdocken“ immer sichtbar (kompakt: „← Menü“ unter der Leiste, „Abdocken“ oben rechts) ──
		var back = At(this, new KitButton("← Menü", new Vector2(L.Back.W, L.Back.H)) { FontSize = 20 }, L.Back.X, L.Back.Y);
		back.Pressed += () => BackToMenu?.Invoke();
		var undock = At(this, new KitButton("Abdocken", new Vector2(L.Undock.W, L.Undock.H)) { FontSize = 22, State = KitButtonState.Ready }, L.Undock.X, L.Undock.Y);
		undock.Pressed += () => UndockPressed?.Invoke();
	}

	/// <summary>Runde 34: zuletzt gerechnetes Gerüst (Selbsttest, Meldungslage).</summary>
	public DockShell? Shell { get; private set; }
	/// <summary>Maßstab der Dock-Schicht (Eltern-Steuerelement).</summary>
	float Scale0() => GetParent() is Control p && p.Scale.X > 0f ? p.Scale.X : 1f;
	/// <summary>Runde 34: Lücke der Kopfleiste für die große Meldung, in Bildpunkten (null = nicht gebaut).</summary>
	public Rect2? AlertSlotGlobal => Shell is { } l && IsVisibleInTree()
		? new Rect2(GetGlobalTransform() * new Vector2(l.AlertSlot.X, l.AlertSlot.Y), new Vector2(l.AlertSlot.W, l.AlertSlot.H) * GetGlobalTransform().Scale.X) : null;

	public override void _Notification(int what)
	{
		if (what == NotificationResized && Visible && Ctx is not null && GetChildCount() > 0) { _dirty = true; SafeRebuild(); }
	}

	/// <summary>Baut den Inhalt des Mittelteils (Innenbereich: <see cref="DockShell.InnerW"/> × <see cref="DockShell.InnerH"/>; bei 1280 × 720 658/1008 × 520).</summary>
	protected abstract void BuildContent(Control inner);

	/// <summary>
	/// Rechte Spalte: Schiffskarte mit Hülle, Schild (Phase 23), Sprit, Laderaum, Credits und Fracht (alles echt).
	/// </summary>
	protected void BuildShipPanel(float x, float y, float w, float h)
	{
		var u = Ctx.U; var ship = Ctx.Ship;
		var p = At(this, new PanelContainer(), x, y, w, h);
		p.AddThemeStyleboxOverride("panel", Box(PanelBg, null, 10, 0, 12));
		var sv = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		sv.AddThemeConstantOverride("separation", 7);
		// Runde 34: die Spalte rollt, wenn die Fracht länger ist als die (auf dem Handy kürzere) Spalte — vorher wuchs die Karte über den Rand
		var shipScroll = new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(1, 1) };
		p.AddChild(shipScroll);
		shipScroll.AddChild(sv);
		sv.AddChild(Text("Dein Schiff", 20, Cyan));
		var sketch = new ShipSketch { CustomMinimumSize = new Vector2(w - 28, 84), Size = new Vector2(w - 28, 84), MouseFilter = MouseFilterEnum.Ignore };
		sv.AddChild(sketch);

		var hullRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		hullRow.AddThemeConstantOverride("separation", 8);
		var hl = Text("Hülle", 15, Dim); hl.CustomMinimumSize = new Vector2(58, 0);
		var hullBar = new KitBar(w - 150, 12, Green);
		hullBar.Set(ship.Hull, ship.Hull > 60f ? Green : Orange, ship.MaxHull);
		hullRow.AddChild(hl); hullRow.AddChild(hullBar);
		hullRow.AddChild(Text($"{(int)ship.Hull}", 15, White));
		sv.AddChild(hullRow);

		var phRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		phRow.AddThemeConstantOverride("separation", 8);
		var fuelRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		fuelRow.AddThemeConstantOverride("separation", 8);
		var fl = Text("Sprit", 15, Dim); fl.CustomMinimumSize = new Vector2(58, 0);
		float fuelCap = ship.Stats.FuelCapacity;
		var fuelBar = new KitBar(w - 150, 12, Orange);
		fuelBar.Set(ship.TotalFuel, fuelCap > 0f && ship.TotalFuel < 0.2f * fuelCap ? Red : Orange, MathF.Max(1f, fuelCap));
		fuelRow.AddChild(fl); fuelRow.AddChild(fuelBar);
		fuelRow.AddChild(Text(fuelCap > 0f ? $"{(int)ship.TotalFuel}" : "–", 15, White));
		fuelRow.Visible = u.Rules.FuelActive;   // Phase 14: Tankanzeige nur mit Treibstoff
		sv.AddChild(fuelRow);

		// Phase 23: Schild echt (aktuell/Kapazität; „–“ ohne Schildmodul)
		var pl = Text("Schild", 15, Dim); pl.CustomMinimumSize = new Vector2(58, 0);
		phRow.AddChild(pl);
		var shieldBar = new KitBar(w - 150, 12, Cyan);
		shieldBar.Set(ship.ShieldNow, Cyan, MathF.Max(1f, ship.Stats.Shield));
		phRow.AddChild(shieldBar);
		phRow.AddChild(Text(ship.Stats.Shield > 0f ? $"{(int)ship.ShieldNow}" : "–", 15, White));
		sv.AddChild(phRow);

		sv.AddChild(Text("Laderaum", 15, Dim));
		var cargoBar = new KitBar(w - 28, 14, Cyan);
		cargoBar.Set(ship.UsedVolume(u), Cyan, ship.CargoCapacity);
		sv.AddChild(cargoBar);
		sv.AddChild(Text($"{ship.UsedVolume(u):0.#} von {ship.CargoCapacity:0} m³ · {(int)u.WalletOf(ship).Credits} Credits", 14, White));

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
