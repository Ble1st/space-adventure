using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Runde 16 (dritter Handy-Test der Gefechtskamera): Bildschirmfotos im Handyformat aus dem echten Spielablauf. Startargument
/// <c>--phone-shots &lt;ordner&gt;</c> (nicht headless, braucht DISPLAY; Fenster 2340×1080). „Neues Spiel“ mit dem Startpaket „Gefechtstest“ über den
/// Menüweg (zufälliger Seed), die Warnung läuft in Echtzeit ab; dann Tipps auf „Nächster Gegner“ und „Dauerfeuer“ wie am Gerät. Aufnahmen: Warnphase,
/// Kampfbeginn, 10 s Kampf, nach dem Kampf; je Aufnahme die Diagnosezeile der Kamera in der Ausgabe.
/// Aufruf: godot-mono --path godot --resolution 2340x1080 -- --phone-shots /pfad/ordner
/// </summary>
public partial class PhoneShots : Node
{
	readonly Main _m;
	readonly string _dir;
	int _frame, _fightFrame = -1, _endFrame = -1;
	Vector2? _press;
	readonly List<string> _log = new();

	public PhoneShots(Main m, string dir) { _m = m; _dir = dir; }
	/// <summary>Runde 17: Bergbau-Variante (<c>--phone-mining</c>): Schürfer am Fels — Anker fliegen, verankert mit Abraum, Abriss durch Schub, Auto-Mining.</summary>
	public bool Mining { get; init; }
	int _rock;

	void PlaceAtRock()
	{
		var u = _m.Session.U;
		var rock = u.Sectors[u.PlayerSector].Asteroids.First(a => a.Id == _rock);
		var p = _m.Player.Position;
		var from = new Vec2(p.X, p.Z);
		var dir = (from - rock.Position).Length > 1e-3f ? (from - rock.Position).Normalized() : new Vec2(1, 0);
		_m.TestTeleportTo(rock.Position + dir * (rock.Radius + SpaceSim.Core.Simulation.Collision.ShipRadius + 0.2f));
		_m.TestFace(rock.Position);
		_m.TestSetSlider(0f);
	}

	void MiningFrame()
	{
		var u = _m.Session.U;
		switch (_frame)
		{
			case 30: _m.TestMenu("new:miner"); break;
			case 32: _m.TestUndock(); break;
			case 150:
			{
				var me = u.Ships[u.PlayerShip];
				var rock = u.Sectors[u.PlayerSector].Asteroids.Where(a => a.Minable && a.Radius > 4f && u.AsteroidClasses.ContainsKey(a.ClassKey))
					.OrderBy(a => Vec2.Distance(a.Position, me.Position)).First();
				rock.Ore = rock.MaxOre = Mathf.Max(rock.MaxOre, 400f);
				_rock = rock.Id;
				PlaceAtRock();
				break;
			}
			case 175: Capture("m01-anker-fliegt.png"); break;
			case 330: Capture("m02-verankert-abbau.png"); break;
			case 334: Capture("m02b-verankert-spaeter.png"); break;   // Runde 19: zweite Phase der Bohrerdrehung (4 Bilder später)
			case 340: _m.TestSetSlider(0.7f); break;
			case 350: Capture("m03-abriss-schub.png"); break;
			case 360: _m.TestSetSlider(0f); PlaceAtRock(); _m.TestStartMining(); break;
			case 900: Capture("m04-automatik.png"); break;
			// Runde 43: Nahaufnahmen Bergbau — Spieler mit M- und L-Werkzeug, NPC-Schürfer S/M/L am Fels, zwei Blickwinkel, zerbrechender Fels
			case 905: Mining43Setup(); break;
			case 915: Mining43PlayerAt("excavator", _r43RockM); _m.TestSetCam(25f, 22f, 24f); break;
			case 995: Capture("m05-spieler-M-bohrer-am-fels.png"); break;
			case 1000: Mining43PlayerAt("strip_miner", _r43RockL); _m.TestSetCam(140f, 30f, 36f); break;
			case 1080: Capture("m06-spieler-L-bohrer-am-fels.png"); break;
			case 1085: Mining43Npcs(); Mining43Overview(); break;
			case 1090: _m.TestSetCam(_r43Yaw, 30f, 50f); break;
			case 1170: Capture("m07-npc-S-M-L-anker-aussen.png"); break;
			case 1175: _m.TestSetCam(_r43Yaw + 140f, 45f, 65f); break;
			case 1255: Capture("m08-npc-anderer-blickwinkel.png"); break;
			case 1260: Mining43LookAt(_r43NpcS, 16f, 18f); break;
			case 1340: Capture("m09-npc-S-nah.png"); break;
			case 1345: Mining43LookAt(_r43NpcL, 24f, 22f); break;
			case 1425: Capture("m10-npc-L-nah.png"); break;
			case 1430:
			{
				// der Fels direkt vor der Kamera (NPC-L) wird leer abgebaut → zerbricht sichtbar im Bild
				// der NPC-Fels, der der Kamera (Spieler) am nächsten liegt — so liegt das Zerbrechen groß im Bild
				var meP = u.Ships[u.PlayerShip].Position;
				var rb = u.Sectors[u.PlayerSector].Asteroids.Where(a => a.Id == _r43NpcS || a.Id == _r43NpcM || a.Id == _r43NpcL)
					.OrderBy(a => Vec2.Distance(a.Position, meP)).First();
				SpaceSim.Core.Mining.Belt.Remove(u, u.Sectors[u.PlayerSector], rb, rb.Ore);   // leer abgebaut → zerbricht
				_r43BreakRock = rb.Id;
				break;
			}
			case 1440: Capture("m11-fels-zerbricht.png"); break;
			case 1480: Capture("m12-fels-zerbricht-spaeter.png"); break;
			case 1600: Finish(); break;
		}
	}

