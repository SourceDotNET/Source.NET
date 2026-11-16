using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Shared;

static class DebugOverlay
{
	readonly static IVDebugOverlay debugoverlay = Singleton<IVDebugOverlay>();
	public const float Persist = 0.0f;
	const int MAX_OVERLAY_DIST_SQR = 90000000;

	static BasePlayer? GetLocalPlayer() {
#if CLIENT_DLL
	return BasePlayer.GetLocalPlayer();
#else
		return Util.GetListenServerHost();
#endif
	}

	static BasePlayer? GetDebugPlayer() {
#if CLIENT_DLL
	return GetLocalPlayer();
#else
		return Util.PlayerByIndex(-1 /*DebugPlayer*/);
#endif
	}

	public static void Box(in Vector3 origin, in Vector3 mins, in Vector3 maxs, int r, int g, int b, int a, float duration) => BoxAngles(origin, mins, maxs, QAngle.Zero, r, g, b, a, duration);

	public static void BoxDirection(in Vector3 origin, in Vector3 mins, in Vector3 maxs, in Vector3 orientation, int r, int g, int b, int a, float duration) {
		QAngle angle = QAngle.Zero;
		angle.X = Util.VecToYaw(orientation);
		BoxAngles(origin, mins, maxs, angle, r, g, b, a, duration);
	}

	public static void BoxAngles(in Vector3 origin, in Vector3 mins, in Vector3 maxs, QAngle angles, int r, int g, int b, int a, float duration) => debugoverlay.AddBoxOverlay(origin, mins, maxs, angles, r, g, b, a, duration);

	public static void SweptBox(in Vector3 start, in Vector3 end, in Vector3 mins, in Vector3 maxs, QAngle angles, int r, int g, int b, int a, float duration) => debugoverlay.AddSweptBoxOverlay(start, end, mins, maxs, angles, r, g, b, a, duration);

	public static void EntityBounds(BaseEntity entity, int r, int g, int b, int a, float duration) {
		CollisionProperty collide = entity.CollisionProp();
		BoxAngles(collide.GetCollisionOrigin(), collide.OBBMins(), collide.OBBMaxs(), collide.GetCollisionAngles(), r, g, b, a, duration);
	}

	public static void DebugDrawLine(in Vector3 absStart, in Vector3 absEnd, int r, int g, int b, bool test, float duration) => Line(absStart + new Vector3(0, 0, 0.1f), absEnd + new Vector3(0, 0, 0.1f), r, g, b, test, duration);

	public static void Line(in Vector3 origin, in Vector3 target, int r, int g, int b, bool noDepthTest, float duration) {
		BasePlayer? player = GetLocalPlayer();
		if (player == null)
			return;

		// Clip line that is far away
		if (((player.GetAbsOrigin() - origin).LengthSqr() > MAX_OVERLAY_DIST_SQR) &&
			((player.GetAbsOrigin() - target).LengthSqr() > MAX_OVERLAY_DIST_SQR))
			return;

		// Clip line that is behind the client
		player.EyeVectors(out Vector3 clientForward);

		Vector3 toOrigin = origin - player.GetAbsOrigin();
		Vector3 toTarget = target - player.GetAbsOrigin();
		float dotOrigin = MathLib.DotProduct(clientForward, toOrigin);
		float dotTarget = MathLib.DotProduct(clientForward, toTarget);

		if (dotOrigin < 0 && dotTarget < 0)
			return;

		debugoverlay.AddLineOverlay(origin, target, r, g, b, noDepthTest, duration);
	}

	public static void Triangle(in Vector3 p1, in Vector3 p2, in Vector3 p3, int r, int g, int b, int a, bool noDepthTest, float duration) {
		BasePlayer? player = GetLocalPlayer();
		if (player == null)
			return;

		// Clip triangles that are far away
		Vector3 to1 = p1 - player.GetAbsOrigin();
		Vector3 to2 = p2 - player.GetAbsOrigin();
		Vector3 to3 = p3 - player.GetAbsOrigin();

		if ((to1.LengthSqr() > MAX_OVERLAY_DIST_SQR) &&
			(to2.LengthSqr() > MAX_OVERLAY_DIST_SQR) &&
			(to3.LengthSqr() > MAX_OVERLAY_DIST_SQR)) {
			return;
		}

		// Clip triangles that are behind the client
		player.EyeVectors(out Vector3 clientForward, out _, out _);

		float dot1 = MathLib.DotProduct(clientForward, to1);
		float dot2 = MathLib.DotProduct(clientForward, to2);
		float dot3 = MathLib.DotProduct(clientForward, to3);

		if (dot1 < 0 && dot2 < 0 && dot3 < 0)
			return;

		debugoverlay.AddTriangleOverlay(p1, p2, p3, r, g, b, a, noDepthTest, duration);
	}

