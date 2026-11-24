using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Lücken gegenüber Interstellar Pilot 2 im Schiffsmenü: „Umbenennen“ (Übersicht) und „Umladen“ zwischen eigenen Schiffen (Inventar).
/// Anzeige nur aus Core-Daten (<see cref="CargoTransfer"/>, <see cref="ShipNames"/>), ausgeführt nur über deren Funktionen.
/// </summary>
public sealed partial class ShipMenuScreen
{
	ShipId _xferTo;
	readonly Dictionary<GoodId, float> _xferQty = new();
	string _xferMsg = "", _renameMsg = "";
	bool _xferOk = true, _renameOk = true;
	LineEdit? _renameEdit;
	KitButton? _renameButton;
	readonly Dictionary<GoodId, KitButton> _xferButtons = new();
	KitButton? _xferTarget;

	void ResetExtras() { _xferTo = default; _xferQty.Clear(); _xferMsg = ""; _renameMsg = ""; _renameEdit = null; _renameButton = null; _xferButtons.Clear(); _xferTarget = null; }

	// ── Selbsttest ──
	public LineEdit? TestRenameEdit => _renameEdit;
	public KitButton? TestRenameButton => _renameButton;
	public KitButton? TestTransferButton(GoodId g) => _xferButtons.GetValueOrDefault(g);
	public KitButton? TestTransferTargetButton => _xferTarget;
	public ShipId TestTransferTo => _xferTo;
	public string TestTransferMessage => _xferMsg;
	public string TestRenameMessage => _renameMsg;

	/// <summary>Partner für die Signatur (das Menü baut neu, wenn ein Schiff dazukommt oder wegfliegt).</summary>
	string TransferSig(Universe u, Ship s) => Current != Tab.Cargo ? "" : string.Join(",", CargoTransfer.Partners(u, s).Select(o => o.Id.Value));

	float BuildRename(Control p, Universe u, Ship s, float y, float w)
	{
		_renameEdit = null; _renameButton = null;
		if (s.Owner != u.PlayerActor) return y;
		At(p, Text("Name", 16, Dim), 0, y + 16, 260, 24);
		var edit = At(p, KitLineEdit.Create(s.CustomName, ShipNames.Model(u, s), new Vector2(MathF.Min(360f, w - 450f), 56), ShipNames.MaxLength), 270, y);
		_renameEdit = edit;
		var btn = At(p, new KitButton("Umbenennen", new Vector2(170, 56)) { FontSize = 17 }, 270 + edit.Size.X + 10f, y);
		_renameButton = btn;
		var sid = s.Id;
		void Apply()
		{
			if (!u.Ships.TryGetValue(sid, out var sh)) return;
			string text = _renameEdit?.Text ?? "";
			var why = ShipNames.RenameIssue(u, sh, text);
			_renameOk = why is null && ShipNames.Rename(u, sh, text);
			_renameMsg = _renameOk ? (sh.CustomName.Length > 0 ? $"Umbenannt: {sh.CustomName}" : "Name gelöscht – Modellname") : why ?? "Nicht möglich";
			_renameEdit?.ReleaseFocus();
			Rebuild();
		}
		btn.Pressed += Apply;
		edit.TextSubmitted += _ => Apply();
		y += 62f;
		if (_renameMsg.Length > 0) y = Line(p, y, w, "", _renameMsg, _renameOk ? Green : Red);
		return y;
	}

