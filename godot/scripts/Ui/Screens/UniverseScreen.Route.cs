using System;
using System.Collections.Generic;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Mehrpunkt-Route auf der Universumskarte: langes Drücken auf einen Sektor öffnet das Kartenmenü („Ziel wählen“, „Autopilot hin“, „Zur Route hinzufügen“,
/// „Infos“), die Taste „Route“ die Routenliste; Linie und Nummern zeichnet die Karte (<see cref="PlayerRoutes.MapPath"/>).
/// </summary>
public sealed partial class UniverseScreen : IMapPopupHost
{
	/// <summary>„Ziel wählen“ (ohne Autopilot) für einen Sektor.</summary>
	public event Action<SectorId>? TargetSectorRequested;
	/// <summary>„Route starten“.</summary>
	public event Action? RouteStartRequested;

	readonly LongPress _lp = new();
	MapPopup? _popup;
	public MapPopup? Popup => PopupOpen ? _popup : null;
	public bool PopupOpen => _popup is { } p && IsInstanceValid(p) && p.IsInsideTree() && p.Visible;
	protected override bool HoldRebuild => _lp.Down || PopupOpen;
	/// <summary>Selbsttest: Taste „Route“ und zuletzt gezeichnete Routennummern.</summary>
	public KitButton? TestRouteButton { get; private set; }
	public int TestRouteNumbers { get; internal set; }

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
		if (_map.HitAt(canvasPos) is not { } sid) return;
		OpenContext(sid, _map.GetGlobalTransform() * canvasPos);
	}

	/// <summary>Kartenmenü eines Sektors (globale Bildposition des Drückens).</summary>
	public void OpenContext(SectorId sid, Vector2 global)
	{
		var u = Ctx.U;
		if (!u.Sectors.ContainsKey(sid)) return;
		bool known = Knowledge.KnowsSector(u, sid);
		int hops = known ? PlayerRoute(u, sid)?.Count ?? -1 : -1;
		var stop = PlayerRoutes.ForSector(sid);
		string? addWhy = PlayerRoutes.AddIssue(u, stop);
		var entries = new List<MapPopupEntry>
		{
			new("target", "Ziel wählen", hops > 0 ? "ohne loszufliegen" : hops == 0 ? "du bist hier" : "nicht erreichbar", hops > 0, () => { ClosePopup(); TargetSectorRequested?.Invoke(sid); }),
			new("auto", "Autopilot hin", hops > 0 ? $"{hops} {(hops == 1 ? "Sprung" : "Sprünge")}" : hops == 0 ? "du bist hier" : "nicht erreichbar", hops > 0, () => { ClosePopup(); PlanRoute?.Invoke(sid); }),
			new("route", "Zur Route hinzufügen", addWhy ?? "als Halt hinten an", addWhy is null, () => { PlayerRoutes.Add(u, stop); ClosePopup(); }),
			new("info", "Infos", known ? "Sektorkarte öffnen" : "Sektor unbekannt", known, () => { ClosePopup(); SelectedSector = sid; OpenSectorMap?.Invoke(sid); }),
		};
		ShowPopup(Knowledge.SectorName(u, sid), null, entries, global);
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

	/// <summary>Selbsttest: Kartenmenü eines Sektors direkt öffnen und den Druck auf der Karte.</summary>
	public void TestOpenContext(SectorId s) => OpenContext(s, TestSectorScreen(s) ?? GetGlobalRect().GetCenter());
}
