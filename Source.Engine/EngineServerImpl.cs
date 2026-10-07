using CommunityToolkit.HighPerformance;

using Source.Common;
using Source.Common.Audio;
using Source.Common.Bitbuffers;
using Source.Common.Client;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod;
using Source.Common.Mathematics;
using Source.Common.Networking;
using Source.Common.Server;
using Source.Engine.Server;

using Steamworks;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;

namespace Source.Engine;

internal class EngineServer(Cbuf Cbuf, Host Host) : IEngineServer
{
	public void AddOriginToPVS(in Vector3 origin) => SV.AddOriginToPVS(origin);

	public void AllowImmediateEdictReuse() => ED.AllowImmediateReuse();

	const int MAX_TOTAL_ENT_LEAFS = 128;

	public void BuildEntityClusterList(Edict edict, ref PVSInfo pvsInfo) {
		int i, j;
		int area;

		CleanUpEntityClusterList(ref pvsInfo);
		pvsInfo.Clusters = null;
		pvsInfo.ClusterCount = 0;
		pvsInfo.AreaNum = 0;
		pvsInfo.AreaNum2 = 0;
		if (edict == null)
			return;

		ICollideable? collideable = edict.GetCollideable();
		Assert(collideable != null);
		if (collideable == null)
			return;

		Span<int> leafs = stackalloc int[MAX_TOTAL_ENT_LEAFS];
		Span<int> clusters = stackalloc int[MAX_TOTAL_ENT_LEAFS];

		collideable.WorldSpaceSurroundingBounds(out Vector3 worldMins, out Vector3 worldMaxs);
		int leafCount = CM.BoxLeafnums(worldMins, worldMaxs, leafs, out int topnode);

		for (i = 0; i < leafCount; i++) {
			clusters[i] = CM.LeafCluster(leafs[i]);
			area = CM.LeafArea(leafs[i]);
			if (area == 0)
				continue;

			if (pvsInfo.AreaNum != 0 && pvsInfo.AreaNum != area) {
				if (pvsInfo.AreaNum2 != 0 && pvsInfo.AreaNum2 != area && sv.IsLoading())
					ConDMsg($"Object touching 3 areas at {worldMins.X} {worldMins.Y} {worldMins.Z}\n");
				pvsInfo.AreaNum2 = (short)area;
			}
			else
				pvsInfo.AreaNum = (short)area;
		}

		pvsInfo.HeadNode = (short)topnode;
		pvsInfo.Center = (worldMins + worldMaxs) * 0.5f;

		if (leafCount >= MAX_TOTAL_ENT_LEAFS) {
			pvsInfo.ClusterCount = -1;
			return;
		}

		if (leafCount >= 16) {
			clusters[..leafCount].Sort();
			for (i = 0; i < leafCount; i++) {
				if (clusters[i] == -1)
					continue;

				if ((i > 0) && (clusters[i] == clusters[i - 1]))
					continue;

				if (!pvsInfo.AddCluster((ushort)clusters[i]))
					break;
			}
			return;
		}

		for (i = 0; i < leafCount; i++) {
			if (clusters[i] == -1)
				continue;

			for (j = 0; j < i; j++) {
				if (clusters[j] == clusters[i])
					break;
			}

			if (j != i)
				continue;

			if (!pvsInfo.AddCluster((ushort)clusters[i]))
				break;
		}
	}

	static readonly SharedEdictChangeInfo g_SharedEdictChangeInfo = new();
	public static readonly SharedEdictChangeInfo g_pSharedEdictChangeInfo = g_SharedEdictChangeInfo;

	public static void InvalidateSharedEdictChangeInfos() {
		if (g_SharedEdictChangeInfo.SerialNumber == 0xFFFF) {
			g_SharedEdictChangeInfo.SerialNumber = 1;
			for (int i = 0; i < sv.NumEdicts; i++)
				sv.Edicts![i].SetChangeInfoSerialNumber(0);
		}
		else
			g_SharedEdictChangeInfo.SerialNumber++;

		g_SharedEdictChangeInfo.NumChangeInfos = 0;
	}

	public void ChangeLevel(ReadOnlySpan<char> s1, ReadOnlySpan<char> s2) {
		Span<char> cmd = stackalloc char[256];
		Span<char> s1Escaped = stackalloc char[256];
		Span<char> s2Escaped = stackalloc char[256];

		if (!Cbuf.EscapeCommandArg(s1, s1Escaped) || (!s2.IsEmpty && !Cbuf.EscapeCommandArg(s2, s2Escaped))) {
			Warning("Illegal map name in ChangeLevel\n");
			return;
		}

		int cmdLen = 0;
		if (s2.IsEmpty)
			cmdLen = sprintf(cmd, "changelevel %s\n").S(s1Escaped);
		else
			cmdLen = sprintf(cmd, "changelevel %s %s\n").S(s1Escaped).S(s2Escaped);

		if (cmdLen >= 256) {
			Warning("Paramter overflow in ChangeLevel\n");
			return;
		}

		cbuf.AddText(cmd);
	}

	public void ChangeTeam(ReadOnlySpan<char> pTeamName) {
#if !SWDS
		throw new NotImplementedException();
#endif
	}

