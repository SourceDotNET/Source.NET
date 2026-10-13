using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Commands;
using Source.Common.DataCache;
using Source.Common.Engine;
using Source.Common.Formats.Keyvalues;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System;
using System.Buffers;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Source.StudioRender;

public struct BodyPartInfo
{
	public int SubModelIndex;
	public MStudioModel? SubModel;
}
public enum StudioModelLighting
{
	Hardware,
	Software,
	Mouth
}

public struct LightPos
{
	public Vector3 Delta;
	public float Falloff;
	public float Dot;
}

public struct EyeballState
{
	public MStudioEyeball? Eyeball;

	public Matrix3x4 Mat;

	public Vector3 Org;
	public Vector3 Forward;
	public Vector3 Right;
	public Vector3 Up;

	public Vector3 Cornea;
}

[EngineComponent]
public unsafe class StudioRender
{
	IMaterialSystem materialSystem = Singleton<IMaterialSystem>();
	IStudioDataCache studioDataCache = Singleton<IStudioDataCache>();
	IMaterialSystemHardwareConfig hardwareConfig = Singleton<IMaterialSystemHardwareConfig>();

	StudioRenderCtx? pRC;
	Matrix3x4* pBoneToWorld;
	int nBoneToWorld;
	float* pFlexWeights;
	float* pFlexDelayedWeights;
	StudioHeader? StudioHdr;
	StudioMeshData[]? StudioMeshes;

	readonly CachedRenderData VertexCache = new();

	public readonly Matrix3x4[] PoseToWorld = new Matrix3x4[Studio.MAXSTUDIOBONES];
	public readonly Matrix3x4[] PoseToDecal = new Matrix3x4[Studio.MAXSTUDIOBONES];

	internal const int MAXLOCALLIGHTS = 4;

	internal void DrawModel(ref DrawModelInfo info, StudioRenderCtx RC, Span<Matrix3x4> boneToWorld, Span<float> flexWeights, Span<float> flexDelayedWeights, StudioRenderFlags flags) {
		// TODO: a better way to do this that doesnt require unsafe
		nBoneToWorld = boneToWorld.Length;
		fixed (Matrix3x4* pBtW = boneToWorld)
		fixed (float* pFW = flexWeights, pFDW = flexDelayedWeights) {
			pRC = RC;
			pFlexWeights = pFW;
			pFlexDelayedWeights = pFDW;
			pBoneToWorld = pBtW;

			using MatRenderContextPtr pRenderContext = new(materialSystem);

			bool flexConfig = pRC.Config.Flex;
			if ((flags & StudioRenderFlags.DrawNoFlexes) != 0)
				pRC.Config.Flex = false;

			// TODO: Enable wireframe if we're told to...
			int boneMask = Studio.BONE_USED_BY_VERTEX_AT_LOD(info.Lod);

			// Preserve the matrices if we're skinning
			pRenderContext.MatrixMode(MaterialMatrixMode.Model);
			pRenderContext.PushMatrix();
			pRenderContext.LoadIdentity();

			VertexCache.StartModel();

			StudioHdr = info.StudioHdr;
			if (info.HardwareData.LODs == null) {
				Msg($"Missing LODs for {StudioHdr.GetName()}, lod index is {info.Lod}.\n");
				return;
			}
			StudioMeshes = info.HardwareData.LODs[info.Lod].MeshData;

			// Bone to world must be set before calling drawmodel; it uses that here
			ComputePoseToWorld(PoseToWorld, StudioHdr, boneMask, in pRC.ViewOrigin, pBoneToWorld);

			R_StudioRenderModel(pRenderContext, info.Skin, info.Body, info.HitboxSet, info.ClientEntity,
				info.HardwareData.LODs[info.Lod].Materials,
				info.HardwareData.LODs[info.Lod].MaterialFlags, flags, boneMask, info.Lod, info.ColorMeshes);

			// TODO: decals

			// Restore the matrices if we're skinning
			pRenderContext.MatrixMode(MaterialMatrixMode.Model);
			pRenderContext.PopMatrix();

			// TODO: Restore the configs
			pRC.Config.Flex = flexConfig;

			pRenderContext.SetNumBoneWeights(0);
			pRC = null;
			StudioMeshes = null;
			StudioHdr = null;
			pBoneToWorld = null;
			pFlexWeights = null;
			pFlexDelayedWeights = null;
		}
	}

	Matrix3x4 StaticPropRootToWorld;

	internal void DrawModelStaticProp(ref DrawModelInfo info, StudioRenderCtx RC, in Matrix3x4 rootToWorld, StudioRenderFlags flags) {
		pRC = RC;

		using MatRenderContextPtr pRenderContext = new(materialSystem);

		StaticPropRootToWorld = rootToWorld;
		PoseToWorld[0] = rootToWorld;
		fixed (Matrix3x4* pRootToWorld = &StaticPropRootToWorld) {
			pBoneToWorld = pRootToWorld;
			nBoneToWorld = 1;

			bool flexConfig = pRC.Config.Flex;
			pRC.Config.Flex = false;
			bool wireframe = pRC.Config.Wireframe;
			if ((flags & StudioRenderFlags.DrawWireframe) != 0)
				pRC.Config.Wireframe = true;

			int lod = info.Lod;
			StudioHdr = info.StudioHdr;
			StudioMeshes = info.HardwareData.LODs![lod].MeshData;

			R_StudioRenderModel(pRenderContext, info.Skin, info.Body, info.HitboxSet, info.ClientEntity,
				info.HardwareData.LODs[lod].Materials,
				info.HardwareData.LODs[lod].MaterialFlags, flags, Studio.BONE_USED_BY_ANYTHING, lod, info.ColorMeshes);

			// TODO: decals, shadows, flashlight decals

			pRC.Config.Flex = flexConfig;
			pRC.Config.Wireframe = wireframe;

			pRenderContext.SetNumBoneWeights(0);
			pBoneToWorld = null;
			pRC = null;
		}
	}

	private void ComputePoseToWorld(Span<Matrix3x4> poseToWorld, StudioHeader studioHdr, int boneMask, in Vector3 viewOrigin, Matrix3x4* pBoneToWorld) {
		if ((studioHdr.Flags & StudioHdrFlags.StaticProp) != 0) {
			poseToWorld[0] = pBoneToWorld[0];
			return;
		}

		if (studioHdr.LinearBones() == null) {
			for (int i = 0; i < studioHdr.NumBones; i++) {
				MStudioBone pCurBone = studioHdr.Bone(i);
				if ((pCurBone.Flags & boneMask) == 0)
					continue;

				Matrix3x4 poseToBone = pCurBone.PoseToBone;
				MathLib.ConcatTransforms(in pBoneToWorld[i], in poseToBone, out poseToWorld[i]);
			}
		}
		else {
			MStudioLinearBone linearBones = studioHdr.LinearBones()!;

			for (int i = 0; i < studioHdr.NumBones; i++) {
				if ((linearBones.Flags(i) & boneMask) == 0)
					continue;

				Matrix3x4 poseToBone = linearBones.PoseToBone(i);
				MathLib.ConcatTransforms(in pBoneToWorld[i], in poseToBone, out poseToWorld[i]);
			}
		}
	}

	bool SkippedMeshes;
	bool DrawTranslucentSubModels;

	public int R_StudioRenderModel(IMatRenderContext renderContext, int skin, int body, int hitboxset, object? entity,
									Span<IMaterial> materials, Span<int> materialFlags, StudioRenderFlags flags, int boneMask, int lod, Span<ColorMeshInfo> colorMeshes) {
		StudioRenderFlags nDrawGroup = flags & StudioRenderFlags.DrawGroupMask;

		// TODO: Draw modes for entities/bones stuff

		int numTrianglesRendered = 0;

		// Build list of submodels
		BodyPartInfo[] pBodyPartInfo = ArrayPool<BodyPartInfo>.Shared.Rent(StudioHdr!.NumBodyParts);
		for (int i = 0; i < StudioHdr.NumBodyParts; ++i)
			pBodyPartInfo[i].SubModelIndex = R_StudioSetupModel(i, body, out pBodyPartInfo[i].SubModel, StudioHdr);

		if (nDrawGroup != StudioRenderFlags.DrawTranslucentOnly) {
			SkippedMeshes = false;
			DrawTranslucentSubModels = false;
			numTrianglesRendered += R_StudioRenderFinal(renderContext, skin, StudioHdr.NumBodyParts, pBodyPartInfo,
				entity, materials, materialFlags, boneMask, lod, colorMeshes);
		}
		else {
			SkippedMeshes = true;
		}

		if (SkippedMeshes && nDrawGroup != StudioRenderFlags.DrawOpaqueOnly) {
			DrawTranslucentSubModels = true;
			numTrianglesRendered += R_StudioRenderFinal(renderContext, skin, StudioHdr.NumBodyParts, pBodyPartInfo,
				entity, materials, materialFlags, boneMask, lod, colorMeshes);
		}
		ArrayPool<BodyPartInfo>.Shared.Return(pBodyPartInfo, true);
		return numTrianglesRendered;
	}

	MStudioModel? SubModel;

	readonly EyeballState[] EyeballStates = new EyeballState[16];

	private void ComputeEyelidStateFACS(MStudioModel subModel) {
		for (int j = 0; j < subModel.NumEyeballs; j++) {
			R_StudioEyeballPosition(subModel.Eyeball(j), ref EyeballStates[j]);
			R_StudioEyelidFACS(subModel.Eyeball(j), in EyeballStates[j]);
		}
	}

