global using static Game.Shared.ParticleParse;

using Source.Common;
using Source.Common.Formats.Keyvalues;
using Source.Common.Mathematics;
using Source.Particles;

using System.Numerics;

#if CLIENT_DLL
using Game.Client;
#elif GAME_DLL
using Game.Server;
#endif

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

	public static void GMOD_LoadParticleConfigFile(string file) {
		g_pParticleSystemMgr.ShouldLoadSheets(true);
		g_pParticleSystemMgr.ReadParticleConfigFile(file, true, false);
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

	static readonly string?[] AttachmentNames = [
		"start_at_origin",
		"follow_origin",
		"start_at_customorigin",
		"start_at_attachment",
		"follow_attachment",
		"follow_rootbone",
		null,
	];

	public static int GetAttachTypeFromString(ReadOnlySpan<char> str) {
		if (str.IsEmpty)
			return -1;

		for (int i = 0; i < (int)ParticleAttachment.Max; i++) {
			if (AttachmentNames[i] != null && str.Equals(AttachmentNames[i], StringComparison.OrdinalIgnoreCase))
				return i;
		}

		return -1;
	}

	public const int PARTICLE_DISPATCH_FROM_ENTITY = 1 << 0;
	public const int PARTICLE_DISPATCH_RESET_PARTICLES = 1 << 1;

	public static int ParticleSystemIndex(ReadOnlySpan<char> particleName) {
#if CLIENT_DLL
		return Game.Client.HLClient.GetParticleSystemIndex(particleName);
#else
		return Game.Server.GameInterface.GetParticleSystemIndex(particleName);
#endif
	}

	static void SetEffectEntity(EffectData data, BaseEntity? entity) {
#if CLIENT_DLL
		data.Entity = entity != null ? entity.GetRefEHandle() : new BaseHandle();
#else
		data.EntIndex = entity != null ? entity.EntIndex() : 0;
#endif
	}

	public static void DispatchParticleEffect(ReadOnlySpan<char> particleName, ParticleAttachment attachType, BaseEntity? entity, ReadOnlySpan<char> attachmentName, bool resetAllParticlesOnEntity = false) {
		int attachment = -1;
		BaseAnimating? animating = entity?.GetBaseAnimating();
		if (animating != null) {
			attachment = animating.LookupAttachment(attachmentName);
			if (attachment <= 0)
				return;
		}

		DispatchParticleEffect(particleName, attachType, entity, attachment, resetAllParticlesOnEntity);
	}

	public static void DispatchParticleEffect(ReadOnlySpan<char> particleName, ParticleAttachment attachType, BaseEntity? entity = null, int attachmentPoint = -1, bool resetAllParticlesOnEntity = false) {
		EffectData data = new();

		data.HitBox = ParticleSystemIndex(particleName);
		if (entity != null) {
			SetEffectEntity(data, entity);
			data.Flags |= PARTICLE_DISPATCH_FROM_ENTITY;
			data.Origin = entity.GetAbsOrigin();
		}
		data.DamageType = (int)attachType;
		data.AttachmentIndex = attachmentPoint;

		if (resetAllParticlesOnEntity)
			data.Flags |= PARTICLE_DISPATCH_RESET_PARTICLES;

#if GAME_DLL
		if ((data.Flags & PARTICLE_DISPATCH_FROM_ENTITY) != 0 && (attachType == ParticleAttachment.AbsOriginFollow || attachType == ParticleAttachment.PointFollow || attachType == ParticleAttachment.RootBoneFollow)) {
			ReliableBroadcastRecipientFilter filter = new();
			DispatchEffect("ParticleEffect", data, filter);
			return;
		}
#endif
		DispatchEffect("ParticleEffect", data);
	}

	public static void DispatchParticleEffect(int effectIndex, in Vector3 origin, in Vector3 start, in QAngle angles, BaseEntity? entity = null) {
		EffectData data = new() {
			HitBox = effectIndex,
			Origin = origin,
			Start = start,
			Angles = angles
		};

		SetEffectEntity(data, entity);
		if (entity != null) {
			data.Flags |= PARTICLE_DISPATCH_FROM_ENTITY;
			data.DamageType = (int)ParticleAttachment.CustomOrigin;
		}

		DispatchEffect("ParticleEffect", data);
	}

	public static void DispatchParticleEffect(ReadOnlySpan<char> particleName, in Vector3 origin, in QAngle angles, BaseEntity? entity = null)
		=> DispatchParticleEffect(ParticleSystemIndex(particleName), origin, origin, angles, entity);

	public static void DispatchParticleEffect(ReadOnlySpan<char> particleName, in Vector3 origin, in Vector3 start, in QAngle angles, BaseEntity? entity = null)
		=> DispatchParticleEffect(ParticleSystemIndex(particleName), origin, start, angles, entity);

	public static void StopParticleEffects(BaseEntity? entity) {
		EffectData data = new();

		if (entity != null)
			SetEffectEntity(data, entity);

#if GAME_DLL
		ReliableBroadcastRecipientFilter filter = new();
		DispatchEffect("ParticleEffectStop", data, filter);
#else
		DispatchEffect("ParticleEffectStop", data);
#endif
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
