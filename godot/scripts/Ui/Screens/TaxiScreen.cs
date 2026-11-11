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
public sealed partial class TaxiScreen : DockAreaScreen
{
	public override string Id => "dock.taxi";
	public TaxiScreen() => AreaId = DockAreas.Taxi;

	/// <summary>Gebucht: Erfolg und Reisezeit in Ticks (Main lässt die Zeit vergehen und übernimmt das Schiff am Ziel).</summary>
	public event Action<bool, long>? Booked;

	string _message = "";
	bool _msgOk = true;
	int _rows;
	/// <summary>Für den Selbsttest: angezeigte Ziele.</summary>
	public int TestRows => _rows;

	IStationService? Service => Ctx.Services.ForStation(Ctx.U, Ctx.Station).FirstOrDefault(s => s.Key == TaxiService.ServiceKey);

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		float w = inner.Size.X > 0 ? inner.Size.X : 1020f;
		_rows = 0;
		At(inner, Text(_message.Length > 0 ? _message
				: $"Du reist allein, dein Schiff bleibt hier ({(Ctx.Station.Owner == u.PlayerActor ? "eigene Station, gratis" : "Liegegebühr je Klasse")}). Fracht und Treibstoff bleiben an Bord, das Geld bei dir.",
			15, _message.Length > 0 ? (_msgOk ? Green : Red) : new Color(Dim, 0.9f)), 0, 0, w, 22);
		var svc = Service;
		if (svc is null) { At(inner, Text("Kein Taxi an dieser Station.", 18, Dim), 0, 40, w, 26); return; }
		var offers = svc.Offers(u, Ctx.Station, Ctx.Ship);
		float y = 32f;
		foreach (var o in offers.Take(6))
		{
			int id = int.Parse(o.Key[(o.Key.IndexOf(':') + 1)..]);
			var q = TaxiService.Quote(u, Ctx.Station, Ctx.Ship, u.Stations[new StationId(id)]);
			if (q is null) continue;
			bool can = o.Amount >= 1f;
			var row = CardBg(inner, 0, y, w - 8, 76, Card, can ? new Color(Cyan, 0.5f) : new Color(Edge, 0.5f), 10, 1);
			string sector = u.Sectors.TryGetValue(q.To.Sector, out var sec) ? sec.Name : "?";
			At(row, Text($"{q.To.Name} · {sector}", 18, can ? White : Dim), 12, 4, 520, 24);
			string there = q.WaitingShip is { } ws ? $"Dort wartet: #{ws.Id.Value} {(u.ShipModels.TryGetValue(ws.ModelKey, out var m) ? m.Name : "")}"
				: q.RentalThere ? $"Kein eigenes Schiff · Leihschiff ab {q.RentalUpfront:0} Cr (inkl. Pfand)" : "Kein eigenes Schiff, kein Leihdienst";
			string detail = q.Issue is null || q.Issue.StartsWith("Nicht genügend", StringComparison.Ordinal)
				? $"{q.Jumps} Sprung{(q.Jumps == 1 ? "" : "e")} · {q.Ticks / SpaceSim.Core.Simulation.SimClock.TicksPerSecond:0} s · Zoll {q.Toll:0} Cr · {q.Situation}"
				: q.Issue;
			At(row, Text(there, 14, q.WaitingShip is not null ? Cyan : Orange), 12, 28, 620, 20);
			At(row, Text(detail, 13, q.Issue is null ? Dim : Red), 12, 50, 640, 20);
			var b = At(row, new KitButton(can ? $"Buchen {q.Total:0} Cr" : "Nicht buchbar", new Vector2(250, 60)) { FontSize = 17, State = can ? KitButtonState.Ready : KitButtonState.Disabled }, w - 8 - 262, 8);
			string key = o.Key;
			if (can) b.Pressed += () => Book(key);
			y += 82f;
			_rows++;
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
