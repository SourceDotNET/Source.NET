global using static Game.Server.SoundscapeSystemGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.Mathematics;
using Source.Common.Server;

using System.Numerics;

namespace Game.Server;

public struct ClusterSoundscapeList
{
	public ushort SoundscapeCount;
	public ushort FirstSoundscape;
}

public class StringRegistry
{
	readonly List<KeyValuePair<string, int>> StringList = [];

	public ushort AddString(ReadOnlySpan<char> stringText, int stringID) {
		StringList.Add(new(new(stringText), stringID));
		return (ushort)(StringList.Count - 1);
	}

	public int GetStringID(ReadOnlySpan<char> stringText) {
		foreach (var kvp in StringList)
			if (stringText.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase))
				return kvp.Value;

		return -1;
	}

	public ReadOnlySpan<char> GetStringText(int stringID) {
		foreach (var kvp in StringList)
			if (kvp.Value == stringID)
				return kvp.Key;

		return null;
	}

	public ReadOnlySpan<char> GetStringForKey(ushort key) {
		if (key >= StringList.Count)
			return null;
		return StringList[key].Key;
	}

	public int GetIDForKey(ushort key) {
		if (key >= StringList.Count)
			return 0;
		return StringList[key].Value;
	}

	public void ClearStrings() => StringList.Clear();

	public ushort First() => StringList.Count > 0 ? (ushort)0 : InvalidIndex();
	public ushort Next(ushort key) => key + 1 < StringList.Count ? (ushort)(key + 1) : InvalidIndex();
	public ushort InvalidIndex() => ushort.MaxValue;
}

public static class SoundscapeSystemGlobals
{
	public const string SOUNDSCAPE_MANIFEST_FILE = "scripts/soundscapes_manifest.txt";

	public static readonly SoundscapeSystem g_SoundscapeSystem = new("CSoundscapeSystem");

	[ConCommand("soundscape_flush", "Flushes the server & client side soundscapes")]
	static void soundscape_flush() {
		BasePlayer? player = ToBasePlayer(Util.GetCommandClient());
		if (engine.IsDedicatedServer()) {
			// If it's a dedicated server, only the server console can run this.
			if (player != null)
				return;
		}
		else {
			// If it's a listen server, only the listen server host can run this.
			if (player == null || player != Util.GetListenServerHost())
				return;
		}

		g_SoundscapeSystem.FlushSoundscapes();  // don't bother forgetting about the entities
		g_SoundscapeSystem.Init();


		if (engine.IsDedicatedServer()) {
			// If the ds console typed it, send it to everyone.
			for (int i = 1; i <= gpGlobals.MaxClients; i++) {
				BasePlayer? sendToPlayer = Util.PlayerByIndex(i);
				if (sendToPlayer != null)
					engine.ClientCommand(sendToPlayer.Edict()!, "cl_soundscape_flush\n");
			}
		}
		else
			engine.ClientCommand(player!.Edict()!, "cl_soundscape_flush\n");
	}

	[ConCommand("sv_soundscape_printdebuginfo", "print soundscapes", FCvar.DevelopmentOnly)]
	static void sv_soundscape_printdebuginfo() {
		if (!Util.IsCommandIssuedByServerAdmin())
			return;

		g_SoundscapeSystem.PrintDebugInfo();
	}
}

public class SoundscapeSystem(ReadOnlySpan<char> name) : AutoGameSystemPerFrame(name)
{
	readonly StringRegistry Soundscapes = new();
	int SoundscapeCount;
	readonly List<EnvSoundscape> SoundscapeEntities = [];
	readonly List<ClusterSoundscapeList> SoundscapesInCluster = [];
	readonly List<ushort> SoundscapeIndexList = [];
	int ActiveIndex;

	public void AddSoundscapeFile(ReadOnlySpan<char> filename) {
		// Open the soundscape data file, and abort if we can't
		KeyValues keyValuesData = new(filename);
		if (filesystem.LoadKeyValues(keyValuesData, IFileSystem.KeyValuesPreloadType.SoundScape, filename, "GAME")) {
			// parse out all of the top level sections and save their names
			KeyValues? keys = keyValuesData;
			while (keys != null) {
				if (keys.GetFirstSubKey() != null) {
					if (developer.GetBool()) {
						if (keys.Name.Contains('{'))
							Msg($"Error parsing soundscape file {filename} after {(SoundscapeCount > 0 ? Soundscapes.GetStringText(SoundscapeCount - 1) : "FIRST")}\n");
					}
					Soundscapes.AddString(keys.Name, SoundscapeCount);

					SoundscapeCount++;
				}
				keys = keys.GetNextKey();
			}
		}
	}

