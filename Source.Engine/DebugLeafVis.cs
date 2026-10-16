using CommunityToolkit.HighPerformance;

using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;

namespace Source.Engine;

public class LeafVis
{
	public LeafVis() {
		Color = new(float.MaxValue, float.MaxValue, float.MaxValue);

		CollisionBSPData bsp = GetCollisionBSPData();
		NumBrushes = bsp.NumBrushes;
		NumEntityChars = bsp.MapEntityString?.Length ?? 0;
		LeafIndex = 0;
	}

	public bool IsValid() {
		CollisionBSPData bsp = GetCollisionBSPData();
		if (NumBrushes != bsp.NumBrushes || NumEntityChars != (bsp.MapEntityString?.Length ?? 0))
			return false;

		return true;
	}

	public readonly List<Vector3> Verts = [];
	public readonly List<int> PolyVertCount = [];
	public Vector3 Color;
	public int NumBrushes;
	public int NumEntityChars;
	public int LeafIndex;
}

public static partial class DebugLeafVis
{
	const int MAX_LEAF_PVERTS = 128;
	const float MAX_COORD_INTEGER = 16384;

	static readonly MatSysInterface MatSys = Singleton<MatSysInterface>();

	static IMaterial g_materialLeafVisWireframe => field ??= MatSys.GL_LoadMaterial("debug/debugleafviswireframe", MaterialDefines.TEXTURE_GROUP_OTHER)!;
	static IMaterial g_pMaterialDebugFlat => field ??= MatSys.GL_LoadMaterial("debug/debugdrawflattriangles", MaterialDefines.TEXTURE_GROUP_OTHER)!;
	static LeafVis? g_LeafVis = null;

	static void AddPlaneToList(List<CollisionPlane> list, in Vector3 normal, float dist, bool invert) {
		CollisionPlane plane = default;
		plane.Dist = invert ? -dist : dist;
		plane.Normal = invert ? -normal : normal;

		Vector3 point = plane.Dist * plane.Normal;
		for (int i = 0; i < list.Count; i++) {
			if (list[i].Normal == plane.Normal) {
				float d = Vector3.Dot(point, list[i].Normal) - list[i].Dist;
				if (d > 0) {
					CollisionPlane temp = list[i];
					temp.Dist = plane.Dist;
					list[i] = temp;
				}
				return;
			}
		}
		list.Add(plane);
	}

	static void PlaneList(int leafIndex, Model? model, List<CollisionPlane> planeList) {
		if (model == null || model.Brush.Shared == null || model.Brush.Shared.Leafs == null)
			Sys.Error("PlaneList: bad model");

		BSPMLeaf pLeaf = model!.Brush.Shared!.Leafs![leafIndex];
		BSPMNode? pNode = pLeaf.Parent;
		BSPMNode pChild = pLeaf;
		while (pNode != null) {
			bool front = pNode.Children[0] == pChild;
			AddPlaneToList(planeList, pNode.Plane.Normal, pNode.Plane.Dist, !front);
			pChild = pNode;
			pNode = pNode.Parent;
		}
	}

	static Vector3 CSGInsidePoint(Span<CollisionPlane> planes, int planeCount) {
		Vector3 point = new(0, 0, 0);

		for (int i = 0; i < planeCount; i++) {
			float d = Vector3.Dot(planes[i].Normal, point) - planes[i].Dist;
			if (d < 0) {
				point -= d * planes[i].Normal;
			}
		}
		return point;
	}

	static void TranslatePlaneList(Span<CollisionPlane> planes, int planeCount, in Vector3 offset) {
		for (int i = 0; i < planeCount; i++) {
			planes[i].Dist += Vector3.Dot(offset, planes[i].Normal);
		}
	}

