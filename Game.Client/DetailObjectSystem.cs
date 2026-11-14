using static Game.Client.DetailObjectSystemGlobals;
using static Source.Engine.MatSysVars;

using CommunityToolkit.HighPerformance;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.InteropServices;
using Source.Engine;

namespace Game.Client;

static class DetailObjectSystemGlobals
{
	public static ConVar cl_detaildist = new("cl_detaildist", "1200", 0, "Distance at which detail props are no longer visible");
	public static ConVar cl_detailfade = new("cl_detailfade", "400", 0, "Distance across which detail props fade in");
	public static ConVar cl_detail_max_sway = new("cl_detail_max_sway", "0", FCvar.Archive, "Amplitude of the detail prop sway");
	public static ConVar cl_detail_avoid_radius = new("cl_detail_avoid_radius", "0", FCvar.Archive, "radius around detail sprite to avoid players");
	public static ConVar cl_detail_avoid_force = new("cl_detail_avoid_force", "0", FCvar.Archive, "force with which to avoid players ( in units, percentage of the width of the detail sprite )");
	public static ConVar cl_detail_avoid_recover_speed = new("cl_detail_avoid_recover_speed", "0", FCvar.Archive, "how fast to recover position after avoiding players");
	public static ConVar cl_detail_multiplier = new("cl_detail_multiplier", "1", FCvar.Cheat, "extra details to create");
	public static ConVar cl_fastdetailsprites = new("cl_fastdetailsprites", "1", FCvar.Cheat, "whether to use new detail sprite system");
}

public interface IDetailObjectSystem : IGameSystem
{
	IClientRenderable? GetDetailModel(int idx);

	void BuildDetailObjectRenderLists(in Vector3 viewOrigin);

	void RenderOpaqueDetailObjects(int leafCount, Span<LeafIndex_t> leafList);

	void BeginTranslucentDetailRendering();

	void RenderTranslucentDetailObjects(in Vector3 viewOrigin, in Vector3 viewForward, in Vector3 viewRight, in Vector3 viewUp, int leafCount, Span<LeafIndex_t> leafList);

	void RenderTranslucentDetailObjectsInLeaf(in Vector3 viewOrigin, in Vector3 viewForward, in Vector3 viewRight, in Vector3 viewUp, int leaf, Vector3? closestPoint);
}


public class DetailModelAdvInfo
{
	public InlineArray3<Vector3> AnglesForward;
	public InlineArray3<Vector3> AnglesRight;
	public InlineArray3<Vector3> AnglesUp;

	public Vector3 CurrentAvoid;

	public float SwayYaw;

	public float ShapeSize;

	public int ShapeAngle;
	public float SwayAmount;
}

public struct DetailObjectSystemPerLeafData()
{
	public ushort FirstDetailProp = 0;
	public ushort DetailPropCount = 0;
	public int DetailPropRenderFrame = -1;
}

public struct SprintInfo
{
	public ushort SpriteIndex;
	public Half Scale;
}

public class DetailModel : IClientUnknown, IClientRenderable
{
	public struct LightStyleInfo
	{
		public uint LightStyle;
		public uint LightStyleCount;
	}

	Vector3 Origin;
	QAngle Angles;

	ColorRGBExp32 Color;

	byte Orientation;
	byte Type;
	bool HasLightStyle;
	bool Flipped;

	byte Alpha;

	static readonly Dictionary<DetailModel, LightStyleInfo> LightStylesMap = [];

	Model? Model;
	SprintInfo SpriteInfo;

	DetailModelAdvInfo? AdvInfo;

	public DetailModel() { }

	public bool InitCommon(int index, in Vector3 org, in QAngle angles) {
		MathLib.VectorCopy(org, out Origin);
		MathLib.VectorCopy(angles, out Angles);
		Alpha = 255;

		return true;
	}

	public bool Init(int index, in Vector3 org, in QAngle angles, Model? model, ColorRGBExp32 lighting, int lightstyle, byte lightstylecount, int orientation) {
		Color = lighting;
		if (lightstylecount > 0) {
			HasLightStyle = true;
			if (lightstyle >= 0x1000000 || lightstylecount >= 100)
				Error("Light style overflow\n");
			LightStylesMap[this] = new LightStyleInfo { LightStyle = (uint)lightstyle, LightStyleCount = lightstylecount };
		}
		Orientation = (byte)orientation;
		Type = (byte)DetailPropType.Model;
		Model = model;
		return InitCommon(index, org, angles);
	}

	public bool InitSprite(int index, bool flipped, in Vector3 org, in QAngle angles, ushort spriteIndex, ColorRGBExp32 lighting, int lightstyle, byte lightstylecount, int orientation, float scale, byte type, byte shapeAngle, byte shapeSize, byte swayAmount) {
		Color = lighting;
		if (lightstylecount > 0) {
			HasLightStyle = true;
			if (lightstyle >= 0x1000000 || lightstylecount >= 100)
				Error("Light style overflow\n");
			LightStylesMap[this] = new LightStyleInfo { LightStyle = (uint)lightstyle, LightStyleCount = lightstylecount };
		}
		Orientation = (byte)orientation;
		SpriteInfo.SpriteIndex = spriteIndex;
		Type = type;
		SpriteInfo.Scale = (Half)scale;

		AdvInfo = null;
		Assert(type <= 3);
		if (type == (byte)DetailPropType.ShapeTri || type == (byte)DetailPropType.ShapeCross || swayAmount > 0) {
			Angles = angles;
			InitShapedSprite(shapeAngle, shapeSize, swayAmount);
		}

		Flipped = flipped;
		return InitCommon(index, org, angles);
	}

	public void SetAlpha(byte alpha) => Alpha = alpha;

	public IClientUnknown GetIClientUnknown() => this;
	public ICollideable? GetCollideable() => null;
	public IClientNetworkable? GetClientNetworkable() => null;
	public IClientRenderable? GetClientRenderable() => this;
	public IClientEntity? GetIClientEntity() => null;
	public IClientThinkable? GetClientThinkable() => null;

	public int GetBody() => 0;
	public ref readonly Vector3 GetRenderOrigin() => ref Origin;
	public ref readonly QAngle GetRenderAngles() => ref Angles;
	static Matrix3x4 mat;
	public ref readonly Matrix3x4 RenderableToWorldTransform() {
		MathLib.AngleMatrix(GetRenderAngles(), GetRenderOrigin(), out mat);
		return ref mat;
	}
	public bool ShouldDraw() => clientMode.ShouldDrawDetailObjects();
	public bool IsTwoPass() => false;
	public void OnThreadedDrawSetup() { }
	public bool IsTransparent() => Alpha < 255 || modelinfo.IsTranslucent(Model);
	public Model? GetModel() => Model;
	public int DrawModel(StudioFlags flags) {
		if (Alpha == 0 || Model == null)
			return 0;

		int drawn = modelrender.DrawModel(flags, this, MODEL_INSTANCE_INVALID, -1, Model, Origin, Angles, 0, 0, 0);
		return drawn;
	}
	public void ComputeFxBlend() { }
	public int GetFxBlend() => Alpha;
	public bool SetupBones(Span<Matrix3x4> boneToWorldOut, int maxBones, int boneMask, TimeUnit_t currentTime) {
		if (Model == null)
			return false;

		ref readonly QAngle vRenderAngles = ref GetRenderAngles();
		ref readonly Vector3 vRenderOrigin = ref GetRenderOrigin();
		MathLib.AngleMatrix(vRenderAngles, out Matrix3x4 parentTransform);
		parentTransform[0, 3] = vRenderOrigin.X;
		parentTransform[1, 3] = vRenderOrigin.Y;
		parentTransform[2, 3] = vRenderOrigin.Z;

		StudioHeader studioHdr = modelinfo.GetStudiomodel(Model)!;
		for (int i = 0; i < studioHdr.NumBones; i++)
			boneToWorldOut[i] = parentTransform;

		return true;
	}
	public void SetupWeights(Span<Matrix3x4> boneToWorld, Span<float> flexWeights, Span<float> flexDelayedWeights) { }
	public bool UsesFlexDelayedWeights() => false;
	public void DoAnimationEvents() { }
	public void GetRenderBounds(out Vector3 mins, out Vector3 maxs) {
		ModelType modelType = modelinfo.GetModelType(Model);
		if (modelType == ModelType.Studio || modelType == ModelType.Brush)
			modelinfo.GetModelRenderBounds(GetModel(), out mins, out maxs);
		else {
			mins = new(0, 0, 0);
			maxs = new(0, 0, 0);
		}
	}
	public IPVSNotify? GetPVSNotifyInterface() => null;
	public void GetRenderBoundsWorldspace(out Vector3 mins, out Vector3 maxs) => IClientLeafSystemEngine.DefaultRenderBoundsWorldspace(this, out mins, out maxs);
	public bool ShouldReceiveProjectedTextures(ShadowFlags flags) => false;
	public bool GetShadowCastDistance(ref float dist, ShadowType shadowType) { return false; }
	public bool GetShadowCastDirection(ref Vector3 direction, ShadowType shadowType) { return false; }
	public bool UsesPowerOfTwoFrameBufferTexture() => false;
	public bool UsesFullFrameBufferTexture() => false;
	public bool IgnoresZBuffer() => false;
	public bool LODTest() => true;
	public ClientShadowHandle_t GetShadowHandle() => unchecked((ClientShadowHandle_t)~0);
	public ref ClientRenderHandle_t RenderHandle() {
		AssertMsg(false, "CDetailModel has no render handle");
		throw new NotSupportedException();
	}
	public void GetShadowRenderBounds(out Vector3 mins, out Vector3 maxs, ShadowType shadowType) => GetRenderBounds(out mins, out maxs);
	public bool IsShadowDirty() => false;
	public void MarkShadowDirty(bool dirty) { }
	public IClientRenderable? GetShadowParent() => null;
	public IClientRenderable? FirstShadowChild() => null;
	public IClientRenderable? NextShadowPeer() => null;
	public ShadowType ShadowCastType() => ShadowType.None;
	public void CreateModelInstance() { }
	public ModelInstanceHandle_t GetModelInstance() => MODEL_INSTANCE_INVALID;
	public int LookupAttachment(ReadOnlySpan<char> attachmentName) => -1;
	public bool GetAttachment(int number, out Matrix3x4 matrix) {
		matrix = RenderableToWorldTransform();
		return true;
	}
	public bool GetAttachment(int number, out Vector3 origin, out QAngle angles) {
		origin = Origin;
		angles = Angles;
		return true;
	}
	public Span<float> GetRenderClipPlane() => null;
	public int GetSkin() => 0;
	public void RecordToolMessage() { }