	private void R_StudioEyelidFACS(MStudioEyeball eyeball, in EyeballState state) {
		if (eyeball.NonFACS)
			return;

		Vector3 normTarget = new(eyeball.UpperTarget[0], eyeball.UpperTarget[1], eyeball.UpperTarget[2]);
		normTarget /= eyeball.Radius;
		normTarget.X = Math.Clamp(normTarget.X, -1.0f, 1.0f);
		normTarget.Y = Math.Clamp(normTarget.Y, -1.0f, 1.0f);
		normTarget.Z = Math.Clamp(normTarget.Z, -1.0f, 1.0f);

		float upperlid = pFlexWeights[eyeball.UpperFlexDesc[0]] * MathF.Asin(normTarget.X);
		upperlid += pFlexWeights[eyeball.UpperFlexDesc[1]] * MathF.Asin(normTarget.Y);
		upperlid += pFlexWeights[eyeball.UpperFlexDesc[2]] * MathF.Asin(normTarget.Z);

		normTarget = new(eyeball.LowerTarget[0], eyeball.LowerTarget[1], eyeball.LowerTarget[2]);
		normTarget /= eyeball.Radius;
		normTarget.X = Math.Clamp(normTarget.X, -1.0f, 1.0f);
		normTarget.Y = Math.Clamp(normTarget.Y, -1.0f, 1.0f);
		normTarget.Z = Math.Clamp(normTarget.Z, -1.0f, 1.0f);

		float lowerlid = pFlexWeights[eyeball.LowerFlexDesc[0]] * MathF.Asin(normTarget.X);
		lowerlid += pFlexWeights[eyeball.LowerFlexDesc[1]] * MathF.Asin(normTarget.Y);
		lowerlid += pFlexWeights[eyeball.LowerFlexDesc[2]] * MathF.Asin(normTarget.Z);

		MathLib.SinCos(upperlid, out float sinupper, out float cosupper);
		MathLib.SinCos(lowerlid, out float sinlower, out float coslower);

		MathLib.VectorIRotate(in state.Up, in pBoneToWorld[eyeball.Bone], out Vector3 headup);
		MathLib.VectorIRotate(in state.Forward, in pBoneToWorld[eyeball.Bone], out Vector3 headforward);

		Vector3 pos = headup * (sinupper * eyeball.Radius);
		pos += headforward * (cosupper * eyeball.Radius);
		pFlexWeights[eyeball.UpperLidFlexDesc] = Vector3.Dot(pos, eyeball.Up);

		pos = headup * (sinlower * eyeball.Radius);
		pos += headforward * (coslower * eyeball.Radius);
		pFlexWeights[eyeball.LowerLidFlexDesc] = Vector3.Dot(pos, eyeball.Up);
	}

	private static float RampFlexWeight(MStudioFlex flex, float w) {
		if (flex.Target0 == 0.0f && flex.Target1 == 1.0f)
			return w;

		if (w <= flex.Target0 || w >= flex.Target3)
			w = 0.0f;
		else if (w < flex.Target1)
			w = (w - flex.Target0) / (flex.Target1 - flex.Target0);
		else if (w > flex.Target2)
			w = (flex.Target3 - w) / (flex.Target3 - flex.Target2);
		else
			w = 1.0f;

		return w;
	}

	uint flexVertsWarnCount = 0;
	uint flexConversionTimesWarned = 0;

	private void R_StudioFlexVerts(MStudioMesh mesh, int lod) {
		Assert(mesh != null);

		if (VertexCache.IsFlexComputationDone())
			return;

		if (mesh.Model.CacheVertexData(studioDataCache, StudioHdr!) == null)
			return;

		MStudioMeshVertexData? vertData = mesh.GetVertexData(studioDataCache, StudioHdr!);
		Assert(vertData != null);
		if (vertData == null) {
			if (flexVertsWarnCount++ < 20)
				Warning("ERROR: R_StudioFlexVerts, model verts have been compressed, cannot render! (use \"-no_compressed_vvds\")");
			return;
		}

		Assert((StudioHdr!.Flags & StudioHdrFlags.FlexesConverted) != 0);
		if ((StudioHdr.Flags & StudioHdrFlags.FlexesConverted) == 0) {
			if (flexConversionTimesWarned++ < 6)
				Warning("ERROR: flex verts have not been converted (queued loader refcount bug?) - expect to see 'exploded' faces");
		}

		bool hasTangentS = vertData.HasTangentData();

		VertexCache.SetupComputation(mesh, true);

		int i, j, n;

		for (i = 0; i < mesh.NumFlexes; i++) {
			MStudioFlex flex = mesh.Flex(i);

			float w1 = RampFlexWeight(flex, pFlexWeights[flex.FlexDesc]);
			float w2 = RampFlexWeight(flex, pFlexDelayedWeights[flex.FlexDesc]);

			float w3, w4;
			if (flex.FlexPair != 0) {
				w3 = RampFlexWeight(flex, pFlexWeights[flex.FlexPair]);
				w4 = RampFlexWeight(flex, pFlexDelayedWeights[flex.FlexPair]);
			}
			else {
				w3 = w1;
				w4 = w2;
			}

			if (w1 > -0.001 && w1 < 0.001 && w2 > -0.001 && w2 < 0.001) {
				if (w3 > -0.001 && w3 < 0.001 && w4 > -0.001 && w4 < 0.001)
					continue;
			}

			Span<byte> vanim = flex.BaseVertAnim();
			int vanimSizeBytes = flex.VertAnimSizeBytes();

			for (j = 0; j < flex.NumVerts; j++) {
				ref MStudioVertAnim anim = ref MemoryMarshal.AsRef<MStudioVertAnim>(vanim[(j * vanimSizeBytes)..]);
				n = anim.Index;

				if (n < mesh.VertexData.NumLODVertexes[lod]) {
					ref MStudioVertex vert = ref vertData.Vertex(n);

					ref CachedPosNormTan flexedVertex = ref Unsafe.NullRef<CachedPosNormTan>();
					if (!VertexCache.IsVertexFlexed(n)) {
						flexedVertex = ref VertexCache.CreateFlexVertex(n);
						if (Unsafe.IsNullRef(ref flexedVertex))
							continue;

						flexedVertex.Position = vert.Position;
						flexedVertex.Normal = vert.Normal;

						if (hasTangentS) {
							flexedVertex.TangentS = vertData.TangentS(n);
							Assert(flexedVertex.TangentS.W == -1.0f || flexedVertex.TangentS.W == 1.0f);
						}
					}
					else
						flexedVertex = ref VertexCache.GetFlexVertex(n);

					float s = anim.Speed * (1.0f / 255.0f);
					float b = anim.Side * (1.0f / 255.0f);

					float w = (w1 * s + (1.0f - s) * w2) * (1.0f - b) + b * (w3 * s + (1.0f - s) * w4);

					flexedVertex.Position += anim.GetDeltaFixed() * w;
					flexedVertex.Normal += anim.GetNDeltaFixed() * w;

					if (hasTangentS) {
						flexedVertex.TangentS.AsVector3D() += anim.GetNDeltaFixed() * w;
						Assert(flexedVertex.TangentS.W == -1.0f || flexedVertex.TangentS.W == 1.0f);
					}
				}
			}
		}

		VertexCache.RenormalizeFlexVertices(vertData.HasTangentData());
	}

	private void R_StudioEyeballPosition(MStudioEyeball eyeball, ref EyeballState state) {
		state.Eyeball = eyeball;

		Vector3 tmp = eyeball.Org;
		tmp.X += pRC!.Config.EyeShiftX * MathF.Sign(tmp.X);
		tmp.Y += pRC.Config.EyeShiftY * MathF.Sign(tmp.Y);
		tmp.Z += pRC.Config.EyeShiftZ * MathF.Sign(tmp.Z);

		MathLib.VectorTransform(in tmp, in pBoneToWorld[eyeball.Bone], out state.Org);
		MathLib.VectorRotate(in eyeball.Up, in pBoneToWorld[eyeball.Bone], out state.Up);

		state.Forward = pRC.ViewTarget - state.Org;
		MathLib.VectorNormalize(ref state.Forward);

		if (!pRC.Config.EyeMove) {
			MathLib.VectorRotate(in eyeball.Forward, in pBoneToWorld[eyeball.Bone], out state.Forward);
			state.Forward *= -1;
		}

		state.Right = Vector3.Cross(state.Forward, state.Up);
		MathLib.VectorNormalize(ref state.Right);

		float dz = eyeball.ZOffset;

		state.Forward += (eyeball.ZOffset + dz) * state.Right;

		MathLib.VectorNormalize(ref state.Forward);
		state.Right = Vector3.Cross(state.Forward, state.Up);
		MathLib.VectorNormalize(ref state.Right);

		state.Up = Vector3.Cross(state.Right, state.Forward);
		MathLib.VectorNormalize(ref state.Up);

		float scale = (1.0f / eyeball.IrisScale) + pRC.Config.EyeSize;

		if (scale > 0)
			scale = 1.0f / scale;

		state.Mat.M00 = state.Right.X * -scale;
		state.Mat.M01 = state.Right.Y * -scale;
		state.Mat.M02 = state.Right.Z * -scale;
		state.Mat.M10 = state.Up.X * -scale;
		state.Mat.M11 = state.Up.Y * -scale;
		state.Mat.M12 = state.Up.Z * -scale;

		state.Mat.M03 = -Vector3.Dot(state.Org, new Vector3(state.Mat.M00, state.Mat.M01, state.Mat.M02)) + 0.5f;
		state.Mat.M13 = -Vector3.Dot(state.Org, new Vector3(state.Mat.M10, state.Mat.M11, state.Mat.M12)) + 0.5f;
	}

	private int R_StudioRenderFinal(IMatRenderContext renderContext, int skin, int bodyPartCount, BodyPartInfo[] pBodyPartInfo, object? clientEntity, Span<IMaterial> materials, Span<int> materialFlags, int boneMask, int lod, Span<ColorMeshInfo> colorMeshes) {
		int numTrianglesRendered = 0;

		for (int i = 0; i < bodyPartCount; i++) {
			SubModel = pBodyPartInfo[i].SubModel;

			ComputeEyelidStateFACS(SubModel!);

			VertexCache.SetBodyPart(i);
			VertexCache.SetModel(pBodyPartInfo[i].SubModelIndex);

			numTrianglesRendered += R_StudioDrawPoints(renderContext, skin, clientEntity, materials, materialFlags, boneMask, lod, colorMeshes);
		}
		return numTrianglesRendered;
	}

