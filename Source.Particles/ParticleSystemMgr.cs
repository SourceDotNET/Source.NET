using Datamodel;

using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.MaterialSystem;

using DM = Datamodel.Datamodel;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public sealed class ParticleSystemMgr
{
	public static int g_nParticle_Multiplier = 1;

	readonly List<IParticleOperatorDefinition>[] ParticleOperators = new List<IParticleOperatorDefinition>[(int)ParticleFunctionType.Count];

	public ParticleSystemDictionary? Dictionary { get; private set; }

	IParticleSystemQuery Query_ = new DefaultParticleSystemQuery();
	IFileSystem? FileSystem;
	IMaterial? ShadowDepthMaterial;
	double LastSimulationTime;
	bool DidInit;
	bool UsingDefaultQuery = true;
	bool ShouldLoadSheets_ = true;

	public ParticleSystemMgr() {
		for (int i = 0; i < ParticleOperators.Length; i++)
			ParticleOperators[i] = [];
	}

	public IParticleSystemQuery Query() => Query_;

	public bool Init(IParticleSystemQuery? query, IFileSystem fileSystem) {
		FileSystem = fileSystem;

		if (UsingDefaultQuery && query != null) {
			Query_ = query;
			UsingDefaultQuery = false;
		}

		if (DidInit)
			return true;

		Dictionary = new ParticleSystemDictionary(this);
		ParticleOperators[(int)ParticleFunctionType.Children].Add(ChildOperatorDefinition.Instance);

		ShadowDepthMaterial = null;
		if (Singleton<IMaterialSystemHardwareConfig>().GetDXSupportLevel() >= 90) {
			KeyValues vmtKeyValues = new("DepthWrite");
			vmtKeyValues.SetInt("$no_fullbright", 1);
			vmtKeyValues.SetInt("$model", 0);
			vmtKeyValues.SetInt("$alphatest", 0);
			ShadowDepthMaterial = Singleton<IMaterialSystem>().CreateMaterial("__particlesDepthWrite", vmtKeyValues);
		}

		DidInit = true;
		return true;
	}

	public void AddParticleOperator(ParticleFunctionType opType, IParticleOperatorDefinition opFactory)
		=> ParticleOperators[(int)opType].Add(opFactory);

	public List<IParticleOperatorDefinition> GetAvailableParticleOperatorList(ParticleFunctionType whichList) => ParticleOperators[(int)whichList];

	public IDmxElementUnpackStructure GetParticleSystemDefinitionUnpackStructure() => ParticleSystemDefinition.GetUnpackStructure();

	public void ShouldLoadSheets(bool loadSheets) => ShouldLoadSheets_ = loadSheets;
	public bool GetShouldLoadSheets() => ShouldLoadSheets_;

	public void DecommitTempMemory() { }

	public void SetLastSimulationTime(double time) => LastSimulationTime = time;
	public double GetLastSimulationTime() => LastSimulationTime;

	public bool ReadParticleConfigFile(string fileName, bool precache, bool decommitTempMemory = true) {
		if (fileName.StartsWith('!')) {
			fileName = fileName[1..];
			precache = true;
		}

		IMaterialSystemHardwareConfig hardwareConfig = Singleton<IMaterialSystemHardwareConfig>();
		string? fallbackFormat = null;
		if (hardwareConfig.GetDXSupportLevel() < 90)
			fallbackFormat = "{0}_dx80.{1}";
		else if (hardwareConfig.GetDXSupportLevel() == 90 && hardwareConfig.PreferReducedFillrate())
			fallbackFormat = "{0}_dx90_slow.{1}";

		if (fallbackFormat != null) {
			int dot = fileName.LastIndexOfAny(['.', '/', '\\']);
			bool hasExtension = dot >= 0 && fileName[dot] == '.';
			string stripped = hasExtension ? fileName[..dot] : fileName;
			string extension = hasExtension ? fileName[(dot + 1)..] : "pcf";
			string fallback = string.Format(fallbackFormat, stripped, extension);
			if (FileSystem!.FileExists(fallback))
				fileName = fallback;
		}

		using IFileHandle? handle = FileSystem!.Open(fileName, FileOpenOptions.Read | FileOpenOptions.Binary, "GAME");
		if (handle == null || !handle.IsOK()) {
			Warning($"Particles: Missing '{fileName}'\n");
			return false;
		}

		using MemoryStream buffer = new();
		handle.Stream.CopyTo(buffer);
		buffer.Position = 0;
		return ReadParticleDefinitions(buffer, fileName, precache, decommitTempMemory);
	}

	public bool ReadParticleConfigFile(Stream buf, bool precache, bool decommitTempMemory = true, string? fileName = null)
		=> ReadParticleDefinitions(buf, fileName, precache, decommitTempMemory);

	bool ReadParticleDefinitions(Stream buf, string? fileName, bool precache, bool decommitTempMemory) {
		DM dm;
		try {
			dm = DM.Load(buf, Datamodel.Codecs.DeferredMode.Disabled);
		}
		catch (Exception) {
			Warning($"Unable to read particle definition {fileName}! UtlBuffer is probably the wrong type!\n");
			return false;
		}

		using (dm) {
			Element? root = dm.Root;
			if (root == null) {
				Warning($"Unable to read particle definition {fileName}! UtlBuffer is probably the wrong type!\n");
				return false;
			}

			if (string.Equals(root.ClassName, ParticleSystemDefinition.DmeParticleSystemDefinition, StringComparison.OrdinalIgnoreCase)) {
				AddParticleSystem(root, precache);
				return true;
			}

			if (!root.TryGetValue("particleSystemDefinitions", out object? attribute) || attribute is not ElementArray definitions)
				return false;

			foreach (Element? definition in definitions)
				if (definition != null)
					AddParticleSystem(definition, precache);
			return true;
		}
	}

	void AddParticleSystem(Element element, bool precache) {
		ParticleSystemDefinition? def = Dictionary!.AddParticleSystem(element);
		if (def != null && precache) {
			def.AlwaysPrecache = true;
			if (!def.IsPrecached)
				def.Precache();
		}
	}

	public bool IsParticleSystemDefined(string? name) {
		if (string.IsNullOrEmpty(name))
			return false;
		return Dictionary!.FindParticleSystem(name) != null;
	}

	public bool IsParticleSystemDefined(Guid id) => id != Guid.Empty && Dictionary!.FindParticleSystem(id) != null;

	public int GetParticleSystemIndex(string? name) => name == null ? INVALID_PARTICLE_SYSTEM_HANDLE : Dictionary!.FindParticleSystemHandle(name);

	public string GetParticleSystemNameFromIndex(int index) {
		if (index == INVALID_PARTICLE_SYSTEM_HANDLE || index >= Dictionary!.NameCount())
			return "Unknown";
		ParticleSystemDefinition? def = Dictionary.FindParticleSystem(index);
		return def == null ? "Unknown" : def.Name;
	}

	public int GetParticleSystemCount() => Dictionary!.NameCount();

	public ParticleSystemDefinition? FindParticleSystem(string? name) => Dictionary!.FindParticleSystem(name);
	public ParticleSystemDefinition? FindParticleSystem(Guid id) => Dictionary!.FindParticleSystem(id);

	public ParticleCollection? CreateParticleCollection(string? name, float delay = 0.0f, int randomSeed = 0) {
		if (name == null)
			return null;

		ParticleSystemDefinition? def = Dictionary!.FindParticleSystem(name);
		if (def == null) {
			Warning($"Attempted to create unknown particle system type {name}\n");
			return null;
		}

		ParticleCollection collection = new();
		collection.Init(def, delay, randomSeed);
		return collection;
	}

	public ParticleCollection? CreateParticleCollection(Guid id, float delay = 0.0f, int randomSeed = 0) {
		if (id == Guid.Empty)
			return null;

		ParticleSystemDefinition? def = Dictionary!.FindParticleSystem(id);
		if (def == null) {
			Warning($"Attempted to create unknown particle system id {id}\n");
			return null;
		}

		ParticleCollection collection = new();
		collection.Init(def, delay, randomSeed);
		return collection;
	}

	const int NUM_KILL_LISTS = 16;
	readonly int[]?[] FreeKillLists = new int[]?[NUM_KILL_LISTS];
	int NumFreeKillLists = -1;
	readonly Lock KillListLock = new();

	internal void AttachKillList(ParticleCollection particles) {
		lock (KillListLock) {
			if (NumFreeKillLists < 0) {
				for (int i = 0; i < NUM_KILL_LISTS; i++)
					FreeKillLists[i] = new int[MAX_PARTICLES_IN_A_SYSTEM];
				NumFreeKillLists = NUM_KILL_LISTS;
			}

			if (NumFreeKillLists == 0)
				Error("Out of particle kill lists!\n");

			particles.NumParticlesToKill = 0;
			particles.ParticleKillList = FreeKillLists[--NumFreeKillLists];
			FreeKillLists[NumFreeKillLists] = null;
		}
	}

	internal void DetachKillList(ParticleCollection particles) {
		lock (KillListLock) {
			FreeKillLists[NumFreeKillLists++] = particles.ParticleKillList;
			particles.ParticleKillList = null;
		}
	}

	public void PrecacheParticleSystem(string? name) {
		if (string.IsNullOrEmpty(name))
			return;

		ParticleSystemDefinition? def = Dictionary!.FindParticleSystem(name);
		if (def == null) {
			Warning($"Attemped to precache unknown particle system \"{name}\"!\n");
			return;
		}

		if (!def.IsPrecached)
			def.Precache();
	}

	public void UncacheAllParticleSystems() {
		if (Dictionary == null)
			return;

		foreach (ParticleSystemDefinition def in Dictionary.AllDefinitions().ToArray())
			def.Uncache();
	}

	public void RecreateDictionary() => Dictionary = new ParticleSystemDictionary(this);
}
