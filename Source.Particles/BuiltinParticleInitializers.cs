using Datamodel;

using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public static class BuiltinParticleInitializers
{
	public static void AddBuiltInParticleInitializers(ParticleSystemMgr mgr) {
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_CreateWithinSphere.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_CreateWithinBox.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_CreateOnModel.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_PositionOffset.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_PositionWarp.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_CreateSequentialPath.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_CreateAlongPath.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_CreateInHierarchy.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_InitialRepulsionVelocity.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_InitialVelocityNoise.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_InheritVelocity.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RandomLifeTime.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RandomRadius.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RandomAlpha.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RandomRotation.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RandomRotationSpeed.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RandomYaw.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RandomColor.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_ColorLitPerParticle.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RandomTrailLength.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RandomSequence.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_CreationNoise.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RemapScalar.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RemapCPtoScalar.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_AgeNoise.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_SequenceLifeTime.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RandomYawFlip.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RandomSecondSequence.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RtEnvCull.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_VelocityRandom.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RandomScalar.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RandomVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RandomVectorComponent.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_SequenceFromCP.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_VelocityFromCP.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RemapParticleCountToScalar.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RemapScalarToVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RingWave.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_CreateInEpitrochoid.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_MoveBetweenPoints.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_NormalAlignToCP.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_OffsetVectorToVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_DistanceToCPInit.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_NormalOffset.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RemapInitialDirectionToCPToVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RemapInitialCPDirectionToRotation.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_LifespanFromVelocity.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_CreateFromPlaneCache.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_CreateFromParentParticles.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_InheritFromParentParticles.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_ChaoticAttractor.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_PositionPlaceOnGround.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_SetHitboxToClosest.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_SetHitboxToModel.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_ModelCull.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RemapCPtoVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RemapSpeedToScalar.Definition);
	}
}

internal static class ParticleInitUtil
{
	public const int ROTATION_ATTRIBUTES_MASK = 0x101010;
	public const int ALPHA_ATTRIBUTES_MASK = 0x10080;
	public const int INT_ATTRIBUTES_MASK = 0x4800;
	public const float DEG2RAD = 0.017453292f;
	public const float INV_255 = 0.003921569f;
	public static readonly Vector3 Vec3Invalid = new(float.MaxValue, float.MaxValue, float.MaxValue);

	public const int GMOD_ATTRIBUTE_COUNT = 22;

	public static int ClampFieldUpper(int field) => field > GMOD_ATTRIBUTE_COUNT - 1 ? GMOD_ATTRIBUTE_COUNT - 1 : field;

	public static bool CheckField(int field) {
		if ((uint)field < GMOD_ATTRIBUTE_COUNT)
			return true;
		Warning($"GetFloatAttributePtrForWrite invalid attribute {field}\n");
		return false;
	}

	public static int CheckControlPoint(int controlPoint) {
		if ((uint)controlPoint < MAX_PARTICLE_CONTROL_POINTS)
			return controlPoint;
		Warning($"GetControlPointAtCurrentTime invalid control point {controlPoint}\n");
		return 0;
	}

	public static float ReadScalar(ParticleCollection particles, int attribute, int particle)
		=> (INT_ATTRIBUTES_MASK & (1 << attribute)) != 0 ? particles.GetIntAttributeValue(attribute, particle) : particles.GetFloatAttributeValue(attribute, particle);

	public static void WriteScalar(ParticleCollection particles, int attribute, int particle, float value) {
		if ((INT_ATTRIBUTES_MASK & (1 << attribute)) != 0)
			particles.GetIntAttributeRefForWrite(attribute, particle) = (int)value;
		else
			particles.GetFloatAttributeRefForWrite(attribute, particle) = value;
	}

	public static float RemapClamped(float value, float inMin, float inMax, float outMin, float outMax) {
		if (inMin == inMax)
			return value < inMax ? outMin : outMax;
		float t = Math.Clamp((value - inMin) / (inMax - inMin), 0.0f, 1.0f);
		return t * (outMax - outMin) + outMin;
	}

	public static Vector3 ClampedTint(Color tint, Color tintMin, Color tintMax, float amplification) {
		float scale = amplification * INV_255;
		return new(Math.Max(tintMin.R, Math.Min(tint.R, tintMax.R)) * scale,
			Math.Max(tintMin.G, Math.Min(tint.G, tintMax.G)) * scale,
			Math.Max(tintMin.B, Math.Min(tint.B, tintMax.B)) * scale);
	}

	static float Overlay(float color, float tint) => tint >= 0.5f ? 1.0f - (1.0f - color + (1.0f - color)) * (1.0f - tint) : (color + color) * tint;

	public static Vector3 BlendTint(in Vector3 colorMin, in Vector3 colorMax, float rand, in Vector3 tint, float tintPerc, int blendMode) {
		Vector3 color = (colorMax - colorMin) * rand + colorMin;
		Vector3 target = blendMode switch {
			0 => tint,
			1 => new(Overlay(color.X, tint.X), Overlay(color.Y, tint.Y), Overlay(color.Z, tint.Z)),
			2 => Vector3.Min(color, tint),
			3 => Vector3.Max(color, tint),
			4 => color * tint,
			_ => Vector3.Zero
		};
		color = (target - color) * tintPerc + color;
		return new(Math.Min(color.X, 1.0f), Math.Min(color.Y, 1.0f), Math.Min(color.Z, 1.0f));
	}

	public static Vector3 Abs(Vector3 v, in Vector3 absMask) {
		if (absMask.X != 0.0f)
			v.X = MathF.Abs(v.X);
		if (absMask.Y != 0.0f)
			v.Y = MathF.Abs(v.Y);
		if (absMask.Z != 0.0f)
			v.Z = MathF.Abs(v.Z);
		return v;
	}
}

public sealed class C_INIT_RandomLifeTime : ParticleOperatorInstance
{
	public float LifetimeMin;
	public float LifetimeMax;
	public float LifetimeRandExponent;

	public static readonly ParticleOperatorDefinition<C_INIT_RandomLifeTime> Definition = new("Lifetime Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RandomLifeTime>()
			.Float("lifetime_min", "0", (o, v) => o.LifetimeMin = v)
			.Float("lifetime_max", "0", (o, v) => o.LifetimeMax = v)
			.Float("lifetime_random_exponent", "1", (o, v) => o.LifetimeRandExponent = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;
	public override uint GetReadAttributes() => 0;

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context)
		=> InitScalarAttributeRandomRangeScalar(PARTICLE_ATTRIBUTE_LIFE_DURATION, LifetimeMin, LifetimeMax, LifetimeRandExponent, particles, firstParticle, numParticles);

	public override void InitNewParticlesBlock(ParticleCollection particles, int startBlock, int numBlocks, int attributeWriteMask, Span<byte> context)
		=> InitScalarAttributeRandomRangeBlock(PARTICLE_ATTRIBUTE_LIFE_DURATION, LifetimeMin, LifetimeMax, LifetimeRandExponent, particles, startBlock, numBlocks, false);
}

public sealed class C_INIT_RandomRadius : ParticleOperatorInstance
{
	public float RadiusMin;
	public float RadiusMax;
	public float RadiusRandExponent;

	public static readonly ParticleOperatorDefinition<C_INIT_RandomRadius> Definition = new("Radius Random", ParticleOperatorId.PiRadius, false,
		ParticleOperatorUnpack.Begin<C_INIT_RandomRadius>()
			.Float("radius_min", "1", (o, v) => o.RadiusMin = v)
			.Float("radius_max", "1", (o, v) => o.RadiusMax = v)
			.Float("radius_random_exponent", "1", (o, v) => o.RadiusRandExponent = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_RADIUS_MASK;
	public override uint GetReadAttributes() => 0;

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context)
		=> InitScalarAttributeRandomRangeScalar(PARTICLE_ATTRIBUTE_RADIUS, RadiusMin, RadiusMax, RadiusRandExponent, particles, firstParticle, numParticles);

	public override void InitNewParticlesBlock(ParticleCollection particles, int startBlock, int numBlocks, int attributeWriteMask, Span<byte> context)
		=> InitScalarAttributeRandomRangeBlock(PARTICLE_ATTRIBUTE_RADIUS, RadiusMin, RadiusMax, RadiusRandExponent, particles, startBlock, numBlocks, false);
}

public sealed class C_INIT_RandomAlpha : ParticleOperatorInstance
{
	public int AlphaMin;
	public int AlphaMax;
	public float AlphaRandExponent;
	float AlphaMinNorm;
	float AlphaMaxNorm;

	public static readonly ParticleOperatorDefinition<C_INIT_RandomAlpha> Definition = new("Alpha Random", ParticleOperatorId.PiAlpha, false,
		ParticleOperatorUnpack.Begin<C_INIT_RandomAlpha>()
			.Int("alpha_min", "255", (o, v) => o.AlphaMin = v)
			.Int("alpha_max", "255", (o, v) => o.AlphaMax = v)
			.Float("alpha_random_exponent", "1", (o, v) => o.AlphaRandExponent = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_ALPHA_MASK;
	public override uint GetReadAttributes() => 0;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		AlphaMinNorm = AlphaMin * ParticleInitUtil.INV_255;
		AlphaMaxNorm = AlphaMax * ParticleInitUtil.INV_255;
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context)
		=> InitScalarAttributeRandomRangeScalar(PARTICLE_ATTRIBUTE_ALPHA, AlphaMinNorm, AlphaMaxNorm, AlphaRandExponent, particles, firstParticle, numParticles);

	public override void InitNewParticlesBlock(ParticleCollection particles, int startBlock, int numBlocks, int attributeWriteMask, Span<byte> context)
		=> InitScalarAttributeRandomRangeBlock(PARTICLE_ATTRIBUTE_ALPHA, AlphaMinNorm, AlphaMaxNorm, AlphaRandExponent, particles, startBlock, numBlocks, false);
}

public sealed class C_INIT_RandomTrailLength : ParticleOperatorInstance
{
	public float MinLength;
	public float MaxLength;
	public float LengthRandExponent;

