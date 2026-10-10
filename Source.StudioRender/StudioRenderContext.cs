using CommunityToolkit.HighPerformance;

using Source.Common;
using Source.Common.Commands;
using Source.Common.DataCache;
using Source.Common.Engine;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Source.StudioRender;

/// <summary>
/// Analog of StudioRenderContext_t
/// </summary>
public class StudioRenderCtx
{
	public StudioRenderConfig Config;
	public Vector3 ViewTarget;
	public Vector3 ViewOrigin;
	public Vector3 ViewRight;
	public Vector3 ViewUp;
	public Vector3 ViewPlaneNormal;
	public InlineArray6<Vector4> LightBoxColors;
	public int NumLocalLights;
	public InlineArray4<LightDesc> LocalLights;
	public Vector3 ColorMod;
	public float AlphaMod;
	public IMaterial? ForcedMaterial;
	public OverrideType ForcedMaterialType;
}

/// <summary>
/// Analog of CStudioRenderContext
/// </summary>
public class StudioRenderContext(IMaterialSystem materialSystem, IStudioDataCache studioDataCache, StudioRender studioRenderImp) : IStudioRender
{
	readonly IMaterialSystemHardwareConfig hardwareConfig = Singleton<IMaterialSystemHardwareConfig>();
	IMDLCache mdlcache => field ??= Singleton<IMDLCache>();

	public void BeginFrame() {
		RC.Config.SupportsVertexAndPixelShaders = hardwareConfig.SupportsVertexAndPixelShaders();


		studioRenderImp.PrecacheGlint();
	}

	public void EndFrame() {
		throw new NotImplementedException();
	}

	public void UpdateConfig(in StudioRenderConfig config) => RC.Config = config;

	public int GetMaterialListFromBodyAndSkin(MDLHandle_t studio, int skin, int body, Span<IMaterial> outputMaterials) {
		int found = 0;

		StudioHWData? studioHWData = mdlcache.GetHardwareData(studio);
		if (studioHWData == null)
			return 0;

		for (int lodID = studioHWData.RootLOD; lodID < studioHWData.NumLODs; lodID++) {
			StudioHeader studioHdr = mdlcache.GetStudioHdr(studio)!;
			Span<IMaterial> inputMaterials = studioHWData.LODs![lodID].Materials;

			if (skin >= studioHdr.NumSkinFamilies)
				skin = 0;

			Span<short> skinRef = studioHdr.SkinRef(0)[(skin * studioHdr.NumSkinRef)..];

			for (int i = 0; i < studioHdr.NumBodyParts; i++) {
				studioRenderImp.R_StudioSetupModel(i, body, out MStudioModel? model, studioHdr);

				for (int k = 0; k < model!.NumMeshes; ++k) {
					MStudioMesh mesh = model.Mesh(k);
					IMaterial material = inputMaterials[skinRef[mesh.Material]];
					Assert(material != null);

					int m;
					for (m = 0; m < found; m++) {
						if (outputMaterials[m] == material)
							break;
					}
					if (m >= found) {
						outputMaterials[found++] = material!;

						if (found >= outputMaterials.Length)
							return found;
					}
				}
			}
		}

		return found;
	}

	public int GetMaterialList(StudioHeader studioHDR, Span<IMaterial> materials) {
		AssertMsg(studioHDR != null, "Don't ignore this assert! StudioRenderContext.GetMaterialList() has null studioHDR.");

		if (studioHDR == null)
			return 0;

		if (studioHDR.TextureIndex == 0)
			return 0;

		int i;
		int j;
		int found = 0;
		Span<char> path = stackalloc char[MAX_PATH];
		for (i = 0; i < studioHDR.NumTextures; i++) {
			path.Clear();
			IMaterial? material = null;

			for (j = 0; j < studioHDR.NumCDTextures && material.IsErrorMaterial(); j++) {
				// If we don't do this, we get filenames like "materials\\blah.vmt".
				ReadOnlySpan<char> textureName = studioHDR.Texture(i).Name();
				if (!textureName.IsEmpty && textureName[0].IsPathSeparator())
					textureName = textureName[1..];

				// This prevents filenames like /models/blah.vmt.
				ReadOnlySpan<char> pCdTexture = studioHDR.CDTexture(j);
				if (!pCdTexture.IsEmpty && pCdTexture[0].IsPathSeparator())
					pCdTexture = pCdTexture[1..];

				StrTools.ComposeFileName(pCdTexture, textureName, path);
				Span<char> finalPath = path.SliceNullTerminatedString();

				if ((studioHDR.Flags & StudioHdrFlags.Obsolete) != 0)
					material = materialSystem.FindMaterialEx("models/obsolete/obsolete", TEXTURE_GROUP_MODEL, (int)MaterialFindContext.IsOnAModel, false);
				else
					material = materialSystem.FindMaterialEx(finalPath, TEXTURE_GROUP_MODEL, (int)MaterialFindContext.IsOnAModel, false);
			}

			if (material == null)
				continue;

			if (found < materials.Length) {
				int k;

				for (k = 0; k < found; k++)
					if (materials[k] == material)
						break;

				if (k >= found)
					materials[found++] = material;
			}
			else
				break;
		}

		return found;
	}

