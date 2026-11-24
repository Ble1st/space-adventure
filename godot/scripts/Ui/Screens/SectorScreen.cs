using System.Collections.Generic;
using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Sektor“ (UI.md Abschnitt 4): Draufsicht auf den gewählten Sektor (Standard: aktueller):
/// Stationen, Rohstoffgürtel (<see cref="ResourceField"/>), Gates mit Zielsektor, NPC-Schiffe als Symbole
/// (echt über <see cref="Materializer"/>) und der Spieler. Umschalter Universum/Sektor oben. Tippen auf
/// Station/Gate wählt sie und zeigt ihre Info rechts; Knöpfe „Autopilot hierhin“ (echt: nur im aktuellen
/// Sektor bzw. über Route – setzt das Autopilot-Ziel in Main) und „Info“.
/// </summary>
public sealed partial class SectorScreen : CommandScreen, ISessionScreen
{
	/// <summary>Runde 15: Torzeile der Karte (Selbsttest).</summary>
	public string TestGateLine { get; private set; } = "";
	/// <summary>Ausbau Punkt 8: Zustandszeile der gewählten Station (Selbsttest).</summary>
	public string TestWearLine { get; private set; } = "";
	public override string Id => "cc.sector";
	/// <summary>Gerätetest Phase 27: Auswahl und Meldung bei neuer Sitzung zurücksetzen.</summary>
	public void ResetSession() { SelectedSector = null; _selStation = null; _selGate = null; _showInfo = false; }

	/// <summary>Angezeigter Sektor (null = Spielersektor); setzt der Aufrufer vor dem Öffnen.</summary>
	/// <summary>Runde 17 (Handy-Test: veraltete Torkarte, „Autopilot hin“ flog zum zuvor gewählten Tor): ein anderer Sektor löscht die Station-/Torwahl.</summary>
	public SectorId? SelectedSector
	{
		get => _selectedSector;
		set { if (value != _selectedSector) { _selStation = null; _selGate = null; _showInfo = false; } _selectedSector = value; }
	}
	SectorId? _selectedSector;
	/// <summary>Runde 17: beim Öffnen ohne bewusste Wahl den Spielersektor zeigen (Station/Tor-Auswahl verworfen).</summary>
	public void ShowPlayerSector(SectorId player) { SelectedSector = player; _selStation = null; _selGate = null; _showInfo = false; }

	/// <summary>„Autopilot hierhin“: Ziel für Main (NavTarget + optionale Station zum Andocken).</summary>
	public event Action<NavTarget, StationId?>? AutopilotRequested;
	/// <summary>Runde 4: „Ziel wählen“ — Ziel setzen ohne Autopilot.</summary>
	public event Action<NavTarget, StationId?>? TargetRequested;
	/// <summary>Runde 16: „Ziel wählen“ bei einem gewählten Tor — dieselbe Torzielkarte wie beim Antippen (Hülle/Schild, „Tor angreifen“).</summary>
	public event Action<GateId>? GateTargetRequested;
	/// <summary>Test-Hook: „Ziel wählen“ auslösen.</summary>
	public void TestTargetHere()
	{
		if (_selGate is { } sg && Selected == Ctx.U.PlayerSector) { GateTargetRequested?.Invoke(sg); return; }   // Runde 16: wie die Taste
		if (CanAutopilot(Ctx.U, Selected, out var n, out var d)) TargetRequested?.Invoke(n, d);
	}
	/// <summary>Runde 16: Tor in der Sektoransicht wählen (wie Antippen auf der Karte).</summary>
	public void TestSelectGate(GateId g) { _selGate = g; _selStation = null; Invalidate(); }
	/// <summary>Runde 35b (Selbsttest): gewählte Station und Bildpunkt einer Station auf der Karte.</summary>
	public StationId? TestSelStation => _selStation;
	/// <summary>Runde 41 (Selbsttest): Zahl der Missionssymbole auf der Sektorkarte.</summary>
	public int TestMissionSymbols => _map is null ? 0 : _map.TestMissionSymbols;
	/// <summary>Universumsereignisse: Symbole auf der Sektorkarte (Selbsttest).</summary>
	public int TestEventSymbols => _map is null ? 0 : _map.TestEventSymbols;
	public Vector2? TestStationScreen(StationId id) => _map is not null && IsInstanceValid(_map) ? _map.GlobalOf(id) : null;

	SectorId Selected => SelectedSector ?? Ctx.U.PlayerSector;

	/// <summary>Gerätetest Phase 27: die Kopfzeile nennt den gewählten Sektor (vorher „aktuell: …“ auch bei fremder Auswahl).</summary>
	protected override string HeaderInfo(SpaceSim.Core.Model.Universe u, string playerSector) =>
		Selected != u.PlayerSector && u.Sectors.TryGetValue(Selected, out var s) ? $"{Title} · gewählt: {s.Name} · du: {playerSector}" : base.HeaderInfo(u, playerSector);

	StationId? _selStation;
	GateId? _selGate;
	bool _showInfo;

	SectorMapCanvas _map = null!;

	/// <summary>Test-Hook: Sektor wechseln.</summary>
	public void TestSelectSector(SectorId s) { SelectedSector = s; _selStation = null; _selGate = null; Invalidate(); }
	/// <summary>Test-Hook: Station antippen.</summary>
	public void TestSelectStation(StationId s) { _selStation = s; _selGate = null; Invalidate(); }
	/// <summary>Test-Hook: „Autopilot hierhin“ auslösen.</summary>
	public void TestAutopilotHere() => AutopilotHere();

	/// <summary>Runde 35: Kartenbreite = Innenbreite − Detailkarte (300) − Abstände; bei 1022 Innenbreite wie bisher 700.</summary>
	static float MapW(Control inner) => MathF.Max(300f, (inner.Size.X > 0 ? inner.Size.X : 1022f) - 322f);

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		var sel = Selected;

		// ── Oben: Umschalter Universum/Sektor ──
		// UI-Umbau: Universum/Sektor stehen als Unterreiter über dem Inhalt
		var s = u.Sectors[sel];
		At(inner, Text($"{s.Name} · {FactionName(u, s.Owner)}", 17, MapOverlay.FactionColor(s.Owner)), 12, 18, 500, 24);

		// ── Karte links ──
		_map = At(inner, new SectorMapCanvas { MouseFilter = MouseFilterEnum.Stop }, 0, 56, MapW(inner), inner.Size.Y - 56);   // Runde 35: Karte nutzt die gewonnene Fläche
		_map.Setup(this);
		_map.StationTapped += id => { _selStation = id; _selGate = null; _showInfo = false; Invalidate(); };
		_map.GateTapped += id => { _selGate = id; _selStation = null; Invalidate(); };

