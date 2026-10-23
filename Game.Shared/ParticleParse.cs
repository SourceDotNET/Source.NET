using Source.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Game.Shared;

public enum ParticleAttachment : byte
{
	AbsOrigin = 0,
	AbsOriginFollow,
	CustomOrigin,
	Point,
	PointFollow,
	WorldOrigin,
	RootBoneFollow,

	Max,
}

public struct ParticleEffectsColors
{
	[NetworkVar("m_vecColor1")]
	public Vector3 Color1;
	[NetworkVar("m_vecColor2")]
	public Vector3 Color2;
}

public struct ParticleEffectsControlPoint
{
	[NetworkVar("m_eParticleAttachment")]
	public byte ParticleAttachment;
	[NetworkVar("m_vecOffset")]
	public Vector3 Offset;
}
