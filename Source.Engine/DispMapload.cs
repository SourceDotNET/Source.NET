using static Source.Engine.ModelLoader;
using static Source.Engine.DispHelpers;

using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.MaterialSystem;

using System.Numerics;
using System.Runtime.CompilerServices;
using Source.Common.Engine;
using CommunityToolkit.HighPerformance;
using Source.Common.Mathematics;

namespace Source.Engine;

static class DispHelpers
{
	public static void CalcMaxNumVertsAndIndices(int power, out int nVerts, out int nIndices) {
		int sideLength = (1 << power) + 1;
		nVerts = sideLength * sideLength;
		nIndices = (sideLength - 1) * (sideLength - 1) * 2 * 3;
	}
}

public static class DispMapload
{
	static readonly MatSysInterface MatSys = Singleton<MatSysInterface>();

	static void BuildDispGetSurfNormals(Vector3[] points, Vector3[] normals) {
		Vector3[] tmp = new Vector3[2];
		Vector3 normal;
		tmp[0] = points[1] - points[0];
		tmp[1] = points[3] - points[0];
		normal = tmp[1].Cross(tmp[0]);
		MathLib.VectorNormalize(ref normal);

		for (int i = 0; i < 4; i++)
			normals[i] = normal;
	}

	static DispGroup? FindCombo(List<DispGroup> combos, int idLMPage, IMaterial? material) {
		foreach (var c in combos)
			if (c.LightmapPageID == idLMPage && c.Material == material)
				return c;

		return null;
	}

	static DispGroup AddCombo(List<DispGroup> combos, int idLMPage, IMaterial? material) {
		DispGroup combo = new DispGroup {
			LightmapPageID = idLMPage,
			Material = material,
			Visible = 0
		};
		combos.Add(combo);
		return combo;
	}

	static void BuildDispSurfInit(Model world, CoreDispInfo buildDisp, ref BSPMSurface2 worldSurfID) {
		// if (!IS_SURF_VALID(worldSurfID)) return; // TODO
		// ASSERT_SURF_VALID(worldSurfID);

		Vector3[] surfPoints = new Vector3[4];
		Vector3[] surfNormals = new Vector3[4];
		Vector2[] surfTexCoords = new Vector2[4];
		Vector2[,] surfLightCoords = new Vector2[4, 4];

		if (MSurf_VertCount(ref worldSurfID) != 4) return;

#if !SWDS
		MatSys.BuildMSurfaceVerts(world.Brush.Shared, ref worldSurfID, surfPoints, surfTexCoords, surfLightCoords);
#endif
		BuildDispGetSurfNormals(surfPoints, surfNormals);

		CoreDispSurface dispSurf = buildDisp.GetSurface();

		int surfFlag = dispSurf.GetFlags();
		int nLMVects = 1;
		if ((MSurf_Flags(ref worldSurfID) & SurfDraw.BumpLight) != 0) {
			surfFlag |= CoreDispInfo.SURF_BUMPED;
			nLMVects = Constants.NUM_BUMP_VECTS + 1;
		}

		dispSurf.SetPointCount(4);
		for (int i = 0; i < 4; i++) {
			dispSurf.SetPoint(i, surfPoints[i]);
			dispSurf.SetPointNormal(i, surfNormals[i]);
			dispSurf.SetTexCoord(i, surfTexCoords[i]);

			for (int j = 0; j < nLMVects; j++)
				dispSurf.SetLuxelCoord(j, i, surfLightCoords[i, j]);
		}

		Vector3 vecS = MSurf_TexInfo(ref worldSurfID).TextureVecsTexelsPerWorldUnits[0].AsVector3D();
		Vector3 vecT = MSurf_TexInfo(ref worldSurfID).TextureVecsTexelsPerWorldUnits[1].AsVector3D();
		MathLib.VectorNormalize(ref vecS);
		MathLib.VectorNormalize(ref vecT);
		dispSurf.SetSAxis(vecS);
		dispSurf.SetTAxis(vecT);

		dispSurf.SetFlags(surfFlag);
		dispSurf.FindSurfPointStartIndex();
		dispSurf.AdjustSurfPointData();

#if !SWDS
		MatSysInterface.SurfaceCtx ctx = default;
		MatSys.SurfSetupSurfaceContext(ref ctx, ref worldSurfID);
		int lightmapWidth = MSurf_LightmapExtents(ref worldSurfID)[0];
		int lightmapHeight = MSurf_LightmapExtents(ref worldSurfID)[1];

		Vector2 uv = new(0.0f, 0.0f);
		for (int ndxLuxel = 0; ndxLuxel < 4; ndxLuxel++) {
			switch (ndxLuxel) {
				case 0:
					uv.Init(0.0f, 0.0f);
					break;
				case 1:
					uv.Init(0.0f, lightmapHeight);
					break;
				case 2:
					uv.Init(lightmapWidth, lightmapHeight);
					break;
				case 3:
					uv.Init(lightmapWidth, 0.0f);
					break;
			}

			uv.X += 0.5f;
			uv.Y += 0.5f;

			uv *= ctx.Scale;
			uv += ctx.Offset;

			dispSurf.SetLuxelCoord(0, ndxLuxel, uv);
		}
#endif
	}