	public static readonly ParticleOperatorDefinition<C_INIT_RandomTrailLength> Definition = new("Trail Length Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RandomTrailLength>()
			.Float("length_min", "0.1", (o, v) => o.MinLength = v)
			.Float("length_max", "0.1", (o, v) => o.MaxLength = v)
			.Float("length_random_exponent", "1", (o, v) => o.LengthRandExponent = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_TRAIL_LENGTH_MASK;
	public override uint GetReadAttributes() => 0;

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context)
		=> InitScalarAttributeRandomRangeScalar(PARTICLE_ATTRIBUTE_TRAIL_LENGTH, MinLength, MaxLength, LengthRandExponent, particles, firstParticle, numParticles);

	public override void InitNewParticlesBlock(ParticleCollection particles, int startBlock, int numBlocks, int attributeWriteMask, Span<byte> context)
		=> InitScalarAttributeRandomRangeBlock(PARTICLE_ATTRIBUTE_TRAIL_LENGTH, MinLength, MaxLength, LengthRandExponent, particles, startBlock, numBlocks, false);
}

public abstract class GeneralRandomRotation : ParticleOperatorInstance
{
	public float Degrees;
	public float DegreesMin;
	public float DegreesMax;
	public float RotationRandExponent;
	public bool RandomlyFlipDirection;
	protected float Radians;
	protected float RadiansMin;
	protected float RadiansMax;

	protected static DmxElementUnpackStructure<T> Unpack<T>(string initial, string min, string max, string exponent, bool flip) where T : GeneralRandomRotation {
		DmxElementUnpackStructure<T> unpack = ParticleOperatorUnpack.Begin<T>()
			.Float(initial, "0", (o, v) => o.Degrees = v)
			.Float(min, "0", (o, v) => o.DegreesMin = v)
			.Float(max, "360", (o, v) => o.DegreesMax = v)
			.Float(exponent, "1", (o, v) => o.RotationRandExponent = v);
		return flip ? unpack.Bool("randomly_flip_direction", "0", (o, v) => o.RandomlyFlipDirection = v) : unpack;
	}

	protected abstract int GetAttributeToInit();

	public override uint GetWrittenAttributes() => 1u << GetAttributeToInit();
	public override uint GetReadAttributes() => 0;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		Radians = Degrees * ParticleInitUtil.DEG2RAD;
		RadiansMin = DegreesMin * ParticleInitUtil.DEG2RAD;
		RadiansMax = DegreesMax * ParticleInitUtil.DEG2RAD;
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		int attribute = GetAttributeToInit();
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			float value = particles.RandomFloatExp(RadiansMin, RadiansMax, RotationRandExponent) + Radians;
			if (RandomlyFlipDirection) {
				float rand = particles.RandomFloat(0.0f, 1.0f);
				if (rand + rand < 1.0f)
					value = -value;
			}
			particles.GetFloatAttributeRefForWrite(attribute, p) = value;
		}
	}

	public override void InitNewParticlesBlock(ParticleCollection particles, int startBlock, int numBlocks, int attributeWriteMask, Span<byte> context)
		=> InitScalarAttributeRandomRangeBlock(GetAttributeToInit(), RadiansMin, RadiansMax, RotationRandExponent, particles, startBlock, numBlocks, RandomlyFlipDirection);
}

public sealed class C_INIT_RandomRotation : GeneralRandomRotation
{
	public static readonly ParticleOperatorDefinition<C_INIT_RandomRotation> Definition = new("Rotation Random", ParticleOperatorId.PiRotation, false,
		Unpack<C_INIT_RandomRotation>("rotation_initial", "rotation_offset_min", "rotation_offset_max", "rotation_random_exponent", true));

	protected override int GetAttributeToInit() => PARTICLE_ATTRIBUTE_ROTATION;
}

public sealed class C_INIT_RandomRotationSpeed : GeneralRandomRotation
{
	public static readonly ParticleOperatorDefinition<C_INIT_RandomRotationSpeed> Definition = new("Rotation Speed Random", ParticleOperatorId.Generic, false,
		Unpack<C_INIT_RandomRotationSpeed>("rotation_speed_constant", "rotation_speed_random_min", "rotation_speed_random_max", "rotation_speed_random_exponent", true));

	protected override int GetAttributeToInit() => PARTICLE_ATTRIBUTE_ROTATION_SPEED;
}

public sealed class C_INIT_RandomYaw : GeneralRandomRotation
{
	public static readonly ParticleOperatorDefinition<C_INIT_RandomYaw> Definition = new("Rotation Yaw Random", ParticleOperatorId.PiYaw, false,
		Unpack<C_INIT_RandomYaw>("yaw_initial", "yaw_offset_min", "yaw_offset_max", "yaw_random_exponent", false));

	protected override int GetAttributeToInit() => PARTICLE_ATTRIBUTE_YAW;
}

public sealed class C_INIT_RandomYawFlip : ParticleOperatorInstance
{
	public float Percent;

	public static readonly ParticleOperatorDefinition<C_INIT_RandomYawFlip> Definition = new("Rotation Yaw Flip Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RandomYawFlip>()
			.Float("Flip Percentage", ".5", (o, v) => o.Percent = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_YAW_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_YAW_MASK;
	public override bool InitMultipleOverride() => true;

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			if (particles.RandomFloat(0.0f, 1.0f) < Percent)
				particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_YAW, p) += 3.1415927f;
		}
	}
}

public sealed class C_INIT_RandomColor : ParticleOperatorInstance
{
	public Color ColorMin;
	public Color ColorMax;
	public Color TintMin;
	public Color TintMax;
	public float TintPerc;
	public float UpdateThreshold;
	public int TintCP;
	public int FieldOutput;
	public int TintBlendMode;
	public float LightAmplification;
	Vector3 NormColorMin;
	Vector3 NormColorMax;

	public static readonly ParticleOperatorDefinition<C_INIT_RandomColor> Definition = new("Color Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RandomColor>()
			.Color("color1", "255 255 255 255", (o, v) => o.ColorMin = v)
			.Color("color2", "255 255 255 255", (o, v) => o.ColorMax = v)
			.Float("tint_perc", "0.0", (o, v) => o.TintPerc = v)
			.Int("tint control point", "0", (o, v) => o.TintCP = v)
			.Color("tint clamp min", "0 0 0 0", (o, v) => o.TintMin = v)
			.Color("tint clamp max", "255 255 255 255", (o, v) => o.TintMax = v)
			.Float("tint update movement threshold", "32", (o, v) => o.UpdateThreshold = v)
			.Int("tint blend mode", "0", (o, v) => o.TintBlendMode = v)
			.Float("light amplification amount", "1", (o, v) => o.LightAmplification = v)
			.Int("output field", "6", (o, v) => o.FieldOutput = v));

	static ref Vector3 LastPosition(Span<byte> context) => ref MemoryMarshal.AsRef<Vector3>(context);

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => 1UL << (TintCP & 63);
	public override int GetRequiredContextBytes() => 12;

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) => LastPosition(context) = Vector3.Zero;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		NormColorMin = new Vector3(ColorMin.R, ColorMin.G, ColorMin.B) * ParticleInitUtil.INV_255;
		NormColorMax = new Vector3(ColorMax.R, ColorMax.G, ColorMax.B) * ParticleInitUtil.INV_255;
		FieldOutput = Math.Clamp(FieldOutput, 0, MAX_PARTICLE_ATTRIBUTES_GMOD - 1);
	}

	const int MAX_PARTICLE_ATTRIBUTES_GMOD = 22;

	Vector3 ComputeTint(ParticleCollection particles, Span<byte> context) {
		Color tint;
		ParticleCollection? parent = particles.Parent;
		if (parent != null && parent.LocalLightingCP == TintCP)
			tint = parent.LocalLighting;
		else {
			ref Vector3 lastPosition = ref LastPosition(context);
			particles.GetControlPointAtTime(TintCP, particles.CurTime, out Vector3 origin);
			if (Vector3.Distance(lastPosition, origin) >= UpdateThreshold || particles.LocalLightingCP == -1) {
				particles.Mgr!.Query().GetLightingAtPoint(origin, out tint);
				particles.LocalLighting = tint;
				particles.LocalLightingCP = TintCP;
				lastPosition = origin;
			}
			else
				tint = particles.LocalLighting;
		}
		return ParticleInitUtil.ClampedTint(tint, TintMin, TintMax, LightAmplification);
	}

	Vector3 ColorFor(float rand, bool doTint, in Vector3 tint)
		=> doTint ? ParticleInitUtil.BlendTint(NormColorMin, NormColorMax, rand, tint, TintPerc, TintBlendMode) : (NormColorMax - NormColorMin) * rand + NormColorMin;

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		bool doTint = TintPerc != 0.0f;
		Vector3 tint = doTint ? ComputeTint(particles, context) : default;
		for (int p = firstParticle; p < firstParticle + numParticles; p++)
			particles.SetVectorAttribute(FieldOutput, p, ColorFor(particles.RandomFloat(0.0f, 1.0f), doTint, tint));
	}

	public override void InitNewParticlesBlock(ParticleCollection particles, int startBlock, int numBlocks, int attributeWriteMask, Span<byte> context) {
		bool doTint = TintPerc != 0.0f;
		Vector3 tint = doTint ? ComputeTint(particles, context) : default;
		int randContext = RandSIMD.GetContext();
		for (int b = 0; b < numBlocks; b++) {
			Vector128<float> rand = RandSIMD.Next(randContext);
			for (int lane = 0; lane < 4; lane++)
				particles.SetVectorAttribute(FieldOutput, (startBlock + b) * 4 + lane, ColorFor(rand[lane], doTint, tint));
		}
		RandSIMD.ReleaseContext(randContext);
	}
}

