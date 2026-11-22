using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Dock-Bereich „Hangar“ (UI.md Abschnitt 3; Phase 12 echt): oben das aktive Schiff, links die eigenen Schiffe, die hier angedockt liegen
/// (<see cref="Ship.DockedAt"/>) mit Befehl, Liegegebühr und den Aktionen des Dienstes „hangar“ (Übernehmen, Heim schicken, Verkaufen) plus „Befehle“
/// (öffnet die Flotte in der Kommandozentrale), rechts die fremden Schiffe an dieser Station und der Leihdienst („rental“: mieten, zurückgeben).
/// Zeilen nur aus <see cref="IStationService.Offers"/>, ausgeführt nur über <see cref="IStationService.Execute"/>; Meldung aus dem Ergebnis.
/// </summary>
public sealed partial class HangarScreen : DockAreaScreen, ISessionScreen
{
	public override string Id => "dock.hangar";
	/// <summary>Gerätetest Phase 27: Auswahl und Meldung bei neuer Sitzung zurücksetzen.</summary>
	public void ResetSession() { _message = ""; _msgOk = true; }
	public HangarScreen() => AreaId = DockAreas.Hangar;

	/// <summary>„Befehle“: die Flotte der Kommandozentrale mit diesem Schiff öffnen (Main).</summary>
	public event Action<ShipId>? CommandsRequested;
	/// <summary>Kleines Schiffsmenü (Werte, Module, Inventar, Befehl, Verband) — von dort auch „Befehle geben“.</summary>
	public event Action<ShipId>? MenuRequested;
	/// <summary>Nach jedem Execute (Dienst-Schlüssel, Erfolg) – Main spielt den Ton.</summary>
	public event Action<string, bool>? Executed;

	string _message = "";
	bool _msgOk = true;
	int _parkedRows, _foreignRows;
	/// <summary>Für den Selbsttest: angezeigte eigene geparkte Schiffe und fremde Schiffe.</summary>
	public int TestParkedRows => _parkedRows;
	public int TestForeignRows => _foreignRows;

	IStationService? Svc(string key) => Ctx.Services.ForStation(Ctx.U, Ctx.Station).FirstOrDefault(s => s.Key == key);

	/// <summary>Runde 25, für den Selbsttest: zuletzt angezeigter Grund eines abgeblendeten „Verkaufen“.</summary>
	public string TestSellReason { get; private set; } = "";

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U; var ship = Ctx.Ship; var st = Ctx.Station;
		float w = inner.Size.X > 0 ? inner.Size.X : 1020f;
		_parkedRows = 0; _foreignRows = 0;

		string fee = st.Owner == u.PlayerActor ? "Eigene Station: Liegeplätze gratis"
			: $"Liegegebühr für geparkte Schiffe: {u.Rules.BerthFeeBySize.GetValueOrDefault("S"):0}–{u.Rules.BerthFeeBySize.GetValueOrDefault("L"):0} Cr/Tag je Klasse";
		At(inner, Text(_message.Length > 0 ? _message : fee, 15, _message.Length > 0 ? (_msgOk ? Green : Red) : new Color(Dim, 0.9f)), 0, 0, w, 22);

		// ── Aktives Schiff ──
		var card = CardBg(inner, 0, 28, w - 8, 78, new Color(0.12f, 0.22f, 0.4f, 0.95f), Cyan, 10, 2);
		At(card, new ShipSketch { CustomMinimumSize = new Vector2(48, 40), Size = new Vector2(48, 40), MouseFilter = MouseFilterEnum.Ignore }, 12, 14);
		At(card, Text($"Dein Schiff #{ship.Id.Value} · {ModelName(u, ship)} · {ShipQualityNames.Name(ship.Quality)}{(ship.IsRental ? " · Leihschiff" : "")}", 20, White), 72, 6, 640, 28);
		At(card, Text($"AKTIV – von dir gesteuert · Hülle {(int)ship.Hull}/{(int)ship.MaxHull} · Fracht {ship.UsedVolume(u):0.#}/{ship.CargoCapacity:0} m³{(u.Rules.FuelActive && SpaceSim.Core.Flight.TankInfo.ShortLine(u, ship) is { Length: > 0 } tline ? $" · {tline}" : "")} · Besitzer {u.ActorName(ship.Owner)}", 14, Cyan), 72, 40, w - 100, 22);