	public bool LoadModel(StudioHeader studioHdr, Memory<byte> vtxBuffer, StudioHWData studioHWData) {
		int i;
		int j;

		if (studioHdr == null || vtxBuffer.IsEmpty || studioHWData == null)
			return false;

		// NOTE: This must be called *after* Mod_LoadStudioModel
		OptimizedModel.FileHeader vertexHdr = new(vtxBuffer);
		if (vertexHdr.Checksum != studioHdr.Checksum) {
			ConDMsg($"Error! Model {studioHdr.GetName()} .vtx file out of synch with .mdl\n");
			return false;
		}

		studioHWData.NumStudioMeshes = 0;
		for (i = 0; i < studioHdr.NumBodyParts; i++) {
			MStudioBodyParts bodyPart = studioHdr.BodyPart(i);
			for (j = 0; j < bodyPart.NumModels; j++) {
				studioHWData.NumStudioMeshes += bodyPart.Model(j).NumMeshes;
			}
		}

		// Create static meshes
		Assert(vertexHdr.NumLODs != 0);
		studioHWData.RootLOD = Math.Min((int)studioHdr.RootLOD, vertexHdr.NumLODs - 1);
		studioHWData.NumLODs = vertexHdr.NumLODs;
		studioHWData.LODs = new StudioLODData[vertexHdr.NumLODs];
		for (int k = 0; k < vertexHdr.NumLODs; k++)
			studioHWData.LODs[k] = new();

		// reset the runtime flags
		studioHdr.Flags &= ~StudioHdrFlags.UsesEnvCubemap;
		studioHdr.Flags &= ~StudioHdrFlags.UsesFbTexture;
		studioHdr.Flags &= ~StudioHdrFlags.UsesBumpmapping;

		int nColorMeshID = 0;
		int nLodID;
		for (nLodID = studioHWData.RootLOD; nLodID < studioHWData.NumLODs; nLodID++) {
			LoadMaterials(studioHdr, vertexHdr, studioHWData.LODs[nLodID], nLodID);
			R_StudioCreateStaticMeshes(studioHdr, vertexHdr, studioHWData, nLodID, ref nColorMeshID);
			ComputeHWMorphDecalBoneRemap(studioHdr, vertexHdr, studioHWData, nLodID);
			studioHWData.LODs[nLodID].SwitchPoint = vertexHdr.BodyPart(0).Model(0).LOD(nLodID).SwitchPoint;
		}

		return true;
	}

	private void ComputeHWMorphDecalBoneRemap(StudioHeader studioHdr, OptimizedModel.FileHeader vertexHdr, StudioHWData studioHWData, int nLodID) {
		// Decals todo
	}

	private void R_StudioCreateStaticMeshes(StudioHeader studioHdr, OptimizedModel.FileHeader vtxHdr, StudioHWData studioHWData, int nLodID, ref int colorMeshID) {
		int i, j, k;

		studioHWData.LODs![nLodID].MeshData = new StudioMeshData[studioHWData.NumStudioMeshes];
		for (int l = 0; l < studioHWData.NumStudioMeshes; l++)
			studioHWData.LODs![nLodID]!.MeshData![l] = new();

		// Iterate over every body part...
		for (i = 0; i < studioHdr.NumBodyParts; i++) {
			MStudioBodyParts bodyPart = studioHdr.BodyPart(i);
			OptimizedModel.BodyPartHeader vtxBodyPart = vtxHdr.BodyPart(i);

			// Iterate over every submodel...
			for (j = 0; j < bodyPart.NumModels; ++j) {
				MStudioModel model = bodyPart.Model(j);
				OptimizedModel.ModelHeader vtxModel = vtxBodyPart.Model(j);
				OptimizedModel.ModelLODHeader vtxLOD = vtxModel.LOD(nLodID);

				// Determine which meshes should be hw morphed
				// DetermineHWMorphing(model, vtxLOD);

				// Iterate over all the meshes....
				for (k = 0; k < model.NumMeshes; ++k) {
					Assert(model.NumMeshes == vtxLOD.NumMeshes);
					MStudioMesh mesh = model.Mesh(k);
					OptimizedModel.MeshHeader vtxMesh = vtxLOD.Mesh(k);

					Assert(mesh.MeshID < studioHWData.NumStudioMeshes);
					R_StudioCreateSingleMesh(studioHdr, studioHWData.LODs[nLodID],
						mesh, vtxMesh, vtxHdr.MaxBonesPerVert,
						studioHWData.LODs![nLodID].MeshData![mesh.MeshID], ref colorMeshID);
				}
			}
		}
	}
	const int PREFETCH_VERT_COUNT = 4;
	private void R_StudioCreateSingleMesh(StudioHeader studioHdr, StudioLODData studioLodData, MStudioMesh mesh, OptimizedModel.MeshHeader vtxMesh, int numBones, StudioMeshData meshData, ref int colorMeshID) {
		bool needsTangentSpace = MeshNeedsTangentSpace(studioHdr, studioLodData, mesh);

		// Each strip group represents a locking group, it's a set of vertices
		// that are locked together, and, potentially, software light + skinned together
		meshData.NumGroup = vtxMesh.NumStripGroups;
		meshData.MeshGroup = new StudioMeshGroup[vtxMesh.NumStripGroups];

		for (int i = 0; i < vtxMesh.NumStripGroups; ++i) {
			OptimizedModel.StripGroupHeader stripGroup = vtxMesh.StripGroup(i);
			StudioMeshGroup meshGroup = meshData.MeshGroup[i] = new();

			meshGroup.MeshNeedsRestore = false;

			// Set the flags...
			meshGroup.Flags = 0;
			if ((stripGroup.Flags & OptimizedModel.StripGroupFlags.IsFlexed) != 0)
				meshGroup.Flags |= StudioMeshGroupFlags.IsFlexed;

			if ((stripGroup.Flags & OptimizedModel.StripGroupFlags.IsDeltaFlexed) != 0)
				meshGroup.Flags |= StudioMeshGroupFlags.IsDeltaFlexed;

			bool isHwSkinned = !!((stripGroup.Flags & OptimizedModel.StripGroupFlags.IsHWSkinned) != 0);
			if (isHwSkinned)
				meshGroup.Flags |= StudioMeshGroupFlags.IsHWSkinned;

			// get the minimal vertex format for this mesh
			VertexFormat vertexFormat = CalculateVertexFormat(studioHdr, studioLodData, mesh, stripGroup, isHwSkinned);

			// Build the vertex + index buffers
			R_StudioBuildMeshGroup(studioHdr.GetName(), needsTangentSpace, meshGroup, stripGroup, mesh, studioHdr, vertexFormat);

			// Copy over the tristrip and triangle list data
			R_StudioBuildMeshStrips(meshGroup, stripGroup);

			// Builds morph targets
			R_StudioBuildMorph(studioHdr, meshGroup, mesh, stripGroup);

			// Build the mapping from strip group vertex idx to actual mesh idx
			meshGroup.GroupIndexToMeshIndex = new ushort[stripGroup.NumVerts + PREFETCH_VERT_COUNT];
			meshGroup.NumVertices = stripGroup.NumVerts;

			int j;
			for (j = 0; j < stripGroup.NumVerts; ++j)
				meshGroup.GroupIndexToMeshIndex[j] = stripGroup.Vertex(j).OrigMeshVertID;

			// Extra copies are for precaching...
			for (j = stripGroup.NumVerts; j < stripGroup.NumVerts + PREFETCH_VERT_COUNT; ++j)
				meshGroup.GroupIndexToMeshIndex[j] = meshGroup.GroupIndexToMeshIndex[stripGroup.NumVerts - 1];

			// assign the possibly used color mesh id now
			meshGroup.ColorMeshID = colorMeshID++;
		}
	}

