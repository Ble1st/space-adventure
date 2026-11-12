using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using SpaceAdventure.Game;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure.Ui;

static class UiStyle
{
	public static StyleBoxFlat Panel(float alpha = 0.88f)
	{
		var s = new StyleBoxFlat { BgColor = new Color(0.04f, 0.07f, 0.13f, alpha), BorderColor = new Color(0.4f, 0.6f, 0.9f, 0.8f) };
		s.SetCornerRadiusAll(12); s.SetBorderWidthAll(2); s.SetContentMarginAll(16);
		return s;
	}
}

/// <summary>Sternenkarte: Sektoren als Knoten, Gates als Linien. Antippen eines Sektors setzt ihn als Autopilot-Ziel.</summary>
public partial class MapOverlay : Control
{
	public event Action<SectorId>? SectorSelected;
	public event Action? Closed;

	Universe? _u;
	SectorId _player;
	SectorId? _target;
	readonly TouchButton _close = new("Schließen", new Vector2(150, 56));

	public MapOverlay()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		_close.Pressed += () => { Visible = false; Closed?.Invoke(); };
		AddChild(_close);
	}

	public void Open(Universe u, SectorId player, SectorId? target) { _u = u; _player = player; _target = target; Visible = true; QueueRedraw(); }

	Vector2 Place(Sector s, Vector2 center, float scale)
	{
		var all = _u!.Sectors.Values.Select(x => x.MapPosition).ToList();
		var mid = new Vec2((all.Max(p => p.X) + all.Min(p => p.X)) / 2f, (all.Max(p => p.Y) + all.Min(p => p.Y)) / 2f);
		return center + new Vector2((s.MapPosition.X - mid.X) * scale, (s.MapPosition.Y - mid.Y) * scale);
	}

	/// <summary>Phase 21: Abstand je Kartenfeld passend zur Fenstergröße (vorher fest 220 px — bei 11 Sektoren ragte die Karte aus dem Bild).</summary>
	float MapScale()
	{
		var all = _u!.Sectors.Values.Select(x => x.MapPosition).ToList();
		float rx = all.Max(p => p.X) - all.Min(p => p.X), ry = all.Max(p => p.Y) - all.Min(p => p.Y);
		float sx = rx > 0.001f ? (Size.X - 200f) / rx : 220f;
		float sy = ry > 0.001f ? (Size.Y - 230f) / ry : 220f;
		return Mathf.Clamp(Mathf.Min(sx, sy), 90f, 220f);
	}

	/// <summary>Mittelpunkt der Karte (unter der Überschrift).</summary>
	Vector2 MapCenter() => Size / 2f + new Vector2(0, 20f);

	public override void _Notification(int what)
	{
		if (what == NotificationResized && _close != null) _close.Position = new Vector2(Size.X - 170, 16);
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (_u == null || !Pointer.TryRead(@event, out _, out var pos, out bool pressed, out bool motion) || motion || pressed) return;
		var center = MapCenter();
		float scale = MapScale();
		foreach (var s in _u.Sectors.Values)
			if (Place(s, center, scale).DistanceTo(pos) < 44f) { SectorSelected?.Invoke(s.Id); Visible = false; Closed?.Invoke(); break; }
		AcceptEvent();
	}

	public override void _Draw()
	{
		if (_u == null) return;
		DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.02f, 0.03f, 0.08f, 1f));   // Gerätetest: deckend (das HUD schien durch)
		var font = ThemeDB.FallbackFont;
		DrawString(font, new Vector2(24, 40), "Sternenkarte – Sektor antippen für Autopilot", HorizontalAlignment.Left, -1, 24, Colors.White);
		var center = MapCenter();
		float scale = MapScale();

		var player = _u.Ships.GetValueOrDefault(_u.PlayerShip);
		foreach (var g in _u.Gates.Values)
			if (g.Id.Value < g.Target.Value && _u.Gates.TryGetValue(g.Target, out var far))
			{
				// Phase 10: für dich gesperrt (Kontrollpunkt) rot, eingeschränkt orange, geschlossen blass
				bool denied = player is not null && (SpaceSim.Core.Simulation.AccessInfo.ForGate(_u, g, player).Denied || SpaceSim.Core.Simulation.AccessInfo.ForGate(_u, far, player).Denied);
				var col = !g.Open ? new Color(0.3f, 0.6f, 1f, 0.2f) : denied ? new Color(1f, 0.35f, 0.3f, 0.9f)
					: g.State == GateState.Restricted ? new Color(1f, 0.7f, 0.2f, 0.85f) : new Color(0.3f, 0.6f, 1f, 0.8f);
				DrawLine(Place(_u.Sectors[g.Sector], center, scale), Place(_u.Sectors[far.Sector], center, scale), col, 3f);
			}

		var layout = new SpaceAdventure.Ui.Kit.LabelLayout(new Rect2(new Vector2(0, 60), Size - new Vector2(0, 60)), font);
		foreach (var s in _u.Sectors.Values) layout.ReserveCircle(Place(s, center, scale), 40f);
		foreach (var s in _u.Sectors.Values)
		{
			var p = Place(s, center, scale);
			var col = FactionColor(s.Owner);
			DrawCircle(p, 30f, new Color(0.03f, 0.05f, 0.1f, 1f));   // Gerätetest: deckender Knoten — die Torlinien laufen nicht mehr über ihn
			DrawCircle(p, 30f, new Color(col, 0.35f));
			DrawArc(p, 30f, 0, Mathf.Tau, 40, col, 3f);
			if (s.Id == _player) DrawArc(p, 38f, 0, Mathf.Tau, 40, Colors.White, 3f);
			if (s.Id == _target) DrawArc(p, 46f, 0, Mathf.Tau, 40, Colors.Yellow, 3f);
			// Phase 21: Name ohne „Sektor “ und kleiner, damit 11 Sektoren nebeneinander lesbar bleiben
			// Gerätetest: Name und Stationszahl kollisionsfrei neben dem Knoten (LabelLayout)
			layout.Draw(this, p, s.Name.Replace("Sektor ", "") + $" · {s.Stations.Count} St.", 14, Colors.White, 40f, 160f);
		}
	}

	/// <summary>Farbe einer Fraktion; Phase 21: herrenlose Sektoren (Fraktion 0) grau.</summary>
	public static Color FactionColor(FactionId f) => f.Value == 0 ? new Color(0.6f, 0.6f, 0.65f) : (f.Value % 4) switch
	{
		1 => new Color(0.35f, 0.7f, 1f), 2 => new Color(1f, 0.7f, 0.3f), 3 => new Color(0.5f, 1f, 0.5f), _ => new Color(0.9f, 0.5f, 0.9f),
	};
}

