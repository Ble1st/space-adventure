using System.Linq;
using System;
using System.IO;
using Godot;

namespace SpaceAdventure;

/// <summary>
/// Screenshot-Werkzeug fürs Review: Startargument <c>--ui-shots &lt;ordner&gt;</c> (nicht headless, braucht DISPLAY).
/// Lädt eine Demo-Welt, wartet bis die Szene steht, und speichert über RenderingServer.FramePostDraw +
/// Viewport.GetTexture().GetImage().SavePng PNGs: (a) Flug-HUD im Leerlauf, (b) HUD mit Ziel in
/// Laserreichweite, (c) HUD nahe an einer Station (Kontexttaste), (d) geöffnetes Hamburger-Menü,
/// dann das Dock: (e) Andock-Menü, (f) Handel Liste, (g) Handel Liste mit aufgeklappter Zeile,
/// (h) Handel Kacheln, (i) Station-Info, (j) Hangar, (k) Platzhalter-Bereich, Reparatur &amp; Tanken, Ausrüster (Fracht- und
/// Werkzeugslot) und Werft an Werft und Handelsstation (Phase 5c), dann die
/// Kommandozentrale (UI.md Abschnitt 4): (l) Menü, (m) Universum (Fraktionsebene), (n) Universum mit
/// gewähltem Sektor, (o) Sektor, (p) Fraktionsliste, (q) Fraktionsdetail, (r) Meine Fraktion,
/// (s) Flotte, (t) Besitz, (u) Schiffsdetail, (v) Comms, System mit Startpaket-Auswahl, (w) Flug-HUD mit gedrehter Kamera
/// (Yaw 60°, Pitch 40°), Phase 8: Baustelle im Flug, Station-Info der Baustelle, Sektoransicht mit Baustelle, Lager am Lagerhaus,
/// Flug-HUD mit Spalter, Phase 10: Sperren, Phase 11: HUD mit Fahndung und Lage, Lagekarte, Meine Fraktion, Behörde, Station-Info mit Lage,
/// Überfall-Warnung, Sektoransicht mit Polizei und Kaperschiff, Phase 12: Andock-Menü mit Hangar/Taxi, Hangar mit Flotte und Leihschiff, Taxi,
/// Flotte mit Befehlsraster und Zielwahl, Schiffsdetail mit Befehlen, Andock-Menü am Lagerhaus (neun Kacheln) – dann Ende.
/// Aufruf: godot-mono --path godot -- --ui-shots build/ui-shots/
/// </summary>
public partial class UiShots : Node
{
	readonly Main _m;
	readonly string _dir;
	int _stage, _wait;
	SpaceSim.Core.Model.StationId _site;   // Phase 8: Baustelle für die Bildschirmfotos
	SpaceSim.Core.Model.ShipId _nomad;     // Phase 12: Flottenschiff für die Bildschirmfotos

	bool _phase28, _stationsOnly;

	public UiShots(Main m, string dir)
	{
		_m = m;
		_dir = dir;
	}

	public override void _Ready()
	{
		SpaceAdventure.Ui.Kit.UiScale.PinnedFactor = 1f; SpaceAdventure.Ui.Kit.UiScale.Apply();   // Runde 33: Bilder im 1280er-Raster bei Faktor 1, unabhängig von der DPI des Rechners
		DirAccess.MakeDirRecursiveAbsolute(_dir);
		GD.Print($"[ui-shots] Zielordner: {_dir}");
		// Phase 14: --ui-shots-phase14 nimmt nur die Bilder der Phase 14 auf (Standardwelt mit Planeten und Exportsenke)
		if (OS.GetCmdlineUserArgs().Contains("--ui-shots-phase14")) _stage = 548;
		// Phase 21: --ui-shots-phase21 nur die Karten-Bilder der 11-Sektoren-Welt
		if (OS.GetCmdlineUserArgs().Contains("--ui-shots-phase21")) _stage = 561;
		// Phase 25: --ui-shots-phase25 nur die Kampfbilder
		if (OS.GetCmdlineUserArgs().Contains("--ui-shots-phase25")) _stage = 572;
		// Phase 26: --ui-shots-phase26 läuft über die Kampfbilder (Falke an der Werft) weiter zu den Schadens- und Bergungsbildern 66–68
		if (OS.GetCmdlineUserArgs().Contains("--ui-shots-phase26")) _stage = 572;
		// Phase 27: --ui-shots-phase27 wie Phase 26 und weiter zu den Bildern 69–77 (Station-Info, Ausrüster, Hangar, Polizeigefecht, Verband, Startpakete, Comms, Planet, Sternenkarte)
		if (OS.GetCmdlineUserArgs().Contains("--ui-shots-phase27")) _stage = 572;
		// Phase 28: --ui-shots-phase28 wie Phase 27 (Bilder 63–77, u. a. Ausrüster 70, Polizeigefecht 72, Sektoransicht 73) und weiter zu 78–81
		// (Stationsangriff mit Raketen, Station schießt zurück, Station-Info mit Verteidigung, Krieg in den Fraktionen)
		_phase28 = OS.GetCmdlineUserArgs().Contains("--ui-shots-phase28");
		if (_phase28) _stage = 572;
		// Runde 8: --ui-shots-stations nur die Bilder der Stationsmodelle (90–92)
		_stationsOnly = OS.GetCmdlineUserArgs().Contains("--ui-shots-stations");
		if (_stationsOnly) _stage = 594_1;
		// Jumpgate-Modelle: --ui-shots-gates nur die Torbilder (93–96, UiShots.Gates.cs)
		if (OS.GetCmdlineUserArgs().Contains("--ui-shots-gates")) _stage = GateShotsStart;
		// Runde 11: --ui-shots-ships nur die Bilder der Schiffsmodelle (97–98, UiShots.Ships.cs)
		if (OS.GetCmdlineUserArgs().Contains("--ui-shots-ships")) _stage = ShipShotsStart;
		// Himmelskugeln: --ui-shots-sky nur die Himmelsbilder je Sektor (UiShots.Sky.cs)
		if (OS.GetCmdlineUserArgs().Contains("--ui-shots-sky")) _stage = SkyShotsStart;
		_wait = 30;   // erst einmal die Szene stehen lassen
	}

