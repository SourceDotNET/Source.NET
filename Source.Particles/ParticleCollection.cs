using Source.Common;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public struct ParticleControlPoint
{
	public Vector3 Position;
	public Vector3 PrevPosition;
	public Vector3 ForwardVector;
	public Vector3 UpVector;
	public Vector3 RightVector;
	public Vector3 Velocity;
	public float Radius;
	public float Density;
	public float Duration;
	public object? Object;
	public int Parent;
}

public struct PathParameters
{
	public int StartControlPointNumber;
	public int EndControlPointNumber;
	public int BulgeControl;
	public float Bulge;
	public float MidPoint;

	public void ClampControlPointIndices() {
		StartControlPointNumber = Math.Max(0, Math.Min(MAX_PARTICLE_CONTROL_POINTS - 1, StartControlPointNumber));
		EndControlPointNumber = Math.Max(0, Math.Min(MAX_PARTICLE_CONTROL_POINTS - 1, EndControlPointNumber));
	}
}

public class ParticleCollection
{
	public const int NUM_COLLISION_CACHE_MODES = 4;
	const int CONSTANT_FLOATS = MAX_PARTICLE_ATTRIBUTES * 12;

	static readonly float[] RandomFloats = Source.Particles.RandomFloats.Table;

	public static ReadOnlySpan<float> RandomFloatTable => RandomFloats;

	public int PaddedActiveParticles;
	public float CurTime;
	public int ActiveParticles;
	public float Dt;
	public float PreviousDt = 0.05f;
	public double NextSleepTime;

	public ParticleSystemMgr? Mgr;
	public ParticleSystemDefinition? Def;
	public int AllocatedParticles;
	public int MaxAllowedParticles;
	public bool Dormant;
	public bool EmissionStopped;
	public bool RequiresOrderInvariance;

	public int LocalLightingCP = -1;
	public Color LocalLighting = new(255, 255, 255, 255);

	public readonly ParticleControlPoint[] ControlPoints = new ParticleControlPoint[MAX_PARTICLE_CONTROL_POINTS];
	public readonly ModelHitBoxesInfo[] ControlPointHitBoxes = new ModelHitBoxesInfo[MAX_PARTICLE_CONTROL_POINTS];

	public byte[]? OperatorContextData;
	public ParticleCollection? Next;
	public ParticleCollection? Prev;

	public readonly object?[] CollisionCacheData = new object?[NUM_COLLISION_CACHE_MODES];
	public ParticleCollection? Parent;
	public ParticleCollection? Children;

	protected bool BoundsValid;
	protected Vector3 MinBounds;
	protected Vector3 MaxBounds;
	protected int HighestCP;
	protected int TargetCP;

	float[] Memory = [];
	readonly int[] AttributeOffsets = new int[MAX_PARTICLE_ATTRIBUTES];
	readonly int[] InitialAttributeOffsets = new int[MAX_PARTICLE_ATTRIBUTES];
	readonly int[] ParticleFloatStrides = new int[MAX_PARTICLE_ATTRIBUTES];
	readonly int[] ParticleInitialFloatStrides = new int[MAX_PARTICLE_ATTRIBUTES];

	int PerParticleInitializedAttributeMask;
	int PerParticleUpdatedAttributeMask;
	int PerParticleReadInitialAttributeMask;

	ulong ControlPointReadMask;
	int ParticleFlags = PCFLAGS_FIRST_FRAME;
	bool IsScrubbable;
	bool IsRunningInitializers;
	bool IsRunningOperators;
	bool IsTranslucent_;
	bool IsTwoPass_;
	bool AnyUsesPowerOfTwoFrameBufferTexture;
	bool AnyUsesFullFrameBufferTexture;
	bool IsBatchable_;
	bool UsesPowerOfTwoFrameBufferTexture_;
	bool UsesFullFrameBufferTexture_;

	int DrawnFrames;
	int SimulatedFrames;

	Vector3 Center;

	int UniqueParticleId;

	int RandomQueryCount;
	int RandomSeed;
	int OperatorRandomSampleOffset_;

	internal int NumParticlesToKill;
	internal int[]? ParticleKillList;

	ParticleCollection? NextDef;
	ParticleCollection? PrevDef;

	public ParticleCollection() {
		for (int i = 0; i < MAX_PARTICLE_CONTROL_POINTS; i++) {
			ref ParticleControlPoint cp = ref ControlPoints[i];
			cp.ForwardVector = new(0, 1, 0);
			cp.UpVector = new(0, 0, 1);
			cp.RightVector = new(1, 0, 0);
			cp.Density = 1.0f;
			ControlPointHitBoxes[i] = new();
		}
		Array.Fill(InitialAttributeOffsets, -1);
	}

	public Vector128<float> CurTime4 => Vector128.Create(CurTime);

	public bool Init(ParticleSystemMgr mgr, string? particleSystemName) {
		if (particleSystemName == null)
			return false;

		ParticleSystemDefinition? def = mgr.FindParticleSystem(particleSystemName);
		if (def == null) {
			Warning($"Attempted to create unknown particle system type \"{particleSystemName}\"!\n");
			return false;
		}
		return Init(mgr, def);
	}

	public bool Init(ParticleSystemMgr mgr, ParticleSystemDefinition? def) {
		if (def == null) {
			Warning("Particlelib: Missing precache for particle system type \"unknown\"!\n");
			def = mgr.FindParticleSystem("error");
			if (def == null)
				return false;
		}
		Init(def, 0.0f, 0);
		return IsValid();
	}

