using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 129 (Runde 45, docs/POLITIK.md „Runde 45“): neues Spiel (Händler); (1) Comms → Diplomatie → Union: „Bündnis vorschlagen“ über einen
/// echten Tipp (Beziehung vorher auf 0,7 gestellt) → Bündnis besteht, Detailzeile „Bündnis: mit dir …“, Fraktionen-Detail zeigt das Bündnis; (2) Krieg mit der
/// Gilde (gealtert, Spieler klar überlegen): „Tribut fordern“ über zwei echte Tipps (KitConfirm) → Geld in der Spielerkasse, Frieden; (3) Meine Fraktion →
/// Sektorpolitik in Nebelmark: Sektorbehörde gründen und Sperrstufe 1 setzen (Knöpfe der Karte) → Sektor gehört dir, Stufe 1 gilt.
/// </summary>
public partial class SelfTest
{
	Vector2 _r45At;
	float _r45Wallet;
	FactionId _r45Union, _r45Guild;

	Ui.Kit.KitButton? R45Button(Node root, params string[] labels) =>
		Buttons(root).FirstOrDefault(b => labels.Contains(b.Text) && b.IsVisibleInTree() && b.State != Ui.Kit.KitButtonState.Disabled);

	/// <summary>Diplomatie-Detail so weit rollen, dass die Taste sichtbar ist; liefert ihre Mitte (null = nicht sichtbar).</summary>
	Vector2? R45Visible(Ui.Hud hud, Ui.Kit.KitButton b)
	{
		var sc = hud.CcComms.TestDetailScroll;
		if (sc is null) return b.GetGlobalRect().GetCenter();
		var view = sc.GetGlobalRect();
		var r = b.GetGlobalRect();
		if (view.Encloses(r)) return r.GetCenter();
		// UI-Umbau (Mittelteil niedriger): ganz unten gerollt reicht es, wenn die Mitte der Taste sichtbar ist
		if (sc.ScrollVertical >= (int)(sc.GetVScrollBar().MaxValue - sc.GetVScrollBar().Page) - 1 && view.HasPoint(r.GetCenter())) return r.GetCenter();
		float dy = r.Position.Y - view.Position.Y - 40f;
		sc.ScrollVertical = (int)Mathf.Clamp(sc.ScrollVertical + dy / Mathf.Max(0.01f, sc.GetGlobalTransform().Scale.Y), 0f, (float)sc.GetVScrollBar().MaxValue);
		return null;
	}

