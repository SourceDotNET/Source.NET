#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
#if CLIENT_DLL
global using PhysBeam = Game.Client.C_PhysBeam;
#else
global using PhysBeam = Game.Server.PhysBeam;
#endif

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;
#if GAME_DLL
using Game.Server.GarrysMod;
using Source.Common.GarrysMod.Lua;
using System.Buffers;
#endif
#if CLIENT_DLL
using Game.Client.GarrysMod;
using Source.Common.Input;
using Source.Common.GarrysMod.Lua;
#endif

using System.Numerics;
using System.Runtime.CompilerServices;

#if CLIENT_DLL
namespace Game.Client;

using FIELD = Source.FIELD<C_WeaponPhysGun>;
#else
namespace Game.Server;
using FIELD = Source.FIELD<WeaponPhysGun>;
#endif

using Table =
#if CLIENT_DLL
	RecvTable;
#else
	SendTable;
#endif

using Class =
#if CLIENT_DLL
	ClientClass;
#else
	ServerClass;
#endif



[LinkEntityToClass("weapon_physgun")]
[PrecacheWeaponRegister("weapon_physgun")]
[NetworkName("CWeaponPhysGun")]
public partial class
#if CLIENT_DLL
	C_WeaponPhysGun : C_BaseHL2MPCombatWeapon
#else
	WeaponPhysGun : BaseHL2MPCombatWeapon
