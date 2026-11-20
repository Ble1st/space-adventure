using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.World;

namespace SpaceAdventure;

/// <summary>
/// Modellbilder für den Vergleich der Oberflächen (vorher/nachher): <c>--model-shots &lt;dir&gt;</c> rendert ausgewählte Schiffe, Stationen und das Tor in einem
/// eigenen Unterfenster (eigene Welt, Licht wie im Spiel, Himmelskugel als Hintergrund) aus zwei Abständen — nah (Modell füllt das Bild) und Spielabstand
/// (Kamera 57 Einheiten wie die Rückansicht) — im Handyformat 1560 × 720. Optionen: <c>--model-shots-all</c> (alle Modelle),
/// <c>--no-surface-detail</c> (ohne die gemeinsamen Texturen = Stand vorher). Braucht ein Fenster (nicht headless).
/// </summary>
public partial class ModelShots : Node
{
	readonly string _dir;
	readonly bool _all;
	SubViewport _vp = null!;
	Camera3D _cam = null!;
	Node3D _stage = null!;
	readonly List<(string Kind, string Key)> _todo = new();
	int _i = -1, _wait, _phase;
	float _r;
	Vector3 _center;

	readonly string[]? _ships;
	ShipVisual? _visual;

	/// <param name="ships">Optional (<c>--model-shots-ships=a,b,c</c>): nur diese Schiffsmodelle, ohne Stationen und Tor.</param>
	public ModelShots(string dir, bool all, string[]? ships = null) { _dir = dir; _all = all; _ships = ships; }

