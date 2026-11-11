namespace Source.Common.Networking;

using Source;
using System.Diagnostics;
using System.Numerics;

using static Dbg;
using static Protocol;
using static Constants;
using Source.Common.Bitbuffers;
using Source.Common.Hashing;
using Source.Common.Mathematics;
using System.Text;
using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.Utilities;
using System.Buffers;
using System.Runtime.CompilerServices;

public static class NetMessageExtensions
{
	public static void WriteNetMessageType<T>(this bf_write buffer, T msg) where T : INetMessage => buffer.WriteUBitLong((uint)msg.GetMessageType(), NETMSG_TYPE_BITS);
	public static SignOnState ReadSignOnState(this bf_read buffer) => (SignOnState)buffer.ReadByte();
	public static void WriteSignOnState(this bf_write buffer, SignOnState state) => buffer.WriteByte((byte)state);
	public static void WriteZeros(this bf_write buffer, int amount) {
		for (int i = 0; i < amount; i++) {
			buffer.WriteBool(false);
		}
	}
	public static void WriteOnes(this bf_write buffer, int amount) {
		for (int i = 0; i < amount; i++) {
			buffer.WriteBool(true);
		}
	}
}

public struct cvar_s
{
	public string Name;
	public string Value;
}

public enum ServerOS : byte
{
	Win32 = (byte)'W',
	Linux = (byte)'L'
}

public class NET_Tick : NetMessage
{
	public NET_Tick() : base(NET.Tick) { reliable = false; }
	public NET_Tick(long tick, float frametime, float framedev) : base(NET.Tick) {
		reliable = false;
		Tick = (int)tick;
		HostFrameTime = frametime;
		HostFrameDeviation = framedev;
	}

	public int Tick;
	public float HostFrameTime;
	public float HostFrameDeviation;
	public const float NET_TICK_SCALEUP = 100000.0f;
	public override bool ReadFromBuffer(bf_read buffer) {
		INetChannel netchan = GetNetChannel() ?? throw new Exception("No net channel found!");
		Tick = buffer.ReadLong();
		HostFrameTime = buffer.ReadUBitLong(16) / NET_TICK_SCALEUP;
		HostFrameDeviation = buffer.ReadUBitLong(16) / NET_TICK_SCALEUP;

		return !buffer.Overflowed;
	}

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		buffer.WriteLong(Tick);
		buffer.WriteUBitLong((uint)Math.Clamp((int)(NET_TICK_SCALEUP * HostFrameTime), 0, 65535), 16);
		buffer.WriteUBitLong((uint)Math.Clamp((int)(NET_TICK_SCALEUP * HostFrameDeviation), 0, 65535), 16);
		return !buffer.Overflowed;
	}

	public override string ToString() => $"NET_Tick: tick {Tick}";
}
public class NET_SetConVar : NetMessage
{
	public List<cvar_s> ConVars;
	public override NetChannelGroup GetGroup() => NetChannelGroup.StringCmd;
	public NET_SetConVar() : base(NET.SetConVar) {
		ConVars = [];
	}


	public void AddCVar(string name, string value) {
		name = name.Length >= 260 ? name.Substring(0, 260) : name;
		value = value.Length >= 260 ? value.Substring(0, 260) : value;

		ConVars.Add(new() {
			Name = name,
			Value = value
		});
	}

	public override bool ReadFromBuffer(bf_read buffer) {
		int numvars = buffer.ReadByte();
		DevMsg($"{numvars} vars received\n");

		ConVars.Clear();

		for (int i = 0; i < numvars; i++) {
			cvar_s var = new();
			buffer.ReadString(out var.Name!, 260);
			buffer.ReadString(out var.Value!, 260);
			ConVars.Add(var);
		}

		return !buffer.Overflowed;
	}
	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);

		nint numvars = ConVars.Count;
		Debug.Assert(numvars <= byte.MaxValue);

		buffer.WriteByte((int)numvars);
		foreach (var cvar in ConVars) {
			buffer.WriteString(cvar.Name, limit: 260);
			buffer.WriteString(cvar.Value, limit: 260);
		}

		return !buffer.Overflowed;
	}

	public override string ToString() {
		string[] vars = new string[ConVars.Count];
		for (int i = 0; i < ConVars.Count; i++) {
			vars[i] = $"    {ConVars[i].Name}: {ConVars[i].Value}";
		}
		return $"NET_SetConVar: {ConVars.Count}, \n" + string.Join("\n", vars);
	}
}
public class NET_StringCmd : NetMessage
{
	public NET_StringCmd() : base(NET.StringCmd) { }
	public NET_StringCmd(string cmd) : base(NET.StringCmd) => Command = cmd;
	public override NetChannelGroup GetGroup() => NetChannelGroup.StringCmd;

	public string Command = "";

	public override bool ReadFromBuffer(bf_read buffer) => buffer.ReadString(out Command!, 1024);
	public override bool WriteToBuffer(bf_write buffer) {
		if (string.IsNullOrEmpty(Command))
			return false; // don't write anything

		buffer.WriteNetMessageType(this);
		return buffer.WriteString(Command ?? " NET_StringCmd NULL");
	}

	public override string ToString() {
		return $"NET_StringCmd: \"{Command}\"";
	}
}
public class NET_SignonState : NetMessage
{
	public SignOnState SignOnState { get; set; }
	public int SpawnCount { get; set; }

	public NET_SignonState() : base(NET.SignOnState) {

	}
	public override NetChannelGroup GetGroup() => NetChannelGroup.SignOn;
	public NET_SignonState(SignOnState signOnState, int spawnCount) : base(NET.SignOnState) {
		SignOnState = signOnState;
		SpawnCount = spawnCount;
	}

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		buffer.WriteSignOnState(SignOnState);
		buffer.WriteLong(SpawnCount);

		return !buffer.Overflowed;
	}

	public override bool ReadFromBuffer(bf_read buffer) {
		SignOnState = buffer.ReadSignOnState();
		SpawnCount = buffer.ReadLong();

		return !buffer.Overflowed;
	}

	public override string ToString() => $"{GetName()}: state {SignOnState}, count {SpawnCount}";
}
public class SVC_Print : NetMessage
{
	public string? Text;
	public SVC_Print() : base(SVC.Print) { }
	public SVC_Print(ReadOnlySpan<char> msg) : base(SVC.Print) => Text = msg.Length == 0 ? null : new(msg.SliceNullTerminatedString());
	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		return buffer.WriteString(Text ?? "svc_print NULL");
	}

	public override bool ReadFromBuffer(bf_read buffer) {
		return buffer.ReadString(out Text, 2048);
	}

	public override string ToString() {
		return Text ?? "NULL";
	}
}
public class SVC_ServerInfo : NetMessage
{
	public override NetChannelGroup GetGroup() => NetChannelGroup.SignOn;
	public SVC_ServerInfo() : base(SVC.ServerInfo) { }

