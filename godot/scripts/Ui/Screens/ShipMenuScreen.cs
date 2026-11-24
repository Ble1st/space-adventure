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
/// Kleines Schiffsmenü (Nutzerwunsch: „für jedes eigene Schiff ein kleines eigenes Menü, getrennt vom großen Menü, mit allen Werten, Modulen, Inventar,
/// Befehlen und weiteren Schiffen im Verband/Flotte“). Geöffnet aus Hangar (Dock), Flotte und Besitz; „✕ Zurück“ führt dorthin zurück. Nur Anzeige
/// plus Wege: Befehle geben in der Flotte, Übernehmen wie im Hangar — Regeln bleiben im Core.
/// </summary>
public sealed partial class ShipMenuScreen : Control, IScreen, ISessionScreen, IMessageStrip
{
	public string Id => "ship.menu";
	public string Title => "Schiff";
	public Control Node => this;
	public bool IsOpen => Visible;
	public CommandContext Ctx { get; set; } = null!;
	public Rect2? MessageStripGlobal => null;

	/// <summary>Das gezeigte Schiff (setzt Main vor dem Öffnen).</summary>
	public ShipId Ship;
	/// <summary>„✕ Zurück“.</summary>
	public event Action? Closed;
	/// <summary>„Befehle“ → Flotte mit diesem Schiff gewählt.</summary>
	public event Action<ShipId>? CommandsRequested;
	/// <summary>„Übernehmen“ (Fernwechsel/Hangar).</summary>
	public event Action<ShipId>? SwitchRequested;
	/// <summary>Anderes Schiff aus dem Verband/der Flotte öffnen.</summary>
	public event Action<ShipId>? OtherShipRequested;

	public enum Tab { Overview, Modules, Cargo, Command, Fleet }
	public Tab Current = Tab.Overview;
	long _builtTick = -1;
	string _sig = "";

