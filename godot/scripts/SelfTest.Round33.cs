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
					Check(fl.TestSliderRect == new Rect2(660, 646, 600, 68) && fl.TestReverseRect == new Rect2(470, 590, 80, 56),
						$"E2: 1280 × 720 unverändert (Regler {fl.TestSliderRect}, RÜCK {fl.TestReverseRect})");
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
			Check(Mathf.Abs(fl.Scale.X - factor) < 0.001f && root.ContentScaleFactor == 1f && fl.Layout is { Compact: true },
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
			// Safe Area: Ränder in Einheiten der Schicht (Aussparung links, Gestenleiste unten)
			FlightHud_TestInsets(new UiInsets(44, 14, 30, 24));
			return false;
		}
		if (f == 104)
		{
			var vis = GetViewport().GetVisibleRect().Size;
			float k = fl.Scale.X;
			var usable = new Rect2(new Vector2(44, 14) * k, vis - new Vector2(44 + 30, 14 + 24) * k);
			CheckFlightRects("Safe Area", usable);
			FlightHud_TestInsets(null);
			UiScale.TestForce = null; UiScale.TestPpi = null;
			UiScale.Set(UiSize.Normal);
			return false;
		}
		if (f == 108)
		{
			Check(fl.Scale.X == UiScale.Factor && root.ContentScaleFactor == 1f, $"E2: zurück (Schicht {fl.Scale.X}, Faktor {UiScale.Factor})");
			return true;
		}
		return false;
	}

	Vector2 _r33Tap;

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
		_m.HudRef.Flight.GetChildren().OfType<Control>().Where(c => c.IsVisibleInTree() && c is KitButton or KitSlider && c.Size.X > 1f)
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