/// <summary>Debug-Ansicht der Wirtschaft: Preise je Ware, Bestände, Flotten und die stärksten Handelsrouten.</summary>
public partial class EconomyPanel : PanelContainer
{
	readonly Label _label = new() { Text = "" };
	double _next;

	public EconomyPanel()
	{
		AddThemeStyleboxOverride("panel", UiStyle.Panel());
		MouseFilter = MouseFilterEnum.Ignore;
		_label.AddThemeFontSizeOverride("font_size", 13);   // über 20 Waren: kompakt, damit alles auf den Bildschirm passt
		AddChild(_label);
		Visible = false;
	}

	public void Refresh(GameSession session, bool force = false)
	{
		double now = Time.GetTicksMsec() / 1000.0;
		if (!Visible || (!force && now < _next)) return;
		_next = now + 1.0;

		var u = session.U;
		var snap = EconomyStats.Snapshot(u);
		var sb = new StringBuilder();
		sb.AppendLine($"WIRTSCHAFT   Tick {snap.Tick}");
		sb.AppendLine($"Stationen {snap.Stations}   Händler {snap.Traders}   Miner {snap.Miners}");
		sb.AppendLine($"Credits: Stationen {snap.StationCredits:0}   Schiffe {snap.ShipCredits:0}   Spieler {snap.PlayerCredits:0}   Kassen {snap.TreasuryCredits:0}");
		sb.AppendLine();
		sb.AppendLine($"{"Ware",-10}{"Preis",7}{"/Basis",8}{"Bestand",10}");
		foreach (var g in snap.Goods)
			sb.AppendLine($"{g.Name,-10}{g.AvgPrice,7:0.0}{g.PriceRatio,7:0.00}x{g.TotalStock,10:0}");
		sb.AppendLine();
		sb.AppendLine("Handelsrouten (Volumen):");
		foreach (var r in session.Routes.Top(5))
		{
			string from = r.Route.From.Value < 0 ? "Miner" : u.Stations.GetValueOrDefault(r.Route.From)?.Name ?? "(aufgegeben)";
			string to = u.Stations.GetValueOrDefault(r.Route.To)?.Name ?? "(aufgegeben)";
			sb.AppendLine($"{u.Goods[r.Route.Good].Name,-9} {Short(from)} → {Short(to)}  {r.Volume:0}");
		}
		_label.Text = sb.ToString();
	}

	static string Short(string s) => s.Length > 14 ? s[..14] : s;
}
