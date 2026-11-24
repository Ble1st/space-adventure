using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Diagnostics;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Politik → Statistik“ (Lücken gegenüber Interstellar Pilot 2): Spielerstatistik als Liste — nur Anzeige aus
/// <see cref="PlayerStatsInfo.Rows"/> (Core, gezählt von <c>PlayerStatsSystem</c>).
/// </summary>
public sealed partial class StatsScreen : CommandScreen
{
	public override string Id => "cc.stats";
	readonly Dictionary<string, string> _shown = new();
	/// <summary>Selbsttest: zuletzt angezeigter Wert einer Zeile ("" = nicht gezeigt).</summary>
	public string TestValue(string label) => _shown.GetValueOrDefault(label, "");

	protected override string HeaderInfo(SpaceSim.Core.Model.Universe u, string playerSector) => $"Statistik · {SpaceSim.Core.Model.ActorNames.Tagged(u, u.PlayerActor)}";

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		var rows = PlayerStatsInfo.Rows(u);
		_shown.Clear();
		float iw = InnerW(inner);
		bool two = iw >= 900f;
		float cw = two ? (iw - 36f) / 2f : iw - 24f;
		int perCol = two ? (rows.Count + 1) / 2 : rows.Count;
		float h = 56f + perCol * 32f;
		for (int c = 0; c < (two ? 2 : 1); c++)
		{
			var card = CardBg(inner, 12 + c * (cw + 12f), 12, cw, h, Card);
			At(card, Text(c == 0 ? "Deine Statistik" : "Kampf, Aufträge, Reisen", 18, Cyan), 20, 12, cw - 40, 26);
			float y = 48;
			foreach (var (label, value) in rows.Skip(c * perCol).Take(perCol))
			{
				Row(card, 20, y, cw - 40, label, value, 15);
				_shown[label] = value;
				y += 32;
			}
		}
		WrappedText(inner, "Gezählt ab Spielbeginn: Kassenzu- und -abgänge, eigene Käufe/Verkäufe, Abbau eigener Schiffe, Abschüsse mit eigener Beteiligung.", 12, 24 + h, iw - 24, 13, new Color(Dim, 0.8f), 18);
	}
}
