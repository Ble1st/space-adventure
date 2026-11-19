using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Universum“ (UI.md Abschnitt 4): Sektorkarte aus echten Core-Daten – Sektoren mit
/// <see cref="Sector.MapPosition"/>, Gates als Linien (Phase 10: offen, eingeschränkt, geschlossen/zerstört und „für dich gesperrt“ nach
/// <see cref="GateAccess"/>), Fraktionsfarben als Halo nach <see cref="Sector.Owner"/>, Spielerposition, Stationszahl. Ebenen umschaltbar:
/// Fraktion (echt, mit Kontrollstufe und „umkämpft“), Schiffe (echt: NPC-Schiffe je Sektor), Preise (echt: <see cref="SectorEconomy.PriceLevel"/>),
/// Sicherheit (Phase 10 echt: Sperrstufe und Urteil der Kontrollpunkte für dich, <see cref="AccessInfo"/>). Tippen wählt einen Sektor; rechts die
/// Detailkarte mit Kontrolle, Einfluss, Sperre und Route nach deinem Mobilitätsprofil und den Knöpfen „Sektorkarte öffnen“,
/// „Route planen/Autopilot“ (echt, setzt das Autopilot-Ziel in Main) und „Taxi buchen“ (Platzhalter).
/// </summary>
public sealed partial class UniverseScreen : CommandScreen, ISessionScreen
{
	public override string Id => "cc.universe";
	/// <summary>Runde 41 (Selbsttest): Zahl der gezeichneten Missionssymbole auf der Universumskarte.</summary>
	public int TestMissionSymbols;
	/// <summary>Gerätetest Phase 27: Auswahl und Meldung bei neuer Sitzung zurücksetzen.</summary>
	public void ResetSession() { SelectedSector = null; }

	/// <summary>Kartenebene.</summary>
	public enum Layer { Faction, Ships, Prices, Security }

	/// <summary>Aktuelle Ebene (Standard Fraktion).</summary>
	public Layer CurrentLayer { get; set; } = Layer.Faction;
	/// <summary>Gewählter Sektor (null = Spielersektor).</summary>
	public SectorId? SelectedSector { get; set; }
	/// <summary>Fraktion, deren Gebiet markiert wird („Gebiet auf Karte“ aus dem Fraktionen-Bereich).</summary>
	public FactionId? MarkFaction { get; set; }

	/// <summary>„Sektorkarte öffnen“: wechselt zum Sektor-Bereich mit diesem Sektor.</summary>
	public event Action<SectorId>? OpenSectorMap;
	/// <summary>„Route planen/Autopilot“: setzt das Autopilot-Ziel in Main (wie die Sternenkarte).</summary>
	public event Action<SectorId>? PlanRoute;

	UniverseMapCanvas _map = null!;
	/// <summary>Runde 35, Selbsttest: Kartenfläche in Bildpunkten.</summary>
	/// <summary>Runde 35b (Selbsttest): Bildpunkt eines Sektors auf der Karte (wie der Tipp ihn sucht).</summary>
	public Vector2? TestSectorScreen(SectorId id) => _map is not null && IsInstanceValid(_map) ? _map.GlobalOf(id) : null;
	public Rect2 TestMapRect => _map is not null && IsInstanceValid(_map) ? _map.GetGlobalRect() : new Rect2();

	/// <summary>Gewählter Sektor (null aufgelöst auf den Spielersektor).</summary>
	SectorId Selected => SelectedSector ?? Ctx.U.PlayerSector;

	/// <summary>Sprungzahl der Route vom Spielersektor zum gewählten Sektor nach dem Profil des Spielerschiffs (-1 = nicht erreichbar).</summary>
	public int TestRouteHops => PlayerRoute(Ctx.U, Selected)?.Count ?? -1;

	/// <summary>Phase 10: Route des Spielers nach seinem Mobilitätsprofil (gesperrte Gates werden umgangen).</summary>
	internal static List<GateId>? PlayerRoute(Universe u, SectorId to) =>
		u.FindRoute(u.PlayerSector, to, GateAccess.ProfileOf(u, u.Ships[u.PlayerShip]));

	/// <summary>Test-Hook: Sektor wählen.</summary>
	public void TestSelect(SectorId s) { SelectedSector = s; Invalidate(); }
	/// <summary>Test-Hook: „Route planen/Autopilot“ für den gewählten Sektor auslösen.</summary>
	public void TestPlanRoute() { if (SelectedSector is { } s) PlanRoute?.Invoke(s); }

