using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Dock-Bereich „Werft“ (UI.md Abschnitt 3, Phase 5c): die kaufbaren Schiffsmodelle der Station – Zeilen ausschließlich aus
/// <see cref="IStationService.Offers"/> des Dienstes „shipdealer“ („buy:&lt;Modell&gt;“), Kauf nur über
/// <see cref="IStationService.Execute"/>. Je Modell Preis, Werte (ShipStats des Werks-Fits) und Material aus
/// <see cref="ShipDealerService.Quote"/>: was im Stationslager liegt und was fehlt. Kleine Schiffe gibt es auch an der
/// Handelsstation. Nach dem Kauf fliegt der Spieler das neue Schiff, das alte bleibt im Hangar dieser Station.
/// </summary>
public sealed partial class ShipyardScreen : DockAreaScreen, ISessionScreen
{
	public override string Id => "dock.shipyard";
	/// <summary>Gerätetest Phase 27: Auswahl und Meldung bei neuer Sitzung zurücksetzen.</summary>
	public void ResetSession() { _message = ""; _msgOk = true; _scrollV = 0; }
	public ShipyardScreen() => AreaId = DockAreas.Shipyard;
	/// <summary>Runde 25 (Gerätetest f01: die Kaufmeldung stand beim nächsten Besuch noch da): beim Öffnen gilt nur die Erklärzeile.</summary>
	protected override void OnOpen() { _message = ""; _msgOk = true; }

	/// <summary>Wird nach jedem Execute ausgelöst (true = Erfolg) – Main spielt den Ton und übernimmt das neue Schiff.</summary>
	public event Action<bool>? Executed;

	string _message = "";
	bool _msgOk = true;
	int _rows, _locked;
	ScrollContainer? _scroll;
	int _scrollV;

	IStationService? Service =>
		Ctx.Services.ForStation(Ctx.U, Ctx.Station).FirstOrDefault(s => s.Key == ShipDealerService.ServiceKey);

	protected override void Rebuild()
	{
		if (_scroll != null && IsInstanceValid(_scroll)) _scrollV = _scroll.ScrollVertical;
		base.Rebuild();
		if (_scroll != null) _scroll.ScrollVertical = _scrollV;
	}

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		float w = inner.Size.X > 0 ? inner.Size.X : 1020f;
		_rows = 0;
		_locked = 0;
		_scroll = null;
		float assembly = u.StationTypes.TryGetValue(Ctx.Station.TypeKey, out var t) && t.ShipDealer is { } sd ? sd.Assembly : 0f;
		At(inner, Text(_message.Length > 0 ? _message
			: $"Preis = Bausatz + Module + {assembly * 100f:0}\u00A0% Montage. Güteklasse wird gewürfelt: {QualityOdds(u)}",
			15, _message.Length > 0 ? (_msgOk ? Green : Red) : new Color(Dim, 0.85f)), 0, 0, w, 36);

