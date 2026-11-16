using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBloodSprite", NoBase = true)]
public partial class TEBloodSprite(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Direction)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(R)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(G)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(B)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(A)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Size)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_vecDirection")]
	public Vector3 Direction;
	[NetworkVar("r")]
	public int R;
	[NetworkVar("g")]
	public int G;
	[NetworkVar("b")]
	public int B;
	[NetworkVar("a")]
	public int A;
	[NetworkVar("m_nSize")]
	public int Size;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		R = 255;
		G = 255;
		B = 63;
		A = 255;
		Size = 16;
		Origin = current_origin;

		Origin.Z += 24;

		MathLib.AngleVectors(current_angles, out Vector3 forward);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		Origin += forward * 50.0f;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEBloodSprite g_TEBloodSprite = new("Blood Sprite");

	public static void TE_BloodSprite<IRF>(scoped ref IRF filter, float delay, in Vector3 org, in Vector3 dir, int r, int g, int b, int a, int size) where IRF : IRecipientFilter {
		g_TEBloodSprite.Origin = org;
		g_TEBloodSprite.Direction = dir;
		g_TEBloodSprite.R = r;
		g_TEBloodSprite.G = g;
		g_TEBloodSprite.B = b;
		g_TEBloodSprite.A = a;
		g_TEBloodSprite.Size = size;

		g_TEBloodSprite.Create(ref filter, delay);
	}
}