	public int Protocol;
	public int ServerCount;
	public bool IsDedicated;
	public bool IsHLTV;
	public ServerOS ServerOS;
	public uint CRC32;
	public MD5Value MapMD5 = new();
	public int MaxClients;
	public int MaxClasses;
	public int PlayerSlot;
	public double TickInterval;
	public string GameDirectory = "";
	public string MapName = "";
	public string SkyName = "";
	public string HostName = "";
	public string LoadingURL = "";
	public string Gamemode = "";

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);

		buffer.WriteShort(Protocol);
		buffer.WriteLong(ServerCount);
		buffer.WriteOneBit(IsHLTV ? 1 : 0);
		buffer.WriteOneBit(IsDedicated ? 1 : 0);
		buffer.WriteLong(unchecked((int)0xffffffff));  // Used to be client.dll CRC.  This was far before signed binaries, VAC, and cross-platform play
		buffer.WriteWord(MaxClasses);
		buffer.WriteBytes(MD5Value.ToEditableBytes(ref MapMD5));       // To prevent cheating with hacked maps
		buffer.WriteByte(PlayerSlot);
		buffer.WriteByte(MaxClients);
		buffer.WriteFloat((float)TickInterval);
		buffer.WriteChar((byte)ServerOS);
		buffer.WriteString(GameDirectory);
		buffer.WriteString(MapName);
		buffer.WriteString(SkyName);
		buffer.WriteString(HostName);
		buffer.WriteString(LoadingURL);
		buffer.WriteString(Gamemode);

		return !buffer.Overflowed;
	}

	public override bool ReadFromBuffer(bf_read buffer) {
		Protocol = buffer.ReadShort();
		ServerCount = buffer.ReadLong();
		IsHLTV = buffer.ReadOneBit() != 0;
		IsDedicated = buffer.ReadOneBit() != 0;
		buffer.ReadLong();  // Legacy client CRC.
		MaxClasses = buffer.ReadWord();

		// Prevent cheating with hacked maps
		buffer.ReadBytes(MD5Value.ToEditableBytes(ref MapMD5));

		PlayerSlot = buffer.ReadByte();
		MaxClients = buffer.ReadByte();
		TickInterval = buffer.ReadFloat();
		ServerOS = (ServerOS)buffer.ReadChar();

		GameDirectory = buffer.ReadString(260) ?? "";
		MapName = buffer.ReadString(260) ?? "";
		SkyName = buffer.ReadString(260) ?? "";
		HostName = buffer.ReadString(260) ?? "";
		LoadingURL = buffer.ReadString(260) ?? "";
		Gamemode = buffer.ReadString(260) ?? "";

		return !buffer.Overflowed;
	}

	public override string ToString() => $"SVC_ServerInfo: game \"{GameDirectory}\", map \"{MapName}\", max {MaxClients}";
}

public class SVC_SendTable : NetMessage
{
	public SVC_SendTable() : base(SVC.SendTable) { }
	public override NetChannelGroup GetGroup() => NetChannelGroup.SignOn;
	public bool NeedsDecoder;
	public int Length;
	public readonly bf_read DataIn = new();
	public readonly bf_write DataOut = new();

	public override bool WriteToBuffer(bf_write buffer) {
		Length = DataOut.BitsWritten;
		buffer.WriteNetMessageType(this);

		buffer.WriteOneBit(NeedsDecoder ? 1 : 0);
		buffer.WriteShort(Length);
		buffer.WriteBits(DataOut.GetData(), Length);

		return !buffer.Overflowed;
	}

	public override bool ReadFromBuffer(bf_read buffer) {
		NeedsDecoder = buffer.ReadOneBit() != 0;
		Length = buffer.ReadShort();     // TODO do we have a maximum length ? check that

		buffer.CopyTo(DataIn);

		return buffer.SeekRelative(Length);
	}
};

public class SVC_ClassInfo : NetMessage
{
	public struct Class
	{
		public int ClassID;
		public string DataTableName;
		public string ClassName;
	}
	public override NetChannelGroup GetGroup() => NetChannelGroup.SignOn;
	public bool CreateOnClient;
	public List<Class> Classes = [];
	public int NumServerClasses = 0;
	public SVC_ClassInfo() : base(SVC.ClassInfo) { }

	public override bool ReadFromBuffer(bf_read buffer) {
		Classes.Clear();

		int numServerClasses = buffer.ReadShort();
		int serverClassBits = (int)MathF.Log2(numServerClasses) + 1;

		NumServerClasses = numServerClasses;
		CreateOnClient = buffer.ReadBool();

		if (CreateOnClient)
			return !buffer.Overflowed;

		for (int i = 0; i < NumServerClasses; i++) {
			Class serverclass = new Class();

			serverclass.ClassID = (int)buffer.ReadUBitLong(serverClassBits);
			serverclass.ClassName = buffer.ReadString(256) ?? "";
			serverclass.DataTableName = buffer.ReadString(256) ?? "";

			Classes.Add(serverclass);
		}

		return !buffer.Overflowed;
	}

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		buffer.WriteShort(NumServerClasses);
		buffer.WriteBool(CreateOnClient);

		if (CreateOnClient)
			return !buffer.Overflowed;

		int serverClassBits = (int)MathF.Log2(NumServerClasses) + 1;

		foreach (var serverclass in Classes) {
			buffer.WriteUBitLong((uint)serverclass.ClassID, serverClassBits);
			buffer.WriteString(serverclass.ClassName, true, 256);
			buffer.WriteString(serverclass.DataTableName, true, 256);
		}

		return !buffer.Overflowed;
	}
}
public class SVC_CreateStringTable : NetMessage
{
	public SVC_CreateStringTable() : base(SVC.CreateStringTable) { }
	public override NetChannelGroup GetGroup() => NetChannelGroup.SignOn;


	public string TableName = "";
	public int MaxEntries;
	public int NumEntries;
	public bool UserDataFixedSize;
	public int UserDataSize;
	public int UserDataSizeBits;
	public bool IsFilenames;
	public int Length;
	public readonly bf_read DataIn = new();
	public readonly bf_write DataOut = new();
	public bool DataCompressed;

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		Length = DataOut.BitsWritten;

		buffer.WriteString(TableName);

		int encodeBits = (int)MathF.Log2(MaxEntries);
		buffer.WriteUBitLong((uint)encodeBits, 5);
		buffer.WriteUBitLong((uint)NumEntries, encodeBits + 1);
		buffer.WriteVarInt32((uint)Length);
		buffer.WriteBool(UserDataFixedSize);

		if (UserDataFixedSize) {
			buffer.WriteUBitLong((uint)UserDataSize, 12);
			buffer.WriteUBitLong((uint)UserDataSizeBits, 4);
		}

		buffer.WriteBool(DataCompressed);

		return buffer.WriteBits(DataOut.BaseArray, Length);
	}

	public override bool ReadFromBuffer(bf_read buffer) {
		char prefix = '\0';
		byte prefixR = (byte)buffer.PeekUBitLong(8);
		Encoding.ASCII.GetChars(new ReadOnlySpan<byte>(in prefixR), new Span<char>(ref prefix));

		if (prefix == ':') {
			IsFilenames = true;
			buffer.ReadByte();
		}
		else
			IsFilenames = false;

		TableName = buffer.ReadString(500) ?? throw new Exception();

		int encodeBits = (int)buffer.ReadUBitLong(5);
		MaxEntries = 1 << encodeBits;
		NumEntries = (int)buffer.ReadUBitLong(encodeBits + 1);

		// Protocol difference here we should account for later
		Length = (int)buffer.ReadVarInt32();

		UserDataFixedSize = buffer.ReadBool();
		if (UserDataFixedSize) {
			UserDataSize = (int)buffer.ReadUBitLong(12);
			UserDataSizeBits = (int)buffer.ReadUBitLong(4);
		}
		else {
			UserDataSize = 0;
			UserDataSizeBits = 0;
		}

		DataCompressed = buffer.ReadBool();

		buffer.CopyTo(DataIn);
		Msg($"svc_CreateStringTable: {TableName}, contains {NumEntries}/{MaxEntries}\n");
		return buffer.SeekRelative(Length);
	}

	public override string ToString() => $"SVC_CreateStringTable: table {TableName}, entries {NumEntries}, bytes {Bits2Bytes(Length)} userdatasize {UserDataSize} userdatabits {UserDataSizeBits}";
}
public class SVC_UpdateStringTable : NetMessage
{
	public SVC_UpdateStringTable() : base(SVC.UpdateStringTable) { }
	public override NetChannelGroup GetGroup() => NetChannelGroup.StringTable;

