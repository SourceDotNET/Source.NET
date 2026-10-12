using Game.Shared;

using Source;
using Source.Common;

namespace Game.Server;

using DEFINE = Source.DEFINE<LogicAuto>;

[LinkEntityToClass("logic_auto")]
class LogicAuto : BaseEntity
{
	const int SF_AUTO_FIREONCE = 0x01;

	OutputEvent OnMapSpawn = new();
	OutputEvent OnNewGame = new();
	OutputEvent OnLoadGame = new();
	OutputEvent OnMapTransition = new();
	OutputEvent OnBackgroundMap = new();
	OutputEvent OnMultiNewMap = new();
	OutputEvent OnMultiNewRound = new();

	string? globalstate;

	public static readonly new DataMap DataDesc = new(typeof(LogicAuto), BaseEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(globalstate), FieldType.String, "globalstate"),
		DEFINE.OUTPUT(nameof(OnMapSpawn), "OnMapSpawn", eventFuncs),
		DEFINE.OUTPUT(nameof(OnNewGame), "OnNewGame", eventFuncs),
		DEFINE.OUTPUT(nameof(OnLoadGame), "OnLoadGame", eventFuncs),
		DEFINE.OUTPUT(nameof(OnMapTransition), "OnMapTransition", eventFuncs),
		DEFINE.OUTPUT(nameof(OnBackgroundMap), "OnBackgroundMap", eventFuncs),
		DEFINE.OUTPUT(nameof(OnMultiNewMap), "OnMultiNewMap", eventFuncs),
		DEFINE.OUTPUT(nameof(OnMultiNewRound), "OnMultiNewRound", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public override void Activate() {
		base.Activate();
		SetNextThink(gpGlobals.CurTime + 0.2f);
	}

	public override void Think() {
		if (globalstate == null || GlobalEntity.GetState(globalstate) == GlobalEState.On) {
			if (gpGlobals.LoadType == MapLoadType.Transition)
				OnMapTransition.FireOutput(null, this);
			else if (gpGlobals.LoadType == MapLoadType.NewGame)
				OnNewGame.FireOutput(null, this);
			else if (gpGlobals.LoadType == MapLoadType.LoadGame)
				OnLoadGame.FireOutput(null, this);
			else if (gpGlobals.LoadType == MapLoadType.Background)
				OnBackgroundMap.FireOutput(null, this);

			OnMapSpawn.FireOutput(null, this);

			if (g_pGameRules.IsMultiplayer()) {
				if (g_pGameRules.InRoundRestart())
					OnMultiNewRound.FireOutput(null, this);
				else
					OnMultiNewMap.FireOutput(null, this);
			}

			if (HasSpawnFlags(SF_AUTO_FIREONCE))
				Util.Remove(this);
		}
	}
}
