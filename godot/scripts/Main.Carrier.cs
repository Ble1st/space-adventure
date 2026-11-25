using Godot;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceAdventure.Ui.Screens;
using SpaceAdventure.World;
using SpaceSim.Core.Weapons;

namespace SpaceAdventure;

/// <summary>
/// Träger (KAMPF.md „Träger und Bordjäger“) in der Ansicht: Schacht „Jäger“ im Daumenbogen — Tippen startet die eingeschleusten Bordjäger
/// (<see cref="Carriers.TryLaunch"/>), sind welche draußen, ruft es sie zurück (<see cref="Carriers.TryRecall"/>). Dazu Meldungen zu Start, Landung und Verlust.
/// Regeln nur im Core; die Ansicht zeigt und löst aus.
/// </summary>
public partial class Main
{
	/// <summary>Selbsttest: letzte Meldung der Trägertaste bzw. eines Trägerereignisses.</summary>
	internal string LastCarrierMessage { get; private set; } = "";

	/// <summary>Tippen auf „Jäger“: starten oder zurückrufen (je nachdem, ob welche draußen sind).</summary>
	internal void CarrierDown()
	{
		var u = _session.U;
		SyncPlayerData();
		var c = PlayerData;
		bool ok = Carriers.Launched(u, c.Id).Count > 0 ? Carriers.TryRecall(u, c, out var msg) : Carriers.TryLaunch(u, c, out msg);
		LastCarrierMessage = msg;
		_hud.Toast(msg, 3f);
		if (ok) _sfx.Play(SfxId.Click);
	}

	/// <summary>Zustand des Schachts „Jäger“ (true = behandelt).</summary>
	bool CarrierBay(int i, WeaponBay bay)
	{
		if (bay.Key != WeaponBays.Fighters.Key) return false;
		var u = _session.U;
		var c = PlayerData;
		LastCarrierId = c.Id;   // für die Verlustmeldung, falls der Träger fällt
		int at = Carriers.Stowed(u, c.Id).Count, out_ = Carriers.Launched(u, c.Id).Count, bays = Carriers.Bays(c);
		BayState st; string detail;
		if (out_ > 0) { st = BayState.Reloading; detail = "zurückrufen"; }
		else if (Carriers.LaunchIssue(u, c) is { } why) { st = at == 0 ? BayState.NoAmmo : BayState.NoTarget; detail = at == 0 ? "leer" : why.StartsWith("Angedockt") ? "angedockt" : "nicht jetzt"; }
		else { st = BayState.Ready; detail = "starten"; }
		_bayStates[i] = st;
		_bayViews[i] = new BayView { Name = bay.Name, Place = $"{at} an Bord · {out_} draußen", Detail = detail, State = st, Reload = bays > 0 ? Mathf.Clamp((float)out_ / bays, 0f, 1f) : 1f, Facing = bay.Facing, ArcDegrees = bay.ArcDegrees };
		return true;
	}

	/// <summary>Ereignisse am aktiven Träger (true = behandelt).</summary>
	bool CarrierEvent(SimEvent e)
	{
		var u = _session.U;
		switch (e)
		{
			case CarrierLaunched cl when cl.Carrier == u.PlayerShip && cl.Auto:
				CarrierMessage($"{cl.Count} Bordjäger gestartet");
				return true;
			case CarrierFighterDocked fd when fd.Carrier == u.PlayerShip && fd.Reason is "left" or "docked":
				CarrierMessage(fd.Reason == "docked" ? "Bordjäger vor dem Andocken gelandet" : "Bordjäger gelandet (Träger verlässt den Sektor)");
				return true;
			case CarrierFightersLost fl when fl.Count > 0 && (fl.Carrier == u.PlayerShip || fl.Carrier == LastCarrierId):
				_hud.Alert($"Mit dem Träger gingen {fl.Count} Bordjäger im Hangar verloren.", 6f);
				return true;
		}
		return false;
	}

	/// <summary>Zuletzt geflogener Träger (für die Verlustmeldung, wenn er das aktive Schiff war).</summary>
	ShipId LastCarrierId;

	void CarrierMessage(string text)
	{
		LastCarrierMessage = text;
		_hud.Toast(text, 3f);
	}
}
