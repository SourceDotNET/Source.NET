using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
using Source.Engine;
namespace Game.Client;

[NetworkName("CTEDecal")]
public partial class C_TEDecal : C_BaseTempEntity
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Start)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Entity)) { Bits = 13, Flags = PropFlags.Unsigned },
		new(nameof(Hitbox)) { Bits = 12, Flags = PropFlags.Unsigned },
		new(nameof(Index)) { Bits = 9, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_vecStart")]
	public Vector3 Start;
	[NetworkVar("m_nEntity")]
	public int Entity;
	[NetworkVar("m_nHitbox")]
	public int Hitbox;
	[NetworkVar("m_nIndex")]
	public int Index;

	public override void PostDataUpdate(DataUpdateType updateType) {
		BroadcastRecipientFilter filter = new();
		TE_Decal(filter, 0.0f, in Origin, in Start, Entity, Hitbox, Index);
	}
}

public static partial class TempEnts
{
	public static void TE_Decal(IRecipientFilter filter, float delay, in Vector3 pos, in Vector3 start, int entity, int hitbox, int index) {
		Trace tr = default;

		if (entity == 0 && hitbox != 0) {
			Ray ray = new();
			ray.Init(in start, in pos);
			StaticPropMgrGlobals.g_StaticPropMgr.AddDecalToStaticProp(start, pos, hitbox - 1, index, false, tr);
		}
		else {
			C_BaseEntity? ent = cl_entitylist.GetEnt(entity);
			if (ent == null)
				return;

			ent.AddDecal(in start, in pos, in pos, hitbox, index, false, ref tr);
		}
	}
}
