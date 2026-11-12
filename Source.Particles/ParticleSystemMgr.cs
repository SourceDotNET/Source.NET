using Datamodel;

using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

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

		RandSIMD.Seed(12345678);
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

	readonly Dictionary<string, Sheet?> SheetList = new(StringComparer.OrdinalIgnoreCase);

	public Sheet? FindOrLoadSheet(string fileName, ITexture texture) {
		if (!ShouldLoadSheets_)
			return null;

		if (SheetList.TryGetValue(fileName, out Sheet? sheet))
			return sheet;

		Span<byte> data = texture.GetResourceData(VTF_RSRC_SHEET);
		sheet = data.IsEmpty ? null : new Sheet(data);
		SheetList[fileName] = sheet;
		return sheet;
	}

	public Sheet? FindOrLoadSheet(IMaterial? material) {
		if (material == null)
			return null;

		IMaterialVar var = material.FindVar("$basetexture", out bool found, true);
		if (!found || var == null || !var.IsTexture())
			return null;

		ITexture? texture = var.GetTextureValue();
		if (texture == null || texture.IsError())
			return null;

		return FindOrLoadSheet(new string(texture.GetName()), texture);
	}

	public void FlushAllSheets() => SheetList.Clear();

	const uint VTF_RSRC_SHEET = 0x10;

	static bool DidAddSimulationOperators;
	static bool DidAddRenderingOperators;

	public void AddBuiltinSimulationOperators() {
		if (DidAddSimulationOperators)
			return;
		DidAddSimulationOperators = true;
		BuiltinParticleOperators.AddBuiltInParticleOperators(this);
		BuiltinParticleInitializers.AddBuiltInParticleInitializers(this);
		BuiltinParticleEmitters.AddBuiltInParticleEmitters(this);
		BuiltinParticleForces.AddBuiltInParticleForceGenerators(this);
		BuiltinParticleConstraints.AddBuiltInParticleConstraints(this);
	}

	public void AddBuiltinRenderingOperators() {
		if (DidAddRenderingOperators)
			return;
		DidAddRenderingOperators = true;
		BuiltinParticleRenderers.AddBuiltInParticleRenderers(this);
	}

	int ParticleVertexCount;
	int ParticleIndexCount;
	bool FrameWarningNeeded;

	public void TallyParticlesRendered(int vertexCount, int indexCount = 0) {
		int previous = ParticleVertexCount;
		ParticleIndexCount += indexCount;
		ParticleVertexCount += vertexCount;
		if (previous + vertexCount > 50000)
			FrameWarningNeeded = true;
	}

	sealed class RenderCacheEntry
	{
		public IMaterial? Material;
		public readonly List<ParticleCollection> Collections = [];
	}

	struct BatchStep
	{
		public ParticleCollection Particles;
		public int RendererIndex;
		public int FirstParticle;
		public int ParticleCount;
		public int VertCount;
	}

	sealed class RenderCacheBatch
	{
		public readonly List<BatchStep> Steps = [];
		public int VertCount;
		public int IndexCount;
	}

	readonly List<RenderCacheEntry> RenderCache = [];
	readonly List<RenderCacheBatch> Batches = [];

	public void ResetRenderCache() {
		foreach (RenderCacheEntry entry in RenderCache)
			entry.Collections.Clear();
	}

	public void AddToRenderCache(ParticleCollection particles) {
		if (!particles.IsValid())
			return;

		IMaterial material = particles.Def!.GetMaterial()!;
		if (material.IsTranslucent())
			return;

		particles.NextSleepTime = Math.Max(particles.NextSleepTime, GetLastSimulationTime() + particles.Def.NoDrawTimeToGoToSleep);

		RenderCacheEntry? cache = null;
		foreach (RenderCacheEntry entry in RenderCache) {
			if (entry.Material == material) {
				cache = entry;
				break;
			}
		}

		if (cache == null) {
			cache = new() { Material = material };
			RenderCache.Add(cache);
		}

		cache.Collections.Add(particles);

		for (ParticleCollection? child = particles.Children; child != null; child = child.Next)
			AddToRenderCache(child);
	}

	void BuildBatchList(RenderCacheEntry cache, IMatRenderContext renderContext) {
		Batches.Clear();

		int maxVertices = renderContext.GetMaxVerticesToRender(cache.Material);
		int maxIndices = renderContext.GetMaxIndicesToRender();

		RenderCacheBatch batch = new();
		Batches.Add(batch);

		foreach (ParticleCollection particles in cache.Collections) {
			int rendererCount = particles.GetRendererCount();
			if (!particles.IsValid() || rendererCount <= 0)
				continue;

			for (int i = 0; i < rendererCount; i++) {
				ParticleOperatorInstance renderer = particles.GetRenderer(i)!;
				int firstParticle = 0;
				while (firstParticle < particles.ActiveParticles) {
					int count = renderer.GetParticlesToRender(particles, particles.GetRendererContext(i), firstParticle, maxVertices, maxIndices, out int vertCount, out int indexCount);
					int stepFirst = firstParticle;
					firstParticle += count;

					if (count < 1) {
						if (batch.VertCount == 0)
							break;
						batch = new();
						Batches.Add(batch);
						if (firstParticle >= particles.ActiveParticles)
							break;
						continue;
					}

					batch.Steps.Add(new() {
						Particles = particles,
						RendererIndex = i,
						FirstParticle = stepFirst,
						ParticleCount = count,
						VertCount = vertCount
					});
					batch.VertCount += vertCount;
					batch.IndexCount += indexCount;
				}
			}
		}

		RenderCacheBatch last = Batches[^1];
		if (last.VertCount <= 0 || last.IndexCount <= 0)
			Batches.RemoveAt(Batches.Count - 1);
	}

	public void DrawRenderCache(bool shadowDepth) {
		if (RenderCache.Count == 0)
			return;

		using MatRenderContextPtr renderContext = new(Singleton<IMaterialSystem>());

		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		foreach (RenderCacheEntry cache in RenderCache) {
			if (cache.Collections.Count == 0)
				continue;

			IMaterial? material = shadowDepth ? ShadowDepthMaterial : cache.Material;
			BuildBatchList(cache, renderContext.Context);
			if (Batches.Count == 0)
				continue;

			renderContext.Bind(material);
			IMesh mesh = renderContext.GetDynamicMesh(true)!;
			foreach (RenderCacheBatch batch in Batches) {
				TallyParticlesRendered(batch.VertCount * 3, batch.IndexCount * 3);

				MeshBuilder meshBuilder = new();
				meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, batch.VertCount, batch.IndexCount);

				int vertexOffset = 0;
				foreach (BatchStep step in batch.Steps) {
					ParticleOperatorInstance renderer = step.Particles.GetRenderer(step.RendererIndex)!;
					renderer.RenderUnsorted(step.Particles, step.Particles.GetRendererContext(step.RendererIndex), renderContext.Context, ref meshBuilder, vertexOffset, step.FirstParticle, step.ParticleCount);
					vertexOffset += step.VertCount;
				}

				meshBuilder.End();
				mesh.Draw();
			}
		}

		ResetRenderCache();

		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PopMatrix();
	}
}
