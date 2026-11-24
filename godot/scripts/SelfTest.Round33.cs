using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Ui;
using SpaceAdventure.Ui.Kit;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 109 (Runde 33, UI-ANPASSUNG.md Etappe 2): (1) bei Faktor 1 und 1280 × 720 die klassische Anordnung (Lagen wie bisher);
/// (2) Hamburger-Menü: Zeile „UI-Größe“ passt (gemessen), ein echter Tipp schaltet die Stufe um, die Beschriftung zeigt die neue Stufe, der Hinweis liegt
/// sichtbar über dem offenen Menü (Handytest A15: der Tipp hatte keine Wirkung — die Aktion landete im default-Zweig von OnCommandAction);
/// (3) Faktor 1,3 und 1,15 (A15-DPI): Flug-Schicht skaliert, kompakte Zonen, jede sichtbare Taste in der Fläche und ohne Überlappung, die Meldung im Flug
/// liegt auf keiner Taste; (4) Safe Area an allen vier Rändern eingehalten; (5) zurück auf normal/Faktor 1.
/// </summary>
public partial class SelfTest
{
	int _r33Step;

	bool Round33Stage(int f)
	{
		var root = GetTree().Root;
		var fl = _m.HudRef.Flight;
		switch (f)
		{
			case 1:
			{
				_m.TestCloseCommand();
				if (root.Size.X < 640) root.Size = new Vector2I(1280, 720);
				UiScale.TestPpi = 96f; UiScale.TestCap = null; FlightHud_TestInsets(null);
				UiScale.Set(UiSize.Normal);
				return false;
			}
			case 4:
			{
				var vis = GetViewport().GetVisibleRect().Size;
				Check(fl.Scale.X == 1f && fl.Layout is { Compact: false }, $"E2: Faktor 1 bei {vis} → klassische Anordnung (Schicht {fl.Scale.X}, kompakt {fl.Layout?.Compact})");
				if (Mathf.Abs(vis.X - 1280f) < 1f && Mathf.Abs(vis.Y - 720f) < 1f)
					// UI-Umbau: Regler waagerecht unten rechts, RÜCK links daneben (Core: FlightHudLayout)
					Check(fl.TestSliderRect == new Rect2(910, 624, 360, 86) && fl.TestReverseRect == new Rect2(830, 642, 72, 68),
						$"E2: 1280 × 720 Grundanordnung (Regler {fl.TestSliderRect}, RÜCK {fl.TestReverseRect})");
				// Menüzeile „UI-Größe“
				UiScale.TestPpi = 396f;
				UiScale.Set(UiSize.Normal);
				_m.TestOpenCommand();
				return false;
			}
			case 10:
			{
				var b = _m.HudRef.Command.TestUiSizeButton;
				if (b is null) { Check(false, "E2: Menüzeile „UI-Größe“ fehlt"); return true; }
				Check(b.Overflow().Length == 0 && b.Text == "UI-Größe: normal" && b.Sub.Contains("mm"),
					$"E2: Menüzeile passt („{b.Text}“ / „{b.Sub}“, Überlauf „{b.Overflow()}“)");
				// sichtbar machen (die Zeile liegt unten in der rollenden Liste)
				if (FindScrollAncestor(b) is { } sc) sc.EnsureControlVisible(b);
				return false;
			}
			case 14:
			{
				var b = _m.HudRef.Command.TestUiSizeButton!;
				_r33Tap = root.GetFinalTransform() * b.GetGlobalRect().GetCenter();
				PushWindow(_r33Tap, true);
				return false;
			}
			case 16: PushWindow(_r33Tap, false); return false;
			case 20:
			{
				var b = _m.HudRef.Command.TestUiSizeButton!;
				Check(UiScale.Size == UiSize.Large && b.Text == "UI-Größe: groß" && _m.HudRef.Screens.Current == _m.HudRef.Command,
					$"E2: echter Tipp auf „UI-Größe“ schaltet auf groß, Beschriftung neu, Menü bleibt offen ({UiScale.Size}, „{b.Text}“, Bildschirm {_m.HudRef.Screens.Current?.Id})");
				Check(_m.HudRef.ToastRect is { } tr && tr.Size.X > 0f && _m.HudRef.TestToastText.StartsWith("UI-Größe groß"),
					$"E2: Hinweis über dem Menü sichtbar („{_m.HudRef.TestToastText}“)");
				_m.TestCloseCommand();
				_r33Step = 0;
				return false;
			}
		}
		// Faktoren 1,3 und 1,15 (je ein Block ab Bild 30 und 70), danach Safe Area
		if (f is 30 or 70)
		{
			float factor = f == 30 ? 1.3f : 1.15f;
			UiScale.TestPpi = 396f;
			UiScale.Set(f == 30 ? UiSize.Large : UiSize.Normal);
			UiScale.TestForce = factor;
			UiScale.Apply();
			_m.HudRef.Alert("Prüfmeldung (Runde 33): Überfall angekündigt – andocken oder Sektor verlassen.", 20f);
			return false;
		}
		if (f is 40 or 80)
		{
			float factor = f == 40 ? 1.3f : 1.15f;
			Check(fl.Scale.X <= factor + 0.001f && fl.Scale.X >= factor - 0.05f && root.ContentScaleFactor == 1f && fl.Layout is { Compact: true },
				$"E2: Flug-Schicht {fl.Scale.X} (soll {factor}), Wurzel {root.ContentScaleFactor}, kompakt {fl.Layout?.Compact}");
			CheckFlightRects($"Faktor {factor}", new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size));
			var al = _m.HudRef.TestAlertRect;
			var hit = FlightButtons().Where(x => x.R.Intersects(al)).Select(x => x.Name).ToList();
			Check(_m.HudRef.TestAlert.Length > 0 && hit.Count == 0 && new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size).Grow(0.5f).Encloses(al),
				$"E2: Meldung bei {factor} {al} auf keiner Taste ({string.Join(", ", hit)})");
			_m.HudRef.HideAlert();
			return false;
		}
		if (f == 100)
		{
			// Safe Area: Ränder in Bildpunkten bei Faktor 1 (Aussparung links, Gestenleiste unten)
			FlightHud_TestInsets(new UiInsets(44, 14, 30, 24));
			return false;
		}
		if (f == 104)
		{
			var vis = GetViewport().GetVisibleRect().Size;
			var usable = new Rect2(new Vector2(44, 14), vis - new Vector2(44 + 30, 14 + 24));
			CheckFlightRects("Safe Area", usable);
			FlightHud_TestInsets(null);
			// Runde 33b: Kriegstest (Bollwerk, fünf Schächte) bei Faktor 1,3 — echte Tipps auf skalierte Tasten
			_m.TestMenu("new:war");
			return false;
		}
		if (f == 150)
		{
			UiScale.TestForce = 1.3f; UiScale.Apply();
			return false;
		}
		if (f == 156)
		{
			int bays = _m.TestBayPlaces.Count();
			// HUD-Neu: Gruppen statt Einzelschächte — der Faktor sinkt nur, wenn nicht einmal die aufgeklappte Leiste Platz hätte
			Check(bays >= 5 && fl.Layout is { Overflow: 0 } && fl.Scale.X <= 1.3f + 0.001f && fl.Scale.X >= 1f && fl.WantedFactor == 1.3f,
				$"33b: {bays} Schächte bei Stufe 1,3 → Schicht {fl.Scale.X:0.00} (jede Taste eigener Platz; Überlauf {fl.Layout?.Overflow})");
			CheckFlightRects("33b fünf Schächte", new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size));
			_r33Tap = root.GetFinalTransform() * fl.TestTurnLeftRect.GetCenter();
			PushWindow(_r33Tap, true);
			return false;
		}
		if (f == 158) { Check(fl.TurnLeftHeld, "33b: echter Tipp auf ◀ bei Faktor 1,3 hält die Taste"); PushWindow(_r33Tap, false); return false; }
		if (f == 160)
		{
			_m.TestSetSlider(0f);
			_r33Tap = root.GetFinalTransform() * fl.TestSliderPoint(0.75f);
			PushWindow(_r33Tap, true);
			return false;
		}
		if (f == 162) { PushWindow(_r33Tap, false); return false; }
		if (f == 164)
		{
			Check(Mathf.Abs(fl.SliderValue - 0.75f) < 0.06f, $"33b: Tipp auf 75 % des skalierten Schubreglers ergibt {fl.SliderValue:0.00}");
			_m.TestSetSlider(0f);
			_r33Fire = _m.TestFight.AutoFire;
			_r33Tap = root.GetFinalTransform() * fl.TestAutoFireGlobal.GetCenter();
			PushWindow(_r33Tap, true);
			return false;
		}
		if (f == 166) { PushWindow(_r33Tap, false); return false; }
		if (f == 170)
		{
			Check(_m.TestFight.AutoFire != _r33Fire, $"33b: echter Tipp auf „Dauerfeuer“ bei Faktor 1,3 schaltet um ({_r33Fire} → {_m.TestFight.AutoFire})");
			// HUD-Neu (Leviathan, zehn Waffenschächte): Gruppen im Daumenbogen; aufgeklappt jeder Schacht einzeln (keine Sammeltaste mehr)
			var d = new Ui.Screens.FlightHudData { HasWeapons = true };
			d.Bays = Enumerable.Range(0, 10).Select(i => new Ui.Screens.BayView { Name = $"Laser {i + 1}", Place = "Turm", Detail = "", State = i % 2 == 0 ? SpaceSim.Core.Weapons.BayState.Ready : SpaceSim.Core.Weapons.BayState.Reloading }).ToList();
			d.Groups = new[] { "Laser", "Rakete", "Geschütz" }.Select(l => new Ui.Screens.GroupView { Label = l, Sub = "1/3 bereit", State = SpaceSim.Core.Weapons.BayState.Ready, Combat = true }).ToList();
			fl.Refresh(d);
			fl.TestExpanded = true;
			fl.Refresh(d);
			Check(fl.TestVisibleBays == 10 && fl.TestGroupLabels.Count == 3 && fl.Layout is { Overflow: 0 }, $"HUD-Neu: zehn Schächte aufgeklappt einzeln ({fl.TestVisibleBays}), drei Gruppen ({string.Join(",", fl.TestGroupLabels)}), Überlauf {fl.Layout?.Overflow}");
			CheckFlightRects("HUD-Neu Leviathan aufgeklappt", new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size));
			fl.TestExpanded = false;
			// Runde 34b (Prüfung B6): im Gefecht fällt ein Waffenmodul aus (der Schacht verschwindet) — Schicht und Zellen bleiben stehen
			float before = fl.Scale.X; int cells = fl.TestLayoutBays;
			var d2 = new Ui.Screens.FlightHudData { HasWeapons = true, InCombat = true, Bays = d.Bays.Take(2).ToList() };
			fl.Refresh(d2);
			Check(fl.Scale.X == before && fl.TestLayoutBays == cells, $"34b: im Gefecht springt die Schicht nicht, wenn Schächte wegfallen (Faktor {before:0.00} → {fl.Scale.X:0.00}, Zellen {cells} → {fl.TestLayoutBays})");
			UiScale.TestForce = null; UiScale.TestPpi = null;
			UiScale.Set(UiSize.Normal);
			return false;
		}
		if (f == 176)
		{
			Check(fl.Scale.X == UiScale.Factor && root.ContentScaleFactor == 1f, $"E2: zurück (Schicht {fl.Scale.X}, Faktor {UiScale.Factor})");
			return true;
		}
		return false;
	}

	Vector2 _r33Tap;
	bool _r33Fire;

	void FlightHud_TestInsets(UiInsets? ins)
	{
		Ui.Screens.FlightHud.TestInsets = ins;
		_m.HudRef.Flight.TestRelayout();
	}

	static ScrollContainer? FindScrollAncestor(Node n)
	{
		for (var p = n.GetParent(); p is not null; p = p.GetParent()) if (p is ScrollContainer s) return s;
		return null;
	}

	/// <summary>Sichtbare Tasten/Regler des Flug-HUD als Bildschirmrechtecke (enthält die Skalierung der Schicht).</summary>
	List<(string Name, Rect2 R)> FlightButtons() =>
		_m.HudRef.Flight.GetChildren().OfType<Control>().Where(c => c.IsVisibleInTree() && c is KitButton or KitSlider or KitVSlider && c.Size.X > 1f)
			.Select(c => (c is KitButton k ? (k.Text.Length > 0 ? k.Text : "Taste") : "Regler", c.GetGlobalRect())).ToList();

	void CheckFlightRects(string what, Rect2 area)
	{
		var list = FlightButtons();
		var outside = list.Where(x => !area.Grow(0.5f).Encloses(x.R)).Select(x => $"{x.Name} {x.R}").ToList();
		var over = new List<string>();
		for (int i = 0; i < list.Count; i++)
			for (int j = i + 1; j < list.Count; j++)
				if (list[i].R.Intersection(list[j].R) is { } x && x.Size.X * x.Size.Y > 1f) over.Add($"{list[i].Name} × {list[j].Name}");
		Check(outside.Count == 0 && over.Count == 0, $"E2 {what}: {list.Count} Tasten in {area}, außerhalb [{string.Join("; ", outside)}], Überlappung [{string.Join("; ", over)}]");
	}
}