	public void GetColorModulation(Span<float> color) {
		if (mat_fullbright.GetInt() == 1) {
			color[0] = color[1] = color[2] = 1.0f;
			return;
		}

		Vector3 normal = new(1, 0, 0);
		engine.ComputeDynamicLighting(Origin, normal, out Vector3 tmp);

		float val = engine.LightStyleValue(0);
		color[0] = tmp.X + val * MathLib.TexLightToLinear(Color.R, Color.Exponent);
		color[1] = tmp.Y + val * MathLib.TexLightToLinear(Color.G, Color.Exponent);
		color[2] = tmp.Z + val * MathLib.TexLightToLinear(Color.B, Color.Exponent);

		if (HasLightStyle) {
			if (LightStylesMap.TryGetValue(this, out LightStyleInfo info)) {
				int nLightStyles = (int)info.LightStyleCount;
				int iLightStyle = (int)info.LightStyle;
				for (int i = 0; i < nLightStyles; ++i) {
					ref DetailPropLightstylesLump lighting = ref ((DetailObjectSystem)DetailObjectSystem.GetDetailObjectSystem()).DetailLighting(iLightStyle + i);
					val = engine.LightStyleValue(lighting.Style);
					if (val != 0) {
						color[0] += val * MathLib.TexLightToLinear(lighting.Lighting.R, lighting.Lighting.Exponent);
						color[1] += val * MathLib.TexLightToLinear(lighting.Lighting.G, lighting.Lighting.Exponent);
						color[2] += val * MathLib.TexLightToLinear(lighting.Lighting.B, lighting.Lighting.Exponent);
					}
				}
			}
		}

		engine.LinearToGamma(color, color);
	}

	public void ComputeAngles() {
		switch (Orientation) {
			case 0:
				break;

			case 1: {
					MathLib.VectorSubtract(CurrentViewOrigin(), Origin, out Vector3 vecDir);
					MathLib.VectorAngles(vecDir, out Angles);
				}
				break;

			case 2: {
					MathLib.VectorSubtract(CurrentViewOrigin(), Origin, out Vector3 vecDir);
					vecDir.Z = 0.0f;
					MathLib.VectorAngles(vecDir, out Angles);
				}
				break;
		}
	}

	public void DrawSprite(ref MeshBuilder meshBuilder) {
		switch ((DetailPropType)Type) {
			case DetailPropType.ShapeCross:
				DrawTypeShapeCross(ref meshBuilder);
				break;

			case DetailPropType.ShapeTri:
				DrawTypeShapeTri(ref meshBuilder);
				break;

			case DetailPropType.Sprite:
				DrawTypeSprite(ref meshBuilder);
				break;

			default:
				Assert(false);
				break;
		}
	}

	public int QuadsToDraw() => QuadCount[Type];

	public void DrawTypeSprite(ref MeshBuilder meshBuilder) {
		Assert(Type == (byte)DetailPropType.Sprite);

		Span<float> vecColor = stackalloc float[3];
		GetColorModulation(vecColor);

		Color color = new((byte)(vecColor[0] * 255.0f), (byte)(vecColor[1] * 255.0f), (byte)(vecColor[2] * 255.0f), Alpha);

		ref DetailPropSpriteDict dict = ref ((DetailObjectSystem)DetailObjectSystem.GetDetailObjectSystem()).DetailSpriteDict(SpriteInfo.SpriteIndex);

		MathLib.AngleVectors(Angles, out _, out Vector3 dx, out Vector3 dy);

		float scale = (float)SpriteInfo.Scale;
		MathLib.Vector2DMultiply(dict.UL, scale, out Vector2 ul);
		MathLib.Vector2DMultiply(dict.LR, scale, out Vector2 lr);

		UpdatePlayerAvoid();

		Vector3 vecSway = Vector3.Zero;

		if (AdvInfo != null) {
			vecSway = AdvInfo.CurrentAvoid * (float)SpriteInfo.Scale;
			float flSwayAmplitude = AdvInfo.SwayAmount * cl_detail_max_sway.GetFloat();
			if (flSwayAmplitude > 0) {
				vecSway += dx * MathF.Sin((float)(gpGlobals.CurTime + Origin.X)) * flSwayAmplitude;
			}
		}

		MathLib.VectorMA(Origin, ul.X, dx, out Vector3 vecOrigin);
		MathLib.VectorMA(vecOrigin, ul.Y, dy, out vecOrigin);
		dx *= lr.X - ul.X;
		dy *= lr.Y - ul.Y;

		Vector2 texul = dict.TexUL;
		Vector2 texlr = dict.TexLR;

		if (!Flipped) {
			texul.X = dict.TexLR.X;
			texlr.X = dict.TexUL.X;
		}

		meshBuilder.Position3fv(vecOrigin + vecSway);
		meshBuilder.Color4ubv(color);
		meshBuilder.TexCoord2fv(0, texul);
		meshBuilder.AdvanceVertex();

		vecOrigin += dy;
		meshBuilder.Position3fv(vecOrigin);
		meshBuilder.Color4ubv(color);
		meshBuilder.TexCoord2f(0, texul.X, texlr.Y);
		meshBuilder.AdvanceVertex();

		vecOrigin += dx;
		meshBuilder.Position3fv(vecOrigin);
		meshBuilder.Color4ubv(color);
		meshBuilder.TexCoord2fv(0, texlr);
		meshBuilder.AdvanceVertex();

		vecOrigin -= dy;
		meshBuilder.Position3fv(vecOrigin + vecSway);
		meshBuilder.Color4ubv(color);
		meshBuilder.TexCoord2f(0, texlr.X, texul.Y);
		meshBuilder.AdvanceVertex();
	}

	public void DrawTypeShapeCross(ref MeshBuilder meshBuilder) {
		Assert(Type == (byte)DetailPropType.ShapeCross);

		Span<float> vecColor = stackalloc float[3];
		GetColorModulation(vecColor);

		Span<byte> color =
		[
			(byte)(vecColor[0] * 255.0f),
			(byte)(vecColor[1] * 255.0f),
			(byte)(vecColor[2] * 255.0f),
			Alpha,
		];
		ref DetailPropSpriteDict dict = ref ((DetailObjectSystem)DetailObjectSystem.GetDetailObjectSystem()).DetailSpriteDict(SpriteInfo.SpriteIndex);

		Vector2 texul = dict.TexUL;
		Vector2 texlr = dict.TexLR;

		if (Model == null) {
			texul.X = dict.TexLR.X;
			texlr.X = dict.TexUL.X;
		}

		Vector2 texumid, texlmid;
		texumid.Y = texul.Y;
		texlmid.Y = texlr.Y;
		texumid.X = texlmid.X = (texul.X + texlr.X) / 2;

		Vector2 texll;
		texll.X = texul.X;
		texll.Y = texlr.Y;

		float scale = (float)SpriteInfo.Scale;
		Vector2 ul = dict.UL * scale;
		Vector2 lr = dict.LR * scale;

		float sizeX = (lr.X - ul.X) / 2;
		float sizeY = lr.Y - ul.Y;

		UpdatePlayerAvoid();

		Vector3 vecSway = AdvInfo!.CurrentAvoid * sizeX * 2;
		float swayAmplitude = AdvInfo.SwayAmount * cl_detail_max_sway.GetFloat();
		if (swayAmplitude > 0) {
			vecSway += Util.YawToVector(AdvInfo.SwayYaw) * MathF.Sin((float)(gpGlobals.CurTime + Origin.X)) * swayAmplitude;
		}

		MathLib.VectorMA(Origin, ul.Y, AdvInfo.AnglesUp[0], out Vector3 vecOrigin);

		Vector3 forward = AdvInfo.AnglesForward[0] * sizeX;
		Vector3 right = AdvInfo.AnglesRight[0] * sizeX;
		Vector3 up = AdvInfo.AnglesUp[0] * sizeY;

		Vector3 viewOffset = CurrentViewOrigin() - Origin;
		bool frontSide = Vector3.Dot(forward, viewOffset) > 0;
		bool rightSide = Vector3.Dot(right, viewOffset) > 0;
		int branch = frontSide ? (rightSide ? 0 : 3) : (rightSide ? 1 : 2);

		int drawn = 0;
		while (drawn < 4) {
			switch (branch) {
				case 0:
					DrawSwayingQuad(ref meshBuilder, vecOrigin, vecSway, texumid, texlr, color, -forward, up);
					break;
				case 1:
					DrawSwayingQuad(ref meshBuilder, vecOrigin, vecSway, texumid, texll, color, -right, up);
					break;
				case 2:
					DrawSwayingQuad(ref meshBuilder, vecOrigin, vecSway, texumid, texll, color, forward, up);
					break;
				case 3:
					DrawSwayingQuad(ref meshBuilder, vecOrigin, vecSway, texumid, texlr, color, right, up);
					break;
			}

			drawn++;
			branch++;
			if (branch > 3)
				branch = 0;
		}
	}

