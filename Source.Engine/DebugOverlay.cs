using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Mathematics;

using System.Numerics;

namespace Source.Engine;

public enum OverlayType
{
	Box,
	Sphere,
	Line,
	Triangle,
	SweptBox,
	Box2
}

public class OverlayBase : IPoolableObject
{
	public static T New<T>() where T : OverlayBase, new() {
		return ObjectPool<T>.Shared.Alloc();
	}

	public static void Free<T>(T obj) where T : OverlayBase, new() {
		ObjectPool<T>.Shared.Free(obj);
	}

	public OverlayBase() { Reset(); }
	public OverlayType Type;
	public long CreationTick;
	public long ServerCount;
	public TimeUnit_t EndTime;
	public OverlayBase? NextOverlay;

	public void Init() { }
	public virtual void Reset() {
		Type = OverlayType.Box;
		ServerCount = -1;
		CreationTick = -1;
		EndTime = 0;
		NextOverlay = null;
	}

	public bool IsDead() {
		if (ServerCount != cl.ServerCount)
			return true;

		if (CreationTick != -1) {
			if (DebugOverlay.GetOverlayTick() > CreationTick)
				return true;

			return false;
		}

		if (EndTime == IVDebugOverlay.NDEBUG_PERSIST_TILL_NEXT_SERVER)
			return false;

		return (cl.GetTime() >= EndTime);
	}

	public void SetEndTime(float duration) {
		ServerCount = cl.ServerCount;

		if (duration <= 0.0f) {
			CreationTick = DebugOverlay.GetOverlayTick();
			return;
		}

		if (duration == IVDebugOverlay.NDEBUG_PERSIST_TILL_NEXT_SERVER)
			EndTime = IVDebugOverlay.NDEBUG_PERSIST_TILL_NEXT_SERVER;
		else
			EndTime = cl.GetTime() + duration;
	}
}

public class OverlayBox : OverlayBase
{
	public override void Reset() {
		base.Reset();
		Type = OverlayType.Box;
	}
	public Vector3 Origin;
	public Vector3 Mins;
	public Vector3 Maxs;
	public Vector3 Angles;
	public int R;
	public int G;
	public int B;
	public int A;
}

public class OverlayLine : OverlayBase
{
	public override void Reset() {
		base.Reset();
		Type = OverlayType.Line;
	}
	public Vector3 Start;
	public Vector3 End;
	public int R;
	public int G;
	public int B;
	public int A;
	public bool NoDepthTest;
}

public class OverlayTriangle : OverlayBase
{
	public override void Reset() {
		base.Reset();
		Type = OverlayType.Triangle;
	}
	public Vector3 P1;
	public Vector3 P2;
	public Vector3 P3;
	public int R;
	public int G;
	public int B;
	public int A;
	public bool NoDepthTest;
}

public class OverlayBox2 : OverlayBase
{
	public override void Reset() {
		base.Reset();
		Type = OverlayType.Box2;
	}
	public Vector3 Origin;
	public Vector3 Mins;
	public Vector3 Maxs;
	public Vector3 Angles;
	public Color EdgeColor;
	public Color FaceColor;
}

public class OverlaySphere : OverlayBase
{
	public override void Reset() {
		base.Reset();
		Type = OverlayType.Sphere;
	}
	public Vector3 Origin;
	public float Radius;
	public int Theta;
	public int Phi;
	public int R;
	public int G;
	public int B;
	public int A;
}

public class OverlaySweptBox : OverlayBase
{
	public override void Reset() {
		base.Reset();
		Type = OverlayType.SweptBox;
	}
	public Vector3 Start;
	public Vector3 End;
	public Vector3 Mins;
	public Vector3 Maxs;
	public QAngle Angles;
	public int R;
	public int G;
	public int B;
	public int A;
}

public class DebugOverlay : IVDebugOverlay
{
	private InlineArray1024<char> Text;
	private nint argptr;

	public static long GetOverlayTick() => sv.IsActive() ? sv.TickCount : cl.GetClientTickCount();

	static readonly object s_OverlayMutex = new();
	static OverlayBase? s_pOverlays;
	static OverlayText? s_pOverlayText;