public sealed class C_INIT_ColorLitPerParticle : ParticleOperatorInstance
{
	public Color ColorMin;
	public Color ColorMax;
	public Color TintMin;
	public Color TintMax;
	public float TintPerc;
	public int TintBlendMode;
	public float LightAmplification;
	Vector3 NormColorMin;
	Vector3 NormColorMax;

	public static readonly ParticleOperatorDefinition<C_INIT_ColorLitPerParticle> Definition = new("Color Lit Per Particle", ParticleOperatorId.PiTintRGB, false,
		ParticleOperatorUnpack.Begin<C_INIT_ColorLitPerParticle>()
			.Color("color1", "255 255 255 255", (o, v) => o.ColorMin = v)
			.Color("color2", "255 255 255 255", (o, v) => o.ColorMax = v)
			.Float("light bias", "0.0", (o, v) => o.TintPerc = v)
			.Color("tint clamp min", "0 0 0 0", (o, v) => o.TintMin = v)
			.Color("tint clamp max", "255 255 255 255", (o, v) => o.TintMax = v)
			.Int("tint blend mode", "0", (o, v) => o.TintBlendMode = v)
			.Float("light amplification amount", "1", (o, v) => o.LightAmplification = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_TINT_RGB_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		NormColorMin = new Vector3(ColorMin.R, ColorMin.G, ColorMin.B) * ParticleInitUtil.INV_255;
		NormColorMax = new Vector3(ColorMax.R, ColorMax.G, ColorMax.B) * ParticleInitUtil.INV_255;
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			particles.Mgr!.Query().GetLightingAtPoint(particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p), out Color light);
			Vector3 tint = ParticleInitUtil.ClampedTint(light, TintMin, TintMax, LightAmplification);
			float rand = particles.RandomFloat(0.0f, 1.0f);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_TINT_RGB, p, ParticleInitUtil.BlendTint(NormColorMin, NormColorMax, rand, tint, TintPerc, TintBlendMode));
		}
	}
}

public sealed class C_INIT_RandomSequence : ParticleOperatorInstance
{
	const int MAX_SEQUENCE_TABLE = 64;

	public int SequenceMin;
	public int SequenceMax;
	public bool Shuffle;
	public bool Linear;

	public static readonly ParticleOperatorDefinition<C_INIT_RandomSequence> Definition = new("Sequence Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RandomSequence>()
			.Int("sequence_min", "0", (o, v) => o.SequenceMin = v)
			.Int("sequence_max", "0", (o, v) => o.SequenceMax = v)
			.Bool("shuffle", "0", (o, v) => o.Shuffle = v)
			.Bool("linear", "0", (o, v) => o.Linear = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER_MASK;
	public override uint GetReadAttributes() => 0;
	public override int GetRequiredContextBytes() => (MAX_SEQUENCE_TABLE + 1) * sizeof(int);

	static int GetEntry(Span<int> table, int index) => (uint)index < MAX_SEQUENCE_TABLE ? table[index + 1] : index;

	static void SetEntry(Span<int> table, int index, int value) {
		if ((uint)index < MAX_SEQUENCE_TABLE)
			table[index + 1] = value;
	}

	void ShuffleTable(ParticleCollection particles, Span<int> table) {
		for (int j = SequenceMax; SequenceMin < j; j--) {
			int swap = (int)((j - SequenceMin + 1) * particles.RandomFloat(0.0f, 1.0f)) + SequenceMin;
			int value = GetEntry(table, j);
			SetEntry(table, j, GetEntry(table, swap));
			SetEntry(table, swap, value);
		}
	}

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		if (!Shuffle && !Linear)
			return;

		Span<int> table = MemoryMarshal.Cast<byte, int>(context);
		table[0] = SequenceMin;
		for (int i = SequenceMin; i <= SequenceMax; i++)
			SetEntry(table, i, i);
		if (Shuffle && SequenceMin < SequenceMax)
			ShuffleTable(particles, table);
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		if (!Shuffle && !Linear) {
			for (int p = firstParticle; p < firstParticle + numParticles; p++)
				particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER, p) = particles.RandomInt(SequenceMin, SequenceMax);
			return;
		}

		Span<int> table = MemoryMarshal.Cast<byte, int>(context);
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			if (SequenceMax < table[0]) {
				if (Shuffle && SequenceMin < SequenceMax)
					ShuffleTable(particles, table);
				table[0] = SequenceMin;
			}
			particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER, p) = GetEntry(table, table[0]);
			table[0]++;
		}
	}
}

public sealed class C_INIT_RandomSecondSequence : ParticleOperatorInstance
{
	public int SequenceMin;
	public int SequenceMax;

	public static readonly ParticleOperatorDefinition<C_INIT_RandomSecondSequence> Definition = new("Sequence Two Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RandomSecondSequence>()
			.Int("sequence_min", "0", (o, v) => o.SequenceMin = v)
			.Int("sequence_max", "0", (o, v) => o.SequenceMax = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER1_MASK;
	public override uint GetReadAttributes() => 0;

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		for (int p = firstParticle; p < firstParticle + numParticles; p++)
			particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER1, p) = particles.RandomInt(SequenceMin, SequenceMax);
	}
}

public sealed class C_INIT_SequenceLifeTime : ParticleOperatorInstance
{
	public float FramesPerSecond;

	public static readonly ParticleOperatorDefinition<C_INIT_SequenceLifeTime> Definition = new("Lifetime From Sequence", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_SequenceLifeTime>()
			.Float("Frames Per Second", "30", (o, v) => o.FramesPerSecond = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER_MASK;
	public override bool InitMultipleOverride() => true;

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		Sheet? sheet = particles.Sheet;
		if (FramesPerSecond == 0.0f || sheet == null)
			return;

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			int sequence = (int)particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER, p);
			float frameSpan = (uint)sequence < Sheet.MAX_SEQUENCES ? sheet.FrameSpan[sequence] : 0.0f;
			particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_LIFE_DURATION, p) = frameSpan == 0.0f ? 1.0f : frameSpan / FramesPerSecond;
		}
	}
}

public sealed class C_INIT_CreateWithinSphere : ParticleOperatorInstance
{
	public float RadiusMin;
	public float RadiusMax;
	public Vector3 DistanceBias;
	public Vector3 DistanceBiasAbs;
	public int ControlPointNumber;
	public int ScaleCP;
	public float SpeedMin;
	public float SpeedMax;
	public float SpeedRandExp;
	public bool LocalCoords;
	public bool UseHighestEndCP;
	public float EndCPGrowthTime;
	public Vector3 LocalCoordinateSystemSpeedMin;
	public Vector3 LocalCoordinateSystemSpeedMax;
	public int CreateInModel;
	bool HasDistanceBias;
	bool HasDistanceBiasAbs;