	public void DrawTypeShapeTri(ref MeshBuilder meshBuilder) {
		Assert(Type == (byte)DetailPropType.ShapeTri);

		Span<float> vecColor = stackalloc float[3];
		GetColorModulation(vecColor);

		Span<byte> color =
		[
			(byte)(vecColor[0] * 255.0f),
			(byte)(vecColor[1] * 255.0f),
			(byte)(vecColor[2] * 255.0f),
			Alpha,
		];
		ref DetailPropSpriteDict dict = ref ((DetailObjectSystem)DetailObjectSystem.GetDetailObjectSystem()).DetailSpriteDict(SpriteInfo.SpriteIndex);

		Vector2 texul = dict.TexUL;
		Vector2 texlr = dict.TexLR;

		if (Model == null) {
			texul.X = dict.TexLR.X;
			texlr.X = dict.TexUL.X;
		}

		float scale = (float)SpriteInfo.Scale;
		Vector2 ul = dict.UL * scale;
		Vector2 lr = dict.LR * scale;

		Vector3 viewOffset = CurrentViewOrigin() - Origin;

		bool outsideA = Vector3.Dot(AdvInfo!.AnglesForward[0], viewOffset) > 0;
		bool outsideB = Vector3.Dot(AdvInfo.AnglesForward[1], viewOffset) > 0;
		bool outsideC = Vector3.Dot(AdvInfo.AnglesForward[2], viewOffset) > 0;

		int branch = 0;
		if (outsideA && !outsideB)
			branch = 1;
		else if (outsideB && !outsideC)
			branch = 2;

		float height = lr.Y - ul.Y;
		float width = lr.X - ul.X;

		Vector3 vecHeight, vecWidth;

		UpdatePlayerAvoid();

		Vector3 vecSwayYaw = Util.YawToVector(AdvInfo.SwayYaw);
		float swayAmplitude = AdvInfo.SwayAmount * cl_detail_max_sway.GetFloat();

		int drawn = 0;
		while (drawn < 3) {
			vecHeight = AdvInfo.AnglesUp[branch] * height;
			vecWidth = AdvInfo.AnglesRight[branch] * width;

			MathLib.VectorMA(Origin, ul.X, AdvInfo.AnglesRight[branch], out Vector3 vecOrigin);
			MathLib.VectorMA(vecOrigin, ul.Y, AdvInfo.AnglesUp[branch], out vecOrigin);
			MathLib.VectorMA(vecOrigin, AdvInfo.ShapeSize * width, AdvInfo.AnglesForward[branch], out vecOrigin);

			Vector3 vecSway = (AdvInfo.CurrentAvoid * width) +
				vecSwayYaw * MathF.Sin((float)(gpGlobals.CurTime + Origin.X + branch)) * swayAmplitude;

			DrawSwayingQuad(ref meshBuilder, vecOrigin, vecSway, texul, texlr, color, vecWidth, vecHeight);

			drawn++;
			branch++;
			if (branch > 2)
				branch = 0;
		}
	}

	public void UpdatePlayerAvoid() {
		float force = cl_detail_avoid_force.GetFloat();

		if (force < 0.1)
			return;

		if (AdvInfo == null)
			return;

		float radius = cl_detail_avoid_radius.GetFloat();
		float recoverSpeed = cl_detail_avoid_recover_speed.GetFloat();

		Vector3 avoidance;
		C_BaseEntity? ent;

		float maxForce = 0;
		Vector3 maxAvoid = new(0, 0, 0);

		PlayerEnumerator avoid = new(radius, Origin);
		partition.EnumerateElementsInSphere((SpatialPartitionListMask_t)PartitionListMask.ClientSolidEdicts, Origin, radius, false, ref avoid);

		int c = avoid.GetObjectCount();
		for (int i = 0; i < c + 1; i++) {
			if (i == c) {
				ent = C_BasePlayer.GetLocalPlayer();
				if (ent == null) continue;
			}
			else
				ent = avoid.GetObject(i);

			avoidance = Origin - ent!.GetAbsOrigin();
			avoidance.Z = 0;

			float dist = avoidance.Length2D();

			if (dist > radius)
				continue;

			float forceScale = (float)MathLib.RemapValClamped(dist, 0, radius, force, 0.0);

			if (forceScale > maxForce) {
				maxForce = forceScale;
				avoidance = Vector3.Normalize(avoidance);
				avoidance *= maxForce;
				maxAvoid = avoidance;
			}
		}

		if (maxAvoid.Length2D() > AdvInfo.CurrentAvoid.Length2D())
			recoverSpeed = 10;

		AdvInfo.CurrentAvoid[0] = MathLib.Approach(maxAvoid[0], AdvInfo.CurrentAvoid[0], recoverSpeed);
		AdvInfo.CurrentAvoid[1] = MathLib.Approach(maxAvoid[1], AdvInfo.CurrentAvoid[1], recoverSpeed);
		AdvInfo.CurrentAvoid[2] = MathLib.Approach(maxAvoid[2], AdvInfo.CurrentAvoid[2], recoverSpeed);
	}

	public void InitShapedSprite(byte shapeAngle, byte shapeSize, byte swayAmount) {
		Assert(AdvInfo == null);
		AdvInfo = new DetailModelAdvInfo();
		Assert(AdvInfo != null);

		if (AdvInfo != null) {
			AdvInfo.ShapeAngle = shapeAngle;
			AdvInfo.SwayAmount = swayAmount / 255.0f;
			AdvInfo.ShapeSize = shapeSize / 255.0f;
			AdvInfo.CurrentAvoid = Vector3.Zero;
			AdvInfo.SwayYaw = random.RandomFloat(0, 180);
		}

		switch ((DetailPropType)Type) {
			case DetailPropType.ShapeTri:
				InitShapeTri();
				break;

			case DetailPropType.ShapeCross:
				InitShapeCross();
				break;

			default:
				break;
		}
	}
	public void InitShapeTri() {
		MathLib.AngleMatrix(Angles, out Matrix3x4 matrix);

		for (int i = 0; i < 3; i++) {
			QAngle anglesRotated = new(AdvInfo!.ShapeAngle, i * 120, 0);

			MathLib.AngleVectors(anglesRotated, out Vector3 rotForward, out Vector3 rotRight, out Vector3 rotUp);

			MathLib.VectorRotate(rotForward, matrix, out AdvInfo.AnglesForward[i]);
			MathLib.VectorRotate(rotRight, matrix, out AdvInfo.AnglesRight[i]);
			MathLib.VectorRotate(rotUp, matrix, out AdvInfo.AnglesUp[i]);
		}
	}
	public void InitShapeCross() {
		MathLib.AngleVectors(Angles, out AdvInfo!.AnglesForward[0], out AdvInfo.AnglesRight[0], out AdvInfo.AnglesUp[0]);
	}

	public void DrawSwayingQuad(ref MeshBuilder meshBuilder, Vector3 vecOrigin, Vector3 vecSway, Vector2 texul, Vector2 texlr, Span<byte> color, Vector3 width, Vector3 height) {
		Color col = new(color[0], color[1], color[2], color[3]);

		meshBuilder.Position3fv(vecOrigin + vecSway);
		meshBuilder.TexCoord2fv(0, texul);
		meshBuilder.Color4ubv(col);
		meshBuilder.AdvanceVertex();

		vecOrigin += height;
		meshBuilder.Position3fv(vecOrigin);
		meshBuilder.TexCoord2f(0, texul.X, texlr.Y);
		meshBuilder.Color4ubv(col);
		meshBuilder.AdvanceVertex();

		vecOrigin += width;
		meshBuilder.Position3fv(vecOrigin);
		meshBuilder.TexCoord2fv(0, texlr);
		meshBuilder.Color4ubv(col);
		meshBuilder.AdvanceVertex();

		vecOrigin -= height;
		meshBuilder.Position3fv(vecOrigin + vecSway);
		meshBuilder.TexCoord2f(0, texlr.X, texul.Y);
		meshBuilder.Color4ubv(col);
		meshBuilder.AdvanceVertex();
	}

	public int GetDetailType() => Type;
	public byte GetAlpha() => Alpha;

	public bool IsDetailModelTranslucent() => Type >= (byte)DetailPropType.Sprite || modelinfo.IsTranslucent(GetModel());

	public void SetRefEHandle(in BaseHandle handle) => Assert(false);
	public ref readonly BaseHandle GetRefEHandle() {
		Assert(false);
		throw new NotSupportedException();
	}

	static readonly int[] QuadCount = [
		0, //DETAIL_PROP_TYPE_MODEL
		1, //DETAIL_PROP_TYPE_SPRITE
		4, //DETAIL_PROP_TYPE_SHAPE_CROSS
		3 //DETAIL_PROP_TYPE_SHAPE_TRI
	];
}

public struct DetailPropSpriteDict
{
	public Vector2 UL;
	public Vector2 LR;
	public Vector2 TexUL;
	public Vector2 TexLR;
}

public struct FastSpriteX4
{
	public FourVectors Pos;
	public fltx4 HalfWidth;
	public fltx4 Height;
	public InlineArray4<InlineArray4<byte>> RGBColor;
	public InlineArray4<DetailPropSpriteDict> SpriteDefs;

	public void ReplicateFirstEntryToOthers() {
		HalfWidth = MathLib.ReplicateX4(MathLib.SubFloat(ref HalfWidth, 0));
		Height = MathLib.ReplicateX4(MathLib.SubFloat(ref Height, 0));

		for (int i = 1; i < 4; i++)
			for (int j = 0; j < 4; j++) {
				RGBColor[i][j] = RGBColor[0][j];
			}
		Pos.x = new Vector4(MathLib.SubFloat(ref Pos.x, 0));
		Pos.y = new Vector4(MathLib.SubFloat(ref Pos.y, 0));
		Pos.z = new Vector4(MathLib.SubFloat(ref Pos.z, 0));
	}
}

