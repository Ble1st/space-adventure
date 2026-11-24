using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Simulation;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>Ausrüster: „Bauplan anwenden“ — fehlende Module des Bauplans einbauen (Core <see cref="Blueprints.Apply"/>, Regeln des Dienstes „outfitter“).</summary>
public sealed partial class OutfitterScreen
{
	readonly Dictionary<int, KitButton> _bpApply = new();
	/// <summary>Selbsttest: „Anwenden“-Taste eines Bauplans.</summary>
	public KitButton? TestApplyButton(int id) => _bpApply.TryGetValue(id, out var b) && IsInstanceValid(b) ? b : null;

	/// <summary>Baupläne für das Modell dieses Schiffs als Zeilen oben in der rechten Spalte (nichts, wenn es keine gibt).</summary>
	void AddBlueprintRows(VBoxContainer box, float w)
	{
		_bpApply.Clear();
		var u = Ctx.U; var ship = Ctx.Ship;
		var list = Blueprints.Of(u).Where(b => b.ModelKey == ship.ModelKey).OrderBy(b => b.Id).ToList();
		if (list.Count == 0 || ship.Owner != u.PlayerActor) return;
		var head = Text("Baupläne", 15, Cyan);
		box.AddChild(head);
		foreach (var bp in list)
		{
			string? issue = Blueprints.ApplyIssue(u, Ctx.Station, ship, bp);
			int diff = Blueprints.Differences(u, ship, bp)?.Count ?? 0;
			var row = new Control { CustomMinimumSize = new Vector2(w, 64), MouseFilter = MouseFilterEnum.Pass };
			var bg = new Panel { MouseFilter = MouseFilterEnum.Ignore };
			bg.AddThemeStyleboxOverride("panel", Box(Card, issue is null ? Edge : new Color(Dim, 0.25f), 10, 1, 0));
			row.AddChild(bg);
			bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			At(row, Text(KitButton.Fit(ThemeDB.FallbackFont, $"„{bp.Name}“", 16, w - 190), 16, White), 14, 4, w - 190, 24);
			At(row, Text(KitButton.Fit(ThemeDB.FallbackFont, issue ?? $"{diff} Steckplatz/-plätze weichen ab — Module aus Fracht oder Lager", 13, w - 190), 13, issue is null ? Green : Dim), 14, 32, w - 190, 20);
			var b = At(row, new KitButton("Anwenden", new Vector2(150, 56)) { FontSize = 16, State = issue is null ? KitButtonState.Ready : KitButtonState.Disabled }, w - 160, 4);
			int id = bp.Id;
			if (issue is null) b.Pressed += () => ApplyBlueprint(id);
			_bpApply[id] = b;
			box.AddChild(row);
		}
	}

	/// <summary>Wendet einen Bauplan an (nur über die Core-Regel) und zeigt die Meldung.</summary>
	public BlueprintApplyResult ApplyBlueprint(int id)
	{
		var bp = Blueprints.Find(Ctx.U, id);
		var r = bp is null ? new BlueprintApplyResult(0, 0, 0f, "Bauplan unbekannt.") : Blueprints.Apply(Ctx.U, Ctx.Station, Ctx.Ship, bp);
		_message = r.Message;
		_msgOk = r.Installed > 0;
		Executed?.Invoke(_msgOk);
		Invalidate();
		return r;
	}
}
