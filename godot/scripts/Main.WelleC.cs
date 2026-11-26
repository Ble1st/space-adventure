using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Welle C (2): Vorwarnung, wenn die Kasse weniger als <c>PlayerUpkeepWarnDays</c> Tage Regierungsunterhalt deckt — einmal je Unterschreiten als Alarm
/// (Text aus dem Core, <see cref="PlayerUpkeepOrder.WarnText"/>); steigt die Reichweite wieder darüber, ist die Warnung wieder scharf.
/// </summary>
public partial class Main
{
	float _upkeepCheck;
	bool _upkeepWarned;
	/// <summary>Selbsttest: zuletzt gezeigte Vorwarnung.</summary>
	public string TestUpkeepWarning { get; private set; } = "";

	void UpkeepWarnTick(float delta)
	{
		if (_session is null) return;
		_upkeepCheck -= delta;
		if (_upkeepCheck > 0f) return;
		_upkeepCheck = 1f;
		bool warn = PlayerUpkeepOrder.Warn(_session.U);
		if (warn && !_upkeepWarned)
		{
			TestUpkeepWarning = PlayerUpkeepOrder.WarnText(_session.U);
			_hud.Alert(TestUpkeepWarning, 8f);
		}
		_upkeepWarned = warn;
	}
}
