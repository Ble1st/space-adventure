using System;
using System.Linq;
using Godot;
using SpaceSim.Core.Ui;
using SpaceAdventure.Ui.Kit;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 111 (Runde 35, UI-ANPASSUNG.md Etappe E4): Kommandozentrale. (1) Faktor 1 bei 1280 × 720: klassisches Gerüst, Inhalt 1022 × 520 wie
/// bisher; (2) Stufe 1,3 (16:9): Schicht mit dem vollen Faktor 1,3, jede Kommandozentralen-Ansicht ohne Taste außerhalb und ohne Überlappung, die
/// Universumskarte nutzt die gewonnene Fläche; (3) echter Tipp auf einen Eintrag der Leiste wechselt den Bereich; (4) zurück.
/// </summary>
public partial class SelfTest
{
	int _r35Area;
	Vector2 _r35At;
	Rect2 _r35Rect;
	int _r35Wait;
	SpaceSim.Core.Model.SectorId _r35Sector;
	SpaceSim.Core.Model.StationId _r35Station;
	KitScroll? _r35Scroll;
	int _r35Cells;
	float _r35Scale;

	bool Round35Stage(int f)
	{
		var root = GetTree().Root;
		var hud = _m.HudRef;
		switch (f)
		{
			case 1:
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();
				if (root.Size.X < 640 || root.Size.X == root.Size.Y) root.Size = new Vector2I(1280, 720);
				UiScale.TestForce = null; UiScale.TestPpi = null; UiScale.Set(UiSize.Normal);
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Universe);
				return false;
			case 12:
			{
				var vis = GetViewport().GetVisibleRect().Size;
				Check(hud.CcFactor == 1f && hud.CcUniverse.Shell is { Compact: false }, $"E4: Faktor 1 → klassisches Gerüst (Schicht {hud.CcFactor}, kompakt {hud.CcUniverse.Shell?.Compact})");
				if (Mathf.Abs(vis.X - 1280f) < 1f && Mathf.Abs(vis.Y - 720f) < 1f)
					Check(hud.CcUniverse.Shell is { } sh && sh.InnerW == 1022f && sh.InnerH == 520f && Mathf.Abs(hud.CcUniverse.TestMapRect.Size.X - 700f) < 1f,
						$"E4: 1280 × 720 wie bisher (Innen {hud.CcUniverse.Shell?.InnerW}×{hud.CcUniverse.Shell?.InnerH}, Karte {hud.CcUniverse.TestMapRect.Size})");
				UiScale.TestPpi = 396f; UiScale.TestForce = 1.3f; UiScale.Apply();
				_r35Area = 0;
				return false;
			}
			case 30:
			{
				var vis = GetViewport().GetVisibleRect();
				float want = CommandLayout.FitFactor(vis.Size.X, vis.Size.Y, new UiInsets(0, 0, 0, 0), 1.3f);
				Check(Mathf.Abs(hud.CcFactor - 1.3f) < 0.005f && Mathf.Abs(want - 1.3f) < 0.005f, $"E4: Kommandozentrale bei Stufe 1,3 → Schicht {hud.CcFactor:0.00} (Kern {want:0.00})");
				var map = hud.CcUniverse.TestMapRect;
				Check(map.Size.X > 300f && map.Size.Y > 200f, $"E4: Universumskarte nutzt die Fläche ({map.Size})");
				return false;
			}
		}
		// alle Bereiche nacheinander öffnen und prüfen (je 14 Bilder)
		var areas = Ui.Screens.CommandAreas.All.Select(a => a.Id).ToList();
		if (f >= 40 && f < 40 + areas.Count * 14)
		{
			int k = (f - 40) / 14, ph = (f - 40) % 14;
			if (ph == 0) _m.TestOpenCcArea(areas[k]);
			if (ph == 12 && hud.Screens.Current is Control sc)
			{
				CheckButtons($"E4 {areas[k]} 1,3", sc, GetViewport().GetVisibleRect());
				// Runde 35b: dieselbe Textregel wie --ui-check (mit der Schrift gemessen, gezeichnete Texte eingeschlossen) — kein Text über Text/Taste, nichts beschnitten
				DrawnText.Enabled = true;
				sc.QueueRedraw();
				foreach (var n in UiCheck.Visible(sc)) if (n is Control cc) cc.QueueRedraw();
			}
			if (ph == 13 && hud.Screens.Current is Control scw && UiCheck.Visible(scw).OfType<ScrollContainer>().Any(x => x.Size.X <= 0f || x.Size.Y <= 0f) && _r35Wait++ < 30)
			{ _stageStart += 1; return false; }   // erst messen, wenn der Neuaufbau ausgelegt ist (rollende Flächen mit Größe 0 = noch nicht ausgelegt)
			if (ph == 13 && hud.Screens.Current is Control sc2b)
			{
				_r35Wait = 0;
				var sc2 = sc2b;
				var scope = UiCheck.Visible(sc2).ToList();
				var inter = scope.Where(c => UiCheck.Interactive(c) && c.Size.X > 1f && c.Size.Y > 1f && c.MouseFilter != Control.MouseFilterEnum.Ignore).ToList();
				var notes = new System.Collections.Generic.List<string>();
				var tc = UiCheck.TextChecks(scope, inter, notes);
				Check(tc.TextOverlaps == 0 && tc.Clipped == 0, $"35b {areas[k]} 1,3 ({GetViewport().GetVisibleRect().Size}): Text über Text/Taste {tc.TextOverlaps}, beschnitten {tc.Clipped} [{string.Join("; ", notes.Take(4))}]"
					+ (tc.Clipped > 0 ? " " + string.Join(" ", scope.OfType<KitButton>().Take(3).Select(b => $"{b.Text}@{b.GetGlobalRect()} in {(b.GetParent() as Control)?.GetGlobalRect()}")) + " scrolls " + string.Join(" ", scope.OfType<KitScroll>().Select(x => x.GetGlobalRect().ToString())) : ""));
				DrawnText.Enabled = false;
			}
			return false;
		}
		int t = f - (40 + areas.Count * 14);
		switch (t)
		{
			case 0:
			{
				// echter Tipp auf „Flotte“ in der Leiste
				var b = Buttons(hud.Screens.Current as Node ?? hud).FirstOrDefault(x => x.Text == "Flotte" && x.IsVisibleInTree());
				if (b is null) { Check(false, "E4: Leisteneintrag „Flotte“ fehlt"); return false; }
				var br = b.GetGlobalRect();
				if (br != _r35Rect && _r35Wait++ < 30) { _r35Rect = br; _stageStart += 1; return false; }   // erst tippen, wenn das Layout steht
				_r35At = br.GetCenter();
				Tap(_r35At, true);
				return false;
			}
			case 2: Tap(_r35At, false); return false;
			case 10:
				Check(hud.Screens.Current == hud.CcFleet, $"E4: echter Tipp auf „Flotte“ in der skalierten Leiste öffnet die Flotte ({hud.Screens.Current?.Id})");
				UiScale.TestForce = null; UiScale.TestPpi = null; UiScale.Set(UiSize.Normal);
				_m.TestCloseCommand();
				return false;
			case 16:
				Check(hud.CcFactor == UiScale.CommandFactor, $"E4: zurück (Zentrale {hud.CcFactor})");
				// Runde 35b: echte Tipps auf Karten und echter Wisch in der skalierten Zentrale
				UiScale.TestPpi = 396f; UiScale.TestForce = 1.3f; UiScale.Apply();
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Universe);
				_r35Wait = 0; _r35Rect = default;
				return false;
			case 30:
			{
				var u = _m.Session.U;
				var target = u.Sectors.Values.Select(x => x.Id).First(id => id != u.PlayerSector);
				_r35Sector = target;
				if (hud.CcUniverse.TestSectorScreen(target) is not { } at) { Check(false, "35b: Universumskarte fehlt"); return false; }
				_r35At = at; Tap(at, true); return false;
			}
			case 32: Tap(_r35At, false); return false;
			case 40:
				Check(hud.CcUniverse.SelectedSector == _r35Sector, $"35b: echter Tipp auf einen Sektor der skalierten Universumskarte wählt ihn ({hud.CcUniverse.SelectedSector?.Value} statt {_r35Sector.Value})");
				hud.CcSector.ShowPlayerSector(_m.Session.U.PlayerSector);
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Sector);
				return false;
			case 52:
			{
				var u = _m.Session.U;
				_r35Station = u.Sectors[u.PlayerSector].Stations.First();
				if (hud.CcSector.TestStationScreen(_r35Station) is not { } at) { Check(false, "35b: Sektorkarte fehlt"); return false; }
				_r35At = at; Tap(at, true); return false;
			}
			case 54: Tap(_r35At, false); return false;
			case 62:
				Check(hud.CcSector.TestSelStation == _r35Station, $"35b: echter Tipp auf eine Station der skalierten Sektorkarte wählt sie ({hud.CcSector.TestSelStation?.Value} statt {_r35Station.Value})");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Factions);
				return false;
			case 74:
			{
				// echter Wisch in einer rollenden Liste der Zentrale: der Inhalt folgt dem Finger 1:1 (in Bildpunkten)
				// die innerste rollende Liste (verschachtelt im als Ganzes rollenden Bereich)
				_r35Scroll = UiCheck.Visible(hud.CcFactions).OfType<KitScroll>().Where(x => x.GetVScrollBar().MaxValue - x.GetVScrollBar().Page > 60)
					.OrderByDescending(x => { int d = 0; for (Node? n = x; n is not null; n = n.GetParent()) d++; return d; }).FirstOrDefault();
				if (_r35Scroll is null) { Check(false, "35b: keine rollende Liste in „Fraktionen“"); return false; }
				GD.Print($"[r35] Wischliste {_r35Scroll.GetGlobalRect()} max {_r35Scroll.GetVScrollBar().MaxValue} page {_r35Scroll.GetVScrollBar().Page} Fenster {GetViewport().GetVisibleRect()}");
				_r35Scroll.ScrollTo(0);
				_r35At = _r35Scroll.GetGlobalRect().GetCenter() + new Vector2(0, 60);
				PushMouse(_r35At, true, false);
				return false;
			}
			case 76: PushMouse(_r35At - new Vector2(0, 40), true, true); return false;
			case 78: PushMouse(_r35At - new Vector2(0, 160), true, true); return false;
			case 80:
			{
				PushMouse(_r35At - new Vector2(0, 160), false, false);
				float k = hud.CcFactor;
				int got = _r35Scroll?.ScrollVertical ?? -1;
				float want = MathF.Min(160f / k, _r35Scroll is { } ss ? (float)(ss.GetVScrollBar().MaxValue - ss.GetVScrollBar().Page) : 0f);
				Check(Mathf.Abs(got - want) <= 3f, $"35b: Wisch 160 px in der Zentrale (Schicht {k:0.00}) rollt {got} (soll {want:0}) — 1:1");
				UiScale.TestForce = null; UiScale.TestPpi = null; UiScale.Set(UiSize.Normal);
				_m.TestCloseCommand();
				// Gefechtssperre der Flugschicht über den echten Ablauf (Main): Gefecht läuft → ein Schacht fällt weg → Zellen bleiben; Gefecht endet → frei
				UiScale.TestPpi = 396f; UiScale.TestForce = 1.3f; UiScale.Apply();
				// Prüfung Punkt 11: Kosten des Neuaufbaus der längsten Handelsliste (Werft Delta) samt Namens-Einpassung
				var yard = _m.Session.U.StationsById().FirstOrDefault(x => x.Name == "Werft Delta");
				if (yard is not null) { _m.TestDockAt(yard.Id); _m.TestOpenDockArea(Ui.Screens.DockAreas.Trade); }
				return false;
			}
			case 100:
			{
				var (rb, rb0, fit, rows) = _m.HudRef.DockTrade.TestTimeRebuild(20);
				GD.Print($"[perf] Handelsliste Werft Delta: Neuaufbau {rb:0.00} ms (ohne Einpassen der Labels {rb0:0.00} ms), Namens-Einpassung aller {rows} Zeilen beim Zeichnen {fit:0.000} ms ungespeichert, {_m.HudRef.DockTrade.CachedFitMs:0.000} ms mit dem Zwischenspeicher des Spiels (FitName wie beim Zeichnen; erster, kalter Durchgang {_m.HudRef.DockTrade.ColdFitMs:0.000} ms)");
				Check(rows > 30, $"35b: Handelsliste mit {rows} Zeilen gemessen");
				_m.TestUndock();
				_m.TestMenu("new:combat");
				return false;
			}
			case 160:
			{
				// Überfall auf den Spieler wie im echten Ablauf (Engage.StartRaid; die Ankündigung des Gefechtstests schaltet den Zeitraffer ab)
				var u = _m.Session.U;
				var me = u.Ships[u.PlayerShip];
				var raider = u.ShipsById().FirstOrDefault(SpaceSim.Core.Simulation.ThreatMap.IsRaider);
				if (raider is null) { Check(false, "35b: kein Kaperer im Gefechtstest"); return false; }
				u.PendingEncounters.Clear();
				if (raider.Transit is not null) u.FinishTransit(raider);
				if (raider.Sector != me.Sector) u.MoveShip(raider, me.Sector);
				raider.Position = me.Position + new SpaceSim.Core.Model.Vec2(25f, 0f); raider.DockedAt = default;
				SpaceSim.Core.Combat.Engage.StartRaid(u, raider, me, u.Sectors[me.Sector]);
				return false;
			}
			case 200:
			{
				var u = _m.Session.U;
				var ship = u.Ships[u.PlayerShip];
				if (u.Fights.Of(ship.Id) is null) { Check(false, $"35b: Gefechtstest — kein Gefecht nach dem Zeitraffer (Tick {u.Tick}, Warnung {u.PendingEncounters.Count}, Schiff {ship.ModelKey}, Raids {u.Rules.PlayerRaids})"); return false; }
				_r35Cells = hud.Flight.TestLayoutBays; _r35Scale = hud.Flight.Scale.X;
				int slot = Enumerable.Range(0, ship.Fit.Count).LastOrDefault(i => ship.Fit[i] is { } m && u.Modules.TryGetValue(m, out var md) && md.IsWeapon, -1);
				if (slot < 0) { Check(false, "35b: keine Waffe am Falken"); return false; }
				ship.Fit[slot] = null; ship.RecomputeStats(u);
				return false;
			}
			case 212:
			{
				var u = _m.Session.U;
				Check(u.Fights.Of(u.PlayerShip) is not null && hud.Flight.TestLayoutBays == _r35Cells && hud.Flight.Scale.X == _r35Scale,
					$"35b: echtes Gefecht, Schacht fällt weg → Zellen {_r35Cells} → {hud.Flight.TestLayoutBays}, Schicht {_r35Scale:0.00} → {hud.Flight.Scale.X:0.00} (fest)");
				SpaceSim.Core.Combat.PlayerCombat.Withdraw(u);
				foreach (var e in u.Fights.In(u.PlayerSector).ToList()) foreach (var p in e.Participants.Where(p => p.Active && p.Ship != u.PlayerShip).ToList()) SpaceSim.Core.Combat.Engage.Leave(u, e, p, SpaceSim.Core.Model.CombatExit.Fled);
				return false;
			}
			case 240:
			{
				var u = _m.Session.U;
				Check(u.Fights.Of(u.PlayerShip) is null && hud.Flight.TestLayoutBays < _r35Cells,
					$"35b: Gefecht vorbei → Sperre gelöst (Zellen {_r35Cells} → {hud.Flight.TestLayoutBays})");
				UiScale.TestForce = null; UiScale.TestPpi = null; UiScale.Set(UiSize.Normal);
				return false;
			}
			case 250: return true;
		}
		return false;
	}
}
