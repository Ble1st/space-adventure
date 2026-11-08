using Godot;

namespace SpaceAdventure.World;

/// <summary>Kleine Helfer zum Erzeugen von Materialien und Meshes im Code (bis echte Assets existieren).</summary>
public static class Gfx
{
	public static StandardMaterial3D Mat(Color color, float emissive = 0f, float metallic = 0.3f)
	{
		var m = new StandardMaterial3D { AlbedoColor = color, Metallic = metallic, Roughness = 0.5f };
		if (emissive > 0f) { m.EmissionEnabled = true; m.Emission = color; m.EmissionEnergyMultiplier = emissive; }
		return m;
	}

	public static MeshInstance3D Mesh(Mesh mesh, Material mat, Vector3 pos, Node parent)
	{
		var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos };
		parent.AddChild(mi);
		return mi;
	}

	public static Label3D Label(string text, Vector3 pos) => new()
	{
		Text = text, Position = pos, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, PixelSize = 0.04f,
	};
}
