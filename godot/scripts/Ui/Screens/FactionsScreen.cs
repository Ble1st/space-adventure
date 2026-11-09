using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Fraktionen“ (UI.md Abschnitt 4): Liste aller Fraktionen aus dem Core – Name, Farbe,
/// Anzahl Stationen, Anzahl Schiffe, Gesamtvermögen (alles echt). Beziehung zum Spieler, Art, Lage,
/// Persönlichkeit, Beziehungen untereinander und Ereignisse sind Platzhalter über <see cref="UiPlaceholders"/>
/// (<see cref="Faction.Relations"/>/<see cref="Faction.Territory"/> sind im Core ungenutzt; Territory wird
/// angezeigt, falls echte Werte vorhanden). Detailansicht beim Antippen; Knöpfe „Kontakt aufnehmen“
/// (öffnet den Comms-Platzhalter) und „Gebiet auf Karte“ (echt: Universum mit Fraktionsebene und markiertem Besitz).
/// </summary>
public sealed partial class FactionsScreen : CommandScreen
{
	public override string Id => "cc.factions";

	/// <summary>Angezeigte Fraktion (null = erste); setzt der Aufrufer oder Antippen.</summary>
	public FactionId? Selected { get; set; }

	/// <summary>„Kontakt aufnehmen“: öffnet den Comms-Bereich (Platzhalter).</summary>
	public event Action? CommsRequested;
	/// <summary>„Gebiet auf Karte“: Universum öffnen mit Fraktionsebene und markiertem Besitz.</summary>
	public event Action<FactionId>? TerritoryRequested;

	FactionId SelectedFaction => Selected ?? (Ctx.U.Factions.Keys.OrderBy(k => k.Value).FirstOrDefault());

	/// <summary>Test-Hook: Fraktion wählen.</summary>
	public void TestSelect(FactionId f) { Selected = f; Invalidate(); }

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		var factions = u.Factions.Values.OrderBy(f => f.Id.Value).ToList();
		var sel = SelectedFaction;

		// ── Liste links ──
		var list = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		list.AddThemeConstantOverride("separation", 6);
		At(inner, list, 12, 12, 400, 500);
		foreach (var f in factions)
		{
			int stations = u.Stations.Values.Count(st => st.Owner == f.Id);
			int ships = u.Ships.Values.Count(sh => sh.Faction == f.Id);
			float wealth = u.Stations.Values.Where(st => st.Owner == f.Id).Sum(st => st.Credits)
				+ u.Ships.Values.Where(sh => sh.Faction == f.Id).Sum(sh => sh.Credits);
			bool isSel = f.Id == sel;
			var row = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
			row.AddThemeStyleboxOverride("panel", Box(isSel ? new Color(0.14f, 0.24f, 0.42f, 0.95f) : Card, isSel ? Cyan : null, 8, isSel ? 2 : 0, 10));
			row.CustomMinimumSize = new Vector2(388, 96);
			var f2 = f;
			row.GuiInput += e =>
			{
				if (Pointer.TryRead(e, out _, out _, out bool pressed, out bool motion) && pressed && !motion) { Selected = f2.Id; Invalidate(); row.AcceptEvent(); }
			};
			var g = new FactionRow(f2, stations, ships, wealth, isSel);
			row.AddChild(g);
			list.AddChild(row);
		}

		// ── Detail rechts ──
		if (!u.Factions.TryGetValue(sel, out var fac)) return;
		var det = CardBg(inner, 424, 12, 598, 500, Card);
		var col = MapOverlay.FactionColor(fac.Id);
		int stCount = u.Stations.Values.Count(st => st.Owner == fac.Id);
		int shCount = u.Ships.Values.Count(sh => sh.Faction == fac.Id);
		float wealth2 = u.Stations.Values.Where(st => st.Owner == fac.Id).Sum(st => st.Credits)
			+ u.Ships.Values.Where(sh => sh.Faction == fac.Id).Sum(sh => sh.Credits);
		var sectors = u.Sectors.Values.Where(s => s.Owner == fac.Id).Select(s => s.Name).ToList();

		At(det, new ColorRect { Color = col, CustomMinimumSize = new Vector2(22, 22), Size = new Vector2(22, 22), MouseFilter = MouseFilterEnum.Ignore }, 18, 18);
		At(det, Text(fac.Name, 26, White), 50, 14, 420, 34);
		At(det, Text($"{stCount} Stationen · {shCount} Schiffe · Vermögen {wealth2:0} Cr (echt)", 15, Dim), 50, 46, 530, 22);
		Row(det, 18, 78, 560, "Beziehung zu dir", UiPlaceholders.FactionRelationToPlayer, 14);
		Row(det, 18, 100, 560, "Art", UiPlaceholders.FactionKind, 14);
		string lage = sectors.Count > 0 ? "Sektoren: " + string.Join(", ", sectors) : "kein Hoheitsgebiet im Core";
		if (fac.Territory.Count > 0) lage += $" · Territory: {fac.Territory.Count}";
		Row(det, 18, 122, 560, "Lage", lage, 14);

