using System.Linq;
using Godot;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 161 (Mehrpunkt-Route, Lücke gegenüber Interstellar Pilot 2): drei Halte per langem Drücken auf der Karte anlegen (Station und Tor auf der
/// Sektorkarte, Nachbarsektor auf der Universumskarte, jeweils „Zur Route hinzufügen“ per echtem Tipp), das Kartenmenü per Zurück-Taste und per Tipp daneben
/// schließen, die Routenliste über „Route“ öffnen, „Route starten“ antippen, die Route im Zeitraffer abfliegen (HUD-Zeile „Route n/3 → …“ zählt mit),
/// danach „Funk“ aus dem Kartenmenü einer Station.
/// </summary>
public partial class SelfTest
{
	StationId _rtStation, _rtFarStation;
	GateId _rtGate;
	SectorId _rtFar;
	Vector2 _rtAt;
	int _rtT0 = -1, _rtDone = -1;
	readonly System.Collections.Generic.HashSet<string> _rtSeen = new();

	/// <summary>Langes Drücken: Finger an <paramref name="at"/> ab Bild <paramref name="from"/>, nach 45 Bildern (0,75 s) loslassen.</summary>
	bool LongPressAt(int f, int from, Vector2 at)
	{
		if (f == from) { _rtAt = at; Tap(at, true); }
		if (f == from + 45) Tap(_rtAt, false);
		return f == from + 45;
	}

	void TapPopup(Ui.Screens.IMapPopupHost host, string key, bool down)
	{
		var b = host.Popup?.Button(key);
		if (b is null) { Fail($"RT: Kartenmenü ohne Eintrag „{key}“ ({string.Join(",", host.Popup?.Buttons.Select(x => x.Text) ?? new string[0])})"); return; }
		TapKit(b, down, key);
	}

	void CheckPopup(Ui.Screens.IMapPopupHost host, string what)
	{
		Check(host.PopupOpen, $"RT: {what}: Kartenmenü offen");
		if (host.Popup is not { } p) return;
		var vis = GetViewport().GetVisibleRect();
		foreach (var b in p.Buttons.Where(b => b.IsVisibleInTree()))
		{
			var r = b.GetGlobalRect();
			if (r.Size.Y < 55.5f) Fail($"RT: {what}: Taste „{b.Text}“ nur {r.Size.Y:0} px hoch");
			if (!vis.Encloses(r)) Fail($"RT: {what}: Taste „{b.Text}“ außerhalb des Bildes ({r})");
		}
	}

	bool RouteStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		var sec = hud.CcSector;
		var uni = hud.CcUniverse;
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 10:
			{
				u.Rules.PlayerRaids = false;   // kein Überfall mitten im Abflug (Warnungen schalten den Zeitraffer ab)
				_m.TestStopShip();
				var me = u.Ships[u.PlayerShip];
				var g = u.Sectors[u.PlayerSector].Gates.Select(id => u.Gates[id]).OrderBy(x => x.Id.Value)
					.First(x => u.StationsById().Any(s => s.Sector == u.Gates[x.Target].Sector && s.Dockable && s.Site is null));
				_rtGate = g.Id; _rtFar = u.Gates[g.Target].Sector;
				_rtStation = u.StationsById().Where(s => s.Sector == u.PlayerSector && s.Dockable && s.Site is null)
					.OrderBy(s => Vec2.Distance(s.Position, g.Position)).First().Id;
				Check(PlayerRoutes.Of(u) is null, "RT: neue Sitzung ohne Route");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Sector);
				return false;
			}
			case 20:
			{
				var at = sec.TestStationScreen(_rtStation);
				if (at is null) { Fail("RT: Station nicht auf der Sektorkarte"); return true; }
				LongPressAt(f, 20, at.Value);
				return false;
			}
			case 40: Check(!sec.PopupOpen, "RT: nach 20 Bildern noch kein Menü (langes Drücken braucht 0,5 s)"); return false;
			case 65: LongPressAt(f, 20, _rtAt); return false;
			case 70:
				CheckPopup(sec, "Station");
				Check(sec.Popup?.Title.Contains(u.Stations[_rtStation].Name.Split(' ')[0]) == true, $"RT: Menütitel nennt die Station ({sec.Popup?.Title})");
				foreach (var k in new[] { "target", "auto", "route", "info", "hail" }) Check(sec.Popup?.Button(k) is not null, $"RT: Stationsmenü bietet „{k}“");
				TapPopup(sec, "route", true);
				return false;
			case 72: TapPopup(sec, "route", false); return false;
			case 78:
			{
				Check(!sec.PopupOpen, "RT: „Zur Route hinzufügen“ schließt das Menü");
				Check(PlayerRoutes.Of(u)?.Stops.Count == 1 && PlayerRoutes.Of(u)!.Stops[0].Kind == RouteStopKind.Station, "RT: Halt 1 = Station");
				var at = sec.TestGateScreen(_rtGate);
				if (at is null) { Fail("RT: Tor nicht auf der Sektorkarte"); return true; }
				LongPressAt(f, 78, at.Value);
				return false;
			}
			case 123: LongPressAt(f, 78, _rtAt); return false;
			case 128:
				CheckPopup(sec, "Tor");
				TapPopup(sec, "route", true);
				return false;
			case 130: TapPopup(sec, "route", false); return false;
			case 136:
				Check(PlayerRoutes.Of(u)?.Stops.Count == 2 && PlayerRoutes.Of(u)!.Stops[1].Kind == RouteStopKind.Gate, "RT: Halt 2 = Tor");
				Check(sec.TestRouteNumbers == 2, $"RT: Sektorkarte zeichnet 2 Haltnummern ({sec.TestRouteNumbers})");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Universe);
				return false;
			case 146:
			{
				var at = uni.TestSectorScreen(_rtFar);
				if (at is null) { Fail("RT: Nachbarsektor nicht auf der Universumskarte"); return true; }
				LongPressAt(f, 146, at.Value);
				return false;
			}
			case 191: LongPressAt(f, 146, _rtAt); return false;
			case 196:
				CheckPopup(uni, "Sektor");
				foreach (var k in new[] { "target", "auto", "route", "info" }) Check(uni.Popup?.Button(k) is not null, $"RT: Sektormenü bietet „{k}“");
				TapPopup(uni, "route", true);
				return false;
			case 198: TapPopup(uni, "route", false); return false;
			case 204:
				Check(PlayerRoutes.Of(u)?.Stops.Count == 3 && PlayerRoutes.Of(u)!.Stops[2].Kind == RouteStopKind.Sector, "RT: Halt 3 = Sektor");
				Check(uni.TestRouteNumbers == 3, $"RT: Universumskarte zeichnet 3 Haltnummern ({uni.TestRouteNumbers})");
				LongPressAt(f, 204, uni.TestSectorScreen(_rtFar)!.Value);
				return false;
			case 249: LongPressAt(f, 204, _rtAt); return false;
			case 254:
			{
				CheckPopup(uni, "Zurück");
				string back = _m.HandleBack();
				Check(back == "kartenmenü" && !uni.PopupOpen && hud.Screens.Current == uni, $"RT: Zurück schließt nur das Kartenmenü ({back})");
				return false;
			}
			case 258: LongPressAt(f, 258, uni.TestSectorScreen(_rtFar)!.Value); return false;
			case 303: LongPressAt(f, 258, _rtAt); return false;
			case 308:
			{
				CheckPopup(uni, "daneben");
				var card = uni.Popup!.GetGlobalTransform() * uni.Popup.CardRect.Position;
				var vis = GetViewport().GetVisibleRect();
				_rtAt = card.X > vis.Size.X / 2f ? new Vector2(vis.Size.X * 0.15f, vis.Size.Y * 0.5f) : new Vector2(vis.Size.X * 0.85f, vis.Size.Y * 0.5f);
				Tap(_rtAt, true);
				return false;
			}
			case 310: Tap(_rtAt, false); return false;
			case 314:
				Check(!uni.PopupOpen && hud.Screens.Current == uni, "RT: Tipp neben das Menü schließt es");
				TapKit(uni.TestRouteButton, true, "Route");
				return false;
			case 316: TapKit(uni.TestRouteButton, false, "Route"); return false;
			case 322:
				CheckPopup(uni, "Routenliste");
				Check(uni.Popup?.Title == "Route", $"RT: Routenliste ({uni.Popup?.Title})");
				foreach (var k in new[] { "start", "dock", "stop:1", "stop:2", "stop:3", "clear" }) Check(uni.Popup?.Button(k) is not null, $"RT: Routenliste hat „{k}“");
				Check(uni.Popup?.Button("dock")?.Text.EndsWith("aus") == true, "RT: Andocken standardmäßig aus");
				TapPopup(uni, "start", true);
				return false;
			case 324: TapPopup(uni, "start", false); return false;
			case 330:
				Check(hud.Screens.Current is not Ui.Screens.CommandScreen, "RT: „Route starten“ kehrt in den Flug zurück");
				Check(_m.TestAutopilotOn && _m.TestRouteFlying, "RT: Autopilot fliegt die Route");
				Check(hud.Flight.TestRoute.StartsWith("Route 1/3 →"), $"RT: HUD-Zeile ({hud.Flight.TestRoute})");
				Check(hud.Flight.TestRouteFits, "RT: HUD-Zeile passt ohne Kürzung");
				_m.TestSetFast(true);
				_rtT0 = f; _rtSeen.Clear(); _rtDone = -1;
				return false;
		}
		if (_rtT0 > 0 && _rtDone < 0 && f > _rtT0)
		{
			string line = hud.Flight.TestRoute;
			if (line.Length > 9) _rtSeen.Add(line[..9]);
			if (PlayerRoutes.Of(u) is { Finished: true } && !_m.TestAutopilotOn)
			{
				_m.TestSetFast(false);
				Check(_rtSeen.Contains("Route 2/3") && _rtSeen.Contains("Route 3/3"), $"RT: HUD zählte die Halte mit ({string.Join(", ", _rtSeen)})");
				Check(u.PlayerSector == _rtFar, $"RT: am Ende im Sektor hinter dem Tor ({u.Sectors[u.PlayerSector].Name})");
				Check(hud.Flight.TestRoute.Length == 0, "RT: HUD-Zeile nach dem Abflug weg");
				GD.Print($"[RT] Route mit 3 Halten in {f - _rtT0} Bildern abgeflogen (Zeitraffer)");
				_rtDone = f;
				_rtFarStation = u.StationsById().Where(s => s.Sector == _rtFar && s.Dockable && s.Site is null).OrderBy(s => s.Id.Value).First().Id;
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Sector);
			}
			else if (f - _rtT0 > 12000) { Fail($"RT: Route nicht abgeflogen ({hud.Flight.TestRoute}, Autopilot {_m.TestAutopilotOn}, Halt {PlayerRoutes.Of(u)?.Next})"); return true; }
			return false;
		}
		if (_rtDone > 0)
		{
			int g = f - _rtDone;
			if (g == 8) LongPressAt(0, 0, sec.TestStationScreen(_rtFarStation) ?? Vector2.Zero);
			if (g == 53) Tap(_rtAt, false);
			if (g == 58) { CheckPopup(sec, "Funk"); TapPopup(sec, "hail", true); }
			if (g == 60) TapPopup(sec, "hail", false);
			if (g == 66)
			{
				Check(hud.Flight.HailOpen, "RT: „Funk“ aus dem Kartenmenü öffnet das Gespräch mit der Station");
				string back = _m.HandleBack();
				Check(back == "funk" && !hud.Flight.HailOpen, $"RT: Zurück schließt den Funk ({back})");
				_rtT0 = -1; _rtDone = -1;
				return true;
			}
		}
		return false;
	}
}