	static VertexFormat ComputeDisplacementStaticMeshVertexFormat(IMaterial? material, DispGroup combo, Span<BSPDDispInfo> mapDisps) {
		VertexFormat vertexFormat = material!.GetVertexFormat();
		return vertexFormat;
	}

	static void AddEmptyMesh(Model world, DispGroup combo, Span<BSPDDispInfo> mapDisps, Span<int> dispInfos, int nDisps, int totalVerts, int totalIndices) {
		MatRenderContextPtr pRenderContext = new(materials);

		GroupMesh pMesh = new GroupMesh();
		combo.Meshes.Add(pMesh);

		VertexFormat vertexFormat = ComputeDisplacementStaticMeshVertexFormat(combo.Material, combo, mapDisps);
		pMesh.Mesh = pRenderContext.CreateStaticMesh(vertexFormat, MaterialDefines.TEXTURE_GROUP_STATIC_VERTEX_BUFFER_DISP);
		pMesh.Group = combo;
		pMesh.NumVisible = 0;

		using MeshBuilder builder = new();
		builder.Begin(pMesh.Mesh, MaterialPrimitiveType.Triangles, totalVerts, totalIndices);

		builder.AdvanceIndices(totalIndices);
		builder.AdvanceVertices(totalVerts);

		builder.End();

		pMesh.DispInfos.SetSize(nDisps);
		pMesh.Visible.SetSize(nDisps);
		pMesh.VisibleDisps.SetSize(nDisps);

		int iVertOffset = 0;
		int iIndexOffset = 0;
		for (int disp = 0; disp < nDisps; disp++) {
			DispInfo pDisp = DispInfo.GetModelDisp(world, dispInfos[disp])!;
			ref BSPDDispInfo mapDisp = ref mapDisps[dispInfos[disp]];

			pDisp.Mesh = pMesh;
			pDisp.VertOffset = iVertOffset;
			pDisp.IndexOffset = iIndexOffset;

			CalcMaxNumVertsAndIndices(mapDisp.Power, out int nVerts, out int nIndices);
			iVertOffset += nVerts;
			iIndexOffset += nIndices;

			pMesh.DispInfos[disp] = pDisp;
		}

		Assert(iVertOffset == totalVerts);
		Assert(iIndexOffset == totalIndices);
	}

