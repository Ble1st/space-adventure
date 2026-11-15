using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Ui;
using SpaceAdventure.Ui.Kit;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 113 (Runde 37b, Handytest r37). Alles über echte Tipps/Tasten:
/// (N1) jeder Weg Dock/Flug ↔ Menü/Karte/Kommandozentrale und zurück endet in der Grundansicht (angedockt Dock-Menü, sonst Flug-HUD) — „≡ Menü“ → „Karte“ →
/// „Schließen“ (der Blocker), Karte → Sektor antippen, Menü „Schließen“, Menü-Hintergrund, „← Flug“, Escape und Zurück-Taste (WM_GO_BACK) aus Dock-Bereich,
/// Zentrale, Menü, Karte, Dock-Menü und Flug; (N2) die Leiste der Zentrale lässt sich per Wisch in beide Richtungen rollen und bleibt dort über Neuaufbauten;
/// (N3) der Rahmen des Mittelteils hat in jedem Bereich die Gerüstgröße.
/// </summary>
public partial class SelfTest
{
	readonly List<(string Name, int Wait, Func<bool> Act)> _r37b = new();
	int _r37bIdx, _r37bWait = -1;
	Vector2 _r37bAt;
	int _r37bRail;

	bool Round37bStage(int f)
	{
		var hud = _m.HudRef;
		if (f == 1)
		{
			_r37b.Clear(); _r37bIdx = 0; _r37bWait = -1;
			Build37b();
			return false;
		}
		// Runde 37c: zweite Zustellung desselben Drucks im nächsten Bild (wie am Gerät, 2–16 ms später)
		if (_r37bSecondBack) { _r37bSecondBack = false; GetTree().Root.PropagateNotification((int)Node.NotificationWMGoBackRequest); }
		if (_r37bIdx >= _r37b.Count) return true;
		if (_r37bWait > 0) { _r37bWait--; return false; }
		var (name, wait, act) = _r37b[_r37bIdx];
		bool ok;
		try { ok = act(); }
		catch (Exception e) { Check(false, $"37b {name}: {e.Message}"); ok = true; }
		if (!ok) return false;   // Schritt wartet noch (z. B. Layout)
		_r37bIdx++;
		_r37bWait = wait;
		return false;
	}

	void Step(string name, int wait, Func<bool> act) => _r37b.Add((name, wait, act));
	void Step(string name, int wait, Action act) => _r37b.Add((name, wait, () => { act(); return true; }));

	/// <summary>Echter Tipp (Drücken, zwei Bilder, Loslassen) auf die Mitte eines Rechtecks.</summary>
	void TapSteps(string name, Func<Rect2?> where)
	{
		Step(name + " (drücken)", 2, () => { var r = where(); if (r is null) { Check(false, $"37b {name}: Ziel fehlt"); return; } _r37bAt = r.Value.GetCenter(); Tap(_r37bAt, true); });
		Step(name + " (loslassen)", 12, () => Tap(_r37bAt, false));
	}

	Rect2? ButtonRect(Node root, string text) => Buttons(root).FirstOrDefault(b => b.Text == text && b.IsVisibleInTree())?.GetGlobalRect();

	void ExpectBase(string what)
	{
		CheckSingleBack(what);
		var hud = _m.HudRef;
		bool docked = _m.TestDocked;
		bool ok = docked ? hud.Screens.Current == hud.DockMenu && hud.DockMenu.Visible && !hud.Map.Visible
			: (hud.Screens.Current is null || hud.Screens.Current == hud.Flight) && hud.Flight.Visible && !hud.Map.Visible;
		Check(ok, $"37b N1 {what}: zurück in der Grundansicht ({(docked ? "Dock-Menü" : "Flug-HUD")}; jetzt {hud.Screens.Current?.Id ?? "-"}, Flug sichtbar {hud.Flight.Visible}, Karte {hud.Map.Visible})");
	}