public struct FastSpriteQuadBuildoutBufferX4
{
	public InlineArray4<FourVectors> Coords;
	public InlineArray4<InlineArray4<byte>> RGBColor;
	public fltx4 Alpha;
	public InlineArray4<DetailPropSpriteDict> SpriteDefs;
}

public struct FastSpriteQuadBuildoutBufferNonSIMDView
{
	public InlineArray4<float> X0, Y0, Z0;
	public InlineArray4<float> X1, Y1, Z1;
	public InlineArray4<float> X2, Y2, Z2;
	public InlineArray4<float> X3, Y3, Z3;

	public InlineArray4<InlineArray4<byte>> RGBColor;
	public InlineArray4<float> Alpha;
	public InlineArray4<nint> SpriteDefs;
}

public class FastDetailLeafSpriteList : ClientLeafSubSystemData
{
	internal int NumSprites;
	internal int NumSIMDSprites;
	internal FastSpriteX4[]? Sprites;
	internal int StartSIMDSprite;

	internal int NumPendingSprites;
	internal int StartSpriteIndex;

	public FastDetailLeafSpriteList() {
		NumPendingSprites = 0;
		StartSpriteIndex = 0;
	}
}

public class DetailObjectSystem : IDetailObjectSystem, ISpatialLeafEnumerator
{
	struct DetailModelDict
	{
		public Model? Model;
	}

	struct EnumContext
	{
		public Vector3 ViewOrigin;
		public int BuildWorldListNumber;
	}

	struct SortInfo
	{
		public int Index;
		public float Distance;
	}

	readonly List<DetailModelDict> DetailObjectDict = new(32);
	readonly List<DetailModel> DetailObjects = [];
	readonly List<DetailPropSpriteDict> DetailSpriteDictList = new(32);
	readonly List<DetailPropSpriteDict> DetailSpriteDictFlipped = new(32);
	readonly List<DetailPropLightstylesLump> DetailLightingList = [];
	FastSpriteX4[]? FastSpriteData;

	MaterialReference DetailSpriteMaterial = new();
	MaterialReference DetailWireframeMaterial = new();

	int SpriteCount;
	int FirstSprite;
	int SortedLeaf;
	int SortedFastLeaf;
	SortInfo[]? SortInfos;
	SortInfo[]? FastSortInfos;
	FastSpriteQuadBuildoutBufferX4[]? BuildoutBuffer;

	float DefaultFadeStart;
	float DefaultFadeEnd;

	float CurMaxSqDist;
	float CurFadeSqDist;
	float CurFalloffFactor;

	const string DETAIL_SPRITE_MATERIAL = "detail/detailsprites";

	static bool DetailObjectIsFastSprite(in DetailObjectLump lump) {
		return cl_fastdetailsprites.GetInt() != 0
			&& lump.Type == (byte)DetailPropType.Sprite
			&& lump.LightStyleCount == 0
			&& lump.Orientation == 2
			&& lump.ShapeAngle == 0
			&& lump.ShapeSize == 0
			&& lump.SwayAmount == 0;
	}

	static Vector3 RandomVector(float min, float max) => new(random.RandomFloat(min, max), random.RandomFloat(min, max), random.RandomFloat(min, max));

	static readonly DetailObjectSystem s_DetailObjectSystem = new();
	public static IDetailObjectSystem GetDetailObjectSystem() => s_DetailObjectSystem;

	public ReadOnlySpan<char> Name() => "DetailObjectSystem";

	public DetailObjectSystem() { }

	public bool IsPerFrame() => false;

	public bool Init() {
		DefaultFadeStart = cl_detailfade.GetFloat();
		DefaultFadeEnd = cl_detaildist.GetFloat();
		return true;
	}
	public void PostInit() { }
	public void Shutdown() { }

	public void LevelInitPreEntity() {
		DetailSpriteMaterial.Init("detail/detailsprites", MaterialDefines.TEXTURE_GROUP_OTHER);
		DetailWireframeMaterial.Init("debug/debugspritewireframe", MaterialDefines.TEXTURE_GROUP_OTHER);

		if (engine.GameLumpVersion((int)GameLump.DetailProps) < 4) {
			Warning("Map uses old detail prop file format.. ignoring detail props\n");
			return;
		}

		int size = engine.GameLumpSize((int)GameLump.DetailProps);
		byte[] fileMemory = new byte[size];
		if (engine.LoadGameLump((int)GameLump.DetailProps, fileMemory)) {
			using MemoryStream buf = new(fileMemory);
			UnserializeModelDict(buf);

			switch (engine.GameLumpVersion((int)GameLump.DetailProps)) {
				case 4:
					UnserializeDetailSprites(buf);
					UnserializeModels(buf);
					break;
			}
		}

		if (DetailObjects.Count != 0 || DetailSpriteDictList.Count != 0) {
			IMaterial pMat = DetailSpriteMaterial.Get()!;
			float ratio = (float)pMat.GetMappingWidth() / pMat.GetMappingHeight();
			if (ratio > 1.0) {
				Span<DetailPropSpriteDict> spriteDict = DetailSpriteDictList.AsSpan();
				Span<DetailPropSpriteDict> spriteDictFlipped = DetailSpriteDictFlipped.AsSpan();
				for (int i = 0; i < spriteDict.Length; i++) {
					spriteDict[i].TexUL.Y *= ratio;
					spriteDict[i].TexLR.Y *= ratio;
					spriteDictFlipped[i].TexUL.Y *= ratio;
					spriteDictFlipped[i].TexLR.Y *= ratio;
				}
			}
		}

		int detailPropLightingLump;
		if (Singleton<IMaterialSystemHardwareConfig>().GetHDRType() != HDRType.None)
			detailPropLightingLump = (int)GameLump.DetailPropLightingHDR;
		else
			detailPropLightingLump = (int)GameLump.DetailPropLighting;
		size = engine.GameLumpSize(detailPropLightingLump);

		fileMemory = new byte[size];
		if (engine.LoadGameLump(detailPropLightingLump, fileMemory)) {
			using MemoryStream buf = new(fileMemory);
			UnserializeModelLighting(buf);
		}
	}

	public void LevelInitPostEntity() {
		ReadOnlySpan<char> detailSpriteMaterial = DETAIL_SPRITE_MATERIAL;
		C_World? world = C_World.GetClientWorldEntity();
		if (world != null && !world.GetDetailSpriteMaterial().IsEmpty)
			detailSpriteMaterial = world.GetDetailSpriteMaterial();
		DetailSpriteMaterial.Init(detailSpriteMaterial, MaterialDefines.TEXTURE_GROUP_OTHER);

		if (C_EnvDetailController.GetDetailController() != null) {
			cl_detailfade.SetValue(Math.Min(DefaultFadeStart, C_EnvDetailController.GetDetailController()!.FadeStartDist));
			cl_detaildist.SetValue(Math.Min(DefaultFadeEnd, C_EnvDetailController.GetDetailController()!.FadeEndDist));
		}
		else {
			cl_detailfade.SetValue(DefaultFadeStart);
			cl_detaildist.SetValue(DefaultFadeEnd);
		}
	}
	public void LevelShutdownPreClearSteamAPIContext() { }
	public void LevelShutdownPreEntity() {
		DetailObjects.Clear();
		DetailObjectDict.Clear();
		DetailSpriteDictList.Clear();
		DetailSpriteDictFlipped.Clear();
		DetailLightingList.Clear();
		// DetailSpriteMaterial.Shutdown();
		FastSpriteData = null;
		FreeSortBuffers();
	}
	public void LevelShutdownPostEntity() {
		// DetailWireframeMaterial.Shutdown();
	}

	public void OnSave() { }
	public void OnRestore() { }
	public void SafeRemoveIfDesired() { }

	public IClientRenderable? GetDetailModel(int idx) {
		if (DetailObjects[idx].GetDetailType() != (int)DetailPropType.Model)
			return null;

		return DetailObjects[idx];
	}


	public void BuildDetailObjectRenderLists(in Vector3 viewOrigin) {
		if (!clientMode.ShouldDrawDetailObjects() || r_DrawDetailProps.GetInt() == 0)
			return;

		if (FastSpriteData == null && DetailObjects.Count == 0)
			return;

		EnumContext ctx = new() {
			ViewOrigin = viewOrigin,
			BuildWorldListNumber = view.BuildWorldListsNumber()
		};

		for (int i = DetailObjectDict.Count; --i >= 0;) {
			if (modelinfo.ModelHasMaterialProxy(DetailObjectDict[i].Model))
				modelinfo.RecomputeTranslucency(DetailObjectDict[i].Model, 0, 0, null);
		}

		float factor = 1.0f;
		C_BasePlayer? local = C_BasePlayer.GetLocalPlayer();
		if (local != null)
			factor = local.GetFOVDistanceAdjustFactor();

		CurMaxSqDist = cl_detaildist.GetFloat() * cl_detaildist.GetFloat();
		CurFadeSqDist = cl_detaildist.GetFloat() - cl_detailfade.GetFloat();

		CurMaxSqDist /= factor;
		CurFadeSqDist /= factor;

		if (CurFadeSqDist > 0)
			CurFadeSqDist *= CurFadeSqDist;
		else
			CurFadeSqDist = 0;
		CurFadeSqDist = Math.Min(CurFadeSqDist, CurMaxSqDist - 1);
		CurFalloffFactor = 255.0f / (CurMaxSqDist - CurFadeSqDist);

		ISpatialQuery query = engine.GetBSPTreeQuery()!;
		GCHandle handle = GCHandle.Alloc(ctx);
		try {
			ISpatialLeafEnumerator queryRef = this;
			query.EnumerateLeavesInSphere(CurrentViewOrigin(), cl_detaildist.GetFloat(), ref queryRef, GCHandle.ToIntPtr(handle));
		}
		finally {
			handle.Free();
		}
	}

	public void RenderOpaqueDetailObjects(int leafCount, Span<LeafIndex_t> leafList) { }

