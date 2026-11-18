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
	public void ResetSession() { _tab = "inbox"; _sel = -1; _diplSel = 0; _detailOpen = false; _message = ""; _msgAt = 0; }
	/// <summary>Runde 42c (Handytest): beim Öffnen des Bereichs alte Meldung zurücksetzen (wie der Dock-Bereich „Aufträge“).</summary>
	protected override void OnOpen() { _message = ""; _msgAt = 0; }

	string _tab = "inbox";        // "inbox" | "diplomacy" | "missions"
	int _sel = -1;                // gewählte Nachricht (Id)
	int _diplSel;                 // gewählte Fraktion (Index in NpcFactionIds)
	bool _detailOpen;             // schmal: Detail statt Liste
	string _message = "";
	ulong _msgAt;                 // Runde 42c: Zeitpunkt der Meldung (Eigenuhr, test überschreibbar)
	/// <summary>Runde 42c: Uhr für die Meldungs-Lebensdauer (Selbsttest stellt um).</summary>
	internal static Func<ulong> MsgNow = () => (ulong)Time.GetTicksMsec();
	/// <summary>Runde 42c: Meldungen im Comms-Bereich verfallen nach dieser Zeit (ms) — sie dürfen nicht minutenlang stehen bleiben.</summary>
	const int MsgMs = 6000;
	/// <summary>Runde 42c: Meldung noch sichtbar (Text da und Frist nicht abgelaufen).</summary>
	public bool TestMessageVisible => _message.Length > 0 && MsgNow() - _msgAt < MsgMs;
	readonly KitConfirm _confirm = new();
	KitScroll? _detailScroll;   // Runde 41 (A3): Vorschauzeilen unter den Knöpfen schieben sie tiefer — Detail scrollt (Test-Hook)
	/// <summary>Runde 45, Selbsttest: Detail-Scroll des Diplomatie-Reiters (echte Tipps auf Knöpfe weiter unten).</summary>
	public KitScroll? TestDetailScroll => _detailScroll;

	// ── Test-Hooks ──
	/// <summary>Selbsttest: Zahl der Posteingang-Nachrichten.</summary>
	public int TestInboxCount => Ctx.U.Inbox.Count;
	/// <summary>Selbsttest: ungelesene Nachrichten.</summary>
	public int TestUnread => SpaceSim.Core.Comms.Comms.Unread(Ctx.U);
	/// <summary>Selbsttest: aktiver Reiter.</summary>
	public string TestTab => _tab;
	/// <summary>Selbsttest: Reiter setzen (Meldung wird dabei zurückgesetzt, wie beim Tippen).</summary>
	public void TestSetTab(string t) { _tab = t; _message = ""; _msgAt = 0; Invalidate(); }
	/// <summary>Selbsttest (Runde 42c): Meldung setzen, als hätte eine Aktion sie gemeldet.</summary>
	public void TestShowMessage(string msg) { _message = msg; _msgAt = MsgNow(); Invalidate(); }
	/// <summary>Selbsttest (Runde 42d): zuletzt gebaute Geldzeilen der aktiven Aufträge im Reiter „Aufträge“.</summary>
	public readonly System.Collections.Generic.List<string> TestMoneyLines = new();
	/// <summary>Runde 45, Selbsttest: zuletzt gezeigte Bündniszeile im Diplomatie-Detail.</summary>
	public string TestAllianceRow { get; private set; } = "";
	/// <summary>Selbsttest: Zahl der Fraktionszeilen im Diplomatie-Reiter.</summary>
	public int TestDiplomacyRows => Ctx.U.NpcFactionIds().Count;
	/// <summary>Selbsttest: Fraktion wählen (Index) und Detail öffnen.</summary>
	public void TestSelectFaction(int i) { _diplSel = i; _detailOpen = true; Invalidate(); }
	/// <summary>Selbsttest: zuletzt gemeldetes Aktionsergebnis.</summary>
	public string TestMessage => _message;
	/// <summary>Selbsttest: Zahl der aktiven Aufträge im Reiter „Aufträge“.</summary>
	public int TestMissionRows => Ctx.U.Missions.Count(m => m.State == SpaceSim.Core.Model.MissionState.Active);
	/// <summary>Selbsttest: Diplomatie-Detail ganz nach unten scrollen (Knöpfe mit Vorschauzeile liegen sonst unten abgeschnitten).</summary>
	public void TestScrollDetailEnd() { if (_detailScroll is { } sc) { sc.ScrollVertical = (int)sc.GetVScrollBar().MaxValue; Invalidate(); } }
	/// <summary>Selbsttest: Diplomatie-Detail schrittweise nach unten scrollen (bis zu einem límites).</summary>
	public void TestScrollDetailStep(int d) { if (_detailScroll is { } sc) sc.ScrollVertical = (int)Mathf.Min(sc.GetVScrollBar().MaxValue, Math.Max(0, sc.ScrollVertical + d)); }

	static string FactionNameOf(Universe u, FactionId f) => u.Factions.TryGetValue(f, out var x) ? SpaceSim.Core.Model.ActorNames.Tagged(u, f, x.Name) : $"?{f.Value}";

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		// ── Reiter ──
		float tx = 12;
		foreach (var (id, label) in new[] { ("inbox", "Posteingang"), ("diplomacy", "Diplomatie"), ("missions", "Aufträge") })
		{
			int unread = SpaceSim.Core.Comms.Comms.Unread(u);
			var b = At(inner, new KitButton(label, new Vector2(180, 44)) { FontSize = 17, State = _tab == id ? KitButtonState.On : KitButtonState.Normal }, tx, 12);
			if (id == "inbox" && unread > 0) { b.Detail = unread.ToString(); b.DetailColor = Orange; }
			if (id == "missions")
			{
				int act = u.Missions.Count(m => m.State == SpaceSim.Core.Model.MissionState.Active);
				if (act > 0) { b.Detail = act.ToString(); b.DetailColor = Cyan; }
			}
			string t = id;
			b.Pressed += () => { _tab = t; _detailOpen = false; _message = ""; _msgAt = 0; Invalidate(); };   // Runde 42c: Reiterwechsel setzt die Meldung zurück
			tx += 192;
		}
		// ── Meldungsfach (Runde 42c, Handytest: alte Meldung lief minutenlang über den Listentext) — eigenes Fach unter den
		// Reitern, die Liste beginnt erst darunter; Meldung verfällt nach MsgMs und wird bei Wechsel/Öffnen zurückgesetzt ──
		float top = 64f;
		if (TestMessageVisible)
		{
			float mh = WrappedText(inner, _message, 12, 58, InnerW(inner) - 24f, 14, Orange, 18) - 58f;
			top = 64f + MathF.Max(24f, mh + 6f);
		}
		if (_tab == "inbox") BuildInbox(inner, u, top);
		else if (_tab == "diplomacy") BuildDiplomacy(inner, u, top);
		else BuildMissions(inner, u, top);
	}

	// ───────── Posteingang ─────────

	void BuildInbox(Control inner, Universe u, float top)
	{
		var msgs = u.Inbox.OrderByDescending(m => m.Id).ToList();
		if (_sel < 0 || !msgs.Any(m => m.Id == _sel)) _sel = msgs.Count > 0 ? msgs[0].Id : -1;
		bool narrow = Narrow(inner);
		if (narrow && _detailOpen) { BuildInboxDetail(inner, u, msgs, narrow: true, top); return; }
		BuildInboxList(inner, u, msgs, narrow, top);
		if (!narrow) BuildInboxDetail(inner, u, msgs, narrow: false, top);
	}

	void BuildInboxList(Control inner, Universe u, System.Collections.Generic.List<CommsMessage> msgs, bool narrow, float top)
	{
		float lw = narrow ? InnerW(inner) - 24f : 380f;
		float lh = InnerH(inner) - top - (narrow ? 32f : 66f);
		var scroll = At(inner, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 12, top, lw, lh);
		var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
		list.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(list);
		if (msgs.Count == 0)
			At(inner, Text("Keine Nachrichten.", 15, Dim), 24, top + 16, lw - 20, 22);
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
		At(inner, Text(unread > 0 ? $"{unread} ungelesen" : "alles gelesen", 13, new Color(Dim, 0.85f)), 12, top + lh + 6, lw, 18);
	}

	void BuildInboxDetail(Control inner, Universe u, System.Collections.Generic.List<CommsMessage> msgs, bool narrow, float top)
	{
		var m = msgs.FirstOrDefault(x => x.Id == _sel);
		float dx = narrow ? 12f : 404f, dy = top;
		float dw = narrow ? InnerW(inner) - 24f : InnerW(inner) - dx - 12f;
		float dh = InnerH(inner) - dy - 12f;
		var card = CardBg(inner, dx, dy, dw, dh, Card);
		// Runde 42b: Inhalt der Karte scrollt, „Ignorieren“ bleibt feste Leiste unten (lange Texte liefen sonst über den Knopf)
		var scroll = At(card, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 0, 0, dw, dh - 64f);
		var content = new Control { MouseFilter = MouseFilterEnum.Ignore, Position = Vector2.Zero, Size = new Vector2(dw, dh - 64f) };
		scroll.AddChild(content);
		float y = dy + 12;
		if (narrow)
		{
			var back = At(inner, new KitButton("← Liste", new Vector2(150, 48)) { FontSize = 16 }, 12, top);
			back.Pressed += () => { _detailOpen = false; Invalidate(); };
			y = top + 56;
		}
		if (m is null) { At(content, Text("Keine Nachricht gewählt.", 15, Dim), 16, y - dy + 8, dw - 32, 22); return; }
		var (subject, body) = SpaceSim.Core.Comms.CommsText.Describe(u, m);
		string from = m.From.Value == 0 ? "System" : u.ActorName(m.From);
		At(content, Text(from, 20, White), 16, y - dy, dw - 32, 26); y += 28;
		At(content, Text(subject, 15, Cyan), 16, y - dy, dw - 32, 20); y += 24;
		y += WrappedText(content, body, 16, y - dy, dw - 32, 14, White, 18) - (y - dy) + 8;
		// Antworten mit Folgenvorschau (rein aus der Registry berechnet, Ausführung nur über die Registry)
		var answers = SpaceSim.Core.Comms.CommsText.Answers(u, m);
		if (answers.Count > 0)
		{
			At(content, Text("Deine Antwort", 15, Cyan), 16, y - dy, dw - 32, 20); y += 24;
			foreach (var key in answers)
			{
				var offer = Ctx.Comms.Preview(u, key);
				string label = key.StartsWith("war:", StringComparison.Ordinal) ? "Krieg erklären"
					: key.StartsWith("ceasefire:", StringComparison.Ordinal) ? "Waffenstillstand annehmen"
					: key.StartsWith("peace:", StringComparison.Ordinal) ? "Frieden annehmen"
					: key.StartsWith("mission:accept:", StringComparison.Ordinal) ? "Auftrag annehmen"
					: key.StartsWith("contract:haggle:", StringComparison.Ordinal) ? "Nachverhandeln (+5 %)" : key;
				string k = key;
				KitButton b;
				if (key.StartsWith("war:", StringComparison.Ordinal))
					b = _confirm.Wire(new KitButton(label, new Vector2(dw - 32, 56)) { FontSize = 16, AlignLeft = true }, "comms:" + key, label, () => Execute(k));
				else
					b = new KitButton(label, new Vector2(dw - 32, 56)) { FontSize = 16, AlignLeft = true };
				if (!offer.Possible) b.State = KitButtonState.Disabled;
				else b.Pressed += () => Execute(k);
				At(content, b, 16, y - dy);
				y += 62;
				// Runde 41 (A3): Folgen als umbrechende Vorschauzeile unter dem Knopf, nicht in der Knopfzeile (wurde abgeschnitten)
				string preview = offer.Possible ? offer.Preview : (offer.Reason ?? "nicht möglich");
				if (preview.Length > 0) y += WrappedText(content, preview, 22, y - dy, dw - 60, 12, offer.Possible ? new Color(UiKit.Dim, 0.95f) : Orange, 15) - (y - dy) + 8;
			}
		}
		content.Size = new Vector2(dw, MathF.Max(dh - 64f, y - dy + 8f));   // Inhalt so hoch wie nötig — die Karte scrollt
		// Ignorieren kostet nichts
		var ign = At(card, new KitButton("Ignorieren", new Vector2(160, 44)) { FontSize = 15 }, 16, dh - 56 + dy - dy);
		int mid = m.Id;
		ign.Pressed += () => { SpaceSim.Core.Comms.Comms.MarkRead(u, mid); _message = "Als gelesen markiert."; _msgAt = MsgNow(); if (narrow) _detailOpen = false; Invalidate(); };
	}

	// ───────── Aufträge (Runde 41) ─────────

	/// <summary>Reiter „Aufträge“: aktive Aufträge mit Abbrechen (Folge als Vorschauzeile darunter) und offene Comms-Angebote
	/// (Lieferverträge annehmen/nachverhandeln). Anzeige rein aus dem Core, Ausführung nur über die Comms-Registry.
	/// Runde 42c (Handytest): der Inhalt ist ein freies Control, kein VBox (der streckte die Knöpfe auf volle Breite und
	/// verzerrte die Zeilenabstände); Höhen kommen aus dem gemessenen Text (WrappedText-End-Y), jeder Knopf steht auf
	/// eigener Zeile unter seinem Text — nie über Listentext.</summary>
	void BuildMissions(Control inner, Universe u, float top)
	{
		TestMoneyLines.Clear();
		float w = InnerW(inner) - 24f;
		float h = InnerH(inner) - top - 12f;
		var scroll = At(inner, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 12, top, w, h);
		var box = new Control { MouseFilter = MouseFilterEnum.Ignore, Position = Vector2.Zero, Size = new Vector2(w, h) };
		scroll.AddChild(box);

		At(box, Text("Aktive Aufträge", 20, Cyan), 0, 0, w - 20, 28);
		float y = 34f;
		var active = u.Missions.Where(m => m.State == SpaceSim.Core.Model.MissionState.Active).OrderBy(m => m.Id).ToList();
		if (active.Count == 0) { At(box, Text("Keine laufenden Aufträge — Angebote hängen an Stationen (Dock → Aufträge) oder im Posteingang (Liefervertrag).", 14, new Color(Dim, 0.9f)), 0, y, w - 20, 40); y += 46f; }
		foreach (var m in active)
		{
			if (SpaceSim.Core.Missions.MissionRules.Types.Get(m.Type) is not { } type) continue;
			// Runde 42b/42c: Höhe aus dem gemessenen Inhalt — WrappedText liefert die End-Y, also Höhe = Ende − Anfang
			float mh = WrappedText(box, type.Describe(u, m), 0, y, w - 8, 15, Orange, 19) - y;
			// Runde 42d (Handytest): beim Liefervertrag „Festpreis … · Kasse reserviert“ statt „Belohnung 0 Cr (reserviert)“
			string money = $"{SpaceSim.Core.Missions.MissionRules.MoneyLine(u, m)} · Pfand {m.Deposit:0} Cr · Rest {SpaceSim.Core.Missions.MissionRules.Rest(Math.Max(0L, m.Deadline - u.Tick))}";
			TestMoneyLines.Add(money);
			float bh = WrappedText(box, money, 0, y + mh + 2, w - 8, 13, Dim, 17) - (y + mh + 2);
			y += mh + bh + 6f;
			var key = $"mission:abandon:{m.Id}";
			var offer = Ctx.Comms.Preview(u, key);
			var b = At(box, new KitButton("Abbrechen", new Vector2(200, 52)) { FontSize = 16, State = offer.Possible ? KitButtonState.Ready : KitButtonState.Disabled }, 0, y);
			string k = key;
			if (offer.Possible) b.Pressed += () => Execute(k);
			y += 58f;
			string folge = offer.Possible ? offer.Preview : (offer.Reason ?? "nicht möglich");
			if (folge.Length > 0) y += WrappedText(box, folge, 0, y, w - 8, 12, offer.Possible ? new Color(Dim, 0.95f) : Orange, 15) - y + 10;
		}

		// offene Comms-Angebote (Lieferverträge): annehmen / nachverhandeln
		y += 6f;
		At(box, Text("Offene Angebote (Comms)", 20, Cyan), 0, y, w - 20, 28);
		y += 34f;
		var offers = Ctx.Comms.Offers(u).Where(o => o.Key.StartsWith("mission:accept:", StringComparison.Ordinal) || o.Key.StartsWith("contract:haggle:", StringComparison.Ordinal)).ToList();
		if (offers.Count == 0) { At(box, Text("Keine offenen Angebote.", 14, new Color(Dim, 0.9f)), 0, y, w - 20, 22); y += 28f; }
		foreach (var o in offers)
		{
			float th = WrappedText(box, o.Text, 0, y, w - 8, 14, White, 18) - y;
			y += th + 2f;
			string prev = o.Possible ? o.Preview : (o.Reason ?? "nicht möglich");
			if (prev.Length > 0) y += WrappedText(box, prev, 0, y, w - 8, 12, o.Possible ? new Color(Dim, 0.95f) : Orange, 15) - y + 6f;
			string label = o.Key.StartsWith("mission:accept:", StringComparison.Ordinal) ? "Annehmen" : "Nachverhandeln (+5 %)";
			var b = At(box, new KitButton(label, new Vector2(230, 52)) { FontSize = 16, State = o.Possible ? KitButtonState.Ready : KitButtonState.Disabled }, 0, y);
			string k = o.Key;
			if (o.Possible) b.Pressed += () => Execute(k);
			y += 58f;
		}
		box.Size = new Vector2(w, MathF.Max(h, y + 8f));
	}

	// ───────── Diplomatie ─────────

	void BuildDiplomacy(Control inner, Universe u, float top)
	{
		var factions = u.NpcFactionIds().ToList();
		if (factions.Count == 0) { At(inner, Text("Keine KI-Fraktionen.", 15, Dim), 24, top + 16, 400, 22); return; }
		if (_diplSel >= factions.Count) _diplSel = 0;
		bool narrow = Narrow(inner);
		if (narrow && _detailOpen) { BuildDiplomacyDetail(inner, u, factions[_diplSel], narrow: true, top); return; }
		BuildDiplomacyList(inner, u, factions, narrow, top);
		if (!narrow) BuildDiplomacyDetail(inner, u, factions[_diplSel], narrow: false, top);
	}

	void BuildDiplomacyList(Control inner, Universe u, System.Collections.Generic.List<FactionId> factions, bool narrow, float top)
	{
		float lw = narrow ? InnerW(inner) - 24f : 380f;
		float lh = InnerH(inner) - top - 32f;
		var scroll = At(inner, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 12, top, lw, lh);
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

	void BuildDiplomacyDetail(Control inner, Universe u, FactionId f, bool narrow, float top)
	{
		float dx = narrow ? 12f : 404f, dy = top;
		float dw = narrow ? InnerW(inner) - 24f : InnerW(inner) - dx - 12f;
		float dh = InnerH(inner) - dy - 12f;
		var frame = CardBg(inner, dx, dy, dw, dh, Card);
		float bodyTop = 0f;
		if (narrow)
		{
			var back = At(inner, new KitButton("← Liste", new Vector2(150, 48)) { FontSize = 16 }, 12, top);
			back.Pressed += () => { _detailOpen = false; Invalidate(); };
			bodyTop = 56f;   // Inhalt erst unter der „← Liste“-Taste (Runde 40: Name/Relation lagen über der Taste)
		}
		var body = At(frame, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 0, bodyTop, dw, dh - 64 - bodyTop);
		_detailScroll = body;
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
			: Wars.CeasefireActive(u, war) ? $"Waffenstillstand noch {SpaceSim.Core.Missions.MissionRules.Rest(war.CeasefireUntil - u.Tick)}"
			: $"Krieg seit Tick {war.Since} (Müdigkeit {war.Weariness:0.00})";
		Row(det, 18, y, dw - 36, "Verhältnis", state, 14); y += 22;
		int wanted = Wanted.LevelOf(u, u.PlayerActor, f);
		float fine = u.Actors[u.PlayerActor].Wanted.TryGetValue(f, out var wr) ? Wanted.FineOf(u, wr) : 0f;
		Row(det, 18, y, dw - 36, "Fahndung", wanted > 0 ? $"{SecurityNames.WantedLevel(wanted)} — Strafe {fine:0} Cr" : "sauber", 14); y += 22;
		float claim = u.Actors[u.PlayerActor].BountyClaims.TryGetValue(f, out var cl) ? cl : 0f;
		if (claim > 0f) { Row(det, 18, y, dw - 36, "Kopfgeldanspruch", $"{claim:0} Cr (abholen im Kopfgeldbüro)", 14); y += 22; }
		// Runde 45: Bündnis (eigenes und ihre übrigen Verbündeten) und Tribut im Krieg
		bool allied = me is not null && Alliances.Allied(u, me.Value, f);
		var theirAllies = Alliances.AlliesOf(u, f).Where(x => me is null || x != me.Value).Select(x => FactionNameOf(u, x)).ToList();
		string allianceText = (allied ? $"mit dir seit {SpaceSim.Core.Comms.Comms.Ago(u, Alliances.Between(u, me!.Value, f)!.Since)}" : "nicht mit dir")
			+ (theirAllies.Count > 0 ? $" · verbündet mit {string.Join(", ", theirAllies)}" : "");
		Row(det, 18, y, dw - 36, "Bündnis", allianceText, 14); y += 22;
		TestAllianceRow = allianceText;
		if (war is not null && me is not null)
		{
			Row(det, 18, y, dw - 36, "Tribut", $"sie fordern {Wars.TributeFor(u, war, f):0} Cr · du kannst {Wars.DemandAmount(u, war, f):0} Cr fordern", 14); y += 22;
		}
		y += 6;
		At(det, Text("Aktionen (Vorschau echt, Ausführung über die Comms-Registry)", 14, Cyan), 18, y, dw - 36, 20); y += 26;

		// Knöpfe je Aktion; die Folgen stehen als umbrechende Vorschauzeile UNTER dem Knopf (Runde 41, A3: in der Knopf-Zeile wurde der
		// Text abgeschnitten — „…Kriegss…“), der Knopf selbst trägt nur Titel und Betrag
		void ActionButton(string key, string label, bool confirm)
		{
			var offer = Ctx.Comms.Preview(u, key);
			string k = key;
			string detail = offer.Cost < 0f ? $"{-offer.Cost:0} Cr zahlen" : "";
			KitButton b;
			if (confirm)
				b = _confirm.Wire(new KitButton(label, new Vector2(dw - 36, 56)) { FontSize = 16, AlignLeft = true, Detail = detail }, "comms:" + key, label, () => Execute(k));
			else
				b = new KitButton(label, new Vector2(dw - 36, 56)) { FontSize = 16, AlignLeft = true, Detail = detail };
			if (!offer.Possible) { b.State = KitButtonState.Disabled; }
			else if (!confirm) b.Pressed += () => Execute(k);
			At(det, b, 18, y);
			y += 62;
			string preview = offer.Possible ? offer.Preview : (offer.Reason ?? "nicht möglich");
			if (preview.Length > 0)
			{
				float ph = WrappedText(det, preview, 24, y, dw - 60, 12, offer.Possible ? new Color(UiKit.Dim, 0.95f) : Orange, 15);
				y += ph - y + 8;
			}
		}
		ActionButton($"fine:{f.Value}", "Strafe zahlen", confirm: false);
		ActionButton($"war:{f.Value}", "Krieg erklären", confirm: true);
		ActionButton($"ceasefire:{f.Value}", "Waffenstillstand", confirm: false);
		ActionButton($"peace:{f.Value}", "Frieden", confirm: false);
		int tributeAmount = war is not null ? Math.Max(1, (int)MathF.Ceiling(Wars.TributeFor(u, war, f))) : 1;
		ActionButton($"tribute:{f.Value}:{tributeAmount}", "Tribut zahlen", confirm: false);
		// Runde 45: Tribut fordern, Bündnis vorschlagen/verlassen, Beistand leisten (Ausführung nur über die Registry)
		if (war is not null) ActionButton($"demand:{f.Value}", "Tribut fordern", confirm: true);
		if (u.Rules.AlliancesActive)
		{
			if (allied) ActionButton($"alliance:leave:{f.Value}", "Bündnis verlassen", confirm: true);
			else ActionButton($"alliance:propose:{f.Value}", "Bündnis vorschlagen", confirm: false);
			foreach (var c in Alliances.Calls(u).Where(c => c.Ally == f).ToList())
				ActionButton($"alliance:aid:{c.Attacker.Value}", $"Beistand: Krieg gegen {FactionNameOf(u, c.Attacker)}", confirm: true);
		}
		det.CustomMinimumSize = new Vector2(dw - 16f, y + 8);
		// Runde 42c: die Meldung steht im eigenen Fach oben (BuildContent) — nie als Überlagerung über diesem Detail
	}

	/// <summary>Führt ein Comms-Angebot aus (nur über die Registry) und meldet das Ergebnis (Meldung mit Verfallszeit, Runde 42c).</summary>
	void Execute(string key)
	{
		var u = Ctx.U;
		var r = Ctx.Comms.Execute(u, key);
		_message = r.Message;
		_msgAt = MsgNow();
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
			// Runde 42d (Handytest): nur offene Angebote zeigen „verfällt in …“ (Restzeit); erledigte/angenommene/gescheiterte nicht
			UiKit.DrawFit(this, 2, font, new Vector2(14, 64), SpaceSim.Core.Comms.CommsText.RowMeta(_u, _m), 11, Size.X - 40f, new Color(Dim, 0.8f));
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
			string line2 = (war is null ? state : state) + (u.PlayerFaction is { } m3 && Alliances.Allied(u, m3, _f) ? " · Bündnis" : "") + (wanted > 0 ? $" · Fahndung {SecurityNames.WantedLevel(wanted)}" : "");
			UiKit.DrawFit(this, 2, font, new Vector2(14, 68), line2, 12, Size.X - 30f, stCol);
			if (_sel) UiKit.DrawText(this, 3, font, new Vector2(Size.X - 20f, 26), "▶", 14, Cyan);
		}
	}
}
