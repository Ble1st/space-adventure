using System;
using System.Linq;
using Godot;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 166 (Wurmlöcher): Jäger in Borealis vor dem stabilen Wurmloch nach Kalypso — Portal in der Ansicht, Symbol auf Sektor- und
/// Universumskarte, Eintrag in der Scanliste; ein echter Tipp an das rechte Ende des Schubreglers fliegt hinein → Kalypso, kein Zoll. Dort reißt ein
/// instabiles Wurmloch vor dem Bug auf (Meldung, Portal, Scanliste mit „?“), echter Schub fliegt hindurch → Abgrund, Ziel aufgeklärt; ein weiteres fällt
/// zusammen (Meldung, Portal weg).
/// </summary>
public partial class SelfTest
{
	Vector2 _whTapAt;
	int _whArriveFrame = -1, _whUnstableFrame = -1, _whThroughFrame = -1;
	float _whWallet;
	int _whId;

	static readonly SectorId WhBorealis = new(5), WhKalypso = new(10), WhAbgrund = new(11);

	void WhThrottle(float share, bool down)
	{
		var r = _m.HudRef.Flight.TestSliderRect;
		if (down) _whTapAt = r.Position + new Vector2(MathF.Max(2f, r.Size.X * share), r.Size.Y * 0.5f);
		Tap(_whTapAt, down);
	}

	/// <summary>Freier Punkt im Abstand <paramref name="dist"/> vom Spieler (16 feste Richtungen, erste freie).</summary>
	Vec2 WhFreeAhead(Universe u, Vec2 from, float dist)
	{
		var s = u.Sectors[u.PlayerSector];
		for (int k = 0; k < 16; k++)
		{
			var p = from + Vec2.FromAngle(k * MathF.Tau / 16f) * dist;
			bool clear = s.Asteroids.All(a => Vec2.Distance(p, a.Position) > a.Radius + 14f && SegmentClear(from, p, a.Position, a.Radius + 4f))
				&& s.Stations.All(id => Vec2.Distance(p, u.Stations[id].Position) > u.Stations[id].Radius + 30f && SegmentClear(from, p, u.Stations[id].Position, u.Stations[id].Radius + 6f))
				&& s.Gates.All(g => Vec2.Distance(p, u.Gates[g].Position) > 40f);
			if (clear) return p;
		}
		return from + new Vec2(dist, 0f);
	}

	static bool SegmentClear(Vec2 a, Vec2 b, Vec2 c, float r)
	{
		var ab = b - a; float len2 = ab.X * ab.X + ab.Y * ab.Y;
		float t = len2 < 1e-6f ? 0f : Math.Clamp(((c.X - a.X) * ab.X + (c.Y - a.Y) * ab.Y) / len2, 0f, 1f);
		return Vec2.Distance(a + ab * t, c) > r;
	}

