using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Bars;
using SpaceSim.Core.Services;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Dock-Bereich „Bar“ (Dienst „bar“, docs/BARS.md): links das Rubbellos (Kaufen, neun Felder zum Antippen, „Alle aufdecken“, „Gewinn abholen“), rechts
/// (schmal: darunter) die Gerüchte aus <see cref="Bars.Rumours"/> (das kaufbare mit „Kaufen“) und die Söldner-Abkürzung. Knöpfe nur aus
/// <see cref="IStationService.Offers"/>, ausgeführt nur über <see cref="IStationService.Execute"/>; Meldung aus <see cref="ServiceResult.Message"/>.
/// </summary>
public sealed partial class BarScreen : DockAreaScreen
{
	public override string Id => "dock.bar";
	public BarScreen() => AreaId = DockAreas.Bar;

	/// <summary>Wird nach jedem Execute ausgelöst (true = Erfolg) – Main spielt den Ton.</summary>
	public event Action<bool>? Executed;

	string _message = "";
	bool _msgOk = true;
	readonly Dictionary<string, KitButton> _buttons = new();
	int _rumours;

	IStationService? Service => Ctx.Services.ForStation(Ctx.U, Ctx.Station, Ctx.Ship).FirstOrDefault(s => s.Key == BarService.ServiceKey);

	protected override void OnOpen() { _message = ""; }

	protected override void BuildContent(Control inner)
	{
		float w = inner.Size.X > 0 ? inner.Size.X : 670f;
		_buttons.Clear();
		_rumours = 0;
		var u = Ctx.U;
		var svc = Service;
		IReadOnlyList<ServiceOffer> offers = svc is null ? Array.Empty<ServiceOffer>() : svc.Offers(u, Ctx.Station, Ctx.Ship);
		float msgEnd = WrappedText(inner, _message.Length > 0 ? _message
				: "Musik, Gerüchte und Rubbellose. Wer Söldner sucht, fragt hier nach.", 0, 0, w, 15,
			_message.Length > 0 ? (_msgOk ? Green : Red) : new Color(Dim, 0.85f));
		float y0 = MathF.Max(44f, msgEnd + 6f);
		if (w >= 680f)
		{
			float left = MathF.Min(360f, w * 0.48f);
			Card(inner, offers, 0, y0, left);
			Talk(inner, offers, left + 16, y0, w - left - 16);
		}
		else
		{
			float y = Card(inner, offers, 0, y0, w);
			Talk(inner, offers, 0, y + 14, w);
		}
	}

	/// <summary>Rubbellos-Karte. Liefert die y-Position darunter.</summary>
	float Card(Control inner, IReadOnlyList<ServiceOffer> offers, float x, float y0, float w)
	{
		var u = Ctx.U;
		var r = u.Rules;
		At(inner, Text("Rubbellos", 20, Cyan), x, y0, w, 28);
		float y = y0 + 32f;
		var t = Bars.OpenTicket(u);
		bool here = t is not null && t.Station == Ctx.Station.Id;
		if (!here)
		{
			y = WrappedText(inner, $"{r.BarTicketPrice:0} Cr je Los · drei gleiche Symbole gewinnen: {Scratchcard.PrizeTable(r)}.", x, y, w, 14, Dim);
			y = WrappedText(inner, $"Auszahlung höchstens aus der Stationskasse. Im Mittel zahlt ein Los {Scratchcard.ExpectedValue(r):0.#} Cr zurück.", x, y + 2, w, 13, new Color(Dim, 0.75f));
			var o = offers.FirstOrDefault(of => of.Key == BarService.Ticket);
			if (o.Key is not null)
			{
				bool can = o.Amount > 0f;
				var b = At(inner, new KitButton($"Los kaufen · {o.Price:0} Cr", new Vector2(MathF.Min(w, 260f), 56)) { FontSize = 17, State = can ? KitButtonState.Ready : KitButtonState.Disabled }, x, y + 8);
				Wire(b, o.Key, can);
				y += 70f;
				string note = o.Note ?? $"Heute noch {o.Stock:0} Lose hier.";
				y = WrappedText(inner, note, x, y, w, 13, o.Note is not null ? Orange : Dim);
			}
			return y;
		}
		// neun Felder
		float cell = MathF.Min(104f, (w - 16f) / 3f);
		var winning = t!.AllRevealed ? t.Tier : -1;
		for (int i = 0; i < Scratchcard.Fields; i++)
		{
			float cx = x + (i % 3) * (cell + 8f), cy = y + (i / 3) * 72f;
			bool open = t.IsRevealed(i);
			string label = open ? Scratchcard.SymbolName(t.Symbols[i]) : "?";
			var b = At(inner, new KitButton(label, new Vector2(cell, 64)) { FontSize = open ? 15 : 24,
				State = !open ? KitButtonState.Ready : t.Symbols[i] == winning ? KitButtonState.On : KitButtonState.Normal }, cx, cy);
			if (!open) Wire(b, $"{BarService.RevealKey}:{i}", true);
		}
		y += 3 * 72f + 4f;
		var all = offers.FirstOrDefault(of => of.Key == $"{BarService.RevealKey}:{BarService.All}");
		var claim = offers.FirstOrDefault(of => of.Key == BarService.Claim);
		float bw = MathF.Min(170f, (w - 8f) / 2f);
		if (all.Key is not null) Wire(At(inner, new KitButton("Alle aufdecken", new Vector2(bw, 56)) { FontSize = 16, State = KitButtonState.Normal }, x, y), all.Key, true);
		if (claim.Key is not null)
		{
			bool can = claim.Amount > 0f;
			var b = At(inner, new KitButton(claim.Text, new Vector2(bw, 56)) { FontSize = 16, State = can ? (t.Prize > 0 ? KitButtonState.Ready : KitButtonState.Normal) : KitButtonState.Disabled }, x + bw + 8f, y);
			Wire(b, claim.Key, can);
		}
		y += 62f;
		string status = !t.AllRevealed ? $"{t.RevealedCount} von {Scratchcard.Fields} Feldern offen — tippe ein Feld."
			: t.Prize > 0 ? $"Gewinn: {t.Prize} Cr" + (Bars.Payout(u, t) + 0.5f < t.Prize ? $" (die Bar kann nur {Bars.Payout(u, t):0} zahlen)" : "") : "Kein Dreier — Niete.";
		return WrappedText(inner, status, x, y, w, 14, t.AllRevealed && t.Prize > 0 ? Green : Dim);
	}

