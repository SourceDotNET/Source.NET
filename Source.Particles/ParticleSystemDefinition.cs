using Datamodel;

using Source.Common.MaterialSystem;

using System.Numerics;

using static Source.MaterialDefines;
using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public class ParticleChildrenInfo
{
	public Guid Id;
	public string? Name;
	public bool UseNameBasedLookup;
	public float Delay;
}

public sealed class ParticleSystemDefinition(ParticleSystemMgr mgr)
{
	public const string DmeParticleSystemDefinition = "DmeParticleSystemDefinition";

	static readonly string[] RemapOperatorNameTable = [
		"alpha_fade", "Alpha Fade and Decay",
		"alpha_fade_in_random", "Alpha Fade In Random",
		"alpha_fade_out_random", "Alpha Fade Out Random",
		"basic_movement", "Movement Basic",
		"color_fade", "Color Fade",
		"controlpoint_light", "Color Light From Control Point",
		"Dampen Movement Relative to Control Point", "Movement Dampen Relative to Control Point",
		"Distance Between Control Points Scale", "Remap Distance Between Two Control Points to Scalar",
		"Distance to Control Points Scale", "Remap Distance to Control Point to Scalar",
		"lifespan_decay", "Lifespan Decay",
		"lock to bone", "Movement Lock to Bone",
		"postion_lock_to_controlpoint", "Movement Lock to Control Point",
		"maintain position along path", "Movement Maintain Position Along Path",
		"lock to saved position along path", "Movement Lock to Saved Position Along Path",
		"Match Particle Velocities", "Movement Match Particle Velocities",
		"Max Velocity", "Movement Max Velocity",
		"noise", "Noise Scalar",
		"vector noise", "Noise Vector",
		"oscillate_scalar", "Oscillate Scalar",
		"oscillate_vector", "Oscillate Vector",
		"Orient Rotation to 2D Direction", "Rotation Orient to 2D Direction",
		"radius_scale", "Radius Scale",
		"Random Cull", "Cull Random",
		"remap_scalar", "Remap Scalar",
		"rotation_movement", "Rotation Basic",
		"rotation_spin", "Rotation Spin Roll",
		"rotation_spin yaw", "Rotation Spin Yaw",
		"alpha_random", "Alpha Random",
		"color_random", "Color Random",
		"create from parent particles", "Position From Parent Particles",
		"Create In Hierarchy", "Position In CP Hierarchy",
		"random position along path", "Position Along Path Random",
		"random position on model", "Position on Model Random",
		"sequential position along path", "Position Along Path Sequential",
		"position_offset_random", "Position Modify Offset Random",
		"position_warp_random", "Position Modify Warp Random",
		"position_within_box", "Position Within Box Random",
		"position_within_sphere", "Position Within Sphere Random",
		"Inherit Velocity", "Velocity Inherit from Control Point",
		"Initial Repulsion Velocity", "Velocity Repulse from World",
		"Initial Velocity Noise", "Velocity Noise",
		"Initial Scalar Noise", "Remap Noise to Scalar",
		"Lifespan from distance to world", "Lifetime from Time to Impact",
		"Pre-Age Noise", "Lifetime Pre-Age Noise",
		"lifetime_random", "Lifetime Random",
		"radius_random", "Radius Random",
		"random yaw", "Rotation Yaw Random",
		"Randomly Flip Yaw", "Rotation Yaw Flip Random",
		"rotation_random", "Rotation Random",
		"rotation_speed_random", "Rotation Speed Random",
		"sequence_random", "Sequence Random",
		"second_sequence_random", "Sequence Two Random",
		"trail_length_random", "Trail Length Random",
		"velocity_random", "Velocity Random",
	];