	public static void EntityText(int entityID, int text_offset, ReadOnlySpan<char> text, float duration, int r, int g, int b, int a) => debugoverlay.AddEntityTextOverlay(entityID, text_offset, duration, (int)Math.Clamp(r * 255.0f, 0.0f, 255.0f), (int)Math.Clamp(g * 255.0f, 0.0f, 255.0f), (int)Math.Clamp(b * 255.0f, 0.0f, 255.0f), (int)Math.Clamp(a * 255.0f, 0.0f, 255.0f), text);

	public static void EntityTextAtPosition(in Vector3 origin, int text_offset, ReadOnlySpan<char> text, float duration, int r, int g, int b, int a) => debugoverlay.AddTextOverlayRGB(origin, text_offset, duration, r, g, b, a, text);

	public static void Grid(in Vector3 position) => debugoverlay.AddGridOverlay(position);

	public static void Text(in Vector3 origin, ReadOnlySpan<char> text, bool viewCheck, float duration) {
		BasePlayer? player = GetLocalPlayer();
		if (player == null)
			return;

		// Clip text that is far away
		if ((player.GetAbsOrigin() - origin).LengthSqr() > MAX_OVERLAY_DIST_SQR)
			return;

		// Clip text that is behind the client
		player.EyeVectors(out Vector3 clientForward);

		Vector3 toText = origin - player.GetAbsOrigin();
		float dotPr = MathLib.DotProduct(clientForward, toText);

		if (dotPr < 0)
			return;

		// Clip text that is obscured
		if (viewCheck) {
			Util.TraceLine(player.GetAbsOrigin(), origin, Mask.Opaque, null, Source.CollisionGroup.None, out Trace tr);

			if ((tr.EndPos - origin).Length() > 10)
				return;
		}

		debugoverlay.AddTextOverlay(origin, duration, text);
	}

	public static void ScreenText(float flXpos, float flYpos, ReadOnlySpan<char> text, int r, int g, int b, int a, float duration) => debugoverlay.AddScreenTextOverlay(flXpos, flYpos, duration, r, g, b, a, text);

	public static void Cross3D(in Vector3 position, in Vector3 mins, in Vector3 maxs, int r, int g, int b, bool noDepthTest, float duration) {
		Vector3 start = mins + position;
		Vector3 end = maxs + position;
		Line(start, end, r, g, b, noDepthTest, duration);

		start.X += maxs.X - mins.X;
		end.X -= maxs.X - mins.X;
		Line(start, end, r, g, b, noDepthTest, duration);

		start.Y += maxs.Y - mins.Y;
		end.Y -= maxs.Y - mins.Y;
		Line(start, end, r, g, b, noDepthTest, duration);

		start.X -= maxs.X - mins.X;
		end.X += maxs.X - mins.X;
		Line(start, end, r, g, b, noDepthTest, duration);
	}

	public static void Cross3D(in Vector3 position, float size, int r, int g, int b, bool noDepthTest, float duration) {
		Line(position + new Vector3(size, 0, 0), position - new Vector3(size, 0, 0), r, g, b, noDepthTest, duration);
		Line(position + new Vector3(0, size, 0), position - new Vector3(0, size, 0), r, g, b, noDepthTest, duration);
		Line(position + new Vector3(0, 0, size), position - new Vector3(0, 0, size), r, g, b, noDepthTest, duration);
	}

	public static void Cross3DOriented(in Vector3 position, QAngle angles, float size, int r, int g, int b, bool noDepthTest, float duration) {
		MathLib.AngleVectors(angles, out Vector3 forward, out Vector3 right, out Vector3 up);

		forward *= size;
		right *= size;
		up *= size;

		Line(position + right, position - right, r, g, b, noDepthTest, duration);
		Line(position + forward, position - forward, r, g, b, noDepthTest, duration);
		Line(position + up, position - up, r, g, b, noDepthTest, duration);
	}

