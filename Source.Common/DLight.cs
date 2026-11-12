namespace Source;

using System.Numerics;

[Flags]
public enum DLightFlags
{
	NoWorldIllumination = 0x1,
	NoModelIllumination = 0x2,
	AddDisplacementAlpha = 0x4,
	SubtractDisplacementAlpha = 0x8,
	DisplacementMask = AddDisplacementAlpha | SubtractDisplacementAlpha,
}

public class DLight
{
	public DLightFlags Flags;
	public Vector3 Origin;
	public float Radius;
	public ColorRGBExp32 Color;
	public TimeUnit_t Die;
	public float Decay;
	public float MinLight;
	public int Key;
	public int Style;

	public Vector3 Direction;
	public float InnerAngle;
	public float OuterAngle;

	public void Clear() {
		Flags = 0;
		Origin = default;
		Radius = 0;
		Color = default;
		Die = 0;
		Decay = 0;
		MinLight = 0;
		Key = 0;
		Style = 0;
		Direction = default;
		InnerAngle = 0;
		OuterAngle = 0;
	}

	public float GetRadius() => Radius;

	public float GetRadiusSquared() => Radius * Radius;

	public bool IsRadiusGreaterThanZero() => Radius > 0.0f;
}
