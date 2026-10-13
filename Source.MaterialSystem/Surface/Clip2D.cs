using CommunityToolkit.HighPerformance;

using Source.Common.GUI;

using System.Numerics;

namespace Source.MaterialSystem.Surface;

public static class Clip2D
{
	public delegate bool InsideCheck(in ScissorRectState rect, in SurfaceVertex vert);
	public delegate float ClipResult(in ScissorRectState rect, in Vector2 one, in Vector2 two);

	public static bool TopInside(in ScissorRectState rect, in SurfaceVertex vert) => vert.Position.Y >= rect.Top;
	public static float TopClip(in ScissorRectState rect, in Vector2 one, in Vector2 two) => (rect.Top - one.Y) / (two.Y - one.Y);

	public static bool LeftInside(in ScissorRectState rect, in SurfaceVertex vert) => vert.Position.X >= rect.Left;
	public static float LeftClip(in ScissorRectState rect, in Vector2 one, in Vector2 two) => (one.X - rect.Left) / (one.X - two.X);

	public static bool RightInside(in ScissorRectState rect, in SurfaceVertex vert) => vert.Position.X < rect.Right;
	public static float RightClip(in ScissorRectState rect, in Vector2 one, in Vector2 two) => (rect.Right - one.X) / (two.X - one.X);

	public static bool BottomInside(in ScissorRectState rect, in SurfaceVertex vert) => vert.Position.Y < rect.Bottom;
	public static float BottomClip(in ScissorRectState rect, in Vector2 one, in Vector2 two) => (one.Y - rect.Bottom) / (one.Y - two.Y);

	public static bool ClipLine(in ScissorRectState rect, in Span<SurfaceVertex> inVerts, Span<SurfaceVertex> outVerts) {
		if (rect.Scissor && !rect.FullScreenScissor) {
			Span<SurfaceVertex> tempVerts = stackalloc SurfaceVertex[2];
			if (!ClipLineToPlane(in rect, TopInside, TopClip, inVerts, tempVerts))
				return false;
			if (!ClipLineToPlane(in rect, BottomInside, BottomClip, tempVerts, outVerts))
				return false;
			if (!ClipLineToPlane(in rect, LeftInside, LeftClip, outVerts, tempVerts))
				return false;
			if (!ClipLineToPlane(in rect, RightInside, RightClip, tempVerts, outVerts))
				return false;

			return true;
		}
		else {
			outVerts[0] = inVerts[0];
			outVerts[1] = inVerts[1];
			return true;
		}
	}

	public static bool ClipLineToPlane(in ScissorRectState state, InsideCheck inside, ClipResult clip, Span<SurfaceVertex> inVerts, Span<SurfaceVertex> outVerts) {
		bool startInside = inside(in state, inVerts[0]);
		bool endInside = inside(in state, inVerts[1]);

		// Cull
		if (!startInside && !endInside)
			return false;

		if (startInside && endInside) {
			outVerts[0] = inVerts[0];
			outVerts[1] = inVerts[1];
		}
		else {
			int inIndex = startInside ? 0 : 1;
			outVerts[inIndex] = inVerts[inIndex];
			Intersect(in state, inVerts[0], inVerts[1], out outVerts[1 - inIndex], inside, clip);
		}

		return true;
	}

	public static void Intersect(in ScissorRectState rect, in SurfaceVertex start, in SurfaceVertex end, out SurfaceVertex clipped, InsideCheck inside, ClipResult clip) {
		float t = clip(in rect, in start.Position, in end.Position);
		clipped = new() {
			Position = Vector2.Lerp(start.Position, end.Position, t),
			TexCoord = Vector2.Lerp(start.TexCoord, end.TexCoord, t),
		};
	}

	public class ScreenClipState
	{
		public int ClipCount;
		public int TempCount;
		public int CurrVert;
		public List<SurfaceVertex> TempVertices = [];
		public List<SurfaceVertex>[] ClipVertices = [[], []];
	}

