using Game.Shared;

using Source.Common;

namespace Game.Server;

[LinkEntityToClass("func_physbox")]
[NetworkName("CPhysBox")]
public class PhysBox : Breakable
{
	public const int SF_PHYSBOX_ENABLE_ON_PHYSCANNON = 0x20000;

	public static readonly SendTable DT_PhysBox = new(DT_BaseEntity, []);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_PhysBox);
}