	public static void Cross3DOriented(Matrix3x4 m, float size, int c, bool noDepthTest, float duration) {
		MathLib.MatrixGetColumn(m, 0, out Vector3 forward);
		MathLib.MatrixGetColumn(m, 1, out Vector3 left);
		MathLib.MatrixGetColumn(m, 2, out Vector3 up);
		MathLib.MatrixGetColumn(m, 3, out Vector3 position);

		forward *= size;
		left *= size;
		up *= size;

		Line(position + left, position - left, 0, c, 0, noDepthTest, duration);
		Line(position + forward, position - forward, c, 0, 0, noDepthTest, duration);
		Line(position + up, position - up, 0, 0, c, noDepthTest, duration);
	}

	public static void DrawTickMarkedLine(in Vector3 startPos, in Vector3 endPos, float tickDist, int tickTextDist, int r, int g, int b, bool noDepthTest, float duration) {
		BasePlayer? player = GetDebugPlayer();
		if (player == null)
			return;

		Vector3 lineDir = endPos - startPos;
		float lineDist = MathLib.VectorNormalize(ref lineDir);
		int numTicks = (int)(lineDist / tickDist);
		Vector3 bodyDir;

#if CLIENT_DLL
		MathLib.AngleVectors(player.LocalEyeAngles(), out bodyDir);
#else
		bodyDir = player.BodyDirection2D();
#endif

		Vector3 upVec = 4 * bodyDir;
		Vector3 tickPos = startPos;
		int tickTextCnt = 0;

		MathLib.CrossProduct(lineDir, upVec, out Vector3 sideDir);

		Line(startPos, endPos, r, g, b, noDepthTest, duration);

		for (int i = 0; i < numTicks + 1; i++) {
			Vector3 tickLeft = tickPos - sideDir;
			Vector3 tickRight = tickPos + sideDir;

			if (tickTextCnt == tickTextDist) {
				Vector3 textPos = tickLeft + new Vector3(0, 0, 8);
				Line(tickLeft, tickRight, 255, 255, 255, noDepthTest, duration);
				Text(textPos, i.ToString(), true, 0);
				tickTextCnt = 0;
			}
			else
				Line(tickLeft, tickRight, r, g, b, noDepthTest, duration);

			tickTextCnt++;

			tickPos += (tickDist * lineDir);
		}
	}

	public static void DrawGroundCrossHairOverlay() {
		BasePlayer? player = GetDebugPlayer();
		if (player == null)
			return;

		Vector3 source = player.EyePosition();
		player.EyeVectors(out Vector3 forward);

		Util.TraceLine(source, source + forward * 2048, Mask.Solid, player, Source.CollisionGroup.None, out Trace tr);
		float dotPr = MathLib.DotProduct(new Vector3(0, 0, 1), tr.Plane.Normal);
		if (tr.Fraction != 1.0 && dotPr > 0.5) {
			tr.EndPos.Z += 1;
			float scale = 6;
			Vector3 startPos = tr.EndPos + new Vector3(-scale, 0, 0);
			Vector3 endPos = tr.EndPos + new Vector3(scale, 0, 0);
			Line(startPos, endPos, 255, 0, 0, false, 0);

			startPos = tr.EndPos + new Vector3(0, -scale, 0);
			endPos = tr.EndPos + new Vector3(0, scale, 0);
			Line(startPos, endPos, 255, 0, 0, false, 0);
		}
	}

