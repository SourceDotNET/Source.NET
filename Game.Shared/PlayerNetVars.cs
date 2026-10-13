using Source;
using Source.Common;

using System.Numerics;

namespace Game.Shared;

public static class PlayerNetVars
{

}

public struct FogParams()
{
	[NetworkName("dirPrimary")]
	public Vector3 DirPrimary;
	[NetworkName("colorPrimary")]
	public Color ColorPrimary;
	[NetworkName("colorSecondary")]
	public Color ColorSecondary;
	[NetworkName("colorPrimaryHDR")]
	public Color ColorPrimaryHDR;
	[NetworkName("colorSecondaryHDR")]
	public Color ColorSecondaryHDR;
	[NetworkName("colorPrimaryLerpTo")]
	public Color ColorPrimaryLerpTo;
	[NetworkName("colorSecondaryLerpTo")]
	public Color ColorSecondaryLerpTo;
	[NetworkName("start")]
	public float Start;
	[NetworkName("end")]
	public float End;
	[NetworkName("farz")]
	public float FarZ;
	[NetworkName("maxdensity")]
	public float MaxDensity;
	[NetworkName("startLerpTo")]
	public float StartLerpTo;
	[NetworkName("endLerpTo")]
	public float EndLerpTo;
	[NetworkName("maxdensityLerpTo")]
	public float MaxDensityLerpTo;
	[NetworkName("lerptime")]
	public TimeUnit_t LerpTime;
	[NetworkName("duration")]
	public TimeUnit_t Duration;
	[NetworkName("enable")]
	public bool Enable;
	[NetworkName("blend")]
	public bool Blend;
	[NetworkName("radial")]
	public bool Radial;
	[NetworkName("HDRColorScale")]
	public float HDRColorScale;
}


public struct FogPlayerParams()
{
#if CLIENT_DLL || GAME_DLL
	[NetworkName("m_hCtrl")]
	public Handle<BaseEntity> Ctrl = new();
	#endif
	public TimeUnit_t TransitionTime = -1;

	public Color OldColor;
	public float OldStart;
	public float OldEnd;

	public Color NewColor;
	public float NewStart;
	public float NewEnd;
}



public struct Sky3DParams()
{
	[NetworkName("scale")]
	public int Scale;
	[NetworkName("origin")]
	public Vector3 Origin;
	[NetworkName("area")]
	public int Area;

	[NetworkName("fog")]
	public FogParams Fog = new();
}

public struct AudioParams()
{
	[NetworkName("localSound")]
	public InlineArrayNumLocalAudioSounds<Vector3> LocalSound;
	[NetworkName("soundscapeIndex")]
	public int SoundscapeIndex;
	[NetworkName("localBits")]
	public int LocalBits;
	[NetworkName("ent")]
	public EHANDLE Ent = new();
}
