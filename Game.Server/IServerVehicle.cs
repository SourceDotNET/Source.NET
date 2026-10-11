using Game.Shared;

using Source.Common.Mathematics;

using System.Numerics;

using static Source.Common.Networking.SVC_ClassInfo;

namespace Game.Server;

public interface IServerVehicle : IVehicle
{
	BaseEntity? GetVehicleEnt();

	// Get and set the current driver. Use PassengerRole_t enum in shareddefs.h for adding passengers
	void SetPassenger(PassengerRole role, BaseCombatCharacter? passenger);

	// Is the player visible while in the vehicle? (this is a constant the vehicle)
	bool IsPassengerVisible(PassengerRole role = PassengerRole.Driver);

	// Can a given passenger take damage?
	bool IsPassengerDamagable(PassengerRole role = PassengerRole.Driver);
	// bool PassengerShouldReceiveDamage(ref TakeDamageInfo info );

	// Is the vehicle upright?
	bool IsVehicleUpright();

	// Whether or not we're in a transitional phase
	bool IsPassengerEntering();
	bool IsPassengerExiting();

	// Get a position in *world space* inside the vehicle for the player to start at
	void GetPassengerSeatPoint(PassengerRole role, out Vector3 point, out QAngle angles);

	void HandlePassengerEntry(BaseCombatCharacter? passenger, bool allowEntryOutsideZone = false);
	bool HandlePassengerExit(BaseCombatCharacter? passenger);

	// Get a point in *world space* to leave the vehicle from (may be in solid)
	bool GetPassengerExitPoint(PassengerRole nRole, out Vector3 point, out QAngle angles);
	int GetEntryAnimForPoint(out Vector3 point);
	int GetExitAnimToUse(out Vector3 eyeExitEndpoint, out bool allPointsBlocked);
	void HandleEntryExitFinish(bool exitAnimOn, bool resetAnim);
	Class_T ClassifyPassenger(BaseCombatCharacter? passenger, Class_T defaultClassification);
	// float PassengerDamageModifier(in TakeDamageInfo info );
	// const vehicleparams_t* GetVehicleParams();
	// IPhysicsVehicleController GetVehicleController();
	// int NPC_GetAvailableSeat(BaseCombatCharacter? passenger, ReadOnlySpan<char> strRoleName, VehicleSeatQuery_e nQueryType);
	bool NPC_AddPassenger(BaseCombatCharacter? passenger, ReadOnlySpan<char> roleName, int seat);
	bool NPC_RemovePassenger(BaseCombatCharacter? passenger);
	bool NPC_GetPassengerSeatPosition(BaseCombatCharacter? passenger, out Vector3 resultPos, out QAngle resultAngle);
	bool NPC_GetPassengerSeatPositionLocal(BaseCombatCharacter? passenger, out Vector3 resultPos, out QAngle resultAngle);
	int NPC_GetPassengerSeatAttachment(BaseCombatCharacter? passenger);
	bool NPC_HasAvailableSeat(ReadOnlySpan<char> roleName);
	// const PassengerSeatAnims_t* NPC_GetPassengerSeatAnims( BaseCombatCharacter* passenger, PassengerSeatAnimType_t nType );
	BaseCombatCharacter? NPC_GetPassengerInSeat(PassengerRole role, int seatID);
	void RestorePassengerInfo();
	bool NPC_CanDrive();
	// void NPC_SetDriver(NPC_VehicleDriver? driver);
	void NPC_DriveVehicle();
	void NPC_ThrottleCenter();
	void NPC_ThrottleReverse();
	void NPC_ThrottleForward();
	void NPC_Brake();
	void NPC_TurnLeft(float degrees);
	void NPC_TurnRight(float degrees);
	void NPC_TurnCenter();
	void NPC_PrimaryFire();
	void NPC_SecondaryFire();
	bool NPC_HasPrimaryWeapon();
	bool NPC_HasSecondaryWeapon();
	void NPC_AimPrimaryWeapon(Vector3 target);
	void NPC_AimSecondaryWeapon(Vector3 target);
	void Weapon_PrimaryRanges(out float minRange, out float maxRange);
	void Weapon_SecondaryRanges(out float minRange, out float maxRange);
	float Weapon_PrimaryCanFireAt();
	float Weapon_SecondaryCanFireAt();
	void ReloadScript();
}
