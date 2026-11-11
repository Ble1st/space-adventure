using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Meine Fraktion“ (UI.md Abschnitt 4), seit Phase 9 aus dem Spielerakteur (<see cref="Universe.Player"/>): Größenklasse und
/// Fortschritt zur nächsten Klasse (Größenzahl aus dem Besitz, <see cref="ActorRules"/>), eigene Schiffe und Stationen (Besitzer = du),
/// Mitgliedschaft, Kasse der Person und die Beziehungen der Fraktionen zu dir (<see cref="Relations.Effective"/>). Unterhalt, Liegegebühr,
/// Einnahmen, Fahndung und die Fähigkeiten mit Schloss sind weiter Platzhalter über <see cref="UiPlaceholders"/>.
/// </summary>
public sealed partial class MyFactionScreen : CommandScreen
{
	public override string Id => "cc.myfaction";

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		var me = EconomyStats.Actors(u).First(a => a.Id == u.PlayerActor);
		var thresholds = u.Rules.ActorSizeThresholds;
		int ci = (int)me.Class;

		// ── Karte 1 (links oben): Du ──
		var a = CardBg(inner, 12, 12, 500, 250, Card);
		At(a, Text("Du", 26, White), 20, 14, 460, 34);
		At(a, Text($"{ActorNames.Size(me.Class)} · Größe {me.Size:0.0}", 15, Dim), 20, 52, 460, 22);
		string next = ci < thresholds.Count
			? $"Nächste Stufe {ActorNames.Size((ActorSizeClass)(ci + 1))} ab Größe {thresholds[ci]:0} (noch {thresholds[ci] - me.Size:0.0})"
			: "Höchste Stufe erreicht";
		At(a, Text(next, 15, Orange), 20, 80, 460, 22);
		float lo = ci == 0 ? 0f : thresholds[ci - 1], hi = ci < thresholds.Count ? thresholds[ci] : me.Size;
		float frac = hi > lo ? Mathf.Clamp((me.Size - lo) / (hi - lo), 0f, 1f) : 1f;
		At(a, new ColorRect { Color = new Color(0, 0, 0, 0.5f), CustomMinimumSize = new Vector2(460, 10), Size = new Vector2(460, 10), MouseFilter = MouseFilterEnum.Ignore }, 20, 108);
		At(a, new ColorRect { Color = Orange, CustomMinimumSize = new Vector2(460 * frac, 10), Size = new Vector2(460 * frac, 10), MouseFilter = MouseFilterEnum.Ignore }, 20, 108);
		WrappedText(a, $"Eigene Schiffe: {me.Ships} · eigene Stationen: {me.Stations} · Kasse {me.Treasury:0} Cr (Größe = Schiffe × {u.Rules.ActorShipWeight:0} + Stationen × {u.Rules.ActorStationWeight:0} + Kasse ÷ {u.Rules.ActorMoneyScale:0})", 20, 124, 460, 13, White, 18);
		string membership = u.Player.Membership is { } m ? FactionName(u, m) : "keiner Fraktion (unabhängig)";
		WrappedText(a, $"Mitglied von: {membership}", 20, 178, 460, 14, new Color(Dim, 0.95f), 19);
		WrappedText(a, "Weitere Schiffe, eine eigene Station oder mehr Geld heben die Stufe.", 20, 206, 460, 13, new Color(Dim, 0.75f), 17);

		// ── Karte 2 (rechts oben): Kasse der Person ──
		var b = CardBg(inner, 524, 12, 486, 250, Card);
		At(b, Text("Kasse der Person", 18, Cyan), 20, 14, 446, 26);
		// Phase 5b: das Geld hängt an der Person (Universe.PlayerWallet), nicht am Schiff (POLITIK.md „Spielerkasse“)
		At(b, Text($"{(int)u.PlayerWallet.Credits} Cr", 26, Orange, HorizontalAlignment.Right), 20, 42, 446, 34);
		WrappedText(b, "Spielerkasse (echt): Geld der Person, nicht des Schiffs – Handel, Tanken und Reparatur buchen hierauf", 20, 80, 446, 12, new Color(Dim, 0.75f), 16);
		Row(b, 20, 126, 446, "Unterhalt Schiffe/Tag", UiPlaceholders.MyFactionUpkeep, 14);
		Row(b, 20, 150, 446, "Liegegebühr/Tag (fremde St.)", UiPlaceholders.MyFactionBerthFee, 14);
		Row(b, 20, 174, 446, "Einnahmen/Tag (Schätzung)", UiPlaceholders.MyFactionIncome, 14);
		At(b, new KitButton("Beitreten", new Vector2(150, 46)) { FontSize = 16, State = KitButtonState.Disabled, Detail = UiPlaceholders.Soon }, 20, 204);
		At(b, new KitButton("Gründen (ab Kleinfirma)", new Vector2(240, 46)) { FontSize = 15, State = KitButtonState.Disabled, Detail = UiPlaceholders.Soon }, 180, 204);

		// ── Karte 3 (links unten): Beziehungen der Fraktionen zu dir (echt), Fahndung (Platzhalter) ──
		var rep = CardBg(inner, 12, 274, 500, 234, Card);
		At(rep, Text("Beziehungen und Fahndung", 18, Cyan), 20, 14, 460, 26);
		At(rep, Text(UiPlaceholders.MyFactionWanted, 13, new Color(Dim, 0.8f)), 20, 44, 460, 20);
		float ry = 72;
		foreach (var f in u.Factions.Values.OrderBy(f => f.Id.Value).Take(4))
		{
			float v = Relations.Effective(u, f.Id, u.PlayerActor);
			At(rep, new ColorRect { Color = MapOverlay.FactionColor(f.Id), CustomMinimumSize = new Vector2(12, 12), Size = new Vector2(12, 12), MouseFilter = MouseFilterEnum.Ignore }, 22, ry + 3);
			At(rep, Text(f.Name, 15, White), 42, ry, 240, 20);
			At(rep, Text($"{Relations.Label(v)} ({(v > 0 ? "+" : "")}{v:0.00})", 14, v >= 0.15f ? Green : v <= -0.15f ? Red : White, HorizontalAlignment.Right), 280, ry, 200, 20);
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