	public static readonly ParticleOperatorDefinition<C_INIT_CreateWithinSphere> Definition = new("Position Within Sphere Random", ParticleOperatorId.PiPosition, false,
		ParticleOperatorUnpack.Begin<C_INIT_CreateWithinSphere>()
			.Float("distance_min", "0", (o, v) => o.RadiusMin = v)
			.Float("distance_max", "0", (o, v) => o.RadiusMax = v)
			.Vector3("distance_bias", "1 1 1", (o, v) => o.DistanceBias = v)
			.Vector3("distance_bias_absolute_value", "0 0 0", (o, v) => o.DistanceBiasAbs = v)
			.Bool("bias in local system", "0", (o, v) => o.LocalCoords = v)
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v)
			.Float("speed_min", "0", (o, v) => o.SpeedMin = v)
			.Float("speed_max", "0", (o, v) => o.SpeedMax = v)
			.Float("speed_random_exponent", "1", (o, v) => o.SpeedRandExp = v)
			.Vector3("speed_in_local_coordinate_system_min", "0 0 0", (o, v) => o.LocalCoordinateSystemSpeedMin = v)
			.Vector3("speed_in_local_coordinate_system_max", "0 0 0", (o, v) => o.LocalCoordinateSystemSpeedMax = v)
			.Int("create in model", "0", (o, v) => o.CreateInModel = v)
			.Bool("randomly distribute to highest supplied Control Point", "0", (o, v) => o.UseHighestEndCP = v)
			.Float("randomly distribution growth time", "0", (o, v) => o.EndCPGrowthTime = v)
			.Int("scale cp (distance/speed/local speed)", "-1", (o, v) => o.ScaleCP = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;

	public override ulong GetReadControlPointMask() {
		ulong mask = 1UL << (ControlPointNumber & 63);
		if (UseHighestEndCP)
			return ~(mask - 1);
		if (ScaleCP != -1)
			mask |= 1UL << (ScaleCP & 63);
		return mask;
	}

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		ControlPointNumber = Math.Clamp(ControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		ScaleCP = Math.Clamp(ScaleCP, -1, MAX_PARTICLE_CONTROL_POINTS - 1);
		HasDistanceBias = DistanceBias.X != 1.0f || DistanceBias.Y != 1.0f || DistanceBias.Z != 1.0f;
		HasDistanceBiasAbs = DistanceBiasAbs.X != 0.0f || DistanceBiasAbs.Y != 0.0f || DistanceBiasAbs.Z != 0.0f;
	}

	int PickControlPoint(ParticleCollection particles, List<int>? validCPs) {
		if (validCPs == null || validCPs.Count == 0)
			return ControlPointNumber;

		float growth = 1.0f;
		if (EndCPGrowthTime != 0.0f)
			growth = Math.Min(particles.CurTime, EndCPGrowthTime) / EndCPGrowthTime;

		if (growth >= 1.0f)
			return validCPs[(int)(validCPs.Count * particles.RandomFloat(0.0f, 1.0f))];

		int highest = particles.GetHighestControlPoint();
		int low = ControlPointNumber;
		int high = highest < ControlPointNumber ? ControlPointNumber : highest;
		int query = particles.RandomQueryCount++;
		growth = Math.Clamp(growth, 0.0f, 1.0f);
		int currentHigh = (int)MathF.Floor(growth * (high - low) + low);
		int target = (int)((currentHigh - low + 1) * particles.RandomFloat(query, 0.0f, 1.0f)) + low;
		int found = validCPs.IndexOf(target);
		if (found >= 0)
			return validCPs[found];

		particles.RandomQueryCount = query + 2;
		return validCPs[(int)(validCPs.Count * particles.RandomFloat(query + 1, 0.0f, 1.0f))];
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		Vector3 scale = Vector3.One;
		if (ScaleCP >= 0)
			scale = particles.ControlPoints[ScaleCP].Position;

		List<int>? validCPs = null;
		if (UseHighestEndCP) {
			validCPs = [];
			for (int i = ControlPointNumber; i <= particles.GetHighestControlPoint(); i++) {
				if (particles.ControlPoints[i].Position != ParticleInitUtil.Vec3Invalid)
					validCPs.Add(i);
			}
		}

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			float time = Math.Max(particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p), particles.CurTime - particles.Dt);
			int currentControlPoint = UseHighestEndCP ? PickControlPoint(particles, validCPs) : ControlPointNumber;

			Vector3 randomPosition;
			Vector3 randomDirection;
			int tries = 0;
			while (true) {
				float length = particles.RandomVectorInUnitSphere(particles.RandomQueryCount, out randomDirection);
				particles.RandomQueryCount += 3;

				if (HasDistanceBiasAbs)
					randomDirection = ParticleInitUtil.Abs(randomDirection, DistanceBiasAbs);
				randomDirection *= DistanceBias;
				MathLib.VectorNormalize(ref randomDirection);

				randomPosition = randomDirection * (((RadiusMax - RadiusMin) * length + RadiusMin) * scale.X);

				if (!HasDistanceBias || !LocalCoords) {
					particles.GetControlPointAtTime(currentControlPoint, time, out Vector3 controlPoint);
					randomPosition += controlPoint;
				}
				else {
					particles.GetControlPointTransformAtTime(currentControlPoint, time, out Matrix3x4 transform);
					MathLib.VectorTransform(in randomPosition, in transform, out randomPosition);
				}

				if (CreateInModel == 0)
					break;
				if (particles.Mgr!.Query().MovePointInsideControllingObject(particles, particles.ControlPoints[currentControlPoint].Object, ref randomPosition))
					break;
				if (++tries >= 10)
					break;
			}

			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, randomPosition);

			if ((attributeWriteMask & PARTICLE_ATTRIBUTE_PREV_XYZ_MASK) == 0)
				continue;

			Vector3 offset = Vector3.Zero;
			if (SpeedMax > 0.0f)
				offset = -randomDirection * (particles.RandomFloatExp(SpeedMin, SpeedMax, SpeedRandExp) * scale.Y);

			float localX = particles.RandomFloat(LocalCoordinateSystemSpeedMin.X, LocalCoordinateSystemSpeedMax.X);
			float localY = particles.RandomFloat(LocalCoordinateSystemSpeedMin.Y, LocalCoordinateSystemSpeedMax.Y);
			float localZ = particles.RandomFloat(LocalCoordinateSystemSpeedMin.Z, LocalCoordinateSystemSpeedMax.Z);

			ref readonly ParticleControlPoint cp = ref particles.ControlPoint(currentControlPoint);
			offset -= cp.ForwardVector * (localX * scale.Z);
			offset -= cp.RightVector * (localY * scale.Z);
			offset -= cp.UpVector * (localZ * scale.Z);

			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, randomPosition + offset * particles.PreviousDt);
		}
	}
}

public sealed class C_INIT_CreateWithinBox : ParticleOperatorInstance
{
	public Vector3 Min;
	public Vector3 Max;
	public int ControlPointNumber;
	public bool LocalSpace;

	public static readonly ParticleOperatorDefinition<C_INIT_CreateWithinBox> Definition = new("Position Within Box Random", ParticleOperatorId.PiPosition, false,
		ParticleOperatorUnpack.Begin<C_INIT_CreateWithinBox>()
			.Vector3("min", "0 0 0", (o, v) => o.Min = v)
			.Vector3("max", "0 0 0", (o, v) => o.Max = v)
			.Int("control point number", "0", (o, v) => o.ControlPointNumber = v)
			.Bool("use local space", "0", (o, v) => o.LocalSpace = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override ulong GetReadControlPointMask() => 1UL << (ControlPointNumber & 63);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		int controlPoint = Math.Clamp(ControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			float creationTime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p);
			particles.RandomVector(Min, Max, out Vector3 position);

			if (!LocalSpace) {
				particles.GetControlPointAtTime(controlPoint, creationTime, out Vector3 cpPosition);
				position += cpPosition;
			}
			else {
				particles.GetControlPointTransformAtTime(controlPoint, creationTime, out Matrix3x4 transform);
				MathLib.VectorRotate(in position, in transform, out position);
			}

			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, position);
			if ((attributeWriteMask & PARTICLE_ATTRIBUTE_PREV_XYZ_MASK) != 0)
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, position);
		}
	}
}

public sealed class C_INIT_CreateOnModel : ParticleOperatorInstance
{
	const int MAX_POINTS_PER_BATCH = 100;

	public int ControlPointNumber;
	public int ForceInModel;
	public int DesiredHitbox;
	public float HitBoxScale;
	public Vector3 DirectionBias;
	public string HitboxSetName = "effects";

	public static readonly ParticleOperatorDefinition<C_INIT_CreateOnModel> Definition = new("Position on Model Random", ParticleOperatorId.PiPosition, false,
		ParticleOperatorUnpack.Begin<C_INIT_CreateOnModel>()
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v)
			.Int("force to be inside model", "0", (o, v) => o.ForceInModel = v)
			.Float("model hitbox scale", "1.0", (o, v) => o.HitBoxScale = v)
			.Vector3("direction bias", "0 0 0", (o, v) => o.DirectionBias = v)
			.Int("desired hitbox", "-1", (o, v) => o.DesiredHitbox = v)
			.String("hitbox set", "effects", 128, (o, v) => o.HitboxSetName = v));

	public override uint GetWrittenAttributes() => 0xc105;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override ulong GetReadControlPointMask() => 1UL << (ControlPointNumber & 63);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		int controlPoint = Math.Clamp(ControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		particles.UpdateHitBoxInfo(controlPoint, HitboxSetName);

		Span<Vector3> points = stackalloc Vector3[MAX_POINTS_PER_BATCH];
		Span<Vector3> relativeCoords = stackalloc Vector3[MAX_POINTS_PER_BATCH];
		Span<int> hitboxIndices = stackalloc int[MAX_POINTS_PER_BATCH];
		int particle = firstParticle;
		while (numParticles > 0) {
			int count = Math.Min(numParticles, MAX_POINTS_PER_BATCH);
			points[..count].Clear();
			relativeCoords[..count].Clear();
			hitboxIndices[..count].Clear();
			particles.Mgr!.Query().GetRandomPointsOnControllingObjectHitBox(particles, controlPoint, count, HitBoxScale, ForceInModel, points, DirectionBias, relativeCoords, hitboxIndices, DesiredHitbox, HitboxSetName);

			for (int i = 0; i < count; i++, particle++) {
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, particle, points[i]);
				if ((attributeWriteMask & PARTICLE_ATTRIBUTE_PREV_XYZ_MASK) != 0)
					particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, particle, points[i]);
				if ((attributeWriteMask & PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ_MASK) != 0)
					particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ, particle, relativeCoords[i]);
				if ((attributeWriteMask & PARTICLE_ATTRIBUTE_HITBOX_INDEX_MASK) != 0)
					particles.GetIntAttributeRefForWrite(PARTICLE_ATTRIBUTE_HITBOX_INDEX, particle) = hitboxIndices[i];
			}
			numParticles -= count;
		}
	}
}

public sealed class C_INIT_PositionOffset : ParticleOperatorInstance
{
	public Vector3 OffsetMin;
	public Vector3 OffsetMax;
	public int ControlPointNumber;
	public bool LocalCoords;
	public bool Proportional;

	public static readonly ParticleOperatorDefinition<C_INIT_PositionOffset> Definition = new("Position Modify Offset Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_PositionOffset>()
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v)
			.Vector3("offset min", "0 0 0", (o, v) => o.OffsetMin = v)
			.Vector3("offset max", "0 0 0", (o, v) => o.OffsetMax = v)
			.Bool("offset in local space 0/1", "0", (o, v) => o.LocalCoords = v)
			.Bool("offset proportional to radius 0/1", "0", (o, v) => o.Proportional = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_RADIUS_MASK | PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override ulong GetReadControlPointMask() => 1UL << ControlPointNumber;
	public override bool InitMultipleOverride() => true;

	public override void InitParams(ParticleSystemDefinition def, Element element)
		=> ControlPointNumber = Math.Clamp(ControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			Vector3 offset;
			if (!Proportional)
				particles.RandomVector(OffsetMin, OffsetMax, out offset);
			else {
				float radius = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_RADIUS, p);
				particles.RandomVector(OffsetMin * radius, OffsetMax * radius, out offset);
			}

			if (LocalCoords) {
				particles.GetControlPointTransformAtTime(ControlPointNumber, particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p), out Matrix3x4 transform);
				MathLib.VectorRotate(in offset, in transform, out offset);
			}

			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) + offset);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p) + offset);
		}
	}
}

