using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Fraktionen“ (UI.md Abschnitt 4), seit Phase 9 aus den echten Akteuren des Core: links alle Akteure außer dir —
/// Fraktionen zuerst, dann aufgestiegene Einzelakteure (nach Größe) und die Sammelgruppe „Freie“ — mit Art, Größenklasse, Besitz, Kasse und
/// Beziehung zu dir (<see cref="EconomyStats.Actors"/>, <see cref="Relations.Effective"/>). Rechts das Detail: Mitgliedschaft, Größenzahl, Lage,
/// Persönlichkeit (<see cref="Actor.Personality"/>) und die Beziehungen zu den anderen Fraktionen und Gruppen (gespeicherte Abweichung oder
/// Vorgabe). Nur „Letzte Ereignisse“ und „Kontakt aufnehmen“ sind noch Platzhalter (<see cref="UiPlaceholders"/>).
/// </summary>
public sealed partial class FactionsScreen : CommandScreen
{
	public override string Id => "cc.factions";

	/// <summary>Angezeigter Akteur (null = erste Fraktion); setzt der Aufrufer oder Antippen.</summary>
	public ActorId? Selected { get; set; }

	/// <summary>„Kontakt aufnehmen“: öffnet den Comms-Bereich (Platzhalter).</summary>
	public event Action? CommsRequested;
	/// <summary>„Gebiet auf Karte“: Universum öffnen mit Fraktionsebene und markiertem Besitz.</summary>
	public event Action<FactionId>? TerritoryRequested;

	/// <summary>Test-Hook: Akteur wählen (Fraktionen über ihre Id, implizit umgewandelt).</summary>
	public void TestSelect(ActorId a) { Selected = a; Invalidate(); }

	/// <summary>Test-Hook: die Zeilen der Liste (Akteure in Anzeige-Reihenfolge).</summary>
	public IReadOnlyList<ActorStat> TestRows => Rows(Ctx.U);

	/// <summary>Reihenfolge der Liste: Fraktionen (Id), Einzelakteure (Größe absteigend, Id), Sammelgruppe; der Spieler steht unter „Meine Fraktion“.</summary>
	static List<ActorStat> Rows(Universe u)
	{
		var all = EconomyStats.Actors(u).Where(a => a.Id != u.PlayerActor).ToList();
		return all.Where(a => u.Factions.ContainsKey(new FactionId(a.Id.Value))).OrderBy(a => a.Id.Value)
			.Concat(all.Where(a => a.Kind == ActorKind.Independent).OrderByDescending(a => a.Size).ThenBy(a => a.Id.Value))
			.Concat(all.Where(a => a.Kind == ActorKind.Free))
			.ToList();
	}

	static Color ColorOf(Universe u, ActorId a) => u.FactionOf(a) is { } f ? MapOverlay.FactionColor(f) : new Color(0.65f, 0.68f, 0.72f);

	static Color RelColor(float v) => v >= 0.15f ? Green : v <= -0.15f ? Red : White;

	static string Signed(float v) => $"{(v > 0 ? "+" : "")}{v:0.00}";

	/// <summary>Zahl mit Einzahl/Mehrzahl.</summary>
	static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

	/// <summary>Art und Größenklasse; gleichlautende (Staat · Staat) nur einmal.</summary>
	static string KindAndClass(ActorKind kind, ActorSizeClass cls) =>
		ActorNames.Kind(kind) == ActorNames.Size(cls) ? ActorNames.Kind(kind) : $"{ActorNames.Kind(kind)} · {ActorNames.Size(cls)}";

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		var rows = Rows(u);
		if (rows.Count == 0) return;
		var sel = Selected is { } s && rows.Any(r => r.Id == s) ? s : rows[0].Id;

