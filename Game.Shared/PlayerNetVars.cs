using Source;
using Source.Common;

using System.Numerics;

namespace Game.Shared;

public static class PlayerNetVars
{

}

public struct FogParams()
{
	[NetworkVar("dirPrimary")]
	public Vector3 DirPrimary;
	[NetworkVar("colorPrimary")]
	public Color ColorPrimary;
	[NetworkVar("colorSecondary")]
	public Color ColorSecondary;
	[NetworkVar("colorPrimaryHDR")]
	public Color ColorPrimaryHDR;
	[NetworkVar("colorSecondaryHDR")]
	public Color ColorSecondaryHDR;
	[NetworkVar("colorPrimaryLerpTo")]
	public Color ColorPrimaryLerpTo;
	[NetworkVar("colorSecondaryLerpTo")]
	public Color ColorSecondaryLerpTo;
	[NetworkVar("start")]
	public float Start;
	[NetworkVar("end")]
	public float End;
	[NetworkVar("farz")]
	public float FarZ;
	[NetworkVar("maxdensity")]
	public float MaxDensity;
	[NetworkVar("startLerpTo")]
	public float StartLerpTo;
	[NetworkVar("endLerpTo")]
	public float EndLerpTo;
	[NetworkVar("maxdensityLerpTo")]
	public float MaxDensityLerpTo;
	[NetworkVar("lerptime")]
	public TimeUnit_t LerpTime;
	[NetworkVar("duration")]
	public TimeUnit_t Duration;
	[NetworkVar("enable")]
	public bool Enable;
	[NetworkVar("blend")]
	public bool Blend;
	[NetworkVar("radial")]
	public bool Radial;
	[NetworkVar("HDRColorScale")]
	public float HDRColorScale;
}


public struct FogPlayerParams()
{
#if CLIENT_DLL || GAME_DLL
	[NetworkVar("m_hCtrl")]
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
	[NetworkVar("scale")]
	public int Scale;
	[NetworkVar("origin")]
	public Vector3 Origin;
	[NetworkVar("area")]
	public int Area;

	[NetworkVar("fog")]
	public FogParams Fog = new();
}

public struct AudioParams()
{
	[NetworkVar("localSound")]
	public InlineArrayNumLocalAudioSounds<Vector3> LocalSound;
	[NetworkVar("soundscapeIndex")]
	public int SoundscapeIndex;
	[NetworkVar("localBits")]
	public int LocalBits;
	[NetworkVar("ent")]
	public EHANDLE Ent = new();
}
