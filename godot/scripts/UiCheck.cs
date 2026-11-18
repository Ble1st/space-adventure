using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Ui;
using SpaceAdventure.Ui.Kit;

namespace SpaceAdventure;

/// <summary>
/// Runde 31 (UI-ANPASSUNG.md Etappe 0): Prüfwerkzeug <c>--ui-check &lt;ordner&gt;</c>. Öffnet jeden Bildschirm (Flug-HUD in den Zuständen normal, Warnung,
/// Gefecht, Auto-Mining, Stationsangriff; Hamburger-Menü; Meldung/Toast; Sternenkarte; Dock-Menü und alle Dock-Bereiche einer Werft; alle Bereiche der
/// Kommandozentrale; Startpaket-Auswahl) in sechs Fenstergrößen und drei UI-Faktoren (1 = heute; 1,15/1,3 = Stufen normal/groß ohne Obergrenze) und
/// prüft automatisch: Überlappung interaktiver Elemente, gekürzte Texte („…“ bzw. Text breiter als sein Feld), Tippflächen unter 7 mm (angenommene DPI je
/// Auflösung), Elemente außerhalb der Fläche, Füllgrad der Vollbild-Bildschirme. Ergebnis: <c>ui-check.txt</c> (Tabelle), <c>ui-check.json</c> und PNGs
/// (nur mit Fenster, nicht headless).
/// </summary>
public partial class UiCheck : Node
{
	readonly Main _m;
	readonly string _dir;
	public UiCheck(Main m, string dir) { _m = m; _dir = dir; }
	/// <summary>Runde 33: nur die Flug-Zustände (HUD, Menü, Meldung) — für schnelle Durchläufe der Etappe E2.</summary>
	public bool FlightOnly { get; init; }

	/// <summary>Fenstergröße und angenommene Geräte-DPI (dokumentierte Annahme je Auflösung).</summary>
	static readonly (int W, int H, float Ppi, string Device)[] AllSizes =
	{
		(1280, 720, 294f, "5″-Handy 720p"),
		(1560, 720, 282f, "6,1″-Handy 19,5:9 720p"),
		(2340, 1080, 396f, "Samsung A15 6,5″ (Referenz)"),
		(1920, 1200, 224f, "10,1″-Tablet 16:10"),
		(2048, 1536, 264f, "9,7″-Tablet 4:3"),
		(960, 540, 245f, "4,5″-Handy / kleines Fenster"),
		(2340, 1080, 396f, "Samsung A15 mit Kamera-Aussparung links"),
		(2340, 1080, 396f, "Samsung A15 mit Aussparung laut dumpsys"),
	};
	/// <summary>
	/// Runde 37 (Handytest r35 g31: „Verkaufen“ ragte über den Zeilenrand — am Gerät nimmt die Kamera-Aussparung links ≈ 80 Bildpunkte, die Prüfung lief ohne
	/// sichere Ränder und sah breitere Zeilen): sichere Ränder je Gerät in logischen Bildpunkten bei Faktor 1 (L, O, R, U).
	/// </summary>
	/// Runde 37b: die echten Werte des A15 (dumpsys: Querformat links 80, rechts 135, oben 84 Gerätepunkte bei 2340 × 1080) in logische Punkte umgerechnet
	/// (× 720/1080): links 53, rechts 90, oben 56.
	/// Runde 37c: zwei Fassungen — „a“ = was die App zur Laufzeit bekommt (Gerätelog r37b: GetDisplaySafeArea 80,0 2260×1080 → nur links 80 Gerätepunkte =
	/// 53 logisch; dagegen wird gebaut), „d“ = die dumpsys-Werte (zusätzlich oben 84, rechts 135 — Statusleiste/Navigationsleiste, falls sie in einem
	/// anderen Modus doch gemeldet werden; strengere Gegenprobe).
	static UiInsets InsetsOf(string device) => device.Contains("dumpsys") ? new UiInsets(80f * 720f / 1080f, 84f * 720f / 1080f, 135f * 720f / 1080f, 0f)
		: device.Contains("Aussparung") ? new UiInsets(80f * 720f / 1080f, 0f, 0f, 0f) : new UiInsets(0f, 0f, 0f, 0f);
	/// <summary>Runde 37c: Kürzel der Prüfgröße (Dateinamen, Protokoll, Auswahl): „2340x1080“, „2340x1080a“ (Laufzeit-Aussparung), „2340x1080d“ (dumpsys).</summary>
	static string KeyOf((int W, int H, float Ppi, string Device) z) => $"{z.W}x{z.H}" + (z.Device.Contains("dumpsys") ? "d" : z.Device.Contains("Aussparung") ? "a" : "");
	static readonly float[] AllFactors = { 1f, 1.15f, 1.3f };
	(int W, int H, float Ppi, string Device)[] Sizes = AllSizes;
	float[] Factors = AllFactors;
	/// <summary>Nur Dock-/Kommandozentralen-Zustände, keine Flugzustände (Runde 35b: schneller Durchlauf).</summary>
	public bool NoFlight { get; init; }
	/// <summary>Runde 35b: Auswahl für schnelle Durchläufe — „1280x720,2340x1080“ bzw. „1.3“ (leer = alle).</summary>
	public void Select(string? sizes, string? factors)
	{
		if (!string.IsNullOrEmpty(sizes)) Sizes = AllSizes.Where(z => sizes.Split(',').Contains(KeyOf(z))).ToArray();
		if (!string.IsNullOrEmpty(factors)) Factors = factors.Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
	}

	sealed record Finding(string State, int W, int H, float Factor, int Interactive, int Overlaps, int Truncated, int Small7, int Small5, int Outside, float Fill, int ShipCovered, float Effective,
		int MessageCovers, float Content, float MinMm, int Visible, bool Unstable, float Layout, int TextOverlaps, int ClippedCount, float MinFont,
		List<string> Notes, string Key = "");
	readonly List<Finding> _results = new();
	readonly List<(string Name, Action Setup, int Wait)> _plan = new();
	int _sizeIdx, _factorIdx, _wait;
	string _state = "";
	bool _shot;
	bool Headless => DisplayServer.GetName() == "headless";

	public override void _Ready()
	{
		SpaceAdventure.Ui.Kit.DrawnText.Enabled = true;   // Runde 35b: gezeichnete Texte melden
		DirAccess.MakeDirRecursiveAbsolute(_dir);
		GD.Print($"[ui-check] Zielordner {_dir}");
		BeginSize();
	}

	void BeginSize()
	{
		var (w, h, ppi, dev) = Sizes[_sizeIdx];
		UiScale.TestInsets = InsetsOf(dev);
		// Größer als der Prüfbildschirm (2340×1080 auf 1920×1080): bei Streckmodus canvas_items/expand hängt das logische Layout nur vom
		// Seitenverhältnis ab — das Fenster bekommt dasselbe Seitenverhältnis in passender Größe, die Millimeter rechnen mit der Zielauflösung
		var win = WindowFor(w, h);
		DisplayServer.WindowSetSize(win);
		GetTree().Root.Size = win;
		UiScale.TestPpi = ppi;
		// Faktor fest vorgeben (bleibt auch beim Größenwechsel des Fensters): hier wird bewusst jeder Faktor vermessen, nicht nur der der Stufe
		UiScale.TestForce = Factors[_factorIdx];
		UiScale.Apply();
		BuildPlan();
		_wait = 20;
	}