	public override void _Process(double delta)
	{
		if (_wait > 0 && --_wait > 0) return;
		// Setup und Aufnahme in getrennten Frames, damit die Aufnahme den fertigen Zustand zeigt
		switch (_stage)
		{
			case 0: Capture("01-hud-leerlauf.png"); _stage = 1; _wait = 15; break;
			case 1: _m.TestAimAtNearestRock(); _stage = 2; _wait = 15; break;
			case 2: Capture("02-hud-ziel-in-laserreichweite.png"); _stage = 3; _wait = 15; break;
			case 3: _m.TestTeleportNearStation(); _stage = 4; _wait = 15; break;
			case 4: Capture("03-hud-nahe-station-kontext.png"); _stage = 5; _wait = 15; break;
			case 5: _m.TestOpenCommand(); _stage = 6; _wait = 15; break;
			case 6: Capture("04-hamburger-menue.png"); _stage = 60; _wait = 10; break;
			case 60: _m.HudRef.Screens.Hide(); _stage = 7; _wait = 10; break;   // Schließen erst im Folgeframe, sonst fehlt das Menü im Bild
			// ── Dock-Bilder (UI.md Abschnitt 3) ──
			case 7: _m.TestTeleportNearStation(); _stage = 8; _wait = 15; break;
			case 8: _m.TestDockNearest(); _stage = 9; _wait = 90; break;   // Einflug-Tween abwarten
			case 9: Capture("05-dock-menue.png"); _stage = 10; _wait = 15; break;
			case 10: _m.TestOpenDockArea("trade"); _stage = 11; _wait = 20; break;
			case 11: Capture("06-dock-handel-liste.png"); _stage = 12; _wait = 10; break;
			case 12: _m.HudRef.DockTrade.TestExpandFirst(); _stage = 13; _wait = 20; break;
			case 13: Capture("07-dock-handel-zeile-aufgeklappt.png"); _stage = 14; _wait = 10; break;
			case 14: _m.HudRef.DockTrade.TestSelectFirstTile(); _stage = 15; _wait = 20; break;
			case 15: Capture("08-dock-handel-kacheln.png"); _stage = 16; _wait = 10; break;
			case 16: _m.TestOpenDockArea("info"); _stage = 17; _wait = 20; break;
			case 17: Capture("09-dock-station-info.png"); _stage = 18; _wait = 10; break;
			case 18: _m.TestOpenDockArea("hangar"); _stage = 19; _wait = 20; break;
			case 19: Capture("10-dock-hangar.png"); _stage = 20; _wait = 10; break;
			case 20: _m.TestOpenDockArea("repair"); _stage = 21; _wait = 20; break;
			case 21: Capture("11-dock-bereich-platzhalter.png"); _stage = 210; _wait = 10; break;
			case 210: // Reparatur & Tanken (Phase 4c) mit einer Raffinerie als Station, Tank halb leer
			{
				var u = _m.Session.U;
				var prop = u.FindGood("propellant")!.Id;
				_m.Session.Player.Fuel[prop] = 80f;
				_m.Session.U.PlayerWallet.Credits = 4000f;
				_m.HudRef.DockCtx.Station = u.Stations.Values.First(x => x.TypeKey == "fuel_refinery");
				_m.HudRef.Screens.Show(_m.HudRef.DockRepair);
				_stage = 211; _wait = 20; break;
			}
			case 211: Capture("11b-dock-reparatur-tanken.png"); _stage = 212; _wait = 10; break;
			case 212: // Phase 5c: Ausrüster an der Werft, Frachtslot gewählt
			{
				var u = _m.Session.U;
				var yard = u.Stations.Values.First(x => x.TypeKey == "shipyard");
				_m.Session.U.PlayerWallet.Credits = 4000f;
				_m.HudRef.DockCtx.Station = yard;
				_m.HudRef.DockCtx.Ship = _m.Session.Player;
				_m.HudRef.DockOutfitter.TestSelectSlot(_m.Session.Player.Slots.FindIndex(x => x.Type == "cargo"));
				_m.HudRef.Screens.Show(_m.HudRef.DockOutfitter);
				_stage = 213; _wait = 20; break;
			}
			case 213: Capture("11c-dock-ausruester.png"); _stage = 214; _wait = 10; break;
			case 214: _m.HudRef.DockOutfitter.TestSelectSlot(_m.Session.Player.Slots.FindIndex(x => x.Type == "tool")); _stage = 215; _wait = 20; break;
			case 215: Capture("11d-dock-ausruester-werkzeug.png"); _stage = 2151; _wait = 10; break;
			case 2151: // Phase 7: Abschnitt „Verbessern“ (Dienst „upgrade“) am Triebwerk-Slot, ein Fehlschlag für den Mitleidszähler
			{
				var u = _m.Session.U;
				var pl = _m.Session.Player;
				int engine = pl.Slots.FindIndex(x => x.Type == "engine");
				pl.UpgradePity[engine] = 1;
				_m.HudRef.DockOutfitter.TestSelectSlot(engine);
				_stage = 2152; _wait = 20; break;
			}
			case 2152: Capture("11g-dock-ausruester-upgrade.png"); _stage = 216; _wait = 10; break;
			case 216: _m.HudRef.Screens.Show(_m.HudRef.DockShipyard); _stage = 217; _wait = 20; break;   // Werft: alle Klassen
			case 217: Capture("11e-dock-werft.png"); _stage = 218; _wait = 10; break;
			case 218: // Handelsstation: nur Skiffs
			{
				var u = _m.Session.U;
				_m.HudRef.DockCtx.Station = u.Stations.Values.First(x => x.TypeKey == "trade_station");
				_m.HudRef.Screens.Hide();   // neu öffnen, damit der Bereich mit der anderen Station aufbaut
				_stage = 2185; _wait = 5; break;
			}
			case 2185: _m.HudRef.Screens.Show(_m.HudRef.DockShipyard); _stage = 219; _wait = 20; break;
			case 219: Capture("11f-dock-werft-handelsstation.png"); _stage = 22; _wait = 10; break;
			// ── Kommandozentrale (UI.md Abschnitt 4) ──
			// Aufnahme und Zustandsänderung nie im selben Frame (FramePostDraw feuert nach _Process):
			// jeder Schritt wechselt nur eine Sache, die Aufnahme passiert im Folgeframe
			case 22: _m.TestUndock(); _stage = 23; _wait = 60; break;   // Abdocken, dann ist der Flug-HUD wieder da
			case 23: _m.TestOpenCommand(); _stage = 24; _wait = 15; break;
			case 24: Capture("12-kommandozentrale-menue.png"); _stage = 25; _wait = 10; break;
			case 25: _m.HudRef.Screens.Hide(); _stage = 26; _wait = 10; break;
			case 26: _m.TestOpenCcArea("universe"); _stage = 27; _wait = 20; break;
			case 27: Capture("13-cc-universum.png"); _stage = 28; _wait = 10; break;
			case 28: _m.TestCcSelectSector(new SpaceSim.Core.Model.SectorId(4)); _stage = 29; _wait = 20; break;
			case 29: Capture("14-cc-universum-gewaehlter-sektor.png"); _stage = 30; _wait = 10; break;
			case 30: _m.TestOpenCcArea("sector"); _stage = 31; _wait = 20; break;
			case 31: Capture("15-cc-sektor.png"); _stage = 32; _wait = 10; break;
			case 32:
				// Phase 9: ein freier Händler steigt zum Einzelakteur auf, damit die Liste alle Arten zeigt
				if (_m.Session.U.Ships.Values.Where(x => x.Role == SpaceSim.Core.Model.ShipRole.Trader && x.Owner == _m.Session.U.FreeActor).OrderBy(x => x.Id.Value).FirstOrDefault() is { } fs)
					SpaceSim.Core.Simulation.ActorRules.Promote(_m.Session.U, fs);
				_m.TestOpenCcArea("factions"); _stage = 33; _wait = 20; break;
			case 33: Capture("16-cc-fraktionen.png"); _stage = 34; _wait = 10; break;
			case 34: _m.TestCcSelectFaction(new SpaceSim.Core.Model.FactionId(2)); _stage = 35; _wait = 20; break;
			case 35: Capture("17-cc-fraktionsdetail.png"); _stage = 36; _wait = 10; break;
			case 36: _m.TestOpenCcArea("myfaction"); _stage = 37; _wait = 20; break;
			case 37: Capture("18-cc-meine-fraktion.png"); _stage = 38; _wait = 10; break;
			case 38: _m.TestOpenCcArea("fleet"); _stage = 39; _wait = 20; break;
			case 39: Capture("19-cc-flotte.png"); _stage = 40; _wait = 10; break;
			case 40: _m.TestOpenCcArea("assets"); _stage = 41; _wait = 20; break;
			case 41: Capture("20-cc-besitz.png"); _stage = 42; _wait = 10; break;
			case 42: _m.TestCcOpenShipDetail(); _stage = 43; _wait = 20; break;
			case 43: Capture("21-cc-schiffsdetail.png"); _stage = 44; _wait = 10; break;
			case 44: _m.TestOpenCcArea("comms"); _stage = 45; _wait = 20; break;
			case 45: Capture("22-cc-comms.png"); _stage = 450; _wait = 10; break;
			case 450: _m.TestOpenCcArea("system"); _m.HudRef.CcSystem.TestChoosing = true; _stage = 451; _wait = 20; break;   // Phase 5c: Startpakete
			case 451: Capture("22b-cc-system-startpakete.png"); _stage = 46; _wait = 10; break;
			case 46: _m.TestCloseCommand(); _stage = 47; _wait = 10; break;
			case 47: _m.TestSetCam(60f, 40f, 55f); _stage = 48; _wait = 20; break;   // Kamera drehen (HUD bleibt sichtbar)
			case 48: Capture("23-hud-kamera-gedreht.png"); _stage = 480; _wait = 5; break;
			// ── Phase 8: Baustelle, Lagerhaus, Spalter ──
			case 480: // Baustelle im Startsektor, teilweise beliefert; Kamera zurück
			{
				_m.TestResetCam();
				var u = _m.Session.U;
				var sec = u.Sectors[u.PlayerSector];
				var spot = SpaceSim.Core.Simulation.StationLifecycleSystem.FindFreeSpot(u, sec)!.Value;
				var site = SpaceSim.Core.Simulation.ConstructionRules.StartSite(u, u.StationTypes["goods_factory"], sec.Id, spot, sec.Owner, 7000f, null, null);
				site.Stock(u.FindGood("steel")!.Id).Amount = 40f;
				site.Stock(u.FindGood("electronics")!.Id).Amount = 3f;
				_site = site.Id;
				_m.TestTeleportNear(site.Id);
				_stage = 481; _wait = 40; break;
			}
			case 481: Capture("24-hud-baustelle.png"); _stage = 482; _wait = 10; break;
			case 482: _m.TestDockAt(_site); _stage = 483; _wait = 90; break;
			case 483: _m.TestOpenDockArea("info"); _stage = 484; _wait = 20; break;
			case 484: Capture("25-dock-baustelle-info.png"); _stage = 485; _wait = 10; break;
			case 485: _m.TestUndock(); _stage = 486; _wait = 60; break;
			case 486: _m.TestOpenCcArea("sector"); _m.HudRef.CcSector.TestSelectStation(_site); _stage = 487; _wait = 20; break;
			case 487: Capture("26-cc-sektor-baustelle.png"); _stage = 488; _wait = 10; break;
			case 488: _m.TestCloseCommand(); _m.TestEnterSector(new SpaceSim.Core.Model.SectorId(3)); _stage = 489; _wait = 20; break;
			case 489: // Lagerhaus Gamma: Fracht an Bord, ein Posten schon eingelagert
			{
				var u = _m.Session.U;
				var wh = u.Stations.Values.First(x => x.TypeKey == "warehouse");
				var pl = _m.Session.Player;
				pl.Cargo[u.FindGood("ice")!.Id] = 24f;
				pl.Cargo[u.FindGood("copper_ore")!.Id] = 12f;
				wh.PlayerStorage[u.FindGood("titanium")!.Id] = 6f;
				_m.Session.U.PlayerWallet.Credits = 2500f;
				_m.TestDockAt(wh.Id);
				_stage = 490; _wait = 90; break;
			}
			case 490: _m.TestOpenDockArea("storage"); _stage = 491; _wait = 20; break;
			case 491: Capture("27-dock-lager.png"); _stage = 492; _wait = 10; break;
			case 492: _m.TestUndock(); _stage = 493; _wait = 180; break;
			case 493: // Spaltlaser im Werkzeug-Slot, größter Gürtel-Asteroid als Ziel
			{
				var pl = _m.Session.Player;
				int tool = pl.Slots.FindIndex(x => x.Type == "tool");
				if (tool >= 0) { pl.Fit[tool] = "split_laser_s"; pl.RecomputeStats(_m.Session.U); }
				_m.TestAimAtBiggestRock();
				_stage = 494; _wait = 30; break;
			}
			case 494: Capture("28-hud-spalter.png"); _stage = 495; _wait = 5; break;
			case 495: // Phase 10: Gilde sieht die Union feindlich — Gamma (Kontrolle) Stufe 3, Delta (Dominanz) Stufe 2
			{
				var uni = _m.Session.U;
				SpaceSim.Core.Simulation.Relations.Set(uni, new SpaceSim.Core.Model.FactionId(2), new SpaceSim.Core.Model.FactionId(1), -0.9f);
				SpaceSim.Core.Simulation.FactionPolicySystem.SetLevel(uni, uni.Sectors[new SpaceSim.Core.Model.SectorId(3)], 3);
				SpaceSim.Core.Simulation.FactionPolicySystem.SetLevel(uni, uni.Sectors[new SpaceSim.Core.Model.SectorId(4)], 2);
				_m.TestEnterSector(new SpaceSim.Core.Model.SectorId(2));
				_m.TestOpenCcArea("universe");
				_m.HudRef.CcUniverse.CurrentLayer = Ui.Screens.UniverseScreen.Layer.Security;
				_m.TestCcSelectSector(new SpaceSim.Core.Model.SectorId(3));
				_stage = 496; _wait = 20; break;
			}
			case 496: Capture("29-cc-universum-sicherheit-sperre.png"); _stage = 497; _wait = 10; break;
			case 497:
				_m.HudRef.CcUniverse.CurrentLayer = Ui.Screens.UniverseScreen.Layer.Faction;
				_m.TestCcSelectSector(new SpaceSim.Core.Model.SectorId(4));
				_stage = 498; _wait = 20; break;
			case 498: Capture("30-cc-universum-fraktion-zoll.png"); _stage = 499; _wait = 10; break;
			case 499: _m.TestOpenCcArea("sector"); _m.TestCcSelectSectorView(new SpaceSim.Core.Model.SectorId(2)); _stage = 500; _wait = 20; break;
			case 500: Capture("31-cc-sektor-gesperrtes-gate.png"); _stage = 501; _wait = 10; break;
			case 501:
			{
				_m.TestCloseCommand();
				var uni = _m.Session.U;
				var st = uni.Stations.Values.First(x => x.Sector.Value == 2 && x.Market.Count > 0);
				_m.TestDockAt(st.Id);
				_stage = 502; _wait = 90; break;
			}
			case 502:
				_m.HudRef.DockCtx.Station = _m.Session.U.Stations.Values.First(x => x.Sector.Value == 3 && x.TypeKey == "colony");
				_m.TestOpenDockArea("info"); _stage = 503; _wait = 20; break;
			case 503: Capture("32-dock-station-info-politik.png"); _stage = 504; _wait = 10; break;
			// ── Phase 11: Lagekarte, Fahndung, Behörde, Überfall-Warnung, Polizei und Piraten im Sektor ──
			case 504:
			{
				_m.HudRef.DockCtx.Station = _m.Session.U.Stations.Values.First(x => x.Sector.Value == 2 && x.Market.Count > 0);
				_m.TestUndock();
				var uni = _m.Session.U;
				SpaceSim.Core.Simulation.FactionPolicySystem.SetLevel(uni, uni.Sectors[new SpaceSim.Core.Model.SectorId(3)], 0);
				SpaceSim.Core.Simulation.FactionPolicySystem.SetLevel(uni, uni.Sectors[new SpaceSim.Core.Model.SectorId(4)], 0);
				SpaceSim.Core.Simulation.Relations.Set(uni, new SpaceSim.Core.Model.FactionId(2), new SpaceSim.Core.Model.FactionId(1), 0.25f);
				SpaceSim.Core.Simulation.ThreatMap.Report(uni, new SpaceSim.Core.Model.SectorId(4), 3f);
				SpaceSim.Core.Simulation.ThreatMap.Report(uni, new SpaceSim.Core.Model.SectorId(2), 1.2f);
				SpaceSim.Core.Simulation.Wanted.Report(uni, uni.PlayerActor, new SpaceSim.Core.Model.FactionId(2), "toll_evasion");
				SpaceSim.Core.Simulation.Wanted.Report(uni, uni.PlayerActor, new SpaceSim.Core.Model.FactionId(2), "smuggling");
				uni.Rules.PlayerRaids = false;
				_stage = 505; _wait = 120; break;
			}
			case 505: _m.TestEnterSector(new SpaceSim.Core.Model.SectorId(3)); _stage = 506; _wait = 30; break;
			case 506: Capture("33-hud-fahndung-lage.png"); _stage = 507; _wait = 10; break;
			case 507:
				_m.TestOpenCommand(); _m.TestOpenCcArea("universe");
				_m.HudRef.CcUniverse.CurrentLayer = Ui.Screens.UniverseScreen.Layer.Security;
				_m.TestCcSelectSector(new SpaceSim.Core.Model.SectorId(4));
				_stage = 508; _wait = 20; break;
			case 508: Capture("34-cc-universum-lagekarte.png"); _stage = 509; _wait = 10; break;
			case 509: _m.HudRef.CcUniverse.CurrentLayer = Ui.Screens.UniverseScreen.Layer.Faction; _m.TestOpenCcArea("myfaction"); _stage = 510; _wait = 20; break;
			case 510: Capture("35-cc-meine-fraktion-fahndung.png"); _stage = 511; _wait = 10; break;
			case 511:
			{
				_m.TestCloseCommand();
				var uni = _m.Session.U;
				uni.Player.BountyClaims[new SpaceSim.Core.Model.FactionId(2)] = 300f;
				var hq = uni.Stations.Values.First(x => x.Sector.Value == 3 && x.TypeKey == "headquarters");
				_m.TestDockAt(hq.Id);
				_stage = 512; _wait = 90; break;
			}
			case 512: Capture("36-dock-menue-behoerde.png"); _stage = 513; _wait = 10; break;
			case 513: _m.TestOpenDockArea("authority"); _stage = 514; _wait = 20; break;
			case 514: Capture("37-dock-behoerde.png"); _stage = 515; _wait = 10; break;
			case 515: _m.TestOpenDockArea("info"); _stage = 516; _wait = 20; break;
			case 516: Capture("38-dock-station-info-lage.png"); _stage = 517; _wait = 10; break;
			case 517: _m.TestUndock(); _stage = 518; _wait = 120; break;
			case 518: // Überfall-Vorwarnung: Kaperschiff neben dem Spieler abseits der Stationen
			{
				var uni = _m.Session.U;
				var sector = uni.Sectors[uni.PlayerSector];
				var gatePos = uni.Gates[sector.Gates[0]].Position;
				var spot = gatePos;
				for (int k = 0; k < 24; k++)
				{
					var c = gatePos + SpaceSim.Core.Model.Vec2.FromAngle(k * 0.5236f) * (25f + 10f * (k / 12));
					if (sector.Stations.All(id => SpaceSim.Core.Model.Vec2.Distance(uni.Stations[id].Position, c) > 60f)) { spot = c; break; }
				}
				_m.TestTeleportTo(spot);
				var pl = _m.Session.Player;
				pl.Cargo[uni.FindGood("food")!.Id] = 40f;
				var raider = uni.Ships.Values.Where(SpaceSim.Core.Simulation.ThreatMap.IsRaider).OrderBy(x => x.Id.Value).First();
				if (raider.Transit != null) uni.FinishTransit(raider);
				if (raider.Sector != sector.Id) uni.MoveShip(raider, sector.Id);
				raider.Position = spot + new SpaceSim.Core.Model.Vec2(14f, -6f);
				raider.Order = new SpaceSim.Core.Model.PatrolOrder(sector.Id);
				raider.NextThinkTick = uni.Tick + 100_000;
				uni.Rules.PlayerRaids = true; uni.Rules.RaidRatePerTick = 1f; uni.Rules.PlayerRaidWeight = 1e6f; uni.Rules.InterceptRatePerTick = 0f;
				uni.Rules.PlayerWarningTicks = 400;
				_stage = 519; _wait = 5; break;
			}
			case 519:
				if (_m.LastSecurityMessage.StartsWith("Piraten")) { _stage = 520; _wait = 20; }
				break;
			case 520: Capture("39-hud-ueberfall-warnung.png"); _stage = 521; _wait = 10; break;
			case 521: _m.TestOpenCommand(); _m.TestOpenCcArea("sector"); _m.TestCcSelectSectorView(_m.Session.U.PlayerSector); _stage = 522; _wait = 20; break;
			case 522: Capture("40-cc-sektor-polizei-piraten.png"); _stage = 523; _wait = 10; break;
			// ── Phase 12: Spielerflotte, Hangar, Taxi, Leihschiff, Befehle ──
			case 523:
			{
				_m.TestCloseCommand();
				var u = _m.Session.U;
				u.Rules.PlayerRaids = false; u.PendingEncounters.Clear();
				var hub = u.Stations.Values.First(x => x.TypeKey == "trade_station");
				var yard = u.Stations.Values.First(x => x.TypeKey == "shipyard");
				SpaceSim.Core.Model.Ship Own(string model, SpaceSim.Core.Model.Station at)
				{
					var m = u.ShipModels[model];
					var s = SpaceSim.Core.Simulation.ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit,
						SpaceSim.Core.Model.ShipRole.Player, SpaceSim.Core.Model.Controller.Player, u.PlayerActor, at, 0f);
					s.HomeBase = at.Id;
					u.AddShip(s);
					return s;
				}
				_nomad = Own("nomad", hub).Id;
				Own("pip", hub);
				var w = Own("warden", yard);
				SpaceSim.Core.Simulation.Commands.Give(u, w, new SpaceSim.Core.Model.PatrolCommand(new[] { yard.Sector, hub.Sector }), out _);
				var pr = Own("prospector", yard);
				SpaceSim.Core.Simulation.Commands.Give(u, pr, new SpaceSim.Core.Model.MineCommand(new SpaceSim.Core.Model.AutoLimits(yard.Sector, 1, 0f, 0f)), out _);
				var lead = Own("pip", yard);
				var sq = SpaceSim.Core.Simulation.Squadrons.Create(u, lead, "Staffel Delta", out _)!;
				SpaceSim.Core.Simulation.Squadrons.Join(u, sq, Own("pip", yard), out _);
				var npc = u.Ships.Values.Where(x => !x.IsPlayer && x.Transit is null).OrderBy(x => x.Role == SpaceSim.Core.Model.ShipRole.Trader ? 0 : 1).FirstOrDefault();
				if (npc is not null) SpaceSim.Core.Simulation.PlayerFleet.Dock(u, npc, hub);   // ein fremdes Schiff im Hangar
				_m.Session.U.PlayerWallet.Credits = 6000f;
				_m.TestDockAt(hub.Id);
				_stage = 524; _wait = 90; break;
			}
			case 524: Capture("41-dock-menue-hangar-taxi.png"); _stage = 525; _wait = 10; break;
			case 525: _m.TestOpenDockArea("hangar"); _stage = 526; _wait = 20; break;
			case 526: Capture("42-dock-hangar-flotte.png"); _stage = 527; _wait = 10; break;
			case 527: _m.TestOpenDockArea("taxi"); _stage = 528; _wait = 20; break;
			case 528: Capture("43-dock-taxi.png"); _stage = 529; _wait = 10; break;
			case 529: _m.TestFleetSelect(_nomad); _stage = 530; _wait = 20; break;
			case 530: Capture("44-cc-flotte-befehle.png"); _stage = 531; _wait = 10; break;
			case 531: _m.HudRef.CcFleet.TestChoose("goto"); _stage = 532; _wait = 20; break;
			case 532: Capture("45-cc-flotte-zielwahl.png"); _stage = 533; _wait = 10; break;
			case 533: _m.TestOpenCcArea("assets"); _m.HudRef.CcAssets.TestOpenShipDetail(_nomad); _stage = 534; _wait = 20; break;
			case 534: Capture("46-cc-schiffsdetail-befehle.png"); _stage = 535; _wait = 10; break;
			case 535:
			{
				_m.TestCloseCommand();
				_m.TestUndock();
				var wh = _m.Session.U.Stations.Values.First(x => x.TypeKey == "warehouse");
				_m.TestDockAt(wh.Id);
				_stage = 536; _wait = 90; break;
			}
			case 536: Capture("47-dock-menue-lagerhaus-neun-kacheln.png"); _stage = 537; _wait = 10; break;
			case 537: // Phase 13: Forschungsinstitut der Union — Bereich „Forschung“ mit etwas Fortschritt (ein Projekt erforscht, eins halb)
			{
				var u = _m.Session.U;
				var union = u.Factions[new SpaceSim.Core.Model.FactionId(1)];
				var fusion = u.Research.Get("fusion_mk3");
				if (fusion is not null && !union.Research.IsCompleted(fusion.Key)) SpaceSim.Core.Simulation.Research.AddPoints(u, union, fusion, fusion.Points);
				if (u.Research.Get("precision_lasers") is { } las && !union.Research.IsCompleted(las.Key)) union.Research.Progress[las.Key] = las.Points * 0.45f;
				_m.TestUndock();
				var lab = u.Stations.Values.First(x => x.TypeKey == "research_station" && x.Sector.Value == 1);
				u.PlayerWallet.Credits = 12000f;
				_m.TestDockAt(lab.Id);
				_stage = 538; _wait = 90; break;
			}
			case 538: Capture("48-dock-menue-forschungsstation.png"); _stage = 539; _wait = 10; break;
			case 539: _m.TestOpenDockArea("research"); _stage = 540; _wait = 20; break;
			case 540: Capture("49-dock-forschung.png"); _stage = 541; _wait = 10; break;
			case 541: _m.TestOpenCommand(); _m.TestOpenCcArea("factions"); _m.HudRef.CcFactions.TestSelect(new SpaceSim.Core.Model.FactionId(1)); _stage = 542; _wait = 20; break;
			case 542: Capture("50-cc-fraktionen-forschung.png"); _stage = 543; _wait = 10; break;
			case 543:
			{
				_m.TestCloseCommand();
				_m.TestUndock();
				var yard = _m.Session.U.Stations.Values.First(x => x.TypeKey == "shipyard");
				_m.TestDockAt(yard.Id);
				_stage = 544; _wait = 90; break;
			}
			case 544: _m.TestOpenDockArea("shipyard"); _m.HudRef.DockShipyard.TestScrollToEnd(); _stage = 545; _wait = 20; break;
			case 545: Capture("51-dock-werft-nicht-erforscht.png"); _stage = 546; _wait = 10; break;
			case 546: _m.TestOpenDockArea("outfitter"); _m.HudRef.DockOutfitter.TestSelectSlot(_m.Session.Player.Slots.FindIndex(sl => sl.Type == "reactor")); _stage = 547; _wait = 20; break;
			case 547: Capture("52-dock-ausruester-nicht-erforscht.png"); _stage = 548; _wait = 10; break;
			// Phase 14: Standardwelt (Treibstoff und Forschung aus) — Flug in Gamma mit Planet, Exportsenke in der Stationsinfo, Planet im Universum
			case 548: _m.TestCloseCommand(); _m.TestUndock(); _m.TestMenu("new"); _stage = 549; _wait = 30; break;
			case 549:
			{
				var u = _m.Session.U;
				u.Rules.PlayerRaids = false;
				_m.TestEnterSector(new SpaceSim.Core.Model.SectorId(3));
				_m.TestTeleportNear(new SpaceSim.Core.Model.StationId(39));
				_stage = 550; _wait = 40; break;
			}
			case 550: Capture("53-flug-gamma-planet-ohne-tank.png"); _stage = 551; _wait = 10; break;
			case 551:
			{
				var u = _m.Session.U;
				var post = u.Stations[new SpaceSim.Core.Model.StationId(39)];
				SpaceSim.Core.Simulation.ExportRules.OnDelivered(u, post, u.FindGood("luxury_goods")!.Id, 4f, 2000f);
				SpaceSim.Core.Simulation.ExportRules.OnDelivered(u, post, u.FindGood("food")!.Id, 60f, 700f);
				_m.TestDockAt(post.Id);
				_stage = 552; _wait = 60; break;
			}
			case 552: _m.TestOpenDockArea("info"); _stage = 553; _wait = 20; break;
			case 553: Capture("54-dock-info-export.png"); _stage = 554; _wait = 10; break;
			case 554: _m.TestOpenDockArea("trade"); _stage = 555; _wait = 20; break;
			case 555: Capture("55-dock-handel-exportsenke.png"); _stage = 556; _wait = 10; break;
			case 556: _m.TestUndock(); _m.TestOpenCommand(); _m.TestOpenCcArea("universe"); _m.TestCcSelectSector(new SpaceSim.Core.Model.SectorId(3)); _stage = 557; _wait = 20; break;
			case 557: Capture("56-cc-universum-planet.png"); _stage = 558; _wait = 10; break;
			case 558: // Phase 16: Stahl an Bord am Stahlwerk — die Verkaufszeile nennt den Grund („Stellt die Ware selbst her“)
			{
				var u = _m.Session.U;
				var works = u.Stations.Values.First(x => x.TypeKey == "steelworks" && !x.IsBuildSite);
				_m.TestCloseCommand();
				_m.Session.Player.Cargo[u.FindGood("steel")!.Id] = 10f;
				_m.TestDockAt(works.Id);
				_stage = 559; _wait = 60; break;
			}
			case 559: _m.TestOpenDockArea("trade"); _m.HudRef.DockTrade.TestRebuild(); _m.HudRef.DockTrade.TestExpand(_m.Session.U.FindGood("steel")!.Id); _stage = 560; _wait = 20; break;
			case 560: Capture("57-dock-handel-annahmegrund.png"); _stage = 561; _wait = 10; break;
			// Phase 21: 11-Sektoren-Karte — Universum (Fraktion, Schiffe, Lage), Sternenkarte, Taxiliste
			case 561:
				_m.TestUndock(); _m.TestCloseCommand(); _m.TestOpenCommand(); _m.TestOpenCcArea("universe");
				_m.HudRef.CcUniverse.CurrentLayer = Ui.Screens.UniverseScreen.Layer.Faction;
				_m.TestCcSelectSector(new SpaceSim.Core.Model.SectorId(7)); _stage = 562; _wait = 20; break;
			case 562: Capture("58-cc-universum-elf-sektoren.png"); _stage = 563; _wait = 10; break;
			case 563: _m.HudRef.CcUniverse.CurrentLayer = Ui.Screens.UniverseScreen.Layer.Ships; _m.TestCcSelectSector(new SpaceSim.Core.Model.SectorId(10)); _stage = 564; _wait = 20; break;
			case 564: Capture("59-cc-universum-schiffe.png"); _stage = 565; _wait = 10; break;
			case 565: _m.HudRef.CcUniverse.CurrentLayer = Ui.Screens.UniverseScreen.Layer.Security; _m.TestCcSelectSector(new SpaceSim.Core.Model.SectorId(11)); _stage = 566; _wait = 20; break;
			case 566: Capture("60-cc-universum-lage-elf.png"); _stage = 567; _wait = 10; break;
			case 567:
			{
				_m.HudRef.CcUniverse.CurrentLayer = Ui.Screens.UniverseScreen.Layer.Faction;
				_m.TestCloseCommand();
				var u = _m.Session.U;
				_m.HudRef.Map.Open(u, u.PlayerSector, new SpaceSim.Core.Model.SectorId(8));
				_stage = 568; _wait = 20; break;
			}
			case 568: Capture("61-sternenkarte-elf-sektoren.png"); _stage = 569; _wait = 10; break;
			case 569: _m.HudRef.Map.Visible = false; _m.TestDockAt(new SpaceSim.Core.Model.StationId(30)); _stage = 570; _wait = 60; break;
			case 570: _m.TestOpenDockArea("taxi"); _stage = 571; _wait = 20; break;
			case 571: Capture("62-dock-taxi-liste.png"); _stage = 572; _wait = 10; break;
			// Phase 25: Spielerkampf — Jäger in Alpha, Kaperer als Ziel, Waffenschächte; Beschusswarnung; Munition im Bereich „Reparatur“
			case 572:
			{
				if (_m.Docked) { _m.TestUndock(); _wait = 40; break; }
				var uni = _m.Session.U;
				if (uni.PlayerSector.Value != 1) { _m.TestEnterSector(new SpaceSim.Core.Model.SectorId(1)); _wait = 30; break; }
				var pl = uni.Ships[uni.PlayerShip];
				var model = uni.ShipModels["falcon"];
				var at = uni.StationsById().First(x => !x.IsBuildSite && x.Sector == uni.PlayerSector);
				var f = SpaceSim.Core.Simulation.ShipPurchase.Create(uni, uni.NewShipId(), model.Key, uni.ShipClasses[model.ClassKey].Slots, model.Fit,
					SpaceSim.Core.Model.ShipRole.Player, SpaceSim.Core.Model.Controller.Player, uni.PlayerActor, at, 0f);
				uni.AddShip(f);
				f.DockedAt = default; f.Position = pl.Position + new SpaceSim.Core.Model.Vec2(6f, 0f);
				_m.TestSwitchRemote(f.Id);
				_stage = 573; _wait = 40; break;
			}
			case 573:
			{
				var uni = _m.Session.U;
				var pl = uni.Ships[uni.PlayerShip];
				var raider = uni.ShipsById().First(SpaceSim.Core.Simulation.ThreatMap.IsRaider);
				if (raider.Transit is not null) uni.FinishTransit(raider);
				raider.Order = new SpaceSim.Core.Model.PatrolOrder(uni.PlayerSector); raider.NextThinkTick = uni.Tick + 100000;
				if (raider.Sector != uni.PlayerSector) uni.MoveShip(raider, uni.PlayerSector);
				raider.Position = pl.Position + new SpaceSim.Core.Model.Vec2(4f, -28f); raider.DockedAt = default;
				_m.TestFace(raider.Position);
				_m.TestNextEnemy();
				_stage = 580; _wait = 6; break;
			}
			case 580: _m.TestAllFire(); _stage = 574; _wait = 10; break;   // einen Frame später: Schachtzustände für das neue Ziel sind berechnet
			case 574: Capture("63-flug-kampf-ziel-waffen.png"); _stage = 575; _wait = 10; break;
			case 575:
			{
				var uni = _m.Session.U;
				var pl = uni.Ships[uni.PlayerShip];
				if (uni.Fights.Of(pl.Id) is null) { _stage = 577; _wait = 5; break; }
				_m.Session.RunTicks(12);
				pl.Hull = pl.MaxHull;
				_stage = 576; _wait = 4; break;
			}
			case 576: Capture("64-flug-gefecht-beschuss.png"); _stage = 577; _wait = 10; break;
			case 577:
			{
				SpaceSim.Core.Combat.PlayerCombat.Withdraw(_m.Session.U);
				var yard = _m.Session.U.StationsById().First(x => x.TypeKey == "shipyard");
				_m.Session.Player.Systems.AmmoMissing = _m.Session.Player.Stats.AmmoCapacity * 0.6f;
				_m.TestDockAt(yard.Id);
				_stage = 578; _wait = 60; break;
			}
			case 578: _m.TestOpenDockArea("repair"); _stage = 579; _wait = 20; break;
			case 579: Capture("65-dock-reparatur-munition.png"); _stage = 581; _wait = 10; break;
			case 581:
			{
				// Phase 26: beschädigtes Schiff an der Werft — Schadensliste und Modul-Reparaturzeilen
				var uni = _m.Session.U;
				var pl = _m.Session.Player;
				pl.Hull = pl.MaxHull * 0.45f;
				SpaceSim.Core.Combat.ModuleDamage.SetCondition(uni, pl, pl.Fit.IndexOf("weapon_laser_s"), 0.2f);
				SpaceSim.Core.Combat.ModuleDamage.SetCondition(uni, pl, pl.Fit.FindIndex(k => k is not null && k.StartsWith("chem_engine")), 0.55f);
				_m.TestOpenDockArea("trade");   // Bereich neu aufbauen (Reparatur war schon offen)
				_stage = 588; _wait = 10; break;
			}
			case 588: _m.TestOpenDockArea("repair"); _stage = 582; _wait = 20; break;
			case 582: Capture("66-dock-reparatur-module.png"); _stage = 583; _wait = 10; break;
			case 583:
			{
				_m.TestOpenCommand();
				_m.TestOpenCcArea("assets");
				_m.HudRef.CcAssets.TestOpenShipDetail();
				_stage = 584; _wait = 20; break;
			}
			case 584: Capture("67-schiffsdetail-schaeden.png"); _stage = 585; _wait = 10; break;
			case 585:
			{
				_m.TestCloseCommand();
				var uni = _m.Session.U;
				if (_m.Docked) { _m.TestUndock(); _wait = 60; break; }
				var pl = _m.Session.Player;
				var food = uni.FindGood("food")!.Id;
				var w = SpaceSim.Core.Simulation.Wrecks.Drop(uni, uni.PlayerSector, pl.Position + new SpaceSim.Core.Model.Vec2(-6f, -14f),
					new System.Collections.Generic.Dictionary<SpaceSim.Core.Model.GoodId, float> { [food] = 12f }, default, "Wrack: Korsar")!;
				w.Kind = SpaceSim.Core.Model.WreckKind.Wreck;
				w.Modules.Add(new SpaceSim.Core.Model.WreckModule { Key = "weapon_laser_s", Condition = 0.7f });
				SpaceSim.Core.Simulation.Wrecks.Drop(uni, uni.PlayerSector, pl.Position + new SpaceSim.Core.Model.Vec2(8f, -10f),
					new System.Collections.Generic.Dictionary<SpaceSim.Core.Model.GoodId, float> { [food] = 5f }, default, "Frachtbehälter");
				uni.Rules.FireExtinguishChance = 0f;
				SpaceSim.Core.Combat.ModuleDamage.Ignite(uni, pl, pl.Fit.FindIndex(k => k is not null && k.StartsWith("reactor")));
				_stage = 586; _wait = 20; break;
			}
			case 586: Capture("68-flug-wrack-brand.png"); _stage = 587; _wait = 10; break;
			// ── Phase 27: Flottenkampf und Gerätetest-Befunde ──
			case 587:
			{
				var uni = _m.Session.U;
				SpaceSim.Core.Combat.ModuleDamage.ExtinguishAll(uni, _m.Session.Player, "test");
				var yard = uni.Stations.Values.First(x => x.Name == "Werft Delta");
				_m.TestDockAt(yard.Id);
				_stage = 588_1; _wait = 60; break;
			}
			case 588_1: _m.TestOpenDockArea("info"); _stage = 588_2; _wait = 20; break;
			case 588_2: Capture("69-dock-station-info.png"); _stage = 588_3; _wait = 10; break;
			case 588_3: _m.TestOpenDockArea("outfitter"); _stage = 588_4; _wait = 20; break;
			case 588_4: Capture("70-dock-ausruester.png"); _stage = 588_5; _wait = 10; break;
			case 588_5: _m.TestOpenDockArea("hangar"); _stage = 588_6; _wait = 20; break;
			case 588_6: Capture("71-dock-hangar.png"); _stage = 588_7; _wait = 10; break;
			case 588_7: _m.TestUndock(); _stage = 588_8; _wait = 80; break;
			case 588_8:
			{
				// Polizeigefecht gegen den gesuchten Spieler (Taste „Ergeben“)
				var uni = _m.Session.U;
				var pl = _m.Session.Player;
				var sec = uni.Sectors[uni.PlayerSector];
				SpaceSim.Core.Simulation.Wanted.Report(uni, uni.PlayerActor, sec.Owner, "assault");
				var cops = SpaceSim.Core.Simulation.SecuritySystem.PoliceOf(uni, sec.Owner).Where(c => !uni.Fights.IsEngaged(c.Id)).Take(2).ToList();
				int i = 0;
				foreach (var c in cops)
				{
					if (c.Command is not null) SpaceSim.Core.Simulation.Commands.Cancel(uni, c);
					if (c.Transit is not null) uni.FinishTransit(c);
					if (c.Sector != uni.PlayerSector) uni.MoveShip(c, uni.PlayerSector);
					c.Position = pl.Position + new SpaceSim.Core.Model.Vec2(30f + 10f * i, -25f); c.DockedAt = default; i++;
				}
				if (cops.Count > 0) SpaceSim.Core.Combat.Engage.StartArrest(uni, cops, pl, sec);
				_stage = 588_9; _wait = 40; break;
			}
			case 588_9: Capture("72-hud-polizeigefecht-ergeben.png"); _stage = 589_1; _wait = 10; break;
			case 589_1:
			{
				_m.TestSurrender();
				var uni = _m.Session.U;
				var f = uni.Sectors[uni.PlayerSector].Owner;
				var cops = SpaceSim.Core.Simulation.SecuritySystem.PoliceOf(uni, f).Where(c => !uni.Fights.IsEngaged(c.Id)).Take(2).ToList();
				foreach (var c in cops) c.DutySector = uni.PlayerSector;
				SpaceSim.Core.Simulation.PoliceCommands.Organize(uni, f, SpaceSim.Core.Simulation.SecuritySystem.PoliceOf(uni, f));
				_m.TestOpenCommand(); _m.TestOpenCcArea("sector");
				_stage = 589_2; _wait = 30; break;
			}
			case 589_2: Capture("73-sektor-verband.png"); _stage = 589_3; _wait = 10; break;
			case 589_3: _m.TestOpenCcArea("system"); _m.HudRef.CcSystem.TestChoosing = true; _stage = 589_4; _wait = 20; break;
			case 589_4: Capture("74-system-startpakete.png"); _stage = 589_5; _wait = 10; break;
			case 589_5: _m.HudRef.CcSystem.TestChoosing = false; _m.TestOpenCcArea("comms"); _stage = 589_6; _wait = 20; break;
			case 589_6: Capture("75-comms.png"); _stage = 589_7; _wait = 10; break;
			case 589_7:
			{
				_m.TestCloseCommand();
				var beta = new SpaceSim.Core.Model.SectorId(2);
				_m.TestEnterSector(beta);
				_m.TestTeleportTo(new SpaceSim.Core.Model.Vec2(0f, 0f));
				_stage = 589_8; _wait = 40; break;
			}
			case 589_8: Capture("76-flug-beta-planet.png"); _stage = 589_9; _wait = 10; break;
			case 589_9: _m.HudRef.Map.Open(_m.Session.U, _m.Session.U.PlayerSector, null); _stage = 590_1; _wait = 20; break;
			case 590_1: Capture("77-sternenkarte.png"); _stage = 590_2; _wait = 10; break;
			case 590_2: _m.HudRef.Map.Visible = false; _stage = _phase28 ? 590_3 : 999_999; _wait = 5; break;
			// ── Phase 28: Stationsangriff (Raketenwerfer statt Geschütz), Gegenwehr, Verteidigung in der Station-Info, Krieg ──
			case 590_3:
			{
				var uni = _m.Session.U;
				var pl = uni.Ships[uni.PlayerShip];
				int slot = pl.Fit.IndexOf("weapon_massdriver_s");
				if (slot >= 0) { pl.Fit[slot] = "missile_launcher_s"; pl.RecomputeStats(uni); }
				var st = uni.Stations.Values.Where(x => x.Sector == uni.PlayerSector && SpaceSim.Core.Combat.StationCombat.DefenseOf(uni, x) is { Guns.Count: > 0 }).OrderBy(x => x.Id.Value).First();
				_p28Station = st.Id;
				pl.Hull = pl.MaxHull;
				_m.TestTeleportTo(st.Position + new SpaceSim.Core.Model.Vec2(0f, 45f));
				_m.TestFace(st.Position);
				_m.TestSetShipTarget(default);
				_m.TestSetStationTarget(st.Id);
				_stage = 590_4; _wait = 10; break;
			}
			case 590_4: Capture("78a-flug-station-gewaehlt.png"); _stage = 590_5; _wait = 10; break;
			case 590_5:
			{
				var uni = _m.Session.U;
				_m.TestArmStation();
				_m.TestFace(uni.Stations[_p28Station].Position);
				_m.SyncHudForTest();
				_m.TestFireSlot(uni.Ships[uni.PlayerShip].Fit.IndexOf("missile_launcher_s"));
				_m.TestAllFire();
				_stage = 590_6; _wait = 3; break;
			}
			case 590_6: _m.Session.RunTicks(3); _stage = 590_7; _wait = 3; break;
			case 590_7: Capture("78-flug-station-angriff-rakete.png"); _stage = 590_8; _wait = 10; break;
			case 590_8: _m.Session.RunTicks(16); _m.Session.Player.Hull = _m.Session.Player.MaxHull; _stage = 590_9; _wait = 4; break;
			case 590_9: Capture("79-flug-station-schiesst-zurueck.png"); _stage = 591_1; _wait = 10; break;
			case 591_1:
			{
				var uni = _m.Session.U;
				var pl = uni.Ships[uni.PlayerShip];
				if (uni.Fights.Of(pl.Id) is { } e && e.Of(pl.Id) is { Active: true } p) SpaceSim.Core.Combat.Engage.Leave(uni, e, p, SpaceSim.Core.Model.CombatExit.Fled);
				_m.TestDockAt(_p28Station);
				_stage = 591_2; _wait = 60; break;
			}
			case 591_2: _m.TestOpenDockArea("info"); _stage = 591_3; _wait = 20; break;
			case 591_3: Capture("80-dock-station-info-verteidigung.png"); _stage = 591_4; _wait = 10; break;
			case 591_4:
			{
				_m.TestUndock();
				var uni = _m.Session.U;
				SpaceSim.Core.Simulation.Wars.Declare(uni, new SpaceSim.Core.Model.FactionId(1), new SpaceSim.Core.Model.FactionId(2));
				_m.TestOpenCommand(); _m.TestOpenCcArea("factions"); _m.TestCcSelectFaction(new SpaceSim.Core.Model.FactionId(1));
				_stage = 591_5; _wait = 30; break;
			}
			case 591_5: Capture("81-fraktionen-krieg.png"); _stage = 591_6; _wait = 10; break;
			case 591_6: _m.TestCloseCommand(); _stage = 591_7; _wait = 20; break;
			case 591_7: Capture("82-flug-kriegsmeldung.png"); _stage = 592_1; _wait = 10; break;
			// ── Zweiter Gerätetest Phase 28: Meldung über dem Dock-Menü, Handel mit offenem Editor, Universum-Detail, Gefechtstest, Nomad ──
			case 592_1:
			{
				var uni = _m.Session.U;
				_m.HudRef.HideAlert();
				var farm = uni.Stations.Values.First(x => x.Name == "Farm Gamma");
				_m.TestDockAt(farm.Id);
				_stage = 592_2; _wait = 60; break;
			}
			case 592_2:
				_m.HudRef.Alert("Schiff zerstört (Piraten), Fracht verloren. Gelandet: Farm Gamma — Leihschiff Pip: Gebühr 60 + Pfand 400 Cr bezahlt, Miete 120 Cr/Tag.", 20f, sticky: true);
				_stage = 592_3; _wait = 10; break;
			case 592_3: Capture("83-dock-menue-verlustmeldung.png"); _stage = 592_4; _wait = 5; break;
			case 592_4:
			{
				_m.TestOpenDockArea("trade");
				_stage = 592_5; _wait = 10; break;
			}
			case 592_5:
			{
				_m.HudRef.DockTrade.TestOpenRow(_m.HudRef.DockTrade.TestLastGood);
				_stage = 592_6; _wait = 20; break;
			}
			case 592_6: Capture("84-dock-handel-editor-unten.png"); _stage = 592_7; _wait = 5; break;
			case 592_7:
			{
				_m.HudRef.HideAlert();
				_m.TestUndock();
				_stage = 592_8; _wait = 80; break;
			}
			case 592_8: _m.TestOpenCommand(); _m.TestOpenCcArea("universe"); _m.TestCcSelectSector(_m.Session.U.PlayerSector); _stage = 592_9; _wait = 20; break;
			case 592_9: Capture("85-universum-hier.png"); _stage = 593_1; _wait = 5; break;
			case 593_1: _m.TestCloseCommand(); _m.TestMenu("new:combat"); _stage = 593_2; _wait = 30; break;
			case 593_2: Capture("86-gefechtstest-warnung.png"); _stage = 593_3; _wait = 5; break;
			case 593_3:
			{
				var uni = _m.Session.U;
				_m.Session.RunTicks(64);
				_m.TestNextEnemy();
				if (SpaceSim.Core.Combat.PlayerCombat.PositionOf(uni, _m.TestFight.Target) is { } tp) _m.TestFace(tp.Pos);
				_stage = 593_4; _wait = 20; break;
			}
			case 593_4: Capture("87-gefechtstest-gefecht.png"); _stage = 593_5; _wait = 5; break;
			case 593_5: _m.TestMenu("new:trader"); _stage = 593_6; _wait = 30; break;
			case 593_6: Capture("88-nomad-ohne-laser.png"); _stage = 593_7; _wait = 5; break;
			case 593_7: _m.TestMenu("new:war"); _stage = 593_8; _wait = 30; break;
			case 593_8:
			{
				var uni = _m.Session.U;
				var hq = uni.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
				_m.TestFace(hq.Position);
				_m.TestSetStationTarget(hq.Id);
				_m.TestStopAutomation();
				_stage = 593_9; _wait = 15; break;
			}
			case 593_9:
			{
				// Prüfung Runde 2: warum „Nächster Gegner“ sichtbar ist (Gegner im Sektor nennen)
				var uni = _m.Session.U;
				GD.Print("[ui-shots] Gegner im Sektor: " + string.Join(", ", SpaceSim.Core.Combat.PlayerCombat.Enemies(uni, uni.Ships[uni.PlayerShip])
					.Select(id => $"{uni.Ships[id].Role} #{id.Value} {(uni.Ships[id].Transit is not null ? "auf Durchreise" : "geparkt")}")));
				Capture("89-kriegstest-station-gewaehlt.png"); _stage = 594_1; _wait = 5; break;
			}
			// Runde 8: Stationsmodelle — Flug nahe Werft und Sektor-HQ (Delta), Piratenversteck (Abgrund), Sektor mit vielen Stationen von weiter oben (Vesta)
			case 594_1:
			{
				_m.TestMenu("new");
				var uni = _m.Session.U;
				var yard = uni.Stations.Values.First(x => x.TypeKey == "shipyard" && uni.Sectors[x.Sector].Name.Contains("Delta"));
				_m.TestEnterSector(yard.Sector);
				var hq = uni.Stations.Values.First(x => x.Sector == yard.Sector && x.TypeKey == "sector_hq");
				// zwischen Werft und HQ, etwas zur Seite, etwas weiter oben (zwei Zoomstufen)
				var mid = new SpaceSim.Core.Model.Vec2((yard.Position.X + hq.Position.X) / 2f, (yard.Position.Y + hq.Position.Y) / 2f);
				_m.TestTeleportTo(SpaceSim.Core.Combat.CombatSpace.Clear(uni, yard.Sector, mid + new SpaceSim.Core.Model.Vec2(0f, 70f)));
				_m.TestFace(mid);
				_m.TestMenu("zoom_out"); _m.TestMenu("zoom_out");
				_stage = 594_2; _wait = 30; break;
			}
			case 594_2: Capture("90-stationen-werft-hq.png"); _m.TestMenu("cam_reset"); _stage = 594_3; _wait = 5; break;
			case 594_3:
			{
				var uni = _m.Session.U;
				var hide = uni.Stations.Values.First(x => x.TypeKey == "pirate_hideout");
				_m.TestEnterSector(hide.Sector);
				_m.TestTeleportTo(SpaceSim.Core.Combat.CombatSpace.Clear(uni, hide.Sector, hide.Position + new SpaceSim.Core.Model.Vec2(42f, 12f)));
				_m.TestFace(hide.Position);
				_m.TestMenu("zoom_out");
				_stage = 594_4; _wait = 30; break;
			}
			case 594_4: Capture("91-stationen-piratenversteck.png"); _m.TestMenu("cam_reset"); _stage = 594_5; _wait = 5; break;
			case 594_5:
			{
				var uni = _m.Session.U;
				var busiest = uni.Sectors.Values.OrderByDescending(x => x.Stations.Count).First();
				_m.TestEnterSector(busiest.Id);
				var c = busiest.Stations.Select(id => uni.Stations[id].Position).Aggregate(new SpaceSim.Core.Model.Vec2(0f, 0f), (a, b) => a + b) * (1f / busiest.Stations.Count);
				_m.TestTeleportTo(c + new SpaceSim.Core.Model.Vec2(0f, 60f));
				_m.TestMenu("zoom_out"); _m.TestMenu("zoom_out"); _m.TestMenu("zoom_out");
				_stage = 594_6; _wait = 30; break;
			}
			case 594_6: Capture("92-stationen-sektor-vesta-uebersicht.png"); _m.TestMenu("cam_reset"); _stage = GateShotsStart; _wait = 5; break;
			case >= GateShotsStart and < 596_000: GateShots(); break;   // Jumpgate-Modelle 93–96
			case >= ShipShotsStart and < 598_000: ShipShots(); break;   // Runde 11: Schiffsmodelle 97–98
			case >= SkyShotsStart and < 600_000: SkyShots(); break;   // Himmelskugeln je Sektor
			default:
				GD.Print($"[ui-shots] fertig: {_dir}");
				GetTree().Quit(0);
				SetProcess(false);
				break;
		}
	}

	SpaceSim.Core.Model.StationId _p28Station;

	/// <summary>Speichert das aktuelle Frame als PNG, sobald der Frame fertig gezeichnet ist.</summary>
	void Capture(string file)
	{
		string path = Path.Combine(_dir, file);
		RenderingServer.FramePostDraw += Shot;
		void Shot()
		{
			RenderingServer.FramePostDraw -= Shot;
			var img = GetViewport().GetTexture().GetImage();
			var err = img.SavePng(path);
			GD.Print($"[ui-shots] {file}  {img.GetWidth()}x{img.GetHeight()}  err={err}");
		}
	}
}
