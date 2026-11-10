using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Meine Fraktion“ (UI.md Abschnitt 4): Kasse der Person (= Credits des Spielerschiffs –
/// im Core hängt das Geld heute am Schiff und wandert später zur Person, siehe POLITIK.md „Spielerkasse“,
/// deshalb so beschriftet), Zahl eigener Schiffe/Stationen (echt). Größenstufe, Fortschritt, Mitgliedschaft,
/// Unterhalt, Liegegebühr, Einnahmen, Ruf/Fahndung und Fähigkeiten mit Schloss sind Platzhalter über
/// <see cref="UiPlaceholders"/>.
/// </summary>
public sealed partial class MyFactionScreen : CommandScreen
{
	public override string Id => "cc.myfaction";

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		var ship = Ctx.Player;
		int myShips = u.Ships.Values.Count(s => s.IsPlayer);
		int myStations = 0;   // Spielerbesitz an Stationen gibt es im Core noch nicht

		// ── Karte 1 (links oben): Du ──
		var a = CardBg(inner, 12, 12, 500, 250, Card);
		At(a, Text("Du", 26, White), 20, 14, 460, 34);
		At(a, Text(UiPlaceholders.MyFactionSize, 15, Dim), 20, 52, 460, 22);
		At(a, Text(UiPlaceholders.MyFactionProgress, 15, Orange), 20, 80, 460, 22);
		WrappedText(a, $"Eigene Schiffe: {myShips} (echt) · eigene Stationen: {myStations} (Spielerbesitz gibt es im Core noch nicht)", 20, 112, 460, 14, White, 19);
		WrappedText(a, $"Mitglied von: {UiPlaceholders.MyFactionMembership}", 20, 158, 460, 14, new Color(Dim, 0.85f), 19);
		WrappedText(a, "Tipp: eine eigene Station oder weitere Schiffe heben die Stufe – " + UiPlaceholders.Tag, 20, 196, 460, 13, new Color(Dim, 0.75f), 17);

		// ── Karte 2 (rechts oben): Kasse der Person ──
		var b = CardBg(inner, 524, 12, 486, 250, Card);
		At(b, Text("Kasse der Person", 18, Cyan), 20, 14, 446, 26);
		// Das Geld hängt heute am Spielerschiff (Ship.Credits) und wandert später zur Person (POLITIK.md „Spielerkasse“)
		At(b, Text($"{(int)ship.Credits} Cr", 26, Orange, HorizontalAlignment.Right), 20, 42, 446, 34);
		WrappedText(b, "= Credits des Spielerschiffs (echt) – wandert später zur Person, siehe POLITIK.md „Spielerkasse“", 20, 80, 446, 12, new Color(Dim, 0.75f), 16);
		Row(b, 20, 126, 446, "Unterhalt Schiffe/Tag", UiPlaceholders.MyFactionUpkeep, 14);
		Row(b, 20, 150, 446, "Liegegebühr/Tag (fremde St.)", UiPlaceholders.MyFactionBerthFee, 14);
		Row(b, 20, 174, 446, "Einnahmen/Tag (Schätzung)", UiPlaceholders.MyFactionIncome, 14);
		At(b, new KitButton("Beitreten", new Vector2(150, 46)) { FontSize = 16, State = KitButtonState.Disabled, Detail = UiPlaceholders.Soon }, 20, 204);
		At(b, new KitButton("Gründen (ab Kleinfirma)", new Vector2(240, 46)) { FontSize = 15, State = KitButtonState.Disabled, Detail = UiPlaceholders.Soon }, 180, 204);

		// ── Karte 3 (links unten): Ruf und Fahndung ──
		var rep = CardBg(inner, 12, 274, 500, 234, Card);
		At(rep, Text("Ruf und Fahndung", 18, Cyan), 20, 14, 460, 26);
		At(rep, Text(UiPlaceholders.MyFactionReputation, 14, new Color(Dim, 0.8f)), 20, 46, 460, 22);
		float ry = 76;
		foreach (var f in u.Factions.Values.OrderBy(f => f.Id.Value).Take(5))
		{
			At(rep, new ColorRect { Color = MapOverlay.FactionColor(f.Id), CustomMinimumSize = new Vector2(12, 12), Size = new Vector2(12, 12), MouseFilter = MouseFilterEnum.Ignore }, 22, ry + 3);
			At(rep, Text(f.Name, 15, White), 42, ry, 240, 20);
			At(rep, Text("Ruf: " + UiPlaceholders.Tag, 14, new Color(Dim, 0.8f)), 290, ry, 190, 20);
			ry += 30;
		}

		// ── Karte 4 (rechts unten): Fähigkeiten mit Schloss ──
		var perm = CardBg(inner, 524, 274, 486, 234, Card);
		At(perm, Text("Fähigkeiten nach Größe", 18, Cyan), 20, 14, 260, 26);
		At(perm, Text(UiPlaceholders.Tag, 12, new Color(Dim, 0.6f)), 290, 18, 120, 18);
		float ay = 48;
		foreach (var (name, unlocked, note) in UiPlaceholders.MyFactionAbilities)
		{
			At(perm, Text(unlocked ? "✓" : "🔒", 15, unlocked ? Green : Dim), 20, ay, 24, 20);
			At(perm, Text(name, 15, unlocked ? White : Dim), 48, ay, 250, 20);
			At(perm, Text(note, 12, new Color(Dim, 0.8f), HorizontalAlignment.Right), 300, ay, 166, 20);
			ay += 30;
		}
	}
}