	public void AddBoxOverlay(in Vector3 origin, in Vector3 mins, in Vector3 maxs, in QAngle angles, int r, int g, int b, int a, float duration) {
		if (cl.IsPaused())
			return;

		lock (s_OverlayMutex) {
			OverlayBox new_overlay = OverlayBase.New<OverlayBox>();

			new_overlay.Origin = origin;

			new_overlay.Mins[0] = mins[0];
			new_overlay.Mins[1] = mins[1];
			new_overlay.Mins[2] = mins[2];

			new_overlay.Maxs[0] = maxs[0];
			new_overlay.Maxs[1] = maxs[1];
			new_overlay.Maxs[2] = maxs[2];

			new_overlay.Angles = angles;

			new_overlay.R = r;
			new_overlay.G = g;
			new_overlay.B = b;
			new_overlay.A = a;

			new_overlay.SetEndTime(duration);

			new_overlay.NextOverlay = s_pOverlays;
			s_pOverlays = new_overlay;
		}
	}

	public void AddBoxOverlay2(in Vector3 origin, in Vector3 mins, in Vector3 max, in QAngle orientation, in Color faceColor, in Color edgeColor, float duration) {
		if (cl.IsPaused())
			return;

		lock (s_OverlayMutex) {
			OverlayBox2 new_overlay = OverlayBase.New<OverlayBox2>();

			new_overlay.Origin = origin;

			new_overlay.Mins[0] = mins[0];
			new_overlay.Mins[1] = mins[1];
			new_overlay.Mins[2] = mins[2];

			new_overlay.Maxs[0] = max[0];
			new_overlay.Maxs[1] = max[1];
			new_overlay.Maxs[2] = max[2];

			new_overlay.Angles = orientation;

			new_overlay.FaceColor = faceColor;
			new_overlay.EdgeColor = edgeColor;

			new_overlay.SetEndTime(duration);

			new_overlay.NextOverlay = s_pOverlays;
			s_pOverlays = new_overlay;
		}
	}

	static bool GetEntityOriginClientOrServer(int ent_num, out Vector3 origin) {
		lock (s_OverlayMutex) {
			origin = default;

			if (sv.IsActive()) {
				Edict? e = sv.Edicts![ent_num];
				if (e != null) {
					IServerEntity? serverEntity = e.GetIServerEntity();
					if (serverEntity != null)
						CM.WorldSpaceCenter(serverEntity.GetCollideable()!, out origin);

					return true;
				}
			}
			else {
				IClientEntity? clent = entitylist.GetClientEntity(ent_num);
				if (clent != null) {
					CM.WorldSpaceCenter(clent.GetCollideable()!, out origin);
					return true;
				}
			}

			return false;
		}
	}

	public void AddEntityTextOverlay(int ent_index, int line_offset, float duration, int r, int g, int b, int a, ReadOnlySpan<char> text) {
		if (cl.IsPaused())
			return;

		lock (s_OverlayMutex) {
			OverlayText new_overlay = new();

			GetEntityOriginClientOrServer(ent_index, out Vector3 myPos);

			MathLib.VectorCopy(myPos, out new_overlay.Origin);
			strcpy(new_overlay.Text, text);
			new_overlay.UseOrigin = true;
			new_overlay.LineOffset = line_offset;
			new_overlay.SetEndTime(duration);
			new_overlay.R = r;
			new_overlay.G = g;
			new_overlay.B = b;
			new_overlay.A = a;

			new_overlay.NextOverlayText = s_pOverlayText;
			s_pOverlayText = new_overlay;
		}
	}

	static Vector3 s_vGridPosition;

	public void AddGridOverlay(in Vector3 origin) {
		if (cl.IsPaused())
			return;

		lock (s_OverlayMutex) {
			s_vGridPosition[0] = origin[0];
			s_vGridPosition[1] = origin[1];
			s_vGridPosition[2] = origin[2];
			s_bDrawGrid = true;
		}
	}