	private int R_StudioDrawPoints(IMatRenderContext renderContext, int skin, object? clientEntity, Span<IMaterial> materials, Span<int> materialFlagsSpan, int boneMask, int lod, Span<ColorMeshInfo> colorMeshes) {
		int numTrianglesRendered = 0;

		// happens when there's a model load failure
		if (StudioMeshes == null)
			return 0;

		// todo: wireframe translucent thing

		if (pRC!.Config.Skin != 0) {
			skin = pRC.Config.Skin;
			if (skin >= StudioHdr!.NumSkinFamilies)
				skin = 0;
		}

		Span<short> pskinref = StudioHdr!.SkinRef(0);
		if (skin > 0 && skin < StudioHdr!.NumSkinFamilies)
			pskinref = pskinref[(skin * StudioHdr!.NumSkinRef)..];

		for (int i = 0; i < SubModel!.NumMeshes; ++i) {
			MStudioMesh pmesh = SubModel.Mesh(i);
			StudioMeshData pMeshData = StudioMeshes[pmesh.MeshID];
			Assert(pMeshData != null);

			if (pMeshData.NumGroup == 0)
				continue;

			StudioModelLighting lighting = StudioModelLighting.Hardware;
			int materialFlags = materialFlagsSpan[pskinref[pmesh.Material]];

			IMaterial? pMaterial = R_StudioSetupSkinAndLighting(renderContext, pskinref[pmesh.Material], materials, materialFlags, clientEntity, colorMeshes, ref lighting);
			if (pMaterial == null)
				continue;

			VertexCache.SetMesh(i);

			// The following are special cases that can't be covered with
			// the normal static/dynamic methods due to optimization reasons
			switch (pmesh.MaterialType) {
				case 1:
					numTrianglesRendered += R_StudioDrawEyeball(renderContext, pmesh, pMeshData, lighting, pMaterial, lod);
					break;
				default:
					numTrianglesRendered += R_StudioDrawMesh(renderContext, pmesh, pMeshData, lighting, pMaterial, colorMeshes, lod);
					break;
			}
		}

		// Reset this state so it doesn't hose other parts of rendering
		renderContext.SetNumBoneWeights(0);

		return numTrianglesRendered;
	}

	static TokenCache eyeOriginCache;
	static TokenCache eyeUpCache;
	static TokenCache irisUCache;
	static TokenCache irisVCache;
	static TokenCache glintUCache;
	static TokenCache glintVCache;

	private void SetEyeMaterialVars(IMaterial? material, MStudioEyeball eyeball, in Vector3 eyeOrigin, in Matrix3x4 irisTransform, in Matrix3x4 glintTransform) {
		if (material == null)
			return;

		IMaterialVar? var = material.FindVarFast("$eyeorigin", ref eyeOriginCache);
		if (var != null)
			var.SetVecValue(in eyeOrigin);

		var = material.FindVarFast("$eyeup", ref eyeUpCache);
		if (var != null)
			var.SetVecValue(in eyeball.Up);

		var = material.FindVarFast("$irisu", ref irisUCache);
		if (var != null)
			var.SetVecValue(irisTransform.M00, irisTransform.M01, irisTransform.M02, irisTransform.M03);

		var = material.FindVarFast("$irisv", ref irisVCache);
		if (var != null)
			var.SetVecValue(irisTransform.M10, irisTransform.M11, irisTransform.M12, irisTransform.M13);

		var = material.FindVarFast("$glintu", ref glintUCache);
		if (var != null)
			var.SetVecValue(glintTransform.M00, glintTransform.M01, glintTransform.M02, glintTransform.M03);

		var = material.FindVarFast("$glintv", ref glintVCache);
		if (var != null)
			var.SetVecValue(glintTransform.M10, glintTransform.M11, glintTransform.M12, glintTransform.M13);
	}

	private static void ComputeGlintTextureProjection(in EyeballState state, in Vector3 vright, in Vector3 vup, out Matrix3x4 mat) {
		float scale = 1.0f / (state.Eyeball!.Radius * 2);
		mat = default;
		mat.M00 = vright.X * scale;
		mat.M01 = vright.Y * scale;
		mat.M02 = vright.Z * scale;
		mat.M10 = vup.X * scale;
		mat.M11 = vup.Y * scale;
		mat.M12 = vup.Z * scale;

		mat.M03 = -Vector3.Dot(state.Org, new Vector3(mat.M00, mat.M01, mat.M02)) + 0.5f;
		mat.M13 = -Vector3.Dot(state.Org, new Vector3(mat.M10, mat.M11, mat.M12)) + 0.5f;
	}

	static readonly ConVar r_flashlightscissor = new("r_flashlightscissor", "1", 0);

	private void DisableScissor() {
		using MatRenderContextPtr renderContext = new(materialSystem);
		if (r_flashlightscissor.GetBool())
			renderContext.SetScissorRect(-1, -1, -1, -1, false);
	}

	public struct GlintRenderData
	{
		public Vector2 Position;
		public Vector3 Intensity;
	}

	ITexture? GlintTexture;
	ITexture? GlintLODTexture;
	IMaterial? GlintBuildMaterial;
	short GlintWidth;
	short GlintHeight;

	internal void PrecacheGlint() {
		if (GlintTexture == null) {
			materialSystem.BeginRenderTargetAllocation();

			GlintTexture = materialSystem.CreateNamedRenderTargetTextureEx("_rt_eyeglint", 32, 32, RenderTargetSizeMode.NoChange, ImageFormat.BGRA8888, MaterialRenderTargetDepth.None, TextureFlags.ClampS | TextureFlags.ClampT, 0)!;
			GlintTexture.IncrementReferenceCount();

			materialSystem.EndRenderTargetAllocation();

			GlintLODTexture = materialSystem.FindTexture("vgui/black", null, false);
			GlintLODTexture.IncrementReferenceCount();
		}

		if (GlintBuildMaterial == null) {
			KeyValues vmtKeyValues = new("EyeGlint");
			GlintBuildMaterial = materialSystem.CreateMaterial("___glintbuildmaterial", vmtKeyValues);
		}
	}

	private bool R_LightGlintPosition(int index, in Vector3 org, out Vector3 delta, out Vector3 intensity) {
		if (index >= pRC!.NumLocalLights) {
			delta = default;
			intensity = default;
			return false;
		}

		R_WorldLightDelta(in pRC.LocalLights[index], in org, out delta);
		float falloff = R_WorldLightDistanceFalloff(in pRC.LocalLights[index], in delta);

		intensity = pRC.LocalLights[index].Color * falloff;
		return true;
	}

	private int BuildGlintRenderData(Span<GlintRenderData> data, int maxGlints, in EyeballState state, in Vector3 vright, in Vector3 vup, in Vector3 r_origin) {
		Vector3 viewdelta = r_origin - state.Org;
		MathLib.VectorNormalize(ref viewdelta);

		float iris_radius = state.Eyeball!.Radius * (6.0f / 12.0f);
		float cornea_radius = state.Eyeball.Radius * (8.0f / 12.0f);

		float er = iris_radius / state.Eyeball.Radius;
		er = MathF.Sqrt(1 - er * er);

		float cr = iris_radius / cornea_radius;
		cr = MathF.Sqrt(1 - cr * cr);

		float r = er * state.Eyeball.Radius - cr * cornea_radius;
		Vector3 cornea = state.Forward * r;

		float dx = Vector3.Dot(vright, cornea);
		float dy = Vector3.Dot(vup, cornea);

		cornea += state.Org;

		Vector3 reflection;

		int glintCount = 0;
		for (int i = 0; R_LightGlintPosition(i, in cornea, out Vector3 delta, out Vector3 intensity); ++i) {
			MathLib.VectorNormalize(ref delta);
			if (Vector3.Dot(delta, state.Forward) <= 0)
				continue;

			reflection = delta + viewdelta;
			MathLib.VectorNormalize(ref reflection);

			data[glintCount].Position.X = dx + cornea_radius * Vector3.Dot(vright, reflection);
			data[glintCount].Position.Y = dy + cornea_radius * Vector3.Dot(vup, reflection);
			data[glintCount].Intensity = intensity;
			if (++glintCount >= maxGlints)
				return maxGlints;

			if (!R_LightGlintPosition(i, in state.Org, out delta, out intensity))
				continue;

			MathLib.VectorNormalize(ref delta);
			if (Vector3.Dot(delta, state.Forward) >= er)
				continue;

			data[glintCount].Position.X = state.Eyeball.Radius * Vector3.Dot(vright, reflection);
			data[glintCount].Position.Y = state.Eyeball.Radius * Vector3.Dot(vup, reflection);
			data[glintCount].Intensity = intensity;
			if (++glintCount >= maxGlints)
				return maxGlints;
		}
		return glintCount;
	}

	private ITexture? RenderGlintTexture(in EyeballState state, in Vector3 vright, in Vector3 vup, in Vector3 r_origin) {
		Span<GlintRenderData> renderData = stackalloc GlintRenderData[16];
		int glintCount = BuildGlintRenderData(renderData, renderData.Length, in state, in vright, in vup, in r_origin);

		if (glintCount == 0)
			return GlintLODTexture;

		using MatRenderContextPtr renderContext = new(materialSystem);
		renderContext.PushRenderTargetAndViewport(GlintTexture);

		IMaterial? prevMaterial = renderContext.GetCurrentMaterial();
		object? prevProxy = renderContext.GetCurrentProxy();
		int prevBoneCount = renderContext.GetCurrentNumBones();
		MaterialHeightClipMode prevClipMode = renderContext.GetHeightClipMode();
		bool prevClippingEnabled = renderContext.EnableClipping(false);
		bool inFlashlightMode = renderContext.GetFlashlightMode();

		if (inFlashlightMode)
			DisableScissor();

		renderContext.ClearColor4ub(0, 0, 0, 0);
		renderContext.ClearBuffers(true, false, false);

		renderContext.SetFlashlightMode(false);
		renderContext.SetHeightClipMode(MaterialHeightClipMode.Disable);
		renderContext.SetNumBoneWeights(0);
		renderContext.Bind(GlintBuildMaterial!, null);

		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		MeshBuilder meshBuilder = new();
		IMesh mesh = renderContext.GetDynamicMesh();
		meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, glintCount * 4, glintCount * 6);

