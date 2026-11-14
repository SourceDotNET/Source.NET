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

	public Vector3 RenderMins;
	public Vector3 RenderMaxs;

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

	public void SetBoneAngles(byte index, in Vector3 ang, bool networkUpdate) {
		if (BoneAng[index] != ang)
			BoneAng[index] = ang;
	}

	public void SetBoneScale(byte index, Vector3 scale, bool networkUpdate) {
		if (scale.Length() > 512.0f)
			scale = Vector3.Normalize(scale) * 512.0f;

		if (BoneScale[index] != scale)
			BoneScale[index] = scale;
	}

	public void ScaleBone(int index, ref Matrix3x4 bone) {
		if ((uint)index < Studio.MAXSTUDIOBONES) {
			MathLib.SetScaleMatrix(BoneScale[index].X, BoneScale[index].Y, BoneScale[index].Z, out Matrix3x4 scale);
			MathLib.ConcatTransforms(bone, scale, out bone);
		}
	}

	public void PostBuildTransformations(C_BaseAnimating animating, StudioHdr hdr) {
		for (int i = 0; i < hdr.NumBones(); i++)
			ScaleBone(i, ref animating.GetBoneForWrite(i));

		modelinfo.GetModelRenderBounds(animating.GetModel(), out Vector3 mins, out Vector3 maxs);
		for (int i = 0; i < hdr.NumBones(); i++) {
			if ((animating.BoneAccessor.GetReadableBones() & hdr.Bone(i).Flags) == 0)
				continue;

			MathLib.MatrixGetColumn(animating.GetBone(i), 3, out Vector3 origin);
			if (origin.X <= -0.01f || origin.X >= 0.01f || origin.Y <= -0.01f || origin.Y >= 0.01f || origin.Z <= -0.01f || origin.Z >= 0.01f) {
				Vector3 local;
				if (animating.GetAbsAngles() == vec3_angle)
					local = origin - animating.GetAbsOrigin();
				else
					MathLib.VectorITransform(origin, animating.EntityToWorldTransform(), out local);

				float pad = 32.0f;
				if (IsPlayer() || IsNPC())
					pad = 4.0f;

				mins = Vector3.Min(mins, local - new Vector3(pad));
				maxs = Vector3.Max(maxs, local + new Vector3(pad));
			}
		}
		RenderMins = mins;
		RenderMaxs = maxs;
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
