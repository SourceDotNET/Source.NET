#if CLIENT_DLL || GAME_DLL

#if CLIENT_DLL
global using BaseParticleEntity = Game.Client.C_BaseParticleEntity;
using Source.Common;

using Game.Shared;
namespace Game.Client;
#else
using Source.Common;

using Game.Shared;
namespace Game.Server;
#endif

[NetworkName("CBaseParticleEntity")]
public partial class
#if CLIENT_DLL
	C_BaseParticleEntity: C_BaseEntity
#else
	BaseParticleEntity : BaseEntity
#endif
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}

#endif