		// Persönlichkeit (Platzhalter)
		At(det, Text("Persönlichkeit", 16, Cyan), 18, 156, 280, 22);
		At(det, Text(UiPlaceholders.Tag, 12, new Color(Dim, 0.6f)), 130, 158, 120, 18);
		var pers = UiPlaceholders.FactionPersonality(fac.Name);
		float py = 182;
		foreach (var (name, v) in new[] { ("Aggression", pers.Aggression), ("Gier", pers.Greed), ("Kooperation", pers.Cooperation), ("Gesetzestreue", pers.Lawfulness) })
		{
			At(det, Text(name, 14, Dim), 18, py, 130, 18);
			At(det, new ColorRect { Color = new Color(0, 0, 0, 0.5f), CustomMinimumSize = new Vector2(150, 10), Size = new Vector2(150, 10), MouseFilter = MouseFilterEnum.Ignore }, 150, py + 4);
			At(det, new ColorRect { Color = Cyan, CustomMinimumSize = new Vector2(150 * v, 10), Size = new Vector2(150 * v, 10), MouseFilter = MouseFilterEnum.Ignore }, 150, py + 4);
			py += 26;
		}

		// Beziehungen untereinander: echt, falls Einträge vorhanden, sonst Platzhalter
		At(det, Text("Beziehungen zu anderen", 16, Cyan), 340, 156, 242, 22);
		if (fac.Relations.Count > 0)
		{
			float ry = 182;
			foreach (var (other, v) in fac.Relations.OrderBy(kv => kv.Key.Value))
			{
				At(det, Text(FactionName(u, other), 14, White), 340, ry, 140, 18);
				At(det, Text($"{(v > 0 ? "+" : "")}{v:0}", 14, v < 0 ? Red : Green, HorizontalAlignment.Right), 480, ry, 100, 18);
				ry += 22;
			}
		}
		else WrappedText(det, UiPlaceholders.FactionRelationsSoon, 340, 182, 242, 13, new Color(Dim, 0.8f), 17);

		// Ereignisse (Platzhalter)
		At(det, Text("Letzte Ereignisse", 16, Cyan), 18, 300, 280, 22);
		float ey = 326;
		foreach (var ev in UiPlaceholders.FactionEvents)
		{
			At(det, Text("▮ " + ev, 13, new Color(Dim, 0.85f)), 18, ey, 560, 18);
			ey += 20;
		}

		// Knöpfe
		var bComms = At(det, new KitButton("Kontakt aufnehmen", new Vector2(220, 50)) { FontSize = 17 }, 370, 448);
		bComms.Pressed += () => CommsRequested?.Invoke();
		bComms.Detail = UiPlaceholders.Tag;
		var bMap = At(det, new KitButton("Gebiet auf Karte", new Vector2(200, 50)) { FontSize = 16 }, 150, 448);
		bMap.Pressed += () => TerritoryRequested?.Invoke(fac.Id);
	}

	/// <summary>Eine Zeile der Fraktionsliste: Farbe, Name, Stationen/Schiffe, Vermögen, Beziehungswert (Platzhalter).</summary>
	sealed partial class FactionRow : Control
	{
		readonly Faction _f; readonly int _st, _sh; readonly float _wealth; readonly bool _sel;
		public FactionRow(Faction f, int st, int sh, float wealth, bool sel)
		{
			_f = f; _st = st; _sh = sh; _wealth = wealth; _sel = sel;
			MouseFilter = MouseFilterEnum.Ignore;
			CustomMinimumSize = new Vector2(368, 76);
			Size = CustomMinimumSize;
		}

		public override void _Draw()
		{
			var font = ThemeDB.FallbackFont;
			var col = MapOverlay.FactionColor(_f.Id);
			DrawCircle(new Vector2(22, 26), 11f, col);
			DrawString(font, new Vector2(44, 32), _f.Name, HorizontalAlignment.Left, -1, 19, White);
			DrawString(font, new Vector2(44, 54), $"{_sh} Schiffe · {_st} Stationen", HorizontalAlignment.Left, -1, 13, Dim);
			DrawString(font, new Vector2(44, 72), $"Vermögen {_wealth:0} Cr · zu dir: {UiPlaceholders.Tag}", HorizontalAlignment.Left, -1, 12, new Color(Dim, 0.8f));
			DrawString(font, new Vector2(368 - 150, 32), _sel ? "▶" : "", HorizontalAlignment.Left, -1, 14, Cyan);
		}
	}
}
