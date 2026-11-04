using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("manipulate_bone")]
[NetworkName("CBoneManipulate")]
public partial class BoneManipulate : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(BonePos)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(BoneAng)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(BoneScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(BoneJiggle)) { Bits = 4, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_BonePos")]
	public partial ref readonly InlineArrayMaxStudioBones<Vector3> BonePos { get; }
	[NetworkVar("m_BoneAng")]
	public partial ref readonly InlineArrayMaxStudioBones<Vector3> BoneAng { get; }
	[NetworkVar("m_BoneScale")]
	public partial ref readonly InlineArrayMaxStudioBones<Vector3> BoneScale { get; }
	[NetworkVar("m_BoneJiggle")]
	public partial ref readonly InlineArrayMaxStudioBones<int> BoneJiggle { get; }

	InlineArrayMaxStudioBones<Vector3> LocalBonePos;
	InlineArrayMaxStudioBones<Vector3> LocalBoneAng;

	public void SetBonePosition(byte index, Vector3 pos, bool networkUpdate) {
		if (pos.Length() > 128.0f)
			pos = Vector3.Normalize(pos) * 128.0f;

		if (networkUpdate && BonePos[index] != pos) {
			NetworkStateChanged();
			BonePosForModify()[index] = pos;
		}

		LocalBonePos[index] = pos;
	}

	public void SetBoneAngles(byte index, in Vector3 ang, bool networkUpdate) {
		if (networkUpdate && BoneAng[index] != ang) {
			NetworkStateChanged();
			BoneAngForModify()[index] = ang;
		}

		LocalBoneAng[index] = ang;
	}

	public void SetBoneScale(byte index, Vector3 scale) {
		if (scale.Length() > 512.0f)
			scale = Vector3.Normalize(scale) * 512.0f;

		if (BoneScale[index] != scale) {
			NetworkStateChanged();
			BoneScaleForModify()[index] = scale;
		}
	}

	public ref readonly Vector3 GetBonePosition(byte index) => ref LocalBonePos[index];
	public ref readonly Vector3 GetBoneAngles(byte index) => ref LocalBoneAng[index];
	public ref readonly Vector3 GetBoneScale(byte index) => ref BoneScale[index];
}
