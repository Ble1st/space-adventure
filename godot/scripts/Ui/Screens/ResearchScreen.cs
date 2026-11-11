using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Dock-Bereich „Forschung“ (Phase 13, Dienst „research“ an Forschungsstationen): links die Projekte der Fraktion der Station mit Status, FP-Balken,
/// Freischaltungen und Zusatzverbrauch, je Projekt „Beauftragen“ (Gebühr); rechts der Betrieb der Station (FP je Takt, Verbrauch, warum sie stockt,
/// Kasse, voraussichtliche Dauer) und „Mitfinanzieren“ mit den Beträgen des Dienstes. Anzeige aus <see cref="ResearchInfo"/> (reine Core-Daten),
/// Knöpfe ausschließlich aus <see cref="IStationService.Offers"/>, ausgeführt nur über <see cref="IStationService.Execute"/>; Meldung aus
/// <see cref="ServiceResult.Message"/>.
/// </summary>
public sealed partial class ResearchScreen : DockAreaScreen
{
	public override string Id => "dock.research";
	public ResearchScreen() => AreaId = DockAreas.Research;

	/// <summary>Wird nach jedem Execute ausgelöst (true = Erfolg) – Main spielt den Ton.</summary>
	public event Action<bool>? Executed;

	string _message = "";
	bool _msgOk = true;
	int _rows, _fundButtons;

	IStationService? Service() => Ctx.Services.ForStation(Ctx.U, Ctx.Station).FirstOrDefault(s => s.Key == ResearchService.ServiceKey);

