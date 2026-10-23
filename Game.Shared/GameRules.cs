#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
#if CLIENT_DLL
global using static Game.Client.C_GameRules;

global using GameRules = Game.Client.C_GameRules;
global using GameRulesProxy = Game.Client.C_GameRulesProxy;
namespace Game.Client;
#else
global using static Game.Server.GameRules;

global using GameRules = Game.Server.GameRules;
global using GameRulesProxy = Game.Server.GameRulesProxy;
namespace Game.Server;
#endif

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Formats.Keyvalues;
using Source.Common.Mathematics;
using Source.Engine.Server;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

public enum GameRulesRespawnReturnCode
{
	None,

	WeaponRespawnYes,
	WeaponRespawnNo,

	AmmoRespawnYes,
	AmmoRespawnNo,

	ItemRespawnYes,
	ItemRespawnNo,

	PlayerDropGunAll,
	PlayerDropGunActive,
	PlayerDropGunNo,

	PlayerDropAmmoAll,
	PlayerDropAmmoActive,
	PlayerDropAmmoNo,
}

public enum GameRulesPlayerRelationship
{
	NotTeammate,
	Teammate,
	Enemy,
	Ally,
	Neutral
}

[NetworkName("CGameRulesProxy", NoBase = true)]
public partial class
#if CLIENT_DLL
	C_GameRulesProxy
#else
	GameRulesProxy
#endif
	: BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

	public virtual GameRules GameRules => gameRules!;
	GameRules? gameRules = null;

	public static GameRulesProxy? s_GameRulesProxy;

	public static void NotifyNetworkStateChanged() => s_GameRulesProxy?.NetworkStateChanged();
}

public abstract class
#if CLIENT_DLL
	C_GameRules
#else
	GameRules
