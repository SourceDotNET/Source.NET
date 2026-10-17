global using static Source.Engine.RAreaPortalGlobals;
global using static Source.Engine.RAreaPortal;

using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;

namespace Source.Engine;

public struct PortalRect
{
	public float Left, Top, Right, Bottom;
}

public class AreaCullInfo
{
	public Frustum_t Frustum = new();
	public PortalRect Rect;
	public ushort GlobalCounter;
}

public static class RAreaPortalGlobals
{
	public const int MAX_PORTAL_VERTS = 32;
	public const byte PLANE_ANYZ = 5;

	public readonly static ConVar r_ClipAreaPortals = new("r_ClipAreaPortals", "1", FCvar.Cheat);
	public readonly static ConVar r_DrawPortals = new("r_DrawPortals", "0", FCvar.Cheat);

	public static readonly List<PortalRect> g_PortalRects = [];
	public static byte[] g_RenderAreaBits = new byte[32];
}

public static class RAreaPortal
{
	static bool g_bViewerInSolidSpace = false;

	static readonly byte[] g_AreaStack = new byte[32];

	static readonly List<AreaCullInfo> g_AreaCullInfo = [];

	static readonly ushort[] g_VisibleAreas = new ushort[BSPFileCommon.MAX_MAP_AREAS];
	static int g_nVisibleAreas;

	static ushort g_GlobalCounter = 1;

	static ViewSetup g_viewSetup;
	static PortalRect g_viewWindow;
	static Matrix4x4 g_ScreenFromWorldProjection;

	public static void R_Areaportal_LevelInit() {
		g_AreaCullInfo.Clear();
		for (int i = 0; i < host_state.WorldBrush!.NumAreas; i++)
			g_AreaCullInfo.Add(new());
	}

	public static void R_Areaportal_LevelShutdown() {
		g_AreaCullInfo.Clear();
		g_PortalRects.Clear();
	}

	static void R_SetBit(byte[] bits, int bit) => bits[bit >> 3] |= (byte)(1 << (bit & 7));

	static void R_ClearBit(byte[] bits, int bit) => bits[bit >> 3] &= (byte)~(1 << (bit & 7));

	static byte R_TestBit(byte[] bits, int bit) => (byte)(bits[bit >> 3] & (1 << (bit & 7)));

	static bool GetPortalScreenExtents(ref BSPDAreaPortal portal, out PortalRect portalRect, Span<float> reflectionWaterHeight) {
		Span<Vector3> v0 = stackalloc Vector3[MAX_PORTAL_VERTS];
		Span<Vector3> v1 = stackalloc Vector3[MAX_PORTAL_VERTS];

		portalRect = default;
		portalRect.Left = portalRect.Bottom = float.MaxValue;
		portalRect.Right = portalRect.Top = float.MinValue;
		bool validExtents = false;

		int startVerts = Math.Min((int)portal.ClipPortalVerts, MAX_PORTAL_VERTS);
		if (startVerts != 0) {
			WorldBrushData brushData = host_state.WorldBrush!;
			int passCount = (!reflectionWaterHeight.IsEmpty) ? 2 : 1;
			for (int j = 0; j < passCount; ++j) {
				int i;
				for (i = 0; i < startVerts; i++) {
					v0[i] = brushData.ClipPortalVerts![portal.FirstClipPortalVert + i];

					if (j == 1)
						v0[i].Z = 2.0f * reflectionWaterHeight[0] - v0[i].Z;
				}

				int curList = 0;
				bool allClipped = false;
				for (int iPlane = 0; iPlane < 4; iPlane++) {
					ref readonly CollisionPlane plane = ref g_Frustum.GetPlane(iPlane);

					Span<Vector3> pIn = curList == 0 ? v0 : v1;
					Span<Vector3> pOut = curList == 0 ? v1 : v0;

					int outVerts = 0;
					int prev = startVerts - 1;
					float prevDot = Vector3.Dot(plane.Normal, pIn[prev]) - plane.Dist;
					for (int cur = 0; cur < startVerts; cur++) {
						float curDot = Vector3.Dot(plane.Normal, pIn[cur]) - plane.Dist;

						if ((curDot > 0) != (prevDot > 0)) {
							if (outVerts < MAX_PORTAL_VERTS) {
								float t = prevDot / (prevDot - curDot);
								MathLib.VectorLerp(pIn[prev], pIn[cur], t, out pOut[outVerts]);

								++outVerts;
							}
						}

						if (curDot > 0) {
							if (outVerts < MAX_PORTAL_VERTS) {
								pOut[outVerts] = pIn[cur];
								++outVerts;
							}
						}

						prevDot = curDot;
						prev = cur;
					}

					if (outVerts == 0) {
						allClipped = true;
						break;
					}

					startVerts = outVerts;
					curList = curList == 0 ? 1 : 0;
				}

				if (allClipped)
					continue;

				Assert(curList == 0);
				for (i = 0; i < startVerts; i++) {
					ref Vector3 point = ref v0[i];

					g_EngineRenderer.ClipTransformWithProjection(g_ScreenFromWorldProjection, point, out Vector3 screenPos);

					portalRect.Left = MathF.Min(screenPos.X, portalRect.Left);
					portalRect.Bottom = MathF.Min(screenPos.Y, portalRect.Bottom);
					portalRect.Top = MathF.Max(screenPos.Y, portalRect.Top);
					portalRect.Right = MathF.Max(screenPos.X, portalRect.Right);
				}
				validExtents = true;
			}
		}

		if (!validExtents) {
			portalRect.Left = portalRect.Bottom = 0;
			portalRect.Right = portalRect.Top = 0;
		}

		return validExtents;
	}

