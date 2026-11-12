using Datamodel;

namespace Source.Particles;

public interface IParticleOperatorDefinition
{
	string? GetName();
	ParticleOperatorInstance? CreateInstance(Guid id);
	IDmxElementUnpackStructure? GetUnpackStructure();
	ParticleOperatorId GetId();
	bool IsObsolete();
}

public sealed class ParticleOperatorDefinition<T>(string factoryName, ParticleOperatorId id, bool isObsolete, IDmxElementUnpackStructure? unpackParams) : IParticleOperatorDefinition
	where T : ParticleOperatorInstance, new()
{
	public string GetName() => factoryName;
	public ParticleOperatorId GetId() => id;
	public IDmxElementUnpackStructure? GetUnpackStructure() => unpackParams;
	public bool IsObsolete() => isObsolete;

	public ParticleOperatorInstance? CreateInstance(Guid instanceId) {
		T op = new();
		op.SetDefinition(this, instanceId);
		return op;
	}
}

public struct ParticleVisibilityInputs
{
	public float CameraBias;
	public float InputMin;
	public float InputMax;
	public float AlphaScaleMin;
	public float AlphaScaleMax;
	public float RadiusScaleMin;
	public float RadiusScaleMax;
	public float ProxyRadius;
	public float BBoxScale;
	public bool UseBBox;
	public int CPin;
}

public abstract class ParticleOperatorInstance
{
	public float OpStartFadeInTime;
	public float OpEndFadeInTime;
	public float OpStartFadeOutTime;
	public float OpEndFadeOutTime;
	public float OpFadeOscillatePeriod;

	IParticleOperatorDefinition? Def;
	Guid Id;

	internal void SetDefinition(IParticleOperatorDefinition def, Guid id) {
		Def = def;
		Id = id;
	}

	public IParticleOperatorDefinition? GetDefinition() => Def;
	public Guid GetId() => Id;

	public virtual void InitParams(ParticleSystemDefinition def, Element element) { }
	public virtual int GetRequiredContextBytes() => 0;
	public virtual void InitializeContextData(ParticleCollection particles, Span<byte> context) { }

	public abstract uint GetWrittenAttributes();
	public abstract uint GetReadAttributes();
	public virtual ulong GetReadControlPointMask() => 0;
	public virtual uint GetReadInitialAttributes() => 0;

	public virtual void Operate(ParticleCollection particles, float opStrength, Span<byte> context) { }

	public virtual uint Emit(ParticleCollection particles, float opCurStrength, Span<byte> context) => 0;
	public virtual void StopEmission(ParticleCollection particles, Span<byte> context, bool infiniteOnly = false) { }
	public virtual void StartEmission(ParticleCollection particles, Span<byte> context, bool infiniteOnly = false) { }
	public virtual void Restart(ParticleCollection particles, Span<byte> context) { }
	public virtual bool MayCreateMoreParticles(ParticleCollection particles, Span<byte> context) => false;
	public virtual void SkipToTime(float time, ParticleCollection particles, Span<byte> context) { }

	public virtual void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) { }
	public virtual void InitNewParticlesBlock(ParticleCollection particles, int startBlock, int numBlocks, int attributeWriteMask, Span<byte> context)
		=> InitNewParticlesScalar(particles, 4 * startBlock, 4 * numBlocks, attributeWriteMask, context);

	public void InitNewParticles(ParticleCollection particles, int firstParticle, int particleCount, int attributeWriteMask, Span<byte> context) {
		if (particleCount == 0)
			return;

		if (particleCount < 16) {
			InitNewParticlesScalar(particles, firstParticle, particleCount, attributeWriteMask, context);
			return;
		}

		if ((firstParticle & 3) != 0) {
			int head = 4 - (firstParticle & 3);
			InitNewParticlesScalar(particles, firstParticle, head, attributeWriteMask, context);
			particleCount -= head;
			firstParticle += head;
		}

		InitNewParticlesBlock(particles, firstParticle / 4, particleCount / 4, attributeWriteMask, context);

		if ((particleCount & 3) != 0)
			InitNewParticlesScalar(particles, firstParticle + (particleCount & ~3), particleCount & 3, attributeWriteMask, context);
	}

	public virtual bool IsBatchable() => true;
	public virtual bool IsFinalConstraint() => false;
	public virtual bool InitMultipleOverride() => false;
	public virtual bool IsScrubSafe() => false;
	public virtual bool ShouldRunBeforeEmitters() => false;
	public virtual bool RequiresOrderInvariance() => false;
}

public abstract class ParticleRenderOperatorInstance : ParticleOperatorInstance
{
	public ParticleVisibilityInputs VisibilityInputs;
}

public static class ParticleOperatorUnpack
{
	public static DmxElementUnpackStructure<T> Begin<T>() where T : ParticleOperatorInstance
		=> new DmxElementUnpackStructure<T>()
			.Float("operator start fadein", "0", (o, v) => o.OpStartFadeInTime = v)
			.Float("operator end fadein", "0", (o, v) => o.OpEndFadeInTime = v)
			.Float("operator start fadeout", "0", (o, v) => o.OpStartFadeOutTime = v)
			.Float("operator end fadeout", "0", (o, v) => o.OpEndFadeOutTime = v)
			.Float("operator fade oscillate", "0", (o, v) => o.OpFadeOscillatePeriod = v);

	public static DmxElementUnpackStructure<T> BeginRender<T>() where T : ParticleRenderOperatorInstance
		=> Begin<T>()
			.Int("Visibility Proxy Input Control Point Number", "-1", (o, v) => o.VisibilityInputs.CPin = v)
			.Float("Visibility Proxy Radius", "1.0", (o, v) => o.VisibilityInputs.ProxyRadius = v)
			.Float("Visibility input minimum", "0", (o, v) => o.VisibilityInputs.InputMin = v)
			.Float("Visibility input maximum", "1", (o, v) => o.VisibilityInputs.InputMax = v)
			.Float("Visibility Alpha Scale minimum", "0", (o, v) => o.VisibilityInputs.AlphaScaleMin = v)
			.Float("Visibility Alpha Scale maximum", "1", (o, v) => o.VisibilityInputs.AlphaScaleMax = v)
			.Float("Visibility Radius Scale minimum", "1", (o, v) => o.VisibilityInputs.RadiusScaleMin = v)
			.Float("Visibility Radius Scale maximum", "1", (o, v) => o.VisibilityInputs.RadiusScaleMax = v)
			.Float("Visibility Camera Depth Bias", "0", (o, v) => o.VisibilityInputs.CameraBias = v);
}

public sealed class ChildOperatorDefinition : IParticleOperatorDefinition
{
	public static readonly ChildOperatorDefinition Instance = new();

	public static readonly DmxElementUnpackStructure<ParticleChildrenInfo> ChildrenInfoUnpack = new DmxElementUnpackStructure<ParticleChildrenInfo>()
		.Float("delay", "0.0", (c, v) => c.Delay = v);

	public string? GetName() => null;
	public ParticleOperatorInstance? CreateInstance(Guid id) => null;
	public IDmxElementUnpackStructure? GetUnpackStructure() => ChildrenInfoUnpack;
	public ParticleOperatorId GetId() => ParticleOperatorId.Generic;
	public bool IsObsolete() => false;
}
