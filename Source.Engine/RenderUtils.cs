using Source.Common.Formats.Keyvalues;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;

namespace Source.Engine;

/// <summary>
/// Various render util functions.
/// </summary>
public class RenderUtils(IMaterialSystem materials)
{
	public void DrawScreenSpaceRectangle(IMaterial material, int destX, int destY, int width, int height,
										 float srcTextureX0, float srcTextureY0, float srcTextureX1, float srcTextureY1,
										 int srcTextureWidth, int srcTextureHeight, object? clientRenderable, int xDice, int yDice,
										 float depth
										) {
		using MatRenderContextPtr renderContext = new(materials);

		if (width <= 0 || height <= 0)
			return;

		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		renderContext.Bind(material, clientRenderable);

		int xSegments = Math.Max(xDice, 1);
		int ySegments = Math.Max(yDice, 1);

		using MeshBuilder meshBuilder = new();
		IMesh mesh = renderContext.GetDynamicMesh(true);
		meshBuilder.Begin(mesh, MaterialPrimitiveType.Quads, xSegments * ySegments);
		{
			renderContext.GetRenderTargetDimensions(out int screenWidth, out int screenHeight);
			float flLeftX = destX;
			float flRightX = destX + width;

			float flTopY = destY;
			float flBottomY = destY + height;

			float flSubrectWidth = srcTextureX1 - srcTextureX0;
			float flSubrectHeight = srcTextureY1 - srcTextureY0;

			float texelsPerPixelX = (width > 1) ? flSubrectWidth / (width - 1) : 0.0f;
			float texelsPerPixelY = (height > 1) ? flSubrectHeight / (height - 1) : 0.0f;

			float flLeftU = srcTextureX0 + 0.5f - (0.5f * texelsPerPixelX);
			float flRightU = srcTextureX1 + 0.5f + (0.5f * texelsPerPixelX);
			float flTopV = srcTextureY0 + 0.5f - (0.5f * texelsPerPixelY);
			float flBottomV = srcTextureY1 + 0.5f + (0.5f * texelsPerPixelY);

			float flOOTexWidth = 1.0f / srcTextureWidth;
			float flOOTexHeight = 1.0f / srcTextureHeight;
			flLeftU *= flOOTexWidth;
			flRightU *= flOOTexWidth;
			flTopV *= flOOTexHeight;
			flBottomV *= flOOTexHeight;

			int vx, vy, vw, vh;
			renderContext.GetViewport(out vx, out vy, out vw, out vh);

			// map from screen pixel coords to -1..1
			flRightX = MathLib.Lerp(-1, 1, 0, vw, flRightX);
			flLeftX = MathLib.Lerp(-1, 1, 0, vw, flLeftX);
			flTopY = MathLib.Lerp(1, -1, 0, vh, flTopY);
			flBottomY = MathLib.Lerp(1, -1, 0, vh, flBottomY);

			if ((xSegments > 1) || (ySegments > 1)) {
				// Screen height and width of a subrect
				float flWidth = (flRightX - flLeftX) / (float)xSegments;
				float flHeight = (flTopY - flBottomY) / (float)ySegments;

				// UV height and width of a subrect
				float flUWidth = (flRightU - flLeftU) / (float)xSegments;
				float flVHeight = (flBottomV - flTopV) / (float)ySegments;

				for (int x = 0; x < xSegments; x++) {
					for (int y = 0; y < ySegments; y++) {
						// Top left
						meshBuilder.Position3f(flLeftX + (float)x * flWidth, flTopY - (float)y * flHeight, depth);
						meshBuilder.Normal3f(0.0f, 0.0f, 1.0f);
						meshBuilder.TexCoord2f(0, flLeftU + (float)x * flUWidth, flTopV + (float)y * flVHeight);
						meshBuilder.TangentS3f(0.0f, 1.0f, 0.0f);
						meshBuilder.TangentT3f(1.0f, 0.0f, 0.0f);
						meshBuilder.AdvanceVertex();

						// Top right (x+1)
						meshBuilder.Position3f(flLeftX + (float)(x + 1) * flWidth, flTopY - (float)y * flHeight, depth);
						meshBuilder.Normal3f(0.0f, 0.0f, 1.0f);
						meshBuilder.TexCoord2f(0, flLeftU + (float)(x + 1) * flUWidth, flTopV + (float)y * flVHeight);
						meshBuilder.TangentS3f(0.0f, 1.0f, 0.0f);
						meshBuilder.TangentT3f(1.0f, 0.0f, 0.0f);
						meshBuilder.AdvanceVertex();

						// Bottom right (x+1), (y+1)
						meshBuilder.Position3f(flLeftX + (float)(x + 1) * flWidth, flTopY - (float)(y + 1) * flHeight, depth);
						meshBuilder.Normal3f(0.0f, 0.0f, 1.0f);
						meshBuilder.TexCoord2f(0, flLeftU + (float)(x + 1) * flUWidth, flTopV + (float)(y + 1) * flVHeight);
						meshBuilder.TangentS3f(0.0f, 1.0f, 0.0f);
						meshBuilder.TangentT3f(1.0f, 0.0f, 0.0f);
						meshBuilder.AdvanceVertex();

						// Bottom left (y+1)
						meshBuilder.Position3f(flLeftX + (float)x * flWidth, flTopY - (float)(y + 1) * flHeight, depth);
						meshBuilder.Normal3f(0.0f, 0.0f, 1.0f);
						meshBuilder.TexCoord2f(0, flLeftU + (float)x * flUWidth, flTopV + (float)(y + 1) * flVHeight);
						meshBuilder.TangentS3f(0.0f, 1.0f, 0.0f);
						meshBuilder.TangentT3f(1.0f, 0.0f, 0.0f);
						meshBuilder.AdvanceVertex();
					}
				}
			}
			else // just one quad
			{
				for (int corner = 0; corner < 4; corner++) {
					bool bLeft = (corner == 0) || (corner == 3);
					meshBuilder.Position3f((bLeft) ? flLeftX : flRightX, (corner & 2) != 0 ? flBottomY : flTopY, depth);
					meshBuilder.Normal3f(0.0f, 0.0f, 1.0f);
					meshBuilder.TexCoord2f(0, (bLeft) ? flLeftU : flRightU, (corner & 2) != 0 ? flBottomV : flTopV);
					meshBuilder.TangentS3f(0.0f, 1.0f, 0.0f);
					meshBuilder.TangentT3f(1.0f, 0.0f, 0.0f);
					meshBuilder.AdvanceVertex();
				}
			}

		}
		meshBuilder.End();
		mesh.Draw();

		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PopMatrix();

		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PopMatrix();
	}