	public int CheckAreasConnected(int area1, int area2) => CM.AreasConnected(area1, area2);

	public bool CheckBoxInPVS(in Vector3 mins, in Vector3 maxs, ReadOnlySpan<byte> checkpvs) {
		if (!CM.BoxVisible(mins, maxs, checkpvs))
			return false;

		return true;
	}

	public int CheckHeadnodeVisible(int nodenum, ReadOnlySpan<byte> pvs) =>CM.HeadnodeVisible(nodenum, pvs) ? 1 : 0;

	public bool CheckOriginInPVS(in Vector3 org, ReadOnlySpan<byte> checkpvs) {
		int clusterIndex = CM.LeafCluster(CM.PointLeafnum(org));

		if (clusterIndex < 0)
			return false;

		int offset = clusterIndex >> 3;
		if (offset > checkpvs.Length) {
			Sys.Error($"CheckOriginInPVS:  cluster would read past end of pvs data ({offset}:{checkpvs.Length})\n");
			return false;
		}

		if ((checkpvs[offset] & (1 << (clusterIndex & 7))) == 0)
			return false;

		return true;
	}

	public void CleanUpEntityClusterList(ref PVSInfo pvsInfo) {
		if (pvsInfo.ClusterCount > CheckTransmitInfo.MAX_FAST_ENT_CLUSTERS) {
			pvsInfo.Clusters = null;
			pvsInfo.ClusterCount = 0;
		}
	}

	public void ClearSaveDir() {
		// todo saverestore.ClearSaveDir();
	}

	public void ClearSaveDirAfterClientLoad() {
		throw new NotImplementedException();
	}

	public void ClientCommand(Edict edict, ReadOnlySpan<char> cmd) {
		if (cmd.IsEmpty) {
			Warning("ClientCommand, 0 length string supplied.\n");
			return;
		}

		int entnum = NUM_FOR_EDICT(edict);

		if (entnum < 1 || entnum > sv.GetClientCount()) {
			ConMsg($"\n!!!\n\nStuffCmd:  Some entity tried to stuff '{cmd}' to console buffer of entity {entnum} when maxclients was set to {sv.GetMaxClients()}, ignoring\n\n");
			return;
		}

		NET_StringCmd stringCmd = new(new(cmd));
		sv.GetClient(entnum - 1)!.SendNetMsg(stringCmd);
	}

	public void ClientCommandKeyValues(Edict edict, KeyValues command) {
		throw new NotImplementedException();
	}

	public void ClientPrintf(Edict edict, ReadOnlySpan<char> szMsg) {
		int entnum = NUM_FOR_EDICT(edict);
		if (entnum < 1 || entnum > sv.GetClientCount()) {
			ConMsg("tried to sprint to a non-client\n");
			return;
		}

		sv.Client(entnum - 1).ClientPrintf(szMsg);
	}

	public int CompareFileTime(ReadOnlySpan<char> filename1, ReadOnlySpan<char> filename2, ref int compare) {
		DateTime ft1 = g_pFileSystem.GetFileTime(filename1);
		DateTime ft2 = g_pFileSystem.GetFileTime(filename2);

		compare = ft1.CompareTo(ft2);
		return 1;
	}

#if SWDS
	public void Con_NPrintf(int pos, ReadOnlySpan<char> msg) {}
	public void Con_NXPrintf(in Con_NPrint_s info, ReadOnlySpan<char> msg) {}
#else
	public void Con_NPrintf(int pos, ReadOnlySpan<char> msg) {
		if (IsDedicatedServer())
			return;

		Con.NPrintF(pos, msg);
	}

	public void Con_NXPrintf(in Con_NPrint_s info, ReadOnlySpan<char> msg) {
		Con.NXPrintF(in info, msg);
	}
#endif

	public bool CopyFile(ReadOnlySpan<char> source, ReadOnlySpan<char> destination) => Common.CopyFile(source, destination);

	public Edict? CreateEdict(int forceEdictIndex = -1) {
		Edict? edict = ED.Alloc(forceEdictIndex);

		serverPluginHandler.OnEdictAllocated(edict);

		return edict;
	}

	public Edict? CreateFakeClient(ReadOnlySpan<char> netname) {
		GameClient? fcl = (GameClient?)sv.CreateFakeClient(netname);
		return fcl?.Edict;
	}

	public Edict? CreateFakeClientEx(ReadOnlySpan<char> netname, bool reportFakeClient = true) {
		sv.SetReportNewFakeClients(reportFakeClient);
		Edict? ret = CreateFakeClient(netname);
		sv.SetReportNewFakeClients(true);
		return ret;
	}

	public ISpatialPartition CreateSpatialPartition(in Vector3 worldmin, in Vector3 worldmax) {
		throw new NotImplementedException();
	}

	public void CrosshairAngle(Edict clientent, float pitch, float yaw) {

	}

	public void DestroySpatialPartition(ISpatialPartition spatialPartition) {
		throw new NotImplementedException();
	}