	public int TableID;
	public int ChangedEntries;
	public int Length;
	public readonly bf_read DataIn = new();
	public readonly bf_write DataOut = new();

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		Length = DataOut.BitsWritten;
		buffer.WriteUBitLong((uint)TableID, MAX_TABLES_BITS);
		if (ChangedEntries == 1)
			buffer.WriteBool(false);
		else {
			buffer.WriteBool(true);
			buffer.WriteWord(ChangedEntries);
		}
		buffer.WriteUBitLong((uint)Length, 20);
		return buffer.WriteBits(DataOut.BaseArray, Length);
	}

	public override bool ReadFromBuffer(bf_read buffer) {
		TableID = (int)buffer.ReadUBitLong(MAX_TABLES_BITS);

		if (buffer.ReadBool() != false)
			ChangedEntries = buffer.ReadWord();
		else
			ChangedEntries = 1;

		Length = (int)buffer.ReadUBitLong(20);

		buffer.CopyTo(DataIn);
		return buffer.SeekRelative(Length);
	}

	public override string ToString() {
		return $"table {TableID}, changed {ChangedEntries}, bytes {Bits2Bytes(Length)}";
	}
}
public class SVC_VoiceInit : NetMessage
{
	public SVC_VoiceInit() : base(SVC.VoiceInit) { }
	public SVC_VoiceInit(ReadOnlySpan<char> codec, int quality) : base(SVC.VoiceInit) {
		VoiceCodec = codec.IsEmpty ? "" : new(codec.SliceNullTerminatedString());
		Quality = quality;
	}
	public override NetChannelGroup GetGroup() => NetChannelGroup.SignOn;

	public string VoiceCodec = "";
	public int Quality;
	public int SampleRate;

	public override bool ReadFromBuffer(bf_read buffer) {
		VoiceCodec = buffer.ReadString(260) ?? "";
		// This is a HACK!!!!!!
		ConVarRef sv_use_steam_voice = new("sv_use_steam_voice");
		if (sv_use_steam_voice.GetBool())
			VoiceCodec = "steam";

		byte legacyQuality = buffer.ReadByte();
		Quality = legacyQuality;
		if (legacyQuality == 255) {
			// v2 packet
			SampleRate = buffer.ReadShort();
		}
		else {
			// v1 packet
			// Hacky workaround for v1 packets not actually indicating if we were using steam voice -- we've kept the steam
			// voice separate convar that was in use at the time as replicated&hidden, and if whatever network stream we're
			// interpreting sets it, lie about the subsequent voice init's codec & sample rate.
			if (sv_use_steam_voice.GetBool()) {
				Msg("Legacy SVC_VoiceInit - got a set for sv_use_steam_voice convar, assuming Steam voice\n");
				VoiceCodec = "steam";
				// Legacy steam voice can always be parsed as auto sample rate.
				SampleRate = 0;
			}
			else if (VoiceCodec.Equals("vaudio_celt", StringComparison.OrdinalIgnoreCase)) {
				// Legacy rate vaudio_celt always selected during v1 packet era
				SampleRate = 22050;
			}
			else {
				// Legacy rate everything but CELT always selected during v1 packet era
				SampleRate = 11025;
			}
		}

		return !buffer.Overflowed;
	}

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		buffer.WriteString(VoiceCodec);
		buffer.WriteByte(Quality);
		return !buffer.Overflowed;
	}
}
public class SVC_Sounds : NetMessage
{
	public SVC_Sounds() : base(SVC.Sounds) { }
	public override NetChannelGroup GetGroup() => NetChannelGroup.Sounds;
	public bool ReliableSound;
	public int NumSounds;
	public int Length;
	public readonly bf_read DataIn = new();
	public readonly bf_write DataOut = new();

	public override bool ReadFromBuffer(bf_read buffer) {
		ReliableSound = buffer.ReadOneBit() != 0;

		if (ReliableSound) {
			NumSounds = 1;
			Length = (int)buffer.ReadUBitLong(8);
		}
		else {
			NumSounds = (int)buffer.ReadUBitLong(8);
			Length = (int)buffer.ReadUBitLong(16);
		}
		buffer.CopyTo(DataIn);
		return buffer.SeekRelative(Length);
	}

	public override bool WriteToBuffer(bf_write buffer) {
		Length = DataOut.BitsWritten;

		buffer.WriteNetMessageType(this);

		Assert(NumSounds > 0);

		if (ReliableSound) {
			// as single sound message is 32 bytes long maximum
			buffer.WriteOneBit(1);
			buffer.WriteUBitLong((uint)Length, 8);
		}
		else {
			// a bunch of unreliable messages
			buffer.WriteOneBit(0);
			buffer.WriteUBitLong((uint)NumSounds, 8);
			buffer.WriteUBitLong((uint)Length, 16);
		}

		return buffer.WriteBits(DataOut.BaseArray, Length);
	}
	public override string ToString() {
		return $"number {NumSounds},{(ReliableSound ? " reliable" : " ")} bytes {Bits2Bytes(Length)}";
	}
}

public enum SVC_PrefetchType : ushort
{
	Sound
}

public class SVC_Prefetch : NetMessage
{
	public SVC_Prefetch() : base(SVC.Sounds) { }
	public override NetChannelGroup GetGroup() => NetChannelGroup.Sounds;

	public SVC_PrefetchType Type;
	public ushort SoundIndex;

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		buffer.WriteUBitLong(SoundIndex, g_MaxSoundIndexBits);

		return !buffer.Overflowed;
	}

	public override bool ReadFromBuffer(bf_read buffer) {
		Type = SVC_PrefetchType.Sound;
		// if(GetNetChannel()!.GetMsgHandler().GetDemoProtoclVersion > 22)
		// etc..

		SoundIndex = (ushort)buffer.ReadUBitLong(13);

		return !buffer.Overflowed;
	}
}
public class SVC_BSPDecal : NetMessage
{
	public SVC_BSPDecal() : base(SVC.BSPDecal) { }

	public Vector3 Pos;
	public int DecalTextureIndex;
	public int EntityIndex;
	public int ModelIndex;
	public bool LowPriority;

	public override bool ReadFromBuffer(bf_read buffer) {
		Pos = buffer.ReadBitVec3Coord();
		DecalTextureIndex = (int)buffer.ReadUBitLong(MAX_DECAL_INDEX_BITS);

		if (buffer.ReadBool()) {
			EntityIndex = (int)buffer.ReadUBitLong(MAX_EDICT_BITS);
			ModelIndex = (int)buffer.ReadUBitLong(SP_MODEL_INDEX_BITS);
		}
		else {
			EntityIndex = 0;
			ModelIndex = 0;
		}
		LowPriority = buffer.ReadBool();

		return !buffer.Overflowed;
	}

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		buffer.WriteBitVec3Coord(Pos);
		buffer.WriteUBitLong((uint)DecalTextureIndex, MAX_DECAL_INDEX_BITS);

		if (EntityIndex != 0 || ModelIndex != 0) {
			buffer.WriteBool(true);
			buffer.WriteUBitLong((uint)EntityIndex, MAX_EDICT_BITS);
			buffer.WriteUBitLong((uint)ModelIndex, SP_MODEL_INDEX_BITS);
		}
		else
			buffer.WriteBool(false);

		buffer.WriteBool(LowPriority);

		return !buffer.Overflowed;
	}

	public override string ToString() => $"SVC_BSPDecal: tex {DecalTextureIndex}, ent {EntityIndex}, mod {ModelIndex}, lowpriority {(LowPriority ? "1" : "0")}";
}
public class SVC_GameEvent : NetMessage
{
	public SVC_GameEvent() : base(SVC.GameEvent) { }
	public override NetChannelGroup GetGroup() => NetChannelGroup.Events;

	public int Length;
	public readonly bf_read DataIn = new();
	public readonly bf_write DataOut = new();

	public override bool ReadFromBuffer(bf_read buffer) {
		Length = (int)buffer.ReadUBitLong(Protocol.NETMSG_LENGTH_BITS);
		buffer.CopyTo(DataIn);
		return buffer.SeekRelative(Length);
	}

