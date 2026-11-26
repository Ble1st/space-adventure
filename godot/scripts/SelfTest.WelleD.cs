using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 175 (Welle D): neues Spiel (Händler), ein eigener Tanker (Zisterne, Treibstoff im Laderaum) und ein Bollwerk mit leerem Tank an der
/// nächsten Station; Kommando → Flotte, echte Tipps auf den Tanker und auf „Versorgen“ → der Tanker führt den Befehl, das Bollwerk wird aus seiner Fracht
/// betankt; „Meine Fraktion“ zeigt die Zeile „Treibstoff Flotte“ (Kosten je Stunde) ohne Überlauf.
/// </summary>
public partial class SelfTest
{
	ShipId _wdT, _wdB;
	GoodId _wdFuel;

	bool WelleDStage(int f)
	{
		var u = _m.Session.U;
		Node cc = (Node?)_m.HudRef.Screens.Current?.Node ?? _m.HudRef;
		var fs = _m.HudRef.CcFleet;
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 4:
			{
				_m.Session.RunTicks(2);
				if (_m.TestDocked) _m.TestUndock();
				Check(u.Rules.FleetSupply && u.Rules.PlayerStationFunding && u.Rules.PlayerMinerFuelCost, "WD: Welle-D-Regeln in der Standardwelt an");
				u.WalletOf(u.Ships[u.PlayerShip]).Credits = 200000f;
				var me = u.Ships[u.PlayerShip];
				var st = u.StationsById().Where(s => s.Sector == me.Sector && !s.IsBuildSite && Docking.Allows(u, s, me)).OrderBy(s => Vec2.Distance(s.Position, me.Position)).First();
				var tanker = FoOwn(u, "cistern", st);
				var bastion = FoOwn(u, "bastion", st);
				_wdT = tanker.Id; _wdB = bastion.Id;
				_wdFuel = Refuel.ActiveFuel(bastion)!.Value;
				bastion.Fuel.Clear();
				tanker.Cargo[_wdFuel] = 150f;
				_m.TestOpenCommand();
				return false;
			}
			case 6: PressButton(_m.HudRef.Command, "Kommando", true); return false;
			case 7: PressButton(_m.HudRef.Command, "Kommando", false); return false;
			case 9: PressButton(cc, "Flotte", true); return false;
			case 10: PressButton(cc, "Flotte", false); return false;
			case 12: Check(_m.TestScreenOpen("cc.fleet"), "WD: Kommando → Flotte offen"); return false;
		}
		if (f >= 14 && f < 18) { FoTap(f, 14, () => FoRowButton(cc, FoRow(u, _wdT)), "Tanker"); return false; }
		if (f >= 19 && f < 23) { FoTap(f, 19, () => fs.TestCommandButton(CommandKinds.Supply), "Versorgen"); return false; }
		if (f == 24)
		{
			Check(u.Ships[_wdT].Command is SupplyCommand, $"WD: „Versorgen“ per Tipp gegeben ({fs.TestMessage})");
			Check(FleetOverflow(cc).Length == 0, $"WD: keine Taste läuft über ({FleetOverflow(cc)})");
			_m.Session.RunTicks(60);
			return false;
		}
		if (f == 26)
		{
			var b = u.Ships[_wdB];
			Check(b.FuelAmount(_wdFuel) > 1f, $"WD: Bollwerk aus der Tankerfracht betankt ({b.FuelAmount(_wdFuel):0.#}/{b.Stats.FuelCapacity:0}, Tanker {u.Ships[_wdT].Cargo.GetValueOrDefault(_wdFuel):0.#})");
			_m.TestOpenCcArea(Ui.Screens.CommandAreas.MyFaction);
			_m.HudRef.Screens.Current?.Refresh();
			return false;
		}
		if (f == 30)
		{
			var mf = _m.HudRef.CcMyFaction;
			Check(mf.TestFuelText.Length > 0 && (mf.TestFuelText.StartsWith("keine") || mf.TestFuelText.Contains("Cr/h")), $"WD: Zeile „Treibstoff Flotte“ ({mf.TestFuelText})");
			_m.TestCloseCommand();
			return true;
		}
		return false;
	}
}