		// ── Liste links (scrollbar: aufgestiegene Einzelakteure kommen und gehen) ──
		var scroll = At(inner, new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, 12, 12, 404, 500);
		var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		list.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(list);
		foreach (var a in rows)
		{
			bool isSel = a.Id == sel;
			var row = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
			row.AddThemeStyleboxOverride("panel", Box(isSel ? new Color(0.14f, 0.24f, 0.42f, 0.95f) : Card, isSel ? Cyan : null, 8, isSel ? 2 : 0, 8));
			row.CustomMinimumSize = new Vector2(388, 84);
			var id = a.Id;
			row.GuiInput += e =>
			{
				if (Pointer.TryRead(e, out _, out _, out bool pressed, out bool motion) && pressed && !motion) { Selected = id; Invalidate(); row.AcceptEvent(); }
			};
			float rel = Relations.Effective(u, a.Id, u.PlayerActor);
			row.AddChild(new ActorRow(a, ColorOf(u, a.Id), rel, isSel));
			list.AddChild(row);
		}

		// ── Detail rechts ──
		var stat = rows.First(r => r.Id == sel);
		var actor = u.Actors[sel];
		var det = CardBg(inner, 424, 12, 598, 500, Card);
		bool isFaction = actor.IsFaction(u);
		bool isFree = actor.Kind == ActorKind.Free;
		At(det, new ColorRect { Color = ColorOf(u, sel), CustomMinimumSize = new Vector2(22, 22), Size = new Vector2(22, 22), MouseFilter = MouseFilterEnum.Ignore }, 18, 18);
		At(det, Text(actor.Name, 26, White), 50, 12, 520, 34);
		string member = isFaction ? "Fraktion (Akteur mit Politik)"
			: actor.Membership is { } m ? $"Mitglied von {FactionName(u, m)}" : "unabhängig";
		string head = isFree ? "Sammelgruppe der freien Einzelschiffe (jedes ein Einzelakteur)" : $"{KindAndClass(actor.Kind, stat.Class)} (Größe {stat.Size:0.0}) · {member}";
		At(det, Text(head, 14, Dim), 50, 46, 530, 20);

		float relMe = Relations.Effective(u, sel, u.PlayerActor);
		var ry = 74f;
		Row(det, 18, ry, 560, "Beziehung zu dir", $"{Relations.Label(relMe)} ({Signed(relMe)})", 14); ry += 22;
		Row(det, 18, ry, 560, "Kasse", isFree ? "keine (jedes Schiff hat seine Betriebskasse)" : $"{stat.Treasury:0} Cr", 14); ry += 22;
		Row(det, 18, ry, 560, "Besitz", $"{Count(stat.Ships, "Schiff", "Schiffe")} · {Count(stat.Stations, "Station", "Stationen")} · {Count(stat.Sectors, "Sektor", "Sektoren")}", 14); ry += 22;
		var sectors = isFaction ? u.Sectors.Values.Where(x => x.Owner.Value == sel.Value).OrderBy(x => x.Id.Value).Select(x => x.Name).ToList() : new List<string>();
		Row(det, 18, ry, 560, "Lage", sectors.Count > 0 ? string.Join(", ", sectors) : "kein Hoheitsgebiet", 14);

		// Persönlichkeit (echt; die Sammelgruppe teilt einen Satz Werte)
		At(det, Text("Persönlichkeit", 16, Cyan), 18, 176, 280, 22);
		var p = actor.Personality;
		float py = 202;
		foreach (var (name, v) in new[] { ("Aggression", p.Aggression), ("Gier", p.Greed), ("Kooperation", p.Cooperation), ("Gesetzestreue", p.Lawfulness) })
		{
			At(det, Text(name, 14, Dim), 18, py, 130, 18);
			At(det, new ColorRect { Color = new Color(0, 0, 0, 0.5f), CustomMinimumSize = new Vector2(150, 10), Size = new Vector2(150, 10), MouseFilter = MouseFilterEnum.Ignore }, 150, py + 4);
			At(det, new ColorRect { Color = Cyan, CustomMinimumSize = new Vector2(150 * v, 10), Size = new Vector2(150 * v, 10), MouseFilter = MouseFilterEnum.Ignore }, 150, py + 4);
			At(det, Text($"{v:0.00}", 12, Dim), 306, py, 40, 18);
			py += 26;
		}