	static readonly DmxElementUnpackStructure<ParticleSystemDefinition> UnpackStructure = new DmxElementUnpackStructure<ParticleSystemDefinition>()
		.Int("max_particles", "1000", (d, v) => d.MaxParticles = v)
		.Int("initial_particles", "0", (d, v) => d.InitialParticles = v)
		.String("material", "vgui/white", MAX_PATH, (d, v) => d.MaterialName = v)
		.Vector3("bounding_box_min", "-10 -10 -10", (d, v) => d.BoundingBoxMin = v)
		.Vector3("bounding_box_max", "10 10 10", (d, v) => d.BoundingBoxMax = v)
		.Float("cull_radius", "0", (d, v) => d.CullRadius = v)
		.Float("cull_cost", "1", (d, v) => d.CullFillCost = v)
		.Int("cull_control_point", "0", (d, v) => d.CullControlPoint = v)
		.String("cull_replacement_definition", "", 128, (d, v) => d.CullReplacementName = v)
		.Float("radius", "5", (d, v) => d.ConstantRadius = v)
		.Color("color", "255 255 255 255", (d, v) => d.ConstantColor = v)
		.Float("rotation", "0", (d, v) => d.ConstantRotation = v)
		.Float("rotation_speed", "0", (d, v) => d.ConstantRotationSpeed = v)
		.Int("sequence_number", "0", (d, v) => d.ConstantSequenceNumber = v)
		.Int("sequence_number 1", "0", (d, v) => d.ConstantSequenceNumber1 = v)
		.Int("group id", "0", (d, v) => d.GroupID = v)
		.Float("maximum time step", "0.1", (d, v) => d.MaximumTimeStep = v)
		.Float("maximum sim tick rate", "0.0", (d, v) => d.MaximumSimTime = v)
		.Float("minimum sim tick rate", "0.0", (d, v) => d.MinimumSimTime = v)
		.Int("minimum rendered frames", "0", (d, v) => d.MinimumFrames = v)
		.Int("control point to disable rendering if it is the camera", "-1", (d, v) => d.SkipRenderControlPoint = v)
		.Float("maximum draw distance", "100000.0", (d, v) => d.MaxDrawDistance = v)
		.Float("time to sleep when not drawn", "8", (d, v) => d.NoDrawTimeToGoToSleep = v)
		.Bool("Sort particles", "1", (d, v) => d.ShouldSort = v)
		.Bool("batch particle systems", "0", (d, v) => d.ShouldBatch = v)
		.Bool("view model effect", "0", (d, v) => d.ViewModelEffect = v)
		.Bool("screen space effect", "0", (d, v) => d.ScreenSpaceEffect = v);

	public static IDmxElementUnpackStructure GetUnpackStructure() => UnpackStructure;

	internal readonly ParticleSystemMgr Mgr = mgr;

	public int InitialParticles;
	public int PerParticleUpdatedAttributeMask;
	public int PerParticleInitializedAttributeMask;
	public int InitialAttributeReadMask;
	public int AttributeReadMask;
	public ulong ControlPointReadMask;
	public Vector3 BoundingBoxMin;
	public Vector3 BoundingBoxMax;
	public string MaterialName = "";
	public readonly MaterialReference Material = new();
	public ParticleCollection? FirstCollection;
	public string CullReplacementName = "";
	public float CullRadius;
	public float CullFillCost = 1.0f;
	public int CullControlPoint;
	public int RetireCheckFrame;

	public Color ConstantColor = new(0, 0, 0, 0);
	public float ConstantRadius;
	public float ConstantRotation;
	public float ConstantRotationSpeed;
	public int ConstantSequenceNumber;
	public int ConstantSequenceNumber1;
	public int GroupID;
	public float MaximumTimeStep;
	public float MaximumSimTime;
	public float MinimumSimTime;
	public int MinimumFrames;
	public bool ViewModelEffect;
	public bool ScreenSpaceEffect;

	public int ContextDataSize;
	public Guid Id;

	public float MaxDrawDistance;
	public float NoDrawTimeToGoToSleep;
	public int MaxParticles;
	public int SkipRenderControlPoint;
	public string Name = "";

	public readonly List<ParticleOperatorInstance> Operators = [];
	public readonly List<ParticleOperatorInstance> Renderers = [];
	public readonly List<ParticleOperatorInstance> Initializers = [];
	public readonly List<ParticleOperatorInstance> Emitters = [];
	public readonly List<ParticleOperatorInstance> ForceGenerators = [];
	public readonly List<ParticleOperatorInstance> Constraints = [];
	public readonly List<ParticleChildrenInfo> Children = [];