	private void R_StudioBuildMeshGroup(ReadOnlySpan<char> pModelName, bool bNeedsTangentSpace, StudioMeshGroup pMeshGroup, OptimizedModel.StripGroupHeader pStripGroup, MStudioMesh pMesh, StudioHeader pStudioHdr, VertexFormat vertexFormat) {
		using MatRenderContextPtr renderContext = new(materialSystem);

		// We have to do this here because of skinning; there may be any number of
		// materials that are applied to this mesh.
		// Copy over all the vertices + indices in this strip group
		pMeshGroup.Mesh = renderContext.CreateStaticMesh(vertexFormat, TEXTURE_GROUP_STATIC_VERTEX_BUFFER_MODELS);

		VertexCompressionType compressionType = CompressionType(vertexFormat);

		pMeshGroup.ColorMeshID = -1;

		bool hwSkin = (pMeshGroup.Flags & StudioMeshGroupFlags.IsHWSkinned) != 0;

		// This mesh could have tristrips or trilists in it
		MeshBuilder meshBuilder = new();
		meshBuilder.SetCompressionType(compressionType);
		meshBuilder.Begin(pMeshGroup.Mesh, MaterialPrimitiveType.Heterogenous, hwSkin ? pStripGroup.NumVerts : 0, pStripGroup.NumIndices);

		int i;
		bool bBadBoneWeights = false;
		if (hwSkin) {
			MStudioMeshVertexData? vertData = GetFatVertexData(pMesh, pStudioHdr);
			Assert(vertData != null);

			for (i = 0; i < pStripGroup.NumVerts; ++i) {
				bool success = R_AddVertexToMesh(pModelName, bNeedsTangentSpace, ref meshBuilder, ref pStripGroup.Vertex(i), pMesh, vertData, hwSkin);
				if (!success)
					bBadBoneWeights = true;
			}
		}

		if (bBadBoneWeights) {
			MStudioModel pModel = pMesh.Model;
			ConMsg($"Bad data found in model \"{pModel.Name()}\" (bad bone weights)\n");
		}

		for (i = 0; i < pStripGroup.NumIndices; ++i) {
			meshBuilder.Index(pStripGroup.Index(i));
			meshBuilder.AdvanceIndex();
		}

		meshBuilder.End();

		// Copy over the strip indices. We need access to the indices for decals
		pMeshGroup.Indices = new ushort[pStripGroup.NumIndices];
		memcpy(pMeshGroup.Indices, pStripGroup.Indices());

		// TODO: Statistics gathering?
	}