	static bool GetRectIntersection(in PortalRect rect1, in PortalRect rect2, out PortalRect outRect) {
		outRect = default;
		outRect.Left = MathF.Max(rect1.Left, rect2.Left);
		outRect.Right = MathF.Min(rect1.Right, rect2.Right);
		if (outRect.Left >= outRect.Right)
			return false;

		outRect.Bottom = MathF.Max(rect1.Bottom, rect2.Bottom);
		outRect.Top = MathF.Min(rect1.Top, rect2.Top);
		if (outRect.Bottom >= outRect.Top)
			return false;

		return true;
	}

	static void R_FlowThroughArea(int area, in Vector3 vecVisOrigin, in PortalRect clipRect, ReadOnlySpan<VisOverrideData> visData, Span<float> reflectionWaterHeight) {
		if (g_AreaCullInfo[area].GlobalCounter != g_GlobalCounter) {
			g_VisibleAreas[g_nVisibleAreas] = (ushort)area;
			++g_nVisibleAreas;

			g_AreaCullInfo[area].GlobalCounter = g_GlobalCounter;
			g_AreaCullInfo[area].Rect.Left = clipRect.Left;
			g_AreaCullInfo[area].Rect.Top = clipRect.Top;
			g_AreaCullInfo[area].Rect.Right = clipRect.Right;
			g_AreaCullInfo[area].Rect.Bottom = clipRect.Bottom;
		}
		else {
			ref PortalRect frustumRect = ref g_AreaCullInfo[area].Rect;
			frustumRect.Left = MathF.Min(frustumRect.Left, clipRect.Left);
			frustumRect.Bottom = MathF.Min(frustumRect.Bottom, clipRect.Bottom);
			frustumRect.Top = MathF.Max(frustumRect.Top, clipRect.Top);
			frustumRect.Right = MathF.Max(frustumRect.Right, clipRect.Right);
		}

		R_SetBit(g_RenderAreaBits, area);

		R_SetBit(g_AreaStack, area);

		WorldBrushData brushData = host_state.WorldBrush!;

		Assert(area < host_state.WorldBrush!.NumAreas);
		ref BSPDArea areaData = ref host_state.WorldBrush!.Areas![area];

		for (int iAreaPortal = 0; iAreaPortal < areaData.NumAreaPortals; iAreaPortal++) {
			Assert(areaData.FirstAreaPortal + iAreaPortal < brushData.NumAreaPortals);
			ref BSPDAreaPortal areaPortal = ref brushData.AreaPortals![areaData.FirstAreaPortal + iAreaPortal];

			if (R_TestBit(g_AreaStack, areaPortal.OtherArea) != 0)
				continue;

			if (R_TestBit(cl.AreaPortalBits, areaPortal.PortalKey) == 0)
				continue;

			ref CollisionPlane plane = ref brushData.Planes![areaPortal.PlaneNum];
			float dist = Vector3.Dot(plane.Normal, vecVisOrigin) - plane.Dist;
			if (dist < -0.1f)
				continue;

			if (R_TestBit(cl.AreaBits, areaPortal.OtherArea) == 0)
				continue;

			PortalRect portalRect;
			bool portalVis = true;

			float distTolerance = (visData.Length != 0) ? visData[0].DistToAreaPortalTolerance : 0.1f;
			if (dist > distTolerance)
				portalVis = GetPortalScreenExtents(ref areaPortal, out portalRect, reflectionWaterHeight);
			else {
				portalRect = default;
				portalRect.Left = -1;
				portalRect.Top = 1;
				portalRect.Right = 1;
				portalRect.Bottom = -1;
			}
			if (portalVis) {
				if (GetRectIntersection(in portalRect, in clipRect, out PortalRect intersection)) {
					if (r_DrawPortals.GetInt() != 0) {
						g_PortalRects.Add(intersection);
					}

					R_FlowThroughArea(areaPortal.OtherArea, vecVisOrigin, in intersection, visData, reflectionWaterHeight);
				}
			}
		}

		R_ClearBit(g_AreaStack, area);
	}

