using Source.Common;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Game.Client;

public struct ParticleLightInfo
{
	[NetworkVar("m_vPos")]
	public Vector3 Pos;
	[NetworkVar("m_vColor")]
	public Vector3 Color;
	[NetworkVar("m_flIntensity")]
	public float Intensity;
}