	static void FillStaticBuffer(GroupMesh mesh, DispInfo disp, CoreDispInfo coreDisp, Span<DispVert> verts, int lightmaps) {
#if !SWDS
		CalcMaxNumVertsAndIndices(disp.GetPower(), out int nVerts, out int nIndices);

		using MeshBuilder builder = new();
		builder.BeginModify(mesh.Mesh!, disp.VertOffset, nVerts, 0, 0);

		MatSysInterface.SurfaceCtx ctx = default;
		MatSys.SurfSetupSurfaceContext(ref ctx, ref disp.GetParent());

		for (int i = 0; i < nVerts; i++) {
			Vector3 pos = coreDisp.GetVert(i);
			builder.Position3f(pos.X, pos.Y, pos.Z);

			Vector3 normal = coreDisp.GetNormal(i);
			builder.Normal3f(normal.X, normal.Y, normal.Z);

			coreDisp.GetTangentS(i, out Vector3 vec);
			builder.TangentS3fv(vec);

			coreDisp.GetTangentT(i, out vec);
			builder.TangentT3fv(vec);

			coreDisp.GetTexCoord(i, out Vector2 texCoord);
			builder.TexCoord2f(0, texCoord.X, texCoord.Y);

			coreDisp.GetLuxelCoord(0, i, out Vector2 lightCoord);
			builder.TexCoord2f(DISP_LMCOORDS_STAGE, lightCoord.X, lightCoord.Y);

			float alpha = coreDisp.GetAlpha(i);
			alpha *= 1.0f / 255.0f;
			alpha = Math.Clamp(alpha, 0.0f, 1.0f);
			builder.Color4f(1.0f, 1.0f, 1.0f, alpha);

			if (lightmaps > 1) {
				MatSys.SurfComputeLightmapCoordinate(ref ctx, ref disp.GetParent(), ref disp.GetVertex(i).Pos, ref lightCoord);
				builder.TexCoord2f(2, ctx.BumpSTexCoordOffset, 0.0f);
			}

			builder.AdvanceVertex();
		}

		builder.EndModify();
#endif
	}

	static void DispInfo_CreateMaterialGroups(Model world, MaterialSystem_SortInfo[] sortInfos) {
		for (int disp = 0; disp < world.Brush.Shared!.NumDispInfos; disp++) {
			DispInfo pDisp = DispInfo.GetModelDisp(world, disp)!;

			int idLMPage = sortInfos[MSurf_MaterialSortID(ref pDisp.ParentSurfID)].LightmapPageID;

			DispGroup? pCombo = FindCombo(g_DispGroups, idLMPage, MSurf_TexInfo(ref pDisp.ParentSurfID).Material);
			pCombo ??= AddCombo(g_DispGroups, idLMPage, MSurf_TexInfo(ref pDisp.ParentSurfID).Material);

			pCombo.DispInfos.Add(disp);
		}
	}

	static void DispInfo_LinkToParentFaces(Model world, Span<BSPDDispInfo> mapDisps, nint numDisplacements) {
		for (int disp = 0; disp < numDisplacements; disp++) {
			ref readonly BSPDDispInfo mapDisp = ref mapDisps[disp];
			DispInfo pDisp = DispInfo.GetModelDisp(world, disp)!;

			ref BSPMSurface2 surfID = ref SurfaceHandleFromIndex(mapDisp.MapFace);
			Assert(mapDisp.MapFace >= 0 && mapDisp.MapFace < world.Brush.Shared.NumSurfaces);
			Assert(MSurf_Flags(ref surfID) & SurfDraw.HasDisp);
			surfID.DispInfo = pDisp;
			pDisp.SetParent(ref surfID, world.Brush.Shared);
		}
	}

	static void DispInfo_CreateEmptyStaticBuffers(Model world, Span<BSPDDispInfo> mapDisps) {
		foreach (var combo in g_DispGroups) {
			int totalVerts = 0, totalIndices = 0;
			int iStart = 0;
			for (int disp = 0; disp < combo.DispInfos.Count; disp++) {
				ref BSPDDispInfo mapDisp = ref mapDisps[combo.DispInfos[disp]];

				CalcMaxNumVertsAndIndices(mapDisp.Power, out int nVerts, out int nIndices);

				// If we're going to pass our vertex buffer limit, or we're at the last one,
				// make a static buffer and fill it up.
				if ((totalVerts + nVerts) > MAX_STATIC_BUFFER_VERTS || (totalIndices + nIndices) > MAX_STATIC_BUFFER_INDICES) {
					AddEmptyMesh(world, combo, mapDisps, combo.DispInfos.AsSpan()[iStart..], disp - iStart, totalVerts, totalIndices);
					Assert(totalVerts > 0 && totalIndices > 0);

					totalVerts = totalIndices = 0;
					iStart = disp;
					--disp;
				}
				else if (disp == combo.DispInfos.Count - 1) {
					AddEmptyMesh(world, combo, mapDisps, combo.DispInfos.AsSpan()[iStart..], disp - iStart + 1, totalVerts + nVerts, totalIndices + nIndices);
					break;
				}
				else {
					totalVerts += nVerts;
					totalIndices += nIndices;
				}
			}
		}
	}