	private bool R_AddVertexToMesh(ReadOnlySpan<char> pModelName, bool bNeedsTangentSpace, ref MeshBuilder meshBuilder, ref OptimizedModel.Vertex pVertex, MStudioMesh pMesh, MStudioMeshVertexData? vertData, bool hwSkin) {
		bool bOK = true;
		int idx = pVertex.OrigMeshVertID;

		ref MStudioVertex vert = ref vertData!.Vertex(idx);

		meshBuilder.Position3fv(in vert.Position);
		meshBuilder.Normal3fv(in vert.Normal);

		meshBuilder.TexCoord2fv(0, in vert.TexCoord);

		if (vertData.HasTangentData())
			meshBuilder.UserData(in vertData.TangentS(idx));

		meshBuilder.Color4ub(255, 255, 255, 255);

		Span<float> boneWeights = stackalloc float[Studio.MAX_NUM_BONE_INDICES];
		if (hwSkin) {
			int i;
			ref MStudioBoneWeight boneWeight = ref vertData.BoneWeights(idx);

			float totalWeight = 0;
			for (i = 0; i < pVertex.NumBones; ++i)
				totalWeight += boneWeight.Weight[pVertex.BoneWeightIndex[i]];

			if ((pVertex.NumBones > 0) && (boneWeight.NumBones <= 3) && MathF.Abs(totalWeight - 1.0f) > 1e-3f) {
				bOK = false;
				totalWeight = 1.0f;
			}

			if (totalWeight == 0.0f)
				totalWeight = 1.0f;

			float invTotalWeight = 1.0f / totalWeight;

			for (i = 0; i < pVertex.NumBones; ++i) {
				if (pVertex.BoneID[i] == -1) {
					boneWeights[i] = 0.0f;
					meshBuilder.BoneMatrix(i, IMesh.BONE_MATRIX_INDEX_INVALID);
				}
				else {
					float weight = boneWeight.Weight[pVertex.BoneWeightIndex[i]];
					boneWeights[i] = weight * invTotalWeight;
					meshBuilder.BoneMatrix(i, pVertex.BoneID[i]);
				}
			}

			for (; i < Studio.MAX_NUM_BONE_INDICES; i++) {
				boneWeights[i] = 0.0f;
				meshBuilder.BoneMatrix(i, IMesh.BONE_MATRIX_INDEX_INVALID);
			}
		}
		else
			for (int i = 0; i < Studio.MAX_NUM_BONE_INDICES; ++i) {
				boneWeights[i] = (i == 0) ? 1.0f : 0.0f;
				meshBuilder.BoneMatrix(i, IMesh.BONE_MATRIX_INDEX_INVALID);
			}

		Assert(pVertex.NumBones <= 3);

		if (pVertex.NumBones > 0)
			meshBuilder.CompressedBoneWeight3fv(boneWeights);

		meshBuilder.AdvanceVertex();

		return bOK;
	}

	private MStudioMeshVertexData? GetFatVertexData(MStudioMesh pMesh, StudioHeader pStudioHdr) {
		if (pMesh.Model.CacheVertexData(studioDataCache, pStudioHdr) == null)
			return null;

		MStudioMeshVertexData? pVertData = pMesh.GetVertexData(studioDataCache, pStudioHdr);
		return pVertData;
	}

	private VertexCompressionType CompressionType(VertexFormat vertexFormat) {
		return VertexCompressionType.None; //not implemented
	}

	private void R_StudioBuildMeshStrips(StudioMeshGroup pMeshGroup, OptimizedModel.StripGroupHeader pStripGroup) {
		pMeshGroup.NumStrips = pStripGroup.NumStrips;
		pMeshGroup.StripData = new OptimizedModel.StripHeader[pStripGroup.NumStrips];

		for (int i = 0; i < pStripGroup.NumStrips; ++i) {
			OptimizedModel.StripHeader sourceStrip = pStripGroup.Strip(i);

			int stripHeaderSize = OptimizedModel.StripHeader.SIZEOF;
			int boneStateChangeSize = sourceStrip.NumBoneStateChanges * Marshal.SizeOf<OptimizedModel.BoneStateChangeHeader>();
			int totalSize = stripHeaderSize + boneStateChangeSize;

			Memory<byte> stripMemory = new byte[totalSize];

			sourceStrip.Data.Span[..OptimizedModel.StripHeader.SIZEOF].CopyTo(stripMemory.Span);

			if (sourceStrip.NumBoneStateChanges > 0) {
				Span<OptimizedModel.BoneStateChangeHeader> sourceBoneStates = sourceStrip.Data.Span[sourceStrip.BoneStateChangeOffset..].Cast<byte, OptimizedModel.BoneStateChangeHeader>()[..sourceStrip.NumBoneStateChanges];
				Span<OptimizedModel.BoneStateChangeHeader> destBoneStates = stripMemory.Span[stripHeaderSize..].Cast<byte, OptimizedModel.BoneStateChangeHeader>();

				sourceBoneStates.CopyTo(destBoneStates);
			}

			pMeshGroup.StripData[i] = new OptimizedModel.StripHeader(stripMemory);
			pMeshGroup.StripData[i].BoneStateChangeOffset = stripHeaderSize;
		}
	}

	private void R_StudioBuildMorph(StudioHeader studioHdr, StudioMeshGroup meshGroup, MStudioMesh mesh, OptimizedModel.StripGroupHeader stripGroup) {
		// todo
	}

