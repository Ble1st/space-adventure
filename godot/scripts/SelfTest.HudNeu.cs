using System;
using System.Linq;
using Godot;
using SpaceSim.Core.Ui;
using SpaceAdventure.Ui.Kit;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 115 (HUD-Neu, Entwurf A + Aufklappleiste aus B), alles über echte Tipps im Gefechtstest (Falke): FEUER antippen feuert, FEUER halten
/// schaltet Dauerfeuer, ein Gruppenknopf feuert seine Gruppe, ⚡ schaltet die Leistung reihum, „Einzeln“ klappt die Einzelschächte auf und ein Einzelschacht
/// ist antippbar; Leviathan: zehn Schächte einzeln; im Menü „Kommando“ und „Zeitraffer“; A15 bei „normal“: jede Flug-Taste ≥ 7 mm, nichts in der Aussparung.
/// </summary>
public partial class SelfTest
{
	Vector2 _hnAt;
	bool _hnFire;
	ulong _hnShots;
	int _hnWait;

	bool HudNeuStage(int f)
	{
		var hud = _m.HudRef;
		var fl = hud.Flight;
		var root = GetTree().Root;
		switch (f)
		{
			case 1:
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();
				hud.HideAlert();
				if (root.Size != new Vector2I(1280, 720)) root.Size = new Vector2I(1280, 720);
				UiScale.TestForce = null; UiScale.TestPpi = null; UiScale.Set(UiSize.Normal);
				_m.TestMenu("new:combat");
				return false;
			case 60:
			{
				hud.HideAlert();
				var raider = _m.Session.U.ShipsById().FirstOrDefault(SpaceSim.Core.Simulation.ThreatMap.IsRaider);
				if (raider is not null) _m.TestSetShipTarget(raider.Id);
				Check(fl.TestGroupLabels.Count >= 2 && fl.TestGroupLabels.Contains("Laser"), $"HUD-Neu: Falke zeigt Gruppen nach Waffentyp ({string.Join(", ", fl.TestGroupLabels)})");
				// FEUER halten (≥ 0,55 s) → Dauerfeuer um
				_hnFire = _m.TestFight.AutoFire;
				_hnAt = fl.TestFireRect.GetCenter();
				Tap(_hnAt, true);
				return false;
			}
			case 60 + 50:   // 50 Bilder ≈ 0,8 s
				Log($"HUD-Neu: FEUER gehalten {KitButton.AnyHeld}, Meldung {hud.AlertRect}, Punkt {_hnAt}");
				Tap(_hnAt, false); return false;
			case 116:
				Check(_m.TestFight.AutoFire != _hnFire, $"HUD-Neu: FEUER halten schaltet Dauerfeuer ({_hnFire} → {_m.TestFight.AutoFire})");
				if (_m.TestFight.AutoFire) _m.TestFight.AutoFire = false;
				// ⚡ antippen → nächste Stellung
				_hnFire = false;
				_hnAt = fl.TestPowerRect.GetCenter();
				_hnShots = (ulong)(int)fl.TestMode;
				Tap(_hnAt, true);
				return false;
			case 118: Tap(_hnAt, false); return false;
			case 126:
				Check((int)fl.TestMode != (int)_hnShots, $"HUD-Neu: ⚡ schaltet die Leistungsverteilung weiter ({(SpaceSim.Core.Model.PowerMode)(int)_hnShots} → {fl.TestMode})");
				// Gruppe „Laser“ antippen (feuert, wenn bereit; sonst Hinweis) — der Druck kommt an
				{
					int li = fl.TestGroupLabels.IndexOf("Laser");
					_hnAt = fl.TestGroupRect(Math.Max(0, li)).GetCenter();
				}
				Tap(_hnAt, true);
				return false;
			case 128: Log($"HUD-Neu: Gruppe gehalten {KitButton.AnyHeld}, Meldung {hud.AlertRect}, Punkt {_hnAt}, Gruppen {string.Join(" ", Enumerable.Range(0, fl.TestGroupLabels.Count).Select(fl.TestGroupRect))}"); Tap(_hnAt, false); return false;
			case 136:
			{
				// wie früher die Einzelschächte: ohne bereite Waffe ist die Gruppe gesperrt und nennt den Grund im Untertext; sonst feuert der Tipp oder nennt ihn
				int li = Math.Max(0, fl.TestGroupLabels.IndexOf("Laser"));
				bool locked = fl.TestGroupDisabled(li) && fl.TestGroupSub(li).Length > 0;
				Check(_m.LastGroupFired == "Laser" || _m.TestLastHint.Contains("Laser") || locked,
					$"HUD-Neu: Tipp auf die Gruppe „Laser“ feuert sie, nennt den Grund oder ist mit Grund gesperrt (gefeuert „{_m.LastGroupFired}“, Hinweis „{_m.TestLastHint}“, gesperrt {fl.TestGroupDisabled(li)} „{fl.TestGroupSub(li)}“)");
			}
				Check(!fl.TestExpandVisible, "HUD-Neu: Falke ohne „Einzeln“ (jede Gruppe hat nur einen Schacht — der Bogen zeigt schon jeden)");
				// Bollwerk: zwei Raketenwerfer in einer Gruppe → „Einzeln“ klappt die Schächte auf
				_m.TestUseModel("bastion");
				return false;
			case 142:
				Check(fl.TestExpandVisible, $"HUD-Neu: Bollwerk zeigt „Einzeln“ (Gruppen {string.Join(", ", fl.TestGroupLabels)})");
				_hnAt = fl.TestCombatLayout(0).First(x => x.Name == "Einzeln").Rect.GetCenter();
				_hnAt = fl.GetGlobalTransform() * _hnAt;
				Tap(_hnAt, true);
				return false;
			case 144: Tap(_hnAt, false); return false;
			case 152:
				Check(fl.TestExpanded && fl.TestVisibleBays >= 2, $"HUD-Neu: „Einzeln“ klappt die Schächte auf ({fl.TestVisibleBays})");
				CheckButtons("HUD-Neu aufgeklappt (Bollwerk)", fl, GetViewport().GetVisibleRect());
				fl.TestExpanded = false;
				// Leviathan: zehn Waffen, aufgeklappt jeder Schacht einzeln
				{
					var lev = _m.Session.U.ShipModels.Values.FirstOrDefault(m => m.ClassKey == "battlecruiser");
					if (lev is not null) _m.TestUseModel(lev.Key);
				}
				return false;
			case 164:
				fl.TestExpanded = true;
				return false;
			case 174:
			{
				int bays = _m.TestBayPlaces.Count();
				Check(bays >= 9 && fl.TestVisibleBays == bays && fl.Layout is { Overflow: 0 }, $"HUD-Neu: Leviathan — alle {bays} Schächte einzeln aufgeklappt ({fl.TestVisibleBays}), Überlauf {fl.Layout?.Overflow}, Gruppen {string.Join(", ", fl.TestGroupLabels)}");
				CheckButtons("HUD-Neu Leviathan aufgeklappt", fl, GetViewport().GetVisibleRect());
				fl.TestExpanded = false;
				// A15 bei „normal“ mit Aussparung: Tasten ≥ 7 mm, nichts in der Aussparung
				root.Size = new Vector2I(1560, 720);
				UiScale.TestInsets = new UiInsets(53, 0, 0, 0);
				UiScale.TestPpi = 396f; UiScale.Set(UiSize.Normal); UiScale.Apply();
				return false;
			}
			case 190:
			{
				var vis = GetViewport().GetVisibleRect();
				float factor = fl.Scale.X;
				var small = Buttons(fl).Where(b => b.IsVisibleInTree()).Select(b => (b, mm: UiLayout.Millimetres(MathF.Min(b.GetGlobalRect().Size.X, b.GetGlobalRect().Size.Y), 2340, 1080, 1f, 396f))).Where(x => x.mm < 7f - 0.05f).Select(x => $"{x.b.Text} {x.mm:0.0} mm").ToList();
				Check(small.Count == 0 && factor >= 1.149f, $"HUD-Neu: A15 „normal“ (Faktor {factor:0.00}) — jede Flug-Taste ≥ 7 mm [{string.Join("; ", small)}]");
				var inCut = Buttons(fl).Where(b => b.IsVisibleInTree() && b.GetGlobalRect().Position.X < 53f * vis.Size.X / 1560f - 0.5f).Select(b => b.Text).ToList();
				Check(inCut.Count == 0, $"HUD-Neu: keine Taste in der Kamera-Aussparung [{string.Join(", ", inCut)}]");
				UiScale.TestInsets = null; UiScale.TestPpi = null; UiScale.Set(UiSize.Normal);
				root.Size = new Vector2I(1280, 720);
				// Menü: Kommando und Zeitraffer
				_m.TestOpenCommand();
				return false;
			}
			case 200:
			{
				var b = Buttons(hud.Command).FirstOrDefault(x => x.Text.StartsWith("Zeitraffer", StringComparison.Ordinal));
				Check(b is not null && Buttons(hud.Command).Any(x => x.Text == "Kommando"), "HUD-Neu: „Kommando“ und „Zeitraffer“ stehen im Menü");
				_hnFire = _m.TestFast;
				_m.TestCommand("fast");
				return false;
			}
			case 206:
				Check(_m.TestFast != _hnFire, $"HUD-Neu: „Zeitraffer“ im Menü schaltet ({_hnFire} → {_m.TestFast})");
				_m.TestCommand("fast");
				_m.TestCommand("command");
				return false;
			case 216:
				Check(hud.Screens.Current is Ui.Screens.CommandScreen, $"HUD-Neu: „Kommando“ im Menü öffnet die Zentrale ({hud.Screens.Current?.Id})");
				_m.TestCloseCommand();
				return false;
			case 222:
				return true;
		}
		return false;
	}
}