	public static bool DispInfo_CreateFromMapDisp(Model world, int disp, ref BSPDDispInfo mapDisp, CoreDispInfo coreDisp, Span<DispVert> verts, Span<DispTri> tris, MaterialSystem_SortInfo[] sortInfos, bool restoring) {
		DispInfo pDisp = DispInfo.GetModelDisp(world, disp)!;

		coreDisp.GetSurface().SetPointStart(mapDisp.StartPosition);
		coreDisp.InitDispInfo(mapDisp.Power, mapDisp.MinTess, mapDisp.SmoothingAngle, verts, tris);
		coreDisp.SetNeighborData(mapDisp.EdgeNeighbors, mapDisp.CornerNeighbors);

		ErrorIfNot(coreDisp.GetAllowedVerts().GetNumWords() == 10, $"DispInfo_StoreMapData: size mismatch in 'allowed verts' list ({coreDisp.GetAllowedVerts().GetNumWords()} != 10)");
		for (int iVert = 0; iVert < coreDisp.GetAllowedVerts().GetNumWords(); ++iVert)
			coreDisp.GetAllowedVerts().SetWord(iVert, (uint)mapDisp.AllowedVerts[iVert]);

		ref BSPMSurface2 parent = ref pDisp.GetParent();
		BuildDispSurfInit(world, coreDisp, ref parent);
		if (!coreDisp.Create())
			return false;

		pDisp.PointStart = coreDisp.GetSurface().GetPointStartIndex();

		pDisp.Index = (ushort)disp;

		pDisp.CopyMapDispData(in mapDisp);

		if (!pDisp.CopyCoreDispData(world, sortInfos, coreDisp, restoring))
			return false;

		pDisp.InitializeActiveVerts();
		pDisp.LightmapSamplePositionStart = mapDisp.LightmapSamplePositionStart;

		return true;
	}

	static void DispInfo_CreateStaticBuffersAndTags(Model world, int disp, CoreDispInfo coreDispInfo, Span<DispVert> tempVerts) {
		DispInfo dsp = DispInfo.GetModelDisp(world, disp)!;

		FillStaticBuffer(dsp.Mesh!, dsp, coreDispInfo, tempVerts, dsp.NumLightmaps());

		BuildTagData(coreDispInfo, dsp);
	}

	static unsafe void SetupMeshReaders(Model world, nint numDisplacements) {
		for (int iDisp = 0; iDisp < numDisplacements; iDisp++) {
			DispInfo pDisp = DispInfo.GetModelDisp(world, iDisp)!;

			MeshDesc desc = default;

			desc.Vertex.PositionSize = sizeof(DispRenderVert);
			desc.Vertex.TexCoordSize[0] = sizeof(DispRenderVert);
			desc.Vertex.TexCoordSize[DISP_LMCOORDS_STAGE] = sizeof(DispRenderVert);
			desc.Vertex.NormalSize = sizeof(DispRenderVert);
			desc.Vertex.TangentSSize = sizeof(DispRenderVert);
			desc.Vertex.TangentTSize = sizeof(DispRenderVert);

			DispRenderVert[] pBaseVert = pDisp.Verts.Base();

			desc.Index.IndexSize = 1;

			pDisp.MeshReader.BeginRead_Direct(desc, pDisp.NumVerts(), pDisp.NumIndices);
		}
	}

	static void UpdateDispBBoxes(Model world, nint numDisplacements) {
		for (int iDisp = 0; iDisp < numDisplacements; iDisp++) {
			DispInfo pDisp = DispInfo.GetModelDisp(world, iDisp)!;
			pDisp.UpdateBoundingBox();
		}
	}