	public void EmitAmbientSound(int entindex, in Vector3 pos, ReadOnlySpan<char> samp, float vol, SoundLevel soundlevel, int fFlags, int pitch, float soundtime = 0) {
		SoundInfo sound = default;
		sound.SetDefault();

		samp = samp.SliceNullTerminatedString();

		sound.EntityIndex = entindex;
		sound.Volume = vol;
		sound.Soundlevel = soundlevel;
		sound.Flags = (SoundFlags)fFlags;
		sound.Pitch = pitch;
		sound.Channel = SoundEntityChannel.Static;
		sound.Origin = pos;
		sound.IsAmbient = true;

		// set sound delay

		if (soundtime != 0.0f) {
			sound.Delay = soundtime - sv.GetTime();
			sound.Flags |= SoundFlags.Delay;
		}

		// if this is a sentence, get sentence number
		if (SoundCharsUtils.TestSoundChar(samp, SoundChars.Sentence)) {
			sound.IsSentence = true;
			sound.SoundNum = atoi(SoundCharsUtils.SkipSoundChars(samp));
#if !SWDS
			if (sound.SoundNum >= g_AudioSystem.SentenceCount()) {
				ConMsg($"EmitAmbientSound: invalid sentence number: {SoundCharsUtils.SkipSoundChars(samp)}");
				return;
			}
#endif
		}
		else {
			// check to see if samp was properly precached
			sound.IsSentence = false;
			sound.SoundNum = sv.LookupSoundIndex(samp);
			if (sound.SoundNum <= 0) {
				ConMsg($"EmitAmbientSound:  sound not precached: {samp}\n");
				return;
			}
		}

		if ((fFlags & (int)SoundFlags.Spawning) != 0 && sv.AllowSignOnWrites) {
			SVC_Sounds sndmsg = new();
			byte[] buffer = new byte[32];

			sndmsg.DataOut.StartWriting(buffer, buffer.Length);
			sndmsg.NumSounds = 1;
			sndmsg.ReliableSound = true;

			SoundInfo defaultSound = default; defaultSound.SetDefault();

			sound.WriteDelta(ref defaultSound, sndmsg.DataOut);

			// write into signon buffer
			if (!sndmsg.WriteToBuffer(sv.Signon)) {
				Sys.Error("EmitAmbientSound: Init message would overflow signon buffer!\n");
				return;
			}
		}
		else {
			if ((fFlags & (int)SoundFlags.Spawning) != 0)
				DevMsg("EmitAmbientSound: warning, broadcasting sound labled as SND_SPAWNING.\n");

			// send sound to all active players
			EngineRecipientFilter filter = new();
			filter.AddAllPlayers();
			filter.MakeReliable();
			sv.BroadcastSound(sound, filter);
		}
	}

	public bf_write? EntityMessageBegin(int ent_index, ServerClass ent_class, bool reliable) {
		if (s_MsgData.Started) {
			Sys.Error("EntityMessageBegin:  New message started before matching call to EndMessage.\n ");
			return null;
		}

		s_MsgData.Reset();

		Assert(ent_class != null);

		s_MsgData.Filter = null;
		s_MsgData.Reliable = reliable;
		s_MsgData.Started = true;
		s_MsgData.CurrentMsg = s_MsgData.EntityMsg;

		s_MsgData.EntityMsg.EntityIndex = ent_index;
		s_MsgData.EntityMsg.ClassID = ent_class.ClassID;
		s_MsgData.EntityMsg.DataOut.Reset();

		return s_MsgData.EntityMsg.DataOut;
	}

	public void FadeClientVolume(Edict edict, float fadePercent, float fadeOutSeconds, float holdTime, float fadeInSeconds) {
		int entnum = NUM_FOR_EDICT(edict);

		if (entnum < 1 || entnum > sv.GetClientCount()) {
			ConMsg("tried to DLL_FadeClientVolume a non-client\n");
			return;
		}

		GameClient client = sv.Client(entnum - 1);

		NET_StringCmd sndMsg = new($"soundfade {fadePercent:F1} {holdTime:F1} {fadeOutSeconds:F1} {fadeInSeconds:F1}");
		client.SendNetMsg(sndMsg);
	}

	public void ForceExactFile(ReadOnlySpan<char> s) {
		throw new NotImplementedException();
	}

	public void ForceModelBounds(ReadOnlySpan<char> s, in Vector3 mins, in Vector3 maxs) {
		throw new NotImplementedException();
	}

	public void ForceSimpleMaterial(ReadOnlySpan<char> s) {
		throw new NotImplementedException();
	}

	public int GetAppID() {
		throw new NotImplementedException();
	}

	public int GetArea(in Vector3 origin) => CM.LeafArea(CM.PointLeafnum(origin));

	public void GetAreaBits(int area, Span<byte> bits) => CM.WriteAreaBits(bits, area);

	public bool GetAreaPortalPlane(in Vector3 viewOrigin, int portalKey, out VPlane plane) => CM.GetAreaPortalPlane(viewOrigin, portalKey, out plane);

	public IChangeInfoAccessor GetChangeAccessor(Edict edict) => sv.EdictChangeInfo[NUM_FOR_EDICT(edict)];

	public ReadOnlySpan<char> GetClientConVarValue(int clientIndex, ReadOnlySpan<char> name) {
		if (clientIndex < 1 || clientIndex > sv.GetClientCount()) {
			DevMsg(1, "GetClientConVarValue: player invalid index %i\n", clientIndex);
			return "";
		}

		return sv.GetClient(clientIndex - 1)!.GetUserSetting(name);
	}