	internal void Init(ParticleSystemDefinition def, float delay, int randomSeed) {
		Def = def;
		Mgr = def.Mgr;
		LinkIntoDefList();
		InitStorage(def);

		IsScrubbable = randomSeed != 0;
		if (randomSeed == 0)
			RandomSeed = RuntimeHelpers.GetHashCode(this) + (int)(uint)MSTime;
		else
			RandomSeed = randomSeed;

		SetAttributeToConstant(PARTICLE_ATTRIBUTE_XYZ, 0.0f, 0.0f, 0.0f);
		SetAttributeToConstant(PARTICLE_ATTRIBUTE_PREV_XYZ, 0.0f, 0.0f, 0.0f);
		SetAttributeToConstant(PARTICLE_ATTRIBUTE_LIFE_DURATION, 1.0f);
		SetAttributeToConstant(PARTICLE_ATTRIBUTE_RADIUS, def.ConstantRadius);
		SetAttributeToConstant(PARTICLE_ATTRIBUTE_ROTATION, def.ConstantRotation);
		SetAttributeToConstant(PARTICLE_ATTRIBUTE_ROTATION_SPEED, def.ConstantRotationSpeed);
		SetAttributeToConstant(PARTICLE_ATTRIBUTE_TINT_RGB, def.ConstantColor.R * 0.003921569f, def.ConstantColor.G * 0.003921569f, def.ConstantColor.G * 0.003921569f);
		SetAttributeToConstant(PARTICLE_ATTRIBUTE_ALPHA, def.ConstantColor.A * 0.003921569f);
		SetAttributeToConstant(PARTICLE_ATTRIBUTE_CREATION_TIME, 0.0f);
		SetAttributeToConstant(PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER, def.ConstantSequenceNumber);
		SetAttributeToConstant(PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER1, def.ConstantSequenceNumber1);
		SetAttributeToConstant(PARTICLE_ATTRIBUTE_TRAIL_LENGTH, 0.1f);
		SetAttributeToConstant(PARTICLE_ATTRIBUTE_PARTICLE_ID, 0);
		SetAttributeToConstant(PARTICLE_ATTRIBUTE_YAW, 0);
		SetAttributeToConstant(PARTICLE_ATTRIBUTE_ALPHA2, 1.0f);

		CurTime = -delay;

		if (def.ContextDataSize != 0)
			OperatorContextData = new byte[def.ContextDataSize];

		NextSleepTime = Mgr.GetLastSimulationTime() + def.NoDrawTimeToGoToSleep;

		ReadOnlySpan<List<ParticleOperatorInstance>> olists = [def.Operators, def.Renderers, def.Initializers, def.Emitters, def.ForceGenerators, def.Constraints];
		ReadOnlySpan<List<int>> offsetLists = [def.OperatorsCtxOffsets, def.RenderersCtxOffsets, def.InitializersCtxOffsets, def.EmittersCtxOffsets, def.ForceGeneratorsCtxOffsets, def.ConstraintsCtxOffsets];
		for (int i = 0; i < olists.Length; i++) {
			for (int j = 0; j < olists[i].Count; j++)
				olists[i][j].InitializeContextData(this, ContextSpan(offsetLists[i][j]));
		}

		ControlPointReadMask = def.ControlPointReadMask;

		foreach (ParticleChildrenInfo child in def.Children) {
			if (randomSeed != 0)
				randomSeed += 129;

			float childDelay = child.Delay - CurTime;
			ParticleCollection? childCollection = child.UseNameBasedLookup
				? Mgr.CreateParticleCollection(child.Name, childDelay, randomSeed)
				: Mgr.CreateParticleCollection(child.Id, childDelay, randomSeed);

			if (childCollection == null)
				continue;

			childCollection.Parent = this;
			childCollection.Next = null;
			if (Children == null)
				Children = childCollection;
			else {
				ParticleCollection tail = Children;
				while (tail.Next != null)
					tail = tail.Next;
				tail.Next = childCollection;
				childCollection.Prev = tail;
			}
			ControlPointReadMask |= childCollection.ControlPointReadMask;
		}

		if (Def != null && Def.IsPrecached && Def.Material.Get() != null) {
			IsTranslucent_ = ComputeIsTranslucent();
			IsTwoPass_ = ComputeIsTranslucent() && ContainsOpaqueCollections();
			IsBatchable_ = ComputeIsBatchable();
			LabelTextureUsage();
			AnyUsesPowerOfTwoFrameBufferTexture = ComputeUsesPowerOfTwoFrameBufferTexture();
			AnyUsesFullFrameBufferTexture = ComputeUsesFullFrameBufferTexture();
			RequiresOrderInvariance = ComputeRequiresOrderInvariance();
		}
	}

	public void Destroy() {
		UnlinkFromDefList();

		ParticleCollection? child = Children;
		while (child != null) {
			ParticleCollection? next = child.Next;
			child.Destroy();
			Children = next;
			child = next;
		}

		Memory = [];
		OperatorContextData = null;
		Array.Clear(CollisionCacheData);
		foreach (ModelHitBoxesInfo info in ControlPointHitBoxes) {
			info.HitBoxes = null;
			info.PrevBoxes = null;
		}
		Def = null;
	}

	void InitStorage(ParticleSystemDefinition def) {
		int maxParticles = def.MaxParticles;
		int allocated;
		if (maxParticles < MAX_PARTICLES_IN_A_SYSTEM)
			allocated = ((maxParticles + 3) / 4) * 4 + 4;
		else {
			allocated = MAX_PARTICLES_IN_A_SYSTEM + 4;
			maxParticles = MAX_PARTICLES_IN_A_SYSTEM;
		}
		AllocatedParticles = allocated;
		MaxAllowedParticles = maxParticles;

		PerParticleInitializedAttributeMask = def.PerParticleInitializedAttributeMask;
		PerParticleUpdatedAttributeMask = def.PerParticleUpdatedAttributeMask;
		int perParticleAttributeMask = PerParticleInitializedAttributeMask | PerParticleUpdatedAttributeMask;
		PerParticleReadInitialAttributeMask = def.InitialAttributeReadMask & def.PerParticleUpdatedAttributeMask & def.PerParticleInitializedAttributeMask;
		int constantInitialMask = def.InitialAttributeReadMask & def.PerParticleInitializedAttributeMask & ~def.PerParticleUpdatedAttributeMask;

		int floatsPerParticle = 0;
		int initialFloatsPerParticle = 0;
		for (int bit = 0; bit < MAX_PARTICLE_ATTRIBUTES; bit++) {
			int attrSize = AttributeSize(bit);
			if ((perParticleAttributeMask & (1 << bit)) != 0)
				floatsPerParticle += attrSize;
			if ((PerParticleReadInitialAttributeMask & (1 << bit)) != 0)
				initialFloatsPerParticle += attrSize;
		}

		Memory = new float[CONSTANT_FLOATS + (floatsPerParticle + initialFloatsPerParticle) * allocated];

		int cursor = CONSTANT_FLOATS;
		int initialCursor = CONSTANT_FLOATS + floatsPerParticle * allocated;
		for (int bit = 0; bit < MAX_PARTICLE_ATTRIBUTES; bit++) {
			int attrSize = AttributeSize(bit);
			if ((perParticleAttributeMask & (1 << bit)) == 0) {
				ParticleFloatStrides[bit] = 0;
				AttributeOffsets[bit] = ConstantOffset(bit);
			}
			else {
				ParticleFloatStrides[bit] = attrSize * 4;
				AttributeOffsets[bit] = cursor;
				cursor += allocated * attrSize;
			}

			if ((def.InitialAttributeReadMask & (1 << bit)) == 0) {
				InitialAttributeOffsets[bit] = -1;
				ParticleInitialFloatStrides[bit] = 0;
			}
			else if ((PerParticleReadInitialAttributeMask & (1 << bit)) != 0) {
				InitialAttributeOffsets[bit] = initialCursor;
				ParticleInitialFloatStrides[bit] = attrSize * 4;
				initialCursor += attrSize * allocated;
			}
			else if ((constantInitialMask & (1 << bit)) != 0) {
				InitialAttributeOffsets[bit] = AttributeOffsets[bit];
				ParticleInitialFloatStrides[bit] = ParticleFloatStrides[bit];
			}
			else {
				InitialAttributeOffsets[bit] = ConstantOffset(bit);
				ParticleInitialFloatStrides[bit] = 0;
			}
		}
	}

	static int AttributeSize(int attribute) => (ATTRIBUTES_WHICH_ARE_VEC3S_MASK & (1 << attribute)) != 0 ? 3 : 1;
	static int ConstantOffset(int attribute) => 12 * attribute;

	Span<byte> ContextSpan(int offset) => OperatorContextData.AsSpan(offset);

	public string GetName() => Def?.GetName() ?? "";
	public bool IsValid() => Def != null && Def.GetMaterial() != null;
	public int GetGroupID() => Def!.GroupID;

	void LinkIntoDefList() {
		PrevDef = null;
		NextDef = Def!.FirstCollection;
		Def.FirstCollection = this;
		if (NextDef != null)
			NextDef.PrevDef = this;
	}

	public void UnlinkFromDefList() {
		if (Def == null)
			return;

		if (Def.FirstCollection == this)
			Def.FirstCollection = NextDef;
		else
			PrevDef!.NextDef = NextDef;

		if (NextDef != null)
			NextDef.PrevDef = PrevDef;

		NextDef = null;
		PrevDef = null;
	}

	public ParticleCollection? GetNextCollectionUsingSameDef() => NextDef;

	public Span<float> AttributeMemory => Memory;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetAttributeIndex(int attribute, int particleNumber)
		=> AttributeOffsets[attribute] + ParticleFloatStrides[attribute] * (particleNumber / 4) + (particleNumber & 3);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetInitialAttributeIndex(int attribute, int particleNumber)
		=> InitialAttributeOffsets[attribute] + ParticleInitialFloatStrides[attribute] * (particleNumber / 4) + (particleNumber & 3);

