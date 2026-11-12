using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Dock-Bereich „Reparatur &amp; Tanken“ (UI.md Abschnitt 3, Phase 4c): zeigt die Angebote der Dienste „refuel“ und
/// „repair“ ausschließlich aus <see cref="IStationService.Offers"/> und führt sie nur über
/// <see cref="IStationService.Execute"/> aus. Die Ergebnismeldung (<see cref="ServiceResult.Message"/>) erscheint als
/// Hinweis; rechts das Schiff mit Hülle, Tank und Credits (echt). Bietet die Station nur einen der beiden Dienste,
/// steht beim anderen ein kurzer Hinweis.
/// </summary>
public sealed partial class RepairFuelScreen : DockAreaScreen
{
	public override string Id => "dock.repair";
	public RepairFuelScreen() => AreaId = DockAreas.Repair;
	protected override bool ShowShip => true;

	/// <summary>Wird nach jedem Execute ausgelöst (true = Erfolg) – Main spielt den Ton.</summary>
	public event Action<bool>? Executed;

	string _message = "";
	bool _msgOk = true;
	int _rows;

	IStationService? Service(string key) =>
		Ctx.Services.ForStation(Ctx.U, Ctx.Station).FirstOrDefault(s => s.Key == key);

	protected override void BuildContent(Control inner)
	{
		float w = inner.Size.X > 0 ? inner.Size.X : 670f;
		_rows = 0;
		bool fuel = Ctx.U.Rules.FuelActive;   // Phase 14: ohne Treibstoff nur die Reparatur
		At(inner, Text(_message.Length > 0 ? _message
				: fuel ? "Tanken zum Marktpreis der Station, Reparatur verbraucht Hüllenplatten aus ihrem Lager." : "Reparatur verbraucht Hüllenplatten aus dem Lager der Station.",
			15, _message.Length > 0 ? (_msgOk ? Green : Red) : new Color(Dim, 0.85f)), 0, 0, w, 40);

		float y = 48f;
		if (fuel) y = Section(inner, "Tanken", RefuelService.ServiceKey, "Diese Station verkauft keinen Treibstoff für dein Schiff.", y, w) + 14f;
		_fuelSection = fuel;
		y = Section(inner, "Reparatur", RepairService.ServiceKey, "Diese Station hat keine Hüllenplatten für Reparaturen.", y, w) + 14f;
		// Phase 25: Munition nachladen (Dienst „rearm“) — nur mit eingeschalteter Munition
		if (Ctx.U.Rules.AmmoActive) Section(inner, $"Munition · Magazin {Ctx.Ship.AmmoLoaded:0}/{Ctx.Ship.Stats.AmmoCapacity:0}", RearmService.ServiceKey,
			"Diese Station führt keine Munition.", y, w);
	}

	/// <summary>Eine Dienstgruppe: Überschrift plus je Angebot eine Zeile mit Menge, Preis und Ausführen-Taste.</summary>
	float Section(Control inner, string title, string serviceKey, string missing, float y, float w)
	{
		At(inner, Text(title, 20, Cyan), 0, y, w, 28);
		y += 32f;
		var svc = Service(serviceKey);
		IReadOnlyList<ServiceOffer> offers = svc is null ? Array.Empty<ServiceOffer>() : svc.Offers(Ctx.U, Ctx.Station, Ctx.Ship);
		if (svc is null || offers.Count == 0)
		{
			string text = svc is null ? missing
				: serviceKey == RepairService.ServiceKey ? "Hülle ist unbeschädigt." : missing;
			At(inner, Text(text, 15, new Color(Dim, 0.8f)), 0, y, w, 24);
			return y + 28f;
		}
		foreach (var o in offers)
		{
			bool can = o.Amount >= (serviceKey == RefuelService.ServiceKey ? 1f : 0.01f);
			string unit = serviceKey == RepairService.ServiceKey ? "Hüllenpunkte" : "Einheiten";
			if (serviceKey == RearmService.ServiceKey) can = o.Amount >= 0.01f;
			At(inner, Text(o.Text, 17, White), 0, y + 10, 250, 26);
			At(inner, Text(o.Note is { } note && !can ? note : $"{o.Amount:0.#} {unit} · {o.Amount * o.Price:0} Cr ({o.Price:0.#} je Einh.)", 14, Dim), 250, y + 12, w - 400, 24);
			var b = At(inner, new KitButton("Ausführen", new Vector2(130, 56)) { FontSize = 16, State = can ? KitButtonState.Ready : KitButtonState.Disabled }, w - 140, y);
			string key = o.Key;
			if (can) b.Pressed += () => Execute(serviceKey, key);
			y += 60f;
			_rows++;
		}
		return y;
	}

	/// <summary>Führt ein Angebot über den Dienst aus und zeigt dessen Meldung.</summary>
	public ServiceResult Execute(string serviceKey, string offer)
	{
		var svc = Service(serviceKey);
		var r = svc is null
			? new ServiceResult(false, 0f, 0f, "Dienst ist an dieser Station nicht verfügbar.")
			: svc.Execute(Ctx.U, Ctx.Station, Ctx.Ship, new ServiceRequest(offer, default, 0f));
		_message = r.Message;
		_msgOk = r.Success;
		Executed?.Invoke(r.Success);
		Invalidate();
		return r;
	}

	// ── Zugriff für Selbsttest/Bildschirmfotos ──
	public int TestRowCount => _rows;
	/// <summary>Selbsttest: wurde der Abschnitt „Tanken“ gezeichnet (Phase 14: nur mit Treibstoff)?</summary>
	public bool TestFuelSection => _fuelSection;
	bool _fuelSection;
	public string TestMessage => _message;
}
