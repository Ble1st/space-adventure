using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 143 (eigene Stationen, docs/STATIONEN.md „Eigene Stationen bauen“): neues Spiel (Händler), reich, im herrenlosen Nebelmark an einem
/// freien Platz; Kommandozentrale → „Bauen“ per echtem Tipp auf die Leiste, Typ „Farm“ per Tipp, „Bauplatz wählen“ per Tipp → Geist vor dem Schiff, Flug-HUD
/// weg, Platzierungs-HUD da; ▶ und „↻ 45°“ per Tipp bewegen und drehen den Geist; am Tor rot mit Grund („Platzieren“ gesperrt), am freien Platz grün;
/// „Platzieren“ per Tipp legt die Baustelle an (Besitzer Spieler, Drehung 45°, Treuhandgeld = Bausumme), Hinweis, Flug-HUD zurück, Baustelle in der Ansicht.
/// </summary>
public partial class SelfTest
{
	Vector2 _pbAt, _pbBefore;
	Vec2 _pbSpot;
	float _pbWallet;
	int _pbSites;

	/// <summary>Freier Platz für den Typ in Nebelmark (Core-Platzregel) und die Schiffsposition daneben.</summary>
	static (Vec2 Spot, Vec2 Ship)? PbSpot(Universe u, Sector sec, StationType t)
	{
		float half = StationPlacement.HalfArea(sec);
		for (float x = -half + 40f; x < half - 40f; x += 23f)
			for (float y = -half + 40f; y < half - 40f; y += 23f)
			{
				var p = new Vec2(x, y);
				var ship = p + new Vec2(t.Radius + 30f, 0f);
				if (StationPlacement.Fits(u, sec, p, t.Radius) && StationPlacement.Fits(u, sec, ship, 6f)) return (p, ship);
			}
		return null;
	}

