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
/// Dock-Bereich „Behörde“ (Phase 11, Dienste „court“ und „bounty“ an Regierungsstationen): oben das Gericht — offene Strafen je Fraktion mit Stufe,
/// Verbrechen und Betrag, „Bezahlen“ löscht die Fahndung; darunter das Kopfgeldbüro — dein offenes Kopfgeld mit „Abholen“ und die Fahndungsliste der
/// Fraktion (Gesuchte mit Kopfgeld je Schiff, reine Anzeige). Zeilen ausschließlich aus <see cref="IStationService.Offers"/>, ausgeführt nur über
/// <see cref="IStationService.Execute"/>; Meldung aus <see cref="ServiceResult.Message"/>. Ersetzt an solchen Stationen die Platzhalterkachel „Aufträge“.
/// </summary>
public sealed partial class AuthorityScreen : DockAreaScreen
{
	public override string Id => "dock.authority";
	public AuthorityScreen() => AreaId = DockAreas.Authority;
	protected override bool ShowShip => true;

	/// <summary>Wird nach jedem Execute ausgelöst (true = Erfolg) – Main spielt den Ton.</summary>
	public event Action<bool>? Executed;

	string _message = "";
	bool _msgOk = true;
	int _rows;

	IStationService? Service(string key) => Ctx.Services.ForStation(Ctx.U, Ctx.Station).FirstOrDefault(s => s.Key == key);

	protected override void BuildContent(Control inner)
	{
		float w = inner.Size.X > 0 ? inner.Size.X : 670f;
		_rows = 0;
		var u = Ctx.U;
		string faction = u.FactionOf(Ctx.Station) is { } f && u.Factions.TryGetValue(f, out var fac) ? fac.Name : "?";
		WrappedText(inner, _message.Length > 0 ? _message : $"Behörde von {faction}: Strafe zahlen löscht die Fahndung, Kopfgeld kommt aus der Fraktionskasse.",
			0, 0, w, 14, _message.Length > 0 ? (_msgOk ? Green : Red) : new Color(Dim, 0.85f), 18);
		var court = Service(CourtService.ServiceKey);
		var bounty = Service(BountyService.ServiceKey);
		var fines = court is null ? new List<ServiceOffer>() : court.Offers(u, Ctx.Station, Ctx.Ship).ToList();
		var bOffers = bounty is null ? new List<ServiceOffer>() : bounty.Offers(u, Ctx.Station, Ctx.Ship).ToList();
		float tw = w - 150f;

		// ── Gericht (oben, volle Breite) ──
		At(inner, Text("Gericht", 20, Cyan), 0, 44, w, 28);
		float y = 76f;
		if (fines.Count == 0) { At(inner, Text("Keine offene Strafe — du wirst hier nicht gesucht.", 15, Green), 0, y, w, 24); y += 32f; }
		foreach (var o in fines.Take(3))
		{
			bool can = o.Amount >= 1f;
			At(inner, Text(o.Text, 15, (int)o.Stock >= 3 ? Red : Orange), 0, y + 4, tw, 22);
			At(inner, Text($"Strafe {o.Price:0} Cr · Heat {o.Held:0.0}", 13, Dim), 0, y + 28, tw, 20);
			var b = At(inner, new KitButton("Bezahlen", new Vector2(140, 56)) { FontSize = 16, State = can ? KitButtonState.Ready : KitButtonState.Disabled, Detail = can ? "" : "zu wenig Cr" }, w - 142, y);
			string key = o.Key;
			if (can) b.Pressed += () => Execute(CourtService.ServiceKey, key);
			y += 62f;
			_rows++;
		}

		// ── Kopfgeldbüro (darunter) ──
		y += 8f;
		At(inner, Text("Kopfgeldbüro", 20, Cyan), 0, y, w, 28);
		y += 32f;
		var claim = bOffers.FirstOrDefault(o => o.Key.StartsWith(BountyService.Claim + ":", StringComparison.Ordinal));
		if (claim.Key is null) { At(inner, Text("Kein Kopfgeld offen — besiege Kaperschiffe, die dich überfallen.", 15, new Color(Dim, 0.85f)), 0, y, w, 24); y += 32f; }
		else
		{
			bool can = claim.Amount >= 1f;
			At(inner, Text($"Dein Kopfgeld: {claim.Price:0} Cr", 16, Orange), 0, y + 14, tw, 24);
			var b = At(inner, new KitButton("Abholen", new Vector2(140, 56)) { FontSize = 16, State = can ? KitButtonState.Ready : KitButtonState.Disabled, Detail = can ? "" : "Kasse leer" }, w - 142, y);
			string key = claim.Key;
			if (can) b.Pressed += () => Execute(BountyService.ServiceKey, key);
			_rows++;
			y += 62f;
		}
		At(inner, Text("Fahndungsliste", 16, Dim), 0, y, w, 22);
		y += 26f;
		var list = bOffers.Where(o => o.Key.StartsWith(BountyService.Info + ":", StringComparison.Ordinal)).Take(4).ToList();
		if (list.Count == 0) At(inner, Text("Niemand wird gesucht.", 14, new Color(Dim, 0.8f)), 0, y, w, 22);
		foreach (var o in list)
		{
			At(inner, Text(o.Text, 14, (int)o.Stock >= 3 ? Red : White), 0, y, w - 170, 22);
			At(inner, Text($"{o.Price:0} Cr je Schiff", 14, Orange, HorizontalAlignment.Right), w - 170, y, 170, 22);
			y += 26f;
		}

		// ── Ausbau Punkt 16: Intel kaufen (Sektorkarten, Gebietskarte, Stationslisten der Fraktion) ──
		var intel = Service(SpaceSim.Core.Intel.IntelService.ServiceKey);
		TestIntelKeys.Clear();
		if (intel is null) return;
		y += 12f;
		At(inner, Text("Intel kaufen", 20, Cyan), 0, y, w, 28);
		y += 32f;
		var offers = intel.Offers(u, Ctx.Station, Ctx.Ship).ToList();
		if (offers.Count == 0) { At(inner, Text("Nichts Neues — du kennst das Gebiet dieser Fraktion.", 15, new Color(Dim, 0.85f)), 0, y, w, 24); return; }
		foreach (var o in offers)
		{
			bool can = o.Amount >= 1f;
			float ey = WrappedText(inner, o.Text, 0, y + 4, tw, 14, White, 18);
			At(inner, Text($"{o.Price:0} Cr · Geld an die Fraktionskasse", 13, Dim), 0, ey, tw, 18);
			var b = At(inner, new KitButton("Kaufen", new Vector2(140, 56)) { FontSize = 16, State = can ? KitButtonState.Ready : KitButtonState.Disabled, Detail = can ? "" : "zu wenig Cr" }, w - 142, y);
			string key = o.Key;
			TestIntelKeys.Add(key);
			if (can) b.Pressed += () => Execute(SpaceSim.Core.Intel.IntelService.ServiceKey, key);
			y = MathF.Max(y + 62f, ey + 24f);
			_rows++;
		}
	}

	/// <summary>Ausbau Punkt 16 (Selbsttest): Schlüssel der Intel-Zeilen.</summary>
	public readonly List<string> TestIntelKeys = new();

	/// <summary>Führt ein Angebot eines Dienstes aus und zeigt dessen Meldung.</summary>
	public ServiceResult Execute(string service, string offer)
	{
		var svc = Service(service);
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
	public string TestMessage => _message;
}