	public void AddLineOverlay(in Vector3 origin, in Vector3 dest, int r, int g, int b, bool noDepthTest, float duration) {
		if (cl.IsPaused())
			return;

		lock (s_OverlayMutex) {
			OverlayLine new_overlay = OverlayBase.New<OverlayLine>();

			new_overlay.Start = origin;
			new_overlay.End = dest;

			new_overlay.R = r;
			new_overlay.G = g;
			new_overlay.B = b;
			new_overlay.A = 255;

			new_overlay.NoDepthTest = noDepthTest;

			new_overlay.SetEndTime(duration);

			new_overlay.NextOverlay = s_pOverlays;
			s_pOverlays = new_overlay;
		}
	}

	public void AddLineOverlayAlpha(in Vector3 origin, in Vector3 dest, int r, int g, int b, int a, bool noDepthTest, float duration) {
		if (cl.IsPaused())
			return;

		lock (s_OverlayMutex) {
			OverlayLine new_overlay = OverlayBase.New<OverlayLine>();

			new_overlay.Start = origin;
			new_overlay.End = dest;

			new_overlay.R = r;
			new_overlay.G = g;
			new_overlay.B = b;
			new_overlay.A = a;

			new_overlay.NoDepthTest = noDepthTest;

			new_overlay.SetEndTime(duration);

			new_overlay.NextOverlay = s_pOverlays;
			s_pOverlays = new_overlay;
		}
	}

	public void AddScreenTextOverlay(float flXPos, float flYPos, int line_offset, float duration, int r, int g, int b, int a, ReadOnlySpan<char> text) {
		if (cl.IsPaused())
			return;

		lock (s_OverlayMutex) {
			OverlayText new_overlay = new();

			strcpy(new_overlay.Text, text);
			new_overlay.XPos = flXPos;
			new_overlay.YPos = flYPos;
			new_overlay.UseOrigin = false;
			new_overlay.LineOffset = line_offset;
			new_overlay.SetEndTime(duration);
			new_overlay.R = r;
			new_overlay.G = g;
			new_overlay.B = b;
			new_overlay.A = a;

			new_overlay.NextOverlayText = s_pOverlayText;
			s_pOverlayText = new_overlay;
		}
	}

	public void AddScreenTextOverlay(float flXPos, float flYPos, float duration, int r, int g, int b, int a, ReadOnlySpan<char> text) => AddScreenTextOverlay(flXPos, flYPos, 0, duration, r, g, b, a, text);

	public void AddSweptBoxOverlay(in Vector3 start, in Vector3 end, in Vector3 mins, in Vector3 max, in QAngle angles, int r, int g, int b, int a, float flDuration) {
		if (cl.IsPaused())
			return;

		lock (s_OverlayMutex) {
			OverlaySweptBox new_overlay = OverlayBase.New<OverlaySweptBox>();

			new_overlay.Start = start;
			new_overlay.End = end;

			new_overlay.Mins[0] = mins[0];
			new_overlay.Mins[1] = mins[1];
			new_overlay.Mins[2] = mins[2];

			new_overlay.Maxs[0] = max[0];
			new_overlay.Maxs[1] = max[1];
			new_overlay.Maxs[2] = max[2];

			new_overlay.Angles = angles;

			new_overlay.R = r;
			new_overlay.G = g;
			new_overlay.B = b;
			new_overlay.A = a;

			new_overlay.SetEndTime(flDuration);

			new_overlay.NextOverlay = s_pOverlays;
			s_pOverlays = new_overlay;
		}
	}

	public void AddTextOverlay(in Vector3 origin, float duration, ReadOnlySpan<char> text) {
		if (cl.IsPaused())
			return;

		lock (s_OverlayMutex) {
			OverlayText new_overlay = new();

			MathLib.VectorCopy(origin, out new_overlay.Origin);
			strcpy(new_overlay.Text, text);
			new_overlay.UseOrigin = true;
			new_overlay.LineOffset = 0;
			new_overlay.SetEndTime(duration);
			new_overlay.R = 255;
			new_overlay.G = 255;
			new_overlay.B = 255;
			new_overlay.A = 255;

			new_overlay.NextOverlayText = s_pOverlayText;
			s_pOverlayText = new_overlay;
		}
	}

