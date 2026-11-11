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

	public UiShots(Main m, string dir)
	{
		_m = m;
		_dir = dir;
	}

	public override void _Ready()
	{
		DirAccess.MakeDirRecursiveAbsolute(_dir);
		GD.Print($"[ui-shots] Zielordner: {_dir}");
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
			default:
				GD.Print($"[ui-shots] fertig: {_dir}");
				GetTree().Quit(0);
				SetProcess(false);
				break;
		}
	}

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
