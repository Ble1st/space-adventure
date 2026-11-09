using System;
using System.Collections.Generic;
using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Ein Bildschirm der Oberfläche (Flug-HUD, Kommandozentrale, Dock-Bereiche …). Neue Bildschirme sind
/// eine Klasse plus Registrierung in <see cref="ScreenRegistry"/>; sie folgen den Daten der Simulation.
/// </summary>
public interface IScreen
{
	string Id { get; }
	string Title { get; }
	Control Node { get; }
	bool IsOpen { get; }
	void Open();
	void Close();
	/// <summary>Daten aus der Simulation aktualisieren (z. B. pro Frame).</summary>
	void Refresh();
}

/// <summary>Registry für Bildschirme: eindeutige Ids, exklusives Öffnen der modalen Vollbildschirme.</summary>
public sealed class ScreenRegistry
{
	readonly List<IScreen> _screens = new();
	readonly Dictionary<string, IScreen> _byId = new(StringComparer.Ordinal);
	IScreen? _current;

	public IReadOnlyList<IScreen> All => _screens;
	/// <summary>Gerade exklusiv geöffneter Bildschirm (oder null).</summary>
	public IScreen? Current => _current;

	public void Register(IScreen screen)
	{
		if (_byId.ContainsKey(screen.Id)) throw new ArgumentException($"Doppelter Bildschirm-Schlüssel: {screen.Id}");
		_byId.Add(screen.Id, screen);
		_screens.Add(screen);
	}

	public IScreen Get(string id) => _byId[id];
	public bool TryGet(string id, out IScreen? screen) => _byId.TryGetValue(id, out screen);

	/// <summary>Öffnet einen Bildschirm und schließt den bisher offenen.</summary>
	public void Show(IScreen screen)
	{
		if (_current == screen) return;
		_current?.Close();
		_current = screen;
		screen.Open();
	}

	public void Hide()
	{
		_current?.Close();
		_current = null;
	}
}
