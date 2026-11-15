using System;
using System.Linq;
using Godot;
using SpaceSim.Core.Ui;
using SpaceAdventure.Ui.Kit;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 112 (Runde 37, Handytest r35 und UI-ANPASSUNG.md E5), alles bei Stufe 1,3 auf 1280 × 720:
/// (1) ein Wisch über eine Zeile der Fraktionsliste wählt nichts (vorher wählte das Aufsetzen die Zeile); (2) ein echter Tipp öffnet schmal das Detail mit
/// „← Liste“; (3) die Leiste der Kommandozentrale bleibt nach Rollen und Tipp auf „Comms“ gerollt, der aktive Eintrag sichtbar; (4) im Handel liegt die
/// Tastenreihe des offenen Editors ganz in der Zeile; (5) Hamburger-Menü in der Menü-Schicht: Fenster in der Fläche, Systemzeilen voll belegt;
/// (6) Sektorkarte: jede Station beschriftet.
/// </summary>
public partial class SelfTest
{
	Vector2 _r37At;
	int _r37Wait;
	Rect2 _r37Rect;
	int _r37Rail;

	bool Round37Stage(int f)
	{
		var root = GetTree().Root;
		var hud = _m.HudRef;
		switch (f)
		{
			case 1:
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();
				if (root.Size.X < 640 || root.Size.X == root.Size.Y) root.Size = new Vector2I(1280, 720);
				UiScale.TestPpi = 396f; UiScale.TestForce = 1.3f; UiScale.Apply();
				hud.CcFactions.ResetSession();
				// Runde 37b (Prüfauflage A1: die Liste war in der Testgeometrie nicht rollbar, gewischt wurde die Leiste): genug Akteure, dass die Fraktionsliste rollt
				{
					var u = _m.Session.U;
					foreach (var sh in u.ShipsById().Where(x => x.Owner == u.FreeActor && x.IsEconomyAi).Take(10).ToList()) SpaceSim.Core.Simulation.ActorRules.Promote(u, sh);
				}
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Factions);
				return false;
			case 20:
			{
				// (1) Wisch beginnt auf der zweiten Zeile
				var rows = hud.CcFactions.TestRows;
				// die Fraktionsliste selbst (Schlüssel aus FactionsScreen.cs), nicht die Leiste
				var list = FactionList(hud);
				float room = list is null ? 0f : (float)(list.GetVScrollBar().MaxValue - list.GetVScrollBar().Page);
				Check(!hud.CcFactions.TestDetailOpen && list is not null && room > 100f, $"37b: schmal zeigt „Fraktionen“ die Liste über die ganze Höhe, rollbar ({room:0} px, Detail offen {hud.CcFactions.TestDetailOpen})");
				if (list is null) return false;
				list.ScrollTo(0);
				// Aufsetzen mitten auf der zweiten Zeile (kein Leerraum, keine Taste)
				var row = list.GetChild(0).GetChildren().OfType<PanelContainer>().Skip(1).FirstOrDefault();
				_r37At = row?.GetGlobalRect().GetCenter() ?? list.GetGlobalRect().GetCenter();
				_r37Scroll0 = list.ScrollVertical;
				PushMouse(_r37At, true, false);
				return false;
			}
			case 22: PushMouse(_r37At - new Vector2(0, 30), true, true); return false;
			case 24: PushMouse(_r37At - new Vector2(0, 120), true, true); return false;
			case 26: PushMouse(_r37At - new Vector2(0, 120), false, false); return false;
			case 34:
				Check(FactionList(hud) is { } fl && fl.ScrollVertical > _r37Scroll0 + 40, $"37b: der Wisch rollte die Fraktionsliste ({FactionList(hud)?.ScrollVertical} statt {_r37Scroll0})");
				Check(hud.CcFactions.Selected is null && !hud.CcFactions.TestDetailOpen, $"37: Wisch über die Fraktionsliste wählt nichts (gewählt {hud.CcFactions.Selected?.Value}, Detail {hud.CcFactions.TestDetailOpen})");
				_r37Wait = 0; _r37Rect = default;
				return false;
			case 35:
			{
				// (2) echter Tipp auf die erste sichtbare Zeile
				var list = FactionList(hud);
				if (list is null) { Check(false, "37: Fraktionsliste fehlt"); return false; }
				var row = list.GetChild(0).GetChildren().OfType<PanelContainer>().FirstOrDefault(x => list.GetGlobalRect().Encloses(x.GetGlobalRect()));
				var r = row?.GetGlobalRect() ?? list.GetGlobalRect();
				if (r != _r37Rect && _r37Wait++ < 30) { _r37Rect = r; _stageStart += 1; return false; }
				_r37At = r.GetCenter();
				Tap(_r37At, true);
				return false;
			}
			case 37: Tap(_r37At, false); return false;
			case 47:
				Check(hud.CcFactions.TestDetailOpen && hud.CcFactions.Selected is not null && Buttons(hud.CcFactions).Any(b => b.Text == "← Liste" && b.IsVisibleInTree()),
					$"37: Tipp auf eine Zeile öffnet schmal das Detail mit „← Liste“ (Detail {hud.CcFactions.TestDetailOpen}, gewählt {hud.CcFactions.Selected?.Value})");
				CheckButtons("37 Fraktionsdetail 1,3", hud.CcFactions, GetViewport().GetVisibleRect());
				hud.CcFactions.TestDetailOpen = false;
				return false;
			case 50:
			{
				// (3) Leiste ganz nach unten rollen, dann „Comms“ antippen
				var rail = RailScroll(hud.CcFactions);
				if (rail is null) { Check(false, "37: Leiste der Zentrale ohne Rollbereich"); return false; }
				_r37Rail = (int)(rail.GetVScrollBar().MaxValue - rail.GetVScrollBar().Page);
				Check(_r37Rail > 10, $"37: Leiste rollt bei 1,3 ({_r37Rail})");
				rail.ScrollVertical = _r37Rail;
				_r37Wait = 0; _r37Rect = default;
				return false;
			}
			case 54:
			{
				var b = Buttons(hud.CcFactions).FirstOrDefault(x => x.Text == "Comms" && x.IsVisibleInTree());
				if (b is null) { Check(false, "37: Leisteneintrag „Comms“ fehlt"); return false; }
				var br = b.GetGlobalRect();
				if (br != _r37Rect && _r37Wait++ < 30) { _r37Rect = br; _stageStart += 1; return false; }
				_r37At = br.GetCenter();
				Tap(_r37At, true);
				return false;
			}
			case 56: Tap(_r37At, false); return false;
			case 70:
			{
				var rail = RailScroll(hud.CcComms);
				var b = Buttons(hud.CcComms).FirstOrDefault(x => x.Text == "Comms" && x.IsVisibleInTree());
				bool visible = rail is not null && b is not null && rail.GetGlobalRect().Grow(1f).Encloses(b.GetGlobalRect());
				Check(hud.Screens.Current == hud.CcComms && rail is not null && rail.ScrollVertical > 10 && visible,
					$"37: nach Rollen und Tipp bleibt die Leiste gerollt ({rail?.ScrollVertical} von {_r37Rail}), „Comms“ sichtbar ({visible})");
				_m.TestCloseCommand();
				var yard = _m.Session.U.StationsById().FirstOrDefault(x => x.Name == "Werft Delta");
				if (yard is not null) { _m.TestDockAt(yard.Id); _m.TestOpenDockArea(Ui.Screens.DockAreas.Trade); }
				return false;
			}
			case 84:
			{
				// (4) offene Zeile mit Ware an Bord: Tastenreihe ganz in der Zeile
				var u = _m.Session.U;
				var g = hud.DockTrade.TestGoods.FirstOrDefault();
				var ship = u.Ships[u.PlayerShip];
				if (ship.FreeUnits(u, g) >= 2f) ship.Cargo[g] = (ship.Cargo.TryGetValue(g, out var h) ? h : 0f) + 2f;
				hud.DockTrade.TestOpenRow(g);
				return false;
			}
			case 100:
			{
				var sell = Buttons(hud.DockTrade).FirstOrDefault(b => b.Text == "Verkaufen" && b.IsVisibleInTree());
				var buy = Buttons(hud.DockTrade).FirstOrDefault(b => (b.Text == "Kaufen" || b.Text.EndsWith(" kaufen")) && b.IsVisibleInTree());
				var editor = sell?.GetParent() as Control;
				var row = editor?.GetParent() as Control;
				bool inside = sell is not null && buy is not null && row is not null && row.GetGlobalRect().Grow(0.5f).Encloses(sell.GetGlobalRect()) && row.GetGlobalRect().Grow(0.5f).Encloses(buy.GetGlobalRect());
				Check(inside && sell!.Overflow().Length == 0 && buy!.Overflow().Length == 0,
					$"37: Handels-Editor bei 1,3 — Kaufen {buy?.GetGlobalRect()}, Verkaufen {sell?.GetGlobalRect()} in der Zeile {row?.GetGlobalRect()} (Überlauf „{sell?.Overflow()}“/„{buy?.Overflow()}“)");
				_m.TestUndock();
				_m.TestOpenCommand();
				return false;
			}
			case 112:
			{
				// (5) Hamburger-Menü: Menü-Schicht mit Faktor, Fenster in der Fläche, Systemzeilen voll
				var vis = GetViewport().GetVisibleRect();
				var win = hud.Command.WindowGlobal;
				var rowsN = hud.Command.TestSystemRows;
				Check(Mathf.Abs(hud.MenuFactor - UiScale.Factor) < 0.005f && UiScale.Factor > 1.2f, $"E5: Menü-Schicht mit dem UI-Faktor ({hud.MenuFactor:0.00}, Stufe {UiScale.Factor:0.00})");
				Check(win is { } w && vis.Grow(0.5f).Encloses(w), $"E5: Menüfenster in der Fläche ({win} in {vis})");
				Check(rowsN.Count > 0 && rowsN.Take(rowsN.Count - 1).All(n => n == 2), $"E5: Systemzeilen zu zweit, nur die letzte darf allein stehen ({string.Join(",", rowsN)})");
				CheckButtons("E5 Menü 1,3", hud.Command, vis);
				_m.TestCloseCommand();
				hud.CcSector.ShowPlayerSector(_m.Session.U.PlayerSector);
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Sector);
				return false;
			}
			case 130:
				// (6) Sektorkarte: jede Station beschriftet (notfalls kleiner bzw. mit dem ersten Wort)
				Check(Ui.Screens.SectorScreen.TestUnlabeled == 0, $"37: Sektorkarte bei 1,3 — Stationen ohne Beschriftung {Ui.Screens.SectorScreen.TestUnlabeled}");
				_m.TestCloseCommand();
				UiScale.TestForce = null; UiScale.TestPpi = null; UiScale.Set(UiSize.Normal);
				return false;
			case 136:
				return true;
		}
		return false;
	}

	int _r37Scroll0;
	static KitScroll? FactionList(Ui.Hud hud) => UiCheck.Visible(hud.CcFactions).OfType<KitScroll>().FirstOrDefault(x => x.ScrollKey.StartsWith("FactionsScreen.cs", StringComparison.Ordinal) && x.GetChildCount() > 0 && x.GetChild(0) is VBoxContainer);

	/// <summary>Rollbereich der Bereichsleiste eines Zentralen-Bildschirms (die Leiste enthält „Universum“).</summary>
	static KitScroll? RailScroll(Control screen) =>
		UiCheck.Visible(screen).OfType<KitScroll>().FirstOrDefault(x => Buttons(x).Any(b => b.Text == "Universum" && b.AlignLeft));
}
