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
/// Kommandozentrale „Debug“ (Nutzerwunsch: Schiffe und Stationen für die eigene Fraktion und Waren für Testzwecke spawnen). Drei Spalten: Schiffsmodelle
/// (gespawnt angedockt an der Station, an der du bist, sonst neben dir), Stationstypen (fertig, eigene, im Sektor neben dir), Waren (in das gewählte eigene
/// Schiff, +10/+100). Oben Kasse setzen und das Zielschiff für Waren wechseln. Regeln im Core (<see cref="DebugSpawn"/>).
/// </summary>
public sealed partial class DebugScreen : CommandScreen, ISessionScreen
{
	public override string Id => "cc.debug";
	public void ResetSession() { _target = null; _message = ""; }

	ShipId? _target;
	string _message = "";
	public string TestMessage => _message;
	public ShipId? TestTarget => _target;

	protected override string HeaderInfo(Universe u, string playerSector) => "Debug · nur für Tests: Spawnen ohne Kosten";

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		var own = u.ShipsById().Where(s => s.IsPlayer).ToList();
		if (_target is not { } tid || !u.Ships.ContainsKey(tid)) _target = u.PlayerShip;
		float iw = InnerW(inner), ih = InnerH(inner);

		// Kopfzeile: Kasse, Zielschiff für Waren, Meldung
		var bMoney = At(inner, new KitButton("Kasse +1 Mio", new Vector2(180, 56)) { FontSize = 16 }, 12, 8);
		bMoney.Pressed += () => { DebugSpawn.SetCredits(u, u.PlayerWallet.Credits + 1_000_000f); _message = $"Kasse: {u.PlayerWallet.Credits:0} Cr"; Invalidate(); };
		var ts = u.Ships.GetValueOrDefault(_target!.Value);
		string tname = ts is null ? "—" : $"#{ts.Id.Value} {(u.ShipModels.TryGetValue(ts.ModelKey, out var tm) ? tm.Name : ts.ModelKey)}";
		var bTarget = At(inner, new KitButton("Waren in: " + KitButton.Fit(ThemeDB.FallbackFont, tname, 16, 220), new Vector2(320, 56)) { FontSize = 16, Sub = "antippen = nächstes eigenes Schiff" }, 204, 8);
		bTarget.Pressed += () =>
		{
			if (own.Count == 0) return;
			int i = own.FindIndex(s => s.Id == _target);
			_target = own[(i + 1) % own.Count].Id;
			Invalidate();
		};
		At(inner, Text(KitButton.Fit(ThemeDB.FallbackFont, _message, 15, iw - 560), 15, Green), 540, 24, iw - 552, 24);

		float top = 76f, colH = ih - top - 12f;
		bool narrow = iw < 900f;
		float colW = narrow ? iw - 24f : (iw - 48f) / 3f;
		Column(inner, 12f, top, colW, narrow ? colH / 3f - 8f : colH, "Schiffe (eigene, gratis)",
			DebugSpawn.ShipModels(u).Select(m => (m.Key, m.Name, u.ShipClasses.TryGetValue(m.ClassKey, out var c) ? c.Name : "")).ToList(),
			key => { var s = DebugSpawn.SpawnShip(u, key, out var msg); _message = msg; if (s is not null) _target = s.Id; Invalidate(); });
		Column(inner, narrow ? 12f : 24f + colW, narrow ? top + colH / 3f : top, colW, narrow ? colH / 3f - 8f : colH, "Stationen (eigene, fertig)",
			DebugSpawn.StationTypes(u).Select(t => (t.Key, t.Name, t.Size.ToString())).ToList(),
			key => { DebugSpawn.SpawnStation(u, key, out var msg); _message = msg; Invalidate(); });
		GoodsColumn(inner, u, narrow ? 12f : 36f + 2f * colW, narrow ? top + 2f * colH / 3f : top, colW, narrow ? colH / 3f - 8f : colH);
	}

	void Column(Control inner, float x, float y, float w, float h, string title, List<(string Key, string Name, string Sub)> items, Action<string> onTap)
	{
		At(inner, Text(title, 17, Cyan), x, y, w, 24);
		var scroll = At(inner, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, x, y + 28f, w, h - 28f);
		var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		list.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(list);
		foreach (var it in items)
		{
			var b = new KitButton(KitButton.Fit(ThemeDB.FallbackFont, it.Name, 16, w - 40), new Vector2(w - 16, 56)) { AlignLeft = true, FontSize = 16, Sub = it.Sub };
			string k = it.Key;
			b.Pressed += () => onTap(k);
			list.AddChild(b);
		}
	}

	void GoodsColumn(Control inner, Universe u, float x, float y, float w, float h)
	{
		At(inner, Text("Waren (in das Zielschiff)", 17, Cyan), x, y, w, 24);
		var scroll = At(inner, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, x, y + 28f, w, h - 28f);
		var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		list.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(list);
		foreach (var g in u.Goods.Values.OrderBy(g => g.Name, StringComparer.Ordinal))
		{
			// zwei Zeilen: voller Name oben, Tasten darunter (lange Warennamen wurden einzeilig gekürzt)
			var row = new Control { CustomMinimumSize = new Vector2(w - 16, 86) };
			At(row, Text(g.Name, 15, White), 0, 0, w - 16, 24);
			var gid = g.Id;
			foreach (var (n, dx) in new[] { (10f, 0f), (100f, 90f) })
			{
				var b = At(row, new KitButton($"+{n:0}", new Vector2(80, 56)) { FontSize = 16 }, dx, 26);
				float amount = n;
				b.Pressed += () =>
				{
					if (_target is { } t && u.Ships.TryGetValue(t, out var ship)) { DebugSpawn.AddCargo(u, ship, gid, amount, out var msg); _message = msg; }
					Invalidate();
				};
			}
			list.AddChild(row);
		}
	}
}