#endif
{
	public static readonly Table DT_WeaponPhysGun = new(DT_BaseHL2MPCombatWeapon, [
#if CLIENT_DLL
		RecvPropEHandle(FIELD.OF(nameof(PhysBeam))),
		RecvPropVector(FIELD.OF(nameof(HitPosLocal))),
		RecvPropEHandle(FIELD.OF(nameof(GrabbedEntity)))
#elif GAME_DLL
		SendPropEHandle(FIELD.OF(nameof(PhysBeam))),
		SendPropVector(FIELD.OF(nameof(HitPosLocal)), 0, PropFlags.NoScale),
		SendPropEHandle(FIELD.OF(nameof(GrabbedEntity)))
#endif
	]);

	public static readonly new Class
#if CLIENT_DLL
		ClientClass
#else
		ServerClass
#endif
		= new Class(DT_WeaponPhysGun);

#if CLIENT_DLL
	public static readonly new DataMap PredMap = new([], typeof(C_WeaponPhysGun), BaseHL2MPCombatWeapon.PredMap); public override DataMap? GetPredDescMap() => PredMap;
#endif

	public static readonly ConVar physgun_minrange = new("physgun_minrange", "40", FCvar.Unregistered, "The minimum range of the Physics gun.", 32, 256);
	public static readonly ConVar physgun_maxrange = new("physgun_maxrange", "4096", FCvar.Replicated | FCvar.Notify, "The maximum range of the Physics gun.", 128, 32768);

	static readonly ActTable[] acttable = [
		new() { BaseAct = Activity.ACT_MP_STAND_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_PHYSGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_CROUCH_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_CROUCH_PHYSGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_RUN, WeaponAct = Activity.ACT_HL2MP_RUN_PHYSGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_WALK, WeaponAct = Activity.ACT_HL2MP_WALK_PHYSGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_CROUCHWALK, WeaponAct = Activity.ACT_HL2MP_WALK_CROUCH_PHYSGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_ATTACK_STAND_PRIMARYFIRE, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_PHYSGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_ATTACK_CROUCH_PRIMARYFIRE, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_PHYSGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_RELOAD_STAND, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_PHYSGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_RELOAD_CROUCH, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_PHYSGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_JUMP, WeaponAct = Activity.ACT_HL2MP_JUMP_PHYSGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_SWIM, WeaponAct = Activity.ACT_HL2MP_SWIM_PHYSGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_SWIM_IDLE, WeaponAct = Activity.ACT_HL2MP_SWIM_IDLE_PHYSGUN, Required = false },
	];

	public override ReadOnlySpan<ActTable> ActivityList() => acttable;

	[NetworkName("m_hPhysBeam")]
	public EHANDLE PhysBeam = new();
	[NetworkName("m_vHitPosLocal")]
	public Vector3 HitPosLocal;
	[NetworkName("m_hGrabbedEntity")]
	public EHANDLE GrabbedEntity = new();

	public override bool CanBePickedUpByNPCs() => false;
	public override float GetFireRate() => 0.01f;
	public override ReadOnlySpan<char> GetHoldType() => "physgun";
	public override bool HasAnyAmmo() => true;

	float Distance;
	float NextReload;

	public static Vector3 GetAimForward(BasePlayer player) {
		if (player.WorldClicking && !player.DisableWorldClicking)
			return player.WorldClickVector;

		if (player.IsInAVehicle()) {
			player.CacheVehicleView();
			MathLib.AngleVectors(player.VehicleViewAngles, out Vector3 vehicleForward);
			return vehicleForward;
		}

		MathLib.AngleVectors(player.EyeAngles(), out Vector3 forward);
		return forward;
	}

	public static Matrix3x4 GetPhysBoneMatrix(BaseEntity ent, int bone) {
		IPhysicsObject? phys = null;
		ent.VPhysicsGetObjectList(new Span<IPhysicsObject?>(ref phys));
		phys ??= ent.VPhysicsGetObject();

		Vector3 position;
		QAngle angles;
		if (phys != null)
			phys.GetPosition(out position, out angles);
		else {
			position = ent.GetAbsOrigin();
			angles = ent.GetAbsAngles();
		}

		MathLib.AngleMatrix(angles, position, out Matrix3x4 matrix);
		return matrix;
	}

#if GAME_DLL
	public static readonly ConVar phys_spinspeed = new("phys_spinspeed", "200", 0, "The sensitivity of Physics Gun rotation when using the movement keys. It's serverside.");

	const float ReloadInterval = 0.01f;

	float LastYaw;
	Vector3 Target;
	QAngle Angles;
	IPhysicsObject? PhysObj;
	readonly PhysGunControllerPoint GravCallback = new();

	public PhysGunControllerPoint GetGravCallback() => GravCallback;

	public WeaponPhysGun() {
		Skin = 1;
	}

	public override void Activate() {
		base.Activate();
		Skin = 1;
	}

	public override void Precache() {
		base.Precache();
	}

	public override void OnRestore() {
		base.OnRestore();
		DropEntity();
		CreatePhysBeam();
	}

	public override bool Deploy() {
		DropEntity();
		CreatePhysBeam();
		return base.Deploy();
	}

	public override bool Holster(BaseCombatWeapon? switchingTo) {
		DropEntity();
		RemovePhysBeam();
		return base.Holster(switchingTo);
	}

	public override void Drop(in Vector3 velocity) {
		DropEntity();
		RemovePhysBeam();
		base.Drop(velocity);
	}

	public override void UpdateOnRemove() {
		RemovePhysBeam();
		DropEntity();
		base.UpdateOnRemove();
	}

	public void CreatePhysBeam() {
		RemovePhysBeam();

		Game.Server.PhysBeam? beam = Game.Server.PhysBeam.Create(GetOwnerEntity(), GetAbsOrigin());
		NetworkStateChanged();
		PhysBeam.Set(beam);
	}

	public void RemovePhysBeam() {
		Util.Remove(PhysBeam.Get());
		NetworkStateChanged();
		PhysBeam.Set(null);
	}

	public bool ValidatePhysObj() {
		if (PhysObj != null && !PhysGunGlobals.IsPhysicsObjectValid(PhysObj))
			PhysObj = null;

		return PhysObj != null;
	}

	void SetHitPosLocal(in Vector3 value) {
		NetworkStateChanged();
		HitPosLocal = value;
	}

	void UpdateBeam(BaseEntity? target, BasePlayer owner) {
		if (PhysBeam.Get() is not Game.Server.PhysBeam beam)
			return;

		beam.UpdateBeam(target, HitPosLocal, 0);
		beam.SetAbsOrigin(owner.GetAbsOrigin());
	}

	void UpdateBeam(BaseEntity? target, BasePlayer owner, int bone) {
		if (PhysBeam.Get() is not Game.Server.PhysBeam beam)
			return;

		beam.UpdateBeam(target, HitPosLocal, bone);
		beam.SetAbsOrigin(owner.GetAbsOrigin());
	}

	void EnablePhysBeam(bool on) {
		if (PhysBeam.Get() is Game.Server.PhysBeam beam)
			beam.SetBeamEnabled(on);
	}

	public override bool Reload() {
		BasePlayer? owner = GetOwner() as BasePlayer;
		if (owner == null)
			return false;

		if (NextReload > gpGlobals.CurTime)
			return false;

		NextReload = (float)gpGlobals.CurTime + ReloadInterval;

		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.OnPhysgunReload)) {
			LuaEntity.Push_Entity(this);
			LuaEntity.Push_Entity(owner);
			gGM.CallNoReturns(2);
		}

		return true;
	}

	public override void PrimaryAttack() {
		if (NextPrimaryAttack > gpGlobals.CurTime)
			return;

		GMOD_Player? player = ToGMODPlayer(GetOwner());
		if (player == null)
			return;

		if (GrabbedEntity.Get() != null)
			return;

		Vector3 eye = player.Weapon_ShootPosition();
		Vector3 forward = PhysGunGlobals.GetAimForward(player);

		Ray ray = default;
		ray.Init(eye, eye + forward * physgun_maxrange.GetFloat() * 2);
		TraceFilterSimple filter = new(player, Source.CollisionGroup.None);
		enginetrace.TraceRay(ray, (Mask)0x600400f, ref filter, out Trace tr);

		if (r_visualizetraces.GetBool())
			Game.Shared.DebugOverlay.DebugDrawLine(tr.StartPos, tr.EndPos, 255, 0, 0, true, -1.0f);

		if (tr.Fraction == 1 && !tr.AllSolid && !tr.StartSolid)
			return;

		BaseEntity? hit = tr.Ent;
		if (!tr.DidHitWorld() && hit != null && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PhysgunPickup)) {
			LuaEntity.Push_Entity(player);
			LuaEntity.Push_Entity(hit);
			if (gGM.CallFinish(2)) {
				PhysObj = null;
				NetworkStateChanged();
				GrabbedEntity.Set(hit);
			}
		}

		BaseEntity? grabbed = GrabbedEntity.Get();
		if (grabbed != null) {
			if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.OnPhysgunPickup)) {
				LuaEntity.Push_Entity(player);
				LuaEntity.Push_Entity(grabbed);
				gGM.CallNoReturns(2);
			}

			LastYaw = player.EyeAngles().Y;
			Angles = grabbed.GetAbsAngles();

			if (grabbed is PhysicsProp prop)
				prop.ThrownByPlayer = false;

			Distance = (eye - tr.EndPos).Length();

			QAngle absAngles = grabbed.GetAbsAngles();
			Vector3 hitLocal;
			if (absAngles.X == 0 && absAngles.Y == 0 && absAngles.Z == 0)
				hitLocal = tr.EndPos - grabbed.GetAbsOrigin();
			else
				MathLib.VectorITransform(tr.EndPos, grabbed.EntityToWorldTransform(), out hitLocal);

			if (grabbed.GetSolid() == SolidType.VPhysics) {
				Matrix3x4 boneMatrix = GetPhysBoneMatrix(grabbed, tr.PhysicsBone);
				MathLib.VectorITransform(tr.EndPos, boneMatrix, out hitLocal);
			}
			SetHitPosLocal(hitLocal);

			IPhysicsObject[] list = ArrayPool<IPhysicsObject>.Shared.Rent(VPHYSICS_MAX_OBJECT_LIST_COUNT);
			try {
				int count = grabbed.VPhysicsGetObjectList(list.AsSpan(0, VPHYSICS_MAX_OBJECT_LIST_COUNT));
				PhysObj = (tr.PhysicsBone >= 0 && tr.PhysicsBone < count ? list[tr.PhysicsBone] : null) ?? grabbed.VPhysicsGetObject();

				if (PhysObj != null) {
					PhysObj.Wake();
					PhysObj.GetPosition(out _, out Angles);
					GravCallback.AttachEntity(player, grabbed, PhysObj, tr.EndPos);

					if (!grabbed.IsRagdollProp()) {
						for (int i = 0; i < count; i++)
							list[i]?.EnableMotion(true);
					}
				}
			}
			finally {
				ArrayPool<IPhysicsObject>.Shared.Return(list, true);
			}
		}

		EnablePhysBeam(true);
		UpdateBeam(GrabbedEntity.Get(), player, tr.PhysicsBone);

		if ((player.AfButtonPressed & InButtons.Attack) != 0)
			SendWeaponAnim(Activity.ACT_VM_PRIMARYATTACK);
	}

	public override void SecondaryAttack() {
		if (NextSecondaryAttack > gpGlobals.CurTime)
			return;

		GMOD_Player? player = ToGMODPlayer(GetOwner());
		if (player == null)
			return;

		BaseEntity? grabbed = GrabbedEntity.Get();
		if (grabbed == null)
			return;

		NextSecondaryAttack = gpGlobals.CurTime + 0.5;
		NextPrimaryAttack = NextSecondaryAttack;

		ValidatePhysObj();

		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.OnPhysgunFreeze)) {
			LuaEntity.Push_Entity(this);
			LuaPhysObj.Push_PhysObj(PhysObj);
			LuaEntity.Push_Entity(grabbed);
			LuaEntity.Push_Entity(player);
			gGM.CallNoReturns(4);
		}

		SendWeaponAnim(Activity.ACT_VM_PRIMARYATTACK);
		DropEntity();
	}

	public void DropEntity() {
		GMOD_Player? player = ToGMODPlayer(GetOwner());
		BaseEntity? grabbed = GrabbedEntity.Get();

		if (grabbed != null && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PhysgunDrop)) {
			LuaEntity.Push_Entity(player);
			LuaEntity.Push_Entity(grabbed);
			gGM.CallNoReturns(2);
		}

		EnablePhysBeam(false);
		PhysObj = null;
		GravCallback.DetachEntity(true);

		if (player != null && grabbed != null)
			grabbed.SetPhysicsAttacker(player, gpGlobals.CurTime);

		NetworkStateChanged();
		GrabbedEntity.Set(null);
		SetHitPosLocal(default);
	}

	public override void ItemPostFrame() {
		GMOD_Player? player = ToGMODPlayer(GetOwner());
		if (player == null)
			return;

		if (!player.CanAttack())
			return;

		if ((player.AfButtonPressed & InButtons.Reload) != 0) {
			Reload();
			return;
		}

		if ((player.AfButtonPressed & InButtons.Attack2) != 0) {
			SecondaryAttack();
			return;
		}

		if ((player.Buttons & (InButtons.Attack | InButtons.Attack2)) != InButtons.Attack) {
			DropEntity();
			return;
		}

		PrimaryAttack();

		if (GrabbedEntity.Get() == null)
			return;

		float frameTime = (float)gpGlobals.FrameTime;

		if ((player.Buttons & InButtons.Use) != 0) {
			player.SetPhysicsFlag(PlayerPhysFlag.DirOverride, true);

			if ((player.Buttons & InButtons.Forward) != 0)
				Distance = MathLib.Approach(physgun_maxrange.GetFloat(), Distance, 100 * frameTime);
			if ((player.Buttons & InButtons.Back) != 0)
				Distance = MathLib.Approach(physgun_minrange.GetFloat(), Distance, 100 * frameTime);
			if ((player.Buttons & InButtons.MoveLeft) != 0)
				Angles.Y -= phys_spinspeed.GetFloat() * frameTime;
			if ((player.Buttons & InButtons.MoveRight) != 0)
				Angles.Y += phys_spinspeed.GetFloat() * frameTime;

			ref UserCmd cmd = ref player.GetCurrentUserCommand();
			if (!Unsafe.IsNullRef(ref cmd)) {
				float sensitivity = engine.GetClientConVarFloat(player.EntIndex(), "physgun_rotation_sensitivity", 0.05f);
				sensitivity = Math.Clamp(sensitivity, -1000f, 1000f);

				QAngle delta = new(-cmd.MouseDeltaY * sensitivity, cmd.MouseDeltaX * sensitivity, 0);

				QAngle eyeYaw = new(0, player.EyeAngles().Y, 0);
				MathLib.AngleMatrix(eyeYaw, out Matrix3x4 playerSpace);

				QAngle local = MathLib.TransformAnglesToLocalSpace(Angles, playerSpace);
				MathLib.AngleMatrix(local, out Matrix3x4 current);
				MathLib.AngleMatrix(delta, out Matrix3x4 deltaMatrix);
				MathLib.ConcatTransforms(deltaMatrix, current, out Matrix3x4 combined);
				MathLib.MatrixAngles(combined, out QAngle combinedAngles);

				Angles = MathLib.TransformAnglesToWorldSpace(combinedAngles, playerSpace);
			}
		}
		else
			player.SetPhysicsFlag(PlayerPhysFlag.DirOverride, false);

		int wheel = player.MouseWheel();
		if (wheel != 0) {
			float wheelSpeed = engine.GetClientConVarFloat(player.EntIndex(), "physgun_wheelspeed", 10);
			if (MathF.Abs((int)wheelSpeed) < 0.1f)
				wheelSpeed = 0.1f;

			Distance += wheel * wheelSpeed;
			Distance = Math.Clamp(Distance, physgun_minrange.GetFloat(), physgun_maxrange.GetFloat());
		}

		Target = player.Weapon_ShootPosition() + PhysGunGlobals.GetAimForward(player) * Distance;

		UpdateRotation();
		UpdatePosition();
	}

	static float SnapAngle(float angle, float snap) {
		float quotient = angle / snap;
		int rounded = float.IsFinite(quotient) && MathF.Abs(quotient) < 2147483648f ? (int)MathF.Round(quotient) : int.MinValue;
		return rounded * snap;
	}

	public void UpdateRotation() {
		GMOD_Player? player = ToGMODPlayer(GetOwner());
		BaseEntity? grabbed = GrabbedEntity.Get();
		if (player == null || grabbed == null)
			return;

		float snap = player.GetSnapAngles();
		QAngle snapped = new(SnapAngle(Angles.X, snap), SnapAngle(Angles.Y, snap), SnapAngle(Angles.Z, snap));

		float eyeYaw = player.EyeAngles().Y;
		if (eyeYaw != LastYaw) {
			Angles.Y += eyeYaw - LastYaw;
			LastYaw = eyeYaw;
		}

		QAngle target = Angles;
		const InButtons SpeedUse = InButtons.Speed | InButtons.Use;

		if ((player.Buttons & SpeedUse) == SpeedUse)
			target = snapped;

		InButtons released = player.AfButtonReleased;
		InButtons held = player.Buttons;
		if ((released & SpeedUse) == SpeedUse
			|| ((released & InButtons.Speed) != 0 && (held & InButtons.Use) != 0)
			|| ((released & InButtons.Use) != 0 && (held & InButtons.Speed) != 0)) {
			Angles = target = snapped;
		}

		ValidatePhysObj();
		if (PhysObj != null)
			GravCallback.SetTargetRotation(target);
		else {
			if (grabbed.IsNPC()) {
				target.X = 0;
				target.Z = 0;
			}
			grabbed.SetAbsAngles(target);
		}
	}

	public void UpdatePosition() {
		GMOD_Player? player = ToGMODPlayer(GetOwner());
		BaseEntity? grabbed = GrabbedEntity.Get();
		if (player == null || grabbed == null)
			return;

		if (ValidatePhysObj()) {
			GravCallback.SetTargetPosition(Target, player);
			return;
		}

		Vector3 origin = grabbed.GetAbsOrigin();
		QAngle absAngles = grabbed.GetAbsAngles();
		Vector3 holdWorld;
		if (absAngles.X == 0 && absAngles.Y == 0 && absAngles.Z == 0)
			holdWorld = HitPosLocal + origin;
		else
			MathLib.VectorTransform(HitPosLocal, grabbed.EntityToWorldTransform(), out holdWorld);

		Target -= holdWorld - origin;
		player?.SnapToGrid(ref Target);

		const Mask solidMask = (Mask)0x400b;

		Util.TraceEntity(grabbed, origin, origin, solidMask, out Trace tr);
		if (tr.Fraction < 1 || tr.AllSolid || tr.StartSolid) {
			Util.TraceEntity(grabbed, Target, Target, solidMask, out tr);
			if (!(tr.Fraction < 1 || tr.AllSolid || tr.StartSolid))
				grabbed.SetAbsOrigin(Target);
		}

		Util.TraceEntity(grabbed, origin, Target, solidMask, out tr);
		if (tr.Fraction == 1) {
			grabbed.SetAbsOrigin(tr.EndPos);
			return;
		}

		Util.TraceEntity(grabbed, origin, new Vector3(origin.X, Target.Y, Target.Z), solidMask, out tr);
		Util.TraceEntity(grabbed, origin, new Vector3(Target.X, origin.Y, Target.Z), solidMask, out tr);
		Util.TraceEntity(grabbed, origin, new Vector3(Target.X, Target.Y, origin.Z), solidMask, out tr);
		grabbed.SetAbsOrigin(tr.EndPos);
	}