	static void IncrementGlobalCounter() {
		if (g_GlobalCounter == 0xFFFF) {
			for (int i = 0; i < g_AreaCullInfo.Count; i++)
				g_AreaCullInfo[i].GlobalCounter = 0;

			g_GlobalCounter = 1;
		}
		else
			g_GlobalCounter++;
	}

	static void R_SetupGlobalFrustum() {
		g_viewSetup = g_EngineRenderer.ViewGetCurrent();

		if (g_viewSetup.Ortho) {
			g_viewWindow.Right = g_viewSetup.OrthoRight;
			g_viewWindow.Left = g_viewSetup.OrthoLeft;
			g_viewWindow.Top = g_viewSetup.OrthoTop;
			g_viewWindow.Bottom = g_viewSetup.OrthoBottom;
		}
		else {
			float xFOV = g_EngineRenderer.GetFov() * 0.5f;
			float yFOV = g_EngineRenderer.GetFovY() * 0.5f;

			g_viewWindow.Right = MathF.Tan(MathLib.DEG2RAD(xFOV));
			g_viewWindow.Left = -g_viewWindow.Right;
			g_viewWindow.Top = MathF.Tan(MathLib.DEG2RAD(yFOV));
			g_viewWindow.Bottom = -g_viewWindow.Top;

			if (g_viewSetup.OffCenter)
				AssertMsg(false, "test m_bOffCenter frustums with area portals");

			Matrix4x4 matrixView = default;
			Matrix4x4 matrixProjection = default;
			Matrix4x4 matrixWorldToScreen = default;
			g_viewSetup.ViewToProjectionOverride = false;

			R.ComputeViewMatrices(ref matrixView, ref matrixProjection, ref matrixWorldToScreen, g_viewSetup);

			g_ScreenFromWorldProjection = matrixWorldToScreen;
		}
	}

	static readonly ConVar r_snapportal = new("r_snapportal", "-1", 0);

