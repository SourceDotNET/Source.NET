#if CLIENT_DLL || GAME_DLL

using Source.Common;

namespace Game.Shared;

public partial class PlayerState : PlayerEngineState
{
	[NetworkTable("DT_PlayerState")]
	public static readonly NetworkProp[] Props = [
		nameof(DeadFlag),
	];

	[PredictionTable]
	public static readonly PredictionField[] Prediction = [
		nameof(DeadFlag),
	];

	public Action? OnNetworkStateChanged;

	[NetworkVar("deadflag")]
	public partial bool DeadFlag { get; set; }

	void NetworkStateChanged(IFieldAccessor field) => OnNetworkStateChanged?.Invoke();
}
#endif