public sealed class C_INIT_PositionWarp : ParticleOperatorInstance
{
	struct Context
	{
		public float StartTime;
		public int StartCount;
		public int ParticleCount;
	}

	public Vector3 WarpMin;
	public Vector3 WarpMax;
	public int ControlPointNumber;
	public float WarpTime;
	public float WarpStartTime;
	public bool InvertWarp;
	public bool UseCount;

	public static readonly ParticleOperatorDefinition<C_INIT_PositionWarp> Definition = new("Position Modify Warp Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_PositionWarp>()
			.Int("control point number", "0", (o, v) => o.ControlPointNumber = v)
			.Vector3("warp min", "1 1 1", (o, v) => o.WarpMin = v)
			.Vector3("warp max", "1 1 1", (o, v) => o.WarpMax = v)
			.Float("warp transition time (treats min/max as start/end sizes)", "0", (o, v) => o.WarpTime = v)
			.Float("warp transition start time", "0", (o, v) => o.WarpStartTime = v)
			.Bool("reverse warp (0/1)", "0", (o, v) => o.InvertWarp = v)
			.Bool("use particle count instead of time", "0", (o, v) => o.UseCount = v));

	static ref Context Ctx(Span<byte> context) => ref MemoryMarshal.AsRef<Context>(context);

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_RADIUS_MASK;
	public override ulong GetReadControlPointMask() => 1UL << ControlPointNumber;
	public override bool InitMultipleOverride() => true;
	public override int GetRequiredContextBytes() => 12;

	public override void InitParams(ParticleSystemDefinition def, Element element)
		=> ControlPointNumber = Math.Clamp(ControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		ctx.StartTime = particles.CurTime;
		ctx.StartCount = 0;
		ctx.ParticleCount = 0;
	}

	public override void Restart(ParticleCollection particles, Span<byte> context) => InitializeContextData(particles, context);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		Vector3 warpEnd = InvertWarp ? WarpMin : WarpMax;
		Vector3 warpStart = InvertWarp ? WarpMax : WarpMin;

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			float creationTime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p);
			Vector3 warp;
			if (WarpTime == 0.0f)
				particles.RandomVector(WarpMin, WarpMax, out warp);
			else {
				float t;
				if (!UseCount) {
					float start = WarpStartTime + ctx.StartTime;
					t = ParticleInitUtil.RemapClamped(creationTime, start, start + WarpTime, 0.0f, 1.0f);
				}
				else {
					float start = WarpStartTime + ctx.StartCount;
					t = ParticleInitUtil.RemapClamped(ctx.ParticleCount, start, start + WarpTime, 0.0f, 1.0f);
					ctx.ParticleCount++;
				}
				warp = (warpEnd - warpStart) * t + warpStart;
			}

			particles.GetControlPointTransformAtTime(ControlPointNumber, creationTime, out Matrix3x4 transform);

			MathLib.VectorITransform(particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p), in transform, out Vector3 local);
			MathLib.VectorTransform(local * warp, in transform, out Vector3 position);
			MathLib.VectorITransform(particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p), in transform, out Vector3 prevLocal);
			MathLib.VectorTransform(prevLocal * warp, in transform, out Vector3 prevPosition);

			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, position);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, prevPosition);
		}
	}
}

public sealed class C_INIT_CreateSequentialPath : ParticleOperatorInstance
{
	struct Context
	{
		public int Unused;
		public float PathPosition;
		public float StepDirection;
		public float CPPairPosition;
		public float Step;
		public float CPStep;
	}

	public float MaxDistance;
	public float NumToAssign;
	public bool Loop;
	public bool CPPairs;
	public bool SaveOffset;
	public PathParameters PathParams;

	public static readonly ParticleOperatorDefinition<C_INIT_CreateSequentialPath> Definition = new("Position Along Path Sequential", ParticleOperatorId.PiPosition, false,
		ParticleOperatorUnpack.Begin<C_INIT_CreateSequentialPath>()
			.Float("maximum distance", "0", (o, v) => o.MaxDistance = v)
			.Float("bulge", "0", (o, v) => o.PathParams.Bulge = v)
			.Int("start control point number", "0", (o, v) => o.PathParams.StartControlPointNumber = v)
			.Int("end control point number", "0", (o, v) => o.PathParams.EndControlPointNumber = v)
			.Int("bulge control 0=random 1=orientation of start pnt 2=orientation of end point", "0", (o, v) => o.PathParams.BulgeControl = v)
			.Float("mid point position", "0.5", (o, v) => o.PathParams.MidPoint = v)
			.Float("particles to map from start to end", "100", (o, v) => o.NumToAssign = v)
			.Bool("restart behavior (0 = bounce, 1 = loop )", "1", (o, v) => o.Loop = v)
			.Bool("Use sequential CP pairs between start and end point", "0", (o, v) => o.CPPairs = v)
			.Bool("Save Offset", "0", (o, v) => o.SaveOffset = v));

	static ref Context Ctx(Span<byte> context) => ref MemoryMarshal.AsRef<Context>(context);

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override int GetRequiredContextBytes() => 24;

	public override ulong GetReadControlPointMask() {
		ulong startMask = (1UL << PathParams.StartControlPointNumber) - 1;
		if (CPPairs)
			return ~startMask;
		return ~startMask & ((1UL << ((PathParams.EndControlPointNumber + 1) & 63)) - 1);
	}

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		PathParams.StartControlPointNumber = Math.Clamp(PathParams.StartControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		PathParams.EndControlPointNumber = Math.Clamp(PathParams.EndControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
	}

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		ctx.PathPosition = 0.0f;
		ctx.Step = NumToAssign <= 1.0f ? 0.0f : 1.0f / (NumToAssign - 1.0f);
		ctx.StepDirection = -1.0f;
		if (!CPPairs)
			return;

		ctx.CPPairPosition = PathParams.StartControlPointNumber;
		int pairs = PathParams.EndControlPointNumber - PathParams.StartControlPointNumber;
		if (pairs > 1 && NumToAssign > 1.0f) {
			ctx.Step = pairs * ctx.Step;
			ctx.CPStep = ctx.Step;
		}
		else
			ctx.CPStep = 0.0f;
	}

	public override void Restart(ParticleCollection particles, Span<byte> context) => InitializeContextData(particles, context);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			PathParameters path = PathParams;
			if (CPPairs) {
				float span = PathParams.EndControlPointNumber - PathParams.StartControlPointNumber;
				if (span < ctx.CPPairPosition || ctx.CPPairPosition < PathParams.StartControlPointNumber) {
					if (!Loop) {
						ctx.CPStep = -ctx.CPStep;
						ctx.Step = -ctx.Step;
						ctx.StepDirection = -ctx.StepDirection;
						ctx.CPPairPosition = ctx.CPStep + ctx.CPStep + ctx.CPPairPosition;
						ctx.PathPosition = ctx.Step + ctx.Step + ctx.PathPosition;
					}
					else
						ctx.CPPairPosition -= span;
				}
				path.StartControlPointNumber = (int)ctx.CPPairPosition;
				path.EndControlPointNumber = (int)ctx.CPPairPosition + 1;
			}

			if (ctx.PathPosition > 1.0000001f || ctx.PathPosition < 0.0f) {
				if (!Loop && !CPPairs) {
					ctx.Step = -ctx.Step;
					ctx.StepDirection = -ctx.StepDirection;
					ctx.PathPosition = ctx.Step + ctx.Step + ctx.PathPosition;
				}
				else
					ctx.PathPosition += ctx.StepDirection;
			}

			float t = ctx.PathPosition;
			particles.CalculatePathValues(in path, particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p), out Vector3 startPnt, out Vector3 midPnt, out Vector3 endPnt);

			int query = particles.RandomQueryCount;
			particles.RandomQueryCount += 3;
			Vector3 randomOffset = new(
				particles.RandomFloat(query, -MaxDistance, MaxDistance),
				particles.RandomFloat(query + 1, -MaxDistance, MaxDistance),
				particles.RandomFloat(query + 2, -MaxDistance, MaxDistance));

			Vector3 startMid = (midPnt - startPnt) * t + startPnt;
			Vector3 midEnd = (endPnt - midPnt) * t + midPnt;
			Vector3 position = (midEnd - startMid) * t + startMid + randomOffset;

			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, position);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, position);
			if (SaveOffset)
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ, p, new Vector3(t, path.StartControlPointNumber, path.EndControlPointNumber));

			ctx.PathPosition += ctx.Step;
			ctx.CPPairPosition += ctx.CPStep;
		}
	}
}

public sealed class C_INIT_CreateAlongPath : ParticleOperatorInstance
{
	public float MaxDistance;
	public PathParameters PathParams;
	public bool UseRandomCPPairs;

