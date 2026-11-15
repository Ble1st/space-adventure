using System;
using System.Linq;
using Godot;
using SpaceSim.Core.Ui;
using SpaceAdventure.Ui.Kit;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 114 (Runde 38a, Handy-Abschlusstest): Hamburger-Menü bleibt bei „UI-Größe“, „Musik“ und „Effekte“ offen und die Zeile „UI-Größe“ an derselben
/// Stelle (echte Tipps, auch nach dem Wechsel der Größe); eine fremde Flucht im Sektor erscheint als leise Info, nicht als großer Hinweis.
/// </summary>
public partial class SelfTest
{
	Vector2 _r38At;
	float _r38Off;
	int _r38Wait;
	Rect2 _r38Rect;

	bool Round38aStage(int f)
	{
		var hud = _m.HudRef;
		switch (f)
		{
			case 1:
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();
				hud.HideAlert();
				if (GetTree().Root.Size.X < 640 || GetTree().Root.Size.X == GetTree().Root.Size.Y || GetTree().Root.Size != new Vector2I(1280, 720)) GetTree().Root.Size = new Vector2I(1280, 720);
				UiScale.TestForce = null; UiScale.TestPpi = 396f; UiScale.Set(UiSize.Normal); UiScale.Apply();
				_m.TestOpenCommand();
				return false;
			case 20:
				// Liste so rollen, dass „UI-Größe“ sichtbar ist, aber nicht ganz unten (sonst hält das Ende der Liste die Lage von selbst)
				{
					var sc = hud.Command.TestScroll;
					float max = (float)(sc.GetVScrollBar().MaxValue - sc.GetVScrollBar().Page);
					sc.ScrollVertical = (int)MathF.Max(0f, max - MathF.Min(50f, max / 2f));
					Check(max > 20f, $"38a: Menüliste rollt bei „normal“ ({max:0})");
				}
				return false;
			case 30:
			case 31:
			{
				var b = Buttons(hud.Command).FirstOrDefault(x => x.Text.StartsWith("UI-Größe", StringComparison.Ordinal) && x.IsVisibleInTree());
				if (b is null) { Check(false, "38a: Menüzeile „UI-Größe“ fehlt"); return false; }
				var r = b.GetGlobalRect().Intersection(hud.Command.TestScroll.GetGlobalRect());   // nur der sichtbare Teil (die Liste beschneidet)
				if (r != _r38Rect && _r38Wait++ < 30) { _r38Rect = r; _stageStart += 1; return false; }
				_r38Off = hud.Command.TestRowOffset("ui_size");
				_r38At = r.GetCenter();
				Tap(_r38At, true);
				return false;
			}
			case 33: Tap(_r38At, false); return false;
			case 50:
			{
				float off = hud.Command.TestRowOffset("ui_size");
				Check(hud.Screens.Current == hud.Command, $"38a: Menü bleibt nach „UI-Größe“ offen ({hud.Screens.Current?.Id ?? "-"})");
				GD.Print($"[r38a] Zeile „UI-Größe“: Abstand zum Listenrand {_r38Off:0} → {off:0} logische Punkte, Faktor {hud.MenuFactor:0.00}");
				// ein zweiter Tipp an derselben Bildschirmstelle trifft wieder „UI-Größe“
				var b = Buttons(hud.Command).FirstOrDefault(x => x.Text.StartsWith("UI-Größe", StringComparison.Ordinal) && x.IsVisibleInTree());
				// wo es geht, liegt die Zeile wieder unter dem Finger; geht es nicht (Liste am Ende, Fenster kleiner), liegt dort wenigstens keine andere Taste
				var vis = hud.Command.TestScroll.GetGlobalRect();
				bool under = b is not null && b.GetGlobalRect().Grow(2f).HasPoint(_r38At);
				var other = Buttons(hud.Command).FirstOrDefault(x => x != b && x.IsVisibleInTree() && x.GetGlobalRect().Intersection(x.Text == "Schließen" ? x.GetGlobalRect() : vis).HasPoint(_r38At));
				Check(b is not null && vis.Grow(1f).Encloses(b.GetGlobalRect()) && (under || other is null),
					$"38a: nach dem Wechsel ist „UI-Größe“ ganz sichtbar und der nächste Tipp an derselben Stelle trifft keine andere Taste (unter dem Finger {under}, dort: {other?.Text ?? "nichts"}; {b?.GetGlobalRect()} / {_r38At})");
				return false;
			}
			case 52:
			{
				var b = Buttons(hud.Command).FirstOrDefault(x => x.Text.StartsWith("Effekte", StringComparison.Ordinal) || x.Text.Contains("Effekte"));
				if (b is null) { Check(false, "38a: „Effekte“ fehlt"); return false; }
				hud.Command.TestScroll.ScrollVertical = (int)(hud.Command.TestScroll.GetVScrollBar().MaxValue);
				return false;
			}
			case 60:
			{
				var b = Buttons(hud.Command).First(x => x.Text.Contains("Effekte") && x.IsVisibleInTree());
				_r38At = b.GetGlobalRect().Intersection(hud.Command.TestScroll.GetGlobalRect()).GetCenter(); Tap(_r38At, true); return false;
			}
			case 62: Tap(_r38At, false); return false;
			case 74:
				Check(hud.Screens.Current == hud.Command, $"38a: Menü bleibt nach „Effekte“ offen ({hud.Screens.Current?.Id ?? "-"})");
				SpaceAdventure.Audio.AudioSettings.ToggleEffectsMute();   // zurück
				_m.TestCommand("music_mute");
				return false;
			case 80:
				Check(hud.Screens.Current == hud.Command, $"38a: Menü bleibt nach „Musik“ offen ({hud.Screens.Current?.Id ?? "-"})");
				SpaceAdventure.Audio.AudioSettings.ToggleMusicMute();
				_m.TestCloseCommand();
				UiScale.TestPpi = null; UiScale.Set(UiSize.Normal);
				// fremde Flucht im Sektor → leise Info (ein stehender wichtiger Hinweis hätte Vorrang — erst leeren)
				hud.Toast("", 0.01f);
				{
					var u = _m.Session.U;
					var npc = u.ShipsById().First(s => s.IsEconomyAi);
					u.Events.Publish(new SpaceSim.Core.Events.RefugeReached(0, npc.Id, "gate", u.PlayerSector));
				}
				return false;
			case 84:
				Check(hud.TestToastQuiet && hud.TestLastInfo.Contains("springt auf der Flucht"), $"38a: fremde Flucht im Sektor ist eine leise Info („{hud.TestLastInfo}“, leise {hud.TestToastQuiet})");
				return false;
			case 90:
				return true;
		}
		return false;
	}
}