	public override bool WriteToBuffer(bf_write buffer) {
		Length = DataOut.BitsWritten;
		if (Length >= (1 << Protocol.NETMSG_LENGTH_BITS))
			return false;

		buffer.WriteNetMessageType(this);
		buffer.WriteUBitLong((uint)Length, Protocol.NETMSG_LENGTH_BITS);
		return buffer.WriteBits(DataOut.BaseArray, Length);
	}

	public override string ToString() => $"SVC_GameEvent: bytes {Bits2Bytes(Length)}";
}
public class SVC_GameEventList : NetMessage
{
	public SVC_GameEventList() : base(SVC.GameEventList) { }

	public int NumEvents;
	public int Length;
	public readonly bf_read DataIn = new();
	public readonly bf_write DataOut = new();

	public override bool ReadFromBuffer(bf_read buffer) {
		NumEvents = (int)buffer.ReadUBitLong(MAX_EVENT_BITS);
		Length = (int)buffer.ReadUBitLong(20);
		buffer.CopyTo(DataIn);

		// temp
		byte[] data = new byte[Length];
		buffer.ReadBits(data, Length);

		return !buffer.Overflowed;
	}

	public override bool WriteToBuffer(bf_write buffer) {
		Length = DataOut.BitsWritten;
		if (Length >= (1 << 20))
			return false;

		buffer.WriteNetMessageType(this);
		buffer.WriteUBitLong((uint)NumEvents, MAX_EVENT_BITS);
		buffer.WriteUBitLong((uint)Length, 20);
		return buffer.WriteBits(DataOut.BaseArray, Length);
	}
}
public class SVC_GetCvarValue : NetMessage
{
	public SVC_GetCvarValue() : base(SVC.GetCvarValue) { }

	public QueryCvarCookie_t Cookie;
	public ReadOnlySpan<char> CvarName {
		get => pointNameToBuffer ? NameBuffer.SliceNullTerminatedString() : cvarName;
		set {
			if (pointNameToBuffer) strcpy(NameBuffer, value); else cvarName = new(value);
		}
	}

	string? cvarName;
	readonly char[] NameBuffer = new char[128];
	bool pointNameToBuffer;

	public override bool ReadFromBuffer(bf_read buffer) {
		Cookie = buffer.ReadSBitLong(32);
		buffer.ReadString(NameBuffer);
		pointNameToBuffer = true;
		return !buffer.Overflowed;
	}

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		buffer.WriteSBitLong(Cookie, 32);
		buffer.WriteString(CvarName);
		return !buffer.Overflowed;
	}
}
public class SVC_SetView : NetMessage
{
	public SVC_SetView() : base(SVC.SetView) { }
	public SVC_SetView(int ent) : base(SVC.SetView) => EntityIndex = ent;

	public int EntityIndex;

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		buffer.WriteUBitLong((uint)EntityIndex, MAX_EDICT_BITS);
		return !buffer.Overflowed;
	}

	public override bool ReadFromBuffer(bf_read buffer) {
		EntityIndex = (int)buffer.ReadUBitLong(MAX_EDICT_BITS);
		return !buffer.Overflowed;
	}

	public override string ToString() => $"SVC_SetView: view entity {EntityIndex}";
}
public class SVC_FixAngle : NetMessage
{
	public bool Relative;
	public QAngle Angle;
	public SVC_FixAngle() : base(SVC.FixAngle) { }
	public SVC_FixAngle(bool relative, QAngle angle) : base(SVC.FixAngle) {
		Relative = relative;
		Angle = angle;
	}

	public override bool ReadFromBuffer(bf_read buffer) {
		Relative = buffer.ReadBool();
		Angle = new() {
			X = buffer.ReadBitAngle(16),
			Y = buffer.ReadBitAngle(16),
			Z = buffer.ReadBitAngle(16)
		};

		return !buffer.Overflowed;
	}

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		buffer.WriteBool(Relative);
		buffer.WriteBitAngle(Angle.X, 16);
		buffer.WriteBitAngle(Angle.Y, 16);
		buffer.WriteBitAngle(Angle.Z, 16);
		return !buffer.Overflowed;
	}

	public override string ToString() => $"SVC_FixAngle: {(Relative ? "relative" : "absolute")} {Angle.X}, {Angle.Y}, {Angle.Z}";
}

public class SVC_CrosshairAngle : NetMessage
{
	public QAngle Angle;
	public SVC_CrosshairAngle() : base(SVC.CrosshairAngle) { }
	public SVC_CrosshairAngle(QAngle angle) : base(SVC.CrosshairAngle) {
		Angle = angle;
	}

	public override bool ReadFromBuffer(bf_read buffer) {
		Angle = new() {
			X = buffer.ReadBitAngle(16),
			Y = buffer.ReadBitAngle(16),
			Z = buffer.ReadBitAngle(16)
		};

		return !buffer.Overflowed;
	}

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		buffer.WriteBitAngle(Angle.X, 16);
		buffer.WriteBitAngle(Angle.Y, 16);
		buffer.WriteBitAngle(Angle.Z, 16);
		return !buffer.Overflowed;
	}
}

public class SVC_UserMessage : NetMessage
{
	public SVC_UserMessage() : base(SVC.UserMessage) { }
	public override NetChannelGroup GetGroup() => NetChannelGroup.UserMessage;

	public int MessageType;
	public int Length;
	public readonly bf_read DataIn = new();
	public readonly bf_write DataOut = new();

	public override bool ReadFromBuffer(bf_read buffer) {
		MessageType = buffer.ReadByte();
		Length = (int)buffer.ReadUBitLong(NETMSG_LENGTH_BITS);
		buffer.CopyTo(DataIn);

		return buffer.SeekRelative(Length);
	}

	public override bool WriteToBuffer(bf_write buffer) {
		Length = DataOut.BitsWritten;

		Debug.Assert(Length < 1 << NETMSG_LENGTH_BITS);
		if (Length >= 1 << NETMSG_LENGTH_BITS)
			return false;

		buffer.WriteNetMessageType(this);
		buffer.WriteByte(MessageType);
		buffer.WriteUBitLong((uint)Length, NETMSG_LENGTH_BITS);

		return buffer.WriteBits(DataOut.BaseArray, Length);
	}

	public override string ToString() {
		return $"SVC_UserMessage: type {MessageType}, bytes {Bits2Bytes(Length)}";
	}
}

public class SVC_EntityMessage : NetMessage
{
	public SVC_EntityMessage() : base(SVC.EntityMessage) { reliable = false; }
	public override NetChannelGroup GetGroup() => NetChannelGroup.EntMessage;
	public int EntityIndex;
	public int ClassID;
	public int Length;
	public readonly bf_read DataIn = new();
	public readonly bf_write DataOut = new();


	public override bool ReadFromBuffer(bf_read buffer) {
		EntityIndex = (int)buffer.ReadUBitLong(MAX_EDICT_BITS);
		ClassID = (int)buffer.ReadUBitLong(MAX_SERVER_CLASS_BITS);
		Length = (int)buffer.ReadUBitLong(NETMSG_LENGTH_BITS);
		buffer.CopyTo(DataIn);

		return buffer.SeekRelative(Length);
	}

	public override bool WriteToBuffer(bf_write buffer) {
		Length = DataOut.BitsWritten;

		Debug.Assert(Length < 1 << NETMSG_LENGTH_BITS);
		if (Length >= 1 << NETMSG_LENGTH_BITS)
			return false;

		buffer.WriteNetMessageType(this);
		buffer.WriteUBitLong((uint)EntityIndex, MAX_EDICT_BITS);
		buffer.WriteUBitLong((uint)ClassID, MAX_SERVER_CLASS_BITS);
		buffer.WriteUBitLong((uint)Length, NETMSG_LENGTH_BITS);

		return buffer.WriteBits(DataOut.BaseArray, Length);
	}

	public override string ToString() => $"SVC_EntityMessage: entity {EntityIndex}, class {ClassID}, bytes {Bits2Bytes(Length)}";
}