	int _r43RockM, _r43RockL, _r43NpcS, _r43NpcM, _r43NpcL, _r43BreakRock;
	readonly System.Collections.Generic.List<SpaceSim.Core.Model.ShipId> _r43NpcIds = new();

	void Mining43Setup()
	{
		var u = _m.Session.U;
		_m.TestStopAutomation();
		var me = u.Ships[u.PlayerShip];
		var sector = u.Sectors[u.PlayerSector];
		var rocks = sector.Asteroids.Where(a => a.Minable && a.BaseRadius > 3.5f && u.AsteroidClasses.ContainsKey(a.ClassKey)).ToList();
		foreach (var r in rocks) r.Ore = r.MaxOre = 100_000f;
		var near = rocks.OrderBy(a => Vec2.Distance(a.Position, me.Position)).ToList();
		_rock = near[0].Id;
		_r43RockM = near.First(r => r.Id != _rock && Vec2.Distance(r.Position, near[0].Position) > 25f).Id;
		_r43RockL = near.First(r => r.Id != _rock && r.Id != _r43RockM && Vec2.Distance(r.Position, near[0].Position) > 25f).Id;
		// NPC-Felsen nahe am Spieler-Fels, damit die Übersicht sie fasst
		var pr = near[0].Position;
		var npcRocks = rocks.Where(r => r.Id != _rock && r.Id != _r43RockM && r.Id != _r43RockL)
			.OrderBy(r => Vec2.Distance(r.Position, pr)).Take(3).ToList();
		_r43NpcS = npcRocks[0].Id; _r43NpcM = npcRocks[1].Id; _r43NpcL = npcRocks[2].Id;
		GD.Print($"[phone-shots] R43-Felsen: Spieler {_rock}/{_r43RockM}/{_r43RockL}, NPC {_r43NpcS}/{_r43NpcM}/{_r43NpcL}");
	}

	/// <summary>Runde 43: Spieler-Schiffsmodell wechseln und am Fels verankert abbauen lassen.</summary>
	void Mining43PlayerAt(string modelKey, int rockId)
	{
		var u = _m.Session.U;
		var me = u.Ships[u.PlayerShip];
		var rock = u.Sectors[u.PlayerSector].Asteroids.First(a => a.Id == rockId);
		SpaceSim.Core.Mining.MiningAnchor.Release(u, me, u.PlayerSector, "released");
		foreach (var g in me.Cargo.Keys.ToList()) me.Cargo[g] = 0f;
		var m = u.ShipModels[modelKey];
		me.ModelKey = modelKey;
		me.Slots = new System.Collections.Generic.List<SpaceSim.Core.Model.ClassSlot>(u.ShipClasses[m.ClassKey].Slots);
		me.Fit = new System.Collections.Generic.List<string?>(m.Fit);
		me.RecomputeStats(u);
		me.AnchorRock = rockId;
		me.AnchorProgress = u.Rules.AnchorTicks;
		var dir = (me.Position - rock.Position).Normalized();
		_m.TestTeleportTo(rock.Position + dir * (rock.Radius + SpaceSim.Core.Simulation.Collision.ShipRadius + 0.3f));
		_m.TestFace(rock.Position);
		_m.TestSetSlider(0f);
	}

