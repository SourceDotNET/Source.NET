using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEMuzzleFlash")]
public partial class TEMuzzleFlash(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Angles)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Scale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Type)) { Bits = 4, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_vecAngles")]
	public Vector3 Angles;
	[NetworkVar("m_flScale")]
	public float Scale;
	[NetworkVar("m_nType")]
	public int Type;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) { }
}

public static partial class TempEnts
{
	static readonly TEMuzzleFlash g_TEMuzzleFlash = new("MuzzleFlash");

	public static void TE_MuzzleFlash<IRF>(scoped ref IRF filter, float delay, in Vector3 start, in QAngle angles, float scale, int type) where IRF : IRecipientFilter {
		g_TEMuzzleFlash.Origin = start;
		g_TEMuzzleFlash.Angles = angles;
		g_TEMuzzleFlash.Scale = scale;
		g_TEMuzzleFlash.Type = type;

		g_TEMuzzleFlash.Create(ref filter, delay);
	}
}
