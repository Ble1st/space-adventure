using System.Linq;
using Godot;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 100 (Runde 21): die Vorwarnung zählt herunter; die Hinweisfläche schrumpft mit dem Text; ein Begleiter in Formation zeigt seine
/// dargestellte Entfernung (nicht 0 m); „Nächster Gegner“ räumt einen scharfen Torangriff ab; Besitz-Zeile passt; die Legende der Draufsicht überlappt den
/// Kreisrand nicht.
/// </summary>
public partial class SelfTest
{
	string _r21Warn = "";
	PendingEncounter? _r21Pending;

	bool Round21Stage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef.Flight;
		switch (f)
		{
			case 1:
				_m.TestMenu("new:war");
				return false;
			case 60:
			{
				// Begleiter als Ziel: Entfernung aus der dargestellten Formationsposition
				var me = u.Ships[u.PlayerShip];
				var wing = u.ShipsById().First(s => s.IsPlayer && s.Id != me.Id);
				_m.TestSetShipTarget(wing.Id);
				_m.SyncHudForTest();
				string info = hud.TestTargetInfo;
				var shown = _m.TestView.ShipPosition(wing.Id);
				float viewD = shown is { } sp ? new Vector2(sp.X - _m.Player.Position.X, sp.Z - _m.Player.Position.Z).Length() : 0f;
				Check(viewD > 3f && info.StartsWith("Entfernung") && !info.StartsWith("Entfernung 0 m") && !info.StartsWith("Entfernung 1 m"),
					$"Begleiter-Zielkarte zeigt die dargestellte Entfernung ({info}, Ansicht {viewD:0} m)");
				_m.TestSetShipTarget(default);
				// Hinweisfläche: nach einem dreizeiligen Hinweis ist die Fläche eines kurzen wieder einzeilig
				_m.HudRef.Toast("Ein sehr langer Hinweis, der über mehrere Zeilen umbricht, damit die dunkle Fläche hoch wird — noch ein paar Wörter mehr, damit es sicher drei Zeilen werden.", 3f);
				return false;
			}
			case 62:
				_m.HudRef.Toast("Kurz", 3f);
				return false;
			case 64:
			{
				var tr = _m.HudRef.ToastRect;
				Check(tr is { } r && r.Size.Y < 48f, $"Hinweisfläche folgt dem kurzen Text ({tr?.Size.Y:0} px hoch)");
				// „Nächster Gegner“ räumt eine scharfe Tor-Wahl ab
				var me = u.Ships[u.PlayerShip];
				var gate = u.Sectors[u.PlayerSector].Gates.Select(id => u.Gates[id]).OrderBy(g => g.Id.Value).First(g => GateCombat.TypeOf(u, g) is not null);
				_m.TestTeleportTo(gate.Position + new Vec2(0f, 40f));
				_m.TestFace(gate.Position);
				_m.TestSelectGate(gate.Id);
				_m.TestArmGate(gate.Id);
				Check(_m.TestAtGate, "Torangriff scharf");
				var raider = u.ShipsById().First(s => ThreatMap.IsRaider(s) && !u.Fights.IsEngaged(s.Id));
				if (raider.Transit is not null) u.FinishTransit(raider);
				if (raider.Sector != u.PlayerSector) u.MoveShip(raider, u.PlayerSector);
				raider.Position = gate.Position + new Vec2(30f, 60f); raider.DockedAt = default; raider.Order = null; raider.NextThinkTick = u.Tick + 100000;
				_m.TestNextEnemy();
				_m.SyncHudForTest();
				Check(_m.TestFight.Target == raider.Id && !_m.TestAtGate && !hud.TestGateAttackVisible,
					$"„Nächster Gegner“: Ziel Kaperer, Torangriff und „Tor angreifen“ weg ({_m.TestFight.Target.Value}, {_m.TestAtGate}, {hud.TestGateAttackVisible})");
				_m.TestSetShipTarget(default);
				_m.TestFight.AutoFire = false;
				// Vorwarnung mit Countdown
				_r21Pending = new PendingEncounter { Kind = EncounterKind.Intercept, Target = me.Id, Sector = me.Sector, DueTick = u.Tick + u.Rules.PlayerWarningTicks };
				u.PendingEncounters.Add(_r21Pending);
				u.Events.Publish(new EncounterWarning(EncounterKind.Intercept, me.Sector, default, me.Id, _r21Pending.DueTick, "Prüfwarnung: verlasse den Bereich (15 s), sonst …"));
				_r21Warn = _m.HudRef.TestAlert;
				Check(_r21Warn.Contains("(15 s)"), $"Vorwarnung zeigt 15 s ({_r21Warn})");
				return false;
			}
			case 400:   // ≈ 5,6 s später
			{
				_m.SyncHudForTest();
				string now = _m.HudRef.TestAlert;
				int left = WarningCountdown.SecondsLeft(_r21Pending!.DueTick, u.Tick);
				Check(now.Contains($"({left} s)") && left < 15, $"Vorwarnung zählt herunter ({now}, Rest {left} s)");
				u.PendingEncounters.Remove(_r21Pending);
				_m.HudRef.HideAlert();
				// Besitz-Zeile und Draufsicht-Legende
				var me = u.Ships[u.PlayerShip];
				string row = Ui.Screens.AssetsScreen.RowDetail(ThemeDB.FallbackFont, u, me, $"{u.Sectors[me.Sector].Name} · ({me.Position.X:0}/{me.Position.Y:0})");
				Check(ThemeDB.FallbackFont.GetStringSize(row, HorizontalAlignment.Left, -1, 13).X <= 310f && row.Contains("Wert"), $"Besitz-Zeile passt ({row})");
				// Runde 22 (Handy-Test k8): Fregatte nicht „≈ 0 Cr“, Begleiter mit Folgebefehl „im Verband“ statt „geparkt“
				Check(!row.Contains("≈ 0 Cr") && SpaceSim.Core.Trading.SectorEconomy.PlayerShipValue(u, me) > 1000f, $"Besitz-Zeile zeigt den Schiffswert ({row})");
				var wingR22 = u.ShipsById().First(s => s.IsPlayer && s.Id != me.Id);
				string st22 = Ui.Screens.AssetsScreen.StateText(wingR22, false, false);
				Check(wingR22.DockedAt.Value != 0 ? st22 == "angedockt" : st22.StartsWith("im Verband"), $"Begleiter-Zustand ({st22}, Befehl {wingR22.Command?.GetType().Name})");
				_m.TestOpenCommand();
				_m.TestOpenCcArea("sector");
				return false;
			}
			case 404:
			{
				var (bottom, legend) = _m.HudRef.CcSector.TestLegendGap;
				Check(bottom <= legend, $"Draufsicht: Kreisrand ({bottom:0}) über der Legende ({legend:0})");
				_m.TestCloseCommand();
				return true;
			}
		}
		return false;
	}
}
