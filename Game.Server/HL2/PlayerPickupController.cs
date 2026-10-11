using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Buffers;
using System.Numerics;

namespace Game.Server.HL2;

public class GrabController : IMotionEvent
{
	public static readonly ConVar player_throwforce = new("player_throwforce", "1000");

	// when looking level, hold bottom of object 8 inches below eye level
	const float PLAYER_HOLD_LEVEL_EYES = -8;

	// when looking down, hold bottom of object 0 inches from feet
	const float PLAYER_HOLD_DOWN_FEET = 2;

	// when looking up, hold bottom of object 24 inches above eye level
	const float PLAYER_HOLD_UP_EYES = 24;

	// use a +/-30 degree range for the entire range of motion of pitch
	const float PLAYER_LOOK_PITCH_RANGE = 30;

	// player can reach down 2ft below his feet (otherwise he'll hold the object above the bottom)
	const float PLAYER_REACH_DOWN_DISTANCE = 24;

	const float DEFAULT_MAX_ANGULAR = 360.0f * 10.0f;
	const float REDUCED_CARRY_MASS = 1.0f;

	static void MatrixOrthogonalize(ref Matrix3x4 matrix, int column) {
		Span<Vector3> columns = stackalloc Vector3[3];
		int i;

		for (i = 0; i < 3; i++)
			MathLib.MatrixGetColumn(matrix, i, out columns[i]);

		int index0 = column;
		int index1 = (column + 1) % 3;
		int index2 = (column + 2) % 3;

		columns[index2] = Vector3.Cross(columns[index0], columns[index1]);
		columns[index1] = Vector3.Cross(columns[index2], columns[index0]);
		MathLib.VectorNormalize(ref columns[index2]);
		MathLib.VectorNormalize(ref columns[index1]);
		MathLib.MatrixSetColumn(columns[index1], index1, ref matrix);
		MathLib.MatrixSetColumn(columns[index2], index2, ref matrix);
	}

	static int SIGN(float x) => x < 0 ? -1 : 1;

	static QAngle AlignAngles(in QAngle angles, float cosineAlignAngle) {
		MathLib.AngleMatrix(angles, out Matrix3x4 alignMatrix);

		// NOTE: Must align z first
		for (int j = 3; --j >= 0;) {
			MathLib.MatrixGetColumn(alignMatrix, j, out Vector3 vec);
			for (int i = 0; i < 3; i++) {
				if (MathF.Abs(vec[i]) > cosineAlignAngle) {
					MathLib.SubFloat(ref vec, i) = SIGN(vec[i]);
					MathLib.SubFloat(ref vec, (i + 1) % 3) = 0;
					MathLib.SubFloat(ref vec, (i + 2) % 3) = 0;
					MathLib.MatrixSetColumn(vec, j, ref alignMatrix);
					MatrixOrthogonalize(ref alignMatrix, j);
					break;
				}
			}
		}

		MathLib.MatrixAngles(alignMatrix, out QAngle outAngles);
		return outAngles;
	}

	/// <summary>
	/// Computes a local matrix for the player clamped to valid carry ranges
	/// </summary>
	static void ComputePlayerMatrix(BasePlayer? player, out Matrix3x4 outMatrix) {
		outMatrix = default;
		if (player == null)
			return;

		QAngle angles = player.EyeAngles();
		Vector3 origin = player.EyePosition();

		// 0-360 / -180-180
		//angles.x = init ? 0 : AngleDistance( angles.x, 0 );
		//angles.x = clamp( angles.x, -PLAYER_LOOK_PITCH_RANGE, PLAYER_LOOK_PITCH_RANGE );
		angles.X = 0;

		float feet = player.GetAbsOrigin().Z + player.WorldAlignMins().Z;
		float eyes = origin.Z;
		float zoffset = 0;
		// moving up (negative pitch is up)
		if (angles.X < 0)
			zoffset = MathLib.RemapVal(angles.X, 0, -PLAYER_LOOK_PITCH_RANGE, PLAYER_HOLD_LEVEL_EYES, PLAYER_HOLD_UP_EYES);
		else
			zoffset = MathLib.RemapVal(angles.X, 0, PLAYER_LOOK_PITCH_RANGE, PLAYER_HOLD_LEVEL_EYES, PLAYER_HOLD_DOWN_FEET + (feet - eyes));

		origin.Z += zoffset;
		angles.X = 0;
		MathLib.AngleMatrix(angles, origin, out outMatrix);
	}

