using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;

using System.Numerics;

namespace Game.Server;

public struct SoundscapeUpdate
{
	public BasePlayer? Player;
	public EnvSoundscape? CurrentSoundscape;
	public Vector3 PlayerPosition;
	public float CurrentDistance;
	public int TraceCount;
	public bool InRange;
}

// ----------------------------------------------------------------------------- //
// EnvSoundscape stuff.
// ----------------------------------------------------------------------------- //

[LinkEntityToClass("env_soundscape")]
[LinkEntityToClass("env_sound")]
public class EnvSoundscape : PointEntity
{
	public static readonly ConVar soundscape_debug = new("soundscape_debug", "0", FCvar.Cheat, "When on, draws lines to all env_soundscape entities. Green lines show the active soundscape, red lines show soundscapes that aren't in range, and white lines show soundscapes that are in range, but not the active soundscape.");

	public OutputEvent OnPlay = new();
	public float Radius;
	public string? SoundscapeName;
	public int SoundscapeIndex;
	public int SoundscapeEntityId;
	public InlineArrayNumLocalAudioSounds<string?> PositionNames;

	// If this is set, then this soundscape ignores all its parameters and uses
	// those of this soundscape.
	public Handle<EnvSoundscape> ProxySoundscape = new();

	bool Disabled;

