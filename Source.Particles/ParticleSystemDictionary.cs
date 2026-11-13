using Datamodel;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public sealed class ParticleSystemDictionary(ParticleSystemMgr mgr)
{
	readonly Dictionary<string, int> NameSymbols = new(StringComparer.OrdinalIgnoreCase);
	readonly List<string> SymbolNames = [];
	readonly List<ParticleSystemDefinition?> ParticleNameMap = [];
	readonly List<ParticleSystemDefinition> ParticleIdMap = [];

	public int NameCount() => SymbolNames.Count;
	public int Count() => ParticleIdMap.Count;
	public ParticleSystemDefinition GetParticleSystem(int index) => ParticleIdMap[index];

	public IEnumerable<ParticleSystemDefinition> AllDefinitions() {
		foreach (ParticleSystemDefinition def in ParticleIdMap)
			yield return def;
		foreach (ParticleSystemDefinition? def in ParticleNameMap)
			if (def != null)
				yield return def;
	}

	int FindSymbol(string? name) => name != null && NameSymbols.TryGetValue(name, out int symbol) ? symbol : INVALID_PARTICLE_SYSTEM_HANDLE;

	int AddSymbol(string name) {
		if (NameSymbols.TryGetValue(name, out int symbol))
			return symbol;
		symbol = SymbolNames.Count;
		NameSymbols[name] = symbol;
		SymbolNames.Add(name);
		return symbol;
	}

	public int FindParticleSystemHandle(string? name) => FindSymbol(name);

	public string? GetSymbolName(int handle) => handle >= 0 && handle < SymbolNames.Count ? SymbolNames[handle] : null;

	public ParticleSystemDefinition? FindParticleSystem(int handle)
		=> handle != INVALID_PARTICLE_SYSTEM_HANDLE && handle >= 0 && handle < ParticleNameMap.Count ? ParticleNameMap[handle] : null;

	public ParticleSystemDefinition? FindParticleSystem(string? name) => FindParticleSystem(FindSymbol(name));

	public ParticleSystemDefinition? FindParticleSystem(Guid id) {
		foreach (ParticleSystemDefinition def in ParticleIdMap)
			if (def.GetId() == id)
				return def;
		return null;
	}

	static bool PreventNameBasedLookup(Element element)
		=> element.TryGetValue("preventNameBasedLookup", out object? value) && value is true;

	void SetNameMap(int symbol, ParticleSystemDefinition? def) {
		while (ParticleNameMap.Count <= symbol)
			ParticleNameMap.Add(null);
		ParticleNameMap[symbol] = def;
	}

	void DestroyExistingElement(Element element) {
		if (PreventNameBasedLookup(element)) {
			int index = ParticleIdMap.FindIndex(d => d.GetId() == element.ID);
			if (index < 0)
				return;
			ParticleIdMap[index] = ParticleIdMap[^1];
			ParticleIdMap.RemoveAt(ParticleIdMap.Count - 1);
			return;
		}

		int symbol = FindSymbol(element.Name);
		if (symbol == INVALID_PARTICLE_SYSTEM_HANDLE)
			return;
		SetNameMap(symbol, null);
	}

	public ParticleSystemDefinition? AddParticleSystem(Element particleSystem) {
		if (!string.Equals(particleSystem.ClassName, ParticleSystemDefinition.DmeParticleSystemDefinition, StringComparison.OrdinalIgnoreCase))
			return null;

		DestroyExistingElement(particleSystem);

		ParticleSystemDefinition def = new(mgr);
		if (PreventNameBasedLookup(particleSystem))
			ParticleIdMap.Add(def);
		else
			SetNameMap(AddSymbol(particleSystem.Name), def);

		def.Read(particleSystem);
		return def;
	}
}