	public void PrintDebugInfo() {
		Msg("\n------- SERVER SOUNDSCAPES -------\n");
		for (ushort key = Soundscapes.First(); key != Soundscapes.InvalidIndex(); key = Soundscapes.Next(key)) {
			int id = Soundscapes.GetIDForKey(key);
			ReadOnlySpan<char> name = Soundscapes.GetStringForKey(key);

			Msg($"- {id}: {name}\n");
		}
		Msg("-------- SOUNDSCAPE ENTITIES -----\n");
		for (int entityIndex = 0; entityIndex < SoundscapeEntities.Count; ++entityIndex) {
			EnvSoundscape currentSoundscape = SoundscapeEntities[entityIndex];
			Msg($"- {entityIndex}: {currentSoundscape.GetSoundscapeName()} x:{currentSoundscape.GetAbsOrigin().X:F4} y:{currentSoundscape.GetAbsOrigin().Y:F4} z:{currentSoundscape.GetAbsOrigin().Z:F4}\n");
		}
		Msg("----------------------------------\n\n");
	}

	public override bool Init() {
		SoundscapeCount = 0;

		ReadOnlySpan<char> mapname = gpGlobals.MapName;
		ReadOnlySpan<char> mapSoundscapeFilename = null;
		if (!mapname.IsEmpty)
			mapSoundscapeFilename = $"scripts/soundscapes_{mapname}.txt";

		KeyValues manifest = new(SOUNDSCAPE_MANIFEST_FILE);
		if (filesystem.LoadKeyValues(manifest, IFileSystem.KeyValuesPreloadType.SoundScape, SOUNDSCAPE_MANIFEST_FILE, "GAME")) {
			for (KeyValues? sub = manifest.GetFirstSubKey(); sub != null; sub = sub.GetNextKey()) {
				if (stricmp(sub.Name, "file") == 0) {
					// Add
					AddSoundscapeFile(sub.GetString());
					if (!mapSoundscapeFilename.IsEmpty && FStrEq(sub.GetString(), mapSoundscapeFilename))
						mapSoundscapeFilename = null; // we've already loaded the map's soundscape
					continue;
				}

				Warning($"CSoundscapeSystem::Init:  Manifest '{SOUNDSCAPE_MANIFEST_FILE}' with bogus file type '{sub.Name}', expecting 'file'\n");
			}

			if (!mapSoundscapeFilename.IsEmpty && filesystem.FileExists(mapSoundscapeFilename))
				AddSoundscapeFile(mapSoundscapeFilename);
		}
		else
			Error($"Unable to load manifest file '{SOUNDSCAPE_MANIFEST_FILE}'\n");

		ActiveIndex = 0;

		return true;
	}

	public void FlushSoundscapes() {
		SoundscapeCount = 0;
		Soundscapes.ClearStrings();
	}

	public override void Shutdown() {
		FlushSoundscapes();
		SoundscapeEntities.Clear();
		ActiveIndex = 0;
	}

	public override void LevelInitPreEntity() {
		g_SoundscapeSystem.Shutdown();
		g_SoundscapeSystem.Init();
	}

	public override void LevelInitPostEntity() {
		int clusterCount = engine.GetClusterCount();
		BBox[] clusterbounds = new BBox[clusterCount];
		engine.GetAllClusterBounds(clusterbounds);
		SoundscapesInCluster.Clear();
		for (int i = 0; i < clusterCount; i++)
			SoundscapesInCluster.Add(new() { SoundscapeCount = 0, FirstSoundscape = 0 });

		Span<byte> myPVS = new byte[16 * 1024];
		List<short> clusterIndexList = [];
		List<short> soundscapeIndexList = [];

		// find the clusters visible from each soundscape
		// add this soundscape to the list of soundscapes for that cluster, clip cluster bounds to radius
		for (int i = 0; i < SoundscapeEntities.Count; i++) {
			Vector3 position = SoundscapeEntities[i].GetAbsOrigin();
			float radius = SoundscapeEntities[i].Radius;
			float radiusSq = radius * radius;
			engine.GetPVSForCluster(engine.GetClusterForOrigin(position), myPVS);
			for (int j = 0; j < clusterCount; j++) {
				if ((myPVS[j >> 3] & (1 << (j & 7))) != 0) {
					float distSq = MathLib.CalcSqrDistanceToAABB(clusterbounds[j].Mins, clusterbounds[j].Maxs, position);
					if (distSq < radiusSq || radius < 0) {
						var entry = SoundscapesInCluster[j];
						entry.SoundscapeCount++;
						SoundscapesInCluster[j] = entry;
						clusterIndexList.Add((short)j);
						// UNDONE: Technically you just need a soundscape index and a count for this list.
						soundscapeIndexList.Add((short)i);
					}
				}
			}
		}

		// basically this part is like a radix sort
		// this is how many entries we need in the soundscape index list
		SoundscapeIndexList.Clear();
		for (int i = 0; i < soundscapeIndexList.Count; i++)
			SoundscapeIndexList.Add(0);

		// now compute the starting index of each cluster
		int firstSoundscape = 0;
		for (int i = 0; i < clusterCount; i++) {
			var entry = SoundscapesInCluster[i];
			entry.FirstSoundscape = (ushort)firstSoundscape;
			firstSoundscape += entry.SoundscapeCount;
			entry.SoundscapeCount = 0;
			SoundscapesInCluster[i] = entry;
		}
		// now add each soundscape index to the appropriate cluster's list
		// The resulting list is precomputing all soundscapes that need to be checked for a player
		// in each cluster.  This is used to accelerate the per-frame operations
		for (int i = 0; i < soundscapeIndexList.Count; i++) {
			int cluster = clusterIndexList[i];
			var entry = SoundscapesInCluster[cluster];
			int outIndex = entry.SoundscapeCount + entry.FirstSoundscape;
			entry.SoundscapeCount++;
			SoundscapesInCluster[cluster] = entry;
			SoundscapeIndexList[outIndex] = (ushort)soundscapeIndexList[i];
		}
	}

