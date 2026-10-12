using CommunityToolkit.HighPerformance;

using Source.Common.Formats.Keyvalues;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Shared;

public enum ParsePropDataReturn
{
	Succeeded,
	SuccededAllowedStatic,
	FailedNoData,
	FailedBadData
}

public enum PropDataInteractions
{
	PhysgunWorldStick,      // "onworldimpact"	"stick"
	PhysgunFirstBreak,      // "onfirstimpact"	"break"
	PhysgunFirstPaint,      // "onfirstimpact"	"paintsplat"
	PhysgunFirstImpale,     // "onfirstimpact"	"impale"
	PhysgunLaunchSpinNone, // "onlaunch"		"spin_none"
	PhysgunLaunchSpinZ,    // "onlaunch"		"spin_zaxis"
	PhysgunBreakExplode,    // "onbreak"		"explode_fire"
	PhysgunDamageNone,      // "damage"			"none"
	FireFlammable,           // "flammable"			"yes"
	FireExplosiveResist,    // "explosive_resist"	"yes"
	FireIgniteHalfHealth,   // "ignite"				"halfhealth"
	PhysgunCreateFlare,     // "onpickup"		"create_flare"
	PhysgunAllowOverhead,   // "allow_overhead"	"yes"
	WorldBloodsplat,         // "onworldimpact", "bloodsplat"
	PhysgunNotifyChildren,  // "onfirstimpact" cause attached flechettes to explode
	NumInteractions,
}

public interface IMultiplayerPhysics
{
	PhysicsMultiplayerMode GetMultiplayerPhysicsMode();
	float GetMass();
	bool IsAsleep();
}

public enum PhysicsMultiplayerMode
{
	AutoDetect,
	Solid,
	NonSolid,
	Clientside
}

public enum MultiplayerBreak
{
	Default,
	Serverside,
	Clientside,
	Both
}


public enum PerformanceMode_t
{
	Normal,
	NoGibs,
	FullGibs,
	ReducedGibs,
}

public interface IBreakableWithPropData
{
	void SetDmgModBullet(float dmgMod);
	void SetDmgModClub(float dmgMod);
	void SetDmgModExplosive(float dmgMod);
	float GetDmgModBullet();
	float GetDmgModClub();
	float GetDmgModExplosive();

	// Explosive
	void SetExplosiveRadius(float radius);
	void SetExplosiveDamage(float damage);
	float GetExplosiveRadius();
	float GetExplosiveDamage();

	// Physics damage tables
	void SetPhysicsDamageTable(ReadOnlySpan<char> tableName);
	ReadOnlySpan<char> GetPhysicsDamageTable();

	// Breakable chunks
	void SetBreakableModel(ReadOnlySpan<char> model);
	ReadOnlySpan<char> GetBreakableModel();
	void SetBreakableSkin(int skin);
	int GetBreakableSkin();
	void SetBreakableCount(int count);
	int GetBreakableCount();
	void SetMaxBreakableSize(int size);
	int GetMaxBreakableSize();

	// LOS blocking
	void SetPropDataBlocksLOS(bool blocksLOS);
	void SetPropDataIsAIWalkable(bool blocksLOS);

	// Interactions
	void SetInteraction(PropDataInteractions interaction);
	bool HasInteraction(PropDataInteractions interaction);

	// Multiplayer physics mode
	void SetPhysicsMode(PhysicsMultiplayerMode mode);
	PhysicsMultiplayerMode GetPhysicsMode();

	// Multiplayer breakable spawn behavior
	void SetMultiplayerBreakMode(MultiplayerBreak mode);
	MultiplayerBreak GetMultiplayerBreakMode();

	// Used for debugging
	void SetBasePropData(ReadOnlySpan<char> propdata);
	ReadOnlySpan<char> GetBasePropData();
}
#if CLIENT_DLL || GAME_DLL
public class PropData : AutoGameSystem
{
	// Parsing details for each of the propdata interactions
	readonly struct PropDataInteraction(string sectionName, string keyName, string value)
	{
		public readonly string SectionName = sectionName;
		public readonly string KeyName = keyName;
		public readonly string Value = value;
	}

