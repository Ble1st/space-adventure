using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.World;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Wurmlöcher (ERKUNDUNG.md „Wurmlöcher“): Durchflug durch instabile (Core <see cref="Wormholes.Traverse"/>; die stabilen nimmt der Torsprung in
/// <see cref="Jump"/>), Auswahl als Ziel, Randpfeile und Meldungen beim Aufreißen und Zusammenfallen im Spielersektor.
/// </summary>
public partial class Main
{
	/// <summary>Selbsttest: zuletzt gezeigte Wurmloch-Meldung.</summary>
	internal string LastWormholeMessage { get; private set; } = "";

	/// <summary>Im Einflugradius eines instabilen Wurmlochs? Dann hindurch (aus <see cref="CheckGates"/>).</summary>
	bool CheckUnstableWormholes()
	{
		var u = _session.U;
		if (!u.Rules.Wormholes) return false;
		foreach (var (id, pos) in _view.Unstable)
			if (_player.Position.DistanceTo(pos) < u.Rules.WormholeRadius) { TraverseUnstable(id); return true; }
		return false;
	}

	/// <summary>Durch ein instabiles Wurmloch: Core hängt das Schiff um, die Szene des Zielsektors wird aufgebaut (wie beim Torsprung).</summary>
	void TraverseUnstable(int id)
	{
		var u = _session.U;
		PlayerData.Position = new Vec2(_player.Position.X, _player.Position.Z);
		var from = u.PlayerSector;
		if (Wormholes.Traverse(u, PlayerData, id) is { } issue) { _jumpCooldown = 1f; _hud.Toast($"Wurmloch: {issue}"); return; }
		_music.Sting("sting_jump", Now);
		_view.Rebuild(u);
		_mining.ResetNpc(); _combat.Reset(); _mining.ClearPlayer(); _chunks.Clear(); _mineTarget = null;
		CancelAutopilot("Wurmloch"); _target = null; _targetStation = null; _targetGate = null;
		_player.GiveGrace(2.5f);
		var exit = PlayerData.Position;
		var inward = new Vector3(-exit.X, 0, -exit.Y);
		inward = inward.LengthSquared() > 0.01f ? inward.Normalized() : Vector3.Forward;
		_player.Position = new Vector3(exit.X, 0, exit.Y);
		_player.Heading = Mathf.Atan2(-inward.X, -inward.Z);
		_player.Velocity = inward * 10f;
		_jumpCooldown = 2f;
		_camOffset = _orbit.Offset();
		SnapCam();
		_hud.Flash(new Color(0.85f, 0.5f, 1f));
		_sfx.Play(SfxId.Jump);
		LastWormholeMessage = $"Wurmloch durchflogen: {u.Sectors[from].Name.Split(' ').Last()} → {u.Sectors[u.PlayerSector].Name}";
		_hud.Toast(LastWormholeMessage, 4f);
	}

	/// <summary>Instabiles Wurmloch als Ziel (Antippen, Scanliste): der Autopilot fliegt hinein.</summary>
	void SelectUnstableWormhole(int id)
	{
		var u = _session.U;
		if (Wormholes.Of(u, id) is not { } w || w.Sector != u.PlayerSector) return;
		SelectTarget(new NavTarget(u.PlayerSector, w.Position, "Instabiles Wurmloch", 0f), null, quiet: true);
		_hud.Toast($"Ziel: Instabiles Wurmloch → {Wormholes.TargetText(u, w)} · „Autopilot“ fliegt hinein", 3.5f);
	}
	internal void TestSelectUnstableWormhole(int id) => SelectUnstableWormhole(id);

	/// <summary>Randpfeile: stabile und instabile Wurmlöcher im Spielersektor mit Entfernung (höchstens drei, die nächsten).</summary>
	IReadOnlyList<(Vector2 Screen, string Label)> WormholeScreenPositions()
	{
		var list = new List<(Vector2, string)>();
		if (_session is null || _docked != null || !_session.U.Rules.Wormholes) return list;
		var u = _session.U;
		var me = PlayerData.Position;
		var all = new List<(Vec2 Pos, string Label)>();
		foreach (var gid in u.Sectors[u.PlayerSector].Gates)
			if (u.Gates.TryGetValue(gid, out var g) && g.Wormhole && SpaceSim.Core.Intel.Knowledge.KnowsGate(u, gid))
				all.Add((g.Position, $"Wurmloch → {SpaceSim.Core.Intel.Knowledge.SectorName(u, u.Gates[g.Target].Sector).Split(' ').Last()}"));
		foreach (var w in Wormholes.In(u, u.PlayerSector)) all.Add((w.Position, "Instabiles Wurmloch"));
		foreach (var (pos, label) in all.OrderBy(a => Vec2.Distance(me, a.Pos)).Take(3))
			if (EdgeScreen(new Vector3(pos.X, 0f, pos.Y)) is { } p)
				list.Add((p, $"{label} · {SpaceSim.Core.Flight.DistanceText.Format(Vec2.Distance(me, pos))}"));
		return list;
	}

	/// <summary>Meldungen: Aufreißen und Zusammenfallen im Spielersektor (wichtig bzw. Hinweis), sonst still.</summary>
	bool WormholeEvent(SimEvent e)
	{
		var u = _session.U;
		switch (e)
		{
			case WormholeOpened o:
				if (o.Sector != u.PlayerSector) return true;
				LastWormholeMessage = "Ein instabiles Wurmloch reißt im Sektor auf — Ziel unbekannt, es fällt bald wieder zusammen.";
				_hud.Toast(LastWormholeMessage, 5f);
				return true;
			case WormholeCollapsed c:
				if (c.Sector != u.PlayerSector) return true;
				LastWormholeMessage = "Das instabile Wurmloch ist zusammengefallen.";
				_hud.Info(LastWormholeMessage, 3f);
				return true;
			case WormholeTraversed:
				return true;
		}
		return false;
	}
}
