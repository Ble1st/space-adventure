using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Trading;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Dock-Bereich „Handel“ (UI.md Abschnitt 3): Zeilen kommen vollständig aus <see cref="IStationService.Offers"/>,
/// Aktionen laufen ausschließlich über <see cref="IStationService.Execute"/> („buy:&lt;id&gt;“, „sell:&lt;id&gt;“,
/// „sell_all“) – <see cref="TradeActions"/> wird von der Oberfläche nicht direkt aufgerufen. Liste/Kacheln
/// umschaltbar; Tippen auf eine Zeile klappt Menge (Stepper +/-/Max), Summe und Kaufen/Verkaufen auf; die
/// Ergebnismeldung (<see cref="ServiceResult.Message"/>) erscheint als Hinweis; rechts das Schiff mit Fracht,
/// Laderaum und Credits (echt). „Beste bekannte Verkaufsstelle“ je Ware aus <see cref="MarketInfo"/>, „Rückgängig“
/// und Preisverlauf-Trends sind Platzhalter.
/// </summary>
public sealed partial class TradeScreen : DockAreaScreen
{
	public override string Id => "dock.trade";
	protected override bool ShowShip => true;

	/// <summary>Wird nach jedem Execute ausgelöst (true = Erfolg) – Main spielt den Ton.</summary>
	public event Action<bool>? Executed;

	sealed class Row
	{
		public required GoodId Good;
		public required string Name;
		public required Color Color;
		public required float Price, Stock, Target, Held;
		public ServiceOffer Buy, Sell;
		public bool HasBuy, HasSell;
	}

	IStationService? _svc;
	readonly List<Row> _rows = new();
	bool _tiles;
	GoodId? _expanded;       // aufgeklappte Zeile im Listenmodus
	GoodId? _tileSel;        // gewählte Kachel im Kachelmodus
	float _qty = 1f;         // Menge des Editors
	string _message = "";
	bool _msgOk = true;
	int _scrollV;
	ScrollContainer? _scroll;

	public TradeScreen() => AreaId = DockAreas.Trade;

	protected override void Rebuild()
	{
		if (_scroll != null) _scrollV = _scroll.ScrollVertical;
		base.Rebuild();
		if (_scroll != null) _scroll.ScrollVertical = _scrollV;
	}

