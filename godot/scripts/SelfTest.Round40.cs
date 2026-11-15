using System;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 116 (Runde 40): Comms-Bildschirm echt — (1) Posteingang zeigt echte Nachrichten aus Ereignissen
/// (Fahndung gemeldet → Nachricht, ungelesen-Zähler); (2) Reiter „Diplomatie“ listet die KI-Fraktionen; (3) „Strafe zahlen“
/// zahlt per Knopf (einfacher Tipp) mit +10 % in die Fraktionskasse und löscht die Fahndung; (4) „Krieg erklären“ führt erst
/// beim zweiten Tipp aus (KitConfirm) und erklärt den Krieg. Danach Frieden per Core zum Aufräumen.
/// </summary>
public partial class SelfTest
{
	Vector2 _r40At;
	float _r40Wallet, _r40Treasury;
	Ui.Kit.KitButton? _r40War;
	FactionId _r40Fac;

	Ui.Kit.KitButton? WarButton(Ui.Hud hud) =>
		Buttons(hud.CcComms).FirstOrDefault(b => (b.Text == "Krieg erklären" || b.Text == "Wirklich?") && b.IsVisibleInTree() && b.State != Ui.Kit.KitButtonState.Disabled);

	/// <summary>Tippt „Krieg erklären“ über den Test-Hook des Knopfes (Pressed) — Runde 41 (A3): mit Vorschauzeile unter jedem
	/// Knopf liegt „Krieg erklären“ unten im Detail-Scroll; der Mauspfad ist in anderen Stufen abgedeckt.</summary>
	void WarTap(Ui.Hud hud) => WarButton(hud)?.TestPress();

	bool Round40Stage(int f)
	{
		var hud = _m.HudRef;
		var u = _m.Session.U;
		switch (f)
		{
			case 1:
			{
				// Vorbereitung: erst Ticks laufen lassen (CommsSystem hängt sich beim ersten Schritt an den Bus), dann Fahndung
				// bei der ersten KI-Fraktion melden (→ Posteingang-Nachricht über den Ereignisbus; die Spielerfraktion führt keine
				// Fahndung — Runde 38) + Kasse
				_m.Session.RunTicks(30);
				_r40Fac = u.NpcFactionIds().First();
				Wanted.Report(u, u.PlayerActor, _r40Fac, "assault", 500f);
				u.Player.Treasury.Credits = 50000f;
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Comms);
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 3:
				Check(_m.TestScreenOpen("cc.comms"), "40: Comms-Bereich öffnet ohne Ausnahme");
				Check(hud.CcComms.TestInboxCount > 0, $"40: Posteingang enthält echte Nachrichten ({hud.CcComms.TestInboxCount}, ungelesen {hud.CcComms.TestUnread})");
				Check(hud.CcComms.TestTab == "inbox", "40: Reiter „Posteingang“ ist aktiv");
				PressButton(hud.CcComms, "Diplomatie", true);
				return false;
			case 5:
				PressButton(hud.CcComms, "Diplomatie", false);
				hud.Screens.Current?.Refresh();
				return false;
			case 7:
				Check(hud.CcComms.TestTab == "diplomacy", "40: Reiter „Diplomatie“ wechselt");
				Check(hud.CcComms.TestDiplomacyRows >= 3, $"40: Diplomatie listet die KI-Fraktionen ({hud.CcComms.TestDiplomacyRows} Zeilen)");
				hud.CcComms.TestSelectFaction(0);   // Union (Id 1)
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			case 9:
			{
				var btn = Buttons(hud.CcComms).FirstOrDefault(b => b.Text == "Strafe zahlen" && b.IsVisibleInTree());
				Check(btn is not null && btn.State != Ui.Kit.KitButtonState.Disabled, "40: Knopf „Strafe zahlen“ vorhanden und aktiv (Fahndung offen)");
				if (btn is null) return true;
				_r40Wallet = u.Player.Treasury.Credits;
				_r40Treasury = u.Actors[_r40Fac].Treasury.Credits;
				_r40At = btn.GetGlobalRect().GetCenter();
				PushMouse(_r40At, true, false);
				return false;
			}
			case 11:
				PushMouse(_r40At, false, false);   // loslassen (wird im nächsten Bild verarbeitet)
				return false;
			case 13:
			{
				hud.Screens.Current?.Refresh();
				Check(!u.Actors[u.PlayerActor].Wanted.ContainsKey(_r40Fac), "40: Strafe per Knopf gezahlt — Fahndungseintrag gelöscht");
				Check(u.Player.Treasury.Credits < _r40Wallet, $"40: Spielerkasse gesunken ({_r40Wallet:0} → {u.Player.Treasury.Credits:0} Cr, +10 % Fernzahlung)");
				Check(u.Actors[_r40Fac].Treasury.Credits > _r40Treasury + 1500f, $"40: Strafe + Aufschlag in der Fraktionskasse ({_r40Treasury:0} → {u.Actors[_r40Fac].Treasury.Credits:0} Cr)");
				_r40War = Buttons(hud.CcComms).FirstOrDefault(b => b.Text == "Krieg erklären" && b.IsVisibleInTree());
				Check(_r40War is not null && _r40War.State != Ui.Kit.KitButtonState.Disabled, "40: Knopf „Krieg erklären“ vorhanden und aktiv (Kriegskasse ≥ 15 000 Cr)");
				return false;   // Rect erst im nächsten Bild lesen (nach dem Neuaufbau ist sie noch nicht berechnet)
			}
			case 15:
				if (_r40War is null) return true;
				WarTap(hud);   // erster Tipp → nur armed (KitConfirm)
				return false;
			case 17:
				PushMouse(_r40At, false, false);   // erster Tipp loslassen → nur armed (KitConfirm)
				return false;
			case 19:
			{
				Check(u.Wars.Count == 0, "40: erster Tipp auf „Krieg erklären“ führt noch nicht aus (zwei Taps)");
				WarTap(hud);   // zweiter Tipp → bestätigt
				return false;
			}
			case 21:
				return false;   // Bestätigung lief beim zweiten TestPress in case 19
			case 23:
			{
				hud.Screens.Current?.Refresh();
				Check(u.Wars.Count == 1 && Wars.AtWar(u, u.PlayerFaction ?? default, _r40Fac), "40: zweiter Tipp erklärt den Krieg (über die Registry)");
				Check(hud.CcComms.TestMessage.Length > 0, $"40: Aktionsmeldung im Bildschirm („{hud.CcComms.TestMessage}“)");
				if (u.Wars.Count > 0) Wars.MakePeace(u, u.Wars[0], "manual");   // aufräumen (danach startet der Selbsttest ein neues Spiel)
				_m.TestCloseCommand();
				return true;
			}
		}
		return false;
	}
}
