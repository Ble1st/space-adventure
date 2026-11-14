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

	/// <summary>Fenstergröße und angenommene Geräte-DPI (dokumentierte Annahme je Auflösung).</summary>
	static readonly (int W, int H, float Ppi, string Device)[] Sizes =
	{
		(1280, 720, 294f, "5″-Handy 720p"),
		(1560, 720, 282f, "6,1″-Handy 19,5:9 720p"),
		(2340, 1080, 396f, "Samsung A15 6,5″ (Referenz)"),
		(1920, 1200, 224f, "10,1″-Tablet 16:10"),
		(2048, 1536, 264f, "9,7″-Tablet 4:3"),
		(960, 540, 245f, "4,5″-Handy / kleines Fenster"),
	};
	static readonly float[] Factors = { 1f, 1.15f, 1.3f };

	sealed record Finding(string State, int W, int H, float Factor, int Interactive, int Overlaps, int Truncated, int Small7, int Small5, int Outside, float Fill,
		List<string> Notes);
	readonly List<Finding> _results = new();
	readonly List<(string Name, Action Setup, int Wait)> _plan = new();
	int _sizeIdx, _factorIdx, _wait;
	string _state = "";
	bool _shot;
	bool Headless => DisplayServer.GetName() == "headless";

	public override void _Ready()
	{
		DirAccess.MakeDirRecursiveAbsolute(_dir);
		GD.Print($"[ui-check] Zielordner {_dir}");
		BeginSize();
	}

	void BeginSize()
	{
		var (w, h, ppi, _) = Sizes[_sizeIdx];
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
		Add("hamburger-menue", () => _m.TestOpenCommand(), 10);
		Add("meldung-toast", () => { _m.TestCommand("close"); _m.HudRef.Alert("Prüfmeldung: Überfall angekündigt — andocken oder Sektor verlassen. Dritte Zeile zum Messen.", 30f); _m.HudRef.Toast("Prüftoast: Ware verkauft, 10 × 21,3 = 213 Cr", 30f); }, 10);
		Add("sternenkarte", () => { _m.HudRef.HideAlert(); _m.TestCommand("map"); }, 12);
		Add("dockmenue", () =>
		{
			_m.HudRef.Map.Visible = false;
			var yard = u().StationsById().First(s => s.TypeKey == "shipyard");
			_m.TestDockAt(yard.Id);
			// alle verfügbaren Dock-Bereiche dieser Station gleich hinter diesen Schritt einreihen
			int at = 0;
			foreach (var def in _m.TestDockAreas().Where(d => d.Available).Select(d => d.Id).ToList())
				_plan.Insert(at++, ("dock-" + def, () => _m.TestOpenDockArea(def), 12));
		}, 20);
		Add("kz-open", () => { _m.TestUndock(); }, 20);
		foreach (var (id, _, _) in SpaceAdventure.Ui.Screens.CommandAreas.All)
			Add("kz-" + id, () => { _m.TestOpenCcArea(id); }, 12);
		Add("startpakete", () => { _m.TestOpenCcArea(SpaceAdventure.Ui.Screens.CommandAreas.System); _m.HudRef.CcSystem.TestChoosing = true; }, 12);
		Add("flug-automining", () => { _m.HudRef.CcSystem.TestChoosing = false; _m.TestCloseCommand(); _m.TestMenu("new:miner"); }, 40);
		Add("flug-automining2", () => { _m.TestStartMining(); }, 90);
		Add("flug-warnung", () => { _m.TestMenu("new:combat"); }, 40);
		Add("flug-gefecht", () => { _m.TestSetFast(true); }, 120);
		Add("flug-gefecht2", () => { _m.TestSetFast(false); }, 20);
		Add("flug-stationsangriff", () =>
		{
			_m.TestMenu("new:war");
		}, 40);
		Add("flug-stationsangriff2", () =>
		{
			var hq = u().StationsById().Where(s => s.Sector == u().PlayerSector && s.TypeKey == "sector_hq").FirstOrDefault();
			if (hq is not null) { _m.TestSelectStation(hq.Id); _m.TestArmStation(); }
		}, 30);
	}
	bool _skip;

	public override void _Process(double delta)
	{
		if (_wait > 0) { _wait--; return; }
		if (_shot) return;
		if (_state.Length > 0)
		{
			// Zustand vermessen und abbilden
			if (!_skip && !_state.EndsWith("-open") && _state != "flug-automining" && _state != "flug-gefecht" && _state != "flug-stationsangriff")
			{
				// zweimal im Abstand von 6 Bildern messen und den ruhigeren Befund nehmen (ein Umbau mitten in der Messung zählte sonst doppelte Tasten)
				var m = Measure(_state);
				if (_first is null) { _first = m; _wait = 6; return; }
				var pick = (m.Overlaps + m.Outside) <= (_first.Overlaps + _first.Outside) ? m : _first;
				_first = null;
				_results.Add(pick);
				_lastMeasured = pick.State;
				GD.Print($"[ui-check] {pick.W}x{pick.H} f{pick.Factor:0.00} {pick.State}: Tasten {pick.Interactive}, Überlappung {pick.Overlaps}, gekürzt {pick.Truncated}, <7 mm {pick.Small7}, <5 mm {pick.Small5}, außerhalb {pick.Outside}, Füllgrad {(pick.Fill < 0 ? "–" : $"{pick.Fill * 100:0} %")}");
			}
			_skip = false;
			_state = "";
			if (!Headless && _factorIdx == 0 && _lastMeasured is { } name)
			{
				_shot = true;
				var (w, h, _, _) = Sizes[_sizeIdx];
				string file = System.IO.Path.Combine(_dir, $"{w}x{h}_{name}.png");
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
	Finding? _first;

	/// <summary>Sichtbarer Teil eines Elements: geschnitten mit allen Vorfahren, die ihren Inhalt beschneiden (rollende Listen).</summary>
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

	static bool Interactive(Control c) => c is KitButton or SpaceAdventure.Ui.TouchButton or KitSlider or BaseButton or LineEdit;

	static IEnumerable<Control> Visible(Node n)
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
		if (MathF.Abs(GetTree().Root.ContentScaleFactor - f) > 0.001f) GD.PrintErr($"[ui-check] Faktor {GetTree().Root.ContentScaleFactor} statt {f}");
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
			float mm = UiLayout.Millimetres(MathF.Min(r.Size.X, r.Size.Y), w, h, f, ppi);
			if (mm < 7f) small7++;
			if (mm < 5f) { small5++; if (notes.Count < 24) notes.Add($"Tippfläche {mm:0.0} mm „{Label(c)}“"); }
			if (!vp.Grow(0.5f).Encloses(Clipped(c))) { outside++; if (notes.Count < 30) notes.Add($"außerhalb „{Label(c)}“ {r.Position.X:0},{r.Position.Y:0} {r.Size.X:0}×{r.Size.Y:0}"); }
		}
		foreach (var c in all.Where(c => (modal is null || modal == c || modal.IsAncestorOf(c)) && Clipped(c).Size.X > 1f))
		{
			if (c is Label l && l.Text.Length > 0)
			{
				bool cut = l.Text.Contains('…') || (l.ClipText && font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, l.GetThemeFontSize("font_size")).X > l.Size.X + 1f);
				if (cut) { trunc++; if (notes.Count < 36) notes.Add($"gekürzt „{Short(l.Text)}“"); }
			}
			else if (c is KitButton kb && kb.Text.Length > 0)
			{
				if (font.GetStringSize(kb.Text, HorizontalAlignment.Left, -1, kb.FontSize).X > kb.Size.X - 8f) { trunc++; if (notes.Count < 36) notes.Add($"Taste gekürzt „{Short(kb.Text)}“"); }
			}
		}
		// Füllgrad eines Vollbild-Bildschirms: Fläche der sichtbaren Inhalte (Hülle) / sichtbare Fläche
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
		return new Finding(state.TrimEnd('2'), w, h, f, inter.Count, overlaps, trunc, small7, small5, outside, fill, notes);
	}

	static string Label(Control c) => c switch { KitButton k => Short(k.Text), Label l => Short(l.Text), _ => c.GetType().Name };
	static string Short(string s) { s = s.Replace('\n', ' '); return s.Length > 40 ? s[..39] + "…" : s; }

	void Finish()
	{
		UiScale.TestPpi = null; UiScale.TestCap = null; UiScale.TestForce = null;
		UiScale.Apply();
		var sb = new StringBuilder("UI-PRÜFUNG (Runde 31, Etappe 0): je Bildschirm × Fenster × UI-Faktor — Ü Überlappungen interaktiver Elemente, K gekürzte Texte, "
			+ "T7/T5 Tippflächen unter 7/5 mm (angenommene DPI), A außerhalb der Fläche, F Füllgrad eines Vollbild-Bildschirms\n");
		foreach (var (w, h, ppi, dev) in Sizes) sb.AppendLine($"  {w}×{h}: {dev}, {ppi:0} ppi, Taste 56 px = {UiLayout.Millimetres(56f, w, h, 1f, ppi):0.0} mm bei Faktor 1, DPI-Ziel {UiLayout.DpiFactor(w, h, ppi):0.00}×");
		foreach (float f in Factors)
		{
			sb.AppendLine($"\n== UI-Faktor {f:0.00} ==");
			sb.Append($"{"Bildschirm",-22}");
			foreach (var (w, h, _, _) in Sizes) sb.Append($" | {$"{w}×{h}",-22}");
			sb.AppendLine();
			foreach (var state in _results.Where(r => r.Factor == f).Select(r => r.State).Distinct())
			{
				sb.Append($"{state,-22}");
				foreach (var (w, h, _, _) in Sizes)
				{
					var r = _results.FirstOrDefault(x => x.State == state && x.W == w && x.H == h && x.Factor == f);
					sb.Append(r is null ? $" | {"–",-22}" : $" | {$"Ü{r.Overlaps} K{r.Truncated} T7 {r.Small7}/{r.Interactive} T5 {r.Small5} A{r.Outside}{(r.Fill < 0 ? "" : $" F{r.Fill * 100:0}%")}",-22}");
				}
				sb.AppendLine();
			}
		}
		sb.AppendLine("\nEinzelbefunde (Faktor 1, 2340×1080):");
		foreach (var r in _results.Where(x => x.Factor == 1f && x.W == 2340))
			foreach (var n in r.Notes.Take(8)) sb.AppendLine($"  {r.State}: {n}");
		string txt = System.IO.Path.Combine(_dir, "ui-check.txt");
		System.IO.File.WriteAllText(txt, sb.ToString());
		var json = new StringBuilder("[\n");
		foreach (var r in _results)
			json.AppendLine($"  {{\"state\":\"{r.State}\",\"w\":{r.W},\"h\":{r.H},\"factor\":{r.Factor.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"interactive\":{r.Interactive},\"overlaps\":{r.Overlaps},\"truncated\":{r.Truncated},\"tapUnder7mm\":{r.Small7},\"tapUnder5mm\":{r.Small5},\"outside\":{r.Outside},\"fill\":{r.Fill.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}}},");
		System.IO.File.WriteAllText(System.IO.Path.Combine(_dir, "ui-check.json"), json.ToString().TrimEnd(',', '\n', '\r') + "\n]\n");
		GD.Print($"[ui-check] fertig: {txt}");
		GD.Print("UI-CHECK OK");
		GetTree().Quit(0);
	}
}
