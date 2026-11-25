using System.Collections.Generic;
using SpaceAdventure.Ui.Screens;
using SpaceAdventure.World;
using SpaceSim.Core.Help;
using SpaceSim.Core.Mining;

namespace SpaceAdventure;

/// <summary>
/// Hilfe und Tutorial (UI.md „Hilfe und Tutorial“): Kontexthinweise — einmal je Lage, wenn sie zum ersten Mal besteht (Core: <see cref="HintRules"/>; „gesehen“
/// in den Einstellungen, <see cref="Game.HintSettings"/>) — und der Fortschritt des Tutorials (Core: <see cref="TutorialRules"/>). Hinweise erscheinen als Toast
/// (gleiche Platzierung wie alle Meldungen, nie über Tasten), höchstens einer alle <see cref="HintGapSeconds"/> Sekunden. Prüfläufe zeigen keine Hinweise
/// (ältere Stufen prüfen ihre eigenen Toasts), außer eine Stufe schaltet sie mit <see cref="TestHintsOn"/> ein.
/// </summary>
public partial class Main
{
	/// <summary>Abstand zwischen zwei Hinweisen (Sekunden) und Anzeigedauer.</summary>
	const float HintGapSeconds = 8f, HintSeconds = 7f;
	float _hintCheck, _hintGap;
	int _tutorialShownStep = -1;
	bool _tutorialAdvanced;
	/// <summary>Prüfläufe: Hinweise trotz Prüflauf zeigen (Stufe 168).</summary>
	internal bool TestHintsOn;
	/// <summary>Selbsttest: zuletzt gezeigter Hinweis (Schlüssel und Text), Zahl der gezeigten Hinweise, letzte Tutorialmeldung.</summary>
	internal string LastHintKey { get; private set; } = "";
	internal string LastHintText { get; private set; } = "";
	internal int HintsShown { get; private set; }
	internal readonly System.Collections.Generic.Dictionary<string, int> HintCounts = new();
	internal string LastTutorialMessage { get; private set; } = "";

	void ResetHelpSession() { _tutorialShownStep = -1; _tutorialAdvanced = false; _hintGap = 0f; _hintCheck = 0f; LastTutorialMessage = ""; }

	bool HintsAllowed => Game.HintSettings.Enabled && (!TestKnowAll || TestHintsOn);

	void HelpTick(float delta)
	{
		if (_session is null) return;
		_hintGap = System.Math.Max(0f, _hintGap - delta);
		_hintCheck -= delta;
		if (_hintCheck > 0f) return;
		_hintCheck = 0.25f;
		var u = _session.U;
		var cur = _hud.Screens.Current;
		bool command = cur is CommandScreen cs && CommandAreas.GroupOf(cs.AreaId) != "map";
		TutorialTick(u, new TutorialView(CommandOpen: command, HelpOpen: cur == _hud.CcHelp));
		if (!HintsAllowed || _hintGap > 0f || _transition) return;
		var view = new HintView(TradeOpen: cur == _hud.DockTrade, CommandOpen: command, FleetOpen: cur == _hud.CcFleet,
			MiningContact: _chunks.LastContact is ContactState.Mining or ContactState.Anchoring);
		if (HintRules.Next(HintRules.Active(u, view), Game.HintSettings.Seen) is not { } hint) return;
		Game.HintSettings.MarkSeen(hint.Key);
		LastHintKey = hint.Key;
		LastHintText = "Tipp: " + hint.Text;
		HintsShown++;
		HintCounts[hint.Key] = HintCounts.GetValueOrDefault(hint.Key) + 1;
		_hud.Toast(LastHintText, HintSeconds);
		_hintGap = HintGapSeconds;
	}

	void TutorialTick(SpaceSim.Core.Model.Universe u, TutorialView view)
	{
		if (u.Tutorial is null) return;
		if (TutorialRules.Update(u, view) is { } adv)
		{
			if (adv.Finished)
			{
				LastTutorialMessage = TutorialRules.FinishedText;
				_hud.Alert(LastTutorialMessage, 8f);
				_sfx.Play(SfxId.Click);
				return;
			}
			_tutorialShownStep = -1;   // nächstes Ziel gleich ankündigen
			_tutorialAdvanced = true;
		}
		if (TutorialRules.Current(u) is not { } step || _tutorialShownStep == u.Tutorial.Step) return;
		_tutorialShownStep = u.Tutorial.Step;
		LastTutorialMessage = (_tutorialAdvanced ? "Geschafft! Nächstes Ziel: " : "Tutorial: ") + TutorialRules.GoalText(u, step);
		_tutorialAdvanced = false;
		_hud.Toast(LastTutorialMessage, 6f);
		_hintGap = System.Math.Max(_hintGap, 3f);   // Hinweis nicht gleich darüber
	}
}
