using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Mehrpunkt-Route auf der Sektorkarte: langes Drücken auf Station, Tor, eigenes Schiff oder eine freie Stelle öffnet das Kartenmenü („Ziel wählen“,
/// „Autopilot hin“, „Zur Route hinzufügen“, „Infos“, „Funk“ bei Stationen im eigenen Sektor, „Schiffsmenü“ bei eigenen Schiffen); die Taste „Route“ die
/// Routenliste; Linie und Nummern der Halte in diesem Sektor zeichnet die Karte (<see cref="PlayerRoutes.StopsIn"/>).
/// </summary>
public sealed partial class SectorScreen : IMapPopupHost
{
	public event Action? RouteStartRequested;
	/// <summary>„Funk“ mit einer Station (im eigenen Sektor).</summary>
	public event Action<StationId>? HailRequested;
	/// <summary>„Schiffsmenü“ eines eigenen Schiffs.</summary>
	public event Action<ShipId>? ShipMenuRequested;

	readonly LongPress _lp = new();
	MapPopup? _popup;
	public MapPopup? Popup => PopupOpen ? _popup : null;
	public bool PopupOpen => _popup is { } p && IsInstanceValid(p) && p.IsInsideTree() && p.Visible;
	protected override bool HoldRebuild => _lp.Down || PopupOpen;
	public KitButton? TestRouteButton { get; private set; }
	public int TestRouteNumbers { get; internal set; }
	/// <summary>Selbsttest: Bildpunkt eines Weltorts der gezeigten Sektorkarte.</summary>
	public Vector2? TestWorldScreen(Vec2 world) => _map is not null && IsInstanceValid(_map) ? _map.GetGlobalTransform() * _map.PlaceWorld(world) : null;
	/// <summary>Selbsttest: Bildpunkt eines Tors.</summary>
	public Vector2? TestGateScreen(GateId g) => Ctx.U.Gates.TryGetValue(g, out var gate) ? TestWorldScreen(gate.Position) : null;

	public bool ClosePopup()
	{
		_lp.Reset();
		if (!PopupOpen) return false;
		_popup!.Close();
		return true;
	}

	void BuildRoute(Control inner)
	{
		_lp.Fired -= OnLongPress; _lp.Fired += OnLongPress;
		var b = TestRouteButton = RouteMenus.RouteButton(inner, Ctx.U);
		b.Pressed += () => OpenRouteList(b.GetGlobalRect().Position + new Vector2(-140f, 40f));
	}

	void OnLongPress(Vector2 canvasPos)
	{
		if (_map is null || !IsInstanceValid(_map)) return;
		OpenContextAt(_map.HitAt(canvasPos), _map.GetGlobalTransform() * canvasPos);
	}

	/// <summary>Kartenmenü für das, was unter dem Drücken liegt.</summary>
	void OpenContextAt(MapHit hit, Vector2 global)
	{
		var u = Ctx.U;
		var sel = Selected;
		var profile = GateAccess.ProfileOf(u, u.Ships[u.PlayerShip]);
		bool reach = u.IsReachable(u.PlayerSector, sel, profile);
		var entries = new List<MapPopupEntry>();
		string title;
		if (hit.Station is { } sid && u.Stations.TryGetValue(sid, out var st))
		{
			title = StationNames.Short(u, st);
			var stop = PlayerRoutes.ForStation(u, sid);
			var nav = st.Sector == u.PlayerSector ? Navigator.ForStation(u, sid, u.PlayerShip) : Navigator.ForSector(u, st.Sector, u.PlayerShip);
			StationId? dock = st.Sector == u.PlayerSector ? sid : null;
			entries.Add(new("target", "Ziel wählen", reach ? "ohne loszufliegen" : "nicht erreichbar", reach, () => { ClosePopup(); TargetRequested?.Invoke(nav, dock); }));
			entries.Add(new("auto", "Autopilot hin", reach ? (dock is null ? "in den Sektor" : "fliegt hin und dockt an") : "nicht erreichbar", reach, () => { ClosePopup(); AutopilotRequested?.Invoke(nav, dock); }));
			AddRouteEntry(entries, stop);
			entries.Add(new("info", "Infos", "Marktübersicht rechts", true, () => { ClosePopup(); _selStation = sid; _selGate = null; _showInfo = true; Invalidate(); }));
			bool here = st.Sector == u.PlayerSector;
			entries.Add(new("hail", "Funk", here ? "Gespräch mit der Station" : "nur im eigenen Sektor", here, () => { ClosePopup(); HailRequested?.Invoke(sid); }));
		}
		else if (hit.Gate is { } gid && u.Gates.TryGetValue(gid, out var g))
		{
			var far = u.Gates[g.Target].Sector;
			title = $"Tor → {Knowledge.SectorName(u, far)}";
			bool r2 = u.IsReachable(u.PlayerSector, far, profile);
			var nav = Navigator.ForSector(u, far, u.PlayerShip);
			entries.Add(new("target", "Ziel wählen", r2 ? "ohne loszufliegen" : "nicht erreichbar", r2, () =>
			{
				ClosePopup();
				if (g.Sector == u.PlayerSector) GateTargetRequested?.Invoke(gid); else TargetRequested?.Invoke(nav, null);
			}));
			entries.Add(new("auto", "Autopilot hin", r2 ? "fliegt hin und springt" : "nicht erreichbar", r2, () => { ClosePopup(); AutopilotRequested?.Invoke(nav, null); }));
			AddRouteEntry(entries, PlayerRoutes.ForGate(u, gid));
			entries.Add(new("info", "Infos", "Torkarte rechts", true, () => { ClosePopup(); _selGate = gid; _selStation = null; Invalidate(); }));
		}
		else if (hit.Ship is { } shid && u.Ships.TryGetValue(shid, out var ship))
		{
			title = OrderMove.ShipName(u, shid);
			entries.Add(new("menu", "Schiffsmenü", "Übersicht, Module, Befehl", true, () => { ClosePopup(); ShipMenuRequested?.Invoke(shid); }));
			var stop = PlayerRoutes.ForPoint(sel, ship.Position);
			AddRouteEntry(entries, stop, "Punkt hier zur Route");
		}
		else
		{
			title = $"Punkt ({hit.World.X:0}, {hit.World.Y:0})";
			var nav = new NavTarget(sel, hit.World, $"Punkt {Knowledge.SectorName(u, sel)}");
			entries.Add(new("auto", "Autopilot hin", reach ? "fliegt zu diesem Punkt" : "nicht erreichbar", reach, () => { ClosePopup(); AutopilotRequested?.Invoke(nav, null); }));
			AddRouteEntry(entries, PlayerRoutes.ForPoint(sel, hit.World), "Punkt zur Route");
		}
		ShowPopup(title, null, entries, global);
	}

	void AddRouteEntry(List<MapPopupEntry> entries, RouteStop stop, string text = "Zur Route hinzufügen")
	{
		var u = Ctx.U;
		string? why = PlayerRoutes.AddIssue(u, stop);
		entries.Add(new("route", text, why ?? "als Halt hinten an", why is null, () => { PlayerRoutes.Add(u, stop); ClosePopup(); }));
	}

	void OpenRouteList(Vector2 global) =>
		ShowPopup("Route", RouteMenus.ListInfo(Ctx.U),
			RouteMenus.ListEntries(Ctx.U, () => RouteStartRequested?.Invoke(), () => Callable.From(() => OpenRouteList(global)).CallDeferred(), () => ClosePopup()), global);

	void ShowPopup(string title, string? info, List<MapPopupEntry> entries, Vector2 global)
	{
		if (!PopupOpen)
		{
			var p = new MapPopup();
			_popup = p;
			AddChild(p);
			p.Closed += () => { p.QueueFree(); if (_popup == p) _popup = null; Invalidate(); };
		}
		var local = GetGlobalTransform().AffineInverse() * global;
		var frame = Shell is { } l ? new Rect2(l.Center.X, l.Center.Y, l.Center.W, l.Center.H) : new Rect2(Vector2.Zero, Size);
		_popup!.Show(title, info, entries, local, frame);
	}

	protected override void OnOpen()
	{
		_lp.Reset();
		if (_popup is { } p && IsInstanceValid(p)) p.QueueFree();
		_popup = null;
	}

	/// <summary>Was unter einem Kartenpunkt liegt (Station vor Tor vor eigenem Schiff; sonst nur der Weltort).</summary>
	internal readonly record struct MapHit(StationId? Station, GateId? Gate, ShipId? Ship, Vec2 World);

	/// <summary>Selbsttest: Kartenmenü einer Station direkt öffnen.</summary>
	public void TestOpenContextStation(StationId id) => OpenContextAt(new MapHit(id, null, null, Ctx.U.Stations[id].Position), TestStationScreen(id) ?? GetGlobalRect().GetCenter());

	sealed partial class SectorMapCanvas
	{
		public override void _Process(double delta) => _owner?._lp.Process((float)delta);

		/// <summary>Bildpunkt (lokal) eines Weltorts.</summary>
		public Vector2 PlaceWorld(Vec2 world) => Place(world);

		/// <summary>Weltort eines Kartenpunkts (Umkehrung von <see cref="Place"/>).</summary>
		Vec2 Unplace(Vector2 p) { var f = Fit(); return new Vec2((p.X - f.Origin.X) / (f.Scale * f.Stretch), (p.Y - f.Origin.Y) / f.Scale); }

		public MapHit HitAt(Vector2 pos)
		{
			var s = _u.Sectors[_owner.Selected];
			StationId? st = null; float sd = 24f;
			foreach (var (sid, sp) in Spots())
			{
				float d = sp.DistanceTo(pos);
				if (d < sd) { sd = d; st = sid; }
			}
			if (st is { } station) return new MapHit(station, null, null, _u.Stations[station].Position);
			GateId? gate = null; float gd = 22f;
			foreach (var gid in s.Gates.Where(x => Knowledge.KnowsGate(_u, x)))
			{
				float d = Place(_u.Gates[gid].Position).DistanceTo(pos);
				if (d < gd) { gd = d; gate = gid; }
			}
			if (gate is { } g) return new MapHit(null, g, null, _u.Gates[g].Position);
			ShipId? ship = null; float shd = 16f;
			foreach (var sh in Materializer.Snapshot(_u, s.Id, _owner.Ctx.FractionalTick()))
			{
				if (sh.Id == _u.PlayerShip || !_u.Ships.TryGetValue(sh.Id, out var real) || real.Owner != _u.PlayerActor) continue;
				float d = Place(sh.Position).DistanceTo(pos);
				if (d < shd) { shd = d; ship = sh.Id; }
			}
			if (ship is { } sh2) return new MapHit(null, null, sh2, _u.Ships[sh2].Position);
			return new MapHit(null, null, null, Unplace(pos));
		}

		/// <summary>Mehrpunkt-Route: Linie und Nummern der Halte in diesem Sektor (vom Spieler zum nächsten Halt, wenn der hier liegt).</summary>
		void DrawRoute(ref int slot)
		{
			var stops = PlayerRoutes.StopsIn(_u, _owner.Selected);
			_owner.TestRouteNumbers = stops.Count;
			if (stops.Count == 0) return;
			var spots = Spots();
			Vector2 P(RouteStop stop, Vec2 at) => stop.Kind == RouteStopKind.Station && spots.TryGetValue(new StationId(stop.Id), out var sp) ? sp : Place(at);
			var col = new Color(Cyan, 0.85f);
			if (_owner.Selected == _u.PlayerSector && stops[0].Number == 1)
				DrawDashedLine(Place(_u.Ships[_u.PlayerShip].Position), P(stops[0].Stop, stops[0].At), col, 2f, 8f);
			for (int i = 1; i < stops.Count; i++)
				if (stops[i].Number == stops[i - 1].Number + 1) DrawLine(P(stops[i - 1].Stop, stops[i - 1].At), P(stops[i].Stop, stops[i].At), col, 3f, true);
			var font = ThemeDB.FallbackFont;
			foreach (var (n, stop, at) in stops)
			{
				var c = P(stop, at) + new Vector2(-14f, -14f);
				DrawCircle(c, 10f, Cyan);
				DrawArc(c, 10f, 0, Mathf.Tau, 20, new Color(0.02f, 0.04f, 0.09f), 2f, true);
				UiKit.DrawText(this, slot++, font, c + new Vector2(-10f, 5f), n.ToString(), 13, new Color(0.02f, 0.04f, 0.09f), HorizontalAlignment.Center, 20f);
			}
		}
	}
}