	public int GetAttributeOffset(int attribute, out int floatStride) {
		floatStride = ParticleFloatStrides[attribute];
		return AttributeOffsets[attribute];
	}

	public int GetInitialAttributeOffset(int attribute, out int floatStride) {
		floatStride = ParticleInitialFloatStrides[attribute];
		return InitialAttributeOffsets[attribute];
	}

	public float GetFloatAttributeValue(int attribute, int particleNumber) => Memory[GetAttributeIndex(attribute, particleNumber)];
	public int GetIntAttributeValue(int attribute, int particleNumber) => BitConverter.SingleToInt32Bits(Memory[GetAttributeIndex(attribute, particleNumber)]);

	public Vector3 GetVectorAttributeValue(int attribute, int particleNumber) {
		int index = GetAttributeIndex(attribute, particleNumber);
		return new(Memory[index], Memory[index + 4], Memory[index + 8]);
	}

	public ref float GetFloatAttributeRefForWrite(int attribute, int particleNumber) => ref Memory[GetAttributeIndex(attribute, particleNumber)];
	public ref int GetIntAttributeRefForWrite(int attribute, int particleNumber) => ref Unsafe.As<float, int>(ref Memory[GetAttributeIndex(attribute, particleNumber)]);

	public void SetVectorAttribute(int attribute, int particleNumber, Vector3 v) {
		int index = GetAttributeIndex(attribute, particleNumber);
		Memory[index] = v.X;
		Memory[index + 4] = v.Y;
		Memory[index + 8] = v.Z;
	}

	public float GetInitialFloatAttributeValue(int attribute, int particleNumber) => Memory[GetInitialAttributeIndex(attribute, particleNumber)];

	public Vector3 GetInitialVectorAttributeValue(int attribute, int particleNumber) {
		int index = GetInitialAttributeIndex(attribute, particleNumber);
		return new(Memory[index], Memory[index + 4], Memory[index + 8]);
	}

	public ref float GetInitialFloatAttributeRefForWrite(int attribute, int particleNumber) => ref Memory[GetInitialAttributeIndex(attribute, particleNumber)];

	public Span<float> GetConstantAttributeMemory(int attribute) => Memory.AsSpan(ConstantOffset(attribute), 12);

	void SetAttributeToConstant(int attribute, float value) => Memory.AsSpan(ConstantOffset(attribute), 4).Fill(value);

	void SetAttributeToConstant(int attribute, float x, float y, float z) {
		Span<float> constant = Memory.AsSpan(ConstantOffset(attribute), 12);
		constant[..4].Fill(x);
		constant[4..8].Fill(y);
		constant[8..].Fill(z);
	}

	public void FillAttributeWithConstant(int attribute, float value) {
		int offset = GetAttributeOffset(attribute, out int stride);
		for (int i = 0; i < PaddedActiveParticles; i++) {
			Memory.AsSpan(offset, 4).Fill(value);
			offset += stride;
		}
	}

	public void SwapPosAndPrevPos()
		=> (AttributeOffsets[PARTICLE_ATTRIBUTE_XYZ], AttributeOffsets[PARTICLE_ATTRIBUTE_PREV_XYZ]) = (AttributeOffsets[PARTICLE_ATTRIBUTE_PREV_XYZ], AttributeOffsets[PARTICLE_ATTRIBUTE_XYZ]);

	public void SetNActiveParticles(int count) {
		ActiveParticles = count;
		PaddedActiveParticles = (count + 3) / 4;
	}

	public void KillParticle(int particleIndex) => ParticleKillList![NumParticlesToKill++] = particleIndex;

	public bool HasAttachedKillList() => ParticleKillList != null;

	public void LoanKillListTo(ParticleCollection borrower) {
		borrower.NumParticlesToKill = 0;
		borrower.ParticleKillList = ParticleKillList;
	}

	public bool ReadsControlPoint(int point) => (ControlPointReadMask & (1UL << point)) != 0;

	public void ResetControlPoints() {
		HighestCP = 0;
		for (ParticleCollection? i = Children; i != null; i = i.Next)
			i.ResetControlPoints();
	}

	public int GetControlPointIndex() => TargetCP;

	public void SetControlPointIndex(int whichPoint) {
		HighestCP = Math.Max(HighestCP, whichPoint);
		TargetCP = whichPoint;
		for (ParticleCollection? i = Children; i != null; i = i.Next)
			i.SetControlPointIndex(whichPoint);
	}

	public void SetControlPoint(int whichPoint, in Vector3 v) {
		HighestCP = Math.Max(HighestCP, whichPoint);
		ControlPoints[whichPoint].Position = v;
		for (ParticleCollection? i = Children; i != null; i = i.Next)
			i.SetControlPoint(whichPoint, v);
	}

	public void SetControlPointObject(int whichPoint, object? obj) {
		ControlPoints[whichPoint].Object = obj;
		for (ParticleCollection? i = Children; i != null; i = i.Next)
			i.SetControlPointObject(whichPoint, obj);
	}

	public void SetControlPointOrientation(int whichPoint, in Vector3 forward, in Vector3 right, in Vector3 up) {
		if (MathF.Abs(Vector3.Dot(forward, up)) <= 0.1f && MathF.Abs(Vector3.Dot(forward, right)) <= 0.1f && MathF.Abs(Vector3.Dot(right, up)) <= 0.1f) {
			ref ParticleControlPoint cp = ref ControlPoints[whichPoint];
			cp.ForwardVector = forward;
			cp.UpVector = up;
			cp.RightVector = right;

			for (ParticleCollection? i = Children; i != null; i = i.Next)
				i.SetControlPointOrientation(whichPoint, forward, right, up);
		}
		else
			Warning($"Attempt to set particle collection {GetName()} to invalid orientation matrix\n");
	}

	public void SetControlPointOrientation(int whichPoint, in Quaternion q) {
		MathLib.QuaternionMatrix(in q, out Matrix3x4 mat);
		MathLib.MatrixGetColumn(in mat, 0, out Vector3 forward);
		MathLib.MatrixGetColumn(in mat, 1, out Vector3 left);
		MathLib.MatrixGetColumn(in mat, 2, out Vector3 up);
		SetControlPointOrientation(whichPoint, forward, -left, up);
	}

	public Vector3 TransformAxis(in Vector3 srcAxis, bool localSpace, int controlPointNumber = 0) {
		if (!localSpace)
			return srcAxis;

		ref ParticleControlPoint cp = ref ControlPoints[controlPointNumber];
		return srcAxis.X * cp.RightVector + srcAxis.Y * cp.ForwardVector + srcAxis.Z * cp.UpVector;
	}

	public void SetControlPointForwardVector(int whichPoint, in Vector3 v) {
		ControlPoints[whichPoint].ForwardVector = v;
		for (ParticleCollection? i = Children; i != null; i = i.Next)
			i.SetControlPointForwardVector(whichPoint, v);
	}

	public void SetControlPointUpVector(int whichPoint, in Vector3 v) {
		ControlPoints[whichPoint].UpVector = v;
		for (ParticleCollection? i = Children; i != null; i = i.Next)
			i.SetControlPointUpVector(whichPoint, v);
	}

	public void SetControlPointRightVector(int whichPoint, in Vector3 v) {
		ControlPoints[whichPoint].RightVector = v;
		for (ParticleCollection? i = Children; i != null; i = i.Next)
			i.SetControlPointRightVector(whichPoint, v);
	}