	public static bool DispInfo_LoadDisplacements(Model world, MaterialSystem_SortInfo[] sortInfos) {
		nint numDisplacements = MapLoadHelper.GetLumpSize(LumpIndex.DispInfo) / Unsafe.SizeOf<BSPDDispInfo>();
		nint numLuxels = MapLoadHelper.GetLumpSize(LumpIndex.DispLightmapAlphas);
		nint numSamplePositionBytes = MapLoadHelper.GetLumpSize(LumpIndex.DispLightmapSamplePositions);

		world.Brush.Shared!.NumDispInfos = (int)numDisplacements;
		world.Brush.Shared!.DispInfos = DispInfo_CreateArray(numDisplacements);

		MapLoadHelper dispInfos = new(LumpIndex.DispInfo);

		g_DispLMAlpha.Clear(); g_DispLMAlpha.SetSize((int)numLuxels);
		MapLoadHelper dispLMAlphas = new(LumpIndex.DispLightmapAlphas);
		dispLMAlphas.LoadLumpData(g_DispLMAlpha.AsSpan());

		g_DispLightmapSamplePositions.Clear(); g_DispLightmapSamplePositions.SetSize((int)numLuxels);
		MapLoadHelper dispLMPositions = new(LumpIndex.DispLightmapSamplePositions);
		dispLMAlphas.LoadLumpData(g_DispLightmapSamplePositions.AsSpan());

		DispInfo_ReleaseMaterialSystemObjects(world);

		Span<BSPDDispInfo> tempDisps = stackalloc BSPDDispInfo[BSPFileCommon.MAX_MAP_DISPINFO];
		dispInfos.LoadLumpData(tempDisps);

		DispInfo_LinkToParentFaces(world, tempDisps, numDisplacements);
		DispInfo_CreateMaterialGroups(world, sortInfos);
		DispInfo_CreateEmptyStaticBuffers(world, tempDisps);

		Span<DispVert> tempVerts = stackalloc DispVert[BSPFileCommon.MAX_DISPVERTS];
		Span<DispTri> tempTris = stackalloc DispTri[BSPFileCommon.MAX_DISPTRIS];

		MapLoadHelper dispVerts = new(LumpIndex.DispVerts);
		MapLoadHelper dispTris = new(LumpIndex.DispTris);

		int curVert = 0;
		int curTri = 0;

		CoreDispInfo[] coreDisps = new CoreDispInfo[numDisplacements];
		int disp = 0;
		for (disp = 0; disp < numDisplacements; disp++)
			coreDisps[disp] = new();

		for (disp = 0; disp < numDisplacements; ++disp) {
			ref BSPDDispInfo mapDisp = ref tempDisps[disp];

			int numVerts = BSPFileCommon.NUM_DISP_POWER_VERTS(mapDisp.Power);
			ErrorIfNot(numVerts <= BSPFileCommon.MAX_DISPVERTS, $"DispInfo_LoadDisplacements: invalid vertex count ({numVerts})");
			dispVerts.LoadLumpData(curVert * Unsafe.SizeOf<DispVert>(), numVerts * Unsafe.SizeOf<DispVert>(), tempVerts);
			curVert += numVerts;

			int numTris = BSPFileCommon.NUM_DISP_POWER_TRIS(mapDisp.Power);
			ErrorIfNot(numTris <= BSPFileCommon.MAX_DISPTRIS, $"DispInfo_LoadDisplacements: invalid tri count ({numTris})");
			dispTris.LoadLumpData(curTri * Unsafe.SizeOf<DispTri>(), numTris * Unsafe.SizeOf<DispTri>(), tempTris);
			curTri += numTris;

			if (!DispInfo_CreateFromMapDisp(world, disp, ref mapDisp, coreDisps[disp], tempVerts, tempTris, sortInfos, false))
				return false;
		}

		SmoothDispSurfNormals(coreDisps, numDisplacements);

		for (disp = 0; disp < numDisplacements; ++disp) {
			DispInfo_CreateStaticBuffersAndTags(world, disp, coreDisps[disp], tempVerts);

			DispInfo pDisp = DispInfo.GetModelDisp(world, disp)!;
			pDisp.CopyCoreDispVertData(coreDisps[disp], pDisp.BumpSTexCoordOffset);

		}
		for (disp = 0; disp < numDisplacements; disp++) {
			DispInfo pDisp = DispInfo.GetModelDisp(world, disp)!;
			pDisp.ActiveVerts = pDisp.AllowedVerts;
		}

		for (disp = 0; disp < numDisplacements; disp++) {
			DispInfo pDisp = DispInfo.GetModelDisp(world, disp)!;
			pDisp.TesselateDisplacement();
		}

		SetupMeshReaders(world, numDisplacements);
		UpdateDispBBoxes(world, numDisplacements);

		return true;
	}

