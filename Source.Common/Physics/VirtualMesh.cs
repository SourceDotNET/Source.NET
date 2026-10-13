using System.Numerics;
using System.Runtime.CompilerServices;

namespace Source.Common.Physics;

public static class VirtualMeshConstants
{
	public const int MAX_VIRTUAL_TRIANGLES = 1024;
}

[InlineArray(VirtualMeshConstants.MAX_VIRTUAL_TRIANGLES * 3)]
public struct InlineArrayMaxVirtualTriangles<T> { T first; }

public struct VirtualMeshList
{
	public List<Vector3>? Verts;
	public int IndexCount;
	public int TriangleCount;
	public int VertexCount;
	public int SurfacePropsIndex;
	// public byte hull; // TODO: This was a pointer before. Evaluate?
	public InlineArrayMaxVirtualTriangles<ushort> Indices;
}

public struct VirtualMeshTriangleList
{
	public int TriangleCount;
	public InlineArrayMaxVirtualTriangles<ushort> TriangleIndices;
}

public interface IVirtualMeshEvent
{
	void GetVirtualMesh(object userData, ref VirtualMeshList list);
	void GetWorldspaceBounds(object userData, out Vector3 mins, out Vector3 maxs);
	void GetTrianglesInSphere(object userData, in Vector3 center, float radius, ref VirtualMeshTriangleList list);
}

public struct VirtualMeshParams
{
	public IVirtualMeshEvent? MeshEventHandler;
	public object? UserData;
	public bool BuildOuterHull;
}
