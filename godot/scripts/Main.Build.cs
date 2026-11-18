using System.Linq;
using Godot;
using SpaceAdventure.Ui.Screens;
using SpaceAdventure.World;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Eigene Stationen (docs/STATIONEN.md „Eigene Stationen bauen“) in der Ansicht: Platzierungsmodus. Das Baufenster („Bauen“) wählt einen Typ; vor dem Schiff
/// erscheint ein durchsichtiger Geist des Stationsmodells (grün = gültig, rot = ungültig mit Grund aus der Core-Regel <see cref="PlayerBuilding.PlaceIssue"/>).
/// Steuerkreuz bzw. Pfeiltasten verschieben ihn in der Ebene (relativ zur Kamera), „↻ 15°/45°“ (Tasten Q/E) dreht ihn, „Platzieren“ (Eingabe) legt über den
/// Core die Baustelle an, „Abbrechen“ (Zurück/Escape) verwirft ihn. Solange der Geist steht, ersetzt das Platzierungs-HUD das Flug-HUD und das Schiff hält an.
/// </summary>
public partial class Main
{
	string? _placeType;
	Vector2 _placePos;
	float _placeRot;
	string? _placeIssue;
	Node3D? _ghost;
	MeshInstance3D? _ghostRing;
	StandardMaterial3D? _ghostOk, _ghostBad;
	bool _ghostShownOk;
	/// <summary>Schrittweite des Steuerkreuzes (Einheiten je Tipp) und Tempo der Pfeiltasten (Einheiten je Sekunde).</summary>
	public const float PlaceStep = 8f, PlaceKeySpeed = 60f;

	/// <summary>Selbsttest: Zustand des Platzierungsmodus.</summary>
	internal bool TestPlacing => _placeType is not null;
	internal Vector2 TestPlacePos => _placePos;
	internal float TestPlaceRot => _placeRot;
	internal string? TestPlaceIssue => _placeIssue;
	internal Node3D? TestGhost => _ghost;
	internal bool TestGhostGreen => _ghostShownOk;
	/// <summary>Selbsttest: Geist an einen Platz setzen (wie viele Tipps auf das Steuerkreuz).</summary>
	internal void TestPlaceAt(Vector2 p) { _placePos = p; UpdatePlacement(0f); }
	internal string LastPlaceMessage { get; private set; } = "";
	/// <summary>Prüfwerkzeug: reich, abgedockt, in einem herrenlosen Sektor an einem freien Platz, dann Platzierungsmodus für den Typ.</summary>
	internal void TestBeginPlacement(string typeKey)
	{
		var u = _session.U;
		u.Player.Treasury.Credits = Mathf.Max(u.Player.Treasury.Credits, 300_000f);
		if (_docked != null) Undock();
		var free = u.Sectors.Values.Where(s => PlayerBuilding.SectorIssue(u, s.Id) is null && s.Owner != u.PlayerFaction).OrderBy(s => s.Id.Value).FirstOrDefault();
		if (free is null || !u.StationTypes.TryGetValue(typeKey, out var t)) return;
		TestEnterSector(free.Id);
		float half = StationPlacement.HalfArea(free);
		for (float x = -half + 40f; x < half - 40f; x += 23f)
			for (float y = -half + 40f; y < half - 40f; y += 23f)
			{
				var p = new Vec2(x, y);
				var ship = p + new Vec2(0f, t.Radius + PlayerBuilding.ShipClearance + 30f);
				if (!StationPlacement.Fits(u, free, p, t.Radius) || !StationPlacement.Fits(u, free, ship, 6f)) continue;
				TestTeleportTo(ship);
				_player.Heading = 0f;   // Bug nach −Z: der Geist steht genau am freien Platz
				BeginPlacement(typeKey);
				return;
			}
	}

	void WirePlacement()
	{
		var ph = _hud.Placement;
		ph.Move += (dx, dy) => { _sfx.Play(SfxId.Click); MovePlacement(new Vector2(dx, dy) * PlaceStep); };
		ph.Rotate += deg => { _sfx.Play(SfxId.Click); _placeRot = PlayerBuilding.NormalizeRotation(_placeRot + deg); UpdatePlacement(0f); };
		ph.Place += ConfirmPlacement;
		ph.Cancel += () => EndPlacement("Bauplatz-Wahl abgebrochen.");
	}