	protected override void BuildContent(Control inner)
	{
		_svc = Ctx.Services.TryGet(TradeService.ServiceKey, out var s) && s.IsAvailable(Ctx.U, Ctx.Station) ? s : null;
		LoadRows();
		float w = inner.Size.X > 0 ? inner.Size.X : 670f;

		// ── Kopfzeile: Hinweis (Ergebnismeldung) links, Liste/Kacheln rechts ──
		var msg = At(inner, Text(_message.Length > 0 ? _message : "Markt der Station – Zeile antippen für Menge und Kauf/Verkauf", 15,
			_message.Length > 0 ? (_msgOk ? Green : Red) : new Color(Dim, 0.85f)), 0, 0, w - 200, 40);
		var listBtn = At(inner, new KitButton("Liste", new Vector2(84, 40)) { FontSize = 16 }, w - 186, 0);
		var tileBtn = At(inner, new KitButton("Kacheln", new Vector2(92, 40)) { FontSize = 16 }, w - 96, 0);
		listBtn.State = _tiles ? KitButtonState.Normal : KitButtonState.On;
		tileBtn.State = _tiles ? KitButtonState.On : KitButtonState.Normal;
		listBtn.Pressed += () => { if (_tiles) { _tiles = false; _tileSel = null; Invalidate(); } };
		tileBtn.Pressed += () => { if (!_tiles) { _tiles = true; Invalidate(); } };

		// ── Fußzeile: Alles verkaufen + Rückgängig (Platzhalter) ──
		float fy = inner.Size.Y - 56;
		var sellAll = At(inner, new KitButton("Alles verkaufen", new Vector2(220, 52)) { FontSize = 18 }, 0, fy);
		bool anyCargo = Ctx.Ship.Cargo.Values.Any(v => v > 0f);
		if (!anyCargo) { sellAll.State = KitButtonState.Disabled; sellAll.Sub = "Fracht leer"; }
		else sellAll.Pressed += () => Execute(TradeService.SellAll, 0f);
		At(inner, new KitButton("Rückgängig", new Vector2(170, 52)) { FontSize = 18, State = KitButtonState.Disabled, Sub = UiPlaceholders.Soon }, 232, fy);   // Text: UiPlaceholders.UndoSoon

		if (_svc == null)
		{
			At(inner, Text("An dieser Station gibt es keinen Handel.", 18, Dim), 0, 60, w, 30);
			return;
		}

		if (!_tiles)
		{
			// ── Listenmodus: eine Zeile je Ware, antippen klappt auf ──
			_scroll = At(inner, new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, 0, 46, w, fy - 46 - 8);
			var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			box.AddThemeConstantOverride("separation", 6);
			_scroll.AddChild(box);
			foreach (var row in _rows)
			{
				bool open = _expanded is { } g && g == row.Good;
				var r = new TradeRow(this, row, open, w - 10);
				r.Tapped += () => { _expanded = open ? null : row.Good; if (!open) _qty = DefaultQty(row); Invalidate(); };
				r.EditorBuy += q => Buy(row, q);
				r.EditorSell += q => Sell(row, q);
				r.EditorQty += q => _qty = q;
				if (open) r.SetQty(_qty);
				box.AddChild(r);
			}
		}
		else
		{
			// ── Kachelmodus: Kacheln oben, Editor-Blatt unten für die gewählte Kachel ──
			float sheetH = _tileSel != null ? 158f : 0f;
			_scroll = At(inner, new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, 0, 46, w, fy - 46 - 8 - sheetH);
			var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			grid.AddThemeConstantOverride("h_separation", 8);
			grid.AddThemeConstantOverride("v_separation", 8);
			_scroll.AddChild(grid);
			foreach (var row in _rows)
			{
				bool sel = _tileSel is { } g && g == row.Good;
				var t = new TradeTile(row, sel);
				t.Tapped += () => { _tileSel = sel ? null : row.Good; if (!sel) _qty = DefaultQty(row); Invalidate(); };
				grid.AddChild(t);
			}
			if (_tileSel is { } sg && _rows.FirstOrDefault(r => r.Good == sg) is { } selRow)
			{
				// Blatt mit freier Platzierung (PanelContainer würde Titel und Editor strecken/übereinander legen)
				var sheet = CardBg(inner, 0, fy - sheetH - 4, w, sheetH, new Color(0.06f, 0.1f, 0.18f, 0.98f), Cyan, 12, 2);
				At(sheet, Text($"{selRow.Name} · Preis {selRow.Price:0.0}", 18, White), 10, 4, w - 20, 24);
				var ed = new Editor(this, selRow, w - 12);
				ed.SetQty(_qty);
				ed.Buy += q => Buy(selRow, q);
				ed.Sell += q => Sell(selRow, q);
				ed.QtyChanged += q => _qty = q;
				At(sheet, ed, 6, 32);
			}
		}
	}

	void LoadRows()
	{
		_rows.Clear();
		if (_svc == null) return;
		var u = Ctx.U; var st = Ctx.Station;
		var byGood = new Dictionary<GoodId, Row>();
		foreach (var o in _svc.Offers(u, st, Ctx.Ship))
		{
			if (o.Good.Value == 0) continue;   // „alles verkaufen“ ist Fußzeile, keine Ware
			if (!byGood.TryGetValue(o.Good, out var row))
			{
				row = new Row
				{
					Good = o.Good,
					Name = u.Goods.TryGetValue(o.Good, out var g) ? g.Name : o.Good.Value.ToString(),
					Color = GoodColorOf(u, o.Good),
					Price = o.Price,
					Stock = o.Stock,
					Target = st.Market.TryGetValue(o.Good, out var e) ? e.Target : 0f,
					Held = o.Held,
				};
				byGood[o.Good] = row;
			}
			if (o.Key.StartsWith(TradeService.Buy + ':', StringComparison.Ordinal)) { row.Buy = o; row.HasBuy = true; }
			else if (o.Key.StartsWith(TradeService.Sell + ':', StringComparison.Ordinal)) { row.Sell = o; row.HasSell = true; }
		}
		_rows.AddRange(byGood.Values.OrderBy(r => r.Good.Value));
	}

	static float DefaultQty(Row r) => Mathf.Clamp(Mathf.Max(1f, Mathf.Min(10f, r.Buy.Amount > 0f ? r.Buy.Amount : r.Held)), 1f, 999f);

	/// <summary>Führt ein Angebot über den Dienst aus und zeigt die Ergebnismeldung an.</summary>
	public ServiceResult Execute(string offer, float units)
	{
		if (_svc == null || Ctx == null) return new ServiceResult(false, 0f, 0f, "Kein Handelsdienst an dieser Station");
		var r = _svc.Execute(Ctx.U, Ctx.Station, Ctx.Ship, new ServiceRequest(offer, default, units));
		_message = r.Message;
		_msgOk = r.Success;
		Executed?.Invoke(r.Success);
		Invalidate();
		return r;
	}

