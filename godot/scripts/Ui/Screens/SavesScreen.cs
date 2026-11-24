using System;
using System.Collections.Generic;
using Godot;
using SpaceAdventure.Game;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Persistence;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Mehrere Spielstände: Bildschirm „Laden“ (Liste aller Stände mit Metadaten, „Laden“, „Löschen“ mit Zwei-Tipp) und „Speichern unter“ (Name per Eingabefeld
/// mit Vorschlag aus Sektor und Datum, „Überschreiben“ eines benannten Stands mit Zwei-Tipp). Liegt in der Menü-Schicht über Startbildschirm bzw. Spiel.
/// Laden und Speichern führt Main aus (Universum), Löschen der Dienst (<see cref="SaveService"/>). Neuaufbau nur bei Änderung, nicht beim Tippen.
/// </summary>
public sealed partial class SavesScreen : Control
{
	public enum Mode { Load, SaveAs }
	public Mode Current { get; private set; }
	/// <summary>Stand laden (Kennung).</summary>
	public event Action<string>? LoadRequested;
	/// <summary>Speichern unter: Name, zu überschreibende Kennung (null = neu).</summary>
	public event Action<string, string?>? SaveRequested;
	public event Action? Closed;

	readonly KitConfirm _confirm = new();
	readonly Dictionary<string, KitButton> _loadButtons = new();
	bool _dirty = true;
	string _name = "";
	string _msg = "";
	bool _msgOk = true;
	LineEdit? _edit;
	KitButton? _save;
	Label? _issue;
	List<SaveSlotMeta> _slots = new();

	public SavesScreen()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
	}

	/// <summary>Öffnen; <paramref name="suggestion"/> ist der Namensvorschlag für „Speichern unter“.</summary>
	public void Open(Mode mode, string suggestion = "")
	{
		Current = mode;
		_name = suggestion;
		_msg = "";
		_confirm.Reset();
		Visible = true;
		_dirty = true;
	}

	public void Close() { Visible = false; _confirm.Reset(); }
	public void Invalidate() => _dirty = true;
	public void ShowMessage(string text, bool ok) { _msg = text; _msgOk = ok; _dirty = true; }

	public override void _Notification(int what) { if (what == NotificationResized) _dirty = true; }

	public override void _Process(double delta)
	{
		if (!Visible || !_dirty || Size.X <= 0f || KitButton.AnyHeld > 0) return;
		_dirty = false;
		Rebuild();
	}

	public override void _Draw() => DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.02f, 0.05f, 0.82f));

	void Rebuild()
	{
		if (_edit is not null) _name = _edit.Text;
		foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
		_loadButtons.Clear();
		_edit = null; _save = null; _issue = null;
		QueueRedraw();
		_slots = SaveService.List();
		var si = UiScale.Div(UiScale.SafeInsets1(), IsInsideTree() ? MathF.Max(1f, GetGlobalTransform().Scale.X) : 1f);
		float uw = Size.X - si.Left - si.Right, uh = Size.Y - si.Top - si.Bottom;
		float cw = MathF.Min(980f, uw - 32f), ch = uh - 32f;
		var card = CardBg(this, si.Left + (uw - cw) / 2f, si.Top + 16f, cw, ch, new Color(0.03f, 0.06f, 0.12f, 0.94f), new Color(Cyan, 0.45f), 14, 2);
		string title = Current == Mode.Load ? "Spielstand laden" : "Speichern unter";
		At(card, Text(title, 24, Cyan), 20, 14, cw - 220, 40);
		var back = At(card, new KitButton("✕ Zurück", new Vector2(170, 56)) { FontSize = 18 }, cw - 186, 10);
		back.Pressed += () => { Close(); Closed?.Invoke(); };
		TestBack = back;

		float y = 74;
		if (Current == Mode.SaveAs)
		{
			_edit = At(card, new LineEdit { Text = _name, PlaceholderText = "Name des Spielstands", MaxLength = SaveSlots.MaxNameLength, SelectAllOnFocus = true }, 20, y, cw - 236, 60);
			_edit.AddThemeFontSizeOverride("font_size", 20);
			_edit.TextChanged += _ => UpdateIssue();
			_save = At(card, new KitButton("Speichern", new Vector2(180, 60)) { FontSize = 20, State = KitButtonState.Ready }, cw - 200, y);
			_save.Pressed += () => { var n = _edit?.Text ?? ""; if (SaveSlots.NameIssue(n, _slots) is null) SaveRequested?.Invoke(n, null); else UpdateIssue(); };
			y += 66;
			_issue = At(card, Text("", 15, Orange), 20, y, cw - 40, 24);
			y += 28;
			UpdateIssue();
		}
		var msg = At(card, Text(_msg, 15, _msgOk ? Green : Red), 20, y, cw - 40, 24);
		TestMessage = msg;
		y += 30;
		if (_slots.Count == 0)
		{
			At(card, Text(Current == Mode.Load ? "Keine Spielstände vorhanden." : "Noch keine Spielstände — Name eingeben und „Speichern“.", 17, Dim), 20, y, cw - 40, 30);
			return;
		}
		var scroll = At(card, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, 14, y, cw - 28, ch - y - 12);
		var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		box.AddThemeConstantOverride("separation", 8);
		var pad = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		pad.AddThemeConstantOverride("margin_right", CommandMenu.ScrollbarGap);
		pad.AddChild(box);
		scroll.AddChild(pad);
		float rw = cw - 28 - CommandMenu.ScrollbarGap - 14;
		foreach (var m in _slots) Row(box, m, rw);
	}

	void Row(VBoxContainer box, SaveSlotMeta m, float rw)
	{
		const float rh = 92;
		var holder = new Control { CustomMinimumSize = new Vector2(rw, rh), MouseFilter = MouseFilterEnum.Pass };
		box.AddChild(holder);
		var row = CardBg(holder, 0, 0, rw, rh, new Color(0.04f, 0.08f, 0.16f, 0.9f), new Color(m.Autosave ? Dim : Edge, 0.6f), 10, 1);
		float bw = Current == Mode.Load ? 296f : (m.Autosave ? 0f : 200f);
		float tw = rw - bw - 30f;
		At(row, Text(m.Name, 19, m.Autosave ? Dim : White), 14, 6, tw, 28);
		string when = m.SavedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm") + (m.Autosave ? " · Autosave" : "");
		At(row, Text(when, 14, Cyan), 14, 36, tw, 22);
		At(row, Text(SaveSlots.Line(m), 14, Dim), 14, 60, tw, 22);
		string id = m.Id;
		if (Current == Mode.Load)
		{
			var load = At(row, new KitButton("Laden", new Vector2(140, 60)) { FontSize = 19, State = KitButtonState.Ready }, rw - 298, (rh - 60) / 2f);
			load.Pressed += () => LoadRequested?.Invoke(id);
			_loadButtons[id] = load;
			var del = At(row, new KitButton("Löschen", new Vector2(140, 60)) { FontSize = 18 }, rw - 150, (rh - 60) / 2f);
			_confirm.Wire(del, "del:" + id, "Löschen", () =>
			{
				bool ok = SaveService.Delete(id);
				ShowMessage(ok ? $"Gelöscht: {m.Name}" : $"Löschen fehlgeschlagen: {m.Name}", ok);
			});
		}
		else if (!m.Autosave)
		{
			var over = At(row, new KitButton("Überschreiben", new Vector2(186, 60)) { FontSize = 17 }, rw - 196, (rh - 60) / 2f);
			_confirm.Wire(over, "over:" + id, "Überschreiben", () => SaveRequested?.Invoke(m.Name, id));
		}
	}

	void UpdateIssue()
	{
		if (_edit is null || _issue is null || _save is null) return;
		var issue = SaveSlots.NameIssue(_edit.Text, _slots);
		_issue.Text = issue ?? "";
		_save.State = issue is null ? KitButtonState.Ready : KitButtonState.Disabled;
	}

	// ── Selbsttest / ui-check ──
	public KitButton? TestBack { get; private set; }
	public Label? TestMessage { get; private set; }
	public LineEdit? TestEdit => _edit;
	public KitButton? TestSaveButton => _save;
	public KitButton? TestLoadButton(string id) => _loadButtons.TryGetValue(id, out var b) && IsInstanceValid(b) && b.IsInsideTree() ? b : null;
	public KitButton? TestConfirm(string key) => KitConfirm.Find(this, key);
	public IReadOnlyList<SaveSlotMeta> TestSlots => _slots;
	public void TestRebuild() { _dirty = false; Rebuild(); }
	/// <summary>Alle sichtbaren Texte (Labels) des Bildschirms.</summary>
	public List<string> TestTexts()
	{
		var list = new List<string>();
		void Walk(Node n) { foreach (var c in n.GetChildren()) { if (c is Label l && !l.IsQueuedForDeletion()) list.Add(l.Text); Walk(c); } }
		Walk(this);
		return list;
	}
}