	static void DispInfo_ReleaseMaterialSystemObjects(Model? world) {
		using MatRenderContextPtr renderContext = new(materials);

		foreach (DispGroup group in g_DispGroups) {
			foreach (GroupMesh mesh in group.Meshes)
				renderContext.DestroyStaticMesh(mesh.Mesh!);

			group.Meshes.Clear();
		}

		g_DispGroups.Clear();

		if (world != null) {
			for (int iDisp = 0; iDisp < world.Brush.Shared!.NumDispInfos; iDisp++) {
				DispInfo? disp = DispInfo.GetModelDisp(world, iDisp);
				if (disp == null) {
					Assert(false);
					continue;
				}

				disp.Mesh = null;
				disp.VertOffset = disp.IndexOffset = 0;
			}
		}
	}

	static void BuildTagData(CoreDispInfo coreDisp, DispInfo disp) {
		int walkTest = 0;
		int buildTest = 0;
		int tri;

		for (tri = 0; tri < coreDisp.GetTriCount(); ++tri) {
			if (coreDisp.IsTriTag(tri, DispTriTags.TagWalkable))
				++walkTest;

			if (coreDisp.IsTriTag(tri, DispTriTags.TagBuildable))
				++buildTest;
		}

		walkTest *= 3;
		buildTest *= 3;

		disp.WalkIndices = new ushort[walkTest];
		disp.BuildIndices = new ushort[buildTest];

		int walkCount = 0;
		int buildCount = 0;


		for (tri = 0; tri < coreDisp.GetTriCount(); ++tri) {
			if (coreDisp.IsTriTag(tri, DispTriTags.TagWalkable)) {
				coreDisp.GetTriIndices(tri, out disp.WalkIndices[walkCount], out disp.WalkIndices[walkCount + 1], out disp.WalkIndices[walkCount + 2]);
				walkCount += 3;
			}

			if (coreDisp.IsTriTag(tri, DispTriTags.TagBuildable)) {
				coreDisp.GetTriIndices(tri, out disp.BuildIndices[buildCount], out disp.BuildIndices[buildCount + 1], out disp.BuildIndices[buildCount + 2]);
				buildCount += 3;
			}
		}

		Assert(walkCount == walkTest);
		Assert(buildCount == buildTest);

		disp.WalkIndexCount = walkCount;
		disp.BuildIndexCount = buildCount;
	}

	static int FindNeighborCornerVert(CoreDispInfo disp, in Vector3 point) {
		DispUtilsHelper dispHelper = disp;

		int closest = 0;
		float closestDist = float.MaxValue;
		for (int corner = 0; corner < 4; ++corner) {
			VertIndex cornerVertIndex = dispHelper.GetPowerInfo().GetCornerPointIndex(corner);
			int cornerVert = dispHelper.VertIndexToInt(cornerVertIndex);
			Vector3 cornerPos = disp.GetVert(cornerVert);

			float dist = cornerPos.DistTo(point);
			if (dist < closestDist) {
				closest = corner;
				closestDist = dist;
			}
		}

		if (closestDist <= 0.1f)
			return closest;
		else
			return -1;
	}

	static void UpdateTangentSpace(CoreDispInfo disp, int vert, in Vector3 normal, in Vector3 tanS) {
		disp.SetNormal(vert, tanS);
		MathLib.CrossProduct(tanS, normal, out Vector3 tanT);
		disp.SetTangentS(vert, tanS);
		disp.SetTangentT(vert, tanT);
	}

	static void UpdateTangentSpace(CoreDispInfo disp, in VertIndex index, in Vector3 normal, in Vector3 tanS) => UpdateTangentSpace(disp, disp.VertIndexToInt(index), normal, tanS);

