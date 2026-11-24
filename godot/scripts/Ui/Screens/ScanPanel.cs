using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Flight;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// UI-Umbau (Nutzerwunsch nach Interstellar Pilot 2): Scanliste des Sektors hinter „☰ Sektor“ unten links — alles Sichtbare nach Entfernung, Filter oben,
/// Antippen einer Zeile wählt das Ziel (und schließt die Liste), Halten startet den Autopiloten; unten Ziel ✓ / 📡 Funk / Autopilot für die gewählte Zeile.
/// Inhalt aus <see cref="SectorScan"/> (Core). Liegt in der Flug-HUD-Schicht.
/// </summary>
public sealed partial class ScanPanel : Control
{
	public event Action<ScanContact>? Select, Autopilot, Hail;
	public event Action? CloseRequested;
	List<ScanContact> _items = new();
	string _sig = "";
	string _filter = "all";
	(ScanKind Kind, int Id)? _selected;
	KitScroll _scroll = null!;
	VBoxContainer _box = null!;
	Label _title = null!, _hint = null!;
	readonly List<KitButton> _chips = new();
	KitButton _btnTarget = null!, _btnHail = null!, _btnAuto = null!;
	Control _bg = null!;
	string _sector = "";

	static readonly (string Key, string Label)[] Filters = { ("all", "Alle"), ("station", "Stationen"), ("ship", "Schiffe"), ("gate", "Tore"), ("rock", "Felsen"), ("wreck", "Wracks") };

	public ScanPanel()
	{
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
	}

	public override void _Ready()
	{
		_bg = new Panel { MouseFilter = MouseFilterEnum.Ignore };
		((Panel)_bg).AddThemeStyleboxOverride("panel", Box(new Color(0.03f, 0.06f, 0.12f, 0.82f), new Color(Cyan, 0.6f), 12, 2, 0));
		AddChild(_bg);
		_title = At(this, Text("", 21, Cyan), 16, 8, 300, 28);
		_hint = At(this, Text("", 14, Dim), 16, 36, 300, 20);
		float x = 12;
		foreach (var (key, label) in Filters)
		{
			var b = new KitButton(label, new Vector2(64, 44)) { FontSize = 14 };
			string k = key;
			b.Pressed += () => { _filter = k; _sig = ""; Rebuild(); };
			AddChild(b);
			_chips.Add(b);
			x += 70;
		}
		_scroll = new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		AddChild(_scroll);
		_box = new VBoxContainer();
		_box.AddThemeConstantOverride("separation", 6);
		_scroll.AddChild(_box);
		_btnTarget = new KitButton("Ziel", new Vector2(100, 60)) { FontSize = 16 };
		_btnTarget.Pressed += () => { if (Current() is { } c) Select?.Invoke(c); };
		_btnHail = new KitButton("📡 Funk", new Vector2(100, 60)) { FontSize = 16 };
		_btnHail.Pressed += () => { if (Current() is { } c) Hail?.Invoke(c); };
		_btnAuto = new KitButton("Autopilot", new Vector2(100, 60)) { FontSize = 16 };
		_btnAuto.Pressed += () => { if (Current() is { } c) Autopilot?.Invoke(c); };
		AddChild(_btnTarget); AddChild(_btnHail); AddChild(_btnAuto);
		LayoutParts();
	}

	public override void _Notification(int what) { if (what == NotificationResized && _scroll is not null) { LayoutParts(); _sig = ""; Rebuild(); } }

	void LayoutParts()
	{
		_bg.Position = Vector2.Zero; _bg.Size = Size;
		_title.Size = new Vector2(Size.X - 32, 28); _hint.Size = new Vector2(Size.X - 32, 20);
		// Filter-Chips in einer Zeile (bei schmaler Liste zwei Zeilen)
		float cw = MathF.Floor((Size.X - 24f - 5 * 6f) / 6f), y = 62f;
		bool twoRows = cw < 58f;
		if (twoRows) cw = MathF.Floor((Size.X - 24f - 2 * 6f) / 3f);
		for (int i = 0; i < _chips.Count; i++)
		{
			int col = twoRows ? i % 3 : i, row = twoRows ? i / 3 : 0;
			_chips[i].Position = new Vector2(12 + col * (cw + 6f), y + row * 50f);
			_chips[i].CustomMinimumSize = new Vector2(cw, 44); _chips[i].Size = new Vector2(cw, 44);
		}
		float listTop = y + (twoRows ? 100f : 50f), bh = 60f;
		_scroll.Position = new Vector2(10, listTop);
		_scroll.Size = new Vector2(Size.X - 20, MathF.Max(60f, Size.Y - listTop - bh - 18f));
		float bw = MathF.Floor((Size.X - 20f - 2 * 8f) / 3f);
		_btnTarget.Position = new Vector2(10, Size.Y - bh - 8); _btnHail.Position = new Vector2(18 + bw, Size.Y - bh - 8); _btnAuto.Position = new Vector2(26 + 2 * bw, Size.Y - bh - 8);
		foreach (var b in new[] { _btnTarget, _btnHail, _btnAuto }) { b.CustomMinimumSize = new Vector2(bw, bh); b.Size = new Vector2(bw, bh); }
	}

