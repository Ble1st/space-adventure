using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;

namespace SpaceAdventure.World;

/// <summary>Jumpgate-Modelle im Sektor (siehe <see cref="GateModels"/>): Aufbau je Tor und Abgleich von Zustand und Sektorfarbe ohne Neuaufbau.</summary>
public partial class SectorView
{
	sealed class GateNode { public Node3D Root = null!; public Node3D Model = null!; public GateModels.Look Look; public Color Accent; public float Yaw; }
	readonly Dictionary<GateId, GateNode> _gateNodes = new();
	long _gateSyncTick = -1; int _gateSyncAccess = -1;

	/// <summary>Farbe des Akzents eines Tors: Fraktion, die den Sektor besitzt (wie auf den Karten), herrenlos grau.</summary>
	public static Color GateAccentOf(Universe u, Gate g) =>
		u.Sectors[g.Sector].Owner.Value != 0 ? Ui.MapOverlay.FactionColor(u.Sectors[g.Sector].Owner) : new Color(0.62f, 0.64f, 0.68f);

	/// <summary>Baut das Modell eines Tors unter <paramref name="root"/>; Rückgabe = Oberkante über der Ebene (für die Beschriftung), null = kein Modell.</summary>
	float? BuildGateModel(Universe u, Gate g, Node3D root)
	{
		if (!GateModels.Has) return null;
		var look = GateModels.LookOf(u, g); var accent = GateAccentOf(u, g);
		if (GateModels.Create(look, accent, out float top) is not { } model) return null;
		float yaw = GateModels.YawOf(u, g);
		model.Rotation = new Vector3(0f, yaw, 0f);
		root.AddChild(model);
		_gateNodes[g.Id] = new GateNode { Root = root, Model = model, Look = look, Accent = accent, Yaw = yaw };
		return Mathf.Max(top, GateModels.Scale * 1.36f);   // heile Höhe auch für Trümmer, damit das Etikett nicht springt
	}

	/// <summary>Selbsttest: Tore mit Modell (Id → Modellknoten, Anzeigezustand).</summary>
	public IEnumerable<(GateId Id, Node3D Model, GateModels.Look Look, float Yaw)> GateModelNodes() =>
		_gateNodes.Select(kv => (kv.Key, kv.Value.Model, kv.Value.Look, kv.Value.Yaw));

	/// <summary>
	/// Zustandswechsel (offen/eingeschränkt/für dich gesperrt/geschlossen/zerstört) und Besitzerwechsel ändern keine Struktur — die Einfärbung folgt
	/// hier. Billig: neu bewertet wird nur, wenn ein Tick vergangen ist oder sich der Zugang geändert hat, neu eingefärbt nur bei einer Änderung;
	/// nur heil ↔ zerstört tauscht den Modellknoten des Tors.
	/// </summary>
	public void SyncGates(Universe u, bool force = false)
	{
		if (_gateNodes.Count == 0) return;
		if (!force && u.Tick == _gateSyncTick && u.AccessVersion == _gateSyncAccess) return;
		_gateSyncTick = u.Tick; _gateSyncAccess = u.AccessVersion;
		foreach (var (id, n) in _gateNodes)
		{
			if (!u.Gates.TryGetValue(id, out var g)) continue;
			var look = GateModels.LookOf(u, g); var accent = GateAccentOf(u, g);
			if (look == n.Look && accent == n.Accent) continue;
			bool swap = (look == GateModels.Look.Destroyed) != (n.Look == GateModels.Look.Destroyed);
			n.Look = look; n.Accent = accent;
			if (swap && GateModels.Create(look, accent, out _) is { } model)
			{
				model.Rotation = new Vector3(0f, n.Yaw, 0f);
				n.Root.RemoveChild(n.Model);
				n.Model.QueueFree();
				n.Root.AddChild(model);
				n.Model = model;
			}
			else GateModels.Apply(n.Model, look, accent);
		}
	}
}