	static readonly PropDataInteraction[] PropdataInteractionSections = [
		new("physgun_interactions", "onworldimpact", "stick"),       // PropDataInteractions.PhysgunWorldStick,
		new("physgun_interactions", "onfirstimpact", "break"),       // PropDataInteractions.PhysgunFirstBreak,
		new("physgun_interactions", "onfirstimpact", "paintsplat"),  // PropDataInteractions.PhysgunFirstPaint,
		new("physgun_interactions", "onfirstimpact", "impale"),      // PropDataInteractions.PhysgunFirstImpale,
		new("physgun_interactions", "onlaunch", "spin_none"),        // PropDataInteractions.PhysgunLaunchSpinNone,
		new("physgun_interactions", "onlaunch", "spin_zaxis"),       // PropDataInteractions.PhysgunLaunchSpinZ,
		new("physgun_interactions", "onbreak", "explode_fire"),      // PropDataInteractions.PhysgunBreakExplode,
		new("physgun_interactions", "damage", "none"),               // PropDataInteractions.PhysgunDamageNone,

		new("fire_interactions", "flammable", "yes"),                // PropDataInteractions.FireFlammable,
		new("fire_interactions", "explosive_resist", "yes"),         // PropDataInteractions.FireExplosiveResist,
		new("fire_interactions", "ignite", "halfhealth"),            // PropDataInteractions.FireIgniteHalfHealth,

		new("physgun_interactions", "onpickup", "create_flare"),     // PropDataInteractions.PhysgunCreateFlare,

		new("physgun_interactions", "allow_overhead", "yes"),        // PropDataInteractions.PhysgunAllowOverhead,

		new("world_interactions", "onworldimpact", "bloodsplat"),    // PropDataInteractions.WorldBloodsplat,
	];

	/// <summary>
	/// returns the axis index with the smallest size
	/// </summary>
	static int SmallestAxis(in Vector3 vec) {
		if (vec.X < vec.Y) {
			if (vec.X < vec.Z)
				return 0;
			return 2;
		}
		if (vec.Y < vec.Z)
			return 1;
		return 2;
	}

	public PropData() : base("CPropData") {
		PropDataLoaded = false;
		KVPropData = null;
	}

	// Inherited from IAutoServerSystem
	public override void LevelInitPreEntity() {
		BreakableChunks.Clear();
		ParsePropDataFile();
	}

	public override void LevelShutdownPostEntity() {
		if (KVPropData != null)
			KVPropData = null;
	}

	/// <summary>
	/// Clear out the stats + their history
	/// </summary>
	public void ParsePropDataFile() {
		KVPropData = new KeyValues("PropDatafile");
		if (!KVPropData.LoadFromFile(filesystem, "scripts/propdata.txt")) {
			KVPropData = null;
			return;
		}

		PropDataLoaded = true;

		// Now try and parse out the breakable section
		KeyValues? breakableSection = KVPropData.FindKey("BreakableModels");
		if (breakableSection != null) {
			KeyValues? chunkSection = breakableSection.GetFirstSubKey();
			while (chunkSection != null) {
				// Create a new chunk section and add it to our list
				BreakableChunk breakableChunk = new();
				BreakableChunks.Add(breakableChunk);
				breakableChunk.ChunkType = chunkSection.Name;

				// Read in all the model names
				KeyValues? modelName = chunkSection.GetFirstSubKey();
				while (modelName != null) {
					string model = modelName.Name;
					breakableChunk.ChunkModels.Add(model);
					BaseEntity.PrecacheModel(model);

					modelName = modelName.GetNextKey();
				}

				chunkSection = chunkSection.GetNextKey();
			}
		}
	}

