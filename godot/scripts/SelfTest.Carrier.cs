using System.Linq;
using Godot;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 170 (Träger und Bordjäger): Startpaket „Trägertest“ — Albatros mit zwei Falken im Hangar. Echter Tipp auf die Gruppe „Jäger“ startet
/// beide (sie stehen als eigene Schiffe mit Eskorte im Sektor), ein zweiter Tipp ruft sie zurück, sie landen; im Schiffsmenü (Reiter „Verband“) startet
/// „Jäger starten“ per Tipp, ein eigener Falke in der Nähe wird per Tipp auf „Einschleusen: …“ aufgenommen. Gruppentasten ohne abgeschnittenen Text.
/// </summary>
public partial class SelfTest
{
	Vector2 _crAt;
	ShipId _crSpare;

	bool CarrierStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		var fl = hud.Flight;
		int Group(string label) => fl.TestGroupLabels.IndexOf(label);
		switch (f)
		{
			case 1: _m.TestMenu("new:carrier"); return false;
			case 10:
			{
				u.Rules.PlayerRaids = false;   // kein Überfall mitten im Test (die Kampfregeln prüfen die Core-Tests)
				u.PendingEncounters.Clear();
				var c = u.Ships[u.PlayerShip];
				Check(u.Rules.Carriers, "TR: Träger in der Standardwelt an");
				Check(c.ModelKey == "carrier" && Carriers.IsCarrier(c), $"TR: Startschiff ist ein Träger ({c.ModelKey})");
				Check(Carriers.Stowed(u, c.Id).Count == 2, $"TR: zwei Falken im Hangar ({Carriers.Stowed(u, c.Id).Count})");
				Check(Carriers.Stowed(u, c.Id).All(x => !u.Ships.ContainsKey(x.Id)), "TR: eingeschleuste Jäger sind nicht im Raum");
				int g = Group("Jäger");
				Check(g >= 0, $"TR: Gruppe „Jäger“ im Daumenbogen ({string.Join(", ", fl.TestGroupLabels)})");
				if (g < 0) { Fail("TR: keine Taste „Jäger“"); return false; }
				Check(fl.TestGroupOverflow.Length == 0, $"TR: Gruppentasten ohne abgeschnittenen Text ({fl.TestGroupOverflow})");
				Check(fl.TestGroupState(g) == Ui.Kit.KitButtonState.Ready, $"TR: „Jäger“ bereit ({fl.TestGroupSub(g)})");
				_crAt = fl.TestGroupRect(g).GetCenter();
				Tap(_crAt, true);
				return false;
			}
			case 12: Tap(_crAt, false); return false;
			case 16:
			{
				var c = u.Ships[u.PlayerShip];
				var out_ = Carriers.Launched(u, c.Id);
				Check(out_.Count == 2, $"TR: echter Tipp startet beide Jäger ({out_.Count}; {_m.LastCarrierMessage})");
				Check(out_.All(x => x.Command is EscortCommand ec && ec.Target == c.Id && x.Sector == c.Sector), "TR: gestartete Jäger begleiten den Träger im Sektor");
				Check(Carriers.Stowed(u, c.Id).Count == 0, "TR: Hangar leer");
				int g = Group("Jäger");
				Check(g >= 0 && fl.TestGroupSub(g).Contains("zurück"), $"TR: Taste zeigt „zurückrufen“ ({(g >= 0 ? fl.TestGroupSub(g) : "")})");
				Check(fl.TestGroupOverflow.Length == 0, $"TR: Gruppentasten ohne abgeschnittenen Text nach dem Start ({fl.TestGroupOverflow})");
				if (g >= 0) { _crAt = fl.TestGroupRect(g).GetCenter(); Tap(_crAt, true); }
				return false;
			}
			case 18: Tap(_crAt, false); return false;
			case 22:
			{
				var c = u.Ships[u.PlayerShip];
				Check(Carriers.Launched(u, c.Id).All(x => x.CarrierReturnSince >= 0), $"TR: zweiter Tipp ruft zurück ({_m.LastCarrierMessage})");
				_m.Session.RunTicks(u.Rules.CarrierReturnTicks + 4);
				return false;
			}
			case 26:
			{
				var c = u.Ships[u.PlayerShip];
				Check(Carriers.Launched(u, c.Id).Count == 0 && Carriers.Stowed(u, c.Id).Count == 2, $"TR: Jäger gelandet ({Carriers.Line(u, c)})");
				// eigener Falke neben dem Träger: im Schiffsmenü einschleusbar
				var m = u.ShipModels["falcon"];
				var yard = u.StationsById().First(s => !s.IsBuildSite);
				var spare = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, yard, 0f);
				spare.DockedAt = default; spare.Sector = c.Sector; spare.Position = c.Position + new Vec2(18f, 0f);
				u.AddShip(spare);
				_crSpare = spare.Id;
				_m.TestOpenShipMenu(c.Id, Ui.Screens.ShipMenuScreen.Tab.Fleet);
				return false;
			}
			case 32:
			{
				Check(hud.Screens.Current == hud.ShipMenu && hud.ShipMenu.Current == Ui.Screens.ShipMenuScreen.Tab.Fleet, $"TR: Schiffsmenü im Reiter „Verband“ ({hud.Screens.Current?.Id})");
				var b = hud.ShipMenu.TestButton("Jäger starten");
				Check(b is not null && b.Overflow().Length == 0, "TR: „Jäger starten“ sichtbar, Text passt");
				TapKit(b, true, "Jäger starten");
				return false;
			}
			case 34: TapKit(hud.ShipMenu.TestButton("Jäger starten"), false, "Jäger starten"); return false;
			case 38:
			{
				var c = u.Ships[u.PlayerShip];
				Check(Carriers.Launched(u, c.Id).Count == 2, $"TR: „Jäger starten“ im Schiffsmenü startet ({hud.ShipMenu.TestCarrierMessage})");
				var b = hud.ShipMenu.TestButton("Einschleusen:");
				Check(b is not null && b.Overflow().Length == 0, "TR: „Einschleusen: …“ für den Falken in der Nähe sichtbar");
				TapKit(b, true, "Einschleusen");
				return false;
			}
			case 40: TapKit(hud.ShipMenu.TestButton("Einschleusen:"), false, "Einschleusen"); return false;
			case 44:
			{
				var c = u.Ships[u.PlayerShip];
				Check(!u.Ships.ContainsKey(_crSpare) && Carriers.Stowed(u, c.Id).Any(x => x.Id == _crSpare),
					$"TR: echter Tipp schleust den Falken ein ({hud.ShipMenu.TestCarrierMessage}; {Carriers.Line(u, c)})");
				var back = hud.ShipMenu.TestButton("Jäger zurück");
				Check(back is not null && back.Overflow().Length == 0, "TR: „Jäger zurück“ sichtbar, Text passt");
				return false;
			}
			case 46: _m.TestCloseShipMenu(); return true;
		}
		return false;
	}
}
