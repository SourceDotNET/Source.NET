global using static Game.Server.SoundEntGlobals;

using Game.Shared;

using SharpCompress.Common;
using SharpCompress.Common.Zip;

using Source.Common.Engine;
using Source.Engine;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Game.Server;

public static class SoundEntGlobals
{
	public static SoundEnt? g_pSoundEnt;

	public const int SOUNDENT_VOLUME_MACHINEGUN = 1500;
	public const int SOUNDENT_VOLUME_SHOTGUN = 1500;
	public const int SOUNDENT_VOLUME_PISTOL = 1500;
	public const int SOUNDENT_VOLUME_EMPTY = 500; // volume of the "CLICK" when you have no bullets
	public const int MAX_WORLD_SOUNDS_SP = 64;
	public const int MAX_WORLD_SOUNDS_MP = 128;
	public const int SOUNDLIST_EMPTY = -1;
	public const int SOUNDLISTTYPE_FREE = 1;
	public const int SOUNDLISTTYPE_ACTIVE = 2;
}


[Flags]
public enum SoundInstanceType : uint
{
	None = 0,
	Combat = 0x00000001,
	World = 0x00000002,
	Player = 0x00000004,
	Danger = 0x00000008,
	BulletImpact = 0x00000010,
	Carcass = 0x00000020,
	Meat = 0x00000040,
	Garbage = 0x00000080,
	Thumper = 0x00000100, // keeps certain creatures at bay
	Bugbait = 0x00000200, // gets the antlion's attention
	PhysicsDanger = 0x00000400,
	DangerSniperOnly = 0x00000800, // only scares the sniper NPC.
	MoveAway = 0x00001000,
	PlayerVehicle = 0x00002000,
	ReadinessLow = 0x00004000, // Changes listener's readiness (Player Companion only)
	ReadinessMedium = 0x00008000,
	ReadinessHigh = 0x00010000,

	// Contexts begin here.
	ContextFromSniper = 0x00100000, // additional context for SOUND_DANGER
	ContextGunfire = 0x00200000, // Added to SOUND_COMBAT
	ContextMortar = 0x00400000, // Explosion going to happen here.
	ContextCombineOnly = 0x00800000, // Only combine can hear sounds marked this way
	ContextReactToSource = 0x01000000, // React to sound source's origin, not sound's location
	ContextExplosion = 0x02000000, // Context added to SOUND_COMBAT, usually.
	ContextExcludeCombine = 0x04000000, // Combine do NOT hear this
	ContextDangerApproach = 0x08000000, // Treat as a normal danger sound if you see the source, otherwise turn to face source.
	ContextAlliesOnly = 0x10000000, // Only player allies can hear this sound
	ContextPlayerVehicle = 0x20000000, // HACK: need this because we're not treating the SOUND_xxx values as true bit values! See switch in OnListened.

	AllContexts = 0xFFF00000,
	AllScents = Carcass | Meat | Garbage,
	AllSounds = 0x000FFFFF & ~AllScents
}

// Make as many of these as you want. 
public enum SoundEntChannel
{
	Unspecified = 0,
	Repeating,
	RepeatedDanger,   // for things that make danger sounds frequently.
	RepeatedPhysicsDanger,
	Weapon,
	Injury,
	BulletImpact,
	NPCFootstep,
	SpookyNoise,      // made by zombies in darkness
	ZombineGrenade,
}

public enum SoundPriority
{
	VeryLow = -2,
	Low,
	Normal = 0,
	High,
	VeryHigh,
	Highest,
};


/// <summary>
/// Analog of CSound
/// </summary>
public struct WorldSoundInstance
{
	public WorldSoundInstance() {
		Clear();
	}

