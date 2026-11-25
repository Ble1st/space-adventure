using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Game;
using SpaceSim.Core.Help;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 168 (Hilfe und Tutorial): Startbildschirm → „Tutorial“ steht bei den regulären Paketen, echter Tipp auf „Starten“; Tutorialzeile im
/// Flug; Ziel antippen und echter Tipp auf „Autopilot“ → Schritte „hinfliegen“ und „andocken“ erledigt; der Hinweis „Angedockt“ erscheint einmal;
/// echter Tipp auf „Handel“ → Hinweis „Handel“; Kauf über den Handelsdienst → Schritt „kaufen“; erneutes Andocken zeigt den Andock-Hinweis nicht wieder;
/// ☰ → echter Tipp auf „Hilfe“ öffnet den Kodex, er rollt, ein Thema öffnet per Tipp den Artikel, Zurück führt zur Liste, die Suche filtert.
/// </summary>
public partial class SelfTest
{
	int _hpPhase, _hpT0;
	StationId _hpStation;
	Vector2 _hpAt;

	bool HelpStage(int f)
	{
		var hud = _m.HudRef;
		switch (f)
		{
			case 1:
				GetTree().Root.Size = new Vector2I(1280, 720);
				HintSettings.Forget();
				HintSettings.SetEnabled(true);
				HintSettings.Reset();
				_m.TestHintsOn = true;
				_hpPhase = 0;
				_m.TestShowStart();
				return false;
			case 6:
			{
				var texts = hud.Start.TestChooserTexts();
				int tut = texts.FindIndex(t => t.StartsWith("Tutorial ·"));
				int tests = texts.FindIndex(t => t.Contains("Testszenarien"));
				Check(tut >= 0 && (tests < 0 || tut < tests), $"HP: „Tutorial“ bei den regulären Paketen (Karte {tut}, Testkopf {tests})");
				var b = hud.Start.TestStartButton("tutorial");
				if (b is null) { Fail("HP: keine Starttaste für das Tutorial"); return true; }
				ScrollIntoView(b);
				return false;
			}
			case 10:
			{
				var b = hud.Start.TestStartButton("tutorial");
				if (b is null) { Fail("HP: Starttaste verschwunden"); return true; }
				_hpAt = b.GetGlobalRect().GetCenter();
				Tap(_hpAt, true);
				return false;
			}
			case 12: Tap(_hpAt, false); return false;
			case 30:
			{
				var u = _m.Session.U;
				Check(!_m.TestInStartMenu, "HP: Tutorial gestartet (Startbildschirm zu)");
				Check(u.Tutorial is { Step: 0, Done: false }, $"HP: Tutorial läuft bei Schritt 1 ({u.Tutorial?.Step})");
				if (u.Tutorial is null) return true;
				_hpStation = u.Tutorial.Station;
				Check(u.Stations[_hpStation].TypeKey == "steelworks", "HP: Tutorialstation ist das Stahlwerk");
				Check(hud.Flight.TestTutorial.StartsWith("Tutorial 1/"), $"HP: Tutorialzeile im Flug ({hud.Flight.TestTutorial})");
				Check(hud.Flight.TestTutorialFits, $"HP: Tutorialzeile passt ohne Kürzung ({hud.Flight.TestTutorial})");
				Check(_m.LastTutorialMessage.StartsWith("Tutorial: Fliege zu"), $"HP: Ziel als Meldung ({_m.LastTutorialMessage})");
				_m.TestSelectStation(_hpStation);
				return false;
			}
			case 34: PressButton(hud.Flight, "Autopilot", true); return false;
			case 36: PressButton(hud.Flight, "Autopilot", false); _hpPhase = 1; _hpT0 = f; return false;
		}
		if (f < 40) return false;
		var w = _m.Session.U;
		switch (_hpPhase)
		{
			case 1:   // Autopilot fliegt hin und dockt an → Schritte 1 und 2
				if (_m.Docked && w.Tutorial!.Step >= 2)
				{
					Check(_m.LastTutorialMessage.Contains("Kaufe"), $"HP: nächstes Ziel „Kaufen“ angekündigt ({_m.LastTutorialMessage})");
					_hpPhase = 2; _hpT0 = f;
				}
				else if (f - _hpT0 > 3600) { Fail($"HP: Autopilot/Andocken nicht erreicht (Schritt {w.Tutorial?.Step}, angedockt {_m.Docked}, Autopilot {_m.TestAutopilotOn})"); return true; }
				return false;
			case 2:   // Andock-Hinweis erscheint (einmal)
				if (_m.LastHintKey == "first_dock")
				{
					Check(_m.LastHintText.StartsWith("Tipp: Angedockt"), $"HP: Andock-Hinweis als Toast ({_m.LastHintText})");
					Check(HintSettings.FileSeen().Contains("first_dock"), $"HP: „gesehen“ in den Einstellungen ({HintSettings.FileSeen()})");
					PressButton(hud.DockMenu, "Handel", true);
					_hpPhase = 3; _hpT0 = f;
				}
				else if (f - _hpT0 > 900) { Fail($"HP: Andock-Hinweis kam nicht (zuletzt {_m.LastHintKey})"); return true; }
				return false;
			case 3:
				if (f - _hpT0 == 2) PressButton(hud.DockMenu, "Handel", false);
				if (f - _hpT0 == 20)
				{
					Check(hud.Screens.Current == hud.DockTrade, $"HP: echter Tipp auf „Handel“ öffnet den Handel ({hud.Screens.Current?.Id})");
					// Kauf über den Handelsdienst (wie die Taste „Kaufen“)
					var me = w.Ships[w.PlayerShip];
					var st = w.Stations[_hpStation];
					var trade = new TradeService();
					var offer = trade.Offers(w, st, me).FirstOrDefault(o => o.Key.StartsWith("buy:") && o.Amount >= 1f);
					Check(offer.Key is not null, "HP: das Stahlwerk verkauft etwas");
					if (offer.Key is not null) Check(trade.Execute(w, st, me, new ServiceRequest(offer.Key, offer.Good, 1f)).Success, "HP: Kauf gebucht");
				}
				if (f - _hpT0 > 20 && w.Tutorial!.Step >= 3 && _m.LastHintKey == "first_trade")
				{
					Check(_m.LastTutorialMessage.Contains("Lege ab"), $"HP: nach dem Kauf „Ablegen“ als Ziel ({_m.LastTutorialMessage})");
					_m.TestUndock();
					_hpPhase = 4; _hpT0 = f;
				}
				else if (f - _hpT0 > 1200) { Fail($"HP: Kauf-Schritt oder Handels-Hinweis fehlt (Schritt {w.Tutorial?.Step}, Hinweis {_m.LastHintKey})"); return true; }
				return false;
			case 4:   // erneut andocken: der Andock-Hinweis kommt nicht wieder
				if (f - _hpT0 == 90)
				{
					Check(w.Tutorial!.Step >= 4, $"HP: Ablegen erkannt (Schritt {w.Tutorial.Step})");
					_m.TestDockAt(_hpStation);
				}
				if (f - _hpT0 == 90 + 720)
				{
					Check(_m.Docked, "HP: wieder angedockt");
					Check(_m.HintCounts.GetValueOrDefault("first_dock") == 1, $"HP: Andock-Hinweis nur einmal ({_m.HintCounts.GetValueOrDefault("first_dock")}×)");
					_m.TestUndock();
					_m.TestOpenCommand();
				}
				if (f - _hpT0 == 90 + 740)
				{
					var b = hud.Command.TestEntry("help");
					if (b is null) { Fail("HP: Menüzeile „Hilfe“ fehlt"); return true; }
					ScrollIntoView(b);
				}
				if (f - _hpT0 == 90 + 750) TapAt(hud.Command.TestEntry("help"), true, "„Hilfe“");
				if (f - _hpT0 == 90 + 752) { TapAt(hud.Command.TestEntry("help"), false, "„Hilfe“"); _hpPhase = 5; _hpT0 = f; }
				return false;
			case 5:
			{
				var h = hud.CcHelp;
				int t = f - _hpT0;
				if (t == 20)
				{
					Check(hud.Screens.Current == h, $"HP: echter Tipp auf „Hilfe“ öffnet den Kodex ({hud.Screens.Current?.Id})");
					Check(h.TestTopicCount == HelpData.Default.Topics.Count && h.TestTopicCount >= 10, $"HP: alle Themen gelistet ({h.TestTopicCount})");
					var sc = h.TestAreaScroll;
					Check(sc is not null && sc.GetVScrollBar().MaxValue > sc.Size.Y + 50, $"HP: Liste höher als die Fläche (rollt: {sc?.GetVScrollBar().MaxValue:0} > {sc?.Size.Y:0})");
					if (sc is not null) sc.ScrollVertical = 300;
				}
				if (t == 40)
				{
					var sc = h.TestAreaScroll;
					Check(sc is not null && sc.WantedScroll >= 250, $"HP: Kodex gerollt und Lage gehalten ({sc?.WantedScroll})");   // direkt nach einem Neuaufbau steht die Lage noch aus (KitScroll.WantedScroll)
					var b = h.TestTopic("hitze");
					if (b is null) { Fail("HP: Thema „Hitze“ fehlt"); return true; }
					if (sc is not null) sc.ScrollVertical = Mathf.Max(0, (int)(b.GlobalPosition.Y - sc.GlobalPosition.Y + sc.ScrollVertical - 60f));
				}
				if (t == 46)
				{
					var b = h.TestTopic("hitze");
					var sc = h.TestAreaScroll;
					Check(b is not null && sc is not null && sc.GetGlobalRect().HasPoint(b.GetGlobalRect().GetCenter()), $"HP: Thema „Hitze“ sichtbar ({b?.GetGlobalRect()} in {sc?.GetGlobalRect()})");
					TapAt(b, true, "Thema „Hitze“");
				}
				if (t == 48) TapAt(h.TestTopic("hitze"), false, "Thema „Hitze“");
				if (t == 66)
				{
					Check(h.TestArticle == "hitze" && h.TestParagraphs.Count >= 2, $"HP: Tipp öffnet den Artikel ({h.TestArticle}, {h.TestParagraphs.Count} Absätze)");
					string back = _m.HandleBack();
					Check(back == "hilfethema", $"HP: Zurück führt zur Themenliste ({back})");
				}
				if (t == 80)
				{
					Check(h.TestArticle is null && hud.Screens.Current == h, "HP: Liste wieder offen, Kodex bleibt");
					h.TestSetQuery("wurmloecher");
					Check(h.TestTopicCount >= 1 && h.TestTopic("wurmloecher") is not null, $"HP: Suche filtert ({h.TestTopicCount} Treffer)");
					h.TestSetQuery("");
				}
				if (t == 90)
				{
					Check(h.TestTopicCount == HelpData.Default.Topics.Count, "HP: leere Suche zeigt wieder alle");
					_m.TestCloseCommand();
					_m.TestHintsOn = false;
					return true;
				}
				return false;
			}
		}
		return false;
	}
}
