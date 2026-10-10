global using WEAPON_FILE_INFO_HANDLE = ushort;

#if CLIENT_DLL
using Game.Client.HUD;
#endif

using Source.Common.Filesystem;
using Source;

using Source.Common.Formats.Keyvalues;

namespace Game.Shared;

public enum WeaponSound
{
	Empty,
	Single,
	SingleNPC,
	Double,
	DoubleNPC,
	Burst,
	Reload,
	ReloadNPC,
	MeleeMiss,
	MeleeHit,
	MeleeHitWorld,
	Special1,
	Special2,
	Special3,
	Taunt,
	Deploy,

	Num
}

public delegate FileWeaponInfo CreateWeaponInfoFn();

public static class WeaponParse
{
	public static CreateWeaponInfoFn CreateWeaponInfo = () => new FileWeaponInfo();

	static readonly FileWeaponInfo NullWeaponInfo = new();
	static WEAPON_FILE_INFO_HANDLE currentHandle = 0;
	static readonly Dictionary<ulong, WEAPON_FILE_INFO_HANDLE> WeaponInfoHashDatabase = [];
	static readonly Dictionary<WEAPON_FILE_INFO_HANDLE, FileWeaponInfo> WeaponInfoDatabase = [];

	public static int GetWeaponSoundFromString(ReadOnlySpan<char> str) {
		for (WeaponSound i = WeaponSound.Empty; i < WeaponSound.Num; i++) {
			if (stricmp(str, FileWeaponInfo.WeaponSoundCategories[(int)i]) == 0)
				return (int)i;
		}
		return -1;
	}

	internal static void PrecacheFileWeaponInfoDatabase(IFileSystem filesystem) {
		if (WeaponInfoDatabase.Count != 0)
			return;

		Span<char> fileBase = stackalloc char[512];

		KeyValues manifest = new("weaponscripts");
		if (manifest.LoadFromFile(filesystem, "scripts/weapon_manifest.txt", "GAME")) {
			for (KeyValues? sub = manifest.GetFirstSubKey(); sub != null; sub = sub.GetNextKey()) {
				if (stricmp(sub.Name, "file") == 0) {
					fileBase.Clear();
					sub.GetString().FileBase(fileBase);
					WEAPON_FILE_INFO_HANDLE tmp;
#if CLIENT_DLL
					if (ReadWeaponDataFromFileForSlot(filesystem, fileBase.SliceNullTerminatedString(), out tmp))
						gWR.LoadWeaponSprites(tmp);
#else
					ReadWeaponDataFromFileForSlot(filesystem, fileBase, out tmp);
#endif
				}
				else
					Error($"Expecting 'file', got {sub.Name}\n");
			}
		}
	}

	public static bool ReadWeaponDataFromFileForSlot(IFileSystem filesystem, ReadOnlySpan<char> weaponName, out WEAPON_FILE_INFO_HANDLE handle) {
		handle = FindWeaponInfoSlot(weaponName);
		FileWeaponInfo? fileInfo = GetFileWeaponInfoFromHandle(handle);
		Assert(fileInfo != null);
		if (fileInfo.ParsedScript)
			return true;

		Span<char> sz = stackalloc char[128];
		sprintf(sz, "scripts/weapons/%s.txt").S(weaponName);

		KeyValues keyValues = new KeyValues();
		if (!keyValues.LoadFromFile(filesystem, sz.SliceNullTerminatedString()))
			return false;

		fileInfo.Parse(keyValues, weaponName);

		return true;
	}

	public static FileWeaponInfo GetFileWeaponInfoFromHandle(WEAPON_FILE_INFO_HANDLE handle) {
		if (handle < 0 || handle >= WeaponInfoDatabase.Count)
			return NullWeaponInfo;

		if (handle == unchecked((WEAPON_FILE_INFO_HANDLE)(-1)))
			return NullWeaponInfo;

		return WeaponInfoDatabase.TryGetValue(handle, out var info) ? info : NullWeaponInfo;
	}

