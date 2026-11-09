using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SpaceAdventure.Ui;

namespace SpaceAdventure.Prototypes;

/// <summary>
/// Erzeugt UI-Vorschläge als Bilder (nur Entwurfswerkzeug, nicht Teil des Spiels).
/// Aufruf: godot-mono --path godot res://prototypes/proto.tscn -- --backdrop out.png
///         godot-mono --path godot res://prototypes/proto.tscn -- --shots outdir backdrop.png
/// </summary>
public partial class ProtoRunner : Control
{
	public override async void _Ready()
	{
		await Frames(this, 2);
		var args = OS.GetCmdlineUserArgs();
		try
		{
			int i = Array.IndexOf(args, "--backdrop");
			if (i >= 0) { await CaptureBackdrop(args[i + 1]); GetTree().Quit(0); return; }
			i = Array.IndexOf(args, "--shots");
			if (i >= 0) { await CaptureShots(args[i + 1], args[i + 2]); GetTree().Quit(0); return; }
			GD.PrintErr("Aufruf: -- --backdrop out.png | --shots outdir backdrop.png");
		}
		catch (Exception e) { GD.PrintErr(e.ToString()); }
		GetTree().Quit(1);
	}

	static async Task Frames(Node n, int count) { for (int k = 0; k < count; k++) await n.ToSignal(n.GetTree(), SceneTree.SignalName.ProcessFrame); }

	async Task Save(string path)
	{
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		var img = GetViewport().GetTexture().GetImage();
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		img.SavePng(path);
		GD.Print($"gespeichert: {path} ({img.GetWidth()}x{img.GetHeight()})");
	}

	async Task CaptureBackdrop(string outPath)
	{
		var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate();
		GetTree().Root.AddChild(main);
		await Frames(this, 150);
		foreach (var c in main.GetChildren()) if (c is Hud h) h.Visible = false;
		await Frames(this, 3);
		await Save(outPath);
	}

	async Task CaptureShots(string outDir, string backdrop)
	{
		var tex = ImageTexture.CreateFromImage(Image.LoadFromFile(backdrop));
		var data = ProtoData.Create();
		for (int v = 1; v <= 5; v++)
			foreach (var mode in new[] { "hud", "dock" })
			{
				foreach (var c in GetChildren()) c.QueueFree();
				await Frames(this, 2);
				var bg = new TextureRect { Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale };
				bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
				AddChild(bg);
				var layer = new Control();
				layer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
				AddChild(layer);
				ProtoVariants.Build(v, mode, layer, data);
				await Frames(this, 4);
				await Save(Path.Combine(outDir, $"v{v}_{mode}.png"));
			}
	}
}
