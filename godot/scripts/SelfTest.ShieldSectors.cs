using Godot;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 171 (Richtungsschilde): Falke im Flug — Taste „Schild“ unter ⚡ und kleine Raute in der Schildzeile sichtbar, nichts überlappt; echte
/// Tipps schalten die Ausrichtung ausgeglichen → vorne → hinten → ausgeglichen (Core-Zustand und Tastenzeile folgen). Ein Treffer aus Richtung des Hecks
/// (Richtung aus den Positionen, <see cref="ShieldSectors.SectorFrom"/>) leert nur den hinteren Sektor; beide Rauten zeigen es, der Rest des Schilds steht.
/// </summary>
public partial class SelfTest
{
	Vector2 _ssAt;
	int _ssStep;

	bool ShieldSectorStage(int f)
	{
		var u = _m.Session.U;
		var fl = _m.HudRef.Flight;
		if (f == 1) { _m.TestMenu("new:fighter"); return false; }
		if (f == 8)
		{
			if (_m.TestDocked) _m.TestUndock();
			u.Rules.PlayerRaids = false;
			return false;
		}
		if (f == 30)
		{
			var me = u.Ships[u.PlayerShip];
			Check(u.Rules.ShieldSectors, "Richtungsschilde: in der Standardwelt an");
			Check(me.Stats.Shield > 0f, "Richtungsschilde: Falke hat einen Schild");
			Check(fl.TestShieldFocusVisible, "Richtungsschilde: Taste „Schild“ sichtbar");
			Check(fl.TestShieldRowDiamondVisible, "Richtungsschilde: Raute in der Schildzeile sichtbar");
			Check(fl.TestShieldFocusRect.Size.X >= 56f && fl.TestShieldFocusRect.Size.Y >= 56f, $"Richtungsschilde: Taste groß genug ({fl.TestShieldFocusRect.Size})");
			Check(fl.TestShieldRowDiamondRect.End.X <= fl.TestStatusRect.End.X + 0.5f, $"Richtungsschilde: Raute im Statusfeld ({fl.TestShieldRowDiamondRect} in {fl.TestStatusRect})");
			Check(fl.Layout is { Overflow: 0 }, $"Richtungsschilde: Anordnung ohne Überlappung ({fl.Layout?.Overflow})");
			Check(!fl.TestShieldFocusRect.Intersects(fl.TestPowerRect), "Richtungsschilde: Taste liegt nicht auf ⚡");
			Check(me.Systems.Focus == ShieldFocus.Balanced && fl.TestShieldFocusSub == "ausgegl.", $"Richtungsschilde: Start ausgeglichen ({me.Systems.Focus}, „{fl.TestShieldFocusSub}“)");
			_ssStep = 0;
			return false;
		}
		// drei echte Tipps: vorne, hinten, ausgeglichen
		var want = new[] { ShieldFocus.Front, ShieldFocus.Back, ShieldFocus.Balanced };
		var subs = new[] { "vorne", "hinten", "ausgegl." };
		if (_ssStep < 3)
		{
			int t = f - 40 - _ssStep * 20;
			if (t == 0) { _ssAt = fl.TestShieldFocusRect.GetCenter(); Tap(_ssAt, true); }
			else if (t == 3) Tap(_ssAt, false);
			else if (t == 10)
			{
				var me = u.Ships[u.PlayerShip];
				Check(me.Systems.Focus == want[_ssStep], $"Richtungsschilde: Tipp {_ssStep + 1} stellt {want[_ssStep]} ein ({me.Systems.Focus})");
				Check(fl.TestShieldFocusSub == subs[_ssStep], $"Richtungsschilde: Tastenzeile „{subs[_ssStep]}“ ({fl.TestShieldFocusSub})");
				int focus = want[_ssStep] == ShieldFocus.Balanced ? -1 : (int)want[_ssStep] - 1;
				Check(fl.TestShieldDiamondFocus == focus, $"Richtungsschilde: Raute markiert die Ausrichtung ({fl.TestShieldDiamondFocus})");
				_ssStep++;
			}
			return false;
		}
		if (_ssStep == 3)
		{
			// ein Treffer von hinten: Schütze hinter dem Heck (Gegenrichtung des Bugs)
			var me = u.Ships[u.PlayerShip];
			float h = _m.TestHeadingRad;
			var bow = new Vec2(-Mathf.Sin(h), -Mathf.Cos(h));
			int sector = ShieldSectors.SectorFrom(me.Position, bow, me.Position - bow * 40f);
			Check(sector == ShieldSectors.Back, $"Richtungsschilde: Schütze hinter dem Schiff trifft den hinteren Sektor ({sector})");
			float before = me.ShieldNow;
			var hit = Hit.Damage(1000f, DamageType.Energy, new TargetProfile(ShieldSectors.Facing(u, me, sector), me.Stats.Armor, 0f, 1f), 1f, u.Rules);
			me.Hull = me.MaxHull;
			Hit.Apply(u, me, hit, me.MaxHull * 0.5f, sector);
			Check(ShieldSectors.ChargeOf(u, me, ShieldSectors.Back) < 0.01f, "Richtungsschilde: hinterer Sektor leer");
			Check(me.ShieldNow > before * 0.7f, $"Richtungsschilde: die anderen Sektoren stehen ({before:0} → {me.ShieldNow:0})");
			Check(me.Hull < me.MaxHull, "Richtungsschilde: der Rest ging durch auf die Hülle");
			_ssStep = 4; _ssAt = new Vector2(f, 0);
			return false;
		}
		if (_ssStep == 4 && f >= (int)_ssAt.X + 6)
		{
			Check(fl.TestShieldDiamondFill(ShieldSectors.Back) < 0.1f && fl.TestShieldDiamondFill(ShieldSectors.Front) > 0.9f,
				$"Richtungsschilde: Raute zeigt hinten leer, vorne voll ({fl.TestShieldDiamondFill(ShieldSectors.Back):0.00}/{fl.TestShieldDiamondFill(ShieldSectors.Front):0.00})");
			var me = u.Ships[u.PlayerShip];
			me.Hull = me.MaxHull; me.Systems.ShieldMissing = 0f;
			ShieldSectors.Sync(me.Systems, me.Stats, u.Rules);
			Log("Richtungsschilde: drei Tipps, Treffer von hinten, Rauten geprüft");
			return true;
		}
		if (f > 600) { Fail($"Richtungsschilde: Ablauf nicht beendet (Schritt {_ssStep})"); return true; }
		return false;
	}
}