	/// <summary>Runde 43: drei NPC-Schürfer (S/M/L) an ihren Felsen verankert, die wirklich abbauen (MineOrder).</summary>
	void Mining43Npcs()
	{
		var u = _m.Session.U;
		var sector = u.Sectors[u.PlayerSector];
		var yard = u.StationsById().First(s => !s.IsBuildSite);
		(string Model, int Rock)[] specs = { ("unit_miner_100", _r43NpcS), ("excavator", _r43NpcM), ("strip_miner", _r43NpcL) };
		foreach (var (model, rockId) in specs)
		{
			var rock = sector.Asteroids.First(a => a.Id == rockId);
			var m = u.ShipModels[model];
			var npc = SpaceSim.Core.Simulation.ShipPurchase.Create(u, u.NewShipId(), model, u.ShipClasses[m.ClassKey].Slots, m.Fit, SpaceSim.Core.Model.ShipRole.Miner, SpaceSim.Core.Model.Controller.Ai, u.FreeActor, yard, 2000f);
			u.AddShip(npc);
			if (npc.Sector != u.PlayerSector) u.MoveShip(npc, u.PlayerSector);
			npc.DockedAt = default;
			for (int i = 0; i < npc.Fit.Count; i++)
				if (npc.Fit[i] is { } k && u.Modules.TryGetValue(k, out var mod) && mod.Stats.GetValueOrDefault("splitPower") > 0f) npc.Fit[i] = null;
			npc.RecomputeStats(u);
			npc.Cargo.Clear();
			// NPC auf die Seite des Felsens zur Feldmitte — von außen gut sichtbar
			var field = sector.FieldFor(rock.Position);
			var side = field is not null ? (field.Position - rock.Position).Normalized() : new Vec2(1f, 0f);
			npc.Position = rock.Position + side * (rock.Radius + SpaceSim.Core.Mining.ContactMining.ToolReach(npc, u));
			npc.AnchorRock = rockId;
			npc.AnchorProgress = u.Rules.AnchorTicks;
			npc.MinePhase = SpaceSim.Core.Model.MinePhase.Mining;
			int fi = sector.Fields.FindIndex(fl => ReferenceEquals(sector.FieldFor(rock.Position), fl));
			npc.Order = new SpaceSim.Core.Model.MineOrder(sector.Id, fi, u.Stations.Keys.First());
			_r43NpcIds.Add(npc.Id);
			GD.Print($"[phone-shots] R43-NPC {model} an Fels {rockId} bei {npc.Position}, Marker {_m.TestView.ShipPosition(npc.Id)}");
		}
	}

	float _r43Yaw;

	/// <summary>Runde 43: Übersicht über die drei NPC-Felsen — Spieler mittendrin, Blick auf den mittleren NPC.</summary>
	void Mining43Overview()
	{
		var u = _m.Session.U;
		var me = u.Ships[u.PlayerShip];
		var sector = u.Sectors[u.PlayerSector];
		SpaceSim.Core.Mining.MiningAnchor.Release(u, me, u.PlayerSector, "released");
		me.AnchorRock = 0;
		var rocks = _r43NpcIds.Select(id => sector.Asteroids.First(a => a.Id == u.Ships[id].AnchorRock)).ToList();
		var c = rocks.Aggregate(new Vec2(0f, 0f), (a, r) => a + r.Position) * (1f / rocks.Count);
		var away = (c - sector.Fields[0].Position).Normalized();
		var at = c + away * 26f;
		_m.TestTeleportTo(at);
		var mid = rocks[1].Position;
		_m.TestFace(mid);
		var d = mid - at;
		_r43Yaw = Mathf.RadToDeg(Mathf.Atan2(-d.X, -d.Y));
		_m.TestSetSlider(0f);
	}

