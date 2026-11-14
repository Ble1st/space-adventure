using System.Linq;
using Godot;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 96 (Phase 32): Startpaket „Schürfer“ (Goldsucher mit Bohrkopf und Harpunenwerfern) am Fels — verankern (Trossen an den Harpunenknoten,
/// Spitzen ausgeblendet, Einschlag auf der Oberfläche), Abbau nur verankert, Abriss durch Schub und durch einen Treffer, danach Auto-Mining rund
/// (ansetzen → verankern → abbauen) ohne harten Stoß.
/// </summary>
public partial class SelfTest
{
	int _anRock; float _anOre; int _anHits;

	void AnchorPlace()
	{
		var u = _m.Session.U;
		var rock = u.Sectors[u.PlayerSector].Asteroids.First(a => a.Id == _anRock);
		var p = _m.Player.Position;
		var from = new Vec2(p.X, p.Z);
		var dir = (from - rock.Position).Length > 1e-3f ? (from - rock.Position).Normalized() : new Vec2(1, 0);
		var at = rock.Position + dir * (rock.Radius + Collision.ShipRadius + 0.2f);
		_m.TestTeleportTo(at);
		_m.TestFace(rock.Position);
		_m.TestSetSlider(0f);
	}

	bool AnchorStage(int f)
	{
		var u = _m.Session.U;
		var me = u.Ships[u.PlayerShip];
		switch (f)
		{
			case 1:
				_m.TestMenu("new:miner");
				return false;
			case 2:
				_m.TestUndock();   // das Startpaket kann angedockt beginnen
				return false;
			case 95:
			{
				Check(u.Rules.MiningAnchors, "Standardwelt: Anker an");
				Check(me.Stats.UsesContact, $"Schürfer hat ein Kontaktwerkzeug ({me.ModelKey})");
				var pos = me.Position;
				var rock = u.Sectors[u.PlayerSector].Asteroids.Where(a => a.Minable && a.Radius > 3.5f && u.AsteroidClasses.ContainsKey(a.ClassKey))
					.OrderBy(a => Vec2.Distance(a.Position, pos)).First();
				rock.Ore = rock.MaxOre = Mathf.Max(rock.MaxOre, 400f);
				_anRock = rock.Id;
				_anHits = _m.TestHits;
				AnchorPlace();
				return false;
			}
			case 120:
				Check(me.AnchorRock == _anRock && MiningAnchor.State(u, me) == AnchorState.Flying, $"Anker fliegt ({me.AnchorRock}, {me.AnchorProgress:0.0})");
				Check(me.UsedVolume(u) < 0.01f, "vor dem Halten kein Abbau");
				return false;
			case 290:
			{
				Check(MiningAnchor.State(u, me) == AnchorState.Held, $"Anker hält ({me.AnchorProgress:0.0}/{u.Rules.AnchorTicks})");
				Check(_m.TestPinned, "verankertes Schiff liegt fest (kein Abdriften)");
				var av = _m.TestAnchors;
				Check(av.VisibleTethers >= 2, $"Trossen sichtbar ({av.VisibleTethers})");
				Check(av.HarpoonsHidden(me.Id), "Harpunenspitzen am Modell ausgeblendet (fliegen als Projektil)");
				var vis = _m.Player.Visual;
				var muz = av.MuzzlesOf(me.Id);
				if (vis is { Harpoons.Count: > 0 })
					Check(muz.Count == vis.Harpoons.Count && muz.Select((m, i) => m.DistanceTo(vis.Harpoons[i].GlobalPosition)).All(d => d < 0.05f), "Trossen beginnen an den Harpunenknoten");
				var rock = u.Sectors[u.PlayerSector].Asteroids.First(a => a.Id == _anRock);
				var c = new Vector3(rock.Position.X, 0, rock.Position.Y);
				Check(av.TipsOf(me.Id).All(t => Mathf.Abs(t.DistanceTo(c) - rock.Radius * 0.92f) < 0.6f), "Spitzen stecken in der Felsoberfläche");
				Check(av.PlumeOn(me.Id), "Abraumfahne beim Abbau");
				_anOre = me.UsedVolume(u);
				Check(_anOre > 1f, $"Abbau läuft verankert ({_anOre:0.0} Einheiten)");
				// Abriss durch Schub
				_m.TestSetSlider(0.7f);
				return false;
			}
			case 293:
				Check(me.AnchorRock == 0 && _m.TestAnchorBroke.Contains("Schub"), $"Schub reißt die Trossen ({_m.TestAnchorBroke})");
				Check(_m.TestAnchors.SnappingCount > 0 || _m.TestAnchors.ActiveCount == 0, "Trossen schnappen zurück");
				_m.TestSetSlider(0f);
				AnchorPlace();
				return false;
			case 450:
				Check(MiningAnchor.State(u, me) == AnchorState.Held, "neu verankert");
				Hit.Apply(u, me, new HitResult(true, 1f, 0f, 0f, 1f));
				return false;
			case 452:
				Check(MiningAnchor.State(u, me) != AnchorState.Held && _m.TestAnchorBroke.Contains("Treffer"), $"Treffer reißt die Trossen ({_m.TestAnchorBroke})");
				// Auto-Mining: ansetzen → verankern → abbauen
				foreach (var g in me.Cargo.Keys.ToList()) me.Cargo[g] = 0f;
				_anOre = 0f;
				_m.TestStartMining();
				return false;
		}
		if (f > 452 && f < 2500)
		{
			if (_m.TestMinerStatus == "Abbau (verankert)" && me.UsedVolume(u) > 3f)
			{
				Check(_m.TestHits == _anHits, $"keine harten Stöße beim Verankern ({_m.TestHits - _anHits})");
				_m.TestStopAutomation();
				GD.Print($"[selftest] Anker: Auto-Mining verankert nach {(f - 452) / 60f:0.0} s, {me.UsedVolume(u):0.0} Einheiten");
				return true;
			}
			return false;
		}
		if (f >= 2500) { Check(false, $"Auto-Mining verankert nicht ({_m.TestMinerStatus}, {me.UsedVolume(u):0.0})"); return true; }
		return false;
	}

	static readonly string[] LeakPackages = { "new:fighter", "new:combat", "new:trader", "new:miner", "new:war", "new" };
	long _leakMem; int _leakFrom;

	/// <summary>
	/// Runde 17 (Handy-Test: Speicher wächst mit „Neues Spiel“): fünf neue Spiele mit verschiedenen Startpaketen hintereinander, je mit etwas Laufzeit (Gefecht im
	/// Gefechtstest) — danach darf nur noch ein Universum leben; dazu die Godot-Monitore.
	/// </summary>
	bool SessionLeakStage(int f)
	{
		int round = f / 120, rf = f % 120;
		if (round < LeakPackages.Length)
		{
			if (f == 1) _leakFrom = _m.TestAttachedCount;
			if (rf == 1) _m.TestMenu(LeakPackages[round]);
			if (rf > 1 && rf < 100) _m.Session.RunTicks(4);
			if (rf == 110 && round == 1) _leakMem = System.GC.GetTotalMemory(true);
			return false;
		}
		int all = _m.TestLiveUniverses();
		int alive = _m.TestLiveUniverses(_leakFrom);   // nur die Sitzungen dieser Stufe (frühere Stufen des Selbsttests halten eigene Verweise)
		long mem = System.GC.GetTotalMemory(true);
		GD.Print($"[selftest] Sitzungswechsel: lebende Universen dieser Stufe {alive} (insgesamt {all}), verwaltet {mem / 1048576.0:0.0} MB (nach Runde 1: {_leakMem / 1048576.0:0.0} MB), "
			+ $"Objekte {Performance.GetMonitor(Performance.Monitor.ObjectCount)}, Knoten {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)}, Ressourcen {Performance.GetMonitor(Performance.Monitor.ObjectResourceCount)}, "
			+ $"Texturen {Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) / 1048576.0:0.0} MB, Videospeicher {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0:0.0} MB");
		Check(alive == 1, $"nach fünf „Neues Spiel“ lebt nur ein Universum ({alive})");
		return true;
	}
}
