using System.Linq;
using Godot;
using SpaceAdventure.Ui.Screens;
using SpaceAdventure.World;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Weapons;

namespace SpaceAdventure;

/// <summary>
/// Sprungantrieb und Tarnfeld (SCHIFFSSLOTS.md 4 und 6) in der Ansicht: Schächte „Sprung“ (Zielwahl auf der Sternenkarte, Laden, Abklingen; erneutes Tippen bricht
/// das Laden ab) und „Tarnung“ (Umschalter), Ladeleuchten, Ankunft (Blitz, Schonfrist, Sektor neu), durchsichtiges Schiff bei Tarnung. Regeln nur im Core.
/// </summary>
public partial class Main
{
	JumpChargeFx? _jumpFx;
	bool _cloakShown;
	Node? _cloakVisual;
	float _jumpFxTime;
	/// <summary>Selbsttest: letzte Meldung von Sprung/Tarnung.</summary>
	internal string LastJumpCloakMessage { get; private set; } = "";

	/// <summary>Taste „Sprung“ oder „Tarnung“ gedrückt.</summary>
	void JumpCloakDown(string key)
	{
		var u = _session.U;
		SyncPlayerData();
		if (key == WeaponBays.Cloak.Key)
		{
			if (PlayerData.Cloaked) { Cloak.Break(u, PlayerData, "off"); return; }
			if (Cloak.TurnOn(u, PlayerData) is { } issue) JumpCloakMessage($"Tarnung nicht möglich: {issue}");
			return;
		}
		if (JumpDrive.Charging(PlayerData)) { JumpDrive.Abort(u, PlayerData, "cancelled", "abgebrochen"); return; }
		if (JumpDrive.ReadyIssue(u, PlayerData) is { } why) { JumpCloakMessage($"Sprung nicht möglich: {why}"); return; }
		var targets = JumpDrive.Targets(u, PlayerData).Select(t => t.Sector).ToList();
		if (targets.Count == 0) { JumpCloakMessage("Sprung: kein Sektor in Reichweite"); return; }
		_sfx.Play(SfxId.Click);
		_hud.Map.OpenJump(u, u.PlayerSector, targets);
	}

	/// <summary>Sprungziel auf der Karte gewählt: Laden beginnen (Regler auf STOPP, Autopilot und Auto-Mining aus — das Schiff muss stillhalten).</summary>
	void StartJumpTo(SectorId target)
	{
		var u = _session.U;
		SyncPlayerData();
		if (JumpDrive.Start(u, PlayerData, target) is { } issue) { JumpCloakMessage($"Sprung nicht möglich: {issue}"); return; }
		CancelAutopilot();
		if (_miner.Enabled) SetAutoMining(false, announce: false);
		_hud.Flight.SetSlider(0f);
		_player.Velocity = Vector3.Zero;
	}

	void JumpCloakMessage(string text)
	{
		LastJumpCloakMessage = text;
		_hud.Toast(text, 3f);
	}

	/// <summary>Ereignisse von Sprung und Tarnung (nur das aktive Schiff); true = behandelt.</summary>
	bool JumpCloakEvent(SimEvent e)
	{
		var u = _session.U;
		switch (e)
		{
			case JumpChargeStarted js when js.Ship == u.PlayerShip:
				_sfx.Play(SfxId.Click);
				JumpCloakMessage($"Sprungantrieb lädt → {u.Sectors[js.To].Name} ({js.ChargeTicks / SpaceSim.Core.Simulation.SimClock.TicksPerSecond:0} s) – stillhalten");
				return true;
			case JumpChargeAborted ja when ja.Ship == u.PlayerShip:
				JumpCloakMessage($"Sprung abgebrochen: {ja.Reason}");
				return true;
			case ShipDriveJumped dj when dj.Ship == u.PlayerShip:
				OnDriveJumped(dj);
				return true;
			case CloakChanged cc when cc.Ship == u.PlayerShip:
				JumpCloakMessage(cc.On ? $"Tarnung an (Tarnstärke {PlayerData.Stats.Cloak:0.#})" : cc.Reason == "off" ? "Tarnung aus" : $"Tarnung gebrochen: {Cloak.ReasonText(cc.Reason)}");
				return true;
		}
		return false;
	}

	/// <summary>Sprung ausgeführt (im Takt der Simulation): Schiff an den Ankunftsort, Schonfrist, Blitz, Kamera; der Sektor wird nach dem Takt neu gebaut.</summary>
	void OnDriveJumped(ShipDriveJumped dj)
	{
		var at = new Vector3(dj.Position.X, 0, dj.Position.Y);
		_player.Position = at;
		_player.Velocity = Vector3.Zero;
		var inward = (-at).Normalized();
		if (inward.LengthSquared() > 0.01f) _player.Heading = Mathf.Atan2(-inward.X, -inward.Z);
		_player.GiveGrace(2.5f);
		CancelAutopilot();
		_mineTarget = null; _chunks.Clear(); _mining.ClearPlayer();
		_jumpCooldown = 2f;
		_camOffset = _orbit.Offset();
		SnapCam();
		_hud.Flash(new Color(0.55f, 0.9f, 1f), 0.8f);
		_sfx.Play(SfxId.Jump);
		_music.Sting("sting_jump", Now);
		_fx.Burst(at, new Color(0.5f, 0.9f, 1f), 40, 18f, 0.9f);
		LastJumpMessage = dj.Message;
		JumpCloakMessage(dj.Message);
	}