	public readonly bool DoesSoundExpire() => NoExpirationTime == false;
	public readonly TimeUnit_t SoundExpirationTime() => NoExpirationTime ? TimeUnit_t.MaxValue : ExpireTime;
	public void SetSoundOrigin(in Vector3 origin) => Origin = origin;
	public unsafe ref readonly Vector3 GetSoundOrigin() => ref Origin;
	public ref readonly Vector3 GetSoundReactOrigin() {
		switch (Type) {
			case SoundInstanceType.BulletImpact:
			case SoundInstanceType.PhysicsDanger:
				if (Owner.Get() != null) {
					// We really want the origin of this sound's 
					// owner.
					return ref Owner.Get()!.GetAbsOrigin();
				}
				else {
					// If the owner is somehow invalid, we'll settle
					// for the sound's origin rather than a crash.
					return ref GetSoundOrigin();
				}
				break;
		}

		if ((Type & SoundInstanceType.ContextReactToSource) != 0)
			if (Owner.Get() != null)
				return ref Owner.Get()!.GetAbsOrigin();

		// Check for types with additional context.
		if ((Type & SoundInstanceType.Danger) != 0) {
			if ((Type & SoundInstanceType.ContextFromSniper) != 0) {
				if (Owner.Get() != null) {
					// Be afraid of the sniper's location, not where the bullet will hit.
					return ref Owner.Get()!.GetAbsOrigin();
				}
				else
					return ref GetSoundOrigin();
			}
		}


		return ref GetSoundOrigin();
	}
	public bool FIsSound() {
		switch (SoundTypeNoContext()) {
			case SoundInstanceType.Combat:
			case SoundInstanceType.World:
			case SoundInstanceType.Player:
			case SoundInstanceType.Danger:
			case SoundInstanceType.DangerSniperOnly:
			case SoundInstanceType.Thumper:
			case SoundInstanceType.BulletImpact:
			case SoundInstanceType.Bugbait:
			case SoundInstanceType.PhysicsDanger:
			case SoundInstanceType.MoveAway:
			case SoundInstanceType.PlayerVehicle:
				return true;
			default: return false;
		}
	}
	public bool FIsScent() {
		switch (Type) {
			case SoundInstanceType.Carcass:
			case SoundInstanceType.Meat:
			case SoundInstanceType.Garbage:
				return true;
			default: return false;
		}
	}
	public readonly bool IsSoundType(SoundInstanceType soundFlags) => (Type & soundFlags) != 0;
	public readonly SoundInstanceType SoundType() => Type;
	public readonly SoundInstanceType SoundContext() => Type & SoundInstanceType.AllContexts;
	public readonly SoundInstanceType SoundTypeNoContext() => Type & ~SoundInstanceType.AllContexts;
	public readonly int Volume() => iVolume;
	public float OccludedVolume() { return iVolume * OcclusionScale; }
	public readonly int NextSound() => Next;
	public void Reset() {
		Origin = vec3_origin;
		Type = 0;
		iVolume = 0;
		Next = SOUNDLIST_EMPTY;
	}
	public readonly SoundEntChannel SoundChannel() => OwnerChannelIndex;
	public readonly bool ValidateOwner() => !HasOwner || Owner.Get() != null;

	public EHANDLE Owner;               // sound's owner
	public EHANDLE Target;              // Sounds's target - an odd concept. For a gunfire sound, the target is the entity being fired at
	public int iVolume;              // how loud the sound is
	public float OcclusionScale;       // How loud the sound is when occluded by the world. (volume * occlusionscale)
	public SoundInstanceType Type;                // what type of sound this is
	public int NextAudible;         // temporary link that NPCs use to build a list of audible sounds

	void Clear() {
		Origin = vec3_origin;
		Type = 0;
		iVolume = 0;
		OcclusionScale = 0;
		ExpireTime = 0;
		NoExpirationTime = false;
		Next = SOUNDLIST_EMPTY;
		NextAudible = 0;
	}

	public TimeUnit_t ExpireTime;   // when the sound should be purged from the list
	public short Next;      // index of next sound in this list ( Active or Free )
	public bool NoExpirationTime;
	public SoundEntChannel OwnerChannelIndex;

	public Vector3 Origin; // sound's location in space

	public bool HasOwner;   // Lets us know if this sound was created with an owner. In case the owner goes null.

	public int MyIndex;        // debugging
}


[LinkEntityToClass("soundent")]
public class SoundEnt : PointEntity
{
	// Construction, destruction
	public static bool InitSoundEnt() {
		g_pSoundEnt = (SoundEnt)BaseEntity.Create("soundent", vec3_origin, vec3_angle, GetWorldEntity())!;
		if (g_pSoundEnt == null) {
			Warning("**COULD NOT CREATE SOUNDENT**\n");
			return false;
		}
		g_pSoundEnt.AddEFlags(EFL.KeepOnRecreateEntities);
		return true;
	}
	public static void ShutdownSoundEnt() {
		if (g_pSoundEnt != null) {
			// Wtf? This calls the static method??? Excuse me???????? g_pSoundEnt.FreeList();
			g_pSoundEnt = null;
		}
	}

