using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 150 (Ausbau Punkt 9, docs/POLITIK.md „Firmensitz und Büro“): neues Spiel (Händler), reich gemacht, in Nebelmark (herrenlos) über
/// „Meine Fraktion“ → „Firmensitz gründen“ mit zwei echten Tipps (KitConfirm) → Sitz gehört dir, Kasse um die Kosten kleiner, Befehlsstufe höher, Fehden frei;
/// dann in Alpha (Sektor der Union, Zustimmung) „Büro gründen“ mit zwei Tipps → Büro; Andocken am Büro: Hangar und Aufträge, Stationsinfo-Zeile;
/// Fraktionen-Detail der eigenen Fraktion nennt Firmensitz und Büro.
/// </summary>
public partial class SelfTest
{
	Vector2 _fsAt;
	float _fsWallet;
	ActorSizeClass _fsClass;
	StationId _fsOffice;

	bool TapTwice(int f, int start, string label, ref Vector2 at)
	{
		var hud = _m.HudRef;
		int k = f - start;
		if (k is 0 or 2 or 4)
		{
			var b = R45Button(hud.CcMyFaction, label);
			if (b is not null) HqVisible(b);
			return false;
		}
		if (k == 8)
		{
			var b = R45Button(hud.CcMyFaction, label);
			if (b is null || HqVisible(b) is not { } p) { Fail($"FS: „{label}“ fehlt oder nicht sichtbar ({hud.CcMyFaction.TestFirmSeatLine} | {hud.CcMyFaction.TestOfficeLine})"); return false; }
			at = p;
			Tap(at, true);
		}
		if (k == 10) Tap(at, false);
		if (k == 14)
		{
			var b = R45Button(hud.CcMyFaction, "Wirklich?");
			if (b is null || HqVisible(b) is not { } p) { Fail($"FS: bestätigende Taste für „{label}“ nicht sichtbar"); return false; }
			at = p;
			Tap(at, true);
		}
		if (k == 16) Tap(at, false);
		return false;
	}

