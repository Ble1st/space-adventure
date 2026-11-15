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
/// „sell_all“, „undo“ (Runde 39)) – <see cref="TradeActions"/> wird von der Oberfläche nicht direkt aufgerufen. Liste/Kacheln
/// umschaltbar; Tippen auf eine Zeile klappt Menge (Stepper +/-/Max), Summe und Kaufen/Verkaufen auf; die
/// Ergebnismeldung (<see cref="ServiceResult.Message"/>) erscheint als Hinweis; rechts das Schiff mit Fracht,
/// Laderaum und Credits (echt). „Beste bekannte Verkaufsstelle“ je Ware aus <see cref="MarketInfo"/>; die Fußzeile
/// „Rückgängig“ ist echt (zwei Taps über <see cref="KitConfirm"/>, Unterzeile = Buchung bzw. Sperrgrund, Ausführung
/// nur über Execute(„undo“)); Preisverlauf-Trends bleiben Platzhalter.
/// </summary>
public sealed partial class TradeScreen : DockAreaScreen, ISessionScreen
{
	public override string Id => "dock.trade";
	/// <summary>Gerätetest Phase 27: Auswahl und Meldung bei neuer Sitzung zurücksetzen.</summary>
	/// <summary>Runde 37c: Zurück-Taste — eine offene Zeile bzw. ein offenes Kachelblatt zuerst schließen (true = geschlossen).</summary>
	public bool CloseOpenRow()
	{
		if (_expanded is null && _tileSel is null) return false;
		_expanded = null; _tileSel = null;
		Invalidate();
		return true;
	}

	public void ResetSession() { _expanded = null; _tileSel = null; _qty = 10f; _message = ""; _msgOk = true; _hint.Clear(); }
	protected override bool ShowShip => true;

	/// <summary>Wird nach jedem Execute ausgelöst (true = Erfolg) – Main spielt den Ton.</summary>
	public event Action<bool>? Executed;
	ServiceOffer _sellAll;   // Phase 17: Angebot „Alles verkaufen“ (Menge = was es bewegt)
	ServiceOffer _undo;      // Runde 39: Angebot „undo“ (Menge 1 = ausführbar, Notiz = Sperrgrund)

	sealed class Row
	{
		public required GoodId Good;
		public required string Name;
		public required Color Color;
		public required float Price, Stock, Target, Held;
		public ServiceOffer Buy, Sell, Drop;
		public bool HasBuy, HasSell, HasDrop;
		/// <summary>Phase 18: führt die Station die Ware (sonst nur Abwurf-Zeile für Fracht, die sie nicht handelt)?</summary>
		public bool Traded;
	}

	IStationService? _svc;
	readonly List<Row> _rows = new();
	bool _tiles;
	GoodId? _expanded;       // aufgeklappte Zeile im Listenmodus
	GoodId? _tileSel;        // gewählte Kachel im Kachelmodus
	float _qty = 10f;        // Menge des Editors (Runde 28: bleibt, wie eingestellt — auch beim Öffnen einer anderen Zeile)
	string _message = "";
	bool _msgOk = true;
	ScrollContainer? _scroll;
	/// <summary>Runde 34: aufgeklappte Zeile (Knoten) und wie viele Bilder lang sie nach dem Öffnen ganz sichtbar gehalten wird.</summary>
	TradeRow? _openRow;
	float _tileW = 214f;
	int _ensureFrames;

	/// <summary>
	/// Runde 34 (Handytest r33 i2: nach dem Öffnen von „Energie“ lagen Menge/Max/Kaufen OBEN abgeschnitten): nach dem Öffnen einige Bilder lang mit den echten
	/// Knotenlagen (nicht den angenommenen Zeilenhöhen) nachrollen — die Oberkante hat Vorrang, dann die Unterkante samt „Abwerfen“. Ein späteres Wiederherstellen
	/// der alten Rollposition (Neuaufbau je Tick) kann so die Zeile nicht mehr verschieben.
	/// </summary>
	public override void _Process(double delta)
	{
		if (_ensureFrames <= 0) return;
		if (KitScroll.Dragging) { _ensureFrames = 0; return; }   // Runde 34b (Prüfung B7): der Nutzer rollt selbst — nicht zurückholen
		_ensureFrames--;
		if (_scroll is not KitScroll ks || _openRow is not { } row || !IsInstanceValid(row) || !row.IsInsideTree() || KitScroll.Dragging) return;
		float top = row.Position.Y, bottom = top + row.Size.Y + 10f, view = ks.Size.Y, cur = ks.ScrollVertical;
		if (view <= 1f) return;
		int want = (int)cur;
		if (top < cur) want = (int)top;
		else if (bottom > cur + view) want = (int)MathF.Min(top, bottom - view);
		if (want != (int)cur) ks.ScrollTo(want);
	}

	/// <summary>Runde 34, Selbsttest: liegt die aufgeklappte Zeile ganz im sichtbaren Teil der Liste (Oberkante und, wenn sie passt, Unterkante)?</summary>
	public bool TestOpenRowVisible(out string why)
	{
		why = "";
		if (_scroll is not KitScroll ks || _openRow is not { } row || !IsInstanceValid(row)) { why = "keine offene Zeile"; return false; }
		var sr = ks.GetGlobalRect(); var rr = row.GetGlobalRect();
		bool topOk = rr.Position.Y >= sr.Position.Y - 1f, botOk = rr.End.Y <= sr.End.Y + 1f || rr.Size.Y > sr.Size.Y;
		why = $"Zeile {rr.Position.Y:0}…{rr.End.Y:0}, Liste {sr.Position.Y:0}…{sr.End.Y:0}";
		return topOk && botOk;
	}
	readonly KitConfirm _confirm = new();   // Phase 18: „Abwerfen“ erst beim zweiten Tipp
	internal readonly HintState _hint = new();
	/// <summary>Runde 28: letzter Druck auf eine Zeile/Kachel (Ware, Kachel?, Bildschirmposition) — überlebt Neuaufbauten.</summary>
	(GoodId Good, bool Tile, Vector2 At)? _press;   // Runde 26: gehaltener Abnehmer je Ware und Flugzeiten (Sitzung der Handelsansicht)

	public TradeScreen() => AreaId = DockAreas.Trade;

	protected override void Rebuild()
	{
		// Zweiter Gerätetest Phase 28: die Rollposition hält allein KitScroll (Capture/Restore über den stabilen Schlüssel, gesetzt nach dem Layout) — das
		// zusätzliche sofortige Setzen hier traf die noch nicht ausgelegte Liste (Wert 0) und ließ sie zwischen oben und gerollt springen
		_scroll = null;
		base.Rebuild();
	}