	private bool MeshNeedsTangentSpace(StudioHeader studioHdr, StudioLODData studioLodData, MStudioMesh mesh) {
		if (studioHdr == null || studioHdr.NumSkinFamilies == 0)
			return false;

		Span<short> skinref = studioHdr.SkinRef(0);
		for (int i = 0; i < studioHdr.NumSkinFamilies; i++) {
			IMaterial? material = studioLodData.Materials?[skinref[mesh.Material]];
			Assert(material != null);
			if (material == null)
				continue;

			if (material.NeedsTangentSpace())
				return true;
		}
		return false;
	}

	private VertexFormat CalculateVertexFormat(StudioHeader studioHdr, StudioLODData studioLodData, MStudioMesh mesh, OptimizedModel.StripGroupHeader group, bool isHwSkinned) {
		bool bSkinnedMesh = studioHdr.NumBones > 1;

		if (bSkinnedMesh)
			return MaterialVertexFormat.SkinnedModel;
		else
			return MaterialVertexFormat.Model;
	}

	private int GetNumBoneWeights(OptimizedModel.StripGroupHeader group) {
		int nBoneWeightsMax = 0;

		for (int i = 0; i < group.NumStrips; i++) {
			OptimizedModel.StripHeader pStrip = group.Strip(i);
			nBoneWeightsMax = Math.Max(nBoneWeightsMax, (int)pStrip.NumBones);
		}

		return nBoneWeightsMax;
	}

	struct Threaded_LoadMaterials_Data
	{
		public StudioRenderContext Context;
		public int ID;
		public int LodID;
		public StudioHeader Hdr;
		public OptimizedModel.FileHeader VtxHeader;
		public StudioLODData LodData;
	}

	private void LoadMaterials(StudioHeader hdr, OptimizedModel.FileHeader vtxHeader, StudioLODData lodData, int lodID) {
		if (hdr.NumTextures == 0) {
			lodData.Materials = null;
			return;
		}
		lodData.Materials = new IMaterial[hdr.NumTextures];
		lodData.MaterialFlags = new int[hdr.NumTextures];

		if (hdr.TextureIndex == 0)
			return;

		for (int i = 0; i < hdr.NumTextures; i++) {
			Threaded_LoadMaterials_Data threadData = new Threaded_LoadMaterials_Data() {
				Context = this,
				ID = i,
				LodID = lodID,
				Hdr = hdr,
				VtxHeader = vtxHeader,
				LodData = lodData
			};
			Threaded_LoadMaterials(in threadData);
		}
	}

	private void Threaded_LoadMaterials(in Threaded_LoadMaterials_Data threadData) {
		StudioHeader hdr = threadData.Hdr;
		OptimizedModel.FileHeader vtxHeader = threadData.VtxHeader;
		StudioLODData lodData = threadData.LodData;
		int lodID = threadData.LodID;
		int i = threadData.ID;

		Span<char> path = stackalloc char[MAX_PATH];
		IMaterial? material = null;

		// search through all specified directories until a valid material is found
		for (int j = 0; j < hdr.NumCDTextures && material.IsErrorMaterial(); j++) {
			memreset(path);
			// If we don't do this, we get filenames like "materials\\blah.vmt".
			ReadOnlySpan<char> textureName = GetTextureName(hdr, vtxHeader, lodID, i);
			if (!textureName.IsEmpty && (textureName[0] == StrTools.CORRECT_PATH_SEPARATOR || textureName[0] == StrTools.INCORRECT_PATH_SEPARATOR))
				textureName = textureName[1..];

			// This prevents filenames like /models/blah.vmt.
			ReadOnlySpan<char> cdTexture = hdr.CDTexture(j);
			if (!cdTexture.IsEmpty && (cdTexture[0] == StrTools.CORRECT_PATH_SEPARATOR || cdTexture[0] == StrTools.INCORRECT_PATH_SEPARATOR))
				cdTexture = cdTexture[1..];

			StrTools.ComposeFileName(cdTexture, textureName, path);
			Span<char> finalPath = path.SliceNullTerminatedString();

			if ((hdr.Flags & StudioHdrFlags.Obsolete) != 0) {
				material = materialSystem.FindMaterial("models/obsolete/obsolete", TEXTURE_GROUP_MODEL, false);
				if (material.IsErrorMaterial())
					Warning("StudioRender: OBSOLETE material missing: \"models/obsolete/obsolete\"\n");
			}
			else
				material = materialSystem.FindMaterial(finalPath, TEXTURE_GROUP_MODEL, false);
		}
		if (material.IsErrorMaterial()) {
			// hack - if it isn't found, go through the motions of looking for it again
			// so that the materialsystem will give an error.
			Span<char> szPrefix = stackalloc char[256];
			strcpy(szPrefix, hdr.GetName());
			StrTools.StrConcat(szPrefix, " : ", StrTools.COPY_ALL_CHARACTERS);
			for (int j = 0; j < hdr.NumCDTextures; j++) {
				strcpy(path, hdr.CDTexture(j));
				ReadOnlySpan<char> textureName = GetTextureName(hdr, vtxHeader, lodID, i);
				StrTools.StrConcat(path, textureName, StrTools.COPY_ALL_CHARACTERS);
				StrTools.FixSlashes(path);
				Span<char> finalPath = path.SliceNullTerminatedString();
				materialSystem.FindMaterial(finalPath, TEXTURE_GROUP_MODEL, true, szPrefix);
			}
		}
		if (material.IsErrorMaterial()) {
			Warning($"Material '{path.SliceNullTerminatedString()}' not found.\n");
		}
		lodData.Materials![i] = material!;
		if (material != null) {
			// Increment the reference count for the material.
			// material.IncrementReferenceCount();
			threadData.Context.ComputeMaterialFlags(hdr, lodData, material);
			lodData.MaterialFlags[i] = UsesMouthShader(material) ? 1 : 0;
		}
	}