	/// <summary>Fenstergröße mit dem Seitenverhältnis der Zielauflösung, höchstens 1900×1000 (Prüfbildschirm).</summary>
	static Vector2I WindowFor(int w, int h)
	{
		float k = MathF.Min(1f, MathF.Min(1900f / w, 1000f / h));
		return new Vector2I((int)MathF.Round(w * k), (int)MathF.Round(h * k));
	}

	void BuildPlan()
	{
		_plan.Clear();
		Func<Universe> u = () => _m.Session.U;
		void Add(string n, Action a, int wait = 12) => _plan.Add((n, a, wait));
		Add("flug-normal", () => { _m.TestMenu("new:trader"); }, 60);
		if (!NoFlight) Add("hamburger-menue", () => _m.TestOpenCommand(), 10);
		// Runde 44: Menü mit Rückansicht (Modus-Taste aktiv, Zeile „Höhe − / +“)
		if (!NoFlight) Add("menue-kamera-rueck", () => _m.TestCameraAction("cam_chase"), 10);
		if (!NoFlight) Add("meldung-toast", () => { _m.TestCameraAction("cam_free"); _m.TestCommand("close"); _m.HudRef.Alert("Prüfmeldung: Überfall angekündigt — andocken oder Sektor verlassen. Dritte Zeile zum Messen.", 30f); _m.HudRef.Toast("Prüftoast: Ware verkauft, 10 × 21,3 = 213 Cr", 30f); }, 10);
		if (!FlightOnly)
		{
			Add("sternenkarte", () => { _m.HudRef.HideAlert(); _m.TestCommand("map"); }, 12);
			// Runde 37b (Prüfauflage A4/A2): Sternenkarte mit Hinweis und Meldung (im Meldungsstreifen, nie über Knoten/Namen)
			Add("sternenkarte-toast", () => { _m.HudRef.Alert("Prüfmeldung über der Sternenkarte", 30f); _m.HudRef.Toast("Prüftoast über der Sternenkarte: Autopilot gesetzt, zweite Zeile zum Messen des Umbruchs", 30f); }, 12);
			// Runde 34 (E3): Dock-Menü und alle Dock-Bereiche — an der Werft und an weiteren Stationen für die Bereiche, die die Werft nicht hat (Lager, Behörde,
			// Taxi …); jeder Bereich wird einmal vermessen
			var seenAreas = new HashSet<string>();
			foreach (var typeKey in new[] { "shipyard", "warehouse", "sector_hq", "trade_station", "pirate_hideout" })   // Schwarzmarkt: Versteck mit Hehler
			{
				string tk = typeKey;
				Add(tk == "shipyard" ? "dockmenue" : "dockmenue-" + tk, () =>
				{
					_m.HudRef.Map.Visible = false; _m.HudRef.HideAlert(); _m.HudRef.Toast("", 0.01f);
					if (_m.TestDocked) _m.TestUndock();
					var st = u().StationsById().FirstOrDefault(s => s.TypeKey == tk);
					if (st is null) return;
					_m.TestDockAt(st.Id);
					// Schwarzmarkt: Hehlerware und Fracht an Bord, damit beide Spalten Zeilen haben (die Messung braucht Inhalt)
					if (tk == "pirate_hideout" && u().Ships.TryGetValue(u().PlayerShip, out var psBm))
					{
						foreach (var g in u().Goods.Values.Where(g => !u().Modules.ContainsKey(g.Key)).Take(4)) st.BlackStock[g.Id] = 12.5f;
						var food = u().FindGood("food");
						if (food is not null) psBm.Cargo[food.Id] = 5f;
					}
					// Runde 42c: versiegelte Auftragsfracht in der Schiffspalte (alle Dock-Zustände messen sie mit)
					if (u().Ships.TryGetValue(u().PlayerShip, out var ps42) && ps42.MissionCargo < 20f) ps42.MissionCargo = 20f;
					int at = 0;
					foreach (var def in _m.TestDockAreas().Where(d => d.Available).Select(d => d.Id).ToList())
						if (seenAreas.Add(def)) _plan.Insert(at++, ("dock-" + def, () => _m.TestOpenDockArea(def), 12));
				}, 20);
			}
			// Runde 35c (Prüfung W5): die aufgeklappte Schiffskarte („Dein Schiff“ bei eingeklappter Schiffsspalte) — ohne Einklappen zeigt der Handel die Spalte
			Add("dock-schiff", () =>
			{
				_m.HudRef.Map.Visible = false;
				if (_m.TestDocked) _m.TestUndock();
				var yard = u().StationsById().FirstOrDefault(s => s.TypeKey == "shipyard");
				if (yard is null) return;
				_m.TestDockAt(yard.Id);
				_m.TestOpenDockArea(SpaceAdventure.Ui.Screens.DockAreas.Trade);
				_m.HudRef.DockTrade.TestShipOpen = true;
			}, 20);
			// Runde 37 (Handytest r35 g31: „Verkaufen“ ragte im offenen Editor über den Zeilenrand — das Werkzeug öffnete nie eine Zeile): Handel mit
			// aufgeklappter Zeile (Ware an Bord, damit der Verkaufsteil gezeichnet wird)
			Add("dock-handel-zeile", () =>
			{
				_m.HudRef.DockTrade.TestShipOpen = false;
				var tr = _m.HudRef.DockTrade;
				var goods = tr.TestGoods;
				if (goods.Count == 0) return;
				var g = goods[0];
				var ship = u().Ships.TryGetValue(u().PlayerShip, out var ps) ? ps : null;
				if (ship is not null && ship.FreeUnits(u(), g) >= 3f) ship.Cargo[g] = (ship.Cargo.TryGetValue(g, out var have) ? have : 0f) + 3f;
				tr.TestOpenRow(g);
			}, 20);
			Add("dock-menue-toast", () => { _m.HudRef.DockTrade.TestShipOpen = false;  _m.TestOpenCommand(); _m.HudRef.Toast(UiScale.ShortText() + " — zweite Zeile zum Messen des Umbruchs über dem offenen Menü", 30f); }, 12);
			Add("kz-open", () => { _m.TestCloseCommand(); if (_m.TestDocked) _m.TestUndock(); }, 20);
			foreach (var (id, _, _) in SpaceAdventure.Ui.Screens.CommandAreas.All)
				Add("kz-" + id, () => { _m.TestOpenCcArea(id); }, 12);
			// Runde 40: Comms mit Diplomatie-Reiter und ausgewählter Fraktion (Knöpfe mit Vorschau)
			Add("kz-comms-diplomatie", () => { _m.TestOpenCcArea(SpaceAdventure.Ui.Screens.CommandAreas.Comms); _m.HudRef.CcComms.TestSetTab("diplomacy"); _m.HudRef.CcComms.TestSelectFaction(0); }, 12);
			// Runde 42c (Handytest): Comms-Reiter „Aufträge“ mit aktiver Meldung — Meldungsfach oben, Liste beginnt darunter (nie über Listentext)
			Add("kz-comms-auftraege-meldung", () => { _m.TestOpenCcArea(SpaceAdventure.Ui.Screens.CommandAreas.Comms); var cs = _m.HudRef.CcComms; cs.TestSetTab("missions"); cs.TestShowMessage("Auftrag angenommen: Transport — versiegelte Fracht 20 m³ → Elektronikfabrik Kalypsо · noch 9:59 (zweite Zeile zum Messen des Umbruchs im Meldungsfach)"); }, 12);
			// Runde 37b (A4): Fraktionsdetail (schmal: Detail statt Liste)
			Add("kz-fraktionsdetail", () => { _m.TestOpenCcArea(SpaceAdventure.Ui.Screens.CommandAreas.Factions); var fr = _m.HudRef.CcFactions; fr.Selected = fr.TestRows.FirstOrDefault()?.Id; fr.TestDetailOpen = true; }, 12);
			Add("startpakete", () => { _m.TestOpenCcArea(SpaceAdventure.Ui.Screens.CommandAreas.System); _m.HudRef.CcSystem.TestChoosing = true; }, 12);
		}
		if (NoFlight) { Add("ende", () => { _m.HudRef.CcSystem.TestChoosing = false; _m.TestCloseCommand(); _m.HudRef.HideAlert(); _m.HudRef.Toast("", 0.01f); }, 5); return; }   // Runde 37b: der lange Prüfhinweis des Menüs gehört nicht in den Flug
		Add("flug-automining", () => { _m.HudRef.HideAlert(); _m.HudRef.Map.Visible = false; _m.HudRef.CcSystem.TestChoosing = false; _m.TestCloseCommand(); _m.TestMenu("new:miner"); }, 40);
		Add("flug-automining2", () => { _m.TestStartMining(); }, 90);
		Add("flug-warnung", () => { _m.TestMenu("new:combat"); }, 40);
		Add("flug-gefecht", () => { _m.TestSetFast(true); }, 120);
		Add("flug-gefecht2", () => { _m.TestSetFast(false); }, 20);
		// HUD-Neu: Leviathan (zehn Waffen) im Gefechtstest — Gruppen im Daumenbogen, dann die Einzelschächte aufgeklappt
		Add("flug-leviathan", () =>
		{
			_m.TestMenu("new:combat");
		}, 40);
		Add("flug-leviathan2", () =>
		{
			var lev = u().ShipModels.Values.FirstOrDefault(m => m.ClassKey == "battlecruiser");
			if (lev is not null) _m.TestUseModel(lev.Key);
		}, 40);
		Add("flug-leviathan-einzeln", () => { _m.HudRef.Flight.TestExpanded = true; }, 20);
		Add("flug-leviathan-einzeln2", () => { }, 6);
		Add("flug-stationsangriff", () =>
		{
			_m.HudRef.Flight.TestExpanded = false;
			_m.TestMenu("new:war");
		}, 40);
		// Runde 37b (A4): gewählte Station vor dem Angriff („Station angreifen“ sichtbar, nicht scharf)
		Add("flug-stationswahl", () =>
		{
			var hq = u().StationsById().Where(s => s.Sector == u().PlayerSector && s.TypeKey == "sector_hq").FirstOrDefault();
			if (hq is not null) _m.TestSelectStation(hq.Id);
		}, 30);
		Add("flug-stationsangriff2", () =>
		{
			var hq = u().StationsById().Where(s => s.Sector == u().PlayerSector && s.TypeKey == "sector_hq").FirstOrDefault();
			if (hq is not null) { _m.TestSelectStation(hq.Id); _m.TestArmStation(); }
		}, 30);
		// Eigene Stationen: Platzierungs-HUD (Geist am freien Platz, grün) und am Tor (rot mit Grund)
		Add("flug-platzierung", () => { _m.TestMenu("new:trader"); }, 40);
		Add("flug-platzierung2", () => { _m.HudRef.HideAlert(); _m.TestBeginPlacement("farm"); }, 30);
		Add("flug-platzierung-rot", () =>
		{
			var sec = u().Sectors[u().PlayerSector];
			if (sec.Gates.Count > 0) { var g = u().Gates[sec.Gates[0]].Position; _m.TestPlaceAt(new Godot.Vector2(g.X, g.Y)); }
		}, 12);
	}
	bool _skip;