	private static WEAPON_FILE_INFO_HANDLE LookupWeaponInfoSlot(ReadOnlySpan<char> weaponName) {
		return WeaponInfoHashDatabase.TryGetValue(weaponName.Hash(), out var handle) ? handle : unchecked((WEAPON_FILE_INFO_HANDLE)(-1));
	}

	private static WEAPON_FILE_INFO_HANDLE FindWeaponInfoSlot(ReadOnlySpan<char> weaponName) {
		UtlSymId_t hash = weaponName.Hash();
		if (WeaponInfoHashDatabase.TryGetValue(hash, out var lookup))
			return lookup;

		FileWeaponInfo insert = CreateWeaponInfo();

		lookup = currentHandle++;
		WeaponInfoDatabase[lookup] = insert;
		WeaponInfoHashDatabase[hash] = lookup;
		return lookup;
	}

	public const int MAX_SHOOT_SOUNDS = 16;
	public const int MAX_WEAPON_STRING = 80;
	public const int MAX_WEAPON_PREFIX = 16;
	public const int MAX_WEAPON_AMMO_NAME = 32;

	public const string WEAPON_PRINTNAME_MISSING = "!!! Missing printname on weapon";
}

public class FileWeaponInfo
{
	public FileWeaponInfo() {
		for (int i = 0; i < ShootSounds.Length; i++) {
			ShootSounds[i] = new char[WeaponParse.MAX_WEAPON_STRING];
		}
	}
	public static readonly string[] WeaponSoundCategories = [
		"empty",
		"single_shot",
		"single_shot_npc",
		"double_shot",
		"double_shot_npc",
		"burst",
		"reload",
		"reload_npc",
		"melee_miss",
		"melee_hit",
		"melee_hit_world",
		"special1",
		"special2",
		"special3",
		"taunt",
		"deploy"
	];

	public virtual void Parse(KeyValues keyValuesData, ReadOnlySpan<char> weaponName) {
		ParsedScript = true;

		strcpy(ClassName, weaponName);
		strcpy(PrintName, keyValuesData.GetString("printname", WeaponParse.WEAPON_PRINTNAME_MISSING));

		strcpy(ViewModel, keyValuesData.GetString("viewmodel"));
		strcpy(WorldModel, keyValuesData.GetString("playermodel"));
		strcpy(AnimationPrefix, keyValuesData.GetString("anim_prefix"));
		Slot = keyValuesData.GetInt("bucket", 0);
		Position = keyValuesData.GetInt("bucket_position", 0);

#if CLIENT_DLL
		// todo: hud_fastswitch
#endif

		MaxClip1 = keyValuesData.GetInt("clip_size", WEAPON_NOCLIP);
		MaxClip2 = keyValuesData.GetInt("clip2_size", WEAPON_NOCLIP);
		DefaultClip1 = keyValuesData.GetInt("default_clip", MaxClip1);
		DefaultClip2 = keyValuesData.GetInt("default_clip2", MaxClip2);
		Weight = keyValuesData.GetInt("weight", 0);

		Flags = (WeaponFlags)keyValuesData.GetInt("item_flags", (int)WeaponFlags.LimitInWorld);

		AutoSwitchTo = (keyValuesData.GetInt("autoswitchto", 1) != 0) ? true : false;
		AutoSwitchFrom = (keyValuesData.GetInt("autoswitchfrom", 1) != 0) ? true : false;
		BuiltRightHanded = (keyValuesData.GetInt("BuiltRightHanded", 1) != 0) ? true : false;
		AllowFlipping = (keyValuesData.GetInt("AllowFlipping", 1) != 0) ? true : false;
		MeleeWeapon = (keyValuesData.GetInt("MeleeWeapon", 0) != 0) ? true : false;

		// Primary ammo used
#if CLIENT_DLL || GAME_DLL // Annoying but needed to avoid errors (IntelliSense freaks out thinking its going to build Shared independently)
		ReadOnlySpan<char> pAmmo = keyValuesData.GetString("primary_ammo", "None");
		strcpy(Ammo1, strcmp("None", pAmmo) == 0 ? "" : pAmmo);
		AmmoType = GetAmmoDef().Index(Ammo1);

		// Secondary ammo used
		pAmmo = keyValuesData.GetString("secondary_ammo", "None");
		strcpy(Ammo2, strcmp("None", pAmmo) == 0 ? "" : pAmmo);
		Ammo2Type = GetAmmoDef().Index(Ammo2);
#endif

		for (int i = 0; i < ShootSounds.Length; i++)
			memreset(ShootSounds[i]);

		KeyValues? soundData = keyValuesData.FindKey("SoundData");
		if (soundData != null) {
			for (WeaponSound i = WeaponSound.Empty; i < WeaponSound.Num; i++) {
				ReadOnlySpan<char> soundname = soundData.GetString(WeaponSoundCategories[(int)i]);
				if (!soundname.IsEmpty)
					strcpy(ShootSounds[(int)i], soundname);
			}
		}
	}

