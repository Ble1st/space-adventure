using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Dock-Bereich „Taxi“ (UI.md Abschnitt 3 „Taxi“, Phase 12): Das Taxi bringt dich allein zu einer anderen Station, dein Schiff bleibt hier angedockt.
/// Je Ziel Preis (inkl. Zoll), Dauer, Lage und ob dort ein eigenes Schiff wartet oder ein Leihschiff nötig ist; gesperrte Routen zeigen den Grund.
/// Zeilen aus <see cref="IStationService.Offers"/> des Dienstes „taxi“ (Details aus <see cref="TaxiService.Quote"/>), gebucht nur über
/// <see cref="IStationService.Execute"/>. Die Reisezeit (Ergebnis: Ticks) lässt Main im Zeitraffer vergehen.
/// </summary>
public sealed partial class TaxiScreen : DockAreaScreen, ISessionScreen
{
	public override string Id => "dock.taxi";
	/// <summary>Gerätetest Phase 27: Auswahl und Meldung bei neuer Sitzung zurücksetzen.</summary>
	public void ResetSession() { _message = ""; _msgOk = true; }
	public TaxiScreen() => AreaId = DockAreas.Taxi;

	/// <summary>Gebucht: Erfolg und Reisezeit in Ticks (Main lässt die Zeit vergehen und übernimmt das Schiff am Ziel).</summary>
	public event Action<bool, long>? Booked;

	string _message = "";
	bool _msgOk = true;
	int _rows, _hidden;
	/// <summary>Für den Selbsttest: angezeigte Ziele.</summary>
	public int TestRows => _rows;
	/// <summary>Für den Selbsttest: ausgeblendete Ziele ohne eigenes Schiff und ohne Leihdienst (Phase 21).</summary>
	public int TestHidden => _hidden;
	/// <summary>Für den Selbsttest: Liste neu aufbauen.</summary>
	public void TestRebuild() => Invalidate();

	IStationService? Service => Ctx.Services.ForStation(Ctx.U, Ctx.Station).FirstOrDefault(s => s.Key == TaxiService.ServiceKey);

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		float w = inner.Size.X > 0 ? inner.Size.X : 1020f;
		_rows = 0; _hidden = 0;
		At(inner, Text(_message.Length > 0 ? _message
				: $"Du reist allein, dein Schiff bleibt hier ({(Ctx.Station.Owner == u.PlayerActor ? "eigene Station, gratis" : "Liegegebühr je Klasse")}). Fracht und Treibstoff bleiben an Bord, das Geld bei dir.",
			15, _message.Length > 0 ? (_msgOk ? Green : Red) : new Color(Dim, 0.9f)), 0, 0, w, 22);
		var svc = Service;
		if (svc is null) { At(inner, Text("Kein Taxi an dieser Station.", 18, Dim), 0, 40, w, 26); return; }
		var offers = svc.Offers(u, Ctx.Station, Ctx.Ship);
		// Phase 21: Ziele in einer Bildlaufliste (vorher die ersten sechs); gezeigt werden nur Ziele, an denen ein eigenes Schiff wartet oder ein Leihschiff
		// zu haben ist — bei 11 Sektoren gibt es Dutzende Hangar-Stationen ohne beides, die nie buchbar sind (sie stehen als Summe unter der Liste);
		// buchbare zuerst, dann nach Reisezeit (Reihenfolge aus TaxiService.Quotes)
		var scroll = At(inner, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 0, 32, w, (inner.Size.Y > 0 ? inner.Size.Y : 520f) - 32 - 4);   // Runde 34: Höhe des Bereichs
		var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
		list.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(list);
		float rw = w - 22f;
		float y = 32f;
		foreach (var o in offers)
		{
			int id = int.Parse(o.Key[(o.Key.IndexOf(':') + 1)..]);
			var q = TaxiService.Quote(u, Ctx.Station, Ctx.Ship, u.Stations[new StationId(id)]);
			if (q is null) continue;
			if (q.WaitingShip is null && !q.RentalThere) { _hidden++; continue; }
			bool can = o.Amount >= 1f;
			var row = new Control { CustomMinimumSize = new Vector2(rw, 76), MouseFilter = MouseFilterEnum.Pass };
			list.AddChild(row);
			CardBg(row, 0, 0, rw, 76, Card, can ? new Color(Cyan, 0.5f) : new Color(Edge, 0.5f), 10, 1);
			string sector = u.Sectors.TryGetValue(q.To.Sector, out var sec) ? sec.Name : "?";
			At(row, Text($"{q.To.Name} · {sector}", 18, can ? White : Dim), 12, 4, rw - 290, 24);
			string there = q.WaitingShip is { } ws ? $"Dort wartet: #{ws.Id.Value} {(u.ShipModels.TryGetValue(ws.ModelKey, out var m) ? m.Name : "")}"
				: q.RentalThere ? $"Kein eigenes Schiff · Leihschiff ab {q.RentalUpfront:0} Cr (inkl. Pfand)" : "Kein eigenes Schiff, kein Leihdienst";
			string detail = q.Issue is null || q.Issue.StartsWith("Nicht genügend", StringComparison.Ordinal)
				? $"{q.Jumps} Sprung{(q.Jumps == 1 ? "" : "e")} · {q.Ticks / SpaceSim.Core.Simulation.SimClock.TicksPerSecond:0} s · Zoll {q.Toll:0} Cr · {q.Situation}"
				: q.Issue;
			At(row, Text(there, 14, q.WaitingShip is not null ? Cyan : Orange), 12, 28, rw - 290, 20);
			At(row, Text(detail, 13, q.Issue is null ? Dim : Red), 12, 50, rw - 290, 20);
			var b = At(row, new KitButton(can ? $"Buchen {q.Total:0} Cr" : "Nicht buchbar", new Vector2(250, 60)) { FontSize = 17, State = can ? KitButtonState.Ready : KitButtonState.Disabled }, rw - 262, 8);
			string key = o.Key;
			if (can) b.Pressed += () => Book(key);
			_rows++;
		}
		if (_hidden > 0)
		{
			var note = Text($"{_hidden} weitere Stationen mit Hangar: dort wartet kein eigenes Schiff und es gibt kein Leihschiff.", 13, new Color(Dim, 0.8f));
			note.CustomMinimumSize = new Vector2(rw, 26);
			list.AddChild(note);
		}
		if (_rows == 0) At(inner, Text("Keine Taxiziele: Stationen mit Hangar, an denen ein eigenes Schiff wartet oder ein Leihschiff zu haben ist.", 16, Dim), 0, y, w, 24);
	}

	/// <summary>Bucht eine Fahrt über den Dienst; bei Erfolg meldet <see cref="Booked"/> die Reisezeit an Main.</summary>
	public ServiceResult Book(string offer)
	{
		var svc = Service;
		var r = svc is null ? new ServiceResult(false, 0f, 0f, "Kein Taxi an dieser Station.")
			: svc.Execute(Ctx.U, Ctx.Station, Ctx.Ship, new ServiceRequest(offer, default, 0f));
		_message = r.Message;
		_msgOk = r.Success;
		Booked?.Invoke(r.Success, r.Success ? (long)r.Units : 0);
		if (Visible && !r.Success) Invalidate();
		return r;
	}
}
