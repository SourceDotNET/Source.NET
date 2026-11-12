#if CLIENT_DLL || GAME_DLL
using Game.Shared;

using Source.Common;
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaPhysCollide
{
	[LuaClass(typeof(PhysCollide), NullError = "Tried to use a null PhysCollide!")]
	public static readonly LuaClass LC_PhysCollide = new("PhysCollide", LuaType.PhysCollide, null, null);

	public static void Push_PhysCollide(PhysCollide? collide) => LC_PhysCollide.Push(collide);

	[LuaMethod]
	static int PhysCollide__Destroy(ILuaInterface lua) {
		PhysCollide? collide = (PhysCollide?)LC_PhysCollide.Get(1);
		if (collide != null) {
			lua.SetUserType(1, 0);
			physcollision.DestroyCollide(collide);
		}
		return 0;
	}

	[LuaMethod]
	static int PhysCollide__IsValid(ILuaInterface lua) {
		PhysCollide? collide = (PhysCollide?)LC_PhysCollide.Get(1);
		lua.PushBool(collide != null);
		return 1;
	}

	[LuaMethod]
	static int PhysCollide____tostring(ILuaInterface lua) {
		PhysCollide? collide = (PhysCollide?)LC_PhysCollide.Get(1);
		if (collide == null) {
			lua.PushString("PhysCollide [NULL]");
			return 1;
		}
		lua.PushString("PhysCollide");
		return 1;
	}

	[LuaMethod]
	static int PhysCollide__TraceBox(ILuaInterface lua) {
		PhysCollide? collide = (PhysCollide?)LC_PhysCollide.Get(1);
		if (collide == null)
			lua.Error("Tried to use a null PhysCollide!");

		Vector3 origin = LuaVector.Get_Vector(2);
		QAngle angles = LuaAngle.Get_Angle(3);
		Vector3 start = LuaVector.Get_Vector(4);
		Vector3 end = LuaVector.Get_Vector(5);
		LuaVector.OrderVectors(LuaVector.Get_Vector(6), LuaVector.Get_Vector(7), out Vector3 mins, out Vector3 maxs);

		if (!origin.IsValid() || !angles.IsValid() || !start.IsValid() || !end.IsValid() || !mins.IsValid() || !maxs.IsValid()) {
			lua.PushBool(false);
			return 1;
		}

		Ray ray = default;
		ray.Init(start, end, mins, maxs);
		physcollision.TraceBox(in ray, collide!, in origin, in angles, out Trace tr);

		if (tr.Fraction < 1.0f || tr.StartSolid || tr.AllSolid) {
			LuaVector.Push_Vector(tr.EndPos);
			LuaVector.Push_Vector(tr.Plane.Normal);
			lua.PushNumber(tr.Fraction);
			return 3;
		}

		lua.PushBool(false);
		return 1;
	}

	[LuaGlobal]
	static int CreatePhysCollideBox(ILuaInterface lua) {
		LuaVector.OrderVectors(LuaVector.Get_Vector(1), LuaVector.Get_Vector(2), out Vector3 mins, out Vector3 maxs);

		PhysCollide? collide = null;
		if (mins.IsValid() && maxs.IsValid() && maxs.X - mins.X >= 0.0001f && maxs.Y - mins.Y >= 0.0001f && maxs.Z - mins.Z >= 0.0001f) {
			Span<Vector3> verts = [
				new(mins.X, mins.Y, mins.Z),
				new(maxs.X, mins.Y, mins.Z),
				new(mins.X, maxs.Y, mins.Z),
				new(maxs.X, maxs.Y, mins.Z),
				new(mins.X, mins.Y, maxs.Z),
				new(maxs.X, mins.Y, maxs.Z),
				new(mins.X, maxs.Y, maxs.Z),
				new(maxs.X, maxs.Y, maxs.Z),
			];
			Span<PhysConvex> convex = [physcollision.ConvexFromVerts(verts)];
			collide = physcollision.ConvertConvexToCollide(convex);
		}

		Push_PhysCollide(collide);
		return 1;
	}

	[LuaGlobal]
	static int CreatePhysCollidesFromModel(ILuaInterface lua) {
		string name = lua.CheckString(1);
		if (name.Length <= 1 || name[0] == ' ')
			return 0;

		int modelIndex = modelinfo.GetModelIndex(name);
		if (modelIndex < 0)
			return 0;

		VCollide? vcollide = modelinfo.GetVCollide(modelIndex);
		if (vcollide == null)
			return 0;

		PhysCollide?[] collides = new PhysCollide?[vcollide.SolidCount];
		for (int i = 0; i < vcollide.SolidCount; i++) {
			PhysCollide? solid = vcollide.Solids![i];
			if (solid == null)
				return 0;

			ICollisionQuery? query = physcollision.CreateQueryModel(solid);
			if (query == null || query.ConvexCount() < 1)
				return 0;

			int convexCount = query.ConvexCount();
			List<PhysConvex> convexes = new(convexCount);
			for (int j = 0; j < convexCount; j++) {
				int triCount = query.TriangleCount(j);
				if (triCount <= 2)
					continue;

				Vector3[] verts = new Vector3[triCount * 3];
				for (int k = 0; k < triCount; k++)
					query.GetTriangleVerts(j, k, verts.AsSpan(k * 3, 3));

				convexes.Add(physcollision.ConvexFromVerts(verts));
			}

			physcollision.DestroyQueryModel(query);
			collides[i] = physcollision.ConvertConvexToCollide(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(convexes));
		}

		lua.PreCreateTable(vcollide.SolidCount, 0);
		for (int i = 0; i < vcollide.SolidCount; i++) {
			lua.PushNumber(i + 1);
			Push_PhysCollide(collides[i]);
			lua.SetTable(-3);
		}
		return 1;
	}
}
#endif
