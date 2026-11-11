using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Sektor“ (UI.md Abschnitt 4): Draufsicht auf den gewählten Sektor (Standard: aktueller):
/// Stationen, Rohstoffgürtel (<see cref="ResourceField"/>), Gates mit Zielsektor, NPC-Schiffe als Symbole
/// (echt über <see cref="Materializer"/>) und der Spieler. Umschalter Universum/Sektor oben. Tippen auf
/// Station/Gate wählt sie und zeigt ihre Info rechts; Knöpfe „Autopilot hierhin“ (echt: nur im aktuellen
/// Sektor bzw. über Route – setzt das Autopilot-Ziel in Main) und „Info“.
/// </summary>
public sealed partial class SectorScreen : CommandScreen
{
	public override string Id => "cc.sector";

	/// <summary>Angezeigter Sektor (null = Spielersektor); setzt der Aufrufer vor dem Öffnen.</summary>
	public SectorId? SelectedSector { get; set; }

	/// <summary>„Autopilot hierhin“: Ziel für Main (NavTarget + optionale Station zum Andocken).</summary>
	public event Action<NavTarget, StationId?>? AutopilotRequested;

	SectorId Selected => SelectedSector ?? Ctx.U.PlayerSector;

	StationId? _selStation;
	GateId? _selGate;
	bool _showInfo;

	SectorMapCanvas _map = null!;

	/// <summary>Test-Hook: Sektor wechseln.</summary>
	public void TestSelectSector(SectorId s) { SelectedSector = s; _selStation = null; _selGate = null; Invalidate(); }
	/// <summary>Test-Hook: Station antippen.</summary>
	public void TestSelectStation(StationId s) { _selStation = s; _selGate = null; Invalidate(); }
	/// <summary>Test-Hook: „Autopilot hierhin“ auslösen.</summary>
	public void TestAutopilotHere() => AutopilotHere();

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		var sel = Selected;

		// ── Oben: Umschalter Universum/Sektor ──
		var segU = At(inner, new KitButton("Universum", new Vector2(120, 40)) { FontSize = 17 }, 12, 10);
		segU.Pressed += () => RaiseAreaSelected(CommandAreas.Universe);
		At(inner, new KitButton("Sektor", new Vector2(120, 40)) { FontSize = 17, State = KitButtonState.On }, 138, 10);
		var s = u.Sectors[sel];
		At(inner, Text($"{s.Name} · {FactionName(u, s.Owner)}", 17, MapOverlay.FactionColor(s.Owner)), 280, 18, 400, 24);

		// ── Karte links ──
		_map = At(inner, new SectorMapCanvas { MouseFilter = MouseFilterEnum.Stop }, 0, 56, 700, 464);
		_map.Setup(this);
		_map.StationTapped += id => { _selStation = id; _selGate = null; _showInfo = false; Invalidate(); };
		_map.GateTapped += id => { _selGate = id; _selStation = null; Invalidate(); };