	public void AddTextOverlay(in Vector3 origin, int line_offset, float duration, ReadOnlySpan<char> text) {
		if (cl.IsPaused())
			return;

		lock (s_OverlayMutex) {
			OverlayText new_overlay = new();

			MathLib.VectorCopy(origin, out new_overlay.Origin);
			strcpy(new_overlay.Text, text);
			new_overlay.UseOrigin = true;
			new_overlay.LineOffset = line_offset;
			new_overlay.SetEndTime(duration);
			new_overlay.R = 255;
			new_overlay.G = 255;
			new_overlay.B = 255;
			new_overlay.A = 255;

			new_overlay.NextOverlayText = s_pOverlayText;
			s_pOverlayText = new_overlay;
		}
	}

	public void AddTextOverlay(in Vector3 origin, int line_offset, float duration, int r, int g, int b, int a, ReadOnlySpan<char> text) { }

	public void AddTextOverlayRGB(in Vector3 origin, int line_offset, float duration, float r, float g, float b, float alpha, ReadOnlySpan<char> text) {
		if (cl.IsPaused())
			return;

		lock (s_OverlayMutex) {
			OverlayText new_overlay = new();

			MathLib.VectorCopy(origin, out new_overlay.Origin);
			strcpy(new_overlay.Text, text);
			new_overlay.UseOrigin = true;
			new_overlay.LineOffset = line_offset;
			new_overlay.SetEndTime(duration);
			new_overlay.R = (int)Math.Clamp(r * 255.0f, 0.0f, 255.0f);
			new_overlay.G = (int)Math.Clamp(g * 255.0f, 0.0f, 255.0f);
			new_overlay.B = (int)Math.Clamp(b * 255.0f, 0.0f, 255.0f);
			new_overlay.A = (int)Math.Clamp(alpha * 255.0f, 0.0f, 255.0f);
			new_overlay.UseOrigin = true;

			new_overlay.NextOverlayText = s_pOverlayText;
			s_pOverlayText = new_overlay;
		}
	}

	public void AddTextOverlayRGB(in Vector3 origin, int line_offset, float duration, int r, int g, int b, int a, ReadOnlySpan<char> text) => AddTextOverlayRGB(origin, line_offset, duration, r * 1.0f / 255.0f, g * 1.0f / 255.0f, b * 1.0f / 255.0f, a * 1.0f / 255.0f, text);

	public void AddTriangleOverlay(in Vector3 p1, in Vector3 p2, in Vector3 p3, int r, int g, int b, int a, bool noDepthTest, float duration) {
		if (cl.IsPaused())
			return;

		lock (s_OverlayMutex) {
			OverlayTriangle new_overlay = OverlayBase.New<OverlayTriangle>();
			new_overlay.P1 = p1;
			new_overlay.P2 = p2;
			new_overlay.P3 = p3;

			new_overlay.R = r;
			new_overlay.G = g;
			new_overlay.B = b;
			new_overlay.A = a;
			new_overlay.NoDepthTest = noDepthTest;

			new_overlay.SetEndTime(duration);

			new_overlay.NextOverlay = s_pOverlays;
			s_pOverlays = new_overlay;
		}
	}

	public static void ClearAllOverlays() {
		lock (s_OverlayMutex) {
			while (s_pOverlays != null) {
				OverlayBase pOldOverlay = s_pOverlays;
				s_pOverlays = s_pOverlays.NextOverlay;
				DestroyOverlay(pOldOverlay);
			}
			s_pOverlayText = null;
		}

		s_bDrawGrid = false;
	}

	void IVDebugOverlay.ClearAllOverlays() => DebugOverlay.ClearAllOverlays();

	public void ClearDeadOverlays() {
		lock (s_OverlayMutex) {
			OverlayText? currText = s_pOverlayText;
			OverlayText? lastText = null;

			while (currText != null) {
				if (currText.IsDead()) {
					if (lastText != null)
						lastText.NextOverlayText = currText.NextOverlayText;
					else
						s_pOverlayText = currText.NextOverlayText;

					currText = currText.NextOverlayText;
				}
				else {
					lastText = currText;
					currText = currText.NextOverlayText;
				}
			}
		}
	}

