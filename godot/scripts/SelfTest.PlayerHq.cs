using System.Linq;
using Godot;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 142 (Eigenes Hauptquartier, docs/POLITIK.md „Eigenes Hauptquartier“): neues Spiel (Händler), reich gemacht, Sektorbehörde in Nebelmark
/// (Core-Aufruf — die Gründung per Tipp prüft Stufe 129); „Meine Fraktion“ → „Hauptquartier gründen“: erster echter Tipp führt nicht aus (KitConfirm),
/// zweiter gründet → Kasse um die Kosten kleiner, Nebelmark unter Kontrolle (Sperrstufe bis 5), Zeile des Hauptquartiers, Fraktionen-Detail der eigenen
/// Fraktion zeigt das Hauptquartier.
/// </summary>
public partial class SelfTest
{
	Vector2 _hqAt;
	float _hqWallet;

	/// <summary>Rollt die umgebende Liste so, dass die Taste ganz sichtbar ist; liefert ihre Mitte (null = noch nicht sichtbar).</summary>
	static Vector2? HqVisible(Ui.Kit.KitButton b)
	{
		Node? n = b.GetParent();
		while (n is not null && n is not ScrollContainer) n = n.GetParent();
		var r = b.GetGlobalRect();
		if (n is not ScrollContainer sc) return r.GetCenter();
		var view = sc.GetGlobalRect();
		if (view.Encloses(r)) return r.GetCenter();
		float dy = r.Position.Y - view.Position.Y - 40f;
		sc.ScrollVertical = (int)Mathf.Clamp(sc.ScrollVertical + dy / Mathf.Max(0.01f, sc.GetGlobalTransform().Scale.Y), 0f, (float)sc.GetVScrollBar().MaxValue);
		return null;
	}

	bool PlayerHqStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
			{
				Check(u.Rules.PlayerHq && u.Rules.PlayerGovernmentDebt, "HQ: eigenes Hauptquartier in der Standardwelt an");
				u.Player.Treasury.Credits = 2_000_000f;
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();
				_m.TestEnterSector(u.Sectors.Values.First(s => s.Name.Contains("Nebelmark")).Id);
				return false;
			}
			case 12:
			{
				var neb = u.Sectors.Values.First(s => s.Name.Contains("Nebelmark")).Id;
				var cands = new[] { new Vec2(0, 0), new(200, 200), new(-200, 200), new(200, -200), new(-200, -200), new(400, 0), new(-400, 0) };
				foreach (var c in cands) { _m.TestTeleportTo(c); if (PlayerSeats.FoundIssue(u, neb) is null) break; }
				var res = CommsActions.Default().Execute(u, $"seat:found:{neb.Value}");
				Check(res.Success, $"HQ: Sektorbehörde in Nebelmark ({res.Message})");
				foreach (var c in cands) { _m.TestTeleportTo(c); if (PlayerHq.FoundIssue(u, neb) is null) break; }
				Check(PlayerHq.FoundIssue(u, neb) is null, $"HQ: Gründung in Nebelmark möglich ({PlayerHq.FoundIssue(u, neb)})");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.MyFaction);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 16: case 18: case 20:
			{
				var b = R45Button(hud.CcMyFaction, "Hauptquartier gründen");
				if (b is not null) HqVisible(b);
				return false;
			}
			case 24:
			{
				var b = R45Button(hud.CcMyFaction, "Hauptquartier gründen");
				if (b is null || HqVisible(b) is not { } at) { Fail($"HQ: „Hauptquartier gründen“ fehlt oder nicht sichtbar ({hud.CcMyFaction.TestHqLine})"); return false; }
				_hqAt = at;
				_hqWallet = u.Player.Treasury.Credits;
				Tap(_hqAt, true);
				return false;
			}
			case 26: Tap(_hqAt, false); return false;
			case 30:
			{
				Check(PlayerHq.Current(u) is null, "HQ: erster Tipp gründet noch nicht (zwei Tipps)");
				var b = R45Button(hud.CcMyFaction, "Wirklich?", "Hauptquartier gründen");
				if (b is null || HqVisible(b) is not { } at) { Fail("HQ: bestätigende Taste nicht sichtbar"); return false; }
				_hqAt = at;
				Tap(_hqAt, true);
				return false;
			}
			case 32: Tap(_hqAt, false); return false;
			case 36:
			{
				hud.Screens.Current?.Refresh();
				var hq = PlayerHq.Current(u);
				var neb = u.Sectors.Values.First(s => s.Name.Contains("Nebelmark"));
				Check(hq is not null && hq.Sector == neb.Id, $"HQ: zweiter Tipp gründet das Hauptquartier in Nebelmark ({hud.CcMyFaction.TestPolicyMessage})");
				float cost = PlayerHq.Cost(u, PlayerHq.HqType(u)!);
				Check(Mathf.Abs(_hqWallet - u.Player.Treasury.Credits - cost) < 50f, $"HQ: Kasse um die Kosten kleiner ({_hqWallet:0} → {u.Player.Treasury.Credits:0}, Kosten {cost:0})");
				Check(neb.Control == ControlStage.Control && SectorControl.MaxLevel(u, neb) == 5, $"HQ: Nebelmark unter Kontrolle ({neb.Control}, Deckel {SectorControl.MaxLevel(u, neb)})");
				Check(hq is not null && hud.CcMyFaction.TestHqLine.Contains(hq.Name), $"HQ: Karte zeigt das Hauptquartier ({hud.CcMyFaction.TestHqLine})");
				Check(Buttons(hud.CcMyFaction).Any(b => b.Text == "Hauptquartier aufgeben"), "HQ: Taste „Hauptquartier aufgeben“");
				Check(Buttons(hud.CcMyFaction).Any(b => b.Text is "Stufe 5" or "5"), "HQ: Stufenknöpfe bis 5");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Factions);
				hud.CcFactions.TestSelect(u.PlayerActor);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 40:
			{
				Check(hud.CcFactions.TestHq.StartsWith("Hauptquartier:"), $"HQ: Fraktionen-Detail zeigt das Hauptquartier ({hud.CcFactions.TestHq})");
				_m.TestCloseCommand();
				return true;
			}
		}
		return false;
	}
}