	private ReadOnlySpan<char> GetTextureName(StudioHeader hdr, OptimizedModel.FileHeader vtxHeader, int lodID, int inMaterialID) {
		OptimizedModel.MaterialReplacementListHeader materialReplacementList = vtxHeader.MaterialReplacementList(lodID);
		int i;
		for (i = 0; i < materialReplacementList.NumReplacements; i++) {
			OptimizedModel.MaterialReplacementHeader materialReplacement = materialReplacementList.MaterialReplacement(i);
			if (materialReplacement.MaterialID == inMaterialID) {
				string str = materialReplacement.MaterialReplacementName();
				return str;
			}
		}
		return hdr.Texture(inMaterialID).Name();
	}

	static TokenCache bumpvarCache = default;
	static TokenCache phongVarCache = default;
	static TokenCache clientShaderCache;

	static bool UsesMouthShader(IMaterial material) {
		IMaterialVar? clientShaderVar = material.FindVarFast("$clientShader", ref clientShaderCache);
		if (clientShaderVar != null)
			return stricmp(clientShaderVar.GetStringValue(), "MouthShader") == 0;
		return false;
	}

	private void ComputeMaterialFlags(StudioHeader hdr, StudioLODData lodData, IMaterial material) {
		if (material.UsesEnvCubemap())
			hdr.Flags |= StudioHdrFlags.UsesEnvCubemap;

		if (material.NeedsPowerOfTwoFrameBufferTexture(false))
			hdr.Flags |= StudioHdrFlags.UsesFbTexture;

		IMaterialVar? bumpMatVar = material.FindVarFast("$bumpmap", ref bumpvarCache);
		if (bumpMatVar != null && bumpMatVar.IsDefined() && material.NeedsTangentSpace())
			hdr.Flags |= StudioHdrFlags.UsesBumpmapping;

		IMaterialVar? phongMatVar = material.FindVarFast("$phong", ref phongVarCache);
		if (phongMatVar != null && phongMatVar.IsDefined() && phongMatVar.GetIntValue() != 0)
			hdr.Flags |= StudioHdrFlags.UsesBumpmapping;
	}

	// DEVIATION: But the way Source does it is so confusing and very much so built for C++ it seems...
	// so it seems more reasonable to deviate.
	List<Matrix3x4> matrices = [];
	public Span<Matrix3x4> LockBoneMatrices(int boneCount) {
		matrices.Clear();
		matrices.EnsureCountDefault(boneCount);
		return matrices.AsSpan();
	}

	public void UnloadModel(StudioHWData hardwareData) {
		throw new NotImplementedException();
	}

	public void UnlockBoneMatrices() {

	}

	readonly List<float> flexWeightBuffer = [];
	readonly List<float> flexDelayedWeightBuffer = [];

	public void LockFlexWeights(int weightCount, out Span<float> flexWeights) {
		flexWeightBuffer.Clear();
		flexWeightBuffer.EnsureCountDefault(weightCount);
		flexWeights = flexWeightBuffer.AsSpan();
	}

	public void LockFlexWeights(int weightCount, out Span<float> flexWeights, out Span<float> flexDelayedWeights) {
		LockFlexWeights(weightCount, out flexWeights);
		flexDelayedWeightBuffer.Clear();
		flexDelayedWeightBuffer.EnsureCountDefault(weightCount);
		flexDelayedWeights = flexDelayedWeightBuffer.AsSpan();
	}

	public void UnlockFlexWeights() {

	}

	static readonly ConVar r_randomflex = new("r_randomflex", "0", FCvar.Cheat);

	private static void GenerateRandomFlexWeights(int weightCount, Span<float> weights, Span<float> delayedWeights) {
		int randomFlex = r_randomflex.GetInt();
		if (randomFlex <= 0 || weights.IsEmpty)
			return;

		if (randomFlex > weightCount)
			randomFlex = weightCount;

		Span<int> indices = stackalloc int[weightCount];
		for (int i = 0; i < weightCount; ++i)
			indices[i] = i;

		for (int i = 0; i < weightCount; ++i) {
			int n = RandomInt(0, weightCount - 1);
			int temp = indices[n];
			indices[n] = indices[i];
			indices[i] = temp;
		}

		weights[..weightCount].Clear();
		for (int i = 0; i < randomFlex; ++i)
			weights[indices[i]] = RandomFloat(0.0f, 1.0f);

		if (!delayedWeights.IsEmpty) {
			delayedWeights[..weightCount].Clear();
			for (int i = 0; i < randomFlex; ++i)
				delayedWeights[indices[i]] = RandomFloat(0.0f, 1.0f);
		}
	}

