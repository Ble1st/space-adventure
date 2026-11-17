using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Trading;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Dock-Bereich „Schwarzmarkt“ (Dienst „blackmarket“ am Piratenversteck, docs/SCHWARZMARKT.md): links die Fracht an Bord mit „Verkaufen“ an den Hehler,
/// rechts (schmal: darunter) die Hehlerware mit „Kaufen“. Zeilen ausschließlich aus <see cref="IStationService.Offers"/>, ausgeführt nur über
/// <see cref="IStationService.Execute"/> (ganze angebotene Menge); Meldung aus <see cref="ServiceResult.Message"/>. Kopf: Ankaufsanteil, freie Hehlerkasse.
/// </summary>
public sealed partial class BlackMarketScreen : DockAreaScreen
{
	public override string Id => "dock.blackmarket";
	public BlackMarketScreen() => AreaId = DockAreas.BlackMarket;
	protected override bool ShowShip => true;

	/// <summary>Wird nach jedem Execute ausgelöst (true = Erfolg) – Main spielt den Ton.</summary>
	public event Action<bool>? Executed;

	string _message = "";
	bool _msgOk = true;
	int _rows;
	readonly Dictionary<string, KitButton> _buttons = new();

	IStationService? Service => Ctx.Services.ForStation(Ctx.U, Ctx.Station, Ctx.Ship).FirstOrDefault(s => s.Key == BlackMarketService.ServiceKey);

	protected override void OnOpen() { _message = ""; }

	protected override void BuildContent(Control inner)
	{
		float w = inner.Size.X > 0 ? inner.Size.X : 670f;
		_rows = 0;
		_buttons.Clear();
		var u = Ctx.U;
		At(inner, Text(_message.Length > 0 ? _message
				: $"Der Hehler fragt nicht nach Herkunft oder Fahndung. Er zahlt {100f * u.Rules.BlackMarketBuyShare:0} % (frei: {BlackMarket.Purse(u, Ctx.Station):0} Cr), "
				  + $"Hehlerware kostet {100f * u.Rules.BlackMarketSellFactor:0} % des Basispreises.",
			15, _message.Length > 0 ? (_msgOk ? Green : Red) : new Color(Dim, 0.85f)), 0, 0, w, 44);
		var svc = Service;
		IReadOnlyList<ServiceOffer> offers = svc is null ? Array.Empty<ServiceOffer>() : svc.Offers(u, Ctx.Station, Ctx.Ship);
		var sells = offers.Where(o => o.Key.StartsWith(BlackMarketService.Sell + ":")).ToList();
		var buys = offers.Where(o => o.Key.StartsWith(BlackMarketService.Buy + ":")).ToList();
		if (w >= 640f)
		{
			float half = (w - 16) / 2f;
			Column(inner, "An Bord → Hehler", sells, "Nichts an Bord.", 0, 52, half, true);
			Column(inner, "Hehlerware", buys, "Der Hehler hat nichts auf Lager.", half + 16, 52, half, false);
		}
		else
		{
			float y = Column(inner, "An Bord → Hehler", sells, "Nichts an Bord.", 0, 52, w, true);
			Column(inner, "Hehlerware", buys, "Der Hehler hat nichts auf Lager.", 0, y + 12, w, false);
		}
	}

	float Column(Control inner, string title, List<ServiceOffer> offers, string empty, float x, float y0, float w, bool sell)
	{
		At(inner, Text(title, 20, Cyan), x, y0, w, 28);
		float y = y0 + 34f;
		if (offers.Count == 0) { At(inner, Text(empty, 15, new Color(Dim, 0.8f)), x, y, w, 24); return y + 28f; }
		foreach (var o in offers)
		{
			bool can = o.Amount >= 0.01f;
			At(inner, Text(Ctx.U.Goods[o.Good].Name, 17, White), x, y + 4, w - 140, 24);
			// kurz halten (ui-check: die Zeile hat w − 140 Platz); ein Grund ersetzt die Zahlen, wenn nichts geht
			string detail = o.Note is { } note && !can ? note
				: sell ? $"{o.Held:0.#} an Bord · je {o.Price:0.##} Cr" : $"{o.Stock:0.#} da · je {o.Price:0.##} Cr";
			At(inner, Text(detail, 13, o.Note is not null && !can ? Orange : Dim), x, y + 28, w - 140, 20);
			var b = At(inner, new KitButton(sell ? "Verkaufen" : "Kaufen", new Vector2(130, 56)) { FontSize = 16, State = can ? KitButtonState.Ready : KitButtonState.Disabled }, x + w - 132, y);
			string key = o.Key;
			_buttons[key] = b;
			if (can) b.Pressed += () => Execute(key);
			y += 62f;
			_rows++;
		}
		return y;
	}

	/// <summary>Führt ein Angebot über den Dienst aus (ganze angebotene Menge) und zeigt dessen Meldung.</summary>
	public ServiceResult Execute(string offer)
	{
		var svc = Service;
		var r = svc is null
			? new ServiceResult(false, 0f, 0f, "Kein Schwarzmarkt an dieser Station.")
			: svc.Execute(Ctx.U, Ctx.Station, Ctx.Ship, new ServiceRequest(offer, default, 0f));
		_message = r.Message;
		_msgOk = r.Success;
		Executed?.Invoke(r.Success);
		Invalidate();
		return r;
	}

	// ── Zugriff für Selbsttest ──
	public int TestRowCount => _rows;
	public string TestMessage => _message;
	public KitButton? TestButton(string key) => _buttons.TryGetValue(key, out var b) && IsInstanceValid(b) ? b : null;
}