	static void BlendSubNeighbors(CoreDispInfo[] listBase, nint listSize) {
		for (int disp = 0; disp < listSize; ++disp) {
			CoreDispInfo dispInfo = listBase[disp];
			if (dispInfo == null) continue;

			for (int edge = 0; edge < 4; ++edge) {
				DispNeighbor edgeNeighbor = dispInfo.GetEdgeNeighbor(edge);

				if (!edgeNeighbor.SubNeighbors[0].IsValid() || !edgeNeighbor.SubNeighbors[1].IsValid())
					continue;

				VertIndex midPointIndex = dispInfo.GetEdgeMidPoint(edge);
				int midPoint = dispInfo.VertIndexToInt(midPointIndex);

				Vector3 midPointPos = dispInfo.GetVert(midPoint);

				CoreDispInfo neighbor1 = listBase[edgeNeighbor.SubNeighbors[0].GetNeighborIndex()];
				CoreDispInfo neighbor2 = listBase[edgeNeighbor.SubNeighbors[1].GetNeighborIndex()];

				int[] corners = new int[2];
				corners[0] = FindNeighborCornerVert(neighbor1, midPointPos);
				corners[1] = FindNeighborCornerVert(neighbor2, midPointPos);
				if (corners[0] != -1 && corners[1] != -1) {
					VertIndex[] cornerIndices = [
						neighbor1.GetCornerPointIndex(corners[0]),
						neighbor2.GetCornerPointIndex(corners[1])
					];

					Vector3 average = dispInfo.GetNormal(midPoint);
					average += neighbor1.GetNormal(cornerIndices[0]);
					average += neighbor2.GetNormal(cornerIndices[1]);

					MathLib.VectorNormalize(ref average);
					Vector3 avgTanS = dispInfo.GetTangentS(midPoint);
					avgTanS += neighbor1.GetTangentS(cornerIndices[0]);
					avgTanS += neighbor2.GetTangentS(cornerIndices[1]);
					MathLib.VectorNormalize(ref avgTanS);

					UpdateTangentSpace(dispInfo, midPoint, average, avgTanS);
					UpdateTangentSpace(neighbor1, cornerIndices[0], average, avgTanS);
					UpdateTangentSpace(neighbor2, cornerIndices[1], average, avgTanS);
				}
			}
		}
	}

	static int GetAllNeighbors(CoreDispInfo disp, int[] neighbors) {
		int numNeighbors = 0;

		for (int corner = 0; corner < 4; corner++) {
			DispCornerNeighbors cornerNeighbors = disp.GetCornerNeighbors(corner);

			for (int i = 0; i < cornerNeighbors.NumNeighbors; i++) {
				if (numNeighbors < 512)
					neighbors[numNeighbors++] = cornerNeighbors.Neighbors[i];
			}
		}

		for (int edge = 0; edge < 4; edge++) {
			DispNeighbor edgeNeighbor = disp.GetEdgeNeighbor(edge);

			for (int i = 0; i < 2; i++) {
				if (edgeNeighbor.SubNeighbors[i].IsValid())
					if (numNeighbors < 512)
						neighbors[numNeighbors++] = edgeNeighbor.SubNeighbors[i].GetNeighborIndex();
			}
		}

		return numNeighbors;
	}

	static void BlendCorners(CoreDispInfo[] listBase, nint listSize) {
		for (int disp = 0; disp < listSize; ++disp) {
			CoreDispInfo dispInfo = listBase[disp];

			int[] neighbors = new int[512];
			int numNeighbors = GetAllNeighbors(dispInfo, neighbors);

			int[] nbCornerVerts = new int[numNeighbors];

			for (int corner = 0; corner < 4; corner++) {
				VertIndex cornerVertIndex = dispInfo.GetCornerPointIndex(corner);
				int cornerVert = dispInfo.VertIndexToInt(cornerVertIndex);
				Vector3 cornerPos = dispInfo.GetVert(cornerVert);

				Vector3 average = dispInfo.GetNormal(cornerVert);
				dispInfo.GetTangentS(cornerVert, out Vector3 avgTanS);

				for (int neighbor = 0; neighbor < numNeighbors; neighbor++) {
					int nbListIndex = neighbors[neighbor];
					CoreDispInfo nb = listBase[nbListIndex];

					int nbCorner = FindNeighborCornerVert(nb, cornerPos);
					if (nbCorner == -1) {
						nbCornerVerts[neighbor] = -1;
					}
					else {
						VertIndex nbCornerVertIndex = nb.GetCornerPointIndex(nbCorner);
						int nbVert = nb.VertIndexToInt(nbCornerVertIndex);
						nbCornerVerts[neighbor] = nbVert;
						average += nb.GetNormal(nbVert);
						avgTanS += nb.GetTangentS(nbVert);
					}
				}

				MathLib.VectorNormalize(ref average);
				MathLib.VectorNormalize(ref avgTanS);
				UpdateTangentSpace(dispInfo, cornerVert, average, avgTanS);

				for (int neighbor = 0; neighbor < numNeighbors; neighbor++) {
					int nbListIndex = neighbors[neighbor];
					if (nbCornerVerts[neighbor] == -1) continue;

					CoreDispInfo nb = listBase[nbListIndex];
					UpdateTangentSpace(nb, nbCornerVerts[neighbor], average, avgTanS);
				}
			}
		}
	}