		var svc = Service;
		if (svc is null) { At(inner, Text("Diese Station verkauft keine Schiffe.", 16, new Color(Dim, 0.8f)), 0, 48, w, 26); return; }
		_scroll = At(inner, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, 0, 42, w, (inner.Size.Y > 0 ? inner.Size.Y : 520f) - 42 - 4);   // Runde 34: Höhe des Bereichs
		var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		box.AddThemeConstantOverride("separation", 6);
		_scroll.AddChild(box);
		foreach (var o in svc.Offers(u, Ctx.Station, Ctx.Ship))
		{
			if (ShipDealerService.ModelKeyOf(o.Key) is not { } key || ShipDealerService.Quote(u, Ctx.Station, Ctx.Ship, key) is not { } q) continue;
			box.AddChild(ModelRow(o, q, w - 14));
			_rows++;
		}
	}

	Control ModelRow(ServiceOffer o, ShipQuote q, float w)
	{
		var u = Ctx.U;
		bool can = o.Amount >= 1f;
		var row = new Control { CustomMinimumSize = new Vector2(w, 112), MouseFilter = MouseFilterEnum.Pass };
		var bg = new Panel { MouseFilter = MouseFilterEnum.Ignore };
		bg.AddThemeStyleboxOverride("panel", Box(Card, can ? Edge : new Color(Dim, 0.25f), 10, 1, 0));
		row.AddChild(bg);
		bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		At(row, new ShipSketch { MouseFilter = MouseFilterEnum.Ignore }, 8, 22, 64, 64);
		At(row, Text(o.Text, 18, White), 84, 4, w - 420, 26);
		var s = q.Stats;
		// Gerätetest Phase 27: „kein Laser“ meinte den Bergbaulaser — bei Kampfschiffen missverständlich; Waffen werden genannt
		int weapons = q.Model.Fit.Count(k => k is not null && Ctx.U.Modules.TryGetValue(k, out var wm) && wm.IsWeapon);
		string caps = (s.Capabilities.HasFlag(Capability.Mining) ? "Bergbaulaser" : weapons > 0 ? "" : "kein Bergbaulaser")
			+ (weapons > 0 ? $"{(s.Capabilities.HasFlag(Capability.Mining) ? " · " : "")}{weapons} {(weapons == 1 ? "Waffe" : "Waffen")}" : "") + (s.CargoSolid > 0f || s.CargoLiquid > 0f
			? $" · fest {s.CargoSolid + s.CargoAny:0}/flüssig {s.CargoLiquid + s.CargoAny:0}" : "")
			+ (q.Model.Jobs.Count >= 2 ? " · Mehrzweck" : "");   // Phase 6: Multirole-Modell (KI wählt Handel oder Schürfen)
		At(row, Text($"Fracht {s.Cargo:0} m³ · Hülle {s.Hull:0} · Tempo {s.Speed:0.0}{(Ctx.U.Rules.FuelActive ? $" · Tank {s.FuelCapacity:0}" : "")} · {caps}", 14, Dim), 84, 32, w - 84 - 176, 22);   // Runde 34b (Handytest r34 g2: „Mehr…“): bis vor die Kauftaste (darunter), Schrift wird eingepasst
		string material = q.Missing.Count == 0
			? "Material im Lager: " + string.Join(", ", q.Materials.Select(n => $"{n.Units:0}× {u.Goods[n.Good].Name}"))
			: "Fehlt im Lager: " + string.Join(", ", q.Missing.Select(n => $"{n.Units:0}× {u.Goods[n.Good].Name}"));
		// Phase 13: gesperrte Modelle bleiben sichtbar — statt des Materials steht, welche Forschung fehlt
		if (q.Locked is { } locked) material = $"Nicht erforscht: {Ctx.U.Research.LockOf(UnlockKind.Model, q.Model.Key)?.Name ?? locked} — die Fraktion dieser Werft muss es erst erforschen.";
		float matEnd = WrappedText(row, material, 84, 56, w - 84 - 190, 13, q.Locked is not null ? new Color(0.75f, 0.6f, 1f) : q.Missing.Count == 0 ? Green : Orange, 18);   // mehrzeilig, endet vor der Kauftaste
		// Runde 37 (Handytest r35 n44/g54: „1× Kinetisches Geschütz“ lag auf dem Kartenrand): die Karte wächst mit der Materialliste
		if (matEnd + 8f > 112f) row.CustomMinimumSize = new Vector2(w, matEnd + 8f);
		At(row, Text(q.Affordable ? $"{o.Price:0} Cr" : $"zu teuer · {o.Price:0} Cr", 20, q.Affordable ? Orange : Red, HorizontalAlignment.Right), w - 316, 6, 300, 28);
		var b = At(row, new KitButton("Kaufen", new Vector2(150, 56)) { FontSize = 18, State = can ? KitButtonState.Ready : KitButtonState.Disabled }, w - 166, 44);
		if (q.Locked is not null) _locked++;
		string key = o.Key;
		if (can) b.Pressed += () => Execute(key);
		return row;
	}

	/// <summary>Phase 7: Chancen der Güteklassen aus den Regeln, z. B. „Standard 70 % · Verbessert 20 % · Elite 8 % · Meisterwerk 2 %“.</summary>
	static string QualityOdds(Universe u) => string.Join(" · ", u.Rules.QualityChances.Take(4)
		.Select((c, i) => $"{ShipQualityNames.Name((ShipQuality)i)}\u00A0{c * 100f:0}\u00A0%"));   // Runde 37: „%“ nie allein in einer Zeile

	/// <summary>Führt ein Angebot über den Dienst aus, zeigt dessen Meldung und übernimmt bei Erfolg das neue aktive Schiff in den Kontext.</summary>
	public ServiceResult Execute(string offer)
	{
		var svc = Service;
		var r = svc is null
			? new ServiceResult(false, 0f, 0f, "Dienst ist an dieser Station nicht verfügbar.")
			: svc.Execute(Ctx.U, Ctx.Station, Ctx.Ship, new ServiceRequest(offer, default, 0f));
		if (r.Success) Ctx.Ship = Ctx.U.Ships[Ctx.U.PlayerShip];   // das gekaufte Schiff ist jetzt aktiv
		_message = r.Message;
		_msgOk = r.Success;
		Executed?.Invoke(r.Success);
		Invalidate();
		return r;
	}

	// ── Zugriff für Selbsttest/Bildschirmfotos ──
	public int TestRowCount => _rows;
	/// <summary>Bildschirmfotos: zu den gesperrten Modellen rollen (Excavator steht in der Schlüsselreihenfolge weit oben, sonst ans Ende).</summary>
	public void TestScrollToEnd() { if (_scroll != null) { _scrollV = 112 * 2; _scroll.ScrollVertical = _scrollV; } }
	/// <summary>Phase 13: Zeilen mit gesperrtem (nicht erforschtem) Modell.</summary>
	public int TestLockedRows => _locked;
	public string TestMessage => _message;
}
