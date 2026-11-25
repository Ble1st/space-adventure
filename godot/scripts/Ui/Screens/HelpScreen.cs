using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Help;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Hilfe und Tutorial: Kodex in der Kommandozentrale („Nachrichten → Hilfe“, auch ☰ → „Hilfe“). Oben ein Suchfeld, darunter die Themen als Tasten bzw. der
/// gewählte Artikel. Alle Texte kommen aus den Core-Daten (<see cref="HelpData"/>, Suche <see cref="HelpBook.Search(string)"/>); der Bereich rollt als Ganzes
/// (<see cref="KitScroll"/> der Zentrale). Getippte Suche filtert die Liste sofort, ohne den Bereich neu aufzubauen (sonst verlöre das Feld den Fokus).
/// </summary>
public sealed partial class HelpScreen : CommandScreen, ISessionScreen
{
	public override string Id => "cc.help";
	string _query = "";
	string? _article;
	Control? _list, _inner;
	Vector2 _baseMin;
	LineEdit? _search;
	readonly Dictionary<string, KitButton> _topicButtons = new();
	readonly List<string> _paragraphs = new();

	protected override string HeaderInfo(SpaceSim.Core.Model.Universe u, string playerSector) => "Hilfe · Kodex mit allen Themen";

	public void ResetSession() { _query = ""; _article = null; }

	/// <summary>Artikel offen → zurück zur Themenliste (Zurück-Taste); false = kein Artikel offen.</summary>
	public bool CloseArticle()
	{
		if (_article is null) return false;
		_article = null;
		Invalidate();
		return true;
	}

	/// <summary>Öffnet ein Thema direkt (z. B. aus einem Hinweis).</summary>
	public void ShowTopic(string key) { _article = HelpBook.Find(key) is null ? null : key; Invalidate(); }

	// ── Selbsttest ──
	public string? TestArticle => _article;
	public IReadOnlyList<string> TestParagraphs => _paragraphs;
	public int TestTopicCount => _topicButtons.Count;
	public KitButton? TestTopic(string key) => _topicButtons.TryGetValue(key, out var b) && IsInstanceValid(b) && b.IsInsideTree() ? b : null;
	public LineEdit? TestSearch => _search;
	/// <summary>Selbsttest: Suche wie getippt setzen (filtert die Liste sofort).</summary>
	public void TestSetQuery(string q) { _query = q; if (_search is not null && IsInstanceValid(_search)) _search.Text = q; FillList(); }
	public KitScroll? TestAreaScroll => _inner?.GetParent() as KitScroll;

	protected override void BuildContent(Control inner)
	{
		_inner = inner;
		_baseMin = inner.CustomMinimumSize;
		_topicButtons.Clear();
		_paragraphs.Clear();
		float iw = InnerW(inner);
		float w = MathF.Min(900f, iw - 24f);
		At(inner, Text("Hilfe · Kodex", 20, Cyan), 12, 10, w, 28);
		if (_article is { } key && HelpBook.Find(key) is { } topic)
		{
			_list = null; _search = null;
			var back = At(inner, new KitButton("← Themen", new Vector2(160, 56)) { FontSize = 17 }, 12, 44);
			back.Pressed += () => { _article = null; Invalidate(); };
			float y = WrappedText(inner, topic.Title, 12, 112, w, 22, White, 30) + 8f;
			foreach (var p in topic.Paragraphs)
			{
				_paragraphs.Add(p);
				y = WrappedText(inner, p, 12, y, w, 16, new Color(0.86f, 0.9f, 0.96f), 23) + 12f;
			}
			return;
		}
		float sw = MathF.Min(560f, w - 150f);
		_search = At(inner, KitLineEdit.Create(_query, "Suchen: Handel, Hitze, Wurmloch …", new Vector2(sw, 56), 40), 12, 44);
		_search.TextChanged += t => { _query = t; FillList(); };
		_search.TextSubmitted += _ => _search.ReleaseFocus();
		var clear = At(inner, new KitButton("Leeren", new Vector2(130, 56)) { FontSize = 17 }, 12 + sw + 10, 44);
		clear.Pressed += () => { _query = ""; Invalidate(); };
		_list = At(inner, new Control { MouseFilter = MouseFilterEnum.Pass }, 0, 112, iw, 10);
		FillList();
	}

	/// <summary>Themenliste unter dem Suchfeld (Neuaufbau nur der Liste; die Höhe des rollenden Bereichs folgt).</summary>
	void FillList()
	{
		if (_list is null || !IsInstanceValid(_list)) return;
		foreach (var c in _list.GetChildren()) { _list.RemoveChild(c); c.QueueFree(); }
		_topicButtons.Clear();
		float w = MathF.Min(900f, _list.Size.X - 24f);
		var hits = HelpBook.Search(_query);
		float y = 0f;
		if (hits.Count == 0)
		{
			y = WrappedText(_list, $"Nichts gefunden zu „{_query}“. Versuche ein anderes Wort.", 12, 4, w, 16, Dim, 22) + 8f;
		}
		foreach (var t in hits)
		{
			string preview = t.Paragraphs.Count > 0 ? t.Paragraphs[0] : "";
			var b = At(_list, new KitButton(t.Title, new Vector2(w, 64)) { FontSize = 19, AlignLeft = true, Sub = KitButton.Fit(ThemeDB.FallbackFont, preview, 14, w - 40) }, 12, y);
			string k = t.Key;
			b.Pressed += () => { _article = k; _search?.ReleaseFocus(); Invalidate(); };
			_topicButtons[k] = b;
			y += 72f;
		}
		_list.CustomMinimumSize = new Vector2(_list.Size.X, y);
		_list.Size = new Vector2(_list.Size.X, y);
		if (_inner is not null && IsInstanceValid(_inner))
		{
			float bottom = 112f + y + 8f;
			_inner.CustomMinimumSize = new Vector2(_inner.CustomMinimumSize.X, MathF.Max(bottom, _baseMin.Y));
		}
	}
}
