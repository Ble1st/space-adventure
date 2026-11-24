using System;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Lücken gegenüber Interstellar Pilot 2: Name und dreistelliges Kürzel der eigenen Fraktion ändern („Deine Fraktion“ → Knopf „Name/Kürzel ändern“).
/// Eingabe über zwei <see cref="LineEdit"/>s (virtuelle Tastatur auf Android), Prüfung und Ausführung im Core (<see cref="PlayerIdentity"/>).
/// Der eingetippte Text überlebt Neuaufbauten (gemerkt im Bildschirm).
/// </summary>
public sealed partial class MyFactionScreen : ISessionScreen
{
	bool _renaming;
	string _nameText = "", _tagText = "", _renameMsg = "";
	LineEdit? _nameEdit, _tagEdit;
	KitButton? _renameOpen, _renameApply;
	public void ResetSession() { _renaming = false; _renameMsg = ""; }

	// ── Selbsttest ──
	public KitButton? TestRenameOpen => _renameOpen;
	public KitButton? TestRenameApply => _renameApply;
	public LineEdit? TestNameEdit => _nameEdit;
	public LineEdit? TestTagEdit => _tagEdit;
	public string TestRenameMessage => _renameMsg;

	void BuildRenameButton(Control card, float x, float y)
	{
		var u = Ctx.U;
		_renameOpen = At(card, new KitButton("Name/Kürzel ändern", new Vector2(190, 56)) { FontSize = 15, State = _renaming ? KitButtonState.On : KitButtonState.Normal }, x, y);
		_renameOpen.Pressed += () =>
		{
			_renaming = !_renaming;
			_nameText = u.Player.Name; _tagText = u.Player.Tag; _renameMsg = "";
			Invalidate();
		};
	}

	void BuildRenameEditor(Control inner)
	{
		_nameEdit = null; _tagEdit = null; _renameApply = null;
		if (!_renaming) return;
		var u = Ctx.U;
		float w = MathF.Min(560f, InnerW(inner) - 24f);
		var card = CardBg(inner, 12, 12, w, 250, PanelSolid, Cyan, 12, 2);
		At(card, Text("Eigene Fraktion umbenennen", 18, Cyan), 20, 12, w - 40, 26);
		At(card, Text("Name", 15, Dim), 20, 58, 90, 24);
		_nameEdit = At(card, KitLineEdit.Create(_nameText, "Name der Fraktion", new Vector2(w - 140, 56), PlayerIdentity.MaxName), 110, 44);
		_nameEdit.TextChanged += t => _nameText = t;
		At(card, Text("Kürzel", 15, Dim), 20, 122, 90, 24);
		_tagEdit = At(card, KitLineEdit.Create(_tagText, "ABC", new Vector2(130, 56), 3), 110, 108);
		_tagEdit.TextChanged += t => _tagText = t;
		At(card, Text("drei Zeichen A–Z, 0–9", 13, new Color(Dim, 0.8f)), 252, 124, w - 272, 22);
		string? issue = PlayerIdentity.RenameIssue(u, _nameText, _tagText);
		string line = _renameMsg.Length > 0 ? _renameMsg : issue ?? $"Wird: [{PlayerIdentity.NormalTag(_tagText)}] {ShipNames.Clean(_nameText)}";
		At(card, Text(KitButton.Fit(ThemeDB.FallbackFont, line, 14, w - 40), 14, issue is null && _renameMsg.Length == 0 ? Green : _renameMsg.Length > 0 ? Green : Orange), 20, 170, w - 40, 22);
		_renameApply = At(card, new KitButton("Übernehmen", new Vector2(170, 56)) { FontSize = 16, State = KitButtonState.Ready }, w - 380, 186);
		_renameApply.Pressed += () =>
		{
			var why = PlayerIdentity.RenameIssue(u, _nameText, _tagText);
			if (why is null && PlayerIdentity.Rename(u, _nameText, _tagText)) { _renameMsg = ""; _renaming = false; }
			else _renameMsg = why ?? "Nicht möglich";
			Invalidate();
		};
		var cancel = At(card, new KitButton("Abbrechen", new Vector2(170, 56)) { FontSize = 16 }, w - 194, 186);
		cancel.Pressed += () => { _renaming = false; _renameMsg = ""; Invalidate(); };
	}
}