	internal void RenderLine(in Vector3 start, in Vector3 end, in Color color, bool zBuffer) {
		InitializeStandardMaterials();

		using MatRenderContextPtr renderContext = new(materials);
		renderContext.Bind(zBuffer ? Wireframe : WireframeIgnoreZ);

		byte chRed = color.R;
		byte chGreen = color.G;
		byte chBlue = color.B;
		byte chAlpha = color.A;

		IMesh mesh = renderContext.GetDynamicMesh();
		MeshBuilder meshBuilder = new();
		meshBuilder.Begin(mesh, MaterialPrimitiveType.Lines, 1);

		meshBuilder.Position3fv(start);
		meshBuilder.Color4ub(chRed, chGreen, chBlue, chAlpha);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3fv(end);
		meshBuilder.Color4ub(chRed, chGreen, chBlue, chAlpha);
		meshBuilder.AdvanceVertex();

		meshBuilder.End();
		mesh.Draw();
	}

	internal void RenderTriangle(in Vector3 p1, in Vector3 p2, in Vector3 p3, in Color color, bool zBuffer) {
		IMaterial pMaterial = zBuffer ? VertexColor : VertexColorIgnoreZ;
		RenderTriangle(p1, p2, p3, color, pMaterial);
	}

	internal void RenderTriangle(in Vector3 p1, in Vector3 p2, in Vector3 p3, in Color color, IMaterial pMaterial) {
		InitializeStandardMaterials();

		using MatRenderContextPtr renderContext = new(materials);
		renderContext.Bind(pMaterial);

		byte chRed = color.R;
		byte chGreen = color.G;
		byte chBlue = color.B;
		byte chAlpha = color.A;

		MathLib.VectorSubtract(in p2, in p1, out Vector3 vecDelta1);
		MathLib.VectorSubtract(in p3, in p1, out Vector3 vecDelta2);
		MathLib.CrossProduct(in vecDelta1, in vecDelta2, out Vector3 vecNormal);
		MathLib.VectorNormalize(ref vecNormal);

		IMesh mesh = renderContext.GetDynamicMesh();
		MeshBuilder meshBuilder = new();
		meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, 1);