	public void SetControlPointParent(int whichPoint, int n) {
		ControlPoints[whichPoint].Parent = n;
		for (ParticleCollection? i = Children; i != null; i = i.Next)
			i.SetControlPointParent(whichPoint, n);
	}

	public void SetControlPointVelocity(int whichPoint, Vector3 velocity) {
		HighestCP = Math.Max(HighestCP, whichPoint);
		ControlPoints[whichPoint].Velocity = velocity;
		for (ParticleCollection? i = Children; i != null; i = i.Next)
			i.SetControlPointVelocity(whichPoint, velocity);
	}

	public void SetControlPointDensity(int whichPoint, float density) {
		HighestCP = Math.Max(HighestCP, whichPoint);
		ControlPoints[whichPoint].Density = density;
		for (ParticleCollection? i = Children; i != null; i = i.Next)
			i.SetControlPointDensity(whichPoint, density);
	}

	public void SetControlPointDuration(int whichPoint, float duration) {
		HighestCP = Math.Max(HighestCP, whichPoint);
		ControlPoints[whichPoint].Duration = duration;
		for (ParticleCollection? i = Children; i != null; i = i.Next)
			i.SetControlPointDuration(whichPoint, duration);
	}

	public void SetControlPointRadius(int whichPoint, float radius) {
		HighestCP = Math.Max(HighestCP, whichPoint);
		ControlPoints[whichPoint].Radius = radius;
		for (ParticleCollection? i = Children; i != null; i = i.Next)
			i.SetControlPointRadius(whichPoint, radius);
	}

	public ref readonly ParticleControlPoint ControlPoint(int index) => ref ControlPoints[index];
	public ref readonly Vector3 GetControlPointAtCurrentTime(int controlPoint) => ref ControlPoints[controlPoint].Position;

	public void GetControlPointOrientationAtCurrentTime(int controlPoint, out Vector3 forward, out Vector3 right, out Vector3 up) {
		ref ParticleControlPoint cp = ref ControlPoints[controlPoint];
		forward = cp.ForwardVector;
		right = cp.RightVector;
		up = cp.UpVector;
	}

	public int GetControlPointParent(int controlPoint) => ControlPoints[controlPoint].Parent;
	public float GetControlPointRadiusAtCurrentTime(int controlPoint) => ControlPoints[controlPoint].Radius;
	public float GetControlPointDensityAtCurrentTime(int controlPoint) => ControlPoints[controlPoint].Density;
	public float GetControlPointDurationAtCurrentTime(int controlPoint) => ControlPoints[controlPoint].Duration;
	public Vector3 GetControlPointVelocityAtCurrentTime(int controlPoint) => ControlPoints[controlPoint].Velocity;

	public void GetControlPointAtTime(int controlPoint, float time, out Vector3 controlPointOut) {
		if (controlPoint > HighestCP)
			DevWarning(2, $"Warning : Particle system ({GetName()}) using unassigned ControlPoint {controlPoint}!\n");

		ref ParticleControlPoint cp = ref ControlPoints[controlPoint];
		if (Dt == 0.0f) {
			controlPointOut = cp.Position;
			return;
		}

		float t = (time + Dt - CurTime) / Dt;
		if (t <= 0.0f)
			t = 0.0f;
		controlPointOut = new(
			(cp.Position.X - cp.PrevPosition.X) * t + cp.PrevPosition.X,
			(cp.Position.Y - cp.PrevPosition.Y) * t + cp.PrevPosition.Y,
			t * (cp.Position.Z - cp.PrevPosition.Z) + cp.PrevPosition.Z
		);
	}

	public void GetControlPointAtPrevTime(int controlPoint, out Vector3 controlPointOut) => controlPointOut = ControlPoints[controlPoint].PrevPosition;

	public void GetControlPointOrientationAtTime(int controlPoint, float time, out Vector3 forward, out Vector3 right, out Vector3 up)
		=> GetControlPointOrientationAtCurrentTime(controlPoint, out forward, out right, out up);

	public void GetControlPointTransformAtCurrentTime(int controlPoint, out Matrix3x4 mat)
		=> BuildControlPointTransform(controlPoint, ControlPoints[controlPoint].Position, out mat);

	public void GetControlPointTransformAtTime(int controlPoint, float time, out Matrix3x4 mat) {
		GetControlPointAtTime(controlPoint, time, out Vector3 position);
		BuildControlPointTransform(controlPoint, position, out mat);
	}

	void BuildControlPointTransform(int controlPoint, in Vector3 position, out Matrix3x4 mat) {
		ref ParticleControlPoint cp = ref ControlPoints[controlPoint];
		mat = new(
			cp.ForwardVector.X, -cp.RightVector.X, cp.UpVector.X, position.X,
			cp.ForwardVector.Y, -cp.RightVector.Y, cp.UpVector.Y, position.Y,
			cp.ForwardVector.Z, -cp.RightVector.Z, cp.UpVector.Z, position.Z
		);
	}

	public int GetHighestControlPoint() => HighestCP;

	public void GetBounds(out Vector3 min, out Vector3 max) {
		min = MinBounds;
		max = MaxBounds;
	}

	public bool HasMoved() {
		if (Def == null)
			return false;

		for (int i = 0; i <= HighestCP; i++) {
			if ((Def.ControlPointReadMask & (1UL << i)) != 0 && ControlPoints[i].PrevPosition != ControlPoints[i].Position)
				return true;
		}

		for (ParticleCollection? child = Children; child != null; child = child.Next) {
			if (child.HasMoved())
				return true;
		}
		return false;
	}

	public int OperatorRandomSampleOffset() => OperatorRandomSampleOffset_;

	public int RandomInt(int randomSampleId, int min, int max) {
		float rand = RandomFloats[(RandomSeed + randomSampleId) & RANDOM_FLOAT_MASK];
		rand *= max + 1 - min;
		return (int)rand + min;
	}

	public float RandomFloat(int randomSampleId, float min, float max) {
		float rand = RandomFloats[(RandomSeed + randomSampleId) & RANDOM_FLOAT_MASK];
		rand *= max - min;
		rand += min;
		return rand;
	}

	public Vector128<float> RandomFloat(Vector128<int> particleId, int randomSampleOffset) {
		int ofs = RandomSeed + randomSampleOffset;
		return Vector128.Create(
			RandomFloats[(ofs + particleId[0]) & RANDOM_FLOAT_MASK],
			RandomFloats[(ofs + particleId[1]) & RANDOM_FLOAT_MASK],
			RandomFloats[(ofs + particleId[2]) & RANDOM_FLOAT_MASK],
			RandomFloats[(ofs + particleId[3]) & RANDOM_FLOAT_MASK]
		);
	}

	public float RandomFloatExp(int randomSampleId, float min, float max, float exponent) {
		float rand = RandomFloats[(RandomSeed + randomSampleId) & RANDOM_FLOAT_MASK];
		rand = MathF.Pow(rand, exponent);
		rand *= max - min;
		rand += min;
		return rand;
	}

	public void RandomVector(int randomSampleId, float min, float max, out Vector3 vector) {
		float delta = max - min;
		int baseId = RandomSeed + randomSampleId;
		vector.X = RandomFloats[baseId & RANDOM_FLOAT_MASK] * delta + min;
		vector.Y = RandomFloats[(baseId + 1) & RANDOM_FLOAT_MASK] * delta + min;
		vector.Z = RandomFloats[(baseId + 2) & RANDOM_FLOAT_MASK] * delta + min;
	}

	public void RandomVector(int randomSampleId, in Vector3 vecMin, in Vector3 vecMax, out Vector3 vector) {
		int baseId = RandomSeed + randomSampleId;
		vector.X = RandomFloat(baseId, vecMin.X, vecMax.X);
		vector.Y = RandomFloat(baseId + 1, vecMin.Y, vecMax.Y);
		vector.Z = RandomFloat(baseId + 2, vecMin.Z, vecMax.Z);
	}

