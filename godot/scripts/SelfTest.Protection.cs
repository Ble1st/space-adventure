using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 154 (Ausbau Punkt 3, docs/POLITIK.md „Schutzvertrag“): neues Spiel (Händler), Comms → Diplomatie → Union: „Schutzvertrag schließen“
/// mit zwei echten Tipps → Vertrag läuft (Gebühr von der Kasse an die Fraktionskasse, Detailzeile „Schutz“); „Schutzvertrag kündigen“ mit einem Tipp → vorbei.
/// </summary>
public partial class SelfTest
{
	float _pcWallet, _pcTreasury;
	ulong _pcArmedAt;
	int _pcConfirm = -1, _pcCancel = -1;
	FactionId _pcUnion;

	bool ProtectionStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		if (f >= 30) return ProtectionLater(f);
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
				Check(u.Rules.ProtectionContracts && u.Rules.PursuitCrossBorder && u.Rules.Convoys, "PC: Schutzvertrag, Grenzverfolgung und Konvois in der Standardwelt an");
				u.Player.Treasury.Credits = 5000f;
				_pcUnion = u.NpcFactionIds()[0];
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();   // Vorstufe kann angedockt enden (Dockebene über dem Kommandozentrum)
				return false;
			case 8:
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Comms);
				hud.Screens.Current?.Refresh();
				return false;
			case 10: PressButton(hud.CcComms, "Diplomatie", true); return false;
			case 12: PressButton(hud.CcComms, "Diplomatie", false); hud.Screens.Current?.Refresh(); return false;
			case 14:
				hud.CcComms.TestSelectFaction(_pcUnion); hud.Screens.Current?.Refresh();
				_pcWallet = u.Player.Treasury.Credits; _pcTreasury = u.Actors[_pcUnion].Treasury.Credits;
				return false;
			case >= 18 and <= 24: return FdTap(hud, f - 18, "Schutzvertrag schließen", "erster Tipp");
			case 28:
				_pcArmedAt = Time.GetTicksMsec();
				Check(Protection.Of(u, _pcUnion) is null, "PC: erster Tipp auf „Schutzvertrag schließen“ führt noch nicht aus (zwei Tipps)");
				return false;
		}
		return false;
	}

	/// <summary>Zweiter Tipp nach der Entprellung (250 ms Echtzeit) innerhalb des 3-s-Fensters — unabhängig von der Bildrate; dann prüfen und kündigen.</summary>
	bool ProtectionLater(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		if (_pcConfirm < 0)
		{
			if (Time.GetTicksMsec() - _pcArmedAt >= 400) _pcConfirm = f;
			if (f > 600) { Fail("PC: Zeitüberschreitung"); return true; }
			return false;
		}
		int k = f - _pcConfirm;
		if (k <= 6) { FdTap(hud, k, "Wirklich?", "Bestätigung"); return false; }
		if (k == 10)
		{
			hud.Screens.Current?.Refresh();
			float fee = u.Rules.ProtectionFeePerDay;
			Check(Protection.Of(u, _pcUnion) is not null, $"PC: zwei Tipps schließen den Vertrag ({hud.CcComms.TestMessage})");
			Check(Mathf.Abs(u.Player.Treasury.Credits - (_pcWallet - fee)) < 1f, $"PC: Tagesgebühr bezahlt ({_pcWallet:0} → {u.Player.Treasury.Credits:0})");
			Check(u.Actors[_pcUnion].Treasury.Credits >= _pcTreasury + fee - 1f, "PC: Gebühr in der Fraktionskasse");
			Check(hud.CcComms.TestProtectionRow.StartsWith("läuft seit"), $"PC: Detailzeile „Schutz“ ({hud.CcComms.TestProtectionRow})");
			_pcArmedAt = Time.GetTicksMsec();
			return false;
		}
		if (_pcCancel < 0) { if (k > 10 && Time.GetTicksMsec() - _pcArmedAt >= 400) _pcCancel = f; return false; }
		int c = f - _pcCancel;
		if (c <= 6) { FdTap(hud, c, "Schutzvertrag kündigen", "kündigen"); return false; }
		if (c == 10)
		{
			hud.Screens.Current?.Refresh();
			Check(Protection.Of(u, _pcUnion) is null, $"PC: ein Tipp kündigt den Vertrag ({hud.CcComms.TestMessage})");
			_pcConfirm = -1; _pcCancel = -1;
			_m.TestCloseCommand();
			return true;
		}
		return false;
	}
}