	static readonly float[] ZeroFlexWeights = new float[Studio.MAXSTUDIOFLEXDESC];


	readonly StudioRenderCtx RC = new();
	public void DrawModel(ref DrawModelResults results, ref DrawModelInfo info, Span<Matrix3x4> boneToWorld, Span<float> flexWeights, Span<float> flexDelayedWeights, in Vector3 origin, StudioRenderFlags flags = StudioRenderFlags.DrawEntireModel) {
		// Set to zero in case we don't render anything.
		if (!Unsafe.IsNullRef(ref results))
			results.ActualTriCount = results.TextureMemoryBytes = 0;


		if (info.StudioHdr == null || info.HardwareData == null || info.HardwareData.NumLODs == 0 || info.HardwareData.LODs == null)
			return;

		GenerateRandomFlexWeights(info.StudioHdr.NumFlexDesc, flexWeights, flexDelayedWeights);

		using MatRenderContextPtr renderContext = new(materialSystem);
		info.Lod = ComputeRenderLOD(renderContext, info, origin, out float flMetric);
		if (!Unsafe.IsNullRef(ref results)) {
			results.LODUsed = info.Lod;
			results.LODMetric = flMetric;
		}

		Span<float> flexWeightsOrZero = flexWeights.IsEmpty ? ZeroFlexWeights : flexWeights;
		Span<float> flexDelayedWeightsOrWeights = flexDelayedWeights.IsEmpty ? flexWeightsOrZero : flexDelayedWeights;

		InvokeBindProxies(ref info);
		studioRenderImp.DrawModel(ref info, RC, boneToWorld, flexWeightsOrZero, flexDelayedWeightsOrWeights, flags);
	}

	private void InvokeBindProxies(ref DrawModelInfo info) {
		if (RC.ForcedMaterial != null) {
			if (RC.ForcedMaterialType == OverrideType.Normal && RC.ForcedMaterial.HasProxy()) {
				RC.ForcedMaterial.CallBindProxy(info.ClientEntity);
			}
			return;
		}

		// get skinref array
		int nSkin = (RC.Config.Skin > 0) ? RC.Config.Skin : info.Skin;
		Span<short> pSkinRef = info.StudioHdr.SkinRef(0);
		if (nSkin > 0 && nSkin < info.StudioHdr.NumSkinFamilies)
			pSkinRef = pSkinRef[(nSkin * info.StudioHdr.NumSkinRef)..];

		// This is used to ensure proxies are only called once
		int nBufSize = info.StudioHdr.NumTextures;
		Span<bool> pProxyCalled = stackalloc bool[nBufSize];

		Span<IMaterial> ppMaterials = info.HardwareData.LODs![info.Lod].Materials;
		MStudioModel pModel;
		for (int i = 0; i < info.StudioHdr.NumBodyParts; ++i) {
			studioRenderImp.R_StudioSetupModel(i, info.Body, out pModel, info.StudioHdr);
			for (int somethingOtherThanI = 0; somethingOtherThanI < pModel.NumMeshes; ++somethingOtherThanI) {
				MStudioMesh pMesh = pModel.Mesh(somethingOtherThanI);
				int nMaterialIndex = pSkinRef[pMesh.Material];
				if (pProxyCalled[nMaterialIndex])
					continue;
				pProxyCalled[nMaterialIndex] = true;
				IMaterial? pMaterial = ppMaterials[nMaterialIndex];
				if (pMaterial != null && pMaterial.HasProxy()) {
					pMaterial.CallBindProxy(info.ClientEntity);
				}
			}
		}
	}

	private int ComputeRenderLOD(MatRenderContextPtr renderContext, DrawModelInfo info, Vector3 origin, out float metric) {
		int lod = info.Lod;
		int lastlod = info.HardwareData.NumLODs - 1;
		metric = 0;
		if (lod == Studio.USESHADOWLOD)
			return lastlod;

		if (lod != -1)
			return Math.Clamp(lod, info.HardwareData.RootLOD, lastlod);

		float screenSize = renderContext.ComputePixelWidthOfSphere(origin, 0.5f);
		lod = ComputeModelLODAndMetric(info.HardwareData, screenSize, out metric);

		if ((info.StudioHdr.Flags & StudioHdrFlags.HasShadowLod) != 0)
			lastlod--;

		lod = Math.Clamp(lod, info.HardwareData.RootLOD, lastlod);
		return lod;
	}

	private int ComputeModelLODAndMetric(StudioHWData hardwareData, float unitSphereSize, out float metric) {
		metric = hardwareData.LODMetric(unitSphereSize);
		return hardwareData.GetLODForMetric(metric);
	}

	public void SetViewState(in Vector3 viewOrigin, in Vector3 viewRight, in Vector3 viewUp, in Vector3 viewForward) {
		RC.ViewOrigin = viewOrigin;
		RC.ViewRight = viewRight;
		RC.ViewUp = viewUp;
		RC.ViewPlaneNormal = viewForward;
	}

	public void SetColorModulation(Vector3 color) {
		RC.ColorMod = color;
	}