	public float RandomVectorInUnitSphere(int randomSampleId, out Vector3 vector) {
		float r0 = RandomFloats[(randomSampleId + RandomSeed) & RANDOM_FLOAT_MASK] * 0.9999f + 0.0001f;
		float r1 = RandomFloats[(RandomSeed + 1 + randomSampleId) & RANDOM_FLOAT_MASK] * 0.9999f + 0.0001f;
		float r2 = RandomFloats[(RandomSeed + 2 + randomSampleId) & RANDOM_FLOAT_MASK] * 0.9999f + 0.0001f;

		(float sinPhi, float cosPhi) = MathF.SinCos(MathF.Acos(1.0f - (r0 + r0)));
		(float sinTheta, float cosTheta) = MathF.SinCos(r1 * 6.2831855f);
		float radius = MathF.Cbrt(r2);

		vector.Z = cosPhi * radius;
		vector.X = sinPhi * radius * cosTheta;
		vector.Y = sinPhi * radius * sinTheta;
		return radius;
	}

	public int RandomInt(int min, int max) => RandomInt(RandomQueryCount++, min, max);
	public float RandomFloat(float min, float max) => RandomFloat(RandomQueryCount++, min, max);
	public float RandomFloatExp(float min, float max, float exponent) => RandomFloatExp(RandomQueryCount++, min, max, exponent);
	public void RandomVector(float min, float max, out Vector3 vector) => RandomVector(RandomQueryCount++, min, max, out vector);
	public void RandomVector(in Vector3 vecMin, in Vector3 vecMax, out Vector3 vector) => RandomVector(RandomQueryCount++, vecMin, vecMax, out vector);
	public float RandomVectorInUnitSphere(out Vector3 vector) => RandomVectorInUnitSphere(RandomQueryCount++, out vector);

	static float FadeInOut(float fadeInStart, float fadeInEnd, float fadeOutStart, float fadeOutEnd, float curTime) {
		if (fadeInStart > curTime || (fadeOutEnd > 0.0f && curTime > fadeOutEnd))
			return 0.0f;

		if (fadeInEnd <= fadeInStart)
			fadeInEnd = fadeInStart;
		if (fadeOutStart <= fadeInEnd)
			fadeOutStart = fadeInEnd;
		if (fadeOutEnd <= fadeOutStart)
			fadeOutEnd = fadeOutStart;

		float strength;
		if (fadeInEnd <= fadeInStart || fadeInEnd <= curTime)
			strength = 1.0f;
		else {
			strength = (curTime - fadeInStart) / (fadeInEnd - fadeInStart);
			if (strength >= 1.0f)
				strength = 1.0f;
		}

		if (fadeOutStart < fadeOutEnd && fadeOutStart < curTime) {
			float fadeOut = (curTime - fadeOutEnd) / (fadeOutStart - fadeOutEnd);
			if (fadeOut <= strength)
				strength = fadeOut;
		}
		return strength;
	}

	public bool CheckIfOperatorShouldRun(ParticleOperatorInstance op, out float curStrength) {
		float time = CurTime;
		if (op.OpFadeOscillatePeriod > 0.0f)
			time = (CurTime / op.OpFadeOscillatePeriod) % 1.0f;

		curStrength = FadeInOut(op.OpStartFadeInTime, op.OpEndFadeInTime, op.OpStartFadeOutTime, op.OpEndFadeOutTime, time);
		return curStrength > 0.0f;
	}

	public void Restart() {
		ParticleSystemDefinition def = Def!;
		for (int i = 0; i < def.Emitters.Count; i++)
			def.Emitters[i].Restart(this, ContextSpan(def.EmittersCtxOffsets[i]));

		for (ParticleCollection? child = Children; child != null; child = child.Next)
			child.Restart();
	}

	void UpdatePrevControlPoints(float dt) {
		PreviousDt = dt;
		for (int i = 0; i <= HighestCP; i++)
			ControlPoints[i].PrevPosition = ControlPoints[i].Position;
		ParticleFlags |= PCFLAGS_PREV_CONTROL_POINTS_INITIALIZED;
	}

	public void InitializeNewParticles(int firstParticle, int particleCount, uint inittedMask) {
		ParticleSystemDefinition def = Def!;
		int attrsLeftToInit = (PerParticleInitializedAttributeMask | PerParticleUpdatedAttributeMask) & ~(int)inittedMask;

		int count = def.Initializers.Count;
		if (count > 0) {
			for (int i = 0; i < count; i++) {
				ParticleOperatorInstance op = def.Initializers[i];
				int written = (int)op.GetWrittenAttributes();
				if ((attrsLeftToInit & written) == 0 || op.InitMultipleOverride())
					continue;

				RunInitializer(op, ContextSpan(def.InitializersCtxOffsets[i]), firstParticle, particleCount, attrsLeftToInit);
				attrsLeftToInit &= ~written;
			}

			for (int i = 0; i < count; i++) {
				ParticleOperatorInstance op = def.Initializers[i];
				int written = (int)op.GetWrittenAttributes();
				if (!op.InitMultipleOverride())
					continue;

				RunInitializer(op, ContextSpan(def.InitializersCtxOffsets[i]), firstParticle, particleCount, attrsLeftToInit);
				attrsLeftToInit &= ~written;
			}
		}

		if (attrsLeftToInit != 0)
			InitParticleAttributes(firstParticle, particleCount, attrsLeftToInit);

		if (PerParticleReadInitialAttributeMask != 0)
			CopyInitialAttributeValues(firstParticle, particleCount);
	}

	void RunInitializer(ParticleOperatorInstance op, Span<byte> context, int firstParticle, int particleCount, int attrsLeftToInit) {
		if (IsScrubbable && !op.IsScrubSafe()) {
			for (int p = firstParticle; p < firstParticle + particleCount; p++)
				op.InitNewParticlesScalar(this, p, 1, attrsLeftToInit, context);
		}
		else
			op.InitNewParticles(this, firstParticle, particleCount, attrsLeftToInit, context);
	}

	void InitParticleAttributes(int startParticle, int numParticles, int attrsLeftToInit) {
		for (int p = startParticle; p < startParticle + numParticles; p++) {
			for (int attr = 0; attr < MAX_PARTICLE_ATTRIBUTES; attr++) {
				if ((attrsLeftToInit & (1 << attr)) == 0)
					continue;

				int index = GetAttributeIndex(attr, p);
				if (attr == PARTICLE_ATTRIBUTE_PARTICLE_ID) {
					Memory[index] = BitConverter.Int32BitsToSingle((UniqueParticleId + RandomSeed) & RANDOM_FLOAT_MASK);
					UniqueParticleId++;
				}
				else if (attr == PARTICLE_ATTRIBUTE_CREATION_TIME)
					Memory[index] = CurTime;
				else {
					int constant = ConstantOffset(attr);
					Memory[index] = Memory[constant];
					if (ParticleFloatStrides[attr] == 12) {
						Memory[index + 4] = Memory[constant + 4];
						Memory[index + 8] = Memory[constant + 8];
					}
				}
			}
		}
	}

	void CopyInitialAttributeValues(int startParticle, int numParticles) {
		for (int p = startParticle; p < startParticle + numParticles; p++) {
			for (int attr = 0; attr < MAX_PARTICLE_ATTRIBUTES; attr++) {
				if ((PerParticleReadInitialAttributeMask & (1 << attr)) == 0)
					continue;

				int src = GetAttributeIndex(attr, p);
				int dst = GetInitialAttributeIndex(attr, p);
				Memory[dst] = Memory[src];
				if (ParticleFloatStrides[attr] == 12) {
					Memory[dst + 4] = Memory[src + 4];
					Memory[dst + 8] = Memory[src + 8];
				}
			}
		}
	}

