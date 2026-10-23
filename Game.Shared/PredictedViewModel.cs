#if CLIENT_DLL || GAME_DLL
#if CLIENT_DLL
global using PredictedViewModel = Game.Client.C_PredictedViewModel;
namespace Game.Client;
#else
global using PredictedViewModel = Game.Server.PredictedViewModel;
namespace Game.Server;
#endif

using Source.Common;
using Game.Shared;

[LinkEntityToClass("predicted_viewmodel")]
[NetworkName("CPredictedViewModel")]
public partial class
#if CLIENT_DLL
	C_PredictedViewModel
#else
	PredictedViewModel
#endif

	: BaseViewModel
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

#if CLIENT_DLL
	public override bool ShouldPredict() {
		if (GetOwner() != null && GetOwner() == C_BasePlayer.GetLocalPlayer())
			return true;

		return base.ShouldPredict();
	}
#endif
}
#endif
