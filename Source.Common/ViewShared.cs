using Source.Common.Mathematics;

using System.Numerics;

namespace Source.Common;

public enum ClearFlags
{
	ClearColor = 0x1,
	ClearDepth = 0x2,
	ClearFullTarget = 0x4,
	NoDraw = 0x8,
	ClearObeyStencil = 0x10,
	ClearStencil = 0x20,
}

public enum StereoEye
{
	Mono = 0,
	Left = 1,
	Right = 2,
	Max = 3,
}

public struct ViewSetup
{
	public ViewSetup() {
		DoBloomAndToneMapping = true;
	}

	public int X;
	public int UnscaledX;
	public int Y;
	public int UnscaledY;
	public int Width;
	public int UnscaledWidth;
	public int Height;
	public StereoEye StereoEye;
	public int UnscaledHeight;
	public bool Ortho;
	public float OrthoLeft;
	public float OrthoTop;
	public float OrthoRight;
	public float OrthoBottom;
	public float FOV;
	public float FOVViewmodel;
	public Vector3 Origin;
	public QAngle Angles;
	public float ZNear;
	public float ZFar;
	public float ZNearViewmodel;
	public float ZFarViewmodel;
	public bool RenderToSubrectOfLargerScreen;
	public float AspectRatio;
	public bool OffCenter;
	public float OffCenterTop;
	public float OffCenterBottom;
	public float OffCenterLeft;
	public float OffCenterRight;
	public bool DoBloomAndToneMapping;
	public bool CacheFullSceneState;
	public bool ViewToProjectionOverride;
	public Matrix4x4 ViewToProjection;
}