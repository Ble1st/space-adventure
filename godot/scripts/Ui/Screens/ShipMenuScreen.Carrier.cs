using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Model;
using System.Linq;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Träger (KAMPF.md „Träger und Bordjäger“): Abschnitt „Hangar“ im Reiter „Verband“ des Schiffsmenüs — Belegung, Jäger an Bord (Hülle), „Jäger starten“,
/// „Jäger zurück“ und „Einschleusen“ für eigene S-Schiffe in der Nähe. Nur Anzeige; ausgeführt über <see cref="Carriers"/>.
/// </summary>
public sealed partial class ShipMenuScreen
{
	string _carrierMsg = "";
	/// <summary>Selbsttest: letzte Meldung des Hangar-Abschnitts.</summary>
	internal string TestCarrierMessage => _carrierMsg;

	/// <summary>Selbsttest: sichtbare Taste, deren Text mit <paramref name="text"/> beginnt (null = keine).</summary>
	internal KitButton? TestButton(string text) => Find(this, text);

	static KitButton? Find(Node n, string text)
	{
		foreach (var c in n.GetChildren())
		{
			if (c is KitButton b && b.IsVisibleInTree() && b.Text.StartsWith(text)) return b;
			if (Find(c, text) is { } r) return r;
		}
		return null;
	}

	/// <summary>Was sich am Hangar sichtbar ändern kann (für die Neuaufbau-Signatur).</summary>
	static string CarrierSig(Universe u, Ship s) =>
		!u.Rules.Carriers || !Carriers.IsCarrier(s) ? ""
		: $"{string.Join(",", Carriers.Stowed(u, s.Id).Select(f => $"{f.Id.Value}:{(int)(f.Hull * 10f / System.MathF.Max(1f, f.MaxHull))}"))}|{Carriers.Launched(u, s.Id).Count}|{Carriers.Candidates(u, s).Count}";

	float BuildCarrier(Control p, Universe u, Ship s, float w)
	{
		if (!u.Rules.Carriers || !Carriers.IsCarrier(s)) return 0f;
		float y = 0f;
		y = Line(p, y, w, "Hangar", Carriers.Line(u, s), Cyan);
		foreach (var f in Carriers.Stowed(u, s.Id))
			y = Line(p, y, w, "  an Bord", $"{ShipNames.Display(u, f)} · Hülle {f.Hull / System.MathF.Max(1f, f.MaxHull):0%}");
		y += 4f;
		var sid = s.Id;
		var launch = At(p, new KitButton("Jäger starten", new Vector2(220, 56)) { FontSize = 17 }, 0, y);
		if (Carriers.LaunchIssue(u, s) is { } li) { launch.State = KitButtonState.Disabled; launch.Sub = KitButton.Fit(ThemeDB.FallbackFont, li, 12, 210); }
		launch.Pressed += () => { Carriers.TryLaunch(Ctx.U, Ctx.U.Ships[sid], out _carrierMsg); Rebuild(); };
		var recall = At(p, new KitButton("Jäger zurück", new Vector2(220, 56)) { FontSize = 17 }, 236, y);
		if (Carriers.RecallIssue(u, s) is { } ri) { recall.State = KitButtonState.Disabled; recall.Sub = KitButton.Fit(ThemeDB.FallbackFont, ri, 12, 210); }
		recall.Pressed += () => { Carriers.TryRecall(Ctx.U, Ctx.U.Ships[sid], out _carrierMsg); Rebuild(); };
		y += 62f;
		foreach (var f in Carriers.Candidates(u, s))
		{
			var fid = f.Id;
			var b = At(p, new KitButton(KitButton.Fit(ThemeDB.FallbackFont, $"Einschleusen: {ShipNames.Display(u, f)}", 16, w - 40), new Vector2(w, 56)) { AlignLeft = true, FontSize = 16 }, 0, y);
			b.Pressed += () =>
			{
				var uu = Ctx.U;
				if (uu.Ships.TryGetValue(fid, out var ff) && uu.Ships.TryGetValue(sid, out var cc)) Carriers.TryStow(uu, cc, ff, out _carrierMsg);
				Rebuild();
			};
			y += 62f;
		}
		if (_carrierMsg.Length > 0)
		{
			At(p, Text(KitButton.Fit(ThemeDB.FallbackFont, _carrierMsg, 14, w), 14, Dim), 0, y, w, 22);
			y += 26f;
		}
		return y + 8f;
	}
}
