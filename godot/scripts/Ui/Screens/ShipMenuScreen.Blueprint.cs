using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Schiffsmenü, Reiter „Module“: Baupläne (eigene Schiffsvarianten). Namensfeld + „Als Bauplan speichern“, darunter die Liste mit „Löschen“ (zwei Tipps).
/// Regeln im Core (<see cref="Blueprints"/>); Kauf in der Werft, Anwenden beim Ausrüster.
/// </summary>
public sealed partial class ShipMenuScreen
{
	LineEdit? _bpEdit;
	string _bpName = "";
	string _bpMsg = "";
	bool _bpOk = true;
	KitButton? _bpSave;

	/// <summary>Tippt der Spieler gerade den Bauplannamen (dann kein Neuaufbau)?</summary>
	bool EditingBlueprintName => _bpEdit is { } e && IsInstanceValid(e) && e.HasFocus();

	// ── Selbsttest ──
	public LineEdit? TestBlueprintEdit => _bpEdit is { } e && IsInstanceValid(e) ? e : null;
	public KitButton? TestSaveBlueprintButton => _bpSave is { } b && IsInstanceValid(b) ? b : null;
	public string TestBlueprintMessage => _bpMsg;

	float BuildBlueprints(Control p, Universe u, Ship s, float w, float y)
	{
		_bpEdit = null; _bpSave = null;
		if (s.Owner != u.PlayerActor || !s.HasModel) return y;
		At(p, Text("Baupläne", 18, Cyan), 0, y, w, 26);
		y += 30f;
		if (_bpName.Length == 0 && u.ShipModels.TryGetValue(s.ModelKey, out var m)) _bpName = $"{m.Name} Variante";
		var edit = new LineEdit { Text = _bpName, MaxLength = Blueprints.MaxNameLength, PlaceholderText = "Name des Bauplans", CustomMinimumSize = new Vector2(w - 300f, 56) };
		edit.AddThemeFontSizeOverride("font_size", 18);
		edit.TextChanged += t => _bpName = t;
		At(p, edit, 0, y, w - 300f, 56);
		_bpEdit = edit;
		string? issue = Blueprints.SaveIssue(u, s, _bpName);
		var save = At(p, new KitButton("Als Bauplan speichern", new Vector2(288, 56)) { FontSize = 16, State = KitButtonState.Ready }, w - 288f, y);
		var sid = s.Id;
		save.Pressed += () =>
		{
			if (!Ctx.U.Ships.TryGetValue(sid, out var sh)) return;
			if (_bpEdit is { } e && IsInstanceValid(e)) _bpName = e.Text;
			var bp = Blueprints.Save(Ctx.U, sh, _bpName, out var msg);
			_bpMsg = msg; _bpOk = bp is not null;
			_bpEdit?.ReleaseFocus();
			Rebuild();
		};
		_bpSave = save;
		y += 62f;
		string note = _bpMsg.Length > 0 ? _bpMsg : issue is null
			? "Speichert Modell und Module (Werks-Steckplätze); kaufbar in jeder Werft, die die Klasse baut. Gleicher Name ersetzt."
			: issue;
		y = WrappedText(p, note, 0, y, w, 14, _bpMsg.Length > 0 ? (_bpOk ? Green : Orange) : issue is null ? Dim : Orange, 18) + 6f;
		var list = Blueprints.Of(u).OrderBy(b => b.Id).ToList();
		if (list.Count == 0) { At(p, Text("Noch keine Baupläne.", 14, Dim), 0, y, w, 22); return y + 26f; }
		foreach (var bp in list)
		{
			var row = At(p, new KitButton($"„{bp.Name}“", new Vector2(w - 150f, 60)) { AlignLeft = true, FontSize = 16, Sub = Blueprints.Describe(u, bp), State = KitButtonState.Normal }, 0, y);
			row.MouseFilter = MouseFilterEnum.Ignore;
			var del = At(p, new KitButton("Löschen", new Vector2(140, 60)) { FontSize = 16 }, w - 140f, y);
			int id = bp.Id;
			_confirm.Wire(del, $"bp-del:{id}", "Löschen", () => { Blueprints.Delete(Ctx.U, id); _bpMsg = "Bauplan gelöscht."; _bpOk = true; Rebuild(); });
			y += 66f;
		}
		return y;
	}
}