	/// <summary>Runde 43: Spieler auf die Gegenseite eines NPC-Felsens setzen, Kamera hinter ihm auf den NPC am Fels (Nahaufnahme).</summary>
	void Mining43LookAt(int rockId, float dist, float pitch)
	{
		var u = _m.Session.U;
		var me = u.Ships[u.PlayerShip];
		var sector = u.Sectors[u.PlayerSector];
		var rock = sector.Asteroids.First(a => a.Id == rockId);
		SpaceSim.Core.Mining.MiningAnchor.Release(u, me, u.PlayerSector, "released");
		me.AnchorRock = 0;
		var field = sector.FieldFor(rock.Position);
		var fside = field is not null ? (field.Position - rock.Position).Normalized() : new Vec2(1f, 0f);   // NPC sitzt auf der Feldseite
		var perp = new Vec2(-fside.Y, fside.X);
		var npcAt = rock.Position + fside * (rock.Radius + 4f);   // ungefähre NPC-Position am Fels
		// Spieler seitlich versetzt auf der Feldseite: 3/4-Blick auf Schiff und Bohrpunkt
		var at = rock.Position + fside * (rock.Radius + dist + 6f) + perp * (rock.Radius + dist * 0.9f);
		_m.TestTeleportTo(at);
		_m.TestFace(npcAt);
		_m.TestSetSlider(0f);
		var d = npcAt - at;
		_r43Yaw = Mathf.RadToDeg(Mathf.Atan2(-d.X, -d.Y));
		_m.TestSetCam(_r43Yaw, pitch, dist);
	}

	public override void _Ready()
	{
		SpaceAdventure.Ui.Kit.UiScale.PinnedFactor = 1f; SpaceAdventure.Ui.Kit.UiScale.Apply();   // Runde 33: Bilder im 1280er-Raster bei Faktor 1, unabhängig von der DPI des Rechners
		DirAccess.MakeDirRecursiveAbsolute(_dir);
		// Fenstergröße über --resolution (z. B. 1560x720 = Seitenverhältnis des Geräts 2340×1080, logisch 1560×720)
	}

	void Capture(string file)
	{
		string path = Path.Combine(_dir, file);
		string diag = _m.CameraDebugLine();
		RenderingServer.FramePostDraw += Shot;
		void Shot()
		{
			RenderingServer.FramePostDraw -= Shot;
			var img = GetViewport().GetTexture().GetImage();
			img.SavePng(path);
			string line = $"[phone-shots] {file} {img.GetWidth()}x{img.GetHeight()} · {diag}";
			GD.Print(line);
			_log.Add(line);
		}
	}

	static IEnumerable<Ui.Kit.KitButton> Buttons(Node root)
	{
		foreach (var c in root.GetChildren())
		{
			if (c is Ui.Kit.KitButton b) yield return b;
			foreach (var x in Buttons(c)) yield return x;
		}
	}

	void Tap(string label, bool down)
	{
		label = SelfTest.HudAlias(label);
		if (down) _press = Buttons(_m.HudRef.Flight).FirstOrDefault(b => b.Text == label && b.IsVisibleInTree())?.GetGlobalRect().GetCenter();
		if (_press is not { } at) { GD.Print($"[phone-shots] Taste „{label}“ nicht gefunden"); return; }
		var vp = GetViewport();
		if (Ui.Pointer.TouchMode) vp.PushInput(new InputEventScreenTouch { Index = 0, Position = at, Pressed = down }, true);
		else vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = at, GlobalPosition = at, Pressed = down, ButtonMask = down ? MouseButtonMask.Left : 0 }, true);
	}

	public override void _Process(double delta)
	{
		_frame++;
		if (Mining) { MiningFrame(); return; }
		var u = _m.Session.U;
		switch (_frame)
		{
			case 30: _m.TestMenu("new:combat"); return;   // Menüweg „Neues Spiel → Gefechtstest“ (BuildDefault ohne Seed)
			case 150: Capture("01-warnphase.png"); return;
		}
		if (_frame > 150 && _fightFrame < 0 && u.Fights.Of(u.PlayerShip) is not null) _fightFrame = _frame;
		if (_fightFrame > 0)
		{
			int f = _frame - _fightFrame;
			switch (f)
			{
				case 30: Capture("02-kampfbeginn.png"); break;
				case 60: Tap("Nächster Gegner", true); break;
				case 64: Tap("", false); break;
				case 90: Tap("Dauerfeuer", true); break;
				case 94: Tap("", false); break;
				case 120: Capture("03-nach-tipps.png"); break;
				case 630: Capture("04-kampf-10s.png"); break;
			}
			if (f > 630 && _endFrame < 0 && u.Fights.Of(u.PlayerShip) is null) _endFrame = _frame;
			if (_endFrame > 0 && _frame - _endFrame == 90) Capture("05-nach-kampf.png");
			if ((_endFrame > 0 && _frame - _endFrame == 120) || f == 3600) Finish();
		}
		if (_frame == 2400 && _fightFrame < 0) { GD.Print("[phone-shots] kein Kampf nach 40 s"); Finish(); }
	}

	void Finish()
	{
		File.WriteAllLines(Path.Combine(_dir, "diagnose.txt"), _log);
		GD.Print("[phone-shots] fertig");
		GetTree().Quit(0);
	}
}