	public SoundEnt() {

	}

	public override void OnRestore() {
		base.OnRestore();

		if (g_pSoundEnt != null) {
			Assert(g_pSoundEnt != this);
			Util.Remove(g_pSoundEnt);
		}
		g_pSoundEnt = this;
	}
	public override void Precache() {
		// todo
	}
	public override void Spawn() {
		SetSolid(Source.SolidType.None);
		Initialize();
		SetNextThink(gpGlobals.CurTime + 1);
	}
	public override void Think() {
		// todo
	}
	public void Initialize() {
		// todo
	}
	public override EntityCapabilities ObjectCaps() { return base.ObjectCaps() & ~EntityCapabilities.AcrossTransition; }

	public static void InsertSound(SoundInstanceType type, in Vector3 origin, int volume, float duration, BaseEntity? owner = null, SoundEntChannel soundChannelIndex = SoundEntChannel.Unspecified, BaseEntity? soundTarget = null) {
		int iThisSound;

		if (g_pSoundEnt == null)
			return;

		if (soundChannelIndex == SoundEntChannel.Unspecified) {
			// No sound channel specified. So just make a new sound.
			iThisSound = g_pSoundEnt.IAllocSound();
		}
		else {
			// If this entity has already got a sound in the soundlist that's on this
			// channel, update that sound. Otherwise add a new one.
			iThisSound = g_pSoundEnt.FindOrAllocateSound(owner, soundChannelIndex);
		}

		if (iThisSound == SOUNDLIST_EMPTY) {
			DevMsg("Could not AllocSound() for InsertSound() (Game DLL)\n");
			return;
		}

		ref WorldSoundInstance sound = ref g_pSoundEnt.SoundPool[iThisSound];

		sound.SetSoundOrigin(origin);
		sound.Type = type;
		sound.iVolume = volume;
		sound.OcclusionScale = 0.5f;
		sound.ExpireTime = gpGlobals.CurTime + duration;
		sound.NoExpirationTime = false;
		sound.Owner.Set(owner);
		sound.Target.Set(soundTarget);
		sound.OwnerChannelIndex = soundChannelIndex;

		// Keep track of whether this sound had an owner when it was made. If the sound has a long duration,
		// the owner could disappear by the time someone hears this sound, so we have to look at this boolean
		// and throw out sounds who have a NULL owner but this field set to true. (sjb) 12/2/2005
		sound.HasOwner = owner != null;

		if (displaysoundlist.GetInt() == 1)
			Msg($"  Added Sound! Type:{sound.SoundType()}  Duration:{duration} (Time:{gpGlobals.CurTime})\n");
		if (displaysoundlist.GetInt() == 2 && (type & SoundInstanceType.Danger) != 0)
			Msg($"  Added Danger Sound! Duration:{duration} (Time:{gpGlobals.CurTime}\n");
	}
	public static void FreeSound(int sound, int previous) {
		if (g_pSoundEnt == null) {
			// no sound ent!
			return;
		}

		if (previous != SOUNDLIST_EMPTY)
			// sound is not the head of the active list, so
			// must fix the index for the Previous sound
			g_pSoundEnt.SoundPool[previous].Next = g_pSoundEnt.SoundPool[sound].Next;
		else
			// the sound we're freeing IS the head of the active list.
			g_pSoundEnt.ActiveSound = g_pSoundEnt.SoundPool[sound].Next;

		// make sound the head of the Free list.
		g_pSoundEnt.SoundPool[sound].Next = (short)g_pSoundEnt.iFreeSound;
		g_pSoundEnt.iFreeSound = sound;
	}
	public static int ActiveList() {
		if (g_pSoundEnt == null)
			return SOUNDLIST_EMPTY;

		return g_pSoundEnt.ActiveSound;
	}
	public static int FreeList() {
		if (g_pSoundEnt == null)
			return SOUNDLIST_EMPTY;

		return g_pSoundEnt.iFreeSound;
	}
	public static ref WorldSoundInstance SoundPointerForIndex(int iIndex) {
		if (g_pSoundEnt == null)
			return ref Unsafe.NullRef<WorldSoundInstance>();

		if (iIndex > (MAX_WORLD_SOUNDS_MP - 1)) {
			Msg("SoundPointerForIndex() - Index too large!\n");
			return ref Unsafe.NullRef<WorldSoundInstance>();
		}

		if (iIndex < 0) {
			Msg("SoundPointerForIndex() - Index < 0!\n");
			return ref Unsafe.NullRef<WorldSoundInstance>();
		}

		return ref g_pSoundEnt.SoundPool[iIndex];
	}
	public static ref WorldSoundInstance GetLoudestSoundOfType(SoundInstanceType iType, in Vector3 earPosition) {
		ref WorldSoundInstance loudestSound = ref Unsafe.NullRef<WorldSoundInstance>();

		int iThisSound;
		int iBestSound = SOUNDLIST_EMPTY;
		float flBestDist = MAX_COORD_RANGE * MAX_COORD_RANGE;// so first nearby sound will become best so far.
		float flDist;
		ref WorldSoundInstance pSound = ref Unsafe.NullRef<WorldSoundInstance>();

		iThisSound = ActiveList();

		while (iThisSound != SOUNDLIST_EMPTY) {
			pSound = ref SoundPointerForIndex(iThisSound);

			if (!Unsafe.IsNullRef(ref pSound) && pSound.Type == iType && pSound.ValidateOwner()) {
				flDist = (pSound.GetSoundOrigin() - earPosition).Length();

				//FIXME: This doesn't match what's in Listen()
				//flDist = UTIL_DistApprox( pSound->GetSoundOrigin(), vecEarPosition );

				if (flDist <= pSound.iVolume && flDist < flBestDist) {
					loudestSound = ref pSound;

					iBestSound = iThisSound;
					flBestDist = flDist;
				}
			}

			iThisSound = pSound.Next;
		}

		return ref loudestSound;
	}
	public static int ClientSoundIndex(Edict client) {
		int ret = ENTINDEX(client) - 1;

#if DEBUG
		if (ret < 0 || ret >= gpGlobals.MaxClients)
			Msg("** ClientSoundIndex returning a bogus value! **\n");

#endif // _DEBUG

		return ret;
	}