	void Buy(Row row, float qty) => Execute($"{TradeService.Buy}:{row.Good.Value}", qty);
	void Sell(Row row, float qty) => Execute($"{TradeService.Sell}:{row.Good.Value}", qty);

	// ── Test-Hooks (Selbsttest, UiShots) ──

	/// <summary>Wie die Kauf-/Verkauf-Tasten: Execute über den Dienst.</summary>
	public ServiceResult TestExecute(string offer, float units) => Execute(offer, units);
	/// <summary>Klappt die erste Zeile auf (Screenshot).</summary>
	public void TestExpandFirst()
	{
		if (_rows.Count == 0) return;
		_expanded = _rows[0].Good;
		_qty = DefaultQty(_rows[0]);
		Invalidate();
	}
	/// <summary>Phase 16: baut die Zeilen neu auf (nach einem Stationswechsel im Test, wenn der Bereich schon offen ist).</summary>
	public void TestRebuild() => Invalidate();
	/// <summary>Phase 16: klappt die Zeile einer Ware auf (Screenshot).</summary>
	public void TestExpand(GoodId good)
	{
		if (_rows.FirstOrDefault(r => r.Good == good) is not { } row) return;
		_tiles = false; _tileSel = null; _expanded = good; _qty = DefaultQty(row);
		Invalidate();
	}
	/// <summary>Schaltet zwischen Liste und Kacheln um (Screenshot).</summary>
	public void TestSetTiles(bool tiles) { _tiles = tiles; if (!tiles) _tileSel = null; Invalidate(); }
	public void TestSelectFirstTile()
	{
		if (_rows.Count == 0) return;
		_tiles = true; _tileSel = _rows[0].Good; _qty = DefaultQty(_rows[0]);
		Invalidate();
	}
	public string TestMessage => _message;
	/// <summary>Phase 16: Annahme-Hinweis der Verkaufszeile einer Ware (null = keiner) und ob die Station jetzt etwas davon annimmt.</summary>
	public string? TestSellNote(GoodId good) => _rows.FirstOrDefault(r => r.Good == good)?.Sell.Note;
	public bool TestSellEnabled(GoodId good) => _rows.FirstOrDefault(r => r.Good == good) is { } r && r.Held >= 1f && r.Sell.Amount >= 1f;
	public int TestRowCount => _rows.Count;
	public bool TestTiles => _tiles;

	/// <summary>Eine Zeile der Liste: oben Ware/Preis/Bestand/an Bord, aufgeklappt der Mengen-Editor.</summary>
	sealed partial class TradeRow : Control
	{
		public event Action? Tapped;
		public event Action<float>? EditorBuy, EditorSell, EditorQty;

		readonly TradeScreen _owner;
		readonly Row _row;
		readonly bool _open;
		Editor? _editor;

		public TradeRow(TradeScreen owner, Row row, bool open, float w)
		{
			_owner = owner;
			_row = row; _open = open;
			MouseFilter = MouseFilterEnum.Stop;
			CustomMinimumSize = new Vector2(w, open ? 56 + 116 : 56);
			Size = CustomMinimumSize;
		}

		public void SetQty(float q) => _editor?.SetQty(q);

		public override void _Ready()
		{
			if (_open)
			{
				_editor = new Editor(_owner, _row, Size.X);
				_editor.Buy += q => EditorBuy?.Invoke(q);
				_editor.Sell += q => EditorSell?.Invoke(q);
				_editor.QtyChanged += q => EditorQty?.Invoke(q);
				AddChild(_editor);
				_editor.Position = new Vector2(0, 56);
			}
		}

		public override void _GuiInput(InputEvent @event)
		{
			if (!Pointer.TryRead(@event, out int idx, out var pos, out bool pressed, out bool motion)) return;
			if (!motion && !pressed && new Rect2(Vector2.Zero, new Vector2(Size.X, 56)).HasPoint(pos))
			{
				Tapped?.Invoke();
				AcceptEvent();
			}
		}

