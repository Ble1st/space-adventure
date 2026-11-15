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
public sealed partial class FactionsScreen : CommandScreen, ISessionScreen
{
	public override string Id => "cc.factions";
	/// <summary>Gerätetest Phase 27: Auswahl und Meldung bei neuer Sitzung zurücksetzen.</summary>
	public void ResetSession() { Selected = null; _detailOpen = false; }

	/// <summary>
	/// Runde 37 (Handytest r35 g12: schmal lag die Liste über dem Detail mit doppeltem Rollen, nur ≈ 2,5 Zeilen sichtbar): schmal zeigt der Bereich entweder
	/// die Liste über die ganze Höhe oder — nach dem Antippen einer Zeile — das Detail mit „← Liste“.
	/// </summary>
	bool _detailOpen;
	/// <summary>Runde 37c: zuletzt schmal gebaut (Liste oder Detail)?</summary>
	public bool IsNarrow { get; private set; }
	/// <summary>Selbsttest: schmal ist das Detail offen.</summary>
	public bool TestDetailOpen { get => _detailOpen; set { _detailOpen = value; Invalidate(); } }

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
		bool narrow = IsNarrow = InnerW(inner) < 1021.5f;   // Runde 35/37: schmal — Liste oder Detail; Runde 37b: schmal, sobald Liste (404) und Detail (598) ab x 424 nicht nebeneinander passen
		if (!narrow || !_detailOpen) BuildList(inner, u, rows, sel, narrow);
		if (narrow && !_detailOpen) return;
		BuildDetail(inner, u, rows, sel, narrow);
	}

	void BuildList(Control inner, Universe u, List<ActorStat> rows, ActorId sel, bool narrow)
	{
		var scroll = At(inner, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, 12, 12, narrow ? InnerW(inner) - 24f : 404, narrow ? InnerH(inner) - 24f : 500);
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
			UiKit.OnTap(row, () => { Selected = id; if (narrow) _detailOpen = true; Invalidate(); });   // Runde 37: Wisch wählt nichts; schmal öffnet das Detail
			float rel = Relations.Effective(u, a.Id, u.PlayerActor);
			row.AddChild(new ActorRow(a, ColorOf(u, a.Id), rel, isSel));
			list.AddChild(row);
		}
	}

	void BuildDetail(Control inner, Universe u, List<ActorStat> rows, ActorId sel, bool narrow)
	{
		// ── Detail rechts (schmal: über die ganze Fläche, mit „← Liste“) ──
		var stat = rows.First(r => r.Id == sel);
		var actor = u.Actors[sel];
		float dy = 12f;
		if (narrow)
		{
			var back = At(inner, new KitButton("← Liste", new Vector2(160, 56)) { FontSize = 17 }, 12, 8);
			back.Pressed += () => { _detailOpen = false; Invalidate(); };
			dy = 72f;
		}
		// Runde 37 (Handytest r35 n14: bei „normal“ lagen die Knöpfe unter dem Rand): Karte in der Höhe der Fläche, Knöpfe als fester Fuß, der Text darüber rollt
		float detW = narrow ? MathF.Max(598f, InnerW(inner) - 24f) : 598f;
		float detH = MathF.Max(300f, InnerH(inner) - dy - 12f);
		var frame = narrow ? CardBg(inner, 12, dy, detW, detH, Card) : CardBg(inner, 424, 12, detW, detH, Card);
		// Runde 37b (Handytest r37: schmal nur ≈ 4 Zeilen über der Knopfzeile): schmal stehen die Knöpfe oben neben „← Liste“, der Text hat die ganze Karte
		float footH = narrow ? 0f : 76f;
		var bodyScroll = At(frame, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 0, 0, detW, detH - footH);
		var det = new Control { MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(detW - 8f, 10) };
		bodyScroll.AddChild(det);
		bool isFaction = actor.IsFaction(u);
		bool isFree = actor.Kind == ActorKind.Free;
		At(det, new ColorRect { Color = ColorOf(u, sel), CustomMinimumSize = new Vector2(22, 22), Size = new Vector2(22, 22), MouseFilter = MouseFilterEnum.Ignore }, 18, 18);
		At(det, Text(SpaceSim.Core.Model.ActorNames.Tagged(u, actor.Id), 26, White), 50, 12, 520, 34);
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
		// Phase 22: Handlungsfähigkeit (Invarianten: Rücklage, Mindestpolizei, Bauschiff, Sitz; Piraten: Versteck) — reine Core-Anzeige
		TestViability = FactionViability.Text(u, sel) ?? "";
		// Phase 28: Kriegslage neben der Handlungsfähigkeit
		TestWar = isFaction ? SpaceSim.Core.Combat.WarInfo.FactionLine(u, new FactionId(sel.Value)) : "";
		if (TestViability.Length > 0 || TestWar.Length > 0)
		{
			bool war = TestWar.StartsWith("Krieg", System.StringComparison.Ordinal);
			// Prüfung Phase 28: im Krieg zuerst die Kriegszeile, die Handlungsfähigkeit nur kurz (vorher lief die Zeile über den Rand)
			string viable = TestViability.StartsWith("handlungsfähig", System.StringComparison.Ordinal) ? "handlungsfähig" : TestViability;
			string line = war ? $"{TestWar} · {viable}" : string.Join(" · ", new[] { TestViability, TestWar }.Where(x => x.Length > 0));
			At(det, Text(line, 12, war ? Red : TestViability == "handlungsfähig" ? Dim : Orange), 18, ry + 20, 560, 16);
		}

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
			// Runde 35b: Namensspalte breiter (bei 1,15 wurde „[HPY] Handelsbund Pyrrha“ gekürzt), der Wert braucht nur „+0,50“
			At(det, Text(SpaceSim.Core.Model.ActorNames.Tagged(u, o), 13, White), 356, oy, 176, 18);
			At(det, Text($"{Signed(v)}", 13, RelColor(v), HorizontalAlignment.Right), 532, oy, 50, 18);
			At(det, Text(stored ? Relations.Label(v) : $"{Relations.Label(v)} · Vorgabe", 11, new Color(Dim, 0.85f)), 356, oy + 16, 226, 14);
			oy += 34;
		}

		// Phase 13: Fraktionen zeigen ihren Forschungsstand (echt, ResearchInfo); andere Akteure weiter die Ereignisse (Platzhalter)
		if (u.Rules.ResearchActive && isFaction && ResearchInfo.ForFaction(u, new FactionId(sel.Value)) is { Total: > 0 } rv)   // Phase 14: nur mit Forschung
		{
			TestResearchShown = true;
			At(det, Text("Forschung", 16, Cyan), 18, 316, 120, 22);
			At(det, Text(rv.Summary, 13, White), 120, 318, 460, 20);
			float fy = 342;
			foreach (var pv in rv.Projects)
			{
				var col = pv.Status == ResearchStatus.Done ? Green : pv.Status == ResearchStatus.Running ? Cyan : new Color(Dim, 0.85f);
				string st = pv.Status == ResearchStatus.Done ? "erforscht" : $"{ResearchInfo.StatusName(pv.Status)} {pv.Fraction:P0}";
				At(det, Text($"{pv.Project.Name} — {st}", 13, col), 18, fy, 330, 18);
				fy += 20;
			}
			string lab = rv.Stations.Count == 0 ? "keine Forschungsstation" : string.Join(" · ", rv.Stations.Select(x => $"{x.Station.Name}: {x.Stalled ?? "läuft"}"));
			WrappedText(det, lab, 18, fy + 2, 560, 11, new Color(Dim, 0.85f), 14);
		}
		else
		{
			TestResearchShown = false;
			At(det, Text("Letzte Ereignisse", 16, Cyan), 18, 316, 280, 22);
			float ey = 342;
			foreach (var ev in UiPlaceholders.FactionEvents)
			{
				At(det, Text("▮ " + ev, 13, new Color(Dim, 0.85f)), 18, ey, 560, 18);
				ey += 20;
			}
		}

		// Knöpfe
		det.CustomMinimumSize = new Vector2(detW - 8f, UiKit.ContentBottom(det) + 8f);
		Control btnHost = narrow ? inner : frame;
		float bx0 = narrow ? 184f : 362f, by0 = narrow ? 8f : detH - 68f, mapX = narrow ? 184f + 232f : 150f;
		var bComms = At(btnHost, new KitButton("Kontakt aufnehmen", new Vector2(220, 56)) { FontSize = 17 }, bx0, by0);
		bComms.Pressed += () => CommsRequested?.Invoke();
		bComms.Detail = UiPlaceholders.Tag;
		if (isFaction)
		{
			var bMap = At(btnHost, new KitButton("Gebiet auf Karte", new Vector2(200, 56)) { FontSize = 16 }, mapX, by0);
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
			DrawnText.Clear(this);
			float tw = Size.X - 52f;   // Runde 35b: Breite der Zeile, gemessen eingepasst und gemeldet
			UiKit.DrawFit(this, 0, font, new Vector2(40, 28), _a.Tag.Length > 0 ? $"[{_a.Tag}] {_a.Name}" : _a.Name, 18, tw - 20f, White, minSize: 13);   // Phase 29: Kürzel
			string line = _a.Kind == ActorKind.Free
				? $"Sammelgruppe · {Count(_a.Ships, "Schiff", "Schiffe")}"
				: $"{KindAndClass(_a.Kind, _a.Class)} · {Count(_a.Ships, "Schiff", "Schiffe")} · {Count(_a.Stations, "Station", "Stationen")}";
			UiKit.DrawFit(this, 1, font, new Vector2(40, 48), line, 13, tw, Dim);
			string money = _a.Kind == ActorKind.Free ? "Betriebskassen je Schiff" : $"Kasse {_a.Treasury:0} Cr";
			UiKit.DrawFit(this, 2, font, new Vector2(40, 66), $"{money} · zu dir: {Relations.Label(_rel)}", 12, tw, new Color(Dim, 0.9f));
			if (_sel) UiKit.DrawText(this, 3, font, new Vector2(Size.X - 18f, 28), "▶", 14, Cyan);
		}
	}

	/// <summary>Selbsttest: zeigt die Detailansicht den Forschungsstand (Phase 14: nur mit eingeschalteter Forschung)?</summary>
	public bool TestResearchShown { get; private set; }
	/// <summary>Selbsttest: zuletzt gezeigte Handlungsfähigkeit (Phase 22; leer für Akteure ohne Invarianten).</summary>
	/// <summary>Selbsttest Phase 28: Kriegslage der gewählten Fraktion („Frieden“ / „Krieg mit …“).</summary>
	public string TestWar { get; private set; } = "";
	public string TestViability { get; private set; } = "";
}