	public static readonly ParticleOperatorDefinition<C_INIT_CreateAlongPath> Definition = new("Position Along Path Random", ParticleOperatorId.PiPosition, false,
		ParticleOperatorUnpack.Begin<C_INIT_CreateAlongPath>()
			.Float("maximum distance", "0", (o, v) => o.MaxDistance = v)
			.Float("bulge", "0", (o, v) => o.PathParams.Bulge = v)
			.Int("start control point number", "0", (o, v) => o.PathParams.StartControlPointNumber = v)
			.Int("end control point number", "0", (o, v) => o.PathParams.EndControlPointNumber = v)
			.Int("bulge control 0=random 1=orientation of start pnt 2=orientation of end point", "0", (o, v) => o.PathParams.BulgeControl = v)
			.Float("mid point position", "0.5", (o, v) => o.PathParams.MidPoint = v)
			.Bool("randomly select sequential CP pairs between start and end points", "0", (o, v) => o.UseRandomCPPairs = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;

	public override ulong GetReadControlPointMask()
		=> ((1UL << ((PathParams.EndControlPointNumber + 1) & 63)) - 1) & ~((1UL << (PathParams.StartControlPointNumber & 63)) - 1);

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		PathParams.StartControlPointNumber = Math.Clamp(PathParams.StartControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		PathParams.EndControlPointNumber = Math.Clamp(PathParams.EndControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			PathParameters path = PathParams;
			if (UseRandomCPPairs) {
				int low = path.StartControlPointNumber + 1;
				int end = (int)((path.EndControlPointNumber - low + 1) * particles.RandomFloat(0.0f, 1.0f)) + low;
				path.StartControlPointNumber = end - 1;
				path.EndControlPointNumber = end;
			}

			particles.CalculatePathValues(in path, particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p), out Vector3 startPnt, out Vector3 midPnt, out Vector3 endPnt);

			int query = particles.RandomQueryCount;
			particles.RandomQueryCount += 4;
			float t = particles.RandomFloat(query, 0.0f, 1.0f);
			Vector3 randomOffset = new(
				particles.RandomFloat(query + 1, -MaxDistance, MaxDistance),
				particles.RandomFloat(query + 2, -MaxDistance, MaxDistance),
				particles.RandomFloat(query + 3, -MaxDistance, MaxDistance));

			Vector3 startMid = (midPnt - startPnt) * t + startPnt;
			Vector3 midEnd = (endPnt - midPnt) * t + midPnt;
			Vector3 position = (midEnd - startMid) * t + startMid + randomOffset;

			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, position);
			if ((attributeWriteMask & PARTICLE_ATTRIBUTE_PREV_XYZ_MASK) != 0)
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, position);
		}
	}
}

public sealed class C_INIT_InitialRepulsionVelocity : ParticleOperatorInstance
{
	static readonly Vector3[] TraceDirections = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];

	public string CollisionGroupName = "NONE";
	public int CollisionGroupNumber;
	public Vector3 MinVelocity;
	public Vector3 MaxVelocity;
	public int ControlPointNumber;
	public bool PerParticle;
	public bool Translate;
	public bool Proportional;
	public float TraceLength;
	public bool PerParticleTR;
	public bool InheritFromParent;
	public int ChildCP;
	public int ChildGroupID;

	public static readonly ParticleOperatorDefinition<C_INIT_InitialRepulsionVelocity> Definition = new("Velocity Repulse from World", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_InitialRepulsionVelocity>()
			.Vector3("minimum velocity", "0 0 0", (o, v) => o.MinVelocity = v)
			.Vector3("maximum velocity", "1 1 1", (o, v) => o.MaxVelocity = v)
			.String("collision group", "NONE", 128, (o, v) => o.CollisionGroupName = v)
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v)
			.Bool("Per Particle World Collision Tests", "0", (o, v) => o.PerParticle = v)
			.Bool("Use radius for Per Particle Trace Length", "0", (o, v) => o.PerParticleTR = v)
			.Bool("Offset instead of accelerate", "0", (o, v) => o.Translate = v)
			.Bool("Offset proportional to radius 0/1", "0", (o, v) => o.Proportional = v)
			.Float("Trace Length", "64.0", (o, v) => o.TraceLength = v)
			.Bool("Inherit from Parent", "0", (o, v) => o.InheritFromParent = v)
			.Int("control points to broadcast to children (n + 1)", "-1", (o, v) => o.ChildCP = v)
			.Int("Child Group ID to affect", "0", (o, v) => o.ChildGroupID = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_RADIUS_MASK | PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override bool InitMultipleOverride() => true;

	public override ulong GetReadControlPointMask() {
		if (PerParticle)
			return 0;
		ulong mask = 1UL << (ControlPointNumber & 63);
		if (InheritFromParent)
			mask |= 1UL << ((ControlPointNumber + 1) & 63);
		return mask;
	}

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		CollisionGroupNumber = def.Mgr.Query().GetCollisionGroupFromName(CollisionGroupName);
		ControlPointNumber = Math.Clamp(ControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
	}

	float TraceRepulsion(IParticleSystemQuery query, in Vector3 origin, float traceLength, out Vector3 direction) {
		float total = 0.0f;
		direction = Vector3.Zero;
		for (int i = 0; i < TraceDirections.Length; i++) {
			GameTrace trace = default;
			query.TraceLine(origin, origin + TraceDirections[i] * traceLength, (Mask)Contents.Solid, null, CollisionGroupNumber, ref trace);
			float blocked = 1.0f - trace.Fraction;
			total += blocked;
			direction -= TraceDirections[i] * blocked;
		}

		if (direction == Vector3.Zero) {
			direction = new Vector3(0, 0, 1);
			total = 0.0f;
		}
		MathLib.VectorNormalize(ref direction);
		return total;
	}

	void BroadcastToChildren(ParticleCollection particles, in Vector3 direction, float total) {
		for (ParticleCollection? child = particles.Children; child != null; child = child.Next) {
			if (child.GetGroupID() != ChildGroupID)
				continue;
			if ((uint)ChildCP < MAX_PARTICLE_CONTROL_POINTS)
				child.SetControlPoint(ChildCP, direction);
			if ((uint)(ChildCP + 1) < MAX_PARTICLE_CONTROL_POINTS)
				child.SetControlPoint(ChildCP + 1, new Vector3(total, 0, 0));
		}
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		IParticleSystemQuery query = particles.Mgr!.Query();

		if (PerParticle) {
			for (int p = firstParticle; p < firstParticle + numParticles; p++) {
				Vector3 origin = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
				float radius = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_RADIUS, p);
				float total = TraceRepulsion(query, origin, PerParticleTR ? radius : TraceLength, out Vector3 direction);
				Vector3 velocity = ((MaxVelocity - MinVelocity) * total + MinVelocity) * direction * total;
				if (Proportional)
					velocity *= radius;
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, origin + velocity);
				if (Translate)
					particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p) + velocity);
			}
			return;
		}

		float creationTime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, firstParticle);
		Vector3 repulsion;
		if (!InheritFromParent) {
			particles.GetControlPointAtTime(ControlPointNumber, creationTime, out Vector3 origin);
			float total = TraceRepulsion(query, origin, TraceLength, out Vector3 direction);
			direction *= total;
			repulsion = ((MaxVelocity - MinVelocity) * total + MinVelocity) * direction;
			if (ChildCP != -1)
				BroadcastToChildren(particles, direction, total);
		}
		else {
			particles.GetControlPointAtTime(ControlPointNumber, creationTime, out Vector3 direction);
			particles.GetControlPointAtTime(ControlPointNumber + 1, creationTime, out Vector3 amount);
			repulsion = ((MaxVelocity - MinVelocity) * amount.X + MinVelocity) * direction;
		}

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			if (Proportional)
				repulsion *= particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_RADIUS, p);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) + repulsion);
			if (Translate)
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p) + repulsion);
		}
	}
}

public sealed class C_INIT_CreateInHierarchy : ParticleOperatorInstance
{
	public float MaxDistance;
	public float GrowthTime;
	public PathParameters PathParams;
	public int DesiredEndPoint;
	public int DesiredMidPoint;
	public bool UseHighestEndCP;
	public Vector3 DistanceBias;
	public Vector3 DistanceBiasAbs;
	bool HasDistanceBias;
	bool HasDistanceBiasAbs;

