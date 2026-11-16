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
public partial class MapOverlay : Control, SpaceAdventure.Ui.Kit.IMessageStrip
{
	/// <summary>Runde 37b (Prüfauflage A2): unterer Streifen ohne Knoten und Namen — Meldung und Hinweis liegen dort statt über der Karte.</summary>
	public const float StripHeight = 60f;
	public Rect2? MessageStripGlobal => IsVisibleInTree() ? new Rect2(GetGlobalTransform() * new Vector2(Safe.Left + 16f, Size.Y - Safe.Bottom - StripHeight - 8f), new Vector2(Size.X - Safe.Left - Safe.Right - 32f, StripHeight) * GetGlobalTransform().Scale.X) : null;

	/// <summary>Runde 37c (Prüfung M3): sichere Ränder der Karte in ihrer Schicht (Titel, Schließen, Meldungsstreifen und Karte liegen innerhalb).</summary>
	SpaceSim.Core.Ui.UiInsets Safe => SpaceAdventure.Ui.Kit.UiScale.Div(SpaceAdventure.Ui.Kit.UiScale.SafeInsets1(), IsInsideTree() ? MathF.Max(1f, GetGlobalTransform().Scale.X) : 1f);
	/// <summary>Nutzbare Fläche (innerhalb der sicheren Ränder).</summary>
	Rect2 Usable => new(new Vector2(Safe.Left, Safe.Top), Size - new Vector2(Safe.Left + Safe.Right, Safe.Top + Safe.Bottom));
	public event Action<SectorId>? SectorSelected;
	public event Action? Closed;

	Universe? _u;
	/// <summary>Runde 17: Verweis auf das alte Universum lösen (Sitzungswechsel).</summary>
	public void Forget() => _u = null;
	SectorId _player;
	SectorId? _target;
	readonly TouchButton _close = new("Schließen", new Vector2(150, 56));

	/// <summary>Runde 41 (Selbsttest): Zahl der gezeichneten Missionssymbole.</summary>
	public int TestMissionSymbols;
	/// <summary>Runde 42d (Handytest): Legendentext für den Auftragsmarker (goldener Diamant am Sektorknoten).</summary>
	public const string LegendText = "◆/★ = Auftrag";
	/// <summary>Runde 42d (Selbsttest): Legende wurde gezeichnet.</summary>
	public bool TestLegendDrawn;