		const float epsilon = 0.5f / 32.0f;
		int index = 0;
		for (int i = 0; i < glintCount; ++i) {
			ref GlintRenderData glint = ref renderData[i];

			float x = (glint.Position.X + 0.5f) * GlintWidth;
			float y = (glint.Position.Y + 0.5f) * GlintHeight;
			Vector2 glintCenter = new(x, y);
			float ooWidth = 1.0f / GlintWidth;
			float ooHeight = 1.0f / GlintHeight;

			int x0 = (int)MathF.Floor(x);
			int y0 = (int)MathF.Floor(y);
			int x1 = x0 + 1;
			int y1 = y0 + 1;
			x0 -= 2;
			y0 -= 2;

			float screenX0 = x0 * 2 * ooWidth + epsilon - 1;
			float screenX1 = x1 * 2 * ooWidth + epsilon - 1;
			float screenY0 = -(y0 * 2 * ooHeight + epsilon - 1);
			float screenY1 = -(y1 * 2 * ooHeight + epsilon - 1);

			ReadOnlySpan<float> intensity = [glint.Intensity.X, glint.Intensity.Y, glint.Intensity.Z];

			meshBuilder.Position3f(screenX0, screenY0, 0.0f);
			meshBuilder.TexCoord2f(0, x0, y0);
			meshBuilder.TexCoord2fv(1, in glintCenter);
			meshBuilder.TexCoord3fv(2, intensity);
			meshBuilder.AdvanceVertex();

			meshBuilder.Position3f(screenX1, screenY0, 0.0f);
			meshBuilder.TexCoord2f(0, x1, y0);
			meshBuilder.TexCoord2fv(1, in glintCenter);
			meshBuilder.TexCoord3fv(2, intensity);
			meshBuilder.AdvanceVertex();

			meshBuilder.Position3f(screenX1, screenY1, 0.0f);
			meshBuilder.TexCoord2f(0, x1, y1);
			meshBuilder.TexCoord2fv(1, in glintCenter);
			meshBuilder.TexCoord3fv(2, intensity);
			meshBuilder.AdvanceVertex();

			meshBuilder.Position3f(screenX0, screenY1, 0.0f);
			meshBuilder.TexCoord2f(0, x0, y1);
			meshBuilder.TexCoord2fv(1, in glintCenter);
			meshBuilder.TexCoord3fv(2, intensity);
			meshBuilder.AdvanceVertex();

			meshBuilder.FastIndex((ushort)index);
			meshBuilder.FastIndex((ushort)(index + 1));
			meshBuilder.FastIndex((ushort)(index + 2));
			meshBuilder.FastIndex((ushort)index);
			meshBuilder.FastIndex((ushort)(index + 2));
			meshBuilder.FastIndex((ushort)(index + 3));
			index += 4;
		}

		meshBuilder.End();
		mesh.Draw();

		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PopMatrix();

		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PopMatrix();

		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PopMatrix();

		renderContext.PopRenderTargetAndViewport();

		renderContext.Bind(prevMaterial!, prevProxy);
		renderContext.SetNumBoneWeights(prevBoneCount);
		renderContext.SetHeightClipMode(prevClipMode);
		renderContext.EnableClipping(prevClippingEnabled);
		renderContext.SetFlashlightMode(inFlashlightMode);