	public static readonly ParticleOperatorDefinition<C_INIT_CreateInHierarchy> Definition = new("Position In CP Hierarchy", ParticleOperatorId.PiPosition, false,
		ParticleOperatorUnpack.Begin<C_INIT_CreateInHierarchy>()
			.Float("maximum distance", "0", (o, v) => o.MaxDistance = v)
			.Float("bulge", "0", (o, v) => o.PathParams.Bulge = v)
			.Int("start control point number", "0", (o, v) => o.DesiredMidPoint = v)
			.Int("end control point number", "1", (o, v) => o.DesiredEndPoint = v)
			.Int("bulge control 0=random 1=orientation of start pnt 2=orientation of end point", "0", (o, v) => o.PathParams.BulgeControl = v)
			.Float("mid point position", "0.5", (o, v) => o.PathParams.MidPoint = v)
			.Float("growth time", "0.0", (o, v) => o.GrowthTime = v)
			.Bool("use highest supplied end point", "0", (o, v) => o.UseHighestEndCP = v)
			.Vector3("distance_bias", "1 1 1", (o, v) => o.DistanceBias = v)
			.Vector3("distance_bias_absolute_value", "0 0 0", (o, v) => o.DistanceBiasAbs = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;

	public override ulong GetReadControlPointMask() {
		ulong startMask = (1UL << (DesiredMidPoint & 63)) - 1;
		if (UseHighestEndCP)
			return ~startMask;
		return ~startMask & ((1UL << ((DesiredEndPoint + 1) & 63)) - 1);
	}

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		HasDistanceBias = DistanceBias.X != 1.0f || DistanceBias.Y != 1.0f || DistanceBias.Z != 1.0f;
		HasDistanceBiasAbs = DistanceBiasAbs.X != 0.0f || DistanceBiasAbs.Y != 0.0f || DistanceBiasAbs.Z != 0.0f;
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		int endPoint = UseHighestEndCP ? particles.GetHighestControlPoint() : DesiredEndPoint;

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			float creationTime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p);
			int cp;
			float growth;
			if (GrowthTime < particles.CurTime || endPoint < 1) {
				int low = Math.Min(DesiredMidPoint + 1, endPoint);
				growth = 1.0f;
				cp = (int)((endPoint - low + 1) * particles.RandomFloat(0.0f, 1.0f)) + low;
			}
			else {
				int firstChild = DesiredMidPoint + 1;
				float low = Math.Min(firstChild, endPoint);
				float high;
				if (GrowthTime == 0.0f)
					high = GrowthTime <= creationTime ? endPoint : low;
				else
					high = Math.Clamp(creationTime / GrowthTime, 0.0f, 1.0f) * (endPoint - low) + low;

				float rand = particles.RandomFloat(0.0f, 1.0f);
				float lowest = high <= firstChild ? high : firstChild;
				growth = high / endPoint;
				cp = (int)(((int)high - (int)lowest + 1) * rand) + (int)lowest;
				if (GrowthTime == 0.0f) {
					if (creationTime < GrowthTime)
						growth = 0.0f;
				}
				else
					growth = Math.Clamp(creationTime / GrowthTime, 0.0f, 1.0f) * growth;
			}

			PathParameters path = PathParams;
			path.StartControlPointNumber = particles.ControlPoints[cp].Parent;
			path.EndControlPointNumber = cp;
			particles.CalculatePathValues(in path, creationTime, out Vector3 startPnt, out Vector3 midPnt, out Vector3 endPnt);

			int query = particles.RandomQueryCount;
			particles.RandomQueryCount += 4;
			float t = particles.RandomFloat(query, 0.0f, 1.0f);
			Vector3 randomOffset = new(
				particles.RandomFloat(query + 1, -MaxDistance, MaxDistance),
				particles.RandomFloat(query + 2, -MaxDistance, MaxDistance),
				particles.RandomFloat(query + 3, -MaxDistance, MaxDistance));
			if (HasDistanceBiasAbs)
				randomOffset = ParticleInitUtil.Abs(randomOffset, DistanceBiasAbs);

			Vector3 startMid = (midPnt - startPnt) * t + startPnt;
			Vector3 midEnd = (endPnt * growth - midPnt) * t + midPnt;
			Vector3 position = (midEnd - startMid) * t + startMid + randomOffset * DistanceBias;

			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, position);
			if ((attributeWriteMask & PARTICLE_ATTRIBUTE_PREV_XYZ_MASK) != 0)
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, position);
		}
	}
}

public sealed class C_INIT_InitialVelocityNoise : ParticleOperatorInstance
{
	public Vector3 AbsVal;
	public Vector3 AbsValInv;
	public Vector3 OffsetLoc;
	public float Offset;
	public Vector3 OutputMin;
	public Vector3 OutputMax;
	public float NoiseScale;
	public float NoiseScaleLoc;
	public int ControlPointNumber;
	public bool LocalSpace;

	public static readonly ParticleOperatorDefinition<C_INIT_InitialVelocityNoise> Definition = new("Velocity Noise", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_InitialVelocityNoise>()
			.Int("Control Point Number", "0", (o, v) => o.ControlPointNumber = v)
			.Float("Time Noise Coordinate Scale", "1", (o, v) => o.NoiseScale = v)
			.Float("Spatial Noise Coordinate Scale", "0.01", (o, v) => o.NoiseScaleLoc = v)
			.Float("Time Coordinate Offset", "0", (o, v) => o.Offset = v)
			.Vector3("Spatial Coordinate Offset", "0 0 0", (o, v) => o.OffsetLoc = v)
			.Vector3("Absolute Value", "0 0 0", (o, v) => o.AbsVal = v)
			.Vector3("Invert Abs Value", "0 0 0", (o, v) => o.AbsValInv = v)
			.Vector3("output minimum", "0 0 0", (o, v) => o.OutputMin = v)
			.Vector3("output maximum", "1 1 1", (o, v) => o.OutputMax = v)
			.Bool("Apply Velocity in Local Space (0/1)", "0", (o, v) => o.LocalSpace = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override ulong GetReadControlPointMask() => 1UL << ControlPointNumber;
	public override bool InitMultipleOverride() => true;

	public override void InitParams(ParticleSystemDefinition def, Element element)
		=> ControlPointNumber = Math.Clamp(ControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);

	static float Shape(float noise, bool abs, bool invert) {
		if (abs)
			noise = MathF.Abs(noise);
		return invert ? 1.0f - noise : noise;
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		bool anyInvert = AbsValInv.X != 0.0f || AbsValInv.Y != 0.0f || AbsValInv.Z != 0.0f;
		Vector3 valueScale = new(AbsVal.X != 0.0f ? 1.0f : 0.5f, AbsVal.Y != 0.0f ? 1.0f : 0.5f, AbsVal.Z != 0.0f ? 1.0f : 0.5f);
		Vector3 range = OutputMax - OutputMin;

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			Vector3 position = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
			float creationTime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p);
			float coordTime = (Offset + creationTime) * NoiseScale;
			Vector3 coord = (position + OffsetLoc) * NoiseScaleLoc + new Vector3(coordTime);

			float noiseX = NoiseSIMD.Noise(coord.X, coord.Y, coord.Z);
			float noiseY = NoiseSIMD.Noise(coord.X + 100000.5f, coord.Y + 300000.25f, coord.Z + 9000001.0f);
			float noiseZ = NoiseSIMD.Noise(coord.X + 110000.25f, coord.Y + 310000.75f, coord.Z + 9100000.0f);

			Vector3 noise = new(
				Shape(noiseX, AbsVal.X != 0.0f, anyInvert && AbsValInv.X != 0.0f),
				Shape(noiseY, AbsVal.Y != 0.0f, anyInvert && AbsValInv.Y != 0.0f),
				Shape(noiseZ, AbsVal.Z != 0.0f, anyInvert && AbsValInv.Z != 0.0f));

			Vector3 velocity = (noise * range * valueScale + (Vector3.One - valueScale) * range + OutputMin) * particles.PreviousDt;

			if (LocalSpace) {
				particles.GetControlPointTransformAtTime(ControlPointNumber, creationTime, out Matrix3x4 transform);
				MathLib.VectorRotate(in velocity, in transform, out velocity);
			}

			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p) - velocity);
		}
	}
}

public sealed class C_INIT_InheritVelocity : ParticleOperatorInstance
{
	public int ControlPointNumber;
	public float VelocityScale;

	public static readonly ParticleOperatorDefinition<C_INIT_InheritVelocity> Definition = new("Velocity Inherit from Control Point", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_InheritVelocity>()
			.Int("control point number", "0", (o, v) => o.ControlPointNumber = v)
			.Float("velocity scale", "1", (o, v) => o.VelocityScale = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override ulong GetReadControlPointMask() => 1UL << ControlPointNumber;
	public override bool InitMultipleOverride() => true;

	public override void InitParams(ParticleSystemDefinition def, Element element)
		=> ControlPointNumber = Math.Clamp(ControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		particles.GetControlPointAtPrevTime(ControlPointNumber, out Vector3 prevPosition);
		Vector3 delta = (particles.ControlPoints[ControlPointNumber].Position - prevPosition) * VelocityScale;
		for (int p = firstParticle; p < firstParticle + numParticles; p++)
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p) - delta);
	}
}

public sealed class C_INIT_CreationNoise : ParticleOperatorInstance
{
	public int FieldOutput;
	public bool AbsVal;
	public bool AbsValInv;
	public float Offset;
	public float OutputMin;
	public float OutputMax;
	public float NoiseScale;
	public float NoiseScaleLoc;
	public Vector3 OffsetLoc;
	public float WorldTimeScale;

	public static readonly ParticleOperatorDefinition<C_INIT_CreationNoise> Definition = new("Remap Noise to Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_CreationNoise>()
			.Float("time noise coordinate scale", "0.1", (o, v) => o.NoiseScale = v)
			.Float("spatial noise coordinate scale", "0.001", (o, v) => o.NoiseScaleLoc = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("time coordinate offset", "0", (o, v) => o.Offset = v)
			.Vector3("spatial coordinate offset", "0 0 0", (o, v) => o.OffsetLoc = v)
			.Bool("absolute value", "0", (o, v) => o.AbsVal = v)
			.Bool("invert absolute value", "0", (o, v) => o.AbsValInv = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v)
			.Float("world time noise coordinate scale", "0", (o, v) => o.WorldTimeScale = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;

	public override void InitParams(ParticleSystemDefinition def, Element element)
		=> FieldOutput = Math.Clamp(FieldOutput, 0, 21);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		float valueScale = AbsVal ? 1.0f : 0.5f;
		float outputMin = OutputMin;
		float outputMax = OutputMax;
		if ((ParticleInitUtil.ROTATION_ATTRIBUTES_MASK & (1 << FieldOutput)) != 0) {
			outputMin *= ParticleInitUtil.DEG2RAD;
			outputMax *= ParticleInitUtil.DEG2RAD;
		}
		float range = outputMax - outputMin;
		float coordTime = (particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, firstParticle) + Offset) * NoiseScale;
		float worldTime = (float)(uint)MSTime * WorldTimeScale;

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			Vector3 coord = (particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) + OffsetLoc) * NoiseScaleLoc + new Vector3(worldTime + coordTime);
			float noise = NoiseSIMD.Noise(coord.X, coord.Y, coord.Z);
			if (AbsVal)
				noise = MathF.Abs(noise);
			if (AbsValInv)
				noise = 1.0f - noise;

			float value = noise * range * valueScale + (1.0f - valueScale) * range + outputMin;
			if ((ParticleInitUtil.ALPHA_ATTRIBUTES_MASK & (1 << FieldOutput)) != 0)
				value = Math.Clamp(value, 0.0f, 1.0f);
			particles.GetFloatAttributeRefForWrite(FieldOutput, p) = value;
		}
	}
}