		// ── Eigene Schiffe hier (Dienst „hangar“) ──
		// Runde 34b (Prüfung B1): schmale Fläche (Handy bei „groß“) — die rechte Spalte (fremde Schiffe, Leihdienst) rückt unter die eigenen Schiffe
		bool stacked = w < 900f;
		float lw = stacked ? w : 610f;
		var hangar = Svc(HangarService.ServiceKey);
		var offers = hangar?.Offers(u, st, ship) ?? Array.Empty<ServiceOffer>();
		var parked = PlayerFleet.ParkedAt(u, st);
		At(inner, Text(hangar is null ? "Diese Station hat keinen Hangar." : parked.Count > 0 ? "Deine Schiffe hier" : "Keine weiteren eigenen Schiffe an dieser Station", 17,
			parked.Count > 0 ? Cyan : new Color(Dim, 0.8f)), 0, 114, lw, 24);
		float y = 142f;
		foreach (var other in parked.Take(3))
		{
			var row = CardBg(inner, 0, y, lw - 8, 108, Card, new Color(Edge, 0.6f), 10, 1);
			At(row, Text($"#{other.Id.Value} · {ModelName(u, other)} · {ShipQualityNames.Name(other.Quality)}{(other.IsRental ? " · Leihschiff" : "")}", 17, White), 12, 4, 420, 24);
			float berth = Hangar.BerthFeeNow(u, other);
			At(row, Text($"{Commands.Describe(u, other)} · Hülle {(int)other.Hull}/{(int)other.MaxHull}{(u.Rules.FuelActive ? $" · Tank {other.TotalFuel:0}" : "")}" + (berth > 0f ? $" · {berth:0} Cr/Tag" : ""), 13, Dim), 12, 28, lw - 30, 20);
			float bx = 12;
			foreach (var (action, label) in new[] { (HangarService.Switch, "Übernehmen"), (HangarService.Home, "Heim"), (HangarService.SellShip, "Verkaufen") })
			{
				var o = offers.FirstOrDefault(x => x.Key == $"{action}:{other.Id.Value}");
				bool can = o.Key is not null && o.Amount >= 1f;
				string text = action == HangarService.SellShip && o.Key is not null ? $"{label} {o.Price:0}" : label;
				var b = At(row, new KitButton(text, new Vector2(action == HangarService.SellShip ? 150 : 124, 56)) { FontSize = 15, State = can ? (action == HangarService.Switch ? KitButtonState.Ready : KitButtonState.Normal) : KitButtonState.Disabled }, bx, 48);
				string key = $"{action}:{other.Id.Value}";
				if (can) b.Pressed += () => Execute(HangarService.ServiceKey, key);
				// Runde 25: abgeblendetes „Verkaufen“ nennt den Grund (z. B. die Kasse der Werft reicht nicht)
				else if (action == HangarService.SellShip && o.Note is { Length: > 0 } why)
				{
					b.Sub = why.Contains("Kasse") ? why[(why.IndexOf("Kasse"))..].TrimEnd(')', '.') : why.TrimEnd('.');
					TestSellReason = why;
				}
				bx += action == HangarService.SellShip ? 158 : 132;
			}
			var id = other.Id;
			var bc = At(row, new KitButton("Schiffsmenü", new Vector2(124, 56)) { FontSize = 15, Sub = "Befehle, Inventar" }, bx, 48);
			bc.Pressed += () => MenuRequested?.Invoke(id);
			y += 114f;
			_parkedRows++;
		}
		if (parked.Count > 3) At(inner, Text($"+ {parked.Count - 3} weitere (Flotte in der Kommandozentrale)", 13, Dim), 0, y, lw, 20);

