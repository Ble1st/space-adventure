using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Comms“ (Runde 40, echt): zwei Reiter — <b>Posteingang</b> (Nachrichten aus Simulationsereignissen,
/// <see cref="SpaceSim.Core.Comms.CommsSystem"/>; Antworten mit Folgenvorschau, Ausführung nur über die Aktionsregistry) und
/// <b>Diplomatie</b> (je KI-Fraktion: Beziehung, Krieg/Waffenstillstand, Fahndung + Strafe; Knöpfe Strafe zahlen / Krieg erklären
/// (zwei Taps, <see cref="KitConfirm"/>) / Waffenstillstand / Frieden / Tribut, jeweils mit Vorschau aus
/// <see cref="SpaceSim.Core.Comms.CommsActions"/>). Schmale Breite: Liste/Detail wie <see cref="FactionsScreen"/>.
/// Der Zähler ungelesener Nachrichten steht echt am Hamburger-Menü und an der Leiste der Zentrale.
/// </summary>
public sealed partial class CommsScreen : CommandScreen, ISessionScreen
{
	public override string Id => "cc.comms";
	/// <summary>Gerätetest: Auswahl, Reiter und Meldung bei neuer Sitzung zurücksetzen.</summary>
	public void ResetSession() { _tab = "inbox"; _sel = -1; _diplSel = 0; _detailOpen = false; _message = ""; }

	string _tab = "inbox";        // "inbox" | "diplomacy"
	int _sel = -1;                // gewählte Nachricht (Id)
	int _diplSel;                 // gewählte Fraktion (Index in NpcFactionIds)
	bool _detailOpen;             // schmal: Detail statt Liste
	string _message = "";
	readonly KitConfirm _confirm = new();

	// ── Test-Hooks ──
	/// <summary>Selbsttest: Zahl der Posteingang-Nachrichten.</summary>
	public int TestInboxCount => Ctx.U.Inbox.Count;
	/// <summary>Selbsttest: ungelesene Nachrichten.</summary>
	public int TestUnread => SpaceSim.Core.Comms.Comms.Unread(Ctx.U);
	/// <summary>Selbsttest: aktiver Reiter.</summary>
	public string TestTab => _tab;
	/// <summary>Selbsttest: Reiter setzen.</summary>
	public void TestSetTab(string t) { _tab = t; Invalidate(); }
	/// <summary>Selbsttest: Zahl der Fraktionszeilen im Diplomatie-Reiter.</summary>
	public int TestDiplomacyRows => Ctx.U.NpcFactionIds().Count;
	/// <summary>Selbsttest: Fraktion wählen (Index) und Detail öffnen.</summary>
	public void TestSelectFaction(int i) { _diplSel = i; _detailOpen = true; Invalidate(); }
	/// <summary>Selbsttest: zuletzt gemeldetes Aktionsergebnis.</summary>
	public string TestMessage => _message;

	static string FactionNameOf(Universe u, FactionId f) => u.Factions.TryGetValue(f, out var x) ? SpaceSim.Core.Model.ActorNames.Tagged(u, f, x.Name) : $"?{f.Value}";

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		// ── Reiter ──
		float tx = 12;
		foreach (var (id, label) in new[] { ("inbox", "Posteingang"), ("diplomacy", "Diplomatie") })
		{
			int unread = SpaceSim.Core.Comms.Comms.Unread(u);
			var b = At(inner, new KitButton(label, new Vector2(180, 44)) { FontSize = 17, State = _tab == id ? KitButtonState.On : KitButtonState.Normal }, tx, 12);
			if (id == "inbox" && unread > 0) { b.Detail = unread.ToString(); b.DetailColor = Orange; }
			string t = id;
			b.Pressed += () => { _tab = t; _detailOpen = false; Invalidate(); };
			tx += 192;
		}
		if (_tab == "inbox") BuildInbox(inner, u); else BuildDiplomacy(inner, u);
	}

	// ───────── Posteingang ─────────

	void BuildInbox(Control inner, Universe u)
	{
		var msgs = u.Inbox.OrderByDescending(m => m.Id).ToList();
		if (_sel < 0 || !msgs.Any(m => m.Id == _sel)) _sel = msgs.Count > 0 ? msgs[0].Id : -1;
		bool narrow = Narrow(inner);
		if (narrow && _detailOpen) { BuildInboxDetail(inner, u, msgs, narrow: true); return; }
		BuildInboxList(inner, u, msgs, narrow);
		if (!narrow) BuildInboxDetail(inner, u, msgs, narrow: false);
	}

	void BuildInboxList(Control inner, Universe u, System.Collections.Generic.List<CommsMessage> msgs, bool narrow)
	{
		float lw = narrow ? InnerW(inner) - 24f : 380f;
		float lh = narrow ? InnerH(inner) - 96f : InnerH(inner) - 130f;
		var scroll = At(inner, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 12, 64, lw, lh);
		var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
		list.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(list);
		if (msgs.Count == 0)
			At(inner, Text("Keine Nachrichten.", 15, Dim), 24, 80, lw - 20, 22);
		foreach (var m in msgs)
		{
			bool isSel = m.Id == _sel;
			var row = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
			row.AddThemeStyleboxOverride("panel", Box(isSel ? new Color(0.14f, 0.24f, 0.42f, 0.95f) : Card, isSel ? Cyan : null, 8, isSel ? 2 : 0, 10));
			row.CustomMinimumSize = new Vector2(lw - 16f, 74);
			int id = m.Id;
			UiKit.OnTap(row, () =>   // Runde 37: Wisch wählt nichts
			{
				_sel = id;
				SpaceSim.Core.Comms.Comms.MarkRead(u, id);
				if (narrow) _detailOpen = true;
				Invalidate();
			});
			row.AddChild(new InboxRow(u, m, isSel));
			list.AddChild(row);
		}
		int unread = SpaceSim.Core.Comms.Comms.Unread(u);
		At(inner, Text(unread > 0 ? $"{unread} ungelesen" : "alles gelesen", 13, new Color(Dim, 0.85f)), 12, 64 + lh + 6, lw, 18);
	}

	void BuildInboxDetail(Control inner, Universe u, System.Collections.Generic.List<CommsMessage> msgs, bool narrow)
	{
		var m = msgs.FirstOrDefault(x => x.Id == _sel);
		float dx = narrow ? 12f : 404f, dy = narrow ? 64f : 64f;
		float dw = narrow ? InnerW(inner) - 24f : InnerW(inner) - dx - 12f;
		float dh = narrow ? InnerH(inner) - dy - 12f : InnerH(inner) - dy - 12f;
		var card = CardBg(inner, dx, dy, dw, dh, Card);
		float y = dy + 12;
		if (narrow)
		{
			var back = At(inner, new KitButton("← Liste", new Vector2(150, 48)) { FontSize = 16 }, 12, 64);
			back.Pressed += () => { _detailOpen = false; Invalidate(); };
			y = 64 + 56;
		}
		if (m is null) { At(card, Text("Keine Nachricht gewählt.", 15, Dim), 16, y - dy + 8, dw - 32, 22); return; }
		var (subject, body) = SpaceSim.Core.Comms.CommsText.Describe(u, m);
		string from = m.From.Value == 0 ? "System" : u.ActorName(m.From);
		At(card, Text(from, 20, White), 16, y - dy, dw - 32, 26); y += 28;
		At(card, Text(subject, 15, Cyan), 16, y - dy, dw - 32, 20); y += 24;
		y += WrappedText(card, body, 16, y - dy, dw - 32, 14, White, 18) + 8;
		// Antworten mit Folgenvorschau (rein aus der Registry berechnet, Ausführung nur über die Registry)
		var answers = SpaceSim.Core.Comms.CommsText.Answers(u, m);
		if (answers.Count > 0)
		{
			At(card, Text("Deine Antwort", 15, Cyan), 16, y - dy, dw - 32, 20); y += 24;
			foreach (var key in answers)
			{
				var offer = Ctx.Comms.Preview(u, key);
				string label = key.StartsWith("war:", StringComparison.Ordinal) ? "Krieg erklären"
					: key.StartsWith("ceasefire:", StringComparison.Ordinal) ? "Waffenstillstand annehmen"
					: key.StartsWith("peace:", StringComparison.Ordinal) ? "Frieden annehmen" : key;
				string k = key;
				KitButton b;
				if (key.StartsWith("war:", StringComparison.Ordinal))
					b = _confirm.Wire(new KitButton(label, new Vector2(dw - 32, 64)) { FontSize = 16, AlignLeft = true }, "comms:" + key, label, () => Execute(k), Short(offer.Preview));
				else
					b = new KitButton(label, new Vector2(dw - 32, 64)) { FontSize = 16, AlignLeft = true, Sub = Short(offer.Preview) };
				if (!offer.Possible) { b.State = KitButtonState.Disabled; b.Detail = Short(offer.Reason ?? "nicht möglich"); }
				else b.Pressed += () => Execute(k);
				At(card, b, 16, y - dy);
				y += 70;
			}
		}
		// Ignorieren kostet nichts
		var ign = At(card, new KitButton("Ignorieren", new Vector2(160, 44)) { FontSize = 15 }, 16, dh - 56 + dy - dy);
		int mid = m.Id;
		ign.Pressed += () => { SpaceSim.Core.Comms.Comms.MarkRead(u, mid); _message = "Als gelesen markiert."; if (narrow) _detailOpen = false; Invalidate(); };
		if (_message.Length > 0)
			WrappedText(card, _message, 190, dh - 52, dw - 206, 13, Orange, 16);
	}

	// ───────── Diplomatie ─────────

	void BuildDiplomacy(Control inner, Universe u)
	{
		var factions = u.NpcFactionIds().ToList();
		if (factions.Count == 0) { At(inner, Text("Keine KI-Fraktionen.", 15, Dim), 24, 80, 400, 22); return; }
		if (_diplSel >= factions.Count) _diplSel = 0;
		bool narrow = Narrow(inner);
		if (narrow && _detailOpen) { BuildDiplomacyDetail(inner, u, factions[_diplSel], narrow: true); return; }
		BuildDiplomacyList(inner, u, factions, narrow);
		if (!narrow) BuildDiplomacyDetail(inner, u, factions[_diplSel], narrow: false);
	}

	void BuildDiplomacyList(Control inner, Universe u, System.Collections.Generic.List<FactionId> factions, bool narrow)
	{
		float lw = narrow ? InnerW(inner) - 24f : 380f;
		float lh = narrow ? InnerH(inner) - 96f : InnerH(inner) - 96f;
		var scroll = At(inner, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 12, 64, lw, lh);
		var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
		list.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(list);
		for (int i = 0; i < factions.Count; i++)
		{
			bool isSel = i == _diplSel;
			var row = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
			row.AddThemeStyleboxOverride("panel", Box(isSel ? new Color(0.14f, 0.24f, 0.42f, 0.95f) : Card, isSel ? Cyan : null, 8, isSel ? 2 : 0, 10));
			row.CustomMinimumSize = new Vector2(lw - 16f, 84);
			int idx = i;
			UiKit.OnTap(row, () => { _diplSel = idx; if (narrow) _detailOpen = true; Invalidate(); });
			row.AddChild(new FactionRow(u, factions[i], isSel));
			list.AddChild(row);
		}
	}

	void BuildDiplomacyDetail(Control inner, Universe u, FactionId f, bool narrow)
	{
		float dx = narrow ? 12f : 404f, dy = 64f;
		float dw = narrow ? InnerW(inner) - 24f : InnerW(inner) - dx - 12f;
		float dh = InnerH(inner) - dy - 12f;
		var frame = CardBg(inner, dx, dy, dw, dh, Card);
		float bodyTop = 0f;
		if (narrow)
		{
			var back = At(inner, new KitButton("← Liste", new Vector2(150, 48)) { FontSize = 16 }, 12, 64);
			back.Pressed += () => { _detailOpen = false; Invalidate(); };
			bodyTop = 56f;   // Inhalt erst unter der „← Liste“-Taste (Runde 40: Name/Relation lagen über der Taste)
		}
		var body = At(frame, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 0, bodyTop, dw, dh - 64 - bodyTop);
		var det = new Control { MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(dw - 16f, 10) };
		body.AddChild(det);

		var me = u.PlayerFaction;
		string name = FactionNameOf(u, f);
		At(det, Text(name, 22, White), 18, 10, dw - 36, 28);
		float rel = me is not null ? Relations.Effective(u, me.Value, f) : 0f;
		float y = 44;
		Row(det, 18, y, dw - 36, "Beziehung", $"{Relations.Label(rel)} ({(rel > 0 ? "+" : "")}{rel:0.00})", 14); y += 22;
		var war = me is not null ? Wars.Between(u, me.Value, f) : null;
		string state = war is null ? "Frieden"
			: Wars.CeasefireActive(u, war) ? $"Waffenstillstand bis Tick {war.CeasefireUntil} (noch {(war.CeasefireUntil - u.Tick) / 240} min)"
			: $"Krieg seit Tick {war.Since} (Müdigkeit {war.Weariness:0.00})";
		Row(det, 18, y, dw - 36, "Verhältnis", state, 14); y += 22;
		int wanted = Wanted.LevelOf(u, u.PlayerActor, f);
		float fine = u.Actors[u.PlayerActor].Wanted.TryGetValue(f, out var wr) ? Wanted.FineOf(u, wr) : 0f;
		Row(det, 18, y, dw - 36, "Fahndung", wanted > 0 ? $"{SecurityNames.WantedLevel(wanted)} — Strafe {fine:0} Cr" : "sauber", 14); y += 22;
		float claim = u.Actors[u.PlayerActor].BountyClaims.TryGetValue(f, out var cl) ? cl : 0f;
		if (claim > 0f) { Row(det, 18, y, dw - 36, "Kopfgeldanspruch", $"{claim:0} Cr (abholen im Kopfgeldbüro)", 14); y += 22; }
		y += 6;
		At(det, Text("Aktionen (Vorschau echt, Ausführung über die Comms-Registry)", 14, Cyan), 18, y, dw - 36, 20); y += 26;

		// Knöpfe je Aktion mit Vorschau; Krieg zweistufig (KitConfirm)
		void ActionButton(string key, string label, bool confirm)
		{
			var offer = Ctx.Comms.Preview(u, key);
			string k = key;
			KitButton b;
			if (confirm)
				b = _confirm.Wire(new KitButton(label, new Vector2(dw - 36, 66)) { FontSize = 16, AlignLeft = true }, "comms:" + key, label, () => Execute(k), Short(offer.Preview));
			else
				b = new KitButton(label, new Vector2(dw - 36, 66)) { FontSize = 16, AlignLeft = true, Sub = Short(offer.Preview) };
			if (!offer.Possible) { b.State = KitButtonState.Disabled; b.Detail = Short(offer.Reason ?? "nicht möglich"); b.Sub = ""; }
			else if (!confirm) b.Pressed += () => Execute(k);
			At(det, b, 18, y);
			y += 72;
		}
		ActionButton($"fine:{f.Value}", "Strafe zahlen", confirm: false);
		ActionButton($"war:{f.Value}", "Krieg erklären", confirm: true);
		ActionButton($"ceasefire:{f.Value}", "Waffenstillstand", confirm: false);
		ActionButton($"peace:{f.Value}", "Frieden", confirm: false);
		int tributeAmount = war is not null ? Math.Max(1, (int)MathF.Ceiling(Wars.TributeFor(u, war, f))) : 1;
		ActionButton($"tribute:{f.Value}:{tributeAmount}", "Tribut zahlen", confirm: false);
		det.CustomMinimumSize = new Vector2(dw - 16f, y + 8);
		if (_message.Length > 0)
			WrappedText(frame, _message, 16, dh - 52, dw - 32, 13, Orange, 16);
	}

	/// <summary>Kurztext für Unterzeilen (einzeilig).</summary>
	static string Short(string? s) => s is null ? "" : s.Replace('\n', ' ').Length > 110 ? s.Replace('\n', ' ')[..107] + "…" : s.Replace('\n', ' ');

	/// <summary>Führt ein Comms-Angebot aus (nur über die Registry) und meldet das Ergebnis.</summary>
	void Execute(string key)
	{
		var u = Ctx.U;
		var r = Ctx.Comms.Execute(u, key);
		_message = r.Message;
		Invalidate();
	}

	// ───────── Zeilen ─────────

	/// <summary>Eine Posteingangszeile: Absender, Betreff, Ungelesen-Punkt.</summary>
	sealed partial class InboxRow : Control
	{
		readonly Universe _u; readonly CommsMessage _m; readonly bool _sel;
		public InboxRow(Universe u, CommsMessage m, bool sel)
		{
			_u = u; _m = m; _sel = sel;
			MouseFilter = MouseFilterEnum.Ignore;
			CustomMinimumSize = new Vector2(340, 66);
			Size = CustomMinimumSize;
		}

		public override void _Draw()
		{
			var font = ThemeDB.FallbackFont;
			DrawnText.Clear(this);
			string from = _m.From.Value == 0 ? "System" : _u.ActorName(_m.From);
			var (subject, _) = SpaceSim.Core.Comms.CommsText.Describe(_u, _m);
			UiKit.DrawFit(this, 0, font, new Vector2(14, 24), from, 15, Size.X - 40f, White);
			UiKit.DrawFit(this, 1, font, new Vector2(14, 46), subject, 13, Size.X - 40f, Dim);
			UiKit.DrawFit(this, 2, font, new Vector2(14, 64), $"Tick {_m.Created}" + (_m.Expires > 0 ? $" · verfällt Tick {_m.Expires}" : ""), 11, Size.X - 40f, new Color(Dim, 0.8f));
			if (!_m.Read) DrawCircle(new Vector2(Size.X - 16f, 20), 6f, Orange);
		}
	}

	/// <summary>Eine Fraktionszeile im Diplomatie-Reiter: Name, Beziehung, Kriegs-/Fahndungszustand.</summary>
	sealed partial class FactionRow : Control
	{
		readonly Universe _u; readonly FactionId _f; readonly bool _sel;
		public FactionRow(Universe u, FactionId f, bool sel)
		{
			_u = u; _f = f; _sel = sel;
			MouseFilter = MouseFilterEnum.Ignore;
			CustomMinimumSize = new Vector2(340, 76);
			Size = CustomMinimumSize;
		}

		public override void _Draw()
		{
			var font = ThemeDB.FallbackFont;
			DrawnText.Clear(this);
			var u = _u;
			string name = u.Factions.TryGetValue(_f, out var fac) ? SpaceSim.Core.Model.ActorNames.Tagged(u, _f, fac.Name) : $"?{_f.Value}";
			float rel = u.PlayerFaction is { } me ? Relations.Effective(u, me, _f) : 0f;
			Color relCol = rel >= 0.15f ? Green : rel <= -0.15f ? Red : White;
			UiKit.DrawFit(this, 0, font, new Vector2(14, 26), name, 17, Size.X - 30f, White, minSize: 13);
			UiKit.DrawFit(this, 1, font, new Vector2(14, 48), $"Beziehung {(rel > 0 ? "+" : "")}{rel:0.00} · {Relations.Label(rel)}", 13, Size.X - 30f, relCol);
			var war = u.PlayerFaction is { } m2 ? Wars.Between(u, m2, _f) : null;
			int wanted = Wanted.LevelOf(u, u.PlayerActor, _f);
			string state = war is null ? "Frieden" : Wars.CeasefireActive(u, war) ? "Waffenstillstand" : "KRIEG";
			Color stCol = war is null ? Dim : Red;
			string line2 = (war is null ? state : state) + (wanted > 0 ? $" · Fahndung {SecurityNames.WantedLevel(wanted)}" : "");
			UiKit.DrawFit(this, 2, font, new Vector2(14, 68), line2, 12, Size.X - 30f, stCol);
			if (_sel) UiKit.DrawText(this, 3, font, new Vector2(Size.X - 20f, 26), "▶", 14, Cyan);
		}
	}
}
