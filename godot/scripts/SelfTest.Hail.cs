using System.Linq;
using Godot;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 156 (Funk Stufe 1): ein Kaperer kündigt einen Überfall an; der Kaperer wird Ziel, die Scanliste öffnet sich, ein echter Tipp auf „📡 Funk“
/// öffnet das Gespräch; „Hinhalten“ verlängert die Frist (einmal), „Schutzgeld zahlen“ bucht von der Kasse und beendet die Warnung; „Funk aus“ schließt;
/// Funk mit einer Station: „Lage erfragen“ antwortet; Zurück schließt das Funkfenster.
/// </summary>
public partial class SelfTest
{
	ShipId _hlRaider;
	long _hlDue;
	float _hlCash;

	void TapKit(Ui.Kit.KitButton? b, bool down, string what)
	{
		if (b is null || !b.IsVisibleInTree()) { Fail($"HL: Taste {what} nicht sichtbar"); return; }
		Tap(b.GetGlobalRect().GetCenter(), down);
	}

	bool HailStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 8:
			{
				var me = u.Ships[u.PlayerShip];
				me.Cargo[u.FindGood("food")!.Id] = 30f;
				u.Player.Treasury.Credits = 5000f;
				var raider = u.ShipsById().First(ThreatMap.IsRaider);
				if (raider.Transit is not null) u.FinishTransit(raider);
				raider.Order = null;
				raider.DockedAt = default;   // je nach Weltverlauf liegt der Kaperer angedockt im Versteck — angedockte Schiffe stehen nicht in der Scanliste
				if (raider.Sector != me.Sector) u.MoveShip(raider, me.Sector);
				raider.Position = me.Position + new Vec2(45f, 0f);
				_hlRaider = raider.Id;
				u.PendingEncounters.RemoveAll(x => x.Target == me.Id);
				u.PendingEncounters.Add(new PendingEncounter { Kind = EncounterKind.Raid, Source = raider.Id, Target = me.Id, Sector = me.Sector, DueTick = u.Tick + 4000 });
				Check(_m.TestSelectShip(raider.Id), "HL: Kaperer als Ziel gewählt");
				hud.Flight.SetScanOpen(true);
				return false;
			}
			case 16:
			{
				var row = hud.Flight.Scan.TestRow($"#{_hlRaider.Value}");
				Check(row is not null, $"HL: Kaperer steht in der Scanliste ({hud.Flight.Scan.TestRowCount} Zeilen)");
				Check(hud.Flight.Scan.TestHailButton.State != Ui.Kit.KitButtonState.Disabled, "HL: „📡 Funk“ ist für ein Schiff bereit");
				TapKit(hud.Flight.Scan.TestHailButton, true, "Funk");
				return false;
			}
			case 18: TapKit(hud.Flight.Scan.TestHailButton, false, "Funk"); return false;
			case 22:
			{
				Check(hud.Flight.HailOpen && !hud.Flight.ScanOpen, "HL: echter Tipp auf „📡 Funk“ öffnet das Gespräch, die Scanliste schließt");
				Check(hud.Flight.Hail.TestWho.Contains($"#{_hlRaider.Value}"), $"HL: Gespräch nennt den Kaperer ({hud.Flight.Hail.TestWho})");
				foreach (var t in new[] { "Schutzgeld", "Drohen", "Hinhalten", "Fracht abwerfen" })
					Check(hud.Flight.Hail.TestTopic(t) is not null, $"HL: Thema „{t}“ angeboten");
				_hlDue = u.PendingEncounters.First(x => x.Source == _hlRaider).DueTick;
				TapKit(hud.Flight.Hail.TestTopic("Hinhalten"), true, "Hinhalten");
				return false;
			}
			case 24: TapKit(hud.Flight.Hail.TestTopic("Hinhalten"), false, "Hinhalten"); return false;
			case 28:
			{
				var p = u.PendingEncounters.FirstOrDefault(x => x.Source == _hlRaider);
				Check(p is not null && p.Stalled && p.DueTick > _hlDue, $"HL: „Hinhalten“ verlängert die Frist ({_hlDue} → {p?.DueTick})");
				Check(hud.Flight.Hail.TestReply.Contains("hältst"), $"HL: Antwort sichtbar ({hud.Flight.Hail.TestReply})");
				Check(hud.Flight.Hail.TestTopic("Hinhalten")?.State == Ui.Kit.KitButtonState.Disabled, "HL: zweites Hinhalten gesperrt");
				_hlCash = u.Player.Treasury.Credits;
				TapKit(hud.Flight.Hail.TestTopic("Schutzgeld"), true, "Schutzgeld");
				return false;
			}
			case 30: TapKit(hud.Flight.Hail.TestTopic("Schutzgeld"), false, "Schutzgeld"); return false;
			case 34:
			{
				Check(!u.PendingEncounters.Any(x => x.Source == _hlRaider), "HL: Schutzgeld beendet die Warnung");
				Check(u.Player.Treasury.Credits < _hlCash, $"HL: Schutzgeld von der Kasse gebucht ({_hlCash:0} → {u.Player.Treasury.Credits:0})");
				TapKit(Buttons(hud.Flight.Hail).FirstOrDefault(b => b.Text == "Funk aus"), true, "Funk aus");
				return false;
			}
			case 36: TapKit(Buttons(hud.Flight.Hail).FirstOrDefault(b => b.Text == "Funk aus"), false, "Funk aus"); return false;
			case 40:
			{
				Check(!hud.Flight.HailOpen, "HL: „Funk aus“ schließt das Gespräch");
				var me = u.Ships[u.PlayerShip];
				var st = u.StationsById().Where(s => s.Sector == me.Sector && s.Dockable).OrderBy(s => Vec2.Distance(s.Position, me.Position)).First();
				_m.TestOpenHail(HailTarget.OfStation(st.Id));
				return false;
			}
			case 44: TapKit(hud.Flight.Hail.TestTopic("Lage erfragen"), true, "Lage erfragen"); return false;
			case 46: TapKit(hud.Flight.Hail.TestTopic("Lage erfragen"), false, "Lage erfragen"); return false;
			case 50:
			{
				Check(hud.Flight.Hail.TestReply.Contains("Sicherheit"), $"HL: Station antwortet mit der Lage ({hud.Flight.Hail.TestReply})");
				Check(hud.Flight.Hail.TestTopic("Andockerlaubnis") is not null, "HL: Station bietet „Andockerlaubnis“");
				string back = _m.HandleBack();
				Check(back == "funk" && !hud.Flight.HailOpen, $"HL: Zurück schließt das Funkfenster ({back})");
				return true;
			}
		}
		return false;
	}
}
