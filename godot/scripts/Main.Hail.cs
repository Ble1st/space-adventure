using Godot;
using SpaceAdventure.World;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Funk Stufe 1: „📡 Funk“ in der Scanliste öffnet das Gespräch mit dem Ziel (<see cref="Ui.Screens.HailPanel"/>). Themen und Wirkung kommen nur aus
/// <see cref="Hail"/> (Core); die Ansicht zeigt die Antwort und öffnet bei Bedarf das Schiffsmenü.
/// </summary>
public partial class Main
{
	HailTarget? _hailTarget;
	bool _hailWired;

	partial void OpenHailPartial(ScanContact c, ref bool handled)
	{
		if (c.Kind is not (ScanKind.Ship or ScanKind.Station)) return;
		OpenHailTo(c.Kind == ScanKind.Ship ? HailTarget.OfShip(new ShipId(c.Id)) : HailTarget.OfStation(new StationId(c.Id)));
		handled = true;
	}

	void OpenHailTo(HailTarget t)
	{
		WireHail();
		_hailTarget = t;
		_hud.Flight.SetHailOpen(true);
		_hud.Flight.Hail.SetView(Hail.View(_session.U, t));
	}

	void WireHail()
	{
		if (_hailWired) return;
		_hailWired = true;
		_hud.Flight.Hail.CloseRequested += CloseHail;
		_hud.Flight.Hail.Topic += key =>
		{
			if (_hailTarget is not { } t) return;
			_sfx.Play(SfxId.Click);
			var r = Hail.Execute(_session.U, t, key);
			_hud.Flight.Hail.SetReply(r.Message, r.Success);
			_hud.Flight.Hail.SetView(Hail.View(_session.U, t));
			if (r.OpenShipMenu && t.Ship.Value != 0) { CloseHail(); OpenShipMenu(t.Ship); }
		};
	}

	void CloseHail()
	{
		_hailTarget = null;
		_hud.Flight.SetHailOpen(false);
	}

	/// <summary>Solange offen: Themen neu berechnen (Warnfristen, Kasse, Ziel verschwunden).</summary>
	void RefreshHail()
	{
		if (_hailTarget is not { } t || !_hud.Flight.HailOpen) return;
		_hud.Flight.Hail.SetView(Hail.View(_session.U, t));
	}

	internal HailTarget? TestHailTarget => _hailTarget;
	internal void TestOpenHail(HailTarget t) => OpenHailTo(t);
}
