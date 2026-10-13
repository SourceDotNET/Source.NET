namespace Game.Server;

// All of them used to by dynamically allocated via GetInteractionID,
// to simplify things its now a fixed identifier, nothing stores them and dynamically
// allocating has no advantage.
public enum AI_Interaction
{
	None = 0,

	AntlionFoundTarget,
	AntlionFiredAtTarget,
	AntlionKilled,

	AntlionGuardFoundPhysicsObject,
	AntlionGuardShovedPhysicsObject,

	BarnacleVictimDangle,
	BarnacleVictimReleased,
	BarnacleVictimGrab,
	BarnacleVictimBite,

	BullsquidThrow,

	CombineBash,
	CombineRequestCover,

	HoundeyeGroupAttack,
	HoundeyeGroupRetreat,
	HoundeyeGroupRalley,

	HunterFoundEnemy,

	ScannerInspect,
	ScannerInspectBegin,
	ScannerInspectDone,
	ScannerInspectHandsUp,
	ScannerInspectShowArmband,
	ScannerSupportEntity,
	ScannerSupportPosition,

	MetrocopPointed,
	MetrocopStartedStitch,
	MetrocopIdleChatter,
	MetrocopClearSentenceQueues,

	ScriptedTarget,

	StalkerBurn,

	VortigauntStomp,
	VortigauntStompFail,
	VortigauntStompHit,
	VortigauntKick,
	VortigauntClaw,

	TurretStillStanding,

	PlayerLaunchedRPG,

	HitByPlayerThrownPhysObj,
	PlayerPuntedHeavyObject,

	ZombieMeleeWarning,
}