	bool WormholeStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		if (f == 1) { _m.TestMenu("new:fighter"); return false; }
		var me = u.Ships[u.PlayerShip];
		var wh = u.Gates.Values.FirstOrDefault(g => g.Wormhole && g.Sector == WhBorealis);
		if (f == 8)
		{
			if (_m.TestDocked) _m.TestUndock();
			u.Rules.PlayerRaids = false;
			Check(u.Rules.Wormholes, "Wurmloch: in der Standardwelt an");
			Check(wh is not null, "Wurmloch: Borealis hat ein stabiles Wurmloch");
			if (wh is null) return true;
			Check(u.Gates[wh.Target].Sector == WhKalypso, "Wurmloch: Borealis führt nach Kalypso");
			_m.TestEnterSector(WhBorealis);
			var dir = wh.Position.Length < 1e-3f ? new Vec2(1f, 0f) : (default(Vec2) - wh.Position) * (1f / wh.Position.Length);
			_m.TestTeleportTo(wh.Position + dir * 60f);
			_m.TestFace(wh.Position);
			_whWallet = u.WalletOf(me).Credits;
			return false;
		}
		if (wh is null) return true;
		if (f == 12)
		{
			Check(_m.TestView.PortalCount >= 1, $"Wurmloch: Portal in der Ansicht ({_m.TestView.PortalCount})");
			Check(SectorScan.For(u, u.PlayerShip, me.Position).Any(c => c.Kind == ScanKind.Gate && c.Name.StartsWith("Wurmloch")), "Wurmloch: Eintrag in der Scanliste");
			_m.TestOpenCcArea(Ui.Screens.CommandAreas.Sector);
			hud.Screens.Current?.Refresh();
			return false;
		}
		if (f == 16)
		{
			Check(hud.CcSector.TestWormholesDrawn >= 1, $"Wurmloch: Symbol auf der Sektorkarte ({hud.CcSector.TestWormholesDrawn})");
			_m.TestOpenCcArea(Ui.Screens.CommandAreas.Universe);
			hud.Screens.Current?.Refresh();
			return false;
		}
		if (f == 20)
		{
			Check(hud.CcUniverse.TestWormholeMarks.Links == 2, $"Wurmloch: zwei Verbindungen auf der Universumskarte ({hud.CcUniverse.TestWormholeMarks.Links})");
			_m.TestCloseCommand();
			return false;
		}
		if (f == 26) { WhThrottle(0.98f, true); return false; }
		if (f == 28) { WhThrottle(0.98f, false); return false; }
		if (f < 30) return false;
		// echter Flug ins stabile Wurmloch
		if (_whArriveFrame < 0)
		{
			if (u.PlayerSector == WhKalypso)
			{
				_whArriveFrame = f;
				Check(_m.LastWormholeMessage.Contains("Wurmloch"), $"Wurmloch: Meldung beim Durchflug („{_m.LastWormholeMessage}“)");
				Check(Math.Abs(u.WalletOf(me).Credits - _whWallet) < 0.01f, "Wurmloch: kein Zoll");
				WhThrottle(0f, true);
				return false;
			}
			if (f > 1500) { Fail($"Wurmloch: Flug ins stabile Wurmloch nicht angekommen (Sektor {u.PlayerSector.Value}, Abstand {Vec2.Distance(me.Position, wh.Position):0})"); return true; }
			return false;
		}
		if (f == _whArriveFrame + 2) { WhThrottle(0f, false); return false; }
		if (f == _whArriveFrame + 90)
		{
			var at = WhFreeAhead(u, me.Position, 55f);
			var w = Wormholes.Force(u, WhKalypso, WhAbgrund, at);
			Check(w is not null, "Wurmloch: instabiles erzwungen");
			if (w is null) return true;
			_whId = w.Id;
			_m.TestFace(at);
			return false;
		}
		if (f == _whArriveFrame + 96)
		{
			Check(_m.LastWormholeMessage.Contains("reißt"), $"Wurmloch: Meldung beim Aufreißen („{_m.LastWormholeMessage}“)");
			Check(_m.TestView.Unstable.Any(x => x.Id == _whId), "Wurmloch: instabiles Portal in der Ansicht");
			Check(SectorScan.For(u, u.PlayerShip, me.Position).Any(c => c.Kind == ScanKind.Wormhole && c.Name.StartsWith("Instabiles Wurmloch")), "Wurmloch: Scanliste zeigt das instabile Wurmloch");
			WhThrottle(0.98f, true);
			return false;
		}
		if (f == _whArriveFrame + 98) { WhThrottle(0.98f, false); return false; }
		if (f < _whArriveFrame + 100) return false;
		if (_whUnstableFrame < 0)
		{
			if (u.PlayerSector == WhAbgrund)
			{
				_whUnstableFrame = f;
				Check(_m.LastWormholeMessage.Contains("Wurmloch durchflogen"), $"Wurmloch: Meldung beim Durchflug des instabilen („{_m.LastWormholeMessage}“)");
				Check(SpaceSim.Core.Intel.Knowledge.Visited(u, WhAbgrund), "Wurmloch: Zielsektor aufgeklärt");
				Check(Wormholes.Of(u, _whId)?.Traversed == true, "Wurmloch: als durchflogen markiert");
				WhThrottle(0f, true);
				return false;
			}
			if (f > _whArriveFrame + 1600) { Fail($"Wurmloch: Flug ins instabile Wurmloch nicht angekommen (Sektor {u.PlayerSector.Value})"); return true; }
			return false;
		}
		if (f == _whUnstableFrame + 2) { WhThrottle(0f, false); return false; }
		if (f == _whUnstableFrame + 30)
		{
			var w2 = Wormholes.Force(u, WhAbgrund, WhBorealis, WhFreeAhead(u, me.Position, 80f));
			Check(w2 is not null, "Wurmloch: zweites instabiles erzwungen");
			if (w2 is null) return true;
			_whThroughFrame = f;
			return false;
		}
		if (_whThroughFrame > 0 && f == _whThroughFrame + 6)
		{
			Check(_m.TestView.Unstable.Count == 1, $"Wurmloch: zweites Portal sichtbar ({_m.TestView.Unstable.Count})");
			foreach (var w in u.UnstableWormholes.Where(x => x.Sector == WhAbgrund)) w.EndTick = u.Tick;
			Wormholes.Expire(u);
			return false;
		}
		if (_whThroughFrame > 0 && f == _whThroughFrame + 12)
		{
			Check(_m.LastWormholeMessage.Contains("zusammengefallen"), $"Wurmloch: Meldung beim Zusammenfallen („{_m.LastWormholeMessage}“)");
			Check(_m.TestView.Unstable.Count == 0, "Wurmloch: Portal nach dem Zusammenfallen weg");
			Log($"Wurmloch: stabil in Bild {_whArriveFrame}, instabil in Bild {_whUnstableFrame}");
			return true;
		}
		return false;
	}
}