	static void CSGPlaneList(LeafVis pVis, List<CollisionPlane> planeList) {
		int planeCount = planeList.Count;
		Span<Vector3> vertsIn = stackalloc Vector3[MAX_LEAF_PVERTS];
		Span<Vector3> vertsOut = stackalloc Vector3[MAX_LEAF_PVERTS];

		Vector3 insidePoint = CSGInsidePoint(planeList.AsSpan(), planeList.Count);
		TranslatePlaneList(planeList.AsSpan(), planeList.Count, -insidePoint);

		for (int i = 0; i < planeCount; i++) {
			int vertCount = MathLib.PolyFromPlane(vertsIn, planeList[i].Normal, planeList[i].Dist); // BaseWindingForPlane()

			int j;
			for (j = 0; j < planeCount; j++) {
				if (i == j)
					continue;

				if (vertCount < 3)
					continue;

				vertCount = MathLib.ClipPolyToPlane(vertsIn, vertCount, vertsOut, planeList[j].Normal, planeList[j].Dist);

				for (int k = 0; k < vertCount; k++)
					vertsIn[k] = vertsOut[k];
			}

			if (vertCount >= 3) {
				pVis.PolyVertCount.Add(vertCount);
				for (j = 0; j < vertCount; j++) {
					Vector3 vert = vertsIn[j] + insidePoint;
					pVis.Verts.Add(vert);
				}
			}
		}
	}

	static void LeafvisChanged(IConVar leafvisVar, in ConVarChangeContext ctx) {
		if (g_LeafVis != null)
			g_LeafVis = null;
	}

	static void AddLeafPortals(LeafVis pLeafvis, int leafIndex) {
		List<CollisionPlane> planeList = [];
		Vector3 normal;

		PlaneList(leafIndex, host_state.WorldModel, planeList);

		normal = new(0, 0, 0);
		// x-axis
		normal[0] = 1;
		AddPlaneToList(planeList, normal, MAX_COORD_INTEGER, true);
		AddPlaneToList(planeList, normal, -MAX_COORD_INTEGER, false);
		normal[0] = 0;

		// y-axis
		normal[1] = 1;
		AddPlaneToList(planeList, normal, MAX_COORD_INTEGER, true);
		AddPlaneToList(planeList, normal, -MAX_COORD_INTEGER, false);
		normal[1] = 0;

		// z-axis
		normal[2] = 1;
		AddPlaneToList(planeList, normal, MAX_COORD_INTEGER, true);
		AddPlaneToList(planeList, normal, -MAX_COORD_INTEGER, false);
		CSGPlaneList(pLeafvis, planeList);
	}

	public static ConVar mat_leafvis = new("mat_leafvis", "0", FCvar.Cheat, "Draw wireframe of current leaf", null, null, LeafvisChanged);
	public static ConVar r_visambient = new("r_visambient", "0", 0, "Draw leaf ambient lighting samples.  Needs mat_leafvis 1 to work");

	static int last_leaf = -1;
	public static void LeafVisBuild(in Vector3 p) {
		if (mat_leafvis.GetInt() == 0) {
			Assert(g_LeafVis == null);
			return;
		}
		else {
			int leafIndex = CM.PointLeafnum(p);
			if (g_LeafVis != null && last_leaf == leafIndex)
				return;

			DevMsg(1, $"Leaf {leafIndex}, Area {CM.LeafArea(leafIndex)}, Cluster {CM.LeafCluster(leafIndex)}\n");
			last_leaf = leafIndex;

			g_LeafVis = new LeafVis {
				Color = new(1.0f, 0.0f, 0.0f),
				LeafIndex = leafIndex
			};
			switch (mat_leafvis.GetInt()) {
				case 2: {
						BSPMLeaf[] pLeaf = host_state.WorldModel!.Brush.Shared!.Leafs!;
						int leafCount = pLeaf.Length;
						int visCluster = pLeaf[leafIndex].Cluster;
						for (int i = 0; i < leafCount; i++) {
							if (pLeaf[i].Cluster == visCluster) {
								AddLeafPortals(g_LeafVis, i);
							}
						}
					}
					break;
				case 3: {
						Span<byte> pvs = stackalloc byte[BSPFileCommon.MAX_MAP_LEAFS / 8];
						BSPMLeaf[] pLeaf = host_state.WorldModel!.Brush.Shared!.Leafs!;
						int leafCount = pLeaf.Length;
						int visCluster = pLeaf[leafIndex].Cluster;
						CM.Vis(pvs, pvs.Length, visCluster, CM.DVIS_PVS);

						for (int i = 0; i < leafCount; i++) {
							int cluster = pLeaf[i].Cluster;
							if (cluster >= 0 && (pvs[cluster >> 3] & (1 << (cluster & 7))) != 0) {
								AddLeafPortals(g_LeafVis, i);
							}
						}
					}
					break;
				case 0:
				default:
					AddLeafPortals(g_LeafVis, leafIndex);
					break;
			}
		}
	}

