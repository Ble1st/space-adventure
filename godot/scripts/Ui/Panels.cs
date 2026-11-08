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
		var mid = new Vec2(all.Average(p => p.X), all.Average(p => p.Y));
		return center + new Vector2((s.MapPosition.X - mid.X) * scale, (s.MapPosition.Y - mid.Y) * scale);
	}

	public override void _Notification(int what)
	{
		if (what == NotificationResized && _close != null) _close.Position = new Vector2(Size.X - 170, 16);
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (_u == null || !Pointer.TryRead(@event, out _, out var pos, out bool pressed, out bool motion) || motion || pressed) return;
		var center = Size / 2f;
		foreach (var s in _u.Sectors.Values)
			if (Place(s, center, 220f).DistanceTo(pos) < 44f) { SectorSelected?.Invoke(s.Id); Visible = false; Closed?.Invoke(); break; }
		AcceptEvent();
	}

	public override void _Draw()
	{
		if (_u == null) return;
		DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.02f, 0.03f, 0.08f, 0.94f));
		var font = ThemeDB.FallbackFont;
		DrawString(font, new Vector2(24, 40), "Sternenkarte – Sektor antippen für Autopilot", HorizontalAlignment.Left, -1, 24, Colors.White);
		var center = Size / 2f;

		foreach (var g in _u.Gates.Values)
			if (g.Id.Value < g.Target.Value && _u.Gates.TryGetValue(g.Target, out var far))
				DrawLine(Place(_u.Sectors[g.Sector], center, 220f), Place(_u.Sectors[far.Sector], center, 220f), new Color(0.3f, 0.6f, 1f, g.Open ? 0.8f : 0.2f), 3f);

		foreach (var s in _u.Sectors.Values)
		{
			var p = Place(s, center, 220f);
			var col = FactionColor(s.Owner);
			DrawCircle(p, 30f, new Color(col, 0.35f));
			DrawArc(p, 30f, 0, Mathf.Tau, 40, col, 3f);
			if (s.Id == _player) DrawArc(p, 38f, 0, Mathf.Tau, 40, Colors.White, 3f);
			if (s.Id == _target) DrawArc(p, 46f, 0, Mathf.Tau, 40, Colors.Yellow, 3f);
			DrawString(font, p + new Vector2(-60, 56), s.Name, HorizontalAlignment.Center, 120, 18, Colors.White);
			DrawString(font, p + new Vector2(-60, 76), $"{s.Stations.Count} Stationen", HorizontalAlignment.Center, 120, 14, new Color(0.7f, 0.8f, 1f));
		}
	}

	public static Color FactionColor(FactionId f) => (f.Value % 4) switch
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
		sb.AppendLine($"Credits: Stationen {snap.StationCredits:0}   Schiffe {snap.ShipCredits:0}");
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

/// <summary>Stationsansicht beim Andocken (derzeit nur Information; Handel setzt hier später an).</summary>
public partial class DockPanel : PanelContainer
{
	public event Action? Undock;
	public event Action? SellAll;
	readonly Label _label = new();
	readonly TouchButton _undock = new("Abdocken", new Vector2(200, 56));
	readonly TouchButton _sell = new("Alles verkaufen", new Vector2(240, 56));

	public DockPanel()
	{
		AddThemeStyleboxOverride("panel", UiStyle.Panel(0.94f));
		Visible = false;
		var box = new VBoxContainer();
		_label.AddThemeFontSizeOverride("font_size", 18);
		box.AddChild(_label);
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 12);
		row.AddChild(_sell); row.AddChild(_undock);
		box.AddChild(row);
		AddChild(box);
		_undock.Pressed += () => Undock?.Invoke();
		_sell.Pressed += () => SellAll?.Invoke();
	}

	public void Open(Universe u, Station st, Ship? ship = null)
	{
		var sb = new StringBuilder();
		string type = u.StationTypes.TryGetValue(st.TypeKey, out var t) ? t.Name : st.TypeKey;
		sb.AppendLine($"{st.Name}  ({type})");
		sb.AppendLine($"Fraktion: {u.Factions[st.Owner].Name}   Kasse: {st.Credits:0}");
		sb.AppendLine();
		sb.AppendLine($"{"Ware",-10}{"Bestand",9}{"Soll",7}{"Preis",9}");
		foreach (var (gid, e) in st.Market.OrderBy(k => k.Key.Value))
			sb.AppendLine($"{u.Goods[gid].Name,-10}{e.Amount,9:0}{e.Target,7:0}{e.Price,9:0.0}");
		if (ship != null)
		{
			sb.AppendLine();
			sb.AppendLine($"Deine Fracht ({ship.UsedVolume(u):0}/{ship.CargoCapacity:0}):  Credits {ship.Credits:0}");
			if (ship.Cargo.Count == 0) sb.AppendLine("  leer");
			foreach (var (g, units) in ship.Cargo)
				sb.AppendLine($"  {u.Goods[g].Name,-10}{units,7:0}" + (st.Market.TryGetValue(g, out var e) ? $"   Kurs {e.Price:0.0}" : "   (wird hier nicht gekauft)"));
			sb.AppendLine($"  Erlös bei Verkauf: {SpaceSim.Core.Trading.TradeActions.Quote(u, ship, st):0}");
		}
		_label.Text = sb.ToString();
		Visible = true;
	}
}

public partial class MenuPanel : PanelContainer
{
	public event Action<string>? Action;

	public MenuPanel()
	{
		AddThemeStyleboxOverride("panel", UiStyle.Panel(0.95f));
		Visible = false;
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 10);
		foreach (var (id, text) in new[] { ("save", "Speichern"), ("load", "Laden"), ("new", "Neues Spiel"), ("close", "Zurück") })
		{
			var b = new TouchButton(text, new Vector2(260, 56));
			string key = id;
			b.Pressed += () => Action?.Invoke(key);
			box.AddChild(b);
		}
		AddChild(box);
	}
}