	public override void _Process(double delta)
	{
		if (_wait > 0) { _wait--; return; }
		if (_shot) return;
		if (_state.Length > 0)
		{
			// Zustand vermessen und abbilden
			if (!_skip && !_state.EndsWith("-open") && _state != "flug-automining" && _state != "flug-gefecht" && _state != "flug-stationsangriff" && _state != "flug-leviathan" && _state != "flug-leviathan-einzeln" && _state != "flug-platzierung")
			{
				// Runde 34b (Prüfung B5): messen, bis zwei aufeinanderfolgende Messungen (Abstand 6 Bilder) dieselbe Zahl sichtbarer und interaktiver Elemente haben —
				// höchstens 8 Versuche, sonst „instabil“ (vorher wurde von zwei Messungen die „ruhigere“ genommen, das konnte ein halb aufgebauter Zustand sein)
				var m = Measure(_state);
				if (_first is null || m.Interactive != _first.Interactive || m.Visible != _first.Visible || MathF.Abs(m.Layout - _first.Layout) > 0.5f)
				{
					if (++_tries < 8) { _first = m; _wait = 6; return; }
					m = m with { Unstable = true };
				}
				var pick = m;
				_first = null; _tries = 0;
				_results.Add(pick);
				_lastMeasured = pick.State;
				foreach (var note in pick.Notes.Where(x => x.Contains("außerhalb") || x.StartsWith("Schiff verdeckt") || x.StartsWith("Meldung") || x.StartsWith("Hinweis") || x.StartsWith("Text über") || x.StartsWith("beschnitten") || x.StartsWith("Überlappung") || x.StartsWith("gekürzt") || x.StartsWith("Taste gekürzt"))) GD.Print($"[ui-check]   {note}");
				GD.Print($"[ui-check] {pick.Key} f{pick.Factor:0.00} {pick.State}: Tasten {pick.Interactive}, Überlappung {pick.Overlaps}, gekürzt {pick.Truncated}, <7 mm {pick.Small7}, <5 mm {pick.Small5}, kleinste {pick.MinMm:0.0} mm, außerhalb {pick.Outside}, Meldung über Inhalt {pick.MessageCovers}, Text über {pick.TextOverlaps}, beschnitten {pick.ClippedCount}, kleinste Schrift {pick.MinFont:0.0} px, Hülle {(pick.Fill < 0 ? "–" : $"{pick.Fill * 100:0} %")}, belegt {(pick.Content < 0 ? "–" : $"{pick.Content * 100:0} %")}{(pick.Unstable ? " INSTABIL" : "")}");
			}
			_skip = false;
			_state = "";
			if (!Headless && _lastMeasured is { } name)   // Runde 35b: Bilder für alle Faktoren
			{
				_shot = true;
				var (w, h, _, dv) = Sizes[_sizeIdx];
				string wh = KeyOf(Sizes[_sizeIdx]);
				string file = System.IO.Path.Combine(_dir, MathF.Abs(Factors[_factorIdx] - 1f) < 0.001f ? $"{wh}_{name}.png" : $"{wh}_f{Factors[_factorIdx] * 100:0}_{name}.png");
				RenderingServer.FramePostDraw += Shot;
				void Shot()
				{
					RenderingServer.FramePostDraw -= Shot;
					GetViewport().GetTexture().GetImage().SavePng(file);
					_shot = false;
				}
			}
			_lastMeasured = null;
			return;
		}
		if (_plan.Count == 0)
		{
			_factorIdx++;
			if (_factorIdx >= Factors.Length) { _factorIdx = 0; _sizeIdx++; }
			if (_sizeIdx >= Sizes.Length) { Finish(); return; }
			BeginSize();
			return;
		}
		var (n, a, wait) = _plan[0];
		_plan.RemoveAt(0);
		_state = n;
		try { a(); } catch (Exception e) { GD.PrintErr($"[ui-check] {n}: {e.Message}"); _skip = true; }
		_wait = wait;
	}
	string? _lastMeasured;
	int _tries;
	Finding? _first;