	public ShipMenuScreen()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
	}

	public void ResetSession() { Current = Tab.Overview; ResetExtras(); foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); } }
	public void Open() { Visible = true; _bpName = ""; _bpMsg = ""; Rebuild(); }   // Baupläne: Name/Meldung je Öffnen neu
	public void Close() => Visible = false;

	/// <summary>Neu aufbauen, wenn sich die angezeigten Werte geändert haben (nie bei gedrückter Taste oder laufendem Wisch).</summary>
	public void Refresh()
	{
		if (!Visible || Ctx?.U is null) return;
		if (Ctx.U.Tick == _builtTick) return;
		if (KitButton.AnyHeld > 0 || KitScroll.Dragging || EditingBlueprintName) return;   // Baupläne: nie beim Tippen des Namens
		if (GetViewport()?.GuiGetFocusOwner() is LineEdit) return;   // Umbenennen: nicht unter dem Tippen neu aufbauen
		if (Signature() == _sig) { _builtTick = Ctx.U.Tick; return; }
		var keep = KitScroll.Capture(this);
		Rebuild();
		KitScroll.Restore(this, keep);
	}

	public override void _Process(double delta) => Refresh();

	public override void _Draw()
	{
		if (Visible) DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.02f, 0.05f, 0.88f));
	}

	/// <summary>Was sich sichtbar ändern kann (Ort, Befehl, Fracht, Rumpf, Kasse, Verband) — nur dann wird neu aufgebaut.</summary>
	string Signature()
	{
		var u = Ctx.U;
		if (!u.Ships.TryGetValue(Ship, out var s)) return "weg";
		string cargo = string.Join(",", s.Cargo.Where(kv => kv.Value > 0.01f).OrderBy(kv => kv.Key.Value).Select(kv => $"{kv.Key.Value}:{kv.Value:0.0}"));
		return $"{s.Sector.Value}|{s.DockedAt.Value}|{s.Transit is not null}|{(int)s.Hull}|{(int)s.ShieldNow}|{s.TotalFuel:0}|{cargo}|{Commands.Describe(u, s)}|{s.Squadron.Value}|{(int)u.WalletOf(s).Credits}|{u.PlayerShip.Value}|{u.PlayerOnFoot}|{Current}|{s.CustomName}|{TransferSig(u, s)}";
	}

	void Rebuild()
	{
		foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
		QueueRedraw();
		var u = Ctx.U;
		_builtTick = u.Tick;
		_sig = Signature();
		float W = Size.X > 0 ? Size.X : 1280f, H = Size.Y > 0 ? Size.Y : 720f;
		float pw = MathF.Min(980f, W - 32f), ph = MathF.Min(660f, H - 24f);
		float px = (W - pw) * 0.5f, py = (H - ph) * 0.5f;
		var card = CardBg(this, px, py, pw, ph, PanelSolid, Edge, 16, 2);

		var back = At(card, new KitButton("✕ Zurück", new Vector2(150, 56)) { FontSize = 18 }, pw - 166, 12);
		back.Pressed += () => Closed?.Invoke();
		if (!u.Ships.TryGetValue(Ship, out var s))
		{
			At(card, Text("Dieses Schiff gibt es nicht mehr.", 22, Orange), 20, 20, pw - 200, 32);
			return;
		}
		bool active = s.Id == u.PlayerShip;
		string model = u.ShipModels.TryGetValue(s.ModelKey, out var m) ? m.Name : s.ModelKey;
		string cls = m is not null && u.ShipClasses.TryGetValue(m.ClassKey, out var c0) ? c0.Name : "";
		At(card, Text(KitButton.Fit(ThemeDB.FallbackFont, s.CustomName.Length > 0 ? $"{ShipNames.Display(u, s)} · {model}" : $"#{s.Id.Value} {model}", 24, pw - 200), 24, Cyan), 20, 12, pw - 200, 32);
		At(card, Text(KitButton.Fit(ThemeDB.FallbackFont, $"{cls} · {ShipQualityNames.Name(s.Quality)} · {Where(u, s)}{(active ? (u.PlayerOnFoot ? " · aktiv (du bist zu Fuß)" : " · aktiv (du bist an Bord)") : "")}", 15, pw - 200), 15, Dim), 20, 46, pw - 200, 22);

		// Reiter
		float tx = 20f;
		foreach (var (tab, label) in new[] { (Tab.Overview, "Übersicht"), (Tab.Modules, "Module"), (Tab.Cargo, "Inventar"), (Tab.Command, "Befehl"), (Tab.Fleet, "Verband") })
		{
			var b = At(card, new KitButton(label, new Vector2(150, 56)) { FontSize = 17, State = Current == tab ? KitButtonState.On : KitButtonState.Normal }, tx, 78);
			var t = tab;
			b.Pressed += () => { Current = t; Rebuild(); };
			tx += 158f;
		}
		var scroll = At(card, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, 20, 146, pw - 40, ph - 160);
		var body = new Control { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		scroll.AddChild(body);
		float bw = pw - 60f;
		float y = Current switch
		{
			Tab.Overview => BuildOverview(body, u, s, bw),
			Tab.Modules => BuildModules(body, u, s, bw),
			Tab.Cargo => BuildCargo(body, u, s, bw),
			Tab.Command => BuildCommand(body, u, s, bw, active),
			_ => BuildFleet(body, u, s, bw),
		};
		body.CustomMinimumSize = new Vector2(bw, y + 12f);
	}

	static string Where(Universe u, Ship s)
	{
		string sector = u.Sectors.TryGetValue(s.Sector, out var sec) ? sec.Name : "?";
		if (u.Stations.TryGetValue(s.DockedAt, out var st)) return $"angedockt an {SpaceSim.Core.Model.StationNames.Short(u, st)}";
		if (s.Transit is not null) return $"unterwegs ({sector})";
		return $"im Raum, {sector}";
	}

	static float Line(Control parent, float y, float w, string name, string value, Color? color = null)
	{
		At(parent, Text(name, 16, Dim), 0, y, 260, 24);
		At(parent, Text(KitButton.Fit(ThemeDB.FallbackFont, value, 16, w - 270), 16, color ?? White), 270, y, w - 270, 24);
		return y + 28f;
	}

	float BuildOverview(Control p, Universe u, Ship s, float w)
	{
		var st = s.Stats;
		float y = 0f;
		y = Line(p, y, w, "Rumpf", $"{(int)s.Hull} / {(int)s.MaxHull}", s.Hull < s.MaxHull * 0.5f ? Orange : null);
		y = Line(p, y, w, "Schild", st.Shield > 0f ? $"{(int)s.ShieldNow} / {(int)st.Shield} (Regen {st.ShieldRegen:0.#}/s)" : "–");
		y = Line(p, y, w, "Panzerung", st.Armor > 0f ? $"{st.Armor:0}" : "–");
		y = Line(p, y, w, "Tempo", $"{st.Speed:0.#} · Masse {st.Mass:0} · Schub {st.Thrust:0}");
		y = Line(p, y, w, "Leistung", $"Verbrauch {st.PowerDraw:0.#} von {st.PowerOutput:0.#} · Spitze {st.PeakDraw:0.#} · Speicher {st.PowerStorage:0}");
		y = Line(p, y, w, "Laderaum", $"{s.UsedVolume(u) + s.MissionCargo:0.#} / {s.CargoCapacity:0} m³" + (st.CargoSolid + st.CargoLiquid > 0f ? $" (fest {st.CargoSolid:0}, flüssig {st.CargoLiquid:0}, frei {st.CargoAny:0})" : ""));
		if (u.Rules.FuelActive) y = Line(p, y, w, "Tank", st.FuelCapacity > 0f ? SpaceSim.Core.Flight.TankInfo.HudLine(u, s) : "–");
		y = Line(p, y, w, "Waffen", st.WeaponPower > 0f ? $"Stärke {st.WeaponPower:0.#} · Munition {st.AmmoCapacity:0} · Raketen {st.MissileCapacity:0} · Abwehr {st.PointDefense:P0}" : "keine");
		y = Line(p, y, w, "Bergbau", st.MiningTier > 0f ? $"Stufe {st.MiningTier:0.##}" + (st.UsesContact ? " (Kontaktwerkzeug)" : " (Laser)") : "–");
		if (st.GasCollect > 0f) y = Line(p, y, w, "Gas", $"{st.GasCollect:0.#}/s");
		if (st.Passengers > 0) y = Line(p, y, w, "Passagiere", $"{st.Passengers} Plätze");
		if (st.JumpRange > 0) y = Line(p, y, w, "Sprungantrieb", $"{st.JumpRange} Sprünge");
		if (st.Cloak > 0f) y = Line(p, y, w, "Tarnfeld", $"Stärke {st.Cloak:0.#}");
		y = Line(p, y, w, "Fähigkeiten", s.Capabilities.ToString());
		y = Line(p, y, w, "Unterhalt", $"{st.Upkeep:0.##} Cr/Tick");
		y = Line(p, y, w, "Kasse", $"{(int)u.WalletOf(s).Credits} Cr");
		return BuildRename(p, u, s, y + 8f, w);
	}

	float BuildModules(Control p, Universe u, Ship s, float w)
	{
		float y = 0f;
		for (int i = 0; i < s.Slots.Count; i++)
		{
			var slot = s.Slots[i];
			string key = i < s.Fit.Count ? s.Fit[i] ?? "" : "";
			string name = key.Length == 0 ? "— leer —" : u.Modules.TryGetValue(key, out var mod) ? mod.Name : key;
			float cond = s.ConditionOf(i);
			bool burning = s.Systems.Burning.Contains(i);
			string state = key.Length == 0 ? "" : $" · Zustand {cond:P0}{(cond < u.Rules.ModuleFailCondition ? " AUSGEFALLEN" : "")}{(burning ? " BRENNT" : "")}";
			y = Line(p, y, w, $"{i + 1}. {slot.Type} {slot.Size}", name + state, burning || cond < u.Rules.ModuleFailCondition ? Red : cond < 0.75f ? Orange : null);
		}
		if (s.Slots.Count == 0) y = Line(p, y, w, "Module", "keine Steckplätze (Schiff ohne Modell)");
		return BuildBlueprints(p, u, s, w, y + 8f);   // Baupläne: als Bauplan speichern, Liste, Löschen
	}

	/// <summary>UI-Umbau (Nutzerwunsch „im eigenen Schiff sehen, was geladen ist“): Inventar mit Farbleiste, je Ware Menge, Wert, bester bekannter Abnehmer
	/// (Tipp = Autopilot dorthin, nur für das aktive Schiff) und „Abwerfen“ (zwei Tipps); versiegelte Auftragsfracht und Tank darunter.</summary>
	float BuildCargo(Control p, Universe u, Ship s, float w)
	{
		float y = 0f;
		float used = s.UsedVolume(u) + s.MissionCargo, cap = MathF.Max(1f, s.CargoCapacity);
		float worth = s.Cargo.Where(kv => kv.Value > 0.01f && u.Goods.ContainsKey(kv.Key)).Sum(kv => u.Goods[kv.Key].BasePrice * kv.Value);
		y = Line(p, y, w, "Laderaum", $"{used:0.#} von {s.CargoCapacity:0} m³ · Wert ≈ {worth:0} Cr");
		var bar = At(p, new FlightHud.CargoBar(), 0, y + 2, w, 14);
		foreach (var (gid, amount) in s.Cargo.Where(kv => kv.Value > 0.01f && u.Goods.ContainsKey(kv.Key)).OrderByDescending(kv => kv.Value * u.Goods[kv.Key].VolumePerUnit))
			bar.Segments.Add((Color.FromHtml(u.Goods[gid].Color), amount * u.Goods[gid].VolumePerUnit / cap));
		if (s.MissionCargo > 0.01f) bar.Segments.Add((FlightHud.MissionGold, s.MissionCargo / cap));
		y += 26f;
		bool active = s.Id == u.PlayerShip;
		bool any = false;
		foreach (var (gid, amount) in s.Cargo.Where(kv => kv.Value > 0.01f).OrderBy(kv => kv.Key.Value).ToList())
		{
			any = true;
			var g = u.Goods.TryGetValue(gid, out var gg) ? gg : null;
			string gname = g?.Name ?? $"Ware {gid.Value}";
			var buyer = SpaceSim.Core.Trading.MarketInfo.BestBuyerForGood(u, gid);
			string where = buyer is null ? "kein bekannter Abnehmer" : $"{SpaceSim.Core.Model.StationNames.WithoutOwner(u, buyer)} · {buyer.Market.GetValueOrDefault(gid)?.Price ?? 0f:0.0} Cr"
				+ (u.Sectors.TryGetValue(buyer.Sector, out var bs) && buyer.Sector != s.Sector ? $" · {bs.Name}" : " · hier im Sektor");
			var row = At(p, new KitButton(gname, new Vector2(w - 150f, 60)) { AlignLeft = true, FontSize = 17, Sub = $"{amount:0.#} · ≈ {(g?.BasePrice ?? 0f) * amount:0} Cr · {where}" }, 0, y);
			var col = g is null ? Dim : Color.FromHtml(g.Color);
			row.Symbol = k => k.DrawRect(new Rect2(12, k.Size.Y / 2f - 7f, 14, 14), col);
			if (buyer is not null && active)
			{
				var bid = buyer.Id;
				row.Pressed += () => BuyerRequested?.Invoke(bid);
			}
			else row.State = KitButtonState.Normal;
			string? issue = SpaceSim.Core.Trading.CargoJettison.Issue(u, s, gid);
			var drop = At(p, new KitButton("Abwerfen", new Vector2(140, 60)) { FontSize = 16 }, w - 140f, y);
			if (issue is not null) { drop.State = KitButtonState.Disabled; drop.Sub = "gesperrt"; }
			else
			{
				var sid = s.Id;
				_confirm.Wire(drop, $"drop:{sid.Value}:{gid.Value}", "Abwerfen", () =>
				{
					if (u.Ships.TryGetValue(sid, out var sh)) SpaceSim.Core.Trading.CargoJettison.Jettison(u, sh, gid, 0f);
					Rebuild();
				});
			}
			y += 68f;
		}
		if (s.MissionCargo > 0.01f) { any = true; y = Line(p, y, w, "Versiegelte Fracht", $"{s.MissionCargo:0.#} m³ (Auftrag)", FlightHud.MissionGold); }
		if (!any) y = Line(p, y, w, "An Bord", "leer");
		y += 6f;
		foreach (var line in SpaceSim.Core.Flight.TankInfo.For(u, s))
			y = Line(p, y, w, "Tank: " + line.Name, SpaceSim.Core.Flight.TankInfo.Line(line));
		if (active && s.Cargo.Count > 0) y = Line(p, y + 4, w, "Tipp", "Ware antippen: Autopilot zum besten Abnehmer", Dim);
		return BuildTransfer(p, u, s, y + 12f, w);
	}

	/// <summary>UI-Umbau: Inventar-Zeile angetippt — Autopilot zum besten Abnehmer dieser Ware.</summary>
	public event Action<StationId>? BuyerRequested;
	readonly KitConfirm _confirm = new();

	float BuildCommand(Control p, Universe u, Ship s, float w, bool active)
	{
		float y = 0f;
		y = Line(p, y, w, "Befehl", Commands.Describe(u, s), s.Command is null ? Dim : Cyan);
		y = Line(p, y, w, "Arbeit", s.Order?.GetType().Name.Replace("Order", "") ?? "–");
		y = Line(p, y, w, "Kampfregeln", s.Engagement.ToString());
		if (s.Emergency) y = Line(p, y, w, "Notlage", "fliegt zur Reparatur/zum Tanken", Orange);
		y += 8f;
		var bCmd = At(p, new KitButton("Befehle geben (Flotte)", new Vector2(300, 56)) { FontSize = 17 }, 0, y);
		var sid = s.Id;
		bCmd.Pressed += () => CommandsRequested?.Invoke(sid);
		if (s.Command is not null && !(active && !u.PlayerOnFoot))
		{
			var bStop = At(p, new KitButton("Befehl beenden", new Vector2(220, 56)) { FontSize = 17 }, 316, y);
			bStop.Pressed += () => { Commands.Cancel(u, u.Ships[sid]); Rebuild(); };
		}
		if (!active || u.PlayerOnFoot)
		{
			var bSw = At(p, new KitButton("Übernehmen", new Vector2(200, 56)) { FontSize = 17 }, 552, y);
			bSw.Pressed += () => SwitchRequested?.Invoke(sid);
		}
		return y + 64f;
	}

	float BuildFleet(Control p, Universe u, Ship s, float w)
	{
		float y = 0f;
		var sq = Squadrons.Get(u, s);
		if (sq is not null)
		{
			y = Line(p, y, w, "Verband", $"{sq.Name} · {sq.Members.Count} Schiffe · Formation {sq.Formation}");
			foreach (var mid in sq.Members) y = ShipRow(p, u, mid, y, w, mid == sq.Leader ? "Anführer" : "Mitglied");
		}
		else y = Line(p, y, w, "Verband", "keiner");
		y += 8f;
		var others = u.ShipsById().Where(o => o.IsPlayer && o.Id != s.Id && (sq is null || !sq.Members.Contains(o.Id))).ToList();
		y = Line(p, y, w, "Flotte", $"{others.Count + 1} eigene Schiffe");
		foreach (var o in others) y = ShipRow(p, u, o.Id, y, w, "");
		return y;
	}

	float ShipRow(Control p, Universe u, ShipId id, float y, float w, string role)
	{
		if (!u.Ships.TryGetValue(id, out var o)) return y;
		var b = At(p, new KitButton(KitButton.Fit(ThemeDB.FallbackFont, $"{ShipNames.Display(u, o)}{(role.Length > 0 ? " · " + role : "")}", 16, w - 40), new Vector2(w, 56))
		{ AlignLeft = true, FontSize = 16, Sub = KitButton.Fit(ThemeDB.FallbackFont, $"{Where(u, o)} · {Commands.Describe(u, o)}", 13, w - 40) }, 0, y);
		var oid = o.Id;
		b.Pressed += () => OtherShipRequested?.Invoke(oid);
		return y + 62f;
	}
}
