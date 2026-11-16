using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Runde 41: Dock-Bereich „Aufträge“ (Dienst „missions“): oben die Angebote der Station mit Vorschau (Belohnung, Pfand, Frist, Ziel)
/// und „Annehmen“, darunter die aktiven Aufträge mit Fortschritt — Transport-Fracht an der Zielstation „Abliefern“, sonst „Abbrechen“
/// mit Folge (Pfand verfällt, Beziehung sinkt). Anzeige liest direkt aus dem Core (rein), ausgeführt wird ausschließlich über den
/// Dienst <see cref="MissionService"/> (<c>accept:&lt;id&gt;</c>, <c>abandon:&lt;id&gt;</c>, <c>deliver:&lt;id&gt;</c>).
/// </summary>
public sealed partial class MissionsScreen : DockAreaScreen
{
	public override string Id => "dock.missions";
	public MissionsScreen() => AreaId = DockAreas.Missions;
	protected override bool ShowShip => true;

	/// <summary>Wird nach jedem Execute ausgelöst (true = Erfolg) – Main spielt den Ton.</summary>
	public event Action<bool>? Executed;

	string _message = "";
	bool _msgOk = true;
	int _offers;
	readonly Dictionary<int, KitButton> _acceptButtons = new();   // Runde 42 (Selbsttest): Annehmen-Knopf je Angebots-Id

	/// <summary>Runde 42b (Handytest): alte Meldung („Auftrag angenommen …“) nicht beim erneuten Öffnen des Bereichs zeigen.</summary>
	protected override void OnOpen() { _message = ""; }

	SpaceSim.Core.Services.IStationService? Service() =>
		Ctx.Services.ForStation(Ctx.U, Ctx.Station, Ctx.Ship).FirstOrDefault(s => s.Key == MissionService.ServiceKey);

	static string StationName(Universe u, StationId id) => u.Stations.TryGetValue(id, out var s) ? s.Name : $"#{id.Value}";

	/// <summary>Ziel-Station eines Angebots (Transport → Zielstation, sonst die Angebots-Station).</summary>
	static Station TargetStationOf(Universe u, Station st, MissionOffer o) =>
		o.Type == TransportMission.KeyId && o.Args.TryGetValue("target", out var ts) && int.TryParse(ts, out var tv)
			&& u.Stations.TryGetValue(new StationId(tv), out var t) ? t : st;

	/// <summary>Vorschauzeile eines Angebots: Belohnung (bzw. Festpreis beim Vertrag), Pfand, eine Frist (bei Annahme, je Auftragsart), geschätzte Flugzeit, Ziel.</summary>
	static string Preview(Universe u, Station st, Ship ship, MissionOffer o)
	{
		var type = MissionRules.Types.Get(o.Type);
		var tgt = TargetStationOf(u, st, o);
		string ziel = type?.TargetText(u, o) ?? st.Name;   // Runde 42: Ziel je Art (Station, Schiff, Wegpunkte)
		long frist = type?.DeadlineOf(u, o, ship) ?? MissionRules.DeadlineFrom(u, ship, st) - u.Tick;
		string reward = o.Reward > 0f ? $"Belohnung {o.Reward:0} Cr" : "Ware zum Festpreis (Liefervertrag)";
		// Runde 42c: geschätzte reale Flugzeit des Spielers in der Vorschau (Fristen sind daraus abgeleitet)
		string flug = MissionRules.TripText(u, ship.Sector, ship.Position, tgt.Sector, tgt.DockPoint(ship.Id), ship);
		return $"{reward} · Pfand {o.Deposit:0} Cr · Frist {MissionRules.Rest(frist)} · Flug ~{flug} · Ziel {ziel}";
	}

