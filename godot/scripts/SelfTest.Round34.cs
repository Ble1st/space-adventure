using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Ui;
using SpaceAdventure.Ui.Kit;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 110 (Runde 34, UI-ANPASSUNG.md Etappe E3, Handytest r33): (1) Dock-Gerüst bei 1280 × 720 und Faktor 1 wie bisher; (2) Handel an einer Farm:
/// Energie gekauft (Rückkaufdeckel) → Zeilenhinweis passt gemessen in die Zeile, Editorzeile 3 ohne doppeltes „hier:“; (3) letzte Zeile offen, Liste unten,
/// dann die erste Zeile per echtem Tipp öffnen — sie liegt samt Editor ganz in der Liste (Handytest: oben abgeschnitten); (4) Hinweis über dem offenen Menü
/// ganz im Bild; (5) Faktor 1,3: Dock-Schicht mit dem gefitteten Faktor, alle Tasten des Bereichs im Bild und ohne Überlappung, echter Tipp auf „Kacheln“;
/// (6) zurück.
/// </summary>
public partial class SelfTest
{
	Vector2 _r34At;
	Rect2 _r34Head;
	int _r34Try, _r34Wait, _r34Wait2;

	bool Round34Stage(int f)
	{
		var u = _m.Session.U;
		var trade = _m.HudRef.DockTrade;
		var root = GetTree().Root;
		switch (f)
		{
			case 1:
			{
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();
				if (root.Size.X < 640 || root.Size.X == root.Size.Y) root.Size = new Vector2I(1280, 720);
				UiScale.TestForce = null; UiScale.TestPpi = null; UiScale.Set(UiSize.Normal);
				_m.TestMenu("new:trader");   // frische Welt: nicht gesucht, Laderaum leer (die Stufen davor hinterlassen beides)
				return false;
			}
			case 5:
			{
				_m.Session.RunTicks(2);   // neue Welt: Preise erst nach dem ersten Takt
				var farm = u.StationsById().First(s => s.TypeKey == "farm" && s.Market.ContainsKey(SpaceSim.Core.Demo.DemoUniverse.Energy));
				var me = u.Ships[u.PlayerShip];
				u.WalletOf(me).Credits = 100000f;
				me.Cargo.Clear();
				farm.Market[SpaceSim.Core.Demo.DemoUniverse.Energy].Amount = MathF.Max(farm.Market[SpaceSim.Core.Demo.DemoUniverse.Energy].Amount, 150f);
				_m.TestDockAt(farm.Id);
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
				return false;
			}
			case 10: _m.TestOpenDockArea(Ui.Screens.DockAreas.Trade); return false;
			case 20:
			{
				var vis = GetViewport().GetVisibleRect().Size;
				Check(_m.HudRef.DockFactor == 1f && trade.Shell is { Compact: false }, $"E3: Faktor 1 → klassisches Dock-Gerüst (Schicht {_m.HudRef.DockFactor}, kompakt {trade.Shell?.Compact})");
				if (Mathf.Abs(vis.X - 1280f) < 1f && Mathf.Abs(vis.Y - 720f) < 1f)
					Check(trade.Shell is { } sh && sh.Center == new UiRect(224, 84, 690, 540) && sh.Ship == new UiRect(924, 84, 342, 540),
						$"E3: Handel bei 1280 × 720 wie bisher (Mitte {trade.Shell?.Center}, Schiff {trade.Shell?.Ship})");
				var res = trade.TestExecute($"buy:{SpaceSim.Core.Demo.DemoUniverse.Energy.Value}", 50f);
				Check(res.Success, $"E3: 50 Energie an der Farm gekauft ({res.Message})");
				_m.Session.RunTicks(2);   // der Markt rechnet den Preis neu — erst dann liegt der Deckel unter dem Marktpreis
				trade.TestRebuild();
				return false;
			}
			case 30:
			{
				trade.TestExpand(SpaceSim.Core.Demo.DemoUniverse.Energy);
				return false;
			}
			case 40:
			{
				var cap = SpaceSim.Core.Trading.PlayerLedger.SaleNoteShort(u, u.Ships[u.PlayerShip], _m.HudRef.DockCtx.Station, SpaceSim.Core.Demo.DemoUniverse.Energy);
				Check(cap is not null, $"E3: Kauf hier öffnet den Rückkaufdeckel ({cap ?? "kein Deckel"}; Meldung „{trade.TestMessageText}“, an Bord {u.Ships[u.PlayerShip].Cargo.GetValueOrDefault(SpaceSim.Core.Demo.DemoUniverse.Energy):0}, Preis {_m.HudRef.DockCtx.Station.Market[SpaceSim.Core.Demo.DemoUniverse.Energy].Price:0.0}, Posten {u.Ledger.Count})");
				var rn = Ui.Screens.TradeScreen.TestRowNotes.GetValueOrDefault(SpaceSim.Core.Demo.DemoUniverse.Energy.Value);
				Check(rn.Fits && rn.Text is { Length: > 0 } && rn.Text.Contains("Deckel"),
					$"E3/Handytest j2: Deckel-Hinweis der Energiezeile passt gemessen in die Zeile („{rn.Text}“)");
				var (_, _, l3) = trade.TestEditorLines;
				Check(!l3.Contains("hier: Wegen") && !l3.Contains("hier: hier"), $"E3/Handytest: Editorzeile 3 ohne doppeltes „hier“ („{l3}“)");
				// letzte Zeile öffnen (Liste rollt nach unten)
				trade.TestExpand(trade.TestLastGood);
				return false;
			}
			case 60:
			{
				// so weit hochrollen, dass nur der untere Teil der ersten Zeile sichtbar ist (wie am Gerät angetippt)
				if (trade.TestScroll is { } sc)
				{
					var head = trade.TestRowHead(trade.TestGoods[0]);
					var area = sc.GetGlobalRect();
					sc.ScrollTo((int)MathF.Max(0f, sc.ScrollVertical - (area.Position.Y - head.Position.Y) + 30f));
				}
				return false;
			}
			case 66:
			{
				var head = trade.TestRowHead(trade.TestGoods[0]);
				var area = trade.TestScroll?.GetGlobalRect() ?? new Rect2();
				// Runde 34b: erst tippen, wenn das Layout steht (Kopflage zwei Bilder gleich) — sonst trifft der Tipp eine noch nicht einsortierte Zeile
				if (head != _r34Head && _r34Wait++ < 30) { _r34Head = head; _stageStart += 1; return false; }
				var visible = head.Intersection(area);
				if (visible.Size.Y < 8f) { if (++_r34Try < 5) { trade.TestScroll?.ScrollTo(0); _stageStart += 6; } return false; }
				_r34At = visible.GetCenter();
				Tap(_r34At, true);
				return false;
			}
			case 68: Tap(_r34At, false); return false;
			case 110:
			{
				Check(trade.TestExpanded is { } ex && ex == trade.TestGoods[0], "E3: Tipp öffnet die erste Zeile");
				Check(trade.TestOpenRowVisible(out string why), $"E3/Handytest i2: geöffnete Zeile samt Editor ganz sichtbar ({why})");
				// Hinweis über dem offenen Menü (angedockt „≡ Menü“)
				_m.TestOpenCommand();
				_m.HudRef.Toast(UiScale.ShortText() + " — ein Hinweis, der umbricht, damit seine Höhe zählt (Handytest c2/c4)", 6f);
				return false;
			}
			case 116:
			{
				var vis = GetViewport().GetVisibleRect();
				var t = _m.HudRef.TestToastRect;
				Check(vis.Grow(0.5f).Encloses(t) && _m.HudRef.TestToastText.Length > 0, $"E3/Handytest c2: Hinweis über dem Menü ganz im Bild ({t} in {vis})");
				_m.TestCloseCommand();
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
				UiScale.TestPpi = 396f; UiScale.TestForce = 1.3f; UiScale.Apply();
				return false;
			}
			case 130:
			{
				var vis = GetViewport().GetVisibleRect();
				float want = DockLayout.FitFactor(vis.Size.X, vis.Size.Y, new UiInsets(0, 0, 0, 0), 1.3f);
				Check(Mathf.Abs(_m.HudRef.DockFactor - want) < 0.005f && want > 1f && trade.Shell is { Compact: true } sh && sh.InnerW >= DockLayout.MinInnerShipW - 0.5f,
					$"E3: Dock-Schicht bei Stufe 1,3 → {_m.HudRef.DockFactor:0.00} (soll {want:0.00}), kompakt {trade.Shell?.Compact}, Innenbreite {trade.Shell?.InnerW:0}");
				CheckButtons("E3 Handel 1,3", trade, vis);
				return false;
			}
			case 131:
			{
				// Runde 34b: erst tippen, wenn die Taste nach dem Umskalieren still liegt (zwei Bilder gleiche Lage)
				var kr = Buttons(trade).First(b => b.Text == "Kacheln" && b.IsVisibleInTree()).GetGlobalRect();
				if (kr != _r34Head && _r34Wait2++ < 30) { _r34Head = kr; _stageStart += 1; return false; }
				_r34At = kr.GetCenter();
				Tap(_r34At, true);
				return false;
			}
			case 133: Tap(_r34At, false); return false;
			case 140:
			{
				Check(Buttons(trade).Any(b => b.Text == "Kacheln" && b.State == KitButtonState.On), "E3: echter Tipp auf „Kacheln“ bei skaliertem Dock");
				trade.TestSetTiles(false);
				return false;
			}
			case 144:
			{
				// Runde 34b (Prüfung B4, Handytest r34: 400 px Finger → 470 px Liste): echter Wisch in der skalierten Dock-Schicht — der Inhalt folgt dem Finger 1:1
				if (trade.TestScroll is not { } sc) { Check(false, "34b: Handelsliste fehlt"); return false; }
				sc.ScrollTo(0);
				_r34At = sc.GetGlobalRect().GetCenter() + new Vector2(0, 60);
				PushMouse(_r34At, true, false);
				return false;
			}
			case 146: PushMouse(_r34At - new Vector2(0, 40), true, true); return false;
			case 148: PushMouse(_r34At - new Vector2(0, 160), true, true); return false;
			case 150:
			{
				PushMouse(_r34At - new Vector2(0, 160), false, false);
				float k = _m.HudRef.DockFactor;
				int got = trade.TestScroll?.ScrollVertical ?? -1;
				Check(Mathf.Abs(got - 160f / k) <= 3f, $"34b: Wisch 160 px bei Dock-Schicht {k:0.00} rollt {got} (soll {160f / k:0}) — der Inhalt folgt dem Finger 1:1");
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Info);
				return false;
			}
			case 160:
			{
				CheckButtons("E3 Stationsinfo 1,3", _m.HudRef.DockInfo, GetViewport().GetVisibleRect());
				_m.HudRef.DockMenu.Visible = false;
				UiScale.TestForce = null; UiScale.TestPpi = null; UiScale.Set(UiSize.Normal);
				_m.TestUndock();
				return false;
			}
			case 170:
				Check(_m.HudRef.DockFactor == UiScale.DockFactor, $"E3: zurück (Dock {_m.HudRef.DockFactor})");
				return true;
		}
		return false;
	}

	void PushMouse(Vector2 at, bool down, bool motion)
	{
		if (Ui.Pointer.TouchMode)
		{
			if (motion) GetViewport().PushInput(new InputEventScreenDrag { Index = 0, Position = at }, true);
			else GetViewport().PushInput(new InputEventScreenTouch { Index = 0, Position = at, Pressed = down }, true);
		}
		else if (motion) GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left }, true);
		else GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = at, GlobalPosition = at, Pressed = down, ButtonMask = down ? MouseButtonMask.Left : 0 }, true);
	}

	/// <summary>Alle sichtbaren Tasten eines Bildschirms (sichtbarer Teil, beschnitten durch rollende Vorfahren): in der Fläche, keine Überlappung.</summary>
	void CheckButtons(string what, Control screen, Rect2 area)
	{
		static Rect2 Clip(Control c)
		{
			var r = c.GetGlobalRect();
			for (var n = c.GetParent(); n is not null; n = n.GetParent())
				if (n is Control pc && pc.ClipContents) r = r.Intersection(pc.GetGlobalRect());
			return r;
		}
		var list = Buttons(screen).Where(b => b.IsVisibleInTree()).Select(b => (b.Text, R: Clip(b))).Where(x => x.R.Size.X > 1f && x.R.Size.Y > 1f).ToList();
		var outside = list.Where(x => !area.Grow(0.5f).Encloses(x.R)).Select(x => $"{x.Text} {x.R}").ToList();
		var over = new List<string>();
		for (int i = 0; i < list.Count; i++)
			for (int j = i + 1; j < list.Count; j++)
				if (list[i].R.Intersection(list[j].R) is { } x && x.Size.X * x.Size.Y > 25f) over.Add($"{list[i].Text} × {list[j].Text}");
		Check(list.Count > 0 && outside.Count == 0 && over.Count == 0, $"{what}: {list.Count} Tasten, außerhalb [{string.Join("; ", outside)}], Überlappung [{string.Join("; ", over)}]");
	}
}
