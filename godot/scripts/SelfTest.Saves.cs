using System;
using System.Linq;
using Godot;
using SpaceAdventure.Game;
using SpaceAdventure.Ui.Screens;
using SpaceSim.Core.Persistence;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 158 (mehrere Spielstände): per echtem Tipp „Speichern unter“ im Pause-Menü mit eigenem Namen, ein zweiter Stand; auf dem Startbildschirm
/// „Laden“ zeigt beide mit Metadaten; der ältere wird geladen (Tick und Kasse passen); ein Stand wird mit zwei Tipps gelöscht; das Autosave-Intervall wird in
/// den Einstellungen umgestellt und in der Test-Einstellungsdatei geprüft. Die Stände liegen im eigenen Prüfverzeichnis (user://test-saves/selftest).
/// </summary>
public partial class SelfTest
{
	long _svTick1;
	string _svId1 = "", _svId2 = "";
	ulong _svNow;
	Func<ulong>? _svClock;

	void TapAt(Control? c, bool down, string what)
	{
		if (c is null || !c.IsVisibleInTree()) { Fail($"SV: {what} nicht sichtbar"); return; }
		Tap(c.GetGlobalRect().GetCenter(), down);
	}

	bool SavesStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		var sv = hud.Saves;
		switch (f)
		{
			case 1:
				Check(SaveService.DirPath.Contains("test-saves"), $"SV: Prüfläufe speichern im eigenen Verzeichnis ({SaveService.DirPath})");
				_m.TestMenu("new:trader");
				return false;
			case 10:
				u.Player.Treasury.Credits = 1234f;
				_m.TestOpenCommand();
				return false;
			case 14:
			{
				var b = hud.Command.TestEntry("saveas");
				if (b is not null) ScrollIntoView(b);
				return false;
			}
			case 18: TapAt(hud.Command.TestEntry("saveas"), true, "„Speichern unter“"); return false;
			case 20: TapAt(hud.Command.TestEntry("saveas"), false, "„Speichern unter“"); return false;
			case 26:
			{
				Check(sv.Visible && sv.Current == SavesScreen.Mode.SaveAs, "SV: „Speichern unter“ öffnet den Bildschirm");
				string sector = u.Sectors[u.PlayerSector].Name;
				Check(sv.TestEdit is { } e && e.Text.StartsWith(sector), $"SV: Namensvorschlag aus Sektor und Datum („{sv.TestEdit?.Text}“)");
				TapAt(sv.TestEdit, true, "Eingabefeld"); return false;
			}
			case 28:
				TapAt(sv.TestEdit, false, "Eingabefeld");
				if (sv.TestEdit is { } ed) { ed.Text = "Test Eins"; ed.EmitSignal(LineEdit.SignalName.TextChanged, ed.Text); }
				return false;
			case 30: TapAt(sv.TestSaveButton, true, "„Speichern“"); return false;
			case 32: TapAt(sv.TestSaveButton, false, "„Speichern“"); return false;
			case 38:
			{
				var m = SaveService.List().FirstOrDefault(x => x.Name == "Test Eins");
				Check(m is { Autosave: false }, "SV: Stand „Test Eins“ gespeichert");
				Check(sv.TestMessage?.Text.StartsWith("Gespeichert") == true, $"SV: Rückmeldung „{sv.TestMessage?.Text}“");
				Check(sv.TestTexts().Contains("Test Eins"), "SV: Liste zeigt den neuen Stand");
				if (m is not null) { _svId1 = m.Id; _svTick1 = m.Tick; Check(Math.Abs(m.Credits - 1234f) < 0.01f, "SV: Metadaten-Kasse 1234"); }
				u.Player.Treasury.Credits = 9999f;   // zweiter Stand: andere Kasse
				if (sv.TestEdit is { } ed2) { ed2.Text = "Test Zwei"; ed2.EmitSignal(LineEdit.SignalName.TextChanged, ed2.Text); }
				return false;
			}
			case 40: TapAt(sv.TestSaveButton, true, "„Speichern“"); return false;
			case 42: TapAt(sv.TestSaveButton, false, "„Speichern“"); return false;
			case 48:
			{
				var m = SaveService.List().FirstOrDefault(x => x.Name == "Test Zwei");
				Check(m is not null, "SV: zweiter Stand „Test Zwei“ gespeichert");
				if (m is not null) _svId2 = m.Id;
				TapAt(sv.TestBack, true, "„✕ Zurück“"); return false;
			}
			case 50: TapAt(sv.TestBack, false, "„✕ Zurück“"); return false;
			case 54:
				Check(!sv.Visible, "SV: „Zurück“ schließt den Bildschirm");
				_m.TestMenu("mainmenu");   // speichert den Autosave, zeigt den Startbildschirm
				return false;
			case 58: _m.TestShowStart(); return false;
			case 60: TapAt(hud.Start.TestLoad, true, "„Laden“ (Startbildschirm)"); return false;
			case 62: TapAt(hud.Start.TestLoad, false, "„Laden“ (Startbildschirm)"); return false;
			case 68:
			{
				Check(sv.Visible && sv.Current == SavesScreen.Mode.Load, "SV: „Laden“ öffnet die Liste");
				var texts = sv.TestTexts();
				Check(texts.Contains("Test Eins") && texts.Contains("Test Zwei"), $"SV: Liste zeigt beide Stände ({string.Join(" | ", texts.Take(12))})");
				var m1 = sv.TestSlots.FirstOrDefault(x => x.Id == _svId1);
				Check(m1 is not null && texts.Contains(SaveSlots.Line(m1)) && SaveSlots.Line(m1).Contains("1.234 Cr") && SaveSlots.Line(m1).Contains("Spielzeit"), $"SV: Metadatenzeile „{(m1 is null ? "-" : SaveSlots.Line(m1))}“");
				Check(sv.TestSlots.Any(x => x.Autosave), "SV: Autosave-Stand des Spiels steht in der Liste");
				var b = sv.TestLoadButton(_svId1);
				if (b is not null) ScrollIntoView(b);
				return false;
			}
			case 70: TapAt(sv.TestLoadButton(_svId1), true, "„Laden“ (Test Eins)"); return false;
			case 72: TapAt(sv.TestLoadButton(_svId1), false, "„Laden“ (Test Eins)"); return false;
			case 76:
				Check(!sv.Visible && !hud.Start.Visible, "SV: Laden schließt Liste und Startbildschirm");
				Check(u.Tick >= _svTick1 && u.Tick <= _svTick1 + 2, $"SV: geladener Tick {u.Tick} = gespeichert {_svTick1}");
				Check(Math.Abs(u.PlayerWallet.Credits - 1234f) < 0.01f, $"SV: geladene Kasse {u.PlayerWallet.Credits:0.##} = 1234");
				sv.Open(SavesScreen.Mode.Load);
				return false;
			case 80:
			{
				_svClock = Ui.Kit.KitConfirm.Now;
				_svNow = 1_000_000;
				Ui.Kit.KitConfirm.Now = () => _svNow;
				var d = sv.TestConfirm("del:" + _svId2);
				if (d is not null) ScrollIntoView(d);
				return false;
			}
			case 82: TapAt(sv.TestConfirm("del:" + _svId2), true, "„Löschen“"); return false;
			case 84: TapAt(sv.TestConfirm("del:" + _svId2), false, "„Löschen“"); return false;
			case 88:
				Check(SaveService.List().Any(x => x.Id == _svId2), "SV: erster Tipp auf „Löschen“ löscht nichts");
				Check(sv.TestConfirm("del:" + _svId2)?.Text == "Wirklich?", "SV: „Löschen“ fragt „Wirklich?“");
				_svNow += 800;
				TapAt(sv.TestConfirm("del:" + _svId2), true, "„Löschen“ (2)");
				return false;
			case 90: TapAt(sv.TestConfirm("del:" + _svId2), false, "„Löschen“ (2)"); return false;
			case 96:
				Check(!SaveService.List().Any(x => x.Id == _svId2), "SV: zweiter Tipp löscht „Test Zwei“");
				Check(SaveService.List().Any(x => x.Id == _svId1), "SV: „Test Eins“ bleibt");
				Check(!sv.TestTexts().Contains("Test Zwei"), "SV: Liste ohne den gelöschten Stand");
				if (_svClock is not null) Ui.Kit.KitConfirm.Now = _svClock;
				sv.Close();
				_m.TestMenu("mainmenu");
				return false;
			case 100: _m.TestShowStart(); return false;
			case 102: TapAt(hud.Start.TestSettings, true, "„Einstellungen“"); return false;
			case 104: TapAt(hud.Start.TestSettings, false, "„Einstellungen“"); return false;
			case 110:
			{
				var b = hud.Start.TestAutosave(60);
				if (b is not null) ScrollIntoView(b);
				return false;
			}
			case 112: TapAt(hud.Start.TestAutosave(60), true, "Autosave „1 min“"); return false;
			case 114: TapAt(hud.Start.TestAutosave(60), false, "Autosave „1 min“"); return false;
			case 120:
				Check(SaveSettings.AutosaveSeconds == 60, $"SV: Autosave-Intervall 60 s (ist {SaveSettings.AutosaveSeconds})");
				Check(Audio.AudioSettings.Path.Contains("settings-test") && SaveSettings.FileValue() == 60, $"SV: Testeinstellungsdatei {Audio.AudioSettings.Path} hat autosave_seconds = {SaveSettings.FileValue()}");
				Check(hud.Start.TestAutosave(60)?.State == Ui.Kit.KitButtonState.On, "SV: „1 min“ ist markiert");
				SaveSettings.SetAutosave(SaveSlots.DefaultAutosaveSeconds);
				_m.TestLeaveStart();
				return false;
			case 124: return true;
		}
		return false;
	}
}