	public static readonly new DataMap DataDesc = new(typeof(EnvSoundscape), PointEntity.DataDesc, [
		DEFINE<EnvSoundscape>.KEYFIELD(nameof(Radius), FieldType.Float, "radius"),
		// don't save, recomputed on load
		//DEFINE_FIELD( m_soundscapeIndex, FIELD_INTEGER ),
		DEFINE<EnvSoundscape>.FIELD(nameof(SoundscapeName), FieldType.String),
		DEFINE<EnvSoundscape>.FIELD(nameof(ProxySoundscape), FieldType.EHandle),

		DEFINE<EnvSoundscape>.KEYFIELD(nameof(Disabled), FieldType.Boolean, "StartDisabled"),

		DEFINE<EnvSoundscape>.INPUTFUNC(FieldType.Void, "Enable", nameof(InputEnable), (INPUTFUNCPTR)((self, data) => ((EnvSoundscape)self).InputEnable(data))),
		DEFINE<EnvSoundscape>.INPUTFUNC(FieldType.Void, "Disable", nameof(InputDisable), (INPUTFUNCPTR)((self, data) => ((EnvSoundscape)self).InputDisable(data))),
		DEFINE<EnvSoundscape>.INPUTFUNC(FieldType.Void, "ToggleEnabled", nameof(InputToggleEnabled), (INPUTFUNCPTR)((self, data) => ((EnvSoundscape)self).InputToggleEnabled(data))),

		DEFINE<EnvSoundscape>.OUTPUT(nameof(OnPlay), "OnPlay", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public EnvSoundscape() {
		SoundscapeName = null;
		SoundscapeIndex = -1;
		SoundscapeEntityId = -1;
		Disabled = false;
		g_SoundscapeSystem.AddSoundscapeEntity(this);
	}

	public override void UpdateOnRemove() {
		g_SoundscapeSystem.RemoveSoundscapeEntity(this);
		base.UpdateOnRemove();
	}

	public void InputEnable(InputData inputdata) {
		if (!IsEnabled())
			Enable();
	}

	public void InputDisable(InputData inputdata) {
		if (IsEnabled())
			Disable();
	}

	public void InputToggleEnabled(InputData inputdata) {
		if (IsEnabled())
			Disable();
		else
			Enable();
	}

	//-----------------------------------------------------------------------------
	// Purpose: Returns whether the laser is currently active.
	//-----------------------------------------------------------------------------
	bool IsEnabled() {
		return !Disabled;
	}

	//-----------------------------------------------------------------------------
	// Purpose:
	//-----------------------------------------------------------------------------
	void Disable() {
		Disabled = true;

		// Reset if we are the currently active soundscape
	}

	//-----------------------------------------------------------------------------
	// Purpose:
	//-----------------------------------------------------------------------------
	void Enable() {
		Disabled = false;

		// Force the player to recheck soundscapes
	}

	public ReadOnlySpan<char> GetSoundscapeName() => SoundscapeName;

	public override bool KeyValue(ReadOnlySpan<char> keyName, ReadOnlySpan<char> value) {
		if (FStrEq(keyName, "soundscape"))
			SoundscapeName = new(value.SliceNullTerminatedString());
		else if (keyName.StartsWith("position", StringComparison.OrdinalIgnoreCase) && keyName.Length == 9 && char.IsDigit(keyName[8]) && keyName[8] - '0' < NUM_AUDIO_LOCAL_SOUNDS)
			PositionNames[keyName[8] - '0'] = new(value.SliceNullTerminatedString());
		else
			return base.KeyValue(keyName, value);

		return true;
	}

	// returns true if the given sound entity is in range
	// and can see the given player entity (pTarget)

	public bool InRangeOfPlayer(BasePlayer target) {
		Vector3 vecSpot1 = EarPosition();
		Vector3 vecSpot2 = target.EarPosition();

		// calc range from sound entity to player
		Vector3 vecRange = vecSpot2 - vecSpot1;
		float range = vecRange.Length();
		if (Radius > range || Radius == -1) {
			Util.TraceLine(vecSpot1, vecSpot2, Mask.SolidBrushOnly | Mask.Water, target, Source.CollisionGroup.None, out Trace tr);

			if (tr.Fraction == 1 && !tr.StartSolid)
				return true;
		}

		return false;
	}

	public override EdictFlags UpdateTransmitState() {
		// Always transmit all soundscapes to the player.
		return SetTransmitState(EdictFlags.Always);
	}

	public void WriteAudioParamsTo(ref AudioParams audio) {
		audio.Ent.Set(this);
		audio.SoundscapeIndex = SoundscapeIndex;
		audio.LocalBits = 0;
		for (int i = 0; i < NUM_AUDIO_LOCAL_SOUNDS; i++) {
			if (PositionNames[i] != null) {
				// We are a valid entity for a sound position
				BaseEntity? entity = gEntList.FindEntityByName(null, PositionNames[i], this, this);
				if (entity != null) {
					audio.LocalBits |= 1 << i;
					audio.LocalSound[i] = entity.GetAbsOrigin();
				}
			}
		}

		OnPlay.FireOutput(this, this);
	}

	//
	// A client that is visible and in range of a sound entity will
	// have its soundscape set by that sound entity.  If two or more
	// sound entities are contending for a client, then the nearest
	// sound entity to the client will set the client's soundscape.
	// A client's soundscape will remain set to its prior value until
	// a new in-range, visible sound entity resets a new soundscape.
	//

	// CONSIDER: if player in water state, autoset and underwater soundscape?
	public void UpdateForPlayer(ref SoundscapeUpdate update) {
		if (!IsEnabled()) {
			if (update.CurrentSoundscape == this) {
				update.CurrentSoundscape = null;
				update.CurrentDistance = 0;
				update.InRange = false;
			}
			return;
		}

		// calc range from sound entity to player
		Vector3 target = EarPosition();
		float range = (update.PlayerPosition - target).Length();

		if (update.CurrentSoundscape == this) {
			update.CurrentDistance = range;
			update.InRange = false;
			if (Radius > range || Radius == -1) {
				update.TraceCount++;
				Util.TraceLine(target, update.PlayerPosition, Mask.SolidBrushOnly | Mask.Water, update.Player, Source.CollisionGroup.None, out Trace tr);
				if (tr.Fraction == 1 && !tr.StartSolid)
					update.InRange = true;
			}
		}
		else {
			if ((!update.InRange || range < update.CurrentDistance) && (Radius > range || Radius == -1)) {
				update.TraceCount++;
				Util.TraceLine(target, update.PlayerPosition, Mask.SolidBrushOnly | Mask.Water, update.Player, Source.CollisionGroup.None, out Trace tr);

				if (tr.Fraction == 1 && !tr.StartSolid) {
					ref AudioParams audio = ref update.Player!.GetAudioParams();
					WriteAudioParamsTo(ref audio);
					update.CurrentSoundscape = this;
					update.InRange = true;
					update.CurrentDistance = range;
				}
			}
		}


		if (soundscape_debug.GetBool()) {
			float persist = (float)IVDebugOverlay.NDEBUG_PERSIST_TILL_NEXT_SERVER;
			// draw myself
			DebugOverlay.Box(GetAbsOrigin(), new(-10, -10, -10), new(10, 10, 10), 255, 0, 255, 64, persist);

			if (update.Player != null) {
				ref AudioParams audio = ref update.Player.GetAudioParams();
				if (audio.Ent.Get() != this) {
					if (InRangeOfPlayer(update.Player))
						DebugOverlay.Line(GetAbsOrigin(), update.Player.WorldSpaceCenter(), 255, 255, 255, true, persist);
					else
						DebugOverlay.Line(GetAbsOrigin(), update.Player.WorldSpaceCenter(), 255, 0, 0, true, persist);
				}
				else {
					if (InRangeOfPlayer(update.Player))
						DebugOverlay.Line(GetAbsOrigin(), update.Player.WorldSpaceCenter(), 0, 255, 0, true, persist);
					else
						DebugOverlay.Line(GetAbsOrigin(), update.Player.WorldSpaceCenter(), 255, 170, 0, true, persist);

					// also draw lines to each sound position.
					// we don't store the number of local sound positions, just a bitvector of which ones are on.
					uint soundbits = (uint)audio.LocalBits;
					float periodic = 2.0f * MathF.Sin(((float)(gpGlobals.CurTime % 2.0) - 1.0f) * MathF.PI); // = -4f .. 4f
					for (int ii = 0; ii < NUM_AUDIO_LOCAL_SOUNDS; ++ii) {
						if ((soundbits & (1 << ii)) != 0) {
							Vector3 soundLoc = audio.LocalSound[ii];
							DebugOverlay.Line(GetAbsOrigin(), soundLoc, 0, 32, 255, false, persist);
							DebugOverlay.Cross3D(soundLoc, 16.0f + periodic, 0, 0, 255, false, persist);
						}
					}
				}
			}

			DebugOverlay.EntityTextAtPosition(GetAbsOrigin(), 0, SoundscapeName, persist, 255, 255, 255, 255);
		}
	}

	//
	// env_soundscape - spawn a sound entity that will set player soundscape
	// when player moves in range and sight.
	//
	//
	public override void Spawn() {
		Precache();
		// Because the soundscape has no model, need to make sure it doesn't get culled from the PVS for this reason and therefore
		//  never exist on the client, etc.
		AddEFlags(EFL.ForceCheckTransmit);
	}

	public override void Precache() {
		if (SoundscapeName == null) {
			DevMsg("Found soundscape entity with no soundscape name.\n");
			return;
		}

		SoundscapeIndex = g_SoundscapeSystem.GetSoundscapeIndex(SoundscapeName);
		if (!g_SoundscapeSystem.IsValidIndex(SoundscapeIndex))
			DevWarning($"Can't find soundscape: {SoundscapeName}\n");
	}
}

// ----------------------------------------------------------------------------- //
// EnvSoundscapeProxy stuff.
// ----------------------------------------------------------------------------- //

[LinkEntityToClass("env_soundscape_proxy")]
public class EnvSoundscapeProxy : EnvSoundscape
{
	string? MainSoundscapeName;

	public static readonly new DataMap DataDesc = new(typeof(EnvSoundscapeProxy), EnvSoundscape.DataDesc, [
		DEFINE<EnvSoundscapeProxy>.KEYFIELD(nameof(MainSoundscapeName), FieldType.String, "MainSoundscapeName")
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public EnvSoundscapeProxy() {
		MainSoundscapeName = null;
	}

	public override void Activate() {
		if (MainSoundscapeName != null) {
			BaseEntity? entity = gEntList.FindEntityByName(null, MainSoundscapeName);
			if (entity != null)
				ProxySoundscape.Set(entity as EnvSoundscape);
		}

		EnvSoundscape? proxySoundscape = ProxySoundscape.Get();
		if (proxySoundscape != null) {
			// Copy the relevant parameters from our main soundscape.
			SoundscapeIndex = proxySoundscape.SoundscapeIndex;
			for (int i = 0; i < NUM_AUDIO_LOCAL_SOUNDS; i++)
				PositionNames[i] = proxySoundscape.PositionNames[i];
		}
		else
			Warning($"env_soundscape_proxy can't find target soundscape: '{MainSoundscapeName}'\n");

		base.Activate();
	}

	// Here just to stop it falling back to EnvSoundscape's, and
	// printing bogus errors about missing soundscapes.
	public override void Precache() { return; }
}

// ---------------------------------------------------------------------------------------------------- //
// EnvSoundscapeTriggerable
// ---------------------------------------------------------------------------------------------------- //

[LinkEntityToClass("env_soundscape_triggerable")]
public class EnvSoundscapeTriggerable : EnvSoundscape
{
	public static readonly new DataMap DataDesc = new(typeof(EnvSoundscapeTriggerable), EnvSoundscape.DataDesc, []);
	public override DataMap? GetDataDescMap() => DataDesc;

	// Passed through from TriggerSoundscape.
	public void DelegateStartTouch(BaseEntity ent) {
		if (ent is not BasePlayer player)
			return;

		// Just in case.. we shouldn't already be in the player's list because it should have
		// called DelegateEndTouch, but this seems to happen when they're noclipping.
		player.TriggerSoundscapeList.Remove(GetRefEHandle());

		// Add us to the player's list of soundscapes and
		player.TriggerSoundscapeList.Insert(0, GetRefEHandle());
		WriteAudioParamsTo(ref player.GetAudioParams());
	}

	public void DelegateEndTouch(BaseEntity ent) {
		if (ent is not BasePlayer player)
			return;

		// Remove us from the ent's list of soundscapes.
		player.TriggerSoundscapeList.Remove(GetRefEHandle());
		while (player.TriggerSoundscapeList.Count > 0) {
			if (player.TriggerSoundscapeList[0].Get() is EnvSoundscapeTriggerable ss) {
				// Make this one current.
				ss.WriteAudioParamsTo(ref player.GetAudioParams());
				return;
			}
			else
				player.TriggerSoundscapeList.RemoveAt(0);
		}

		// No soundscapes left.
		player.GetAudioParams().Ent.Set(null);
	}

	// Overrides the base class's think and prevents it from running at all.
	public override void Think() {
	}
}

// ---------------------------------------------------------------------------------------------------- //
// TriggerSoundscape
// ---------------------------------------------------------------------------------------------------- //
[LinkEntityToClass("trigger_soundscape")]
public class TriggerSoundscape : BaseTrigger
{
	Handle<EnvSoundscapeTriggerable> Soundscape = new();
	string? SoundscapeName;

	List<Handle<BasePlayer>> Spectators = []; // spectators in our volume

	public static readonly new DataMap DataDesc = new(typeof(TriggerSoundscape), BaseTrigger.DataDesc, [
		DEFINE<TriggerSoundscape>.THINKFUNC(nameof(PlayerUpdateThink)),
		DEFINE<TriggerSoundscape>.KEYFIELD(nameof(SoundscapeName), FieldType.String, "soundscape"),
		DEFINE<TriggerSoundscape>.FIELD(nameof(Soundscape), FieldType.EHandle),
		DEFINE<TriggerSoundscape>.UTLVECTOR(nameof(Spectators), FieldType.EHandle),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public override void StartTouch(BaseEntity? other) {
		if (Soundscape.Get() != null)
			Soundscape.Get()!.DelegateStartTouch(other!);

		base.StartTouch(other);
	}

	public override void EndTouch(BaseEntity? other) {
		if (Soundscape.Get() != null)
			Soundscape.Get()!.DelegateEndTouch(other!);

		base.EndTouch(other);
	}

	public override void Spawn() {
		base.Spawn();
		InitTrigger();

		SetThink(PlayerUpdateThink);
		SetNextThink(gpGlobals.CurTime + 0.2f);
	}

	public override void Activate() {
		Soundscape.Set(gEntList.FindEntityByName(null, SoundscapeName) as EnvSoundscapeTriggerable);
		base.Activate();
	}

	// look for dead/spectating players in our volume, to call touch on
	public void PlayerUpdateThink() {
		int i;
		SetNextThink(gpGlobals.CurTime + 0.2f);

		List<Handle<BasePlayer>> oldSpectators = Spectators;
		Spectators = [];

		for (i = 1; i <= gpGlobals.MaxClients; ++i) {
			BasePlayer? player = Util.PlayerByIndex(i);

			if (player == null)
				continue;

			if (player.IsAlive())
				continue;

			Handle<BasePlayer> hPlayer = new();
			hPlayer.Set(player);

			// if the spectator is intersecting the trigger, track it, and start a touch if it is just starting to touch
			if (Intersects(player)) {
				if (!oldSpectators.Contains(hPlayer))
					StartTouch(player);
				Spectators.Add(hPlayer);
			}
		}

		// check for spectators who are no longer intersecting
		for (i = 0; i < oldSpectators.Count; ++i) {
			BasePlayer? player = oldSpectators[i].Get();

			if (player == null)
				continue;

			if (!Spectators.Contains(oldSpectators[i]))
				EndTouch(player);
		}
	}
}
