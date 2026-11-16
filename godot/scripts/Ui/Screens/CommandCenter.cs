using System;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kontext der Kommandozentrale: Universum, Spielerschiff und abgeleitete Anzeigezustände. Main setzt die
/// Felder vor dem Öffnen; die Bildschirme lesen nur daraus (die Simulation bleibt im Core).
/// </summary>
public sealed class CommandContext
{
	public Universe U = null!;
	public Ship Player = null!;
	/// <summary>Aktueller Tick inkl. Bruchteil (für die NPC-Positionen über Materializer).</summary>
	public Func<double> FractionalTick = () => 0;
	/// <summary>True, solange der Auto-Mining-Modus des Spielers läuft (View-Zustand, wird an Main durchgereicht).</summary>
	public Func<bool> AutoMiningOn = () => false;
	/// <summary>Station, an der der Spieler gerade angedockt ist (sonst null).</summary>
	public StationId? Docked;
	/// <summary>Phase 12: Dienst-Registry (Taxi auf der Universumskarte, Verkauf im Besitz); Instanz aus der Sitzung.</summary>
	public SpaceSim.Core.Services.StationServices Services = SpaceSim.Core.Services.StationServices.Default();
	/// <summary>Runde 40: Comms-Aktionsregistry der Sitzung (Posteingang-Antworten und Diplomatie-Knöpfe führen nur darüber aus).</summary>
	public SpaceSim.Core.Comms.CommsActions Comms = SpaceSim.Core.Comms.CommsActions.Default();
	/// <summary>Runde 38: Einnahmen der Spielerkasse (Ereignisse des letzten Spieltags) für „Meine Fraktion“; null = keine Messung.</summary>
	public Func<SpaceSim.Core.Diagnostics.PlayerIncome?> Income = () => null;
}

/// <summary>Bereiche der Kommandozentrale (linke Leiste), Reihenfolge wie in UI.md Abschnitt 4.</summary>
public static class CommandAreas
{
	public const string Universe = "universe";
	public const string Sector = "sector";
	public const string Factions = "factions";
	public const string MyFaction = "myfaction";
	public const string Fleet = "fleet";
	public const string Assets = "assets";
	public const string Comms = "comms";
	public const string System = "system";

	/// <summary>Alle Bereiche in der Reihenfolge der Leiste („System“ zuletzt).</summary>
	public static readonly (string Id, string Label, string Sub)[] All =
	{
		(Universe, "Universum", "Sektorkarte & Route"),
		(Sector, "Sektor", "Draufsicht"),
		(Factions, "Fraktionen", "Beziehungen"),
		(MyFaction, "Meine Fraktion", "Kasse & Ruf"),
		(Fleet, "Flotte", "Schiffe & Befehle"),
		(Assets, "Besitz", "Schiffe & Stationen"),
		(Comms, "Comms", "Posteingang"),
		(System, "System", "Spielstand"),
	};

	public static string LabelFor(string id) => id switch
	{
		Universe => "Universum",
		Sector => "Sektor",
		Factions => "Fraktionen",
		MyFaction => "Meine Fraktion",
		Fleet => "Flotte",
		Assets => "Besitz",
		Comms => "Comms",
		System => "System",
		_ => id,
	};
}

/// <summary>
/// Basis der Kommandozentralen-Bildschirme (UI.md Abschnitt 4, Vorlage prototypes/FinalUi2.cs „Shell“ und
/// <see cref="DockAreaScreen"/>): oben Titel und Credits, links die Leiste mit den Bereichen
/// (<see cref="CommandAreas"/>), Inhalt rechts, „← Flug“ unten links. Konkrete Bereiche bauen den Mittelteil
/// in <see cref="BuildContent"/>.
/// </summary>
public abstract partial class CommandScreen : Control, IScreen, IMessageStrip
{
	public abstract string Id { get; }
	/// <summary>Id des angezeigten Bereichs; setzt der Aufrufer vor dem Öffnen (<see cref="CommandAreas"/>).</summary>
	public string AreaId { get; set; } = "";
	public virtual string Title => CommandAreas.LabelFor(AreaId);
	public Control Node => this;
	public bool IsOpen => Visible;

