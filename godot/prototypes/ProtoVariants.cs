using Godot;

namespace SpaceAdventure.Prototypes;

public static class ProtoVariants
{
	public static void Build(int v, string mode, Control root, ProtoData d)
	{
		switch (v)
		{
			case 1: if (mode == "hud") V1.Hud(root, d); else V1.Dock(root, d); break;
			case 2: if (mode == "hud") V2.Hud(root, d); else V2.Dock(root, d); break;
			case 3: if (mode == "hud") V3.Hud(root, d); else V3.Dock(root, d); break;
			case 4: if (mode == "hud") V4.Hud(root, d); else V4.Dock(root, d); break;
			case 5: if (mode == "hud") V5.Hud(root, d); else V5.Dock(root, d); break;
		}
	}

	/// <summary>Abdunklung hinter dem Dock, damit die Oberfläche lesbar bleibt.</summary>
	public static void Dim(Control root, float a)
	{
		var r = new ColorRect { Color = new Color(0.01f, 0.02f, 0.05f, a), MouseFilter = Control.MouseFilterEnum.Ignore };
		root.AddChild(r);
		r.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
	}
}