public class SVC_PacketEntities : NetMessage
{
	public SVC_PacketEntities() : base(SVC.PacketEntities) { }
	public override NetChannelGroup GetGroup() => NetChannelGroup.Entities;
	public int MaxEntries;
	public int UpdatedEntries;
	public bool IsDelta;
	public bool UpdateBaseline;
	public int Baseline;
	public int DeltaFrom;
	public int Length;
	public readonly bf_read DataIn = new();
	public readonly bf_write DataOut = new();

	public override bool ReadFromBuffer(bf_read buffer) {
		MaxEntries = (int)buffer.ReadUBitLong(MAX_EDICT_BITS);
		IsDelta = buffer.ReadBool();

		if (IsDelta)
			DeltaFrom = buffer.ReadLong();
		else
			DeltaFrom = -1;

		Baseline = (int)buffer.ReadUBitLong(1);
		UpdatedEntries = (int)buffer.ReadUBitLong(MAX_EDICT_BITS);
		Length = (int)buffer.ReadUBitLong(DELTASIZE_BITS);
		UpdateBaseline = buffer.ReadBool();
		buffer.CopyTo(DataIn);

		return buffer.SeekRelative(Length);
	}

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		buffer.WriteUBitLong((uint)MaxEntries, MAX_EDICT_BITS);
		buffer.WriteBool(IsDelta);
		if (IsDelta)
			buffer.WriteLong(DeltaFrom);
		buffer.WriteUBitLong((uint)Baseline, 1);
		buffer.WriteUBitLong((uint)UpdatedEntries, MAX_EDICT_BITS);
		buffer.WriteUBitLong((uint)Length, DELTASIZE_BITS);
		buffer.WriteBool(UpdateBaseline);
		return buffer.WriteBits(DataOut.BaseArray, Length);
	}

	public override string ToString() {
		return $"SVC_PacketEntities: delta {DeltaFrom}, max {MaxEntries}, changed {UpdatedEntries}, changed {(UpdateBaseline ? "BL update" : "")}, bytes {Bits2Bytes(Length)}";
	}
}

public class SVC_VoiceData : NetMessage
{
	public SVC_VoiceData() : base(SVC.VoiceData) { reliable = false; }
	public override NetChannelGroup GetGroup() => NetChannelGroup.Voice;

	public int FromClient;
	public bool Proximity;
	public int Length;
	public readonly bf_read DataIn = new();
	public byte[]? DataOut;

	public override bool ReadFromBuffer(bf_read buffer) {
		FromClient = buffer.ReadByte();
		Proximity = buffer.ReadByte() != 0;
		Length = buffer.ReadWord();

		buffer.CopyTo(DataIn);
		return buffer.SeekRelative(Length);
	}

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		buffer.WriteByte(FromClient);
		buffer.WriteByte(Proximity ? 1 : 0);
		buffer.WriteWord(Length);

		return buffer.WriteBits(DataOut, Length);
	}
}

public class CLC_VoiceData : NetMessage
{
	public CLC_VoiceData() : base(CLC.VoiceData) { reliable = false; }
	public override NetChannelGroup GetGroup() => NetChannelGroup.Voice;

	public int Length;
	public readonly bf_read DataIn = new();
	public readonly bf_write DataOut = new();

	public override bool ReadFromBuffer(bf_read buffer) {
		Length = buffer.ReadWord();  // length in bits
		buffer.CopyTo(DataIn);
		return buffer.SeekRelative(Length);
	}

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		Length = DataOut.BitsWritten;
		buffer.WriteWord(Length);    // length in bits
		return buffer.WriteBits(DataOut.GetData(), Length);
	}
}

public class CLC_Move : NetMessage
{
	public CLC_Move() : base(CLC.Move) { reliable = false; }
	public override NetChannelGroup GetGroup() => NetChannelGroup.Move;

	public int BackupCommands;
	public int NewCommands;
	public int Length;
	public readonly bf_read DataIn = new();
	public readonly bf_write DataOut = new();

	public override bool ReadFromBuffer(bf_read buffer) {
		NewCommands = (int)buffer.ReadUBitLong(NUM_NEW_COMMAND_BITS);
		BackupCommands = (int)buffer.ReadUBitLong(NUM_BACKUP_COMMAND_BITS);
		Length = buffer.ReadWord();
		buffer.CopyTo(DataIn);
		return buffer.SeekRelative(Length);
	}

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		Length = DataOut.BitsWritten;

		buffer.WriteUBitLong((uint)NewCommands, NUM_NEW_COMMAND_BITS);
		buffer.WriteUBitLong((uint)BackupCommands, NUM_BACKUP_COMMAND_BITS);

		buffer.WriteWord(Length);

		return buffer.WriteBits(DataOut.BaseArray, Length);
	}

	public override string ToString() => $"CLC_Move: backup {BackupCommands}, new {NewCommands}, bytes {Bits2Bytes(Length)}";
}
public class CLC_ListenEvents : NetMessage, IPoolableObject
{
	public CLC_ListenEvents() : base(CLC.ListenEvents) { }
	public override NetChannelGroup GetGroup() => NetChannelGroup.SignOn;
	public MaxEventNumberBitSet EventArray = new();

	public void Init() { }
	public void Reset() {
		EventArray.ClearAll();
	}

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		int count = MAX_EVENT_NUMBER / 32;
		for (int i = 0; i < count; i++)
			buffer.WriteUBitLong(EventArray.GetWord(i), 32);

		return !buffer.Overflowed;
	}

	public override bool ReadFromBuffer(bf_read buffer) {
		int count = MAX_EVENT_NUMBER / 32;
		for (int i = 0; i < count; i++)
			EventArray.SetWord(i, buffer.ReadUBitLong(32));

		return !buffer.Overflowed;
	}

	public override string ToString() {
		int count = 0;
		for (int i = 0; i < MAX_EVENT_NUMBER; i++) {
			if (EventArray.Get(i) != 0)
				count++;
		}
		return $"CLC_ListenEvents: registered events {count}";
	}
}
public class CLC_ClientInfo : NetMessage
{
	public CLC_ClientInfo() : base(CLC.ClientInfo) { }

	public int ServerCount;
	public int SendTableCRC;
	public bool IsHLTV;
	public ulong FriendsID;
	public string? FriendsName = string.Empty;
	public uint[] CustomFiles = new uint[MAX_CUSTOM_FILES];

	// 01110100
	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);

		buffer.WriteLong(ServerCount);
		buffer.WriteLong(SendTableCRC);


		buffer.WriteBool(IsHLTV);
		buffer.WriteLong((int)FriendsID);
		buffer.WriteString("");
		for (int i = 0; i < MAX_CUSTOM_FILES; i++) {
			if (CustomFiles[i] != 0) {
				buffer.WriteBool(true);
				buffer.WriteUBitLong(CustomFiles[i], 32);
			}
			else buffer.WriteBool(false);
		}
		//buffer.WriteBool(false);

		return !buffer.Overflowed;
	}

	public override bool ReadFromBuffer(bf_read buffer) {
		ServerCount = buffer.ReadLong();
		SendTableCRC = buffer.ReadLong();
		IsHLTV = buffer.ReadBool();
		FriendsID = (ulong)buffer.ReadLong();
		buffer.ReadString(out FriendsName, 256);
		for (int i = 0; i < MAX_CUSTOM_FILES; i++) {
			if (buffer.ReadBool()) {
				CustomFiles[i] = buffer.ReadUBitLong(32);
			}
			else CustomFiles[i] = 0;
		}

		return !buffer.Overflowed;
	}

	public override string ToString() {
		return $"ServerCount: {ServerCount}, SendTableCRC: {SendTableCRC}";
	}
}

public class CLC_BaselineAck : NetMessage
{
	public CLC_BaselineAck() : base(CLC.BaselineAck) { }
	public CLC_BaselineAck(int tick, int baseline) : base(CLC.BaselineAck) {
		BaselineTick = tick;
		BaselineNumber = baseline;
	}
	public override NetChannelGroup GetGroup() => NetChannelGroup.Entities;