	public GrabController() {
		Shadow.DampFactor = 1.0f;
		Shadow.TeleportDistance = 0;
		ErrorTime = 0;
		Error = 0;
		// make this controller really stiff!
		Shadow.MaxSpeed = 1000;
		Shadow.MaxAngular = DEFAULT_MAX_ANGULAR;
		Shadow.MaxDampSpeed = Shadow.MaxSpeed * 2;
		Shadow.MaxDampAngular = Shadow.MaxAngular;
		AttachedEntity.Set(null);
		PreferredCarryAngles = vec3_angle;
		HasPreferredCarryAngles = false;
		DistanceOffset = 0;
	}

	public void OnRestore() {
		Controller?.SetEventHandler(this);
	}

	public void SetTargetPosition(in Vector3 target, in QAngle targetOrientation) {
		Shadow.TargetPosition = target;
		Shadow.TargetRotation = targetOrientation;

		TimeToArrive = gpGlobals.FrameTime;

		BaseEntity? attached = GetAttached();
		if (attached != null) {
			IPhysicsObject? obj = attached.VPhysicsGetObject();

			if (obj != null)
				obj.Wake();
			else
				DetachEntity(false);
		}
	}

	public float ComputeError() {
		if (ErrorTime <= 0)
			return 0;

		BaseEntity? attached = GetAttached();
		if (attached != null) {
			IPhysicsObject? obj = attached.VPhysicsGetObject();

			if (obj != null) {
				obj.GetShadowPosition(out Vector3 pos, out _);

				float error = (Shadow.TargetPosition - pos).Length();
				if (ErrorTime > 0) {
					if (ErrorTime > 1)
						ErrorTime = 1;

					float speed = error / ErrorTime;
					if (speed > Shadow.MaxSpeed)
						error *= 0.5f;

					Error = (1 - ErrorTime) * Error + error * ErrorTime;
				}
			}
			else {
				DevMsg("Object attached to Physcannon has no physics object\n");
				DetachEntity(false);
				return 9999; // force detach
			}

			if (attached.IsEFlagSet(EFL.IsBeingLiftedByBarnacle))
				Error *= 3.0f;
		}

		ErrorTime = 0;

		return Error;
	}

	public float GetLoadWeight() => LoadWeight;
	public void SetAngleAlignment(float alignAngleCosine) => AngleAlignment = alignAngleCosine;
	public void SetIgnorePitch(bool ignore) => IgnoreRelativePitch = ignore;

	public QAngle TransformAnglesToPlayerSpace(in QAngle anglesIn, BasePlayer player) {
		if (IgnoreRelativePitch) {
			QAngle angleTest = player.EyeAngles();
			angleTest.X = 0;
			MathLib.AngleMatrix(angleTest, out Matrix3x4 test);
			return MathLib.TransformAnglesToLocalSpace(anglesIn, test);
		}
		return MathLib.TransformAnglesToLocalSpace(anglesIn, player.EntityToWorldTransform());
	}

	public QAngle TransformAnglesFromPlayerSpace(in QAngle anglesIn, BasePlayer player) {
		if (IgnoreRelativePitch) {
			QAngle angleTest = player.EyeAngles();
			angleTest.X = 0;
			MathLib.AngleMatrix(angleTest, out Matrix3x4 test);
			return MathLib.TransformAnglesToWorldSpace(anglesIn, test);
		}
		return MathLib.TransformAnglesToWorldSpace(anglesIn, player.EntityToWorldTransform());
	}