		// ── Info-Karte rechts ──
		BuildDetail(inner, sel);
	}

	void BuildDetail(Control inner, SectorId sel)
	{
		var u = Ctx.U;
		var s = u.Sectors[sel];
		var card = CardBg(inner, 712, 56, 300, 464, Card);

		if (_selStation is { } sid && u.Stations.TryGetValue(sid, out var st))
		{
			string type = u.StationTypes.TryGetValue(st.TypeKey, out var t) ? t.Name : st.TypeKey;
			At(card, Text(st.Name, 21, Cyan), 16, 10, 268, 28);
			At(card, Text($"{type} · {u.ActorName(st.Owner)} · {SectorName(u, st.Sector)}", 14, Dim), 16, 40, 268, 20);
			At(card, Text($"Kasse {st.Credits:0} Cr · {st.Market.Count} Waren am Markt", 14, White), 16, 66, 268, 20);
			if (ConstructionInfo.Of(u, st) is { } site)
			{
				// Phase 8: Baustelle — Zieltyp, Status und Material (echt aus ConstructionInfo)
				At(card, Text($"Baut: {site.TypeName} · {100f * site.Progress:0} %", 15, Orange), 16, 94, 268, 20);
				At(card, Text(site.State, 13, White), 16, 116, 268, 18);
				At(card, Text($"Bauschiff: {site.Builder}", 13, Dim), 16, 136, 268, 18);
				float sy = 160;
				foreach (var l in site.Lines)
				{
					At(card, Text($"{l.Name}  {l.Delivered:0}/{l.Required:0}", 13, l.Delivered >= l.Required ? Green : White), 24, sy, 268, 18);
					sy += 19;
				}
			}
			else if (_showInfo)
			{
				// „Info“: Marktübersicht der Station (echt aus Station.Market)
				At(card, Text("Markt (Auswahl)", 14, Dim), 16, 94, 268, 18);
				float my = 116;
				foreach (var (gid, e) in st.Market.OrderBy(kv => kv.Key.Value).Take(9))
				{
					At(card, Text($"{GoodName(u, gid)}  {e.Amount:0}/{e.Target:0} · {e.Price:0.0} Cr", 13, White), 24, my, 268, 18);
					my += 19;
				}
			}
			else
			{
				At(card, Text("Tippe „Info“ für die Marktübersicht.", 13, new Color(Dim, 0.75f)), 16, 94, 268, 18);
			}
		}
		else if (_selGate is { } gid && u.Gates.TryGetValue(gid, out var g))
		{
			var target = u.Sectors[u.Gates[g.Target].Sector];
			At(card, Text($"Gate → {target.Name}", 21, Cyan), 16, 10, 268, 28);
			// Phase 10: Zustand und Urteil des Kontrollpunkts für dich (echt aus AccessInfo)
			var gp = AccessInfo.ForGate(u, g, u.Ships[u.PlayerShip]);
			Color gc = gp.Denied || !g.Open ? Red : gp.Toll > 0f ? Orange : Green;
			At(card, Text($"Zustand: {gp.State}", 15, gc), 16, 42, 268, 20);
			WrappedText(card, gp.Verdict, 16, 66, 268, 13, gc, 16);
			At(card, Text($"Sprung von {s.Name} nach {target.Name}", 14, White), 16, 104, 268, 20);
			At(card, Text("„Autopilot hierhin“ fliegt zum Gate.", 13, new Color(Dim, 0.75f)), 16, 128, 268, 18);
		}
		else
		{
			int npcs = UniverseScreen.ShipCountOf(u, sel);
			At(card, Text(s.Name, 21, Cyan), 16, 10, 268, 28);
			// Phase 14: Planet des Sektors (Kulisse der 3D-Ansicht, Abnehmer der Exportsenke)
			TestPlanet = s.Planet?.Name ?? "";
			var pol = AccessInfo.ForSector(u, sel, u.Ships[u.PlayerShip]);
			At(card, Text($"Besitzer: {pol.Owner} · {pol.Stage}", 14, MapOverlay.FactionColor(s.Owner)), 16, 40, 268, 20);
			if (s.Planet is { } planet) At(card, Text($"Planet {planet.Name} · {s.Stations.Count} Stationen · {s.Fields.Count} Gürtel", 13, White), 16, 64, 268, 20);
			else At(card, Text($"{s.Stations.Count} Stationen · {s.Fields.Count} Gürtel · {s.Gates.Count} Gate{(s.Gates.Count == 1 ? "" : "s")}", 14, White), 16, 64, 268, 20);
			At(card, Text($"{npcs} fremde Schiffe im Sektor", 14, White), 16, 86, 268, 20);
			// Phase 10: Sperrstufe statt Platzhalter-Sicherheit
			Color lc = pol.PlayerDenied ? Red : pol.EntryLevel > 0 ? Orange : Green;
			float py = WrappedText(card, $"Sperre: {pol.Lock}", 16, 110, 268, 13, lc, 16);
			py = WrappedText(card, $"Für dich: {pol.ForPlayer}", 16, py, 268, 13, lc, 16);
			// Phase 11: Lagekarte (echt)
			var sec = SecurityInfo.ForSector(u, sel, u.Ships[u.PlayerShip]);
			py = WrappedText(card, $"Lage: {sec.Summary} · {sec.Patrols} Streife{(sec.Patrols == 1 ? "" : "n")}", 16, py + 2, 268, 13, sec.Threat >= 1f ? Orange : new Color(Dim, 0.95f), 16);
			At(card, Text("Tippe eine Station oder ein Gate an.", 13, new Color(Dim, 0.75f)), 16, py + 8, 268, 18);
		}

		// Knöpfe unten: „Autopilot hierhin“ (echt, nur aktueller Sektor bzw. über Route) und „Info“
		var bAuto = At(card, new KitButton("Autopilot hierhin", new Vector2(268, 52)) { FontSize = 17 }, 16, 300);
		if (CanAutopilot(u, sel, out var nav, out var dock))
			bAuto.Pressed += () => { if (CanAutopilot(u, sel, out var n2, out var d2)) AutopilotRequested?.Invoke(n2, d2); };
		else { bAuto.State = KitButtonState.Disabled; bAuto.Detail = sel == u.PlayerSector ? "erst Station/Gate antippen" : "kein Weg (Gates gesperrt)"; }
		var bInfo = At(card, new KitButton("Info", new Vector2(130, 48)) { FontSize = 17 }, 16, 360);
		if (_selStation != null) bInfo.Pressed += () => { _showInfo = !_showInfo; Invalidate(); };
		else { bInfo.State = KitButtonState.Disabled; bInfo.Detail = "Station antippen"; }
		var bBack = At(card, new KitButton("← Universum", new Vector2(130, 48)) { FontSize = 17 }, 154, 360);
		bBack.Pressed += () => RaiseAreaSelected(CommandAreas.Universe);
	}

	/// <summary>Phase 10: Mobilitätsprofil des Spielerschiffs (Routen respektieren Sperren).</summary>
	static MobilityProfile PlayerProfile(Universe u) => GateAccess.ProfileOf(u, u.Ships[u.PlayerShip]);

	/// <summary>Ist „Autopilot hierhin“ möglich und wohin? Echt über <see cref="Navigator"/>/<see cref="Universe.FindRoute"/> mit deinem Profil.</summary>
	bool CanAutopilot(Universe u, SectorId sel, out NavTarget nav, out StationId? dock)
	{
		nav = default; dock = null;
		if (_selStation is { } sid && u.Stations.TryGetValue(sid, out var st))
		{
			if (st.Sector != u.PlayerSector) { if (!u.IsReachable(u.PlayerSector, st.Sector, PlayerProfile(u))) return false; nav = Navigator.ForSector(u, st.Sector, u.PlayerShip); return true; }
			nav = Navigator.ForStation(u, sid, u.PlayerShip); dock = sid; return true;
		}
		if (_selGate is { } gid && u.Gates.TryGetValue(gid, out var g))
		{
			var target = u.Gates[g.Target].Sector;
			if (sel != u.PlayerSector && !u.IsReachable(u.PlayerSector, sel, PlayerProfile(u))) return false;
			nav = Navigator.ForSector(u, target, u.PlayerShip); return true;
		}
		if (sel == u.PlayerSector) return false;   // nichts angetippt
		if (!u.IsReachable(u.PlayerSector, sel, PlayerProfile(u))) return false;
		nav = Navigator.ForSector(u, sel, u.PlayerShip); return true;
	}

	void AutopilotHere()
	{
		if (CanAutopilot(Ctx.U, Selected, out var nav, out var dock)) AutopilotRequested?.Invoke(nav, dock);
	}

	/// <summary>Draufsicht eines Sektors: Stationen, Gürtel, Gates mit Ziel, NPC-Schiffe (Materializer), Spieler.</summary>
	sealed partial class SectorMapCanvas : Control
	{
		SectorScreen _owner = null!;
		Universe _u = null!;
		public event Action<StationId>? StationTapped;
		public event Action<GateId>? GateTapped;

		public void Setup(SectorScreen owner)
		{
			_owner = owner;
			_u = owner.Ctx.U;
		}

		Vector2 Place(Vec2 world)
		{
			return Size / 2f + new Vector2(world.X * ContentScale(), world.Y * ContentScale());
		}

		/// <summary>Zoom auf den Inhalt (Stationen, Gürtel, Gates, Spieler), nicht auf den leeren Sektorrand.</summary>
		float ContentScale()
		{
			var s = _u.Sectors[_owner.Selected];
			float maxD = 260f;
			foreach (var sid in s.Stations) maxD = Mathf.Max(maxD, _u.Stations[sid].Position.Length);
			foreach (var f in s.Fields) maxD = Mathf.Max(maxD, f.Position.Length + SpaceSim.Core.Mining.MiningRules.BeltRadius);
			foreach (var gid in s.Gates) maxD = Mathf.Max(maxD, _u.Gates[gid].Position.Length);
			if (s.Id == _u.PlayerSector) maxD = Mathf.Max(maxD, _u.Ships[_u.PlayerShip].Position.Length + 40f);
			return (Mathf.Min(Size.X, Size.Y) / 2f - 36f) / maxD;
		}

		public override void _GuiInput(InputEvent @event)
		{
			if (!Pointer.TryRead(@event, out _, out var pos, out bool pressed, out bool motion) || motion || pressed) return;
			var s = _u.Sectors[_owner.Selected];
			StationId? st = null; float sd = 24f;
			foreach (var sid in s.Stations)
			{
				float d = Place(_u.Stations[sid].Position).DistanceTo(pos);
				if (d < sd) { sd = d; st = sid; }
			}
			if (st is { } station) { StationTapped?.Invoke(station); AcceptEvent(); return; }
			GateId? gate = null; float gd = 22f;
			foreach (var gid in s.Gates)
			{
				float d = Place(_u.Gates[gid].Position).DistanceTo(pos);
				if (d < gd) { gd = d; gate = gid; }
			}
			if (gate is { } g) { GateTapped?.Invoke(g); AcceptEvent(); }
		}

		public override void _Draw()
		{
			if (_u == null) return;
			var s = _u.Sectors[_owner.Selected];
			float scale = ContentScale();
			var c = Size / 2f;
			var font = ThemeDB.FallbackFont;

			// Sektorscheibe (nur wenn im Bild), Ringe
			float edge = s.Radius * scale;
			if (edge < 700f)
			{
				DrawCircle(c, edge, new Color(0.03f, 0.05f, 0.1f, 0.9f));
				DrawArc(c, edge, 0, Mathf.Tau, 64, new Color(Edge, 0.6f), 2f, true);
			}
			else
			{
				DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.03f, 0.05f, 0.1f, 0.9f));
				DrawArc(c, Mathf.Min(Size.X, Size.Y) / 2f - 8f, 0, Mathf.Tau, 64, new Color(Edge, 0.6f), 2f, true);
			}
			DrawArc(c, edge * 0.66f, 0, Mathf.Tau, 48, new Color(1, 1, 1, 0.05f), 1f);
			DrawArc(c, edge * 0.33f, 0, Mathf.Tau, 48, new Color(1, 1, 1, 0.05f), 1f);

			// Rohstoffgürtel (Label unterhalb des Gürtels, damit es die Stationsnamen nicht verdeckt)
			foreach (var f in s.Fields)
			{
				var p = Place(f.Position);
				float br = SpaceSim.Core.Mining.MiningRules.BeltRadius * scale;
				DrawArc(p, br, 0, Mathf.Tau, 40, new Color(0.65f, 0.6f, 0.5f, 0.35f), 2f, true);
				DrawCircle(p, 5f, new Color(0.65f, 0.6f, 0.5f, 0.8f));
				var cls = _u.AsteroidClasses.TryGetValue(f.ClassKey, out var ac) ? ac.Name : f.ClassKey;
				DrawString(font, p + new Vector2(-80, br + 16), $"Gürtel · {cls}", HorizontalAlignment.Center, 160, 12, new Color(Dim, 0.9f));
			}

			// Gates mit Zielsektor
			foreach (var gid in s.Gates)
			{
				var g = _u.Gates[gid];
				var gp = Place(g.Position);
				var target = _u.Sectors[_u.Gates[g.Target].Sector];
				DrawCircle(gp, 13f, PanelSolid);
				// Phase 10: rot = geschlossen oder für dich gesperrt, orange = eingeschränkt (Zoll/Kontrolle)
				var gpol = AccessInfo.ForGate(_u, g, _u.Ships[_u.PlayerShip]);
				Color gcol = !g.Open || gpol.Denied ? Red : g.State == GateState.Restricted ? Orange : Green;
				DrawArc(gp, 13f, 0, Mathf.Tau, 24, gcol, 3f, true);
				DrawString(font, gp + new Vector2(-70, -22), $"→ {target.Name}{(!g.Open ? " · geschlossen" : gpol.Denied ? " · gesperrt" : g.State == GateState.Restricted ? " · Kontrolle" : "")}", HorizontalAlignment.Center, 140, 12, gcol);
			}

			// Stationen: Label radial nach außen versetzt, damit die Namen sich nicht stapeln
			foreach (var sid in s.Stations)
			{
				var st = _u.Stations[sid];
				var p = Place(st.Position);
				bool sel = _owner._selStation == sid;
				var col = MapOverlay.FactionColor(_u.FactionOf(st) ?? default);
				if (ConstructionInfo.Of(_u, st) is { } site)
				{
					// Phase 8: Baustelle als Raute mit Materialbalken (orange = läuft, grau = verlassen)
					var sc = site.Abandoned ? Dim : Orange;
					var pts = new[] { p + new Vector2(0, -11), p + new Vector2(11, 0), p + new Vector2(0, 11), p + new Vector2(-11, 0), p + new Vector2(0, -11) };
					DrawColoredPolygon(pts[..4], new Color(0.06f, 0.1f, 0.2f, 0.95f));
					DrawPolyline(pts, sc, sel ? 4f : 2f, true);
					DrawRect(new Rect2(p + new Vector2(-14, 14), new Vector2(28, 4)), new Color(Dim, 0.4f));
					DrawRect(new Rect2(p + new Vector2(-14, 14), new Vector2(28 * site.Progress, 4)), sc);
				}
				else
				{
					DrawRect(new Rect2(p - new Vector2(9, 9), new Vector2(18, 18)), new Color(0.06f, 0.1f, 0.2f, 0.95f));
					DrawRect(new Rect2(p - new Vector2(9, 9), new Vector2(18, 18)), col, false, sel ? 4f : 2f);
				}
				if (sel) DrawArc(p, 20f, 0, Mathf.Tau, 24, Cyan, 2f, true);
				var outward = (p - c);
				if (outward.Length() < 1f) outward = new Vector2(0, -1);
				var lp = p + outward.Normalized() * 30f;
				DrawString(font, lp + new Vector2(-80, -6), st.Name, HorizontalAlignment.Center, 160, sel ? 13 : 11, sel ? Cyan : White);
			}

			// NPC-Schiffe (echt über Materializer)
			var snap = Materializer.Snapshot(_u, s.Id, _owner.Ctx.FractionalTick());
			foreach (var sh in snap)
			{
				var p = Place(sh.Position);
				Color col = sh.Mining ? Orange : sh.Role switch
				{
					ShipRole.Miner => Green,
					ShipRole.Police => new Color(0.35f, 0.55f, 1f),   // Phase 11: Streife
					ShipRole.Pirate => Red,                          // Phase 11: Kaperschiff
					_ => new Color(0.5f, 0.8f, 1f),
				};
				var dir = sh.Direction.Length > 0.01f ? new Vector2(sh.Direction.X, sh.Direction.Y) : new Vector2(0, -1);
				float ang = Mathf.Atan2(dir.Y, dir.X) + Mathf.Pi / 2f;
				var pts = new[]
				{
					p + new Vector2(0, -7).Rotated(ang), p + new Vector2(5.5f, 4).Rotated(ang), p + new Vector2(-5.5f, 4).Rotated(ang),
				};
				DrawColoredPolygon(pts, col);
			}

			// Spieler
			if (s.Id == _u.PlayerSector)
			{
				var pl = _u.Ships[_u.PlayerShip];
				var p = Place(pl.Position);
				DrawShip(p, 9f, Cyan);
				DrawString(font, p + new Vector2(12, 4), $"du · #{pl.Id.Value}", HorizontalAlignment.Left, -1, 12, Cyan);
			}

			DrawString(font, new Vector2(12, Size.Y - 10), "◼ Station   ◆ Baustelle   ◯ Gate   ▲ Schiff (orange schürft, blau Polizei, rot Pirat)   ▲ cyan = du", HorizontalAlignment.Left, -1, 13, new Color(Dim, 0.8f));
		}

		void DrawShip(Vector2 c, float s, Color col)
		{
			var pts = new[]
			{
				c + new Vector2(0, -s), c + new Vector2(s * 0.55f, s * 0.35f), c + new Vector2(0, s * 0.55f), c + new Vector2(-s * 0.55f, s * 0.35f),
			};
			DrawColoredPolygon(pts, new Color(0.25f, 0.33f, 0.5f));
			var closed = new System.Collections.Generic.List<Vector2>(pts) { pts[0] };
			DrawPolyline(closed.ToArray(), col, 1.5f, true);
		}
	}

	/// <summary>Selbsttest: Planet in der zuletzt gezeichneten Sektor-Detailkarte (Phase 14; leer ohne Planet).</summary>
	public string TestPlanet { get; private set; } = "";
}
