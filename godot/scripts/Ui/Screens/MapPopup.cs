using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>Ein Eintrag des Kartenmenüs: Taste mit Text/Unterzeile; <see cref="Confirm"/> = Zwei-Tipp-Bestätigung (<see cref="KitConfirm"/>).</summary>
public sealed record MapPopupEntry(string Key, string Text, string Sub, bool Enabled, Action Run, bool Confirm = false, bool On = false);

/// <summary>
/// Mehrpunkt-Route: kleines Menü über der Universums-/Sektorkarte (Kontextmenü nach langem Drücken, Routenliste). Eine Taste je Eintrag (≥ 56 px hoch),
/// liegt im übergebenen Rahmen (sicherer Bereich der Fläche); Tipp daneben oder „Schließen“ schließt, Zurück-Taste über <see cref="Close"/>.
/// Ausführung nur über die Einträge (Core-Funktionen der Aufrufer).
/// </summary>
public sealed partial class MapPopup : Control
{
	public const float W = 300f, EntryH = 56f, Gap = 6f;
	public event Action? Closed;
	readonly KitConfirm _confirm = new();
	readonly List<KitButton> _buttons = new();
	public string Title { get; private set; } = "";
	public IReadOnlyList<KitButton> Buttons => _buttons;
	public KitButton? Button(string key) => _buttons.FirstOrDefault(b => IsInstanceValid(b) && b.HasMeta("key") && b.GetMeta("key").AsString() == key);
	/// <summary>Rahmen der Karte (lokal).</summary>
	public Rect2 CardRect { get; private set; }

	public MapPopup()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		UiKit.OnTap(this, Close);   // Tipp daneben schließt (ganzer Tipp: Drücken und Loslassen auf dem Hintergrund)
	}

	/// <summary>Baut das Menü: <paramref name="anchor"/> = Stelle des Drückens (lokal zum Elternteil), <paramref name="frame"/> = erlaubter Bereich.</summary>
	public void Show(string title, string? info, IReadOnlyList<MapPopupEntry> entries, Vector2 anchor, Rect2 frame)
	{
		foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
		_buttons.Clear();
		Title = title;
		var all = entries.Append(new MapPopupEntry("close", "Schließen", "", true, Close)).ToList();
		float infoH = info is { Length: > 0 } ? Wrap(info, 13, W - 24f).Count * 17f + 4f : 0f;
		float head = 40f + infoH;
		float listH = all.Count * (EntryH + Gap);
		float maxH = MathF.Max(200f, frame.Size.Y - 8f);
		float h = MathF.Min(head + listH + 10f, maxH);
		float x = Mathf.Clamp(anchor.X + 14f, frame.Position.X + 4f, frame.End.X - W - 4f);
		float y = Mathf.Clamp(anchor.Y - 30f, frame.Position.Y + 4f, frame.End.Y - h - 4f);
		var card = CardBg(this, x, y, W, h, PanelSolid, Cyan);
		card.MouseFilter = MouseFilterEnum.Stop;
		CardRect = new Rect2(x, y, W, h);
		At(card, Text(title, 17, Cyan), 12, 8, W - 24, 26);
		if (infoH > 0f) WrappedText(card, info!, 12, 36, W - 24, 13, Dim, 17);
		var scroll = At(card, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever, MouseFilter = MouseFilterEnum.Pass },
			6, head, W - 12, h - head - 6);
		var list = new Control { MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(W - 12, listH) };
		scroll.AddChild(list);
		float ey = 0f;
		foreach (var e in all)
		{
			var b = new KitButton(e.Text, new Vector2(W - 12, EntryH)) { FontSize = 16, AlignLeft = true, Sub = e.Sub };
			b.SetMeta("key", e.Key);
			At(list, b, 0, ey);
			if (!e.Enabled) b.State = KitButtonState.Disabled;
			else if (e.Confirm) _confirm.Wire(b, e.Key, e.Text, () => e.Run(), e.Sub);
			else { if (e.On) b.State = KitButtonState.On; var run = e.Run; b.Pressed += run; }
			_buttons.Add(b);
			ey += EntryH + Gap;
		}
		Visible = true;
	}

	public void Close()
	{
		if (!Visible) return;
		Visible = false;
		_confirm.Reset();
		Closed?.Invoke();
	}
}

/// <summary>Bildschirm mit Kartenmenü: die Zurück-Taste schließt es zuerst (innerste Ebene).</summary>
public interface IMapPopupHost
{
	bool PopupOpen { get; }
	bool ClosePopup();
	MapPopup? Popup { get; }
}

/// <summary>Mehrpunkt-Route: gemeinsame Teile der beiden Karten (Taste „Route“, Routenliste) — Inhalt aus <see cref="SpaceSim.Core.Flight.PlayerRoutes"/>.</summary>
public static class RouteMenus
{
	/// <summary>Taste „Route (n)“ oben rechts in der Innenfläche (über der Detailkarte, 170 × 56).</summary>
	public static KitButton RouteButton(Control inner, SpaceSim.Core.Model.Universe u)
	{
		var r = SpaceSim.Core.Flight.PlayerRoutes.Of(u);
		int left = r is null || r.Finished ? 0 : r.Stops.Count - r.Next;
		var b = new KitButton(left > 0 ? $"Route ({left})" : "Route", new Vector2(170, 56)) { FontSize = 17, Sub = left > 0 ? "Liste · starten" : "lang drücken: +Halt" };
		b.SetMeta("key", "route");
		return At(inner, b, MathF.Max(0f, inner.Size.X - 178f), 0);
	}

	/// <summary>Einträge der Routenliste.</summary>
	public static List<MapPopupEntry> ListEntries(SpaceSim.Core.Model.Universe u, Action start, Action refresh, Action close)
	{
		var list = new List<MapPopupEntry>();
		var r = SpaceSim.Core.Flight.PlayerRoutes.Of(u);
		bool any = r is { Stops.Count: > 0 };
		list.Add(new MapPopupEntry("start", r is { Finished: true, Stops.Count: > 0 } ? "Route neu starten" : "Route starten", "Autopilot fliegt die Halte ab", any, () => { close(); start(); }));
		bool dock = r?.DockAtStations ?? false;
		list.Add(new MapPopupEntry("dock", $"An Stationen andocken: {(dock ? "an" : "aus")}", dock ? "Abdocken fliegt weiter" : "fliegt nur vorbei", true,
			() => { SpaceSim.Core.Flight.PlayerRoutes.SetDock(u, !dock); refresh(); }, On: dock));
		if (r is not null)
			for (int i = r.Next; i < r.Stops.Count; i++)
			{
				int idx = i;
				list.Add(new MapPopupEntry($"stop:{i - r.Next + 1}", $"{i - r.Next + 1}. {SpaceSim.Core.Flight.PlayerRoutes.Label(u, r.Stops[i])}", "antippen: entfernen", true,
					() => { SpaceSim.Core.Flight.PlayerRoutes.Remove(u, idx); refresh(); }));
			}
		list.Add(new MapPopupEntry("clear", "Route leeren", "", any, () => { SpaceSim.Core.Flight.PlayerRoutes.Clear(u); refresh(); }, Confirm: true));
		return list;
	}

	public static string ListInfo(SpaceSim.Core.Model.Universe u)
	{
		if (!u.Ships.TryGetValue(u.PlayerShip, out var me)) return "";
		return SpaceSim.Core.Flight.PlayerRoutes.Summary(u, u.PlayerSector, me.Position, me);
	}
}
