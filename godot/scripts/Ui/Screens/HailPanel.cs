using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Comms;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Funk Stufe 1: Gespräch mit dem gewählten Ziel (Schiff oder Station) an der Stelle der Scanliste. Inhalt nur aus <see cref="Hail.View"/>,
/// Ausführung nur über <see cref="Hail.Execute"/> (in Main). Oben Name und Gruß, darunter eine Taste je Thema (gesperrt mit Grund), unten die Antwort.
/// </summary>
public sealed partial class HailPanel : Control
{
	public event Action<string>? Topic;
	public event Action? CloseRequested;
	Control _bg = null!;
	Label _who = null!, _greet = null!, _reply = null!, _captain = null!;
	KitScroll _scroll = null!;
	VBoxContainer _box = null!;
	KitButton _close = null!;
	string _sig = "";
	HailView? _view;

	public HailPanel() { MouseFilter = MouseFilterEnum.Stop; Visible = false; }

	public override void _Ready()
	{
		_bg = new Panel { MouseFilter = MouseFilterEnum.Ignore };
		((Panel)_bg).AddThemeStyleboxOverride("panel", Box(new Color(0.03f, 0.06f, 0.12f, 0.86f), new Color(Cyan, 0.6f), 12, 2, 0));
		AddChild(_bg);
		_who = At(this, Text("", 19, Cyan), 16, 8, 300, 26);
		_who.ClipText = true; _who.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_captain = At(this, Text("", 14, Orange), 16, 34, 300, 20);   // Piloten: Kapitän mit Rang
		_captain.Visible = false;
		_greet = At(this, Text("", 15, White), 16, 36, 300, 44);
		_greet.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_scroll = new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		AddChild(_scroll);
		_box = new VBoxContainer();
		_box.AddThemeConstantOverride("separation", 6);
		_scroll.AddChild(_box);
		_reply = At(this, Text("", 14, Green), 16, 0, 300, 44);
		_reply.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_close = new KitButton("Funk aus", new Vector2(120, 56)) { FontSize = 16 };
		_close.Pressed += () => CloseRequested?.Invoke();
		AddChild(_close);
		LayoutParts();
	}

	public override void _Notification(int what) { if (what == NotificationResized && _scroll is not null) { LayoutParts(); _sig = ""; Rebuild(); } }

	void LayoutParts()
	{
		_bg.Position = Vector2.Zero; _bg.Size = Size;
		float w = Size.X - 32f;
		bool cap = _captain.Visible;
		_who.Size = new Vector2(w, 26); _captain.Size = new Vector2(w, 20);
		_greet.Position = new Vector2(16, cap ? 56 : 36); _greet.Size = new Vector2(w, 44);
		float top = cap ? 104 : 84;
		float bottom = Size.Y - 8f - 56f;
		_close.Position = new Vector2(Size.X - 130f, bottom);
		_close.CustomMinimumSize = new Vector2(120, 56); _close.Size = new Vector2(120, 56);
		_reply.Position = new Vector2(16, bottom - 50f); _reply.Size = new Vector2(w, 46);
		_scroll.Position = new Vector2(10, top);
		_scroll.Size = new Vector2(Size.X - 20, MathF.Max(60f, bottom - 54f - top));
	}

	/// <summary>Neue Ansicht (jede HUD-Aktualisierung, solange offen) — neu aufbauen nur bei geänderten Themen.</summary>
	public void SetView(HailView view)
	{
		_view = view;
		string sig = view.Who + "|" + view.Greeting + "|" + string.Join(";", view.Topics.Select(t => $"{t.Key}:{t.Text}:{t.Possible}:{t.Reason}"));
		if (sig == _sig || _box is null) return;
		_sig = sig;
		Rebuild();
	}

	public void SetReply(string text, bool ok)
	{
		if (_reply is null) return;
		_reply.Text = text;
		_reply.AddThemeColorOverride("font_color", ok ? Green : Orange);
	}

	public void Invalidate() { _sig = ""; if (_reply is not null) _reply.Text = ""; }

	void Rebuild()
	{
		if (_box is null || _view is null) return;
		foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
		_who.Text = "📡 " + _view.Who;
		_greet.Text = "„" + _view.Greeting + "“";
		bool hadCap = _captain.Visible;
		_captain.Visible = _view.Captain is { Count: > 0 };
		if (_captain.Visible) _captain.Text = FitOption(_captain, _view.Captain!, Size.X - 32f);
		if (hadCap != _captain.Visible) LayoutParts();
		float w = MathF.Max(100f, _scroll.Size.X - 14f);
		foreach (var t in _view.Topics)
		{
			var b = new KitButton(t.Text, new Vector2(w, 60)) { AlignLeft = true, FontSize = 17, Sub = t.Possible ? t.Detail : t.Reason ?? "" };
			b.State = t.Possible ? KitButtonState.Normal : KitButtonState.Disabled;
			string key = t.Key;
			b.Pressed += () => Topic?.Invoke(key);
			_box.AddChild(b);
		}
	}

	/// <summary>Erste Form, die in die Breite passt (gemessen mit der Schrift), sonst die kürzeste.</summary>
	static string FitOption(Label l, IReadOnlyList<string> options, float width)
	{
		var font = l.GetThemeFont("font"); int size = l.GetThemeFontSize("font_size");
		foreach (var o in options) if (font.GetStringSize(o, HorizontalAlignment.Left, -1, size).X <= width) return o;
		return options[^1];
	}

	// Selbsttest
	public string TestCaptain => _captain is { Visible: true } ? _captain.Text : "";
	public KitButton? TestTopic(string textPart) => _box?.GetChildren().OfType<KitButton>().FirstOrDefault(b => b.Text.Contains(textPart));
	public string TestReply => _reply?.Text ?? "";
	public string TestWho => _who?.Text ?? "";
}