	public BaseEntity? GetAttached() => AttachedEntity.Get();

	public void AttachEntity(BasePlayer player, BaseEntity entity, IPhysicsObject phys, bool isMegaPhysCannon, in Vector3 grabPosition, bool useGrabPosition) {
		// play the impact sound of the object hitting the player
		// used as feedback to let the player know he picked up the object
		int hitMaterial = phys.GetMaterialIndex();
		int playerMaterial = player.VPhysicsGetObject() != null ? player.VPhysicsGetObject()!.GetMaterialIndex() : hitMaterial;
		PhysicsImpactSound(player, phys, SoundEntityChannel.Static, hitMaterial, playerMaterial, 1.0f, 64);
		phys.GetPosition(out Vector3 position, out QAngle angles);
		// If it has a preferred orientation, use that instead.
		Pickup.GetPreferredCarryAngles(entity, player, player.EntityToWorldTransform(), ref angles);

		//	ComputeMaxSpeed( pEntity, pPhys );

		// Carried entities can never block LOS
		CarriedEntityBlocksLOS = entity.BlocksLOS();
		entity.SetBlocksLOS(false);
		Controller = physenv.CreateMotionController(this);
		Controller.AttachObject(phys, true);
		// Don't do this, it's causing trouble with constraint solvers.
		//m_controller->SetPriority( IPhysicsMotionController::HIGH_PRIORITY );

		phys.Wake();
		PhysSetGameFlags(phys, PhysicsFlags.PlayerHeld);
		SetTargetPosition(position, angles);
		AttachedEntity.Set(entity);
		IPhysicsObject[] list = ArrayPool<IPhysicsObject>.Shared.Rent(VPHYSICS_MAX_OBJECT_LIST_COUNT);
		int count = entity.VPhysicsGetObjectList(list.AsSpan(0, VPHYSICS_MAX_OBJECT_LIST_COUNT));
		LoadWeight = 0;
		float damping = 10;
		float factor = count / 7.5f;
		if (factor < 1.0f)
			factor = 1.0f;

		for (int i = 0; i < count; i++) {
			float mass = list[i].GetMass();
			list[i].GetDamping(out float speedDamping, out SavedRotDamping[i]);
			LoadWeight += mass;
			SavedMass[i] = mass;

			// reduce the mass to prevent the player from adding crazy amounts of energy to the system
			list[i].SetMass(REDUCED_CARRY_MASS / factor);
			list[i].SetDamping(ref speedDamping, ref damping);
		}
		ArrayPool<IPhysicsObject>.Shared.Return(list, true);

		// Give extra mass to the phys object we're actually picking up
		phys.SetMass(REDUCED_CARRY_MASS);
		phys.EnableDrag(false);

		ErrorTime = isMegaPhysCannon ? -1.5f : -1.0f; // 1 seconds until error starts accumulating
		Error = 0;
		ContactAmount = 0;

		AttachedAnglesPlayerSpace = TransformAnglesToPlayerSpace(angles, player);
		if (AngleAlignment != 0)
			AttachedAnglesPlayerSpace = AlignAngles(AttachedAnglesPlayerSpace, AngleAlignment);

		// Ragdolls don't offset this way
		if (entity is RagdollProp)
			AttachedPositionObjectSpace = default;
		else
			MathLib.VectorITransform(entity.WorldSpaceCenter(), entity.EntityToWorldTransform(), out AttachedPositionObjectSpace);

		// If it's a prop, see if it has desired carry angles
		if (entity is PhysicsProp prop) {
			// m_bHasPreferredCarryAngles = pProp->GetPropDataAngles( "preferred_carryangles", m_vecPreferredCarryAngles );
			HasPreferredCarryAngles = false;
			DistanceOffset = prop.GetCarryDistanceOffset();
		}
		else {
			HasPreferredCarryAngles = false;
			DistanceOffset = 0;
		}

		AllowObjectOverhead = IsObjectAllowedOverhead(entity);
	}