	public readonly List<int> OperatorsCtxOffsets = [];
	public readonly List<int> RenderersCtxOffsets = [];
	public readonly List<int> InitializersCtxOffsets = [];
	public readonly List<int> EmittersCtxOffsets = [];
	public readonly List<int> ForceGeneratorsCtxOffsets = [];
	public readonly List<int> ConstraintsCtxOffsets = [];

	public float TotalSimTime;
	public float UncomittedTotalSimTime;
	public float MaxMeasuredSimTime;
	public int MaximumActiveParticles;
	public bool ShouldSort = true;
	public bool ShouldBatch;
	public bool IsPrecached;
	public bool AlwaysPrecache;

	public string GetName() => Name;
	public Guid GetId() => Id;
	public IMaterial? GetMaterial() => Material.Get();
	public bool ShouldAlwaysPrecache() => AlwaysPrecache;
	public bool IsViewModelEffect() => ViewModelEffect;
	public bool IsScreenSpaceEffect() => ScreenSpaceEffect;
	public float GetCullRadius() => CullRadius;
	public float GetCullFillCost() => CullFillCost;
	public int GetCullControlPoint() => CullControlPoint;
	public string GetCullReplacementDefinition() => CullReplacementName;
	public bool HasRetirementBeenChecked(int frame) => RetireCheckFrame == frame;
	public void MarkRetirementCheck(int frame) => RetireCheckFrame = frame;
	public void MarkReadsControlPoint(int point) => ControlPointReadMask |= 1UL << point;
	public bool ReadsControlPoint(int point) => (ControlPointReadMask & (1UL << point)) != 0;

	public void Read(Element element) {
		Name = element.Name;
		Id = element.ID;
		UnpackStructure.UnpackIntoStructure(element, this);

		if (InitialParticles < 0)
			InitialParticles = 0;

		MaxParticles = Math.Min(Math.Max(MaxParticles, 1) * ParticleSystemMgr.g_nParticle_Multiplier, MAX_PARTICLES_IN_A_SYSTEM);

		if (CullRadius > 0)
			ControlPointReadMask |= 1UL << (CullControlPoint & 0x3F);

		ParseOperators("renderers", ParticleFunctionType.Renderer, element, Renderers);
		ParseOperators("operators", ParticleFunctionType.Operator, element, Operators);
		ParseOperators("initializers", ParticleFunctionType.Initializer, element, Initializers);
		ParseOperators("emitters", ParticleFunctionType.Emitter, element, Emitters);
		ParseChildren(element);
		ParseOperators("forces", ParticleFunctionType.ForceGenerator, element, ForceGenerators);
		ParseOperators("constraints", ParticleFunctionType.Constraint, element, Constraints);
		SetupContextData();
	}

	static string RemapOperatorName(string name) {
		for (int i = 0; i < RemapOperatorNameTable.Length; i += 2) {
			if (string.Equals(name, RemapOperatorNameTable[i], StringComparison.OrdinalIgnoreCase))
				return RemapOperatorNameTable[i + 1];
		}
		return name;
	}