	/// <summary>Baufenster: „Bauplatz wählen“ — Kommandozentrale zu, Schiff anhalten, Geist vor das Schiff.</summary>
	void BeginPlacement(string typeKey)
	{
		var u = _session.U;
		if (!u.StationTypes.TryGetValue(typeKey, out var t)) return;
		SyncPlayerData();
		if (PlayerBuilding.TypeIssue(u, t) is { } why) { _hud.Toast($"Bauen nicht möglich: {why}", 3.5f); return; }
		if (_placeType is not null) EndPlacement(null);
		_sfx.Play(SfxId.Click);
		_hud.Map.Visible = false;
		_hud.Screens.Hide();
		CancelAutopilot();
		if (_miner.Enabled) SetAutoMining(false, announce: false);
		_hud.Flight.SetSlider(0f);
		_player.Velocity = Vector3.Zero;
		_placeType = typeKey;
		_placeRot = 0f;
		float h = _player.Heading;
		var fwd = new Vector2(-Mathf.Sin(h), -Mathf.Cos(h));
		var start = new Vector2(_player.Position.X, _player.Position.Z) + fwd * (t.Radius + PlayerBuilding.ShipClearance + 30f);
		_placePos = start;
		BuildGhost(t);
		_hud.Flight.Visible = false;
		_hud.Placement.Visible = true;
		UpdatePlacement(0f);
		_hud.Toast($"Bauplatz für {t.Name} wählen: Steuerkreuz verschiebt, ↻ dreht, „Platzieren“ legt die Baustelle an.", 4f);
	}