	/// <summary>Sichtbarer Teil eines Elements: geschnitten mit allen Vorfahren, die ihren Inhalt beschneiden (rollende Listen).</summary>

	/// <summary>
	/// Runde 35b: Textprüfung (Selbsttest Stufe 111 nutzt dieselbe Regel) — Textflächen mit der Schrift gemessen (Labels, gezeichnete Texte aus
	/// <see cref="SpaceAdventure.Ui.Kit.DrawnText"/>), Text über Text/Taste und Beschnitt am Rahmen bzw. Clip (senkrecht nur innerhalb rollender Listen erlaubt).
	/// </summary>
	public static (List<(Control Owner, Rect2 R, Rect2 Full, string Text, float Eff)> Texts, int TextOverlaps, int Clipped, int DrawnTruncated) TextChecks(List<Control> scope, List<Control> inter, List<string> notes)
	{
		var font = ThemeDB.FallbackFont;
		int drawnTrunc = 0;
		// ── Runde 35b (Prüfung: das Werkzeug übersah sichtbare Mängel) ──
		// Textflächen: sichtbare Labels mit der Schrift gemessen (nicht die Control-Breite), gezeichnete Texte aus DrawnText
		// R = sichtbarer Teil (für Überdeckung), Full = ungeschnittene Textfläche (für den Beschnitt; Runde 35c, Prüfung W1: geschnitten konnte C nie anschlagen)
		var texts = new List<(Control Owner, Rect2 R, Rect2 Full, string Text, float Eff)>();
		foreach (var c in scope)
		{
			if (c is not Godot.Label l || l.Text.Length == 0 || Clipped(c).Size.X <= 1f || Alpha(c) < 0.05f) continue;   // ausgeblendet (Modulate) zählt nicht
			var g = l.GetGlobalRect(); float k = l.GetGlobalTransform().Scale.Y;
			int fs = l.GetThemeFontSize("font_size");
			var lf = l.GetThemeFont("font") ?? font;
			float tw, th;
			float lh = lf.GetHeight(fs) + l.GetThemeConstant("line_spacing");
			int nl;
			if (l.AutowrapMode != TextServer.AutowrapMode.Off) { tw = l.Size.X; nl = Math.Max(1, l.GetVisibleLineCount()); }
			else
			{
				tw = lf.GetStringSize(l.Text, HorizontalAlignment.Left, -1, fs).X;
				if (l.ClipText) tw = MathF.Min(tw, l.Size.X);
				nl = Math.Max(1, l.Text.Split('\n').Length);
			}
			th = nl * lh - l.GetThemeConstant("line_spacing");   // Zeilenblock ohne den Abstand nach der letzten Zeile
			float x = l.HorizontalAlignment == HorizontalAlignment.Center ? (l.Size.X - tw) / 2f : l.HorizontalAlignment == HorizontalAlignment.Right ? l.Size.X - tw : 0f;
			float y = l.VerticalAlignment == VerticalAlignment.Center ? (l.Size.Y - th) / 2f : l.VerticalAlignment == VerticalAlignment.Bottom ? l.Size.Y - th : 0f;
			// Glyphenkern: die Zeilenhöhe enthält Ober-/Unterlängenraum; eng gesetzte Zeilen berühren sich dort, ohne dass sich Schrift überdeckt
			float pad = MathF.Max(0f, (lf.GetHeight(fs) - fs) / 2f);
			y += pad; th = MathF.Max(1f, (nl - 1) * lh + fs);
			var full = new Rect2(g.Position + new Vector2(x, y) * k, new Vector2(tw, th) * k);
			var r = full.Intersection(ClipOf(l));   // was ein rollender/beschneidender Vorfahr wegschneidet, ist nicht sichtbar
			if (r.Size.X <= 1f || r.Size.Y <= 1f) continue;
			texts.Add((l, r, full, l.Text, fs * k));
		}
		var drawnOwners = new Dictionary<ulong, CanvasItem>();
		foreach (var c in scope) drawnOwners[c.GetInstanceId()] = c;
		foreach (var e in SpaceAdventure.Ui.Kit.DrawnText.All)
		{
			if (!drawnOwners.TryGetValue(e.Owner, out var ci) || ci is not Control oc) continue;
			var gt = oc.GetGlobalTransform();
			var r = new Rect2(gt * e.Local.Position, e.Local.Size * gt.Scale.Y);
			var vis = ClipOf(oc).Intersection(r);
			if (vis.Size.X <= 1f || vis.Size.Y <= 1f) continue;
			texts.Add((oc, vis, r, e.Shown, e.Size * gt.Scale.Y));
			if (e.Shown != e.Full) { drawnTrunc++; if (notes.Count < 44) notes.Add($"gekürzt (gezeichnet) „{Short(e.Shown)}“"); }
		}
		// Text gegen Text und Text gegen Taste (nicht Vorfahr/Nachfahr; Überdeckung ≥ 9 px²)
		int textOver = 0;
		for (int i = 0; i < texts.Count; i++)
		{
			for (int j = i + 1; j < texts.Count; j++)
			{
				if (texts[i].Owner == texts[j].Owner && texts[i].Owner is not Godot.Label) { if (texts[i].R.Intersection(texts[j].R) is var xx && xx.Size.X * xx.Size.Y >= 9f) { textOver++; if (notes.Count < 50) notes.Insert(0, $"Text über Text „{Short(texts[i].Text)}“ × „{Short(texts[j].Text)}“"); } continue; }
				if (texts[i].Owner.IsAncestorOf(texts[j].Owner) || texts[j].Owner.IsAncestorOf(texts[i].Owner)) continue;
				var x = texts[i].R.Intersection(texts[j].R);
				if (x.Size.X * x.Size.Y >= 9f) { textOver++; if (notes.Count < 50) notes.Insert(0, $"Text über Text „{Short(texts[i].Text)}“ × „{Short(texts[j].Text)}“"); }
			}
			foreach (var b in inter)
			{
				if (b == texts[i].Owner || b.IsAncestorOf(texts[i].Owner) || texts[i].Owner.IsAncestorOf(b)) continue;
				var x = texts[i].R.Intersection(Clipped(b));
				if (x.Size.X * x.Size.Y >= 9f) { textOver++; if (notes.Count < 50) notes.Insert(0, $"Text über Taste „{Short(texts[i].Text)}“ × „{Label(b)}“"); }
			}
		}
		// Beschnitt: Text/Taste nicht ganz sichtbar, ohne dass der Bereich dort senkrecht rollt; oder über den Rahmen der Karte hinaus
		int clippedCount = 0;
		var shapes = texts.Select(t => (t.Owner, R: t.Full, Name: t.Text, Drawn: t.Owner is not Godot.Label)).Concat(inter.Select(b => (Owner: b, R: b.GetGlobalRect(), Name: Label(b), Drawn: false))).ToList();
		foreach (var (owner, full, name, drawn) in shapes)
		{
			string? why = null;
			bool inList = false;   // schon durch eine senkrecht rollende Liste gegangen: oben/unten anschneiden ist dort Absicht
			// gezeichnete Texte: auch der Zeichner selbst begrenzt, wenn er beschneidet (seine Fläche ist dann der Rahmen)
			for (Node? n = drawn && owner.ClipContents ? owner : owner.GetParent(); n is not null && why is null; n = n.GetParent())
			{
				if (n is not Control pc) continue;
				var pr = pc.GetGlobalRect();
				if (pr.Size.X <= 0f || pr.Size.Y <= 0f) continue;   // noch nicht ausgelegt (Neuaufbau im selben Bild)
				bool frame = pc is PanelContainer || (pc.GetChildCount() > 0 && pc.GetChild(0) is Panel && pc is not ScrollContainer);
				bool vlist = pc is ScrollContainer sc0 && sc0.VerticalScrollMode != ScrollContainer.ScrollMode.Disabled;
				if (!pc.ClipContents && !frame && !vlist) continue;
				bool outsideX = full.Position.X < pr.Position.X - 1.5f || full.End.X > pr.End.X + 1.5f;
				bool outsideY = !inList && (full.Position.Y < pr.Position.Y - 1.5f || full.End.Y > pr.End.Y + 1.5f);
				// Runde 44h (Handy-Test r44 s02: „Rückansicht“ im Menü lag an der Scrollleiste): eine Taste/ein Text darf die sichtbare senkrechte
				// Scrollleiste der rollenden Liste nicht berühren oder überdecken — mindestens ScrollbarGap Abstand (die Liste muss um Leiste + Abstand schmaler sein)
				if (vlist && pc is ScrollContainer sc1 && sc1.GetVScrollBar() is { } vbar && vbar.IsVisibleInTree() && VisibleBar(sc1, vbar) is var vr && vr.Size.X > 0f
					&& full.Intersection(vr.GrowIndividual(ScrollbarGap, 0f, 0f, 0f)) is var ov && ov.Size.X > 0.5f && ov.Size.Y > 0.5f)
				{ why = "an/über der Scrollleiste"; break; }
				// eine rollende Liste (auch der als Ganzes rollende Bereich) darf unten/oben anschneiden — seitlich nicht
				if (vlist && !outsideX) { inList = true; continue; }
				if (!outsideX && !outsideY) continue;
				if (frame && !pc.ClipContents && full.Intersection(pr).Size.X <= 0f) continue;   // ganz außerhalb des Rahmens: Teil einer anderen Ebene (z. B. Überlagerung)
				why = $"{(outsideX ? "seitlich" : "senkrecht")} am Rahmen {pc.GetType().Name}";
			}
			// Runde 37 (Handytest r35 g31: „Verkaufen“ ragte über eine selbst gezeichnete Zeile — kein Panel, kein Clip, also nie gemeldet): eine Taste
			// darf nicht seitlich über die Fläche ihres nächsten ausgelegten Vorfahren (Größe > 0) hinausragen
			if (why is null && !drawn && owner is not Godot.Label && inter.Contains(owner))
			{
				for (Node? n = owner.GetParent(); n is not null; n = n.GetParent())
				{
					if (n is not Control pc || n is ScrollContainer) break;
					var pr = pc.GetGlobalRect();
					if (pr.Size.X <= 0.5f || pr.Size.Y <= 0.5f) continue;
					if (full.Position.X < pr.Position.X - 1.5f || full.End.X > pr.End.X + 1.5f) why = $"seitlich über {pc.GetType().Name}";
					break;
				}
			}
			if (why is not null) { clippedCount++; if (notes.Count < 56) notes.Insert(0, $"beschnitten „{Short(name)}“ ({why})"); }
		}
		return (texts, textOver, clippedCount, drawnTrunc);
	}

