using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;

namespace SpaceAdventure.World;

/// <summary>
/// Runde 41: Leuchtfeuer der Missionsziele in der 3D-Szene — eigene kleine Ansicht mit Pool (höchstens 3, wie die Randpfeile).
/// Jeder Beacon ist ein dünner goldener Lichtstrahl mit Kopfkugel und Beschriftung (Auftragstext aus
/// <see cref="MissionMarkers.For"/>); die Beacons des Pools werden je Bild auf die Marker im aktuellen Sektor gesetzt,
/// nicht pro Marker neu erzeugt. Gold wie <see cref="Ui.Screens.FlightHud.MissionGold"/>.
/// </summary>
public partial class MissionView : Node3D
{
	const int Max = 3;
	static readonly Color Gold = new(1f, 0.84f, 0.2f);
	static StandardMaterial3D? _mat;

	sealed partial class Beacon : Node3D
	{
		public MeshInstance3D Beam = null!;
		public MeshInstance3D Head = null!;
		public Label3D Text = null!;
		public float Phase;
	}

	readonly List<Beacon> _pool = new();

	public MissionView()
	{
		_mat ??= Gfx.Mat(Gold, 6f);
		for (int i = 0; i < Max; i++)
		{
			var b = new Beacon { Name = $"MissionBeacon{i}" };
			b.Beam = Gfx.Mesh(new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.55f, Height = 60f, RadialSegments = 8 }, _mat, Vector3.Zero, b);
			b.Beam.Position = new Vector3(0, 30f, 0);
			b.Head = Gfx.Mesh(new SphereMesh { Radius = 2.0f, Height = 4f, RadialSegments = 10, Rings = 5 }, _mat, Vector3.Zero, b);
			b.Head.Position = new Vector3(0, 61f, 0);
			b.Text = Gfx.Label("", new Vector3(0, 66f, 0));
			b.Text.Modulate = Gold;
			b.Text.PixelSize = 0.12f;
			b.AddChild(b.Text);
			b.Visible = false;
			AddChild(b);
			_pool.Add(b);
		}
	}

	/// <summary>Selbsttest: Zahl der sichtbaren Leuchtfeuer.</summary>
	public int TestVisible { get; private set; }
	/// <summary>Selbsttest: Texte der sichtbaren Leuchtfeuer.</summary>
	public IReadOnlyList<string> TestLabels => _pool.Where(b => b.Visible).Select(b => b.Text.Text).ToList();

	/// <summary>Setzt die gepoolten Beacons auf die Missionsmarker des aktuellen Sektors (andere Sektoren zeigen die Karten).</summary>
	public void Sync(Universe u, double delta)
	{
		var markers = u.Rules.MissionsActive
			? MissionMarkers.For(u).Where(mk => mk.Sector == u.PlayerSector).Take(Max).ToList()
			: new List<MissionMarker>();
		TestVisible = markers.Count;
		for (int i = 0; i < _pool.Count; i++)
		{
			var b = _pool[i];
			if (i < markers.Count)
			{
				var mk = markers[i];
				b.Position = new Vector3(mk.Position.X, 0, mk.Position.Y);
				b.Text.Text = mk.Label;
				b.Visible = true;
				b.Phase += (float)delta;
				float s = 1f + 0.18f * Mathf.Sin(b.Phase * 3.2f);   // leichtes Pulsieren
				b.Head.Scale = new Vector3(s, s, s);
			}
			else b.Visible = false;
		}
	}
}