	static void R_SetupVisibleAreaFrustums() {
		for (int i = 0; i < g_nVisibleAreas; i++) {
			AreaCullInfo info = g_AreaCullInfo[g_VisibleAreas[i]];

			PortalRect portalWindow = new() {
				Left = (float)MathLib.RemapVal(info.Rect.Left, -1, 1, g_viewWindow.Left, g_viewWindow.Right),
				Right = (float)MathLib.RemapVal(info.Rect.Right, -1, 1, g_viewWindow.Left, g_viewWindow.Right),
				Top = (float)MathLib.RemapVal(info.Rect.Top, -1, 1, g_viewWindow.Bottom, g_viewWindow.Top),
				Bottom = (float)MathLib.RemapVal(info.Rect.Bottom, -1, 1, g_viewWindow.Bottom, g_viewWindow.Top)
			};

			if (g_viewSetup.Ortho) {
				float orgOffset = Vector3.Dot(CurrentViewOrigin(), CurrentViewRight());
				info.Frustum.SetPlane((int)FrustumPlane.Left, PLANE_ANYZ, CurrentViewRight(), portalWindow.Left + orgOffset);
				info.Frustum.SetPlane((int)FrustumPlane.Right, PLANE_ANYZ, -CurrentViewRight(), -portalWindow.Right - orgOffset);

				orgOffset = Vector3.Dot(CurrentViewOrigin(), CurrentViewUp());
				info.Frustum.SetPlane((int)FrustumPlane.Top, PLANE_ANYZ, CurrentViewUp(), portalWindow.Top + orgOffset);
				info.Frustum.SetPlane((int)FrustumPlane.Bottom, PLANE_ANYZ, -CurrentViewUp(), -portalWindow.Bottom - orgOffset);
			}
			else {
				if (g_viewSetup.OffCenter)
					AssertMsg(false, "test m_bOffCenter frustums with area portals");

				Vector3 normal;

				normal = portalWindow.Right * CurrentViewForward() - CurrentViewRight();
				MathLib.VectorNormalize(ref normal);
				info.Frustum.SetPlane((int)FrustumPlane.Right, PLANE_ANYZ, normal, Vector3.Dot(normal, CurrentViewOrigin()));

				normal = CurrentViewRight() - portalWindow.Left * CurrentViewForward();
				MathLib.VectorNormalize(ref normal);
				info.Frustum.SetPlane((int)FrustumPlane.Left, PLANE_ANYZ, normal, Vector3.Dot(normal, CurrentViewOrigin()));

				normal = portalWindow.Top * CurrentViewForward() - CurrentViewUp();
				MathLib.VectorNormalize(ref normal);
				info.Frustum.SetPlane((int)FrustumPlane.Top, PLANE_ANYZ, normal, Vector3.Dot(normal, CurrentViewOrigin()));

				normal = CurrentViewUp() - portalWindow.Bottom * CurrentViewForward();
				MathLib.VectorNormalize(ref normal);
				info.Frustum.SetPlane((int)FrustumPlane.Bottom, PLANE_ANYZ, normal, Vector3.Dot(normal, CurrentViewOrigin()));

				info.Frustum.SetPlane((int)FrustumPlane.FarZ, PLANE_ANYZ, -CurrentViewForward(),
					Vector3.Dot(-CurrentViewForward(), CurrentViewOrigin() + CurrentViewForward() * g_viewSetup.ZFar));
			}

			if (r_snapportal.GetInt() >= 0) {
				if (g_VisibleAreas[i] == r_snapportal.GetInt()) {
					info.Frustum.SetPlane((int)FrustumPlane.NearZ, PLANE_ANYZ, CurrentViewForward(),
						Vector3.Dot(CurrentViewForward(), CurrentViewOrigin()));
					info.Frustum.SetPlane((int)FrustumPlane.FarZ, PLANE_ANYZ, -CurrentViewForward(),
						Vector3.Dot(-CurrentViewForward(), CurrentViewOrigin() + CurrentViewForward() * 500));
					r_snapportal.SetValue(-1);
					// todo CSGFrustum
				}
			}
		}
	}

	static bool R_CullNodeInternal(BSPMNode node, ref int clipMask, Frustum_t frustum) {
		int outClipMask = clipMask & FRUSTUM_CLIP_IN_AREA;

		float centerDotNormal, halfDiagDotAbsNormal;
		if ((clipMask & FRUSTUM_CLIP_RIGHT) != 0) {
			ref readonly CollisionPlane plane = ref frustum.GetPlane((int)FrustumPlane.Right);
			centerDotNormal = Vector3.Dot(node.Center, plane.Normal) - plane.Dist;
			halfDiagDotAbsNormal = Vector3.Dot(node.HalfDiagonal, frustum.GetAbsNormal((int)FrustumPlane.Right));
			if (centerDotNormal + halfDiagDotAbsNormal < 0.0f)
				return true;
			if (centerDotNormal - halfDiagDotAbsNormal < 0.0f)
				outClipMask |= FRUSTUM_CLIP_RIGHT;
		}

		if ((clipMask & FRUSTUM_CLIP_LEFT) != 0) {
			ref readonly CollisionPlane plane = ref frustum.GetPlane((int)FrustumPlane.Left);
			centerDotNormal = Vector3.Dot(node.Center, plane.Normal) - plane.Dist;
			halfDiagDotAbsNormal = Vector3.Dot(node.HalfDiagonal, frustum.GetAbsNormal((int)FrustumPlane.Left));
			if (centerDotNormal + halfDiagDotAbsNormal < 0.0f)
				return true;
			if (centerDotNormal - halfDiagDotAbsNormal < 0.0f)
				outClipMask |= FRUSTUM_CLIP_LEFT;
		}

		if ((clipMask & FRUSTUM_CLIP_TOP) != 0) {
			ref readonly CollisionPlane plane = ref frustum.GetPlane((int)FrustumPlane.Top);
			centerDotNormal = Vector3.Dot(node.Center, plane.Normal) - plane.Dist;
			halfDiagDotAbsNormal = Vector3.Dot(node.HalfDiagonal, frustum.GetAbsNormal((int)FrustumPlane.Top));
			if (centerDotNormal + halfDiagDotAbsNormal < 0.0f)
				return true;
			if (centerDotNormal - halfDiagDotAbsNormal < 0.0f)
				outClipMask |= FRUSTUM_CLIP_TOP;
		}

		if ((clipMask & FRUSTUM_CLIP_BOTTOM) != 0) {
			ref readonly CollisionPlane plane = ref frustum.GetPlane((int)FrustumPlane.Bottom);
			centerDotNormal = Vector3.Dot(node.Center, plane.Normal) - plane.Dist;
			halfDiagDotAbsNormal = Vector3.Dot(node.HalfDiagonal, frustum.GetAbsNormal((int)FrustumPlane.Bottom));
			if (centerDotNormal + halfDiagDotAbsNormal < 0.0f)
				return true;
			if (centerDotNormal - halfDiagDotAbsNormal < 0.0f)
				outClipMask |= FRUSTUM_CLIP_BOTTOM;
		}

		clipMask = outClipMask;
		return false;
	}