	public void RenderTranslucentDetailObjects(in Vector3 viewOrigin, in Vector3 viewForward, in Vector3 viewRight, in Vector3 viewUp, int leafCount, Span<LeafIndex_t> leafList) {
		if (leafCount == 0)
			return;

		Assert(SpriteCount == FirstSprite);

		RenderFastSprites(viewOrigin, viewForward, viewRight, viewUp, leafCount, leafList);

		int quadCount = CountSpriteQuadsInLeafList(leafCount, leafList);
		if (quadCount == 0)
			return;

		using MatRenderContextPtr renderContext = new(materials);
		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		IMaterial material = DetailSpriteMaterial.Get()!;
		if (GLRSurfGlobals.ShouldDrawInWireFrameMode() || r_DrawDetailProps.GetInt() == 2)
			material = DetailWireframeMaterial.Get()!;

		MeshBuilder meshBuilder = new();
		IMesh mesh = renderContext.GetDynamicMesh(true, null, null, material);

		int maxVerts = 32768 /*GetMaxToRender*/, maxIndices = 32768;
		int maxQuadsToDraw = maxIndices / 6;
		if (maxQuadsToDraw > maxVerts / 4)
			maxQuadsToDraw = maxVerts / 4;

		if (maxQuadsToDraw == 0)
			return;

		int quadsToDraw = quadCount;
		if (quadsToDraw > maxQuadsToDraw)
			quadsToDraw = maxQuadsToDraw;

		meshBuilder.Begin(mesh, MaterialPrimitiveType.Quads, quadsToDraw);

		int quadsDrawn = 0;
		for (int i = 0; i < leafCount; ++i) {
			int leaf = leafList[i];

			clientLeafSystem.GetDetailObjectsInLeaf(leaf, out int firstDetailObject, out int detailObjectCount);

			Span<SortInfo> sortInfo = SortInfos!;
			int count = SortSpritesBackToFront(leaf, viewOrigin, viewForward, sortInfo);

			for (int j = 0; j < count; ++j) {
				DetailModel model = DetailObjects[sortInfo[j].Index];
				int quadsInModel = model.QuadsToDraw();

				if (quadsDrawn + quadsInModel > quadsToDraw) {
					meshBuilder.End();
					mesh.Draw();

					quadCount -= quadsDrawn;
					quadsToDraw = quadCount;
					if (quadsToDraw > maxQuadsToDraw)
						quadsToDraw = maxQuadsToDraw;

					meshBuilder.Begin(mesh, MaterialPrimitiveType.Quads, quadsToDraw);
					quadsDrawn = 0;
				}

				model.DrawSprite(ref meshBuilder);

				quadsDrawn += quadsInModel;
			}
		}

		meshBuilder.End();
		mesh.Draw();

		renderContext.PopMatrix();
	}

	public void RenderTranslucentDetailObjectsInLeaf(in Vector3 viewOrigin, in Vector3 viewForward, in Vector3 viewRight, in Vector3 viewUp, int leaf, Vector3? closestPoint) {
		RenderFastTranslucentDetailObjectsInLeaf(viewOrigin, viewForward, viewRight, viewUp, leaf, closestPoint);

		if (SortedLeaf != leaf) {
			SortedLeaf = leaf;
			SpriteCount = 0;
			FirstSprite = 0;

			Span<LeafIndex_t> leafIndex = [(LeafIndex_t)leaf];
			int spriteCount = CountSpritesInLeafList(1, leafIndex);
			if (spriteCount == 0)
				return;

			SpriteCount = SortSpritesBackToFront(leaf, viewOrigin, viewForward, SortInfos!);
			Assert(SpriteCount <= spriteCount);
		}

		if (SpriteCount == FirstSprite)
			return;

		float minDistance = 0.0f;
		if (closestPoint.HasValue) {
			MathLib.VectorSubtract(closestPoint.Value, viewOrigin, out Vector3 vecDelta);
			minDistance = vecDelta.LengthSquared();
		}

		if (SortInfos![FirstSprite].Distance < minDistance)
			return;

		IMatRenderContext renderContext = materials.GetRenderContext();
		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		IMaterial material = DetailSpriteMaterial.Get()!;
		if (GLRSurfGlobals.ShouldDrawInWireFrameMode() || r_DrawDetailProps.GetInt() == 2)
			material = DetailWireframeMaterial.Get()!;

		MeshBuilder meshBuilder = new();
		IMesh mesh = renderContext.GetDynamicMesh(true, null, null, material);

		int maxVerts = 32768, maxIndices = 32768; /*GetMaxToRender*/

		int quadCount = (SpriteCount - FirstSprite) * 4;
		int maxQuadsToDraw = maxIndices / 6;
		if (maxQuadsToDraw > maxVerts / 4)
			maxQuadsToDraw = maxVerts / 4;

		if (maxQuadsToDraw == 0)
			return;

		int quadsToDraw = quadCount;
		if (quadsToDraw > maxQuadsToDraw)
			quadsToDraw = maxQuadsToDraw;

		meshBuilder.Begin(mesh, MaterialPrimitiveType.Quads, quadsToDraw);

		int quadsDrawn = 0;
		while (FirstSprite < SpriteCount && SortInfos![FirstSprite].Distance >= minDistance) {
			DetailModel model = DetailObjects[SortInfos![FirstSprite].Index];
			int quadsInModel = model.QuadsToDraw();
			if (quadsDrawn + quadsInModel > quadsToDraw) {
				meshBuilder.End();
				mesh.Draw();

				quadCount = (SpriteCount - FirstSprite) * 4;
				quadsToDraw = quadCount;
				if (quadsToDraw > maxQuadsToDraw)
					quadsToDraw = maxQuadsToDraw;

				meshBuilder.Begin(mesh, MaterialPrimitiveType.Quads, quadsToDraw);
				quadsDrawn = 0;
			}

			model.DrawSprite(ref meshBuilder);
			++FirstSprite;
			quadsDrawn += quadsInModel;
		}
		meshBuilder.End();
		mesh.Draw();

		renderContext.PopMatrix();
	}

	public void RenderFastTranslucentDetailObjectsInLeaf(in Vector3 viewOrigin, in Vector3 viewForward, in Vector3 viewRight, in Vector3 viewUp, int leaf, Vector3? closestPoint) {
		if (clientLeafSystem.GetSubSystemDataInLeaf(leaf, ClientLeafSystem.CLSUBSYSTEM_DETAILOBJECTS) is not FastDetailLeafSpriteList data)
			return;

		if (SortedFastLeaf != leaf) {
			SortedFastLeaf = leaf;
			data.NumPendingSprites = BuildOutSortedSprites(data, viewOrigin, viewForward, viewRight, viewUp);
			data.StartSpriteIndex = 0;
		}

		if (data.NumPendingSprites == 0)
			return;

		float minDistance = 0.0f;
		if (closestPoint.HasValue) {
			MathLib.VectorSubtract(closestPoint.Value, viewOrigin, out Vector3 vecDelta);
			minDistance = vecDelta.LengthSquared();
		}

		if (FastSortInfos![data.StartSpriteIndex].Distance < minDistance)
			return;

		int count = data.NumPendingSprites;

		if (r_DrawDetailProps.GetInt() == 0)
			return;

		IMatRenderContext renderContext = materials.GetRenderContext();
		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		IMaterial material = DetailSpriteMaterial.Get()!;
		if (GLRSurfGlobals.ShouldDrawInWireFrameMode() || r_DrawDetailProps.GetInt() == 2)
			material = DetailWireframeMaterial.Get()!;

		MeshBuilder meshBuilder = new();
		IMesh mesh = renderContext.GetDynamicMesh(true, null, null, material);

		int maxVerts = 32768, maxIndices = 32768; /*GetMaxToRender*/
		int maxQuadsToDraw = maxIndices / 6;
		if (maxQuadsToDraw > maxVerts / 4)
			maxQuadsToDraw = maxVerts / 4;

		if (maxQuadsToDraw == 0)
			return;

		int quadsToDraw = Math.Min(count, maxQuadsToDraw);
		int quadsRemaining = quadsToDraw;

		meshBuilder.Begin(mesh, MaterialPrimitiveType.Quads, quadsToDraw);

		int drawIdx = data.StartSpriteIndex;

		Span<byte> color = stackalloc byte[4];

		while (count != 0 && FastSortInfos![drawIdx].Distance >= minDistance) {
			if (quadsRemaining == 0) {
				meshBuilder.End();
				mesh.Draw();
				quadsRemaining = quadsToDraw;
				meshBuilder.Begin(mesh, MaterialPrimitiveType.Quads, quadsToDraw);
			}
			int toDraw = Math.Min(count, quadsRemaining);
			count -= toDraw;
			quadsRemaining -= toDraw;
			while (toDraw-- != 0) {
				int nSIMDIdx = FastSortInfos![drawIdx].Index >> 2;
				int subIdx = FastSortInfos![drawIdx].Index & 3;

				ref FastSpriteQuadBuildoutBufferX4 quad = ref BuildoutBuffer![nSIMDIdx];

				color[0] = quad.RGBColor[subIdx][0];
				color[1] = quad.RGBColor[subIdx][1];
				color[2] = quad.RGBColor[subIdx][2];
				color[3] = (byte)(BitConverter.SingleToInt32Bits(MathLib.SubFloat(ref quad.Alpha, subIdx)) >> (MANTISSA_LSB_OFFSET * 8));

				ref DetailPropSpriteDict pDict = ref quad.SpriteDefs[subIdx];
				Color col = new(color[0], color[1], color[2], color[3]);

				meshBuilder.Position3f(MathLib.SubFloat(ref quad.Coords[0].x, subIdx), MathLib.SubFloat(ref quad.Coords[0].y, subIdx), MathLib.SubFloat(ref quad.Coords[0].z, subIdx));
				meshBuilder.Color4ubv(col);
				meshBuilder.TexCoord2f(0, pDict.TexLR.X, pDict.TexLR.Y);
				meshBuilder.AdvanceVertex();

				meshBuilder.Position3f(MathLib.SubFloat(ref quad.Coords[1].x, subIdx), MathLib.SubFloat(ref quad.Coords[1].y, subIdx), MathLib.SubFloat(ref quad.Coords[1].z, subIdx));
				meshBuilder.Color4ubv(col);
				meshBuilder.TexCoord2f(0, pDict.TexLR.X, pDict.TexUL.Y);
				meshBuilder.AdvanceVertex();

				meshBuilder.Position3f(MathLib.SubFloat(ref quad.Coords[2].x, subIdx), MathLib.SubFloat(ref quad.Coords[2].y, subIdx), MathLib.SubFloat(ref quad.Coords[2].z, subIdx));
				meshBuilder.Color4ubv(col);
				meshBuilder.TexCoord2f(0, pDict.TexUL.X, pDict.TexUL.Y);
				meshBuilder.AdvanceVertex();

				meshBuilder.Position3f(MathLib.SubFloat(ref quad.Coords[3].x, subIdx), MathLib.SubFloat(ref quad.Coords[3].y, subIdx), MathLib.SubFloat(ref quad.Coords[3].z, subIdx));
				meshBuilder.Color4ubv(col);
				meshBuilder.TexCoord2f(0, pDict.TexUL.X, pDict.TexLR.Y);
				meshBuilder.AdvanceVertex();
				drawIdx++;
			}
		}
		data.NumPendingSprites = count;
		data.StartSpriteIndex = drawIdx;

		meshBuilder.End();
		mesh.Draw();
		renderContext.PopMatrix();
	}

