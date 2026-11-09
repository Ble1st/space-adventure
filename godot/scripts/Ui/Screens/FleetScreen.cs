using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Flotte“ (UI.md Abschnitt 4): Liste der Schiffe des Spielers (echt: das Spielerschiff
/// mit Auftrag aus <see cref="Ship.Order"/> bzw. Auto-Mining-Zustand). Befehlsraster (Fliegen, Halten, Handel,
/// Schürfen, Patrouille, Wache, Eskorte, Heimkehr), Verhalten (Passiv, Verteidigen, Angriff, Feuer halten),
/// Automatik und Befehlskapazität sind sichtbar, aber Platzhalter (gesperrt, „Bald“) – Wirkung hat nichts
/// davon, außer der Auto-Mining-Schalter, der an Main durchgereicht wird und echt umschaltet.
/// </summary>
public sealed partial class FleetScreen : CommandScreen
{
	public override string Id => "cc.fleet";

	/// <summary>Auto-Mining umschalten (echte Entsprechung: <see cref="MiningAutopilot"/> des Spielers in Main).</summary>
	public event Action<bool>? AutoMiningToggled;

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		var ships = u.Ships.Values.Where(s => s.Role == ShipRole.Player).OrderBy(s => s.Id.Value).ToList();

		// ── Liste links: die Schiffe des Spielers ──
		At(inner, Text("Deine Schiffe", 18, Cyan), 12, 12, 360, 24);
		var list = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		list.AddThemeConstantOverride("separation", 6);
		At(inner, list, 12, 42, 380, 470);
		foreach (var ship in ships)
		{
			var row = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
			row.AddThemeStyleboxOverride("panel", Box(new Color(0.14f, 0.24f, 0.42f, 0.95f), Cyan, 8, 2, 10));
			row.CustomMinimumSize = new Vector2(368, 96);
			row.AddChild(new ShipRow(ship, Ctx.AutoMiningOn()));
			list.AddChild(row);
		}
		if (ships.Count == 0) At(inner, Text("Keine Schiffe – " + UiPlaceholders.Tag, 14, Dim), 24, 74, 360, 20);
		var add = At(inner, new KitButton("+ Weiteres Schiff", new Vector2(368, 48)) { FontSize = 16, State = KitButtonState.Disabled, Sub = UiPlaceholders.Soon + " (Phase 12)" }, 12, 150);
		At(inner, Text("(der Core kennt nur das aktive Spielerschiff als Besitz des Spielers)", 12, new Color(Dim, 0.7f)), 12, 206, 368, 36);

		// ── Detail rechts: Befehlsraster, Verhalten, Automatik, Befehlskapazität ──
		var det = CardBg(inner, 404, 12, 606, 500, Card);
		var ship0 = ships.FirstOrDefault() ?? Ctx.Player;
		At(det, Text($"Schiff #{ship0.Id.Value} · {ship0.Role}", 22, White), 20, 12, 560, 30);
		bool mining = Ctx.AutoMiningOn();
		string auftrag = mining ? "Auto-Mining läuft (Automatik)" : OrderText(u, ship0);
		At(det, Text($"Auftrag: {auftrag} (echt)", 14, mining ? Orange : Dim), 20, 46, 560, 20);
		At(det, Text($"Standort: {SectorName(u, ship0.Sector)} · Hülle {(int)ship0.Hull} (echt)", 14, White), 20, 70, 560, 20);

		// Automatik: einziger echter Schalter (Auto-Mining)
		At(det, Text("Automatik", 16, Cyan), 20, 104, 200, 22);
		var bMine = At(det, new KitButton("Auto-Mining", new Vector2(220, 46)) { FontSize = 17, State = mining ? KitButtonState.On : KitButtonState.Normal }, 20, 130);
		bMine.Pressed += () => AutoMiningToggled?.Invoke(!Ctx.AutoMiningOn());
		At(det, Text("einziger echter Schalter hier – der Rest ist " + UiPlaceholders.Tag, 12, new Color(Dim, 0.75f)), 250, 142, 340, 20);

		// Befehlsraster (gesperrt)
		At(det, Text("Neuen Befehl geben", 16, Cyan), 20, 190, 300, 22);
		float bx = 20, by = 216;
		int i = 0;
		foreach (var cmd in UiPlaceholders.FleetCommands)
		{
			At(det, new KitButton(cmd, new Vector2(134, 44)) { FontSize = 15, State = KitButtonState.Disabled, Detail = UiPlaceholders.Soon }, bx + (i % 4) * 144, by + (i / 4) * 52);
			i++;
		}
		At(det, Text("grau: keine Wirkung – " + UiPlaceholders.Tag + " (" + UiPlaceholders.Soon + ")", 12, new Color(Dim, 0.7f)), 20, by + 2 * 52 + 2, 560, 18);

		// Verhalten (gesperrt)
		At(det, Text("Verhalten", 16, Cyan), 20, 346, 200, 22);
		float rx = 20;
		foreach (var beh in UiPlaceholders.FleetBehaviors)
		{
			At(det, new KitButton(beh, new Vector2(134, 40)) { FontSize = 14, State = KitButtonState.Disabled, Detail = UiPlaceholders.Soon }, rx, 372);
			rx += 144;
		}

		// Befehlskapazität (Platzhalter)
		At(det, Text(UiPlaceholders.FleetCommandCapacity, 14, new Color(Dim, 0.85f)), 20, 428, 560, 22);
		At(det, new KitButton("Befehl erteilen", new Vector2(210, 46)) { FontSize = 17, State = KitButtonState.Disabled, Detail = UiPlaceholders.Soon }, 390, 454);
	}

	/// <summary>Zeile eines Schiffes: Name, Auftrag/Auto-Mining, Status.</summary>
	sealed partial class ShipRow : Control
	{
		readonly Ship _ship; readonly bool _mining;
		public ShipRow(Ship ship, bool mining)
		{
			_ship = ship; _mining = mining;
			MouseFilter = MouseFilterEnum.Ignore;
			CustomMinimumSize = new Vector2(348, 76);
			Size = CustomMinimumSize;
		}

		public override void _Draw()
		{
			var font = ThemeDB.FallbackFont;
			DrawString(font, new Vector2(12, 28), $"Dein Schiff #{_ship.Id.Value}", HorizontalAlignment.Left, -1, 19, White);
			DrawString(font, new Vector2(12, 50), _mining ? "Auto-Mining (Automatik)" : "kein Auftrag", HorizontalAlignment.Left, -1, 13, _mining ? Orange : Dim);
			DrawString(font, new Vector2(12, 70), "von dir gesteuert · aktiv", HorizontalAlignment.Left, -1, 13, Cyan);
		}
	}
}