	static void ClampPhysicsVelocity(IPhysicsObject phys, float linearLimit, float angularLimit) {
		phys.GetVelocity(out Vector3 vel, out Vector3 angVel);
		float speed = MathLib.VectorNormalize(ref vel) - linearLimit;
		float angSpeed = MathLib.VectorNormalize(ref angVel) - angularLimit;
		speed = speed < 0 ? 0 : -speed;
		angSpeed = angSpeed < 0 ? 0 : -angSpeed;
		vel *= speed;
		angVel *= angSpeed;
		phys.AddVelocity(vel, angVel);
	}

	public void DetachEntity(bool clearVelocity) {
		Assert(!PhysIsInCallback());
		BaseEntity? entity = GetAttached();
		if (entity != null) {
			// Restore the LS blocking state
			entity.SetBlocksLOS(CarriedEntityBlocksLOS);
			IPhysicsObject[] list = ArrayPool<IPhysicsObject>.Shared.Rent(VPHYSICS_MAX_OBJECT_LIST_COUNT);
			int count = entity.VPhysicsGetObjectList(list.AsSpan(0, VPHYSICS_MAX_OBJECT_LIST_COUNT));
			for (int i = 0; i < count; i++) {
				IPhysicsObject? phys = list[i];
				if (phys == null)
					continue;

				// on the odd chance that it's gone to sleep while under anti-gravity
				phys.EnableDrag(true);
				phys.Wake();
				phys.SetMass(SavedMass[i]);
				phys.GetDamping(out float speedDamping, out _);
				phys.SetDamping(ref speedDamping, ref SavedRotDamping[i]);
				PhysClearGameFlags(phys, PhysicsFlags.PlayerHeld);
				if (clearVelocity)
					PhysForceClearVelocity(phys);
				else
					ClampPhysicsVelocity(phys, HL2_Player.HL2_NORM_SPEED * 1.5f, 2.0f * 360.0f);
			}
			ArrayPool<IPhysicsObject>.Shared.Return(list, true);
		}

		AttachedEntity.Set(null);
		if (Controller != null)
			physenv.DestroyMotionController(Controller);
		Controller = null;
	}

	static bool InContactWithHeavyObject(IPhysicsObject obj, float heavyMass) {
		bool contact = false;
		IPhysicsFrictionSnapshot snapshot = obj.CreateFrictionSnapshot();
		while (snapshot.IsValid()) {
			IPhysicsObject other = snapshot.GetObject(1)!;
			if (!other.IsMoveable() || other.GetMass() > heavyMass) {
				contact = true;
				break;
			}
			snapshot.NextFrictionData();
		}
		obj.DestroyFrictionSnapshot(snapshot);
		return contact;
	}

	public SimResult Simulate(IPhysicsMotionController? controller, IPhysicsObject? obj, TimeUnit_t deltaTime, out Vector3 linear, out Vector3 angular) {
		HLShadowControlParams shadowParams = Shadow;
		if (InContactWithHeavyObject(obj!, GetLoadWeight()))
			ContactAmount = MathLib.Approach(0.1f, ContactAmount, (float)deltaTime * 2.0f);
		else
			ContactAmount = MathLib.Approach(1.0f, ContactAmount, (float)deltaTime * 2.0f);

		shadowParams.MaxAngular = Shadow.MaxAngular * ContactAmount * ContactAmount * ContactAmount;
		TimeToArrive = obj!.ComputeShadowControl(shadowParams, TimeToArrive, deltaTime);

		// Slide along the current contact points to fix bouncing problems
		obj.GetVelocity(out Vector3 velocity, out Vector3 angVel);
		PhysComputeSlideDirection(obj, velocity, angVel, out velocity, out _, true, GetLoadWeight());
		obj.SetVelocityInstantaneous(velocity, angVel);

		linear = default;
		angular = default;
		ErrorTime += (float)deltaTime;

		return SimResult.LocalAcceleration;
	}