	public MapOverlay()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		_close.Pressed += () => { Visible = false; Closed?.Invoke(); };
		AddChild(_close);
	}

	void PlaceClose() { var p = new Vector2(Size.X - Safe.Right - 170, Safe.Top + 16); if (_close.Position != p) _close.Position = p; }

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
		float sx = rx > 0.001f ? (Usable.Size.X - 200f) / rx : 220f;
		float sy = ry > 0.001f ? (Usable.Size.Y - 190f - StripHeight) / ry : 220f;   // Runde 37b: Platz für den Meldungsstreifen unten
		return Mathf.Clamp(Mathf.Min(sx, sy), 90f, 220f);
	}

	/// <summary>Runde 37b (Selbsttest): Bildpunkt eines Sektorknotens (für einen echten Tipp).</summary>
	public Vector2? TestSectorScreen(SectorId id) => _u is null || !_u.Sectors.TryGetValue(id, out var s) ? null : GetGlobalTransform() * Place(s, MapCenter(), MapScale());

	/// <summary>Mittelpunkt der Karte (unter der Überschrift).</summary>
	Vector2 MapCenter() => Usable.GetCenter() + new Vector2(0, 26f - StripHeight / 2f);

	public override void _Notification(int what)
	{
		if (what == NotificationResized && _close != null) PlaceClose();
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
		TestLegendDrawn = false;
		DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.02f, 0.03f, 0.08f, 1f));   // Gerätetest: deckend (das HUD schien durch)
		var font = ThemeDB.FallbackFont;
		SpaceAdventure.Ui.Kit.DrawnText.Clear(this);
		PlaceClose();
		var us = Usable;
		SpaceAdventure.Ui.Kit.UiKit.DrawFit(this, 0, font, us.Position + new Vector2(24, 40), "Sternenkarte – Sektor antippen für Autopilot", 24, us.Size.X - 48f - 170f, Colors.White);
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

		SpaceAdventure.Ui.Kit.DrawnText.Clear(this);
		var layout = new SpaceAdventure.Ui.Kit.LabelLayout(new Rect2(us.Position + new Vector2(0, 60), us.Size - new Vector2(0, 60 + StripHeight + 8f)), font);
		foreach (var s in _u.Sectors.Values) layout.ReserveCircle(Place(s, center, scale), 40f);
		// Runde 42d (Handytest): Legende „◆/★ = Auftrag“ für den Auftragsmarker — unter dem Titel, innerhalb der
		// sicheren Fläche; ihre Fläche wird für Sektorbeschriftungen freigehalten
		var legendPos = us.Position + new Vector2(24, 76);
		float legendW = font.GetStringSize(LegendText, HorizontalAlignment.Left, -1, 13).X + 8f;
		layout.Reserve(new Rect2(legendPos - new Vector2(4, 4), new Vector2(legendW, 22f)));
		foreach (var s in _u.Sectors.Values)
		{
			var p = Place(s, center, scale);
			var col = FactionColor(s.Owner);
			DrawCircle(p, 30f, new Color(0.03f, 0.05f, 0.1f, 1f));   // Gerätetest: deckender Knoten — die Torlinien laufen nicht mehr über ihn
			DrawCircle(p, 30f, new Color(col, 0.35f));
			DrawArc(p, 30f, 0, Mathf.Tau, 40, col, 3f);
			if (s.Id == _player) DrawArc(p, 38f, 0, Mathf.Tau, 40, Colors.White, 3f);
			if (s.Id == _target) DrawArc(p, 46f, 0, Mathf.Tau, 40, Colors.Yellow, 3f);
			// Runde 41: Missionsziel im Sektor — goldener Diamant am Knoten (einer je Auftrag, max 3)
			int mmi = 0;
			foreach (var _mk in SpaceSim.Core.Missions.MissionMarkers.For(_u).Where(mk => mk.Sector == s.Id))
			{
				if (mmi >= 3) break;
				var c = p + new Vector2(30f + mmi * 13f, -30f);
				float d = 7f;
				DrawColoredPolygon(new[] { c + new Vector2(0, -d), c + new Vector2(d, 0), c + new Vector2(0, d), c + new Vector2(-d, 0) }, Screens.FlightHud.MissionGold);
				mmi++;
			}
			TestMissionSymbols += mmi;
			// Phase 21: Name ohne „Sektor “ und kleiner, damit 11 Sektoren nebeneinander lesbar bleiben
			// Gerätetest: Name und Stationszahl kollisionsfrei neben dem Knoten (LabelLayout)
			layout.Draw(this, p, s.Name.Replace("Sektor ", "") + $" · {s.Stations.Count} St.", 14, Colors.White, 40f, 160f);
		}

		// Runde 42d (Handytest): Legende zeichnen (nach den Beschriftungen, damit sie in der Prüf-Tabelle steht)
		SpaceAdventure.Ui.Kit.UiKit.DrawFit(this, 90, font, legendPos, LegendText, 13, us.Size.X - 48f, Screens.FlightHud.MissionGold);
		TestLegendDrawn = true;
	}

	/// <summary>Farbe einer Fraktion; Phase 21: herrenlose Sektoren (Fraktion 0) grau.</summary>
	// Runde 38: die Spielerfraktion (Id-Zahl des Spielerakteurs, ab ActorId.FirstGenerated — nur der Spieler ist eine Fraktion mit erzeugter Id) ist cyan
	public static Color FactionColor(FactionId f) => f.Value == 0 ? new Color(0.6f, 0.6f, 0.65f) : f.Value >= ActorId.FirstGenerated ? new Color(0.3f, 0.95f, 1f) : (f.Value % 4) switch
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
