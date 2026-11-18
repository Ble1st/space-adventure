using System.Linq;
using Godot;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 130 (Sprungantrieb und Tarnfeld, SCHIFFSSLOTS.md 4 und 6): neues Spiel (Händler, Nomade), Sprungantrieb und Tarnfeld eingebaut, an eine Stelle
/// ohne Massenschatten. Echte Tipps: „Tarnung“ an (Schiff durchsichtig, Taste „AN“), wieder aus; „Sprung“ öffnet die Sternenkarte im Sprungmodus, Tipp auf einen
/// erreichbaren Sektor startet das Laden (Ladeleuchten), im Zeitraffer springt das Schiff — Sektor gewechselt, Ansicht neu gebaut, Schiff am Ankunftsort mit
/// Schonfrist, Taste „Sprung“ kühlt ab; alle Gruppentasten ohne abgeschnittenen Text.
/// </summary>
public partial class SelfTest
{
	Vector2 _jcAt;
	SectorId _jcFrom, _jcTarget;
	int _jcJumpFrame = -1;

	bool JumpCloakStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		var fl = hud.Flight;
		var me = u.Ships[u.PlayerShip];
		int Group(string label) => fl.TestGroupLabels.IndexOf(label);
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
			{
				_m.TestUndock();
				u.Rules.PlayerRaids = false;   // kein Überfall mitten im Test
				int util = me.Slots.FindIndex(s => s.Type == "utility");
				if (util < 0) { Fail("SP: Nomade ohne Utility-Slot"); return false; }
				me.Fit[util] = "jump_drive_s";
				me.Slots.Add(new ClassSlot { Type = "utility", Size = SlotSize.S });
				me.Fit.Add("cloak_s");
				me.RecomputeStats(u);
				Vec2? spot = null;
				for (int r = 120; r < 3000 && spot is null; r += 40)
					for (int k = 0; k < 16; k++)
					{
						var p = Vec2.FromAngle(k * Mathf.Pi / 8f) * r;
						if (JumpDrive.ShadowAt(u, u.PlayerSector, p) is null) { spot = p; break; }
					}
				if (spot is null) { Fail("SP: keine Stelle ohne Massenschatten"); return false; }
				_m.TestTeleportTo(spot.Value);
				_jcFrom = u.PlayerSector;
				return false;
			}
			case 12:
			{
				Check(Group("Sprung") >= 0 && Group("Tarnung") >= 0, $"SP: Gruppen „Sprung“ und „Tarnung“ ({string.Join(", ", fl.TestGroupLabels)})");
				Check(fl.TestGroupOverflow.Length == 0, $"SP: Gruppentasten ohne abgeschnittenen Text ({fl.TestGroupOverflow})");
				int g = Group("Tarnung");
				if (g < 0) { Fail("SP: keine Taste „Tarnung“"); return false; }
				_jcAt = fl.TestGroupRect(g).GetCenter();
				Tap(_jcAt, true);
				return false;
			}
			case 14: Tap(_jcAt, false); return false;
			case 20:
			{
				int g = Group("Tarnung");
				Check(me.Cloaked, $"SP: Tarnung per Tipp an ({_m.LastJumpCloakMessage})");
				Check(g >= 0 && fl.TestGroupSub(g).StartsWith("AN"), $"SP: Taste zeigt „AN“ ({(g >= 0 ? fl.TestGroupSub(g) : "-")})");
				Check(_m.TestPlayerTransparency >= 0.5f, $"SP: Schiff durchsichtig ({_m.TestPlayerTransparency:0.00})");
				Check(GateAccessStealth(u, me) > 1f, "SP: Mobilitätsprofil mit Tarnstärke");
				Check(fl.TestGroupOverflow.Length == 0, $"SP: Text passt bei Tarnung an ({fl.TestGroupOverflow})");
				Tap(_jcAt, true);
				return false;
			}
			case 22: Tap(_jcAt, false); return false;
			case 28:
			{
				Check(!me.Cloaked, "SP: Tarnung per Tipp aus");
				Check(_m.TestPlayerTransparency < 0.01f, $"SP: Schiff wieder deckend ({_m.TestPlayerTransparency:0.00})");
				int g = Group("Sprung");
				if (g < 0) { Fail("SP: keine Taste „Sprung“"); return false; }
				Check(fl.TestGroupState(g) == Ui.Kit.KitButtonState.Ready, $"SP: „Sprung“ bereit ({fl.TestGroupSub(g)})");
				_jcAt = fl.TestGroupRect(g).GetCenter();
				Tap(_jcAt, true);
				return false;
			}
			case 30: Tap(_jcAt, false); return false;
			case 36:
			{
				Check(_m.TestMapJumpMode, "SP: Sternenkarte im Sprungmodus");
				var targets = JumpDrive.Targets(u, me);
				if (targets.Count == 0) { Fail("SP: kein Sprungziel in Reichweite"); return false; }
				_jcTarget = targets.Last().Sector;   // weitester Sektor in Reichweite (2 Hops)
				if (hud.Map.TestSectorScreen(_jcTarget) is not { } p) { Fail("SP: Zielsektor nicht auf der Karte"); return false; }
				_jcAt = p;
				Tap(_jcAt, true);
				return false;
			}
			case 38: Tap(_jcAt, false); return false;
			case 42:
				Check(!hud.Map.Visible, "SP: Karte nach der Wahl zu");
				Check(JumpDrive.Charging(me) && me.JumpTarget == _jcTarget, $"SP: Sprungantrieb lädt → {_jcTarget.Value} ({_m.LastJumpCloakMessage})");
				Check(_m.TestJumpFxVisible, "SP: Ladeleuchten sichtbar");
				Check(fl.TestGroupSub(Group("Sprung")).StartsWith("lädt"), $"SP: Taste zeigt „lädt“ ({fl.TestGroupSub(Group("Sprung"))})");
				_m.TestSetFast(true);   // Zeitraffer: 15 s Laden in 1,5 s
				return false;
		}
		if (f > 42 && _jcJumpFrame < 0)
		{
			if (u.PlayerSector == _jcTarget) _jcJumpFrame = f;
			else if (f > 400) { Fail($"SP: kein Sprung nach 360 Bildern (lädt {JumpDrive.Progress(me):0.00}, {_m.LastJumpCloakMessage})"); return false; }
			return false;
		}
		if (_jcJumpFrame >= 0 && f == _jcJumpFrame + 6)
		{
			_m.TestSetFast(false);
			var view = _m.TestPlayerViewPos;
			Check(me.Sector == _jcTarget && u.PlayerSector == _jcTarget, "SP: Schiff im Zielsektor");
			Check(!_m.TestView.NeedsRebuild(u), "SP: Ansicht des Zielsektors gebaut");
			Check(new Vec2(view.X, view.Z).Equals(me.Position) || Vec2.Distance(new Vec2(view.X, view.Z), me.Position) < 3f, $"SP: Ansicht am Ankunftsort ({view} / {me.Position})");
			Check(JumpDrive.ShadowAt(u, _jcTarget, me.Position) is null, "SP: Ankunft ohne Massenschatten");
			Check(_m.TestPlayerGrace > 0f, "SP: Schonfrist nach dem Sprung");
			Check(_m.LastJumpCloakMessage.StartsWith("Sprung nach"), $"SP: Meldung „{_m.LastJumpCloakMessage}“");
			Check(!_m.TestJumpFxVisible, "SP: Ladeleuchten aus");
			int g = Group("Sprung");
			Check(g >= 0 && fl.TestGroupSub(g).StartsWith("kühlt"), $"SP: Taste kühlt ab ({(g >= 0 ? fl.TestGroupSub(g) : "-")})");
			Check(fl.TestGroupOverflow.Length == 0, $"SP: Text passt beim Abklingen ({fl.TestGroupOverflow})");
			Check(JumpDrive.Hops(u, _jcFrom, _jcTarget) is >= 1 and <= 2, "SP: Ziel in Reichweite (Hops)");
			return true;
		}
		return false;
	}

	static float GateAccessStealth(Universe u, Ship me) => SpaceSim.Core.Simulation.GateAccess.ProfileOf(u, me).Stealth;
}