	public CSteamID? GetClientSteamID(Edict playerEdict) {
		int entnum = NUM_FOR_EDICT(playerEdict);
		return GetClientSteamIDByPlayerIndex(entnum);
	}

	public CSteamID? GetClientSteamIDByPlayerIndex(int entNum) {
		if (entNum < 1 || entNum > sv.GetClientCount())
			return null;

		GameClient? client = sv.Client(entNum - 1);
		if (client == null)
			return null;

		if (!client.IsConnected() || !client.SteamID.IsValid())
			return null;

		return client.SteamID;
	}

	public int GetClusterCount() {
		CollisionBSPData? bspData = GetCollisionBSPData();
		if (bspData != null && bspData.MapVis != null)
			return bspData.NumClusters;
		return 0;
	}

	public int GetAllClusterBounds(Span<BBox> bboxList) {
		int maxBBox = bboxList.Length;
		CollisionBSPData? bspData = GetCollisionBSPData();
		WorldBrushData? worldbrush = Singleton<CommonHostState>().WorldBrush;
		if (bspData != null && bspData.MapVis != null && worldbrush != null) {
			// clamp to max clusters in the map
			if (maxBBox > bspData.NumClusters)
				maxBBox = bspData.NumClusters;

			// reset all of the bboxes
			for (int i = 0; i < maxBBox; i++)
				MathLib.ClearBounds(out bboxList[i].Mins, out bboxList[i].Maxs);

			// add each leaf's bounds to the bounds for that cluster
			for (int i = 0; i < worldbrush.NumLeafs; i++) {
				Source.Common.Formats.BSP.BSPMLeaf leaf = worldbrush.Leafs![i];
				// skip solid leaves and leaves with cluster < 0
				if ((leaf.Contents & (int)Source.Common.Formats.BSP.Contents.Solid) == 0 && leaf.Cluster >= 0 && leaf.Cluster < maxBBox) {
					Vector3 mins, maxs;
					mins = leaf.Center - leaf.HalfDiagonal;
					maxs = leaf.Center + leaf.HalfDiagonal;
					MathLib.AddPointToBounds(mins, ref bboxList[leaf.Cluster].Mins, ref bboxList[leaf.Cluster].Maxs);
					MathLib.AddPointToBounds(maxs, ref bboxList[leaf.Cluster].Mins, ref bboxList[leaf.Cluster].Maxs);
				}
			}

			return bspData.NumClusters;
		}
		return 0;
	}

	public int GetClusterForOrigin(in Vector3 org) {
		return CM.LeafCluster(CM.PointLeafnum(org));
	}

	public int GetEntityCount() => sv.NumEdicts - sv.FreeEdicts;

	public ref readonly MaxEdictsBitVec GetEntityTransmitBitsForClient(int iClientIndex) {
		if (iClientIndex < 0 || iClientIndex >= sv.GetClientCount()) {
			Assert(false);
			return ref Unsafe.NullRef<MaxEdictsBitVec>();
		}

		GameClient client = sv.Client(iClientIndex);
		ClientFrame? deltaFrame = client.FrameManager.GetClientFrame(client.DeltaTick);
		if (deltaFrame == null)
			return ref Unsafe.NullRef<MaxEdictsBitVec>();

		return ref deltaFrame.TransmitEntity;
	}

	public void GetGameDir(Span<char> getGameDir) => strcpy(getGameDir, Common.Gamedir);

	public CSteamID? GetGameServerSteamID() {
		CSteamID sid = Steam3Server().GetGSSteamID();

		if (!sid.IsValid())
			return null;

		return sid;
	}

	public ReadOnlyMemory<byte> GetMapEntitiesString() {
		return GetCollisionBSPData().MapEntityData;
	}

	public ReadOnlySpan<char> GetMostRecentlyLoadedFileName() {
		throw new NotImplementedException();
	}

	public bool GetPlayerInfo(int entNum, out PlayerInfo info) => sv.GetPlayerInfo(entNum - 1, out info);

	public INetChannelInfo? GetPlayerNetInfo(int playerIndex) {
		if (playerIndex < 1 || playerIndex > sv.GetClientCount())
			return null;

		GameClient client = sv.Client(playerIndex - 1);

		return client.NetChannel;
	}

	public ReadOnlySpan<char> GetPlayerNetworkIDString(Edict e) {
		if (!sv.IsActive() || e == null)
			return null;

		for (int i = 0; i < sv.GetClientCount(); i++) {
			GameClient cl = sv.Client(i);

			if (cl.Edict == e)
				return cl.GetNetworkIDString();
		}

		return null;
	}

	public int GetPlayerUserId(Edict e) {
		if (!sv.IsActive() || e == null)
			return -1;

		for (int i = 0; i < sv.GetClientCount(); i++) {
			GameClient cl = sv.Client(i);

			if (cl.Edict == e)
				return cl.UserID;
		}

		return -1;
	}

