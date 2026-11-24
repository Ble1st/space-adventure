using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Ui;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 160 (Universumsereignisse): neues Spiel (Schürfer: Goldsucher mit Traktor), ein Frachtfund wird im Spielersektor neben dem Schiff
/// erzwungen. Prüft: wichtige Meldung (Toast) mit „Frachtfund“, türkises Symbol auf Sektor- und Universumskarte, Ereigniszeile in der Detailkarte; ein
/// echter Tipp auf „Traktor“ schaltet den Strahl an, die Behälter werden in den Laderaum gezogen, das Ereignis endet „alles geborgen“.
/// </summary>
public partial class SelfTest
{
	Vector2 _ueAt;
	int _ueEvent;
	float _ueHeld;

	bool UniverseEventStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		var fl = hud.Flight;
		int Group(string label) => fl.TestGroupLabels.IndexOf(label);
		switch (f)
		{
			case 1: _m.TestMenu("new:miner"); return false;
			case 8: _m.TestUndock(); u.Rules.PlayerRaids = false; u.Rules.UniverseEventChance = 0f; return false;   // keine zufälligen Ereignisse dazwischen
			case 30:
			{
				var me = u.Ships[u.PlayerShip];
				var e = UniverseEvents.Force(u, CargoFindEvent.KeyValue, me.Sector, me.Position + new Vec2(8f, 0f));
				Check(e is not null, "UE: Frachtfund im Spielersektor erzwungen");
				if (e is null) return true;
				_ueEvent = e.Id;
				var ore = u.FindGood("iron_ore") ?? u.Goods.Values.First();
				foreach (var id in e.Wrecks)
					if (Wrecks.Get(u, id) is { } w) { w.Cargo.Clear(); w.Cargo[ore.Id] = 3f; w.Position = me.Position + new Vec2(8f, (id % 3) * 4f); }
				_ueHeld = me.Cargo.Values.Sum();
				return false;
			}
			case 34:
				Check(_m.LastUniverseEventMessage.Contains("Frachtfund") && _m.LastUniverseEventWeight == MessageWeight.Important,
					$"UE: Toast zum Frachtfund im eigenen Sektor („{_m.LastUniverseEventMessage}“, {_m.LastUniverseEventWeight})");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Sector);
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			case 38:
				Check(hud.CcSector.TestEventSymbols >= 1, $"UE: türkises Symbol auf der Sektorkarte ({hud.CcSector.TestEventSymbols})");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Universe);
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			case 42:
				Check(hud.CcUniverse.TestEventSymbols >= 1, $"UE: Symbol auf der Universumskarte ({hud.CcUniverse.TestEventSymbols})");
				Check(hud.CcUniverse.TestEventLine.Contains("Frachtfund"), $"UE: Ereigniszeile in der Detailkarte („{hud.CcUniverse.TestEventLine}“)");
				_m.TestCloseCommand();
				return false;
			case 50:
			{
				int g = Group("Traktor");
				Check(g >= 0, $"UE: Taste „Traktor“ ({string.Join(", ", fl.TestGroupLabels)})");
				if (g < 0) return true;
				Check(!_m.TestTractorOn, "UE: Traktor zu Beginn aus");
				_ueAt = fl.TestGroupRect(g).GetCenter();
				Tap(_ueAt, true);
				return false;
			}
			case 52: Tap(_ueAt, false); return false;
			case 56: Check(_m.TestTractorOn, "UE: echter Tipp auf „Traktor“ schaltet den Strahl an"); return false;
			case 200:
			{
				var me = u.Ships[u.PlayerShip];
				var e = u.UniverseEvents.FirstOrDefault(x => x.Id == _ueEvent);
				Check(me.Cargo.Values.Sum() >= _ueHeld + 8.9f, $"UE: Traktor zieht die Behälter in den Laderaum ({_ueHeld:0.#} → {me.Cargo.Values.Sum():0.#})");
				Check(e is null || e.Wrecks.All(id => Wrecks.Get(u, id) is null), "UE: alle Behälter des Ereignisses geleert");
				_m.Session.RunTicks(UniverseEventSystem.Interval + 2);
				return false;
			}
			case 206:
				Check(u.UniverseEvents.All(x => x.Id != _ueEvent), "UE: Ereignis endet, wenn alles geborgen ist");
				Check(_m.LastUniverseEventMessage.Contains("vorbei"), $"UE: Endmeldung („{_m.LastUniverseEventMessage}“)");
				Check(UniverseEventInfo.Markers(u).All(m => m.Event != _ueEvent), "UE: Marker verschwindet");
				_m.TestTractor(false);
				return true;
		}
		return false;
	}
}