	public void SetAlphaModulation(float alpha) {
		RC.AlphaMod = alpha;
	}

	public void SetEyeViewTarget(StudioHeader? studioHdr, int bodyPart, in Vector3 viewtarget) => RC.ViewTarget = viewtarget;

	public void ForcedMaterialOverride(IMaterial? newMaterial, OverrideType overrideType = OverrideType.Normal) {
		RC.ForcedMaterial = newMaterial;
		RC.ForcedMaterialType = overrideType;
	}

	public int ComputeModelLod(StudioHWData hardwareData, float unitSphereSize) => ComputeModelLODAndMetric(hardwareData, unitSphereSize, out _);

	static readonly Vector3[] AmbientLightDir = [
		new( 1,  0,  0),
		new(-1,  0,  0),
		new( 0,  1,  0),
		new( 0, -1,  0),
		new( 0,  0,  1),
		new( 0,  0, -1)
	];

	public int GetNumAmbientLightSamples() => 6;

	public ReadOnlySpan<Vector3> GetAmbientLightDirections() => AmbientLightDir;

	public void SetAmbientLightColors(ReadOnlySpan<Vector3> colors) {
		for (int i = 0; i < 6; i++) {
			MathLib.VectorCopy(colors[i], out RC.LightBoxColors[i].AsVector3D());
			RC.LightBoxColors[i].W = 1.0f;
		}

		// S-FIXME: Would like to get this into the render thread, but there's systemic confusion
		// about whether to set lighting state here or in the material system
		using MatRenderContextPtr renderContext = new(materialSystem);
		renderContext.SetAmbientLightCube(RC.LightBoxColors);
	}

	public void SetLocalLights(int lightCount, ReadOnlySpan<LightDesc> lights) {
		RC.NumLocalLights = CopyLocalLightingState(StudioRender.MAXLOCALLIGHTS, RC.LocalLights, lightCount, lights);

		using MatRenderContextPtr renderContext = new(materialSystem);

		renderContext.SetAmbientLightCube(RC.LightBoxColors);

		if (RC.Config.SoftwareLighting || RC.NumLocalLights == 0)
			renderContext.DisableAllLocalLights();
		else {
			int i;
			int maxLightCount = hardwareConfig.MaxNumLights();
			int localLightCount = Math.Min(RC.NumLocalLights, maxLightCount);
			LightDesc desc = default;
			desc.Type = LightType.Disable;

			for (i = 0; i < localLightCount; ++i)
				renderContext.SetLight(i, RC.LocalLights[i]);
			for (; i < maxLightCount; ++i)
				renderContext.SetLight(i, desc);
		}
	}

	public void ComputeLighting(ReadOnlySpan<Vector3> ambient, int lightCount, Span<LightDesc> lights, in Vector3 pt, in Vector3 normal, out Vector3 lighting) {
		if (RC.Config.FullBright != 0) {
			lighting = new(1.0f, 1.0f, 1.0f);
			return;
		}

		if (lightCount > StudioRender.MAXLOCALLIGHTS)
			lightCount = StudioRender.MAXLOCALLIGHTS;

		// todo fixme
		Span<LightPos> lightPos = stackalloc LightPos[StudioRender.MAXLOCALLIGHTS];
		StudioRender.R_LightStrengthWorld(in pt, lightCount, lights, lightPos);
		StudioRender.R_LightAmbient_3D(in normal, ambient, out lighting);
		StudioRender.R_LightEffectsWorld3(lights, lightPos, in normal, ref lighting, lightCount);
	}

	public void ComputeLightingConstDirectional(ReadOnlySpan<Vector3> ambient, int lightCount, Span<LightDesc> lights, in Vector3 pt, in Vector3 normal, out Vector3 lighting, float directionalAmount) {
		if (RC.Config.FullBright != 0) {
			lighting = new(1.0f, 1.0f, 1.0f);
			return;
		}

		if (lightCount > StudioRender.MAXLOCALLIGHTS) {
			AssertMsg(false, "Light count out of range in ComputeLighting\n");
			lightCount = StudioRender.MAXLOCALLIGHTS;
		}

		// todo fixme
		Span<LightPos> lightPos = stackalloc LightPos[StudioRender.MAXLOCALLIGHTS];
		StudioRender.R_LightStrengthWorld(in pt, lightCount, lights, lightPos);
		StudioRender.R_LightAmbient_3D(in normal, ambient, out lighting);
	}

	private static int CopyLocalLightingState(int maxLights, Span<LightDesc> dest, int lightCount, ReadOnlySpan<LightDesc> src) {
		if (lightCount > maxLights)
			lightCount = maxLights;

		for (int i = 0; i < lightCount; i++) {
			dest[i] = src[i];
			LightTypeOptimizationFlags flags = 0;
			if (dest[i].Attenuation0 != 0.0f)
				flags |= LightTypeOptimizationFlags.HasAttenuation0;
			if (dest[i].Attenuation1 != 0.0f)
				flags |= LightTypeOptimizationFlags.HasAttenuation1;
			if (dest[i].Attenuation2 != 0.0f)
				flags |= LightTypeOptimizationFlags.HasAttenuation2;
			dest[i].Flags = (uint)flags;
		}

		return lightCount;
	}
}