	bool PlayerBuildStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		var neb = u.Sectors.Values.First(s => s.Name.Contains("Nebelmark"));
		var farm = u.StationTypes["farm"];
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
			{
				Check(u.Rules.PlayerBuilding, "Bauen: eigene Stationen in der Standardwelt an");
				u.Player.Treasury.Credits = 300_000f;
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();
				_m.TestEnterSector(neb.Id);
				return false;
			}
			case 10:
			{
				if (PbSpot(u, neb, farm) is not { } sp) { Fail("Bauen: kein freier Platz in Nebelmark"); return true; }
				_pbSpot = sp.Spot;
				_m.TestTeleportTo(sp.Ship);
				_pbSites = PlayerBuilding.OwnSites(u).Count;
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Assets);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 14:
			{
				var b = Buttons(hud.CcAssets).FirstOrDefault(x => x.Text == "Bauen" && x.IsVisibleInTree());
				if (b is null) { Fail("Bauen: Leisteneintrag „Bauen“ fehlt"); return true; }
				_pbAt = b.GetGlobalRect().GetCenter();
				Tap(_pbAt, true);
				return false;
			}
			case 16: Tap(_pbAt, false); return false;
			case 20:
			{
				Check(hud.Screens.Current == hud.CcBuild, $"Bauen: Tipp auf die Leiste öffnet das Baufenster ({hud.Screens.Current?.Id ?? "-"})");
				Check(hud.CcBuild.TestKeys.Contains("farm") && !hud.CcBuild.TestKeys.Contains("headquarters") && !hud.CcBuild.TestKeys.Contains("research_station"),
					$"Bauen: Liste ohne Regierung/Forschung ({string.Join(",", hud.CcBuild.TestKeys)})");
				var b = Buttons(hud.CcBuild).FirstOrDefault(x => x.Text == farm.Name && x.IsVisibleInTree());
				if (b is null || b.GetGlobalRect().Size.Y < 50f) { Fail($"Bauen: Eintrag „{farm.Name}“ fehlt"); return true; }
				_pbAt = b.GetGlobalRect().GetCenter();
				Tap(_pbAt, true);
				return false;
			}
			case 22: Tap(_pbAt, false); return false;
			case 26:
			{
				hud.Screens.Current?.Refresh();
				Check(hud.CcBuild.TestSelected == "farm", $"Bauen: Farm gewählt ({hud.CcBuild.TestSelected})");
				Check(hud.CcBuild.TestAllowedLine.StartsWith("Hier möglich"), $"Bauen: in Nebelmark erlaubt ({hud.CcBuild.TestAllowedLine})");
				Check(hud.CcBuild.TestBillLine.Length > 2, $"Bauen: Stückliste gezeigt ({hud.CcBuild.TestBillLine})");
				var b = Buttons(hud.CcBuild).FirstOrDefault(x => x.Text == "Bauplatz wählen" && x.IsVisibleInTree() && x.State != Ui.Kit.KitButtonState.Disabled);
				if (b is null) { Fail("Bauen: „Bauplatz wählen“ fehlt oder gesperrt"); return true; }
				_pbAt = b.GetGlobalRect().GetCenter();
				Tap(_pbAt, true);
				return false;
			}
			case 28: Tap(_pbAt, false); return false;
			case 34:
			{
				Check(_m.TestPlacing && _m.TestGhost is not null, "Bauen: Platzierungsmodus mit Geist");
				Check(hud.Placement.Visible && !hud.Flight.Visible && hud.Screens.Current is null or Ui.Screens.FlightHud, $"Bauen: Platzierungs-HUD statt Flug-HUD ({hud.Screens.Current?.Id ?? "-"})");
				foreach (var pb in new[] { hud.Placement.UpBtn, hud.Placement.DownBtn, hud.Placement.LeftBtn, hud.Placement.RightBtn, hud.Placement.Rot15Btn, hud.Placement.Rot45Btn, hud.Placement.PlaceBtn, hud.Placement.CancelBtn })
					Check(pb.GetGlobalRect().Size.X >= 63f && pb.GetGlobalRect().Size.Y >= 63f, $"Bauen: Taste „{pb.Text}“ mindestens 64 px ({pb.GetGlobalRect().Size})");
				_pbBefore = _m.TestPlacePos;
				_pbAt = hud.Placement.RightBtn.GetGlobalRect().GetCenter();
				Tap(_pbAt, true);
				return false;
			}
			case 36: Tap(_pbAt, false); return false;
			case 40:
			{
				float moved = (_m.TestPlacePos - _pbBefore).Length();
				Check(Mathf.Abs(moved - Main.PlaceStep) < 0.5f, $"Bauen: ▶ verschiebt den Geist um einen Schritt ({moved:0.0})");
				_pbAt = hud.Placement.Rot45Btn.GetGlobalRect().GetCenter();
				Tap(_pbAt, true);
				return false;
			}
			case 42: Tap(_pbAt, false); return false;
			case 46:
			{
				Check(Mathf.Abs(_m.TestPlaceRot - 45f) < 0.01f, $"Bauen: „↻ 45°“ dreht den Geist ({_m.TestPlaceRot})");
				Check(_m.TestGhost is { } g && Mathf.Abs(g.RotationDegrees.Y - 45f) < 0.5f, "Bauen: Geist im Bild gedreht");
				var gate = u.Gates[neb.Gates[0]].Position;
				_m.TestPlaceAt(new Vector2(gate.X, gate.Y));
				return false;
			}
			case 48:
			{
				Check(_m.TestPlaceIssue is not null && !_m.TestGhostGreen, $"Bauen: am Tor rot ({_m.TestPlaceIssue})");
				Check(hud.Placement.StatusText.StartsWith("Ungültig") && hud.Placement.PlaceBtn.State == Ui.Kit.KitButtonState.Disabled, $"Bauen: Grund gezeigt, „Platzieren“ gesperrt ({hud.Placement.StatusText})");
				_m.TestPlaceAt(new Vector2(_pbSpot.X, _pbSpot.Y));
				return false;
			}
			case 50:
			{
				Check(_m.TestPlaceIssue is null && _m.TestGhostGreen, $"Bauen: am freien Platz grün ({_m.TestPlaceIssue})");
				Check(hud.Placement.StatusText.StartsWith("Platz frei"), $"Bauen: Platzierungs-HUD meldet „Platz frei“ ({hud.Placement.StatusText})");
				_pbWallet = u.Player.Treasury.Credits;
				_pbAt = hud.Placement.PlaceBtn.GetGlobalRect().GetCenter();
				Tap(_pbAt, true);
				return false;
			}
			case 52: Tap(_pbAt, false); return false;
			case 58:
			{
				var sites = PlayerBuilding.OwnSites(u);
				Check(sites.Count == _pbSites + 1, $"Bauen: „Platzieren“ legt eine eigene Baustelle an ({sites.Count})");
				var site = sites.LastOrDefault();
				float cost = PlayerBuilding.Cost(u, farm);
				Check(site is not null && site.Site!.TypeKey == "farm" && site.Owner == u.PlayerActor && Mathf.Abs(site.Rotation - 45f) < 0.01f
					&& Vec2.Distance(site.Position, _pbSpot) < 0.01f, "Bauen: Baustelle Farm, eigene, gedreht, am Platz des Geists");
				Check(Mathf.Abs(_pbWallet - u.Player.Treasury.Credits - cost) < 1f && site is not null && Mathf.Abs(site.Credits - cost) < 1f, $"Bauen: Bausumme als Treuhandgeld ({cost:0})");
				Check(!_m.TestPlacing && hud.Flight.Visible && !hud.Placement.Visible, "Bauen: Platzierungsmodus beendet, Flug-HUD zurück");
				Check(_m.LastPlaceMessage.StartsWith("Baustelle") && hud.TestToastText.StartsWith("Baustelle"), $"Bauen: Hinweis zur Baustelle ({hud.TestToastText})");
				Check(site is not null && _m.TestView.StationModelNodes().Any(n => n.Id == site.Id), "Bauen: Baustelle in der Sektoransicht");
				return true;
			}
		}
		return false;
	}
}