	public int GetPVSForCluster(int cluster, Span<byte> outputpvs) {
		int length = (CM.NumClusters() + 7) >> 3;

		if (!outputpvs.IsEmpty) {
			if (outputpvs.Length < length) {
				Sys.Error($"GetPVSForOrigin called with inusfficient sized pvs array, need {length} bytes!");
				return length;
			}

			CM.Vis(outputpvs, outputpvs.Length, cluster, CM.DVIS_PVS);
		}

		return length;
	}

	public ReadOnlySpan<char> GetSaveFileName() {
		throw new NotImplementedException();
	}

	public int GetServerVersion() => GetSteamInfIDVersionInfo().ServerVersion;

	public int IndexOfEdict(Edict? edict) {
		if (edict == null)
			return 0;

		int index = Array.IndexOf(sv.Edicts!, edict);
		if (index < 0 || index > sv.MaxEdicts)
			Sys.Error($"Bad entity in IndexOfEdict() index {index} pEdict {edict} sv.edicts {sv.Edicts}\n");

		return index;
	}

	public void InsertServerCommand(ReadOnlySpan<char> str) {
		throw new NotImplementedException();
	}

	public bool IsClientFullyAuthenticated(Edict edict) {
		int entnum = NUM_FOR_EDICT(edict);
		if (entnum < 1 || entnum > sv.GetClientCount())
			return false;

		GameClient? client = sv.Client(entnum - 1);
		return client?.IsFullyAuthenticated() ?? false;
	}

	public bool IsDecalPrecached(ReadOnlySpan<char> s) {
		throw new NotImplementedException();
	}

	public bool IsDedicatedServer() => sv.IsDedicated();

	public bool IsGenericPrecached(ReadOnlySpan<char> s) {
		throw new NotImplementedException();
	}

	public int IsInCommentaryMode() {
		throw new NotImplementedException();
	}

	public int IsInEditMode() => InEditMode() ? 1 : 0;

	public bool IsInternalBuild() => false;

	public bool IsLowViolence() {
		throw new NotImplementedException();
	}

	public int IsMapValid(ReadOnlySpan<char> filename) => modelloader.Map_IsValid(filename) ? 1 : 0;

	public bool IsModelPrecached(ReadOnlySpan<char> s) => SV.ModelIndex(s) != -1;

	public bool IsPaused() => sv.IsPaused();

	public void LightStyle(int style, ReadOnlySpan<char> val) {
		// change the string in string table
		INetworkStringTable stringTable = sv.GetLightStyleTable()!;
		stringTable!.SetStringUserData(style, val.Length + 1, val.Cast<char, byte>());
	}

	public void LoadAdjacentEnts(ReadOnlySpan<char> oldLevel, ReadOnlySpan<char> landmarkName) {
		throw new NotImplementedException();
	}

	public bool LoadGameState(ReadOnlySpan<char> mapName, bool createPlayers) {
		throw new NotImplementedException();
	}

	public bool LockNetworkStringTables(bool shouldLock) {
		throw new NotImplementedException();
	}

	public void LogPrint(ReadOnlySpan<char> msg) {
		// todo
	}

	int Message_CheckMessageLength() {
		if (s_MsgData.CurrentMsg == s_MsgData.UserMsg) {
			Span<char> msgname = stackalloc char[256];
			int msgsize = -1;
			int msgtype = s_MsgData.UserMsg.MessageType;

			if (!serverGameDLL.GetUserMessageInfo(msgtype, msgname, out msgsize)) {
				Warning($"Unable to find user message for index {msgtype}\n");
				return -1;
			}

			int bytesWritten = s_MsgData.UserMsg.DataOut.BytesWritten;

			if (msgsize == -1) {
				if (bytesWritten > Constants.MAX_USER_MSG_DATA) {
					Warning($"DLL_MessageEnd:  Refusing to send user message {msgname} of {bytesWritten} bytes to client, user message size limit is {Constants.MAX_USER_MSG_DATA} bytes\n");
					return -1;
				}
			}
			else if (msgsize != bytesWritten) {
				Warning($"User Msg '{msgname}': {bytesWritten} bytes written, expected {msgsize}\n");
				return -1;
			}

			return bytesWritten; // all checks passed, estimated final length
		}

		if (s_MsgData.CurrentMsg == s_MsgData.EntityMsg) {
			int bytesWritten = s_MsgData.EntityMsg.DataOut.BytesWritten;

			if (bytesWritten > Constants.MAX_ENTITY_MSG_DATA) // TODO use a define or so
			{
				Warning($"Entity Message to {s_MsgData.EntityMsg.EntityIndex}, {bytesWritten} bytes written (max is {Constants.MAX_ENTITY_MSG_DATA})\n");
				return -1;
			}

			return bytesWritten; // all checks passed, estimated final length
		}

		Warning("MessageEnd unknown message type.\n");
		return -1;

	}

	public void MessageEnd() {
		if (!s_MsgData.Started) {
			Sys.Error("MESSAGE_END called with no active message\n");
			return;
		}

		int length = Message_CheckMessageLength();

		// check to see if it's a valid message
		if (length < 0) {
			s_MsgData.Reset(); // clear message data
			return;
		}

		if (s_MsgData.Filter != null) {
			// send entity/user messages only to full connected clients in filter
			sv.BroadcastMessage(s_MsgData.CurrentMsg!, s_MsgData.Filter);
		}
		else {
			// send entity messages to all full connected clients 
			sv.BroadcastMessage(s_MsgData.CurrentMsg!, true, s_MsgData.Reliable);
		}

		s_MsgData.Reset(); // clear message data
	}