	public void BeginTranslucentDetailRendering() {
		SortedLeaf = -1;
		SortedFastLeaf = -1;
		SpriteCount = FirstSprite = 0;
	}

	public bool EnumerateLeaf(int leaf, nint context) {
		EnumContext ctx = (EnumContext)GCHandle.FromIntPtr(context).Target!;
		clientLeafSystem.DrawDetailObjectsInLeaf(leaf, ctx.BuildWorldListNumber, out int firstDetailObject, out int detailObjectCount);

		for (int i = 0; i < detailObjectCount; ++i) {
			DetailModel model = DetailObjects[firstDetailObject + i];
			MathLib.VectorSubtract(model.GetRenderOrigin(), ctx.ViewOrigin, out Vector3 v);

			float sqDist = v.LengthSquared();

			model.SetAlpha(255);
			if (sqDist < CurMaxSqDist) {
				if (sqDist > CurFadeSqDist)
					model.SetAlpha((byte)(CurFalloffFactor * (CurMaxSqDist - sqDist)));
				else
					model.SetAlpha(255);

				model.ComputeAngles();
			}
			else
				model.SetAlpha(0);
		}
		return true;
	}

	public ref DetailPropLightstylesLump DetailLighting(int i) => ref DetailLightingList.AsSpan()[i];
	public ref DetailPropSpriteDict DetailSpriteDict(int i) => ref DetailSpriteDictList.AsSpan()[i];

	const int MAGIC_NUMBER = 1 << 23;
	const int MANTISSA_LSB_OFFSET = 0;
	static readonly fltx4 Four_MagicNumbers = MathLib.ReplicateX4(MAGIC_NUMBER);
	static readonly fltx4 Four_255s = MathLib.ReplicateX4(255.0f);

	int BuildOutSortedSprites(FastDetailLeafSpriteList data, in Vector3 viewOrigin, in Vector3 viewForward, in Vector3 viewRight, in Vector3 viewUp) {
		int simdSprites = data.NumSIMDSprites;
		FastSpriteX4[] sprites = data.Sprites!;
		SortInfo[] outSort = FastSortInfos!;
		FastSpriteQuadBuildoutBufferX4[] quadBufferOut = BuildoutBuffer!;
		int spriteIdx = data.StartSIMDSprite;
		int outIdx = 0;
		int quadIdx = 0;
		int curidx = 0;
		int lastBfMask = 0;

		FourVectors vecViewPos = default;
		vecViewPos.DuplicateVector(viewOrigin);
		fltx4 maxsqdist = MathLib.ReplicateX4(CurMaxSqDist);

		fltx4 falloffFactor = MathLib.ReplicateX4(1.0f / (CurMaxSqDist - CurFadeSqDist));
		fltx4 startFade = MathLib.ReplicateX4(CurFadeSqDist);

		FourVectors vecUp = default;
		vecUp.DuplicateVector(new Vector3(0, 0, 1));
		FourVectors vecFwd = default;
		vecFwd.DuplicateVector(viewForward);

		do {
			ref FastSpriteX4 spr = ref sprites[spriteIdx];
			FourVectors ofs = spr.Pos;
			ofs -= vecViewPos;
			Vector4 ofsDotFwd = ofs.Dot(vecFwd);
			Vector4 distanceSquared = ofs.Dot(ofs);
			lastBfMask = MathLib.TestSignSIMD(MathLib.OrSIMD(ofsDotFwd.AsVector128(), MathLib.CmpGtSIMD(distanceSquared.AsVector128(), maxsqdist)));
			if (lastBfMask != 0xf) {
				FourVectors dx1 = default;
				dx1.x = -ofs.y;
				dx1.y = ofs.x;
				dx1.z = Vector4.Zero;
				dx1.VectorNormalizeFast();

				FourVectors vecDx = dx1;
				FourVectors vecDy = vecUp;

				FourVectors vecPos0 = spr.Pos;

				vecDx *= spr.HalfWidth.AsVector4();
				vecDy *= spr.Height.AsVector4();
				fltx4 alpha = MathLib.MulSIMD(falloffFactor, MathLib.SubSIMD(distanceSquared.AsVector128(), startFade));
				alpha = MathLib.SubSIMD(MathLib.Four_Ones, MathLib.MinSIMD(MathLib.MaxSIMD(alpha, MathLib.Four_Zeros), MathLib.Four_Ones));

				ref FastSpriteQuadBuildoutBufferX4 quad = ref quadBufferOut[quadIdx];
				quad.Alpha = MathLib.MaddSIMD(Four_255s, alpha, Four_MagicNumbers);

				vecPos0 += vecDx;
				quad.Coords[0] = vecPos0;
				vecPos0 -= vecDy;
				quad.Coords[1] = vecPos0;
				vecPos0 -= vecDx;
				vecPos0 -= vecDx;
				quad.Coords[2] = vecPos0;
				vecPos0 += vecDy;
				quad.Coords[3] = vecPos0;

				for (int j = 0; j < 4; j++)
					quad.SpriteDefs[j] = spr.SpriteDefs[j];
				for (int i = 0; i < 4; i++)
					for (int j = 0; j < 4; j++)
						quad.RGBColor[i][j] = spr.RGBColor[i][j];

				outSort[outIdx + 0].Index = curidx;
				outSort[outIdx + 0].Distance = MathLib.SubFloat(ref distanceSquared, 0);
				outSort[outIdx + 1].Index = curidx + 1;
				outSort[outIdx + 1].Distance = MathLib.SubFloat(ref distanceSquared, 1);
				outSort[outIdx + 2].Index = curidx + 2;
				outSort[outIdx + 2].Distance = MathLib.SubFloat(ref distanceSquared, 2);
				outSort[outIdx + 3].Index = curidx + 3;
				outSort[outIdx + 3].Distance = MathLib.SubFloat(ref distanceSquared, 3);
				curidx += 4;
				outIdx += 4;
				quadIdx++;
			}
			spriteIdx++;
		} while (--simdSprites != 0);

		int count = outIdx;
		if (lastBfMask != 0xf)
			count -= (0 - data.NumSprites) & 3;

		if (count != 0) {
			Span<SortInfo> s = outSort.AsSpan(0, count);
			s.Sort((a, b) => SortLessFunc(a, b) ? -1 : (SortLessFunc(b, a) ? 1 : 0));
		}
		return count;
	}