	public int BaselineTick;
	public int BaselineNumber;

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		buffer.WriteLong(BaselineTick);
		buffer.WriteUBitLong((uint)BaselineNumber, 1);

		return !buffer.Overflowed;
	}

	public override bool ReadFromBuffer(bf_read buffer) {
		BaselineTick = buffer.ReadLong();
		BaselineNumber = (int)buffer.ReadUBitLong(1);
		return !buffer.Overflowed;
	}

	public override string ToString() => $"CLC_BaselineAck: tick {BaselineTick}";
}
public class SVC_TempEntities : NetMessage
{
	public SVC_TempEntities() : base(SVC.TempEntities) { }
	public override NetChannelGroup GetGroup() => NetChannelGroup.Events;
	public int NumEntries;
	public int Length;
	public readonly bf_read DataIn = new();
	public readonly bf_write DataOut = new();

	public override bool ReadFromBuffer(bf_read buffer) {
		NumEntries = (int)buffer.ReadUBitLong(EventInfo.EVENT_INDEX_BITS);
		Length = (int)buffer.ReadVarInt32();

		buffer.CopyTo(DataIn);
		return buffer.SeekRelative(Length);
	}

	public override bool WriteToBuffer(bf_write buffer) {
		Length = DataOut.BitsWritten;

		buffer.WriteNetMessageType(this);
		buffer.WriteUBitLong((uint)NumEntries, EventInfo.EVENT_INDEX_BITS);
		buffer.WriteVarInt32((uint)Length);
		return buffer.WriteBits(DataOut.GetData(), Length);
	}

	public override string ToString() {
		return $"svc_TempEntities: number {NumEntries}, bytes {Bits2Bytes(Length)}";
	}
}
public class CLC_RespondCvarValue : NetMessage
{
	public QueryCvarCookie_t Cookie;

	public ReadOnlySpan<char> CvarName {
		get => pointNameToBuffer ? NameBuffer.SliceNullTerminatedString() : cvarName;
		set {
			if (pointNameToBuffer) strcpy(NameBuffer, value); else cvarName = new(value);
		}
	}
	public ReadOnlySpan<char> CvarValue {
		get => pointValueToBuffer ? ValueBuffer.SliceNullTerminatedString() : cvarValue;
		set {
			if (pointValueToBuffer) strcpy(ValueBuffer, value); else cvarValue = new(value);
		}
	}
	public QueryCvarValueStatus StatusCode;

	string? cvarName;
	string? cvarValue;
	readonly char[] NameBuffer = new char[256];
	readonly char[] ValueBuffer = new char[256];
	bool pointNameToBuffer;
	bool pointValueToBuffer;

	public CLC_RespondCvarValue() : base(CLC.RespondCvarValue) { }

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);

		buffer.WriteSBitLong(Cookie, 32);
		buffer.WriteSBitLong((int)StatusCode, 4);

		buffer.WriteString(CvarName);
		buffer.WriteString(CvarValue);

		return !buffer.Overflowed;
	}
	public override bool ReadFromBuffer(bf_read buffer) {
		Cookie = buffer.ReadSBitLong(32);
		StatusCode = (QueryCvarValueStatus)buffer.ReadSBitLong(4);

		// Read the name.
		buffer.ReadString(NameBuffer);
		pointNameToBuffer = true;

		// Read the value.
		buffer.ReadString(ValueBuffer);
		pointValueToBuffer = true;

		return !buffer.Overflowed;
	}
}

internal static class NetFileFuncs
{

	public static readonly string[] g_MostCommonPathIDs = [
		"GAME",
		"MOD"
	];

	public static readonly string[] g_MostCommonPrefixes = [
		"materials",
		"models",
		"sounds",
		"scripts"
	];

	public static int FindCommonPathID(ReadOnlySpan<char> pathID) {
		for (int i = 0; i < g_MostCommonPathIDs.Length; i++) {
			if (stricmp(pathID, g_MostCommonPathIDs[i]) == 0)
				return i;
		}
		return -1;
	}

	public static int FindCommonPrefix(ReadOnlySpan<char> str) {
		for (int i = 0; i < g_MostCommonPrefixes.Length; i++) {
			if (stristr(str, g_MostCommonPrefixes[i]) == str) {
				int iNextChar = (int)strlen(g_MostCommonPrefixes[i]);
				if (str[iNextChar] == '/' || str[iNextChar] == '\\')
					return i;
			}
		}
		return -1;
	}
}

public class CLC_FileCRCCheck : NetMessage
{
	public InlineArrayMaxPath<char> PathID;
	public InlineArrayMaxPath<char> Filename;
	public MD5Value MD5;
	public CRC32_t CRCIOs;
	public int FileHashType;
	public int FileLen;
	public int PackFileNumber;
	public int PackFileID;
	public int FileFraction;

	public CLC_FileCRCCheck() : base(CLC.FileCRCCheck) { }
	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);

		// Reserved for future use.
		buffer.WriteOneBit(0);

		// Just write a couple bits for the path ID if it's one of the common ones.
		int iCode = NetFileFuncs.FindCommonPathID(PathID);
		if (iCode == -1) {
			buffer.WriteUBitLong(0, 2);
			buffer.WriteString(PathID);
		}
		else {
			buffer.WriteUBitLong((uint)(iCode + 1), 2);
		}

		iCode = NetFileFuncs.FindCommonPrefix(Filename);
		if (iCode == -1) {
			buffer.WriteUBitLong(0, 3);
			buffer.WriteChar(1); // so we can detect the new message version
			buffer.WriteString(Filename);
		}
		else {
			buffer.WriteUBitLong((uint)(iCode + 1), 3);
			buffer.WriteChar(1); // so we can detect the new message version
			buffer.WriteString(Filename[(int)(strlen(NetFileFuncs.g_MostCommonPrefixes[iCode]) + 1)..]);
		}

		buffer.WriteBits(const_reinterpret<MD5Value, byte>(new(in MD5)), Unsafe.SizeOf<MD5Value>() * 8);
		buffer.WriteUBitLong(CRCIOs, 32);
		buffer.WriteUBitLong((uint)FileHashType, 32);
		buffer.WriteUBitLong((uint)FileLen, 32);
		buffer.WriteUBitLong((uint)PackFileNumber, 32);
		buffer.WriteUBitLong((uint)PackFileID, 32);
		buffer.WriteUBitLong((uint)FileFraction, 32);
		return !buffer.Overflowed;
	}
	public override bool ReadFromBuffer(bf_read buffer) {
		buffer.ReadOneBit();

		// Read the path ID.
		int iCode = (int)buffer.ReadUBitLong(2);
		if (iCode == 0) {
			buffer.ReadString(PathID);
		}
		else if ((iCode - 1) < NetFileFuncs.g_MostCommonPathIDs.Length) {
			strcpy(PathID, NetFileFuncs.g_MostCommonPathIDs[iCode - 1]);
		}
		else {
			AssertMsg(false, "Invalid path ID code in CLC_FileCRCCheck");
			return false;
		}

		// Prefix string
		iCode = (int)buffer.ReadUBitLong(3);

		// Read filename, and check for the new message format version?
		Span<char> szTemp = stackalloc char[MAX_PATH];
		int c = buffer.ReadChar();
		bool bNewVersion = false;
		if (c == 1) {
			bNewVersion = true;
			buffer.ReadString(szTemp);
		}
		else {
			szTemp[0] = (char)c;
			buffer.ReadString(szTemp[1..]);
		}
		if (iCode == 0) {
			strcpy(Filename, szTemp);
		}
		else if ((iCode - 1) < NetFileFuncs.g_MostCommonPrefixes.Length) {
			sprintf(Filename, "%s%c%s").S(NetFileFuncs.g_MostCommonPrefixes[iCode - 1]).C(StrTools.CORRECT_PATH_SEPARATOR).S(szTemp);
		}
		else {
			AssertMsg(false, "Invalid prefix code in CLC_FileCRCCheck.");
			return false;
		}

		if (bNewVersion) {
			buffer.ReadBits(reinterpret<MD5Value, byte>(new(ref MD5)), Unsafe.SizeOf<MD5Value>() * 8);
			CRCIOs = buffer.ReadUBitLong(32);
			FileHashType = (int)buffer.ReadUBitLong(32);
			FileLen = (int)buffer.ReadUBitLong(32);
			PackFileNumber = (int)buffer.ReadUBitLong(32);
			PackFileID = (int)buffer.ReadUBitLong(32);
			FileFraction = (int)buffer.ReadUBitLong(32);
		}
		else {
			/* m_CRC */
			buffer.ReadUBitLong(32);
			CRCIOs = buffer.ReadUBitLong(32);
			FileHashType = (int)buffer.ReadUBitLong(32);
		}

		return !buffer.Overflowed;
	}
}
public class CLC_FileMD5Check : NetMessage
{
	public InlineArrayMaxPath<char> PathID;
	public InlineArrayMaxPath<char> Filename;
	public MD5Value MD5;