	/// <summary>Gerüchte und Söldner.</summary>
	void Talk(Control inner, IReadOnlyList<ServiceOffer> offers, float x, float y0, float w)
	{
		var u = Ctx.U;
		At(inner, Text("Gerüchte", 20, Cyan), x, y0, w, 28);
		float y = y0 + 32f;
		foreach (var rm in Bars.Rumours(u, Ctx.Station))
		{
			_rumours++;
			var o = rm.Paid ? offers.FirstOrDefault(of => of.Key == $"{BarService.Rumour}:{rm.Reveal.Value}") : default;
			float tw = o.Key is not null ? w - 150f : w;
			float end = WrappedText(inner, "„" + rm.Text + "“", x, y, tw, 14, White);
			if (o.Key is not null)
			{
				bool can = o.Amount > 0f;
				var b = At(inner, new KitButton($"Kaufen · {o.Price:0} Cr", new Vector2(142, 56)) { FontSize = 15, State = can ? KitButtonState.Ready : KitButtonState.Disabled }, x + w - 142, y);
				Wire(b, o.Key, can);
				end = MathF.Max(end, y + 58f);
				if (!can && o.Note is { } n) end = WrappedText(inner, n, x, end, w, 13, Orange);
			}
			y = end + 8f;
		}
		var mercs = offers.Where(of => of.Key.StartsWith(BarService.Merc + ":")).ToList();
		if (mercs.Count == 0) return;
		At(inner, Text("Söldner", 20, Cyan), x, y + 4, w, 28);
		y += 36f;
		y = WrappedText(inner, $"Der Wirt vermittelt Söldner von {SpaceSim.Core.Model.ActorNames.Tagged(u, Ctx.Station.Owner)} — {u.Rules.MercenaryFeePerDay:0} Cr je Tag.", x, y, w, 13, Dim);
		float bx = x;
		string? reason = null;
		foreach (var o in mercs)
		{
			bool can = o.Amount > 0f;
			string days = o.Key[(BarService.Merc.Length + 1)..];
			var b = At(inner, new KitButton($"{days} Tag{(days == "1" ? "" : "e")} anheuern", new Vector2(MathF.Min(170f, (w - 8f) / 2f), 56)) { FontSize = 15, State = can ? KitButtonState.Ready : KitButtonState.Disabled }, bx, y + 4);
			Wire(b, o.Key, can);
			bx += MathF.Min(170f, (w - 8f) / 2f) + 8f;
			if (!can) reason ??= o.Note;
		}
		y += 64f;
		if (reason is not null) WrappedText(inner, reason, x, y, w, 13, Orange);
	}

	void Wire(KitButton b, string key, bool can)
	{
		_buttons[key] = b;
		if (can) b.Pressed += () => Execute(key);
	}

	/// <summary>Führt ein Angebot über den Dienst aus und zeigt dessen Meldung.</summary>
	public ServiceResult Execute(string offer)
	{
		var svc = Service;
		var r = svc is null
			? new ServiceResult(false, 0f, 0f, "Keine Bar an dieser Station.")
			: svc.Execute(Ctx.U, Ctx.Station, Ctx.Ship, new ServiceRequest(offer, default, 0f));
		_message = r.Message;
		_msgOk = r.Success;
		Executed?.Invoke(r.Success);
		Invalidate();
		return r;
	}

	// ── Zugriff für Selbsttest ──
	public string TestMessage => _message;
	public int TestRumourCount => _rumours;
	public KitButton? TestButton(string key) => _buttons.TryGetValue(key, out var b) && IsInstanceValid(b) ? b : null;
}