	/// <summary>
	/// Parse a keyvalues section into the prop
	/// <br/>
	/// interactionSection is a bit of jiggery-pokery to get around the unfortunate
	/// fact that the interaction KV sections ("physgun_interactions", "fire_interactions", etc)
	/// are OUTSIDE the "prop_data" KV section in the model, but may be contained WITHIN the
	/// specified Base's "prop_data" section (i.e. in propdata.txt)
	/// </summary>
	public ParsePropDataReturn ParsePropFromKV(BaseEntity prop, KeyValues section, KeyValues interactionSection) {
		if (prop is not IBreakableWithPropData breakableInterface)
			return ParsePropDataReturn.FailedBadData;

		ParsePropDataReturn baseResult = ParsePropDataReturn.Succeeded;

		// Do we have a base?
		ReadOnlySpan<char> baseName = section.GetString("base");
		if (!baseName.IsEmpty) {
			baseResult = ParsePropFromBase(prop, baseName);
			if ((baseResult != ParsePropDataReturn.Succeeded) && (baseResult != ParsePropDataReturn.SuccededAllowedStatic))
				return baseResult;
		}

		// Allow overriding of Block LOS
		int blockLOS = (int)section.GetFloat("blockLOS", -1);
		if (blockLOS != -1)
			breakableInterface.SetPropDataBlocksLOS(blockLOS != 0);

		// Set whether AI can walk on this prop
		int isWalkable = (int)section.GetFloat("AIWalkable", -1);
		if (isWalkable != -1)
			breakableInterface.SetPropDataIsAIWalkable(isWalkable != 0);

		// Set custom damage table
		ReadOnlySpan<char> tableName;
		if (breakableInterface.GetPhysicsDamageTable().IsEmpty)
			tableName = section.GetString("damage_table", null);
		else
			tableName = section.GetString("damage_table", breakableInterface.GetPhysicsDamageTable());

		if (!tableName.IsEmpty)
			breakableInterface.SetPhysicsDamageTable(tableName);
		else
			breakableInterface.SetPhysicsDamageTable(null);

		// Get multiplayer physics mode if not set by map
		breakableInterface.SetPhysicsMode((PhysicsMultiplayerMode)section.GetInt("physicsmode",
			(int)breakableInterface.GetPhysicsMode()));

		ReadOnlySpan<char> multiplayer_break = section.GetString("multiplayer_break", null);
		if (!multiplayer_break.IsEmpty) {
			MultiplayerBreak mode = MultiplayerBreak.Default;
			if (stricmp(multiplayer_break, "server") == 0)
				mode = MultiplayerBreak.Serverside;
			else if (stricmp(multiplayer_break, "client") == 0)
				mode = MultiplayerBreak.Clientside;
			else if (stricmp(multiplayer_break, "both") == 0)
				mode = MultiplayerBreak.Both;
			breakableInterface.SetMultiplayerBreakMode(mode);
		}

		// Get damage modifiers, but only if they're specified, because our base may have already overridden them.
		breakableInterface.SetDmgModBullet(section.GetFloat("dmg.bullets", breakableInterface.GetDmgModBullet()));
		breakableInterface.SetDmgModClub(section.GetFloat("dmg.club", breakableInterface.GetDmgModClub()));
		breakableInterface.SetDmgModExplosive(section.GetFloat("dmg.explosive", breakableInterface.GetDmgModExplosive()));

		// Get the health (unless this is an override prop)
		if (!BaseEntity.FClassnameIs(prop, "prop_physics_override") && !BaseEntity.FClassnameIs(prop, "prop_dynamic_override")) {
			prop.SetHealth(section.GetInt("health", prop.GetHealth()));

			// Explosive?
			breakableInterface.SetExplosiveDamage(section.GetFloat("explosive_damage", breakableInterface.GetExplosiveDamage()));
			breakableInterface.SetExplosiveRadius(section.GetFloat("explosive_radius", breakableInterface.GetExplosiveRadius()));

#if GAME_DLL
			// If we now have health, we're not allowed to ignore physics damage
			if (prop.GetHealth() != 0)
				prop.RemoveSpawnFlags(Game.Server.BreakableProp.SF.DontTakePhysicsDamage);
#endif
		}

		ReadOnlySpan<char> breakableModel;
		if (breakableInterface.GetBreakableModel().IsEmpty)
			breakableModel = section.GetString("breakable_model", null);
		else
			breakableModel = section.GetString("breakable_model", breakableInterface.GetBreakableModel());

		if (!breakableModel.IsEmpty)
			breakableInterface.SetBreakableModel(breakableModel);
		else
			breakableInterface.SetBreakableModel(null);

		breakableInterface.SetBreakableSkin(section.GetInt("breakable_skin", breakableInterface.GetBreakableSkin()));
		breakableInterface.SetBreakableCount(section.GetInt("breakable_count", breakableInterface.GetBreakableCount()));

		// Calculate the maximum size of the breakables this breakable will produce
		Vector3 size = prop.CollisionProp().OBBSize();
		// Throw away the smallest coord
		int smallest = SmallestAxis(size);
		MathLib.SubFloat(ref size, smallest) = 1;
		float volume = size.X * size.Y * size.Z;
		int maxSize = (int)MathF.Floor(volume / (32.0f * 32.0f));
		breakableInterface.SetMaxBreakableSize(maxSize);

		// Now parse our interactions
		for (int i = 0; i < PropdataInteractionSections.Length; i++) {
			// If we hit this assert, we have too many interactions for our current storage solution to handle
			Assert(i < 32);

			ref readonly PropDataInteraction interaction = ref PropdataInteractionSections[i];

			KeyValues? currentInter = interactionSection.FindKey(interaction.SectionName);
			if (currentInter != null) {
				ReadOnlySpan<char> interBase = currentInter.GetString(interaction.KeyName);
				if (!interBase.IsEmpty && stricmp(interBase, interaction.Value) == 0)
					breakableInterface.SetInteraction((PropDataInteractions)i);
			}
		}

		// If the base said we're allowed to be static, return that
		if (baseResult == ParsePropDataReturn.SuccededAllowedStatic)
			return ParsePropDataReturn.SuccededAllowedStatic;

		// Otherwise, see if our propdata says we are allowed to be static
		if (section.GetInt("allowstatic", 0) != 0)
			return ParsePropDataReturn.SuccededAllowedStatic;

		return ParsePropDataReturn.Succeeded;
	}