	public void SkipToTime(float t) {
		if (t <= CurTime)
			return;

		UpdatePrevControlPoints(t - CurTime);
		CurTime = t;
		ParticleFlags &= ~PCFLAGS_FIRST_FRAME;

		ParticleSystemDefinition def = Def!;
		for (int i = 0; i < def.Emitters.Count; i++)
			def.Emitters[i].SkipToTime(t, this, ContextSpan(def.EmittersCtxOffsets[i]));

		int childIndex = 0;
		for (ParticleCollection? child = Children; child != null; child = child.Next)
			child.SkipToTime(t - def.Children[childIndex++].Delay);
	}

	void SimulateFirstFrame() {
		Dt = 0.0f;
		DrawnFrames = 0;
		SimulatedFrames = 1;

		if ((ParticleFlags & PCFLAGS_PREV_CONTROL_POINTS_INITIALIZED) == 0)
			UpdatePrevControlPoints(0.05f);

		OperatorRandomSampleOffset_ = 0;
		ParticleSystemDefinition def = Def!;
		for (int i = 0; i < def.Operators.Count; i++) {
			ParticleOperatorInstance op = def.Operators[i];
			if (op.ShouldRunBeforeEmitters() && CheckIfOperatorShouldRun(op, out float strength)) {
				op.Operate(this, strength, ContextSpan(def.OperatorsCtxOffsets[i]));
				UpdatePrevControlPoints(0.05f);
			}
			OperatorRandomSampleOffset_ += 17;
		}

		int initialParticles = Math.Min(def.InitialParticles, MaxAllowedParticles);
		if (initialParticles < 1)
			return;

		SetNActiveParticles(initialParticles);
		InitializeNewParticles(0, initialParticles, 0);
	}

	public void Simulate(float dt, bool updateBboxOnly) {
		if (dt < 0.0f || Def == null)
			return;

		if (CurTime < 0.0f) {
			if (dt >= 1e-22f) {
				CurTime += dt;
				UpdatePrevControlPoints(dt);
			}
			return;
		}

		if ((ParticleFlags & PCFLAGS_FIRST_FRAME) != 0) {
			SimulateFirstFrame();
			ParticleFlags &= ~PCFLAGS_FIRST_FRAME;
		}

		if (dt < 1e-22f)
			return;

		bool attachedKillList = false;
		if (ParticleKillList == null) {
			Mgr!.AttachKillList(this);
			attachedKillList = true;
		}

		if (!updateBboxOnly)
			RunSimulationSteps(dt);

		for (ParticleCollection? child = Children; child != null; child = child.Next) {
			child.NumParticlesToKill = 0;
			child.ParticleKillList = ParticleKillList;
			child.Simulate(dt, updateBboxOnly);
			child.ParticleKillList = null;
		}

		if (attachedKillList)
			Mgr!.DetachKillList(this);

		UpdatePrevControlPoints(dt);
		BloatBoundsUsingControlPoint();
	}

	void RunSimulationSteps(float dt) {
		ParticleSystemDefinition def = Def!;
		SimulatedFrames++;

		float maxTimeStep = def.MaximumTimeStep;
		float maxDt;
		if (maxTimeStep <= 0.0f) {
			maxTimeStep = 0.1f;
			maxDt = 1.0f;
		}
		else
			maxDt = maxTimeStep * 10.0f;

		float remaining = dt;
		if (def.MaximumSimTime != 0.0f && DrawnFrames <= def.MinimumFrames) {
			if (dt + CurTime > def.MaximumSimTime) {
				remaining = def.MaximumSimTime - CurTime;
				if (remaining < def.MinimumSimTime)
					remaining = def.MinimumSimTime;
			}
			DrawnFrames++;
		}

		if (remaining >= maxDt)
			remaining = maxDt;

		while (remaining > 0.0f) {
			OperatorRandomSampleOffset_ = 0;
			float step = remaining <= maxTimeStep ? remaining : maxTimeStep;
			remaining -= step;
			Dt = step;
			CurTime += step;

			for (int i = 0; i < def.Operators.Count; i++) {
				ParticleOperatorInstance op = def.Operators[i];
				if (!op.ShouldRunBeforeEmitters() || !CheckIfOperatorShouldRun(op, out float strength))
					continue;

				op.Operate(this, strength, ContextSpan(def.OperatorsCtxOffsets[i]));
				if (NumParticlesToKill != 0)
					ApplyKillList();
				OperatorRandomSampleOffset_ += 17;
			}

			int prevActive = ActiveParticles;
			for (int i = 0; i < def.Emitters.Count; i++) {
				ParticleOperatorInstance emitter = def.Emitters[i];
				if (CheckIfOperatorShouldRun(emitter, out float strength)) {
					uint inittedMask = emitter.Emit(this, strength, ContextSpan(def.EmittersCtxOffsets[i]));
					if (ActiveParticles != prevActive)
						InitializeNewParticles(prevActive, ActiveParticles - prevActive, inittedMask);
				}
				prevActive = ActiveParticles;
			}

			OperatorRandomSampleOffset_ = 0;
			if (prevActive == 0)
				continue;

			for (int i = 0; i < def.Operators.Count; i++) {
				ParticleOperatorInstance op = def.Operators[i];
				if (op.ShouldRunBeforeEmitters() || !CheckIfOperatorShouldRun(op, out float strength))
					continue;

				op.Operate(this, strength, ContextSpan(def.OperatorsCtxOffsets[i]));
				if (NumParticlesToKill != 0) {
					ApplyKillList();
					if (ActiveParticles == 0)
						break;
				}
				OperatorRandomSampleOffset_ += 17;
			}
		}
	}

	void MoveParticle(int initialIndex, int newIndex) {
		int newVec = (newIndex & 3) + (newIndex >> 2) * 12;
		int oldVec = (initialIndex & 3) + (initialIndex >> 2) * 12;

		for (int attr = 0; attr < MAX_PARTICLE_ATTRIBUTES; attr++) {
			MoveAttribute(AttributeOffsets[attr], ParticleFloatStrides[attr], initialIndex, newIndex, oldVec, newVec);
			MoveAttribute(InitialAttributeOffsets[attr], ParticleInitialFloatStrides[attr], initialIndex, newIndex, oldVec, newVec);
		}
	}

	void MoveAttribute(int offset, int stride, int initialIndex, int newIndex, int oldVec, int newVec) {
		if (stride == 4)
			Memory[offset + newIndex] = Memory[offset + initialIndex];
		else if (stride == 12) {
			Memory[offset + newVec] = Memory[offset + oldVec];
			Memory[offset + newVec + 4] = Memory[offset + oldVec + 4];
			Memory[offset + newVec + 8] = Memory[offset + oldVec + 8];
		}
	}

