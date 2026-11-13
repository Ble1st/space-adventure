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
public abstract partial class CommandScreen : Control, IScreen
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

	public void Open() { Visible = true; Rebuild(); }
	public void Close() => Visible = false;

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
		foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }

		var u = Ctx.U; var ship = Ctx.Player;
		string sector = u.Sectors.TryGetValue(u.PlayerSector, out var sec) ? sec.Name : "?";

		// ── Top-Leiste: Titel links, Credits und Schiff rechts ──
		var top = At(this, new Control { MouseFilter = MouseFilterEnum.Ignore }, 20, 10, 1240, 64);
		At(top, Text("Kommandozentrale", 28, Cyan), 0, 6);
		At(top, Text(HeaderInfo(u, sector), 16, Dim), 0, 42);
		At(top, Text($"{(int)u.WalletOf(ship).Credits} Credits", 26, Orange, HorizontalAlignment.Right), 940, 0, 300, 32);
		At(top, Text($"Schiff #{ship.Id.Value} · {sector}", 15, Dim, HorizontalAlignment.Right), 940, 38, 300, 22);

		// ── Linke Leiste: Wechsel zwischen den Bereichen ──
		var rail = At(this, new PanelContainer { MouseFilter = MouseFilterEnum.Stop }, 14, 84, 200, 540);
		rail.AddThemeStyleboxOverride("panel", Box(PanelBg, null, 10, 0, 8));
		var rv = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		rv.AddThemeConstantOverride("separation", 6);
		rail.AddChild(rv);
		foreach (var (id, label, sub) in CommandAreas.All)
		{
			var b = new KitButton(label, new Vector2(184, 56)) { AlignLeft = true, FontSize = 16, Sub = sub };
			string key = id;
			b.Pressed += () => AreaSelected?.Invoke(key);
			if (id == AreaId) b.State = KitButtonState.On;
			if (id == CommandAreas.Comms && UiPlaceholders.CommsUnread > 0)
			{
				b.Detail = UiPlaceholders.CommsUnread.ToString();
				b.DetailColor = Orange;
			}
			rv.AddChild(b);
		}
		rv.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
		rv.AddChild(Text($"Schiff: #{ship.Id.Value} (aktiv)", 13, Dim));

		// ── Mittelteil ──
		var center = At(this, new PanelContainer(), 224, 84, 1042, 540);
		center.AddThemeStyleboxOverride("panel", Box(PanelBg, Edge, 10, 2, 10));
		var inner = new Control { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(1022, 520), Size = new Vector2(1022, 520) };
		center.AddChild(inner);
		BuildContent(inner);

		// ── Fußleiste: „← Flug“ immer sichtbar ──
		var back = At(this, new KitButton("← Flug", new Vector2(160, 58)) { FontSize = 20 }, 14, 648);
		back.Pressed += () => BackToFlight?.Invoke();
	}

	/// <summary>Baut den Inhalt des Mittelteils (Innenbereich 1022 × 520 px).</summary>
	protected abstract void BuildContent(Control inner);

	/// <summary>Zeilenhilfe in frei platzierten Karten: Name links, Wert rechts, an fester Position.</summary>
	protected static void Row(Control parent, float x, float y, float w, string name, string value, int size = 15)
	{
		var r = At(parent, new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }, x, y, w, size + 6);
		r.AddThemeConstantOverride("separation", 8);
		var l = Text(name, size, Dim); l.CustomMinimumSize = new Vector2(150, 0);
		r.AddChild(l);
		var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
		r.AddChild(spacer);
		r.AddChild(Text(value, size, White));
	}

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