	public void Message_DetermineMulticastRecipients(bool usepas, in Vector3 origin, ref AbsolutePlayerLimitBitVec playerbits) {
		int cluster = CM.LeafCluster(CM.PointLeafnum(origin));
		byte[] pvs = new byte[BSPFileCommon.MAX_MAP_LEAFS / 8];
		int visflag = usepas ? CM.DVIS_PAS : CM.DVIS_PVS;

		CM.Vis(pvs, pvs.Length, cluster, visflag);

		playerbits.ClearAll();

		// Check for relevent clients
		for (int i = 0; i < sv.GetClientCount(); i++) {
			GameClient client = sv.Client(i);

			if (!client.IsActive())
				continue;

			// HACK:  Should above also check pClient->spawned instead of this
			if (client.Edict == null || client.Edict.IsFree() || client.Edict.GetUnknown() == null)
				continue;

			// Always add the or Replay client
			if (client.IsHLTV()) {
				playerbits.Set(i);
				continue;
			}

			serverGameClients.ClientEarPosition(client.Edict, out Vector3 vecEarPosition);

			int iBitNumber = CM.LeafCluster(CM.PointLeafnum(vecEarPosition));
			if ((pvs[iBitNumber >> 3] & (1 << (iBitNumber & 7))) == 0)
				continue;

			playerbits.Set(i);
		}
	}

	public void MultiplayerEndGame() {
		throw new NotImplementedException();
	}

	public void NotifyEdictFlagsChange(int edict) => CL.LocalNetworkBackdoor?.NotifyEdictFlagsChange((uint)edict);

	public ReadOnlySpan<char> ParseFile(ReadOnlySpan<char> data, Span<char> token) => FilesystemHelpers.ParseFile(data, token, out _);

	public Edict? PEntityOfEntIndex(int iEntIndex) {
		if (iEntIndex >= 0 && iEntIndex < sv.MaxEdicts) {
			Edict? edict = sv.Edicts![iEntIndex];
			if (!edict.IsFree())
				return edict;
		}

		return null;
	}

	public void PlaybackTempEntity(IRecipientFilter filter, float delay, object sender, SendTable st, int classID) {
		if (sv.TempEntities.Count >= ((1 << EventInfo.EVENT_INDEX_BITS) - 1))
			sv.TempEntities.RemoveAt(0);

		classID = classID + 1;

		byte[] data = new byte[EventInfo.MAX_EVENT_DATA];
		bf_write buffer = new(data, data.Length);

		if (!EngineSendTable.Encode(st, sender, buffer, classID, null, false)) {
			Host.Error($"PlaybackTempEntity: SendTable_Encode returned false (ent {classID}), overflow? {(buffer.Overflowed ? 1 : 0)}\n");
			return;
		}

		EventInfo newEvent = new();

		newEvent.Filter.AddPlayersFromFilter(filter);

		newEvent.ClassID = (short)classID;
		newEvent.SendTable = st;
		newEvent.FireDelay = delay;

		newEvent.Bits = buffer.BitsWritten;
		int size = Net.Bits2Bytes(buffer.BitsWritten);
		newEvent.Data = new byte[size];
		data.AsSpan(0, size).CopyTo(newEvent.Data);

		sv.TempEntities.Add(newEvent);
	}

	public int PrecacheDecal(ReadOnlySpan<char> name, bool preload = false) {
		throw new NotImplementedException();
	}

	public int PrecacheGeneric(ReadOnlySpan<char> s, bool preload = false) {
		throw new NotImplementedException();
	}

	void PR_CheckEmptyString(ReadOnlySpan<char> s) {
		if (s.Length == 0)
			Host.Error($"Bad string: {s}");
	}

	public int PrecacheModel(ReadOnlySpan<char> s, bool preload = false) {
		if (sv.GetModelPrecacheTable()!.GetNumStrings() != 0) // Allow the gamedll to call it with "" if the stringtable is empty. It is most likely recreating the entire stringtable.
			PR_CheckEmptyString(s);

		int i = SV.FindOrAddModel(s, preload);
		if (i >= 0)
			return i;

		Host.Error($"EngineServer.PrecacheModel: '{s}' overflow, too many models");
		return 0;
	}

	//-----------------------------------------------------------------------------
	// Purpose: Precache a sentence file (parse on server, send to client)
	// Input  : *s - file name
	//-----------------------------------------------------------------------------
	public int PrecacheSentenceFile(ReadOnlySpan<char> s, bool preload = false) {
		// UNDONE:  Set up preload flag

		// UNDONE: Send this data to the client to support multiple sentence files
#if !SWDS
		g_AudioSystem.ReadSentenceFile(s);
#endif

		return 0;
	}

	public void RemoveEdict(Edict e) {
		serverPluginHandler.OnEdictFreed(e);
		ED.Free(e);
	}

	public void ResetPVS(byte[] pvs, int pvssize) => SV.ResetPVS(pvs, pvssize);

#if !SWDS
	public ReadOnlySpan<char> SentenceGrounameFromIndex(int groupIndex) {
		return g_AudioSystem.SentenceGroupNameFromIndex(groupIndex);
	}

