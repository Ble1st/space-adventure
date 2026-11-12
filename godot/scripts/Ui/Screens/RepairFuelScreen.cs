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
		// Phase 26: Inhalt in einer Rollfläche (Tanken, Schadensliste, Reparatur mit Modulzeilen und Munition passen nicht mehr auf eine Seite)
		float w0 = inner.Size.X > 0 ? inner.Size.X : 670f, h0 = inner.Size.Y > 0 ? inner.Size.Y : 520f;
		var scroll = At(inner, new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, 0, 0, w0, h0);
		var content = new Control { MouseFilter = MouseFilterEnum.Pass };
		scroll.AddChild(content);
		inner = content;
		float w = w0 - 14f;
		_rows = 0;
		bool fuel = Ctx.U.Rules.FuelActive;   // Phase 14: ohne Treibstoff nur die Reparatur
		At(inner, Text(_message.Length > 0 ? _message
				: fuel ? "Tanken zum Marktpreis der Station, Reparatur verbraucht Hüllenplatten aus ihrem Lager." : "Reparatur verbraucht Hüllenplatten aus dem Lager der Station.",
			15, _message.Length > 0 ? (_msgOk ? Green : Red) : new Color(Dim, 0.85f)), 0, 0, w, 40);

		float y = 48f;
		if (fuel) y = Section(inner, "Tanken", RefuelService.ServiceKey, "Diese Station verkauft keinen Treibstoff für dein Schiff.", y, w) + 14f;
		_fuelSection = fuel;
		// Phase 26: Schadensliste (Hülle, Module, Brand) über der Reparatur
		var dmg = SpaceSim.Core.Combat.DamageInfo.For(Ctx.U, Ctx.Ship);
		At(inner, Text("Schäden: " + dmg.Summary, 15, dmg.Rows.Any(r => r.Condition < 0.999f) || dmg.Fires > 0 ? Orange : Dim), 0, y, w, 22);
		y += 24f;
		foreach (var row in dmg.Rows.Where(r => r.Condition < 0.999f || r.Burning))
		{
			At(inner, Text(row.Text, 14, row.Failed || row.Burning ? Red : Orange), 12, y, w - 12, 20);
			y += 21f;
		}
		_damageRows = dmg.Rows.Count(r => r.Condition < 0.999f || r.Burning);
		y = Section(inner, "Reparatur", RepairService.ServiceKey, "Diese Station hat keine Hüllenplatten für Reparaturen.", y + 6f, w) + 14f;
		// Phase 25: Munition nachladen (Dienst „rearm“) — nur mit eingeschalteter Munition
		if (Ctx.U.Rules.AmmoActive) y = Section(inner, $"Munition · Magazin {Ctx.Ship.AmmoLoaded:0}/{Ctx.Ship.Stats.AmmoCapacity:0}", RearmService.ServiceKey,
			"Diese Station führt keine Munition.", y, w);
		content.CustomMinimumSize = new Vector2(w, y + 8f);
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
			// Phase 26: Modulzeilen in Zustandsprozent, „alles“ mit Gesamtpreis
			string detail = o.Key.StartsWith("repair:module:", StringComparison.Ordinal) ? $"Zustand +{o.Amount * 100f:0} % · {o.Amount * o.Price:0} Cr"
				: o.Key == "repair:all" ? $"Gesamt ca. {o.Price:0} Cr (Platten und Elektronik aus dem Lager)"
				: o.Key == "repair:emergency" ? $"{o.Price:0} Cr auf Schuld — nur Funktion (ausgefallene Module, Hülle {Ctx.U.Rules.EmergencyRepairHullShare * 100f:0} %)"
				: $"{o.Amount:0.#} {unit} · {o.Amount * o.Price:0} Cr ({o.Price:0.#} je Einh.)";
			// zweizeilig: Angebot oben, Menge/Preis darunter (lange Modulnamen überlappen sonst die Preiszeile)
			At(inner, Text(o.Text, 16, White), 0, y + 4, w - 150, 24);
			At(inner, Text(o.Note is { } note && !can ? note : detail, 13, Dim), 0, y + 30, w - 150, 22);
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
	/// <summary>Phase 26: gezeichnete Zeilen der Schadensliste (beschädigte oder brennende Module).</summary>
	public int TestDamageRows => _damageRows;
	int _damageRows;
}