	/// <summary>Zustand der Schächte „Sprung“/„Tarnung“ (true = behandelt).</summary>
	bool JumpCloakBay(int i, WeaponBay bay)
	{
		var u = _session.U;
		var me = PlayerData;
		if (bay.Key == WeaponBays.Jump.Key)
		{
			BayState st; string detail; float prog = 1f;
			if (JumpDrive.Charging(me)) { st = BayState.Reloading; prog = JumpDrive.Progress(me); detail = $"lädt {prog * 100f:0} %"; }
			else if (JumpDrive.CooldownLeft(u, me) is long cd and > 0)
			{
				st = BayState.Reloading; prog = 1f - cd / Mathf.Max(1f, me.Stats.JumpCooldownTicks);
				detail = $"kühlt {JumpDrive.Clock(cd)}";
			}
			else if (JumpDrive.ReadyIssue(u, me) is { } why) { st = BayState.NoTarget; detail = ShortIssue(why); }
			else { st = BayState.Ready; detail = "Ziel wählen"; }
			_bayStates[i] = st;
			_bayViews[i] = new BayView { Name = bay.Name, Place = $"{me.Stats.JumpRange} Hops", Detail = detail, State = st, Reload = prog, Facing = bay.Facing, ArcDegrees = bay.ArcDegrees };
			return true;
		}
		if (bay.Key == WeaponBays.Cloak.Key)
		{
			BayState st; string detail;
			if (me.Cloaked) { st = BayState.NoTarget; detail = $"AN · −{me.Stats.CloakDraw:0.#} Leist."; }
			else if (Cloak.ToggleIssue(u, me) is { } why) { st = BayState.NoPower; detail = ShortIssue(why); }
			else { st = BayState.Ready; detail = "aus"; }
			_bayStates[i] = st;
			_bayViews[i] = new BayView { Name = bay.Name, Place = $"Stärke {me.Stats.Cloak:0.#}", Detail = detail, State = st, Reload = 1f, Facing = bay.Facing, ArcDegrees = bay.ArcDegrees };
			return true;
		}
		return false;
	}

	/// <summary>Kurzer Grund für die Taste (die volle Meldung kommt beim Tippen).</summary>
	static string ShortIssue(string why) => why switch
	{
		var w when w.StartsWith("Massenschatten") => "Massenschatten",
		var w when w.StartsWith("zu wenig Leistung") => "Strom fehlt",
		var w when w.StartsWith("zu wenig Treibstoff") => "kein Treibst.",
		var w when w.StartsWith("kühlt ab") => "kühlt ab",
		_ => why,
	};

	/// <summary>Je Bild: Ladeleuchten, Stillhalten beim Laden, Durchsichtigkeit bei Tarnung.</summary>
	void UpdateJumpCloak(float delta)
	{
		if (_session is null) return;
		var me = PlayerData;
		_jumpFx ??= new JumpChargeFx(_player);
		_jumpFxTime += delta;
		bool charging = JumpDrive.Charging(me) && _docked == null;
		float size = _player.Visual is not null ? ShipModels.ScaleOf(me.ModelKey) : 2.5f;
		_jumpFx.Update(charging ? JumpDrive.Progress(me) : -1f, size, _jumpFxTime);
		// Laden: das Schiff hält still, solange der Regler auf STOPP steht (bewegt es sich, bricht der Core das Laden ab)
		if (charging && _hud.Flight.SliderValue < 0.02f) _player.Velocity = Vector3.Zero;
		bool cloaked = me.Cloaked;
		if (cloaked != _cloakShown || _player.Visual?.Root != _cloakVisual)
		{
			_cloakShown = cloaked;
			_cloakVisual = _player.Visual?.Root;
			CloakLook.Apply(_player, cloaked);
		}
	}

	/// <summary>Selbsttest: Ladeleuchten sichtbar, gezeigter Fortschritt, Durchsichtigkeit des Spielerschiffs.</summary>
	internal bool TestJumpFxVisible => _jumpFx?.Visible == true;
	internal float TestJumpFxShown => _jumpFx?.Shown ?? -1f;
	internal float TestPlayerTransparency => CloakLook.Max(_player);
	internal bool TestMapJumpMode => _hud.Map.Visible && _hud.Map.JumpMode;
	internal Vector3 TestPlayerViewPos => _player.Position;
	internal float TestPlayerGrace => _player.Grace;
}