	public static void HorzArrow(in Vector3 startPos, in Vector3 endPos, float width, int r, int g, int b, int a, bool noDepthTest, float duration) {
		Vector3 lineDir = (endPos - startPos);
		MathLib.VectorNormalize(ref lineDir);
		Vector3 upVec = new(0, 0, 1);

		float radius = (float)(width / 2.0);

		MathLib.CrossProduct(lineDir, upVec, out Vector3 sideDir);

		Vector3 p1 = startPos - sideDir * radius;
		Vector3 p2 = endPos - lineDir * width - sideDir * radius;
		Vector3 p3 = endPos - lineDir * width - sideDir * width;
		Vector3 p4 = endPos;
		Vector3 p5 = endPos - lineDir * width + sideDir * width;
		Vector3 p6 = endPos - lineDir * width + sideDir * radius;
		Vector3 p7 = startPos + sideDir * radius;

		// Outline the arrow
		Line(p1, p2, r, g, b, noDepthTest, duration);
		Line(p2, p3, r, g, b, noDepthTest, duration);
		Line(p3, p4, r, g, b, noDepthTest, duration);
		Line(p4, p5, r, g, b, noDepthTest, duration);
		Line(p5, p6, r, g, b, noDepthTest, duration);
		Line(p6, p7, r, g, b, noDepthTest, duration);

		if (a > 0) {
			// Fill us in with triangles
			Triangle(p5, p4, p3, r, g, b, a, noDepthTest, duration); // Tip
			Triangle(p1, p7, p6, r, g, b, a, noDepthTest, duration); // Shaft
			Triangle(p6, p2, p1, r, g, b, a, noDepthTest, duration);

			// And backfaces
			Triangle(p3, p4, p5, r, g, b, a, noDepthTest, duration); // Tip
			Triangle(p6, p7, p1, r, g, b, a, noDepthTest, duration); // Shaft
			Triangle(p1, p2, p6, r, g, b, a, noDepthTest, duration);
		}
	}

	public static void YawArrow(in Vector3 startPos, float yaw, float length, float width, int r, int g, int b, int a, bool noDepthTest, float duration) {
		Vector3 forward = Util.YawToVector(yaw);
		HorzArrow(startPos, startPos + forward * length, width, r, g, b, a, noDepthTest, duration);
	}

	public static void VertArrow(in Vector3 startPos, in Vector3 endPos, float width, int r, int g, int b, int a, bool noDepthTest, float duration) {
		Vector3 lineDir = endPos - startPos;
		MathLib.VectorNormalize(ref lineDir);
		float radius = width / 2.0f;

		MathLib.VectorVectors(lineDir, out Vector3 sideDir, out Vector3 upVec);

		Vector3 p1 = startPos - upVec * radius;
		Vector3 p2 = endPos - lineDir * width - upVec * radius;
		Vector3 p3 = endPos - lineDir * width - upVec * width;
		Vector3 p4 = endPos;
		Vector3 p5 = endPos - lineDir * width + upVec * width;
		Vector3 p6 = endPos - lineDir * width + upVec * radius;
		Vector3 p7 = startPos + upVec * radius;

		Line(p1, p2, r, g, b, noDepthTest, duration);
		Line(p2, p3, r, g, b, noDepthTest, duration);
		Line(p3, p4, r, g, b, noDepthTest, duration);
		Line(p4, p5, r, g, b, noDepthTest, duration);
		Line(p5, p6, r, g, b, noDepthTest, duration);
		Line(p6, p7, r, g, b, noDepthTest, duration);

		if (a > 0) {
			Triangle(p5, p4, p3, r, g, b, a, noDepthTest, duration);
			Triangle(p1, p7, p6, r, g, b, a, noDepthTest, duration);
			Triangle(p6, p2, p1, r, g, b, a, noDepthTest, duration);

			Triangle(p3, p4, p5, r, g, b, a, noDepthTest, duration);
			Triangle(p6, p7, p1, r, g, b, a, noDepthTest, duration);
			Triangle(p1, p2, p6, r, g, b, a, noDepthTest, duration);
		}
	}

	public static void Axis(in Vector3 position, QAngle angles, float size, bool noDepthTest, float duration) {
		MathLib.AngleVectors(angles, out Vector3 xvec, out Vector3 yvec, out Vector3 zvec);

		xvec = position + (size * xvec);
		yvec = position - (size * yvec);
		zvec = position + (size * zvec);

		Line(position, xvec, 255, 0, 0, noDepthTest, duration);
		Line(position, yvec, 0, 255, 0, noDepthTest, duration);
		Line(position, zvec, 0, 0, 255, noDepthTest, duration);
	}

