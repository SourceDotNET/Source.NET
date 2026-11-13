using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
using Source.Common.Mathematics;
namespace Game.Client;

[LinkEntityToClass("manipulate_bone")]
[NetworkName("CBoneManipulate")]
public partial class C_BoneManipulate : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(BonePos)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(BoneAng)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(BoneScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(BoneJiggle)) { Bits = 4, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_BonePos")]
	public InlineArrayMaxStudioBones<Vector3> BonePos;
	[NetworkVar("m_BoneAng")]
	public InlineArrayMaxStudioBones<Vector3> BoneAng;
	[NetworkVar("m_BoneScale")]
	public InlineArrayMaxStudioBones<Vector3> BoneScale;
	[NetworkVar("m_BoneJiggle")]
	public InlineArrayMaxStudioBones<int> BoneJiggle;

	public C_BoneManipulate() {
		for (int i = 0; i < Studio.MAXSTUDIOBONES; i++) {
			BoneScale[i] = new(1.0f, 1.0f, 1.0f);
			BoneJiggle[i] = 0;
			BonePos[i] = default;
			BoneAng[i] = default;
		}
	}

	public void SetBonePosition(byte index, Vector3 pos, bool networkUpdate) {
		if (pos.Length() > 128.0f)
			pos = Vector3.Normalize(pos) * 128.0f;

		if (BonePos[index] != pos)
			BonePos[index] = pos;
	}

	public void ManipulateBone(int index, ref Vector3 pos, ref Quaternion q) {
		if ((uint)index < Studio.MAXSTUDIOBONES) {
			pos += BonePos[index];
			Vector3 ang = BoneAng[index];
			MathLib.AngleQuaternion(new QAngle(ang.X, ang.Y, ang.Z), out Quaternion angQ);
			MathLib.QuaternionMult(q, angQ, out q);
		}
	}
}
