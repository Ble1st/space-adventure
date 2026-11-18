using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Bauen“ (docs/STATIONEN.md „Eigene Stationen bauen“): links alle Stationstypen, die der Spieler bauen darf (<see cref="PlayerBuilding.Options"/>),
/// darunter die eigenen Baustellen; rechts das Detail — Bausumme, Stückliste (<see cref="ConstructionRules.Bill"/>), Unterhalt, Einfluss, Rezepte, Größe und
/// Radius, ob der Bau hier geht oder warum nicht. „Bauplatz wählen“ schließt die Zentrale und startet im Flug den Platzierungsmodus (Main). Regeln nur im Core.
/// </summary>
public sealed partial class BuildScreen : CommandScreen, ISessionScreen
{
	public override string Id => "cc.build";
	public void ResetSession() { _selected = null; _site = null; _detailOpen = false; _message = ""; }

	/// <summary>„Bauplatz wählen“ für einen Typ: Main schließt die Zentrale und setzt den Geist vor das Schiff.</summary>
	public event Action<string>? PlaceRequested;

	string? _selected;
	StationId? _site;
	bool _detailOpen;
	string _message = "";
	readonly KitConfirm _confirm = new();

	/// <summary>Selbsttest: Schlüssel der Einträge in der Reihenfolge der Liste, gewählter Typ, Zeile „hier erlaubt / Grund“.</summary>
	public List<string> TestKeys { get; } = new();
	public string TestSelected => _selected ?? "";
	public string TestAllowedLine { get; private set; } = "";
	public string TestBillLine { get; private set; } = "";
	/// <summary>Selbsttest: wählt einen Typ wie ein Tipp in der Liste.</summary>
	public void TestSelect(string key) { _selected = key; _site = null; _detailOpen = true; Invalidate(); }