	public OverlayText? GetFirst() => s_pOverlayText;

	public OverlayText? GetNext(OverlayText? current) => current!.NextOverlayText;

	public int ScreenPosition(in Vector3 point, out Vector3 screen) {
		lock (s_OverlayMutex) {
			int retval = g_EngineRenderer.ClipTransform(point, out screen) ? 1 : 0;

			materials.GetRenderContext().GetViewport(out int x, out int y, out int w, out int h);

			screen[0] = screen[0] * w / 2;
			screen[1] = -screen[1] * h / 2;
			screen[0] += w / 2;
			screen[1] += h / 2;
			return retval;
		}
	}

	public int ScreenPosition(float xPos, float yPos, out Vector3 screen) {
		screen = default;
		if (xPos > 1.0 || yPos > 1.0 || xPos < 0.0 || yPos < 0.0)
			return 1;

		lock (s_OverlayMutex) {
			materials.GetRenderContext().GetViewport(out int x, out int y, out int w, out int h);

			screen[0] = xPos * w;
			screen[1] = yPos * h;
			return 0;
		}
	}

	static readonly ConVar enable_debug_overlays = new("enable_debug_overlays", "1", FCvar.GameDLL | FCvar.Cheat, "Enable rendering of debug overlays");

	static long previous_servercount = 0;
	static bool s_bDrawGrid = false;
	public static void Draw3DOverlays() {
		lock (s_OverlayMutex) {
			if (previous_servercount != cl.ServerCount) {
				ClearAllOverlays();
				previous_servercount = cl.ServerCount;
			}

			DrawAllOverlays();

			if (s_bDrawGrid)
				DrawGridOverlay();
		}
	}

	static int gridSpacing = 100;
	static int numHorzSpaces = 16;
	static int numVertSpaces = 3;

	static void DrawGridOverlay() {
		lock (s_OverlayMutex) {
			Vector3 startGrid;
			startGrid.X = gridSpacing * ((int)s_vGridPosition[0] / gridSpacing);
			startGrid.Y = gridSpacing * ((int)s_vGridPosition[1] / gridSpacing);
			startGrid.Z = s_vGridPosition[2];

			startGrid[0] -= (numHorzSpaces / 2) * gridSpacing;
			startGrid[1] -= (numHorzSpaces / 2) * gridSpacing;

			Vector3 color = new(20, 180, 190);
			for (int i = 1; i < numVertSpaces + 1; i++) {
				MathLib.VectorCopy(startGrid, out Vector3 startLine);
				for (int j = 0; j < numHorzSpaces + 1; j++) {
					MathLib.VectorCopy(startLine, out Vector3 endLine);
					endLine[0] += gridSpacing * numHorzSpaces;
					renderUtils.RenderLine(startLine, endLine, new Color((int)color.X, (int)color.Y, (int)color.Z, 255), true);

					MathLib.VectorCopy(startLine, out Vector3 bottomStartLine);
					for (int k = 0; k < numHorzSpaces + 1; k++) {
						MathLib.VectorCopy(bottomStartLine, out Vector3 bottomEndLine);
						bottomEndLine[2] -= gridSpacing;
						renderUtils.RenderLine(bottomStartLine, bottomEndLine, new Color((int)color.X, (int)color.Y, (int)color.Z, 255), true);
						bottomStartLine[0] += gridSpacing;
					}
					startLine[1] += gridSpacing;
				}

				MathLib.VectorCopy(startGrid, out startLine);
				for (int j = 0; j < numHorzSpaces + 1; j++) {
					MathLib.VectorCopy(startLine, out Vector3 endLine);

					endLine[1] += gridSpacing * numHorzSpaces;
					renderUtils.RenderLine(startLine, endLine, new Color((int)color.X, (int)color.Y, (int)color.Z, 255), true);
					startLine[0] += gridSpacing;
				}
				color *= 0.7f;
				startGrid[2] -= gridSpacing;
			}
			s_bDrawGrid = false;
		}
	}
	public static void DrawAllOverlays() {
		if (!enable_debug_overlays.GetBool())
			return;

		lock (s_OverlayMutex) {
			OverlayBase? currOverlay = s_pOverlays;
			OverlayBase? prevOverlay = null;
			OverlayBase? nextOverlay;

			while (currOverlay != null) {
				if (currOverlay.IsDead()) {
					if (prevOverlay != null)
						prevOverlay.NextOverlay = currOverlay.NextOverlay;
					else
						s_pOverlays = currOverlay.NextOverlay;


					nextOverlay = currOverlay.NextOverlay;
					DestroyOverlay(currOverlay);
					currOverlay = nextOverlay;
				}
				else {
					DrawOverlay(currOverlay);
					prevOverlay = currOverlay;
					currOverlay = currOverlay.NextOverlay;
				}
			}
		}
	}

