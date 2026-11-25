using System;
using System.Collections.Generic;
using Godot;
using SpaceSim.Core.Pilots;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Piloten: prozedurales Porträt aus Schichten (Hintergrund, Anzug, Hals, Gesicht, Haare, Augen, Narbe, Helm, Rangabzeichen) — eigene einfache Zeichnung
/// ohne Bilddateien. Die Merkmale kommen rein aus dem Core (<see cref="PortraitSpec"/>); die Ansicht kennt nur die Farbpaletten.
/// </summary>
public sealed partial class PilotPortrait : Control
{
	static readonly Color[] Backs = { new(0.10f, 0.20f, 0.32f), new(0.22f, 0.12f, 0.26f), new(0.12f, 0.24f, 0.18f), new(0.30f, 0.18f, 0.10f), new(0.14f, 0.14f, 0.18f), new(0.08f, 0.22f, 0.26f) };
	static readonly Color[] Skins = { new(0.96f, 0.82f, 0.70f), new(0.86f, 0.66f, 0.50f), new(0.68f, 0.48f, 0.34f), new(0.48f, 0.32f, 0.22f), new(0.80f, 0.72f, 0.82f) };
	static readonly Color[] Hairs = { new(0.10f, 0.08f, 0.06f), new(0.40f, 0.24f, 0.12f), new(0.80f, 0.66f, 0.36f), new(0.62f, 0.20f, 0.10f), new(0.72f, 0.74f, 0.78f), new(0.20f, 0.46f, 0.70f) };
	static readonly Color[] EyeCols = { new(0.20f, 0.40f, 0.70f), new(0.30f, 0.52f, 0.30f), new(0.36f, 0.24f, 0.14f), new(0.60f, 0.62f, 0.66f) };
	static readonly Color[] Insignias = { new(0.95f, 0.78f, 0.25f), new(0.75f, 0.78f, 0.82f), new(0.80f, 0.45f, 0.25f), new(0.40f, 0.85f, 0.95f) };

	PortraitSpec _spec;
	public PortraitSpec Spec { get => _spec; set { _spec = value; QueueRedraw(); } }
	/// <summary>Rang (0..9) für die Zahl der Streifen auf dem Abzeichen.</summary>
	public int Rank { get; set; }

	public PilotPortrait() { MouseFilter = MouseFilterEnum.Ignore; }

	static Vector2[] Ellipse(Vector2 c, float rx, float ry, int n = 28, float from = 0f, float to = MathF.Tau)
	{
		var pts = new Vector2[n];
		for (int i = 0; i < n; i++)
		{
			// voller Kreis ohne doppelten Endpunkt (sonst zerlegt Godot das Polygon nicht und zeichnet nichts)
			bool full = to - from >= MathF.Tau - 1e-4f;
			float a = from + (to - from) * i / (full ? n : n - 1);
			pts[i] = c + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
		}
		return pts;
	}

	public override void _Draw()
	{
		float s = MathF.Min(Size.X, Size.Y);
		var o = (Size - new Vector2(s, s)) / 2f;
		Vector2 P(float x, float y) => o + new Vector2(x * s, y * s);
		var sp = _spec;
		DrawRect(new Rect2(o, new Vector2(s, s)), Backs[sp.Background % Backs.Length]);
		// Lichtschein hinter dem Kopf
		DrawColoredPolygon(Ellipse(P(0.5f, 0.42f), 0.36f * s, 0.36f * s), new Color(1, 1, 1, 0.06f));
		var skin = Skins[sp.Skin % Skins.Length];
		var hair = Hairs[sp.HairColor % Hairs.Length];
		// Anzug (Schultern) und Kragen
		var suit = new Color(0.16f, 0.20f, 0.26f);
		DrawColoredPolygon(new[] { P(0.08f, 1f), P(0.14f, 0.80f), P(0.32f, 0.72f), P(0.68f, 0.72f), P(0.86f, 0.80f), P(0.92f, 1f) }, suit);
		DrawColoredPolygon(new[] { P(0.40f, 0.72f), P(0.50f, 0.84f), P(0.60f, 0.72f) }, suit.Lightened(0.25f));
		// Hals
		DrawRect(new Rect2(P(0.43f, 0.58f), new Vector2(0.14f * s, 0.16f * s)), skin.Darkened(0.12f));
		// Haare hinten (lang)
		if (sp.Hair == 3) DrawColoredPolygon(Ellipse(P(0.5f, 0.50f), 0.24f * s, 0.26f * s), hair.Darkened(0.15f));
		// Gesicht
		DrawColoredPolygon(Ellipse(P(0.5f, 0.42f), 0.18f * s, 0.22f * s), skin);
		// Haare oben
		switch (sp.Hair % 5)
		{
			case 0: DrawColoredPolygon(Ellipse(P(0.5f, 0.32f), 0.19f * s, 0.13f * s, 24, MathF.PI, MathF.Tau), hair); break;
			case 1: DrawColoredPolygon(Ellipse(P(0.5f, 0.30f), 0.20f * s, 0.15f * s, 24, MathF.PI * 1.05f, MathF.Tau * 0.97f), hair); break;
			case 2: DrawRect(new Rect2(P(0.33f, 0.18f), new Vector2(0.34f * s, 0.08f * s)), hair); break;
			case 3: DrawColoredPolygon(Ellipse(P(0.5f, 0.31f), 0.21f * s, 0.15f * s, 24, MathF.PI, MathF.Tau), hair); break;
			default: break;   // kahl
		}
		// Augen
		var eye = EyeCols[sp.Eyes % EyeCols.Length];
		foreach (float ex in new[] { 0.43f, 0.57f })
		{
			DrawCircle(P(ex, 0.42f), 0.026f * s, Colors.White);
			DrawCircle(P(ex, 0.42f), 0.014f * s, eye);
		}
		// Mund
		DrawLine(P(0.45f, 0.54f), P(0.55f, 0.54f), skin.Darkened(0.4f), MathF.Max(1f, 0.012f * s));
		if (sp.Scar) DrawLine(P(0.58f, 0.34f), P(0.62f, 0.48f), skin.Darkened(0.45f), MathF.Max(1f, 0.010f * s));
		// Helm: Ring um den Kopf mit Visierlinie
		if (sp.Helmet)
		{
			DrawArc(P(0.5f, 0.42f), 0.27f * s, MathF.PI * 0.95f, MathF.Tau + MathF.PI * 0.05f, 32, new Color(0.70f, 0.76f, 0.82f), 0.05f * s);
			DrawLine(P(0.26f, 0.42f), P(0.74f, 0.42f), new Color(0.4f, 0.8f, 1f, 0.35f), MathF.Max(1f, 0.012f * s));
		}
		// Rangabzeichen: Streifen auf der Brust
		var ins = Insignias[sp.Insignia % Insignias.Length];
		int bars = Math.Clamp(Rank / 2 + 1, 1, 5);
		for (int i = 0; i < bars; i++)
			DrawRect(new Rect2(P(0.66f, 0.86f - i * 0.03f), new Vector2(0.12f * s, 0.018f * s)), ins);
		DrawRect(new Rect2(o, new Vector2(s, s)), new Color(1, 1, 1, 0.25f), false, 2f);
	}
}
