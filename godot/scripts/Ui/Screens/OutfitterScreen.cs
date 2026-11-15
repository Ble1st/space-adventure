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
/// Dock-Bereich „Ausrüster“ (UI.md Abschnitt 3, Phase 5c): links die Slots des Schiffs mit eingebautem Modul und die
/// Leistungsbilanz, rechts für den gewählten Slot die Angebote des Dienstes „outfitter“ – ausschließlich aus
/// <see cref="IStationService.Offers"/> („fit:&lt;Slot&gt;:&lt;Modul&gt;“, „remove:&lt;Slot&gt;“), ausgeführt nur über
/// <see cref="IStationService.Execute"/>. Je Angebot Preis, Herkunft (Lager/Fracht) und die Wirkung auf die Schiffswerte
/// (Vorher/Nachher aus <see cref="OutfitterService.Preview"/>, reine Core-Rechnung über ShipStats) samt Grund, warum ein
/// Einbau gerade nicht geht. Die Ergebnismeldung kommt aus <see cref="ServiceResult.Message"/>. Phase 7: Abschnitt „Verbessern“ aus dem
/// Dienst „upgrade“ (Modul des Slots, Slot vergrößern, Bonusslot dieser Slotart) mit Chance, Material, Preis und Mitleidszähler.
/// </summary>
public sealed partial class OutfitterScreen : DockAreaScreen, ISessionScreen
{
	public override string Id => "dock.outfitter";
	/// <summary>Gerätetest Phase 27: Auswahl und Meldung bei neuer Sitzung zurücksetzen.</summary>
	public void ResetSession() { _message = ""; _msgOk = true; _slot = -1; _scrollV = 0; }
	public OutfitterScreen() => AreaId = DockAreas.Outfitter;

	/// <summary>Wird nach jedem Execute ausgelöst (true = Erfolg) – Main spielt den Ton.</summary>
	public event Action<bool>? Executed;

	string _message = "";
	bool _msgOk = true;
	int _slot = -1;
	int _rows;
	ScrollContainer? _scroll;
	int _scrollV;

	IStationService? Service =>
		Ctx.Services.ForStation(Ctx.U, Ctx.Station).FirstOrDefault(s => s.Key == OutfitterService.ServiceKey);

	/// <summary>Phase 7: Dienst „upgrade“ dieser Station (Abschnitt „Verbessern“ im Ausrüster), null = keiner.</summary>
	IStationService? UpgradeSvc =>
		Ctx.Services.ForStation(Ctx.U, Ctx.Station).FirstOrDefault(s => s.Key == UpgradeService.ServiceKey);

	protected override void Rebuild()
	{
		if (_scroll != null && IsInstanceValid(_scroll)) _scrollV = _scroll.ScrollVertical;
		base.Rebuild();
		if (_scroll != null) _scroll.ScrollVertical = _scrollV;
	}

	int _damaged;
	/// <summary>Phase 26 Test-Hook: Zahl der als beschädigt markierten Slots.</summary>
	public int TestDamagedSlots => _damaged;

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U; var ship = Ctx.Ship;
		float w = inner.Size.X > 0 ? inner.Size.X : 1020f;
		_rows = 0;
		_damaged = 0;
		_scroll = null;
		At(inner, Text(_message.Length > 0 ? _message : "Slot wählen, Modul aus Lager oder Fracht einbauen. Ausgebautes kauft die Station an (sonst in die Fracht).",
			15, _message.Length > 0 ? (_msgOk ? Green : Red) : new Color(Dim, 0.85f)), 0, 0, w, 36);

		var svc = Service;
		var upSvc = UpgradeSvc;
		var cls = ship.HasModel && u.ShipModels.TryGetValue(ship.ModelKey, out var model) ? u.ShipClasses.GetValueOrDefault(model.ClassKey) : null;
		if ((svc is null && upSvc is null) || cls is null)
		{
			At(inner, Text(cls is not null ? "Diese Station hat keinen Ausrüster." : "Dieses Schiff hat kein Modell und kann nicht umgerüstet werden.", 16, new Color(Dim, 0.8f)), 0, 48, w, 26);
			return;
		}
		var offers = svc is null ? (IReadOnlyList<ServiceOffer>)Array.Empty<ServiceOffer>() : svc.Offers(u, Ctx.Station, ship);
		var upOffers = upSvc is null ? (IReadOnlyList<ServiceOffer>)Array.Empty<ServiceOffer>() : upSvc.Offers(u, Ctx.Station, ship);
		if (_slot < 0 || _slot >= ship.Slots.Count) _slot = FirstSlotWithOffers(offers, ship);