	protected override void BuildContent(Control inner)
	{
		_acceptButtons.Clear();
		float w = inner.Size.X > 0 ? inner.Size.X : 670f;
		float tw = w - 160f;
		_offers = 0;
		var u = Ctx.U; var st = Ctx.Station; var ship = Ctx.Ship;
		WrappedText(inner, _message.Length > 0 ? _message : $"Aufträge von {st.Name}: annehmen, erfüllen, Belohnung aus der Fraktionskasse. Lieferverträge sichern Annahme und Kasse der Station.",
			0, 0, w, 14, _message.Length > 0 ? (_msgOk ? Green : Red) : new Color(Dim, 0.85f), 18);

		// ── Angebote der Station ──
		At(inner, Text("Angebote dieser Station", 20, Cyan), 0, 44, w, 28);
		float y = 76f;
		var allOffers = u.MissionOffers.Where(o => o.At == st.Id).OrderBy(o => o.Id).ToList();
		// Runde 42c (Handytest): nur annehmbare Angebote zeigen; gibt es keines annehmbare, alle mit Grund (statt stummem „nicht möglich“)
		var offers = allOffers.Where(o => MissionRules.AcceptIssue(u, o.Id, ship, remote: false) is null).ToList();
		if (offers.Count == 0 && allOffers.Count > 0) offers = allOffers;
		if (offers.Count == 0) { At(inner, Text("Gerade keine Angebote — in ein paar Minuten schaut die Station neu.", 15, new Color(Dim, 0.85f)), 0, y, w, 24); y += 32f; }
		foreach (var o in offers)
		{
			if (MissionRules.Types.Get(o.Type) is not { } type) continue;
			string? issue = MissionRules.AcceptIssue(u, o.Id, ship, remote: false);
			// Runde 42b (Handytest): Kartenhöhe aus dem gemessenen Inhalt — WrappedText liefert die End-Y, also Höhe = Ende − Anfang
			float th = WrappedText(inner, type.DescribeOffer(u, o), 0, y, tw, 15, White, 19) - y;   // Runde 41 (A3-Prüfung): Titel umbrechend, nicht abgeschnitten
			float ph = WrappedText(inner, Preview(u, st, ship, o) + (issue is not null ? $" · {issue}" : ""), 0, y + th + 2, tw, 13, issue is not null ? Red : Dim, 17) - (y + th + 2);
			var b = At(inner, new KitButton("Annehmen", new Vector2(148, 56)) { FontSize = 16, State = issue is null ? KitButtonState.Ready : KitButtonState.Disabled, Detail = issue is null ? "" : "nicht möglich" }, w - 150, y);
			string key = $"{MissionService.Accept}:{o.Id}";
			if (issue is null) b.Pressed += () => Execute(key);
			_acceptButtons[o.Id] = b;   // Runde 42: Testhilfe
			y += MathF.Max(52f, th + ph + 8f);
			_offers++;
		}

		// ── Aktive Aufträge ( Fortschritt, Frist; an dieser Station: abliefern/abbrechen ) ──
		y += 10f;
		At(inner, Text("Deine aktiven Aufträge", 20, Cyan), 0, y, w, 28);
		y += 32f;
		var active = u.Missions.Where(m => m.State == MissionState.Active).OrderBy(m => m.Id).ToList();
		if (active.Count == 0) { At(inner, Text("Keine laufenden Aufträge.", 15, new Color(Dim, 0.85f)), 0, y, w, 24); y += 32f; }
		foreach (var m in active)
		{
			if (MissionRules.Types.Get(m.Type) is not { } type) continue;
			bool here = m.At == st.Id;
			bool deliverHere = here && m.Type == TransportMission.KeyId
				&& m.Args.TryGetValue("target", out var ts) && int.TryParse(ts, out var tv) && tv == st.Id.Value;
			// Runde 42b: Höhe aus dem gemessenen Inhalt (WrappedText-End-Y minus Anfang)
			float mh = WrappedText(inner, type.Describe(u, m), 0, y, tw, 15, Orange, 19) - y;
			string folge = deliverHere
				? $"Fracht abliefern: Belohnung {m.Reserved:0} Cr + Pfand {m.Deposit:0} Cr zurück"
				: $"Abbrechen: Pfand {m.Deposit:0} Cr verfällt · Beziehung −{u.Rules.MissionRelationLoss:0.00} · {MissionRules.Rest(u.Rules.MissionBlockTicks)} keine neuen Aufträge";
			float fh = WrappedText(inner, folge, 0, y + mh + 2, tw, 13, Dim, 17) - (y + mh + 2);
			if (here)
			{
				string label = deliverHere ? "Abliefern" : "Abbrechen";
				var b = At(inner, new KitButton(label, new Vector2(148, 56)) { FontSize = 16, State = KitButtonState.Ready, Detail = deliverHere ? "" : "Pfand verfällt" }, w - 150, y);
				string key = $"{(deliverHere ? MissionService.Deliver : MissionService.Abandon)}:{m.Id}";
				b.Pressed += () => Execute(key);
			}
			y += MathF.Max(52f, mh + fh + 8f);
		}
	}

	/// <summary>Führt ein Angebot des Dienstes „missions“ aus und zeigt dessen Meldung.</summary>
	public ServiceResult Execute(string offer)
	{
		var svc = Service();
		var r = svc is null
			? new ServiceResult(false, 0f, 0f, "Aufträge an dieser Station nicht verfügbar.")
			: svc.Execute(Ctx.U, Ctx.Station, Ctx.Ship, new ServiceRequest(offer, default, 0f));
		_message = r.Message;
		_msgOk = r.Success;
		Executed?.Invoke(r.Success);
		Invalidate();
		return r;
	}

	// ── Zugriff für Selbsttest/Bildschirmfotos ──
	public int TestOfferCount => _offers;
	public string TestMessage => _message;
	/// <summary>Runde 42 (Selbsttest): Annehmen-Knopf des Angebots mit dieser Id (null = nicht in der Liste).</summary>
	public KitButton? TestAcceptButton(int offerId) => _acceptButtons.TryGetValue(offerId, out var b) && !b.IsQueuedForDeletion() ? b : null;
}