	void BuildGhost(StationType t)
	{
		ClearGhost();
		_ghostOk ??= GhostMaterial(new Color(0.3f, 1f, 0.55f, 0.32f));
		_ghostBad ??= GhostMaterial(new Color(1f, 0.3f, 0.3f, 0.32f));
		_ghost = new Node3D { Name = "PlacementGhost" };
		AddChild(_ghost);
		if (StationModels.Create(t.Key, t.Radius, Colors.White, StationModels.Look.Normal, out _, out _) is { } model)
		{
			model.Name = "GhostModel";
			_ghost.AddChild(model);
		}
		_ghostRing = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = t.Radius - 0.6f, OuterRadius = t.Radius + 0.6f }, Name = "GhostRing" };
		_ghost.AddChild(_ghostRing);
		_ghostShownOk = !_ghostShownOk;   // erzwingt das erste Einfärben
		ApplyGhostLook(!_ghostShownOk);
	}

	static StandardMaterial3D GhostMaterial(Color c) => new()
	{
		AlbedoColor = c, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled, NoDepthTest = false,
	};

	void ApplyGhostLook(bool ok)
	{
		if (_ghost is null || ok == _ghostShownOk) return;
		_ghostShownOk = ok;
		var mat = ok ? _ghostOk! : _ghostBad!;
		void Walk(Node n) { foreach (var c in n.GetChildren()) { if (c is GeometryInstance3D g) g.MaterialOverride = mat; Walk(c); } }
		Walk(_ghost);
	}

	void ClearGhost()
	{
		if (_ghost is not null && IsInstanceValid(_ghost)) _ghost.QueueFree();
		_ghost = null; _ghostRing = null;
	}

	/// <summary>Verschiebung relativ zur Kamera: x = rechts, y = vom Betrachter weg.</summary>
	void MovePlacement(Vector2 screenDelta)
	{
		var b = _cam.GlobalTransform.Basis;
		var fwd = new Vector2(-b.Z.X, -b.Z.Z);
		if (fwd.LengthSquared() < 1e-4f) fwd = new Vector2(0f, -1f);
		fwd = fwd.Normalized();
		var right = new Vector2(-fwd.Y, fwd.X);
		_placePos += right * screenDelta.X + fwd * screenDelta.Y;
		UpdatePlacement(0f);
	}

	/// <summary>Je Bild: Tasten (Desktop), Geist an Platz und Drehung, Prüfung im Core, Anzeige.</summary>
	void UpdatePlacement(float delta)
	{
		if (_placeType is null) return;
		var u = _session.U;
		if (!u.StationTypes.TryGetValue(_placeType, out var t) || _docked != null) { EndPlacement(null); return; }
		bool screenOpen = _hud.Screens.Current is not null and not FlightHud || _hud.Map.Visible;
		_hud.Placement.Visible = !screenOpen;
		if (!screenOpen) _hud.Flight.Visible = false;
		if (delta > 0f && !screenOpen && KeySteering)
		{
			var mv = Vector2.Zero;
			if (Input.IsKeyPressed(Key.Up)) mv.Y += 1f;
			if (Input.IsKeyPressed(Key.Down)) mv.Y -= 1f;
			if (Input.IsKeyPressed(Key.Left)) mv.X -= 1f;
			if (Input.IsKeyPressed(Key.Right)) mv.X += 1f;
			if (mv != Vector2.Zero) { MovePlacement(mv.Normalized() * PlaceKeySpeed * delta); return; }
		}
		SyncPlayerData();
		_placeIssue = PlayerBuilding.PlaceIssue(u, t, new Vec2(_placePos.X, _placePos.Y));
		if (_ghost is not null)
		{
			_ghost.Position = new Vector3(_placePos.X, 0f, _placePos.Y);
			_ghost.RotationDegrees = new Vector3(0f, _placeRot, 0f);
		}
		ApplyGhostLook(_placeIssue is null);
		float dist = new Vector2(_placePos.X - _player.Position.X, _placePos.Y - _player.Position.Z).Length();
		_hud.Placement.SetState($"{t.Name} · {PlayerBuilding.Cost(u, t):0} Cr", _placeIssue,
			$"Drehung {_placeRot:0}° · {dist:0} m vor dir" + (KeySteering ? " · Pfeiltasten, Q/E drehen, Eingabe setzt" : ""));
	}

	/// <summary>Eingaben im Platzierungsmodus (Desktop): Q/E drehen, Eingabe setzt, Escape bricht ab (Escape über die Zurück-Regel).</summary>
	bool PlacementKey(InputEventKey k)
	{
		if (_placeType is null || !k.Pressed || k.Echo) return false;
		switch (k.Keycode)
		{
			case Key.Q: _placeRot = PlayerBuilding.NormalizeRotation(_placeRot - 15f); UpdatePlacement(0f); return true;
			case Key.E: _placeRot = PlayerBuilding.NormalizeRotation(_placeRot + 15f); UpdatePlacement(0f); return true;
			case Key.Enter: case Key.KpEnter: ConfirmPlacement(); return true;
		}
		return false;
	}

	void ConfirmPlacement()
	{
		if (_placeType is null) return;
		var u = _session.U;
		var t = u.StationTypes[_placeType];
		SyncPlayerData();
		var pos = new Vec2(_placePos.X, _placePos.Y);
		if (PlayerBuilding.PlaceIssue(u, t, pos) is { } why) { _hud.Toast($"Hier nicht: {why}", 3f); return; }
		var site = PlayerBuilding.Start(u, t, pos, _placeRot);
		EndPlacement($"Baustelle {t.Name} angelegt ({PlayerBuilding.Cost(u, t):0} Cr Treuhandgeld). Material: {PlayerBuilding.BillText(u, site.Site!.Required)} — ein eigenes Bauschiff mit Befehl „Bauen“ montiert sie.");
		_sfx.Play(SfxId.Click);
	}

	/// <summary>Platzierungsmodus beenden (mit Hinweis), Flug-HUD zurück.</summary>
	void EndPlacement(string? message)
	{
		_placeType = null;
		_placeIssue = null;
		ClearGhost();
		_hud.Placement.Visible = false;
		if (_docked == null && (_hud.Screens.Current is null or FlightHud)) _hud.Flight.Visible = true;
		if (message is not null) { LastPlaceMessage = message; _hud.Toast(message, 5f); }
	}

	/// <summary>Kamera im Platzierungsmodus: Blickpunkt zwischen Schiff und Geist, weit genug heraus für beide.</summary>
	(float Scale, Vector3 Focus)? PlacementCamera(float wantDist)
	{
		if (_placeType is null || !_session.U.StationTypes.TryGetValue(_placeType, out var t)) return null;
		var d = new Vector3(_placePos.X - _player.Position.X, 0f, _placePos.Y - _player.Position.Z);
		float need = (d.Length() + t.Radius) * 1.6f;
		return (Mathf.Max(1f, need / Mathf.Max(1f, wantDist)), d * 0.5f);
	}

	/// <summary>Ereignisse eigener Stationen (Hinweise); true = behandelt.</summary>
	bool BuildEvent(SimEvent e)
	{
		var u = _session.U;
		switch (e)
		{
			case ConstructionCompleted cc when u.Stations.TryGetValue(cc.Station, out var st) && st.Owner == u.PlayerActor && u.Rules.PlayerBuilding:
				_hud.Alert($"Deine Station {st.Name} ist fertig ({u.Sectors[st.Sector].Name}). Kasse und Lager unter „Besitz“ → Stationen.", 8f);
				return true;
		}
		return false;
	}
}