	void ApplyKillList() {
		int leftInKillList = NumParticlesToKill;
		if (leftInKillList == 0)
			return;

		int activeNow = ActiveParticles;
		int[] killList = ParticleKillList!;
		int slot = 0;

		while (leftInKillList != 0 && killList[leftInKillList - 1] >= activeNow)
			leftInKillList--;

		if (RequiresOrderInvariance) {
			int remaining = leftInKillList - 1;
			int finalCount = activeNow - leftInKillList;
			int writeIndex = killList[slot++];
			for (int readIndex = writeIndex + 1; readIndex < activeNow; readIndex++) {
				if (remaining >= 1 && killList[slot] == readIndex) {
					slot++;
					remaining--;
					continue;
				}
				MoveParticle(readIndex, writeIndex++);
			}
			activeNow = finalCount;
		}
		else if (leftInKillList != 0) {
			int killIndex = killList[slot];
			while (true) {
				if (leftInKillList - 1 == 0) {
					activeNow--;
					if (killIndex != activeNow)
						MoveParticle(activeNow, killIndex);
					break;
				}

				int last = leftInKillList - 1;
				bool tailConsumed = false;
				while (true) {
					int previous = activeNow;
					activeNow = previous - 1;
					if (killList[slot + last] != activeNow)
						break;
					last--;
					if (last == 0) {
						activeNow = previous - 2;
						tailConsumed = true;
						break;
					}
				}

				if (tailConsumed) {
					if (killIndex != activeNow)
						MoveParticle(activeNow, killIndex);
					break;
				}

				if (activeNow == killIndex)
					break;

				MoveParticle(activeNow, killIndex);
				leftInKillList = last;
				killIndex = killList[++slot];
			}
		}

		ActiveParticles = activeNow;
		NumParticlesToKill = 0;
		PaddedActiveParticles = (activeNow + 3) / 4;
	}

	public void StopEmission(bool infiniteOnly = false, bool removeAllParticles = false, bool wakeOnStop = false) {
		if (Def == null)
			return;

		Dormant = false;
		if (wakeOnStop)
			NextSleepTime = Math.Max(Mgr!.GetLastSimulationTime() + 10.0f, NextSleepTime);

		EmissionStopped = true;
		for (int i = 0; i < Def.Emitters.Count; i++)
			Def.Emitters[i].StopEmission(this, ContextSpan(Def.EmittersCtxOffsets[i]), infiniteOnly);

		if (removeAllParticles) {
			ActiveParticles = 0;
			PaddedActiveParticles = 0;
		}

		for (ParticleCollection? child = Children; child != null; child = child.Next)
			child.StopEmission(infiniteOnly, removeAllParticles, false);
	}

	public void StartEmission(bool infiniteOnly = false) {
		if (Def == null)
			return;

		EmissionStopped = false;
		StartEmissionInternal(infiniteOnly);
	}

	void StartEmissionInternal(bool infiniteOnly) {
		ParticleSystemDefinition def = Def!;
		for (int i = 0; i < def.Emitters.Count; i++)
			def.Emitters[i].StartEmission(this, ContextSpan(def.EmittersCtxOffsets[i]), infiniteOnly);

		for (ParticleCollection? child = Children; child != null; child = child.Next) {
			if (child.Def != null) {
				child.EmissionStopped = false;
				child.StartEmissionInternal(infiniteOnly);
			}
		}

		NextSleepTime = Mgr!.GetLastSimulationTime() + def.NoDrawTimeToGoToSleep;
	}

	public void SetDormant(bool dormant) {
		if (Dormant == dormant)
			return;

		if (!EmissionStopped) {
			if (dormant) {
				StopEmission();
				Dormant = dormant;
				return;
			}
		}
		else if (dormant)
			return;

		StartEmission();
		Dormant = dormant;
	}

	public bool IsFinished() {
		if (Def == null)
			return true;

		if ((ParticleFlags & PCFLAGS_FIRST_FRAME) != 0 || ActiveParticles != 0 || Dormant)
			return false;

		for (int i = 0; i < Def.Emitters.Count; i++) {
			if (Def.Emitters[i].MayCreateMoreParticles(this, ContextSpan(Def.EmittersCtxOffsets[i])))
				return false;
		}

		for (ParticleCollection? child = Children; child != null; child = child.Next) {
			if (!child.IsFinished())
				return false;
		}
		return true;
	}

	public void RecomputeBounds() {
		int count = ActiveParticles;
		if (count == 0) {
			BoundsValid = false;
			MinBounds = new(float.MaxValue);
			MaxBounds = new(-float.MaxValue);
			Center = Vector3.Zero;
			return;
		}

		const float BIG = 1e23f;
		int offset = GetAttributeOffset(PARTICLE_ATTRIBUTE_XYZ, out int stride);

		Span<float> minX = stackalloc float[4], minY = stackalloc float[4], minZ = stackalloc float[4];
		Span<float> maxX = stackalloc float[4], maxY = stackalloc float[4], maxZ = stackalloc float[4];
		Span<float> sumX = stackalloc float[4], sumY = stackalloc float[4], sumZ = stackalloc float[4];
		minX.Fill(BIG); minY.Fill(BIG); minZ.Fill(BIG);
		maxX.Fill(-BIG); maxY.Fill(-BIG); maxZ.Fill(-BIG);

		int blocks = count / 4;
		for (int b = 0; b < blocks; b++) {
			for (int lane = 0; lane < 4; lane++) {
				float x = Memory[offset + lane], y = Memory[offset + 4 + lane], z = Memory[offset + 8 + lane];
				minX[lane] = MathF.Min(minX[lane], x); maxX[lane] = MathF.Max(maxX[lane], x); sumX[lane] += x;
				minY[lane] = MathF.Min(minY[lane], y); maxY[lane] = MathF.Max(maxY[lane], y); sumY[lane] += y;
				minZ[lane] = MathF.Min(minZ[lane], z); maxZ[lane] = MathF.Max(maxZ[lane], z); sumZ[lane] += z;
			}
			offset += stride;
		}

		BoundsValid = true;
		Vector3 min = new(HorizontalMin(minX), HorizontalMin(minY), HorizontalMin(minZ));
		Vector3 max = new(HorizontalMax(maxX), HorizontalMax(maxY), HorizontalMax(maxZ));
		float totalX = sumX[3] + sumX[2] + sumX[1] + sumX[0];
		float totalY = sumY[1] + sumY[0] + sumY[2] + sumY[3];
		float totalZ = sumZ[3] + sumZ[2] + sumZ[0] + sumZ[1];

		for (int lane = 0; lane < (count & 3); lane++) {
			float x = Memory[offset + lane], y = Memory[offset + 4 + lane], z = Memory[offset + 8 + lane];
			if (x <= min.X) min.X = x;
			if (max.X <= x) max.X = x;
			if (y <= min.Y) min.Y = y;
			if (max.Y <= y) max.Y = y;
			if (z <= min.Z) min.Z = z;
			if (max.Z <= z) max.Z = z;
			totalX += x;
			totalY += y;
			totalZ += z;
		}

		MinBounds = min + Def!.BoundingBoxMin;
		MaxBounds = max + Def.BoundingBoxMax;
		float oneOverCount = 1.0f / count;
		Center = new(totalX * oneOverCount, totalY * oneOverCount, totalZ * oneOverCount);
	}

	static float HorizontalMin(ReadOnlySpan<float> v) {
		float a = v[0] <= v[1] ? v[0] : v[1];
		float b = v[3] <= v[2] ? v[3] : v[2];
		return a <= b ? a : b;
	}

	static float HorizontalMax(ReadOnlySpan<float> v) {
		float a = v[1] <= v[0] ? v[0] : v[1];
		float b = v[2] <= v[3] ? v[3] : v[2];
		return b <= a ? a : b;
	}

	protected void BloatBoundsUsingControlPoint() {
		RecomputeBounds();

		if (SimulatedFrames > 2) {
			for (int i = 0; i <= HighestCP; i++) {
				if ((Def!.ControlPointReadMask & (1UL << i)) == 0)
					continue;

				Vector3 position = ControlPoints[i].Position;
				MinBounds = Vector3.Min(MinBounds, position);
				MaxBounds = Vector3.Max(MaxBounds, position);
			}
		}

		for (ParticleCollection? child = Children; child != null; child = child.Next) {
			MinBounds = Vector3.Min(MinBounds, child.MinBounds);
			MaxBounds = Vector3.Max(MaxBounds, child.MaxBounds);
		}
	}