	bool FirmSeatStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		var neb = u.Sectors.Values.First(s => s.Name.Contains("Nebelmark")).Id;
		var alpha = u.Sectors.Values.First(s => s.Name.Contains("Alpha")).Id;
		var cands = new[] { new Vec2(0, 0), new(200, 200), new(-200, 200), new(200, -200), new(-200, -200), new(400, 0), new(-400, 0), new(0, 400), new(0, -400), new(300, 300), new(-300, -300) };
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
				Check(u.Rules.PlayerFirmSeat && u.Rules.PlayerOffices && u.Rules.FirmSeats && u.Rules.Offices, "FS: Firmensitz und Büros in der Standardwelt an");
				u.Player.Treasury.Credits = 400_000f;   // Befehlsstufe Firma (Geld ÷ 50 000 = 8): der Sitz hebt sie auf Konzern
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();
				_m.TestEnterSector(neb);
				return false;
			case 12:
				foreach (var c in cands) { _m.TestTeleportTo(c); if (PlayerSeatsOffices.SeatIssue(u, neb) is null) break; }
				Check(PlayerSeatsOffices.SeatIssue(u, neb) is null, $"FS: Firmensitz in Nebelmark möglich ({PlayerSeatsOffices.SeatIssue(u, neb)})");
				_fsWallet = u.Player.Treasury.Credits;
				_fsClass = Commands.CapacityClass(u, u.PlayerActor);
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.MyFaction);
				hud.Screens.Current?.Refresh();
				return false;
			case >= 16 and <= 32: return TapTwice(f, 16, "Firmensitz gründen", ref _fsAt);
			case 36:
			{
				hud.Screens.Current?.Refresh();
				var seat = PlayerSeatsOffices.Seat(u);
				Check(seat is not null && seat.Sector == neb, $"FS: zwei Tipps gründen den Firmensitz in Nebelmark ({hud.CcMyFaction.TestPolicyMessage})");
				float cost = FirmSeats.Cost(u, FirmSeats.SeatType(u)!);
				Check(Mathf.Abs(_fsWallet - u.Player.Treasury.Credits - cost) < 50f, $"FS: Kasse um die Kosten kleiner ({_fsWallet:0} → {u.Player.Treasury.Credits:0}, Kosten {cost:0})");
				Check(Commands.CapacityClass(u, u.PlayerActor) > _fsClass, $"FS: Befehlsstufe höher ({_fsClass} → {Commands.CapacityClass(u, u.PlayerActor)})");
				Check(PlayerSeatsOffices.FeudUnlocked(u), "FS: Fehden freigeschaltet");
				Check(seat is not null && hud.CcMyFaction.TestFirmSeatLine.Contains(seat.Name), $"FS: Karte zeigt den Firmensitz ({hud.CcMyFaction.TestFirmSeatLine})");
				Check(Buttons(hud.CcMyFaction).Any(b => b.Text == "Firmensitz aufgeben"), "FS: Taste „Firmensitz aufgeben“");
				Check(hud.CcMyFaction.TestAbilityOverflow.Length == 0, $"FS: Fähigkeitsnotizen passen ({hud.CcMyFaction.TestAbilityOverflow})");
				_m.TestCloseCommand();
				_m.TestEnterSector(alpha);
				return false;
			}
			case 42:
				foreach (var c in cands) { _m.TestTeleportTo(c); if (PlayerSeatsOffices.OfficeIssue(u, alpha) is null) break; }
				Check(PlayerSeatsOffices.OfficeIssue(u, alpha) is null, $"FS: Büro in Alpha möglich ({PlayerSeatsOffices.OfficeIssue(u, alpha)})");
				_fsWallet = u.Player.Treasury.Credits;
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.MyFaction);
				hud.Screens.Current?.Refresh();
				return false;
			case >= 46 and <= 62: return TapTwice(f, 46, "Büro gründen", ref _fsAt);
			case 66:
			{
				hud.Screens.Current?.Refresh();
				var office = Offices.Of(u, u.PlayerActor).FirstOrDefault(o => o.Sector == alpha);
				Check(office is not null, $"FS: zwei Tipps gründen das Büro in Alpha ({hud.CcMyFaction.TestPolicyMessage})");
				if (office is null) return false;
				float cost = Offices.Cost(u, Offices.OfficeType(u)!);
				Check(Mathf.Abs(_fsWallet - u.Player.Treasury.Credits - cost) < 50f, $"FS: Kasse um die Bürokosten kleiner ({_fsWallet:0} → {u.Player.Treasury.Credits:0})");
				Check(u.Sectors[alpha].Owner != u.PlayerFaction, "FS: Büro verschiebt den Sektorbesitz nicht");
				_fsOffice = office.Id;
				_m.TestCloseCommand();
				_m.TestDockAt(_fsOffice);
				return false;
			}
			case 72:
			{
				var areas = _m.TestDockAreas();
				Check(areas.Any(a => a.Id == Ui.Screens.DockAreas.Hangar && a.Available), "FS: Büro hat einen Hangar");
				Check(areas.Any(a => a.Id == Ui.Screens.DockAreas.Missions && a.Available), "FS: Büro hat das Auftragsbrett");
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Info);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 76:
				Check(hud.DockInfo.TestSeatOffice.StartsWith("Büro von"), $"FS: Stationsinfo-Zeile („{hud.DockInfo.TestSeatOffice}“)");
				_m.TestUndock();
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Factions);
				hud.CcFactions.TestSelect(u.PlayerActor);
				hud.Screens.Current?.Refresh();
				return false;
			case 82:
				Check(hud.CcFactions.TestFirmSeat.StartsWith("Firmensitz:") && hud.CcFactions.TestFirmSeat.Contains("1 Büro"), $"FS: Fraktionen-Detail nennt Sitz und Büro ({hud.CcFactions.TestFirmSeat})");
				_m.TestCloseCommand();
				return true;
		}
		return false;
	}
}