	/// <summary>Echter Wisch auf der Leiste: <paramref name="dir"/> +1 = Finger nach unten (Inhalt nach oben, zum Anfang), −1 = Finger nach oben.</summary>
	void RailSwipe(string name, Func<Control> screen, int dir)
	{
		Step("Wisch " + name + " (drücken)", 2, () =>
		{
			var rail = RailScroll(screen());
			if (rail is null) { Check(false, "37b N2: Leiste fehlt"); return; }
			var r = rail.GetGlobalRect();
			_r37bAt = new Vector2(r.GetCenter().X, dir > 0 ? r.Position.Y + 30f : r.End.Y - 30f);
			PushMouse(_r37bAt, true, false);
		});
		Step("Wisch " + name + " 1", 2, () => PushMouse(_r37bAt + new Vector2(0, 40 * dir), true, true));
		Step("Wisch " + name + " 2", 2, () => PushMouse(_r37bAt + new Vector2(0, 400 * dir), true, true));
		Step("Wisch " + name + " loslassen", 40, () => PushMouse(_r37bAt + new Vector2(0, 400 * dir), false, false));
	}

	/// <summary>
	/// Runde 37c (Handytest r37b, Blocker: ein Druck wirkte doppelt): ein echter Druck der Android-Zurück-Taste — im selben Bild die Benachrichtigung und
	/// ein Tastenereignis „Zurück“, im nächsten Bild die Benachrichtigung noch einmal. Geprüft wird, dass genau eine Zustellung verarbeitet wurde.
	/// </summary>
	void Back()
	{
		_r37bHandled0 = _m.BackHandled;
		GetTree().Root.PropagateNotification((int)Node.NotificationWMGoBackRequest);
		GetViewport().PushInput(new InputEventKey { Keycode = Key.Back, Pressed = true }, true);
		GetViewport().PushInput(new InputEventKey { Keycode = Key.Back, Pressed = false }, true);
		_r37bSecondBack = true;
		_r37bBackPending = true;
	}
	bool _r37bSecondBack, _r37bBackPending;
	int _r37bHandled0;
	/// <summary>Nach einem simulierten Druck: genau eine Zustellung verarbeitet (sonst Doppelwirkung).</summary>
	void CheckSingleBack(string what)
	{
		if (!_r37bBackPending) return;
		_r37bBackPending = false;
		Check(_m.BackHandled - _r37bHandled0 == 1, $"37c: ein Druck der Zurück-Taste wirkt genau einmal ({what}: {_m.BackHandled - _r37bHandled0} verarbeitet, {_m.BackIgnored} verworfen insgesamt)");
	}
	void Escape()
	{
		GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true }, true);
		GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = false }, true);
	}

	void Build37b()
	{
		var hud = _m.HudRef;
		Func<SpaceSim.Core.Model.Universe> u = () => _m.Session.U;
		Step("Start", 60, () =>
		{
			_m.TestCloseCommand();
			if (_m.TestDocked) _m.TestUndock();
			hud.Map.Visible = false;
			hud.HideAlert();   // Runde 37c: eine offene Meldung früherer Stufen nähme den ersten Zurück-Druck (innere Ebene zuerst)
			var root = GetTree().Root;
			if (root.Size.X < 640 || root.Size.X == root.Size.Y) root.Size = new Vector2I(1280, 720);
			UiScale.TestForce = null; UiScale.TestPpi = null; UiScale.Set(UiSize.Normal);
			var yard = u().StationsById().First(x => x.Name == "Werft Delta");
			_m.TestDockAt(yard.Id);
		});
		Step("angedockt", 10, () => ExpectBase("nach dem Andocken"));
		// ── der Blocker: Dock → ≡ Menü → Karte → Schließen ──
		TapSteps("≡ Menü", () => ButtonRect(hud.DockMenu, "≡ Menü"));
		Step("Menü offen", 0, () => Check(hud.Screens.Current == hud.Command, $"37b N1: „≡ Menü“ öffnet das Menü ({hud.Screens.Current?.Id})"));
		TapSteps("Karte", () => ButtonRect(hud.Command, "Karte"));
		Step("Karte offen", 0, () => Check(hud.Map.Visible, "37b N1: „Karte“ öffnet die Sternenkarte"));
		TapSteps("Karte schließen", () => hud.Map.GetChildren().OfType<Ui.TouchButton>().FirstOrDefault(b => b.Text == "Schließen")?.GetGlobalRect());
		Step("nach Karte schließen", 0, () => ExpectBase("Dock → Menü → Karte → Schließen"));
		// Karte → Sektor antippen
		TapSteps("≡ Menü 2", () => ButtonRect(hud.DockMenu, "≡ Menü"));
		TapSteps("Karte 2", () => ButtonRect(hud.Command, "Karte"));
		TapSteps("Sektor auf der Karte", () => hud.Map.TestSectorScreen(u().Sectors.Values.First(s => s.Id != u().PlayerSector).Id) is { } p ? new Rect2(p - Vector2.One, Vector2.One * 2f) : null);
		Step("nach Sektorwahl", 0, () => ExpectBase("Dock → Menü → Karte → Sektor antippen"));
		// Menü schließen, Menü-Hintergrund
		TapSteps("≡ Menü 3", () => ButtonRect(hud.DockMenu, "≡ Menü"));
		TapSteps("Schließen", () => ButtonRect(hud.Command, "Schließen"));
		Step("nach Menü schließen", 0, () => ExpectBase("Dock → Menü → Schließen"));
		TapSteps("≡ Menü 4", () => ButtonRect(hud.DockMenu, "≡ Menü"));
		TapSteps("Menü-Hintergrund", () => hud.Command.WindowGlobal is { } w ? new Rect2(new Vector2(MathF.Max(4f, w.Position.X / 2f), w.End.Y - 20f), Vector2.One * 2f) : null);
		Step("nach Hintergrund", 0, () => ExpectBase("Dock → Menü → Hintergrund"));
		// Zurück-Taste und Escape
		Step("Handel öffnen", 12, () => { _m.TestOpenDockArea(Ui.Screens.DockAreas.Trade); hud.DockTrade.CloseOpenRow(); hud.HideAlert(); });
		Step("Zurück aus Handel", 10, () => Back());
		Step("nach Zurück Handel", 0, () => ExpectBase("Dock-Bereich → Zurück-Taste"));
		Step("Handel öffnen 2", 12, () => { _m.TestOpenDockArea(Ui.Screens.DockAreas.Trade); hud.DockTrade.CloseOpenRow(); });
		Step("Escape aus Handel", 10, () => Escape());
		Step("nach Escape Handel", 0, () => ExpectBase("Dock-Bereich → Escape"));
		Step("Zurück im Dock-Menü", 10, () => Back());
		Step("nach Zurück Dock-Menü", 0, () => { ExpectBase("Dock-Menü → Zurück-Taste (bleibt, App läuft weiter)"); Check(_m.TestDocked, "37b: Zurück im Dock-Menü dockt nicht ab"); });
		TapSteps("≡ Menü 5", () => ButtonRect(hud.DockMenu, "≡ Menü"));
		Step("Zurück aus Menü", 10, () => Back());
		Step("nach Zurück Menü", 0, () => ExpectBase("Dock → Menü → Zurück-Taste"));
		TapSteps("≡ Menü 6", () => ButtonRect(hud.DockMenu, "≡ Menü"));
		TapSteps("Karte 3", () => ButtonRect(hud.Command, "Karte"));
		Step("Zurück aus Karte", 10, () => Back());
		Step("nach Zurück Karte", 0, () => ExpectBase("Dock → Menü → Karte → Zurück-Taste"));
		// Zentrale aus dem Dock: „← Flug“ und Zurück
		Step("Zentrale", 14, () => _m.TestOpenCcArea(Ui.Screens.CommandAreas.Fleet));
		TapSteps("← Flug", () => hud.Screens.Current is Node sc ? ButtonRect(sc, "← Flug") : null);
		Step("nach ← Flug", 0, () => ExpectBase("Dock → Zentrale → ← Flug"));
		Step("Zentrale 2", 14, () => _m.TestOpenCcArea(Ui.Screens.CommandAreas.Universe));
		Step("Zurück aus Zentrale", 10, () => Back());
		Step("nach Zurück Zentrale", 0, () => ExpectBase("Dock → Zentrale → Zurück-Taste"));
		// ── Flug ──
		Step("abdocken", 60, () => _m.TestUndock());
		Step("Flug", 0, () => ExpectBase("abgedockt"));
		Step("Zurück im Flug", 10, () => Back());
		Step("Menü durch Zurück", 0, () => Check(hud.Screens.Current == hud.Command, $"37b: Zurück-Taste im Flug öffnet das Menü ({hud.Screens.Current?.Id})"));
		Step("Zurück schließt Menü", 10, () => Back());
		Step("nach Zurück Menü Flug", 0, () => ExpectBase("Flug → Menü → Zurück-Taste"));
		Step("Karte im Flug", 10, () => _m.TestCommand("map"));
		TapSteps("Karte schließen Flug", () => hud.Map.GetChildren().OfType<Ui.TouchButton>().FirstOrDefault(b => b.Text == "Schließen")?.GetGlobalRect());
		Step("nach Karte Flug", 0, () => ExpectBase("Flug → Karte → Schließen"));
		Step("Karte im Flug 2", 10, () => _m.TestCommand("map"));
		Step("Escape Karte Flug", 10, () => Escape());
		Step("nach Escape Karte Flug", 0, () => ExpectBase("Flug → Karte → Escape"));

		// ── Runde 37c: Doppelzustellung im Flug (Menü auf, nicht auf+zu), innere Ebenen zuerst, verschwundene Station ──
		Step("Zurück im Flug (doppelt zugestellt)", 12, () => Back());
		Step("Menü genau einmal auf", 0, () => { CheckSingleBack("Flug"); Check(hud.Screens.Current == hud.Command, $"37c: Zurück im Flug öffnet das Menü (bleibt offen, kein Auf+Zu; {hud.Screens.Current?.Id})"); });
		Step("Zurück im Menü (doppelt)", 12, () => Back());
		Step("Menü genau einmal zu", 0, () => ExpectBase("Flug → Menü → Zurück (doppelt zugestellt)"));
		Step("Meldung offen", 4, () => hud.Alert("Prüfmeldung für die Zurück-Taste", 30f));
		Step("Zurück schließt die Meldung", 12, () => Back());
		Step("nur die Meldung zu", 0, () => { CheckSingleBack("Meldung"); Check(hud.TestAlert.Length == 0 && hud.Screens.Current != hud.Command, $"37c: Zurück schließt zuerst die Meldung ({hud.Screens.Current?.Id ?? "-"})"); });
		Step("Startpakete", 16, () => { _m.TestOpenCcArea(Ui.Screens.CommandAreas.System); hud.CcSystem.TestChoosing = true; });
		Step("Zurück aus Startpaketen", 12, () => Back());
		Step("Startpakete zu, System bleibt", 0, () => { CheckSingleBack("Startpakete"); Check(hud.Screens.Current == hud.CcSystem && !hud.CcSystem.TestChoosing, $"37c: Zurück schließt die Startpaket-Auswahl, „System“ bleibt ({hud.Screens.Current?.Id})"); });
		Step("Zurück aus System", 12, () => Back());
		Step("nach Zurück System", 0, () => ExpectBase("System → Zurück"));
		Step("Fraktionsdetail schmal", 20, () => { UiScale.TestPpi = 396f; UiScale.TestForce = 1.3f; UiScale.Apply(); _m.TestOpenCcArea(Ui.Screens.CommandAreas.Factions); hud.CcFactions.Selected = hud.CcFactions.TestRows[0].Id; hud.CcFactions.TestDetailOpen = true; });
		Step("Zurück im Fraktionsdetail", 12, () => Back());
		Step("zur Liste", 0, () => { CheckSingleBack("Fraktionsdetail"); Check(hud.Screens.Current == hud.CcFactions && !hud.CcFactions.TestDetailOpen, $"37c: Zurück im schmalen Fraktionsdetail führt zur Liste ({hud.Screens.Current?.Id}, Detail {hud.CcFactions.TestDetailOpen})"); });
		Step("Zurück aus Fraktionen", 12, () => { Back(); });
		Step("nach Zurück Fraktionen", 0, () => { ExpectBase("Fraktionsliste → Zurück"); UiScale.TestForce = null; UiScale.TestPpi = null; UiScale.Set(UiSize.Normal); });
		Step("Werft andocken", 60, () => _m.TestDockAt(u().StationsById().First(x => x.Name == "Werft Delta").Id));
		Step("Handel mit offener Zeile", 16, () => { _m.TestOpenDockArea(Ui.Screens.DockAreas.Trade); hud.DockTrade.TestOpenRow(hud.DockTrade.TestGoods[0]); });
		Step("Zurück schließt die Zeile", 12, () => Back());
		Step("Zeile zu, Handel bleibt", 0, () => { CheckSingleBack("Handelszeile"); Check(hud.Screens.Current == hud.DockTrade && hud.DockTrade.TestExpanded is null, $"37c: Zurück schließt zuerst die offene Handelszeile ({hud.Screens.Current?.Id}, offen {hud.DockTrade.TestExpanded?.Value})"); });
		Step("Zurück aus Handel 2", 12, () => Back());
		Step("nach Handel 2", 0, () => ExpectBase("Handel → Zurück"));
		Step("Taxi", 16, () => _m.TestOpenDockArea(Ui.Screens.DockAreas.Taxi));
		Step("Zurück aus Taxi", 12, () => Back());
		Step("nach Taxi", 0, () => ExpectBase("Taxi-Buchung → Zurück"));
		// M1: die angedockte Station verschwindet
		Step("Station entfernen", 4, () =>
		{
			var st = u().Stations[u().StationsById().First(x => x.Name == "Werft Delta").Id];
			SpaceSim.Core.Simulation.Departures.RemoveStation(u(), st, SpaceSim.Core.Model.DepartureReason.Bankrupt, "test");
		});
		Step("Flug nach Stationsverlust", 20, () => { });
		Step("im Flug mit Meldung", 0, () => Check(!_m.TestDocked && hud.Flight.Visible && hud.Screens.Current is null or Ui.Screens.FlightHud && _m.LastForcedUndock.Length > 0,
			$"37c M1: angedockte Station verschwindet → Flug mit Meldung (angedockt {_m.TestDocked}, Flug {hud.Flight.Visible}, Bildschirm {hud.Screens.Current?.Id ?? "-"}, Meldung „{_m.LastForcedUndock}“)"));
		Step("Menü nach Stationsverlust", 10, () => { _m.TestCommand("close"); });
		Step("Grundansicht nach Stationsverlust", 0, () => ExpectBase("nach Stationsverlust"));

		// ── Runde 37c (Handytest r37b k02/k03): Beschriftungen der Sektorkarte bleiben beim Auswählen einer Station, wo sie waren ──
		Dictionary<string, Vector2>? before = null;
		Dictionary<string, Vector2> MapLabels()
		{
			var d = new Dictionary<string, Vector2>();
			foreach (var e in DrawnText.All)
				if (GodotObject.InstanceFromId(e.Owner) is Control c && c.GetType().Name == "SectorMapCanvas" && c.IsVisibleInTree()) d[e.Shown] = e.Local.Position;
			return d;
		}
		Step("Sektor 1,3", 30, () => { DrawnText.Enabled = true; UiScale.TestPpi = 396f; UiScale.TestForce = 1.3f; UiScale.Apply(); hud.CcSector.ShowPlayerSector(u().PlayerSector); _m.TestOpenCcArea(Ui.Screens.CommandAreas.Sector); });
		Step("Beschriftungen vorher", 0, () => { before = MapLabels(); });
		Step("Station wählen", 20, () => hud.CcSector.TestSelectStation(u().Sectors[u().PlayerSector].Stations.OrderBy(x => x.Value).Skip(2).First()));
		Step("Beschriftungen nachher", 0, () =>
		{
			var after = MapLabels();
			var moved = before!.Where(kv => after.TryGetValue(kv.Key, out var p) && p.DistanceTo(kv.Value) > 0.5f).Select(kv => kv.Key).ToList();
			var gone = before!.Keys.Where(k => !after.ContainsKey(k)).ToList();
			Check(before!.Count > 3 && moved.Count == 0 && gone.Count == 0, $"37c: Beschriftungen der Sektorkarte bleiben beim Auswählen stehen ({before!.Count} Namen; verschoben [{string.Join(", ", moved)}], verschwunden [{string.Join(", ", gone)}])");
			_m.TestCloseCommand(); UiScale.TestForce = null; UiScale.TestPpi = null; UiScale.Set(UiSize.Normal);
		});

		// ── Runde 37d: Entprellung mit Zeitgrenze (niedrige Bildrate nachgestellt) und Zurück während der Andock-Animation ──
		ulong fakeMs = 1_000_000;
		int h0 = 0;
		void Note() => GetTree().Root.PropagateNotification((int)Node.NotificationWMGoBackRequest);
		Step("Uhr nachstellen", 6, () => { _m.TestCloseCommand(); if (_m.TestDocked) _m.TestUndock(); hud.HideAlert(); _m.BackClock = () => fakeMs; });
		Step("langsam: erster Druck", 2, () => { _r37bBackPending = false; h0 = _m.BackHandled; fakeMs += 1000; Note(); });
		Step("langsam: zweiter Druck 1 Bild und 200 ms später", 2, () => { fakeMs += 200; Note(); });
		Step("langsam: beide verarbeitet", 12, () =>
		{
			Check(_m.BackHandled - h0 == 2, $"37d: bei niedriger Bildrate (2 Bilder, 200 ms) zählen zwei schnelle Drücke als zwei ({_m.BackHandled - h0} verarbeitet)");
			Check(hud.Screens.Current is null or Ui.Screens.FlightHud && hud.Flight.Visible, $"37d: Menü auf und wieder zu ({hud.Screens.Current?.Id ?? "-"})");
		});
		Step("schnell: Doppelzustellung", 1, () => { h0 = _m.BackHandled; fakeMs += 1000; Note(); });
		Step("schnell: zweite Zustellung 10 ms später", 12, () => { fakeMs += 10; Note(); });
		Step("schnell: einmal", 0, () => Check(_m.BackHandled - h0 == 1 && hud.Screens.Current == hud.Command, $"37d: Doppelzustellung nach 10 ms bleibt ein Druck ({_m.BackHandled - h0} verarbeitet, {hud.Screens.Current?.Id ?? "-"})"));
		Step("Menü zu", 12, () => { fakeMs += 1000; Note(); });
		Step("Uhr zurück", 4, () => { _m.BackClock = () => Time.GetTicksMsec(); ExpectBase("nach dem Uhrtest"); });
		Step("andocken mit Animation", 1, () => { hud.HideAlert(); _m.TestDockAt(u().StationsById().First(x => x.Name == "Kraftwerk Delta" || x.Sector == u().PlayerSector && x.Dockable).Id); });
		Step("Zurück im Anflug", 2, () => { Check(_m.TestTransition, "37d: Anflug-Animation läuft"); Back(); });
		Step("Zurück ignoriert", 60, () => { _r37bBackPending = false; Check(_m.LastBackResult == "animation" && hud.Screens.Current != hud.Command, $"37d: Zurück während der Andock-Animation tut nichts ({_m.LastBackResult}, {hud.Screens.Current?.Id ?? "-"})"); });
		Step("nach der Animation", 0, () => ExpectBase("nach der Andock-Animation"));
		Step("abdocken 37d", 40, () => _m.TestUndock());

		// ── N2: Leiste der Zentrale bei 1,3 in beide Richtungen wischen — zuerst aus „System“ (aktiver Eintrag unten) nach oben, dann wieder nach unten ──
		Step("Zentrale 1,3 System", 24, () => { UiScale.TestPpi = 396f; UiScale.TestForce = 1.3f; UiScale.Apply(); Ui.Screens.CommandScreen.RailPos = 0; Ui.Screens.CommandScreen.ForgetRailArea(); _m.TestOpenCcArea(Ui.Screens.CommandAreas.System); });
		Step("System: Leiste unten", 0, () =>
		{
			var rail = RailScroll(hud.CcSystem);
			_r37bRail = rail is null ? 0 : (int)(rail.GetVScrollBar().MaxValue - rail.GetVScrollBar().Page);
			Check(rail is not null && _r37bRail > 20 && rail.ScrollVertical > 20, $"37b N2: „System“ holt seinen Eintrag ins Bild (Leiste {rail?.ScrollVertical}/{rail?.WantedScroll} von {_r37bRail}, RailPos {Ui.Screens.CommandScreen.RailPos}, Höhe {rail?.Size.Y}, Bereich {hud.CcSystem.AreaId})");
		});
		RailSwipe("nach oben", () => hud.CcSystem, +1);
		Step("oben nach Neuaufbau", 0, () =>
		{
			var rail = RailScroll(hud.CcSystem);
			Check(rail is not null && rail.ScrollVertical <= 2, $"37b N2: Leiste lässt sich aus „System“ nach oben wischen und bleibt dort über Neuaufbauten ({rail?.ScrollVertical})");
		});
		TapSteps("Universum in der Leiste", () => ButtonRect(hud.CcSystem, "Universum"));
		Step("Universum offen", 0, () => Check(hud.Screens.Current == hud.CcUniverse, $"37b N2: nach dem Zurückrollen ist „Universum“ erreichbar ({hud.Screens.Current?.Id})"));
		RailSwipe("nach unten", () => hud.CcUniverse, -1);
		Step("unten nach Neuaufbau", 0, () =>
		{
			var rail = RailScroll(hud.CcUniverse);
			Check(rail is not null && rail.ScrollVertical >= _r37bRail - 2, $"37b N2: Leiste bleibt unten über Neuaufbauten ({rail?.ScrollVertical} von {_r37bRail})");
		});
		RailSwipe("wieder nach oben", () => hud.CcUniverse, +1);
		Step("wieder oben", 0, () =>
		{
			var rail = RailScroll(hud.CcUniverse);
			Check(rail is not null && rail.ScrollVertical <= 2, $"37b N2: Leiste wieder ganz oben ({rail?.ScrollVertical})");
		});
		// ── N3: Rahmen des Mittelteils in jedem Bereich ──
		foreach (var (id, _, _) in Ui.Screens.CommandAreas.All)
		{
			string area = id;
			Step("Bereich " + area, 16, () => _m.TestOpenCcArea(area));
			Step("Rahmen " + area, 0, () =>
			{
				var cc = (hud.Screens.Current as Ui.Screens.CommandScreen)?.CenterCheck;
				Check(cc is { } c && MathF.Abs(c.Actual.Size.X - c.ExpectedSize.X) <= 1.5f && MathF.Abs(c.Actual.Size.Y - c.ExpectedSize.Y) <= 1.5f,
					$"37b N3: Mittelteil „{area}“ hat die Gerüstgröße ({cc?.Actual.Size} statt {cc?.ExpectedSize})");
			});
		}
		Step("Ende", 6, () => { _m.TestCloseCommand(); UiScale.TestForce = null; UiScale.TestPpi = null; UiScale.Set(UiSize.Normal); });
	}
}