	public override void _Ready()
	{
		System.IO.Directory.CreateDirectory(_dir);
		_vp = new SubViewport { Size = new Vector2I(1560, 720), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa2X };
		AddChild(_vp);
		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Sky,
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.32f, 0.35f, 0.47f),
			AmbientLightEnergy = 1.15f,
			ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
			GlowEnabled = true,
		};
		string sky = "res://assets/skyboxes/blue_local_star_and_nebulae.jpg";
		if (ResourceLoader.Exists(sky))
			env.Sky = new Sky { SkyMaterial = new PanoramaSkyMaterial { Panorama = ResourceLoader.Load<Texture2D>(sky), Filter = true } };
		else env.BackgroundMode = Godot.Environment.BGMode.Color;
		_vp.AddChild(new WorldEnvironment { Environment = env });
		_vp.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 30, 0), LightEnergy = 1.3f });
		_vp.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-25, 210, 0), LightEnergy = 0.45f, LightColor = new Color(0.7f, 0.8f, 1f), ShadowEnabled = false });
		_cam = new Camera3D { Fov = 50f, Far = 4000f, Current = true };
		_vp.AddChild(_cam);
		_stage = new Node3D();
		_vp.AddChild(_stage);

		if (_ships is { Length: > 0 })
		{
			foreach (var k in _ships) _todo.Add(("ship", k));
		}
		else if (_all)
		{
			foreach (var k in Glbs(ShipModels.Dir)) _todo.Add(("ship", k));
			foreach (var k in Glbs(StationModels.Dir)) _todo.Add(("station", k));
		}
		else
		{
			_todo.Add(("ship", "falcon"));
			_todo.Add(("ship", "bastion"));
			_todo.Add(("ship", "battlecruiser"));
			_todo.Add(("station", "shipyard"));
			_todo.Add(("station", "solar_plant"));
			_todo.Add(("station", "platform_medium"));
		}
		if (_ships is not { Length: > 0 }) _todo.Add(("gate", "gate"));
		Next();
	}

	static IEnumerable<string> Glbs(string dir) =>
		DirAccess.GetFilesAt(dir).Where(f => f.EndsWith(".glb")).Select(f => f[..^4]).OrderBy(f => f, System.StringComparer.Ordinal);

	static readonly Dictionary<string, float> _radius = SpaceSim.Core.Content.WorldBuilder.BuildDefault().StationTypes.ToDictionary(kv => kv.Key, kv => kv.Value.Radius);

	void Next()
	{
		foreach (var c in _stage.GetChildren()) { _stage.RemoveChild(c); c.QueueFree(); }
		_i++;
		if (_i >= _todo.Count) { GD.Print($"MODEL-SHOTS OK ({_todo.Count} Modelle, Details {(SurfaceDetail.Enabled ? "an" : "aus")}, {SurfaceDetail.MaterialCount} Materialien)"); GetTree().Quit(); return; }
		var (kind, key) = _todo[_i];
		var accent = new Color(0.2f, 0.5f, 1f);
		Node3D? model = null;
		switch (kind)
		{
			case "ship":
				var v = ShipModels.Create(key, accent);
				_visual = v;
				model = v?.Root;
				if (v != null) v.SetEngine(0.6f);
				break;
			case "station":
				_visual = null;
				float r = _radius.GetValueOrDefault(key, 15f);
				model = StationModels.Create(key, r, accent, StationModels.Look.Normal, out _, out _);
				break;
			case "gate":
				model = GateModels.Create(GateModels.Look.Open, accent, out _);
				break;
		}
		if (model is null) { GD.Print($"[model-shots] {kind} {key}: kein Modell"); Next(); return; }
		_stage.AddChild(model);
		var box = Bounds(model);
		_center = box.GetCenter();
		_r = box.Size.Length() * 0.5f;
		_phase = 0;
		Place();
		_wait = 6;
	}

	void Place()
	{
		var dir = new Vector3(Mathf.Sin(Mathf.DegToRad(35f)), 0f, Mathf.Cos(Mathf.DegToRad(35f)));
		if (_phase == 2) dir = new Vector3(dir.X, 0f, -dir.Z);   // Greifer/Backen von vorn
		if (_phase != 1)
		{
			// nah: Modell füllt die Bildhöhe
			float d = _r / Mathf.Tan(Mathf.DegToRad(25f)) * 0.75f;
			var off = (dir * Mathf.Cos(Mathf.DegToRad(30f)) + Vector3.Up * Mathf.Sin(Mathf.DegToRad(30f))) * d;
			_cam.Position = _center + off;
		}
		else
		{
			// Spielabstand: Rückansicht-Kamera (45 hoch, 35 zurück), bei großen Stationen weiter
			float h = 45f, b = 35f;
			float k = Mathf.Max(1f, _r / 18f);
			_cam.Position = _center + dir * b * k + Vector3.Up * h * k;
		}
		_cam.LookAt(_center, Vector3.Up);
	}

	static Aabb Bounds(Node3D root)
	{
		var box = new Aabb();
		bool first = true;
		void Walk(Node n, Transform3D xf)
		{
			foreach (var c in n.GetChildren())
			{
				if (c is not Node3D c3) continue;
				var t = xf * c3.Transform;
				if (c3 is MeshInstance3D mi) { var a = t * mi.GetAabb(); box = first ? a : box.Merge(a); first = false; }
				Walk(c3, t);
			}
		}
		Walk(root, root.Transform);
		return box;
	}

	public override void _Process(double delta)
	{
		if (_i >= _todo.Count || _i < 0) return;
		if (--_wait > 0) return;
		var (kind, key) = _todo[_i];
		var img = _vp.GetTexture().GetImage();
		string tag = SurfaceDetail.Enabled ? "" : "-ohne";
		img.SavePng(System.IO.Path.Combine(_dir, $"{kind}-{key}-{(_phase == 0 ? "nah" : _phase == 1 ? "spiel" : "offen")}{tag}.png"));
		if (_phase == 0) { _phase = 1; Place(); _wait = 4; }
		else if (_phase == 1 && _visual is { Jaws.Count: > 0 } jv)
		{
			// Backen/Greifer halb geöffnet (Mitte der Spalt-/Bergungsbewegung), nah
			jv.SnapJaws();
			jv.AnimateJaws(ShipVisual.JawSeconds * 0.5f);
			_phase = 2; Place(); _wait = 4;
		}
		else Next();
	}
}