	public int GetSoundscapeIndex(ReadOnlySpan<char> name) {
		return Soundscapes.GetStringID(name);
	}

	public bool IsValidIndex(int index) {
		if (index >= 0 && index < SoundscapeCount)
			return true;
		return false;
	}

	public void AddSoundscapeEntity(EnvSoundscape soundscape) {
		if (!SoundscapeEntities.Contains(soundscape)) {
			SoundscapeEntities.Add(soundscape);
			soundscape.SoundscapeEntityId = SoundscapeEntities.Count;
		}
	}

	public void RemoveSoundscapeEntity(EnvSoundscape soundscape) {
		SoundscapeEntities.Remove(soundscape);
		soundscape.SoundscapeEntityId = -1;
	}

	public override void FrameUpdatePostEntityThink() {
		int total = SoundscapeEntities.Count;
		if (total > 0) {
			int traceCount = 0;
			int playerCount = 0;
			// budget tuned for TF.  Do a max of 20 traces.  That's going to happen anyway because a bunch of the maps
			// use radius -1 for all soundscapes.  So to trace one player you'll often need that many and this code must
			// always trace one player's soundscapes.
			// If the map has been optimized, then allow more players to update per frame.
			int maxPlayers = gpGlobals.MaxClients / 2;
			// maxPlayers has to be at least 1
			maxPlayers = Math.Max(1, maxPlayers);
			int maxTraces = 20;
			if (EnvSoundscape.soundscape_debug.GetBool()) {
				maxTraces = 9999;
				maxPlayers = Constants.MAX_PLAYERS;
			}

			// load balance across server ticks a bit by limiting the numbers of players (get cluster for origin)
			// and traces processed in a single tick.  In single player this will update the player every tick
			// because it always does at least one player's full load of work
			for (int i = 0; i < gpGlobals.MaxClients && traceCount <= maxTraces && playerCount <= maxPlayers; i++) {
				ActiveIndex = (ActiveIndex + 1) % gpGlobals.MaxClients;
				BasePlayer? player = Util.PlayerByIndex(ActiveIndex + 1);
				if (player != null && player.IsNetClient()) {
					// check to see if this is the sound entity that is
					// currently affecting this player
					ref readonly AudioParams audio = ref player.GetAudioParams();

					// if we got this far, we're looking at an entity that is contending
					// for current player sound. the closest entity to player wins.
					EnvSoundscape? current = (EnvSoundscape?)audio.Ent.Get();
					if (current != null) {
						int entIndex = current.SoundscapeEntityId - 1;
						Assert(SoundscapeEntities[entIndex] == current);
					}
					SoundscapeUpdate update = new() {
						Player = player,
						CurrentSoundscape = current,
						PlayerPosition = player.EarPosition(),
						InRange = false,
						CurrentDistance = 0,
						TraceCount = 0
					};
					current?.UpdateForPlayer(ref update);

					int clusterIndex = engine.GetClusterForOrigin(update.PlayerPosition);

					if (clusterIndex >= 0 && clusterIndex < SoundscapesInCluster.Count) {
						// find all soundscapes that could possibly attach to this player and update them
						for (int j = 0; j < SoundscapesInCluster[clusterIndex].SoundscapeCount; j++) {
							int ssIndex = SoundscapeIndexList[SoundscapesInCluster[clusterIndex].FirstSoundscape + j];
							if (SoundscapeEntities[ssIndex] == update.CurrentSoundscape)
								continue;
							SoundscapeEntities[ssIndex].UpdateForPlayer(ref update);
						}
					}
					playerCount++;
					traceCount += update.TraceCount;
				}
			}
		}
	}
}