		meshBuilder.Position3fv(p1);
		meshBuilder.Color4ub(chRed, chGreen, chBlue, chAlpha);
		meshBuilder.Normal3fv(vecNormal);
		meshBuilder.TexCoord2f(0, 0.0f, 0.0f);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3fv(p2);
		meshBuilder.Color4ub(chRed, chGreen, chBlue, chAlpha);
		meshBuilder.Normal3fv(vecNormal);
		meshBuilder.TexCoord2f(0, 0.0f, 1.0f);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3fv(p3);
		meshBuilder.Color4ub(chRed, chGreen, chBlue, chAlpha);
		meshBuilder.Normal3fv(vecNormal);
		meshBuilder.TexCoord2f(0, 1.0f, 0.0f);
		meshBuilder.AdvanceVertex();

		meshBuilder.End();
		mesh.Draw();
	}

	internal void RenderBox(in Vector3 origin, in Vector3 angles, in Vector3 mins, in Vector3 maxs, in Color color, bool zBuffer, bool insideOut = false) {
		IMaterial pMaterial = zBuffer ? VertexColor : VertexColorIgnoreZ;
		RenderBox(origin, angles, mins, maxs, color, pMaterial, insideOut);
	}

	static readonly int[][] s_pBoxFaceIndices = [
		[0, 4, 6, 2], // -x
		[5, 1, 3, 7], // +x
		[0, 1, 5, 4], // -y
		[2, 6, 7, 3], // +y
		[0, 2, 3, 1], // -z
		[4, 5, 7, 6]  // +z
	];

	static readonly int[][] s_pBoxFaceIndicesInsideOut = [
		[0, 2, 6, 4 ], // -x
		[5, 7, 3, 1 ], // +x
		[0, 4, 5, 1 ], // -y
		[2, 3, 7, 6 ], // +y
		[0, 1, 3, 2 ],	// -z
		[4, 6, 7, 5 ]  // +z
	];


	bool MaterialsInitialized;
	IMaterial Wireframe = null!;
	IMaterial WireframeIgnoreZ = null!;
	IMaterial VertexColor = null!;
	IMaterial VertexColorIgnoreZ = null!;

	private void RenderBox(Vector3 origin, Vector3 angles, Vector3 mins, Vector3 maxs, Color color, IMaterial pMaterial, bool insideOut) {
		InitializeStandardMaterials();

		using MatRenderContextPtr pRenderContext = new(materials);
		pRenderContext.Bind(pMaterial);

		Span<Vector3> p = stackalloc Vector3[8];
		GenerateBoxVertices(origin, angles, mins, maxs, p);

		byte chRed = color.R;
		byte chGreen = color.G;
		byte chBlue = color.B;
		byte chAlpha = color.A;

		IMesh pMesh = pRenderContext.GetDynamicMesh();
		MeshBuilder meshBuilder = new();
		meshBuilder.Begin(pMesh, MaterialPrimitiveType.Triangles, 12);

		// Draw the box
		Vector3 vecNormal = default;
		for (int i = 0; i < 6; i++) {
			vecNormal.Init();
			vecNormal[i / 2] = (i & 0x1) != 0 ? 1.0f : -1.0f;

			Span<int> ppFaceIndices = insideOut ? s_pBoxFaceIndicesInsideOut[i] : s_pBoxFaceIndices[i];
			for (int j = 1; j < 3; ++j) {
				int i0 = ppFaceIndices[0];
				int i1 = ppFaceIndices[j];
				int i2 = ppFaceIndices[j + 1];

				meshBuilder.Position3fv(p[i0]);
				meshBuilder.Color4ub(chRed, chGreen, chBlue, chAlpha);
				meshBuilder.Normal3fv(vecNormal);
				meshBuilder.TexCoord2f(0, 0.0f, 0.0f);
				meshBuilder.AdvanceVertex();

				meshBuilder.Position3fv(p[i2]);
				meshBuilder.Color4ub(chRed, chGreen, chBlue, chAlpha);
				meshBuilder.Normal3fv(vecNormal);
				meshBuilder.TexCoord2f(0, 1.0f, (j == 1) ? 1.0f : 0.0f);
				meshBuilder.AdvanceVertex();

				meshBuilder.Position3fv(p[i1]);
				meshBuilder.Color4ub(chRed, chGreen, chBlue, chAlpha);
				meshBuilder.Normal3fv(vecNormal);
				meshBuilder.TexCoord2f(0, (j == 1) ? 0.0f : 1.0f, 1.0f);
				meshBuilder.AdvanceVertex();
			}
		}

		meshBuilder.End();
		pMesh.Draw();
	}

	private void GenerateBoxVertices(Vector3 origin, Vector3 angles, Vector3 mins, Vector3 maxs, Span<Vector3> verts) {
		Matrix3x4 fRotateMatrix = default;
		MathLib.AngleMatrix(angles, out fRotateMatrix);

		Vector3 vecPos = default;
		for (int i = 0; i < 8; ++i) {
			vecPos[0] = (i & 0x1) != 0 ? maxs[0] : mins[0];
			vecPos[1] = (i & 0x2) != 0 ? maxs[1] : mins[1];
			vecPos[2] = (i & 0x4) != 0 ? maxs[2] : mins[2];

			MathLib.VectorRotate(in vecPos, in fRotateMatrix, out verts[i]);
			verts[i] += origin;
		}
	}

	private void InitializeStandardMaterials() {
		if (MaterialsInitialized)
			return;

		MaterialsInitialized = true;

		KeyValues pVMTKeyValues = new KeyValues("wireframe");
		pVMTKeyValues.SetInt("$vertexcolor", 1);
		Wireframe = materials.CreateMaterial("__utilWireframe", pVMTKeyValues);

		pVMTKeyValues = new KeyValues("wireframe");
		pVMTKeyValues.SetInt("$vertexcolor", 1);
		pVMTKeyValues.SetInt("$vertexalpha", 1);
		pVMTKeyValues.SetInt("$ignorez", 1);
		WireframeIgnoreZ = materials.CreateMaterial("__utilWireframeIgnoreZ", pVMTKeyValues);

		pVMTKeyValues = new KeyValues("unlitgeneric");
		pVMTKeyValues.SetInt("$vertexcolor", 1);
		pVMTKeyValues.SetInt("$vertexalpha", 1);
		VertexColor = materials.CreateMaterial("__utilVertexColor", pVMTKeyValues);

		pVMTKeyValues = new KeyValues("unlitgeneric");
		pVMTKeyValues.SetInt("$vertexcolor", 1);
		pVMTKeyValues.SetInt("$vertexalpha", 1);
		pVMTKeyValues.SetInt("$ignorez", 1);
		VertexColorIgnoreZ = materials.CreateMaterial("__utilVertexColorIgnoreZ", pVMTKeyValues);
	}

	internal void RenderWireframeBox(in Vector3 origin, in Vector3 angles, in Vector3 mins, in Vector3 maxs, in Color color, bool zBuffer) {
		InitializeStandardMaterials();

		using MatRenderContextPtr pRenderContext = new(materials);
		pRenderContext.Bind(zBuffer ? Wireframe : WireframeIgnoreZ);

		Span<Vector3> p = stackalloc Vector3[8];
		GenerateBoxVertices(origin, angles, mins, maxs, p);

		byte chRed = color.R;
		byte chGreen = color.G;
		byte chBlue = color.B;
		byte chAlpha = color.A;

		IMesh pMesh = pRenderContext.GetDynamicMesh();
		MeshBuilder meshBuilder = new();
		meshBuilder.Begin(pMesh, MaterialPrimitiveType.Lines, 24);

		// Draw the box
		for (int i = 0; i < 6; i++) {
			Span<int> pFaceIndex = s_pBoxFaceIndices[i];

			for (int j = 0; j < 4; ++j) {
				meshBuilder.Position3fv(p[pFaceIndex[j]]);
				meshBuilder.Color4ub(chRed, chGreen, chBlue, chAlpha);
				meshBuilder.AdvanceVertex();

				meshBuilder.Position3fv(p[pFaceIndex[(j == 3) ? 0 : j + 1]]);
				meshBuilder.Color4ub(chRed, chGreen, chBlue, chAlpha);
				meshBuilder.AdvanceVertex();
			}
		}

		meshBuilder.End();
		pMesh.Draw();
	}

	internal void RenderSphere(in Vector3 center, float radius, int theta, int phi, in Color c, IMaterial material) {
		InitializeStandardMaterials();

		using MatRenderContextPtr renderContext = new(materials);

		byte chRed = c.R;
		byte chGreen = c.G;
		byte chBlue = c.B;
		byte chAlpha = c.A;

		int triangles = 2 * theta * (phi - 1);
		int indices = 2 * (theta + 1) * (phi - 1);
		if (triangles == 0)
			return;

		renderContext.Bind(material);
		IMesh mesh = renderContext.GetDynamicMesh();
		MeshBuilder meshBuilder = new();

		meshBuilder.Begin(mesh, MaterialPrimitiveType.TriangleStrip, triangles, indices);

		float OONPhi = 1.0f / (phi - 1);
		float OONTheta = 1.0f / (theta - 1);

		int i, j;
		for (i = 0; i < phi; ++i) {
			for (j = 0; j < theta; ++j) {
				float u = j / (float)(theta - 1);
				float v = i / (float)(phi - 1);
				float t = 2.0f * MathF.PI * u;
				float p = MathF.PI * v;

				(float sinTheta, float cosTheta) = MathF.SinCos(t);
				(float sinPhi, float cosPhi) = MathF.SinCos(p);

				Vector3 pos;
				pos.X = radius * sinPhi * cosTheta;
				pos.Y = radius * sinPhi * sinTheta;
				pos.Z = radius * cosPhi;

				Vector3 normal = pos;
				MathLib.VectorNormalize(ref normal);

				pos += center;

				meshBuilder.Position3f(pos.X, pos.Y, pos.Z);
				meshBuilder.Normal3f(normal.X, normal.Y, normal.Z);
				meshBuilder.Color4ub(chRed, chGreen, chBlue, chAlpha);
				meshBuilder.TexCoord2f(0, j * OONTheta, i * OONPhi);
				meshBuilder.AdvanceVertex();
			}
		}

		int idx = 0;
		for (i = 0; i < phi - 1; ++i) {
			for (j = 0; j < theta; ++j) {
				idx = theta * i + j;

				meshBuilder.Index((ushort)(idx + theta));
				meshBuilder.AdvanceIndex();

				meshBuilder.Index((ushort)idx);
				meshBuilder.AdvanceIndex();
			}

			if (i < phi - 2) {
				meshBuilder.Index((ushort)idx);
				meshBuilder.AdvanceIndex();

				meshBuilder.Index((ushort)(idx + theta + 1));
				meshBuilder.AdvanceIndex();
			}
		}

		meshBuilder.End();
		mesh.Draw();
	}

	internal void RenderSphere(in Vector3 center, float radius, int theta, int phi, in Color c, bool zBuffer) {
		IMaterial material = zBuffer ? VertexColor : VertexColorIgnoreZ;
		Color actual = new(c.R, c.G, c.B, c.A);
		RenderSphere(center, radius, theta, phi, actual, material);
	}

	static void DrawAxes(in Vector3 origin, Span<Vector3> pts, int idx, in Color c, ref MeshBuilder meshBuilder) {
		MathLib.VectorAdd(pts[idx], origin, out Vector3 start);
		meshBuilder.Position3fv(start);
		meshBuilder.Color4ub(c.R, c.G, c.B, c.A);
		meshBuilder.AdvanceVertex();

		int endidx = (idx & 0x1) != 0 ? idx - 1 : idx + 1;
		MathLib.VectorAdd(pts[endidx], origin, out Vector3 temp);
		meshBuilder.Position3fv(temp);
		meshBuilder.Color4ub(c.R, c.G, c.B, c.A);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3fv(start);
		meshBuilder.Color4ub(c.R, c.G, c.B, c.A);
		meshBuilder.AdvanceVertex();

		endidx = (idx & 0x2) != 0 ? idx - 2 : idx + 2;
		MathLib.VectorAdd(pts[endidx], origin, out temp);
		meshBuilder.Position3fv(temp);
		meshBuilder.Color4ub(c.R, c.G, c.B, c.A);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3fv(start);
		meshBuilder.Color4ub(c.R, c.G, c.B, c.A);
		meshBuilder.AdvanceVertex();

		endidx = (idx & 0x4) != 0 ? idx - 4 : idx + 4;
		MathLib.VectorAdd(pts[endidx], origin, out temp);
		meshBuilder.Position3fv(temp);
		meshBuilder.Color4ub(c.R, c.G, c.B, c.A);
		meshBuilder.AdvanceVertex();
	}

	static void DrawExtrusionFace(in Vector3 start, in Vector3 end, Span<Vector3> pts, int idx1, int idx2, in Color c, ref MeshBuilder meshBuilder) {
		MathLib.VectorAdd(pts[idx1], start, out Vector3 temp);
		meshBuilder.Position3fv(temp);
		meshBuilder.Color4ub(c.R, c.G, c.B, c.A);
		meshBuilder.AdvanceVertex();

		MathLib.VectorAdd(pts[idx2], start, out temp);
		meshBuilder.Position3fv(temp);
		meshBuilder.Color4ub(c.R, c.G, c.B, c.A);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3fv(temp);
		meshBuilder.Color4ub(c.R, c.G, c.B, c.A);
		meshBuilder.AdvanceVertex();

		MathLib.VectorAdd(pts[idx2], end, out temp);
		meshBuilder.Position3fv(temp);
		meshBuilder.Color4ub(c.R, c.G, c.B, c.A);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3fv(temp);
		meshBuilder.Color4ub(c.R, c.G, c.B, c.A);
		meshBuilder.AdvanceVertex();

		MathLib.VectorAdd(pts[idx1], end, out temp);
		meshBuilder.Position3fv(temp);
		meshBuilder.Color4ub(c.R, c.G, c.B, c.A);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3fv(temp);
		meshBuilder.Color4ub(c.R, c.G, c.B, c.A);
		meshBuilder.AdvanceVertex();

		MathLib.VectorAdd(pts[idx1], start, out temp);
		meshBuilder.Position3fv(temp);
		meshBuilder.Color4ub(c.R, c.G, c.B, c.A);
		meshBuilder.AdvanceVertex();
	}

	internal void RenderWireframeSweptBox(in Vector3 start, in Vector3 end, in QAngle angles, in Vector3 mins, in Vector3 maxs, in Color c, bool zBuffer) {
		InitializeStandardMaterials();

		using MatRenderContextPtr renderContext = new(materials);
		renderContext.Bind(zBuffer ? Wireframe : WireframeIgnoreZ);

		Color actual = new(c.R, c.G, c.B, c.A);

		MathLib.AngleMatrix(angles, out Matrix3x4 rotateMatrix);

		IMesh mesh = renderContext.GetDynamicMesh();
		MeshBuilder meshBuilder = new();
		meshBuilder.Begin(mesh, MaterialPrimitiveType.Lines, 30);

		MathLib.VectorSubtract(end, start, out Vector3 delta);

		Vector3 temp;
		Span<Vector3> pts = stackalloc Vector3[8];
		Span<float> dot = stackalloc float[8];
		int minidx = 0;
		for (int i = 0; i < 8; ++i) {
			temp.X = (i & 0x1) != 0 ? maxs[0] : mins[0];
			temp.Y = (i & 0x2) != 0 ? maxs[1] : mins[1];
			temp.Z = (i & 0x4) != 0 ? maxs[2] : mins[2];

			MathLib.VectorRotate(temp, rotateMatrix, out pts[i]);

			dot[i] = MathLib.DotProduct(pts[i], delta);
			if (dot[i] < dot[minidx])
				minidx = i;
		}

		int maxidx = minidx ^ 0x7;

		DrawAxes(start, pts, minidx, actual, ref meshBuilder);
		DrawAxes(end, pts, maxidx, actual, ref meshBuilder);

		for (int j = 0; j < 3; ++j) {
			int dirflag1 = 1 << ((j + 1) % 3);
			int dirflag2 = 1 << ((j + 2) % 3);

			int idx1, idx2, idx3;
			idx1 = (minidx & dirflag1) != 0 ? minidx - dirflag1 : minidx + dirflag1;
			idx2 = (minidx & dirflag2) != 0 ? minidx - dirflag2 : minidx + dirflag2;
			idx3 = (minidx & dirflag2) != 0 ? idx1 - dirflag2 : idx1 + dirflag2;

			DrawExtrusionFace(start, end, pts, idx1, idx3, actual, ref meshBuilder);
			DrawExtrusionFace(start, end, pts, idx2, idx3, actual, ref meshBuilder);
		}
		meshBuilder.End();
		mesh.Draw();
	}
}
