using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Richtungsschilde: Rautenanzeige der vier Schildsektoren (vorne oben, rechts, hinten unten, links). Jedes Viertel füllt sich von der Mitte nach außen mit
/// seiner Ladung (Anteil 0..1 aus dem Core, <c>ShieldSectors.Fill</c>); leer = rot umrandet, das ausgerichtete Viertel hat einen hellen Rand. Nur Anzeige.
/// </summary>
public partial class ShieldDiamond : Control
{
	float[] _fill = { 1f, 1f, 1f, 1f };
	int _focus = -1;

	public ShieldDiamond(float size)
	{
		CustomMinimumSize = new Vector2(size, size);
		Size = new Vector2(size, size);
		MouseFilter = MouseFilterEnum.Ignore;
	}

	/// <summary>Füllstand je Sektor (vorne, rechts, hinten, links) und ausgerichteter Sektor (−1 = ausgeglichen).</summary>
	public void Set(float[] fill, int focus)
	{
		bool same = focus == _focus;
		for (int i = 0; i < 4 && same; i++) same = Mathf.Abs(fill[i] - _fill[i]) < 0.01f;
		if (same) return;
		for (int i = 0; i < 4; i++) _fill[i] = Mathf.Clamp(fill[i], 0f, 1f);
		_focus = focus;
		QueueRedraw();
	}

	/// <summary>Selbsttest: zuletzt gesetzte Füllstände.</summary>
	public float FillOf(int sector) => _fill[sector];
	public int Focus => _focus;

	public override void _Draw()
	{
		var c = Size / 2f;
		float r = Mathf.Min(Size.X, Size.Y) / 2f - 1f;
		// Spitzen: vorne (oben), rechts, hinten (unten), links
		var tips = new[] { c + new Vector2(0, -r), c + new Vector2(r, 0), c + new Vector2(0, r), c + new Vector2(-r, 0) };
		for (int i = 0; i < 4; i++)
		{
			// Viertel = Dreieck aus Mitte, Spitze i und den halben Kanten zu den Nachbarspitzen
			var a = (tips[i] + tips[(i + 3) % 4]) / 2f;
			var b = (tips[i] + tips[(i + 1) % 4]) / 2f;
			DrawColoredPolygon(new[] { c, a, tips[i], b }, new Color(0.08f, 0.12f, 0.2f, 0.9f));
			float f = _fill[i];
			if (f > 0.01f)
			{
				var col = f < 0.3f ? UiKit.Orange : UiKit.Cyan;
				DrawColoredPolygon(new[] { c, c + (a - c) * f, c + (tips[i] - c) * f, c + (b - c) * f }, col);
			}
			var edge = i == _focus ? Colors.White : f <= 0.01f ? UiKit.Red : new Color(UiKit.Cyan, 0.55f);
			DrawPolyline(new[] { c, a, tips[i], b, c }, edge, i == _focus ? 1.6f : 1f, true);
		}
	}
}