		// ── links: Slots ──
		At(inner, Text($"Slots · {(u.ShipModels.TryGetValue(ship.ModelKey, out var m) ? m.Name : ship.ModelKey)} · {ShipQualityNames.Name(ship.Quality)}", 17, Cyan), 0, 40, 330, 24);
		// Gerätetest Phase 27: Slotliste rollt (KitScroll), die Leistungsbilanz steht fest unten im Rahmen — nichts ragt mehr unter den Rahmen
		float ih = inner.Size.Y > 0 ? inner.Size.Y : 520f;   // Runde 34: Höhe des Bereichs (Dock-Schicht)
		float py = ih - 4f - 88f;
		var slotScroll = At(inner, new KitScroll(), 0, 68, 336, py - 72);
		var slotBox = new VBoxContainer();
		slotBox.AddThemeConstantOverride("separation", 4);
		slotScroll.AddChild(slotBox);
		for (int i = 0; i < ship.Slots.Count; i++)
		{
			var slot = ship.Slots[i];
			string type = u.SlotTypes.TryGetValue(slot.Type, out var st) ? st.Name : slot.Type;
			string mod = ship.Fit[i] is { } key ? (u.Modules.TryGetValue(key, out var mm) && mm.Name.Length > 0 ? mm.Name : key) : "– leer –";
			bool required = st?.Required ?? false;
			bool bonus = i >= cls.Slots.Count;   // Phase 7: Bonusslot aus Güteklasse oder Upgrade
			// Phase 26: Schadensliste je Slot (Zustand, ausgefallen, Brand)
			if (ship.Fit[i] is not null && (ship.ConditionOf(i) < 0.999f || ship.Systems.IsBurning(i)))
			{
				mod += $" · {ship.ConditionOf(i) * 100f:0} %" + (SpaceSim.Core.Combat.ModuleDamage.Failed(u, ship, i) ? " ausgefallen" : "") + (ship.Systems.IsBurning(i) ? " · BRENNT" : "");
				_damaged++;
			}
			var b = new KitButton($"{i + 1} · {type} {slot.Size}{(required ? " (Pflicht)" : "")}{(bonus ? " · Bonus" : "")}", new Vector2(324, 56))
			{ AlignLeft = true, FontSize = 15, Sub = mod, State = i == _slot ? KitButtonState.On : KitButtonState.Normal };
			slotBox.AddChild(b);
			int idx = i;
			b.Pressed += () => { _slot = idx; _message = ""; Invalidate(); };
		}

		// ── links unten: Leistungsbilanz (echt aus ShipStats) ──
		var s = ship.Stats;
		At(inner, Text($"Leistung {s.PowerDraw:0.#} / {s.PowerOutput:0.#}", 14, Dim), 0, py, 140, 20);
		var bar = At(inner, new KitBar(170, 10, Green), 150, py + 5, 170, 10);
		bar.Set(s.PowerDraw, s.PowerDraw > s.PowerOutput ? Red : Green, Math.Max(1f, s.PowerOutput));
		// Phase 23: Bank und Schild als Laufzeitwerte (aktuell/Kapazität), Magazin
		// Prüfung Phase 27: Zeilen passen in die Spalte (Magazin/Raketen in die dritte Zeile, vorher „Magazin 3…“ abgeschnitten)
		At(inner, Text($"Spitze {s.PeakDraw:0.#} · Bank {ship.Charge:0}/{s.PowerStorage:0} · Schild {ship.ShieldNow:0}/{s.Shield:0}", 14, Dim), 0, py + 24, 340, 20);
		string mags = (s.AmmoCapacity > 0f ? $" · Mag. {SpaceSim.Core.Combat.ShipSystems.Ammo(Ctx.U, ship):0}/{s.AmmoCapacity:0}" : "")
			+ (s.MissileCapacity > 0f ? $" · Rak. {SpaceSim.Core.Combat.Missiles.Loaded(Ctx.U, ship):0}/{s.MissileCapacity:0}" : "");
		At(inner, Text($"Fracht {s.Cargo:0} · Hülle {s.Hull:0} · Tempo {s.Speed:0.0}{(Ctx.U.Rules.FuelActive ? $" · Tank {s.FuelCapacity:0}" : "")}{mags}", 13, new Color(Dim, 0.9f)), 0, py + 46, 340, 20);
		// Phase 26: Zusammenfassung der Schäden (beschädigte Module erst reparieren, dann aus-/umbauen)
		if (_damaged > 0 || ship.Systems.Burning.Count > 0)
			At(inner, Text(SpaceSim.Core.Combat.DamageInfo.For(u, ship).Summary, 12, Orange), 0, py + 66, 340, 20);

