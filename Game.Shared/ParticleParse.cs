global using static Game.Shared.ParticleParse;

using Source.Common;
using Source.Common.Formats.Keyvalues;
using Source.Particles;

using System.Numerics;

namespace Game.Shared;

public enum ParticleAttachment : byte
{
	AbsOrigin = 0,
	AbsOriginFollow,
	CustomOrigin,
	Point,
	PointFollow,
	WorldOrigin,
	RootBoneFollow,

	Max,
}

public struct ParticleEffectsColors
{
	[NetworkVar("m_vecColor1")]
	public Vector3 Color1;
	[NetworkVar("m_vecColor2")]
	public Vector3 Color2;
}

public struct ParticleEffectsControlPoint
{
	[NetworkVar("m_eParticleAttachment")]
	public byte ParticleAttachment;
	[NetworkVar("m_vecOffset")]
	public Vector3 Offset;
}

public static class ParticleParse
{
	public const string PARTICLES_MANIFEST_FILE = "particles/particles_manifest.txt";

	public static readonly ParticleSystemMgr g_pParticleSystemMgr = new();

	public static void GetParticleManifest(List<string> list) {
		KeyValues manifest = new(PARTICLES_MANIFEST_FILE);
		if (manifest.LoadFromFile(filesystem, PARTICLES_MANIFEST_FILE, "GAME")) {
			for (KeyValues? sub = manifest.GetFirstSubKey(); sub != null; sub = sub.GetNextKey()) {
				if (sub.Name.Equals("file", StringComparison.OrdinalIgnoreCase)) {
					list.Add(sub.GetString().ToString());
					continue;
				}

				Warning($"CParticleMgr::Init:  Manifest '{PARTICLES_MANIFEST_FILE}' with bogus file type '{sub.Name}', expecting 'file'\n");
			}
		}
		else
			Warning($"PARTICLE SYSTEM: Unable to load manifest file '{PARTICLES_MANIFEST_FILE}'\n");
	}

	public static void ParseParticleEffects(bool loadSheets, bool precache) {
		g_pParticleSystemMgr.ShouldLoadSheets(loadSheets);

		List<string> files = [];
		GetParticleManifest(files);

		foreach (string file in files)
			g_pParticleSystemMgr.ReadParticleConfigFile(file, precache, false);

		g_pParticleSystemMgr.DecommitTempMemory();
	}

	public static void ParseParticleEffectsMap(ReadOnlySpan<char> mapName, bool loadSheets) {
		List<string> files = [];
		string mapManifestFilename = mapName.IsEmpty ? "" : $"maps/{mapName}_particles.txt";

		KeyValues manifest = new(mapManifestFilename);
		if (!manifest.LoadFromFile(filesystem, mapManifestFilename, "GAME"))
			return;

		DevMsg($"Successfully loaded particle effects manifest '{mapManifestFilename}' for map '{mapName}'\n");
		for (KeyValues? sub = manifest.GetFirstSubKey(); sub != null; sub = sub.GetNextKey()) {
			if (sub.Name.Equals("file", StringComparison.OrdinalIgnoreCase)) {
				string file = sub.GetString().ToString();
				int lastSlash = file.LastIndexOfAny(['/', '\\']);
				ReadOnlySpan<char> path = lastSlash >= 0 ? file.AsSpan(0, lastSlash) : [];
				if (path.StartsWith("!"))
					path = path[1..];

				if (path.Equals("particles", StringComparison.OrdinalIgnoreCase)) {
					files.Add(file);
					continue;
				}

				Warning($"CParticleMgr::LevelInit:  Manifest '{mapManifestFilename}' contains a particle file '{file}' that's not under the particles directory. Custom particles must be placed in the particles directory.\n");
			}
			else
				Warning($"CParticleMgr::LevelInit:  Manifest '{mapManifestFilename}' with bogus file type '{sub.Name}', expecting 'file'\n");
		}

		if (files.Count == 0)
			return;

		g_pParticleSystemMgr.ShouldLoadSheets(loadSheets);

		foreach (string file in files)
			g_pParticleSystemMgr.ReadParticleConfigFile(file, true, true);

		g_pParticleSystemMgr.DecommitTempMemory();
	}

#if GAME_DLL
	public static void PrecacheStandardParticleSystems() {
		for (int i = 0; i < g_pParticleSystemMgr.GetParticleSystemCount(); i++) {
			string particleSystemName = g_pParticleSystemMgr.GetParticleSystemNameFromIndex(i);
			ParticleSystemDefinition? particleSystem = g_pParticleSystemMgr.FindParticleSystem(particleSystemName);
			if (particleSystem != null && particleSystem.ShouldAlwaysPrecache())
				Game.Server.GameInterface.PrecacheParticleSystem(particleSystemName);
		}
	}
#endif
}