	public int SentenceGroupIndexFromName(ReadOnlySpan<char> pGrouname) {
		return g_AudioSystem.SentenceGroupIndexFromName(pGrouname);
	}

	public int SentenceGroupPick(int groupIndex, Span<char> name) {
		Assert(name.Length > 0);

		int pick = g_AudioSystem.SentenceGroupPick(groupIndex, out string found);
		strcpy(name, found);
		return pick;
	}

	public int SentenceGroupPickSequential(int groupIndex, Span<char> name, int sentenceIndex, int reset) {
		Assert(name.Length > 0);

		int pick = g_AudioSystem.SentenceGroupPickSequential(groupIndex, out string found, sentenceIndex, reset != 0);
		strcpy(name, found);
		return pick;
	}

	public int SentenceIndexFromName(ReadOnlySpan<char> pSentenceName) {
		g_AudioSystem.LookupSentence(pSentenceName, out int sentenceIndex);

		return sentenceIndex;
	}

	public float SentenceLength(int sentenceIndex) {
		return g_AudioSystem.SentenceLength(sentenceIndex);
	}

	public ReadOnlySpan<char> SentenceNameFromIndex(int sentenceIndex) {
		return g_AudioSystem.SentenceNameFromIndex(sentenceIndex);
	}
#else
	public ReadOnlySpan<char> SentenceGrounameFromIndex(int groupIndex) => null;
	public int SentenceGroupIndexFromName(ReadOnlySpan<char> pGrouname) => -1;
	public int SentenceGroupPick(int groupIndex, Span<char> name) => -1;
	public int SentenceGroupPickSequential(int groupIndex, Span<char> name, int sentenceIndex, int reset) => -1;
	public int SentenceIndexFromName(ReadOnlySpan<char> pSentenceName) => -1;
	public float SentenceLength(int sentenceIndex) => 0;
	public ReadOnlySpan<char> SentenceNameFromIndex(int sentenceIndex) => null;
#endif

	static bool ValidCmd(ReadOnlySpan<char> cmd) {
		int len = (int)strlen(cmd);
		return len != 0 && (cmd[len - 1] == '\n' || cmd[len - 1] == ';');
	}

	public void ServerCommand(ReadOnlySpan<char> str) {
		if (str.IsEmpty)
			Sys.Error("ServerCommand with NULL string\n");

		if (ValidCmd(str))
			Cbuf.AddText(str);
		else
			ConMsg($"Error, bad server command {str}\n");
	}

	public void ServerExecute() => Cbuf.Execute();

	public void SetAreaPortalState(int portalNumber, int isOpen) => CM.SetAreaPortalState(portalNumber, isOpen);

	public void SetAreaPortalStates(ReadOnlySpan<int> portalNumbers, ReadOnlySpan<int> isOpen) => CM.SetAreaPortalStates(portalNumbers, isOpen);

	public void SetDedicatedServerBenchmarkMode(bool benchmarkMode) {
		throw new NotImplementedException();
	}

	public void SetFakeClientConVarValue(Edict pEntity, ReadOnlySpan<char> cvar, ReadOnlySpan<char> value) {
		int clientnum = NUM_FOR_EDICT(pEntity);
		if (clientnum < 1 || clientnum > sv.GetClientCount())
			Host.Error("DLL_SetView: not a client");

		GameClient client = sv.Client(clientnum - 1);
		if (client.IsFakeClient()) {
			client.SetUserCVar(cvar, value);
			client.ConVarsChanged = true;
		}
	}

	public void SetView(Edict client, Edict viewent) {
		int clientnum = NUM_FOR_EDICT(client);
		if (clientnum < 1 || clientnum > sv.GetClientCount())
			Host.Error("DLL_SetView: not a client");

		GameClient cl = sv.Client(clientnum - 1);
		cl.ViewEntity = viewent;

		SVC_SetView view = new(NUM_FOR_EDICT(viewent));
		cl.SendNetMsg(view);
	}

	public void SolidMoved(Edict pSolidEnt, ICollideable pSolidCollide, Vector3? prevAbsOrigin, bool accurateBboxTriggerChecks) {
		WorldTouch.SV_SolidMoved(pSolidEnt, pSolidCollide, prevAbsOrigin, accurateBboxTriggerChecks);
	}

	public void StaticDecal(in Vector3 originInEntitySpace, int decalIndex, int entityIndex, int modelIndex, bool lowpriority) {
		SVC_BSPDecal decal = new() {
			Pos = originInEntitySpace,
			DecalTextureIndex = decalIndex,
			EntityIndex = entityIndex,
			ModelIndex = modelIndex,
			LowPriority = lowpriority
		};

		if (sv.AllowSignOnWrites)
			decal.WriteToBuffer(sv.Signon);
		else
			sv.BroadcastMessage(decal, false, true);
	}

	public ref ClientTextMessage TextMessageGet(ReadOnlySpan<char> name) {
		throw new NotImplementedException();
	}

	public TimeUnit_t Time() => Sys.Time;

	public void TriggerMoved(Edict pTriggerEnt, bool accurateBboxTriggerChecks) {
		WorldTouch.SV_TriggerMoved(pTriggerEnt, accurateBboxTriggerChecks);
	}