public sealed class C_INIT_AgeNoise : ParticleOperatorInstance
{
	public bool AbsVal;
	public bool AbsValInv;
	public float Offset;
	public float AgeMin;
	public float AgeMax;
	public float NoiseScale;
	public float NoiseScaleLoc;
	public Vector3 OffsetLoc;

	public static readonly ParticleOperatorDefinition<C_INIT_AgeNoise> Definition = new("Lifetime Pre-Age Noise", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_AgeNoise>()
			.Float("time noise coordinate scale", "1.0", (o, v) => o.NoiseScale = v)
			.Float("spatial noise coordinate scale", "1.0", (o, v) => o.NoiseScaleLoc = v)
			.Float("time coordinate offset", "0", (o, v) => o.Offset = v)
			.Vector3("spatial coordinate offset", "0 0 0", (o, v) => o.OffsetLoc = v)
			.Bool("absolute value", "0", (o, v) => o.AbsVal = v)
			.Bool("invert absolute value", "0", (o, v) => o.AbsValInv = v)
			.Float("start age minimum", "0", (o, v) => o.AgeMin = v)
			.Float("start age maximum", "1", (o, v) => o.AgeMax = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK | PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override bool InitMultipleOverride() => true;

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		float valueScale = AbsVal ? 1.0f : 0.5f;
		float range = AgeMax - AgeMin;

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			ref float creationTime = ref particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_CREATION_TIME, p);
			float coordTime = (creationTime + Offset) * NoiseScale;
			Vector3 coord = (particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) + OffsetLoc) * NoiseScaleLoc + new Vector3(coordTime);
			float noise = NoiseSIMD.Noise(coord.X, coord.Y, coord.Z);
			if (AbsVal)
				noise = MathF.Abs(noise);
			if (AbsValInv)
				noise = 1.0f - noise;

			float age = Math.Clamp(range * valueScale * noise + (1.0f - valueScale) * range + AgeMin, 0.0f, 1.0f);
			creationTime -= age * particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_LIFE_DURATION, p);
		}
	}
}

public sealed class C_INIT_RemapScalar : ParticleOperatorInstance
{
	public int FieldInput;
	public int FieldOutput;
	public float InputMin;
	public float InputMax;
	public float OutputMin;
	public float OutputMax;
	public float StartTime;
	public float EndTime;
	public bool ScaleInitialRange;
	public bool ActiveRange;

	public static readonly ParticleOperatorDefinition<C_INIT_RemapScalar> Definition = new("Remap Initial Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RemapScalar>()
			.Float("emitter lifetime start time (seconds)", "-1", (o, v) => o.StartTime = v)
			.Float("emitter lifetime end time (seconds)", "-1", (o, v) => o.EndTime = v)
			.Int("input field", "8", (o, v) => o.FieldInput = v)
			.Float("input minimum", "0", (o, v) => o.InputMin = v)
			.Float("input maximum", "1", (o, v) => o.InputMax = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v)
			.Bool("output is scalar of initial random range", "0", (o, v) => o.ScaleInitialRange = v)
			.Bool("only active within specified input range", "0", (o, v) => o.ActiveRange = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => 1u << FieldInput;
	public override bool InitMultipleOverride() => true;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldInput = Math.Clamp(FieldInput, 0, 21);
		if (FieldOutput < 0 || FieldOutput > 21) {
			FieldOutput = Math.Clamp(FieldOutput, 0, 21);
			return;
		}
		if ((ParticleInitUtil.ALPHA_ATTRIBUTES_MASK & (1 << FieldOutput)) != 0) {
			OutputMin = Math.Clamp(OutputMin, 0.0f, 1.0f);
			OutputMax = Math.Clamp(OutputMax, 0.0f, 1.0f);
		}
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			float creationTime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p);
			float input = ParticleInitUtil.ReadScalar(particles, FieldInput, p);
			bool inTime = (StartTime <= creationTime && creationTime < EndTime) || StartTime == -1.0f || EndTime == -1.0f;
			if (!inTime || (ActiveRange && (input < InputMin || input > InputMax)))
				continue;

			float output = ParticleInitUtil.RemapClamped(input, InputMin, InputMax, OutputMin, OutputMax);
			if (ScaleInitialRange)
				output *= ParticleInitUtil.ReadScalar(particles, FieldOutput, p);
			ParticleInitUtil.WriteScalar(particles, FieldOutput, p, output);
		}
	}
}

public sealed class C_INIT_RemapCPtoScalar : ParticleOperatorInstance
{
	public int CPInput;
	public int FieldOutput;
	public int Field;
	public float InputMin;
	public float InputMax;
	public float OutputMin;
	public float OutputMax;
	public float StartTime;
	public float EndTime;
	public bool ScaleInitialRange;

	public static readonly ParticleOperatorDefinition<C_INIT_RemapCPtoScalar> Definition = new("Remap Control Point to Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RemapCPtoScalar>()
			.Float("emitter lifetime start time (seconds)", "-1", (o, v) => o.StartTime = v)
			.Float("emitter lifetime end time (seconds)", "-1", (o, v) => o.EndTime = v)
			.Int("input control point number", "0", (o, v) => o.CPInput = v)
			.Float("input minimum", "0", (o, v) => o.InputMin = v)
			.Float("input maximum", "1", (o, v) => o.InputMax = v)
			.Int("input field 0-2 X/Y/Z", "0", (o, v) => o.Field = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v)
			.Bool("output is scalar of initial random range", "0", (o, v) => o.ScaleInitialRange = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override ulong GetReadControlPointMask() => 1UL << CPInput;
	public override bool InitMultipleOverride() => true;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = Math.Clamp(FieldOutput, 0, 21);
		CPInput = Math.Clamp(CPInput, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		Field = Math.Clamp(Field, 0, 2);
		if ((ParticleInitUtil.ALPHA_ATTRIBUTES_MASK & (1 << FieldOutput)) != 0) {
			OutputMin = Math.Clamp(OutputMin, 0.0f, 1.0f);
			OutputMax = Math.Clamp(OutputMax, 0.0f, 1.0f);
		}
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		particles.GetControlPointAtTime(CPInput, particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, firstParticle), out Vector3 cpPosition);
		float input = cpPosition[Field];

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			float creationTime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p);
			if (!((StartTime <= creationTime && creationTime < EndTime) || StartTime == -1.0f || EndTime == -1.0f))
				continue;

			float output = ParticleInitUtil.RemapClamped(input, InputMin, InputMax, OutputMin, OutputMax);
			if (ScaleInitialRange)
				output *= ParticleInitUtil.ReadScalar(particles, FieldOutput, p);
			ParticleInitUtil.WriteScalar(particles, FieldOutput, p, output);
		}
	}
}

public sealed class C_INIT_RtEnvCull : ParticleOperatorInstance
{
	public Vector3 TestDir;
	public Vector3 TestNormal;
	public bool UseVelocity;
	public bool CullOnMiss;
	public bool LifeAdjust;
	public string RtEnvName = "PRECIPITATION";
	int RtEnv;

	public static readonly ParticleOperatorDefinition<C_INIT_RtEnvCull> Definition = new("Cull relative to Ray Trace Environment", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RtEnvCull>()
			.Bool("cull on miss", "0", (o, v) => o.CullOnMiss = v)
			.Bool("velocity test adjust lifespan", "0", (o, v) => o.LifeAdjust = v)
			.Bool("use velocity for test direction", "0", (o, v) => o.UseVelocity = v)
			.Vector3("test direction", "0 0 1", (o, v) => o.TestDir = v)
			.Vector3("cull normal", "0 0 0", (o, v) => o.TestNormal = v)
			.String("ray trace environment name", "PRECIPITATION", 128, (o, v) => o.RtEnvName = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override bool InitMultipleOverride() => true;

	public override void InitParams(ParticleSystemDefinition def, Element element)
		=> RtEnv = def.Mgr.Query().GetRayTraceEnvironmentFromName(RtEnvName);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		bool hasNormal = TestNormal != Vector3.Zero;
		IParticleSystemQuery query = particles.Mgr!.Query();

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			ref float lifetime = ref particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_LIFE_DURATION, p);
			if (lifetime == -1.0f)
				continue;

			Vector3 position = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
			if (!UseVelocity) {
				query.TraceAgainstRayTraceEnv(RtEnv, position, TestDir, 0.0f, float.MaxValue, out ParticleRayTraceResult result);
				bool missOrWrongNormal = result.HitId == -1 || (hasNormal && result.SurfaceNormal != TestNormal);
				bool cull = missOrWrongNormal ? CullOnMiss : (!CullOnMiss || hasNormal);
				if (cull)
					lifetime = -1.0f;
				continue;
			}

			Vector3 delta = position - particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
			float length = delta.Length();
			Vector3 direction = delta * (1.0f / length);
			float speed = length / particles.PreviousDt;
			query.TraceAgainstRayTraceEnv(RtEnv, position, direction, 0.0f, lifetime * speed, out ParticleRayTraceResult velocityResult);

			if (velocityResult.HitId == -1 || (hasNormal && velocityResult.SurfaceNormal != TestNormal)) {
				if (CullOnMiss)
					lifetime = -1.0f;
			}
			else if (LifeAdjust)
				lifetime = Math.Min(lifetime, velocityResult.HitDistance / (speed + 1.1920929e-07f));
			else if (!CullOnMiss)
				lifetime = -1.0f;
		}
	}
}