#endif
#if CLIENT_DLL
	static void ResetIfNonFinite(ConVar cvar) {
		if (!float.IsFinite(cvar.GetFloat()))
			cvar.SetValue(cvar.GetDefault());
	}

	static void PhysgunSensitivityChanged(IConVar var, in ConVarChangeContext ctx) {
		ResetIfNonFinite(physgun_rotation_sensitivity);
		ResetIfNonFinite(physgun_wheelspeed);
	}

	public static readonly ConVar physgun_rotation_sensitivity = new("physgun_rotation_sensitivity", "0.05", FCvar.UserInfo | FCvar.Archive, "The sensitivity of Physics Gun rotation.", 0.01, 10000, PhysgunSensitivityChanged);
	public static readonly ConVar physgun_wheelspeed = new("physgun_wheelspeed", "10.0", FCvar.UserInfo | FCvar.Archive, "The sensitivity of Physics Gun wheel scrolling.", -10000, 10000, PhysgunSensitivityChanged);
	public static readonly ConVar physgun_drawbeams = new("physgun_drawbeams", "1", 0, "Whether to draw Physics Gun beams.");

	public override bool Deploy() {
		DropEntity();
		return base.Deploy();
	}

	public override bool Holster(BaseCombatWeapon? switchingTo) {
		DropEntity();
		return base.Holster(switchingTo);
	}

	public override void Drop(in Vector3 velocity) {
		DropEntity();
		base.Drop(velocity);
	}

	public override void UpdateOnRemove() {
		DropEntity();
		base.UpdateOnRemove();
	}

	void EnablePhysBeam(bool on) {
		if (PhysBeam.Get() is C_PhysBeam beam)
			beam.SetBeamEnabled(on);
	}

	public void DropEntity() {
		BasePlayer? player = GetOwner() as BasePlayer;
		BaseEntity? grabbed = GrabbedEntity.Get();
		if (player != null && grabbed != null) {
			if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PhysgunDrop)) {
				LuaEntity.Push_Entity(player);
				LuaEntity.Push_Entity(grabbed);
				gGM.CallNoReturns(2);
			}
		}

		EnablePhysBeam(false);

		GrabbedEntity.Set(null);
		HitPosLocal = default;
	}

	public override bool Reload() {
		if (GetOwner() == null)
			return false;

		if (NextReload > gpGlobals.CurTime)
			return false;

		NextReload = (float)gpGlobals.CurTime + 0.01f;
		return true;
	}

	public override void SecondaryAttack() {
		if (gpGlobals.CurTime < NextSecondaryAttack)
			return;

		if (GetOwner() is not BasePlayer || GrabbedEntity.Get() == null)
			return;

		NextSecondaryAttack = gpGlobals.CurTime + 0.5;
		NextPrimaryAttack = NextSecondaryAttack;
		SendWeaponAnim(Activity.ACT_VM_PRIMARYATTACK);
		DropEntity();
	}

	public override void PrimaryAttack() {
		if (gpGlobals.CurTime < NextPrimaryAttack)
			return;

		BasePlayer? player = GetOwner() as BasePlayer;
		if (player == null)
			return;

		if (GrabbedEntity.Get() != null)
			return;

		Vector3 eye = player.Weapon_ShootPosition();
		Vector3 forward = GetAimForward(player);

		Ray ray = default;
		ray.Init(eye, eye + forward * physgun_maxrange.GetFloat() * 2);
		TraceFilterSimple filter = new(player, Source.CollisionGroup.None);
		enginetrace.TraceRay(ray, (Mask)0x600400f, ref filter, out Trace tr);

		if (r_visualizetraces.GetBool())
			Game.Shared.DebugOverlay.DebugDrawLine(tr.StartPos, tr.EndPos, 255, 0, 0, true, -1.0f);

		if (tr.Fraction >= 1 && !tr.AllSolid && !tr.StartSolid)
			return;

		BaseEntity? hit = tr.Ent;
		if (!tr.DidHitWorld() && hit != null && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PhysgunPickup)) {
			LuaEntity.Push_Entity(player);
			LuaEntity.Push_Entity(hit);
			if (gGM.CallFinish(2))
				GrabbedEntity.Set(hit);
		}

		BaseEntity? grabbed = GrabbedEntity.Get();
		if (grabbed != null) {
			Distance = (eye - tr.EndPos).Length();

			QAngle absAngles = grabbed.GetAbsAngles();
			Vector3 hitLocal;
			if (absAngles.X == 0 && absAngles.Y == 0 && absAngles.Z == 0)
				hitLocal = tr.EndPos - grabbed.GetAbsOrigin();
			else
				MathLib.VectorITransform(tr.EndPos, grabbed.EntityToWorldTransform(), out hitLocal);

			if (grabbed.GetSolid() == SolidType.VPhysics) {
				Matrix3x4 boneMatrix = GetPhysBoneMatrix(grabbed, tr.PhysicsBone);
				MathLib.VectorITransform(tr.EndPos, boneMatrix, out hitLocal);
			}
			HitPosLocal = hitLocal;

			IPhysicsObject? phys = grabbed.VPhysicsGetObject();
			if (phys != null) {
				phys.Wake();
				if (!grabbed.IsRagdollProp())
					phys.EnableMotion(true);
			}
		}

		EnablePhysBeam(true);
		if (PhysBeam.Get() is C_PhysBeam beam) {
			beam.UpdateBeam(GrabbedEntity.Get(), HitPosLocal, tr.PhysicsBone);
			beam.SetAbsOrigin(player.GetAbsOrigin());
		}

		if ((player.AfButtonPressed & InButtons.Attack) != 0)
			SendWeaponAnim(Activity.ACT_VM_PRIMARYATTACK);
	}

	public override void ItemPostFrame() {
		BasePlayer? player = GetOwner() as BasePlayer;
		if (player == null)
			return;

		if (!player.CanAttack())
			return;

		if ((player.AfButtonPressed & InButtons.Reload) != 0) {
			Reload();
			return;
		}

		if ((player.AfButtonPressed & InButtons.Attack2) != 0) {
			SecondaryAttack();
			return;
		}

		if ((player.Buttons & (InButtons.Attack | InButtons.Attack2)) == InButtons.Attack)
			PrimaryAttack();
		else
			DropEntity();
	}

	public override bool ShouldFreezeViewAngles() {
		const InButtons AttackUse = InButtons.Attack | InButtons.Use;
		return GrabbedEntity.Get() != null && (gHUD.KeyBits & AttackUse) == AttackUse;
	}

	public override int KeyInput(int down, ButtonCode keynum, ReadOnlySpan<char> currentBinding) {
		if (GrabbedEntity.Get() == null || (gHUD.KeyBits & InButtons.Attack) == 0)
			return 1;

		if (keynum == ButtonCode.MouseWheelUp) {
			gHUD.KeyBits |= InButtons.Weapon1;
			return 0;
		}
		if (keynum == ButtonCode.MouseWheelDown) {
			gHUD.KeyBits |= InButtons.Weapon2;
			return 0;
		}

		return 1;
	}
#endif
}
#endif
