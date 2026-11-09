using System;
using Godot;
using SpaceAdventure.Ui.Kit;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Hamburger-Menü (öffnet sich unten links). Stufe 3: die Kommandozentrale (UI.md Abschnitt 4) im Vollbild –
/// die Bereichseinträge öffnen direkt den jeweiligen Vollbild-Bereich (<see cref="CommandScreen"/>), dazu die
/// vorhandenen Aktionen (Karte, Wirtschaft, Speichern/Laden/Neues Spiel). Comms zeigt den Zähler ungelesener
/// Platzhalter-Nachrichten (<see cref="UiPlaceholders.CommsUnread"/>).
/// </summary>
public partial class CommandMenu : Control, IScreen
{
	public string Id => "command";
	public string Title => "Kommandozentrale";
	public Control Node => this;
	public bool IsOpen => Visible;

	/// <summary>Aktion eines Eintrags: "map", "economy", "save", "load", "new", "close" oder Bereichs-Id aus <see cref="CommandAreas"/>.</summary>
	public event Action<string>? Action;

	// (Anzeige, Untertitel, Aktionsschlüssel)
	static readonly (string Label, string Sub, string Key)[] Entries =
	{
		("Universum", "Sektorkarte, Ebenen, Route planen", CommandAreas.Universe),
		("Sektor", "Draufsicht auf den aktuellen Sektor", CommandAreas.Sector),
		("Fraktionen", "Beziehungen und Details", CommandAreas.Factions),
		("Meine Fraktion", "Kasse, Ruf, Stufe", CommandAreas.MyFaction),
		("Flotte", "Schiffe und Befehle", CommandAreas.Fleet),
		("Besitz", "Schiffe, Stationen, Verträge, Lager", CommandAreas.Assets),
		("Comms", "Posteingang und Verhandlungen", CommandAreas.Comms),
		("System", "Speichern, Laden, Neues Spiel", CommandAreas.System),
		("Karte", "Sternenkarte öffnen", "map"),
		("Wirtschaft", "Debug-Übersicht", "economy"),
		("Speichern", "", "save"),
		("Laden", "", "load"),
		("Neues Spiel", "", "new"),
	};

	readonly KitWindow _win;

	public CommandMenu()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		_win = new KitWindow("Kommandozentrale", new Vector2(580, 688)) { Position = new Vector2(350, 16) };
		AddChild(_win);
		var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		list.AddThemeConstantOverride("separation", 6);
		foreach (var (label, sub, key) in Entries)
		{
			var b = new KitButton(label, new Vector2(540, 52)) { AlignLeft = true, FontSize = 19, Sub = sub };
			string k = key;
			b.Pressed += () => Action?.Invoke(k);
			if (key == CommandAreas.Comms && UiPlaceholders.CommsUnread > 0)
			{
				b.Detail = UiPlaceholders.CommsUnread.ToString();
				b.DetailColor = Orange;
			}
			list.AddChild(b);
		}
		scroll.AddChild(list);
		_win.Content.AddChild(scroll);
		At(_win.Content, Text("Bereiche öffnen die Kommandozentrale im Vollbild (linke Leiste, „← Flug“ unten links). Comms: " + UiPlaceholders.CommsUnread + " ungelesene Platzhalter-Nachrichten.",
			13, new Color(Dim, 0.75f)), 0, 0, 540, 40);
		var close = new KitButton("Schließen", new Vector2(540, 56)) { FontSize = 20 };
		close.Pressed += () => Action?.Invoke("close");
		_win.Content.AddChild(close);
	}

	public void Open() { Visible = true; QueueRedraw(); }
	public void Close() => Visible = false;
	public void Refresh() { }

	public override void _Draw()
	{
		if (Visible) DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.02f, 0.05f, 0.62f));
	}

	/// <summary>Klick auf den abgedunkelten Hintergrund schließt das Menü.</summary>
	public override void _GuiInput(InputEvent @event)
	{
		if (Pointer.TryRead(@event, out _, out var pos, out bool pressed, out bool motion) && !motion && pressed)
		{
			if (!new Rect2(_win.Position, _win.Size).HasPoint(pos)) Action?.Invoke("close");
			AcceptEvent();
		}
	}
}