	public static void ScreenClip(in ScissorRectState state, ScreenClipState clip, InsideCheck inside, ClipResult clipFunc) {
		if (clip.ClipCount < 3)
			return;

		int numOutVerts = 0;
		Span<SurfaceVertex> source = clip.ClipVertices[clip.CurrVert].AsSpan();
		Span<SurfaceVertex> dest = clip.ClipVertices[clip.CurrVert == 0 ? 1 : 0].AsSpan();

		int numVerts = clip.ClipCount;
		Span<SurfaceVertex> start = source[(numVerts - 1)..];
		bool startInside = inside(in state, start[0]);
		for (int i = 0; i < numVerts; ++i) {
			Span<SurfaceVertex> end = source[i..];
			bool endInside = inside(in state, end[0]);
			if (endInside) {
				if (!startInside) {
					Assert(clip.TempCount <= clip.TempVertices.Count);

					dest[numOutVerts] = clip.TempVertices[clip.TempCount++];

					Intersect(in state, in start[0], in end[0], out dest[numOutVerts], inside, clipFunc);
					++numOutVerts;
				}
				dest[numOutVerts++] = end[0];
			}
			else {
				if (startInside) {
					Assert(clip.TempCount <= clip.TempVertices.Count);
					dest[numOutVerts] = clip.TempVertices[clip.TempCount++];

					Intersect(in state, in start[0], in end[0], out dest[numOutVerts], inside, clipFunc);
					++numOutVerts;
				}
			}
			start = end;
			startInside = endInside;
		}

		clip.CurrVert = 1 - clip.CurrVert;
		clip.ClipCount = numOutVerts;
	}

	static ScreenClipState clipPolygon = new();
	public unsafe static int ClipPolygon(in ScissorRectState state, Span<SurfaceVertex> verts, int translateX, int translateY, out Span<SurfaceVertex> clippedVerts) {
		clipPolygon.TempVertices.EnsureCount(verts.Length * 4);
		clipPolygon.ClipVertices[0].EnsureCount(verts.Length * 4);
		clipPolygon.ClipVertices[1].EnsureCount(verts.Length * 4);

		Span<SurfaceVertex> tempVertices = clipPolygon.TempVertices.AsSpan();
		Span<SurfaceVertex> clipVertices0 = clipPolygon.ClipVertices[0].AsSpan();
		Span<SurfaceVertex> clipVertices1 = clipPolygon.ClipVertices[1].AsSpan();

		for (int i = 0; i < verts.Length; ++i) {
			tempVertices[i] = verts[i];
			tempVertices[i].Position.X += translateX;
			tempVertices[i].Position.Y += translateY;
			clipVertices0[i] = tempVertices[i];
		}

		if (!state.Scissor || state.FullScreenScissor) {
			clippedVerts = clipVertices0[..verts.Length];
			return verts.Length;
		}

		clipPolygon.ClipCount = verts.Length;
		clipPolygon.TempCount = verts.Length;
		clipPolygon.CurrVert = 0;

		ScreenClip(in state, clipPolygon, TopInside, TopClip);
		ScreenClip(in state, clipPolygon, LeftInside, LeftClip);
		ScreenClip(in state, clipPolygon, RightInside, RightClip);
		ScreenClip(in state, clipPolygon, BottomInside, BottomClip);

		if (clipPolygon.ClipCount < 3) {
			clippedVerts = null;
			return 0;
		}

		clippedVerts = clipPolygon.ClipVertices[clipPolygon.CurrVert].AsSpan();
		return clipPolygon.ClipCount;
	}

	public static bool ClipRect(in ScissorRectState scissorRect, in SurfaceVertex inUL, in SurfaceVertex inLR, out SurfaceVertex outUL, out SurfaceVertex outLR) {
		if (scissorRect.Scissor) {
			outUL = default;
			outLR = default;

			outUL.Position.X = scissorRect.Left > inUL.Position.X ? scissorRect.Left : inUL.Position.X;
			outLR.Position.X = scissorRect.Right <= inLR.Position.X ? scissorRect.Right : inLR.Position.X;
			outUL.Position.Y = scissorRect.Top > inUL.Position.Y ? scissorRect.Top : inUL.Position.Y;
			outLR.Position.Y = scissorRect.Bottom <= inLR.Position.Y ? scissorRect.Bottom : inLR.Position.Y;

			// check non intersecting
			if (outUL.Position.X > outLR.Position.X || outUL.Position.Y > outLR.Position.Y)
				return false;

			outUL.TexCoord = inUL.TexCoord;
			outLR.TexCoord = inLR.TexCoord;
		}
		else {
			outUL = inUL;
			outLR = inLR;
		}

		return true;
	}

}