	public void CalculatePathValues(in PathParameters pathIn, float timeStamp, out Vector3 startPnt, out Vector3 midPnt, out Vector3 endPnt) {
		GetControlPointAtTime(pathIn.StartControlPointNumber, timeStamp, out startPnt);
		GetControlPointAtTime(pathIn.EndControlPointNumber, timeStamp, out endPnt);

		Vector3 delta = endPnt - startPnt;
		midPnt = new(pathIn.MidPoint * delta.X + startPnt.X, pathIn.MidPoint * delta.Y + startPnt.Y, pathIn.MidPoint * delta.Z + startPnt.Z);

		if (pathIn.BulgeControl == 0) {
			float bulge = pathIn.Bulge;
			float bulge2 = bulge + bulge;
			midPnt.X = midPnt.X + RandomFloats[RandomSeed & RANDOM_FLOAT_MASK] * bulge2 - bulge;
			midPnt.Y = midPnt.Y - bulge + RandomFloats[(RandomSeed + 1) & RANDOM_FLOAT_MASK] * bulge2;
			midPnt.Z = RandomFloats[(RandomSeed + 2) & RANDOM_FLOAT_MASK] * bulge2 + (midPnt.Z - bulge);
			return;
		}

		int controlPoint = pathIn.BulgeControl == 2 ? pathIn.EndControlPointNumber : pathIn.StartControlPointNumber;
		Vector3 forward = ControlPoints[controlPoint].ForwardVector;
		float deltaLength = delta.Length();
		float offset = deltaLength > 1e-6f ? 1.0f - MathF.Abs(Vector3.Dot(delta, forward) / deltaLength) : 0.0f;

		float forwardLength = forward.Length();
		if (forwardLength > 1e-6f) {
			float scale = offset * pathIn.Bulge * deltaLength / forwardLength;
			midPnt += forward * scale;
		}
	}

	public void UpdateHitBoxInfo(int controlPointNumber) {
		ModelHitBoxesInfo info = ControlPointHitBoxes[controlPointNumber];
		if (CurTime == info.LastUpdateTime)
			return;

		info.LastUpdateTime = CurTime;
		info.HitBoxes ??= new ModelHitBoxInfo[ModelHitBoxesInfo.MAX_HITBOXES];
		info.PrevBoxes ??= new ModelHitBoxInfo[ModelHitBoxesInfo.MAX_HITBOXES];

		info.PrevLastUpdateTime = info.LastUpdateTime;
		info.NumPrevHitBoxes = info.NumHitBoxes;
		(info.HitBoxes, info.PrevBoxes) = (info.PrevBoxes, info.HitBoxes);
		info.NumHitBoxes = Mgr!.Query().GetControllingObjectHitBoxInfo(this, controlPointNumber, info.HitBoxes);
	}

	IMaterial? PrecachedMaterial() => Def != null && Def.IsPrecached ? Def.Material.Get() : null;

	public bool IsTranslucent() => IsTranslucent_;
	public bool IsTwoPass() => IsTwoPass_;
	public bool IsBatchable() => IsBatchable_;

	public bool UsesPowerOfTwoFrameBufferTexture(bool thisFrame) {
		if (!AnyUsesPowerOfTwoFrameBufferTexture || !thisFrame)
			return AnyUsesPowerOfTwoFrameBufferTexture;
		if (ActiveParticles != 0 && UsesPowerOfTwoFrameBufferTexture_)
			return true;
		for (ParticleCollection? child = Children; child != null; child = child.Next) {
			if (child.SystemContainsParticlesWithBoolSet(c => c.UsesPowerOfTwoFrameBufferTexture_))
				return true;
		}
		return false;
	}

	public bool UsesFullFrameBufferTexture(bool thisFrame) {
		if (!AnyUsesFullFrameBufferTexture || !thisFrame)
			return AnyUsesFullFrameBufferTexture;
		if (ActiveParticles != 0 && UsesFullFrameBufferTexture_)
			return true;
		for (ParticleCollection? child = Children; child != null; child = child.Next) {
			if (child.SystemContainsParticlesWithBoolSet(c => c.UsesFullFrameBufferTexture_))
				return true;
		}
		return false;
	}

	bool SystemContainsParticlesWithBoolSet(Func<ParticleCollection, bool> field) {
		if (ActiveParticles != 0 && field(this))
			return true;
		for (ParticleCollection? child = Children; child != null; child = child.Next) {
			if (child.SystemContainsParticlesWithBoolSet(field))
				return true;
		}
		return false;
	}

	void LabelTextureUsage() {
		if (Def != null) {
			IMaterial material = Def.Material.Get()!;
			UsesPowerOfTwoFrameBufferTexture_ = material.NeedsPowerOfTwoFrameBufferTexture(false);
			UsesFullFrameBufferTexture_ = material.NeedsFullFrameBufferTexture(false);
		}
		for (ParticleCollection? child = Children; child != null; child = child.Next)
			child.LabelTextureUsage();
	}

	bool ComputeUsesPowerOfTwoFrameBufferTexture() {
		if (Def == null)
			return false;
		if (Def.Material.Get()!.NeedsPowerOfTwoFrameBufferTexture(false))
			return true;
		for (ParticleCollection? child = Children; child != null; child = child.Next) {
			if (child.AnyUsesPowerOfTwoFrameBufferTexture)
				return true;
		}
		return false;
	}

	bool ComputeUsesFullFrameBufferTexture() {
		if (Def == null)
			return false;
		if (Def.Material.Get()!.NeedsFullFrameBufferTexture(false))
			return true;
		for (ParticleCollection? child = Children; child != null; child = child.Next) {
			if (child.AnyUsesFullFrameBufferTexture)
				return true;
		}
		return false;
	}

	bool ContainsOpaqueCollections() {
		if (Def == null)
			return false;
		if (PrecachedMaterial()?.IsTranslucent() != true)
			return true;
		for (ParticleCollection? child = Children; child != null; child = child.Next) {
			if (child.ContainsOpaqueCollections())
				return true;
		}
		return false;
	}

	bool ComputeIsTranslucent() {
		if (Def == null)
			return false;
		if (PrecachedMaterial()?.IsTranslucent() == true)
			return true;
		for (ParticleCollection? child = Children; child != null; child = child.Next) {
			if (child.IsTranslucent_)
				return true;
		}
		return false;
	}

	bool ComputeIsBatchable() {
		if (PrecachedMaterial() != null) {
			foreach (ParticleOperatorInstance renderer in Def!.Renderers) {
				if (!renderer.IsBatchable())
					return false;
			}
		}
		for (ParticleCollection? child = Children; child != null; child = child.Next) {
			if (!child.IsBatchable_)
				return false;
		}
		return true;
	}

	bool ComputeRequiresOrderInvariance() {
		if (PrecachedMaterial() != null) {
			foreach (ParticleOperatorInstance renderer in Def!.Renderers) {
				if (renderer.RequiresOrderInvariance())
					return true;
			}
		}
		for (ParticleCollection? child = Children; child != null; child = child.Next) {
			if (child.RequiresOrderInvariance)
				return true;
		}
		return false;
	}

	public int GetRendererCount() => PrecachedMaterial() != null ? Def!.Renderers.Count : 0;
	public ParticleOperatorInstance? GetRenderer(int i) => PrecachedMaterial() != null ? Def!.Renderers[i] : null;
	public Span<byte> GetRendererContext(int i) => PrecachedMaterial() != null ? ContextSpan(Def!.RenderersCtxOffsets[i]) : default;
}
