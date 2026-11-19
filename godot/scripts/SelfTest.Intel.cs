using System.Linq;
using Godot;
using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 147 (Ausbau Punkt 16, docs/ERKUNDUNG.md): neues Spiel „Händler“ mit beschränktem Wissen. (1) Universum zeigt unbekannte Sektoren als „?“,
/// Detailkarte „Unbekannter Sektor“ ohne Route; (2) Comms → Diplomatie → Besitzer des Startsektors → „Sektorkarte …“ per echtem Tipp gekauft: Geld an die
/// Fraktionskasse, Sektor besucht und ganz bekannt; (3) Flotte: „Erkunden“ → Sektor bzw. → „Universum“ per Tipp; das Schiff
/// fliegt und deckt auf.
/// </summary>
public partial class SelfTest
{
	ShipId _inScout;
	SectorId _inUnknown, _inBought;
	FactionId _inOwner;
	float _inWallet, _inTreasury;
	Vector2 _inAt;

	bool IntelStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		Node cc = (Node?)hud.Screens.Current?.Node ?? hud;
		var fs = hud.CcFleet;
		switch (f)
		{
			case 1: GetTree().Root.Size = new Vector2I(1280, 720); _m.TestIntelOn = true; _m.TestMenu("new:trader"); return false;
			case 4:
			{
				Check(Knowledge.Limited(u), "Erkundung: neues Spiel „Händler“ beginnt mit beschränktem Wissen");
				if (!Knowledge.Limited(u)) return true;
				_m.Session.RunTicks(2);
				if (_m.TestDocked) _m.TestUndock();
				u.Player.Treasury.Credits = 50_000f;
				var me = u.Ships[u.PlayerShip];
				var at = u.StationsById().Where(s => s.Sector == me.Sector && !s.IsBuildSite && Docking.Allows(u, s, me)).OrderBy(s => s.Id.Value).First();
				_inScout = FoOwn(u, "pip", at).Id;
				_inUnknown = u.Sectors.Keys.OrderBy(s => s.Value).First(s => !Knowledge.KnowsSector(u, s));
				_inOwner = u.Sectors[u.PlayerSector].Owner;
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Universe);
				hud.CcUniverse.TestUnknownDrawn = 0;
				hud.CcUniverse.TestSelect(_inUnknown);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 8:
			{
				Check(hud.CcUniverse.TestUnknownDrawn > 0, $"Erkundung: Universumskarte zeigt unbekannte Sektoren als „?“ ({hud.CcUniverse.TestUnknownDrawn})");
				Check(Labels(cc).Any(l => l.Text == "Unbekannter Sektor"), "Erkundung: Detailkarte „Unbekannter Sektor“");
				Check(Buttons(cc).Any(b => b.Text == "Route planen / Autopilot" && b.State == Ui.Kit.KitButtonState.Disabled), "Erkundung: keine Route zu Unbekanntem");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Comms);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 10: PressButton(hud.CcComms, "Diplomatie", true); return false;
			case 11: PressButton(hud.CcComms, "Diplomatie", false); hud.Screens.Current?.Refresh(); return false;
			case 13:
				Check(hud.CcComms.TestDiplomacyRows < u.NpcFactionIds().Count, $"Erkundung: Diplomatie nur mit bekannten Fraktionen ({hud.CcComms.TestDiplomacyRows} von {u.NpcFactionIds().Count})");
				hud.CcComms.TestSelectFaction(_inOwner);
				hud.Screens.Current?.Refresh();
				return false;
			case 15: case 17: case 19:
			{
				var b = Buttons(hud.CcComms).FirstOrDefault(x => x.Text.StartsWith("Sektorkarte", System.StringComparison.Ordinal) && x.IsVisibleInTree() && x.State != Ui.Kit.KitButtonState.Disabled);
				if (b is null) { if (f == 19) Fail($"Erkundung: kein Knopf „Sektorkarte …“ ({string.Join(", ", hud.CcComms.TestIntelKeys)})"); return false; }
				R45Visible(hud, b);
				return false;
			}
			case 21:
			{
				var b = Buttons(hud.CcComms).FirstOrDefault(x => x.Text.StartsWith("Sektorkarte", System.StringComparison.Ordinal) && x.IsVisibleInTree() && x.State != Ui.Kit.KitButtonState.Disabled);
				if (b is null || R45Visible(hud, b) is not { } at) { Fail("Erkundung: „Sektorkarte …“ nicht sichtbar"); return false; }
				var key = hud.CcComms.TestIntelKeys.First(k => k.Contains(":sector:"));
				_inBought = new SectorId(int.Parse(key.Split(':')[3]));
				_inWallet = u.Player.Treasury.Credits;
				_inTreasury = u.Actors[new ActorId(_inOwner.Value)].Treasury.Credits;
				_inAt = at;
				Tap(_inAt, true);
				return false;
			}
			case 23: Tap(_inAt, false); return false;
			case 26:
			{
				float paid = _inWallet - u.Player.Treasury.Credits;
				Check(paid > 0f, $"Erkundung: Sektorkarte bezahlt ({paid:0} Cr; {hud.CcComms.TestMessage})");
				Check(System.MathF.Abs(u.Actors[new ActorId(_inOwner.Value)].Treasury.Credits - _inTreasury - paid) < 0.5f, "Erkundung: Geld ging an die Fraktionskasse");
				Check(Knowledge.Visited(u, _inBought) && Knowledge.UnknownIn(u, _inBought) == 0, $"Erkundung: gekaufter Sektor {u.Sectors[_inBought].Name} ganz bekannt");
				_m.TestOpenCommand();
				return false;
			}
			case 28: PressButton(hud.Command, "Kommando", true); return false;
			case 29: PressButton(hud.Command, "Kommando", false); return false;
			case 31: PressButton(cc, "Flotte", true); return false;
			case 32: PressButton(cc, "Flotte", false); return false;
			case 34: Check(_m.TestScreenOpen("cc.fleet"), "Erkundung: Kommando → Flotte offen"); hud.HideAlert(); return false;   // eine offene Meldung (Alarmebene) finge die Tipps ab
		}
		// ── Erkunden (Sektor): Schiff → „Erkunden/Sektor“ → Zielsektor (drei Tipps) ──
		int s0 = 36;
		if (f >= s0) hud.HideAlert();   // Meldungen liegen auf der Alarmebene über allem und fingen Tipps ab
		// ── Erkunden (Sektor): Schiff → „Erkunden“ → Zielsektor ──
		if (f >= s0 && f < s0 + 4) { FoTap(f, s0, () => FoRowButton(cc, FoRow(u, _inScout)), "Erkunder"); return false; }
		if (f >= s0 + 5 && f < s0 + 9) { FoTap(f, s0 + 5, () => fs.TestCommandButton(CommandKinds.ExploreSector), "Erkunden"); return false; }
		if (f >= s0 + 10 && f < s0 + 60)
		{
			// bis zu fünf Versuche: hineinrollen, warten bis die Rolle steht, tippen (die Zielliste liegt je nach Fensterzustand tiefer)
			if (u.Ships[_inScout].Command is ExploreSectorCommand) return false;
			int k = (f - s0 - 10) % 10;
			var b = FoTarget(l => l != "Universum");
			if (k == 0 && b is not null) FoScrollTo(b);
			else if (k == 6 && b is not null) TapNode(b, true);
			else if (k == 7 && _p28Press is { } p) Tap(p, false);
			return false;
		}
		if (f == s0 + 60)
		{
			var tb = FoTarget(l => l != "Universum");
			Check(u.Ships[_inScout].Command is ExploreSectorCommand, $"Erkunden (Sektor) per drei Tipps ({fs.TestMessage}; Ziel {tb?.Text} {tb?.State} {tb?.GetGlobalRect()}; Fenster {_m.GetViewport().GetVisibleRect()})");
			Check(FleetOverflow(cc).Length == 0, $"Erkundung: keine Taste läuft über ({FleetOverflow(cc)})");
			return false;
		}
		// ── Erkunden (Universum): „Erkunden“ → Ziel „Universum“ ──
		int s1 = s0 + 62;
		if (f >= s1 && f < s1 + 4) { FoTap(f, s1, () => fs.TestCommandButton(CommandKinds.ExploreSector), "Erkunden"); return false; }
		if (f >= s1 + 5 && f < s1 + 55)
		{
			if (u.Ships[_inScout].Command is ExploreUniverseCommand) return false;
			int k = (f - s1 - 5) % 10;
			var b = FoTarget(l => l == "Universum");
			if (k == 0 && b is not null) FoScrollTo(b);
			else if (k == 6 && b is not null) TapNode(b, true);
			else if (k == 7 && _p28Press is { } p) Tap(p, false);
			return false;
		}
		if (f == s1 + 55)
		{
			Check(u.Ships[_inScout].Command is ExploreUniverseCommand, $"Erkunden (Universum) per Tipp ({fs.TestMessage})");
			int before = Knowledge.Of(u)!.Sectors.Count(kv => kv.Value == SectorKnowledge.Visited);
			_m.Session.RunTicks(3000);
			int after = Knowledge.Of(u)!.Sectors.Count(kv => kv.Value == SectorKnowledge.Visited);
			Check(after > before, $"Erkundung: das Erkundungsschiff deckt Sektoren auf ({before} → {after} besucht)");
			_m.TestIntelOn = false;
			_m.TestCloseCommand();
			return true;
		}
		return false;
	}
}
