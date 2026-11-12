using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Source.Common;

public struct BeamSeg
{
	public Vector3 Pos;
	public Vector3 Color;
	public float TexCoord;
	public float Width;
	public float Alpha;
}

public ref struct BeamSegDraw
{
	ref MeshBuilder pMeshBuilder;
	int MeshVertCount;
	MeshBuilder Mesh;
	BeamSeg Seg;
	int TotalSegs;
	int SegsDrawn;
	Vector3 NormalLast;
	IMatRenderContext RenderContext;

	public void Start(IMatRenderContext renderContext, int segs, IMaterial? material) {
		RenderContext = renderContext;
		SegsDrawn = 0;
		TotalSegs = segs;
		MeshVertCount = 0;
		pMeshBuilder = ref Unsafe.NullRef<MeshBuilder>();

		IMesh mesh = renderContext.GetDynamicMesh(true, null, null, material);
		Mesh.Begin(mesh, MaterialPrimitiveType.TriangleStrip, (segs - 1) * 2);
	}

	public BeamSegDraw() { }

	public BeamSegDraw(ref MeshBuilder meshBuilder, int meshVertCount) {
		pMeshBuilder = ref meshBuilder;
		MeshVertCount = meshVertCount;
	}

	public void StartWithMeshBuilder(IMatRenderContext renderContext, int segs) {
		RenderContext = renderContext;
		SegsDrawn = 0;
		TotalSegs = segs;
	}

	public void NextSeg(ref BeamSeg seg) {
		RenderContext.GetWorldSpaceCameraPosition(out Vector3 vecCameraPos);

		if (SegsDrawn > 0) {
			// Get a vector that is perpendicular to us and perpendicular to the beam.
			// This is used to fatten the beam.
			Vector3 normal, aveNormal;
			ComputeNormal(in vecCameraPos, in Seg.Pos, in seg.Pos, out normal);

			if (SegsDrawn > 1) {
				// Average this with the previous normal
				MathLib.VectorAdd(normal, NormalLast, out aveNormal);
				aveNormal *= 0.5f;
				MathLib.VectorNormalize(ref aveNormal);
			}
			else
				aveNormal = normal;

			NormalLast = normal;
			SpecifySeg(vecCameraPos, aveNormal);
		}

		Seg = seg;
		++SegsDrawn;
		if (SegsDrawn == TotalSegs)
			SpecifySeg(vecCameraPos, NormalLast);
	}

	private void ComputeNormal(in Vector3 vecCameraPos, in Vector3 startPos, in Vector3 nextPos, out Vector3 normal) {
		Vector3 vTangentY;
		MathLib.VectorSubtract(startPos, nextPos, out vTangentY);

		Vector3 vDirToBeam;
		MathLib.VectorSubtract(startPos, vecCameraPos, out vDirToBeam);

		MathLib.CrossProduct(vTangentY, vDirToBeam, out normal);
		MathLib.VectorNormalize(ref normal);
	}

	public void SpecifySeg(in Vector3 vecCameraPos, in Vector3 vNormal) {
		Vector3 vDirToBeam, vTangentY;
		MathLib.VectorSubtract(Seg.Pos, vecCameraPos, out vDirToBeam);
		MathLib.CrossProduct(vDirToBeam, vNormal, out vTangentY);
		MathLib.VectorNormalize(ref vTangentY);

		// Build the endpoints.
		Vector3 vPoint1 = default, vPoint2 = default;
		MathLib.VectorMA(Seg.Pos, Seg.Width * 0.5f, vNormal, out vPoint1);
		MathLib.VectorMA(Seg.Pos, -Seg.Width * 0.5f, vNormal, out vPoint2);


		if (Unsafe.IsNullRef(ref pMeshBuilder)) {
			EmitSegVerts(ref Mesh, in Seg, in vPoint1, in vPoint2, in vNormal, in vTangentY);
			return;
		}

		EmitSegVerts(ref pMeshBuilder, in Seg, in vPoint1, in vPoint2, in vNormal, in vTangentY);
		if (SegsDrawn > 1) {
			int nBase = ((SegsDrawn - 2) * 2) + MeshVertCount;
			pMeshBuilder.FastIndex((ushort)nBase);
			pMeshBuilder.FastIndex((ushort)(nBase + 1));
			pMeshBuilder.FastIndex((ushort)(nBase + 2));
			pMeshBuilder.FastIndex((ushort)(nBase + 1));
			pMeshBuilder.FastIndex((ushort)(nBase + 3));
			pMeshBuilder.FastIndex((ushort)(nBase + 2));
		}
	}

	static void EmitSegVerts(ref MeshBuilder mesh, in BeamSeg seg, in Vector3 vPoint1, in Vector3 vPoint2, in Vector3 vNormal, in Vector3 vTangentY) {
		mesh.Position3fv(vPoint1);
		mesh.Color4f(seg.Color, seg.Alpha);
		mesh.TexCoord2f(0, 0, seg.TexCoord);
		mesh.TexCoord2f(1, 0, seg.TexCoord);
		mesh.TangentS3fv(vNormal);
		mesh.TangentT3fv(vTangentY);
		mesh.AdvanceVertex();

		mesh.Position3fv(vPoint2);
		mesh.Color4f(seg.Color, seg.Alpha);
		mesh.TexCoord2f(0, 1, seg.TexCoord);
		mesh.TexCoord2f(1, 1, seg.TexCoord);
		mesh.TangentS3fv(vNormal);
		mesh.TangentT3fv(vTangentY);
		mesh.AdvanceVertex();
	}

	public void End() {
		if (!Unsafe.IsNullRef(ref pMeshBuilder)) {
			pMeshBuilder = ref Unsafe.NullRef<MeshBuilder>();
			return;
		}
		Mesh.End(false, true);
	}
}