	public float GetSavedMass(IPhysicsObject obj) {
		BaseEntity? held = AttachedEntity.Get();
		if (held != null) {
			if (obj.GetGameData() == held) {
				IPhysicsObject[] list = ArrayPool<IPhysicsObject>.Shared.Rent(VPHYSICS_MAX_OBJECT_LIST_COUNT);
				try {
					int count = held.VPhysicsGetObjectList(list.AsSpan(0, VPHYSICS_MAX_OBJECT_LIST_COUNT));
					for (int i = 0; i < count; i++) {
						if (list[i] == obj)
							return SavedMass[i];
					}
				}
				finally {
					ArrayPool<IPhysicsObject>.Shared.Return(list, true);
				}
			}
		}
		return 0.0f;
	}

	/// <summary>
	/// Is this an object that the player is allowed to lift to a position
	/// directly overhead? The default behavior prevents lifting objects directly
	/// overhead, but there are exceptions for gameplay purposes.
	/// </summary>
	public bool IsObjectAllowedOverhead(BaseEntity entity) {
		// Allow combine balls overhead
		if (entity is PropCombineBall)
			return true;

		// Allow props that are specifically flagged as such
		if (entity is PhysicsProp physProp && physProp.HasInteraction(PropDataInteractions.PhysgunAllowOverhead))
			return true;

		// String checks are fine here, we only run this code one time- when the object is picked up.
		if (entity.ClassMatches("grenade_helicopter"))
			return true;

		if (entity.ClassMatches("weapon_striderbuster"))
			return true;

		return false;
	}

	public bool UpdateObject(BasePlayer player, float error) {
		BaseEntity? entity = GetAttached();
		if (entity == null || ComputeError() > error || player.GetGroundEntity() == entity || entity.VPhysicsGetObject() == null)
			return false;

		//Adrian: Oops, our object became motion disabled, let go!
		IPhysicsObject? phys = entity.VPhysicsGetObject();
		if (phys != null && phys.IsMoveable() == false)
			return false;

		QAngle playerAngles = player.EyeAngles();
		MathLib.AngleVectors(playerAngles, out Vector3 forward, out _, out _);

		float pitch = MathLib.AngleDistance(playerAngles.X, 0);

		if (!AllowObjectOverhead)
			playerAngles.X = Math.Clamp(pitch, -75, 75);
		else
			playerAngles.X = Math.Clamp(pitch, -90, 75);

		// Now clamp a sphere of object radius at end to the player's bbox
		Vector3 radial = physcollision.CollideGetExtent(phys!.GetCollide(), vec3_origin, entity.GetAbsAngles(), -forward);
		Vector3 player2d = player.CollisionProp().OBBMaxs();
		float playerRadius = player2d.Length2D();
		float radius = playerRadius + MathF.Abs(Vector3.Dot(forward, radial));

		float distance = 24 + (radius * 2.0f);

		// Add the prop's distance offset
		distance += DistanceOffset;

		Vector3 start = player.Weapon_ShootPosition();
		Vector3 end = start + (forward * distance);

		TraceFilterSkipTwoEntities traceFilter = new(player, entity, CollisionGroup.None);
		Ray ray = default;
		ray.Init(start, end);
		enginetrace.TraceRay(ray, Mask.SolidBrushOnly, ref traceFilter, out Trace tr);

		if (tr.Fraction < 0.5)
			end = start + forward * (radius * 0.5f);
		else if (tr.Fraction <= 1.0f)
			end = start + forward * (distance - radius);

		player.CollisionProp().WorldSpaceAABB(out Vector3 playerMins, out Vector3 playerMaxs);
		Vector3 playerLine = player.CollisionProp().WorldSpaceCenter();
		MathLib.CalcClosestPointOnLine(end, playerLine + new Vector3(0, 0, playerMins.Z), playerLine + new Vector3(0, 0, playerMaxs.Z), out Vector3 nearest, out _);

		if (!AllowObjectOverhead) {
			Vector3 delta = end - nearest;
			float len = MathLib.VectorNormalize(ref delta);
			if (len < radius)
				end = nearest + radius * delta;
		}

		QAngle angles = TransformAnglesFromPlayerSpace(AttachedAnglesPlayerSpace, player);

		// If it has a preferred orientation, update to ensure we're still oriented correctly.
		Pickup.GetPreferredCarryAngles(entity, player, player.EntityToWorldTransform(), ref angles);

		// We may be holding a prop that has preferred carry angles
		if (HasPreferredCarryAngles) {
			ComputePlayerMatrix(player, out Matrix3x4 tmp);
			angles = MathLib.TransformAnglesToWorldSpace(PreferredCarryAngles, tmp);
		}

		MathLib.AngleMatrix(angles, out Matrix3x4 attachedToWorld);
		MathLib.VectorRotate(AttachedPositionObjectSpace, attachedToWorld, out Vector3 offset);

		SetTargetPosition(end - offset, angles);

		return true;
	}

