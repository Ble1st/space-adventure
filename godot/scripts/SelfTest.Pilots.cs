using System.Linq;
using Godot;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Model;
using SpaceSim.Core.Pilots;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 167 (Piloten): neues Spiel mit dem Jäger; ein Kaperer als Ziel — die Zielkarte und der Funk nennen seinen Kapitän mit Rang; „Politik →
/// Rangliste“ per echtem Tipp auf den Unterreiter: Profilkarte mit Porträt, Name, Rang, Wertung, Platz; nach einem Abschuss steigt die Wertung, der Abschuss
/// steht im Profil und die Beförderung wird gemeldet.
/// </summary>
public partial class SelfTest
{
	ShipId _plRaider;
	string _plRating = "";

	bool PilotsStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		switch (f)
		{
			case 1: _m.TestMenu("new:fighter"); return false;
			case 6:
				if (u.Ships[u.PlayerShip].DockedAt.Value != 0) _m.TestUndock();
				return false;
			case 90:
			{
				var me = u.Ships[u.PlayerShip];
				Check(me.DockedAt.Value == 0, "PL: Jäger abgedockt");
				Check(u.Rules.PilotRanks, "PL: Ränge in der Standardwelt an");
				var raider = u.ShipsById().First(ThreatMap.IsRaider);
				if (raider.Transit is not null) u.FinishTransit(raider);
				raider.Order = null; raider.Command = null; raider.DockedAt = default;
				if (raider.Sector != me.Sector) u.MoveShip(raider, me.Sector);
				raider.Position = me.Position + new Vec2(40f, 0f);
				_plRaider = raider.Id;
				Check(_m.TestSelectShip(raider.Id), "PL: Kaperer als Ziel gewählt");
				return false;
			}
			case 100:
			{
				string cap = hud.Flight.TestTargetCaptain;
				Check(cap.Length > 0 && (cap.Contains(PilotBook.CaptainName(u, _plRaider).Split(' ')[1])), $"PL: Zielkarte nennt den Kapitän ({cap})");
				Check(hud.Flight.TestTargetLineOverflow.Length == 0, $"PL: Zielkarte ohne abgeschnittene Zeile ({hud.Flight.TestTargetLineOverflow})");
				_m.TestOpenHail(new HailTarget(_plRaider, default));
				return false;
			}
			case 106:
			{
				Check(hud.Flight.Hail.TestCaptain.StartsWith("Kpt. "), $"PL: Funk nennt den Kapitän mit Rang ({hud.Flight.Hail.TestCaptain})");
				string back = _m.HandleBack();
				Check(back == "funk", $"PL: Zurück schließt das Funkfenster ({back})");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Stats);
				return false;
			}
			case 112: PressButton(hud.CcStats, "Rangliste", true); return false;
			case 114: PressButton(hud.CcStats, "Rangliste", false); return false;
			case 120:
			{
				var p = hud.CcPilots;
				Check(hud.Screens.Current == p, $"PL: echter Tipp auf „Rangliste“ öffnet die Rangliste ({hud.Screens.Current?.Id})");
				Check(p.TestPortraitShown, "PL: Porträt sichtbar");
				Check(p.TestPlayerName == PilotBook.PlayerName(u) && p.TestPlayerName.Length > 0, $"PL: Pilotenname im Profil ({p.TestPlayerName})");
				Check(p.TestRank.Length > 0 && p.TestRows >= 5, $"PL: Rang und Rangliste ({p.TestRank}, {p.TestRows} Zeilen, Platz {p.TestPlayerPlace})");
				Check(p.TestKills == "0", $"PL: noch kein Abschuss ({p.TestKills})");
				_plRating = p.TestRating;
				// Abschuss: der Jäger stellt den Kaperer, das Gefecht endet mit dessen Zerstörung
				var me = u.Ships[u.PlayerShip];
				var raider = u.Ships[_plRaider];
				var (e, _, them) = PlayerCombat.Engage(u, me, raider);
				Check(e is not null, "PL: Gefecht mit dem Kaperer");
				if (e is not null) Engage.Destroy(u, e, them!, raider);
				return false;
			}
			case 160:
			{
				var p = hud.CcPilots;
				Check(p.TestKills == "1", $"PL: Abschuss im Profil gezählt ({p.TestKills})");
				Check(float.Parse(p.TestRating) > float.Parse(_plRating), $"PL: Wertung steigt nach dem Abschuss ({_plRating} → {p.TestRating})");
				Check(_m.LastRankMessage.StartsWith("Beförderung"), $"PL: Beförderung gemeldet ({_m.LastRankMessage})");
				_m.TestCloseCommand();
				return true;
			}
		}
		return false;
	}
}
