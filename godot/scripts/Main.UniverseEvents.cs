using SpaceSim.Core.Events;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Ui;

namespace SpaceAdventure;

/// <summary>Universumsereignisse: Meldungen zu Beginn und Ende (Gewicht und Text aus <see cref="UniverseEventInfo"/>; unbekannte Sektoren bleiben stumm).</summary>
public partial class Main
{
	/// <summary>Selbsttest: zuletzt gezeigte Ereignismeldung und ihr Gewicht.</summary>
	internal string LastUniverseEventMessage { get; private set; } = "";
	internal MessageWeight LastUniverseEventWeight { get; private set; }
	/// <summary>Selbsttest: Traktorstrahl an?</summary>
	internal bool TestTractorOn => _tractorOn;

	bool UniverseEventEvent(SimEvent e)
	{
		var u = _session.U;
		switch (e)
		{
			case UniverseEventStarted s:
			{
				if (UniverseEventInfo.StartText(u, s) is not { } text) return true;
				var w = UniverseEventInfo.Weight(u, s.Sector);
				LastUniverseEventMessage = text; LastUniverseEventWeight = w;
				if (w == MessageWeight.Important) _hud.Toast(text, 5f); else _hud.Info(text, 4f);
				return true;
			}
			case UniverseEventEnded x:
			{
				if (UniverseEventInfo.EndText(u, x) is { } text) { LastUniverseEventMessage = text; _hud.Info(text, 3f); }
				return true;
			}
		}
		return false;
	}
}
