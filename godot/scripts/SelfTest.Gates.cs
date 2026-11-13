using System.Linq;
using Godot;
using SpaceAdventure.World;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>Selbsttest-Stufe der Jumpgate-Modelle (eigene Datei, damit der Hauptselbsttest kaum berührt wird).</summary>
public partial class SelfTest
{
	GateId _gtGate; Node3D? _gtRoot; int _gtOver;

	/// <summary>
	/// Jumpgate-Modelle: jedes Tor im Sektor hat ein Modell, Öffnungsradius ≈ Sprungradius (10 ± 10 %), Durchflugachse zur Sektormitte, Zollstation
	/// seitlich; offen → eingeschränkt → geschlossen → zerstört → offen über <see cref="GateAccess.SetState"/> schaltet Feld-Shader/Modell ohne Neuaufbau;
	/// Sprung durch ein offenes Tor funktioniert. Rückgabe true = Stufe fertig.
	/// </summary>
	bool GateModelStage(int f)
	{
		var u = _m.Session.U;
		const float jumpRadius = 10f;   // Main.GateRadius
		switch (f)
		{
			case 1: _m.TestMenu("new"); return false;
			case 4:
			{
				var sector = u.Sectors[u.PlayerSector];
				var nodes = _m.TestView.GateModelNodes().ToList();
				Check(nodes.Count == sector.Gates.Count && sector.Gates.Count > 0, $"jedes Tor hat ein Modell ({nodes.Count} von {sector.Gates.Count})");
				foreach (var (id, model, look, yaw) in nodes)
				{
					var g = u.Gates[id];
					float? r = GateModels.OpeningRadius(model);
					Check(r is { } rr && Mathf.Abs(rr - jumpRadius) <= 0.1f * jumpRadius, $"Tor {id.Value}: Öffnungsradius {r:0.0} ≈ Sprungradius {jumpRadius}");
					var axis = model.GlobalTransform.Basis.Z.Normalized();          // Durchflugachse
					var side = model.GlobalTransform.Basis.X.Normalized();          // Zollstation
					var c = sector.Stations.Select(s => u.Stations[s].Position).Concat(sector.Fields.Select(x => x.Position))
						.Concat(sector.Gates.Where(x => x != id).Select(x => u.Gates[x].Position)).Aggregate(new Vec2(0f, 0f), (a, b) => a + b);
					int n = sector.Stations.Count + sector.Fields.Count + sector.Gates.Count - 1;
					var mid = n > 0 ? c * (1f / n) : new Vec2(0f, 0f);
					var inward = new Vector3(mid.X - g.Position.X, 0, mid.Y - g.Position.Y).Normalized();
					Check(Mathf.Abs(axis.Dot(inward)) > 0.99f, $"Tor {id.Value}: Durchflugachse zur Sektormitte ({axis.Dot(inward):0.00})");
					Check(Mathf.Abs(side.Dot(inward)) < 0.05f, $"Tor {id.Value}: Zollstation seitlich, nicht im Anflugweg");
					Check(look == GateModels.LookOf(u, g) && GateModels.Probe(model).FieldMode == (look == GateModels.Look.Closed ? 2 : look == GateModels.Look.Open ? 0 : 1),
						$"Tor {id.Value}: Feld-Shader im Zustand {look}");
				}
				var open = nodes.First(x => u.Gates[x.Id].State == GateState.Open);
				_gtGate = open.Id; _gtRoot = open.Model.GetParent<Node3D>(); _gtOver = GateModels.OverrideCount;
				return false;
			}
			case 5:
			{
				var g = u.Gates[_gtGate];
				int structure = u.StructureVersion;
				(GateState State, int Mode)[] steps = { (GateState.Restricted, 1), (GateState.Closed, 2) };
				foreach (var (state, mode) in steps)
				{
					GateAccess.SetState(u, g, state);
					_m.TestView.SyncGates(u, force: true);
					var node = _m.TestView.GateModelNodes().First(x => x.Id == _gtGate);
					var probe = GateModels.Probe(node.Model);
					Check(probe.FieldMode == mode, $"Tor {state}: Feld-Shader Modus {probe.FieldMode} (erwartet {mode})");
					Check(probe.Glow is { } gl && (state == GateState.Closed ? gl.R > gl.B * 4f : gl.R > 0.9f && gl.G > gl.B), $"Tor {state}: Kanten eingefärbt ({probe.Glow})");
				}
				GateAccess.SetState(u, g, GateState.Destroyed);
				_m.TestView.SyncGates(u, force: true);
				var wreck = _m.TestView.GateModelNodes().First(x => x.Id == _gtGate);
				var wp = GateModels.Probe(wreck.Model);
				Check(wreck.Model.Name == "GateWreck" && wp.FieldMode is null && wp.Ember, "Tor zerstört: Trümmermodell mit Glut, kein Feld");
				Check(wreck.Model.GetParent() == _gtRoot && Mathf.IsEqualApprox(wreck.Model.Rotation.Y, wreck.Yaw), "zerstörtes Tor am selben Platz, gleich ausgerichtet");
				GateAccess.SetState(u, g, GateState.Open);
				_m.TestView.SyncGates(u, force: true);
				var back = _m.TestView.GateModelNodes().First(x => x.Id == _gtGate);
				Check(back.Model.Name == "GateModel" && GateModels.Probe(back.Model).FieldMode == 0, "Tor wieder offen: heiles Modell, Feld offen");
				Check(!_m.TestView.NeedsRebuild(u) && u.StructureVersion == structure && back.Model.GetParent() == _gtRoot, "Zustandswechsel ohne Sektor-Neuaufbau");
				int grown = GateModels.OverrideCount - _gtOver;
				Check(grown <= 16, $"Überschreibungen geteilt ({grown} neu für fünf Zustände)");
				GD.Print($"[selftest] Tormodelle: {GateModels.OverrideCount} geteilte Überschreibungsmaterialien");
				return false;
			}
			case 6:
			{
				var before = u.PlayerSector;
				Check(_m.TestJumpThrough(_gtGate) && u.PlayerSector != before, "Sprung durch das offene Tor");
				return false;
			}
			case 9:
			{
				var nodes = _m.TestView.GateModelNodes().ToList();
				Check(nodes.Count == u.Sectors[u.PlayerSector].Gates.Count, $"Zielsektor: jedes Tor hat ein Modell ({nodes.Count})");
				return true;
			}
		}
		return false;
	}
}
