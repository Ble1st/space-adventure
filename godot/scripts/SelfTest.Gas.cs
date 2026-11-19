using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 144 (Gasernte und Nebel, SCHIFFSSLOTS.md „Gasernte und Nebel“): neues Spiel (Händler, Nomade), Gassammler in den Werkzeugslot, in den
/// Nebelsektor Borealis an die Mitte des Gasfelds. Prüft: Nebel sichtbar (Tiefennebel an, Gasfeld-Wolken gebaut), Taste „Gas“ in den Gruppen; echter Tipp
/// schaltet den Sammler an, Gas landet im Laderaum (Core-Regel), echter Tipp schaltet ihn aus (es kommt nichts mehr dazu); außerhalb des Felds zeigt die
/// Taste „kein Gasfeld“; außerhalb des Nebels ist der Tiefennebel aus; kein abgeschnittener Text.
/// </summary>
public partial class SelfTest
{
	Vector2 _gasAt;
	float _gasBefore;

	bool GasStage(int f)
	{
		var u = _m.Session.U;
		var fl = _m.HudRef.Flight;
		var me = u.Ships[u.PlayerShip];
		var gas = GasHarvest.GasGood(u);
		int Group(string label) => fl.TestGroupLabels.IndexOf(label);
		float Held() => gas is null ? 0f : me.Cargo.GetValueOrDefault(gas.Id);
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
			{
				if (gas is null) { Fail("GA: keine Gasware in der Welt"); return true; }
				_m.TestUndock();
				u.Rules.PlayerRaids = false;
				int tool = me.Slots.FindIndex(s => s.Type == "tool");
				if (tool < 0) { Fail("GA: kein Werkzeugslot"); return true; }
				me.Fit[tool] = "gas_scoop_s";
				me.RecomputeStats(u);
				_m.TestEnterSector(new SectorId(5));
				return false;
			}
			case 12:
			{
				var sec = u.Sectors[u.PlayerSector];
				Check(sec.Nebula && sec.GasFields.Count > 0, $"GA: Borealis ist Nebelsektor mit Gasfeld ({sec.Name})");
				if (sec.GasFields.Count == 0) return true;
				_m.TestTeleportTo(sec.GasFields[0].Position);
				return false;
			}
			case 20:
			{
				var nv = _m.TestNebula;
				Check(nv is not null && nv.FogOn, "GA: Tiefennebel im Nebelsektor an");
				Check(nv is not null && nv.FieldNodes >= 2, $"GA: Gasfeld-Wolken gebaut ({nv?.FieldNodes})");
				int g = Group("Gas");
				Check(g >= 0, $"GA: Taste „Gas“ ({string.Join(", ", fl.TestGroupLabels)})");
				if (g < 0) return true;
				Check(fl.TestGroupOverflow.Length == 0, $"GA: Gruppentasten ohne abgeschnittenen Text ({fl.TestGroupOverflow})");
				_gasAt = fl.TestGroupRect(g).GetCenter();
				_gasBefore = Held();
				Tap(_gasAt, true);
				return false;
			}
			case 22: Tap(_gasAt, false); return false;
			case 90:
			{
				Check(_m.TestGasOn, $"GA: Sammler per Tipp an ({_m.LastGasMessage})");
				Check(Held() > _gasBefore + 0.5f, $"GA: Gas im Laderaum ({_gasBefore:0.0} → {Held():0.0})");
				int g = Group("Gas");
				Check(g >= 0 && fl.TestGroupSub(g) == "sammelt", $"GA: Taste zeigt „sammelt“ ({(g >= 0 ? fl.TestGroupSub(g) : "-")})");
				Check(fl.TestGroupOverflow.Length == 0, $"GA: Text passt beim Sammeln ({fl.TestGroupOverflow})");
				Tap(_gasAt, true);
				return false;
			}
			case 92: Tap(_gasAt, false); return false;
			case 96: _gasBefore = Held(); Check(!_m.TestGasOn, $"GA: Sammler per Tipp aus ({_m.LastGasMessage})"); return false;
			case 130:
			{
				Check(Held() <= _gasBefore + 1e-3f, $"GA: ausgeschaltet kommt nichts dazu ({_gasBefore:0.00} → {Held():0.00})");
				var sec = u.Sectors[u.PlayerSector];
				_m.TestTeleportTo(sec.GasFields[0].Position + new Vec2(sec.GasFields[0].Radius + 80f, 0f));
				Tap(_gasAt, true);
				return false;
			}
			case 132: Tap(_gasAt, false); return false;
			case 140:
			{
				int g = Group("Gas");
				Check(g >= 0 && fl.TestGroupSub(g) == "kein Gasfeld", $"GA: außerhalb „kein Gasfeld“ ({(g >= 0 ? fl.TestGroupSub(g) : "-")})");
				_m.TestEnterSector(new SectorId(1));
				return false;
			}
			case 150:
			{
				Check(_m.TestNebula is { FogOn: false }, "GA: außerhalb des Nebels kein Tiefennebel");
				return true;
			}
		}
		return false;
	}
}