	/// <summary>Runde 44h: Mindestabstand (Bildpunkte) zwischen Inhalt und sichtbarer senkrechter Scrollleiste.</summary>
	public const float ScrollbarGap = 4f;

	/// <summary>Runde 44h: gezeichneter Teil der senkrechten Leiste (global) — ohne den durchsichtigen Abstand links, den <see cref="KitScroll"/> setzt.</summary>
	static Rect2 VisibleBar(ScrollContainer sc, VScrollBar bar)
	{
		var r = bar.GetGlobalRect();
		if (sc is SpaceAdventure.Ui.Kit.KitScroll ks && ks.AppliedBarGap > 0f && bar.Size.X > 0f)
		{
			float g = ks.AppliedBarGap * r.Size.X / bar.Size.X;
			r = new Rect2(r.Position.X + g, r.Position.Y, MathF.Max(0f, r.Size.X - g), r.Size.Y);
		}
		return r;
	}

	/// <summary>Runde 35b: Fläche, auf die die beschneidenden Vorfahren (ClipContents, auch der Knoten selbst) begrenzen — ohne den Knoten-Rahmen selbst.</summary>
	static Rect2 ClipOf(Control c)
	{
		var r = new Rect2(-1e6f, -1e6f, 2e6f, 2e6f);
		for (Node? n = c; n is not null; n = n.GetParent())
			if (n is Control pc && pc.ClipContents) r = r.Intersection(pc.GetGlobalRect());
		return r;
	}

	/// <summary>Runde 35b: wirksame Deckkraft (Modulate der Vorfahren × eigenes) — ein ausgeblendeter Hinweis liegt noch im Baum, ist aber unsichtbar.</summary>
	static float Alpha(CanvasItem c)
	{
		float a = c.SelfModulate.A;
		for (Node? n = c; n is CanvasItem ci; n = n.GetParent()) a *= ci.Modulate.A;
		return a;
	}

	static Rect2 Clipped(Control c)
	{
		var r = c.GetGlobalRect();
		for (var n = c.GetParent(); n is not null; n = n.GetParent())
			if (n is Control pc && pc.ClipContents) { r = r.Intersection(pc.GetGlobalRect()); if (r.Size.X <= 0f || r.Size.Y <= 0f) return new Rect2(); }
		return r;
	}

	/// <summary>Offener Vollbild-Bildschirm, Menü oder Sternenkarte: nur dessen Elemente zählen (das Flug-HUD darunter ist verdeckt).</summary>
	Control? Modal()
	{
		if (_m.HudRef.Map.Visible) return _m.HudRef.Map;
		return _m.HudRef.Screens.Current is Control sc && sc.IsVisibleInTree() && sc != (Control)_m.HudRef.Flight ? sc : null;
	}

	public static bool Interactive(Control c) => c is KitButton or SpaceAdventure.Ui.TouchButton or KitSlider or KitVSlider or BaseButton or LineEdit;

	public static IEnumerable<Control> Visible(Node n)
	{
		foreach (var ch in n.GetChildren())
		{
			if (ch is CanvasLayer cl && !cl.Visible) continue;
			if (ch is Control c)
			{
				if (!c.IsVisibleInTree() || c.IsQueuedForDeletion()) continue;
				yield return c;
			}
			foreach (var x in Visible(ch)) yield return x;
		}
	}