	protected override void BuildContent(Control inner)
	{
		float w = inner.Size.X > 0 ? inner.Size.X : 1020f;
		_rows = 0; _fundButtons = 0;
		var u = Ctx.U;
		var view = ResearchInfo.ForStation(u, Ctx.Station);
		var svc = Service();
		if (view is null || svc is null) { At(inner, Text("Hier wird nicht geforscht.", 18, Dim), 0, 0, w, 28); return; }
		var offers = svc.Offers(u, Ctx.Station, Ctx.Ship).ToList();
		string? refusal = ResearchService.Refusal(u, Ctx.Station, Ctx.Ship);

		WrappedText(inner, _message.Length > 0 ? _message
				: refusal ?? $"Forschung von {view.Name}: {view.Summary}. Freischaltungen gelten für alle Stationen der Fraktion.",
			0, 0, w, 14, _message.Length > 0 ? (_msgOk ? Green : Red) : refusal is null ? new Color(Dim, 0.85f) : Orange, 18);

		// ── links: Projekte ──
		float lw = 600f;
		At(inner, Text("Projekte", 20, Cyan), 0, 42, lw, 28);
		float y = 74f;
		foreach (var p in view.Projects)
		{
			var status = p.Status;
			var color = status switch { ResearchStatus.Done => Green, ResearchStatus.Running => Cyan, ResearchStatus.Available => White, _ => new Color(Dim, 0.8f) };
			At(inner, Text(p.Project.Name, 17, color), 0, y, 300, 24);
			string state = status == ResearchStatus.Done ? $"erforscht (Tick {p.CompletedTick})" : $"{ResearchInfo.StatusName(status)} · {p.Progress:0}/{p.Project.Points:0} FP";
			At(inner, Text(state, 13, Dim, HorizontalAlignment.Right), 300, y + 2, 140, 20);
			var bar = At(inner, new KitBar(440, 8, color), 0, y + 26, 440, 8);
			bar.Set(p.Fraction * 100f, color);
			string detail = "Schaltet frei: " + string.Join(", ", p.Unlocks)
				+ (p.Inputs.Length > 0 ? $" · zusätzlich je Takt {p.Inputs}" : "")
				+ (p.MissingRequires.Count > 0 ? $" · braucht zuerst {string.Join(", ", p.MissingRequires)}" : "");
			WrappedText(inner, detail, 0, y + 38, 440, 12, new Color(Dim, 0.9f), 16);
			var offer = offers.FirstOrDefault(o => o.Key == $"{ResearchService.Commission}:{p.Project.Key}");
			if (offer.Key is not null)
			{
				bool can = offer.Amount >= 1f;
				string why = status == ResearchStatus.Running ? "läuft" : status == ResearchStatus.Blocked ? "gesperrt" : refusal is not null ? "abgelehnt" : "zu wenig Cr";
				// Grund in der Unterzeile (Detail zeichnete rechts außerhalb der zentrierten Taste)
				var b = At(inner, new KitButton("Beauftragen", new Vector2(150, 56)) { FontSize = 15, Sub = can ? $"{offer.Price:0} Cr" : $"{offer.Price:0} Cr · {why}", State = can ? KitButtonState.Ready : KitButtonState.Disabled }, lw - 150, y);
				string key = offer.Key;
				if (can) b.Pressed += () => Execute(key);
			}
			y += 104f;
			_rows++;
		}

		// ── rechts: Betrieb und Mitfinanzierung ──
		float rx = lw + 24f, rw = w - rx;
		At(inner, Text("Betrieb", 20, Cyan), rx, 42, rw, 28);
		float ry = 74f;
		foreach (var st in view.Stations)
		{
			At(inner, Text(st.Station.Name, 15, White), rx, ry, rw, 22);
			At(inner, Text(st.Stalled is null ? $"läuft · {st.Points:0} FP je {st.Ticks} Ticks" : $"stockt: {st.Stalled}", 14, st.Stalled is null ? Green : Orange), rx, ry + 22, rw, 20);
			ry = WrappedText(inner, $"Verbrauch je Takt: {st.Needs} · Kasse {st.Credits:0} Cr", rx, ry + 44, rw, 13, Dim, 17) + 6f;
		}
		string eta = view.EtaTicks is { } t ? $"fertig in ca. {t / (float)SimClock.TicksPerSecond / 60f:0.#} min ({t} Ticks)" : view.Current is null ? "nichts in Arbeit" : "stockt";
		At(inner, Text(view.Current is { } c ? $"Laufend: {c.Project.Name} — {eta}" : eta, 14, Cyan), rx, ry, rw, 22);
		ry += 26f;
		At(inner, Text($"Erforscht gesamt {view.PointsTotal:0} FP · Personal {view.Spent:0} Cr · von dir {view.PlayerFunded:0} Cr", 13, Dim), rx, ry, rw, 20);
		ry += 34f;
		At(inner, Text("Mitfinanzieren", 20, Cyan), rx, ry, rw, 28);
		ry += 32f;
		var funds = offers.Where(o => o.Key.StartsWith(ResearchService.Fund + ":", StringComparison.Ordinal)).ToList();
		if (funds.Count == 0) At(inner, Text("Kein laufendes Projekt.", 14, new Color(Dim, 0.8f)), rx, ry, rw, 22);
		float bx = rx;
		foreach (var o in funds)
		{
			bool can = o.Amount >= 1f;
			var b = At(inner, new KitButton($"{o.Price:0} Cr", new Vector2(120, 56)) { FontSize = 15, Sub = can ? $"+{o.Stock:0} FP" : $"+{o.Stock:0} FP · fehlt Cr", State = can ? KitButtonState.Ready : KitButtonState.Disabled }, bx, ry);
			string key = o.Key;
			if (can) b.Pressed += () => Execute(key);
			bx += 128f;
			_fundButtons++;
		}
		ry += 66f;
		WrappedText(inner, "Güter (Energie, Platin, Wafer, Titan) nimmt die Station über den normalen Handel an — Lieferungen beschleunigen die Forschung auch.",
			rx, ry, rw, 12, new Color(Dim, 0.8f), 16);
	}

	/// <summary>Führt ein Angebot des Dienstes „research“ aus und zeigt dessen Meldung.</summary>
	public ServiceResult Execute(string offer)
	{
		var svc = Service();
		var r = svc is null
			? new ServiceResult(false, 0f, 0f, "Dienst an dieser Station nicht verfügbar.")
			: svc.Execute(Ctx.U, Ctx.Station, Ctx.Ship, new ServiceRequest(offer, default, 0f));
		_message = r.Message;
		_msgOk = r.Success;
		Executed?.Invoke(r.Success);
		Invalidate();
		return r;
	}

	// ── Zugriff für Selbsttest/Bildschirmfotos ──
	public int TestRowCount => _rows;
	public int TestFundButtons => _fundButtons;
	public string TestMessage => _message;
}