		public override void _Draw()
		{
			var u = _row;
			DrawRect(new Rect2(Vector2.Zero, Size), _open ? new Color(0.14f, 0.24f, 0.42f, 0.95f) : Card);
			if (_open) DrawRect(new Rect2(Vector2.Zero, Size), Cyan, false, 2f);
			var font = ThemeDB.FallbackFont;
			// Kopfzeile
			DrawRect(new Rect2(6, 12, 18, 18), u.Color);
			DrawString(font, new Vector2(34, 38), u.Name, HorizontalAlignment.Left, -1, 20, White);
			DrawString(font, new Vector2(220, 38), $"{u.Price:0.0}", HorizontalAlignment.Left, -1, 20, PriceColor(u));
			float fill = u.Target > 0f ? Mathf.Clamp(u.Stock / u.Target, 0f, 1.4f) / 1.4f : 0f;
			DrawRect(new Rect2(320, 18, 110, 10), new Color(0, 0, 0, 0.5f));
			DrawRect(new Rect2(320, 18, 110 * fill, 10), u.Color);
			DrawString(font, new Vector2(438, 38), $"{u.Stock:0}", HorizontalAlignment.Left, -1, 17, Dim);
			DrawString(font, new Vector2(500, 38), u.Held > 0f ? $"Bord {u.Held:0}" : "", HorizontalAlignment.Left, -1, 17, Cyan);
			// Phase 16: warum die Station die Ware nicht (mehr) annimmt — gedimmt unter dem Preis
			if (u.Sell.Note is { } note) DrawString(font, new Vector2(220, 53), note, HorizontalAlignment.Left, Size.X - 260, 12, SellNoteColor(u));
			DrawString(font, new Vector2(Size.X - 34, 38), _open ? "▾" : "▸", HorizontalAlignment.Left, -1, 18, Dim);
		}

		static Color PriceColor(Row r)
		{
			var e = new StockEntry { Amount = r.Stock, Target = r.Target };
			return MarketInfo.Classify(e) switch
			{
				MarketPressure.Scarce => Orange,   // knapp: Preis über Basis
				MarketPressure.Glut => Green,      // Überschuss: Preis unter Basis
				_ => White,
			};
		}
	}

	/// <summary>Phase 16: Farbe des Annahme-Hinweises — orange, wenn Ware an Bord ist und die Station nichts nimmt, sonst gedimmt.</summary>
	static Color SellNoteColor(Row r) => r.Held >= 1f && r.Sell.Amount < 1f ? new Color(Orange, 0.9f) : new Color(Dim, 0.8f);

	/// <summary>Eine Kachel des Kachelmodus.</summary>
	sealed partial class TradeTile : Control
	{
		public event Action? Tapped;
		readonly Row _row;
		readonly bool _sel;

		public TradeTile(Row row, bool sel)
		{
			_row = row; _sel = sel;
			MouseFilter = MouseFilterEnum.Stop;
			CustomMinimumSize = new Vector2(214, 140);
			Size = CustomMinimumSize;
		}

		public override void _GuiInput(InputEvent @event)
		{
			if (!Pointer.TryRead(@event, out _, out var pos, out bool pressed, out bool motion)) return;
			if (!motion && !pressed && new Rect2(Vector2.Zero, Size).HasPoint(pos)) { Tapped?.Invoke(); AcceptEvent(); }
		}

		public override void _Draw()
		{
			var r = _row;
			DrawRect(new Rect2(Vector2.Zero, Size), _sel ? new Color(0.12f, 0.22f, 0.4f, 0.97f) : new Color(0.04f, 0.07f, 0.13f, 0.9f));
			DrawRect(new Rect2(Vector2.Zero, Size), _sel ? Cyan : Edge, false, 2f);
			DrawRect(new Rect2(0, 0, Size.X, 7), r.Color);
			var font = ThemeDB.FallbackFont;
			DrawString(font, new Vector2(12, 34), r.Name, HorizontalAlignment.Left, -1, 20, White);
			DrawString(font, new Vector2(12, 74), $"{r.Price:0.0}", HorizontalAlignment.Left, -1, 30, White);
			DrawString(font, new Vector2(112, 70), UiPlaceholders.TrendSoon, HorizontalAlignment.Left, 96, 11, new Color(Dim, 0.7f));
			float fill = r.Target > 0f ? Mathf.Clamp(r.Stock / r.Target, 0f, 1.4f) / 1.4f : 0f;
			DrawRect(new Rect2(12, 90, 190, 8), new Color(0, 0, 0, 0.5f));
			DrawRect(new Rect2(12, 90, 190 * fill, 8), r.Color);
			DrawString(font, new Vector2(12, 124), $"Bestand {r.Stock:0}", HorizontalAlignment.Left, -1, 14, Dim);
			if (r.Sell.Note is { } note) DrawString(font, new Vector2(12, 112), note, HorizontalAlignment.Left, 190, 11, SellNoteColor(r));   // Phase 16
			if (r.Held > 0f)
			{
				DrawRect(new Rect2(128, 106, 74, 24), new Color(Cyan, 0.25f));
				DrawString(font, new Vector2(128, 124), $"Bord {r.Held:0}", HorizontalAlignment.Center, 74, 14, Cyan);
			}
		}
	}