		return GlintTexture;
	}

	static readonly ConVar r_glint_alwaysdraw = new("r_glint_alwaysdraw", "0");

	private void R_StudioEyeballGlint(in EyeballState state, IMaterialVar glintVar, in Vector3 vright, in Vector3 vup, in Vector3 r_origin) {
		using MatRenderContextPtr renderContext = new(materialSystem);

		if (GlintLODTexture != null && r_glint_alwaysdraw.GetInt() == 0) {
			float pixelArea = renderContext.ComputePixelWidthOfSphere(state.Org, state.Eyeball!.Radius);
			if (pixelArea < pRC!.Config.EyeGlintPixelWidthLODThreshold) {
				glintVar.SetTextureValue(GlintLODTexture);
				return;
			}
		}

		GlintWidth = (short)GlintTexture!.GetActualWidth();
		GlintHeight = (short)GlintTexture.GetActualHeight();

		ITexture? useGlintTexture = RenderGlintTexture(in state, in vright, in vup, in r_origin);

		glintVar.SetTextureValue(useGlintTexture);
	}

	static TokenCache glintCache;

	private int R_StudioDrawEyeball(IMatRenderContext renderContext, MStudioMesh pmesh, StudioMeshData pMeshData, StudioModelLighting lighting, IMaterial pMaterial, int lod) {
		if (!pRC!.Config.Eyes)
			return 0;

		MStudioMeshVertexData? vertData = GetFatVertexData(pmesh, StudioHdr!);
		if (vertData == null)
			return 0;

		int j;
		int numTrianglesRendered = 0;

		bool isDeltaFlexed = false;
		bool isHardwareSkinnedData = false;
		bool isFlexed = false;
		for (j = 0; j < pMeshData.NumGroup; ++j) {
			StudioMeshGroup pGroup = pMeshData.MeshGroup![j];

			if ((pGroup.Flags & StudioMeshGroupFlags.IsDeltaFlexed) != 0 && hardwareConfig.SupportsStreamOffset())
				isDeltaFlexed = true;

			if ((pGroup.Flags & StudioMeshGroupFlags.IsFlexed) != 0)
				isFlexed = true;

			if ((pGroup.Flags & StudioMeshGroupFlags.IsHWSkinned) != 0)
				isHardwareSkinnedData = true;
		}

		bool flexStatic = isDeltaFlexed && hardwareConfig.SupportsStreamOffset();
		bool shouldHardwareSkin = isHardwareSkinnedData && (!isFlexed || flexStatic) &&
			(lighting != StudioModelLighting.Software) && !pRC.Config.SoftwareSkin;

		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.LoadIdentity();

		if (isFlexed && (!flexStatic || !shouldHardwareSkin))
			R_StudioFlexVerts(pmesh, lod);

		MStudioEyeball eyeball = SubModel!.Eyeball(pmesh.MaterialParam);

		MathLib.VectorTransform(in eyeball.Org, in pBoneToWorld[eyeball.Bone], out Vector3 org);

		ComputeGlintTextureProjection(in EyeballStates[pmesh.MaterialParam], in pRC.ViewRight, in pRC.ViewUp, out Matrix3x4 glintMat);

		if (!pRC.Config.Wireframe) {
			IMaterialVar? glintVar = pMaterial.FindVarFast("$glint", ref glintCache);
			if (glintVar != null)
				R_StudioEyeballGlint(in EyeballStates[pmesh.MaterialParam], glintVar, in pRC.ViewRight, in pRC.ViewUp, in pRC.ViewOrigin);
			SetEyeMaterialVars(pMaterial, eyeball, in org, in EyeballStates[pmesh.MaterialParam].Mat, in glintMat);
		}

		if (shouldHardwareSkin) {
			for (j = 0; j < pMeshData.NumGroup; ++j) {
				StudioMeshGroup pGroup = pMeshData.MeshGroup![j];
				numTrianglesRendered += R_StudioDrawStaticMesh(renderContext, pmesh, pGroup, lighting, pRC.AlphaMod, pMaterial, lod, default);
			}

			return numTrianglesRendered;
		}

		renderContext.SetNumBoneWeights(0);
		VertexCache.SetupComputation(pmesh);

		int alphaInt = MathLib.RoundFloatToInt(pRC.AlphaMod * 255);
		byte a = (byte)Math.Clamp(alphaInt, 0, 255);

		MeshBuilder meshBuilder = new();

		bool useHWLighting = pRC.Config.SupportsVertexAndPixelShaders && !pRC.Config.SoftwareLighting;
		for (j = 0; j < pMeshData.NumGroup; ++j) {
			StudioMeshGroup pGroup = pMeshData.MeshGroup![j];

			IMesh mesh = renderContext.GetDynamicMesh(false, null, pGroup.Mesh);

			meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, pmesh.NumVertices, 0);

			for (int i = 0; i < pGroup.NumVertices; ++i) {
				int n = pGroup.GroupIndexToMeshIndex![i];
				ref MStudioVertex vert = ref vertData.Vertex(n);

				ref CachedPosNorm worldVert = ref VertexCache.CreateWorldVertex(n);

				if (VertexCache.IsVertexFlexed(n)) {
					ref CachedPosNormTan flexVert = ref VertexCache.GetFlexVertex(n);
					R_StudioTransform(in flexVert.Position, in vert.BoneWeights, out worldVert.Position.AsVector3D());
					R_StudioRotate(in flexVert.Normal, in vert.BoneWeights, out worldVert.Normal.AsVector3D());
					Assert(worldVert.Normal.X >= -1.05f && worldVert.Normal.X <= 1.05f);
					Assert(worldVert.Normal.Y >= -1.05f && worldVert.Normal.Y <= 1.05f);
					Assert(worldVert.Normal.Z >= -1.05f && worldVert.Normal.Z <= 1.05f);
				}
				else {
					R_StudioTransform(in vert.Position, in vert.BoneWeights, out worldVert.Position.AsVector3D());
					R_StudioRotate(in vert.Normal, in vert.BoneWeights, out worldVert.Normal.AsVector3D());
					Assert(worldVert.Normal.X >= -1.05f && worldVert.Normal.X <= 1.05f);
					Assert(worldVert.Normal.Y >= -1.05f && worldVert.Normal.Y <= 1.05f);
					Assert(worldVert.Normal.Z >= -1.05f && worldVert.Normal.Z <= 1.05f);
				}

				meshBuilder.Position3fv(in worldVert.Position.AsVector3D());

				if (useHWLighting)
					meshBuilder.Normal3fv(in worldVert.Normal.AsVector3D());
				else {
					R_StudioEyeballNormal(eyeball, in org, in worldVert.Position.AsVector3D(), out worldVert.Normal.AsVector3D());

					meshBuilder.Normal3fv(in worldVert.Normal.AsVector3D());
					R_ComputeLightAtPoint3(in worldVert.Position.AsVector3D(), in worldVert.Normal.AsVector3D(), out Vector3 color);

					byte r = MathLib.LinearToLightmap(color.X);
					byte g = MathLib.LinearToLightmap(color.Y);
					byte b = MathLib.LinearToLightmap(color.Z);

					meshBuilder.Color4ub(r, g, b, a);
				}

				meshBuilder.TexCoord2fv(0, in vert.TexCoord);

				meshBuilder.BoneWeight(0, 1.0f);
				meshBuilder.BoneWeight(1, 0.0f);
				meshBuilder.BoneWeight(2, 0.0f);
				meshBuilder.BoneWeight(3, 0.0f);
				meshBuilder.BoneMatrix(0, 0);
				meshBuilder.BoneMatrix(1, 0);
				meshBuilder.BoneMatrix(2, 0);
				meshBuilder.BoneMatrix(3, 0);
				meshBuilder.AdvanceVertex();
			}

			meshBuilder.End();
			mesh.Draw();
		}

		return numTrianglesRendered;
	}

	private void R_StudioTransform(in Vector3 in1, in MStudioBoneWeight boneweight, out Vector3 out1) {
		switch (boneweight.NumBones) {
			case 1:
				MathLib.VectorTransform(in in1, in PoseToWorld[boneweight.Bone[0]], out out1);
				break;

			default:
				out1 = default;
				for (int i = 0; i < boneweight.NumBones; i++) {
					MathLib.VectorTransform(in in1, in PoseToWorld[boneweight.Bone[i]], out Vector3 out2);
					MathLib.VectorMA(out1, boneweight.Weight[i], out2, out out1);
				}
				break;
		}
	}

	private void R_StudioRotate(in Vector3 in1, in MStudioBoneWeight boneweight, out Vector3 out1) {
		if (boneweight.NumBones == 1)
			MathLib.VectorRotate(in in1, in PoseToWorld[boneweight.Bone[0]], out out1);
		else {
			out1 = default;

			for (int i = 0; i < boneweight.NumBones; i++) {
				MathLib.VectorRotate(in in1, in PoseToWorld[boneweight.Bone[i]], out Vector3 out2);
				MathLib.VectorMA(out1, boneweight.Weight[i], out2, out out1);
			}
			MathLib.VectorNormalize(ref out1);
		}
	}

	private static void R_StudioEyeballNormal(MStudioEyeball eyeball, in Vector3 org, in Vector3 pos, out Vector3 normal) {
		normal = pos - org;
		float upAmount = Vector3.Dot(normal, eyeball.Up);
		MathLib.VectorMA(normal, -0.5f * upAmount, eyeball.Up, out normal);
		MathLib.VectorNormalize(ref normal);
	}

	private int R_StudioDrawMesh(IMatRenderContext renderContext, MStudioMesh pmesh, StudioMeshData pMeshData, StudioModelLighting lighting, IMaterial pMaterial, Span<ColorMeshInfo> colorMeshes, int lod) {
		int numTrianglesRendered = 0;

		for (int j = 0; j < pMeshData.NumGroup; ++j) {
			StudioMeshGroup pGroup = pMeshData.MeshGroup![j];

			bool bIsFlexed = (pGroup.Flags & StudioMeshGroupFlags.IsFlexed) != 0;
			bool bIsDeltaFlexed = (pGroup.Flags & StudioMeshGroupFlags.IsDeltaFlexed) != 0;

			bool bFlexStatic = bIsDeltaFlexed && hardwareConfig.SupportsStreamOffset();

			bool bIsHardwareSkinnedData = (pGroup.Flags & StudioMeshGroupFlags.IsHWSkinned) != 0;
			bool bShouldHardwareSkin = bIsHardwareSkinnedData && (!bIsFlexed || bFlexStatic) && (lighting != StudioModelLighting.Software);

			if (bShouldHardwareSkin && !pRC!.Config.DrawNormals && !pRC!.Config.DrawTangentFrame && !pRC!.Config.Wireframe) {
				if (!pRC!.Config.NoHardware)
					numTrianglesRendered += R_StudioDrawStaticMesh(renderContext, pmesh, pGroup, lighting, pRC.AlphaMod, pMaterial, lod, colorMeshes);
			}
			else {
				if (!pRC!.Config.NoSoftware)
					numTrianglesRendered += R_StudioDrawDynamicMesh(renderContext, pmesh, pGroup, lighting, pRC.AlphaMod, pMaterial, lod);
			}
		}
		return numTrianglesRendered;
	}

	private int R_StudioDrawStaticMesh(IMatRenderContext renderContext, MStudioMesh pmesh, StudioMeshGroup pGroup, StudioModelLighting lighting, float alphaMod, IMaterial pMaterial, int lod, Span<ColorMeshInfo> colorMeshes) {
		int numTrianglesRendered = 0;

		bool bDoSoftwareLighting = colorMeshes.IsEmpty &&
			((pRC!.Config.SoftwareSkin) || pRC.Config.DrawNormals || pRC.Config.DrawTangentFrame ||
			// (pMaterial != null ? pMaterial.NeedsSoftwareSkinning() : false) ||
			(pRC.Config.SoftwareLighting) ||
			((lighting != StudioModelLighting.Hardware) && (lighting != StudioModelLighting.Mouth)));

		if (bDoSoftwareLighting) {
			if (pRC.Config.NoSoftware)
				return 0;

			bool needsTangentSpace = pMaterial != null ? pMaterial.NeedsTangentSpace() : false;
			renderContext.MatrixMode(MaterialMatrixMode.Model);
			renderContext.LoadIdentity();

			VertexFormat fmt = ComputeSWSkinVertexFormat(pMaterial!);
			bool dx8Vertex = fmt.GetUserDataSize() != 0;

			IMesh mesh = renderContext.GetDynamicMeshEx(fmt, false, null, pGroup.Mesh);

			MeshBuilder meshBuilder = new();
			meshBuilder.Begin(mesh, MaterialPrimitiveType.Heterogenous, pGroup.NumVertices, 0);

			R_StudioSoftwareProcessMesh(pmesh, ref meshBuilder, pGroup.NumVertices, pGroup.GroupIndexToMeshIndex!, lighting, false, alphaMod, needsTangentSpace, dx8Vertex, pMaterial!);

			meshBuilder.End();

			return R_StudioDrawGroupSWSkin(renderContext, pGroup, mesh);
		}

		// Needed when we switch back and forth between hardware + software lighting
		// TODO ^^^^^^^^^^^^^^^^^^

		bool useSOFlex = hardwareConfig.SupportsStreamOffset();
		if ((pGroup.Flags & StudioMeshGroupFlags.IsDeltaFlexed) != 0 && pRC.Config.Flex) {
			if (useSOFlex) {
				R_StudioProcessFlexedMesh_StreamOffset(pmesh, lod);
				R_StudioFlexMeshGroup(pGroup);
			}
		}

		if (!colorMeshes.IsEmpty && (pGroup.ColorMeshID != -1))
			numTrianglesRendered = R_StudioDrawGroupHWSkin(renderContext, pGroup, pGroup.Mesh, ref colorMeshes[pGroup.ColorMeshID]);
		else
			numTrianglesRendered = R_StudioDrawGroupHWSkin(renderContext, pGroup, pGroup.Mesh, ref Unsafe.NullRef<ColorMeshInfo>());

		if ((pGroup.Flags & StudioMeshGroupFlags.IsDeltaFlexed) != 0 && pRC.Config.Flex) {
			if (useSOFlex)
				pGroup.Mesh!.DisableFlexMesh();
		}

		return numTrianglesRendered;
	}

	private int R_StudioDrawGroupHWSkin(IMatRenderContext renderContext, StudioMeshGroup pGroup, IMesh? mesh, ref ColorMeshInfo colorMeshInfo) {
		int numTrianglesRendered = 0;

		if (StudioHdr!.NumBones == 1) {
			renderContext.MatrixMode(MaterialMatrixMode.Model);
			renderContext.LoadMatrix(PoseToWorld[0]);

			renderContext.SetNumBoneWeights(0);
		}

		if (!Unsafe.IsNullRef(ref colorMeshInfo))
			mesh!.SetColorMesh(colorMeshInfo.Mesh!, colorMeshInfo.VertOffsetInBytes);
		else
			mesh!.SetColorMesh(null!, 0);

		for (int j = 0; j < pGroup.NumStrips; ++j) {
			OptimizedModel.StripHeader pStrip = pGroup.StripData![j];

			if (StudioHdr.NumBones > 1) {
				renderContext.SetNumBoneWeights(pStrip.NumBones);

				for (int k = 0; k < pStrip.NumBoneStateChanges; ++k) {
					ref OptimizedModel.BoneStateChangeHeader pStateChange = ref pStrip.BoneStateChange(k);
					if (pStateChange.NewBoneID < 0)
						break;

					renderContext.LoadBoneMatrix(pStateChange.HardwareID, in PoseToWorld[pStateChange.NewBoneID]);
				}
			}

			mesh.SetPrimitiveType((pStrip.Flags & OptimizedModel.StripHeaderFlags.IsTriStrip) != 0 ? MaterialPrimitiveType.TriangleStrip : MaterialPrimitiveType.Triangles);

			mesh.Draw(pStrip.IndexOffset, pStrip.NumIndices);
			numTrianglesRendered += 0; // TODO: uniquetris
		}
		mesh.SetColorMesh(null, 0);

		return numTrianglesRendered;
	}

	private int R_StudioDrawDynamicMesh(IMatRenderContext renderContext, MStudioMesh pmesh, StudioMeshGroup pGroup, StudioModelLighting lighting, float alphaMod, IMaterial pMaterial, int lod) {
		bool doFlex = ((pGroup.Flags & StudioMeshGroupFlags.IsFlexed) != 0) && pRC!.Config.Flex;

		bool doSoftwareLighting = (pRC!.Config.SoftwareLighting) ||
			((lighting != StudioModelLighting.Hardware) && (lighting != StudioModelLighting.Mouth));

		bool swSkin = doSoftwareLighting || pRC.Config.DrawNormals || pRC.Config.DrawTangentFrame ||
			((pGroup.Flags & StudioMeshGroupFlags.IsHWSkinned) == 0) ||
			pRC.Config.SoftwareSkin ||
			(pMaterial != null ? pMaterial.NeedsSoftwareSkinning() : false);

		if (!doFlex && !swSkin)
			return R_StudioDrawStaticMesh(renderContext, pmesh, pGroup, lighting, alphaMod, pMaterial, lod, default);

		MStudioMeshVertexData? vertData = GetFatVertexData(pmesh, StudioHdr!);
		if (vertData == null)
			return 0;

		int numTrianglesRendered = 0;

		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.LoadIdentity();

		if (doFlex)
			R_StudioFlexVerts(pmesh, lod);

		bool needsTangentSpace = pMaterial != null ? pMaterial.NeedsTangentSpace() : false;

		VertexFormat fmt = ComputeSWSkinVertexFormat(pMaterial!);
		bool dx8Vertex = fmt.GetUserDataSize() != 0;

		IMesh mesh = renderContext.GetDynamicMeshEx(fmt, false, null, pGroup.Mesh);

		MeshBuilder meshBuilder = new();
		meshBuilder.Begin(mesh, MaterialPrimitiveType.Heterogenous, pGroup.NumVertices, 0);

		if (swSkin)
			R_StudioSoftwareProcessMesh(pmesh, ref meshBuilder, pGroup.NumVertices, pGroup.GroupIndexToMeshIndex!, lighting, doFlex, alphaMod, needsTangentSpace, dx8Vertex, pMaterial!);
		else if (doFlex)
			R_StudioProcessFlexedMesh(pmesh, ref meshBuilder, pGroup.NumVertices, pGroup.GroupIndexToMeshIndex!);

		meshBuilder.End();

		if (!swSkin)
			numTrianglesRendered = R_StudioDrawGroupHWSkin(renderContext, pGroup, mesh, ref Unsafe.NullRef<ColorMeshInfo>());
		else
			numTrianglesRendered = R_StudioDrawGroupSWSkin(renderContext, pGroup, mesh);

		// todo

		return numTrianglesRendered;
	}

	private int R_StudioDrawGroupSWSkin(IMatRenderContext renderContext, StudioMeshGroup pGroup, IMesh mesh) {
		int numTrianglesRendered = 0;

		renderContext.SetNumBoneWeights(0);

		for (int j = 0; j < pGroup.NumStrips; ++j) {
			OptimizedModel.StripHeader strip = pGroup.StripData![j];

			mesh.SetPrimitiveType((strip.Flags & OptimizedModel.StripHeaderFlags.IsTriStrip) != 0 ? MaterialPrimitiveType.TriangleStrip : MaterialPrimitiveType.Triangles);

			mesh.Draw(strip.IndexOffset, strip.NumIndices);
			numTrianglesRendered += 0; // TODO: uniquetris
		}

		return numTrianglesRendered;
	}

	private static Matrix3x4 ComputeSkinMatrix(in MStudioBoneWeight boneWeights, Span<Matrix3x4> poseToWorld) {
		Matrix3x4 result;
		switch (boneWeights.NumBones) {
			default:
			case 1:
				return poseToWorld[boneWeights.Bone[0]];

			case 2: {
					ref Matrix3x4 boneMat0 = ref poseToWorld[boneWeights.Bone[0]];
					ref Matrix3x4 boneMat1 = ref poseToWorld[boneWeights.Bone[1]];
					float weight0 = boneWeights.Weight[0];
					float weight1 = boneWeights.Weight[1];

					result = default;
					for (int r = 0; r < 3; ++r)
						for (int c = 0; c < 4; ++c)
							result[r, c] = boneMat0[r, c] * weight0 + boneMat1[r, c] * weight1;
					return result;
				}

			case 3: {
					ref Matrix3x4 boneMat0 = ref poseToWorld[boneWeights.Bone[0]];
					ref Matrix3x4 boneMat1 = ref poseToWorld[boneWeights.Bone[1]];
					ref Matrix3x4 boneMat2 = ref poseToWorld[boneWeights.Bone[2]];
					float weight0 = boneWeights.Weight[0];
					float weight1 = boneWeights.Weight[1];
					float weight2 = boneWeights.Weight[2];

					result = default;
					for (int r = 0; r < 3; ++r)
						for (int c = 0; c < 4; ++c)
							result[r, c] = boneMat0[r, c] * weight0 + boneMat1[r, c] * weight1 + boneMat2[r, c] * weight2;
					return result;
				}
		}
	}

	private void R_PerformLighting(in Vector3 forward, float illum, in Vector3 pos, in Vector3 norm, uint alphaMask, out uint color, StudioModelLighting lighting) {
		if (lighting == StudioModelLighting.Software) {
			R_ComputeLightAtPoint3(in pos, in norm, out Vector3 lightColor);

			byte r = MathLib.LinearToLightmap(lightColor.X);
			byte g = MathLib.LinearToLightmap(lightColor.Y);
			byte b = MathLib.LinearToLightmap(lightColor.Z);

			color = (uint)(b | (g << 8) | (r << 16)) | alphaMask;
		}
		else if (lighting == StudioModelLighting.Mouth) {
			if (illum != 0.0f) {
				R_ComputeLightAtPoint3(in pos, in norm, out Vector3 lightColor);
				// todo R_MouthLighting

				byte r = MathLib.LinearToLightmap(lightColor.X);
				byte g = MathLib.LinearToLightmap(lightColor.Y);
				byte b = MathLib.LinearToLightmap(lightColor.Z);

				color = (uint)(b | (g << 8) | (r << 16)) | alphaMask;
			}
			else
				color = alphaMask;
		}
		else
			color = alphaMask;
	}

	private static void R_TransformVert(in Vector3 srcPos, in Vector3 srcNorm, in Vector4 srcTangentS, in Matrix3x4 skinMat, out Vector3 pos, out Vector3 norm, out Vector4 tangentS, bool hasTangentSpace) {
		MathLib.VectorTransform(in srcPos, in skinMat, out pos);
		MathLib.VectorRotate(in srcNorm, in skinMat, out norm);

		if (hasTangentSpace) {
			MathLib.VectorRotate(new Vector3(srcTangentS.X, srcTangentS.Y, srcTangentS.Z), in skinMat, out Vector3 rotated);
			tangentS = new Vector4(rotated.X, rotated.Y, rotated.Z, srcTangentS.W);
		}
		else
			tangentS = new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
	}

	private void R_StudioSoftwareProcessMesh(MStudioMesh mesh, ref MeshBuilder meshBuilder, int numVertices, ushort[] groupToMesh, StudioModelLighting lighting, bool doFlex, float blend, bool needsTangentSpace, bool dx8Vertex, IMaterial material) {
		uint alphaMask = (uint)MathLib.RoundFloatToInt(blend * 255.0f);
		alphaMask = Math.Clamp(alphaMask, 0u, 255u);
		alphaMask <<= 24;

		MStudioMeshVertexData? vertData = GetFatVertexData(mesh, StudioHdr!);
		if (vertData != null)
			R_StudioSoftwareProcessMesh(vertData, PoseToWorld, VertexCache, ref meshBuilder, numVertices, groupToMesh, alphaMask, lighting, material, needsTangentSpace, doFlex, dx8Vertex);
	}

	private void R_StudioSoftwareProcessMesh(MStudioMeshVertexData vertData, Span<Matrix3x4> poseToWorld, CachedRenderData vertexCache, ref MeshBuilder meshBuilder, int numVertices, ushort[] groupToMesh, uint alphaMask, StudioModelLighting lighting, IMaterial material, bool hasTangentSpace, bool doFlex, bool dx8Vertex) {
		Assert(numVertices > 0);

		float illum = 1.0f;
		Vector3 forward = default;
		// todo

		for (int j = 0; j < numVertices; ++j) {
			int n = groupToMesh[j];
			ref MStudioVertex vert = ref vertData.Vertex(n);

			Matrix3x4 skinMat = ComputeSkinMatrix(in vert.BoneWeights, poseToWorld);

			Vector3 pos;
			Vector3 norm;
			Vector4 tangentS;
			if (doFlex && vertexCache.IsVertexFlexed(n)) {
				ref CachedPosNormTan flexedVertex = ref vertexCache.GetFlexVertex(n);
				if (hasTangentSpace)
					Assert(flexedVertex.TangentS.W == -1.0f || flexedVertex.TangentS.W == 1.0f);

				R_TransformVert(in flexedVertex.Position, in flexedVertex.Normal, in flexedVertex.TangentS, in skinMat, out pos, out norm, out tangentS, hasTangentSpace);
			}
			else {
				Vector4 srcTangentS = hasTangentSpace ? vertData.TangentS(n) : default;
				R_TransformVert(in vert.Position, in vert.Normal, in srcTangentS, in skinMat, out pos, out norm, out tangentS, hasTangentSpace);
			}

			R_PerformLighting(in forward, illum, in pos, in norm, alphaMask, out uint color, lighting);

			meshBuilder.Position3fv(in pos);
			meshBuilder.Normal3fv(in norm);
			meshBuilder.Color4ubv([(byte)(color >> 16), (byte)(color >> 8), (byte)color, (byte)(color >> 24)]);
			meshBuilder.TexCoord2fv(0, in vert.TexCoord);
			if (dx8Vertex)
				meshBuilder.UserData(in tangentS);
			meshBuilder.AdvanceVertex();
		}
	}

	private void ComputeFlexedVertex_StreamOffset(MStudioFlex flex, int vertCount, float w1, float w2, float w3, float w4) {
		float w12 = w1 - w2;
		float w34 = w3 - w4;

		bool wrinkle = flex.VertAnimType != StudioVertAnimType.Normal;
		for (int j = 0; j < flex.NumVerts; j++) {
			int n;
			float s, b;
			Vector4 position, normal;
			if (wrinkle) {
				ref MStudioVertAnimWrinkle anim = ref flex.VertAnimWrinkle(j);
				n = anim.VertAnim.Index;
				s = anim.VertAnim.Speed;
				b = anim.VertAnim.Side;
				position = anim.GetDeltaFixed4DAligned();
				normal = anim.GetNDeltaFixed4DAligned();
			}
			else {
				ref MStudioVertAnim anim = ref flex.VertAnim(j);
				n = anim.Index;
				s = anim.Speed;
				b = anim.Side;
				position = anim.GetDeltaFixed4DAligned();
				normal = anim.GetNDeltaFixed4DAligned();
			}

			if (n >= vertCount)
				continue;

			ref CachedPosNorm flexedVertex = ref Unsafe.NullRef<CachedPosNorm>();
			if (!VertexCache.IsThinVertexFlexed(n)) {
				flexedVertex = ref VertexCache.CreateThinFlexVertex(n);

				Assert(!Unsafe.IsNullRef(ref flexedVertex));
				if (Unsafe.IsNullRef(ref flexedVertex))
					continue;

				flexedVertex.Position = Vector4.Zero;
				flexedVertex.Normal = Vector4.Zero;
			}
			else
				flexedVertex = ref VertexCache.GetThinFlexVertex(n);

			s *= 1.0f / 255.0f;
			b *= 1.0f / 255.0f;

			float wa = w2 + w12 * s;
			float wb = w4 + w34 * s;
			float w = wa + (wb - wa) * b;
			flexedVertex.Position += position * w;
			flexedVertex.Normal += normal * w;
		}
	}

	private void R_StudioProcessFlexedMesh_StreamOffset(MStudioMesh mesh, int lod) {
		if (VertexCache.IsFlexComputationDone())
			return;

		int vertCount = mesh.VertexData.NumLODVertexes[lod];
		VertexCache.SetupComputation(mesh, true);

		for (int i = 0; i < mesh.NumFlexes; i++) {
			MStudioFlex flex = mesh.Flex(i);

			float w1 = RampFlexWeight(flex, pFlexWeights[flex.FlexDesc]);
			float w2 = RampFlexWeight(flex, pFlexDelayedWeights[flex.FlexDesc]);

			float w3, w4;
			if (flex.FlexPair != 0) {
				w3 = RampFlexWeight(flex, pFlexWeights[flex.FlexPair]);
				w4 = RampFlexWeight(flex, pFlexDelayedWeights[flex.FlexPair]);
			}
			else {
				w3 = w1;
				w4 = w2;
			}

			if (w1 > -0.001 && w1 < 0.001 && w2 > -0.001 && w2 < 0.001) {
				if (w3 > -0.001 && w3 < 0.001 && w4 > -0.001 && w4 < 0.001)
					continue;
			}

			ComputeFlexedVertex_StreamOffset(flex, vertCount, w1, w2, w3, w4);
		}
	}

	private void R_StudioFlexMeshGroup(StudioMeshGroup pGroup) {
		MeshBuilder meshBuilder = new();
		using MatRenderContextPtr renderContext = new(materialSystem);
		IMesh mesh = renderContext.GetFlexMesh();
		meshBuilder.Begin(mesh, MaterialPrimitiveType.Heterogenous, pGroup.NumVertices, 0, out int vertexOffsetInBytes);

		for (int j = 0; j < pGroup.NumVertices; j++) {
			int n = pGroup.GroupIndexToMeshIndex![j];
			if (VertexCache.IsThinVertexFlexed(n)) {
				ref CachedPosNorm flexedVertex = ref VertexCache.GetThinFlexVertex(n);
				meshBuilder.Position3fv(in flexedVertex.Position.AsVector3D());
				meshBuilder.NormalDelta3fv(in flexedVertex.Normal.AsVector3D());
				meshBuilder.Wrinkle1f(flexedVertex.Position.W);
			}
			else {
				meshBuilder.Position3f(0.0f, 0.0f, 0.0f);
				meshBuilder.NormalDelta3f(0.0f, 0.0f, 0.0f);
				meshBuilder.Wrinkle1f(0.0f);
			}
			meshBuilder.AdvanceVertex();
		}

		meshBuilder.End(false, false);

		pGroup.Mesh!.SetFlexMesh(mesh, vertexOffsetInBytes);
	}

	private void R_StudioProcessFlexedMesh(MStudioMesh mesh, ref MeshBuilder meshBuilder, int numVertices, ushort[] groupToMesh) {
		MStudioMeshVertexData? vertData = GetFatVertexData(mesh, StudioHdr!);
		if (vertData == null)
			return;

		if (vertData.HasTangentData()) {
			for (int j = 0; j < numVertices; j++) {
				int n = groupToMesh[j];
				ref MStudioVertex vert = ref vertData.Vertex(n);

				if (VertexCache.IsVertexFlexed(n)) {
					ref CachedPosNormTan flexedVertex = ref VertexCache.GetFlexVertex(n);
					meshBuilder.Position3fv(in flexedVertex.Position);
					meshBuilder.BoneWeight(0, 1.0f);
					meshBuilder.BoneWeight(1, 0.0f);
					meshBuilder.BoneWeight(2, 0.0f);
					meshBuilder.BoneWeight(3, 0.0f);
					meshBuilder.BoneMatrix(0, 0);
					meshBuilder.BoneMatrix(1, 0);
					meshBuilder.BoneMatrix(2, 0);
					meshBuilder.BoneMatrix(3, 0);
					meshBuilder.Normal3fv(in flexedVertex.Normal);
					meshBuilder.TexCoord2fv(0, in vert.TexCoord);
					Assert(flexedVertex.TangentS.W == -1.0f || flexedVertex.TangentS.W == 1.0f);
					meshBuilder.UserData(in flexedVertex.TangentS);
				}
				else {
					meshBuilder.Position3fv(in vert.Position);
					meshBuilder.BoneWeight(0, 1.0f);
					meshBuilder.BoneWeight(1, 0.0f);
					meshBuilder.BoneWeight(2, 0.0f);
					meshBuilder.BoneWeight(3, 0.0f);
					meshBuilder.BoneMatrix(0, 0);
					meshBuilder.BoneMatrix(1, 0);
					meshBuilder.BoneMatrix(2, 0);
					meshBuilder.BoneMatrix(3, 0);
					meshBuilder.Normal3fv(in vert.Normal);
					meshBuilder.TexCoord2fv(0, in vert.TexCoord);
					Assert(vertData.TangentS(n).W == -1.0f || vertData.TangentS(n).W == 1.0f);
					meshBuilder.UserData(in vertData.TangentS(n));
				}

				meshBuilder.AdvanceVertex();
			}
		}
		else {
			for (int j = 0; j < numVertices; j++) {
				int n = groupToMesh[j];
				ref MStudioVertex vert = ref vertData.Vertex(n);

				if (VertexCache.IsVertexFlexed(n)) {
					ref CachedPosNormTan flexedVertex = ref VertexCache.GetFlexVertex(n);
					meshBuilder.Position3fv(in flexedVertex.Position);
					meshBuilder.BoneWeight(0, 1.0f);
					meshBuilder.BoneWeight(1, 0.0f);
					meshBuilder.BoneWeight(2, 0.0f);
					meshBuilder.BoneWeight(3, 0.0f);
					meshBuilder.BoneMatrix(0, 0);
					meshBuilder.BoneMatrix(1, 0);
					meshBuilder.BoneMatrix(2, 0);
					meshBuilder.BoneMatrix(3, 0);
					meshBuilder.Normal3fv(in flexedVertex.Normal);
				}
				else {
					meshBuilder.Position3fv(in vert.Position);
					meshBuilder.BoneWeight(0, 1.0f);
					meshBuilder.BoneWeight(1, 0.0f);
					meshBuilder.BoneWeight(2, 0.0f);
					meshBuilder.BoneWeight(3, 0.0f);
					meshBuilder.BoneMatrix(0, 0);
					meshBuilder.BoneMatrix(1, 0);
					meshBuilder.BoneMatrix(2, 0);
					meshBuilder.BoneMatrix(3, 0);
					meshBuilder.Normal3fv(in vert.Normal);
				}
				meshBuilder.TexCoord2fv(0, in vert.TexCoord);
				meshBuilder.AdvanceVertex();
			}
		}
	}

	uint fatVertexWarnCount = 0;

	private MStudioMeshVertexData? GetFatVertexData(MStudioMesh mesh, StudioHeader studioHdr) {
		if (mesh.Model.CacheVertexData(studioDataCache, studioHdr) == null)
			return null;

		MStudioMeshVertexData? vertData = mesh.GetVertexData(studioDataCache, studioHdr);
		Assert(vertData != null);
		if (vertData == null) {
			if (fatVertexWarnCount++ < 20)
				Warning("ERROR: model verts have been compressed, cannot render! (use \"-no_compressed_vvds\")");
		}
		return vertData;
	}

	private void R_ComputeLightAtPoint3(in Vector3 pos, in Vector3 normal, out Vector3 color) {
		if (pRC!.Config.FullBright != 0) {
			color = new(1.0f, 1.0f, 1.0f);
			return;
		}

		Span<LightPos> lightpos = stackalloc LightPos[MAXLOCALLIGHTS];
		R_LightStrengthWorld(in pos, pRC.NumLocalLights, pRC.LocalLights, lightpos);

		R_LightAmbient_4D(in normal, pRC.LightBoxColors, out color);

		R_LightEffectsWorld3(pRC.LocalLights, lightpos, in normal, ref color, pRC.NumLocalLights);
	}

	internal static void R_LightStrengthWorld(in Vector3 vert, int lightcount, Span<LightDesc> desc, Span<LightPos> light) {
		for (int i = 0; i < lightcount; i++) {
			R_WorldLightDelta(in desc[i], in vert, out light[i].Delta);
			light[i].Falloff = R_WorldLightDistanceFalloff(in desc[i], in light[i].Delta);

			MathLib.VectorNormalizeFast(ref light[i].Delta);
			light[i].Dot = MathLib.DotProduct(light[i].Delta, desc[i].Direction);
		}
	}

	private static void R_WorldLightDelta(in LightDesc wl, in Vector3 org, out Vector3 delta) {
		switch (wl.Type) {
			case LightType.Point:
			case LightType.Spot:
				MathLib.VectorSubtract(wl.Position, org, out delta);
				break;

			case LightType.Directional:
				MathLib.VectorMultiply(wl.Direction, -1, out delta);
				break;

			default:
				Assert(false);
				delta = default;
				break;
		}
	}

	private static float R_WorldLightDistanceFalloff(in LightDesc wl, in Vector3 delta) {
		float dist2 = MathLib.DotProduct(delta, delta);

		if (wl.Range != 0.0f) {
			if (dist2 > wl.Range * wl.Range)
				return 0.0f;
		}

		float total = FLT_EPSILON;

		LightTypeOptimizationFlags flags = (LightTypeOptimizationFlags)wl.Flags;

		if ((flags & LightTypeOptimizationFlags.HasAttenuation0) != 0)
			total = wl.Attenuation0;

		if ((flags & LightTypeOptimizationFlags.HasAttenuation1) != 0)
			total += wl.Attenuation1 * MathF.Sqrt(dist2);

		if ((flags & LightTypeOptimizationFlags.HasAttenuation2) != 0)
			total += wl.Attenuation2 * dist2;

		return 1.0f / total;
	}

	private static float R_WorldLightAngle(in LightDesc wl, in Vector3 lnormal, in Vector3 snormal, in Vector3 delta) {
		float dot, dot2, ratio;

		switch (wl.Type) {
			case LightType.Point:
				dot = MathLib.DotProduct(snormal, delta);
				if (dot < 0.0f)
					return 0.0f;
				return dot;

			case LightType.Spot:
				dot = MathLib.DotProduct(snormal, delta);
				if (dot < 0.0f)
					return 0.0f;

				dot2 = -MathLib.DotProduct(delta, lnormal);
				if (dot2 <= wl.PhiDot)
					return 0.0f;

				ratio = dot;
				if (dot2 >= wl.ThetaDot)
					return ratio;

				if ((wl.Falloff == 1.0f) || (wl.Falloff == 0.0f))
					ratio *= (dot2 - wl.PhiDot) / (wl.ThetaDot - wl.PhiDot);
				else
					ratio *= MathF.Pow((dot2 - wl.PhiDot) / (wl.ThetaDot - wl.PhiDot), wl.Falloff);
				return ratio;

			case LightType.Directional:
				dot2 = -MathLib.DotProduct(snormal, lnormal);
				if (dot2 < 0.0f)
					return 0.0f;
				return dot2;

			case LightType.Disable:
				return 0.0f;

			default:
				Assert(false);
				return 0.0f;
		}
	}

	internal static void R_LightEffectsWorld3(Span<LightDesc> desc, Span<LightPos> light, in Vector3 normal, ref Vector3 dest, int numLights) {
		for (int i = 0; i < numLights; i++) {
			if (desc[i].Type == LightType.Disable)
				continue;

			float ratio = light[i].Falloff * R_WorldLightAngle(in desc[i], in desc[i].Direction, in normal, in light[i].Delta);
			if (ratio > 0)
				dest += desc[i].Color * ratio;
		}
	}

	private static void R_LightAmbient_4D(in Vector3 normal, InlineArray6<Vector4> pLightBoxColor, out Vector3 lv) {
		MathLib.VectorScale(normal.X > 0.0f ? pLightBoxColor[0].AsVector3D() : pLightBoxColor[1].AsVector3D(), normal.X * normal.X, out lv);
		MathLib.VectorMA(lv, normal.Y * normal.Y, normal.Y > 0.0f ? pLightBoxColor[2].AsVector3D() : pLightBoxColor[3].AsVector3D(), out lv);
		MathLib.VectorMA(lv, normal.Z * normal.Z, normal.Z > 0.0f ? pLightBoxColor[4].AsVector3D() : pLightBoxColor[5].AsVector3D(), out lv);
	}

	internal static void R_LightAmbient_3D(in Vector3 normal, ReadOnlySpan<Vector3> pLightBoxColor, out Vector3 lv) {
		MathLib.VectorScale(normal.X > 0.0f ? pLightBoxColor[0] : pLightBoxColor[1], normal.X * normal.X, out lv);
		MathLib.VectorMA(lv, normal.Y * normal.Y, normal.Y > 0.0f ? pLightBoxColor[2] : pLightBoxColor[3], out lv);
		MathLib.VectorMA(lv, normal.Z * normal.Z, normal.Z > 0.0f ? pLightBoxColor[4] : pLightBoxColor[5], out lv);
	}

	private static VertexFormat ComputeSWSkinVertexFormat(IMaterial pMaterial) {
		bool DX8OrHigherVertex = pMaterial.GetVertexFormat().GetUserDataSize() != 0;
		VertexFormat fmt = VertexFormat.Position | VertexFormat.Normal | VertexFormat.Color | VertexFormat.BoneIndex | VertexExts.GetBoneWeight(2) | VertexFormat.TexCoord2D_0;
		if (DX8OrHigherVertex)
			fmt |= VertexExts.GetUserDataSize(4);
		return fmt;
	}

	static uint translucentCache = 0;
	static uint originalTextureVarCache = 0;
	static uint lightmapVarCache = 0;

	private IMaterial? R_StudioSetupSkinAndLighting(IMatRenderContext renderContext, int index, Span<IMaterial> materials, int materialFlags, object? clientRenderable, Span<ColorMeshInfo> colorMeshes, ref StudioModelLighting lighting) {
		IMaterial? pMaterial = null;
		bool bCheckForConVarDrawTranslucentSubModels = false;
		// TODO: wireframe
		// todo: env cubemap only

		if (pRC!.ForcedMaterial == null /* TODO: Shadow/SSAO overrides here!! */) {
			pMaterial = materials[index];
			if (pMaterial == null) {
				Assert(false);
				return null;
			}

			bCheckForConVarDrawTranslucentSubModels = true;

			pMaterial.AlphaModulate(pRC.AlphaMod);
			pMaterial.ColorModulate(pRC.ColorMod.X, pRC.ColorMod.Y, pRC.ColorMod.Z);
		}
		else {
			// TODO!!
			Assert(false);
			return null;
		}

		lighting = R_StudioComputeLighting(pMaterial, materialFlags, colorMeshes);
		if (lighting == StudioModelLighting.Mouth) {
			if (!pRC.Config.Teeth || !R_TeethAreVisible())
				return null;

			if (pRC.Config.SupportsVertexAndPixelShaders)
				R_MouthSetupVertexShader(pMaterial);
		}

		// todo: lightmap var

		renderContext.Bind(pMaterial, clientRenderable);

		if (bCheckForConVarDrawTranslucentSubModels) {
			bool translucent = pMaterial.IsTranslucent();

			if (DrawTranslucentSubModels != translucent) {
				SkippedMeshes = true;
				return null;
			}
		}

		return pMaterial;
	}

	static bool R_TeethAreVisible() => true;

	static TokenCache illumVarCache;
	static TokenCache forwardVarCache;

	private void R_MouthSetupVertexShader(IMaterial? material) {
		if (material == null)
			return;

		MStudioMouth mouth = StudioHdr!.Mouth(0);

		float illum = pFlexWeights[mouth.FlexDesc];
		if (illum < 0)
			illum = 0;
		if (illum > 1)
			illum = 1;

		MathLib.VectorRotate(in mouth.Forward, in pBoneToWorld[mouth.Bone], out Vector3 forward);
		forward *= -1;

		IMaterialVar? illumVar = material.FindVarFast("$illumfactor", ref illumVarCache);
		if (illumVar != null)
			illumVar.SetFloatValue(illum);

		IMaterialVar? forwardVar = material.FindVarFast("$forward", ref forwardVarCache);
		if (forwardVar != null)
			forwardVar.SetVecValue(in forward);
	}

	private StudioModelLighting R_StudioComputeLighting(IMaterial pMaterial, int materialFlags, Span<ColorMeshInfo> colorMeshes) {
		Assert(pMaterial != null);
		bool doMouthLighting = materialFlags != 0 && (StudioHdr!.NumMouths >= 1);

		bool doSoftwareLighting = doMouthLighting || (pMaterial!.IsVertexLit() && pMaterial.NeedsSoftwareLighting());

		if (!pRC!.Config.SupportsVertexAndPixelShaders) {
			if (!doSoftwareLighting && !colorMeshes.IsEmpty)
				pMaterial!.SetUseFixedFunctionBakedLighting(true);
			else {
				doSoftwareLighting = true;
				pMaterial!.SetUseFixedFunctionBakedLighting(false);
			}
		}

		StudioModelLighting lighting = StudioModelLighting.Hardware;
		if (doMouthLighting)
			lighting = StudioModelLighting.Mouth;
		else if (doSoftwareLighting)
			lighting = StudioModelLighting.Software;

		return lighting;
	}

	public int R_StudioSetupModel(int bodypart, int entity_body, out MStudioModel? subModel, StudioHeader studioHdr) {
		int index;
		MStudioBodyParts pbodypart;

		if (bodypart > studioHdr.NumBodyParts) {
			ConDMsg($"R_StudioSetupModel: no such bodypart {bodypart}\n");
			bodypart = 0;
		}

		pbodypart = studioHdr.BodyPart(bodypart);

		if (pbodypart.Base == 0) {
			Warning($"Model has missing body part: {studioHdr.GetName()}\n");
			Assert(false);
		}
		index = entity_body / pbodypart.Base;
		index = index % pbodypart.NumModels;

		subModel = pbodypart.Model(index);
		return index;
	}

	public void SetLightingRenderState() {
		using MatRenderContextPtr renderContext = new(materialSystem);

		renderContext.SetAmbientLightCube(pRC!.LightBoxColors);

		if (pRC.Config.SoftwareLighting || pRC.NumLocalLights == 0)
			renderContext.DisableAllLocalLights();
		else {
			int maxLightCount = hardwareConfig.MaxNumLights();
			LightDesc desc = default;
			desc.Type = LightType.Disable;

			int i;
			int lightCount = Math.Min(pRC.NumLocalLights, maxLightCount);
			for (i = 0; i < lightCount; ++i)
				renderContext.SetLight(i, pRC.LocalLights[i]);
			for (; i < maxLightCount; ++i)
				renderContext.SetLight(i, desc);
		}
	}
}