	protected override string HeaderInfo(Universe u, string playerSector) => $"Eigene Stationen · Bauplatz im Flug in {playerSector}";

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		var options = PlayerBuilding.Options(u);
		TestKeys.Clear();
		TestKeys.AddRange(options.Select(o => o.Type.Key));
		if (!u.Rules.PlayerBuilding || options.Count == 0)
		{
			WrappedText(inner, u.Rules.PlayerBuilding ? "In dieser Welt gibt es keine Stationstypen, die du bauen kannst." : "Eigene Stationen sind in dieser Welt abgeschaltet.", 24, 24, InnerW(inner) - 48f, 18, Dim);
			return;
		}
		var sites = PlayerBuilding.OwnSites(u);
		if (_site is { } sid && !sites.Any(s => s.Id == sid)) _site = null;
		if (_selected is null || options.All(o => o.Type.Key != _selected)) _selected = options[0].Type.Key;
		bool narrow = InnerW(inner) < 1000f;
		if (!narrow || !_detailOpen) BuildList(inner, u, options, sites, narrow);
		if (narrow && !_detailOpen) return;
		float dx = narrow ? 12f : 404f, dy = 12f;
		if (narrow)
		{
			var back = At(inner, new KitButton("← Liste", new Vector2(160, 56)) { FontSize = 17 }, 12, 8);
			back.Pressed += () => { _detailOpen = false; Invalidate(); };
			dy = 72f;
		}
		float w = narrow ? InnerW(inner) - 24f : InnerW(inner) - dx - 12f;
		float h = MathF.Max(300f, InnerH(inner) - dy - 12f);
		if (_site is { } s0 && u.Stations.TryGetValue(s0, out var site)) BuildSiteDetail(inner, u, site, dx, dy, w, h);
		else BuildTypeDetail(inner, u, options.First(o => o.Type.Key == _selected), dx, dy, w, h);
	}

	void BuildList(Control inner, Universe u, List<BuildOption> options, List<Station> sites, bool narrow)
	{
		float lw = narrow ? InnerW(inner) - 24f : 380f;
		var scroll = At(inner, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, 12, 12, lw, InnerH(inner) - 24f);
		var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		list.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(list);
		if (sites.Count > 0) list.AddChild(Text($"Eigene Baustellen ({sites.Count}/{u.Rules.PlayerMaxSites})", 15, Orange));
		foreach (var s in sites)
		{
			var info = ConstructionInfo.Of(u, s);
			var b = new KitButton(info?.TypeName ?? s.Name, new Vector2(lw - 24f, 56)) { AlignLeft = true, FontSize = 16, Sub = $"{u.Sectors[s.Sector].Name} · Material {(info?.Progress ?? 0f) * 100f:0} %" };
			if (_site == s.Id) b.State = KitButtonState.On;
			var id = s.Id;
			b.Pressed += () => { _site = id; _detailOpen = true; Invalidate(); };
			list.AddChild(b);
		}
		list.AddChild(Text("Stationstypen", 15, Cyan));
		foreach (var o in options)
		{
			var b = new KitButton(o.Type.Name, new Vector2(lw - 24f, 56)) { AlignLeft = true, FontSize = 16, Sub = $"{o.Cost:0} Cr · {SizeName(o.Type.Size)}" + (o.Issue is null ? " · hier möglich" : "") };
			if (_site is null && o.Type.Key == _selected) b.State = KitButtonState.On;
			if (o.Issue is not null) { b.Detail = "✕"; b.DetailColor = Dim; }
			string key = o.Type.Key;
			b.Pressed += () => { _selected = key; _site = null; _detailOpen = true; Invalidate(); };
			list.AddChild(b);
		}
	}

	static string SizeName(StationSize s) => s switch { StationSize.S => "klein (S)", StationSize.M => "mittel (M)", StationSize.L => "groß (L)", StationSize.XL => "sehr groß (XL)", _ => s.ToString() };

	void BuildTypeDetail(Control inner, Universe u, BuildOption o, float x, float y, float w, float h)
	{
		var t = o.Type;
		var frame = CardBg(inner, x, y, w, h, Card);
		const float footH = 80f;
		var bodyScroll = At(frame, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 0, 0, w, h - footH);
		var det = new Control { MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(w - 8f, 10) };
		bodyScroll.AddChild(det);
		float cw = w - 40f;
		At(det, Text(t.Name, 24, White), 18, 12, cw, 32);
		float cy = 52f;
		Row(det, 18, cy, cw, "Bausumme (Treuhandgeld)", $"{o.Cost:0} Cr aus deiner Kasse", 14); cy += 24;
		TestBillLine = PlayerBuilding.BillText(u, o.Bill);
		cy = WrappedText(det, $"Material: {TestBillLine}", 18, cy, cw, 14, White, 20) + 4f;
		Row(det, 18, cy, cw, "Unterhalt", $"{t.UpkeepPerTick * SimClock.TicksPerSecond * 60f:0.#} Cr/min (aus der Stationskasse)", 14); cy += 24;
		Row(det, 18, cy, cw, "Einfluss im Sektor", $"{t.Influence:0.#}", 14); cy += 24;
		Row(det, 18, cy, cw, "Größe", $"{SizeName(t.Size)} · Radius {t.Radius:0} m", 14); cy += 24;
		if (t.Recipes.Count == 0) cy = WrappedText(det, "Kein Rezept (Lager/Handel)" + (t.Export is not null ? " · Exportsenke an einem Planeten" : "") + (t.Services.Count > 0 ? $" · Dienste: {string.Join(", ", t.Services.Select(k => DockAreas.ServiceName(k)))}" : ""), 18, cy, cw, 14, Dim, 20) + 4f;
		foreach (var r in t.Recipes.Take(8)) cy = WrappedText(det, "Rezept: " + PlayerBuilding.RecipeText(u, r), 18, cy, cw, 14, Dim, 20);
		if (t.Recipes.Count > 8) cy = WrappedText(det, $"… und {t.Recipes.Count - 8} weitere Rezepte", 18, cy, cw, 14, Dim, 20);
		cy += 6f;
		TestAllowedLine = o.Issue is null ? $"Hier möglich ({u.Sectors[u.PlayerSector].Name})" : $"Hier nicht möglich: {o.Issue}";
		cy = WrappedText(det, TestAllowedLine, 18, cy, cw, 15, o.Issue is null ? Green : Orange, 21) + 4f;
		cy = WrappedText(det, "Material bringen eigene Bauschiffe (Befehl „Bauen“) oder Händler über den Handel. Die Montage braucht ein eigenes Bauschiff vor Ort.", 18, cy, cw, 13, new Color(Dim, 0.8f), 18);
		det.CustomMinimumSize = new Vector2(w - 8f, cy + 10f);
		var b = At(frame, new KitButton("Bauplatz wählen", new Vector2(MathF.Min(260f, w - 36f), 56)) { FontSize = 17, Sub = o.Issue is null ? "im Flug vor dem Schiff" : "" }, 18, h - footH + 12f);
		if (o.Issue is not null) b.State = KitButtonState.Disabled;
		else { string key = t.Key; b.Pressed += () => PlaceRequested?.Invoke(key); }
		if (_message.Length > 0) At(frame, Text(_message, 13, Orange), MathF.Min(296f, w - 36f), h - footH + 30f, MathF.Max(100f, w - 316f), 20);
	}

	void BuildSiteDetail(Control inner, Universe u, Station site, float x, float y, float w, float h)
	{
		var info = ConstructionInfo.Of(u, site)!;
		var frame = CardBg(inner, x, y, w, h, Card);
		const float footH = 80f;
		var bodyScroll = At(frame, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 0, 0, w, h - footH);
		var det = new Control { MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(w - 8f, 10) };
		bodyScroll.AddChild(det);
		float cw = w - 40f;
		At(det, Text($"Baustelle: {info.TypeName}", 24, White), 18, 12, cw, 32);
		float cy = 52f;
		Row(det, 18, cy, cw, "Sektor", u.Sectors[site.Sector].Name, 14); cy += 24;
		Row(det, 18, cy, cw, "Treuhandgeld", $"{info.Escrow:0} Cr", 14); cy += 24;
		Row(det, 18, cy, cw, "Bauschiff", info.Builder, 14); cy += 24;
		cy = WrappedText(det, info.State, 18, cy, cw, 15, Cyan, 21) + 4f;
		foreach (var l in info.Lines) { Row(det, 18, cy, cw, l.Name, $"{l.Delivered:0} / {l.Required:0}", 14); cy += 22; }
		det.CustomMinimumSize = new Vector2(w - 8f, cy + 10f);
		var id = site.Id;
		var b = _confirm.Wire(new KitButton("Baustelle aufgeben", new Vector2(MathF.Min(260f, w - 36f), 56)) { FontSize = 16 }, "cancel:" + id.Value, "Baustelle aufgeben", () =>
		{
			if (PlayerBuilding.CancelIssue(Ctx.U, id) is { } why) { _message = why; Invalidate(); return; }
			float refund = PlayerBuilding.Cancel(Ctx.U, id);
			_message = $"Baustelle aufgegeben – {refund:0} Cr zurück, geliefertes Material verfällt.";
			_site = null;
			Invalidate();
		}, "Rest des Treuhandgelds zurück");
		At(frame, b, 18, h - footH + 12f);
	}
}
