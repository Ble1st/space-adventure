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
public sealed partial class UniverseScreen : CommandScreen
{
	public override string Id => "cc.universe";

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
		_map = At(inner, new UniverseMapCanvas { MouseFilter = MouseFilterEnum.Stop }, 0, 56, 700, 464);
		_map.Setup(this);
		_map.Tapped += s => { SelectedSector = s; Invalidate(); };

		// ── Detailkarte rechts ──
		BuildDetail(inner, Selected);
	}

	void BuildDetail(Control inner, SectorId sel)
	{
		var u = Ctx.U;
		var s = u.Sectors[sel];
		var card = CardBg(inner, 712, 56, 300, 464, Card);

		bool isPlayer = sel == u.PlayerSector;
		string owner = FactionName(u, s.Owner);
		Color ownerCol = MapOverlay.FactionColor(s.Owner);
		int ships = ShipCountOf(u, sel);

		var pol = AccessInfo.ForSector(u, sel, u.Ships[u.PlayerShip]);
		At(card, Text(s.Name, 24, Cyan), 16, 10, 268, 32);
		At(card, Text($"{owner} · {pol.Stage}{(isPlayer ? " · du bist hier" : "")}", 15, ownerCol), 16, 44, 268, 22);
		At(card, Text($"Stationen {s.Stations.Count} · Gates {s.Gates.Count} · Schiffe {ships}", 14, White), 16, 68, 268, 20);
		// Phase 10: Sperre und Einfluss (echt)
		Color lockCol = pol.PlayerDenied ? Red : pol.EntryLevel > 0 ? Orange : Green;
		float py = WrappedText(card, $"Sperre: {pol.Lock}", 16, 92, 268, 13, lockCol, 16);
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
		if (s.Gates.Count > maxGates) { At(card, Text($"… und {s.Gates.Count - maxGates} weitere", 12, Dim), 24, gy, 268, 16); gy += 16; }

		// Route vom aktuellen Sektor nach deinem Mobilitätsprofil (echt über Universe.FindRoute mit Profil)
		gy += 4;
		var route = PlayerRoute(u, sel);
		int hops = route?.Count ?? -1;
		if (hops < 0)
			WrappedText(card, "Route: nicht erreichbar – " + (GateAccess.BlockReason(u, u.Ships[u.PlayerShip], u.PlayerSector, sel) ?? "Gates gesperrt"), 16, gy, 268, 13, Red, 16);
		else if (hops == 0)
			At(card, Text("Route: du bist in diesem Sektor", 14, Green), 16, gy, 268, 20);
		else
		{
			At(card, Text($"Route: {hops} {(hops == 1 ? "Sprung" : "Sprünge")} ab {SectorName(u, u.PlayerSector)}", 14, White), 16, gy, 268, 20);
			var way = new List<string>();
			foreach (var gid in route!) way.Add(SectorName(u, u.Gates[u.Gates[gid].Target].Sector));
			WrappedText(card, string.Join(" → ", way), 24, gy + 20, 260, 12, new Color(Dim, 0.9f), 15);
		}

		// Knöpfe unten
		var bOpen = At(card, new KitButton("Sektorkarte öffnen", new Vector2(268, 52)) { FontSize = 17 }, 16, 300);
		bOpen.Pressed += () => OpenSectorMap?.Invoke(sel);
		var bRoute = At(card, new KitButton("Route planen / Autopilot", new Vector2(268, 52)) { FontSize = 17 }, 16, 360);
		if (hops >= 0) bRoute.Pressed += () => PlanRoute?.Invoke(sel);
		else { bRoute.State = KitButtonState.Disabled; bRoute.Detail = "nicht erreichbar"; }
		// Phase 12: Taxi echt — nur angedockt an einer Station mit Taxi; günstigstes buchbares Ziel im gewählten Sektor (Core: TaxiService.Quote)
		var from = Ctx.Docked is { } d && u.Stations.TryGetValue(d, out var ds) && Ctx.Services.ForStation(u, ds).Any(x => x.Key == SpaceSim.Core.Services.TaxiService.ServiceKey) ? ds : null;
		var quote = from is null ? null : SpaceSim.Core.Services.TaxiService.Quotes(u, from, Ctx.Player).Where(q => q.To.Sector == sel && q.Bookable).OrderBy(q => q.Total).FirstOrDefault();
		var bTaxi = At(card, new KitButton("Taxi buchen", new Vector2(268, 48)) { FontSize = 17, State = quote is null ? KitButtonState.Disabled : KitButtonState.Ready,
			Detail = quote is not null ? $"{quote.Total:0} Cr → {quote.To.Name}" : from is null ? "nur angedockt (Taxistand)" : "kein Ziel hier", DetailColor = new Color(Dim, 0.9f) }, 16, 420);
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

		Vector2 Place(Sector s, Vector2 center, float scale)
		{
			var all = _u.Sectors.Values.Select(x => x.MapPosition).ToList();
			var mid = new Vec2(all.Average(p => p.X), all.Average(p => p.Y));
			return center + new Vector2((s.MapPosition.X - mid.X) * scale, (s.MapPosition.Y - mid.Y) * scale);
		}

		float MapScale()
		{
			var all = _u.Sectors.Values.Select(x => x.MapPosition).ToList();
			float rx = all.Max(p => p.X) - all.Min(p => p.X);
			float ry = all.Max(p => p.Y) - all.Min(p => p.Y);
			float sx = rx > 0.001f ? (Size.X - 150f) / rx : 1f;
			float sy = ry > 0.001f ? (Size.Y - 180f) / ry : 1f;
			return Mathf.Min(sx, sy);
		}

		public override void _GuiInput(InputEvent @event)
		{
			if (!Pointer.TryRead(@event, out _, out var pos, out bool pressed, out bool motion) || motion || pressed) return;
			var center = Size / 2f + new Vector2(0, -10f);
			float scale = MapScale();
			SectorId? best = null; float bestD = 46f;
			foreach (var s in _u.Sectors.Values)
			{
				float d = Place(s, center, scale).DistanceTo(pos);
				if (d < bestD) { bestD = d; best = s.Id; }
			}
			if (best is { } sid) { Tapped?.Invoke(sid); AcceptEvent(); }
		}

		public override void _Draw()
		{
			if (_u == null) return;
			var center = Size / 2f + new Vector2(0, -10f);
			float scale = MapScale();
			var sel = _owner.Selected;
			var layer = _owner.CurrentLayer;
			var mark = _owner.MarkFaction;

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
				DrawCircle(p, 44f, new Color(halo, haloA));
				DrawCircle(p, 26f, PanelSolid);
				DrawArc(p, 26f, 0, Mathf.Tau, 32, halo, s.Id == sel ? 5f : 3f, true);
				if (s.Id == _u.PlayerSector) DrawArc(p, 34f, 0, Mathf.Tau, 32, White, 3f, true);
				if (s.Id == sel) DrawArc(p, 40f, 0, Mathf.Tau, 32, Cyan, 2f, true);
				var font = ThemeDB.FallbackFont;
				DrawString(font, p + new Vector2(-60, -6), s.Name, HorizontalAlignment.Center, 120, 16, White);
				string sub = layer switch
				{
					UniverseScreen.Layer.Prices => $"{SectorEconomy.PriceLevel(_u, s.Id):0.00}× · {s.Stations.Count} St.",
					UniverseScreen.Layer.Security => s.Access.Level(AccessDirection.Entry) > 0
						? $"Stufe {s.Access.Level(AccessDirection.Entry)} · {AccessNames.Level(s.Access.Level(AccessDirection.Entry))}"
						: $"Sicherheit {SecurityNames.Security(s.Security)} · Bedrohung {SecurityNames.Threat(s.Threat)}",
					UniverseScreen.Layer.Ships => $"{ShipCountOf(_u, s.Id)} Schiffe · {s.Stations.Count} St.",
					_ => $"{FactionName(_u, s.Owner)} · {AccessNames.Stage(s.Control)}{(s.Contested ? " · umkämpft" : "")}",
				};
				DrawString(font, p + new Vector2(-100, 12), sub, HorizontalAlignment.Center, 200, 11, new Color(Dim, 0.9f));

				// Schiffe-Ebene: NPC-Schiffe als Symbole um den Sektor
				if (layer == UniverseScreen.Layer.Ships)
				{
					int n = ShipCountOf(_u, s.Id);
					int dots = Math.Min(n, 8);
					for (int i = 0; i < dots; i++)
					{
						var dv = Vec2.FromAngle(i * 2.4f) * 52f;
						DrawCircle(p + new Vector2(dv.X, dv.Y), 3.5f, Green);
					}
					if (n > 8) DrawString(font, p + new Vector2(40, 40), $"+{n - 8}", HorizontalAlignment.Left, -1, 12, Green);
				}
				// Spielerposition
				if (s.Id == _u.PlayerSector)
				{
					var sp = p + new Vector2(-18, -38);
					DrawShip(sp, 9f, Cyan);
				}
			}

			// Legende oben im Kartenbereich
			var font2 = ThemeDB.FallbackFont;
			DrawString(font2, new Vector2(12, 20), "— offen   — eingeschränkt   — für dich gesperrt   - - geschlossen", HorizontalAlignment.Left, -1, 13, new Color(Dim, 0.8f));
			DrawLine(new Vector2(12, 26), new Vector2(26, 26), new Color(Dim, 0.7f), 2f);
			DrawLine(new Vector2(78, 26), new Vector2(92, 26), Orange, 2f);
			DrawLine(new Vector2(196, 26), new Vector2(210, 26), Red, 2f);
			if (layer == UniverseScreen.Layer.Security)
				DrawString(font2, new Vector2(12, 46), "Lage: grün sicher · gelb schwach geschützt · orange bedroht/Sperre ab 3 · rot stark bedroht/gesperrt", HorizontalAlignment.Left, -1, 13, new Color(Dim, 0.8f));
			if (layer == UniverseScreen.Layer.Prices)
				DrawString(font2, new Vector2(12, 46), "Preise: grün günstig · rot teuer (× Basis)", HorizontalAlignment.Left, -1, 13, new Color(Dim, 0.8f));
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
}