#endif
: AutoGameSystemPerFrame
{
	[NotNull] public static GameRules g_pGameRules = null!;
	public
#if CLIENT_DLL
	C_GameRules
#else
	GameRules
#endif
	() : base("GameRules") {
		g_pGameRules = this;
#if GAME_DLL
		GetVoiceGameMgr().Init(g_pVoiceGameMgrHelper, gpGlobals.MaxClients);
#endif
	}

	public static readonly ViewVectors g_DefaultViewVectors = new(
		new Vector3(0, 0, 64),          //VEC_VIEW (View)

		new Vector3(-16, -16, 0),       //VEC_HULL_MIN (HullMin)
		new Vector3(16, 16, 72),    //VEC_HULL_MAX (HullMax)

		new Vector3(-16, -16, 0),       //VEC_DUCK_HULL_MIN (DuckHullMin)
		new Vector3(16, 16, 36),    //VEC_DUCK_HULL_MAX	(DuckHullMax)
		new Vector3(0, 0, 28),          //VEC_DUCK_VIEW		(DuckView)

		new Vector3(-10, -10, -10),     //VEC_OBS_HULL_MIN	(ObsHullMin)
		new Vector3(10, 10, 10),    //VEC_OBS_HULL_MAX	(ObsHullMax)

		new Vector3(0, 0, 14)           //VEC_DEAD_VIEWHEIGHT (DeadViewHeight)
	);

	public virtual ReadOnlySpan<char> Name() => "GameRules";

	public abstract bool Damage_IsTimeBased(DamageType dmgType);       // Damage types that are time-based.
	public abstract bool Damage_ShouldGibCorpse(DamageType dmgType);   // Damage types that gib the corpse.
	public abstract bool Damage_ShowOnHUD(DamageType dmgType);     // Damage types that have client HUD art.
	public abstract bool Damage_NoPhysicsForce(DamageType dmgType);    // Damage types that don't have to supply a physics force & position.
	public abstract bool Damage_ShouldNotBleed(DamageType dmgType);    // Damage types that don't make the player bleed.
																	   //Temp: These will go away once DamageTypes become enums.
	public abstract DamageType Damage_GetTimeBased();          // Actual bit-fields.
	public abstract DamageType Damage_GetShouldGibCorpse();
	public abstract DamageType Damage_GetShowOnHud();
	public abstract DamageType Damage_GetNoPhysicsForce();
	public abstract DamageType Damage_GetShouldNotBleed();

	public virtual bool SwitchToNextBestWeapon(BaseCombatCharacter player, BaseCombatWeapon? currentWeapon) {
		return false;
	}
	public virtual BaseCombatWeapon? GetNextBestWeapon(BaseCombatCharacter player, BaseCombatWeapon? currentWeapon) {
		return null;
	}
	public virtual bool ShouldCollide(CollisionGroup collisionGroup0, CollisionGroup collisionGroup1) {
		if (collisionGroup0 > collisionGroup1)
			(collisionGroup0, collisionGroup1) = (collisionGroup1, collisionGroup0);

#if !HL2MP
		if ((collisionGroup0 == CollisionGroup.Player || collisionGroup0 == CollisionGroup.PlayerMovement) &&
			collisionGroup1 == CollisionGroup.PUSHAWAY) {
			return false;
		}
#endif

		if (collisionGroup0 == CollisionGroup.Debris && collisionGroup1 == CollisionGroup.Pushaway) {
			// let debris and multiplayer objects collide
			return true;
		}

		// --------------------------------------------------------------------------
		// NOTE: All of this code assumes the collision groups have been sorted!!!!
		// NOTE: Don't change their order without rewriting this code !!!
		// --------------------------------------------------------------------------

		// Don't bother if either is in a vehicle...
		if ((collisionGroup0 == CollisionGroup.InVehicle) || (collisionGroup1 == CollisionGroup.InVehicle))
			return false;

		if ((collisionGroup1 == CollisionGroup.DoorBlocker) && (collisionGroup0 != CollisionGroup.NPC))
			return false;

		if ((collisionGroup0 == CollisionGroup.Player) && (collisionGroup1 == CollisionGroup.PassableDoor))
			return false;

		if (collisionGroup0 == CollisionGroup.Debris || collisionGroup0 == CollisionGroup.DebrisTrigger) {
			// put exceptions here, right now this will only collide with CollisionGroup.NONE
			return false;
		}

		// Dissolving guys only collide with CollisionGroup.NONE
		if ((collisionGroup0 == CollisionGroup.Dissolving) || (collisionGroup1 == CollisionGroup.Dissolving)) {
			if (collisionGroup0 != CollisionGroup.None)
				return false;
		}

		// doesn't collide with other members of this group
		// or debris, but that's handled above
		if (collisionGroup0 == CollisionGroup.InteractiveDebris && collisionGroup1 == CollisionGroup.InteractiveDebris)
			return false;

#if !HL2MP
		// This change was breaking HL2DM
		// Adrian: TEST! Interactive Debris doesn't collide with the player.
		if (collisionGroup0 == CollisionGroup.INTERACTIVE_DEBRIS && (collisionGroup1 == CollisionGroup.Player_MOVEMENT || collisionGroup1 == CollisionGroup.Player))
			return false;
#endif

		if (collisionGroup0 == CollisionGroup.BreakableGlass && collisionGroup1 == CollisionGroup.BreakableGlass)
			return false;

		// interactive objects collide with everything except debris & interactive debris
		if (collisionGroup1 == CollisionGroup.Interactive && collisionGroup0 != CollisionGroup.None)
			return false;

		// Projectiles hit everything but debris, weapons, + other projectiles
		if (collisionGroup1 == CollisionGroup.Projectile) {
			if (collisionGroup0 == CollisionGroup.Debris ||
				collisionGroup0 == CollisionGroup.Weapon ||
				collisionGroup0 == CollisionGroup.Projectile) {
				return false;
			}
		}

		// Don't let vehicles collide with weapons
		// Don't let players collide with weapons...
		// Don't let NPCs collide with weapons
		// Weapons are triggers, too, so they should still touch because of that
		if (collisionGroup1 == CollisionGroup.Weapon) {
			if (collisionGroup0 == CollisionGroup.Vehicle ||
				collisionGroup0 == CollisionGroup.Player ||
				collisionGroup0 == CollisionGroup.NPC) {
				return false;
			}
		}

		// collision with vehicle clip entity??
		if (collisionGroup0 == CollisionGroup.VehicleClip || collisionGroup1 == CollisionGroup.VehicleClip) {
			// yes then if it's a vehicle, collide, otherwise no collision
			// vehicle sorts lower than vehicle clip, so must be in 0
			if (collisionGroup0 == CollisionGroup.Vehicle)
				return true;
			// vehicle clip against non-vehicle, no collision
			return false;
		}

		return true;
	}
	public virtual int DefaultFOV() => 90;
	public void NetworkStateChanged() {
		// Forward the call to the entity that will send the data.
		GameRulesProxy.NotifyNetworkStateChanged();
	}
	public void NetworkStateChanged(IFieldAccessor accessor) {
		GameRulesProxy.NotifyNetworkStateChanged();
	}
	public virtual ViewVectors GetViewVectors() => g_DefaultViewVectors;
	public virtual float GetAmmoDamage(BaseEntity? attacker, BaseEntity? victim, int ammoType) {
		float damage = 0;
		AmmoDef ammoDef = GetAmmoDef();

		if (attacker.IsPlayer())
			damage = ammoDef.PlrDamage(ammoType);
		else
			damage = ammoDef.NPCDamage(ammoType);

		return damage;
	}
	public virtual float GetDamageMultiplier() => 1.0f;
	public abstract bool IsMultiplayer();// is this a multiplayer game? (either coop or deathmatch)
	public virtual ReadOnlySpan<byte> GetEncryptionKey() => null;
	public virtual bool InRoundRestart() => false;
	public virtual bool AllowThirdPersonCamera() => false;
	public virtual void ClientCommandKeyValues(Edict edict, KeyValues keyvalues) { }
	public virtual bool IsConnectedUserInfoChangeAllowed(BasePlayer? player) {
		Assert(!IsMultiplayer());
		return true;
	}

#if CLIENT_DLL

	public virtual bool IsBonusChallengeTimeBased() => true;
	public virtual bool AllowMapParticleEffect(ReadOnlySpan<char> pszParticleEffect) => true;
	public virtual bool AllowWeatherParticles() => true;
	public virtual bool AllowMapVisionFilterShaders() => false;
	public virtual ReadOnlySpan<char> TranslateEffectForVisionFilter(ReadOnlySpan<char> ros, ReadOnlySpan<char> effectName) { return effectName; }
	public virtual bool IsLocalPlayer(int entIndex) {
		C_BasePlayer? localPlayer = C_BasePlayer.GetLocalPlayer();
		return localPlayer != null && localPlayer == cl_entitylist.GetEnt(entIndex);
	}
	public virtual void ModifySentChat(Span<char> text) { }
	public virtual bool ShouldConfirmOnDisconnect() => false;

#else

	// TODO: public virtual void Status( void (*print) (ReadOnlySpan<char> fmt, ...) ) {}

	public virtual void GetTaggedConVarList(KeyValues keyValues) { }

	// NVNT see if the client of the player entered is using a haptic device.
	public virtual void CheckHaptics(BasePlayer player) {
		ReadOnlySpan<char> pszHH = engine.GetClientConVarValue(player.EntIndex(), "hap_HasDevice");
		if (!pszHH.IsEmpty) {
			int iHH = atoi(pszHH);
			// todo: player.SetHaptics(iHH != 0);
		}
	}

	// Called when game rules are destroyed by CWorld
	public virtual void LevelShutdown() { return; }

	public virtual void Precache() { return; }

	public virtual void RefreshSkillData(bool forceUpdate) {
#if !CLIENT_DLL
		if (!forceUpdate) {
			if (GlobalEntity.IsInTable("skill.cfg"))
				return;
		}
		GlobalEntity.Add("skill.cfg", gpGlobals.MapName, GlobalEState.On);

		ConVarRef skill = new("skill");

		SetSkillLevel(skill.IsValid() ? skill.GetInt() : 1);

#if HL2_DLL
		engine.ServerCommand("exec skill_manifest.cfg\n");
		engine.ServerExecute();
#elif !TF_DLL && !DOD_DLL
		engine.ServerCommand($"exec skill{GetSkillLevel()}.cfg\n");
		engine.ServerExecute();
#endif
#endif
	}

	// Called each frame. This just forwards the call to Think().
	public override void FrameUpdatePostEntityThink() {
		Think();
	}

	public virtual void Think() {// GR_Think - runs every server frame, should handle any timer tasks, periodic events, etc.
		GetVoiceGameMgr().Update(gpGlobals.FrameTime);
	}
	public abstract bool IsAllowedToSpawn(BaseEntity entity);  // Can this item spawn (eg NPCs don't spawn in deathmatch).

	// Called at the end of GameFrame (i.e. after all game logic has run this frame)
	public virtual void EndGameFrame() {
		// g_MultiDamage impl todo
	}

	public virtual bool IsSkillLevel(int iLevel) { return GetSkillLevel() == iLevel; }
	public virtual int GetSkillLevel() { return g_iSkillLevel; }
	public virtual void OnSkillLevelChanged(int skillLevel) { }
	public virtual void SetSkillLevel(int iLevel) {
		int oldLevel = g_iSkillLevel;

		if (iLevel < 1) {
			iLevel = 1;
		}
		else if (iLevel > 3) {
			iLevel = 3;
		}

		g_iSkillLevel = iLevel;

		if (g_iSkillLevel != oldLevel)
			OnSkillLevelChanged(g_iSkillLevel);
	}

	public abstract bool FAllowFlashlight();// Are players allowed to switch on their flashlight?
	public abstract bool FShouldSwitchWeapon(BasePlayer player, BaseCombatWeapon? weapon);// should the player switch to this weapon?

	// Functions to verify the single/multiplayer status of a game
	public abstract bool IsDeathmatch();//is this a deathmatch game?
	public virtual bool IsTeamplay() => false;// is this deathmatch game being played with team rules?
	public abstract bool IsCoOp();// is this a coop game?
	public virtual ReadOnlySpan<char> GetGameDescription() {
#if GMOD_DLL
		return "Garry's Mod";
#else
		return "Half-Life 2";
#endif
	}  // this is the game name that gets seen in the server browser

	// Client connection/disconnection
	public abstract bool ClientConnected(Edict entity, ReadOnlySpan<char> pszName, ReadOnlySpan<char> pszAddress, Span<char> reject);// a client just connected to the server (player hasn't spawned yet)
	public abstract void InitHUD(BasePlayer pl);        // the client dll is ready for updating
	public abstract void ClientDisconnected(Edict client);// a client just disconnected from the server

	// Client damage rules
	public abstract float FlPlayerFallDamage(BasePlayer player);// this client just hit the ground after a fall. How much damage?
	public virtual bool FPlayerCanTakeDamage(BasePlayer player, BaseEntity ent, in TakeDamageInfo info) => true;// can this player take damage from this attacker?
	public virtual bool ShouldAutoAim(BasePlayer player, Edict edict) => true;
	public virtual float GetAutoAimScale(BasePlayer player) => 1.0f;
	// todo: public virtual int GetAutoAimMode() { return AUTOAIM_ON; }

	static readonly ConVar old_radius_damage = new("old_radiusdamage", "0.0", FCvar.Replicated);

	public static bool IsExplosionTraceBlocked(ref GameTrace ptr) {
		if (ptr.DidHitWorld())
			return true;

		if (ptr.Ent == null)
			return false;

		if (ptr.Ent!.GetMoveType() == MoveType.Push) {
			// All doors are push, but not all things that push are doors. This 
			// narrows the search before we start to do classname compares.
			if (BaseEntity.FClassnameIs(ptr.Ent!, "prop_door_rotating") ||
				BaseEntity.FClassnameIs(ptr.Ent!, "func_door") ||
				BaseEntity.FClassnameIs(ptr.Ent!, "func_door_rotating"))
				return true;
		}

		return false;
	}

	public const float ROBUST_RADIUS_PROBE_DIST = 16;

	public virtual bool ShouldUseRobustRadiusDamage(BaseEntity ent) => false;
	public virtual void RadiusDamage(in TakeDamageInfo info, in Vector3 vecSrcIn, float flRadius, Class_T iClassIgnore, BaseEntity? entityIgnore) {
		const Contents MASK_RADIUS_DAMAGE = ((Contents)Mask.Shot) & (~Contents.HitBox);
		BaseEntity? entity = null;
		GameTrace tr;
		float flAdjustedDamage, falloff;
		Vector3 vecSpot;

		Vector3 vecSrc = vecSrcIn;

		if (flRadius != 0)
			falloff = info.GetDamage() / flRadius;
		else
			falloff = 1.0f;

		bool bInWater = (Util.PointContents(vecSrc) & (Contents)Mask.Water) != 0;

#if HL2_DLL
		if (bInWater) {
			// Only muffle the explosion if deeper than 2 feet in water.
			if (0 == (Util.PointContents(vecSrc + new Vector3(0, 0, 24)) & (Contents)Mask.Water))
				bInWater = false;
		}
#endif // HL2_DLL

		vecSrc.Z += 1;// in case grenade is lying on the ground

		float flHalfRadiusSqr = MathF.Sqrt(flRadius / 2.0f);

		// iterate on all entities in the vicinity.
		for (EntitySphereQuery sphere = new(vecSrc, flRadius); (entity = sphere.GetCurrentEntity()) != null; sphere.NextEntity()) {
			// This value is used to scale damage when the explosion is blocked by some other object.
			float flBlockedDamagePercent = 0.0f;

			if (entity == entityIgnore)
				continue;

			if ((Damage)entity.m_takedamage == Damage.No)
				continue;

			// UNDONE: this should check a damage mask, not an ignore
			if (iClassIgnore != Class_T.None && entity.Classify() == iClassIgnore) // houndeyes don't hurt other houndeyes with their attack
				continue;


			// blast's don't tavel into or out of water
			if (bInWater && entity.GetWaterLevel() == WaterLevel.NotInWater)
				continue;

			if (!bInWater && entity.GetWaterLevel() == WaterLevel.Eyes)
				continue;

			// Check that the explosion can 'see' this entity.
			vecSpot = entity.BodyTarget(vecSrc, false);
			Util.TraceLine(vecSrc, vecSpot, (Mask)MASK_RADIUS_DAMAGE, info.GetInflictor(), CollisionGroup.None, out tr);

			if (old_radius_damage.GetBool()) {
				if (tr.Fraction != 1.0 && tr.Ent != entity)
					continue;
			}
			else {
				if (tr.Fraction != 1.0) {
					if (IsExplosionTraceBlocked(ref tr)) {
						if (ShouldUseRobustRadiusDamage(entity)) {
							if (vecSpot.DistToSqr(vecSrc) > flHalfRadiusSqr) {
								// Only use robust model on a target within one-half of the explosion's radius.
								continue;
							}

							Vector3 vecToTarget = vecSpot - tr.EndPos;
							MathLib.VectorNormalize(ref vecToTarget);

							// We're going to deflect the blast along the surface that 
							// interrupted a trace from explosion to this target.
							Vector3 vecUp, vecDeflect;
							MathLib.CrossProduct(vecToTarget, tr.Plane.Normal, out vecUp);
							MathLib.CrossProduct(tr.Plane.Normal, vecUp, out vecDeflect);
							MathLib.VectorNormalize(ref vecDeflect);

							// Trace along the surface that intercepted the blast...
							Util.TraceLine(tr.EndPos, tr.EndPos + vecDeflect * ROBUST_RADIUS_PROBE_DIST, (Mask)MASK_RADIUS_DAMAGE, info.GetInflictor(), CollisionGroup.None, out tr);
							//NDebugOverlay::Line( tr.startpos, tr.endpos, 255, 255, 0, false, 10 );

							// ...to see if there's a nearby edge that the explosion would 'spill over' if the blast were fully simulated.
							Util.TraceLine(tr.EndPos, vecSpot, (Mask)MASK_RADIUS_DAMAGE, info.GetInflictor(), CollisionGroup.None, out tr);
							//NDebugOverlay::Line( tr.startpos, tr.endpos, 255, 0, 0, false, 10 );

							if (tr.Fraction != 1.0 && tr.DidHitWorld()) {
								// Still can't reach the target.
								continue;
							}
							// else fall through
						}
						else {
							continue;
						}
					}

					// UNDONE: Probably shouldn't let children block parents either?  Or maybe those guys should set their owner if they want this behavior?
					// HL2 - Dissolve damage is not reduced by interposing non-world objects
					if (tr.Ent != null && tr.Ent != entity && tr.Ent!.GetOwnerEntity() != entity) {
						// Some entity was hit by the trace, meaning the explosion does not have clear
						// line of sight to the entity that it's trying to hurt. If the world is also
						// blocking, we do no damage.
						BaseEntity blockingEntity = tr.Ent!;
						//Msg( "%s may be blocked by %s...", pEntity.GetClassname(), pBlockingEntity.GetClassname() );

						Util.TraceLine(vecSrc, vecSpot, Mask.Solid, info.GetInflictor(), CollisionGroup.None, out tr);

						if (tr.Fraction != 1.0)
							continue;

						// Now, if the interposing object is physics, block some explosion force based on its mass.
						if (blockingEntity.VPhysicsGetObject() != null) {
							const float MASS_ABSORB_ALL_DAMAGE = 350.0f;
							float flMass = blockingEntity.VPhysicsGetObject()!.GetMass();
							float scale = flMass / MASS_ABSORB_ALL_DAMAGE;

							// Absorbed all the damage.
							if (scale >= 1.0f) {
								continue;
							}

							Assert(scale > 0.0f);
							flBlockedDamagePercent = scale;
							//Msg("  Object (%s) weighing %fkg blocked %f percent of explosion damage\n", pBlockingEntity.GetClassname(), flMass, scale * 100.0f);
						}
						else {
							// Some object that's not the world and not physics. Generically block 25% damage
							flBlockedDamagePercent = 0.25f;
						}
					}
				}
			}
			// decrease damage for an ent that's farther from the bomb.
			flAdjustedDamage = (vecSrc - tr.EndPos).Length() * falloff;
			flAdjustedDamage = info.GetDamage() - flAdjustedDamage;

			if (flAdjustedDamage <= 0)
				continue;

			// the explosion can 'see' this entity, so hurt them!
			if (tr.StartSolid) {
				// if we're stuck inside them, fixup the position and distance
				tr.EndPos = vecSrc;
				tr.Fraction = 0.0f;
			}

			TakeDamageInfo adjustedInfo = info;
			//Msg("%s: Blocked damage: %f percent (in:%f  out:%f)\n", pEntity.GetClassname(), flBlockedDamagePercent * 100, flAdjustedDamage, flAdjustedDamage - (flAdjustedDamage * flBlockedDamagePercent) );
			adjustedInfo.SetDamage(flAdjustedDamage - (flAdjustedDamage * flBlockedDamagePercent));

			// Now make a consideration for skill level!
			if (info.GetAttacker() != null && info.GetAttacker()!.IsPlayer() && entity.IsNPC()) {
				// An explosion set off by the player is harming an NPC. Adjust damage accordingly.
				adjustedInfo.AdjustPlayerDamageInflictedForSkillLevel();
			}

			Vector3 dir = vecSpot - vecSrc;
			MathLib.VectorNormalize(ref dir);

			// If we don't have a damage force, manufacture one
			if (adjustedInfo.GetDamagePosition() == vec3_origin || adjustedInfo.GetDamageForce() == vec3_origin) {
				if (0 == (adjustedInfo.GetDamageType() & DamageType.PreventPhysicsForce))
					CalculateExplosiveDamageForce(ref adjustedInfo, dir, vecSrc);
			}
			else {
				// Assume the force passed in is the maximum force. Decay it based on falloff.
				float flForce = adjustedInfo.GetDamageForce().Length() * falloff;
				adjustedInfo.SetDamageForce(dir * flForce);
				adjustedInfo.SetDamagePosition(vecSrc);
			}

			if (tr.Fraction != 1.0 && entity == tr.Ent) {
				ClearMultiDamage();
				entity.DispatchTraceAttack(adjustedInfo, dir, ref tr);
				ApplyMultiDamage();
			}
			else
				entity.TakeDamage(adjustedInfo);

			// Now hit all triggers along the way that respond to damage... 
			entity.TraceAttackToTriggers(adjustedInfo, vecSrc, tr.EndPos, dir);

#if GAME_DLL
			if (info.GetAttacker() != null && info.GetAttacker()!.IsPlayer() && ToBaseCombatCharacter(tr.Ent) != null) {
				// This is a total hack!!!
				bool bIsPrimary = true;
				BasePlayer player = ToBasePlayer(info.GetAttacker())!;
				BaseCombatWeapon? weapon = player.GetActiveWeapon();
				if (weapon != null && BaseEntity.FClassnameIs(weapon, "weapon_smg1"))
					bIsPrimary = false;

				// todo: gamestats.Event_WeaponHit( player, bIsPrimary, (pWeapon != NULL) ? player.GetActiveWeapon().GetClassname() : "NULL", info );
			}
#endif
		}
	}
	// Let the game rules specify if fall death should fade screen to black
	public virtual bool FlPlayerFallDeathDoesScreenFade(BasePlayer player) => true;

	public abstract bool AllowDamage(BaseEntity victim, in TakeDamageInfo info);


	// Client spawn/respawn control
	public abstract void PlayerSpawn(BasePlayer player);// called by CBasePlayer::Spawn just before releasing player into the game
	public abstract void PlayerThink(BasePlayer player); // called by CBasePlayer::PreThink every frame, before physics are run and after keys are accepted
	public abstract bool FPlayerCanRespawn(BasePlayer player);// is this player allowed to respawn now?
	public abstract TimeUnit_t FlPlayerSpawnTime(BasePlayer player);// When in the future will this player be able to spawn?
	public virtual BaseEntity? GetPlayerSpawnSpot(BasePlayer player) {
		BaseEntity? spawnSpot = player.EntSelectSpawnPoint();
		Assert(spawnSpot != null);

		if (spawnSpot != null) {
			player.SetLocalOrigin(spawnSpot.GetAbsOrigin() + new Vector3(0, 0, 1));
			player.SetAbsVelocity(vec3_origin);
			player.SetLocalAngles(spawnSpot.GetLocalAngles());
			player.Local.PunchAngle = vec3_angle;
			player.Local.PunchAngleVel = vec3_angle;
			player.SnapEyeAngles(spawnSpot.GetLocalAngles());
		}

		return spawnSpot;
	}
	public virtual bool IsSpawnPointValid(BaseEntity spot, BasePlayer player) {
		// todo
		return true;
	}

	public virtual bool AllowAutoTargetCrosshair() => true;
	public virtual bool ClientCommand(BaseEntity edict, in TokenizedCommand args) {
#if GAME_DLL
		if (edict.IsPlayer()) {
			if (GetVoiceGameMgr().ClientCommand((BasePlayer)edict, args))
				return true;
		}
#endif

		return false;
	}
	public virtual void ClientSettingsChanged(BasePlayer player) {

	}

	// Client kills/scoring
	public abstract int IPointsForKill(BasePlayer attacker, BasePlayer killed);// how many points do I award whoever kills this player?
	public abstract void PlayerKilled(BasePlayer victim, in TakeDamageInfo info);// Called each time a player dies
	public abstract void DeathNotice(BasePlayer victim, in TakeDamageInfo info);// Call this from within a GameRules class to report an obituary.
	public virtual ReadOnlySpan<char> GetDamageCustomString(in TakeDamageInfo info) => null;

	// Weapon Damage
	// Determines how much damage Player's attacks inflict, based on skill level.
	public virtual float AdjustPlayerDamageInflicted(float damage) { return damage; }
	public virtual void AdjustPlayerDamageTaken(ref TakeDamageInfo info) { } // Base class does nothing.

	// Weapon retrieval
	public virtual bool CanHavePlayerItem(BasePlayer player, BaseCombatWeapon weapon) {
		return true;
	}

	// Weapon spawn/respawn control
	public abstract GameRulesRespawnReturnCode WeaponShouldRespawn(BaseCombatWeapon weapon);// should this weapon respawn?
	public abstract TimeUnit_t FlWeaponRespawnTime(BaseCombatWeapon weapon);// when may this weapon respawn?
	public abstract TimeUnit_t FlWeaponTryRespawn(BaseCombatWeapon? weapon); // can i respawn now,  and if not, when should i try again?
	public abstract Vector3 VecWeaponRespawnSpot(BaseCombatWeapon weapon);// where in the world should this weapon respawn?

	// Item retrieval
	public abstract bool CanHaveItem(BasePlayer player, Item item);// is this player allowed to take this item?
	public abstract void PlayerGotItem(BasePlayer player, Item item);// call each time a player picks up an item (battery, healthkit)

	// Item spawn/respawn control
	public abstract GameRulesRespawnReturnCode ItemShouldRespawn(Item item);// Should this item respawn?
	public abstract TimeUnit_t FlItemRespawnTime(Item item);// when may this item respawn?
	public abstract Vector3 VecItemRespawnSpot(Item item);// where in the world should this item respawn?
	public abstract QAngle VecItemRespawnAngles(Item item);// what angles should this item use when respawing?

	// Ammo retrieval
	public virtual bool CanHaveAmmo(BaseCombatCharacter? player, int ammoIndex) {
		if (ammoIndex > -1) {
			// Get the max carrying capacity for this ammo
			int iMaxCarry = GetAmmoDef().MaxCarry(ammoIndex);

			// Does the player have room for more of this type of ammo?
			if (player.GetAmmoCount(ammoIndex) < iMaxCarry)
				return true;
		}

		return false;
	}
	public virtual bool CanHaveAmmo(BaseCombatCharacter? player, ReadOnlySpan<char> name) {
		return CanHaveAmmo(player, GetAmmoDef().Index(name));
	}
	public abstract void PlayerGotAmmo(BaseCombatCharacter player, Span<char> name);// called each time a player picks up some ammo in the world
	public virtual float GetAmmoQuantityScale(int i) => 1.0f;

	// AI Definitions
	public virtual void InitDefaultAIRelationships() { }
	public virtual ReadOnlySpan<char> AIClassText(int i) => null;

	// Healthcharger respawn control
	public abstract TimeUnit_t FlHealthChargerRechargeTime(); // how long until a depleted HealthCharger recharges itself?
	public virtual TimeUnit_t FlHEVChargerRechargeTime() => 0;// how long until a depleted HealthCharger recharges itself?

	// What happens to a dead player's weapons
	public abstract GameRulesRespawnReturnCode DeadPlayerWeapons(BasePlayer player);// what do I do with a player's weapons when he's killed?

	// What happens to a dead player's ammo	
	public abstract GameRulesRespawnReturnCode DeadPlayerAmmo(BasePlayer player);// Do I drop ammo when the player dies? How much?

	// Teamplay stuff
	public abstract ReadOnlySpan<char> GetTeamID(BaseEntity entity);// what team is this entity on?
	public abstract GameRulesPlayerRelationship PlayerRelationship(BaseEntity? player, BaseEntity? target);// What is the player's relationship with this entity?
	public abstract bool PlayerCanHearChat(BasePlayer? listener, BasePlayer speaker);
	public virtual void CheckChatText(BasePlayer ply, Span<char> chat) { return; }

	public virtual int GetTeamIndex(ReadOnlySpan<char> teamName) => -1;
	public virtual ReadOnlySpan<char> GetIndexedTeamName(int teamIndex) => "";
	public virtual bool IsValidTeam(ReadOnlySpan<char> teamName) => true;
	public virtual void ChangePlayerTeam(BasePlayer player, ReadOnlySpan<char> teamName, bool kill, bool gib) { }
	public virtual ReadOnlySpan<char> SetDefaultPlayerTeam(BasePlayer player) => "";
	public virtual void UpdateClientData(BasePlayer player) { }

	// Sounds
	public virtual bool PlayTextureSounds() => true;
	public virtual bool PlayFootstepSounds(BasePlayer player) => true;

	// NPCs
	public abstract bool FAllowNPCs();//are NPCs allowed

	// Immediately end a multiplayer game
	public virtual void EndMultiplayerGame() { }

	// trace line rules
	public virtual float WeaponTraceEntity(BaseEntity entity, in Vector3 vecStart, in Vector3 vecEnd, Mask mask, out GameTrace ptr) {
		Util.TraceEntity(entity, vecStart, vecEnd, mask, out ptr);
		return 1.0f;
	}

	// Setup g_pPlayerResource (some mods use a different entity type here).
	public virtual void CreateStandardEntities() {
		g_pPlayerResource = (PlayerResource)BaseEntity.Create("player_manager", vec3_origin, vec3_angle)!;
		g_pPlayerResource.AddEFlags(EFL.KeepOnRecreateEntities);
	}

	// Team name, etc shown in chat and dedicated server console
	public virtual ReadOnlySpan<char> GetChatPrefix(bool teamOnly, BasePlayer? player) {
		if (player != null && player.IsAlive() == false) {
			if (teamOnly)
				return "*DEAD*(TEAM)";
			else
				return "*DEAD*";
		}

		return "";
	}

	// Location name shown in chat
	public virtual ReadOnlySpan<char> GetChatLocation(bool teamOnly, BasePlayer player) => null;

	// VGUI format string for chat, if desired
	public virtual ReadOnlySpan<char> GetChatFormat(bool teamOnly, BasePlayer player) => null;

	// Whether props that are on fire should get a DLIGHT.
	public virtual bool ShouldBurningPropsEmitLight() => false;

	public virtual bool CanEntityBeUsePushed(BaseEntity ent) => true;

	public virtual void CreateCustomNetworkStringTables() { }

	// Game Achievements (server version)
	public virtual void MarkAchievement<IRF>(in IRF filter, ReadOnlySpan<char> achievementName) where IRF : IRecipientFilter {
		throw new NotImplementedException();
	}

	public virtual void ResetMapCycleTimeStamp() { }

	public virtual void OnNavMeshLoad() { }

	// game-specific factories
	// ? public virtual TacticalMissionManager *TacticalMissionManagerFactory();

	public virtual void ProcessVerboseLogOutput() { }

#endif

	public virtual ReadOnlySpan<char> GetGameTypeName() { return null; }
	public virtual int GetGameType() { return 0; }
	public virtual bool ShouldDrawHeadLabels() { return true; }
	public virtual void ClientSpawned(Edict edict) { return; }
	public virtual void OnFileReceived(ReadOnlySpan<char> fileName, uint transferID) { return; }
	public virtual bool IsHolidayActive(int holiday) { return false; }

	public virtual bool IsManualMapChangeOkay(out ReadOnlySpan<char> reason) { reason = default; return true; }
}
#endif