	public bool ParsedScript;
	public bool LoadedHudElements;
	public readonly char[] ClassName = new char[WeaponParse.MAX_WEAPON_STRING];
	public readonly char[] PrintName = new char[WeaponParse.MAX_WEAPON_STRING];        // Name for showing in HUD, etc.
	public readonly char[] ViewModel = new char[WeaponParse.MAX_WEAPON_STRING];        // View model of this weapon
	public readonly char[] WorldModel = new char[WeaponParse.MAX_WEAPON_STRING];       // Model of this weapon seen carried by the player
	public readonly char[] AnimationPrefix = new char[WeaponParse.MAX_WEAPON_PREFIX];  // Prefix of the animations that should be used by the player carrying this weapon
	public int Slot;                                  // inventory slot.
	public int Position;                              // position in the inventory slot.
	public int MaxClip1;                              // max primary clip size (-1 if no clip)
	public int MaxClip2;                              // max secondary clip size (-1 if no clip)
	public int DefaultClip1;                          // amount of primary ammo in the gun when it's created
	public int DefaultClip2;                          // amount of secondary ammo in the gun when it's created
	public int Weight;                                // this value used to determine this weapon's importance in autoselection.
	public int RumbleEffect;                          // Which rumble effect to use when fired? (xbox)
	public bool AutoSwitchTo;                         // whether this weapon should be considered for autoswitching to
	public bool AutoSwitchFrom;                       // whether this weapon can be autoswitched away from when picking up another weapon or ammo
	public WeaponFlags Flags;                                 // miscellaneous weapon flags
	public readonly char[] Ammo1 = new char[WeaponParse.MAX_WEAPON_AMMO_NAME];         // "primary" ammo type
	public readonly char[] Ammo2 = new char[WeaponParse.MAX_WEAPON_AMMO_NAME];         // "secondary" ammo type
	public readonly char[][] ShootSounds = new char[(int)WeaponSound.Num][];
	public int AmmoType;
	public int Ammo2Type;
	public bool MeleeWeapon;
	// This tells if the weapon was built right-handed (defaults to true).
	// This helps cl_righthand make the decision about whether to flip the model or not.
	public bool BuiltRightHanded;
	public bool AllowFlipping;  // False to disallow flipping the model, regardless of whether
															// it is built left or right handed.

#if CLIENT_DLL
	public int SpriteCount;
	public HudTexture IconActive;
	public HudTexture IconInactive;
	public HudTexture IconAmmo;
	public HudTexture IconAmmo2;
	public HudTexture IconCrosshair;
	public HudTexture IconAutoaim;
	public HudTexture IconZoomedCrosshair;
	public HudTexture IconZoomedAutoaim;
	public HudTexture IconSmall;
#endif
}