	public bool IsEmpty() => ActiveSound == SOUNDLIST_EMPTY;
	public int ISoundsInList(int listType) {
		int i;
		int iThisSound = SOUNDLIST_EMPTY;

		if (listType == SOUNDLISTTYPE_FREE)
			iThisSound = iFreeSound;
		else if (listType == SOUNDLISTTYPE_ACTIVE)
			iThisSound = ActiveSound;
		else
			Msg("Unknown Sound List Type!\n");

		if (iThisSound == SOUNDLIST_EMPTY) {
			return 0;
		}

		i = 0;

		while (iThisSound != SOUNDLIST_EMPTY) {
			i++;

			iThisSound = SoundPool[iThisSound].Next;
		}

		return i;
	}
	public int IAllocSound() {
		int iNewSound;

		if (iFreeSound == SOUNDLIST_EMPTY) {
			// no free sound!
			// todo: developer convar in game state if (developer.GetInt() >= 2)
			// todo: developer convar in game state 	Msg("Free Sound List is full!\n");

			return SOUNDLIST_EMPTY;
		}

		// there is at least one sound available, so move it to the
		// Active sound list, and return its SoundPool index.

		iNewSound = iFreeSound;// copy the index of the next free sound

		iFreeSound = SoundPool[iFreeSound].Next;// move the index down into the free list. 

		SoundPool[iNewSound].Next = (short)ActiveSound;// point the new sound at the top of the active list.

		ActiveSound = iNewSound;// now make the new sound the top of the active list. You're done.

#if DEBUG
		SoundPool[iNewSound].MyIndex = iNewSound;
#endif // DEBUG

		return iNewSound;
	}
	public int FindOrAllocateSound(BaseEntity? owner, SoundEntChannel soundChannelIndex) {
		int iSound = ActiveSound;

		while (iSound != SOUNDLIST_EMPTY) {
			ref WorldSoundInstance sound = ref SoundPool[iSound];

			if (sound.OwnerChannelIndex == soundChannelIndex && sound.Owner.Get() == owner)
				return iSound;

			iSound = sound.Next;
		}

		return IAllocSound();
	}

	int iFreeSound;   // index of the first sound in the free sound list
	int ActiveSound; // indes of the first sound in the active sound list
	int LastActiveSounds; // keeps track of the number of active sounds at the last update. (for diagnostic work)
	readonly WorldSoundInstance[] SoundPool = new WorldSoundInstance[MAX_WORLD_SOUNDS_MP];
}