	public CLC_FileMD5Check() : base(CLC.FileMD5Check) { }
	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);

		// Reserved for future use.
		buffer.WriteOneBit(0);

		// Just write a couple bits for the path ID if it's one of the common ones.
		int iCode = NetFileFuncs.FindCommonPathID(PathID);
		if (iCode == -1) {
			buffer.WriteUBitLong(0, 2);
			buffer.WriteString(PathID);
		}
		else {
			buffer.WriteUBitLong((uint)iCode + 1, 2);
		}

		iCode = NetFileFuncs.FindCommonPrefix(Filename);
		if (iCode == -1) {
			buffer.WriteUBitLong(0, 3);
			buffer.WriteString(Filename);
		}
		else {
			buffer.WriteUBitLong((uint)iCode + 1, 3);
			buffer.WriteString(Filename[((int)strlen(NetFileFuncs.g_MostCommonPrefixes[iCode]) + 1)..]);
		}

		buffer.WriteBytes(const_reinterpret<MD5Value, byte>(new(in MD5)));

		return !buffer.Overflowed;
	}
	public override bool ReadFromBuffer(bf_read buffer) {
		// Reserved for future use.
		buffer.ReadOneBit();

		// Read the path ID.
		int iCode = (int)buffer.ReadUBitLong(2);
		if (iCode == 0) {
			buffer.ReadString(PathID);
		}
		else if ((iCode - 1) < NetFileFuncs.g_MostCommonPathIDs.Length) {
			strcpy(PathID, NetFileFuncs.g_MostCommonPathIDs[iCode - 1]);
		}
		else {
			AssertMsg(false, "Invalid path ID code in CLC_FileMD5Check");
			return false;
		}

		// Read the filename.
		iCode = (int)buffer.ReadUBitLong(3);
		if (iCode == 0) {
			buffer.ReadString(Filename);
		}
		else if ((iCode - 1) < NetFileFuncs.g_MostCommonPrefixes.Length) {
			Span<char> szTemp = stackalloc char[MAX_PATH];
			buffer.ReadString(szTemp);
			sprintf(Filename, "%s%c%s").S(NetFileFuncs.g_MostCommonPrefixes[iCode - 1]).C(StrTools.CORRECT_PATH_SEPARATOR).S(szTemp);
		}
		else {
			AssertMsg(false, "Invalid prefix code in CLC_FileMD5Check.");
			return false;
		}

		buffer.ReadBytes(reinterpret<MD5Value, byte>(new(ref MD5)));

		return !buffer.Overflowed;
	}
}

public abstract class Base_CmdKeyValues(KeyValues? keyValues, byte type) : NetMessage(type)
{
	public KeyValues? GetKeyValues() => KeyValues;

	public KeyValues? KeyValues = keyValues;
	public override bool ReadFromBuffer(bf_read buffer) {
		if (KeyValues == null)
			KeyValues = new KeyValues("");

		KeyValues.Clear();

		int numBytes = buffer.ReadLong();
		if (numBytes <= 0 || numBytes > buffer.BytesLeft)
			return false; // don't read past the end of the buffer

		byte[] pvBuffer = ArrayPool<byte>.Shared.Rent(numBytes);
		buffer.ReadBits(pvBuffer, numBytes * 8);

		UtlBuffer bufRead = new(pvBuffer, UtlBuffer.BufferFlags.ReadOnly);
		if (!KeyValues.ReadAsBinary(bufRead)) {
			Assert(false);
			ArrayPool<byte>.Shared.Return(pvBuffer, true);
			return false;
		}

		ArrayPool<byte>.Shared.Return(pvBuffer, true);
		return !buffer.Overflowed;
	}
	public override bool WriteToBuffer(bf_write buffer) {
		if (KeyValues == null)
			return false;
		buffer.WriteNetMessageType(this);

		UtlBuffer bufData = new();
		if (!KeyValues.WriteAsBinary(bufData)) {
			Assert(false);
			return false;
		}

		// Note how many we're sending
		int numBytes = bufData.TellMaxPut();
		buffer.WriteLong(numBytes);
		buffer.WriteBits(bufData.Base(), numBytes * 8);

		return !buffer.Overflowed;
	}
}

public class CLC_CmdKeyValues : Base_CmdKeyValues
{
	public CLC_CmdKeyValues() : base(null, CLC.CmdKeyValues) { }
	public CLC_CmdKeyValues(KeyValues? keyValues) : base(keyValues, CLC.CmdKeyValues) { }

}

public class SVC_CmdKeyValues : Base_CmdKeyValues
{
	public SVC_CmdKeyValues() : base(null, CLC.CmdKeyValues) { }
	public SVC_CmdKeyValues(KeyValues? keyValues) : base(keyValues, CLC.CmdKeyValues) { }
}
// CLC_SaveReplay removed?
/*
public class SVC_SetPauseTimed(bool paused, TimeUnit_t expireTime) : NetMessage(SVC.SetPauseTimed)
{
	public bool Paused = paused;
	public TimeUnit_t ExpireTime = expireTime;

	public override bool ReadFromBuffer(bf_read buffer) {
		Paused = buffer.ReadOneBit() != 0;
		ExpireTime = buffer.ReadFloat();
		return !buffer.Overflowed;
	}

	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		buffer.WriteOneBit(Paused ? 1 : 0);
		buffer.WriteFloat((float)ExpireTime);
		return !buffer.Overflowed;
	}
}
*/
public class EventInfo
{
	public const int EVENT_INDEX_BITS = 8;
	public const int EVENT_DATA_LEN_BITS = 11;
	public const int MAX_EVENT_DATA = 192;
}


public class SVC_SetPause : NetMessage
{
	public SVC_SetPause() : base(SVC.SetPause) { }
	public bool Paused;
	public override bool WriteToBuffer(bf_write buffer) {
		buffer.WriteNetMessageType(this);
		buffer.WriteOneBit(Paused ? 1 : 0);
		return !buffer.Overflowed;
	}
	public override bool ReadFromBuffer(bf_read buffer) {
		Paused = buffer.ReadOneBit() != 0;
		return !buffer.Overflowed;
	}
}


#if GMOD_DLL

public struct GMod_NetMessage
{
	public int NetMessageID;
	public int DataBits;
	public Memory<byte> Data;
}

public struct GMod_LuaAutoRefresh
{

}

public struct GMod_LuaError
{
	public string Error;
}

public struct GMod_RequestLuaFiles;

public struct GMod_LuaCmd
{
	public Memory<byte> Data;
}

public struct GMod_LuaFile_CLC
{
	public InlineArray8192<ushort> FileStringTableEntryIDs;
}

