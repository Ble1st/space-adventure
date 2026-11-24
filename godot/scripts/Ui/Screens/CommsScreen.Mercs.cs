using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Simulation;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Comms, Reiter „Aufträge“: Abschnitt „Söldner“ — laufende Verträge (Restzeit, Sold, „Entlassen“) und Anbieter (Gilden, Unabhängige) mit „Anheuern“ je
/// Vertragslänge. Zeilen aus <see cref="MercenaryAction"/>-Angeboten, Ausführung nur über die Comms-Registry.
/// </summary>
public sealed partial class CommsScreen
{
	readonly Dictionary<string, KitButton> _mercButtons = new();
	/// <summary>Selbsttest: Taste eines Söldner-Angebots („merc:hire:…“ / „merc:end:…“).</summary>
	public KitButton? TestMercButton(string key) => _mercButtons.TryGetValue(key, out var b) && IsInstanceValid(b) ? b : null;

	float BuildMercs(Control box, SpaceSim.Core.Model.Universe u, float y, float w)
	{
		_mercButtons.Clear();
		if (!u.Rules.Mercenaries) return y;
		y += 6f;
		At(box, Text("Söldner", 20, Cyan), 0, y, w - 20, 28);
		y += 34f;
		foreach (var c in u.Mercenaries)
		{
			y = WrappedText(box, Mercenaries.Text(u, c), 0, y, w - 8, 14, c.Ending ? Dim : White, 18) + 4f;
			if (c.Ending) continue;
			string key = $"{MercenaryAction.KeyId}:end:{c.Ship.Value}";
			y = MercButton(box, u, key, "Entlassen", y, w);
		}
		var offers = Ctx.Comms.Offers(u).Where(o => o.Key.StartsWith(MercenaryAction.KeyId + ":hire:", StringComparison.Ordinal)).ToList();
		if (offers.Count == 0) { At(box, Text("Kein Anbieter bekannt.", 14, new Color(Dim, 0.9f)), 0, y, w - 20, 22); return y + 28f; }
		foreach (var o in offers)
		{
			y = WrappedText(box, o.Text, 0, y, w - 8, 14, White, 18) + 2f;
			string prev = o.Possible ? o.Preview : (o.Reason ?? "nicht möglich");
			if (prev.Length > 0) y = WrappedText(box, prev, 0, y, w - 8, 12, o.Possible ? new Color(Dim, 0.95f) : Orange, 15) + 6f;
			y = MercButton(box, u, o.Key, $"Anheuern ({o.Cost:0} Cr)", y, w);
		}
		return y;
	}

	float MercButton(Control box, SpaceSim.Core.Model.Universe u, string key, string label, float y, float w)
	{
		var offer = Ctx.Comms.Preview(u, key);
		var b = At(box, new KitButton(label, new Vector2(MathF.Min(260f, w - 8f), 52)) { FontSize = 16, State = offer.Possible ? KitButtonState.Ready : KitButtonState.Disabled }, 0, y);
		if (offer.Possible) b.Pressed += () => Execute(key);
		_mercButtons[key] = b;
		return y + 58f;
	}
}