	bool Round45Stage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
			{
				var me = u.PlayerFaction;
				if (me is null) { Fail("45: keine Spielerfraktion"); return false; }
				Check(u.Rules.AlliancesActive && u.Rules.AiTributePeace && u.Rules.PlayerSeats, "45: Bündnisse, Tributfrieden und Sektorbehörden in der Standardwelt an");
				_r45Union = u.NpcFactionIds()[0];
				_r45Guild = u.NpcFactionIds()[1];
				Relations.Set(u, _r45Union, me.Value, 0.7f);
				Relations.Set(u, me.Value, _r45Union, 0.7f);
				u.Player.Treasury.Credits = 2_000_000f;
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Comms);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 8: PressButton(hud.CcComms, "Diplomatie", true); return false;
			case 10: PressButton(hud.CcComms, "Diplomatie", false); hud.Screens.Current?.Refresh(); return false;
			case 12:
				Check(hud.CcComms.TestTab == "diplomacy", "45: Reiter „Diplomatie“");
				hud.CcComms.TestSelectFaction(0);
				hud.Screens.Current?.Refresh();
				return false;
			case 14: case 16: case 18:
			{
				var b = R45Button(hud.CcComms, "Bündnis vorschlagen");
				if (b is null) { if (f == 18) Fail("45: Taste „Bündnis vorschlagen“ fehlt oder aus"); return false; }
				if (R45Visible(hud, b) is { } at) { _r45At = at; }
				return false;
			}
			case 20:
			{
				var b = R45Button(hud.CcComms, "Bündnis vorschlagen");
				if (b is null || R45Visible(hud, b) is not { } at) { Fail("45: „Bündnis vorschlagen“ nicht sichtbar"); return false; }
				_r45At = at;
				Tap(_r45At, true);
				return false;
			}
			case 22: Tap(_r45At, false); return false;
			case 26:
			{
				hud.Screens.Current?.Refresh();
				var me = u.PlayerFaction!.Value;
				Check(Alliances.Allied(u, me, _r45Union), $"45: echter Tipp schließt das Bündnis mit der Union ({hud.CcComms.TestMessage})");
				Check(hud.CcComms.TestAllianceRow.StartsWith("mit dir"), $"45: Detailzeile „Bündnis“ ({hud.CcComms.TestAllianceRow})");
				// Krieg mit der Gilde, gealtert; Spieler klar überlegen (Kasse), Gilde zahlungsfähig
				u.Actors[_r45Guild].Treasury.Credits = 200_000f;
				var w = Wars.PlayerDeclare(u, _r45Guild);
				w.Since = u.Tick - 13_000; w.Weariness = 0.33f;
				if (w.A == _r45Guild) w.ShipsLostA = 1; else w.ShipsLostB = 1;   // Ausgleich nach dem Bot-Test: Tribut fordern setzt Verluste des Gegners voraus (Regel TributeDemandNeedsLosses) — das gealterte Kriegsszenario hat schon gekämpft
				Check(Wars.DemandIssue(u, _r45Guild) is null, $"45: Tribut fordern möglich ({Wars.DemandIssue(u, _r45Guild)})");
				hud.CcComms.TestSelectFaction(1);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 28: case 30: case 32:
			{
				var b = R45Button(hud.CcComms, "Tribut fordern");
				if (b is not null) R45Visible(hud, b);
				return false;
			}
			case 34:
			{
				var b = R45Button(hud.CcComms, "Tribut fordern");
				if (b is null || R45Visible(hud, b) is not { } at) { Fail("45: „Tribut fordern“ fehlt oder nicht sichtbar"); return false; }
				_r45At = at;
				_r45Wallet = u.Player.Treasury.Credits;
				Tap(_r45At, true);
				return false;
			}
			case 36: Tap(_r45At, false); return false;
			case 40:
			{
				Check(u.Wars.Count == 1, "45: erster Tipp auf „Tribut fordern“ führt noch nicht aus (zwei Taps)");
				var b = R45Button(hud.CcComms, "Wirklich?", "Tribut fordern");
				if (b is null || R45Visible(hud, b) is not { } at) { Fail("45: bestätigende Taste nicht sichtbar"); return false; }
				_r45At = at;
				Tap(_r45At, true);
				return false;
			}
			case 42: Tap(_r45At, false); return false;
			case 46:
			{
				Check(u.Wars.Count == 0, $"45: zweiter Tipp: Tribut gefordert, Frieden ({hud.CcComms.TestMessage})");
				Check(u.Player.Treasury.Credits > _r45Wallet + 1000f, $"45: Tribut in der Spielerkasse ({_r45Wallet:0} → {u.Player.Treasury.Credits:0} Cr)");
				// Fraktionen-Detail zeigt das Bündnis
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Factions);
				hud.CcFactions.TestSelect(_r45Union);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 48:
			{
				Check(hud.CcFactions.TestAlliance.Contains("dir"), $"45: Fraktionen-Detail zeigt das Bündnis ({hud.CcFactions.TestAlliance})");
				// Sektorbehörde in Nebelmark (herrenlos): Schiff hinfahren, freien Platz suchen
				var neb = u.Sectors.Values.First(s => s.Name.Contains("Nebelmark")).Id;
				_m.TestEnterSector(neb);
				return false;
			}
			case 52:
			{
				var neb = u.Sectors.Values.First(s => s.Name.Contains("Nebelmark")).Id;
				var ship = u.Ships[u.PlayerShip];
				foreach (var cand in new[] { new SpaceSim.Core.Model.Vec2(0, 0), new(200, 200), new(-200, 200), new(200, -200), new(-200, -200), new(400, 0), new(-400, 0) })
				{
					_m.TestTeleportTo(cand);
					if (PlayerSeats.FoundIssue(u, neb) is null) break;
				}
				Check(PlayerSeats.FoundIssue(u, neb) is null, $"45: Gründung in Nebelmark möglich ({PlayerSeats.FoundIssue(u, neb)})");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.MyFaction);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 54: R45Button(hud.CcMyFaction, "Sektorbehörde gründen")?.TestPress(); return false;   // erster Tipp → nur scharf (KitConfirm)
			case 56: R45Button(hud.CcMyFaction, "Wirklich?")?.TestPress(); return false;
			case 60:
			{
				hud.Screens.Current?.Refresh();
				var me = u.PlayerFaction!.Value;
				var neb = u.Sectors.Values.First(s => s.Name.Contains("Nebelmark"));
				Check(neb.Owner == me && PlayerSeats.Seats(u).Count == 1, $"45: Sektorbehörde gegründet, Nebelmark gehört dir ({hud.CcMyFaction.TestPolicyMessage})");
				Check(hud.CcMyFaction.TestPolicyLine.Contains("Nebelmark"), $"45: Karte Sektorpolitik zeigt den Sektor ({hud.CcMyFaction.TestPolicyLine})");
				var s1 = R45Button(hud.CcMyFaction, "Stufe 1");
				Check(s1 is not null, "45: Taste „Stufe 1“ aktiv");
				s1?.TestPress();
				return false;
			}
			case 62:
			{
				var neb = u.Sectors.Values.First(s => s.Name.Contains("Nebelmark"));
				Check(neb.Access.EntryLevel == 1, $"45: Sperrstufe 1 in Nebelmark gesetzt ({hud.CcMyFaction.TestPolicyMessage})");
				Check(Buttons(hud.CcMyFaction).Any(b => b.Text == "Stufe 2" && b.State == Ui.Kit.KitButtonState.Disabled), "45: Stufe 2 erst nach der Mindestdauer");
				_m.TestCloseCommand();
				return true;
			}
		}
		return false;
	}
}