		// Beziehungen zu den Fraktionen und Gruppen (echt: gespeicherte Abweichung oder Vorgabe)
		At(det, Text("Beziehungen", 16, Cyan), 356, 176, 226, 22);
		var others = u.Factions.Keys.OrderBy(f => f.Value).Select(f => (ActorId)f).Append(u.FreeActor).Where(o => o != sel).Take(5).ToList();
		float oy = 202;
		foreach (var o in others)
		{
			float v = Relations.Effective(u, sel, o);
			bool stored = Relations.IsStored(u, sel, o);
			At(det, Text(u.ActorName(o), 13, White), 356, oy, 140, 18);
			At(det, Text($"{Signed(v)}", 13, RelColor(v), HorizontalAlignment.Right), 496, oy, 86, 18);
			At(det, Text(stored ? Relations.Label(v) : $"{Relations.Label(v)} · Vorgabe", 11, new Color(Dim, 0.85f)), 356, oy + 16, 226, 14);
			oy += 34;
		}

		// Ereignisse (Platzhalter: Politik und Ereignisse kommen ab Phase 10)
		At(det, Text("Letzte Ereignisse", 16, Cyan), 18, 316, 280, 22);
		float ey = 342;
		foreach (var ev in UiPlaceholders.FactionEvents)
		{
			At(det, Text("▮ " + ev, 13, new Color(Dim, 0.85f)), 18, ey, 560, 18);
			ey += 20;
		}

		// Knöpfe
		var bComms = At(det, new KitButton("Kontakt aufnehmen", new Vector2(220, 56)) { FontSize = 17 }, 362, 432);
		bComms.Pressed += () => CommsRequested?.Invoke();
		bComms.Detail = UiPlaceholders.Tag;
		if (isFaction)
		{
			var bMap = At(det, new KitButton("Gebiet auf Karte", new Vector2(200, 56)) { FontSize = 16 }, 150, 432);
			var f = new FactionId(sel.Value);
			bMap.Pressed += () => TerritoryRequested?.Invoke(f);
		}
	}

	/// <summary>Eine Zeile der Akteursliste: Farbe, Name, Art und Größenklasse, Besitz und Kasse, Beziehung zu dir.</summary>
	sealed partial class ActorRow : Control
	{
		readonly ActorStat _a; readonly Color _col; readonly float _rel; readonly bool _sel;
		public ActorRow(ActorStat a, Color col, float rel, bool sel)
		{
			_a = a; _col = col; _rel = rel; _sel = sel;
			MouseFilter = MouseFilterEnum.Ignore;
			CustomMinimumSize = new Vector2(368, 68);
			Size = CustomMinimumSize;
		}

		public override void _Draw()
		{
			var font = ThemeDB.FallbackFont;
			DrawCircle(new Vector2(20, 22), 10f, _col);
			DrawString(font, new Vector2(40, 28), _a.Name, HorizontalAlignment.Left, 250, 18, White);
			string line = _a.Kind == ActorKind.Free
				? $"Sammelgruppe · {Count(_a.Ships, "Schiff", "Schiffe")}"
				: $"{KindAndClass(_a.Kind, _a.Class)} · {Count(_a.Ships, "Schiff", "Schiffe")} · {Count(_a.Stations, "Station", "Stationen")}";
			DrawString(font, new Vector2(40, 48), line, HorizontalAlignment.Left, 330, 13, Dim);
			string money = _a.Kind == ActorKind.Free ? "Betriebskassen je Schiff" : $"Kasse {_a.Treasury:0} Cr";
			DrawString(font, new Vector2(40, 66), $"{money} · zu dir: {Relations.Label(_rel)}", HorizontalAlignment.Left, 330, 12, new Color(Dim, 0.9f));
			if (_sel) DrawString(font, new Vector2(350, 28), "▶", HorizontalAlignment.Left, -1, 14, Cyan);
		}
	}
}