	HLShadowControlParams Shadow;
	TimeUnit_t TimeToArrive;
	float ErrorTime;
	float Error;
	float ContactAmount;
	float AngleAlignment;
	bool CarriedEntityBlocksLOS;
	bool IgnoreRelativePitch;

	float LoadWeight;
	readonly float[] SavedRotDamping = new float[VPHYSICS_MAX_OBJECT_LIST_COUNT];
	readonly float[] SavedMass = new float[VPHYSICS_MAX_OBJECT_LIST_COUNT];
	readonly EHANDLE AttachedEntity = new();
	QAngle PreferredCarryAngles;
	bool HasPreferredCarryAngles;
	float DistanceOffset;

	QAngle AttachedAnglesPlayerSpace;
	Vector3 AttachedPositionObjectSpace;

	IPhysicsMotionController? Controller;

	// Can the player hold this object directly overhead? (Default is NO)
	bool AllowObjectOverhead;
}

/// <summary>
/// Player pickup controller
/// </summary>
[LinkEntityToClass("player_pickup")]
public class PlayerPickupController : BaseEntity
{
	const float DOT_30DEGREE = 0.866025403784f;

	public void Init(BasePlayer player, BaseEntity obj) {
		// Holster player's weapon
		if (player.GetActiveWeapon() != null) {
			if (!player.GetActiveWeapon()!.CanHolster() || !player.GetActiveWeapon()!.Holster()) {
				Shutdown();
				return;
			}
		}

		HL2_Player? owner = (HL2_Player?)ToBasePlayer(player);
		owner?.EnableSprint(false);

		// If the target is debris, convert it to non-debris
		if (obj.GetCollisionGroup() == Source.CollisionGroup.Debris) {
			// Interactive debris converts back to debris when it comes to rest
			obj.SetCollisionGroup(Source.CollisionGroup.InteractiveDebris);
		}

		// done so I'll go across level transitions with the player
		SetParent(player);
		grabController.SetIgnorePitch(true);
		grabController.SetAngleAlignment(DOT_30DEGREE);
		Player = player;
		IPhysicsObject? physics = obj.VPhysicsGetObject();

		Pickup.OnPhysGunPickup(obj, Player, PhysGunPickup.PickedUpByPlayer);

		grabController.AttachEntity(player, obj, physics!, false, vec3_origin, false);

		// m_pPlayer->m_Local.m_iHideHUD |= HIDEHUD_WEAPONSELECTION;
		Player.SetUseEntity(this);
	}

