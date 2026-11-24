using System.Linq;
using System;
using Godot;
using SpaceAdventure.Ui.Kit;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kleines Auswahlmenü beim Andocken (UI.md Abschnitt 3, Vorlage prototypes/FinalUi.cs „Menu“): oben die Karten
/// der angedockten Schiffe (Phase 12 echt: aktives Schiff, eigene im Hangar, fremde mit Besitzer, aus <see cref="SpaceSim.Core.Model.Ship.DockedAt"/>),
/// darunter die Bereiche mit Untertitel (Daten aus <see cref="DockAreas.Build"/>, rollbares Raster für bis zu neun Kacheln), unten die große Taste
/// „Abdocken“ und die Liegegebühr der hier geparkten eigenen Schiffe. Als IScreen in der Registry; der Aufrufer setzt <see cref="Ctx"/>.
/// </summary>
public sealed partial class DockMenu : Control, IScreen, ISessionScreen, IMessageStrip
{
	public string Id => "dock.menu";
	public string Title => Ctx != null ? Ctx.Station.Name : "Dock";
	public Control Node => this;
	public bool IsOpen => Visible;

	/// <summary>Kontext des Docks (Universum, Station, Schiff, Dienste).</summary>
	public DockContext Ctx { get; set; } = null!;

	/// <summary>Bereich wurde angetippt (Id aus <see cref="DockAreas"/>; Platzhalter öffnen ihre Platzhalterseite).</summary>
	public event Action<string>? AreaSelected;
	/// <summary>Große Taste „Abdocken“ unten (zu Fuß gedämpft — erst ein Schiff betreten).</summary>
	public event Action? UndockPressed;
	/// <summary>Taste „Schiff verlassen“ (an Bord, angedockt): Core <see cref="SpaceSim.Core.Simulation.PlayerFleet.LeaveShip"/>.</summary>
	public event Action? LeavePressed;
	/// <summary>Runde 23: „≡ Menü“ (Kamera, Ton, Speichern/Laden/Neues Spiel) auch angedockt.</summary>
	public event Action? MenuPressed;

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
		if (Ctx.U.Tick == _builtTick) return;
		// Nutzer: „Aussteigen klappte erst nach mehrfachen Tippen“ — das Menü wurde jede Sekunde komplett neu gebaut, auch unter dem Finger. Jetzt nur, wenn
		// sich Angezeigtes ändert (angedockte Schiffe, Kasse, Hülle, zu Fuß), und nie während eine Taste gehalten wird.
		if (KitButton.AnyHeld > 0) return;
		if (Signature() == _sig) { _builtTick = Ctx.U.Tick; return; }
		Rebuild();
	}

	string _sig = "";
	string Signature()
	{
		var u = Ctx.U; var st = Ctx.Station; var ship = Ctx.Ship;
		var docked = SpaceSim.Core.Simulation.Hangar.DockedShips(u, st).Select(s => s.Id.Value);
		return $"{string.Join(",", docked)}|{u.PlayerOnFoot}|{u.PlayerShip.Value}|{ship.DockedAt.Value}|{(int)ship.Hull}|{(int)u.WalletOf(ship).Credits}|{(int)st.Credits}";
	}

	public override void _Draw()
	{
		if (!Visible) return;
		// Runde 45: der Hintergrund zeigt die angedockte Station (Kamera rotiert) — links kaum Abdunklung, zur Karte (rechts) dichter
		var from = new Color(0.01f, 0.02f, 0.05f, 0.10f);
		var to = new Color(0.01f, 0.02f, 0.05f, 0.68f);
		DrawPolygon(new[] { new Vector2(0, 0), new Vector2(Size.X, 0), new Vector2(Size.X, Size.Y), new Vector2(0, Size.Y) },
			new[] { from.Lerp(to, 0.4f), to, to, from });
	}

	/// <summary>Runde 17 (Speicher wächst mit „Neues Spiel“): die Schiffskarten halten das alte Universum — beim Sitzungswechsel alles abbauen.</summary>
	public void ResetSession() { foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); } }

	SpaceSim.Core.Ui.UiRect _card;
	/// <summary>Runde 34: Karte in Bildpunkten (Meldung/Hinweis links daneben); leer, solange nicht gebaut.</summary>
	SpaceSim.Core.Ui.UiRect _strip;
	/// <summary>Runde 34b: Meldungsstreifen (Core: DockLayout.MenuStrip) in Bildpunkten.</summary>
	public Rect2? MessageStripGlobal => _strip.W > 0f && IsVisibleInTree() ? new Rect2(GetGlobalTransform() * new Vector2(_strip.X, _strip.Y), new Vector2(_strip.W, _strip.H) * GetGlobalTransform().Scale.X) : null;
	public Rect2? CardGlobal => _card.W > 0f && IsVisibleInTree() ? new Rect2(GetGlobalTransform() * new Vector2(_card.X, _card.Y), new Vector2(_card.W, _card.H) * GetGlobalTransform().Scale.X) : null;

	public override void _Notification(int what)
	{
		if (what == NotificationResized && Visible && Ctx is not null && GetChildCount() > 0) Rebuild();
	}

	void Rebuild()
	{
		_builtTick = Ctx.U.Tick;
		_sig = Signature();
		foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }

		var u = Ctx.U; var st = Ctx.Station; var ship = Ctx.Ship;
		string type = u.StationTypes.TryGetValue(st.TypeKey, out var t) ? t.Name : st.TypeKey;
		string faction = SpaceSim.Core.Model.ActorNames.Tagged(u, st.Owner);   // Phase 29: mit Kürzel   // Phase 9: Besitzer ist ein Akteur (Fraktionen tragen ihren Namen)
		string sector = u.Sectors.TryGetValue(st.Sector, out var sec) ? sec.Name : "?";

		// Karte mit freier Platzierung (ein PanelContainer würde alle Kinder strecken); Innenrand 20 px
		// Runde 34 (E3): Karte aus der Fläche der Dock-Schicht (Core: DockLayout.MenuCard) — bei 1280 × 720 wie bisher (350, 30, 580 × 662), sonst mittig
		// und höchstens so hoch wie die Fläche (die Bereichskacheln rollen dann)
		float k0 = GetParent() is Control lp && lp.Scale.X > 0f ? lp.Scale.X : 1f;
		var cr = SpaceSim.Core.Ui.DockLayout.MenuCard(Size.X, Size.Y, UiScale.Div(UiScale.SafeInsets1(), k0));
		_card = cr;
		_strip = SpaceSim.Core.Ui.DockLayout.MenuStrip(Size.X, Size.Y, UiScale.Div(UiScale.SafeInsets1(), k0));
		float ch = cr.H;
		var card = CardBg(this, cr.X, cr.Y, cr.W, ch, new Color(PanelSolid, 0.86f), Edge, 16, 2);   // UI-Umbau: Glas   // Runde 23: Fußzeilen berührten den Rahmen

		// Titel: Station, Sektor, Fraktion, Typ
		At(card, Text(st.Name, 26, Cyan), 20, 14, 460, 32);
		// Runde 23 (Handy-Test: „Neues Spiel“ war angedockt nicht erreichbar — das Hamburger-Menü gehört zur Flugansicht): Menü auch im Dock-Menü
		var menu = At(card, new KitButton("≡ Menü", new Vector2(84, 40)) { FontSize = 15 }, 476, 10);
		menu.Pressed += () => MenuPressed?.Invoke();
		// Runde 23 (Handy-Test: „… · Kass…“ abgeschnitten): die Kasse bleibt immer lesbar — erst fällt der Typ weg, dann wird die Fraktion gekürzt
		string cash = $" · Kasse {st.Credits:0} Cr";
		var subFont = ThemeDB.FallbackFont;
		string sub = $"{sector} · {faction} · {type}";
		if (subFont.GetStringSize(sub + cash, HorizontalAlignment.Left, -1, 15).X > 540f) sub = $"{sector} · {faction}";
		sub = SpaceAdventure.Ui.Kit.KitButton.Fit(subFont, sub, 15, 540f - subFont.GetStringSize(cash, HorizontalAlignment.Left, -1, 15).X - 4f);
		TestSubtitle = sub + cash;
		At(card, Text(sub + cash, 15, Dim), 20, 50, 540, 22);

		// Angedockte Schiffe oben als Karten (Phase 12: echt — das aktive Schiff, eigene im Hangar, fremde mit Besitzer).
		// Zu Fuß gibt es keine aktiv-betrete-Karte: Alle hier liegenden Schiffe zählen gleich (das aktive liegt geparkt dabei oder woanders).
		bool onFoot = u.PlayerOnFoot;
		var docked = SpaceSim.Core.Simulation.Hangar.DockedShips(u, st).Where(s => onFoot || s.Id != ship.Id).ToList();
		var own = docked.Where(s => s.IsPlayer).ToList();
		var foreign = docked.Where(s => !s.IsPlayer).ToList();
		At(card, Text($"ANGEDOCKT · {docked.Count + (onFoot ? 0 : 1)} Schiff{(docked.Count + (onFoot ? 0 : 1) == 1 ? "" : "e")} ({own.Count} eigene im Hangar, {foreign.Count} fremde)", 12, Dim), 20, 80, 540, 18);
		var chips = At(card, new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }, 20, 100, 540, 92);
		chips.AddThemeConstantOverride("separation", 8);
		if (!onFoot) chips.AddChild(new ShipChip(u, ship, true));   // zu Fuß: keine aktiv-Karte (das Schiff liegt geparkt in der Liste)
		var shown = own.Concat(foreign).Take(3).ToList();
		foreach (var other in shown) chips.AddChild(new ShipChip(u, other, false));
		if (docked.Count > shown.Count) chips.AddChild(new ShipChip(u, null, false, docked.Count - shown.Count));

		// Bereiche darunter, je mit Untertitel; bis zu neun Kacheln — das Raster rollt bei Bedarf
		var defs = DockAreas.Build(u, st, Ctx.Services, ship);
		var scroll = At(card, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 20, 198, 552, ch - 314 - (onFoot ? 0 : 66));
		var grid = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
		grid.AddThemeConstantOverride("h_separation", 10);
		grid.AddThemeConstantOverride("v_separation", 8);
		scroll.AddChild(grid);
		foreach (var def in defs)
		{
			if (!def.Available) continue;   // UI-Umbau: nur Dienste, die es hier gibt (vorher graue „Nicht hier“-Kacheln)
			var b = new KitButton(def.Label, new Vector2(262, 62)) { AlignLeft = true, FontSize = 19, Sub = def.Sub };
			string id = def.Id;
			b.Pressed += () => AreaSelected?.Invoke(id);   // auch Platzhalter öffnen ihre „Bald“-Seite
			if (!def.Available) b.Modulate = new Color(1f, 1f, 1f, 0.5f);   // Sub trägt schon „Bald“/„Nicht hier“
			grid.AddChild(b);
		}
		TestTiles = defs.Count(d => d.Available);

		// Große Taste „Abdocken“ unten; daneben „Schiff verlassen“ (an Bord, angedockt). Zu Fuß ist Abdocken aus (— erst Schiff betreten).
		var undock = At(card, onFoot
			? new KitButton("Abdocken – du bist zu Fuß", new Vector2(540, 64)) { FontSize = 20, State = KitButtonState.Disabled, Sub = "im Hangar ein Schiff betreten" }
			: new KitButton("Abdocken", new Vector2(348, 64)) { FontSize = 24, State = KitButtonState.Ready }, 20, ch - 106);
		undock.Pressed += () => UndockPressed?.Invoke();
		// UI-Umbau: Frachtband über „Abdocken“ — Füllstand, Farbleiste je Ware, größte Posten; Tipp öffnet das Inventar
		if (!onFoot)
		{
			float cap = MathF.Max(1f, ship.CargoCapacity), used = ship.UsedVolume(u) + ship.MissionCargo;
			var parts = ship.Cargo.Where(kv => kv.Value > 0.01f && u.Goods.ContainsKey(kv.Key)).Select(kv => (G: u.Goods[kv.Key], A: kv.Value)).OrderByDescending(p => p.A * p.G.VolumePerUnit).ToList();
			string cargoSub = parts.Count == 0 && ship.MissionCargo <= 0.01f ? "leer" : string.Join(" · ", parts.Take(3).Select(p => $"{p.G.Name} {p.A:0}")) + (ship.MissionCargo > 0.01f ? (parts.Count > 0 ? " · " : "") + "versiegelt" : "");
			var band = At(card, new KitButton($"Fracht {used:0}/{ship.CargoCapacity:0} m³", new Vector2(552, 58)) { AlignLeft = true, FontSize = 16, Sub = cargoSub + "  ▸ Inventar" }, 20, ch - 172);
			var bar = At(band, new FlightHud.CargoBar(), 300, 22, 236, 14);
			foreach (var p in parts) bar.Segments.Add((Color.FromHtml(p.G.Color), p.A * p.G.VolumePerUnit / cap));
			if (ship.MissionCargo > 0.01f) bar.Segments.Add((FlightHud.MissionGold, ship.MissionCargo / cap));
			band.Pressed += () => InventoryPressed?.Invoke();
		}
		if (!onFoot)
		{
			var leave = At(card, new KitButton("Schiff verlassen", new Vector2(184, 64)) { FontSize = 15, Sub = "zu Fuß hier" }, 380, ch - 106);
			leave.Pressed += () => LeavePressed?.Invoke();
		}
		if (onFoot)
		{
			At(card, Text("Du bist zu Fuß in der Station — im Hangar ein Schiff übernehmen oder mieten (Leihschiff).", 13, Cyan), 20, ch - 38, 540, 18);
		}
		else
		{
			float fee = own.Sum(s => SpaceSim.Core.Simulation.Hangar.BerthFeeNow(u, s));
			string berth = st.Owner == u.PlayerActor ? "eigene Station: Liegeplätze gratis"
				: fee > 0f ? $"Liegegebühr hier {fee:0} Cr/Tag für {own.Count} Schiff{(own.Count == 1 ? "" : "e")}" : "Liegegebühr nur für geparkte Schiffe";
			At(card, Text($"Hülle {(int)ship.Hull}/{(int)ship.MaxHull}{(SpaceSim.Core.Services.RepairService.NeedsRepair(ship) ? " – Reparatur im Dienst „Reparatur“" : "")} · {(int)u.WalletOf(ship).Credits} Credits", 13, Dim, HorizontalAlignment.Center), 20, ch - 38, 540, 18);
			At(card, Text(berth, 13, Dim, HorizontalAlignment.Center), 20, ch - 22, 540, 18);
		}
	}

	/// <summary>Runde 23: Unterzeile des Titels (Selbsttest: Kasse sichtbar).</summary>
	public string TestSubtitle { get; private set; } = "";

	/// <summary>UI-Umbau: Frachtband angetippt (Inventar öffnen).</summary>
	public event Action? InventoryPressed;

	/// <summary>Für den Selbsttest: Zahl der Bereichskacheln.</summary>
	public int TestTiles { get; private set; }

	/// <summary>Karte eines angedockten Schiffs (Hülle echt); null = „+N weitere“.</summary>
	sealed partial class ShipChip : Control
	{
		readonly SpaceSim.Core.Model.Universe _u;
		readonly SpaceSim.Core.Model.Ship? _ship;
		readonly bool _sel;
		readonly int _more;

		public ShipChip(SpaceSim.Core.Model.Universe u, SpaceSim.Core.Model.Ship? ship, bool sel, int more = 0)
		{
			_u = u; _ship = ship; _sel = sel; _more = more;
			MouseFilter = MouseFilterEnum.Ignore;
			CustomMinimumSize = new Vector2(126, 92);
			Size = CustomMinimumSize;
		}

		public override void _Draw()
		{
			bool own = _ship?.IsPlayer ?? false;
			DrawRect(new Rect2(Vector2.Zero, Size), _sel ? new Color(0.14f, 0.26f, 0.46f, 0.98f) : new Color(0.04f, 0.07f, 0.13f, 0.95f));
			DrawRect(new Rect2(Vector2.Zero, Size), _sel ? Cyan : own ? new Color(Cyan, 0.6f) : new Color(Dim, 0.4f), false, 2f);
			var font = ThemeDB.FallbackFont;
			if (_ship != null)
			{
				var c = new Vector2(63, 24); float s = 15f;
				var pts = new[]
				{
					c + new Vector2(0, -s), c + new Vector2(s * 0.55f, s * 0.35f), c + new Vector2(s * 0.22f, s * 0.55f),
					c + new Vector2(0, s * 0.32f), c + new Vector2(-s * 0.22f, s * 0.55f), c + new Vector2(-s * 0.55f, s * 0.35f),
				};
				DrawColoredPolygon(pts, own ? new Color(0.25f, 0.33f, 0.5f) : new Color(0.35f, 0.3f, 0.25f));
				var closed = new System.Collections.Generic.List<Vector2>(pts) { pts[0] };
				DrawPolyline(closed.ToArray(), own ? Cyan : Orange, 1.5f, true);
				string model = _u.ShipModels.TryGetValue(_ship.ModelKey, out var m) ? m.Name : "";
				string label = $"#{_ship.Id.Value} {model}";
				DrawnText.Clear(this);
				UiKit.DrawFit(this, 0, font, new Vector2(3, 58), label, 13, 120, White, HorizontalAlignment.Center, middle: true);
				string who = _sel ? "aktiv" : own ? (_ship.IsRental ? "Leihschiff" : "im Hangar") : SpaceSim.Core.Model.ActorNames.Tagged(_u, _ship.Owner);
				UiKit.DrawFit(this, 1, font, new Vector2(3, 75), who, 12, 120, own || _sel ? Cyan : Dim, HorizontalAlignment.Center);
				DrawRect(new Rect2(14, 82, 98, 4), new Color(0, 0, 0, 0.5f));
				DrawRect(new Rect2(14, 82, 98 * Mathf.Clamp(_ship.Hull / _ship.MaxHull, 0f, 1f), 4), _ship.Hull > _ship.MaxHull * 0.6f ? Green : Orange);
			}
			else
			{
				DrawnText.Clear(this);
				UiKit.DrawFit(this, 0, font, new Vector2(3, 44), $"+{_more}", 24, 120, new Color(Dim, 0.9f), HorizontalAlignment.Center);
				UiKit.DrawFit(this, 1, font, new Vector2(3, 66), "weitere im Hangar", 12, 120, new Color(Dim, 0.8f), HorizontalAlignment.Center);
			}
		}
	}
}
