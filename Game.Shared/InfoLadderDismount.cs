#if CLIENT_DLL || GAME_DLL

#if CLIENT_DLL
global using InfoLadderDismount = Game.Client.C_InfoLadderDismount;
using Source.Common;

using Game.Shared;
namespace Game.Client;
#else
using Source.Common;

using Game.Shared;
namespace Game.Server;
#endif

[LinkEntityToClass("info_ladder_dismount")]
[NetworkName("CInfoLadderDismount")]
public partial class
#if CLIENT_DLL
	C_InfoLadderDismount: C_BaseEntity
#else
	InfoLadderDismount : BaseEntity
#endif
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}

#endif