	/// <summary>Runde 35: Kartenbreite = Innenbreite − Detailkarte (300) − Abstände; bei 1022 Innenbreite wie bisher 700.</summary>
	static float MapW(Control inner) => MathF.Max(300f, (inner.Size.X > 0 ? inner.Size.X : 1022f) - 322f);

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;

		// ── Oben: Umschalter Universum/Sektor und die Ebenen ──
		var segU = At(inner, new KitButton("Universum", new Vector2(120, 40)) { FontSize = 17, State = KitButtonState.On }, 12, 10);
		var segS = At(inner, new KitButton("Sektor", new Vector2(120, 40)) { FontSize = 17 }, 138, 10);
		segS.Pressed += () => OpenSectorMap?.Invoke(Selected);
		At(inner, Text("Ebene:", 14, Dim), 280, 18);
		float lx = 330;
		foreach (var (layer, label) in new[] { (Layer.Faction, "Fraktion"), (Layer.Ships, "Schiffe"), (Layer.Prices, "Preise"), (Layer.Security, "Sicherheit") })
		{
			var b = At(inner, new KitButton(label, new Vector2(96, 36)) { FontSize = 13, State = layer == CurrentLayer ? KitButtonState.On : KitButtonState.Normal }, lx, 12);
			var l = layer;
			b.Pressed += () => { CurrentLayer = l; Invalidate(); };
			lx += 102;
		}

		// ── Karte links ──
		_map = At(inner, new UniverseMapCanvas { MouseFilter = MouseFilterEnum.Stop }, 0, 56, MapW(inner), inner.Size.Y - 56);   // Runde 35: Karte nutzt die gewonnene Fläche
		_map.Setup(this);
		_map.Tapped += s => { SelectedSector = s; Invalidate(); };