	public void Shutdown(bool thrown = false) {
		BaseEntity? obj = grabController.GetAttached();

		bool clearVelocity = false;
		if (!thrown && obj != null && obj.VPhysicsGetObject() != null && obj.VPhysicsGetObject()!.GetContactPoint(out _, null!))
			clearVelocity = true;

		grabController.DetachEntity(clearVelocity);

		if (obj != null)
			Pickup.OnPhysGunDrop(obj, Player!, thrown ? PhysGunDrop.ThrownByPlayer : PhysGunDrop.DroppedByPlayer);

		if (Player != null) {
			HL2_Player? owner = (HL2_Player?)ToBasePlayer(Player);
			owner?.EnableSprint(true);

			Player.SetUseEntity(null);
			if (Player.GetActiveWeapon() != null) {
				if (!Player.GetActiveWeapon()!.Deploy()) {
					// We tried to restore the player's weapon, but we couldn't.
					// This usually happens when they're holding an empty weapon that doesn't
					// autoswitch away when out of ammo. Switch to next best weapon.
					Player.SwitchToNextBestWeapon(null);
				}
			}

			// m_pPlayer->m_Local.m_iHideHUD &= ~HIDEHUD_WEAPONSELECTION;
		}
		Remove();
	}

	public override bool OnControls(BaseEntity controls) => true;

	public override void Use(BaseEntity? activator, BaseEntity? caller, UseType useType, float value) {
		if (ToBasePlayer(activator) == Player) {
			BaseEntity? attached = grabController.GetAttached();

			// UNDONE: Use vphysics stress to decide to drop objects
			// UNDONE: Must fix case of forcing objects into the ground you're standing on (causes stress) before that will work
			if (attached == null || useType == UseType.Off || (Player!.Buttons & InButtons.Attack2) != 0 || grabController.ComputeError() > 12) {
				Shutdown();
				return;
			}

			//Adrian: Oops, our object became motion disabled, let go!
			IPhysicsObject? phys = attached.VPhysicsGetObject();
			if (phys != null && phys.IsMoveable() == false) {
				Shutdown();
				return;
			}

			// +ATTACK will throw phys objects
			if ((Player.Buttons & InButtons.Attack) != 0) {
				Shutdown(true);
				Player.EyeVectors(out Vector3 launch);
				// JAY: Scale this with mass because some small objects really go flying
				float massFactor = Math.Clamp(phys!.GetMass(), 0.5f, 15);
				massFactor = MathLib.RemapVal(massFactor, 0.5f, 15, 0.5f, 4);
				launch *= GrabController.player_throwforce.GetFloat() * massFactor;

				phys.ApplyForceCenter(launch);
				Vector3 aVel = RandomAngularImpulse(-10, 10) * massFactor;
				phys.ApplyTorqueCenter(aVel);
				return;
			}

			if (useType == UseType.Set) {
				// update position
				grabController.UpdateObject(Player, 12);
			}
		}
	}

	public bool IsHoldingEntity(BaseEntity? ent) => grabController.GetAttached() == ent;
	public GrabController GetGrabController() => grabController;

	readonly GrabController grabController = new();
	BasePlayer? Player;

	public static void PlayerPickupObject(BasePlayer player, BaseEntity obj) {
		//Don't pick up if we don't have a phys object.
		if (obj.VPhysicsGetObject() == null)
			return;

		PlayerPickupController? controller = (PlayerPickupController?)Create("player_pickup", obj.GetAbsOrigin(), vec3_angle, player);

		if (controller == null)
			return;

		controller.Init(player, obj);
	}

	public static bool PlayerPickupControllerIsHoldingEntity(BaseEntity? pickupControllerEntity, BaseEntity? heldEntity) {
		PlayerPickupController? controller = pickupControllerEntity as PlayerPickupController;

		return controller != null ? controller.IsHoldingEntity(heldEntity) : false;
	}

	public static float PlayerPickupGetHeldObjectMass(BaseEntity? pickupControllerEntity, IPhysicsObject heldObject) {
		float mass = 0.0f;
		PlayerPickupController? controller = pickupControllerEntity as PlayerPickupController;
		if (controller != null) {
			GrabController grab = controller.GetGrabController();
			mass = grab.GetSavedMass(heldObject);
		}
		return mass;
	}
}
