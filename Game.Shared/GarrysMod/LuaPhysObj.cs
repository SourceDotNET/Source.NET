#if CLIENT_DLL || GAME_DLL
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Buffers;
using System.Numerics;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaPhysObj
{
	const int MAX_OBJECT_LIST = 0x400;

	[LuaClass(typeof(IPhysicsObject), NullError = "Tried to use a NULL physics object!")]
	public static readonly LuaClass LC_PhysObj = new("PhysObj", LuaType.PhysObj, null, null);

	public static void Push_PhysObj(IPhysicsObject? obj) => LC_PhysObj.Push(obj);

	public static bool IsPhysicsObjectValid(IPhysicsObject? obj) {
		if (obj == null)
			return false;

		ReadOnlySpan<IPhysicsObject> objects = physenv.GetObjectList();
		for (int i = 0; i < objects.Length; i++)
			if (objects[i] == obj)
				return true;

		return false;
	}

	public static IPhysicsObject? Get_IPhysicsObject(int stackPos, bool allowNull) {
		IPhysicsObject? obj = LC_PhysObj.Get(stackPos) as IPhysicsObject;
		if (obj != null) {
			if (IsPhysicsObjectValid(obj))
				return obj;
			obj = null;
		}

		if (!allowNull)
			g_Lua!.Error("Tried to use a NULL physics object!");

		return obj;
	}

	static bool IsFinite(in Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

	static BaseEntity? GetGameEntity(IPhysicsObject obj) => obj.GetGameData() as BaseEntity;

	[LuaMethod]
	static int PhysObj__GetName(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		lua.PushString(obj.GetName());
		return 1;
	}

	[LuaMethod]
	static int PhysObj____eq(ILuaInterface lua) {
		IPhysicsObject? a = Get_IPhysicsObject(1, false);
		IPhysicsObject? b = Get_IPhysicsObject(2, false);
		lua.PushBool(a == b);
		return 1;
	}

	[LuaMethod]
	static int PhysObj____tostring(ILuaInterface lua) {
		IPhysicsObject? obj = LC_PhysObj.Get(1) as IPhysicsObject;
		if (obj == null || !IsPhysicsObjectValid(obj)) {
			lua.PushString("[NULL PhysObject]");
			return 1;
		}

		BaseEntity? ent = GetGameEntity(obj);
		int index = ent != null ? ent.EntIndex() : -1;
		string str = $"PhysObject [{obj.GetName()}][Entity {index}]";
		lua.PushString(str.Length > 0xFF ? str[..0xFF] : str);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__IsValid(ILuaInterface lua) {
		IPhysicsObject? obj = Get_IPhysicsObject(1, true);
		lua.PushBool(obj != null && IsPhysicsObjectValid(obj));
		return 1;
	}

	[LuaMethod]
	static int PhysObj__Wake(ILuaInterface lua) {
		Get_IPhysicsObject(1, false)!.Wake();
		return 0;
	}

	[LuaMethod]
	static int PhysObj__Sleep(ILuaInterface lua) {
		Get_IPhysicsObject(1, false)!.Sleep();
		return 0;
	}

	[LuaMethod]
	static int PhysObj__IsAsleep(ILuaInterface lua) {
		lua.PushBool(Get_IPhysicsObject(1, false)!.IsAsleep());
		return 1;
	}

	[LuaMethod]
	static int PhysObj__GetEntity(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		BaseEntity? ent = GetGameEntity(obj);
		if (ent == null)
			return 0;

		LuaEntity.Push_Entity(ent);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__SetMass(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		float mass = (float)lua.GetNumber(2);
		if (0.1 >= mass)
			mass = 1.0f;
		else if (mass > 50000.0f)
			mass = 50000.0f;

		obj.SetMass(mass);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__GetMass(ILuaInterface lua) {
		lua.PushNumber(Get_IPhysicsObject(1, false)!.GetMass());
		return 1;
	}

	[LuaMethod]
	static int PhysObj__GetInvMass(ILuaInterface lua) {
		lua.PushNumber(Get_IPhysicsObject(1, false)!.GetInvMass());
		return 1;
	}

	[LuaMethod]
	static int PhysObj__GetEnergy(ILuaInterface lua) {
		lua.PushNumber(Get_IPhysicsObject(1, false)!.GetEnergy());
		return 1;
	}

	[LuaMethod]
	static int PhysObj__GetInertia(ILuaInterface lua) {
		LuaVector.Push_Vector(Get_IPhysicsObject(1, false)!.GetInertia());
		return 1;
	}

	[LuaMethod]
	static int PhysObj__GetInvInertia(ILuaInterface lua) {
		LuaVector.Push_Vector(Get_IPhysicsObject(1, false)!.GetInvInertia());
		return 1;
	}

	[LuaMethod]
	static int PhysObj__SetInertia(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 inertia = LuaVector.Get_Vector(2);
		if (!IsFinite(inertia))
			return 0;

		if (inertia.X == 0)
			inertia.X = 1;
		if (inertia.Y == 0)
			inertia.Y = 1;
		if (inertia.Z == 0)
			inertia.Z = 1;

		if (obj.IsMotionEnabled()) {
			obj.SetInertia(inertia);
			return 0;
		}

		obj.EnableMotion(true);
		obj.SetInertia(inertia);
		obj.EnableMotion(false);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__SetDamping(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		float speed = (float)lua.GetNumber(2);
		float rot = (float)lua.GetNumber(3);
		obj.SetDamping(ref speed, ref rot);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__GetDamping(ILuaInterface lua) {
		Get_IPhysicsObject(1, false)!.GetDamping(out float speed, out float rot);
		lua.PushNumber(speed);
		lua.PushNumber(rot);
		return 2;
	}

	[LuaMethod]
	static int PhysObj__GetSpeedDamping(ILuaInterface lua) {
		Get_IPhysicsObject(1, false)!.GetDamping(out float speed, out _);
		lua.PushNumber(speed);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__GetRotDamping(ILuaInterface lua) {
		Get_IPhysicsObject(1, false)!.GetDamping(out _, out float rot);
		lua.PushNumber(rot);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__EnableCollisions(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		obj.EnableCollisions(lua.GetBool(2));
		return 0;
	}

	[LuaMethod]
	static int PhysObj__EnableGravity(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		obj.EnableGravity(lua.GetBool(2));
		return 0;
	}

	[LuaMethod]
	static int PhysObj__EnableDrag(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		obj.EnableDrag(lua.GetBool(2));
		return 0;
	}

	[LuaMethod]
	static int PhysObj__EnableMotion(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		bool enable = lua.GetBool(2);

		BaseEntity? ent = GetGameEntity(obj);
		if (ent != null && ent.IsRagdollProp()) {
			IPhysicsObject[] list = ArrayPool<IPhysicsObject>.Shared.Rent(MAX_OBJECT_LIST);
			try {
				int count = ent.VPhysicsGetObjectList(list.AsSpan(0, MAX_OBJECT_LIST));
				for (int i = 0; i < count; i++)
					list[i].EnableMotion(enable);
			}
			finally {
				ArrayPool<IPhysicsObject>.Shared.Return(list, true);
			}
		}

		obj.EnableMotion(enable);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__IsMotionEnabled(ILuaInterface lua) {
		lua.PushBool(Get_IPhysicsObject(1, false)!.IsMotionEnabled());
		return 1;
	}

	[LuaMethod]
	static int PhysObj__IsCollisionEnabled(ILuaInterface lua) {
		lua.PushBool(Get_IPhysicsObject(1, false)!.IsCollisionEnabled());
		return 1;
	}

	[LuaMethod]
	static int PhysObj__IsGravityEnabled(ILuaInterface lua) {
		lua.PushBool(Get_IPhysicsObject(1, false)!.IsGravityEnabled());
		return 1;
	}

	[LuaMethod]
	static int PhysObj__IsDragEnabled(ILuaInterface lua) {
		lua.PushBool(Get_IPhysicsObject(1, false)!.IsDragEnabled());
		return 1;
	}

	[LuaMethod]
	static int PhysObj__IsMoveable(ILuaInterface lua) {
		lua.PushBool(Get_IPhysicsObject(1, false)!.IsMoveable());
		return 1;
	}

	[LuaMethod]
	static int PhysObj__SetMaterial(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		int index = (int)physprops.GetSurfaceIndex(lua.GetString(2) ?? "");
		if (index > 0)
			obj.SetMaterialIndex(index);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__GetMaterial(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		ReadOnlySpan<char> name = physprops.GetPropName(obj.GetMaterialIndex());
		lua.PushString(name.IsEmpty ? "default" : name);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__LocalToWorld(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 local = LuaVector.Get_Vector(2);
		obj.LocalToWorld(out Vector3 world, local);
		LuaVector.Push_Vector(world);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__WorldToLocal(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 world = LuaVector.Get_Vector(2);
		obj.WorldToLocal(out Vector3 local, world);
		LuaVector.Push_Vector(local);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__LocalToWorldVector(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 local = LuaVector.Get_Vector(2);
		obj.LocalToWorldVector(out Vector3 world, local);
		LuaVector.Push_Vector(world);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__WorldToLocalVector(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 world = LuaVector.Get_Vector(2);
		obj.WorldToLocalVector(out Vector3 local, world);
		LuaVector.Push_Vector(local);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__ApplyTorqueCenter(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 torque = LuaVector.Get_Vector(2);
		if (IsFinite(torque))
			obj.ApplyTorqueCenter(torque);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__ApplyForceCenter(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 force = LuaVector.Get_Vector(2);
		if (IsFinite(force))
			obj.ApplyForceCenter(force);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__ApplyForceOffset(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 force = LuaVector.Get_Vector(2);
		if (IsFinite(force)) {
			Vector3 position = LuaVector.Get_Vector(3);
			obj.ApplyForceOffset(force, position);
		}
		return 0;
	}

	[LuaMethod]
	static int PhysObj__CalculateForceOffset(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 force = LuaVector.Get_Vector(2);
		Vector3 position = LuaVector.Get_Vector(3);
		obj.CalculateForceOffset(force, position, out Vector3 centerForce, out Vector3 centerTorque);
		LuaVector.Push_Vector(centerForce);
		LuaVector.Push_Vector(centerTorque);
		return 2;
	}

	[LuaMethod]
	static int PhysObj__CalculateVelocityOffset(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 force = LuaVector.Get_Vector(2);
		Vector3 position = LuaVector.Get_Vector(3);
		obj.CalculateVelocityOffset(force, position, out Vector3 centerVelocity, out Vector3 centerAngularVelocity);
		LuaVector.Push_Vector(centerVelocity);
		LuaVector.Push_Vector(centerAngularVelocity);
		return 2;
	}

	[LuaMethod]
	static int PhysObj__SetPos(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		BaseEntity? ent = GetGameEntity(obj);
		if (ent != null && ent.IsRagdollProp())
			return 0;

		Vector3 pos = LuaVector.Get_Vector(2);
		bool teleport = lua.GetType(3) != LuaType.Nil && lua.GetBool(3);

		float max = MAX_COORD_FLOAT * 8;
		if (!(-max < pos.X && pos.X < max && -max < pos.Y && pos.Y < max && -max < pos.Z && pos.Z < max)) {
			Warning($"PhysObj:SetPos( {pos.X:F6} {pos.Y:F6} {pos.Z:F6} ): Ignoring unreasonable position.\n");
			return 0;
		}

		obj.GetPosition(out _, out QAngle angles);
		obj.SetPosition(pos, angles, teleport);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__SetAngles(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		BaseEntity? ent = GetGameEntity(obj);
		if (ent != null && ent.IsRagdollProp())
			return 0;

		QAngle angles = LuaAngle.Get_Angle(2);
		float max = 360000f * 360000f;
		if (!(-max < angles.X && angles.X < max && -max < angles.Y && angles.Y < max && -max < angles.Z && angles.Z < max)) {
			Warning($"PhysObj:SetAngles( {angles.X:F6} {angles.Y:F6} {angles.Z:F6} ): Ignoring unreasonable angles.\n");
			return 0;
		}

		obj.GetPosition(out Vector3 pos, out _);
		obj.SetPosition(pos, angles, true);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__GetPos(ILuaInterface lua) {
		Get_IPhysicsObject(1, false)!.GetPosition(out Vector3 pos, out _);
		LuaVector.Push_Vector(pos);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__GetAngles(ILuaInterface lua) {
		Get_IPhysicsObject(1, false)!.GetPosition(out _, out QAngle angles);
		LuaAngle.Push_Angle(angles);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__SetVelocity(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 velocity = LuaVector.Get_Vector(2);
		if (!IsFinite(velocity))
			return 0;

		obj.GetVelocity(out _, out Vector3 angular);
		obj.SetVelocity(velocity, angular);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__SetVelocityInstantaneous(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 velocity = LuaVector.Get_Vector(2);
		if (!IsFinite(velocity))
			return 0;

		obj.GetVelocity(out _, out Vector3 angular);
		obj.SetVelocityInstantaneous(velocity, angular);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__SetAngleVelocity(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 angular = LuaVector.Get_Vector(2);
		if (!IsFinite(angular))
			return 0;

		obj.GetVelocity(out Vector3 velocity, out _);
		obj.SetVelocity(velocity, angular);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__SetAngleVelocityInstantaneous(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 angular = LuaVector.Get_Vector(2);
		if (!IsFinite(angular))
			return 0;

		obj.GetVelocity(out Vector3 velocity, out _);
		obj.SetVelocityInstantaneous(velocity, angular);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__GetVelocity(ILuaInterface lua) {
		Get_IPhysicsObject(1, false)!.GetVelocity(out Vector3 velocity, out _);
		LuaVector.Push_Vector(velocity);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__GetVelocityAtPoint(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 point = LuaVector.Get_Vector(2);
		obj.GetVelocityAtPoint(point, out Vector3 velocity);
		LuaVector.Push_Vector(velocity);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__GetAngleVelocity(ILuaInterface lua) {
		Get_IPhysicsObject(1, false)!.GetVelocity(out _, out Vector3 angular);
		LuaVector.Push_Vector(angular);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__AddVelocity(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 velocity = LuaVector.Get_Vector(2);
		if (IsFinite(velocity))
			obj.AddVelocity(velocity, default);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__AddAngleVelocity(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 angular = LuaVector.Get_Vector(2);
		if (IsFinite(angular))
			obj.AddVelocity(default, angular);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__ComputeShadowControl(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		LuaObject table = new(2, (LuaType)(-1));
		if (!table.isTable()) {
			lua.TypeError("table", 2);
			table.UnReference();
			return 0;
		}

		HLShadowControlParams parms = default;
		float secondsToArrive = table.GetMemberFloat("secondstoarrive", (float)(gpGlobals.IntervalPerTick * 2));
		float delta = table.GetMemberFloat("delta", 0.1f);
		parms.TargetPosition = table.GetMemberVector("pos", default);
		parms.MaxAngular = table.GetMemberFloat("maxangular", 1.0f);
		parms.MaxDampAngular = table.GetMemberFloat("maxangulardamp", 1.0f);
		parms.MaxSpeed = table.GetMemberFloat("maxspeed", 1.0f);
		parms.MaxDampSpeed = table.GetMemberFloat("maxspeeddamp", 1.0f);
		parms.DampFactor = table.GetMemberFloat("dampfactor", 1.0f);
		parms.TeleportDistance = table.GetMemberFloat("teleportdistance", 0.0f);
		parms.TargetRotation = table.GetMemberAngle("angle", default);

		if (secondsToArrive != 0)
			obj.ComputeShadowControl(parms, secondsToArrive, delta);
		else
			lua.Error("ComputeShadowControl Error! secondstoarrive cannot be 0!\n");

		table.UnReference();
		return 0;
	}

	[LuaMethod]
	static int PhysObj__SetDragCoefficient(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		float drag = (float)lua.GetNumber(2);
		obj.SetDragCoefficient(drag, null);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__SetAngleDragCoefficient(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		float drag = (float)lua.GetNumber(2);
		obj.SetDragCoefficient(null, drag);
		return 0;
	}

	[LuaMethod]
	static int PhysObj__SetBuoyancyRatio(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		obj.SetBuoyancyRatio((float)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int PhysObj__GetBuoyancyRatio(ILuaInterface lua) {
		lua.PushNumber(Get_IPhysicsObject(1, false)!.GetBuoyancyRatio());
		return 1;
	}

	[LuaMethod]
	static int PhysObj__GetMassCenter(ILuaInterface lua) {
		LuaVector.Push_Vector(Get_IPhysicsObject(1, false)!.GetMassCenterLocalSpace());
		return 1;
	}

	[LuaMethod]
	static int PhysObj__GetContents(ILuaInterface lua) {
		lua.PushNumber((double)Get_IPhysicsObject(1, false)!.GetContents());
		return 1;
	}

	[LuaMethod]
	static int PhysObj__SetContents(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		obj.SetContents((uint)(long)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int PhysObj__OutputDebugInfo(ILuaInterface lua) {
		Get_IPhysicsObject(1, false)!.OutputDebugInfo();
		return 0;
	}

	[LuaMethod]
	static int PhysObj__AlignAngles(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		QAngle from = LuaAngle.Get_Angle(2);
		QAngle to = LuaAngle.Get_Angle(3);

		obj.GetPosition(out _, out QAngle physAngles);

		MathLib.AngleMatrix(from, out Matrix3x4 fromMatrix);
		MathLib.AngleMatrix(physAngles, out Matrix3x4 physMatrix);
		MathLib.MatrixInvert(physMatrix, out Matrix3x4 physInverse);

		Matrix4x4 a = physInverse;
		Matrix4x4 b = fromMatrix;
		MathLib.MatrixMultiply(a, b, out Matrix4x4 local);
		MathLib.MatrixToAngles(local, out QAngle localAngles);

		MathLib.AngleMatrix(to, out Matrix3x4 toMatrix);
		MathLib.AngleMatrix(localAngles, out Matrix3x4 localMatrix);
		MathLib.MatrixInvert(localMatrix, out Matrix3x4 localInverse);

		Matrix4x4 c = toMatrix;
		Matrix4x4 d = localInverse;
		MathLib.MatrixMultiply(c, d, out Matrix4x4 result);
		MathLib.MatrixToAngles(result, out QAngle resultAngles);

		LuaAngle.Push_Angle(resultAngles);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__RotateAroundAxis(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 axis = LuaVector.Get_Vector(2);
		float degrees = (float)lua.GetNumber(3);

		obj.GetPosition(out _, out QAngle physAngles);
		MathLib.AngleMatrix(physAngles, out Matrix3x4 physMatrix);
		Matrix4x4 phys = physMatrix;

		MathLib.MatrixBuildRotationAboutAxis(out Matrix4x4 rotation, axis, degrees);
		MathLib.MatrixMultiply(rotation, phys, out Matrix4x4 result);
		MathLib.MatrixToAngles(result, out QAngle resultAngles);

		LuaAngle.Push_Angle(resultAngles);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__UpdateShadow(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		Vector3 pos = LuaVector.Get_Vector(2);
		QAngle angles = LuaAngle.Get_Angle(3);

		float max = MAX_COORD_FLOAT * 8;
		if (!(-max < pos.X && pos.X < max && -max < pos.Y && pos.Y < max && -max < pos.Z && pos.Z < max)) {
			Warning($"PhysObj:UpdateShadow( {pos.X:F6} {pos.Y:F6} {pos.Z:F6} ): Ignoring unreasonable position.\n");
			return 0;
		}

		float maxAngle = 360000f * 360000f;
		if (!(-maxAngle < angles.X && angles.X < maxAngle && -maxAngle < angles.Y && angles.Y < maxAngle && -maxAngle < angles.Z && angles.Z < maxAngle)) {
			Warning($"PhysObj:UpdateShadow( {angles.X:F6} {angles.Y:F6} {angles.Z:F6} ): Ignoring unreasonable angles.\n");
			return 0;
		}

		obj.UpdateShadow(pos, angles, false, (float)lua.GetNumber(4));
		return 0;
	}

	[LuaMethod]
	static int PhysObj__GetShadowPos(ILuaInterface lua) {
		Get_IPhysicsObject(1, false)!.GetShadowPosition(out Vector3 pos, out _);
		LuaVector.Push_Vector(pos);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__GetShadowAngles(ILuaInterface lua) {
		Get_IPhysicsObject(1, false)!.GetShadowPosition(out _, out QAngle angles);
		LuaAngle.Push_Angle(angles);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__IsPenetrating(ILuaInterface lua) {
		PhysicsFlags flags = Get_IPhysicsObject(1, false)!.GetGameFlags();
		lua.PushBool((((ushort)flags >> 6) & 1) != 0);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__HasGameFlag(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		ushort flags = (ushort)obj.GetGameFlags();
		lua.PushBool((flags & (int)lua.GetNumber(2)) != 0);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__AddGameFlag(ILuaInterface lua) {
		IPhysicsObject? obj = Get_IPhysicsObject(1, false);
		ushort flag = (ushort)(int)lua.GetNumber(2);
		if (obj != null)
			obj.SetGameFlags((PhysicsFlags)(ushort)((ushort)obj.GetGameFlags() | flag));
		return 0;
	}

	[LuaMethod]
	static int PhysObj__ClearGameFlag(ILuaInterface lua) {
		IPhysicsObject? obj = Get_IPhysicsObject(1, false);
		ushort flag = (ushort)(int)lua.GetNumber(2);
		if (obj != null)
			obj.SetGameFlags((PhysicsFlags)(ushort)((ushort)obj.GetGameFlags() & ~flag));
		return 0;
	}

	[LuaMethod]
	static int PhysObj__GetAABB(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		PhysCollide? collide = obj.GetCollide();
		if (collide == null)
			return 0;

		physcollision.CollideGetAABB(out Vector3 mins, out Vector3 maxs, collide, vec3_origin, vec3_angle);
		LuaVector.Push_Vector(mins);
		LuaVector.Push_Vector(maxs);
		return 2;
	}

	[LuaMethod]
	static int PhysObj__GetVolume(ILuaInterface lua) {
		PhysCollide? collide = Get_IPhysicsObject(1, false)!.GetCollide();
		if (collide == null)
			return 0;

		lua.PushNumber(physcollision.CollideVolume(collide));
		return 1;
	}

	[LuaMethod]
	static int PhysObj__GetSurfaceArea(ILuaInterface lua) {
		PhysCollide? collide = Get_IPhysicsObject(1, false)!.GetCollide();
		if (collide == null)
			return 0;

		lua.PushNumber(physcollision.CollideSurfaceArea(collide));
		return 1;
	}

#if GAME_DLL
	[LuaMethod]
	static int PhysObj__GetStress(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		BaseEntity? ent = GetGameEntity(obj);
		CalculateObjectStress(obj, ent!, out VPhysicsObjectStress stress);
		lua.PushNumber(stress.ExertedStress);
		lua.PushNumber(stress.ReceivedStress);
		return 2;
	}
#endif

	[LuaMethod]
	static int PhysObj__GetMesh(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		PhysCollide? collide = obj.GetCollide();
		if (collide == null)
			return 0;

		int count = physcollision.CreateDebugMesh(collide, out Span<Vector3> verts);
		if (verts.IsEmpty)
			return 0;

		LuaTable table = new(null, (uint)count);
		for (int i = 0; i < count; i++) {
			LuaTable entry = new(null, 0);
			entry.SetMemberVector("pos", verts[i]);
			table.SetMember((float)(i + 1), entry);
			entry.UnReference();
		}

		physcollision.DestroyDebugMesh(count, verts);
		table.Push();
		table.UnReference();
		return 1;
	}

	[LuaMethod]
	static int PhysObj__GetMeshConvexes(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		PhysCollide? collide = obj.GetCollide();
		if (collide == null)
			return 0;

		ICollisionQuery? query = physcollision.CreateQueryModel(collide);
		if (query == null)
			return 0;

		int convexCount = query.ConvexCount();
		LuaTable table = new(null, (uint)convexCount);
		Span<Vector3> tri = stackalloc Vector3[3];
		for (int c = 0; c < convexCount; c++) {
			int triCount = query.TriangleCount(c);
			LuaTable convex = new(null, 0);
			for (int t = 0; t < triCount; t++) {
				tri.Clear();
				query.GetTriangleVerts(c, t, tri);
				for (int v = 0; v < 3; v++) {
					LuaTable vertex = new(null, 0);
					vertex.SetMemberVector("pos", tri[v]);
					convex.SetMember((float)(t * 3 + v + 1), vertex);
					vertex.UnReference();
				}
			}

			table.SetMember((float)(c + 1), convex);
			convex.UnReference();
		}

		physcollision.DestroyQueryModel(query);
		table.Push();
		table.UnReference();
		return 1;
	}

	[LuaMethod]
	static int PhysObj__RecheckCollisionFilter(ILuaInterface lua) {
		Get_IPhysicsObject(1, false)!.RecheckCollisionFilter();
		return 0;
	}

	[LuaMethod]
	static int PhysObj__GetPositionMatrix(ILuaInterface lua) {
		Get_IPhysicsObject(1, false)!.GetPositionMatrix(out Matrix3x4 matrix);
		Matrix4x4 vmatrix = matrix;
		LuaVMatrix.Push_VMatrix(vmatrix);
		return 1;
	}

	[LuaMethod]
	static int PhysObj__GetIndex(ILuaInterface lua) {
		lua.PushNumber((double)Get_IPhysicsObject(1, false)!.GetGameIndex());
		return 1;
	}

	[LuaMethod]
	static int PhysObj__GetFrictionSnapshot(ILuaInterface lua) {
		IPhysicsObject obj = Get_IPhysicsObject(1, false)!;
		LuaTable table = new(null, 0);
		IPhysicsFrictionSnapshot snapshot = obj.CreateFrictionSnapshot();

		int index = 0;
		while (snapshot.IsValid()) {
			LuaTable entry = new(null, 0);
			entry.SetMemberPhysObject("Other", snapshot.GetObject(1));
			entry.SetMember("EnergyAbsorbed", snapshot.GetEnergyAbsorbed());
			entry.SetMember("FrictionCoefficient", snapshot.GetFrictionCoefficient());
			entry.SetMember("NormalForce", snapshot.GetNormalForce());
			snapshot.GetSurfaceNormal(out Vector3 normal);
			entry.SetMemberVector("Normal", normal);
			snapshot.GetContactPoint(out Vector3 contact);
			entry.SetMemberVector("ContactPoint", contact);
			entry.SetMember("Material", (float)snapshot.GetMaterial(0));
			entry.SetMember("MaterialOther", (float)snapshot.GetMaterial(1));
			index++;
			table.SetMember((float)index, entry);
			snapshot.NextFrictionData();
			entry.UnReference();
		}

		obj.DestroyFrictionSnapshot(snapshot);
		table.Push();
		table.UnReference();
		return 1;
	}
}
#endif