	static void BlendEdges(CoreDispInfo[] listBase, nint listSize) {
		for (int disp = 0; disp < listSize; ++disp) {
			CoreDispInfo dispInfo = listBase[disp];
			if (dispInfo == null) continue;

			for (int edge = 0; edge < 4; ++edge) {
				DispNeighbor edgeNeighbor = dispInfo.GetEdgeNeighbor(edge);

				for (int subEdge = 0; subEdge < 2; ++subEdge) {
					DispSubNeighbor sub = edgeNeighbor.SubNeighbors[subEdge];
					if (!sub.IsValid()) continue;

					CoreDispInfo neighbor = listBase[sub.GetNeighborIndex()];
					if (neighbor == null) continue;

					int edgeDim = DispUtilsHelper.g_EdgeDims[edge];

					DispSubEdgeIterator it = new();
					it.Start(dispInfo, edge, subEdge, true);

					it.Next();
					VertIndex prevPos = it.GetVertIndex();
					while (it.Next()) {
						if (!it.IsLastVert()) {
							Vector3 average = dispInfo.GetNormal(it.GetVertIndex()) + neighbor.GetNormal(it.GetNBVertIndex());
							Vector3 avgTanS = dispInfo.GetTangentS(it.GetVertIndex()) + neighbor.GetTangentS(it.GetNBVertIndex());
							MathLib.VectorNormalize(ref average);
							MathLib.VectorNormalize(ref avgTanS);
							UpdateTangentSpace(dispInfo, it.GetVertIndex(), average, avgTanS);
							UpdateTangentSpace(neighbor, it.GetNBVertIndex(), average, avgTanS);
						}

						int prevPosFree = prevPos[edgeDim ^ 1];
						int curPosFree = it.GetVertIndex()[edgeDim ^ 1];
						for (int tween = prevPosFree + 1; tween < curPosFree; tween++) {
							float percent = (float)MathLib.RemapVal(tween, prevPosFree, curPosFree, 0, 1);
							MathLib.VectorLerp(dispInfo.GetNormal(prevPos), dispInfo.GetNormal(it.GetVertIndex()), percent, out Vector3 normal);
							MathLib.VectorNormalize(ref normal);
							MathLib.VectorLerp(dispInfo.GetTangentS(prevPos), dispInfo.GetTangentS(it.GetVertIndex()), percent, out Vector3 avgTanS);
							MathLib.VectorNormalize(ref avgTanS);

							VertIndex tweenIndex = new();
							tweenIndex[edgeDim] = it.GetVertIndex()[edgeDim];
							tweenIndex[edgeDim ^ 1] = (short)tween;
							UpdateTangentSpace(dispInfo, tweenIndex, normal, avgTanS);
						}

						prevPos = it.GetVertIndex();
					}
				}
			}
		}
	}

	static void SmoothDispSurfNormals(CoreDispInfo[] listBase, nint listSize) {
		for (int iDisp = 0; iDisp < listSize; ++iDisp)
			listBase[iDisp].SetDispUtilsHelperInfo(listBase, listSize);

		BlendSubNeighbors(listBase, listSize);
		BlendCorners(listBase, listSize);
		BlendEdges(listBase, listSize);
	}

	public static object? DispInfo_CreateArray(nint numDisplacements) {
		DispArray ret = new DispArray(numDisplacements);
		ret.CurTag = 1;
		for (nint i = 0; i < numDisplacements; i++) {
			ret.DispInfos[i] = new() {
				DispArray = ret
			};
		}
		return ret;
	}
}