	Finding Measure(string state)
	{
		var (w, h, ppi, _) = Sizes[_sizeIdx];
		float f = Factors[_factorIdx];
		var vp = GetViewport().GetVisibleRect();
		// Runde 33: Faktor je Schicht — die Wurzel bleibt bei 1, die Flug-Schicht trägt den Faktor (Vollbilder bis E3–E5 bei 1)
		// Runde 33b: die Flug-Schicht darf unter dem Stufenfaktor liegen (FitFactor: lieber kleiner als zwei Tasten in einer Zelle), nie darüber
		if (MathF.Abs(UiScale.Factor - f) > 0.001f || _m.HudRef.Flight.Scale.X > f + 0.001f) GD.PrintErr($"[ui-check] Flug-Faktor {_m.HudRef.Flight.Scale.X} statt ≤ {f}");
		float rootF = GetTree().Root.ContentScaleFactor;   // globale Rechtecke sind in Bildpunkten dieser Wurzel (Skalierung der Schicht ist darin enthalten)
		if (GetTree().Root.Size != WindowFor(w, h)) GD.PrintErr($"[ui-check] Fenster {GetTree().Root.Size} statt {WindowFor(w, h)} ({w}×{h})");
		var font = ThemeDB.FallbackFont;
		var all = Visible(GetTree().Root).ToList();
		var modal = Modal();
		var inter = all.Where(c => Interactive(c) && c.Size.X > 1f && c.Size.Y > 1f && c.MouseFilter != Control.MouseFilterEnum.Ignore
			&& (modal is null || modal == c || modal.IsAncestorOf(c)) && Clipped(c).Size.X > 1f).ToList();
		var notes = new List<string>();
		int overlaps = 0, trunc = 0, small7 = 0, small5 = 0, outside = 0;
		for (int i = 0; i < inter.Count; i++)
			for (int j = i + 1; j < inter.Count; j++)
			{
				var a = Clipped(inter[i]); var b = Clipped(inter[j]);
				if (inter[i].IsAncestorOf(inter[j]) || inter[j].IsAncestorOf(inter[i])) continue;
				var x = a.Intersection(b);
				if (x.Size.X * x.Size.Y >= 25f) { overlaps++; if (notes.Count < 12) notes.Add($"Überlappung „{Label(inter[i])}“ × „{Label(inter[j])}“"); }
			}
		foreach (var c in inter)
		{
			var r = c.GetGlobalRect();
			float mm = UiLayout.Millimetres(MathF.Min(r.Size.X, r.Size.Y), w, h, rootF, ppi);
			if (mm < 7f) small7++;
			if (mm < 5f) { small5++; if (notes.Count < 24) notes.Add($"Tippfläche {mm:0.0} mm „{Label(c)}“"); }
			if (!vp.Grow(0.5f).Encloses(Clipped(c))) { outside++; if (notes.Count < 30) notes.Add($"außerhalb „{Label(c)}“ {r.Position.X:0},{r.Position.Y:0} {r.Size.X:0}×{r.Size.Y:0}"); }
		}
		// Runde 37c (Prüfung M3): sichere Ränder — keine Taste und kein Text (auch gezeichnet) in der Aussparung, für alle Ebenen
		var si = UiScale.SafeInsets1();
		if (si.Left + si.Top + si.Right + si.Bottom > 0.5f)
		{
			var safe = new Rect2(vp.Position + new Vector2(si.Left, si.Top), vp.Size - new Vector2(si.Left + si.Right, si.Top + si.Bottom)).Grow(0.5f);
			var alertLayer0 = _m.HudRef.TestAlertLayer;
			foreach (var c in inter.Where(c => !safe.Encloses(Clipped(c))))
			{ outside++; if (notes.Count < 34) notes.Add($"außerhalb der sicheren Fläche „{Label(c)}“"); }
			foreach (var c in all.Where(c => c is Label { Text.Length: > 0 } l && Alpha(c) >= 0.05f && (modal is null || modal == c || modal.IsAncestorOf(c) || alertLayer0.IsAncestorOf(c)) && Clipped(c).Size.X > 1f))
			{
				var lr = Clipped(c);
				if (!safe.Encloses(lr) && lr.Intersection(safe).Size.X * lr.Intersection(safe).Size.Y < lr.Size.X * lr.Size.Y - 4f) { outside++; if (notes.Count < 34) notes.Add($"Text außerhalb der sicheren Fläche „{Short(((Label)c).Text)}“"); }
			}
			var scopeIds = new HashSet<ulong>(all.Where(c => modal is null || modal == c || modal.IsAncestorOf(c) || alertLayer0.IsAncestorOf(c)).Select(c => c.GetInstanceId()));
			foreach (var e in SpaceAdventure.Ui.Kit.DrawnText.All)
			{
				if (!scopeIds.Contains(e.Owner) || GodotObject.InstanceFromId(e.Owner) is not Control oc) continue;
				var gt = oc.GetGlobalTransform();
				var r = ClipOf(oc).Intersection(new Rect2(gt * e.Local.Position, e.Local.Size * gt.Scale.Y));
				if (r.Size.X <= 1f || safe.Encloses(r)) continue;
				outside++; if (notes.Count < 34) notes.Add($"Text außerhalb der sicheren Fläche „{Short(e.Shown)}“ (gezeichnet)");
			}
		}
		// Runde 37b (Handytest r37 N3: Comms/System-Rahmen breiter als in den anderen Bereichen): Mittelteil-Rahmen hat genau die Größe aus dem Gerüst
		var cc = modal switch { SpaceAdventure.Ui.Screens.CommandScreen cs => cs.CenterCheck, SpaceAdventure.Ui.Screens.DockAreaScreen ds => ds.CenterCheck, _ => null };
		if (cc is { } chk && (MathF.Abs(chk.Actual.Size.X - chk.ExpectedSize.X) > 1.5f || MathF.Abs(chk.Actual.Size.Y - chk.ExpectedSize.Y) > 1.5f))
		{ outside++; notes.Add($"außerhalb: Rahmen des Mittelteils {chk.Actual.Size.X:0}×{chk.Actual.Size.Y:0} statt {chk.ExpectedSize.X:0}×{chk.ExpectedSize.Y:0}"); }
		foreach (var c in all.Where(c => (modal is null || modal == c || modal.IsAncestorOf(c)) && Clipped(c).Size.X > 1f))
		{
			if (c is Label l && l.Text.Length > 0)
			{
				// Runde 35b: umbrechende Labels sind gekürzt, wenn nicht alle Zeilen sichtbar sind (nicht, wenn der Text breiter ist als eine Zeile)
				bool cut = l.Text.Contains('…') || (l.AutowrapMode != TextServer.AutowrapMode.Off
					? l.GetVisibleLineCount() < l.GetLineCount()
					: l.ClipText && font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, l.GetThemeFontSize("font_size")).X > l.Size.X + 1f);
				if (cut) { trunc++; if (notes.Count < 36) notes.Add($"gekürzt „{Short(l.Text)}“"); }
			}
			else if (c is KitButton kb && kb.Text.Length > 0)
			{
				// Runde 35b: alle Zeilen der Taste (Text, Unterzeile, Detail) so, wie die Taste sie zeichnet
				if (kb.Overflow() is { Length: > 0 } ov) { trunc++; if (notes.Count < 36) notes.Add($"Taste gekürzt „{Short(ov)}“"); }
			}
		}
		// ── Runde 35b (Prüfung: das Werkzeug übersah sichtbare Mängel) ── gemeinsame Regel auch für den Selbsttest
		var scope = all.Where(c => (modal is null || modal == c || modal.IsAncestorOf(c))).ToList();
		var tc = TextChecks(scope, inter, notes);
		var texts = tc.Texts.Select(t => (t.Owner, t.R, t.Text, t.Eff)).ToList(); int textOver = tc.TextOverlaps, clippedCount = tc.Clipped; trunc += tc.DrawnTruncated;
		float minFont = texts.Count == 0 ? float.NaN : texts.Min(t => t.Eff);
		foreach (var b in inter.OfType<KitButton>()) minFont = MathF.Min(float.IsNaN(minFont) ? 999f : minFont, b.FontSize * b.GetGlobalTransform().Scale.Y);
		// Füllgrad eines Vollbild-Bildschirms: Fläche der sichtbaren Inhalte (Hülle) / sichtbare Fläche
		// Runde 34b (Prüfung B2): Meldung und Hinweis dürfen keine Taste und keinen Text verdecken (sichtbarer Teil, Überdeckung ≥ 25 px²)
		int msgCovers = 0;
		var overlays = new List<(string Name, Rect2 R)>();
		if (_m.HudRef.TestAlert.Length > 0) overlays.Add(("Meldung", _m.HudRef.TestAlertRect));
		if (_m.HudRef.ToastRect is { } toastR) overlays.Add(("Hinweis", toastR));
		var alertLayer = _m.HudRef.TestAlertLayer;
		foreach (var (oname, orect) in overlays)
			foreach (var c in all.Where(c => (c is Label { Text.Length: > 0 } || Interactive(c)) && Alpha(c) >= 0.05f && !alertLayer.IsAncestorOf(c) && (modal is null || modal == c || modal.IsAncestorOf(c))))
			{
				var x = Clipped(c).Intersection(orect);
				if (x.Size.X * x.Size.Y < 25f) continue;
				msgCovers++;
				if (notes.Count < 40) notes.Insert(0, $"{oname} verdeckt „{Label(c)}“");
			}
		// Runde 37b (Prüfauflage A2): auch selbst gezeichnete Texte (Sternenkarte, Karten, Zeilen) gelten als Inhalt, den Meldung/Hinweis nicht verdecken dürfen
		var drawnScope = new HashSet<ulong>(all.Where(c => (modal is null || modal == c || modal.IsAncestorOf(c)) && !alertLayer.IsAncestorOf(c)).Select(c => c.GetInstanceId()));
		foreach (var (oname, orect) in overlays)
			foreach (var e in SpaceAdventure.Ui.Kit.DrawnText.All)
			{
				if (!drawnScope.Contains(e.Owner) || GodotObject.InstanceFromId(e.Owner) is not Control oc) continue;
				var gt = oc.GetGlobalTransform();
				var r = ClipOf(oc).Intersection(new Rect2(gt * e.Local.Position, e.Local.Size * gt.Scale.Y));
				var x = r.Intersection(orect);
				if (r.Size.X <= 1f || x.Size.X * x.Size.Y < 25f) continue;
				msgCovers++;
				if (notes.Count < 40) notes.Insert(0, $"{oname} verdeckt „{Short(e.Shown)}“ (gezeichnet)");
			}
		// kleinste Tippfläche (mm) und von Inhalt belegte Fläche (Rasterprobe 48 × 27 über Texte, Tasten, Balken und gezeichnete Flächen; ohne Hintergründe)
		float minMm = inter.Count == 0 ? float.NaN : inter.Min(c => UiLayout.Millimetres(MathF.Min(c.GetGlobalRect().Size.X, c.GetGlobalRect().Size.Y), w, h, GetTree().Root.ContentScaleFactor, ppi));
		float content = -1f;
		if (modal is not null)
		{
			var leaves = Visible(modal).Where(c => c is Label { Text.Length: > 0 } || Interactive(c) || c is KitBar
				|| (c.GetType() != typeof(Control) && c is not Container && c is not Panel && c is not ColorRect && c is not ScrollBar && c.GetChildCount() == 0 && c.Size.X > 4f && c.Size.Y > 4f))
				.Select(Clipped).Where(r => r.Size.X > 1f && r.Size.Y > 1f).ToList();
			int hit = 0, n = 0;
			for (int iy = 0; iy < 27; iy++)
				for (int ix = 0; ix < 48; ix++)
				{
					var pt = vp.Position + new Vector2((ix + 0.5f) * vp.Size.X / 48f, (iy + 0.5f) * vp.Size.Y / 27f);
					n++;
					if (leaves.Exists(r => r.HasPoint(pt))) hit++;
				}
			content = (float)hit / n;
		}
		// Runde 33b: eigenes Schiff im Flug nicht verdeckt — keine sichtbare Taste, keine Meldung, kein Hinweis über der Schiffsmitte (±16 px)
		int covered = 0;
		float eff = modal is null ? _m.HudRef.Flight.Scale.X : modal.GetGlobalTransform().Scale.X;
		if (modal is null && _m.HudRef.Flight.IsVisibleInTree() && _m.HudRef.Flight.ShipScreen is { } sp)
		{
			var box = new Rect2(sp - new Vector2(16f, 16f), new Vector2(32f, 32f));
			var cover = inter.Where(c => Clipped(c).Intersects(box)).Select(c => Label(c)).ToList();
			if (_m.HudRef.AlertRect is { } ar && ar.Intersects(box)) cover.Add("Meldung");
			if (_m.HudRef.ToastRect is { } tr && tr.Intersects(box)) cover.Add("Hinweis");
			if (cover.Count > 0) { covered = 1; notes.Insert(0, $"Schiff verdeckt von {string.Join(", ", cover)} bei {sp.X:0},{sp.Y:0}"); }
		}
		float fill = -1f;
		if (_m.HudRef.Screens.Current is Control screen && screen.IsVisibleInTree() && screen is not SpaceAdventure.Ui.Screens.CommandMenu)
		{
			var kids = Visible(screen).Where(c => c.Size.X > 4f && c.Size.Y > 4f && c is not ColorRect).Select(c => c.GetGlobalRect()).ToList();
			if (kids.Count > 0)
			{
				var box = kids.Aggregate((a, b) => a.Merge(b)).Intersection(vp);
				fill = box.Size.X * box.Size.Y / (vp.Size.X * vp.Size.Y);
			}
		}
		// Runde 34b: Lagesumme der Tasten — eine Messung mitten im Aufbau (Raster noch ohne Layout, alle Tasten auf 0,0) gilt nicht als stabil
		float layoutSum = inter.Sum(c => c.GetGlobalRect().Position.X * 0.37f + c.GetGlobalRect().Position.Y);
		return new Finding(state.TrimEnd('2'), w, h, f, inter.Count, overlaps, trunc, small7, small5, outside, fill, covered, eff, msgCovers, content, minMm, all.Count, false, layoutSum, textOver, clippedCount, minFont, notes, KeyOf(Sizes[_sizeIdx]));
	}

	static string Label(Control c) => c switch { KitButton k => Short(k.Text), Label l => Short(l.Text), _ => c.GetType().Name };
	static string Short(string s) { s = s.Replace('\n', ' '); return s.Length > 40 ? s[..39] + "…" : s; }

	void Finish()
	{
		UiScale.TestPpi = null; UiScale.TestCap = null; UiScale.TestForce = null;
		UiScale.Apply();
		var sb = new StringBuilder("UI-PRÜFUNG (Runde 31/33/34): je Bildschirm × Fenster × UI-Faktor der Stufe (wirkt je Schicht; in Klammern der wirksame Faktor, wenn die Schicht ihn senken musste; S! = eigenes Schiff verdeckt; Runde 34b: M = Meldung/Hinweis über Taste/Text, B = von Inhalt belegte Fläche (Rasterprobe; F war das Hüllenmaß und entfällt), mm = kleinste Taste, ?! = instabil; Runde 35b: T = Text über Text/Taste (mit der Schrift gemessen, auch gezeichnete Texte), C = beschnitten (seitlich am Rahmen/Clip oder senkrecht ohne Rollen), S = kleinste wirksame Schrift in px) — Ü Überlappungen interaktiver Elemente, K gekürzte Texte, "
			+ "T7/T5 Tippflächen unter 7/5 mm (angenommene DPI), A außerhalb der Fläche, F Füllgrad eines Vollbild-Bildschirms\n");
		foreach (var (w, h, ppi, dev) in Sizes) sb.AppendLine($"  {w}×{h}: {dev}, {ppi:0} ppi, Taste 56 px = {UiLayout.Millimetres(56f, w, h, 1f, ppi):0.0} mm bei Faktor 1, DPI-Ziel {UiLayout.DpiFactor(w, h, ppi):0.00}×");
		foreach (float f in Factors)
		{
			sb.AppendLine($"\n== UI-Faktor {f:0.00} ==");
			sb.Append($"{"Bildschirm",-22}");
			foreach (var z in Sizes) sb.Append($" | {KeyOf(z),-22}");
			sb.AppendLine();
			foreach (var state in _results.Where(r => r.Factor == f).Select(r => r.State).Distinct())
			{
				sb.Append($"{state,-22}");
				foreach (var z in Sizes)
				{
					var r = _results.FirstOrDefault(x => x.State == state && x.Key == KeyOf(z) && x.Factor == f);
					sb.Append(r is null ? $" | {"–",-44}" : $" | {$"Ü{r.Overlaps} K{r.Truncated} T7 {r.Small7}/{r.Interactive} T5 {r.Small5} A{r.Outside}{(r.TextOverlaps > 0 ? $" T{r.TextOverlaps}" : "")}{(r.ClippedCount > 0 ? $" C{r.ClippedCount}" : "")}{(r.MessageCovers > 0 ? $" M{r.MessageCovers}" : "")}{(r.ShipCovered > 0 ? " S!" : "")}{(r.Content < 0 ? "" : $" B{r.Content * 100:0}%")}{(float.IsNaN(r.MinMm) ? "" : $" {r.MinMm:0.0}mm")}{(float.IsNaN(r.MinFont) ? "" : $" S{r.MinFont:0}")}{(r.Unstable ? " ?!" : "")}{(MathF.Abs(r.Effective - r.Factor) > 0.005f ? $" ({r.Effective:0.00})" : "")}",-44}");
				}
				sb.AppendLine();
			}
		}
		sb.AppendLine("\nEinzelbefunde (Faktor 1, 2340×1080):");
		foreach (var r in _results.Where(x => x.Factor == 1f && x.Key == "2340x1080"))
			foreach (var n in r.Notes.Take(8)) sb.AppendLine($"  {r.State}: {n}");
		string txt = System.IO.Path.Combine(_dir, "ui-check.txt");
		System.IO.File.WriteAllText(txt, sb.ToString());
		var json = new StringBuilder("[\n");
		foreach (var r in _results)
			json.AppendLine($"  {{\"size\":\"{r.Key}\",\"state\":\"{r.State}\",\"w\":{r.W},\"h\":{r.H},\"factor\":{r.Factor.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"interactive\":{r.Interactive},\"overlaps\":{r.Overlaps},\"truncated\":{r.Truncated},\"tapUnder7mm\":{r.Small7},\"tapUnder5mm\":{r.Small5},\"outside\":{r.Outside},\"shipCovered\":{r.ShipCovered},\"messageCovers\":{r.MessageCovers},\"textOverlaps\":{r.TextOverlaps},\"clipped\":{r.ClippedCount},\"minFontPx\":{(float.IsNaN(r.MinFont) ? "null" : r.MinFont.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))},\"content\":{r.Content.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)},\"minMm\":{(float.IsNaN(r.MinMm) ? "null" : r.MinMm.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))},\"unstable\":{(r.Unstable ? "true" : "false")},\"effective\":{r.Effective.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},\"fill\":{r.Fill.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}}},");
		System.IO.File.WriteAllText(System.IO.Path.Combine(_dir, "ui-check.json"), json.ToString().TrimEnd(',', '\n', '\r') + "\n]\n");
		GD.Print($"[ui-check] fertig: {txt}");
		// Runde 37b (Prüfauflage A3): bei Befunden „UI-CHECK FEHLER“ und Exit-Code 1; begründete Ausnahmen stehen in AllowedTruncations
		var bad = new List<string>();
		foreach (var r in _results)
		{
			int allowed = r.Notes.Count(n => n.StartsWith("gekürzt", StringComparison.Ordinal) && AllowedTruncations.Any(a => r.State == a.State && n.Contains(a.Pattern, StringComparison.Ordinal)));
			int trunc = Math.Max(0, r.Truncated - allowed);
			int sum = r.Overlaps + trunc + r.Outside + r.TextOverlaps + r.ClippedCount + r.MessageCovers + r.ShipCovered + (r.Unstable ? 1 : 0);
			if (sum > 0) bad.Add($"{r.Key} f{r.Factor:0.00} {r.State}: Ü{r.Overlaps} K{trunc} A{r.Outside} T{r.TextOverlaps} C{r.ClippedCount} M{r.MessageCovers}{(r.ShipCovered > 0 ? " S!" : "")}{(r.Unstable ? " instabil" : "")} [{string.Join("; ", r.Notes.Where(n => !n.StartsWith("Tippfläche", StringComparison.Ordinal)).Take(4))}]");
		}
		if (bad.Count == 0) { GD.Print("UI-CHECK OK"); GetTree().Quit(0); return; }
		foreach (var b in bad) GD.PrintErr("[ui-check] BEFUND " + b);
		GD.PrintErr($"UI-CHECK FEHLER: {bad.Count} Messungen mit Befunden");
		GetTree().Quit(1);
	}

	/// <summary>Runde 37b (A3): begründete Ausnahmen — gekürzte Texte, die bewusst so bleiben (Muster im Hinweis, Begründung).</summary>
	public static readonly (string State, string Pattern, string Why)[] AllowedTruncations =
	{
		("dockmenue-trade_station", "Einheitsf…", "Schiffs-Chip im Dock-Menü (126 px, vier je Zeile): lange NPC-Schiffsnamen in der Mitte gekürzt, voller Name im Hangar (Runde 35b)"),
	};
}