		// ── Info-Karte rechts ──
		BuildDetail(inner, sel);
		BuildRoute(inner);   // Mehrpunkt-Route: Taste „Route“ über der Detailkarte
	}

	void BuildDetail(Control inner, SectorId sel)
	{
		var u = Ctx.U;
		var s = u.Sectors[sel];
		// Runde 35b (Prüfung: „Autopilot hin“ in der zweiten Reihe angeschnitten): Karte in der Höhe der Fläche, Knöpfe als fester Fuß, Text darüber rollt
		float cardH = MathF.Max(inner.Size.Y - 56, 300f);
		var frame = CardBg(inner, MapW(inner) + 12, 56, 300, cardH, Card);
		const float footH = 2 * 54 + 10;
		var bodyScroll = At(frame, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 0, 4, 300, cardH - footH - 8);
		var card = new Control { MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(292, 10) };
		bodyScroll.AddChild(card);

		if (_selStation is { } sid && u.Stations.TryGetValue(sid, out var st))
		{
			string type = u.StationTypes.TryGetValue(st.TypeKey, out var t) ? t.Name : st.TypeKey;
			At(card, Text(st.Name, 21, Cyan), 16, 10, 268, 28);
			At(card, Text($"{type} · {SpaceSim.Core.Model.ActorNames.Tagged(u, st.Owner)}", 14, Dim), 16, 40, 268, 20);   // zweiter Gerätetest Phase 28: ohne Sektor (steht in der Kopfzeile), sonst gekürzt
			At(card, Text($"Kasse {st.Credits:0} Cr · {st.Market.Count} Waren am Markt", 14, White), 16, 66, 268, 20);
			// Ausbau Punkt 8: Zustand bei Verschleiß und bei eigenen Stationen (eine Zeile, die übrigen rücken nach unten)
			float wy = 0f;
			if ((st.Integrity < 1f || st.Owner == u.PlayerActor) && StationWear.Text(u, st) is { } wearLine)
			{
				TestWearLine = wearLine;
				var wl = Text(wearLine, 13, st.Integrity < u.Rules.WearStopBelow ? Red : st.Integrity < u.Rules.WearSlowBelow ? Orange : White);
				wl.ClipText = true; wl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
				At(card, wl, 16, 90, 268, 18);
				wy = 22f;
			}
			else TestWearLine = "";
			if (ConstructionInfo.Of(u, st) is { } site)
			{
				// Phase 8: Baustelle — Zieltyp, Status und Material (echt aus ConstructionInfo)
				At(card, Text($"Baut: {site.TypeName} · {100f * site.Progress:0} %", 15, Orange), 16, 94 + wy, 268, 20);
				At(card, Text(site.State, 13, White), 16, 116 + wy, 268, 18);
				At(card, Text($"Bauschiff: {site.Builder}", 13, Dim), 16, 136 + wy, 268, 18);
				float sy = 160 + wy;
				foreach (var l in site.Lines)
				{
					At(card, Text($"{l.Name}  {l.Delivered:0}/{l.Required:0}", 13, l.Delivered >= l.Required ? Green : White), 24, sy, 268, 18);
					sy += 19;
				}
			}
			else if (_showInfo)
			{
				// „Info“: Marktübersicht der Station (echt aus Station.Market)
				At(card, Text("Markt (Auswahl)", 14, Dim), 16, 94 + wy, 268, 18);
				float my = 116 + wy;
				foreach (var (gid, e) in st.Market.OrderBy(kv => kv.Key.Value).Take(9))
				{
					At(card, Text($"{GoodName(u, gid)}  {e.Amount:0}/{e.Target:0} · {e.Price:0.0} Cr", 13, White), 24, my, 268, 18);
					my += 19;
				}
			}
			else
			{
				At(card, Text("Tippe „Info“ für die Marktübersicht.", 13, new Color(Dim, 0.75f)), 16, 94 + wy, 268, 18);
			}
		}
		else if (_selGate is { } gid && u.Gates.TryGetValue(gid, out var g))
		{
			var target = u.Sectors[u.Gates[g.Target].Sector];
			At(card, Text($"Gate → {target.Name}", 21, Cyan), 16, 10, 268, 28);
			// Phase 10: Zustand und Urteil des Kontrollpunkts für dich (echt aus AccessInfo)
			var gp = AccessInfo.ForGate(u, g, u.Ships[u.PlayerShip]);
			Color gc = gp.Denied || !g.Open ? Red : gp.Toll > 0f ? Orange : Green;
			At(card, Text($"Zustand: {gp.State}", 15, gc), 16, 42, 268, 20);
			WrappedText(card, gp.Verdict, 16, 66, 268, 13, gc, 16);
			At(card, Text($"Sprung nach {target.Name.Replace("Sektor ", "")}", 14, White), 16, 104, 268, 20);   // Runde 17: kurz (vorher „Sprung von Sektor Beta nach Sektor D…“)
			// Runde 15: Torkampf — Hülle/Schild/Zustand (Core: GateInfo)
			if (SpaceSim.Core.Combat.GateCombat.TypeOf(u, g) is not null)
			{
				TestGateLine = SpaceSim.Core.Combat.GateInfo.Short(u, g);
				WrappedText(card, TestGateLine, 16, 128, 268, 13, SpaceSim.Core.Combat.GateCombat.IsDown(u, g) ? Red : White, 16);
				At(card, Text("„Autopilot hierhin“ fliegt zum Gate.", 13, new Color(Dim, 0.75f)), 16, 166, 268, 18);
			}
			else At(card, Text("„Autopilot hierhin“ fliegt zum Gate.", 13, new Color(Dim, 0.75f)), 16, 128, 268, 18);
		}
		else
		{
			int npcs = UniverseScreen.ShipCountOf(u, sel);
			At(card, Text(s.Name, 21, Cyan), 16, 10, 268, 28);
			// Phase 14: Planet des Sektors (Kulisse der 3D-Ansicht, Abnehmer der Exportsenke)
			TestPlanet = s.Planet?.Name ?? "";
			var pol = AccessInfo.ForSector(u, sel, u.Ships[u.PlayerShip]);
			At(card, Text($"Besitzer: {pol.Owner} · {pol.Stage}", 14, MapOverlay.FactionColor(s.Owner)), 16, 40, 268, 20);
			// Ausbau Punkt 16: nur Bekanntes zählen; unerkundet sagt es
			int kst = s.Stations.Count(id => Knowledge.KnowsStation(u, id)), kfl = Enumerable.Range(0, s.Fields.Count).Count(k => Knowledge.KnowsField(u, sel, k)), kgt = s.Gates.Count(x => Knowledge.KnowsGate(u, x));
			int unk = Knowledge.UnknownIn(u, sel);
			TestKnownLine = !Knowledge.Visited(u, sel) ? "Unerkundet — Inhalt unbekannt" : unk > 0 ? $"{unk} unbekannt — Erkunden deckt auf" : "";
			if (s.Planet is { } planet) At(card, Text($"Planet {planet.Name} · {kst} Stationen · {kfl} Gürtel", 13, White), 16, 64, 268, 20);
			else At(card, Text($"{kst} Stationen · {kfl} Gürtel · {kgt} Gate{(kgt == 1 ? "" : "s")}", 14, White), 16, 64, 268, 20);
			At(card, Text($"{npcs} fremde Schiffe im Sektor", 14, White), 16, 86, 268, 20);
			// Phase 10: Sperrstufe statt Platzhalter-Sicherheit
			Color lc = pol.PlayerDenied ? Red : pol.EntryLevel > 0 ? Orange : Green;
			float py = WrappedText(card, $"Sperre: {pol.Lock}", 16, 110, 268, 13, lc, 16);
			py = WrappedText(card, $"Für dich: {pol.ForPlayer}", 16, py, 268, 13, lc, 16);
			// Phase 11: Lagekarte (echt)
			var sec = SecurityInfo.ForSector(u, sel, u.Ships[u.PlayerShip]);
			py = WrappedText(card, $"Lage: {sec.Summary} · {sec.Patrols} Streife{(sec.Patrols == 1 ? "" : "n")}", 16, py + 2, 268, 13, sec.Threat >= 1f ? Orange : new Color(Dim, 0.95f), 16);
			// Phase 24: laufende Gefechte (echt aus CombatInfo)
			var fights = SpaceSim.Core.Combat.CombatInfo.InSector(u, sel);
			TestCombat = fights.Count == 0 ? "" : $"Gefecht: {fights[0].Title} · {fights[0].Detail}" + (fights.Count > 1 ? $" (+{fights.Count - 1})" : "");
			if (TestCombat.Length > 0) py = WrappedText(card, TestCombat, 16, py + 2, 268, 13, Red, 16);
			// Phase 27: Verbände, Geleit, Verfolgung im Sektor (echt)
			var links = SpaceSim.Core.Combat.FleetInfo.Links(u, sel);
			int squads = links.Where(l => l.Kind is "squadron" or "pack").Select(l => l.To).Distinct().Count();
			int escorts = links.Count(l => l.Kind == "escort"), chases = links.Count(l => l.Kind == "pursuit");
			TestFleet = squads + escorts + chases == 0 ? "" : $"Flotten: {squads} Verb{(squads == 1 ? "and" : "ände")} · {escorts} Geleit · {chases} Verfolgung";
			if (TestFleet.Length > 0) py = WrappedText(card, TestFleet, 16, py + 2, 268, 13, Cyan, 16);
			if (TestKnownLine.Length > 0) py = WrappedText(card, TestKnownLine, 16, py + 2, 268, 13, Orange, 16);   // Ausbau Punkt 16
			At(card, Text("Tippe eine Station oder ein Gate an.", 13, new Color(Dim, 0.75f)), 16, py + 8, 268, 18);
		}

		// Knöpfe unten: „Autopilot hierhin“ (echt, nur aktueller Sektor bzw. über Route) und „Info“
		// Runde 4: „Ziel wählen“ (ohne loszufliegen) neben „Autopilot“
		card.CustomMinimumSize = new Vector2(292, UiKit.ContentBottom(card) + 8);
		float fy = cardH - footH + 2;
		var bTarget = At(frame, new KitButton("Ziel wählen", new Vector2(130, 50)) { FontSize = 16 }, 16, fy);
		var bAuto = At(frame, new KitButton("Autopilot hin", new Vector2(130, 50)) { FontSize = 16 }, 154, fy);
		if (CanAutopilot(u, sel, out var nav, out var dock))
		{
			bAuto.Pressed += () => { if (CanAutopilot(u, sel, out var n2, out var d2)) AutopilotRequested?.Invoke(n2, d2); };
			bTarget.Pressed += () =>
			{
				if (_selGate is { } sg && sel == u.PlayerSector) { GateTargetRequested?.Invoke(sg); return; }   // Runde 16
				if (CanAutopilot(u, sel, out var n2, out var d2)) TargetRequested?.Invoke(n2, d2);
			};
		}
		else
		{
			string why = sel == u.PlayerSector ? "Ziel antippen" : "kein Weg";
			bAuto.State = KitButtonState.Disabled; bAuto.Sub = why;
			bTarget.State = KitButtonState.Disabled; bTarget.Sub = why;
		}
		var bInfo = At(frame, new KitButton("Info", new Vector2(130, 50)) { FontSize = 17 }, 16, fy + 54);
		if (_selStation != null) bInfo.Pressed += () => { _showInfo = !_showInfo; Invalidate(); };
		else { bInfo.State = KitButtonState.Disabled; bInfo.Detail = "Station antippen"; }
		var bBack = At(frame, new KitButton("← Universum", new Vector2(130, 50)) { FontSize = 17 }, 154, fy + 54);
		bBack.Pressed += () => RaiseAreaSelected(CommandAreas.Universe);
	}

	/// <summary>Phase 10: Mobilitätsprofil des Spielerschiffs (Routen respektieren Sperren).</summary>
	static MobilityProfile PlayerProfile(Universe u) => GateAccess.ProfileOf(u, u.Ships[u.PlayerShip]);

	/// <summary>Ist „Autopilot hierhin“ möglich und wohin? Echt über <see cref="Navigator"/>/<see cref="Universe.FindRoute"/> mit deinem Profil.</summary>
	bool CanAutopilot(Universe u, SectorId sel, out NavTarget nav, out StationId? dock)
	{
		nav = default; dock = null;
		if (!Knowledge.KnowsSector(u, sel)) return false;   // Ausbau Punkt 16: kein Autopilot zu Unbekanntem
		if (_selStation is { } sid && u.Stations.TryGetValue(sid, out var st))
		{
			if (st.Sector != u.PlayerSector) { if (!u.IsReachable(u.PlayerSector, st.Sector, PlayerProfile(u))) return false; nav = Navigator.ForSector(u, st.Sector, u.PlayerShip); return true; }
			nav = Navigator.ForStation(u, sid, u.PlayerShip); dock = sid; return true;
		}
		if (_selGate is { } gid && u.Gates.TryGetValue(gid, out var g))
		{
			var target = u.Gates[g.Target].Sector;
			if (sel != u.PlayerSector && !u.IsReachable(u.PlayerSector, sel, PlayerProfile(u))) return false;
			nav = Navigator.ForSector(u, target, u.PlayerShip); return true;
		}
		if (sel == u.PlayerSector) return false;   // nichts angetippt
		if (!u.IsReachable(u.PlayerSector, sel, PlayerProfile(u))) return false;
		nav = Navigator.ForSector(u, sel, u.PlayerShip); return true;
	}

	void AutopilotHere()
	{
		if (CanAutopilot(Ctx.U, Selected, out var nav, out var dock)) AutopilotRequested?.Invoke(nav, dock);
	}

	/// <summary>Draufsicht eines Sektors: Stationen, Gürtel, Gates mit Ziel, NPC-Schiffe (Materializer), Spieler.</summary>
	sealed partial class SectorMapCanvas : Control
	{
		/// <summary>Runde 41 (Selbsttest): Zahl der gezeichneten Missionssymbole.</summary>
		public int TestMissionSymbols;
		public int TestEventSymbols;
		SectorScreen _owner = null!;
		Universe _u = null!;
		public event Action<StationId>? StationTapped;
		public event Action<GateId>? GateTapped;

		public void Setup(SectorScreen owner)
		{
			_owner = owner;
			_u = owner.Ctx.U;
		}

		/// <summary>Runde 35b (Selbsttest): Bildpunkt einer Station auf der Karte (wie der Tipp sie sucht).</summary>
		public Vector2 GlobalOf(StationId id) => GetGlobalTransform() * (Spots().TryGetValue(id, out var sp) ? sp : Place(_u.Stations[id].Position));

		/// <summary>Halbe Symbolgröße einer Station auf der Karte (Quadrat nach Größenklasse).</summary>
		static float SymbolHalf(Station st) => !st.Dockable ? 7f : Mathf.Clamp(st.Radius * 0.6f, 6f, 16f);

		/// <summary>
		/// Runde 35c (Prüfung: Stationsquadrate lagen bei 1280 × 720 × 1,3 in der Mitte übereinander): Anzeigelage der Stationssymbole — von der echten Lage aus
		/// so lange paarweise auseinandergeschoben, bis sich keine zwei Symbole berühren (feste Reihenfolge nach Id, keine Zufallszahl, reine Anzeige).
		/// Tipp und Beschriftung nutzen dieselbe Lage; eine dünne Linie verbindet ein verschobenes Symbol mit seinem echten Ort.
		/// </summary>
		System.Collections.Generic.Dictionary<StationId, Vector2> Spots()
		{
			var s = _u.Sectors[_owner.Selected];
			var ids = s.Stations.Where(id => Knowledge.KnowsStation(_u, id)).OrderBy(x => x.Value).ToList();   // Ausbau Punkt 16: nur bekannte
			var pos = ids.Select(id => Place(_u.Stations[id].Position)).ToArray();
			var half = ids.Select(id => SymbolHalf(_u.Stations[id])).ToArray();
			for (int it = 0; it < 40; it++)
			{
				bool moved = false;
				for (int i = 0; i < ids.Count; i++)
					for (int j = i + 1; j < ids.Count; j++)
					{
						float need = (half[i] + half[j]) * 1.42f + 10f;   // Quadrate: Diagonale; Runde 37c: mehr Luft (Handytest r37b: Stahlwerk/Panzerwerk eng)
						var d = pos[j] - pos[i];
						float len = d.Length();
						if (len >= need) continue;
						var dir = len > 0.01f ? d / len : new Vector2(MathF.Cos(i + j), MathF.Sin(i + j));
						var push = dir * ((need - len) / 2f + 0.5f);
						pos[i] -= push; pos[j] += push; moved = true;
					}
				// Tore bleiben, wo sie sind (Kreis 13): Stationssymbole weichen ihnen aus
				foreach (var gid in s.Gates.Where(x => Knowledge.KnowsGate(_u, x)))
				{
					var gp = Place(_u.Gates[gid].Position);
					for (int i = 0; i < ids.Count; i++)
					{
						float need = half[i] * 1.42f + 13f + 4f;
						var d = pos[i] - gp;
						float len = d.Length();
						if (len >= need) continue;
						var dir = len > 0.01f ? d / len : new Vector2(1f, 0f);
						pos[i] += dir * (need - len + 0.5f); moved = true;
					}
				}
				if (!moved) break;
			}
			var map = new System.Collections.Generic.Dictionary<StationId, Vector2>();
			for (int i = 0; i < ids.Count; i++) map[ids[i]] = pos[i];
			return map;
		}

		Vector2 Place(Vec2 world) { var f = Fit(); return f.Origin + new Vector2(world.X * f.Scale * f.Stretch, world.Y * f.Scale); }

		/// <summary>
		/// Runde 37 (Handytest r35 n12/n13: der Kreis nutzte nur einen kleinen Teil der Fläche, viele Stationen ohne Namen): Maßstab und Mitte aus dem
		/// Begrenzungsrechteck des Inhalts (Stationen, Tore, Gürtel) — es füllt die Kartenfläche (Rand für Beschriftungen seitlich 70, oben/unten 34), statt
		/// einen Kreis von mindestens 260 Einheiten in das kleinere Maß zu legen. Der Spieler bestimmt den Maßstab nicht (er steht notfalls am Rand).
		/// </summary>
		(float Scale, Vector2 Origin, float Stretch) Fit()
		{
			var s = _u.Sectors[_owner.Selected];
			var key = (Size, s.Id, s.Stations.Count, s.Fields.Count + 1000 * (Knowledge.Of(_u)?.Count ?? 0));   // Ausbau Punkt 16: neu Entdecktes = neu einpassen   // Lagen von Stationen/Toren/Gürteln ändern sich nicht — je Größe und Sektor einmal
			if (_fitKey == key) return _fit;
			_fitKey = key;
			return _fit = ComputeFit(s);
		}
		(Vector2, SectorId, int, int)? _fitKey;
		(float Scale, Vector2 Origin, float Stretch) _fit;

		/// <summary>Runde 37: höchste waagerechte Streckung der Draufsicht (breite Fläche auf dem Handy; Kreise bleiben Kreise, nur die Lagen rücken auseinander).</summary>
		internal const float MaxStretch = 2.5f;

		(float Scale, Vector2 Origin, float Stretch) ComputeFit(Sector s)
		{
			float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
			void Add(float x, float y, float r) { minX = MathF.Min(minX, x - r); maxX = MathF.Max(maxX, x + r); minY = MathF.Min(minY, y - r); maxY = MathF.Max(maxY, y + r); }
			foreach (var sid in s.Stations.Where(id => Knowledge.KnowsStation(_u, id))) Add(_u.Stations[sid].Position.X, _u.Stations[sid].Position.Y, 10f);
			foreach (var f in s.Fields.Where((_, k) => Knowledge.KnowsField(_u, s.Id, k))) Add(f.Position.X, f.Position.Y, SpaceSim.Core.Mining.MiningRules.BeltRadius);
			foreach (var gid in s.Gates.Where(x => Knowledge.KnowsGate(_u, x))) Add(_u.Gates[gid].Position.X, _u.Gates[gid].Position.Y, 10f);
			if (minX > maxX) { minX = minY = -150f; maxX = maxY = 150f; }
			// mindestens 160 Einheiten je Richtung (ein Sektor mit zwei nahen Stationen wird nicht riesig)
			float cx = (minX + maxX) / 2f, cy = (minY + maxY) / 2f;
			float bw = MathF.Max(160f, maxX - minX), bh = MathF.Max(160f, maxY - minY);
			float w = Size.X, h = Size.Y - LegendHeight;
			float scale = MathF.Max(0.05f, MathF.Min((w - 140f) / bw, (h - 68f) / bh));
			// Runde 37 (Handytest r35: die Draufsicht nutzte auf dem breiten Handy nur die Mitte): ist die Fläche breiter als der Inhalt, rücken die Lagen
			// waagerecht auseinander — Symbole und Beschriftungen bekommen Platz, die Anordnung bleibt erkennbar
			// Runde 37b (Handytest r37 f03: noch immer nur die halbe Fläche): Maßstab je Achse nach dem Inhalt, waagerecht bis 2,5-fach
			float stretch = Mathf.Clamp((w - 140f) / (bw * scale), 1f, MaxStretch);
			return (scale, new Vector2(w / 2f - cx * scale * stretch, h / 2f - cy * scale), stretch);
		}
		float ContentScale() => Fit().Scale;
		Vector2 MapOrigin => Fit().Origin;
		// Runde 21 (Handy-Test b04: die Legende überlappte den Kreisrand): die Karte liegt über einem eigenen Legendenstreifen
		internal const float LegendHeight = 60f;
		Rect2 MapRect => new(Vector2.Zero, new Vector2(Size.X, Size.Y - LegendHeight));
		/// <summary>Runde 21/37: unterster Punkt des gezeichneten Sektorrands (nie in den Legendenstreifen) und Oberkante der Legende (Selbsttest).</summary>
		float Stretch => Fit().Stretch;
		internal (float CircleBottom, float LegendTop) TestLegendGap => (MathF.Min(MapOrigin.Y + (_u?.Sectors[_owner.Selected].Radius ?? 0f) * ContentScale(), MapRect.End.Y - 2f) + 1f, Size.Y - 44f - 13f);

		/// <summary>Runde 44: Maßstabsleiste unten links in der Kartenfläche (waagrechter Maßstab, also mit der Streckung) — „500 m“, „1 km“.</summary>
		void DrawScaleBar(Font font, SpaceAdventure.Ui.Kit.LabelLayout layout, float pxPerMeter)
		{
			float len = SpaceSim.Core.Flight.DistanceText.NiceLength(pxPerMeter, 150f);
			if (len <= 0f) return;
			float px = len * pxPerMeter;
			var a = new Vector2(16f, MapRect.End.Y - 14f);
			var b = a + new Vector2(px, 0f);
			var col = new Color(White, 0.8f);
			DrawLine(a, b, col, 2f);
			DrawLine(a + new Vector2(0, -5), a + new Vector2(0, 5), col, 2f);
			DrawLine(b + new Vector2(0, -5), b + new Vector2(0, 5), col, 2f);
			string text = SpaceSim.Core.Flight.DistanceText.Format(len);
			SpaceAdventure.Ui.Kit.UiKit.DrawFit(this, 903, font, new Vector2(b.X + 8f, a.Y + 5f), text, 13, 90f, col);
			ScaleBarText = text;
			ScaleBarPixels = px;
			layout.Reserve(new Rect2(a - new Vector2(4f, 14f), new Vector2(px + 110f, 24f)));
		}
		/// <summary>Runde 44 (Selbsttest/ui-check): Beschriftung und Länge der Maßstabsleiste.</summary>
		internal string ScaleBarText = "";
		internal float ScaleBarPixels;

		/// <summary>Runde 37: Kreis nur so weit zeichnen, wie er in der Kartenfläche liegt (der Sektorrand ist oft größer als die Fläche).</summary>
		void DrawClippedCircle(Vector2 c, float r, Color col, float width, float sx = 1f)
		{
			var rect = MapRect.Grow(-2f);
			var pts = new List<Vector2>();
			const int n = 128;
			for (int i = 0; i <= n; i++)
			{
				var p = c + new Vector2(MathF.Cos(i * Mathf.Tau / n) * sx, MathF.Sin(i * Mathf.Tau / n)) * r;
				if (rect.HasPoint(p)) pts.Add(p);
				else { if (pts.Count >= 2) DrawPolyline(pts.ToArray(), col, width, true); pts.Clear(); }
			}
			if (pts.Count >= 2) DrawPolyline(pts.ToArray(), col, width, true);
		}

		public override void _GuiInput(InputEvent @event)
		{
			if (!Pointer.TryRead(@event, out _, out var pos, out bool pressed, out bool motion)) return;
			// Mehrpunkt-Route: langes Drücken öffnet das Kartenmenü; das Loslassen danach ist kein Tipp
			if (_owner._lp.Input(pressed, motion, pos)) { AcceptEvent(); return; }
			if (motion || pressed) return;
			var s = _u.Sectors[_owner.Selected];
			StationId? st = null; float sd = 24f;
			var spots = Spots();
			foreach (var sid in spots.Keys)
			{
				float d = spots[sid].DistanceTo(pos);
				if (d < sd) { sd = d; st = sid; }
			}
			if (st is { } station) { StationTapped?.Invoke(station); AcceptEvent(); return; }
			GateId? gate = null; float gd = 22f;
			foreach (var gid in s.Gates.Where(x => Knowledge.KnowsGate(_u, x)))
			{
				float d = Place(_u.Gates[gid].Position).DistanceTo(pos);
				if (d < gd) { gd = d; gate = gid; }
			}
			if (gate is { } g) { GateTapped?.Invoke(g); AcceptEvent(); }
		}

		public override void _Draw()
		{
			if (_u == null) return;
			var s = _u.Sectors[_owner.Selected];
			float scale = ContentScale();
			var c = MapRect.GetCenter();
			var font = ThemeDB.FallbackFont;
			// Gerätetest: Beschriftungen werden gesammelt und zum Schluss kollisionsfrei gesetzt (LabelLayout), Symbole halten ihren Platz frei
			SpaceAdventure.Ui.Kit.DrawnText.Clear(this);
			var layout = new SpaceAdventure.Ui.Kit.LabelLayout(new Rect2(Vector2.Zero, Size - new Vector2(0, LegendHeight)), font);
			var labels = new System.Collections.Generic.List<(int Prio, Vector2 At, string Text, int Size, Color Col, float Gap)>();

			// Sektorscheibe (Runde 37: nur der Teil in der Kartenfläche), Ringe
			float edge = s.Radius * scale;
			var o = MapOrigin;
			DrawRect(MapRect, new Color(0.03f, 0.05f, 0.1f, 0.9f));
			DrawClippedCircle(o, edge, new Color(Edge, 0.6f), 2f, Stretch);
			DrawClippedCircle(o, edge * 0.66f, new Color(1, 1, 1, 0.05f), 1f, Stretch);
			DrawClippedCircle(o, edge * 0.33f, new Color(1, 1, 1, 0.05f), 1f, Stretch);
			DrawScaleBar(font, layout, scale * Stretch);

			// Rohstoffgürtel (Label unterhalb des Gürtels, damit es die Stationsnamen nicht verdeckt)
			foreach (var f in s.Fields.Where((_, k) => Knowledge.KnowsField(_u, s.Id, k)))
			{
				var p = Place(f.Position);
				float br = SpaceSim.Core.Mining.MiningRules.BeltRadius * scale;
				DrawArc(p, br, 0, Mathf.Tau, 40, new Color(0.65f, 0.6f, 0.5f, 0.35f), 2f, true);
				DrawCircle(p, 5f, new Color(0.65f, 0.6f, 0.5f, 0.8f));
				var cls = _u.AsteroidClasses.TryGetValue(f.ClassKey, out var ac) ? ac.Name : f.ClassKey;
				labels.Add((4, p + new Vector2(0, br), $"Gürtel · {cls}", 12, new Color(Dim, 0.9f), 6f));
			}

			// Gasernte: Gasfelder als violette Kreise (Radius maßstäblich)
			foreach (var gf in s.GasFields.Where((_, k) => Knowledge.KnowsGasField(_u, s.Id, k)))
			{
				var p = Place(gf.Position);
				float gr = gf.Radius * scale;
				DrawCircle(p, gr, new Color(0.62f, 0.42f, 0.92f, 0.12f));
				DrawArc(p, gr, 0, Mathf.Tau, 48, new Color(0.75f, 0.55f, 1f, 0.6f), 2f, true);
				labels.Add((4, p + new Vector2(0, gr), "Gasfeld", 12, new Color(0.8f, 0.65f, 1f, 0.95f), 6f));
			}

			// Gates mit Zielsektor
			foreach (var gid in s.Gates.Where(x => Knowledge.KnowsGate(_u, x)))
			{
				var g = _u.Gates[gid];
				var gp = Place(g.Position);
				var target = _u.Sectors[_u.Gates[g.Target].Sector];
				DrawCircle(gp, 13f, PanelSolid);
				// Phase 10: rot = geschlossen oder für dich gesperrt, orange = eingeschränkt (Zoll/Kontrolle)
				var gpol = AccessInfo.ForGate(_u, g, _u.Ships[_u.PlayerShip]);
				Color gcol = !g.Open || gpol.Denied ? Red : g.State == GateState.Restricted ? Orange : Green;
				DrawArc(gp, 13f, 0, Mathf.Tau, 24, gcol, 3f, true);
				layout.ReserveCircle(gp, 14f);
				labels.Add((2, gp, $"→ {target.Name}{(!g.Open ? " · geschlossen" : gpol.Denied ? " · gesperrt" : g.State == GateState.Restricted ? " · Kontrolle" : "")}", 12, gcol, 16f));
			}

			// Stationen: Label radial nach außen versetzt, damit die Namen sich nicht stapeln
			var spots = Spots();
			foreach (var sid in spots.Keys)
			{
				var st = _u.Stations[sid];
				var real = Place(st.Position);
				var p = spots[sid];
				// Runde 37 (Handytest r35: Linie zum echten Ort nicht erkennbar): deutlicher — hellere Linie und ein Ring am echten Ort
				if (p.DistanceTo(real) > 3f) { DrawLine(real, p, new Color(White, 0.55f), 1.5f, true); DrawArc(real, 3f, 0, Mathf.Tau, 10, new Color(White, 0.8f), 1.5f, true); }
				bool sel = _owner._selStation == sid;
				var col = MapOverlay.FactionColor(_u.FactionOf(st) ?? default);
				if (ConstructionInfo.Of(_u, st) is { } site)
				{
					// Phase 8: Baustelle als Raute mit Materialbalken (orange = läuft, grau = verlassen)
					var sc = site.Abandoned ? Dim : Orange;
					var pts = new[] { p + new Vector2(0, -11), p + new Vector2(11, 0), p + new Vector2(0, 11), p + new Vector2(-11, 0), p + new Vector2(0, -11) };
					DrawColoredPolygon(pts[..4], new Color(0.06f, 0.1f, 0.2f, 0.95f));
					DrawPolyline(pts, sc, sel ? 4f : 2f, true);
					DrawRect(new Rect2(p + new Vector2(-14, 14), new Vector2(28, 4)), new Color(Dim, 0.4f));
					DrawRect(new Rect2(p + new Vector2(-14, 14), new Vector2(28 * site.Progress, 4)), sc);
				}
				else if (!st.Dockable)
				{
					// Phase 30: Kleinstation — kleines Dreieck (Waffenstellung) bzw. kleiner Kreis (Satellit), Größe nach Radius
					bool armed = _u.StationTypes.TryGetValue(st.TypeKey, out var ot) && (ot.Defense?.Guns.Count ?? 0) > 0;
					if (armed)
					{
						var tri = new[] { p + new Vector2(0, -7), p + new Vector2(6, 5), p + new Vector2(-6, 5), p + new Vector2(0, -7) };
						DrawColoredPolygon(tri[..3], new Color(0.06f, 0.1f, 0.2f, 0.95f));
						DrawPolyline(tri, col, sel ? 3f : 2f, true);
					}
					else DrawArc(p, 4f, 0, Mathf.Tau, 12, col, sel ? 3f : 2f, true);
				}
				else
				{
					// Phase 30: Quadratgröße nach Größenklasse (S kleiner, L/XL größer)
					float h = Mathf.Clamp(st.Radius * 0.6f, 6f, 16f);
					DrawRect(new Rect2(p - new Vector2(h, h), new Vector2(h * 2f, h * 2f)), new Color(0.06f, 0.1f, 0.2f, 0.95f));
					DrawRect(new Rect2(p - new Vector2(h, h), new Vector2(h * 2f, h * 2f)), col, false, sel ? 4f : 2f);
				}
				if (sel) DrawArc(p, 20f, 0, Mathf.Tau, 24, Cyan, 2f, true);
				// Runde 37c (Handytest r37b: Leitlinien kreuzten Symbole; Beschriftungen sprangen beim Auswählen): ganze Symbolfläche freihalten; Auswahl ändert nur
				// die Farbe, nicht Reihenfolge oder Größe — die Lage der Namen bleibt stabil
				float sh = SymbolHalf(st) + 4f;
				layout.Reserve(new Rect2(p - new Vector2(sh, sh), new Vector2(sh * 2f, sh * 2f)));
				labels.Add((1, p, st.Name, 12, sel ? Cyan : White, sh + 2f));
			}

			// Runde 41/42b: Missionsziele im Sektor — goldene Sterne (eigene Form, nicht die Baustellen-Raute) mit Auftragstext (höchstens 3)
			TestMissionSymbols = 0;
			foreach (var mk in SpaceSim.Core.Missions.MissionMarkers.For(_u).Where(m => m.Sector == s.Id).Take(3))
			{
				var p = Place(mk.Position);
				float d = 10f, id = 4f;
				var star = new List<Vector2>();
				for (int i = 0; i < 8; i++)
				{
					float r = i % 2 == 0 ? d : id;
					float a = -Mathf.Pi / 2f + i * Mathf.Pi / 4f;
					star.Add(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
				}
				star.Add(star[0]);
				DrawColoredPolygon(star.Take(8).ToArray(), new Color(0.09f, 0.07f, 0.01f, 0.92f));
				DrawPolyline(star.ToArray(), FlightHud.MissionGold, 2.5f, true);
				layout.ReserveCircle(p, d + 2f);
				labels.Add((0, p + new Vector2(0, d + 9f), mk.Label, 11, FlightHud.MissionGold, 4f));
				TestMissionSymbols++;
			}

			// Universumsereignisse: türkiser Doppelring am Ereignisort mit Beschriftung (nur bekannte Sektoren; höchstens 2)
			TestEventSymbols = 0;
			foreach (var em in SpaceSim.Core.Simulation.UniverseEventInfo.Markers(_u).Where(m => m.Sector == s.Id).Take(2))
			{
				var p = Place(em.Position);
				DrawArc(p, 11f, 0, Mathf.Tau, 24, UniverseScreen.EventColor, 2.5f, true);
				DrawArc(p, 6f, 0, Mathf.Tau, 16, UniverseScreen.EventColor, 2f, true);
				layout.ReserveCircle(p, 13f);
				labels.Add((0, p + new Vector2(0, 20f), em.Label, 11, UniverseScreen.EventColor, 4f));
				TestEventSymbols++;
			}

			// Phase 26: Wracks (graues Kreuz) und Frachtbehälter (oranges Quadrat), echt aus Universe.Wrecks
			foreach (var wr in SpaceSim.Core.Simulation.Wrecks.In(_u, s.Id))
			{
				var p = Place(wr.Position);
				if (wr.Kind == WreckKind.Container) DrawRect(new Rect2(p - new Vector2(4, 4), new Vector2(8, 8)), Orange);
				else { var g = new Color(0.65f, 0.62f, 0.58f); DrawLine(p - new Vector2(6, 6), p + new Vector2(6, 6), g, 2f); DrawLine(p + new Vector2(-6, 6), p + new Vector2(6, -6), g, 2f); }
				TestWrecks++;
			}

			// NPC-Schiffe (echt über Materializer)
			var snap = Materializer.Snapshot(_u, s.Id, _owner.Ctx.FractionalTick());
			// Phase 27: Verbände, Geleit und Verfolgung als Linien zwischen den Schiffen (echt aus FleetInfo: Befehle und Verbände des Core)
			var posOf = new Dictionary<ShipId, Vector2>();
			foreach (var sh in snap) posOf[sh.Id] = Place(sh.Position);
			TestFleetLinks = 0;
			foreach (var link in SpaceSim.Core.Combat.FleetInfo.Links(_u, s.Id))
			{
				if (!posOf.TryGetValue(link.From, out var a) || !posOf.TryGetValue(link.To, out var b)) continue;
				Color lcol = link.Kind switch { "escort" => Green, "pursuit" => Orange, "pack" => new Color(Red, 0.8f), _ => new Color(0.35f, 0.55f, 1f, 0.9f) };
				if (link.Kind == "pursuit") DrawDashedLine(a, b, lcol, 2f, 6f); else DrawLine(a, b, lcol, 2f);
				TestFleetLinks++;
			}
			foreach (var sh in snap)
			{
				var p = Place(sh.Position);
				Color col = sh.Mining ? Orange : sh.Role switch
				{
					ShipRole.Miner => Green,
					ShipRole.Police => new Color(0.35f, 0.55f, 1f),   // Phase 11: Streife
					ShipRole.Pirate => Red,                          // Phase 11: Kaperschiff
					_ => new Color(0.5f, 0.8f, 1f),
				};
				var dir = sh.Direction.Length > 0.01f ? new Vector2(sh.Direction.X, sh.Direction.Y) : new Vector2(0, -1);
				float ang = Mathf.Atan2(dir.Y, dir.X) + Mathf.Pi / 2f;
				var pts = new[]
				{
					p + new Vector2(0, -7).Rotated(ang), p + new Vector2(5.5f, 4).Rotated(ang), p + new Vector2(-5.5f, 4).Rotated(ang),
				};
				DrawColoredPolygon(pts, col);
				if (_u.Fights.IsEngaged(sh.Id)) DrawArc(p, 10f, 0, Mathf.Tau, 16, Red, 2f, true);   // Phase 24: im Gefecht
			}

			// Mehrpunkt-Route: Linie und Nummern der Halte in diesem Sektor
			{
				int routeSlot = 960;
				DrawRoute(ref routeSlot);
			}

			// Spieler
			if (s.Id == _u.PlayerSector)
			{
				var pl = _u.Ships[_u.PlayerShip];
				var p = Place(pl.Position);
				// Gerätetest: weit draußen am Kartenrand mit Richtungshinweis statt den Maßstab zu sprengen
				var inside = MapRect.Grow(-22f);
				bool outside = !inside.HasPoint(p);
				if (outside) p = new Vector2(Mathf.Clamp(p.X, inside.Position.X, inside.End.X), Mathf.Clamp(p.Y, inside.Position.Y, inside.End.Y));
				DrawShip(p, 9f, Cyan);
				layout.ReserveCircle(p, 10f);
				labels.Add((0, p, outside ? $"du · #{pl.Id.Value} (außerhalb)" : $"du · #{pl.Id.Value}", 12, Cyan, 12f));
			}

			// Runde 37 (Handytest r35: viele Stationen ohne Namen; 4:3: Namen weit vom Symbol): passt der Name nicht, kleiner, dann nur das erste Wort
			// („Sektor-HQ“, „Farm“) — so bleibt fast jedes Symbol beschriftet, und der Name liegt dicht am Symbol
			TestUnlabeled = 0;
			foreach (var l in labels.OrderBy(l => l.Prio))
			{
				if (layout.Draw(this, l.At, l.Text, l.Size, l.Col, l.Gap, 200f)) continue;
				if (layout.Draw(this, l.At, l.Text, 10, l.Col, l.Gap - 2f, 160f)) continue;
				// Tore: „→ Sektor Nebelmark“ → „→ Nebelmark“ (das erste Wort allein wäre nur der Pfeil)
				string shortT = l.Text.Replace("Sektor ", "");
				if (shortT != l.Text && layout.Draw(this, l.At, shortT, 10, l.Col, l.Gap - 2f, 160f)) continue;
				if (l.Prio <= 2 && layout.DrawWithLeader(this, l.At, shortT, 10, l.Col, l.Gap - 2f)) continue;
				var words = shortT.Split(' ');
				string first = words[0] == "→" && words.Length > 1 ? "→ " + words[1] : words[0];
				if (first != shortT && layout.Draw(this, l.At, first, 10, l.Col, l.Gap - 2f, 120f)) continue;
				if (first != shortT && l.Prio <= 2 && layout.DrawWithLeader(this, l.At, first, 10, l.Col, l.Gap - 2f)) continue;
				// letzter Ausweg: Leitlinie darf eine andere kreuzen (lieber eine Kreuzung als ein Symbol ohne Namen)
				if (l.Prio <= 1 && layout.DrawWithLeader(this, l.At, first, 10, l.Col, l.Gap - 2f, 120f, allowCross: true)) continue;
				if (l.Prio <= 1) TestUnlabeled++;
			}
			// Legende zweizeilig, gekürzt auf die Breite (lief auf dem Handy über den Rand)
			float lw = Size.X - 24f;
			SpaceAdventure.Ui.Kit.UiKit.DrawFit(this, 900, font, new Vector2(12, Size.Y - 44), "◼ Station   ◆ Baustelle   ◯ Gate   ▲ cyan = du   ★ = Auftrag", 13, lw, new Color(Dim, 0.8f));
			SpaceAdventure.Ui.Kit.UiKit.DrawFit(this, 901, font, new Vector2(12, Size.Y - 26), "▲ orange schürft, blau Polizei, rot Pirat, roter Ring = Gefecht", 13, lw, new Color(Dim, 0.8f));
			SpaceAdventure.Ui.Kit.UiKit.DrawFit(this, 902, font, new Vector2(12, Size.Y - 8), "Linie: blau Verband, grün Geleit, orange gestrichelt Verfolgung", 13, lw, new Color(Dim, 0.8f));
		}

		void DrawShip(Vector2 c, float s, Color col)
		{
			var pts = new[]
			{
				c + new Vector2(0, -s), c + new Vector2(s * 0.55f, s * 0.35f), c + new Vector2(0, s * 0.55f), c + new Vector2(-s * 0.55f, s * 0.35f),
			};
			DrawColoredPolygon(pts, new Color(0.25f, 0.33f, 0.5f));
			var closed = new System.Collections.Generic.List<Vector2>(pts) { pts[0] };
			DrawPolyline(closed.ToArray(), col, 1.5f, true);
		}
	}

	/// <summary>Runde 37: Stationen/Spieler ohne Beschriftung beim letzten Zeichnen (Selbsttest).</summary>
	public static int TestUnlabeled { get; private set; }
	/// <summary>Selbsttest: Planet in der zuletzt gezeichneten Sektor-Detailkarte (Phase 14; leer ohne Planet).</summary>
	public string TestPlanet { get; private set; } = "";
	/// <summary>Phase 27: Flottenzeile der Sektorkarte (Verbände/Geleit/Verfolgung; leer = keine) und Zahl der gezeichneten Verbindungslinien.</summary>
	public string TestFleet { get; private set; } = "";
	public static int TestFleetLinks { get; private set; }

	/// <summary>Phase 24: Gefechtszeile der Sektorkarte (leer = kein Gefecht).</summary>
	public string TestCombat { get; private set; } = "";
	/// <summary>Ausbau Punkt 16 (Selbsttest): Zeile „n unbekannt“/„Unerkundet“ der Sektorkarte.</summary>
	public string TestKnownLine { get; private set; } = "";
	/// <summary>Runde 21: unterer Kreisrand und Oberkante der Legende der Draufsicht (Selbsttest: kein Überlappen).</summary>
	public (float CircleBottom, float LegendTop) TestLegendGap => _map is null || !GodotObject.IsInstanceValid(_map) ? (0f, 1f) : _map.TestLegendGap;
	/// <summary>Runde 44: Maßstabsleiste der Draufsicht (Text, Länge in Bildpunkten).</summary>
	public (string Text, float Px) TestScaleBar => _map is null || !GodotObject.IsInstanceValid(_map) ? ("", 0f) : (_map.ScaleBarText, _map.ScaleBarPixels);
	/// <summary>Phase 26: gezeichnete Wracks/Behälter (aufsummiert über die Zeichendurchläufe; Selbsttest prüft &gt; 0).</summary>
	public static int TestWrecks;
}
