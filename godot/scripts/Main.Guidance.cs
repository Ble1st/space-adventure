using Godot;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Weapons;
using SpaceAdventure.Ui.Screens;
using SpaceAdventure.World;

namespace SpaceAdventure;

/// <summary>
/// Raketenlenkung und Gegenmaßnahmen (KAMPF.md) in der Ansicht: Raketenwarnung (große Meldung mit Lenkart, Restzeit und Richtung, solange eine Rakete
/// auf das aktive Schiff zufliegt), Schächte „Täusch.“ (Täuschkörper ausstoßen) und „Störer“ (Umschalter). Regeln nur im Core (<see cref="Countermeasures"/>).
/// </summary>
public partial class Main
{
	string? _missileWarnShown;
	bool _missileWarnTried;
	/// <summary>Selbsttest: letzte Meldung der Gegenmaßnahmen.</summary>
	internal string LastCounterMessage { get; private set; } = "";
	/// <summary>Selbsttest: zuletzt gezeigte Raketenwarnung (leer = keine).</summary>
	internal string LastMissileWarning { get; private set; } = "";

	/// <summary>Je HUD-Aktualisierung: Warnung zeigen/fortschreiben oder entfernen.</summary>
	void TickMissileWarning()
	{
		var u = _session.U;
		if (!Guidance.Active(u) || !u.Ships.TryGetValue(u.PlayerShip, out var me)) { ClearMissileWarning(); return; }
		var list = Countermeasures.Incoming(u, me);
		if (list.Count == 0) { ClearMissileWarning(); return; }
		string text = Countermeasures.WarningText(list, Countermeasures.DirectionText(list[0], Mathf.RadToDeg(_player.Heading)));
		LastMissileWarning = text;
		if (_missileWarnShown is not null && _hud.ReplaceAlertText(_missileWarnShown, text)) { _missileWarnShown = text; return; }
		if (_missileWarnShown is null && !_missileWarnTried)
		{
			_missileWarnTried = true;   // nur ein Versuch je Anflug: eine stehende Meldung (Schiffsverlust) wird nicht jedes Bild überschrieben
			_hud.Alert(text, 6f);
			_missileWarnShown = _hud.TestAlert == text ? text : null;
		}
	}

	void ClearMissileWarning()
	{
		if (_missileWarnShown is not null && _hud.TestAlert == _missileWarnShown) _hud.HideAlert();
		_missileWarnShown = null;
		_missileWarnTried = false;
	}

	/// <summary>Taste „Täusch.“ oder „Störer“ gedrückt.</summary>
	void CounterDown(string key)
	{
		var u = _session.U;
		SyncPlayerData();
		string? issue = key == WeaponBays.Flares.Key ? Countermeasures.FireFlares(u, PlayerData) : Countermeasures.SetJammer(u, PlayerData, !PlayerData.JammerOn);
		if (issue is not null) CounterMessage($"{(key == WeaponBays.Flares.Key ? "Täuschkörper" : "Störsender")}: {issue}");
		else _sfx.Play(SfxId.Click);
	}

	void CounterMessage(string text)
	{
		LastCounterMessage = text;
		_hud.Toast(text, 3f);
	}

	/// <summary>Ereignisse der Gegenmaßnahmen und gelenkter Raketen am aktiven Schiff (true = behandelt).</summary>
	bool CounterEvent(SimEvent e)
	{
		var u = _session.U;
		switch (e)
		{
			case CountermeasureUsed cm when cm.Ship == u.PlayerShip:
				if (cm.Kind == "flares") { CounterMessage("Täuschkörper ausgestoßen"); _fx.Burst(_player.Position, new Color(1f, 0.7f, 0.3f), 14, 10f, 0.6f); }
				else CounterMessage(cm.On ? $"Störsender an (−{PlayerData.Stats.JamDraw:0.#} Leist.)" : "Störsender aus");
				return true;
			case MissileResolved mr when mr.Target == u.PlayerShip && mr.Outcome is "decoyed" or "jammed" or "blind" or "lock_lost":
				CounterMessage(mr.Outcome switch
				{
					"decoyed" => "Rakete abgelenkt (Täuschkörper)",
					"jammed" => "Rakete gestört (Störsender)",
					"blind" => "Rakete verlor dich (kein Sichtkontakt)",
					_ => "Rakete verlor die Zielführung",
				});
				return true;
		}
		return false;
	}

	/// <summary>Zustand der Schächte „Täusch.“/„Störer“ (true = behandelt).</summary>
	bool CounterBay(int i, WeaponBay bay)
	{
		var u = _session.U;
		var me = PlayerData;
		if (bay.Key == WeaponBays.Flares.Key)
		{
			BayState st; string detail;
			if (Countermeasures.FlaresActive(u, me)) { st = BayState.Reloading; detail = "wirken"; }
			else if (Countermeasures.FlareIssue(u, me) is { } why) { st = why == "keine Täuschkörper" ? BayState.NoAmmo : BayState.NoTarget; detail = why == "keine Täuschkörper" ? "leer" : ShortIssue(why); }
			else { st = BayState.Ready; detail = "bereit"; }
			_bayStates[i] = st;
			_bayViews[i] = new BayView { Name = bay.Name, Place = $"{Countermeasures.FlaresLoaded(u, me):0}/{me.Stats.FlareCapacity:0}", Detail = detail, State = st, Reload = 1f, Facing = bay.Facing, ArcDegrees = bay.ArcDegrees };
			return true;
		}
		if (bay.Key == WeaponBays.Jammer.Key)
		{
			BayState st; string detail;
			if (me.JammerOn) { st = BayState.NoTarget; detail = Countermeasures.JammerWorks(u, me) ? "AN" : "AN · gestört"; }
			else if (Countermeasures.JammerIssue(u, me) is { } why) { st = BayState.NoPower; detail = why == "zu wenig Leistung" ? "Strom fehlt" : ShortIssue(why); }
			else { st = BayState.Ready; detail = "aus"; }
			_bayStates[i] = st;
			_bayViews[i] = new BayView { Name = bay.Name, Place = $"−{me.Stats.JamDraw:0.#} Leist.", Detail = detail, State = st, Reload = 1f, Facing = bay.Facing, ArcDegrees = bay.ArcDegrees };
			return true;
		}
		return false;
	}
}