	float BuildTransfer(Control p, Universe u, Ship s, float y, float w)
	{
		_xferButtons.Clear(); _xferTarget = null;
		if (s.Owner != u.PlayerActor) return y;
		At(p, Text("Umladen", 18, Cyan), 0, y, 300, 26);
		y += 30f;
		var partners = CargoTransfer.Partners(u, s);
		if (partners.Count == 0)
		{
			y = Line(p, y, w, "Zielschiff", "keins – eigenes Schiff an derselben Station oder ≤ 40 m im Raum", Dim);
			return y;
		}
		if (!partners.Any(o => o.Id == _xferTo)) _xferTo = partners[0].Id;
		var to = partners.First(o => o.Id == _xferTo);
		var tb = At(p, new KitButton(KitButton.Fit(ThemeDB.FallbackFont, $"Ziel: {ShipNames.Display(u, to)}", 17, w - 60), new Vector2(w, 56))
		{ AlignLeft = true, FontSize = 17, Sub = KitButton.Fit(ThemeDB.FallbackFont, $"frei {to.CargoCapacity - to.UsedVolume(u) - to.MissionCargo:0.#} m³" + (partners.Count > 1 ? " · antippen = nächstes Schiff" : ""), 13, w - 60) }, 0, y);
		_xferTarget = tb;
		tb.Pressed += () =>
		{
			int i = partners.FindIndex(o => o.Id == _xferTo);
			_xferTo = partners[(i + 1) % partners.Count].Id;
			_xferQty.Clear();
			Rebuild();
		};
		y += 64f;
		if (_xferMsg.Length > 0) y = Line(p, y, w, "", _xferMsg, _xferOk ? Green : Red);
		bool any = false;
		foreach (var (gid, amount) in s.Cargo.Where(kv => kv.Value > 0.01f).OrderBy(kv => kv.Key.Value).ToList())
		{
			any = true;
			string gname = u.Goods.TryGetValue(gid, out var g) ? g.Name : $"Ware {gid.Value}";
			float max = CargoTransfer.MaxUnits(u, s, to, gid);
			float q = Math.Clamp(_xferQty.TryGetValue(gid, out var qq) ? qq : max, 0f, max);
			float step = MathF.Max(1f, MathF.Round(max / 10f));
			float bx = w - 444f;
			At(p, Text(KitButton.Fit(ThemeDB.FallbackFont, $"{gname} · {amount:0.#} an Bord", 16, bx - 12f), 16, White), 0, y + 16, bx - 8f, 24);
			var gidc = gid;
			var minus = At(p, new KitButton("−", new Vector2(56, 56)) { FontSize = 22, State = q > 0f ? KitButtonState.Normal : KitButtonState.Disabled }, bx, y);
			minus.Pressed += () => { _xferQty[gidc] = MathF.Max(0f, q - step); Rebuild(); };
			At(p, Text($"{q:0.#}", 18, White, HorizontalAlignment.Center), bx + 64f, y + 16, 80, 24);
			var plus = At(p, new KitButton("+", new Vector2(56, 56)) { FontSize = 22, State = q < max ? KitButtonState.Normal : KitButtonState.Disabled }, bx + 152f, y);
			plus.Pressed += () => { _xferQty[gidc] = MathF.Min(max, q + step); Rebuild(); };
			var bMax = At(p, new KitButton("Max", new Vector2(80, 56)) { FontSize = 16 }, bx + 216f, y);
			bMax.Pressed += () => { _xferQty[gidc] = max; Rebuild(); };
			var go = At(p, new KitButton("Umladen", new Vector2(140, 56)) { FontSize = 16, State = q > 0f ? KitButtonState.Ready : KitButtonState.Disabled }, bx + 304f, y);
			string? issue = CargoTransfer.Issue(u, s, to, gid);
			if (issue is not null) { go.State = KitButtonState.Disabled; go.Sub = KitButton.Fit(ThemeDB.FallbackFont, issue, 11, 130); }
			_xferButtons[gid] = go;
			var sid = s.Id; var tid = to.Id;
			go.Pressed += () =>
			{
				if (!u.Ships.TryGetValue(sid, out var from) || !u.Ships.TryGetValue(tid, out var dst)) return;
				var why = CargoTransfer.Issue(u, from, dst, gidc);
				float moved = why is null ? CargoTransfer.Transfer(u, from, dst, gidc, q) : 0f;
				_xferOk = moved > 0f;
				_xferMsg = _xferOk ? $"{moved:0.#} {gname} umgeladen nach {ShipNames.Display(u, dst)}" : why ?? "Nichts umgeladen";
				_xferQty.Remove(gidc);
				Rebuild();
			};
			y += 64f;
		}
		if (!any) y = Line(p, y, w, "Fracht", "nichts an Bord zum Umladen", Dim);
		return y;
	}
}
