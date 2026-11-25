using SpaceSim.Core.Events;
using SpaceSim.Core.Pilots;

namespace SpaceAdventure;

/// <summary>Piloten: Rangaufstieg als Hinweis (groß), Abstieg als kleiner Hinweis. Reine Anzeige aus <see cref="PilotRankChanged"/>.</summary>
public partial class Main
{
	/// <summary>Selbsttest: zuletzt gezeigte Rangmeldung.</summary>
	internal string LastRankMessage { get; private set; } = "";

	bool PilotEvent(SimEvent e)
	{
		if (e is not PilotRankChanged rc) return false;
		if (rc.NewRank > rc.OldRank)
		{
			LastRankMessage = $"Beförderung: {PilotBook.RankName(rc.NewRank)} ({rc.Rating:0} Punkte)";
			_hud.Alert(LastRankMessage, 4f);
		}
		else
		{
			LastRankMessage = $"Rang gesenkt: {PilotBook.RankName(rc.NewRank)} ({rc.Rating:0} Punkte)";
			_hud.Info(LastRankMessage, 4f);
		}
		return true;
	}
}
