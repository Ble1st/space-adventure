using System.Linq;
using Godot;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 98 (Runde 19): Startpaket „Ankertest“ verankert ohne Eingabe und baut eine Minute ununterbrochen ab; „Neues Spiel“ setzt eine verstellte Kamera
/// zurück; ein Tipp auf die Toröffnung wählt das Tor auch mit einem Händler direkt daneben, ein Tipp auf den Händler das Schiff (Zielkarte zeigt es); der
/// Warnkasten überdeckt die Beschriftung „RÜCK · max 20 %“ nicht.
/// </summary>
public partial class SelfTest
{
	int _r19Releases; ulong _r19Before;

	bool Round19Stage(int f)
	{
		var u = _m.Session.U;
		switch (f)
		{
			case 1:
				_m.TestSetCam(130f, 20f, 120f);   // verstellte Kamera vor dem neuen Spiel
				return false;
			case 3:
				_m.TestMenu("new:anchor");
				_r19Releases = 0;
				_m.Session.U.Events.Raised += e => { if (e is AnchorReleased ar && ar.Ship == _m.Session.U.PlayerShip) _r19Releases++; };
				return false;
			case 5:
			{
				var (yaw, pitch, dist) = _m.TestCamAngles;
				Check(System.Math.Abs(yaw - OrbitCamera.DefaultYawDeg) < 0.01f && System.Math.Abs(pitch - OrbitCamera.DefaultPitchDeg) < 0.01f
					&& System.Math.Abs(dist - OrbitCamera.DefaultDistance) < 0.01f, $"„Neues Spiel“ setzt die Kamera zurück ({yaw:0}/{pitch:0}/{dist:0})");
				var me = u.Ships[u.PlayerShip];
				Check(me.CargoCapacity >= 500f && !u.Rules.PlayerRaids, $"Ankertest: Laderaum {me.CargoCapacity:0}, keine Überfälle");
				return false;
			}
			case 200:
			{
				var me = u.Ships[u.PlayerShip];
				var rk = u.Sectors[u.PlayerSector].Asteroids.Where(a => a.Minable).OrderBy(a => Vec2.Distance(a.Position, me.Position) - a.Radius).First();
				Check(MiningAnchor.State(u, me) == AnchorState.Held, $"Ankertest: ohne Eingabe verankert ({me.AnchorRock}, {me.AnchorProgress:0.0}, Kontakt {_m.TestContactState}, Kante {Vec2.Distance(rk.Position, me.Position) - rk.Radius:0.0}, Kurs {_m.Player.Heading:0.00} zu {AimAssist.Bearing(me.Position, rk.Position):0.00}, angedockt {me.DockedAt.Value})");
				_r19Before = (ulong)(me.UsedVolume(u) * 10f);
				return false;
			}
			case 3800:   // eine Minute später
			{
				var me = u.Ships[u.PlayerShip];
				Check(MiningAnchor.State(u, me) == AnchorState.Held && _r19Releases == 0, $"Ankertest: eine Minute durchgehend verankert (Abrisse {_r19Releases})");
				Check(me.UsedVolume(u) * 10f > _r19Before + 1000f && me.UsedVolume(u) < me.CargoCapacity, $"Ankertest: Abbau läuft, Laderaum reicht ({me.UsedVolume(u):0}/{me.CargoCapacity:0})");
				// Tor antippen mit einem Händler direkt daneben
				var gate = u.Gates[u.Sectors[u.PlayerSector].Gates.OrderBy(g => g.Value).First()];
				var trader = u.ShipsById().First(s => s.Role == ShipRole.Trader && !s.IsPlayer && !u.Fights.IsEngaged(s.Id));
				if (trader.Transit is not null) u.FinishTransit(trader);
				if (trader.Sector != u.PlayerSector) u.MoveShip(trader, u.PlayerSector);
				trader.Position = gate.Position + new Vec2(7f, 0f); trader.DockedAt = default; trader.Order = null;
				trader.NextThinkTick = u.Tick + 100000;
				_r19Trader = trader.Id; _r19Gate = gate.Id;
				SpaceSim.Core.Mining.MiningAnchor.Release(u, me, u.PlayerSector, "released");
				_m.TestTeleportTo(gate.Position + new Vec2(0f, 60f));
				_m.TestFace(gate.Position);
				return false;
			}
			case 3840:
			{
				var gate = u.Gates[_r19Gate];
				var gc = _m.TestCam.UnprojectPosition(new Vector3(gate.Position.X, 0, gate.Position.Y));
				var picked = _m.TestPickGateAt(gc);
				Check(picked == _r19Gate, $"Tipp auf die Toröffnung wählt das Tor (nicht den Händler daneben) ({picked?.Value})");
				if (_m.TestView.ShipPosition(_r19Trader) is { } tp)
				{
					_m.TestPickGateAt(_m.TestCam.UnprojectPosition(tp));
					_m.SyncHudForTest();
					Check(_m.TestFight.Target == _r19Trader && _m.HudRef.Flight.TestTargetName.Contains($"#{_r19Trader.Value}"),
						$"Tipp genau auf den Händler wählt das Schiff, die Zielkarte zeigt es ({_m.HudRef.Flight.TestTargetName})");
				}
				// Warnkasten und „RÜCK · max 20 %“ überlappen nicht
				_m.HudRef.Alert("Prüfmeldung über dem Regler — zwei Zeilen lang, damit sie die volle Höhe des Warnkastens braucht.", 5f);
				return false;
			}
			case 3845:
				Check(!_m.HudRef.TestAlertRect.Intersects(_m.HudRef.Flight.ReverseLabelRect), $"Warnkasten überdeckt „RÜCK · max 20 %“ nicht ({_m.HudRef.TestAlertRect} / {_m.HudRef.Flight.ReverseLabelRect})");
				_m.HudRef.HideAlert();
				return true;
		}
		return false;
	}
	ShipId _r19Trader; GateId _r19Gate;
}
