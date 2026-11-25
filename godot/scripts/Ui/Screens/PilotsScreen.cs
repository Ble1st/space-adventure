using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Pilots;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Politik → Rangliste“ (Piloten): Profilkarte des Spielerpiloten (prozedurales Porträt, Rang, Wertung, Fortschritt, Abschüsse/Verluste/K:D,
/// Aufträge, Kopfgelder, Verbrechen) und die Rangliste aus Spieler und benannten NPC-Kapitänen mit dem Platz des Spielers — nur Anzeige aus
/// <see cref="PilotBook"/>.
/// </summary>
public sealed partial class PilotsScreen : CommandScreen
{
	public override string Id => "cc.pilots";
	/// <summary>Anzahl der oberen Plätze in der Liste (der Spieler steht zusätzlich darunter, wenn er nicht dabei ist).</summary>
	public const int TopRows = 12;

	// Selbsttest
	public string TestPlayerName { get; private set; } = "";
	public string TestRank { get; private set; } = "";
	public string TestRating { get; private set; } = "";
	public string TestKills { get; private set; } = "";
	public int TestPlayerPlace { get; private set; }
	public int TestRows { get; private set; }
	public bool TestPortraitShown { get; private set; }

	protected override string HeaderInfo(Universe u, string playerSector) => $"Rangliste · {PilotBook.PlayerName(u)}";

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		float iw = InnerW(inner);
		bool narrow = Narrow(inner);
		var all = PilotBook.Ranking(u);
		int place = all.FindIndex(p => p.IsPlayer) + 1;
		var me = all[place - 1];
		var stats = u.Actors.TryGetValue(u.PlayerActor, out var pa) ? pa.Stats : null;

		// Profilkarte
		float pw = narrow ? iw - 24f : 360f, ph = 400f;
		var card = CardBg(inner, 12, 12, pw, ph, Card);
		var portrait = new PilotPortrait { Spec = PilotBook.PlayerPortrait(u), Rank = me.Rank };
		At(card, portrait, 20, 16, 112, 112);
		TestPortraitShown = true;
		float tx = 146, tw = pw - tx - 16f;
		At(card, Text(me.Name, 20, Cyan), tx, 18, tw, 28);
		At(card, Text(PilotBook.RankName(me.Rank), 17, Orange), tx, 50, tw, 24);
		At(card, Text($"{me.Rating:0} Punkte", 15, White), tx, 78, tw, 22);
		var (share, next) = PilotBook.Progress(me.Rating);
		var bar = At(card, new KitBar(tw, 10, Cyan), tx, 104, tw, 10);
		bar.Set(share * 100f);
		At(card, Text(next > 0 ? $"nächster Rang ab {next:0}" : "höchster Rang", 12, Dim), tx, 116, tw, 18);
		TestPlayerName = me.Name; TestRank = PilotBook.RankName(me.Rank); TestRating = $"{me.Rating:0}";
		int kills = stats?.Kills ?? 0, deaths = stats?.ShipsLost ?? 0;
		TestKills = kills.ToString();
		float y = 146;
		var rows = new (string, string)[]
		{
			("Platz", $"{place} von {all.Count}"),
			("Abschüsse", kills.ToString()),
			("Abschusspunkte", $"{stats?.KillPoints ?? 0:0}"),
			("Verluste (Schiffe)", deaths.ToString()),
			("K:D", PilotBook.KillDeath(kills, deaths)),
			("Aufträge erfüllt", (stats?.MissionsDone ?? 0).ToString()),
			("Kopfgelder", (stats?.Bounties ?? 0).ToString()),
			("Verbrechen", (stats?.Crimes ?? 0).ToString()),
		};
		foreach (var (l, v) in rows) { Row(card, 20, y, pw - 40, l, v, 15); y += 30; }
		TestPlayerPlace = place;

		// Rangliste
		float lx = narrow ? 12f : pw + 24f, ly = narrow ? 12f + ph + 12f : 12f, lw = narrow ? iw - 24f : iw - pw - 36f;
		var shown = all.Take(TopRows).Select((p, i) => (p, i + 1)).ToList();
		if (place > TopRows) shown.Add((me, place));
		float rowH = 46f;
		float lh = 56f + shown.Count * rowH + (u.Rules.PilotRanks ? 0f : 40f);
		var list = CardBg(inner, lx, ly, lw, lh, Card);
		At(list, Text("Rangliste der Piloten", 18, Cyan), 16, 12, lw - 32, 26);
		float ry = 46;
		float cPlace = 16, cName = 62, cRating = lw - 186, cKills = lw - 96;
		float nameW = cRating - cName - 8f;
		int n = 0;
		foreach (var (p, pl) in shown)
		{
			if (p.IsPlayer) CardBg(list, 8, ry - 2, lw - 16, rowH - 4, new Color(Cyan, 0.12f), new Color(Cyan, 0.5f), 8, 1);
			At(list, Text($"{pl}.", 16, p.IsPlayer ? Cyan : Dim), cPlace, ry + 2, 44, 22);
			At(list, Text(p.Name, 16, p.IsPlayer ? Cyan : White), cName, ry, nameW, 22);
			At(list, Text($"{PilotBook.RankName(p.Rank)} · {p.Affiliation} · {p.Status}", 12, Dim), cName, ry + 22, nameW, 18);
			At(list, Text($"{p.Rating:0}", 16, White, HorizontalAlignment.Right), cRating, ry + 2, 82, 22);
			At(list, Text($"{p.Kills} K", 15, Dim, HorizontalAlignment.Right), cKills, ry + 2, 80, 22);
			ry += rowH; n++;
		}
		TestRows = n;
		if (!u.Rules.PilotRanks)
			At(list, Text("Ränge sind in dieser Welt abgeschaltet.", 14, Orange), 16, ry + 6, lw - 32, 22);
		WrappedText(inner, "Wertung: gewichtete Abschüsse (Gesetzlose und Gesuchte zählen mehr), Aufträge und Kopfgelder; Verbrechen und verlorene Schiffe ziehen ab.",
			12, MathF.Max(ly + lh, 12f + ph) + 12f, iw - 24, 13, new Color(Dim, 0.8f), 18);
	}
}