		// ── Rechts: fremde Schiffe und Leihdienst ──
		float rx = stacked ? 0f : lw + 8, rw = stacked ? w - 8 : w - rx - 8;
		float ry = stacked ? y + (parked.Count > 3 ? 28f : 8f) : 114f;
		var foreign = Hangar.ForeignDocked(u, st, u.PlayerActor).Where(s => s.Id != ship.Id).ToList();
		At(inner, Text($"Fremde Schiffe hier ({foreign.Count})", 17, Cyan), rx, ry, rw, 24);
		float fy = ry + 28f;
		foreach (var f in foreign.Take(4))
		{
			string job = f.Role switch { ShipRole.Trader => "Händler", ShipRole.Miner => "Schürfer", ShipRole.Builder => "Bauschiff", ShipRole.Police => "Polizei", ShipRole.Pirate => "Pirat", _ => f.Role.ToString() };
			At(inner, Text($"#{f.Id.Value} {ModelName(u, f)} · {job}", 15, White), rx, fy, rw, 20);
			At(inner, Text(SpaceSim.Core.Model.ActorNames.Tagged(u, f.Owner), 12, Dim), rx, fy + 20, rw, 16);
			fy += 40f;
			_foreignRows++;
		}
		if (foreign.Count == 0) { At(inner, Text("Keine fremden Schiffe angedockt.", 14, new Color(Dim, 0.8f)), rx, fy, rw, 20); fy += 28f; }

		var rental = Svc(RentalService.ServiceKey);
		TestRentalNotes.Clear();
		// Runde 35b (Prüfung: gestapelt stand der Leihdienst mit großer Lücke am Fuß, „Mieten“ angeschnitten): gestapelt direkt unter den fremden Schiffen
		fy = stacked ? fy + 8f : Math.Max(fy + 8f, ry + 176f);   // Gerätetest Phase 27: zwei Angebote passen in den Rahmen (vorher ragte „Zurückgeben“ darüber)
		At(inner, Text(rental is null ? "Kein Leihdienst an dieser Station" : "Leihschiff", 17, rental is null ? new Color(Dim, 0.8f) : Cyan), rx, fy, rw, 24);
		fy += 28f;
		if (rental is not null)
			foreach (var o in rental.Offers(u, st, ship).Take(3))
			{
				bool can = o.Amount >= 1f;
				// Runde 37 (Prüfwerkzeug mit Kamera-Aussparung: „Leihschiff Kiebitz mieten (60 Cr + 400 …“): der Angebotstext bricht um, die Taste rückt nach
				float ty = WrappedText(inner, o.Text, rx, fy, rw, 13, can ? White : Dim, 17);
				var b = At(inner, new KitButton(o.Key.StartsWith(RentalService.Rent + ":") ? $"Mieten {o.Price:0}" : "Zurückgeben", new Vector2(rw, 56)) { FontSize = 15, State = can ? KitButtonState.Ready : KitButtonState.Disabled }, rx, ty + 4f);
				string key = o.Key;
				if (can) b.Pressed += () => Execute(RentalService.ServiceKey, key);
				fy = ty + 4f + 62f;
				// Runde 38a (Handytest f32/f33: „Zurückgeben“ ausgegraut, Tipp ohne Meldung): der Grund steht unter der Taste, ein Tipp nennt ihn auch
				if (!can && o.Note is { Length: > 0 } why)
				{
					fy = WrappedText(inner, why, rx, fy - 4f, rw, 13, new Color(Orange, 0.9f), 17) + 6f;
					b.Pressed += () => { _message = why; _msgOk = false; Invalidate(); };
					TestRentalNotes.Add(why);
				}
			}
	}

	/// <summary>Runde 38a: zuletzt gezeigte Gründe unter ausgegrauten Leih-Tasten (Selbsttest).</summary>
	public readonly System.Collections.Generic.List<string> TestRentalNotes = new();

	/// <summary>Führt ein Angebot eines Dienstes (hangar oder rental) aus und zeigt dessen Meldung.</summary>
	public ServiceResult Execute(string service, string offer)
	{
		var svc = Svc(service);
		var r = svc is null
			? new ServiceResult(false, 0f, 0f, "Dienst an dieser Station nicht verfügbar.")
			: svc.Execute(Ctx.U, Ctx.Station, Ctx.Ship, new ServiceRequest(offer, default, 0f));
		_message = r.Message;
		_msgOk = r.Success;
		Executed?.Invoke(service, r.Success);
		Invalidate();
		return r;
	}

	static string ModelName(Universe u, Ship s) => u.ShipModels.TryGetValue(s.ModelKey, out var m) ? m.Name : "ohne Modell";
}