	private static void DrawOverlay(OverlayBase overlay) {
		switch (overlay.Type) {
			case OverlayType.Line:
				OverlayLine line = (OverlayLine)overlay;
				renderUtils.RenderLine(line.Start, line.End, new Color(line.R, line.G, line.B, line.A), !line.NoDepthTest);
				break;
			case OverlayType.Box:
				OverlayBox box = (OverlayBox)overlay;

				if (box.A > 0)
					renderUtils.RenderBox(box.Origin, box.Angles, box.Mins, box.Maxs, new Color(box.R, box.G, box.B, box.A), false);

				renderUtils.RenderWireframeBox(box.Origin, box.Angles, box.Mins, box.Maxs, new Color(box.R, box.G, box.B, 255), true);
				break;
			case OverlayType.Box2:
				OverlayBox2 box2 = (OverlayBox2)overlay;

				if (box2.FaceColor.A > 0)
					renderUtils.RenderBox(box2.Origin, box2.Angles, box2.Mins, box2.Maxs, box2.FaceColor, false);

				if (box2.EdgeColor.A > 0)
					renderUtils.RenderWireframeBox(box2.Origin, box2.Angles, box2.Mins, box2.Maxs, box2.EdgeColor, false);
				break;
#if !SWDS
			case OverlayType.Sphere:
				OverlaySphere sphere = (OverlaySphere)overlay;
				renderUtils.RenderSphere(sphere.Origin, sphere.Radius, sphere.Theta, sphere.Phi, new Color(sphere.R, sphere.G, sphere.B, sphere.A), MatSys.MaterialAmbientCube!);
				break;
#endif
			case OverlayType.SweptBox:
				OverlaySweptBox sweptBox = (OverlaySweptBox)overlay;
				renderUtils.RenderWireframeSweptBox(sweptBox.Start, sweptBox.End, sweptBox.Angles, sweptBox.Mins, sweptBox.Maxs, new Color(sweptBox.R, sweptBox.G, sweptBox.B, sweptBox.A), true);
				break;
			case OverlayType.Triangle:
				OverlayTriangle tri = (OverlayTriangle)overlay;
				renderUtils.RenderTriangle(tri.P1, tri.P2, tri.P3, new Color(tri.R, tri.G, tri.B, tri.A), !tri.NoDepthTest);
				break;
		}
	}

	private static void DestroyOverlay(OverlayBase overlay) {
		switch (overlay.Type) {
			case OverlayType.Line:
				OverlayLine line = (OverlayLine)overlay;
				OverlayBase.Free(line);
				break;
			case OverlayType.Box:
				OverlayBox box = (OverlayBox)overlay;
				OverlayBase.Free(box);
				break;
			case OverlayType.Box2:
				OverlayBox2 box2 = (OverlayBox2)overlay;
				OverlayBase.Free(box2);
				break;
			case OverlayType.Sphere:
				OverlaySphere sphere = (OverlaySphere)overlay;
				OverlayBase.Free(sphere);
				break;
			case OverlayType.SweptBox:
				OverlaySweptBox sweptBox = (OverlaySweptBox)overlay;
				OverlayBase.Free(sweptBox);
				break;
			case OverlayType.Triangle:
				OverlayTriangle tri = (OverlayTriangle)overlay;
				OverlayBase.Free(tri);
				break;
		}
	}
}