	ScanContact? Current() => _selected is { } s ? _items.FirstOrDefault(c => c.Kind == s.Kind && c.Id == s.Id) : null;

	static bool Pass(string f, ScanContact c) => f switch
	{
		"station" => c.Kind == ScanKind.Station, "ship" => c.Kind == ScanKind.Ship, "gate" => c.Kind == ScanKind.Gate,
		"rock" => c.Kind == ScanKind.Rock, "wreck" => c.Kind == ScanKind.Wreck, _ => true,
	};

	/// <summary>Neue Daten (jede HUD-Aktualisierung, solange offen). <paramref name="selected"/> = aktuelles Ziel aus Main.</summary>
	public void SetData(string sector, List<ScanContact> items, (ScanKind, int)? selected)
	{
		_items = items; _sector = sector;
		if (selected is not null) _selected = selected;
		// Entfernungen ändern sich ständig — neu aufbauen nur bei anderer Zusammensetzung oder grob anderen Abständen (alle 50 m)
		string sig = _filter + "|" + string.Join(";", items.Where(c => Pass(_filter, c)).Select(c => $"{(int)c.Kind}:{c.Id}:{(int)(c.Distance / 50f)}:{c.Hostile}")) + "|" + _selected;
		if (sig == _sig) return;
		_sig = sig;
		Rebuild();
	}

	void Rebuild()
	{
		if (_box is null) return;
		foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
		_title.Text = $"In {_sector} · {_items.Count}";
		_hint.Text = "tippen = Ziel · halten = Autopilot";
		for (int i = 0; i < _chips.Count; i++) _chips[i].State = Filters[i].Key == _filter ? KitButtonState.On : KitButtonState.Normal;
		float w = MathF.Max(100f, _scroll.Size.X - 14f);
		foreach (var c in _items.Where(c => Pass(_filter, c)))
		{
			var row = new KitButton(c.Name, new Vector2(w, 58)) { AlignLeft = true, FontSize = 17, Sub = c.Info, Detail = DistanceText.Format(c.Distance) };
			bool sel = _selected is { } s && s.Kind == c.Kind && s.Id == c.Id;
			row.State = sel ? KitButtonState.On : c.Hostile ? KitButtonState.Warn : KitButtonState.Normal;
			row.Symbol = g => DrawKind(g, c);
			var cc = c;
			ulong down = 0;
			row.Down += () => down = Time.GetTicksMsec();
			row.Pressed += () =>
			{
				_selected = (cc.Kind, cc.Id);
				if (Time.GetTicksMsec() - down >= 550) Autopilot?.Invoke(cc); else Select?.Invoke(cc);
			};
			_box.AddChild(row);
		}
		var cur = Current();
		_btnTarget.Text = cur is null ? "Ziel" : "Ziel ✓";
		foreach (var b in new[] { _btnTarget, _btnHail, _btnAuto }) b.State = cur is null ? KitButtonState.Disabled : KitButtonState.Normal;
		_btnHail.State = cur is { Kind: ScanKind.Ship or ScanKind.Station } ? KitButtonState.Normal : KitButtonState.Disabled;
		_btnHail.Sub = cur is { Kind: ScanKind.Ship or ScanKind.Station } ? "" : "kein Funk";
	}

	static void DrawKind(KitButton g, ScanContact c)
	{
		var p = new Vector2(22, g.Size.Y / 2f);
		var col = c.Hostile ? Red : c.Kind switch { ScanKind.Station => Cyan, ScanKind.Rock => new Color(0.79f, 0.55f, 0.35f), ScanKind.Gate => new Color(0.7f, 0.55f, 1f), ScanKind.Wreck => Dim, _ => Green };
		switch (c.Kind)
		{
			case ScanKind.Station: g.DrawColoredPolygon(new[] { p + new Vector2(0, -9), p + new Vector2(9, 0), p + new Vector2(0, 9), p + new Vector2(-9, 0) }, col); break;
			case ScanKind.Ship: g.DrawColoredPolygon(new[] { p + new Vector2(0, -9), p + new Vector2(8, 8), p + new Vector2(-8, 8) }, col); break;
			case ScanKind.Rock: g.DrawCircle(p, 8, col); break;
			case ScanKind.Gate: g.DrawArc(p, 9, 0, Mathf.Tau, 6, col, 2.5f); break;
			default: g.DrawLine(p + new Vector2(-7, -7), p + new Vector2(7, 7), col, 3); g.DrawLine(p + new Vector2(7, -7), p + new Vector2(-7, 7), col, 3); break;
		}
	}

	// Selbsttest
	public int TestRowCount => _box?.GetChildCount() ?? 0;
	public KitButton? TestRow(string namePart) => _box?.GetChildren().OfType<KitButton>().FirstOrDefault(b => b.Text.Contains(namePart));
	public KitButton TestHailButton => _btnHail;
	public KitButton TestAutopilotButton => _btnAuto;
}