	/// <summary>Mengen-Editor (Stepper −/Menge/+/Max, Summe, beste Verkaufsstelle, Kaufen/Verkaufen) – in Zeile und Kachel-Blatt.</summary>
	sealed partial class Editor : Control
	{
		public event Action<float>? Buy, Sell, QtyChanged;

		readonly TradeScreen _owner;
		readonly Row _row;
		readonly Label _qtyLabel;
		readonly KitButton _buyBtn, _sellBtn;
		float _qty = 1f;

		public Editor(TradeScreen owner, Row row, float w)
		{
			_owner = owner;
			_row = row;
			MouseFilter = MouseFilterEnum.Stop;
			CustomMinimumSize = new Vector2(w, 116);
			Size = CustomMinimumSize;

			At(this, Text("Menge", 16, Dim), 34, 22);
			var minus = At(this, new KitButton("−", new Vector2(52, 44)) { FontSize = 26 }, 100, 8);
			_qtyLabel = At(this, Text("1", 26, White, HorizontalAlignment.Center), 156, 8, 80, 44);
			var plus = At(this, new KitButton("+", new Vector2(52, 44)) { FontSize = 26 }, 240, 8);
			var max = At(this, new KitButton("Max", new Vector2(64, 44)) { FontSize = 16 }, 296, 8);
			_buyBtn = At(this, new KitButton("Kaufen", new Vector2(132, 44)) { FontSize = 19, State = KitButtonState.Ready }, 380, 8);
			_sellBtn = At(this, new KitButton("Verkaufen", new Vector2(132, 44)) { FontSize = 17, State = KitButtonState.Warn }, 518, 8);

			minus.Pressed += () => SetQty(_qty - 1f);
			plus.Pressed += () => SetQty(_qty + 1f);
			max.Pressed += () => SetQty(Mathf.Max(_row.Buy.Amount, _row.Held));
			_buyBtn.Pressed += () => Buy?.Invoke(_qty);
			_sellBtn.Pressed += () => Sell?.Invoke(_qty);
			RefreshButtons();
		}

		public void SetQty(float q)
		{
			_qty = Mathf.Clamp(q, 1f, 9999f);
			_qtyLabel.Text = $"{_qty:0.#}";
			QtyChanged?.Invoke(_qty);
			RefreshButtons();
			QueueRedraw();
		}

		void RefreshButtons()
		{
			_buyBtn.State = _row.Buy.Amount >= 1f && _qty >= 1f ? KitButtonState.Ready : KitButtonState.Disabled;
			// Phase 16: deaktiviert, wenn die Station nichts annimmt (Grund steht in der Zeile)
			_sellBtn.State = _row.Held >= 1f && _row.Sell.Amount >= 1f && _qty >= 1f ? KitButtonState.Warn : KitButtonState.Disabled;
		}

		public override void _Draw()
		{
			var font = ThemeDB.FallbackFont;
			float sum = _qty * _row.Price;
			var ctx = _owner.Ctx;
			var best = ctx != null ? MarketInfo.BestBuyerForGood(ctx.U, _row.Good, ctx.Station.Id) : null;
			string bestText = best != null
				? $"beste bekannte Verkaufsstelle: {best.Name} zahlt {best.Market[_row.Good].Price:0.0} ({best.Market[_row.Good].Price - _row.Price:+0.0;-0.0;0.0})"
				: "keine andere bekannte Verkaufsstelle";
			DrawString(font, new Vector2(34, 84), $"Summe {sum:0} Cr · {bestText}", HorizontalAlignment.Left, -1, 14, Dim);
			// Phase 16: Annahme-Hinweis des Verkaufsangebots statt des Trend-Platzhalters, wenn vorhanden
			if (_row.Sell.Note is { } note) DrawString(font, new Vector2(34, 106), $"Verkauf: {note}", HorizontalAlignment.Left, -1, 13, SellNoteColor(_row));
			else DrawString(font, new Vector2(34, 106), UiPlaceholders.TrendSoon, HorizontalAlignment.Left, -1, 13, new Color(Dim, 0.65f));
		}
	}
}