		// ── rechts: Angebote für den gewählten Slot ──
		float rx = 345f, rw = w - rx;
		var cur = ship.Slots[_slot];
		string curType = u.SlotTypes.TryGetValue(cur.Type, out var ct) ? ct.Name : cur.Type;
		At(inner, Text($"Slot {_slot + 1}: {curType} ({cur.Size})", 17, Cyan), rx, 40, rw, 24);
		var slotOffers = offers.Where(o => OfferSlot(o.Key) == _slot).ToList();
		_scroll = At(inner, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, rx, 68, rw, ih - 68 - 4);
		var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		box.AddThemeConstantOverride("separation", 6);
		_scroll.AddChild(box);
		// Phase 7: Abschnitt „Verbessern“ (Dienst „upgrade“): Modul dieses Slots, Slot vergrößern, Bonusslot dieser Slotart
		_upRows = 0;
		var slotUps = upOffers.Where(o => OfferSlot(o.Key) == _slot || o.Key == $"bonus:{cur.Type}").ToList();
		if (slotUps.Count > 0)
		{
			box.AddChild(Text("Verbessern · Chance steigt mit jedem Fehlschlag (Mitleidszähler)", 15, Cyan));
			foreach (var o in slotUps) { box.AddChild(UpgradeRow(o, rw - 14)); _upRows++; }
			if (svc is not null) box.AddChild(Text("Umrüsten", 15, Cyan));
		}
		if (svc is null) return;
		if (slotOffers.Count == 0)
			box.AddChild(Text(ship.Fit[_slot] is null ? "Kein passendes Modul auf Lager." : "Kein passendes Modul auf Lager; Pflichtslots lassen sich nur tauschen.", 15, new Color(Dim, 0.8f)));
		foreach (var o in slotOffers)
		{
			box.AddChild(OfferRow(o, rw - 14));
			_rows++;
		}
	}

	/// <summary>Phase 7: eine Upgrade-Zeile — Änderung und Chance aus dem Angebot, Material/Preis/Mitleid aus <see cref="ShipUpgrade.Quote"/>, Taste „Versuchen“.</summary>
	Control UpgradeRow(ServiceOffer o, float w)
	{
		var u = Ctx.U;
		var q = ShipUpgrade.Quote(u, Ctx.Station, Ctx.Ship, o.Key);
		bool can = o.Amount >= 1f;
		var row = new Control { CustomMinimumSize = new Vector2(w, 82), MouseFilter = MouseFilterEnum.Pass };
		var bg = new Panel { MouseFilter = MouseFilterEnum.Ignore };
		bg.AddThemeStyleboxOverride("panel", Box(Card, can ? Orange : new Color(Dim, 0.25f), 10, 1, 0));
		row.AddChild(bg);
		bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		At(row, Text(o.Text, 15, White), 14, 4, w - 180, 24);
		if (q is not null)
		{
			string mat = string.Join(", ", q.Materials.Select(n => $"{n.Units:0}× {u.Goods[n.Good].Name}"));
			At(row, Text($"Material {mat} · {o.Price:0} Cr · Mitleidszähler {q.Pity}", 13, Dim), 14, 28, w - 190, 20);
			string state = q.Issue ?? (q.Missing.Count > 0 ? "Fehlt im Lager: " + string.Join(", ", q.Missing.Select(n => $"{n.Units:0}× {u.Goods[n.Good].Name}"))
				: !q.Affordable ? "Nicht genügend Credits" : "Material wird auch bei Misserfolg verbraucht");
			At(row, Text(state, 13, can ? Green : Orange), 14, 52, w - 190, 22);
		}
		var b = At(row, new KitButton("Versuchen", new Vector2(150, 56)) { FontSize = 16, State = can ? KitButtonState.Ready : KitButtonState.Disabled }, w - 160, 13);
		string key = o.Key;
		if (can) b.Pressed += () => ExecuteUpgrade(key);
		return row;
	}

	/// <summary>Phase 7: führt ein Upgrade-Angebot nur über den Dienst aus; grün bei gelungenem, orange bei misslungenem Versuch.</summary>
	public ServiceResult ExecuteUpgrade(string offer)
	{
		var svc = UpgradeSvc;
		var r = svc is null
			? new ServiceResult(false, 0f, 0f, "Upgrades gibt es an dieser Station nicht.")
			: svc.Execute(Ctx.U, Ctx.Station, Ctx.Ship, new ServiceRequest(offer, default, 0f));
		_message = r.Message;
		_msgOk = r.Success && r.Units >= 1f;
		Executed?.Invoke(_msgOk);
		Invalidate();
		return r;
	}

	/// <summary>Eine Angebotszeile: Name, Herkunft/Preis, Wirkung (Vorher → Nachher) bzw. Grund, Taste.</summary>
	Control OfferRow(ServiceOffer o, float w)
	{
		var u = Ctx.U; var ship = Ctx.Ship;
		bool remove = o.Key.StartsWith(OutfitterService.Remove + ":", StringComparison.Ordinal);
		string? moduleKey = remove ? null : o.Key[(o.Key.LastIndexOf(':') + 1)..];
		var p = OutfitterService.Preview(u, Ctx.Station, ship, _slot, moduleKey);
		bool can = o.Amount >= 1f;

		var row = new Control { CustomMinimumSize = new Vector2(w, 82), MouseFilter = MouseFilterEnum.Pass };
		var bg = new Panel { MouseFilter = MouseFilterEnum.Ignore };
		bg.AddThemeStyleboxOverride("panel", Box(Card, can ? Edge : new Color(Dim, 0.25f), 10, 1, 0));
		row.AddChild(bg);
		bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		var chip = new ColorRect { Color = DockAreaScreen.GoodColorOf(u, o.Good), MouseFilter = MouseFilterEnum.Ignore };
		At(row, chip, 10, 12, 12, 12);
		// Phase 13: der Sperrgrund steht in der Wirkungszeile — der Titel bleibt kurz (sonst läuft er unter die Taste)
		string title = o.Text.IndexOf(" – nicht erforscht", StringComparison.Ordinal) is var cut and > 0 ? o.Text[..cut] : o.Text;
		At(row, Text(title, 16, White), 30, 4, w - 200, 24);
		string source = remove
			? (o.Price > 0f ? $"Station kauft an: {o.Price:0} Cr" : "Station nimmt nicht an → in die Fracht")
			: (o.Price <= 0f ? $"aus der Fracht ({o.Held:0} an Bord) · kostenlos" : $"Lager {o.Stock:0.#} · {o.Price:0} Cr");
		At(row, Text(source, 13, Dim), 30, 28, w - 200, 20);
		string effect = p is null ? "" : !p.Valid
			? (p.Issue is { } issue ? FitRules.Describe(issue) : p.CargoProblem ?? "")
			: Diff(p.Before, p.After);
		// Phase 13: gesperrtes Modul (für die Fraktion der Station nicht erforscht) — sichtbar, aber nicht einbaubar
		string? locked = moduleKey is null || o.Held >= 1f ? null : SpaceSim.Core.Simulation.Research.LockText(u, Ctx.Station, UnlockKind.Module, moduleKey);
		if (locked is not null) { effect = $"Nicht erforscht ({u.Research.LockOf(UnlockKind.Module, moduleKey!)?.Name}) — " + effect; can = false; }
		// Gerätetest Phase 27: der Grund des Angebots (beschädigtes Modul: „erst reparieren“) geht vor „Nicht genügend Credits“
		else if (!can && o.Note is { Length: > 0 } note) effect = note;
		else if (p is { Valid: true } && !can) effect = "Nicht genügend Credits · " + effect;
		At(row, Text(effect, 13, p is { Valid: true } && can ? Green : Orange), 30, 52, w - 200, 22);
		var b = At(row, new KitButton(remove ? "Ausbauen" : ship.Fit[_slot] is null ? "Einbauen" : "Tauschen", new Vector2(150, 56))
		{ FontSize = 16, State = can ? KitButtonState.Ready : KitButtonState.Disabled }, w - 160, 13);
		string key = o.Key;
		if (can) b.Pressed += () => Execute(key);
		return row;
	}

	/// <summary>Wirkung als Text: nur geänderte Werte, „Name alt→neu“.</summary>
	static string Diff(ShipStats a, ShipStats b)
	{
		var parts = new List<string>();
		void Add(string name, float x, float y, string fmt = "0")
		{
			if (MathF.Abs(x - y) > 0.01f) parts.Add($"{name} {x.ToString(fmt)}→{y.ToString(fmt)}");
		}
		Add("Fracht", a.Cargo, b.Cargo);
		Add("Hülle", a.Hull, b.Hull);
		Add("Tempo", a.Speed, b.Speed, "0.0");
		Add("Leistung frei", a.PowerOutput - a.PowerDraw, b.PowerOutput - b.PowerDraw, "0.#");
		Add("Speicher", a.PowerStorage, b.PowerStorage);
		Add("Schild", a.Shield, b.Shield);              // Phase 23
		Add("Schildladung/s", a.ShieldRegen, b.ShieldRegen, "0.##");
		Add("Magazin", a.AmmoCapacity, b.AmmoCapacity);
		Add("Tank", a.FuelCapacity, b.FuelCapacity);
		bool mineA = a.Capabilities.HasFlag(Capability.Mining), mineB = b.Capabilities.HasFlag(Capability.Mining);
		if (mineA != mineB) parts.Add(mineB ? "+Schürfen" : "−Schürfen");
		bool cargoA = a.Capabilities.HasFlag(Capability.Cargo), cargoB = b.Capabilities.HasFlag(Capability.Cargo);
		if (cargoA != cargoB) parts.Add(cargoB ? "+Fracht" : "−Fracht");
		return parts.Count == 0 ? "Werte unverändert" : string.Join(" · ", parts);
	}

	static int OfferSlot(string key)
	{
		var parts = key.Split(':');
		return parts.Length >= 2 && int.TryParse(parts[1], out int i) ? i : -1;
	}

	static int FirstSlotWithOffers(IReadOnlyList<ServiceOffer> offers, Ship ship)
	{
		for (int i = 0; i < ship.Slots.Count; i++)
			if (offers.Any(o => OfferSlot(o.Key) == i && o.Amount >= 1f)) return i;
		return 0;
	}

	/// <summary>Führt ein Angebot über den Dienst aus und zeigt dessen Meldung.</summary>
	public ServiceResult Execute(string offer)
	{
		var svc = Service;
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
	int _upRows;
	/// <summary>Phase 7: angezeigte Upgrade-Zeilen des gewählten Slots.</summary>
	public int TestUpgradeRows => _upRows;
	public string TestMessage => _message;
	public int TestSelectedSlot => _slot;
	public void TestSelectSlot(int slot) { _slot = slot; _message = ""; Invalidate(); }
	/// <summary>Angebots-Schlüssel des gewählten Slots, wie die Oberfläche sie zeigt (aus Offers).</summary>
	public IReadOnlyList<ServiceOffer> TestSlotOffers() =>
		Service is { } s ? s.Offers(Ctx.U, Ctx.Station, Ctx.Ship).Where(o => OfferSlot(o.Key) == _slot).ToList() : new List<ServiceOffer>();
}