	void RenderFastSprites(in Vector3 viewOrigin, in Vector3 viewForward, in Vector3 viewRight, in Vector3 viewUp, int leafCount, ReadOnlySpan<LeafIndex_t> leafList) {
		int quadCount = CountFastSpritesInLeafList(leafCount, leafList, out int nMaxInLeaf);
		if (quadCount == 0)
			return;
		if (r_DrawDetailProps.GetInt() == 0)
			return;

		IMatRenderContext renderContext = materials.GetRenderContext();
		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		IMaterial material = DetailSpriteMaterial.Get()!;
		if (GLRSurfGlobals.ShouldDrawInWireFrameMode() || r_DrawDetailProps.GetInt() == 2)
			material = DetailWireframeMaterial.Get()!;

		MeshBuilder meshBuilder = new();
		IMesh mesh = renderContext.GetDynamicMesh(true, null, null, material);

		int maxVerts = 32768, maxIndices = 32768; /*GetMaxToRender*/
		int maxQuadsToDraw = maxIndices / 6;
		if (maxQuadsToDraw > maxVerts / 4)
			maxQuadsToDraw = maxVerts / 4;

		if (maxQuadsToDraw == 0)
			return;

		int nQuadsToDraw = Math.Min(quadCount, maxQuadsToDraw);
		int quadsRemaining = nQuadsToDraw;

		meshBuilder.Begin(mesh, MaterialPrimitiveType.Quads, nQuadsToDraw);

		Span<byte> color = stackalloc byte[4];

		for (int i = 0; i < leafCount; ++i) {
			int leaf = leafList[i];

			if (clientLeafSystem.GetSubSystemDataInLeaf(leaf, ClientLeafSystem.CLSUBSYSTEM_DETAILOBJECTS) is FastDetailLeafSpriteList pData) {
				Assert(pData.NumSprites != 0);

				int count = BuildOutSortedSprites(pData, viewOrigin, viewForward, viewRight, viewUp);

				int drawIdx = 0;

				while (count != 0) {
					if (quadsRemaining == 0) {
						meshBuilder.End();
						mesh.Draw();
						quadsRemaining = nQuadsToDraw;
						meshBuilder.Begin(mesh, MaterialPrimitiveType.Quads, nQuadsToDraw);
					}
					int toDraw = Math.Min(count, quadsRemaining);
					count -= toDraw;
					quadsRemaining -= toDraw;
					while (toDraw-- != 0) {
						int nSIMDIdx = FastSortInfos![drawIdx].Index >> 2;
						int nSubIdx = FastSortInfos![drawIdx].Index & 3;

						ref FastSpriteQuadBuildoutBufferX4 quad = ref BuildoutBuffer![nSIMDIdx];

						color[0] = quad.RGBColor[nSubIdx][0];
						color[1] = quad.RGBColor[nSubIdx][1];
						color[2] = quad.RGBColor[nSubIdx][2];
						color[3] = (byte)(BitConverter.SingleToInt32Bits(MathLib.SubFloat(ref quad.Alpha, nSubIdx)) >> (MANTISSA_LSB_OFFSET * 8));

						ref DetailPropSpriteDict pDict = ref quad.SpriteDefs[nSubIdx];
						Color col = new(color[0], color[1], color[2], color[3]);

						meshBuilder.Position3f(MathLib.SubFloat(ref quad.Coords[0].x, nSubIdx), MathLib.SubFloat(ref quad.Coords[0].y, nSubIdx), MathLib.SubFloat(ref quad.Coords[0].z, nSubIdx));
						meshBuilder.Color4ubv(col);
						meshBuilder.TexCoord2f(0, pDict.TexLR.X, pDict.TexLR.Y);
						meshBuilder.AdvanceVertex();

						meshBuilder.Position3f(MathLib.SubFloat(ref quad.Coords[1].x, nSubIdx), MathLib.SubFloat(ref quad.Coords[1].y, nSubIdx), MathLib.SubFloat(ref quad.Coords[1].z, nSubIdx));
						meshBuilder.Color4ubv(col);
						meshBuilder.TexCoord2f(0, pDict.TexLR.X, pDict.TexUL.Y);
						meshBuilder.AdvanceVertex();

						meshBuilder.Position3f(MathLib.SubFloat(ref quad.Coords[2].x, nSubIdx), MathLib.SubFloat(ref quad.Coords[2].y, nSubIdx), MathLib.SubFloat(ref quad.Coords[2].z, nSubIdx));
						meshBuilder.Color4ubv(col);
						meshBuilder.TexCoord2f(0, pDict.TexUL.X, pDict.TexUL.Y);
						meshBuilder.AdvanceVertex();

						meshBuilder.Position3f(MathLib.SubFloat(ref quad.Coords[3].x, nSubIdx), MathLib.SubFloat(ref quad.Coords[3].y, nSubIdx), MathLib.SubFloat(ref quad.Coords[3].z, nSubIdx));
						meshBuilder.Color4ubv(col);
						meshBuilder.TexCoord2f(0, pDict.TexUL.X, pDict.TexLR.Y);
						meshBuilder.AdvanceVertex();
						drawIdx++;
					}
				}
			}
		}
		meshBuilder.End();
		mesh.Draw();
		renderContext.PopMatrix();
	}

	void UnserializeFastSprite(ref FastSpriteX4 spritex4, int subField, in DetailObjectLump lump, bool flipped, in Vector3 posOffset) {
		Vector3 pos = GetSpriteMiddleBottomPosition(lump) + posOffset;

		MathLib.SubFloat(ref spritex4.Pos.x, subField) = pos.X;
		MathLib.SubFloat(ref spritex4.Pos.y, subField) = pos.Y;
		MathLib.SubFloat(ref spritex4.Pos.z, subField) = pos.Z;
		DetailPropSpriteDict sdef = DetailSpriteDictList[lump.DetailModel];

		MathLib.SubFloat(ref spritex4.HalfWidth, subField) = 0.5f * lump.Scale * (sdef.LR.X - sdef.UL.X);
		MathLib.SubFloat(ref spritex4.Height, subField) = lump.Scale * (sdef.LR.Y - sdef.UL.Y);
		if (!flipped)
			sdef = DetailSpriteDictFlipped[lump.DetailModel];

		ColorRGBExp32 rgbcolor = lump.Lighting;
		Span<float> color = [
			MathLib.TexLightToLinear(rgbcolor.R, rgbcolor.Exponent),
			MathLib.TexLightToLinear(rgbcolor.G, rgbcolor.Exponent),
			MathLib.TexLightToLinear(rgbcolor.B, rgbcolor.Exponent),
			255
		];
		engine.LinearToGamma(color, color);
		spritex4.RGBColor[subField][0] = (byte)(255.0f * color[0]);
		spritex4.RGBColor[subField][1] = (byte)(255.0f * color[1]);
		spritex4.RGBColor[subField][2] = (byte)(255.0f * color[2]);
		spritex4.RGBColor[subField][3] = 255;

		spritex4.SpriteDefs[subField] = sdef;
	}

	static void ScanForCounts(Stream buf, out int numOldStyleObjects, out int numFastSpritesToAllocate, out int maxNumOldSpritesInLeaf, out int maxNumFastSpritesInLeaf) {
		long oldpos = buf.Position;
		int count = 0;
		buf.ReadToStruct(ref count);

		int nOld = 0;
		int nFast = 0;
		int detailObjectLeaf = -1;

		int numOldInLeaf = 0;
		int numFastInLeaf = 0;
		int maxOld = 0;
		int maxFast = 0;
		while (--count >= 0) {
			DetailObjectLump lump = default;
			buf.ReadToStruct(ref lump);

			if (detailObjectLeaf != lump.Leaf) {
				nFast += (0 - nFast) & 3;
				maxFast = Math.Max(maxFast, numFastInLeaf);
				maxOld = Math.Max(maxOld, numOldInLeaf);
				numOldInLeaf = 0;
				numFastInLeaf = 0;
				detailObjectLeaf = lump.Leaf;
			}

			if (DetailObjectIsFastSprite(lump)) {
				nFast += cl_detail_multiplier.GetInt();
				numFastInLeaf += cl_detail_multiplier.GetInt();
			}
			else {
				nOld += cl_detail_multiplier.GetInt();
				numOldInLeaf += cl_detail_multiplier.GetInt();
			}
		}

		nFast += (0 - nFast) & 3;
		maxFast = Math.Max(maxFast, numFastInLeaf);
		maxOld = Math.Max(maxOld, numOldInLeaf);

		buf.Position = oldpos;
		numFastSpritesToAllocate = nFast;
		numOldStyleObjects = nOld;
		maxFast = (3 + maxFast) & ~3;
		maxNumOldSpritesInLeaf = maxOld;
		maxNumFastSpritesInLeaf = maxFast;
	}

	void UnserializeModelDict(Stream buf) {
		int count = 0;
		buf.ReadToStruct(ref count);
		DetailObjectDict.EnsureCapacity(count);
		while (--count >= 0) {
			DetailObjectDictLump lump = default;
			buf.ReadToStruct(ref lump);

			Span<byte> nameBytes = lump.Name;
			int len = nameBytes.IndexOf((byte)0);
			if (len < 0)
				len = nameBytes.Length;
			string name = System.Text.Encoding.ASCII.GetString(nameBytes[..len]);

			DetailModelDict dict = default;
			dict.Model = engine.LoadModel(name, true);

			if (modelinfo.IsModelVertexLit(dict.Model)) {
				Warning($"Detail prop model {name} is using vertex-lit materials!\nIt must use unlit materials!\n");
				dict.Model = engine.LoadModel("models/error.mdl");
			}

			DetailObjectDict.Add(dict);
		}

#if DEBUG
		DevMsg($"UnserializeModelDict: {DetailObjectDict.Count} detail prop models\n");
#endif
	}

	void UnserializeDetailSprites(Stream buf) {
		int count = 0;
		buf.ReadToStruct(ref count);
		DetailSpriteDictList.EnsureCapacity(count);
		DetailSpriteDictFlipped.EnsureCapacity(count);
		while (--count >= 0) {
			DetailPropSpriteDict dict = default;
			buf.ReadToStruct(ref dict);
			DetailSpriteDictList.Add(dict);

			DetailPropSpriteDict flipped = dict;
			(flipped.TexLR.X, flipped.TexUL.X) = (flipped.TexUL.X, flipped.TexLR.X);
			DetailSpriteDictFlipped.Add(flipped);
		}

#if DEBUG
		DevMsg($"UnserializeDetailSprites: {DetailSpriteDictList.Count} detail sprites\n");
#endif
	}

