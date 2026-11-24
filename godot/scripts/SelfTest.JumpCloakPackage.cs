using System.Linq;
using Godot;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 141 (Startpaket „Sprung- und Tarntest (Test)“): Kommando → System → Startauswahl, das Paket steht unter „Testszenarien“, echter Tipp auf
/// „Starten“. Danach über die echten Tasten: „Tarnung“ an (Schiff durchsichtig, der lauernde Kaperer wählt den Spieler nicht als Opfer, die Polizei sieht Mk2
/// nicht), „Sprung“ → Sternenkarte → Ziel zwei Hops weit → Laden mit eingeschalteter Tarnung (Leistung reicht, kein Abbruch) → Ankunft, Taste kühlt ab;
/// Treibstoff ist an: voller Tank am Start, der Sprung verbraucht Tank.
/// </summary>
public partial class SelfTest
{
	Vector2 _jpAt;
	SectorId _jpFrom, _jpTarget;
	int _jpJumpFrame = -1;
	float _jpFuel;

	bool JumpCloakPackageStage(int f)
	{
		var hud = _m.HudRef;
		var fl = hud.Flight;
		int Group(string label) => fl.TestGroupLabels.IndexOf(label);
		switch (f)
		{
			case 1: _m.TestShowStart(); hud.Start.ShowTests = true; hud.Start.TestRebuild(); return false;   // UI-Umbau: Startbildschirm
			case 5:
			{
				var texts = hud.Start.TestChooserTexts();
				int header = texts.FindIndex(t => t.Contains("Testszenarien"));
				int card = texts.FindIndex(t => t.Contains("Sprung- und Tarntest"));
				Check(header >= 0 && card > header, $"SJ: „Sprung- und Tarntest (Test)“ unter den Testszenarien (Kopf {header}, Karte {card})");
				var b = hud.Start.TestStartButton("jumpcloak");
				var scroll = hud.Start.TestChooserScroll;
				if (b is null || scroll is null) { Fail("SJ: keine Starttaste für „jumpcloak“"); return false; }
				// Karte in den sichtbaren Bereich rollen, dann echt tippen
				scroll.ScrollVertical = Mathf.Max(0, (int)(b.GlobalPosition.Y - scroll.GlobalPosition.Y + scroll.ScrollVertical - 60f));
				return false;
			}
			case 10:
			{
				var b = hud.Start.TestStartButton("jumpcloak");
				if (b is null) { Fail("SJ: Starttaste verschwunden"); return false; }
				_jpAt = b.GetGlobalRect().GetCenter();
				Check(hud.Start.TestChooserScroll!.GetGlobalRect().HasPoint(_jpAt), $"SJ: Starttaste im sichtbaren Bereich ({_jpAt})");
				Tap(_jpAt, true);
				return false;
			}
			case 12: Tap(_jpAt, false); return false;
			case 24:
			{
				var u = _m.Session.U;
				var me = u.Ships[u.PlayerShip];
				Check(me.ModelKey == "nomad" && me.Fit.Contains("jump_drive_s") && me.Fit.Contains("cloak_s_mk2"),
					$"SJ: Paket gestartet — Nomade mit Sprungantrieb und Tarnfeld Mk2 ({me.ModelKey}: {string.Join(",", me.Fit.Select(x => x ?? "-"))})");
				Check(!hud.Start.Visible && !_m.TestInStartMenu, "SJ: Startbildschirm nach dem Start zu");
				Check(u.Rules.FuelActive && me.TotalFuel >= me.Stats.FuelCapacity - 0.5f, $"SJ: Treibstoff an, voller Tank ({me.TotalFuel:0}/{me.Stats.FuelCapacity:0})");
				Check(u.ShipsById().Any(r => ThreatMap.IsRaider(r) && r.Sector == me.Sector && Vec2.Distance(r.Position, me.Position) < 100f), "SJ: Kaperer in Sichtweite");
				u.Rules.PlayerRaids = false;   // erst tarnen, dann die Kaperer wieder zulassen (eine Vorwarnung würde das Einschalten sperren)
				u.PendingEncounters.RemoveAll(p => p.Target == me.Id);
				_jpFrom = u.PlayerSector;
				_jpFuel = me.TotalFuel;
				int g = Group("Tarnung");
				Check(Group("Sprung") >= 0 && g >= 0, $"SJ: Gruppen „Sprung“ und „Tarnung“ ({string.Join(", ", fl.TestGroupLabels)})");
				if (g < 0) { Fail("SJ: keine Taste „Tarnung“"); return false; }
				_jpAt = fl.TestGroupRect(g).GetCenter();
				Tap(_jpAt, true);
				return false;
			}
			case 26: Tap(_jpAt, false); return false;
			case 32:
			{
				var u = _m.Session.U;
				var me = u.Ships[u.PlayerShip];
				Check(me.Cloaked, $"SJ: Tarnung per Tipp an ({_m.LastJumpCloakMessage})");
				Check(_m.TestPlayerTransparency >= 0.5f, $"SJ: Schiff durchsichtig ({_m.TestPlayerTransparency:0.00})");
				u.Rules.PlayerRaids = true;
				Check(!PirateSystem.Victims(u, me.Sector).Any(v => v.Ship.Id == me.Id), "SJ: Kaperer wählen den getarnten Spieler nicht als Opfer");
				Check(Cloak.HiddenFrom(me, u.Rules.PoliceSensorStrength), "SJ: Mk2 auch vor der Polizei verborgen");
				int g = Group("Sprung");
				if (g < 0) { Fail("SJ: keine Taste „Sprung“"); return false; }
				Check(fl.TestGroupState(g) == Ui.Kit.KitButtonState.Ready, $"SJ: „Sprung“ bereit ({fl.TestGroupSub(g)})");
				_jpAt = fl.TestGroupRect(g).GetCenter();
				Tap(_jpAt, true);
				return false;
			}
			case 34: Tap(_jpAt, false); return false;
			case 40:
			{
				var u = _m.Session.U;
				var me = u.Ships[u.PlayerShip];
				Check(_m.TestMapJumpMode, "SJ: Sternenkarte im Sprungmodus");
				var two = JumpDrive.Targets(u, me).Where(t => t.Hops == 2).Select(t => t.Sector).ToList();
				if (two.Count == 0) { Fail("SJ: kein Ziel zwei Hops weit"); return false; }
				_jpTarget = two[0];
				if (hud.Map.TestSectorScreen(_jpTarget) is not { } p) { Fail("SJ: Zielsektor nicht auf der Karte"); return false; }
				_jpAt = p;
				Tap(_jpAt, true);
				return false;
			}
			case 42: Tap(_jpAt, false); return false;
			case 46:
			{
				var u = _m.Session.U;
				var me = u.Ships[u.PlayerShip];
				Check(JumpDrive.Charging(me) && me.JumpTarget == _jpTarget, $"SJ: lädt → {_jpTarget.Value} ({_m.LastJumpCloakMessage})");
				Check(me.Cloaked, "SJ: Tarnung bleibt beim Laden an");
				Check(me.Stats.PowerDraw <= me.Stats.PowerOutput, $"SJ: Leistung reicht für Tarnung + Laden ({me.Stats.PowerDraw:0.#}/{me.Stats.PowerOutput:0.#})");
				_m.TestSetFast(true);
				return false;
			}
		}
		if (f > 46 && _jpJumpFrame < 0)
		{
			var u = _m.Session.U;
			if (u.PlayerSector == _jpTarget) _jpJumpFrame = f;
			else if (f > 420) { Fail($"SJ: kein Sprung nach 370 Bildern (lädt {JumpDrive.Progress(u.Ships[u.PlayerShip]):0.00}, {_m.LastJumpCloakMessage})"); return true; }
			return false;
		}
		if (_jpJumpFrame >= 0 && f == _jpJumpFrame + 6)
		{
			_m.TestSetFast(false);
			var u = _m.Session.U;
			var me = u.Ships[u.PlayerShip];
			Check(me.Sector == _jpTarget && JumpDrive.Hops(u, _jpFrom, _jpTarget) == 2, "SJ: Sprung zwei Hops weit angekommen");
			Check(!_m.TestView.NeedsRebuild(u), "SJ: Ansicht des Zielsektors gebaut");
			Check(me.TotalFuel < _jpFuel, $"SJ: Sprung verbraucht Treibstoff ({_jpFuel:0.#} → {me.TotalFuel:0.#})");
			int g = Group("Sprung");
			Check(g >= 0 && fl.TestGroupSub(g).StartsWith("kühlt"), $"SJ: Taste kühlt ab ({(g >= 0 ? fl.TestGroupSub(g) : "-")})");
			Check(fl.TestGroupOverflow.Length == 0, $"SJ: Gruppentasten ohne abgeschnittenen Text ({fl.TestGroupOverflow})");
			u.Rules.PlayerRaids = false;   // folgende Stufen bauen ihre eigenen Welten; hier keine Überfälle mehr
			return true;
		}
		return false;
	}
}