public struct GMod_LuaFile_SVC
{
	public ushort FileStringTableEntryID;
	public SHA256Value FileSHA256;
	public Memory<byte> FileContents;
}

public abstract class BaseGModNetMessage(int type, GModMessageType messageType) : NetMessage(type)
{
	// message length (bits)
	public const int GMOD_NETMESSAGE_LENGTH_BITS = 20;
	protected int Bits;
	// message type
	public GModMessageType MessageType = messageType;
	// message contents
	public GMod_NetMessage NetMessage;
	public GMod_LuaAutoRefresh LuaAutoRefresh;
	public GMod_LuaError LuaError;
	public GMod_RequestLuaFiles RequestLuaFiles;
	public GMod_LuaCmd LuaCmd;
	public Memory<byte> RawData;
	public int RawBits;

	public override bool ReadFromBuffer(bf_read buffer) {
		int bits = (int)buffer.ReadUBitLong(GMOD_NETMESSAGE_LENGTH_BITS);
		return ReadPayload(buffer, bits);
	}

	public bool ReadPayload(bf_read buffer, int bits) {
		Bits = bits;
		int startBit = buffer.BitsRead;
		int endBit = startBit + bits;
		RawBits = Math.Max(bits, 0);
		RawData = new byte[Bits2Bytes(RawBits)];
		if (RawBits > 0) {
			buffer.ReadBits(RawData.Span, RawBits);
			buffer.Seek(startBit);
		}
		MessageType = (GModMessageType)buffer.ReadByte();
		if (bits < 1)
			return true;

		if (bits < 0) {
			Warning("Received invalid Garry's Mod net message\n");
			return true;
		}

		int toRead;
		switch (MessageType) {
			case GModMessageType.NetMessage:
				NetMessage.NetMessageID = buffer.ReadWord();
				NetMessage.DataBits = Math.Max(bits - 8 - 16, 0);
				NetMessage.Data = new byte[Bits2Bytes(NetMessage.DataBits)];
				if (NetMessage.DataBits > 0)
					buffer.ReadBits(NetMessage.Data.Span, NetMessage.DataBits);
				break;
			case GModMessageType.LuaAutoRefresh:
				Warning($"LuaAutoRefresh needs to be implemented!\n");
				break;
			case GModMessageType.LuaError:
				if ((toRead = bits - 8) > 0) {
					byte[] error = new byte[Bits2Bytes(toRead)];
					buffer.ReadBits(error, toRead);
					int length = Array.IndexOf(error, (byte)0);
					LuaError.Error = Encoding.UTF8.GetString(error, 0, length < 0 ? error.Length : length);
				}
				else
					LuaError.Error = "";
				break;
			case GModMessageType.RequestLuaFiles: /* no body */  break;
			case GModMessageType.LuaFile:
				ReadLuaFile(buffer, endBit);
				break;
			case GModMessageType.LuaCmd:
				toRead = bits - 8;
				LuaCmd.Data = new byte[Bits2Bytes(Math.Max(toRead, 0))];
				if (toRead > 0)
					buffer.ReadBits(LuaCmd.Data.Span, toRead);
				break;
		}

		return true;
	}

	public override bool WriteToBuffer(bf_write buffer) {
		// Length determination
		int bits = 8;
		switch (MessageType) {
			case GModMessageType.NetMessage:
				bits += sizeof(ushort) * 8;
				bits += NetMessage.DataBits;
				break;
			case GModMessageType.LuaAutoRefresh:

				break;
			case GModMessageType.LuaError:
				bits += (Encoding.UTF8.GetByteCount(LuaError.Error ?? "") + 1) * 8;
				break;
			case GModMessageType.RequestLuaFiles: /* no body */  break;
			case GModMessageType.LuaFile:
				bits += GetLuaFileMessageBits();
				break;
			case GModMessageType.LuaCmd:
				bits += LuaCmd.Data.Length * 8;
				break;
		}

		Bits = bits;
		buffer.WriteNetMessageType(this);
		buffer.WriteUBitLong((uint)bits, GMOD_NETMESSAGE_LENGTH_BITS);
		buffer.WriteByte((byte)MessageType);

		// Send value
		switch (MessageType) {
			case GModMessageType.NetMessage:
				buffer.WriteWord(NetMessage.NetMessageID);
				buffer.WriteBits(NetMessage.Data.Span, NetMessage.DataBits);
				break;
			case GModMessageType.LuaAutoRefresh:
				Warning($"LuaAutoRefresh needs to be implemented!\n");
				break;
			case GModMessageType.LuaError:
				buffer.WriteBytes(Encoding.UTF8.GetBytes((LuaError.Error ?? "") + "\0"));
				break;
			case GModMessageType.RequestLuaFiles: /* no body */  break;
			case GModMessageType.LuaFile:
				WriteLuaFile(buffer);
				break;
			case GModMessageType.LuaCmd:
				buffer.WriteBytes(LuaCmd.Data.Span);
				break;
		}

		return true;
	}

	public abstract void ReadLuaFile(bf_read buffer, int endBit);
	public abstract void WriteLuaFile(bf_write buffer);
	public abstract int GetLuaFileMessageBits();
}

public class SVC_GMod_ServerToClient : BaseGModNetMessage
{
	public GMod_LuaFile_SVC LuaFile;

	public SVC_GMod_ServerToClient() : base(SVC.GMod_ServerToClient, 0) { }
	public SVC_GMod_ServerToClient(GModMessageType messageType) : base(SVC.GMod_ServerToClient, messageType) { }
	public override string ToString() => $"SVC_GMod_ServerToClient: length {Bits2Bytes(Bits)}";

	public override void ReadLuaFile(bf_read buffer, int endBit) {
		LuaFile.FileStringTableEntryID = (ushort)buffer.ReadUBitLong(16);
		buffer.ReadBytes(SHA256Value.ToEditableBytes(ref LuaFile.FileSHA256));

		int fileBits = endBit - buffer.BitsRead;
		LuaFile.FileContents = new byte[fileBits >> 3];
		buffer.ReadBits(LuaFile.FileContents.Span, fileBits);
	}

	public override void WriteLuaFile(bf_write buffer) {
		buffer.WriteWord((int)LuaFile.FileStringTableEntryID);
		buffer.WriteBytes(SHA256Value.ToBytes(ref LuaFile.FileSHA256));
		buffer.WriteBytes(LuaFile.FileContents.Span);
	}

	public override int GetLuaFileMessageBits() => 8 * (sizeof(ushort) + SHA256Value.SIZE_BYTES + LuaFile.FileContents.Length);
}


public class CLC_GMod_ClientToServer : BaseGModNetMessage
{
	public GMod_LuaFile_CLC LuaFile;

	public CLC_GMod_ClientToServer() : base(CLC.GMod_ClientToServer, 0) { }
	public CLC_GMod_ClientToServer(GModMessageType messageType) : base(CLC.GMod_ClientToServer, messageType) { }

	public override string ToString() => $"CLC_GMod_ClientToServer: length {Bits2Bytes(Bits)}";

	public override void ReadLuaFile(bf_read buffer, int endBit) {
		Span<ushort> write = LuaFile.FileStringTableEntryIDs;
		for (int i = 0; i < write.Length; i++) {
			ushort read = buffer.ReadWord();
			if (read == 0) {
				write[i] = 0;
				break;
			}
			write[i] = read;
		}
	}

	public override void WriteLuaFile(bf_write buffer) {
		Span<ushort> write = LuaFile.FileStringTableEntryIDs;
		for (int i = 0; i < write.Length; i++) {
			buffer.WriteUBitLong(write[i], 16);

			if (write[i] == 0)
				break;
		}
	}

	public override int GetLuaFileMessageBits() {
		Span<ushort> write = LuaFile.FileStringTableEntryIDs;
		int idx = write.IndexOf((ushort)0);
		int count = idx < 0 ? write.Length : idx + 1;
		return count * 16;
	}
}
#endif