	/// <summary>
	/// Fill out a prop's with base data parsed from the propdata file
	/// </summary>
	public ParsePropDataReturn ParsePropFromBase(BaseEntity prop, ReadOnlySpan<char> propData) {
		if (!PropDataLoaded)
			return ParsePropDataReturn.FailedNoData;

		if (prop is not IBreakableWithPropData breakableInterface)
			return ParsePropDataReturn.FailedBadData;

		if (KVPropData == null)
			return ParsePropDataReturn.FailedBadData;

		// Find the specified propdata
		KeyValues? section = KVPropData.FindKey(propData);
		if (section == null) {
			Warning($"{prop.GetClassname()} '{prop.GetModelName()}' has a base specified as '{propData}', but there is no matching entry in propdata.txt.\n");
			return ParsePropDataReturn.FailedBadData;
		}

		// Store off the first base data for debugging
		if (breakableInterface.GetBasePropData().IsEmpty)
			breakableInterface.SetBasePropData(propData);

		return ParsePropFromKV(prop, section, section);
	}

	public ReadOnlySpan<char> GetRandomChunkModel(ReadOnlySpan<char> breakableSection, int maxSize = -1) {
		if (!PropDataLoaded)
			return null;

		// Find the right section
		int count = BreakableChunks.Count;
		int i;
		for (i = 0; i < count; i++) {
			if (BreakableChunks[i].ChunkType.AsSpan().StartsWith(breakableSection, StringComparison.Ordinal))
				break;
		}
		if (i == count)
			return null;

		// Now pick a random one and return it
		int random;
		if (maxSize == -1)
			random = RandomInt(0, BreakableChunks[i].ChunkModels.Count - 1);
		else {
			// Don't pick anything over the specified size
			random = RandomInt(0, Math.Min(maxSize, BreakableChunks[i].ChunkModels.Count - 1));
		}

		return BreakableChunks[i].ChunkModels[random];
	}

	protected KeyValues? KVPropData;
	protected bool PropDataLoaded;

	class BreakableChunk
	{
		public string ChunkType = "";
		public readonly List<string> ChunkModels = [];
	}

	readonly List<BreakableChunk> BreakableChunks = [];

	public static readonly PropData g_PropDataSystem = new();
}
#endif