	/// <summary>Kontext (Universum, Spielerschiff …); der Aufrufer setzt ihn vor dem Öffnen.</summary>
	public CommandContext Ctx { get; set; } = null!;

	/// <summary>Wechsel in einen anderen Bereich (Id aus <see cref="CommandAreas"/>).</summary>
	public event Action<string>? AreaSelected;
	/// <summary>„← Flug“: Kommandozentrale schließen, zurück zum Flug.</summary>
	public event Action? BackToFlight;
	/// <summary>System-Bereich: „save“, „load“, „new“ (an Main durchgereicht).</summary>
	public event Action<string>? SystemAction;

	/// <summary>Löst <see cref="AreaSelected"/> aus (nur im deklarierenden Typ erlaubt, deshalb die Hilfsmethode).</summary>
	protected void RaiseAreaSelected(string id) => AreaSelected?.Invoke(id);
	/// <summary>Löst <see cref="SystemAction"/> aus.</summary>
	protected void RaiseSystemAction(string action) => SystemAction?.Invoke(action);

	long _builtTick = -1;
	bool _dirty = true;

	protected CommandScreen()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
	}

	public void Open() { Visible = true; OnOpen(); Rebuild(); }
	public void Close() { Visible = false; }
	/// <summary>Runde 42c: Hook beim Öffnen des Bereichs (z. B. Comms setzt die alte Meldung zurück — wie <see cref="DockAreaScreen"/>-Bereiche).</summary>
	protected virtual void OnOpen() { }
	/// <summary>Runde 17 (Speicher wächst mit „Neues Spiel“): Inhalt abbauen — Knöpfe und Zeilen halten das alte Universum in ihren Schließungen; Open baut neu.</summary>
	public void DropContent() { foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); } _dirty = true; }

	/// <summary>Aktualisiert die Anzeige, wenn sich der Simulations-Tick geändert hat.</summary>
	public void Refresh()
	{
		if (!Visible || Ctx == null) return;
		if (Ctx.U.Tick != _builtTick || _dirty) SafeRebuild();
	}

	/// <summary>
	/// Gerätetest Phase 27: Neuaufbau ohne die Rollposition zu verlieren (die Bereiche bauen sich je Tick neu auf — vorher sprang jede Liste viermal je Sekunde
	/// nach oben) und nie mitten in einem Wisch (der Rollbereich würde unter dem Finger ersetzt).
	/// </summary>
	/// <summary>Kopfzeile unter dem Titel (Bereich und aktueller Sektor); Bereiche mit eigener Auswahl überschreiben sie (Gerätetest Phase 27).</summary>
	protected virtual string HeaderInfo(SpaceSim.Core.Model.Universe u, string playerSector) => $"{Title} · aktuell: {playerSector}";

	void SafeRebuild()
	{
		if ((KitScroll.Dragging || KitButton.AnyHeld > 0) && GetChildCount() > 0) { _dirty = true; return; }   // zweiter Gerätetest Phase 28: auch nicht bei gedrückter Taste
		var keep = KitScroll.Capture(this);
		Rebuild();
		KitScroll.Restore(this, keep);
	}

	/// <summary>Setzt die Anzeige nach einer Aktion (z. B. Sektorauswahl) sofort neu auf.</summary>
	protected void Invalidate() { _dirty = true; SafeRebuild(); }

	public override void _Draw()
	{
		if (Visible) DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.02f, 0.05f, 0.82f));
	}

	/// <summary>Baut das gesamte Gerüst neu auf (Top-Leiste, Seitenleiste, Mittelteil, Fußleiste).</summary>
	protected virtual void Rebuild()
	{
		_builtTick = Ctx.U.Tick;
		_dirty = false;
		// Runde 37b (Handytest r37 N2: die nach unten gerollte Leiste ließ sich nicht mehr nach oben wischen): die gemeinsame Leistenposition folgt der echten
		// Rollposition — vorher wurde sie nur beim Tipp gemerkt, jeder Neuaufbau (je Tick) setzte die Leiste auf den alten Wert zurück
		if (_railScroll is { } rs && IsInstanceValid(rs) && rs.IsInsideTree()) RailPos = rs.WantedScroll;
		foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }

		var u = Ctx.U; var ship = Ctx.Player;
		string sector = u.Sectors.TryGetValue(u.PlayerSector, out var sec) ? sec.Name : "?";

		// Runde 35 (UI-ANPASSUNG.md E4): Gerüst aus der Fläche der Schicht (Core: CommandLayout.Shell) — bei 1280 × 720 die bisherigen Lagen
		float k0 = GetParent() is Control lp && lp.Scale.X > 0f ? lp.Scale.X : 1f;
		var L = Shell = SpaceSim.Core.Ui.CommandLayout.Shell(Size.X, Size.Y, UiScale.Div(UiScale.SafeInsets1(), k0));

		// ── Top-Leiste: Titel links, Credits und Schiff rechts ──
		var top = At(this, new Control { MouseFilter = MouseFilterEnum.Ignore }, L.Top.X, L.Top.Y, L.Top.W, L.Top.H);
		float cxr = L.Credits.X - L.Top.X;
		At(top, Text("Kommandozentrale", L.Compact ? 24 : 28, Cyan), 0, L.Compact ? 2 : 6, cxr - 10, 34);
		At(top, Text(HeaderInfo(u, sector), L.Compact ? 14 : 16, Dim), 0, L.Compact ? 36 : 42, cxr - 10, 22);
		At(top, Text($"{(int)u.WalletOf(ship).Credits} Credits", L.Compact ? 22 : 26, Orange, HorizontalAlignment.Right), cxr, 0, L.Credits.W, 32);
		At(top, Text($"Schiff #{ship.Id.Value} · {sector}", L.Compact ? 14 : 15, Dim, HorizontalAlignment.Right), cxr, L.Compact ? 34 : 38, L.Credits.W, 22);

		// ── Linke Leiste: Wechsel zwischen den Bereichen (Runde 35: rollt, wenn die Leiste niedriger ist als acht Bereiche) ──
		var rail = At(this, new PanelContainer { MouseFilter = MouseFilterEnum.Stop }, L.Rail.X, L.Rail.Y, L.Rail.W, L.Rail.H);
		rail.AddThemeStyleboxOverride("panel", Box(PanelBg, null, 10, 0, 8));
		var railScroll = new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass, OwnPosition = true };   // Runde 37b: Position über RailPos
		rail.AddChild(railScroll);
		var rv = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		rv.AddThemeConstantOverride("separation", 6);
		railScroll.AddChild(rv);
		int activeIdx = 0, ii = 0;
		foreach (var (id, label, sub) in CommandAreas.All)
		{
			var b = new KitButton(label, new Vector2(L.Rail.W - 16, 56)) { AlignLeft = true, FontSize = 16, Sub = sub };
			string key = id;
			if (id == AreaId) activeIdx = ii;
			ii++;
			b.Pressed += () => { RailPos = railScroll.ScrollVertical; AreaSelected?.Invoke(key); };
			if (id == AreaId) b.State = KitButtonState.On;
			int unread = SpaceSim.Core.Comms.Comms.Unread(u);   // Runde 40: echter Zähler aus dem Posteingang
			if (id == CommandAreas.Comms && unread > 0)
			{
				b.Detail = unread.ToString();
				b.DetailColor = Orange;
			}
			rv.AddChild(b);
		}
		rv.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
		rv.AddChild(Text($"Schiff: #{ship.Id.Value} (aktiv)", 13, Dim));
		// Runde 37 (Handytest r35 g16–g18: nach Rollen der Leiste und Antippen sprang sie nach oben, „System“ war bei „groß“ kaum erreichbar): jeder Bereich ist
		// ein eigener Bildschirm mit eigener Leiste — die Rollposition gilt für alle gemeinsam, und der aktive Eintrag bleibt sichtbar
		float view = MathF.Max(56f, L.Rail.H - 16f);
		float top0 = activeIdx * 62f, bot0 = top0 + 56f + 26f;   // + Platz für den Hinweis „weiter unten“ (KitScroll), der sonst den Eintrag verdeckt
		int pos = RailPos;
		// Runde 37b (N2): nur beim Wechsel des Bereichs den aktiven Eintrag ins Bild holen — nicht bei jedem Neuaufbau (sonst holt jeder Tick die Leiste zurück,
		// nachdem man sie weggewischt hat)
		string shownKey = $"{AreaId}@{view:0}";   // auch neu, wenn sich die Leistenhöhe ändert (UI-Größe, Drehen)
		if (_railShownFor != shownKey)
		{
			if (top0 < pos) pos = (int)top0;
			else if (bot0 > pos + view) pos = (int)MathF.Ceiling(bot0 - view);
			_railShownFor = shownKey;
		}
		RailPos = pos;
		railScroll.ScrollTo(pos);
		_railScroll = railScroll;

		// ── Mittelteil (Runde 35: Innenfläche aus dem Gerüst; ist der Inhalt höher, rollt der Bereich als Ganzes) ──
		var center = At(this, new PanelContainer(), L.Center.X, L.Center.Y, L.Center.W, L.Center.H);
		_centerFrame = center; _centerWant = new Vector2(L.Center.W, L.Center.H);
		center.AddThemeStyleboxOverride("panel", Box(PanelBg, Edge, 10, 2, 10));
		var inner = new Control { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(L.InnerW, L.InnerH), Size = new Vector2(L.InnerW, L.InnerH) };
		// Runde 37b (Handytest r37 N3: Comms/System-Rahmen ≈ 18 px weiter rechts): der sichtbare Rollbalken des als Ganzes rollenden Bereichs vergrößerte die
		// Mindestbreite des Rahmens — Balken ausgeblendet (Rollen per Wisch/Rad bleibt, der Hinweis „weiter unten“ zeigt es an)
		var areaScroll = new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever, MouseFilter = MouseFilterEnum.Pass };
		center.AddChild(areaScroll);
		areaScroll.AddChild(inner);
		BuildContent(inner);
		float bottom = ContentBottom(inner);   // Runde 35: rekursiv (Tasten in Karten zählen mit)
		if (bottom > L.InnerH + 1f) inner.CustomMinimumSize = new Vector2(L.InnerW, bottom + 8f);

		// ── Fußleiste: „← Flug“ immer sichtbar ──
		var back = At(this, new KitButton("← Flug", new Vector2(L.Back.W, L.Back.H)) { FontSize = 20 }, L.Back.X, L.Back.Y);
		back.Pressed += () => BackToFlight?.Invoke();
	}

	/// <summary>Runde 37: gemeinsame Rollposition der Bereichsleiste aller Kommandozentralen-Bildschirme.</summary>
	public static int RailPos;
	static string _railShownFor = "";
	/// <summary>Runde 37b: beim Schließen der Zentrale vergessen, für welchen Bereich der aktive Eintrag zuletzt ins Bild geholt wurde.</summary>
	public static void ForgetRailArea() => _railShownFor = "";
	KitScroll? _railScroll;
	/// <summary>Selbsttest: Rollposition der Leiste dieses Bildschirms.</summary>
	public int TestRailScroll => _railScroll is { } r && IsInstanceValid(r) ? r.ScrollVertical : -1;

	/// <summary>Runde 35: zuletzt gerechnetes Gerüst.</summary>
	public SpaceSim.Core.Ui.CommandShell? Shell { get; private set; }
	/// <summary>Runde 35: Meldungsstreifen (Core: CommandShell.MessageStrip) in Bildpunkten.</summary>
	public Rect2? MessageStripGlobal => Shell is { } l && IsVisibleInTree() && l.MessageStrip.W > 0f
		? new Rect2(GetGlobalTransform() * new Vector2(l.MessageStrip.X, l.MessageStrip.Y), new Vector2(l.MessageStrip.W, l.MessageStrip.H) * GetGlobalTransform().Scale.X) : null;

	/// <summary>Runde 37b (N3): Mittelteil-Rahmen — wirklich (Bildpunkte) und laut Gerüst; das Prüfwerkzeug vergleicht beide.</summary>
	Control? _centerFrame;
	Vector2 _centerWant;
	public (Rect2 Actual, Vector2 ExpectedSize)? CenterCheck => _centerFrame is { } cf && IsInstanceValid(cf) && cf.IsVisibleInTree()
		? (cf.GetGlobalRect(), _centerWant * cf.GetGlobalTransform().Scale.X) : null;

	public override void _Notification(int what)
	{
		if (what == NotificationResized && Visible && Ctx is not null && GetChildCount() > 0) { _dirty = true; SafeRebuild(); }
	}

	/// <summary>Baut den Inhalt des Mittelteils (Innenbereich <see cref="SpaceSim.Core.Ui.CommandShell.InnerW"/> × InnerH; bei 1280 × 720 1022 × 520).</summary>
	protected abstract void BuildContent(Control inner);

	/// <summary>
	/// Runde 35 (E4): schmale Innenfläche (Handy bei „groß“, unter 1000) — Bereiche mit Liste + Detail legen beides untereinander (die Liste oben, das Detail
	/// darunter in voller Breite); der Bereich rollt als Ganzes. Bei 1280 × 720 (1022 breit) bleibt alles nebeneinander.
	/// </summary>
	protected static bool Narrow(Control inner) => (inner.Size.X > 0 ? inner.Size.X : 1022f) < 1000f;
	protected static float InnerW(Control inner) => inner.Size.X > 0 ? inner.Size.X : 1022f;
	protected static float InnerH(Control inner) => inner.Size.Y > 0 ? inner.Size.Y : 520f;

	/// <summary>Zeilenhilfe in frei platzierten Karten: Name links, Wert rechts, an fester Position.</summary>
	protected static void Row(Control parent, float x, float y, float w, string name, string value, int size = 15)
	{
		// Runde 25 (Gerätetest g01: „Laufende Kosten“ lief rechts aus der Karte): der Wert bekommt höchstens die Breite neben dem Namen, wird dafür bis
		// Schriftgröße 11 verkleinert und erst dann mit „…“ gekürzt — die Zeile bleibt in der Karte
		var font = ThemeDB.FallbackFont;
		float W(string t, int fs) => font.GetStringSize(t, HorizontalAlignment.Left, -1, fs).X;
		float nameW = MathF.Max(150f, W(name, size) + 4f);
		float room = w - nameW - 8f;
		int vs = size;
		while (vs > 11 && W(value, vs) > room) vs--;
		var l = Text(name, size, Dim);
		At(parent, l, x, y, nameW, size + 6);
		var v = Text(value, vs, White, HorizontalAlignment.Right);
		v.ClipText = true;
		v.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		At(parent, v, x + w - room, y + (size - vs) * 0.5f, room, size + 6);
		LastRowOverflow = W(value, vs) > room + 0.5f;
	}

	/// <summary>Runde 25, für den Selbsttest: musste der Wert der zuletzt gebauten Zeile gekürzt werden?</summary>
	internal static bool LastRowOverflow;

	/// <summary>Name einer Fraktion (oder „?“).</summary>
	protected static string FactionName(Universe u, FactionId f) => u.Factions.TryGetValue(f, out var x) ? SpaceSim.Core.Model.ActorNames.Tagged(u, f, x.Name) : f.Value == 0 ? "herrenlos" : "?";   // Phase 29: mit Kürzel
	/// <summary>Name eines Sektors (oder „?“).</summary>
	protected static string SectorName(Universe u, SectorId id) => u.Sectors.TryGetValue(id, out var s) ? s.Name : "?";
	/// <summary>Name einer Station (oder Id).</summary>
	protected static string StationName(Universe u, StationId id) => u.Stations.TryGetValue(id, out var s) ? s.Name : id.Value.ToString();
	/// <summary>Name einer Ware (oder Id).</summary>
	protected static string GoodName(Universe u, GoodId g) => u.Goods.TryGetValue(g, out var x) ? x.Name : g.Value.ToString();

	/// <summary>Auftrag eines Schiffs als Text (echt aus <see cref="Ship.Order"/>).</summary>
	protected static string OrderText(Universe u, Ship ship) => ship.Order switch
	{
		null => "kein Auftrag",
		TradeOrder t => $"Handel {GoodName(u, t.Good)}: {StationName(u, t.Buy)} → {StationName(u, t.Sell)}",
		MineOrder m => $"Abbau in {SectorName(u, m.Sector)}, liefern an {StationName(u, m.DeliverTo)}",
		PatrolOrder p => $"Patrouille in {SectorName(u, p.Sector)}",
		GoToOrder g => $"Flug nach {StationName(u, g.Target)}",
		_ => ship.Order.GetType().Name,
	};
}
