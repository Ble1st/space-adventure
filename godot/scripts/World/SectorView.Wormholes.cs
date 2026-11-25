using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;

namespace SpaceAdventure.World;

/// <summary>
/// Wurmlöcher im Sektor (ERKUNDUNG.md „Wurmlöcher“): stabile sind Torpaare (<see cref="Gate.Wormhole"/>) und stehen in <see cref="Gates"/> (Main springt wie am
/// Tor); instabile kommen und gehen mit dem Core (<see cref="SyncWormholes"/>) und stehen in <see cref="Unstable"/> (Main fliegt hindurch). Beide als
/// <see cref="WormholeView"/>-Portal mit Schild.
/// </summary>
public partial class SectorView
{
	sealed class UnstableNode { public Node3D Root = null!; public Label3D Label = null!; }
	readonly Dictionary<int, UnstableNode> _unstable = new();
	readonly List<Node3D> _portals = new();

	/// <summary>Instabile Wurmlöcher im Sektor (Id, Mittelpunkt) — für den Durchflug.</summary>
	public List<(int Id, Vector3 Pos)> Unstable { get; } = new();

	/// <summary>Selbsttest: Zahl der gezeichneten Portale (stabil + instabil).</summary>
	public int PortalCount => _portals.Count(IsInstanceValid);

	/// <summary>Stabiles Wurmloch statt Tormodell: Portal und Schild „Wurmloch → Ziel“.</summary>
	void BuildWormholeGate(Universe u, Gate g)
	{
		var pos = new Vector3(g.Position.X, 0, g.Position.Y);
		var root = new Node3D { Position = pos, Name = $"Wormhole{g.Id.Value}" };
		_root.AddChild(root);
		_portals.Add(WormholeView.Build(root, unstable: false));
		var target = u.Sectors[u.Gates[g.Target].Sector];
		var name = Gfx.Label($"Wurmloch → {Knowledge.SectorName(u, target.Id)}", new Vector3(0, WormholeView.VisualRadius + 3f, 0));
		name.NoDepthTest = true; name.RenderPriority = 10;
		name.Modulate = new Color(0.82f, 0.68f, 1f);
		root.AddChild(name);
		Gates.Add((g.Id, pos));
		Targets.Add(new PickTarget("gate", g.Id.Value, pos, $"Wurmloch → {target.Name}"));
	}

	/// <summary>Instabile Wurmlöcher mit dem Core abgleichen (neu, verschwunden, Restzeit im Schild) und die Schimmer zur Kamera drehen.</summary>
	public void SyncWormholes(Universe u, Vector3 camPos)
	{
		if (_root is null) return;
		var live = new HashSet<int>();
		foreach (var w in SpaceSim.Core.Simulation.Wormholes.In(u, _sector))
		{
			live.Add(w.Id);
			string text = SpaceSim.Core.Simulation.Wormholes.Label(u, w);
			if (!_unstable.TryGetValue(w.Id, out var n))
			{
				var root = new Node3D { Name = $"UnstableWormhole{w.Id}", Position = new Vector3(w.Position.X, 0, w.Position.Y) };
				_root.AddChild(root);
				var portal = WormholeView.Build(root, unstable: true);
				_portals.Add(portal);
				var label = Gfx.Label(text, new Vector3(0, WormholeView.VisualRadius + 3f, 0));
				label.NoDepthTest = true; label.RenderPriority = 10;
				label.Modulate = new Color(1f, 0.62f, 0.4f);
				root.AddChild(label);
				_unstable[w.Id] = n = new UnstableNode { Root = root, Label = label };
				Targets.Add(new PickTarget("wormhole", w.Id, root.Position, "Instabiles Wurmloch"));
			}
			if (n.Label.Text != text) n.Label.Text = text;
		}
		foreach (var id in _unstable.Keys.Where(id => !live.Contains(id)).ToList())
		{
			_unstable[id].Root.QueueFree();
			_unstable.Remove(id);
			Targets.RemoveAll(t => t.Kind == "wormhole" && t.Id == id);
		}
		Unstable.Clear();
		foreach (var (id, n) in _unstable) Unstable.Add((id, n.Root.Position));
		_portals.RemoveAll(p => !IsInstanceValid(p));
		foreach (var p in _portals) WormholeView.FaceCamera(p, camPos);
	}
}
