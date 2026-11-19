using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 148 (Nachtrag 1c): „Liefern“ mit Verlustschutz-Schalter per Tipp — Händler → „Liefern“ → Schalter „Verlustschutz“ (Standard an) zeigt
/// geschützte Routen, ein Tipp schaltet auf „Verlust erlaubt“, die gewählte Route trägt <see cref="DeliverCommand.AllowLoss"/>; Sammelpunkt-Befehle tragen
/// eine Frist. Keine Taste läuft über.
/// </summary>
public partial class SelfTest
{
	ShipId _wnM, _wnC, _wnD;

	bool WarNavyStage(int f)
	{
		var u = _m.Session.U;
		Node cc = (Node?)_m.HudRef.Screens.Current?.Node ?? _m.HudRef;
		var fs = _m.HudRef.CcFleet;
		switch (f)
		{
			case 1: _m.TestMenu("new:fighter"); return false;
			case 4:
			{
				_m.Session.RunTicks(2);
				if (_m.TestDocked) _m.TestUndock();
				u.WalletOf(u.Ships[u.PlayerShip]).Credits = 200000f;
				var me = u.Ships[u.PlayerShip];
				var yard = u.StationsById().Where(s => s.Sector == me.Sector && !s.IsBuildSite && Docking.Allows(u, s, me)).OrderBy(s => Vec2.Distance(s.Position, me.Position)).First();
				_wnM = FoOwn(u, "mule", yard).Id;
				_wnC = FoOwn(u, "falcon", yard).Id; _wnD = FoOwn(u, "falcon", yard).Id;
				_m.TestOpenCommand();
				return false;
			}
			case 6: PressButton(_m.HudRef.Command, "Kommando", true); return false;
			case 7: PressButton(_m.HudRef.Command, "Kommando", false); return false;
			case 9: PressButton(cc, "Flotte", true); return false;
			case 10: PressButton(cc, "Flotte", false); return false;
			case 12: Check(_m.TestScreenOpen("cc.fleet"), "Nachtrag 1c: Kommando → Flotte offen"); return false;
		}
		// ── Liefern: M → „Liefern“ → Schalter prüfen → umschalten → Route ──
		int s0 = 14;
		if (f >= s0 && f < s0 + 4) { FoTap(f, s0, () => FoRowButton(cc, FoRow(u, _wnM)), "Händler"); return false; }
		if (f >= s0 + 5 && f < s0 + 9) { FoTap(f, s0 + 5, () => fs.TestCommandButton(CommandKinds.Deliver), "Liefern"); return false; }
		if (f == s0 + 10)
		{
			Check(fs.TestDeliverLossButton is { } b && b.Text == "Verlustschutz" && b.IsVisibleInTree(), $"Liefern: Schalter „Verlustschutz“ sichtbar, Standard an ({fs.TestDeliverLossButton?.Text})");
			Check(FleetOverflow(cc).Length == 0, $"Liefern: keine Taste läuft über ({FleetOverflow(cc)})");
			return false;
		}
		if (f >= s0 + 11 && f < s0 + 15) { FoTap(f, s0 + 11, () => fs.TestDeliverLossButton, "Verlustschutz"); return false; }
		if (f == s0 + 16)
		{
			Check(fs.TestDeliverLossButton is { } b && b.Text == "Verlust erlaubt", $"Liefern: ein Tipp schaltet auf „Verlust erlaubt“ ({fs.TestDeliverLossButton?.Text})");
			Check(FleetOverflow(cc).Length == 0, $"Liefern (umgeschaltet): keine Taste läuft über ({FleetOverflow(cc)})");
			return false;
		}
		if (f >= s0 + 17 && f < s0 + 21) { FoTap(f, s0 + 17, () => FoTarget(_ => true), "Lieferroute"); return false; }
		if (f == s0 + 22)
		{
			Check(u.Ships[_wnM].Command is DeliverCommand { AllowLoss: true }, $"Liefern mit erlaubtem Verlust per Tipp ({fs.TestMessage}; {u.Ships[_wnM].Command})");
			return false;
		}
		// ── Sammelpunkt mit Frist: C → „Sammeln“ → D „mit“ → Station ──
		int s1 = s0 + 24;
		if (f >= s1 && f < s1 + 4) { FoTap(f, s1, () => FoRowButton(cc, FoRow(u, _wnC)), "Schiff C"); return false; }
		if (f >= s1 + 5 && f < s1 + 9) { FoTap(f, s1 + 5, () => fs.TestCommandButton(CommandKinds.Rally), "Sammeln"); return false; }
		if (f >= s1 + 10 && f < s1 + 14) { FoTap(f, s1 + 10, () => fs.TestGroupButton(_wnD), "„mit“ D"); return false; }
		if (f >= s1 + 15 && f < s1 + 19) { FoTap(f, s1 + 15, () => FoTarget(_ => true), "Treffpunkt"); return false; }
		if (f == s1 + 20)
		{
			Check(u.Ships[_wnC].Command is RallyCommand rc && rc.Until > u.Tick && rc.Until <= u.Tick + u.Rules.RallyDeadlineTicks,
				$"Sammelpunkt per Tipp trägt die Frist ({u.Ships[_wnC].Command})");
			Check(u.Ships[_wnD].Command is RallyCommand, "Sammelpunkt: D fliegt mit");
			return false;
		}
		return f >= s1 + 22;
	}
}
