using Godot;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceAdventure.Ui.Screens;
using SpaceSim.Core.Weapons;
using System.Collections.Generic;

namespace SpaceAdventure;

/// <summary>
/// Gasernte (SCHIFFSSLOTS.md „Gasernte und Nebel“) in der Ansicht: Schacht „Gas“ als Umschalter. Ist er an, schöpft der Gassammler je Bild bzw. Teilschritt
/// mit der Core-Regel <see cref="GasHarvest.Step"/>, solange das Schiff in einem Gasfeld steht (Anzeige: sammelt / kein Gasfeld / Kanister voll).
/// </summary>
public partial class Main
{
	bool _gasOn;
	float _gasFxTime;
	internal bool TestGasOn => _gasOn;
	internal string LastGasMessage { get; private set; } = "";

	/// <summary>Tippen auf „Gas“: Sammler an/aus.</summary>
	void GasDown()
	{
		_gasOn = !_gasOn;
		var st = GasHarvest.State(_session.U, PlayerData, new Vec2(_player.Position.X, _player.Position.Z));
		LastGasMessage = _gasOn ? $"Gassammler an ({GasHarvest.Text(st)})" : "Gassammler aus";
		_hud.Toast(LastGasMessage);
	}

	/// <summary>Schöpfen für einen (Teil-)Schritt (aus <see cref="StepMining"/>).</summary>
	void StepGas(float delta)
	{
		if (!_gasOn || _docked != null) return;
		var u = _session.U;
		var ship = PlayerData;
		var pos = new Vec2(_player.Position.X, _player.Position.Z);
		if (GasHarvest.Step(u, ship, pos, delta) > 0f)
		{
			_gasFxTime += delta;
			if (_gasFxTime > 0.25f)
			{
				_gasFxTime = 0f;
				_fx.Burst(_player.Position, new Color(0.7f, 0.55f, 0.95f), 4, 3f, 0.5f);
			}
		}
	}

	/// <summary>Zustand des Schachts „Gas“ (true = behandelt).</summary>
	bool GasBay(int i, WeaponBay bay)
	{
		if (bay.Key != WeaponBays.Gas.Key) return false;
		var st = GasHarvest.State(_session.U, PlayerData, new Vec2(_player.Position.X, _player.Position.Z));
		BayState bs = !_gasOn ? BayState.Ready : st == GasState.Collecting ? BayState.Reloading : st == GasState.Full ? BayState.NoAmmo : BayState.NoTarget;
		string detail = !_gasOn ? (st == GasState.NoField ? "aus · kein Feld" : "aus") : GasHarvest.Text(st);
		_bayStates[i] = bs;
		var gas = GasHarvest.GasGood(_session.U);
		float have = gas is null ? 0f : PlayerData.Cargo.GetValueOrDefault(gas.Id);
		float cap = PlayerData.CapacityVolumeFor(GoodForm.Liquid);
		_bayViews[i] = new BayView { Name = bay.Name, Place = $"{have:0}/{cap:0} m³", Detail = detail, State = bs, Reload = cap > 0f ? Mathf.Clamp(have / cap, 0f, 1f) : 1f, Facing = bay.Facing, ArcDegrees = bay.ArcDegrees };
		return true;
	}
}
