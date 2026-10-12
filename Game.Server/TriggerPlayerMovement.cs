using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;

using FIELD = FIELD<TriggerPlayerMovement>;
[LinkEntityToClass("trigger_playermovement")]
[NetworkName("CTriggerPlayerMovement")]
public class TriggerPlayerMovement : BaseTrigger
{
	public static readonly SendTable DT_TriggerPlayerMovement = new(DT_BaseTrigger, []);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_TriggerPlayerMovement);

	[Flags]
	public enum SF
	{
		MoveAutoDisable = 0x80,      // disable auto movement
		AutoDuck = 0x800,    // Duck automatically
	}

	//-----------------------------------------------------------------------------
	// Purpose: Called when spawning, after keyvalues have been handled.
	//-----------------------------------------------------------------------------
	public override void Spawn() {
		if (HasSpawnFlags(BaseTrigger.SF.OnlyPlayerAllyNPCs)) {
			// @Note (toml 01-07-04): fix up spawn flag collision coding error. Remove at some point once all maps fixed up please!
			DevMsg("*** trigger_playermovement using obsolete spawnflag. Remove and reset with new value for \"Disable auto player movement\"\n");
			RemoveSpawnFlags(BaseTrigger.SF.OnlyPlayerAllyNPCs);
			AddSpawnFlags(SF.MoveAutoDisable);
		}
		base.Spawn();

		InitTrigger();
	}

	// UNDONE: This will not support a player touching more than one of these
	// UNDONE: Do we care?  If so, ref count automovement in the player?
	public override void StartTouch(BaseEntity? other) {
		if (other == null || !PassesTriggerFilters(other))
			return;

		BasePlayer? player = ToBasePlayer(other);

		if (player == null)
			return;

		if (HasSpawnFlags(SF.AutoDuck))
			player.ForceButtons(InButtons.Duck);

		// UNDONE: Currently this is the only operation this trigger can do
		if (HasSpawnFlags(SF.MoveAutoDisable))
			player.Local.AllowAutoMovement = false;
	}

	public override void EndTouch(BaseEntity? other) {
		if (other == null || !PassesTriggerFilters(other))
			return;

		BasePlayer? player = ToBasePlayer(other);

		if (player == null)
			return;

		if (HasSpawnFlags(SF.AutoDuck))
			player.UnforceButtons(InButtons.Duck);

		if (HasSpawnFlags(SF.MoveAutoDisable))
			player.Local.AllowAutoMovement = true;
	}
}
