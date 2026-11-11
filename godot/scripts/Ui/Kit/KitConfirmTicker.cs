using System;
using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Phase 19: prüft je Bild, ob sich der Zustand einer verdrahteten <see cref="KitConfirm"/>-Taste geändert hat (Frist abgelaufen), und beschriftet sie dann neu.
/// Lebt als Kind der Taste und verschwindet mit ihr beim Neuaufbau.
/// </summary>
public partial class KitConfirmTicker : Node
{
	readonly Func<bool>? _armed;
	readonly Action? _relabel;
	bool _last;

	public KitConfirmTicker() { }

	public KitConfirmTicker(Func<bool> armed, Action relabel)
	{
		_armed = armed;
		_relabel = relabel;
		_last = armed();
	}

	public override void _Process(double delta)
	{
		if (_armed is null) return;
		bool now = _armed();
		if (now == _last) return;
		_last = now;
		_relabel?.Invoke();
	}

	/// <summary>Test-Hook: die Prüfung eines Bildes sofort ausführen.</summary>
	public void Poll() => _Process(0);
}