	void UnserializeModels(Stream buf) {
		int firstDetailObject = 0;
		int detailObjectCount = 0;
		int detailObjectLeaf = -1;

		ScanForCounts(buf, out int numOldStyleObjects, out int numFastSpritesToAllocate, out int maxOldInLeaf, out int maxFastInLeaf);

		FreeSortBuffers();

		if (maxOldInLeaf != 0)
			SortInfos = new SortInfo[3 + maxOldInLeaf];
		if (maxFastInLeaf != 0) {
			FastSortInfos = new SortInfo[3 + maxFastInLeaf];
			BuildoutBuffer = new FastSpriteQuadBuildoutBufferX4[1 + maxFastInLeaf / 4];
		}

		if (numFastSpritesToAllocate != 0) {
			Assert((numFastSpritesToAllocate & 3) == 0);
			Assert(FastSpriteData == null);
			FastSpriteData = new FastSpriteX4[numFastSpritesToAllocate >> 2];
		}

		DetailObjects.EnsureCapacity(numOldStyleObjects);

		int count = 0;
		buf.ReadToStruct(ref count);

		int curFastObject = 0;
		int numFastObjectsInCurLeaf = 0;
		int curFastSpriteOut = 0;

		bool flipped = true;
		while (--count >= 0) {
			flipped = !flipped;
			DetailObjectLump lump = default;
			buf.ReadToStruct(ref lump);

			if (detailObjectLeaf != lump.Leaf) {
				if (detailObjectLeaf != -1) {
					if (numFastObjectsInCurLeaf != 0) {
						FastDetailLeafSpriteList newList = new() {
							NumSprites = numFastObjectsInCurLeaf,
							NumSIMDSprites = (3 + numFastObjectsInCurLeaf) >> 2,
							Sprites = FastSpriteData,
							StartSIMDSprite = curFastSpriteOut
						};
						curFastSpriteOut += newList.NumSIMDSprites;
						clientLeafSystem.SetSubSystemDataInLeaf(detailObjectLeaf, ClientLeafSystem.CLSUBSYSTEM_DETAILOBJECTS, newList);
						curFastObject += (0 - curFastObject) & 3;
						numFastObjectsInCurLeaf = 0;
					}
					clientLeafSystem.SetDetailObjectsInLeaf(detailObjectLeaf, firstDetailObject, detailObjectCount);
				}

				detailObjectLeaf = lump.Leaf;
				firstDetailObject = DetailObjects.Count;
				detailObjectCount = 0;
			}

			if (DetailObjectIsFastSprite(lump)) {
				for (int i = 0; i < cl_detail_multiplier.GetInt(); i++) {
					int subField = curFastObject & 3;
					Vector3 pos = new(0, 0, 0);
					if (i != 0) {
						pos += RandomVector(-50, 50);
						pos.Z = 0;
					}
					UnserializeFastSprite(ref FastSpriteData![curFastObject >> 2], subField, lump, flipped, pos);
					if (subField == 0)
						FastSpriteData![curFastObject >> 2].ReplicateFirstEntryToOthers();
					curFastObject++;
					numFastObjectsInCurLeaf++;
				}
			}
			else {
				switch ((DetailPropType)lump.Type) {
					case DetailPropType.Model: {
							int newObj = DetailObjects.Count;
							DetailModel obj = new();
							DetailObjects.Add(obj);
							obj.Init(newObj, lump.Origin, lump.Angles, DetailObjectDict[lump.DetailModel].Model, lump.Lighting, (int)lump.LightStyles, lump.LightStyleCount, lump.Orientation);
							++detailObjectCount;
						}
						break;

					case DetailPropType.Sprite:
					case DetailPropType.ShapeCross:
					case DetailPropType.ShapeTri: {
							for (int i = 0; i < cl_detail_multiplier.GetInt(); i++) {
								Vector3 pos = lump.Origin;
								if (i != 0) {
									pos += RandomVector(-50, 50);
									pos.Z = lump.Origin.Z;
								}
								int newObj = DetailObjects.Count;
								DetailModel obj = new();
								DetailObjects.Add(obj);
								obj.InitSprite(newObj, flipped, pos, lump.Angles, lump.DetailModel, lump.Lighting, (int)lump.LightStyles, lump.LightStyleCount, lump.Orientation, lump.Scale, lump.Type, lump.ShapeAngle, lump.ShapeSize, lump.SwayAmount);
								++detailObjectCount;
							}
						}
						break;
				}
			}
		}

		if (detailObjectLeaf != -1) {
			if (numFastObjectsInCurLeaf != 0) {
				FastDetailLeafSpriteList newList = new() {
					NumSprites = numFastObjectsInCurLeaf,
					NumSIMDSprites = (3 + numFastObjectsInCurLeaf) >> 2,
					Sprites = FastSpriteData,
					StartSIMDSprite = curFastSpriteOut
				};
				curFastSpriteOut += newList.NumSIMDSprites;
				clientLeafSystem.SetSubSystemDataInLeaf(detailObjectLeaf, ClientLeafSystem.CLSUBSYSTEM_DETAILOBJECTS, newList);
			}
			clientLeafSystem.SetDetailObjectsInLeaf(detailObjectLeaf, firstDetailObject, detailObjectCount);
		}

#if DEBUG
		DevMsg($"UnserializeModels: {DetailObjects.Count} detail objects, {curFastObject} fast sprites\n");
#endif
	}

	void UnserializeModelLighting(Stream buf) {
		int count = 0;
		buf.ReadToStruct(ref count);
		DetailLightingList.EnsureCapacity(count);
		while (--count >= 0) {
			DetailPropLightstylesLump lump = default;
			buf.ReadToStruct(ref lump);
			DetailLightingList.Add(lump);
		}

#if DEBUG
		DevMsg($"UnserializeModelLighting: {DetailLightingList.Count} detail lighting entries\n");
#endif
	}

	Vector3 GetSpriteMiddleBottomPosition(in DetailObjectLump lump) {
		ref DetailPropSpriteDict dict = ref DetailSpriteDict(lump.DetailModel);

		MathLib.VectorSubtract(lump.Origin + new Vector3(0, -100, 0), lump.Origin, out Vector3 vecDir);
		vecDir.Z = 0.0f;
		MathLib.VectorAngles(vecDir, out QAngle angles);

		MathLib.AngleVectors(angles, out _, out Vector3 dx, out Vector3 dy);

		float scale = lump.Scale;
		MathLib.Vector2DMultiply(dict.UL, scale, out Vector2 ul);
		MathLib.Vector2DMultiply(dict.LR, scale, out Vector2 lr);

		MathLib.VectorMA(lump.Origin, ul.X, dx, out Vector3 vecOrigin);
		MathLib.VectorMA(vecOrigin, ul.Y, dy, out vecOrigin);
		dx *= lr.X - ul.X;
		dy *= lr.Y - ul.Y;

		return vecOrigin + dy + 0.5f * dx;
	}

	static int CountSpritesInLeafList(int leafCount, ReadOnlySpan<LeafIndex_t> leafList) {
		int propCount = 0;
		for (int i = 0; i < leafCount; ++i) {
			clientLeafSystem.GetDetailObjectsInLeaf(leafList[i], out _, out int detailObjectCount);
			propCount += detailObjectCount;
		}

		return propCount;
	}

	int CountSpriteQuadsInLeafList(int leafCount, ReadOnlySpan<LeafIndex_t> leafList) {
		int quadCount = 0;
		for (int i = 0; i < leafCount; ++i) {
			clientLeafSystem.GetDetailObjectsInLeaf(leafList[i], out int firstDetailObject, out int detailObjectCount);
			for (int j = 0; j < detailObjectCount; ++j) {
				quadCount += DetailObjects[j + firstDetailObject].QuadsToDraw();
			}
		}

		return quadCount;
	}

	static int CountFastSpritesInLeafList(int leafCount, ReadOnlySpan<LeafIndex_t> leafList, out int maxFoundInLeaf) {
		int count = 0;
		int max = 0;
		for (int i = 0; i < leafCount; ++i) {
			if (clientLeafSystem.GetSubSystemDataInLeaf(leafList[i], ClientLeafSystem.CLSUBSYSTEM_DETAILOBJECTS) is FastDetailLeafSpriteList data) {
				count += data.NumSprites;
				max = Math.Max(max, data.NumSprites);
			}
		}
		maxFoundInLeaf = (max + 3) & ~3;
		return count;
	}

	void FreeSortBuffers() {
		SortInfos = null;
		FastSortInfos = null;
		FastSortInfos = null;
		BuildoutBuffer = null;
	}

	static bool SortLessFunc(in SortInfo left, in SortInfo right) => BitConverter.SingleToInt32Bits(left.Distance) > BitConverter.SingleToInt32Bits(right.Distance);
	int SortSpritesBackToFront(int leaf, in Vector3 viewOrigin, in Vector3 viewForward, Span<SortInfo> sortInfo) {
		clientLeafSystem.GetDetailObjectsInLeaf(leaf, out int firstDetailObject, out int detailObjectCount);

		float factor = 1.0f;
		C_BasePlayer? localPlayer = C_BasePlayer.GetLocalPlayer();
		if (localPlayer != null)
			factor = 1.0f / localPlayer.GetFOVDistanceAdjustFactor();

		float maxSqDist;
		float fadeSqDist;
		float detailDist = cl_detaildist.GetFloat();

		maxSqDist = detailDist * detailDist;
		fadeSqDist = detailDist - cl_detailfade.GetFloat();
		maxSqDist *= factor;
		fadeSqDist *= factor;
		if (fadeSqDist > 0)
			fadeSqDist *= fadeSqDist;
		else
			fadeSqDist = 0;
		float falloffFactor = 255.0f / (maxSqDist - fadeSqDist);

		int count = 0;
		detailObjectCount += firstDetailObject;
		for (int j = firstDetailObject; j < detailObjectCount; ++j) {
			DetailModel model = DetailObjects[j];

			MathLib.VectorSubtract(model.GetRenderOrigin(), viewOrigin, out Vector3 vecDelta);
			float sqDist = vecDelta.LengthSqr();
			if (sqDist >= maxSqDist)
				continue;

			if ((fadeSqDist > 0) && (sqDist > fadeSqDist))
				model.SetAlpha((byte)(falloffFactor * (maxSqDist - sqDist)));
			else
				model.SetAlpha(255);

			if ((model.GetDetailType() == (int)DetailPropType.Model) || (model.GetAlpha() == 0))
				continue;

			model.ComputeAngles();
			ref SortInfo sortInfoCurrent = ref sortInfo[count];

			sortInfoCurrent.Index = j;

			sortInfoCurrent.Distance = sqDist;
			++count;
		}

		if (count != 0)
			sortInfo[..count].Sort((a, b) => SortLessFunc(a, b) ? -1 : (SortLessFunc(b, a) ? 1 : 0));

		return count;
	}
}
