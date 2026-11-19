using System.Linq;
using Godot;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 145 (Fusionsbrennstoff und Tankanzeige, SCHIFFSSLOTS.md 14): neues Spiel „Kriegstest“ (Bollwerk mit Fusionstriebwerk). Flug-HUD zeigt die
/// Art „Fusion“, Inhalt/Tank, Reichweite und Sprünge; fast leer wird die Zeile rot. Andocken an einer Treibstoffraffinerie: der Tankbereich nennt
/// Fusionsbrennstoff und die nächste Anreicherungsanlage (keine falsche Art im Angebot); an der Anreicherungsanlage tankt ein echter Tipp auf „Ausführen“
/// den Fusionsbrennstoff; Schiffsspalte zeigt dieselbe Tankzeile.
/// </summary>
public partial class SelfTest
{
	float _tankBefore;

	bool TankStage(int f)
	{
		var u = _m.Session.U;
		var fl = _m.HudRef.Flight;
		var me = u.Ships[u.PlayerShip];
		var ff = u.FindGood("fusion_fuel");
		switch (f)
		{
			case 1: _m.TestMenu("new:war"); return false;
			case 8:
			{
				if (ff is null || !u.Rules.FuelActive) { Fail("TA: kein Fusionsbrennstoff oder Treibstoff aus"); return true; }
				u.Rules.PlayerRaids = false;
				Check(fl.TestFuelName == "Fusion", $"TA: HUD-Zeile heißt „Fusion“ ({fl.TestFuelName})");
				Check(fl.TestFuelInfo.Contains("Spr.") && fl.TestFuelInfo.Contains("/"), $"TA: HUD-Infozeile mit Inhalt/Tank, Reichweite, Sprüngen ({fl.TestFuelInfo})");
				Check(!fl.TestFuelLow, "TA: voller Tank nicht rot");
				me.Fuel[ff.Id] = 0.1f * me.Stats.FuelCapacity;
				return false;
			}
			case 14:
			{
				Check(fl.TestFuelLow, $"TA: fast leer wird rot ({fl.TestFuelInfo})");
				var refinery = u.StationsById().First(s => s.TypeKey == "fuel_refinery");
				_m.TestDockAt(refinery.Id);
				return false;
			}
			case 40:
			{
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Repair);
				return false;
			}
			case 46:
			{
				var rs = _m.HudRef.DockRepair;
				Check(_m.TestScreenOpen("dock.repair"), "TA: Bereich Reparatur & Tanken an der Raffinerie");
				Check(rs.TestTankLine.Contains("Fusionsbrennstoff"), $"TA: Tankzeile nennt Fusionsbrennstoff ({rs.TestTankLine})");
				Check(rs.TestTankMissing.Contains("Anreicherungsanlage"), $"TA: Hinweis auf die nächste Anreicherungsanlage ({rs.TestTankMissing})");
				var plant = u.StationsById().First(s => s.TypeKey == "enrichment_plant");
				_m.TestUndock();
				_m.TestDockAt(plant.Id);
				return false;
			}
			case 80: _m.TestOpenDockArea(Ui.Screens.DockAreas.Repair); return false;
			case 86:
			{
				var rs = _m.HudRef.DockRepair;
				Check(rs.TestRowCount >= 1, $"TA: Anreicherungsanlage bietet Tanken an ({rs.TestRowCount} Zeilen)");
				Check(Ui.Screens.DockAreaScreen.TestTankLine.Contains("Spr."), $"TA: Schiffsspalte zeigt die Tankzeile ({Ui.Screens.DockAreaScreen.TestTankLine})");
				u.Player.Treasury.Credits = 1_000_000f;
				_tankBefore = me.FuelAmount(ff!.Id);
				PressButton(rs, "Ausführen", true);
				return false;
			}
			case 88: PressButton(_m.HudRef.DockRepair, "Ausführen", false); return false;
			case 96:
			{
				Check(me.FuelAmount(ff!.Id) > _tankBefore + 1f, $"TA: per Tipp getankt ({_tankBefore:0} → {me.FuelAmount(ff.Id):0})");
				Check(me.FuelAmount(u.FindGood("propellant")!.Id) <= 0f, "TA: kein Treibstoff im Fusionstank");
				_m.TestUndock();
				return true;
			}
		}
		return false;
	}
}