	protected override void BuildContent(Control inner)
	{
		_openRow = null;
		_svc = Ctx.Services.TryGet(TradeService.ServiceKey, out var s) && s.IsAvailable(Ctx.U, Ctx.Station) ? s : null;
		LoadRows();
		float w = inner.Size.X > 0 ? inner.Size.X : 670f;

		// ── Kopfzeile: Hinweis (Ergebnismeldung) links, Liste/Kacheln rechts ──
		// Runde 31 (Gerätetest handy31: „… davon 3 zum Rückkaufpreis 39…“): die Meldung bricht auf zwei Zeilen um; reicht das nicht, kleinere Schrift
		// (mit der Schrift gemessen)
		string msgText = _message.Length > 0 ? _message : "Markt der Station – Zeile antippen für Menge und Kauf/Verkauf";
		var msgLabel = Text(msgText, MessageFont(msgText, w - 200), _message.Length > 0 ? (_msgOk ? Green : Red) : new Color(Dim, 0.85f));
		msgLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		msgLabel.MaxLinesVisible = 2;
		var msg = At(inner, msgLabel, 0, 0, w - 200, 40);
		var listBtn = At(inner, new KitButton("Liste", new Vector2(84, 40)) { FontSize = 16 }, w - 186, 0);
		var tileBtn = At(inner, new KitButton("Kacheln", new Vector2(92, 40)) { FontSize = 16 }, w - 96, 0);
		listBtn.State = _tiles ? KitButtonState.Normal : KitButtonState.On;
		tileBtn.State = _tiles ? KitButtonState.On : KitButtonState.Normal;
		listBtn.Pressed += () => { if (_tiles) { _tiles = false; _tileSel = null; Invalidate(); } };
		tileBtn.Pressed += () => { if (!_tiles) { _tiles = true; Invalidate(); } };

		// ── Fußzeile: Alles verkaufen + Rückgängig ──
		float fy = inner.Size.Y - 56;
		var sellAll = At(inner, new KitButton("Alles verkaufen", new Vector2(220, 52)) { FontSize = 18 }, 0, fy);
		bool anyCargo = Ctx.Ship.Cargo.Values.Any(v => v > 0f);
		// Phase 17: aktiv genau dann, wenn „Alles verkaufen“ etwas bewegt (Angebotsmenge des Dienstes, gleiche Regel wie die Ausführung)
		if (!anyCargo) { sellAll.State = KitButtonState.Disabled; sellAll.Sub = "Fracht leer"; }
		else if (_sellAll.Amount <= 0f) { sellAll.State = KitButtonState.Disabled; sellAll.Sub = "Station nimmt nichts an"; }
		else sellAll.Pressed += () => Execute(TradeService.SellAll, 0f);
		// Runde 39: „Rückgängig“ echt — zwei Taps (KitConfirm), Unterzeile = die Buchung (Angebotstext) bzw. der Sperrgrund, Ausführung nur über Execute("undo")
		float uw = MathF.Max(170f, w - 240f);
		var undoBtn = At(inner, new KitButton("Rückgängig", new Vector2(uw, 52)) { FontSize = 18 }, 232, fy);
		if (_undo.Amount > 0f)
			_confirm.Wire(undoBtn, TradeService.Undo, "Rückgängig", () => Execute(TradeService.Undo, 0f), _undo.Text);
		else
		{
			undoBtn.State = KitButtonState.Disabled;
			undoBtn.Sub = _undo.Note ?? "Keine offene Buchung";
		}

		if (_svc == null)
		{
			At(inner, Text("An dieser Station gibt es keinen Handel.", 18, Dim), 0, 60, w, 30);
			return;
		}

		if (!_tiles)
		{
			// ── Listenmodus: eine Zeile je Ware, antippen klappt auf ──
			_scroll = At(inner, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, 0, 46, w, fy - 46 - 8);
			var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			box.AddThemeConstantOverride("separation", 6);
			_scroll.AddChild(box);
			foreach (var row in _rows)
			{
				bool open = _expanded is { } g && g == row.Good;
				var r = new TradeRow(this, row, open, w - 10);
				r.Tapped += () =>
				{
					// Runde 23 (Handy-Test: Liste sprang beim Öffnen): die angetippte Zeile bleibt an ihrem Platz — klappt darüber eine andere zu, rollt die Liste nach
					float before = RowTop(row.Good);
					_expanded = open ? null : row.Good; if (!open) _qty = KeepQty(); _hint.Release();   // Runde 27: Zeile zu/auf = Abnehmer neu wählen
					if (_scroll is KitScroll sk) { float delta = RowTop(row.Good) - before; if (MathF.Abs(delta) > 0.5f) sk.ScrollTo((int)MathF.Max(0f, sk.ScrollVertical + delta)); }
					Invalidate(); if (!open) { EnsureExpandedVisible(); _ensureFrames = 30; }
				};
				r.EditorBuy += q => Buy(row, q);
				r.EditorSell += q => Sell(row, q);
				r.EditorDrop += q => Drop(row, q);
				r.EditorQty += q => _qty = q;
				if (open) { r.SetQty(_qty); _openRow = r; }
				box.AddChild(r);
			}
		}
		else
		{
			// ── Kachelmodus: Kacheln oben, Editor-Blatt unten für die gewählte Kachel ──
			float sheetH = _tileSel is { } ts && _rows.FirstOrDefault(r => r.Good == ts) is { } tr ? 26f + Editor.HeightFor(tr) + 6f : 0f;
			_scroll = At(inner, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, 0, 46, w, fy - 46 - 8 - sheetH);
			int cols = SpaceSim.Core.Ui.DockLayout.GridColumns(w, 214f, 8f);   // Runde 34: Spalten aus der Breite (Kachel 214)
			_tileW = MathF.Floor((w - 8f * (cols - 1)) / cols);   // Runde 34b: die Kacheln teilen sich die ganze Breite
			var grid = new GridContainer { Columns = cols, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			grid.AddThemeConstantOverride("h_separation", 8);
			grid.AddThemeConstantOverride("v_separation", 8);
			_scroll.AddChild(grid);
			foreach (var row in _rows)
			{
				bool sel = _tileSel is { } g && g == row.Good;
				var t = new TradeTile(this, row, sel, _tileW);
				t.Tapped += () => { _tileSel = sel ? null : row.Good; if (!sel) _qty = KeepQty(); Invalidate(); };
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
				ed.Drop += q => Drop(selRow, q);
				ed.QtyChanged += q => _qty = q;
				At(sheet, ed, 6, 32);
			}
		}
	}

	/// <summary>
	/// Zweiter Gerätetest Phase 28: die aufgeklappte Zeile ganz in den sichtbaren Bereich rollen (der Editor war unten abgeschnitten). Zeilenhöhen sind fest
	/// (56 px, aufgeklappt + Editor, Abstand 6), daher ohne Layout berechenbar.
	/// </summary>
	/// <summary>Runde 23: Oberkante einer Zeile in der Liste (feste Zeilenhöhen wie <see cref="EnsureExpandedVisible"/>).</summary>
	float RowTop(GoodId good)
	{
		float y = 0f;
		foreach (var row in _rows)
		{
			if (row.Good == good) return y;
			y += (row.Good == _expanded ? 56f + Editor.HeightFor(row) : 56f) + 6f;
		}
		return y;
	}

	void EnsureExpandedVisible()
	{
		if (_scroll is not KitScroll ks || _expanded is not { } g) return;
		float y = 0f, top = -1f, bottom = 0f;
		foreach (var row in _rows)
		{
			float h = row.Good == g ? 56f + Editor.HeightFor(row) : 56f;
			if (row.Good == g) { top = y; bottom = y + h; break; }
			y += h + 6f;
		}
		if (top < 0f) return;
		float view = ks.Size.Y > 0f ? ks.Size.Y : 300f;
		float cur = ks.ScrollVertical;
		bottom += 10f;   // Runde 23: „Abwerfen“ samt Rand ganz sichtbar
		if (bottom > cur + view) ks.ScrollTo((int)Mathf.Min(top, bottom - view));
		else if (top < cur) ks.ScrollTo((int)top);
	}

	/// <summary>Selbsttest: Rollbereich der Liste (Rechteck und Position).</summary>
	public KitScroll? TestScroll => _scroll as KitScroll;
	/// <summary>Runde 23: Hinweiszeilen des offenen Editors (Kauf/Verkauf, Abnehmer, Erwartung) — leer ohne Editor.</summary>
	public (string, string, string) TestEditorLines
	{
		get
		{
			static Editor? Find(Node n) { foreach (var c in n.GetChildren()) { if (c is Editor e && !e.IsQueuedForDeletion()) return e; if (Find(c) is { } f) return f; } return null; }
			return Find(this)?.TestLines ?? ("", "", "");
		}
	}

	/// <summary>Runde 27: genannter Abnehmer des offenen Editors (default = keiner).</summary>
	public StationId TestEditorBuyer
	{
		get
		{
			static Editor? Find(Node n) { foreach (var c in n.GetChildren()) { if (c is Editor e && !e.IsQueuedForDeletion()) return e; if (Find(c) is { } f) return f; } return null; }
			return Find(this)?.TestBuyer ?? default;
		}
	}

	/// <summary>Runde 26: Ampel des offenen Editors (null = ohne Abnehmer oder ohne Editor).</summary>
	public SpaceSim.Core.Trading.TripVerdict? TestEditorVerdict
	{
		get
		{
			static Editor? Find(Node n) { foreach (var c in n.GetChildren()) { if (c is Editor e && !e.IsQueuedForDeletion()) return e; if (Find(c) is { } f) return f; } return null; }
			return Find(this)?.TestVerdict;
		}
	}

	/// <summary>Runde 25: musste eine Hinweiszeile des offenen Editors trotz kürzester Fassung gekürzt werden?</summary>
	public bool TestEditorOverflow
	{
		get
		{
			static Editor? Find(Node n) { foreach (var c in n.GetChildren()) { if (c is Editor e && !e.IsQueuedForDeletion()) return e; if (Find(c) is { } f) return f; } return null; }
			return Find(this)?.TestOverflow ?? false;
		}
	}
	/// <summary>Selbsttest: Menge des Editors, wie der Bildschirm sie hält.</summary>
	public float TestQty => _qty;
	/// <summary>Selbsttest: angezeigte Menge im (einzigen) Editor („“ = kein Editor).</summary>
	public string TestEditorQtyText
	{
		get
		{
			static Editor? Find(Node n) { foreach (var c in n.GetChildren()) { if (c is Editor e && !e.IsQueuedForDeletion()) return e; if (Find(c) is { } f) return f; } return null; }
			return Find(this)?.QtyText ?? "";
		}
	}
	/// <summary>Selbsttest: Bildschirmrechteck der aufgeklappten Zeile samt Editor (leer = keine).</summary>
	public Rect2 TestExpandedRect
	{
		get
		{
			static TradeRow? Find(Node n) { foreach (var c in n.GetChildren()) { if (c is TradeRow r && r.Open && !r.IsQueuedForDeletion()) return r; if (Find(c) is { } f) return f; } return null; }
			return Find(this)?.GetGlobalRect() ?? default;
		}
	}
	/// <summary>Selbsttest: klappt eine Zeile auf wie ein Tipp (inklusive Sichtbarmachen).</summary>
	public void TestOpenRow(GoodId good)
	{
		if (_rows.FirstOrDefault(r => r.Good == good) is not { } row) return;
		_tiles = false; _tileSel = null; _expanded = good; _qty = KeepQty();
		Invalidate();
		EnsureExpandedVisible();
	}
	/// <summary>Runde 28, Selbsttest: aufgeklappte Ware (null = keine) und Bildschirmrechteck der Kopfzeile einer Ware.</summary>
	public GoodId? TestExpanded => _expanded;
	public Rect2 TestRowHead(GoodId good)
	{
		static TradeRow? Find(Node n, GoodId g) { foreach (var c in n.GetChildren()) { if (c is TradeRow r && r.Good == g && !r.IsQueuedForDeletion()) return r; if (Find(c, g) is { } f) return f; } return null; }
		return Find(this, good) is { } row ? new Rect2(row.GlobalPosition, new Vector2(row.Size.X, 56f)) : default;
	}
	public System.Collections.Generic.List<GoodId> TestGoods => _rows.Select(r => r.Good).ToList();
	/// <summary>Runde 28, Selbsttest: Neuaufbau wie bei einer Hangar-/Dock-Änderung.</summary>
	public void TestInvalidate() => Invalidate();
	/// <summary>Runde 27, Selbsttest: „Kaufen“ wie ein Tipp im Editor (mit der Preisprüfung).</summary>
	public void TestBuyRow(GoodId good, float qty) { if (_rows.FirstOrDefault(r => r.Good == good) is { } row) Buy(row, qty); }
	public string TestMessageText => _message;
	public GoodId TestLastGood => _rows.Count > 0 ? _rows[^1].Good : default;
	/// <summary>Selbsttest: Angebotsmenge „kaufen“ der Ware (was „Max“ setzen soll).</summary>
	public float TestBuyAmount(GoodId good) => _rows.FirstOrDefault(r => r.Good == good)?.Buy.Amount ?? -1f;

	void LoadRows()
	{
		_rows.Clear();
		_sellAll = default;
		_undo = default;
		if (_svc == null) return;
		var u = Ctx.U; var st = Ctx.Station;
		var byGood = new Dictionary<GoodId, Row>();
		foreach (var o in _svc.Offers(u, st, Ctx.Ship))
		{
			if (o.Key == TradeService.SellAll) { _sellAll = o; continue; }   // Phase 17: Fußzeile, Menge = was der Verkauf bewegt
			if (o.Key == TradeService.Undo) { _undo = o; continue; }   // Runde 39: Fußzeile „Rückgängig“
			if (o.Good.Value == 0) continue;   // Fußzeilen-Angebote ohne Ware sind keine Zeilen
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
					Traded = st.Market.TryGetValue(o.Good, out var te) && te.Price > 0f,
				};
				byGood[o.Good] = row;
			}
			if (o.Key.StartsWith(TradeService.Buy + ':', StringComparison.Ordinal)) { row.Buy = o; row.HasBuy = true; }
			else if (o.Key.StartsWith(TradeService.Sell + ':', StringComparison.Ordinal)) { row.Sell = o; row.HasSell = true; }
			else if (o.Key.StartsWith(TradeService.Drop + ':', StringComparison.Ordinal)) { row.Drop = o; row.HasDrop = true; }   // Phase 18
		}
		_rows.AddRange(byGood.Values.OrderBy(r => r.Good.Value));
	}

	// Runde 28 (Gerätetest: die Menge sprang beim Öffnen auf die bezahlbare 6 bzw. 3 statt der gewählten 10): die gewählte Menge bleibt; was davon wirklich
	// kaufbar ist, sagt die Kaufzeile („— nur 6 bezahlbar“) und die Taste („6 kaufen“)
	float KeepQty() => _qty >= 1f ? _qty : 10f;

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

	void Buy(Row row, float qty)
	{
		float shown = _shown.GetValueOrDefault(row.Good.Value);
		if (PriceStill(row, buying: true)) Favourable(Execute($"{TradeService.Buy}:{row.Good.Value}", qty), shown, buying: true);
	}
	void Sell(Row row, float qty)
	{
		float shown = _shown.GetValueOrDefault(row.Good.Value);
		if (PriceStill(row, buying: false)) Favourable(Execute($"{TradeService.Sell}:{row.Good.Value}", qty), shown, buying: false);
	}

	/// <summary>
	/// Runde 31 (Gerätetest handy31: angezeigt „Kauf 10 × 24,7“, gebucht 10 × 12,16): Anzeige und Buchung rechnen mit demselben Marktpreis — dazwischen
	/// lag eine Lieferung an die Station (der Preis halbiert sich, wenn sich der Bestand verdoppelt). Zum Vorteil des Spielers wird gebucht (Runde 27);
	/// die Meldung sagt jetzt, dass sich der Preis seit der Anzeige geändert hat.
	/// </summary>
	void Favourable(ServiceResult r, float shown, bool buying)
	{
		if (!r.Success || r.Units <= 0f || shown <= 0f) return;
		float unit = MathF.Abs(r.Credits) / r.Units;
		bool better = buying ? unit < shown * 0.98f : unit > shown * 1.02f;
		if (!better || _message.Contains("Rückkaufpreis")) return;
		_message += $" (Preis seit der Anzeige {shown:0.0} → {unit:0.0})";
		TestFavourable++;
	}
	/// <summary>Runde 31, Selbsttest: Buchungen, deren Preis seit der Anzeige günstiger wurde.</summary>
	public int TestFavourable { get; private set; }

	/// <summary>Runde 31: Schriftgröße der Kopfmeldung — 15, 14 oder 13, die größte, bei der sie auf zwei Zeilen passt.</summary>
	static int MessageFont(string text, float width)
	{
		var font = ThemeDB.FallbackFont;
		foreach (int size in new[] { 15, 14, 13 })
			if (font.GetMultilineStringSize(text, HorizontalAlignment.Left, width, size, 3, TextServer.LineBreakFlag.WordBound).Y <= 2 * font.GetHeight(size) + 1f) return size;
		return 13;
	}

	/// <summary>
	/// Runde 27 (Gerätetest: Zeile 6,0, gebucht 7,5): Zeile und Editor zeigen den Marktpreis desselben Ticks (<see cref="LivePrice"/>, im Tick-Takt neu
	/// gezeichnet). Hat er sich zwischen der letzten Anzeige und dem Tipp um mehr als 2 % zum Nachteil geändert, wird nicht gebucht: die Meldung nennt den
	/// neuen Preis, der nächste Tipp kauft bzw. verkauft zu ihm.
	/// </summary>
	bool PriceStill(Row row, bool buying)
	{
		if (Ctx?.Station.Market.TryGetValue(row.Good, out var e) != true || !_shown.TryGetValue(row.Good.Value, out var shown) || shown <= 0f) return true;
		float now = e!.Price;
		bool worse = buying ? now > shown * 1.02f : now < shown * 0.98f;
		_shown[row.Good.Value] = now;
		if (!worse) return true;
		_message = $"Preis hat sich geändert: jetzt {now:0.0} statt {shown:0.0} — nochmal tippen, um zu {(buying ? "kaufen" : "verkaufen")}.";
		_msgOk = false;
		TestPriceChanged++;
		Invalidate();
		return false;
	}

	/// <summary>Runde 27: zuletzt angezeigter Preis je Ware (Zeile/Editor) — gegen ihn prüft <see cref="PriceStill"/>.</summary>
	readonly System.Collections.Generic.Dictionary<int, float> _shown = new();
	internal void Shown(GoodId good, float price) => _shown[good.Value] = price;
	/// <summary>Runde 27: Marktpreis jetzt (derselbe Tick in Zeile, Editor und Buchung); ohne Eintrag der Zeilenwert.</summary>
	float LivePrice(Row r) => Ctx?.Station.Market.TryGetValue(r.Good, out var e) == true ? e!.Price : r.Price;
	/// <summary>Runde 27, Selbsttest: wie oft ein Tipp wegen eines geänderten Preises nicht gebucht wurde.</summary>
	public int TestPriceChanged { get; private set; }
	/// <summary>Runde 27, Selbsttest: zuletzt angezeigter Preis einer Ware (0 = nie).</summary>
	public float TestShownPrice(GoodId g) => _shown.GetValueOrDefault(g.Value);
	/// <summary>Phase 18: Abwerfen über den Dienst — die gewählte Menge, ab dem Bestand an Bord alles (Menge 0).</summary>
	void Drop(Row row, float qty) => Execute($"{TradeService.Drop}:{row.Good.Value}", qty >= row.Held ? 0f : qty);

	/// <summary>Phase 18: „Abwerfen“ zeigen, wenn Ware an Bord ist, die die Station nicht (ganz) nimmt — neben dem Grund.</summary>
	// Phase 19: ein gesperrter Abwurf (Treuhand-Material einer Baustelle) hat Menge 0 — keine Taste, der Grund steht als Hinweis
	static bool ShowDrop(Row r) => r.HasDrop && r.Drop.Amount > 0f && r.Held > 0f && r.Sell.Amount < r.Held - 0.0001f;

	// ── Test-Hooks (Selbsttest, UiShots) ──

	/// <summary>Wie die Kauf-/Verkauf-Tasten: Execute über den Dienst.</summary>
	public ServiceResult TestExecute(string offer, float units) => Execute(offer, units);
	/// <summary>Klappt die erste Zeile auf (Screenshot).</summary>
	public void TestExpandFirst()
	{
		if (_rows.Count == 0) return;
		_expanded = _rows[0].Good;
		_qty = KeepQty();
		Invalidate();
	}
	/// <summary>Phase 16: baut die Zeilen neu auf (nach einem Stationswechsel im Test, wenn der Bereich schon offen ist).</summary>
	public void TestRebuild() => Invalidate();
	/// <summary>Runde 35b (Prüfung Punkt 11): Zeit eines Neuaufbaus der Liste (ms, Mittel über <paramref name="n"/>) und der Namens-Einpassung aller Zeilen
	/// (FitFontSize + MiddleEllipsis, wie beim Zeichnen; ms je Durchgang über alle Zeilen).</summary>
	public (double RebuildMs, double RebuildNoFitMs, double FitMs, int Rows) TestTimeRebuild(int n)
	{
		UiKit.AutoFit = false;
		Rebuild();
		var sw = System.Diagnostics.Stopwatch.StartNew();
		for (int i = 0; i < n; i++) Rebuild();
		double rb0 = sw.Elapsed.TotalMilliseconds / n;
		UiKit.AutoFit = true;
		Rebuild();
		sw.Restart();
		for (int i = 0; i < n; i++) Rebuild();
		double rb = sw.Elapsed.TotalMilliseconds / n;
		var font = ThemeDB.FallbackFont;
		var rows = BuildRowsForTest();
		sw.Restart();
		for (int i = 0; i < n; i++)
			foreach (var name in rows)
			{
				int ns = SpaceSim.Core.Ui.UiLayout.FitFontSize(fs => font.GetStringSize(name, HorizontalAlignment.Left, -1, fs).X, 180f, 20, 16);
				SpaceSim.Core.Ui.UiLayout.MiddleEllipsis(name, t => font.GetStringSize(t, HorizontalAlignment.Left, -1, ns).X, 180f);
			}
		double fit = sw.Elapsed.TotalMilliseconds / n;
		// Runde 37 (Prüfung: die Messung lief über einen eigenen Speicher, nicht über den des Spiels): gemessen wird FitName — derselbe Aufruf wie in
		// TradeRow._Draw, mit dem echten Zwischenspeicher, der Namensbreite und Untergrenze der gezeichneten Zeilen. Der Speicher wird vorher geleert, der
		// erste Durchgang füllt ihn (kalt, getrennt ausgegeben), gemessen wird das Mittel über alle n Durchgänge
		var drawn = new List<(float W, int Floor)>();
		static void Collect(Node nd, List<(float, int)> acc) { foreach (var c in nd.GetChildren()) { if (c is TradeRow r && !r.IsQueuedForDeletion()) acc.Add((172f + Mathf.Clamp(r.Size.X - 640f, 0f, 100f), Math.Max(14, SpaceSim.Core.Ui.UiLayout.FontFloor(20, UiKit.LayerFactor(r))))); Collect(c, acc); } }
		Collect(this, drawn);
		var (nw, nfl) = drawn.Count > 0 ? drawn[0] : (172f, 16);
		NameFitCache.Clear();
		sw.Restart();
		foreach (var name in rows) FitName(font, name, nw, nfl);
		ColdFitMs = sw.Elapsed.TotalMilliseconds;
		sw.Restart();
		for (int i = 0; i < n; i++)
			foreach (var name in rows) FitName(font, name, nw, nfl);
		CachedFitMs = sw.Elapsed.TotalMilliseconds / n;
		return (rb, rb0, fit, rows.Count);
	}
	public double CachedFitMs, ColdFitMs;
	System.Collections.Generic.List<string> BuildRowsForTest() => Ctx is { } c ? c.Station.Market.Keys.Select(g => c.U.Goods[g].Name).ToList() : new();
	/// <summary>Phase 16: klappt die Zeile einer Ware auf (Screenshot).</summary>
	public void TestExpand(GoodId good)
	{
		if (_rows.FirstOrDefault(r => r.Good == good) is not { } row) return;
		_tiles = false; _tileSel = null; _expanded = good; _qty = KeepQty();
		Invalidate();
	}
	/// <summary>Schaltet zwischen Liste und Kacheln um (Screenshot).</summary>
	public void TestSetTiles(bool tiles) { _tiles = tiles; if (!tiles) _tileSel = null; Invalidate(); }
	public void TestSelectFirstTile()
	{
		if (_rows.Count == 0) return;
		_tiles = true; _tileSel = _rows[0].Good; _qty = KeepQty();
		Invalidate();
	}
	public string TestMessage => _message;
	/// <summary>Phase 16: Annahme-Hinweis der Verkaufszeile einer Ware (null = keiner) und ob die Station jetzt etwas davon annimmt.</summary>
	public string? TestSellNote(GoodId good) => _rows.FirstOrDefault(r => r.Good == good)?.Sell.Note;
	public bool TestSellEnabled(GoodId good) => _rows.FirstOrDefault(r => r.Good == good) is { } r && CanSell(r);
	/// <summary>Phase 18: ob die Zeile einer Ware „Abwerfen“ zeigt.</summary>
	public bool TestDropShown(GoodId good) => _rows.FirstOrDefault(r => r.Good == good) is { } r && ShowDrop(r);
	/// <summary>Phase 18: ein Tipp auf „Abwerfen“ (wie die Taste: der erste macht scharf, der zweite innerhalb von 3 s führt aus); liefert, ob ausgeführt wurde.</summary>
	public bool TestTapDrop(GoodId good, float qty)
	{
		if (_rows.FirstOrDefault(r => r.Good == good) is not { } row || !ShowDrop(row)) return false;
		if (!_confirm.Tap(DropKey(row))) return false;
		Drop(row, qty);
		return true;
	}
	static string DropKey(Row r) => $"drop:{r.Good.Value}";

	/// <summary>Phase 17: ob „Alles verkaufen“ aktiv ist (Angebotsmenge &gt; 0).</summary>
	public bool TestSellAllEnabled => Ctx.Ship.Cargo.Values.Any(v => v > 0f) && _sellAll.Amount > 0f;

	/// <summary>Runde 39, Selbsttest: „Rückgängig“ angeboten (Menge &gt; 0) — Text = die Buchung, Notiz = Sperrgrund.</summary>
	public bool TestUndoEnabled => _undo.Amount > 0f;
	public string TestUndoText => _undo.Text;
	public string? TestUndoNote => _undo.Note;
	/// <summary>
	/// Runde 39, Selbsttest: ein Tipp auf „Rückgängig“ in der Fußzeile (wie die Taste: der erste macht scharf, der zweite
	/// innerhalb von 3 s führt über Execute(„undo“) aus); true = ausgeführt.
	/// </summary>
	public bool TestTapUndo()
	{
		if (_undo.Amount <= 0f) return false;
		if (!_confirm.Tap(TradeService.Undo)) return false;
		Execute(TradeService.Undo, 0f);
		return true;
	}

	/// <summary>Phase 17: Verkauf möglich ⇔ der Dienst würde etwas bewegen (Angebotsmenge &gt; 0; auch ein Bruchteil an Bord, den die Station ganz nimmt).</summary>
	static bool CanSell(Row r) => r.Held > 0f && r.Sell.Amount > 0f;
	public int TestRowCount => _rows.Count;
	public bool TestTiles => _tiles;

	/// <summary>Eine Zeile der Liste: oben Ware/Preis/Bestand/an Bord, aufgeklappt der Mengen-Editor.</summary>
	sealed partial class TradeRow : Control
	{
		public event Action? Tapped;
		public event Action<float>? EditorBuy, EditorSell, EditorQty, EditorDrop;

		readonly TradeScreen _owner;
		readonly Row _row;
		readonly bool _open;
		public bool Open => _open;
		public GoodId Good => _row.Good;
		Editor? _editor;

		public TradeRow(TradeScreen owner, Row row, bool open, float w)
		{
			_owner = owner;
			_row = row; _open = open;
			MouseFilter = MouseFilterEnum.Stop;
			CustomMinimumSize = new Vector2(w, open ? 56 + Editor.HeightFor(row) : 56);
			Size = CustomMinimumSize;
		}

		float _qtyInit = 1f;
		/// <summary>
		/// Menge des Editors. Zweiter Gerätetest Phase 28: der Editor entsteht erst in <see cref="_Ready"/> — die Menge wird gemerkt und dort gesetzt
		/// (vorher ging sie bei jedem Neuaufbau verloren: „Max“ zeigte und kaufte 1, „+“ wirkte nur jedes zweite Mal).
		/// </summary>
		public void SetQty(float q) { _qtyInit = q; _editor?.SetQty(q); }

		public override void _Ready()
		{
			if (_open)
			{
				_editor = new Editor(_owner, _row, Size.X);
				_editor.Buy += q => EditorBuy?.Invoke(q);
				_editor.Sell += q => EditorSell?.Invoke(q);
				_editor.Drop += q => EditorDrop?.Invoke(q);
				_editor.QtyChanged += q => EditorQty?.Invoke(q);
				AddChild(_editor);
				_editor.Position = new Vector2(0, 56);
				_editor.SetQty(_qtyInit, notify: false);
			}
		}

		long _drawnTick = -1;
		public override void _Process(double delta)
		{
			// Runde 27: Preis im Tick-Takt neu zeichnen (vorher nur beim Neuaufbau — Zeile und Editor zeigten verschiedene Preise)
			if (_owner.Ctx?.U is { } uu && uu.Tick != _drawnTick) { _drawnTick = uu.Tick; QueueRedraw(); }
		}

		// Runde 28 (Gerätetest: offene Zeile klappte von selbst zu, ein zweiter Tipp traf eine andere Zeile): ein Tipp zählt nur, wenn Drücken und Loslassen
		// auf DERSELBEN WARE lagen und der Finger kaum wanderte. Der Druck wird im Bildschirm gemerkt (nicht im Knoten), damit ein Neuaufbau zwischen Drücken
		// und Loslassen (Ticks, Hangar) den Tipp weder verschluckt noch einer fremden Zeile zuschreibt
		public override void _GuiInput(InputEvent @event)
		{
			if (!Pointer.TryRead(@event, out int idx, out var pos, out bool pressed, out bool motion)) return;
			var head = new Rect2(Vector2.Zero, new Vector2(Size.X, 56));
			var at = GetGlobalTransform() * pos;
			if (motion) { if (_owner._press is { } p0 && p0.At.DistanceTo(at) > 14f) _owner._press = null; return; }
			if (pressed) { _owner._press = head.HasPoint(pos) ? (_row.Good, false, at) : null; return; }
			if (_owner._press is { } p && !p.Tile && p.Good == _row.Good && p.At.DistanceTo(at) <= 14f && head.HasPoint(pos) && !KitScroll.Dragging)
			{
				_owner._press = null;
				Tapped?.Invoke();
				AcceptEvent();
				return;
			}
			_owner._press = null;
		}

		public override void _Draw()
		{
			var u = _row;
			DrawRect(new Rect2(Vector2.Zero, Size), _open ? new Color(0.14f, 0.24f, 0.42f, 0.95f) : Card);
			if (_open) DrawRect(new Rect2(Vector2.Zero, Size), Cyan, false, 2f);
			var font = ThemeDB.FallbackFont;
			// Kopfzeile
			DrawRect(new Rect2(6, 12, 18, 18), u.Color);
			// Runde 34b (Handytest r34 g3: „Universalfrachtr…“ zweimal untereinander): Name mit der Schrift einpassen — erst kleiner (bis 14), dann in der Mitte
			// kürzen, damit das unterscheidende Ende („… 60“/„… 100“) sichtbar bleibt; nie über den Preis
			// Runde 35b: breite Zeile → breitere Namensspalte (die übrigen Spalten rücken nach rechts); Schrift nie unter die Untergrenze der Ebene
			float ex = Mathf.Clamp(Size.X - 640f, 0f, 100f), nameW = 172f + ex;   // Runde 35c: mindestens 14 px Abstand zum Preis (bei 220)
			// Runde 35b (Prüfung Punkt 11: Einpassen aller 54 Namen kostete ≈ 2 ms je Neuzeichnen): Ergebnis je (Name, Breite, Untergrenze) zwischengespeichert
			int nfloor = Math.Max(14, SpaceSim.Core.Ui.UiLayout.FontFloor(20, UiKit.LayerFactor(this)));
			var (ns, nm) = FitName(font, u.Name, nameW, nfloor);
			DrawnText.Clear(this);
			if (nm == u.Name)
			{
				TestRowNames[u.Good.Value] = nm;
				UiKit.DrawText(this, 0, font, new Vector2(34, 38), nm, ns, White, full: u.Name);
			}
			else
			{
				// Runde 35b (Prüfwerkzeug: „Leichter Sc…bausatz“ bei 1,15 auf Tablets): passt der Name nicht, zweizeilig (Kopfzeile 56 hoch) statt gekürzt
				TestRowNames[u.Good.Value] = u.Name;
				UiKit.DrawFit2(this, 0, font, new Vector2(34, 27), new Vector2(34, 38), u.Name, 18, nameW, White, minSize: 14);
			}
			float live = _owner.LivePrice(u);
			if (u.Traded) _owner.Shown(u.Good, live);
			UiKit.DrawText(this, 1, font, new Vector2(220 + ex, 38), u.Traded ? $"{live:0.0}" : "—", 20, u.Traded ? PriceColor(u) : Dim);
			float fill = u.Target > 0f ? Mathf.Clamp(u.Stock / u.Target, 0f, 1.4f) / 1.4f : 0f;
			DrawRect(new Rect2(320 + ex, 18, 110, 10), new Color(0, 0, 0, 0.5f));
			DrawRect(new Rect2(320 + ex, 18, 110 * fill, 10), u.Color);
			UiKit.DrawText(this, 2, font, new Vector2(438 + ex, 38), $"{u.Stock:0}", 17, Dim);
			if (u.Held > 0f) UiKit.DrawText(this, 3, font, new Vector2(500 + ex, 38), $"Bord {u.Held:0}", 17, Cyan);
			// Phase 16: warum die Station die Ware nicht (mehr) annimmt — gedimmt unter dem Preis
			// Runde 34 (Handytest r33 j2: der Deckel-Hinweis brach am Zeilenende hart ab, ohne „…“): Fassung nach gemessener Breite, notfalls mit „…“
			if (NoteOf(u) is { } note)
			{
				float room = Size.X - 260f - ex;
				string drop = ShowDrop(u) && !_open ? " · antippen: Abwerfen" : "";
				// Runde 34b (Handytest r34 #9: der Deckel-Satz stand bei offener Zeile zweimal im Bild): offen steht er im Editor, in der Zeile nur die Kurzform
				string text = _open && _owner.CapShort(u) is { } capShort ? capShort : _owner.RowNote(u, note, drop, room, 12);
				TestRowNotes[u.Good.Value] = (text, font.GetStringSize(text, HorizontalAlignment.Left, -1, 12).X <= room + 0.5f);
				UiKit.DrawText(this, 4, font, new Vector2(220 + ex, 53), text, 12, SellNoteColor(u));
			}
			UiKit.DrawText(this, 5, font, new Vector2(Size.X - 34, 38), _open ? "▾" : "▸", 18, Dim);
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
	static Color SellNoteColor(Row r) => r.Held > 0f && !CanSell(r) ? new Color(Orange, 0.9f) : new Color(Dim, 0.8f);
	/// <summary>Phase 18: Hinweis der Zeile — Annahmegrund des Verkaufs, für nicht gehandelte Fracht der Grund aus dem Abwurf-Angebot.</summary>
	static string? NoteOf(Row r) => r.HasSell ? r.Sell.Note : r.HasDrop ? r.Drop.Note : null;

	/// <summary>Runde 34, Selbsttest: zuletzt gezeichneter Zeilenhinweis (gemessen gekürzt).</summary>
	public static readonly Dictionary<int, (string Text, bool Fits)> TestRowNotes = new();
	/// <summary>Runde 34b, Selbsttest: gezeichneter Warenname je Zeile (eingepasst).</summary>
	public static readonly Dictionary<int, string> TestRowNames = new();
	static readonly Dictionary<(string, float, int), (int Size, string Shown)> NameFitCache = new();

	/// <summary>Runde 35b/37: eingepasster Warenname (Schriftgröße, gezeigte Fassung) über den Zwischenspeicher — derselbe Aufruf beim Zeichnen und in der Messung.</summary>
	static (int Size, string Shown) FitName(Font font, string name, float nameW, int floor)
	{
		var key = (name, nameW, floor);
		if (NameFitCache.TryGetValue(key, out var nf)) return nf;
		int fsz = SpaceSim.Core.Ui.UiLayout.FitFontSize(fs => font.GetStringSize(name, HorizontalAlignment.Left, -1, fs).X, nameW, 20, floor);
		nf = (fsz, SpaceSim.Core.Ui.UiLayout.MiddleEllipsis(name, t => font.GetStringSize(t, HorizontalAlignment.Left, -1, fsz).X, nameW));
		if (NameFitCache.Count > 2000) NameFitCache.Clear();
		NameFitCache[key] = nf;
		return nf;
	}

	/// <summary>
	/// Runde 34 (Handytest r33): Hinweis unter dem Preis einer Warenzeile — die längste Fassung, die mit der Schrift gemessen in <paramref name="room"/> passt;
	/// für den Rückkaufdeckel die Kurzform („Deckel 4,0 · dein Kauf hier“), zuletzt mit „…“ gekürzt.
	/// </summary>
	string? CapShort(Row r) => Ctx is { } c && r.Sell.Note is not null ? PlayerLedger.SaleNoteShort(c.U, c.Ship, c.Station, r.Good) : null;

	string RowNote(Row r, string note, string drop, float room, int size)
	{
		var font = ThemeDB.FallbackFont;
		bool Fits(string t) => font.GetStringSize(t, HorizontalAlignment.Left, -1, size).X <= room;
		var variants = new List<string> { note + drop, note };
		if (Ctx is { } c && PlayerLedger.SaleNoteShort(c.U, c.Ship, c.Station, r.Good) is { } cap && r.Sell.Note == note)
		{
			variants.Add($"{cap} · dein Kauf hier, bis Preis und Lager zurück");
			variants.Add($"{cap} · dein Kauf hier");
			variants.Add(cap);
		}
		foreach (var v in variants) if (Fits(v)) return v;
		return KitButton.Fit(font, variants[^1], size, room);
	}

	/// <summary>Eine Kachel des Kachelmodus.</summary>
	sealed partial class TradeTile : Control
	{
		public event Action? Tapped;
		readonly Row _row;
		readonly bool _sel;

		readonly TradeScreen _owner;
		public TradeTile(TradeScreen owner, Row row, bool sel, float width = 214f)
		{
			_owner = owner; _row = row; _sel = sel;
			MouseFilter = MouseFilterEnum.Stop;
			CustomMinimumSize = new Vector2(MathF.Max(214f, width), 140);   // Runde 34b (Handytest r34 g3: rechts 140 px frei): Breite aus der Fläche
			Size = CustomMinimumSize;
		}

		public override void _GuiInput(InputEvent @event)
		{
			if (!Pointer.TryRead(@event, out _, out var pos, out bool pressed, out bool motion)) return;
			// Runde 28: wie die Zeile — nur ein Tipp, der auf dieser Kachel begann
			var at = GetGlobalTransform() * pos;
			if (motion) { if (_owner._press is { } p0 && p0.At.DistanceTo(at) > 14f) _owner._press = null; return; }
			if (pressed) { _owner._press = new Rect2(Vector2.Zero, Size).HasPoint(pos) ? (_row.Good, true, at) : null; return; }
			if (_owner._press is { } p && p.Tile && p.Good == _row.Good && p.At.DistanceTo(at) <= 14f && new Rect2(Vector2.Zero, Size).HasPoint(pos) && !KitScroll.Dragging)
			{ _owner._press = null; Tapped?.Invoke(); AcceptEvent(); return; }
			_owner._press = null;
		}

		public override void _Draw()
		{
			var r = _row;
			DrawRect(new Rect2(Vector2.Zero, Size), _sel ? new Color(0.12f, 0.22f, 0.4f, 0.97f) : new Color(0.04f, 0.07f, 0.13f, 0.9f));
			DrawRect(new Rect2(Vector2.Zero, Size), _sel ? Cyan : Edge, false, 2f);
			DrawRect(new Rect2(0, 0, Size.X, 7), r.Color);
			var font = ThemeDB.FallbackFont;
			DrawnText.Clear(this);
			// Runde 35b (Prüfwerkzeug: „Leichter Sc…bausatz“ in schmalen Kacheln): zweizeilig statt in der Mitte gekürzt
			UiKit.DrawFit2(this, 0, font, new Vector2(12, 25), new Vector2(12, 34), r.Name, 20, Size.X - 24f, White, minSize: 14);
			UiKit.DrawText(this, 1, font, new Vector2(12, 74), $"{r.Price:0.0}", 30, White);
			UiKit.DrawFit(this, 2, font, new Vector2(112, 70), UiPlaceholders.TrendSoon, 11, MathF.Min(96f, Size.X - 124f), new Color(Dim, 0.7f));
			float fill = r.Target > 0f ? Mathf.Clamp(r.Stock / r.Target, 0f, 1.4f) / 1.4f : 0f;
			// Runde 29 (Gerätetest b05: „Stellt die Ware selbst her“ lag über „Bestand 152“): Balken, Hinweis und Bestand je in eigener Zeile
			DrawRect(new Rect2(12, 84, 190, 8), new Color(0, 0, 0, 0.5f));
			DrawRect(new Rect2(12, 84, 190 * fill, 8), r.Color);
			if (NoteOf(r) is { } note) UiKit.DrawFit(this, 3, font, new Vector2(12, 106), note, 11, Size.X - 24f, SellNoteColor(r));   // Phase 16
			UiKit.DrawFit(this, 4, font, new Vector2(12, 128), $"Bestand {r.Stock:0}", 14, 110, Dim);
			if (r.Held > 0f)
			{
				DrawRect(new Rect2(128, 112, 74, 22), new Color(Cyan, 0.25f));
				UiKit.DrawFit(this, 5, font, new Vector2(128, 128), $"Bord {r.Held:0}", 14, 74, Cyan, HorizontalAlignment.Center);
			}
		}
	}

	/// <summary>Mengen-Editor (Stepper −/Menge/+/Max, Summe, beste Verkaufsstelle, Kaufen/Verkaufen) – in Zeile und Kachel-Blatt.</summary>
	sealed partial class Editor : Control
	{
		public event Action<float>? Buy, Sell, Drop, QtyChanged;

		/// <summary>Phase 18: Höhe des Editors — mit „Abwerfen“-Zeile (56 px Touch-Ziel) höher.</summary>
		// Runde 23: eine Zeile mehr (Kauf/Verkauf, Abnehmer, Erwartung)
		public static float HeightFor(Row r) => ShowDrop(r) ? 200f : 134f;

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
			CustomMinimumSize = new Vector2(w, HeightFor(row));
			Size = CustomMinimumSize;

			// Runde 37 (Handytest r35 g31/g34: „Verkaufen“ ragte bei „groß“ über den rechten Zeilenrand): Tastenreihe aus der Breite — erst fällt das Wort
			// „Menge“, dann werden Mengenfeld und Kauf-/Verkaufstasten schmaler; nie über den Rand
			var lay = ButtonRow(w);
			if (lay.Label) At(this, Text("Menge", 16, Dim), 34, 22);
			var minus = At(this, new KitButton("−", new Vector2(52, 44)) { FontSize = 26 }, lay.X0, 8);
			_qtyLabel = At(this, Text("1", 26, White, HorizontalAlignment.Center), lay.X0 + 56, 8, lay.Qty, 44);
			var plus = At(this, new KitButton("+", new Vector2(52, 44)) { FontSize = 26 }, lay.X0 + 60 + lay.Qty, 8);
			var max = At(this, new KitButton("Max", new Vector2(64, 44)) { FontSize = 16 }, lay.X0 + 116 + lay.Qty, 8);
			float bx = lay.X0 + 116 + lay.Qty + 64 + lay.Gap;
			_buyBtn = At(this, new KitButton("Kaufen", new Vector2(lay.Btn, 44)) { FontSize = 19, State = KitButtonState.Ready }, bx, 8);
			_sellBtn = At(this, new KitButton("Verkaufen", new Vector2(lay.Btn, 44)) { FontSize = lay.Btn < 120f ? 16 : 17, State = KitButtonState.Warn }, bx + lay.Btn + 6, 8);

			minus.Pressed += () => SetQty(_qty - 1f);
			plus.Pressed += () => SetQty(_qty + 1f);
			max.Pressed += () => SetQty(Mathf.Max(_row.Buy.Amount, _row.Held));
			_buyBtn.Pressed += () => { var c = _owner.Ctx; float can = c is null ? MathF.Floor(_row.Buy.Amount) : MathF.Floor(TradeActions.MaxBuyable(c.U, c.Ship, c.Station, _row.Good)); Buy?.Invoke(MathF.Min(_qty, MathF.Max(1f, can))); };   // Runde 28: nie stillschweigend weniger — die Taste sagt „6 kaufen“
			_sellBtn.Pressed += () => Sell?.Invoke(_qty);
			// Phase 18: Abwerfen neben dem Grund, wenn die Station die Ware an Bord nicht (ganz) nimmt — erst beim zweiten Tipp (KitConfirm)
			if (ShowDrop(row))
			{
				var drop = At(this, new KitButton("Abwerfen", new Vector2(200, 56)) { FontSize = 19 }, 34, 136);
				owner._confirm.Wire(drop, DropKey(row), "Abwerfen", () => Drop?.Invoke(_qty));
				// Runde 25 (Gerätetest a18/a20: „… „Max“ = all…“): zweizeilig statt abgeschnitten
				At(this, Text("Ware verschwindet ohne Erlös", 13, new Color(Dim, 0.85f)), 246, 140, Mathf.Max(100f, w - 260), 18);
				At(this, Text($"gewählte Menge · „Max“ = alles ({row.Held:0.##} an Bord)", 13, new Color(Dim, 0.85f)), 246, 158, Mathf.Max(100f, w - 260), 18);
				TestDropHint = $"Ware verschwindet ohne Erlös · gewählte Menge · „Max“ = alles ({row.Held:0.##} an Bord)";
			}
			RefreshButtons();
		}

		public string QtyText => _qtyLabel.Text;

		/// <summary>
		/// Runde 37: Tastenreihe des Editors für die Zeilenbreite <paramref name="w"/> — Startpunkt der Stepper, Breite des Mengenfelds, Kauf-/Verkaufstasten,
		/// ob das Wort „Menge“ Platz hat. Rechts bleiben 12 px Rand; bei 662 px und mehr die alte Reihe (100/80/132).
		/// </summary>
		public static (float X0, float Qty, float Btn, float Gap, bool Label) ButtonRow(float w)
		{
			float right = w - 12f;
			// alte Reihe: Menge 34, − 100, Feld 80, + , Max, Kaufen 132, Verkaufen 132 → endet bei 650
			if (right >= 650f) return (100f, 80f, 132f, 20f, true);
			bool label = right >= 600f;
			float x0 = label ? 100f : 12f;
			float qty = 64f, gap = 8f;
			float btn = MathF.Floor((right - (x0 + 116f + qty + 64f + gap) - 6f) / 2f);
			if (btn > 132f) btn = 132f;
			if (btn < 104f) { qty = 52f; btn = MathF.Floor((right - (x0 + 116f + qty + 64f + gap) - 6f) / 2f); }
			return (x0, qty, MathF.Max(88f, btn), gap, label);
		}

		public void SetQty(float q, bool notify = true)
		{
			_qty = Mathf.Clamp(q, 1f, 9999f);
			_qtyLabel.Text = $"{_qty:0.#}";
			if (notify) QtyChanged?.Invoke(_qty);
			RefreshButtons();
			QueueRedraw();
		}

		void RefreshButtons()
		{
			// Runde 29 (Gerätetest: „Kaufen“ bei „Lager leer“/„Nur Ankauf (Export)“ sah aktiv aus): die Taste folgt dem Live-Stand wie die Begründungszeile
			// (TradeActions.MaxBuyable, im Tick-Takt), nicht dem Zeilen-Schnappschuss vom letzten Neuaufbau
			var ctx = _owner.Ctx;
			float can = ctx is null ? MathF.Floor(_row.Buy.Amount) : MathF.Floor(TradeActions.MaxBuyable(ctx.U, ctx.Ship, ctx.Station, _row.Good));
			_buyBtn.State = can >= 1f && _qty >= 1f ? KitButtonState.Ready : KitButtonState.Disabled;
			// Runde 28: ist die gewählte Menge nicht ganz kaufbar, nennt die Taste die Menge, die sie wirklich kauft
			_buyBtn.Text = can >= 1f && can < _qty ? $"{can:0} kaufen" : "Kaufen";
			// Phase 16: deaktiviert, wenn die Station nichts annimmt (Grund steht in der Zeile)
			_sellBtn.State = CanSell(_row) && _qty >= 1f ? KitButtonState.Warn : KitButtonState.Disabled;
		}

		long _drawnTick = -1;
		public override void _Process(double delta)
		{
			// Runde 23 (Handy-Test: Summe 1426 angezeigt, 1377 gebucht): Preise und Abnehmer laufend aus dem Markt, nicht aus dem Zeilen-Schnappschuss
			// Runde 27: im Tick-Takt (derselbe Preis wie Zeile und Buchung)
			if (_owner.Ctx?.U is { } uu && uu.Tick != _drawnTick) { _drawnTick = uu.Tick; RefreshButtons(); QueueRedraw(); }
		}

		/// <summary>Runde 23: die drei Hinweiszeilen des Editors (Kauf/Verkauf, Abnehmer, Erwartung) mit Farbe — reine Anzeige, Werte live aus dem Core.</summary>
		public static (string Line1, string Line2, string Line3, bool Loss) Lines(DockContext ctx, Row row, float qty, Func<string, int, bool>? fits = null, HintState? hint = null)
		{
			var r = LinesEx(ctx, row, qty, fits, hint);
			return (r.L1, r.L2, r.L3, r.Verdict == TripVerdict.Loss);
		}

		/// <summary>Runde 26: Hinweiszeilen mit Ampel (grün/gelb/rot) und dem genannten Abnehmer.</summary>
		public static (string L1, string L2, string L3, TripVerdict? Verdict, StationId Buyer) LinesEx(DockContext ctx, Row row, float qty, Func<string, int, bool>? fits, HintState? hint)
		{
			// Runde 25 (Gerätetest a18/a20: Zeilen endeten mit „…“): jede Zeile hat kürzere Fassungen; gewählt wird die längste, die passt
			string Pick(int size, params string[] variants) { if (fits is null) return variants[0]; foreach (var v in variants) if (fits(v, size)) return v; return variants[^1]; }
			var u = ctx.U; var st = ctx.Station; var ship = ctx.Ship;
			float price = st.Market.TryGetValue(row.Good, out var e) ? e.Price : row.Price;
			// Kaufseite: was abgebucht wird — oder der Grund, warum „Kaufen“ aus ist (immer aus dem Angebot)
			// Runde 28: die gewählte Menge bleibt; kann nur ein Teil gekauft werden, sagt die Zeile wie viel und warum
			float canBuy = row.HasBuy ? MathF.Floor(TradeActions.MaxBuyable(u, ship, st, row.Good)) : 0f;   // Runde 29: live wie die Taste
			// Runde 30: Rückkaufdeckel — Kauf- und Verkaufssumme mit den wirksamen Preisen (PlayerLedger), der Grund steht dabei
			float buyCost = PlayerLedger.BuyCost(u, ship, st, row.Good, qty, price).Cost;
			float buyUnit = qty > 0f ? buyCost / qty : price;
			// Runde 32b: in der Kaufzeile die kurze Fassung („Boden p“) — der ausführliche Grund steht in Zeile 3
			string floorNote = PlayerLedger.BuyNoteShort(u, ship, st, row.Good) is { } bn ? " · " + bn : "";
			string buy = row.HasBuy && canBuy >= 1f
				? (canBuy < qty ? Pick(14, $"Kauf {qty:0} × {buyUnit:0.0} = {buyCost:0} Cr — nur {canBuy:0} {LimitText(u, st, ship, row, price)}{floorNote}", $"Kauf {qty:0} × {buyUnit:0.0} — nur {canBuy:0} {LimitText(u, st, ship, row, price)}")
					: Pick(14, $"Kauf {qty:0} × {buyUnit:0.0} = {buyCost:0} Cr{floorNote}", $"Kauf {qty:0} × {buyUnit:0.0} = {buyCost:0} Cr"))
				: $"Kaufen: {BuyReason(u, st, ship, row)}";
			// Verkaufsseite: was ein Verkauf wirklich brächte (Angebotsmenge × wirksamer Preis), 0 wenn die Station nichts nimmt
			float sellUnits = MathF.Min(qty, row.Sell.Amount);
			float sellHere = sellUnits > 0f ? PlayerLedger.SaleValue(u, ship, st, row.Good, sellUnits, price).Revenue : 0f;
			string capNote = row.Held > 0f && PlayerLedger.SaleNoteShort(u, ship, st, row.Good) is { } sn ? $" ({sn})" : "";
			string sell = row.Held > 0f ? (row.Sell.Amount > 0f ? $" · Verkauf hier {sellHere:0} Cr{capNote}" : " · Verkauf hier —") : "";
			// Abnehmer: ehrlich für die gewählte Menge (Kaufseite) bzw. die Ladung (Verkaufsseite)
			bool buying = row.HasBuy && canBuy >= 1f;
			float q = buying ? MathF.Max(1f, qty) : MathF.Max(row.Held, qty);
			// Runde 27: Rangfolge nach dem schlechten Fall (Menge × niedrigster zuletzt gesehener Preis), bei Gleichstand der heutige Preis. Bei offenem Editor
			// bleibt der einmal genannte Abnehmer, bis die Zeile geschlossen oder die Menge geändert wird (vorher wechselte er bei springenden Preisen alle ~5 s)
			// Runde 28: ohne Kauf und ohne Ware an Bord (z. B. Exportsenke „Nur Ankauf“) gibt es keine Abnehmerzeile
			if (!buying && row.Held <= 0f) return (buy + sell, "", "", null, default);
			var list = MarketInfo.HonestBuyers(u, row.Good, q, ship.Sector, ship.Position, st.Id, ship.Id, ctx.Prices);
			BuyerQuote? best = list.Count > 0 ? list[0] : null;
			if (hint?.HeldFor(st.Id, row.Good, q) is { } held)
			{
				BuyerQuote? keep = null;
				foreach (var c in list) if (c.Station.Id == held) { keep = c; break; }
				if (keep is not null) best = keep;
				else if (u.Stations.TryGetValue(held, out var hs) && MarketInfo.QuoteBuyer(u, hs, row.Good, q, ship.Sector, ship.Position, ship.Id, ctx.Prices) is { } hq) best = hq;
			}
			string l2, l3 = ""; TripVerdict? verdict = null;
			if (best is not { } b) l2 = "Derzeit kein Abnehmer";
			else
			{
				hint?.Hold(st.Id, row.Good, q, b.Station.Id);
				string sec = u.Sectors.TryGetValue(b.Station.Sector, out var bs) ? bs.Name.Replace("Sektor ", "") : "?";
				float secs = hint?.Seconds(u, ship, b.Station) ?? float.NaN;
				string time = float.IsNaN(secs) ? "" : ", Flug " + SpaceSim.Core.Flight.PlayerTrip.Text(secs).Replace(" min", "");
				// Runde 27: kein „bei Ankunft“ mehr — heute und der Bereich der letzten Minuten
				// Runde 28 (Gerätetest cg3/cg4: „zuletzt 121,6–155,3“ → direkt nach dem Kauf „zuletzt 74,0–157,0“): der untere Wert war der schlechte Fall, also auch
				// der Preis nach den Lieferungen, die andere Händler gerade bringen (ein Händler hatte eben 100 Einheiten für die Kolonie geladen) — jetzt getrennt
				string range = !b.HasHistory ? "zuletzt –" : MathF.Abs(b.PriceHigh - b.HistoryLow) < 0.05f ? $"zuletzt stabil {b.HistoryLow:0.0}" : $"zuletzt {b.HistoryLow:0.0}–{b.PriceHigh:0.0}";
				if (b.Incoming >= 0.5f && b.PriceArrival < MathF.Min(b.PriceNow, b.HasHistory ? b.HistoryLow : b.PriceNow) - 0.05f)
					range += $" · andere bringen {b.Incoming:0} → etwa {b.PriceArrival:0.0}";
				string name = b.Station.Name, nameShort = name.Length > 18 ? name[..17] + "…" : name;
				l2 = Pick(14, $"Abnehmer: {name} ({sec}, {b.Jumps} Spr.{time}) zahlt jetzt {b.PriceNow:0.0} · {range} · nimmt {b.Units:0.#}",
					$"{name} ({sec}{time}): jetzt {b.PriceNow:0.0} · {range} · nimmt {b.Units:0.#}",
					$"{name}{time}: jetzt {b.PriceNow:0.0} · {range} · nimmt {b.Units:0.#}",
					$"{nameShort}: jetzt {b.PriceNow:0.0} · {range.Replace("zuletzt ", "")} · nimmt {b.Units:0.#}");
				float units = buying ? MathF.Min(qty, canBuy) : MathF.Min(qty, row.Held);
				if (buying || row.Held > 0f)
				{
					// Runde 30: verglichen wird mit dem wirksamen Preis hier (Kauf mit Mindestpreis, „statt hier“ mit Rückkaufdeckel)
					float hereUnit = buying ? PlayerLedger.BuyCost(u, ship, st, row.Good, units, price).Cost / MathF.Max(1f, units) : (units > 0f ? PlayerLedger.SaleValue(u, ship, st, row.Good, units, price).Revenue / units : price);
					price = hereUnit;
					var v = MarketInfo.Verdict(b, price, units);
					verdict = v;
					float worst = MathF.Min(b.Units, units) * b.Price - units * price;
					float today = MathF.Min(b.Units, units) * b.PriceNow - units * price;
					string head = buying ? "" : "statt hier: ";
					l3 = b.Units < units - 0.001f ? Pick(13, $"Verlust: Abnehmer nimmt nur {b.Units:0.#} von {units:0}", $"nimmt nur {b.Units:0.#}/{units:0}")
						: v == TripVerdict.Good ? Pick(13, $"{head}Lohnt sich auch im schlechten Fall: mind. +{worst:0} Cr", $"Lohnt sich im schlechten Fall: mind. +{worst:0} Cr")
						: v == TripVerdict.Loss ? Pick(13, $"{head}Verlust: etwa −{-today:0} Cr", $"Verlust: etwa −{-today:0} Cr")
						: Riskant(b, today, MathF.Min(b.Units, units) * b.PriceAfterLoad - units * price, head, Pick);
				}
			}
			// Runde 34 (Handytest r33: „hier: Wegen deines Kaufs hier: …“ doppelt): der Deckel-Hinweis ist schon ein ganzer Satz — ohne Vorsilbe; nur
			// Annahmegründe der Station („Lager voll“ …) bekommen „Verkauf hier:“
			if (row.Sell.Note is { } note && row.Held > 0f)
			{
				string tail = l3.Length > 0 ? " · " + l3 : "";
				l3 = PlayerLedger.SaleNoteShort(u, ship, st, row.Good) is { } shortCap && PlayerLedger.SaleNote(u, ship, st, row.Good) == note
					? Pick(13, note + tail, note, $"Verkauf hier {shortCap} (dein Kauf hier) bis Preis und Lager zurück", $"Verkauf hier {shortCap} (dein Kauf hier)", $"Verkauf {shortCap}")
					: Pick(13, $"Verkauf hier: {note}" + tail, $"Verkauf hier: {note}", note);
			}
			return (buy + sell, l2, l3, verdict, best?.Station.Id ?? default);
		}

		/// <summary>
		/// Runde 29: gelbe Zeile ohne Zusage — untere Zahl = Ergebnis nach EINER weiteren NPC-Ladung beim Abnehmer (das Band aus dem Verlauf allein wurde in
		/// 32–37 % der gelben Fahrten unterschritten, so in 15–17 %); ist die Spanne ganz positiv, steht der Grund für Gelb dabei.
		/// </summary>
		static string Riskant(BuyerQuote b, float today, float low, string head, Func<int, string[], string> pick)
		{
			low = MathF.Min(low, today);
			string why = !b.HasHistory ? "noch kein voller Verlauf" : b.CashCover < MarketInfo.GreenCashCover ? "Kasse des Abnehmers knapp"
				: b.AcceptCover < MarketInfo.GreenCover ? "Lager des Abnehmers fast voll" : b.Incoming >= 0.5f ? "andere liefern" : "Spanne unter 10 %";
			if (MathF.Abs(today - low) < 0.5f) return pick(13, new[] { $"{head}Riskant: etwa {today:+0;−0;0} Cr ({why})", $"Riskant: etwa {today:+0;−0;0} Cr" });
			string tail = low >= 0f ? $" — {why}" : " (nach einer weiteren Lieferung)";
			return pick(13, new[] { $"{head}Riskant: zwischen {low:+0;−0;0} und {today:+0;−0;0} Cr{tail}", $"Riskant: {low:+0;−0;0} bis {today:+0;−0;0} Cr{tail}", $"Riskant: {low:+0;−0;0} bis {today:+0;−0;0} Cr" });
		}

		/// <summary>Runde 28: was die kaufbare Menge begrenzt — Geld, Lager der Station oder Laderaum.</summary>
		static string LimitText(Universe u, Station st, Ship ship, Row row, float price)
		{
			float cash = price > 0f ? MathF.Floor(u.WalletOf(ship).Credits / price) : float.MaxValue;
			float stock = st.Market.TryGetValue(row.Good, out var e) ? MathF.Floor(e.Amount) : 0f;
			float room = MathF.Floor(ship.FreeUnits(u, row.Good));
			return cash <= stock && cash <= room ? "bezahlbar" : stock <= room ? "auf Lager" : "passen in den Laderaum";
		}

		/// <summary>Runde 23: Grund für ein abgeblendetes „Kaufen“ aus dem Angebot (Exportsenke, Fahndung, Lager leer, Geld, Laderaum).</summary>
		public static string BuyReason(Universe u, Station st, Ship ship, Row row)
		{
			if (!row.Traded || !row.HasBuy) return "Handelt diese Ware nicht";
			if (row.Buy.Note is { Length: > 0 } n) return n;   // Fahndung (Handelsverweigerung)
			if (SpaceSim.Core.Simulation.ExportRules.Buys(u, st, row.Good)) return "Nur Ankauf (Export)";
			if (st.IsBuildSite) return "Baustelle verkauft nicht";
			if (!st.Market.TryGetValue(row.Good, out var e) || e.Amount < 1f) return "Lager leer";
			if (u.WalletOf(ship).Credits < e.Price) return "Nicht genug Credits";
			if (ship.FreeUnits(u, row.Good) < 1f) return "Kein Platz im Laderaum (für diese Form)";
			return "nicht kaufbar";
		}

		public override void _Draw()
		{
			var font = ThemeDB.FallbackFont;
			var ctx = _owner.Ctx;
			if (ctx == null) return;
			float w = Size.X - 48f;
			var (l1, l2, l3, verdict, buyerId) = LinesEx(ctx, _row, _qty, (t, fs) => font.GetStringSize(t, HorizontalAlignment.Left, -1, fs).X <= w, _owner._hint);
			_owner.Shown(_row.Good, _owner.LivePrice(_row));
			TestOverflow = font.GetStringSize(l1, HorizontalAlignment.Left, -1, 14).X > w || font.GetStringSize(l2, HorizontalAlignment.Left, -1, 14).X > w
				|| font.GetStringSize(l3, HorizontalAlignment.Left, -1, 13).X > w;
			DrawnText.Clear(this);
			UiKit.DrawFit(this, 0, font, new Vector2(34, 78), l1, 14, w, Dim);
			UiKit.DrawFit(this, 1, font, new Vector2(34, 98), l2, 14, w, l2.StartsWith("Derzeit") ? new Color(Dim, 0.7f) : White);
			// Runde 26: Ampel — grün nur, wenn auch die vorsichtige Schätzung Gewinn zeigt und der Abnehmer die Ladung sicher nimmt; gelb „unsicher“
			var c3 = verdict switch { TripVerdict.Good => Green, TripVerdict.Unsure => Orange, TripVerdict.Loss => Red, _ => SellNoteColor(_row) };
			if (_row.Sell.Note is not null && _row.Held > 0f && verdict is null) c3 = SellNoteColor(_row);
			UiKit.DrawFit(this, 2, font, new Vector2(34, 118), l3, 13, w, c3);
			TestLines = (l1, l2, l3);
			TestVerdict = verdict;
			TestBuyer = buyerId;
		}

		/// <summary>Runde 23: zuletzt gezeichnete Hinweiszeilen (Selbsttest: Überlauf, Inhalte).</summary>
		public (string, string, string) TestLines { get; private set; }
		public string TestDropHint { get; private set; } = "";
		/// <summary>Runde 25: musste eine Hinweiszeile trotz kürzester Fassung gekürzt werden?</summary>
		public bool TestOverflow { get; private set; }
		/// <summary>Runde 26: Ampel der zuletzt gezeichneten Zeilen.</summary>
		public TripVerdict? TestVerdict { get; private set; }
		public StationId TestBuyer { get; private set; }
	}
}