	class MsgData
	{
		public MsgData() {
			Reset();

			EntityMsg.DataOut.StartWriting(EntityData, EntityData.Length);
			EntityMsg.DataOut.DebugName = "s_MsgData.EntityMsg.DataOut";

			UserMsg.DataOut.StartWriting(UserData, UserData.Length);
			UserMsg.DataOut.DebugName = "s_MsgData.UserMsg.DataOut";
		}

		public void Reset() {
			Filter = null;
			Reliable = false;
			SubType = 0;
			Started = false;
			UserMessageSize = -1;
			UserMessageName = null;
			CurrentMsg = null;
		}

		public readonly byte[] UserData = new byte[PAD_NUMBER(Constants.MAX_USER_MSG_DATA, 4)];    // buffer for outgoing user messages
		public readonly byte[] EntityData = new byte[PAD_NUMBER(Constants.MAX_ENTITY_MSG_DATA, 4)]; // buffer for outgoing entity messages

		public IRecipientFilter? Filter;       // clients who get this message
		public bool Reliable;

		public INetMessage? CurrentMsg;                // pointer to entityMsg or userMessage
		public int SubType;            // usermessage index
		public bool Started;           // IS THERE A MESSAGE IN THE PROCESS OF BEING SENT?
		public int UserMessageSize;
		public string? UserMessageName;

		public readonly SVC_EntityMessage EntityMsg = new();
		public readonly SVC_UserMessage UserMsg = new();
	}
	static readonly MsgData s_MsgData = new();

	public bf_write UserMessageBegin(in IRecipientFilter filter, int msg_index) {
		if (s_MsgData.Started) {
			Sys.Error("UserMessageBegin:  New message started before matching call to EndMessage.\n");
			return null!;
		}

		s_MsgData.Reset();

		Assert(filter);

		s_MsgData.Filter = filter;
		s_MsgData.Reliable = filter.IsReliable();
		s_MsgData.Started = true;

		s_MsgData.CurrentMsg = s_MsgData.UserMsg;

		s_MsgData.UserMsg.MessageType = msg_index;

		s_MsgData.UserMsg.DataOut.Reset();
		return s_MsgData.UserMsg.DataOut;
	}

	public CheckTransmitInfo GetPrevCheckTransmitInfo(Edict playerEdict) {
		int entnum = NUM_FOR_EDICT(playerEdict);
		if (entnum < 1 || entnum > sv.GetClientCount())
			Error("Invalid client specified in GetPrevCheckTransmitInfo\n");

		GameClient client = sv.Client(entnum - 1);
		return client.GetPrevPackInfo();
	}

	public SharedEdictChangeInfo GetSharedEdictChangeInfo() => g_pSharedEdictChangeInfo;

	public Span<float> GMOD_SetTimeManipulator(float scaleFramerate) {
		throw new NotImplementedException();
	}

	public void GMOD_SendToClient<IRF>(ref IRF filter, ReadOnlySpan<byte> data, int dataBits) where IRF : IRecipientFilter {
		SVC_GMod_ServerToClient msg = new();
		msg.SetReliable(true);
		msg.ReadPayload(new bf_read(data.ToArray(), data.Length), dataBits);
		sv.BroadcastMessage(msg, filter);
	}

	public void GMOD_SendToClient(int client, ReadOnlySpan<byte> data, int dataBits) {
		GameClient? cl = sv.Client(client);
		if (cl == null) {
			Msg("Not sending to null client.\n");
			return;
		}

		if (cl.IsFakeClient()) {
			DevMsg($"Not sending to fake client '{cl.GetClientName()}'.\n");
			return;
		}

		SVC_GMod_ServerToClient msg = new();
		msg.SetReliable(true);
		msg.ReadPayload(new bf_read(data.ToArray(), data.Length), dataBits);
		cl.SendNetMsg(msg, false);
		cl.GetNetChannel().Transmit(false);
	}

	public void GMOD_RawServerCommand(ReadOnlySpan<char> command) {
		throw new NotImplementedException();
	}

	public IGMODDataTable GMOD_CreateDataTable() {
		throw new NotImplementedException();
	}

	public void GMOD_DestroyDataTable(IGMODDataTable dataTable) {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GMOD_GetServerAddress() {
		throw new NotImplementedException();
	}

	public uint GMOD_LoadModel(ReadOnlySpan<char> path) {
		throw new NotImplementedException();
	}

	public float GetClientConVarFloat(int client, ReadOnlySpan<char> cvar, float fallback) {
		throw new NotImplementedException();
	}

	public ref CSteamID GMOD_GetPlayerOwnerSteamID(Edict client) {
		throw new NotImplementedException();
	}

	public bool GMOD_GetPlayerIsSpeaking(Edict client) {
		throw new NotImplementedException();
	}

	public bool GMOD_GetPlayerIsBot(Edict client) {
		throw new NotImplementedException();
	}

	public bool GMOD_ShouldUpdateVoiceMasks() {
		throw new NotImplementedException();
	}

	public bool NET_IsHostLocal(ReadOnlySpan<char> unknwon) {
		throw new NotImplementedException();
	}
}