	static void DrawLeafvis(LeafVis pVis) {
		using MatRenderContextPtr renderContext = new(materials);

		int vert = 0;
		g_materialLeafVisWireframe.ColorModulate(pVis.Color[0], pVis.Color[1], pVis.Color[2]);
		renderContext.Bind(g_materialLeafVisWireframe);
		for (int i = 0; i < pVis.PolyVertCount.Count; i++) {
			if (pVis.PolyVertCount[i] >= 3) {
				IMesh mesh = renderContext.GetDynamicMesh();
				MeshBuilder meshBuilder = new();
				meshBuilder.Begin(mesh, MaterialPrimitiveType.Lines, pVis.PolyVertCount[i]);
				for (int j = 0; j < pVis.PolyVertCount[i]; j++) {
					meshBuilder.Position3fv(pVis.Verts[vert + j]);
					meshBuilder.AdvanceVertex();
					meshBuilder.Position3fv(pVis.Verts[vert + ((j + 1) % pVis.PolyVertCount[i])]);
					meshBuilder.AdvanceVertex();
				}
				meshBuilder.End();
				mesh.Draw();
			}
			vert += pVis.PolyVertCount[i];
		}
	}

	static void DrawLeafvis_Solid(LeafVis pVis) {
		using MatRenderContextPtr renderContext = new(materials);

		int vert = 0;

		Vector3 lightNormal = new(1, 1, 1);
		MathLib.VectorNormalize(ref lightNormal);
		renderContext.Bind(g_pMaterialDebugFlat);
		for (int i = 0; i < pVis.PolyVertCount.Count; i++) {
			int vertCount = pVis.PolyVertCount[i];
			if (vertCount >= 3) {
				IMesh mesh = renderContext.GetDynamicMesh();
				MeshBuilder meshBuilder = new();
				int triangleCount = vertCount - 2;
				meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, triangleCount);
				Vector3 e0 = pVis.Verts[vert + 1] - pVis.Verts[vert];
				Vector3 e1 = pVis.Verts[vert + 2] - pVis.Verts[vert];
				Vector3 normal = Vector3.Cross(e1, e0);
				MathLib.VectorNormalize(ref normal);
				float light = 0.5f + (Vector3.Dot(normal, lightNormal) * 0.5f);
				Vector3 color = pVis.Color * light;

				for (int j = 0; j < vertCount; j++) {
					meshBuilder.Position3fv(pVis.Verts[vert + j]);
					meshBuilder.Color3fv([color.X, color.Y, color.Z]);
					meshBuilder.AdvanceVertex();
				}

				for (int j = 0; j < triangleCount; j++) {
					meshBuilder.FastIndex(0);
					meshBuilder.FastIndex((ushort)(j + 2));
					meshBuilder.FastIndex((ushort)(j + 1));
				}
				meshBuilder.End();
				mesh.Draw();
			}
			vert += vertCount;
		}
	}

	static LeafVis? g_FrustumVis = null;
	static readonly LeafVis?[] g_ClipVis = [null, null, null];

	static int FindMinBrush(CollisionBSPData pBSPData, int nodenum, int brushIndex) {
		while (true) {
			if (nodenum < 0) {
				int leafIndex = -1 - nodenum;
				ref CollisionLeaf leaf = ref pBSPData.MapLeafs.AsSpan()[leafIndex];
				int firstbrush = pBSPData.MapLeafBrushes[leaf.FirstLeafBrush];
				if (firstbrush < brushIndex)
					brushIndex = firstbrush;
				return brushIndex;
			}

			ref CollisionNode node = ref pBSPData.MapNodes.AsSpan()[nodenum];
			brushIndex = FindMinBrush(pBSPData, node.Children[0], brushIndex);
			nodenum = node.Children[1];
		}
	}

	static void RecomputeClipbrushes(bool bEnabled) {
		for (int v = 0; v < 3; v++)
			g_ClipVis[v] = null;

		if (!bEnabled)
			return;

		for (int v = 0; v < 3; v++) {
			Contents[] contents = [Contents.PlayerClip | Contents.MonsterClip, Contents.MonsterClip, Contents.PlayerClip];
			g_ClipVis[v] = new LeafVis();
			g_ClipVis[v]!.Color = new(v != 1 ? 1.0f : 0.5f, 0.0f, v != 0 ? 1.0f : 0.0f);
			CollisionBSPData pBSP = GetCollisionBSPData();

			int lastBrush = pBSP.NumBrushes;

			if (pBSP.MapCollisionModels.Length > 1)
				lastBrush = FindMinBrush(pBSP, pBSP.MapCollisionModels[1].HeadNode, lastBrush);

			for (int i = 0; i < lastBrush; i++) {
				ref CollisionBrush pBrush = ref pBSP.MapBrushes.AsSpan()[i];
				if ((pBrush.Contents & (Contents.PlayerClip | Contents.MonsterClip)) == contents[v]) {
					List<CollisionPlane> planeList = [];
					if (pBrush.IsBox()) {
						ref CollisionBoxBrush pBox = ref pBSP.MapBoxBrushes.AsSpan()[pBrush.GetBox()];
						for (int idxSide = 0; idxSide < 3; idxSide++) {
							Vector3 normal = new(0, 0, 0);
							normal[idxSide] = 1.0f;
							AddPlaneToList(planeList, normal, pBox.Maxs[idxSide], true);
							AddPlaneToList(planeList, -normal, -pBox.Mins[idxSide], true);
						}
					}
					else {
						for (int j = 0; j < pBrush.NumSides; j++) {
							ref CollisionBrushSide pSide = ref pBSP.MapBrushSides.AsSpan()[pBrush.FirstBrushSide + j];
							if (pSide.Bevel)
								continue;
							AddPlaneToList(planeList, pSide.Plane.Normal, pSide.Plane.Dist, true);
						}
					}
					CSGPlaneList(g_ClipVis[v]!, planeList);
				}
			}
		}
	}

	static void ClipChanged(IConVar conVar, in ConVarChangeContext ctx) {
		ConVarRef clipVar = new(conVar);
		RecomputeClipbrushes(clipVar.GetBool());
	}

	static ConVar r_drawclipbrushes = new("r_drawclipbrushes", "0", FCvar.Cheat, "Draw clip brushes (red=NPC+player, pink=player, purple=NPC)", null, null, ClipChanged);

	static Vector3 LeafAmbientSamplePos(int leafIndex, in MLeafAmbientLighting sample) {
		BSPMLeaf pLeaf = host_state.WorldBrush!.Leafs![leafIndex];
		Vector3 outv = pLeaf.Center - pLeaf.HalfDiagonal;
		outv.X += sample.X * pLeaf.HalfDiagonal.X * (2.0f / 255.0f);
		outv.Y += sample.Y * pLeaf.HalfDiagonal.Y * (2.0f / 255.0f);
		outv.Z += sample.Z * pLeaf.HalfDiagonal.Z * (2.0f / 255.0f);

		return outv;
	}

	static void ColorRGBExp32ToColor32(in ColorRGBExp32 color, out Color outc) {
		MathLib.ColorRGBExp32ToVector(color, out Vector3 tmp);
		outc = default;
		outc.R = (byte)MathLib.LinearToScreenGamma(tmp.X);
		outc.G = (byte)MathLib.LinearToScreenGamma(tmp.Y);
		outc.B = (byte)MathLib.LinearToScreenGamma(tmp.Z);
	}

	static Vector3 CubeSide(in Vector3 pos, float size, int vert) {
		Vector3 side = pos;
		side.X += (vert & 1) != 0 ? -size : size;
		side.Y += (vert & 2) != 0 ? -size : size;
		side.Z += (vert & 4) != 0 ? -size : size;
		return side;
	}

	static void CubeFace(ref MeshBuilder meshBuilder, in Vector3 org, int v0, int v1, int v2, int v3, float size, in Color color) {
		meshBuilder.Position3fv(CubeSide(org, size, v0));
		meshBuilder.Color4ubv(color);
		meshBuilder.AdvanceVertex();
		meshBuilder.Position3fv(CubeSide(org, size, v1));
		meshBuilder.Color4ubv(color);
		meshBuilder.AdvanceVertex();
		meshBuilder.Position3fv(CubeSide(org, size, v2));
		meshBuilder.Color4ubv(color);
		meshBuilder.AdvanceVertex();
		meshBuilder.Position3fv(CubeSide(org, size, v3));
		meshBuilder.Color4ubv(color);
		meshBuilder.AdvanceVertex();
	}

	public static void LeafVisDraw() {
		if (g_FrustumVis != null)
			DrawLeafvis(g_FrustumVis);

		if (g_LeafVis != null)
			DrawLeafvis(g_LeafVis);

		if (g_ClipVis[0] != null) {
			if (!g_ClipVis[0]!.IsValid())
				RecomputeClipbrushes(true);

			if (r_drawclipbrushes.GetInt() == 2) {
				DrawLeafvis_Solid(g_ClipVis[0]!);
				DrawLeafvis_Solid(g_ClipVis[1]!);
				DrawLeafvis_Solid(g_ClipVis[2]!);
			}
			else {
				DrawLeafvis(g_ClipVis[0]!);
				DrawLeafvis(g_ClipVis[1]!);
				DrawLeafvis(g_ClipVis[2]!);
			}
		}

		if (g_LeafVis != null && r_visambient.GetBool()) {
			using MatRenderContextPtr renderContext = new(materials);
			renderContext.Bind(g_pMaterialDebugFlat);
			float cubesize = 12.0f;
			int leafIndex = g_LeafVis.LeafIndex;
			MLeafAmbientIndex pAmbient = host_state.WorldBrush!.LeafAmbient![leafIndex];
			if (pAmbient.AmbientSampleCount == 0 && pAmbient.FirstAmbientSample != 0) {
				leafIndex = pAmbient.FirstAmbientSample;
				pAmbient = host_state.WorldBrush!.LeafAmbient![leafIndex];
			}
			for (int i = 0; i < pAmbient.AmbientSampleCount; i++) {
				IMesh mesh = renderContext.GetDynamicMesh();
				MeshBuilder meshBuilder = new();
				meshBuilder.Begin(mesh, MaterialPrimitiveType.Quads, 6);
				ref MLeafAmbientLighting sample = ref host_state.WorldBrush!.AmbientSamples![pAmbient.FirstAmbientSample + i];
				Vector3 pos = LeafAmbientSamplePos(leafIndex, sample);
				ColorRGBExp32ToColor32(sample.Cube.Color[0], out Color color); // x
				CubeFace(ref meshBuilder, pos, 4, 6, 2, 0, cubesize, color);
				ColorRGBExp32ToColor32(sample.Cube.Color[1], out color); // -x
				CubeFace(ref meshBuilder, pos, 7, 5, 1, 3, cubesize, color);
				ColorRGBExp32ToColor32(sample.Cube.Color[2], out color); // y
				CubeFace(ref meshBuilder, pos, 0, 1, 5, 4, cubesize, color);
				ColorRGBExp32ToColor32(sample.Cube.Color[3], out color); // -y
				CubeFace(ref meshBuilder, pos, 3, 2, 6, 7, cubesize, color);
				ColorRGBExp32ToColor32(sample.Cube.Color[4], out color); // z
				CubeFace(ref meshBuilder, pos, 2, 3, 1, 0, cubesize, color);
				ColorRGBExp32ToColor32(sample.Cube.Color[5], out color); // -z
				CubeFace(ref meshBuilder, pos, 4, 5, 7, 6, cubesize, color);
				meshBuilder.End();
				mesh.Draw();
			}
		}
	}

	public static void CSGFrustum(in Frustum frustum) {
		g_FrustumVis = new LeafVis {
			Color = new(1.0f, 1.0f, 1.0f)
		};
		List<CollisionPlane> planeList = [];
		for (int i = 0; i < 6; i++) {
			VPlane vp = frustum[i];
			CollisionPlane plane = default;
			plane.Normal = vp.Normal;
			plane.Dist = vp.Dist;
			planeList.Add(plane);
		}
		CSGPlaneList(g_FrustumVis, planeList);
	}
}