	public static void Sphere(in Vector3 center, float radius, int r, int g, int b, bool noDepthTest, float duration) {
		Vector3 edge, lastEdge;

		float axisSize = radius;
		Line(center + new Vector3(0, 0, -axisSize), center + new Vector3(0, 0, axisSize), r, g, b, noDepthTest, duration);
		Line(center + new Vector3(0, -axisSize, 0), center + new Vector3(0, axisSize, 0), r, g, b, noDepthTest, duration);
		Line(center + new Vector3(-axisSize, 0, 0), center + new Vector3(axisSize, 0, 0), r, g, b, noDepthTest, duration);

		lastEdge = new Vector3(radius + center.X, center.Y, center.Z);
		float angle;
		for (angle = 0.0f; angle <= 360.0f; angle += 22.5f) {
			edge.X = radius * MathF.Cos(angle / 180.0f * MathF.PI) + center.X;
			edge.Y = center.Y;
			edge.Z = radius * MathF.Sin(angle / 180.0f * MathF.PI) + center.Z;

			Line(edge, lastEdge, r, g, b, noDepthTest, duration);

			lastEdge = edge;
		}

		lastEdge = new Vector3(center.X, radius + center.Y, center.Z);
		for (angle = 0.0f; angle <= 360.0f; angle += 22.5f) {
			edge.X = center.X;
			edge.Y = radius * MathF.Cos(angle / 180.0f * MathF.PI) + center.Y;
			edge.Z = radius * MathF.Sin(angle / 180.0f * MathF.PI) + center.Z;

			Line(edge, lastEdge, r, g, b, noDepthTest, duration);

			lastEdge = edge;
		}

		lastEdge = new Vector3(center.X, radius + center.Y, center.Z);
		for (angle = 0.0f; angle <= 360.0f; angle += 22.5f) {
			edge.X = radius * MathF.Cos(angle / 180.0f * MathF.PI) + center.X;
			edge.Y = radius * MathF.Sin(angle / 180.0f * MathF.PI) + center.Y;
			edge.Z = center.Z;

			Line(edge, lastEdge, r, g, b, noDepthTest, duration);

			lastEdge = edge;
		}
	}

	public static void Circle(in Vector3 position, float radius, int r, int g, int b, int a, bool bNoDepthTest, float duration) {
		BasePlayer? player = GetLocalPlayer();
		if (player == null)
			return;

		player.EyeVectors(out Vector3 clientForward);

		MathLib.VectorAngles(clientForward, out QAngle angles);

		Circle(position, angles, radius, r, g, b, a, bNoDepthTest, duration);
	}

	public static void Circle(in Vector3 position, QAngle angles, float radius, int r, int g, int b, int a, bool bNoDepthTest, float duration) {
		MathLib.AngleMatrix(angles, position, out Matrix3x4 xform);
		MathLib.MatrixGetColumn(xform, 2, out Vector3 xAxis);
		MathLib.MatrixGetColumn(xform, 1, out Vector3 yAxis);
		Circle(position, xAxis, yAxis, radius, r, g, b, a, bNoDepthTest, duration);
	}

	public static void Circle(in Vector3 position, in Vector3 xAxis, in Vector3 yAxis, float radius, int r, int g, int b, int a, bool bNoDepthTest, float duration) {
		const uint segments = 16;
		const float radStep = (MathF.PI * 2.0f) / segments;

		Vector3 lastPosition;

		Vector3 start = position + xAxis * radius;
		Vector3 pos = start;

		for (uint i = 1; i <= segments; i++) {
			lastPosition = pos;

			MathLib.SinCos(radStep * i, out float sin, out float cos);
			pos = position + (xAxis * cos * radius) + (yAxis * sin * radius);

			Line(lastPosition, pos, r, g, b, bNoDepthTest, duration);

			if (a != 0 && i > 1)
				debugoverlay.AddTriangleOverlay(start, lastPosition, pos, r, g, b, a, bNoDepthTest, duration);
		}
	}

	public static void Sphere(in Vector3 position, QAngle angles, float radius, int r, int g, int b, int a, bool bNoDepthTest, float duration) {
		MathLib.AngleMatrix(angles, position, out Matrix3x4 xform);
		MathLib.MatrixGetColumn(xform, 0, out Vector3 xAxis);
		MathLib.MatrixGetColumn(xform, 1, out Vector3 yAxis);
		MathLib.MatrixGetColumn(xform, 2, out Vector3 zAxis);
		Circle(position, xAxis, yAxis, radius, r, g, b, a, bNoDepthTest, duration);
		Circle(position, yAxis, zAxis, radius, r, g, b, a, bNoDepthTest, duration);
		Circle(position, xAxis, zAxis, radius, r, g, b, a, bNoDepthTest, duration);
	}
}