	public static bool R_CullNode(Frustum_t areaFrustum, BSPMNode node, ref int clipMask) {
		if ((!g_bViewerInSolidSpace) && (node.Area > 0)) {
			if (R_IsAreaVisible(node.Area) == 0)
				return true;

			if (true) {
				if ((clipMask & FRUSTUM_CLIP_IN_AREA) == 0)
					clipMask = FRUSTUM_CLIP_IN_AREA | FRUSTUM_CLIP_ALL;

				areaFrustum = g_AreaCullInfo[node.Area].Frustum;
			}
		}

		return R_CullNodeInternal(node, ref clipMask, areaFrustum);
	}

	static readonly ConVar r_portalscloseall = new("r_portalscloseall", "0", FCvar.Cheat | FCvar.DevelopmentOnly, "Close all portals");
	static readonly ConVar r_portalsopenall = new("r_portalsopenall", "0", FCvar.Cheat, "Open all portals");
	static readonly ConVar r_ShowViewerArea = new("r_ShowViewerArea", "0", 0);

	public static void R_SetupAreaBits(int forceViewLeaf = -1, ReadOnlySpan<VisOverrideData> visData = default, Span<float> waterReflectionHeight = default) {
		IncrementGlobalCounter();

		g_bViewerInSolidSpace = false;

		Array.Clear(g_RenderAreaBits, 0, g_RenderAreaBits.Length);
		Array.Clear(g_AreaStack, 0, g_AreaStack.Length);

		PortalRect rect = new();
		rect.Left = rect.Bottom = -1;
		rect.Top = rect.Right = 1;

		int leaf = forceViewLeaf;
		if (forceViewLeaf == -1)
			leaf = CM.PointLeafnum(g_EngineRenderer.ViewOrigin());

		if (r_portalscloseall.GetBool()) {
			if (cl.AreaBitsValid) {
				Array.Clear(g_RenderAreaBits, 0, g_RenderAreaBits.Length);
				int area = host_state.WorldBrush!.Leafs![leaf].Area;
				R_SetBit(g_RenderAreaBits, area);

				g_VisibleAreas[0] = (ushort)area;
				g_nVisibleAreas = 1;

				g_AreaCullInfo[area].GlobalCounter = g_GlobalCounter;
				g_AreaCullInfo[area].Rect = rect;

				R_SetupVisibleAreaFrustums();
			}
			else
				g_bViewerInSolidSpace = true;

			return;
		}

		if ((host_state.WorldBrush!.Leafs![leaf].Contents & (int)Contents.Solid) != 0 || cl.IsHLTV || !cl.AreaBitsValid || r_portalsopenall.GetBool()) {
			g_bViewerInSolidSpace = true;

			if (r_ShowViewerArea.GetInt() != 0) {
				// todo Con_NPrintf
			}
		}
		else {
			int area = host_state.WorldBrush!.Leafs![leaf].Area;

			if (r_ShowViewerArea.GetInt() != 0) {
				// todo Con_NPrintf
			}

			g_nVisibleAreas = 0;
			Vector3 vecVisOrigin = (visData.Length != 0) ? visData[0].VisOrigin : g_EngineRenderer.ViewOrigin();
			R_SetupGlobalFrustum();
			R_FlowThroughArea(area, vecVisOrigin, in rect, visData, waterReflectionHeight);
			R_SetupVisibleAreaFrustums();
		}
	}

	public static Frustum_t GetAreaFrustum(int area) {
		if (g_AreaCullInfo[area].GlobalCounter == g_GlobalCounter)
			return g_AreaCullInfo[area].Frustum;
		else
			return g_Frustum;
	}

	public static byte R_IsAreaVisible(int area) => (byte)(g_RenderAreaBits[area >> 3] & (1 << (area & 7)));
}