	void ParseOperators(string opKey, ParticleFunctionType functionType, Element element, List<ParticleOperatorInstance> outList) {
		if (!element.TryGetValue(opKey, out object? attribute) || attribute is not ElementArray ops)
			return;

		List<IParticleOperatorDefinition> flist = Mgr.GetAvailableParticleOperatorList(functionType);
		foreach (Element? op in ops) {
			if (op == null)
				continue;

			string functionName = op.TryGetValue("functionName", out object? fn) && fn is string s ? s : "";
			functionName = RemapOperatorName(functionName);

			IParticleOperatorDefinition? opDef = null;
			foreach (IParticleOperatorDefinition candidate in flist) {
				if (string.Equals(functionName, candidate.GetName(), StringComparison.OrdinalIgnoreCase)) {
					opDef = candidate;
					break;
				}
			}

			if (opDef == null) {
				if (flist.Count != 0)
					Warning($"Didn't find particle function {functionName}\n");
				continue;
			}

			ParticleOperatorInstance op2 = opDef.CreateInstance(op.ID)!;
			opDef.GetUnpackStructure()?.UnpackIntoStructure(op, op2);
			op2.InitParams(this, element);
			op2.ComputeStrengthFastPath();

			AttributeReadMask |= (int)op2.GetReadAttributes();
			ControlPointReadMask |= op2.GetReadControlPointMask();

			switch (functionType) {
				case ParticleFunctionType.Renderer:
				case ParticleFunctionType.Operator:
				case ParticleFunctionType.ForceGenerator:
				case ParticleFunctionType.Constraint:
					PerParticleUpdatedAttributeMask |= (int)op2.GetWrittenAttributes();
					InitialAttributeReadMask |= (int)op2.GetReadInitialAttributes();
					break;
				case ParticleFunctionType.Initializer:
				case ParticleFunctionType.Emitter:
					PerParticleInitializedAttributeMask |= (int)op2.GetWrittenAttributes();
					break;
			}

			if (((op2.GetReadAttributes() | op2.GetReadInitialAttributes()) & PARTICLE_ATTRIBUTE_PARTICLE_ID_MASK) != 0) {
				PerParticleInitializedAttributeMask |= PARTICLE_ATTRIBUTE_PARTICLE_ID_MASK;
				InitialAttributeReadMask |= PARTICLE_ATTRIBUTE_PARTICLE_ID_MASK;
			}

			outList.Add(op2);
		}
	}

	void ParseChildren(Element element) {
		if (!element.TryGetValue("children", out object? attribute) || attribute is not ElementArray children)
			return;

		foreach (Element? childRef in children) {
			if (childRef == null)
				continue;
			if (!childRef.TryGetValue("child", out object? childValue) || childValue is not Element particleSystem)
				continue;
			if (!string.Equals(particleSystem.ClassName, DmeParticleSystemDefinition, StringComparison.OrdinalIgnoreCase))
				continue;

			ParticleChildrenInfo info = new();
			Children.Add(info);
			ChildOperatorDefinition.ChildrenInfoUnpack.UnpackIntoStructure(childRef, info);
			info.UseNameBasedLookup = !(particleSystem.TryGetValue("preventNameBasedLookup", out object? prevent) && prevent is true);
			if (info.UseNameBasedLookup)
				info.Name = particleSystem.Name;
			else
				info.Id = particleSystem.ID;

			Mgr.Dictionary!.AddParticleSystem(particleSystem);
		}
	}

	void SetupContextData() {
		ContextDataSize = 0;
		ReadOnlySpan<List<ParticleOperatorInstance>> olists = [Operators, Renderers, Initializers, Emitters, ForceGenerators, Constraints];
		ReadOnlySpan<List<int>> offsetLists = [OperatorsCtxOffsets, RenderersCtxOffsets, InitializersCtxOffsets, EmittersCtxOffsets, ForceGeneratorsCtxOffsets, ConstraintsCtxOffsets];
		for (int i = 0; i < olists.Length; i++) {
			foreach (ParticleOperatorInstance op in olists[i]) {
				offsetLists[i].Add(ContextDataSize);
				ContextDataSize = (ContextDataSize + op.GetRequiredContextBytes() + 15) & ~15;
			}
		}
	}

	ParticleSystemDefinition? FindChild(ParticleChildrenInfo child)
		=> child.UseNameBasedLookup ? Mgr.Dictionary!.FindParticleSystem(child.Name) : Mgr.Dictionary!.FindParticleSystem(child.Id);

	public void Precache() {
		if (IsPrecached)
			return;

		IsPrecached = true;
		Material.Init(MaterialName, TEXTURE_GROUP_OTHER, true);

		foreach (ParticleOperatorInstance renderer in Renderers)
			renderer.Precache(this);

		foreach (ParticleChildrenInfo child in Children) {
			ParticleSystemDefinition? childDef = FindChild(child);
			if (childDef != null && !childDef.IsPrecached)
				childDef.Precache();
		}
	}

	public void Uncache() {
		if (!IsPrecached)
			return;

		IsPrecached = false;
		Material.Shutdown();

		foreach (ParticleChildrenInfo child in Children) {
			ParticleSystemDefinition? childDef = FindChild(child);
			if (childDef != null && childDef.IsPrecached)
				childDef.Uncache();
		}
	}
}