		// ── Detailkarte rechts ──
		BuildDetail(inner, Selected);
	}

	void BuildDetail(Control inner, SectorId sel)
	{
		var u = Ctx.U;
		var s = u.Sectors[sel];
		// Runde 35b (Prüfung: „Sektorkarte öffnen“ lag halb unter dem Rand): Karte in der Höhe der Fläche, die drei Knöpfe als fester Fuß, der Text darüber rollt
		float cardH = MathF.Max(inner.Size.Y - 56, 300f);
		var frame = CardBg(inner, MapW(inner) + 12, 56, 300, cardH, Card);
		const float footH = 3 * 54 + 10;
		// ui-check (Gasernte/Fusion): ein sichtbarer Rollbalken machte die Fläche schmaler als die 268-px-Zeilen — Balken aus, Wischen rollt weiter
		var bodyScroll = At(frame, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever, MouseFilter = MouseFilterEnum.Pass }, 0, 4, 300, cardH - footH - 8);
		var card = new Control { MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(292, 10) };
		bodyScroll.AddChild(card);

		bool isPlayer = sel == u.PlayerSector;
		string owner = FactionName(u, s.Owner);
		Color ownerCol = MapOverlay.FactionColor(s.Owner);
		int ships = ShipCountOf(u, sel);

		var pol = AccessInfo.ForSector(u, sel, u.Ships[u.PlayerShip]);
		At(card, Text(s.Name, 24, Cyan), 16, 10, 268, 32);
		// Phase 14: Planet des Sektors (in der Zeile mit Stationen und Schiffen)
		TestPlanet = s.Planet?.Name ?? "";
		// Runde 35b (Prüfwerkzeug: „… · Dominanz · du hier“ bei 1,15 gekürzt): Besitzerzeile bricht um, der Rest rückt nach
		float oy = WrappedText(card, $"{owner} · {pol.Stage}{(isPlayer ? " · du hier" : "")}", 16, 44, 268, 15, ownerCol, 20);
		if (s.Planet is { } planet) At(card, Text($"Planet {planet.Name} · Stationen {s.Stations.Count} · Schiffe {ships}", 13, White), 16, oy + 2, 268, 20);
		else At(card, Text($"Stationen {s.Stations.Count} · Gates {s.Gates.Count} · Schiffe {ships}", 14, White), 16, oy + 2, 268, 20);
		// Runde 44: Größe des Sektors und Flugzeit dorthin (echte Spielerflugzeit, gecacht)
		TestSizeLine = SizeLine(u, s);
		if (s.Nebula) TestSizeLine += s.GasFields.Count > 0 ? " · Nebel, Gas" : " · Nebel";   // Gasernte: Nebelsektor (schwächere Sensoren, Gasfelder)
		At(card, Text(TestSizeLine, 13, new Color(Cyan, 0.9f)), 16, oy + 22, 268, 18);
		// Phase 10: Sperre und Einfluss (echt)
		Color lockCol = pol.PlayerDenied ? Red : pol.EntryLevel > 0 ? Orange : Green;
		float py = WrappedText(card, $"Sperre: {pol.Lock}", 16, oy + 42, 268, 13, lockCol, 16);
		py = WrappedText(card, $"Für dich: {pol.ForPlayer}", 16, py, 268, 13, lockCol, 16);
		py = WrappedText(card, $"Einfluss: {pol.Influence}", 16, py + 2, 268, 12, new Color(Dim, 0.9f), 16);
		// Phase 11: Lagekarte und deine Fahndung beim Besitzer (echt)
		var sec = SecurityInfo.ForSector(u, sel, u.Ships[u.PlayerShip]);
		py = WrappedText(card, $"Lage: {sec.Summary} · {sec.Patrols} Streife{(sec.Patrols == 1 ? "" : "n")}{(sec.RiskPercent >= 0.5f ? $" · Risiko {sec.RiskPercent:0} %" : "")}", 16, py, 268, 12,
			sec.Threat >= 2.5f ? Red : sec.Threat >= 1f ? Orange : new Color(Dim, 0.95f), 16);
		if (sec.PlayerLevel > 0) py = WrappedText(card, $"Fahndung hier: {sec.PlayerWanted}", 16, py, 268, 12, sec.PlayerLevel >= 3 ? Red : Orange, 16);

		// Gates mit Ziel, Zustand und Urteil des Kontrollpunkts für dich
		float gy = py + 6;
		int maxGates = gy > 200 ? 2 : 3;
		foreach (var gid in s.Gates.Take(maxGates))
		{
			var g = u.Gates[gid];
			var target = u.Sectors[u.Gates[g.Target].Sector];
			var gp = AccessInfo.ForGate(u, g, u.Ships[u.PlayerShip]);
			Color gc = gp.Denied || !g.Open ? Red : gp.Toll > 0f ? Orange : White;
			At(card, Text($"→ {target.Name} · {gp.State}{(gp.Denied ? " · gesperrt" : gp.Toll > 0f ? $" · Zoll {gp.Toll:0}" : "")}", 13, gc), 24, gy, 260, 18);
			gy += 18;
		}
		if (s.Gates.Count > maxGates) { At(card, Text($"+ {s.Gates.Count - maxGates} weitere Gates", 12, Dim), 24, gy, 268, 16); gy += 16; }

		// Route vom aktuellen Sektor nach deinem Mobilitätsprofil (echt über Universe.FindRoute mit Profil)
		gy += 4;
		var route = PlayerRoute(u, sel);
		int hops = route?.Count ?? -1;
		float end;
		if (hops < 0)
			end = WrappedText(card, "Route: nicht erreichbar – " + (GateAccess.BlockReason(u, u.Ships[u.PlayerShip], u.PlayerSector, sel) ?? "Gates gesperrt"), 16, gy, 268, 13, Red, 16);
		else if (hops == 0)
		{ At(card, Text("Route: du bist in diesem Sektor", 14, Green), 16, gy, 268, 20); end = gy + 20; }
		else
		{
			At(card, Text($"Route: {hops} {(hops == 1 ? "Sprung" : "Sprünge")} ab {SectorName(u, u.PlayerSector)}", 14, White), 16, gy, 268, 20);
			var way = new List<string>();
			foreach (var gid in route!) way.Add(SectorName(u, u.Gates[u.Gates[gid].Target].Sector));
			end = WrappedText(card, string.Join(" → ", way), 24, gy + 20, 260, 12, new Color(Dim, 0.9f), 15);
		}
		card.CustomMinimumSize = new Vector2(292, end + 8);

		// Knöpfe als fester Fuß der Karte
		float fy = cardH - footH + 2;
		var bOpen = At(frame, new KitButton("Sektorkarte öffnen", new Vector2(268, 50)) { FontSize = 17 }, 16, fy);
		bOpen.Pressed += () => OpenSectorMap?.Invoke(sel);
		var bRoute = At(frame, new KitButton("Route planen / Autopilot", new Vector2(268, 50)) { FontSize = 17 }, 16, fy + 54);
		if (hops >= 0) bRoute.Pressed += () => PlanRoute?.Invoke(sel);
		else { bRoute.State = KitButtonState.Disabled; bRoute.Detail = "nicht erreichbar"; }
		// Phase 12: Taxi echt — nur angedockt an einer Station mit Taxi; günstigstes buchbares Ziel im gewählten Sektor (Core: TaxiService.Quote)
		var from = Ctx.Docked is { } d && u.Stations.TryGetValue(d, out var ds) && Ctx.Services.ForStation(u, ds).Any(x => x.Key == SpaceSim.Core.Services.TaxiService.ServiceKey) ? ds : null;
		var quote = from is null ? null : SpaceSim.Core.Services.TaxiService.Quotes(u, from, Ctx.Player).Where(q => q.To.Sector == sel && q.Bookable).OrderBy(q => q.Total).FirstOrDefault();
		var bTaxi = At(frame, new KitButton("Taxi buchen", new Vector2(268, 50)) { FontSize = 16, State = quote is null ? KitButtonState.Disabled : KitButtonState.Ready,
			Detail = quote is not null ? $"{quote.Total:0} Cr → {quote.To.Name}" : from is null ? "nur angedockt (Taxistand)" : "kein Ziel hier", DetailColor = new Color(Dim, 0.9f) }, 16, fy + 108);
		if (quote is not null) { var to = quote.To.Id; bTaxi.Pressed += () => TaxiRequested?.Invoke(to); }
	}

	/// <summary>Phase 12: „Taxi buchen“ — Main bucht die Fahrt über den Dienst „taxi“ der angedockten Station.</summary>
	public event System.Action<StationId>? TaxiRequested;

	/// <summary>Anzahl der NPC-Schiffe in einem Sektor (echt aus dem Schiffsindex, ohne Spieler).</summary>
	internal static int ShipCountOf(Universe u, SectorId sector)
	{
		int n = u.Tracker.ParkedIn(sector).Count + u.Tracker.TransitIn(sector).Count;
		if (sector == u.PlayerSector && u.Tracker.ParkedIn(sector).Contains(u.PlayerShip)) n--;
		return n;
	}

	/// <summary>Sektorkarte des Universums: Sektoren, Gates, Ebenen, Spielerposition; Tippen wählt einen Sektor.</summary>
	sealed partial class UniverseMapCanvas : Control
	{
		UniverseScreen _owner = null!;
		Universe _u = null!;
		public event Action<SectorId>? Tapped;

		public void Setup(UniverseScreen owner)
		{
			_owner = owner;
			_u = owner.Ctx.U;
		}

		Vector2 Place(Sector s, Vector2 center, Vector2 scale)
		{
			var all = _u.Sectors.Values.Select(x => x.MapPosition).ToList();
			// Phase 21: Mitte des umschließenden Rechtecks (der Mittelwert verschob die 11-Sektoren-Karte zur dichten Seite)
			var mid = new Vec2((all.Max(p => p.X) + all.Min(p => p.X)) / 2f, (all.Max(p => p.Y) + all.Min(p => p.Y)) / 2f);
			return center + new Vector2((s.MapPosition.X - mid.X) * scale.X, (s.MapPosition.Y - mid.Y) * scale.Y);
		}

		/// <summary>
		/// Feldabstand je Achse. Phase 21: getrennt für x und y (Schema-Karte) — bei 4 × 4 Feldern wäre ein gemeinsamer Maßstab durch die Höhe begrenzt
		/// und die Beschriftungen nebeneinanderliegender Sektoren überlappten; die Breite höchstens das Doppelte der Höhe.
		/// </summary>
		Vector2 MapScale()
		{
			var all = _u.Sectors.Values.Select(x => x.MapPosition).ToList();
			float rx = all.Max(p => p.X) - all.Min(p => p.X);
			float ry = all.Max(p => p.Y) - all.Min(p => p.Y);
			float sx = rx > 0.001f ? (Size.X - 150f) / rx : 1f;
			float sy = ry > 0.001f ? (Size.Y - 130f) / ry : 1f;
			if (rx <= 0.001f) sx = sy;
			if (ry <= 0.001f) sy = sx;
			// Runde 35c (Prüfung: bei 2048 × 1536 lag die Unterzeile von „Abgrund“ halb unter dem Kartenrand): unter der untersten Zeile Platz für Halo und zwei
			// Unterzeilen (Mitte liegt 10 tiefer) — der Halo wächst mit dem Maßstab, deshalb zweimal nachrechnen
			if (ry > 0.001f)
				for (int i = 0; i < 2; i++)
				{
					float r = HaloRadius(Mathf.Min(sx, sy));
					sy = Mathf.Min(sy, (Size.Y - 2f * (r + 32f) - 20f) / ry);
				}
			return new Vector2(Mathf.Min(sx, 2f * sy), Mathf.Min(sy, 2f * sx));
		}

		public Vector2 GlobalOf(SectorId id) => GetGlobalTransform() * Place(_u.Sectors[id], Size / 2f + new Vector2(0, 10f), MapScale());

		public override void _GuiInput(InputEvent @event)
		{
			if (!Pointer.TryRead(@event, out _, out var pos, out bool pressed, out bool motion) || motion || pressed) return;
			var center = Size / 2f + new Vector2(0, 10f);
			var scale = MapScale();
			SectorId? best = null; float bestD = HaloRadius(Mathf.Min(scale.X, scale.Y)) + 2f;
			foreach (var s in _u.Sectors.Values)
			{
				float d = Place(s, center, scale).DistanceTo(pos);
				if (d < bestD) { bestD = d; best = s.Id; }
			}
			if (best is { } sid) { Tapped?.Invoke(sid); AcceptEvent(); }
		}

		/// <summary>Phase 21: Halo- und Knotengröße folgen dem Feldabstand, damit sich bei 11 Sektoren (4 Zeilen) die Halos nicht überlappen.</summary>
		// Runde 35b: bei engem Feld (Faktor 1,3, 4:3) kleinere Halos, damit zwischen den Zeilen Platz für die Unterzeile bleibt
		static float HaloRadius(float scale) => Mathf.Clamp(scale * 0.34f, 24f, 44f);
		static float NodeRadius(float scale) => Mathf.Clamp(scale * 0.24f, 16f, 26f);

		public override void _Draw()
		{
			if (_u == null) return;
			var center = Size / 2f + new Vector2(0, 10f);
			var scale = MapScale();
			float rHalo = HaloRadius(Mathf.Min(scale.X, scale.Y)), rNode = NodeRadius(Mathf.Min(scale.X, scale.Y));
			var sel = _owner.Selected;
			var layer = _owner.CurrentLayer;
			var mark = _owner.MarkFaction;
			DrawnText.Clear(this);
			int slot = 0;
			// Runde 35c (Prüfung: Linien und Halo-Ringe kreuzten Namen und Unterzeilen): Beschriftungen werden gesammelt und zuletzt auf einer dezenten
			// Plakette gezeichnet — keine Linie, kein Ring und kein Knoten eines Nachbarn liegt mehr über einem Text
			var queued = new List<(Vector2 Pos, string Full, int Size, float W, Color Col, bool Middle)>();
			void Queue(Vector2 pos, string full, int size, float w, Color col, bool middle = false) => queued.Add((pos, full, size, w, col, middle));
			// Runde 35b: Platz je Knoten aus dem Spaltenabstand (nicht aus dem Halo) — Namen bei Faktor 1,3 und 4:3 wurden gekürzt
			float colW = Mathf.Max(2f * rHalo + 12f, scale.X - 8f);

			// Gates als Linien (jedes Paar einmal)
			foreach (var g in _u.Gates.Values)
			{
				if (g.Id.Value >= g.Target.Value) continue;
				if (!_u.Gates.TryGetValue(g.Target, out var far)) continue;
				var a = Place(_u.Sectors[g.Sector], center, scale);
				var b = Place(_u.Sectors[far.Sector], center, scale);
				// Phase 10: geschlossen/zerstört rot gestrichelt; für dich gesperrt (eine Richtung reicht) rot; eingeschränkt orange
				var player = _u.Ships[_u.PlayerShip];
				bool denied = AccessInfo.ForGate(_u, g, player).Denied || AccessInfo.ForGate(_u, far, player).Denied;
				if (!g.Open) Dashed(a, b, Red, 3f);
				else if (denied) { DrawLine(a, b, new Color(Red, 0.85f), 3f); DrawCircle((a + b) / 2f, 7f, Red); }
				else if (g.State == GateState.Restricted || far.State == GateState.Restricted) DrawLine(a, b, new Color(Orange, 0.85f), 3f);
				else DrawLine(a, b, new Color(Dim, 0.7f), 2.5f);
			}

			foreach (var s in _u.Sectors.Values)
			{
				var p = Place(s, center, scale);
				Color halo;
				float haloA = 0.30f;
				switch (layer)
				{
					case UniverseScreen.Layer.Faction:
						halo = MapOverlay.FactionColor(s.Owner); haloA = mark is { } mf && s.Owner == mf ? 0.55f : 0.30f;
						break;
					case UniverseScreen.Layer.Prices:
						float lvl = SectorEconomy.PriceLevel(_u, s.Id);
						halo = lvl >= 1.15f ? Red : lvl >= 1.02f ? Orange : lvl <= 0.85f ? Green : Cyan;
						haloA = 0.4f;
						break;
					case UniverseScreen.Layer.Security:
						// Phase 10: Sperrstufe des Sektors (rot, wenn du nicht hineinkommst); Phase 11: sonst die Lagekarte — Bedrohung gegen Sicherheit
						var sp = AccessInfo.ForSector(_u, s.Id, _u.Ships[_u.PlayerShip]);
						halo = sp.PlayerDenied ? Red : sp.EntryLevel >= 3 ? Orange
							: s.Threat >= 2.5f ? Red : s.Threat >= 1f ? Orange : s.Threat >= 0.25f || s.Security < 1f ? new Color(1f, 0.9f, 0.3f) : Green;
						haloA = 0.4f;
						break;
					default:   // Schiffe
						halo = MapOverlay.FactionColor(s.Owner); haloA = 0.12f;
						break;
				}
				DrawCircle(p, rHalo, new Color(halo, haloA));
				if (s.Nebula) DrawArc(p, rHalo + 3f, 0, Mathf.Tau, 32, new Color(0.75f, 0.55f, 1f, 0.75f), 4f, true);   // Gasernte: Nebelsektor violetter Saum
				DrawCircle(p, rNode, PanelSolid);
				DrawArc(p, rNode, 0, Mathf.Tau, 32, halo, s.Id == sel ? 5f : 3f, true);
				if (s.Id == _u.PlayerSector) DrawArc(p, rNode + 8f, 0, Mathf.Tau, 32, White, 3f, true);
				if (s.Id == sel) DrawArc(p, rHalo - 4f, 0, Mathf.Tau, 32, Cyan, 2f, true);
				// Phase 24: laufendes Gefecht im Sektor — rotes Kreuz am Knoten (in allen Ebenen)
				if (SpaceSim.Core.Combat.CombatInfo.CountIn(_u, s.Id) > 0)
				{
					var cx = p + new Vector2(rNode * 0.75f, -rNode * 0.75f);
					DrawLine(cx + new Vector2(-6, -6), cx + new Vector2(6, 6), Red, 3f);
					DrawLine(cx + new Vector2(-6, 6), cx + new Vector2(6, -6), Red, 3f);
				}
				// Runde 41/42b: Missionsziel im Sektor — goldene Sterne rechts oben am Knoten (eigene Form, nicht die Baustellen-Raute; einer je Auftrag, max 3)
				int mmi = 0;
				foreach (var _mk in SpaceSim.Core.Missions.MissionMarkers.For(_u).Where(mk => mk.Sector == s.Id))
				{
					if (mmi >= 3) break;
					var c = p + new Vector2(rNode * 0.75f + mmi * 12f, -rNode * 0.75f);
					float d = 6f, id = 2.4f;
					var star = new List<Vector2>();
					for (int i = 0; i < 8; i++)
					{
						float r = i % 2 == 0 ? d : id;
						float a = -Mathf.Pi / 2f + i * Mathf.Pi / 4f;
						star.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
					}
					DrawColoredPolygon(star.ToArray(), FlightHud.MissionGold);
					mmi++;
				}
				_owner.TestMissionSymbols += mmi;
				var font = ThemeDB.FallbackFont;
				// Phase 21: ohne „Sektor “, damit lange Namen (Hephaistos) in die Spalte passen
				// Gerätetest: Name im Knoten, Unterzeile unter dem Halo, beide auf ihren Platz gekürzt (überlappten auf dem Handy die Nachbarn)
				float nameW = colW;
				Queue(p + new Vector2(-nameW / 2f, 6), s.Name.Replace("Sektor ", ""), 16, nameW, White);
				string sub = layer switch
				{
					UniverseScreen.Layer.Prices => $"{SectorEconomy.PriceLevel(_u, s.Id):0.00}× · {s.Stations.Count} St.",
					UniverseScreen.Layer.Security => s.Access.Level(AccessDirection.Entry) > 0
						? $"Stufe {s.Access.Level(AccessDirection.Entry)} · {AccessNames.Level(s.Access.Level(AccessDirection.Entry))}"
						: SpaceSim.Core.Combat.CombatInfo.CountIn(_u, s.Id) is var nf and > 0 ? $"Gefecht{(nf > 1 ? $" ×{nf}" : "")} · Bedr. {SecurityNames.Threat(s.Threat)}"   // Phase 24
						: $"Sich. {SecurityNames.Security(s.Security)} · Bedr. {SecurityNames.Threat(s.Threat)}",   // Phase 21: kurz, 11 Sektoren nebeneinander
					UniverseScreen.Layer.Ships => $"{ShipCountOf(_u, s.Id)} Schiffe · {s.Stations.Count} St.",
					_ => $"{FactionName(_u, s.Owner)} · {AccessNames.Stage(s.Control)}{(s.Contested ? " · umkämpft" : "")}",
				};
				// Kurzform für enge Karten: Fraktion nur mit Kürzel
				string subShort = layer != UniverseScreen.Layer.Faction ? sub
					: _u.Factions.ContainsKey(s.Owner) ? $"{SpaceSim.Core.Model.ActorNames.Tag(_u, s.Owner)} {AccessNames.Stage(s.Control)}{(s.Contested ? " · umk." : "")}"
					: "herrenlos";
				float subW = colW;
				// Runde 35b: Platz bis zum Halo der nächsten Zeile entscheidet — zwei Zeilen, eine Zeile (Kurzform) oder keine (steht in der Detailkarte)
				float below = scale.Y - 2f * rHalo - 4f;
				int lines = below >= 30f ? 2 : below >= 15f ? 1 : 0;
				int cut = sub.IndexOf(" · ", StringComparison.Ordinal);
				bool fitsOne = font.GetStringSize(sub, HorizontalAlignment.Left, -1, 11).X <= subW + 0.5f;
				if (lines == 0) { }
				else if (lines == 1 || fitsOne || cut <= 0 || font.GetStringSize(sub[..cut], HorizontalAlignment.Left, -1, 11).X > subW + 0.5f)
					Queue(p + new Vector2(-subW / 2f, rHalo + 13f), fitsOne ? sub : subShort, 11, subW, new Color(Dim, 0.9f), middle: true);
				else
				{
					Queue(p + new Vector2(-subW / 2f, rHalo + 13f), sub[..cut], 11, subW, new Color(Dim, 0.9f), middle: true);
					Queue(p + new Vector2(-subW / 2f, rHalo + 26f), sub[(cut + 3)..], 11, subW, new Color(Dim, 0.9f));
				}

				// Schiffe-Ebene: NPC-Schiffe als Symbole um den Sektor
				if (layer == UniverseScreen.Layer.Ships)
				{
					int n = ShipCountOf(_u, s.Id);
					int dots = Math.Min(n, 8);
					for (int i = 0; i < dots; i++)
					{
						var dv = Vec2.FromAngle(i * 2.4f) * (rHalo + 6f);
						DrawCircle(p + new Vector2(dv.X, dv.Y), 3.5f, Green);
					}
					if (n > 8) UiKit.DrawFit(this, slot++, font, p + new Vector2(40, 40), $"+{n - 8}", 12, 0f, Green);
				}
				// Spielerposition
				if (s.Id == _u.PlayerSector)
				{
					// Runde 35b: links neben dem Knoten (über dem Knoten lag es auf der Unterzeile des Sektors darüber)
					var sp = p + new Vector2(-rNode - 12f, -rNode * 0.5f);
					DrawShip(sp, 9f, Cyan);
				}
			}

			// Beschriftungen zuletzt, je auf einer Plakette (mittig über ihrer Breite)
			{
				var lf = ThemeDB.FallbackFont;
				foreach (var q in queued)
				{
					var (shown, fs) = UiKit.FitText(this, lf, q.Full, q.Size, q.W, q.Middle);
					float tw = lf.GetStringSize(shown, HorizontalAlignment.Left, -1, fs).X;
					var plate = new Rect2(q.Pos.X + (q.W - tw) / 2f - 3f, q.Pos.Y - fs * 0.85f - 1f, tw + 6f, fs * 1.15f + 2f);
					DrawRect(plate, new Color(0.02f, 0.04f, 0.09f, 0.82f));
					UiKit.DrawFit(this, slot++, lf, q.Pos, q.Full, q.Size, q.W, q.Col, HorizontalAlignment.Center, q.Middle);
				}
			}

			// Legende oben im Kartenbereich
			var font2 = ThemeDB.FallbackFont;
			float lw = Size.X - 24f;
			UiKit.DrawFit(this, slot++, font2, new Vector2(12, 20), "— offen   — eingeschränkt   — für dich gesperrt   - - geschlossen", 13, lw, new Color(Dim, 0.8f));
			DrawLine(new Vector2(12, 26), new Vector2(26, 26), new Color(Dim, 0.7f), 2f);
			DrawLine(new Vector2(78, 26), new Vector2(92, 26), Orange, 2f);
			DrawLine(new Vector2(196, 26), new Vector2(210, 26), Red, 2f);
			// Runde 42c (Handytest): Auftragsmarker (goldene Sterne an den Sektorknoten, s. o.) in der Legende erklären —
		// Zeile 2 trägt immer „★ = Auftrag“ (kurz genug, dass die Prüfung nicht kürzt)
			string leg2 = layer == UniverseScreen.Layer.Security
				? "★ = Auftrag · Lage: grün sicher · gelb schwach · orange bedroht · rot gesperrt"
				: layer == UniverseScreen.Layer.Prices
					? "★ = Auftrag · Preise: grün günstig · rot teuer"
					: "★ = Auftrag — Missionsziel im Sektor";
			UiKit.DrawFit(this, slot++, font2, new Vector2(12, 46), leg2, 13, lw, new Color(Dim, 0.8f));
		}

		void Dashed(Vector2 a, Vector2 b, Color c, float w)
		{
			var dir = (b - a); float len = dir.Length();
			if (len < 0.001f) return;
			dir /= len;
			for (float t = 0; t < len; t += 14f) DrawLine(a + dir * t, a + dir * Mathf.Min(len, t + 8f), c, w);
		}

		void DrawShip(Vector2 c, float s, Color col)
		{
			var pts = new[]
			{
				c + new Vector2(0, -s), c + new Vector2(s * 0.55f, s * 0.35f), c + new Vector2(0, s * 0.55f), c + new Vector2(-s * 0.55f, s * 0.35f),
			};
			DrawColoredPolygon(pts, new Color(0.25f, 0.33f, 0.5f));
			var closed = new List<Vector2>(pts) { pts[0] };
			DrawPolyline(closed.ToArray(), col, 1.5f, true);
		}
	}

	/// <summary>Runde 44: Zeile „Ø 2,3 km · Flug ~1:40 min“ der Sektorkarte (Selbsttest/ui-check).</summary>
	public string TestSizeLine { get; private set; } = "";

	/// <summary>
	/// Runde 44: Durchmesser des Sektors (2 × Ausdehnung) und — für einen anderen Sektor — die echte Flugzeit des Spielers bis zur ersten
	/// andockbaren Station dort (sonst zum Mittelpunkt); <see cref="SpaceSim.Core.Missions.MissionRules.TripSeconds"/> rechnet sie wie die Missionsfristen und cacht sie.
	/// </summary>
	static string SizeLine(SpaceSim.Core.Model.Universe u, SpaceSim.Core.Model.Sector s)
	{
		string size = "Ø " + SpaceSim.Core.Flight.DistanceText.Format(2f * s.Extent);
		if (s.Id == u.PlayerSector || !u.Ships.TryGetValue(u.PlayerShip, out var me)) return size;
		var dock = s.Stations.Select(id => u.Stations[id]).Where(st => st.Dockable).OrderBy(st => st.Id.Value).FirstOrDefault();
		var to = dock?.DockPoint(me.Id) ?? default;
		float sec = SpaceSim.Core.Missions.MissionRules.TripSeconds(u, me.Sector, me.Position, s.Id, to, me);
		return float.IsInfinity(sec) || float.IsNaN(sec) ? size : $"{size} · Flug {SpaceSim.Core.Flight.PlayerTrip.Text(sec)}";
	}

	/// <summary>Selbsttest: Planet in der zuletzt gezeichneten Sektor-Detailkarte (Phase 14; leer ohne Planet).</summary>
	public string TestPlanet { get; private set; } = "";
}
