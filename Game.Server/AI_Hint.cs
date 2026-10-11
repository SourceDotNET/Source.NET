namespace Game.Server;

public enum Hint : short
{
	Any = -1,
	None = 0,
	NotUsedWorldDoor,
	WorldWindow,
	NotUsedWorldButton,
	NotUsedWorldMachinery,
	NotUsedWorldLedge,
	NotUsedWorldLightSource,
	NotUsedWorldHeatSource,
	NotUsedWorldBlinkingLight,
	NotUsedWorldBrightColors,
	NotUsedWorldHumanBlood,
	NotUsedWorldAlienBlood,

	WorldWorkPosition,
	WorldVisuallyInteresting,
	WorldVisuallyInterestingDontAim,
	WorldInhibitCombineMines,
	WorldVisuallyInterestingStealth,

	TacticalCoverMed = 100,
	TacticalCoverLow,
	TacticalSpawn,
	TacticalPinch,
	NotUsedTacticalGuard,
	TacticalEnemyDisadvantaged,
	NotUsedHealthKit,

	NotUsedUrbanStreetcorner = 200,
	NotUsedUrbanStreetlamp,
	NotUsedUrbanDarkSpot,
	NotUsedUrbanPoster,
	NotUsedUrbanShelter,

	NotUsedAssassinSecluded = 300,
	NotUsedAssassinRafters,
	NotUsedAssassinGround,
	NotUsedAssassinMonkeybars,

	AntlionBurrowPoint = 400,
	AntlionThumperFleePoint,

	HeadcrabBurrowPoint = 450,
	HeadcrabExitPodPoint,

	NotUsedRollerPatrolPoint = 500,
	NotUsedRollerCleanupPoint,

	NotUsedPstormRockSpawn = 600,

	CrowFlytoPoint = 700,

	BugPatrolPoint = 800,

	FollowWaitPoint = 900,
	JumpOverride = 901,
	PlayerSquadTransitonPoint = 902,
	NpcExitPoint = 903,
	StriderNode = 904,

	PlayerAllyMoveAwayDest = 950,
	PlayerAllyFearDest,

	HL1WorldMachinery = 1000,
	HL1WorldBlinkingLight,
	HL1WorldHumanBlood,
	HL1WorldAlienBlood,

	CStrikeHostageEscape = 1100,
}

public class AI_Hint : ServerOnlyEntity
{
	public const int SF_ALLOW_JUMP_UP = 65536;

	public Hint HintType() => throw new NotImplementedException();
	public string? GetGroup() => throw new NotImplementedException();
	public int GetTargetNode() => throw new NotImplementedException();
	public void Unlock(float delay) => throw new NotImplementedException();
}

[Flags]
public enum AI_HintNodeFlags {
	None = 0x00000000,
	Visible = 0x00000001,
	Nearest = 0x00000002,
	Random = 0x00000004
}

public static class AI_HintManager
{
	public static AI_HintNodeFlags GetFlags(ReadOnlySpan<char> token) {
		if (token.Length <= 0)
			return AI_HintNodeFlags.None;

		string lowercase = new string(token).ToLowerInvariant();

		if ("none".Contains(lowercase))
			return AI_HintNodeFlags.None;

		AI_HintNodeFlags bits = 0;

		if ("visible".Contains(lowercase))
			bits |= AI_HintNodeFlags.Visible;

		if ("nearest".Contains(lowercase))
			bits |= AI_HintNodeFlags.Nearest;

		if ("random".Contains(lowercase))
			bits |= AI_HintNodeFlags.Random;

		if ((bits & AI_HintNodeFlags.Nearest) != 0 &&
			 (bits & AI_HintNodeFlags.Random) != 0) {
			bits &= ~AI_HintNodeFlags.Random;

			DevMsg($"HINTFLAGS:{token}, inconsistent, the nearest node is never a random hint node, treating as nearest request!\n");
		}

		return bits;
	}
}
