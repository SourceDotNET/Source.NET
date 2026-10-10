#if CLIENT_DLL || GAME_DLL
using Source.Common.Bitbuffers;
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.InteropServices;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaNet
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_net = new("net");

	static readonly byte[] g_Buffer = new byte[0x10000];
	static readonly bf_write g_Write = new();
	static bool g_Started;
	static bool g_Reliable = true;
	public static bf_read? g_NetIncoming;

	[LuaFunction]
	static int Start(ILuaInterface lua) {
		string name = g_Lua!.CheckString(1);
		if (g_Started)
			g_Lua.ErrorFromLua($"Warning! A net message ({g_Write.DebugName}) is already started! Discarding in favor of the new message! ({name})\n");

		int id = NetworkString.Get(name);
		if (id <= 0) {
			g_Lua.Error("Calling net.Start with unpooled message name! (Did you forget to call util.AddNetworkString serverside?)");
			return 0;
		}

		g_Write.StartWriting(g_Buffer, 0x10000, 0, -1);
		g_Write.WriteByte(0);
		g_Write.WriteWord(id);
		g_Write.DebugName = NetworkString.Convert(id)!;
		g_Started = true;
		g_Reliable = !g_Lua.GetBool(2);
		g_Lua.PushBool(true);
		return 1;
	}

	[LuaFunction]
	static int WriteFloat(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		float value = (float)g_Lua!.CheckNumber(1);
		g_Write.WriteBits(MemoryMarshal.AsBytes(new ReadOnlySpan<float>(in value)), 32);
		return 0;
	}

	[LuaFunction]
	static int WriteDouble(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		double value = g_Lua!.CheckNumber(1);
		g_Write.WriteBits(MemoryMarshal.AsBytes(new ReadOnlySpan<double>(in value)), 64);
		return 0;
	}

	[LuaFunction]
	static int WriteBit(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		bool bit;
		if (g_Lua!.GetType(1) == LuaType.Number)
			bit = lua.GetNumber(1) != 0;
		else
			bit = g_Lua.GetBool(1);

		g_Write.WriteOneBit(bit ? 1 : 0);
		return 0;
	}

	[LuaFunction]
	static int WriteString(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		g_Lua!.CheckString(1);
		ReadOnlySpan<byte> str = g_Lua.GetStringBytes(1);
		int length = str.IndexOf((byte)0);
		g_Write.WriteBytes(length < 0 ? str : str[..length]);
		g_Write.WriteByte(0);
		return 0;
	}

	[LuaFunction]
	static int WriteData(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		ReadOnlySpan<byte> data;
		if (g_Lua!.GetType(1) == LuaType.String)
			data = g_Lua.GetStringBytes(1);
		else {
			g_Lua.CheckString(1);
			data = g_Lua.GetStringBytes(1);
			int nul = data.IndexOf((byte)0);
			if (nul >= 0)
				data = data[..nul];
		}

		int len = data.Length;
		int length = LuaHelper.cvttsd2si(g_Lua.CheckNumberOpt(2, (uint)len));
		if (length == 0)
			return 0;

		if ((uint)length > 0x10000) {
			g_Lua.ErrorFromLua($"net.WriteData: Invalid length {length}!");
			return 0;
		}

		if ((uint)length > (uint)len) {
			g_Lua.ErrorFromLua($"net.WriteData ({g_Write.DebugName}): Warning! Given length ({length}) is longer than length of provided data ({len})! Clamping!");
			length = len;
		}

		g_Write.WriteBytes(data[..length]);
		return 0;
	}

	[LuaFunction]
	static int WriteVector(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		g_Write.WriteBitVec3Coord(LuaVector.Get_Vector(1));
		return 0;
	}

	[LuaFunction]
	static int WriteNormal(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		g_Write.WriteBitVec3Normal(LuaVector.Get_Vector(1));
		return 0;
	}

	[LuaFunction]
	static int WriteAngle(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		g_Write.WriteBitAngles(LuaAngle.Get_Angle(1));
		return 0;
	}

	[LuaFunction]
	static int WriteMatrix(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		ref Matrix4x4 matrix = ref LuaVMatrix.Get_VMatrix(1);
		for (int i = 0; i < 4; i++)
			for (int j = 0; j < 4; j++)
				g_Write.WriteFloat(matrix[i, j]);
		return 0;
	}

	[LuaFunction]
	static int WriteInt(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		int bits = LuaHelper.cvttsd2si(g_Lua!.CheckNumber(2));
		if (bits <= 0)
			return 0;

		g_Write.WriteSBitLong(LuaHelper.cvttsd2si(g_Lua.CheckNumber(1)), bits);
		return 0;
	}

	[LuaFunction]
	static int WriteUInt(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		int bits = LuaHelper.cvttsd2si(g_Lua!.CheckNumber(2));
		if (bits <= 0)
			return 0;

		uint value = (uint)LuaHelper.cvttsd2si64(g_Lua.CheckNumber(1));
		if (g_Write.BitsLeft < bits) {
			g_Write.Seek((int)g_Write.MaxBits);
			g_Write.SetOverflowFlag();
			return 0;
		}

		g_Write.WriteUBitLong(value & ((2u << (bits - 1)) - 1), bits, false);
		return 0;
	}

	[LuaFunction]
	static int WriteUInt64(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		ulong value = Bootil.String.To.UInt64(g_Lua!.CheckString(1));
		g_Write.WriteLongLong((long)value);
		return 0;
	}

	[LuaFunction]
	static int BytesWritten(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		g_Lua!.PushNumber((g_Write.BitsWritten + 7) >> 3);
		g_Lua.PushNumber(g_Write.BitsWritten);
		return 2;
	}

#if CLIENT_DLL
	[LuaFunction]
	static int SendToServer(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		if (g_Write.Overflowed)
			lua.ErrorFromLua("Trying to send an overflowed net message!");

		engine.GMOD_SendToServer(g_Buffer, g_Write.BitsWritten, g_Reliable);
		g_Started = false;
		return 0;
	}
#else
	static int GetPlayerCount() {
		int count = 0;
		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			if (Util.PlayerByIndex(i) != null)
				count++;
		}
		return count;
	}

	static void SendFilter(RecipientFilter filter) => engine.GMOD_SendToClient(ref filter, g_Buffer, g_Write.BitsWritten);

	[LuaFunction]
	static int Broadcast(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		if (GetPlayerCount() <= 0) {
			DevWarning($"Warning! Trying to net.Broadcast a message '{g_Write.DebugName}' with no players on server!\n");
			g_Started = false;
			return 0;
		}

		if (g_Write.Overflowed)
			lua.ErrorFromLua("Trying to send an overflowed net message!");

		BroadcastRecipientFilter filter = new();
		if (g_Reliable)
			filter.MakeReliable();

		SendFilter(filter);
		g_Started = false;
		return 0;
	}

	[LuaFunction]
	static int Send(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		if (GetPlayerCount() <= 0) {
			DevWarning($"Warning! Trying to net.Send a message '{g_Write.DebugName}' with no players on server!\n");
			g_Started = false;
			return 0;
		}

		if (g_Write.Overflowed)
			lua.ErrorFromLua("Trying to send an overflowed net message!");

		RecipientFilter filter = new();
		if (g_Reliable)
			filter.MakeReliable();

		LuaObject obj = new();
		obj.Set(g_Lua!.GetObject(1));
		if (obj.isTable()) {
			for (int i = 1; ; i++) {
				LuaObject value = new();
				obj.GetMember(i, value);
				if (value.isNil()) {
					value.UnReference();
					break;
				}

				if (value.GetType() == LuaType.Entity) {
					BaseEntity? ent = LuaEntity.GetEntityFromUserData(value.GetUserData());
					if (ent != null && ent.IsPlayer())
						filter.AddRecipient(ToBasePlayer(ent)!);
				}
				value.UnReference();
			}
		}
		else if (obj.GetType() == LuaType.Entity) {
			BaseEntity? ent = LuaEntity.GetEntityFromUserData(obj.GetUserData());
			if (ent == null) {
				g_Started = false;
				obj.UnReference();
				return 0;
			}

			if (!ent.IsPlayer()) {
				g_Lua.ErrorFromLua($"Warning! Trying to net.Send a message '{g_Write.DebugName}' to a non-player!\n");
				g_Started = false;
				obj.UnReference();
				return 0;
			}

			filter.AddRecipient(ToBasePlayer(ent)!);
		}
		// todo: else if (obj.GetType() == LuaType.RecipientFilter) for each recipient in Get_CRecipientFilter(1): filter.AddRecipient(UTIL_PlayerByIndex(...))
		else {
			g_Started = false;
			LuaEntity.Get_Entity(1, false);
			obj.UnReference();
			return 0;
		}

		SendFilter(filter);
		g_Started = false;
		obj.UnReference();
		return 0;
	}

	[LuaFunction]
	static int SendOmit(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		if (GetPlayerCount() <= 0) {
			DevWarning($"Warning! Trying to net.SendOmit a message '{g_Write.DebugName}' with no players on server!\n");
			g_Started = false;
			return 0;
		}

		if (g_Write.Overflowed)
			lua.ErrorFromLua("Trying to send an overflowed net message!");

		RecipientFilter filter = new();
		if (g_Reliable)
			filter.MakeReliable();
		filter.AddAllPlayers();

		LuaObject obj = new();
		obj.Set(g_Lua!.GetObject(1));
		if (obj.isTable()) {
			for (int i = 1; ; i++) {
				LuaObject value = new();
				obj.GetMember(i, value);
				if (value.isNil()) {
					value.UnReference();
					break;
				}

				if (value.GetType() == LuaType.Entity) {
					BaseEntity? ent = LuaEntity.GetEntityFromUserData(value.GetUserData());
					if (ent != null && ent.IsPlayer())
						filter.RemoveRecipient(ToBasePlayer(ent)!);
				}
				value.UnReference();
			}
		}
		else if (obj.GetType() == LuaType.Entity) {
			BaseEntity? ent = LuaEntity.GetEntityFromUserData(obj.GetUserData());
			if (ent == null) {
				g_Started = false;
				obj.UnReference();
				return 0;
			}

			if (!ent.IsPlayer()) {
				g_Lua.ErrorFromLua($"Warning! Trying to net.SendOmit a message '{g_Write.DebugName}' to a non-player!\n");
				g_Started = false;
				obj.UnReference();
				return 0;
			}

			filter.RemoveRecipient(ToBasePlayer(ent)!);
		}
		// todo: else if (obj.GetType() == LuaType.RecipientFilter) for each recipient in Get_CRecipientFilter(1): filter.RemoveRecipient(UTIL_PlayerByIndex(...))
		else {
			g_Started = false;
			LuaEntity.Get_Entity(1, false);
			obj.UnReference();
			return 0;
		}

		SendFilter(filter);
		g_Started = false;
		obj.UnReference();
		return 0;
	}

	[LuaFunction]
	static int SendPVS(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		if (GetPlayerCount() <= 0) {
			DevWarning($"Warning! Trying to net.SendPVS a message '{g_Write.DebugName}' with no players on server!\n");
			g_Started = false;
			return 0;
		}

		if (g_Write.Overflowed)
			lua.ErrorFromLua("Trying to send an overflowed net message!");

		RecipientFilter filter = new();
		filter.AddRecipientsByPVS(LuaVector.Get_Vector(1));
		if (g_Reliable)
			filter.MakeReliable();

		SendFilter(filter);
		g_Started = false;
		return 0;
	}

	[LuaFunction]
	static int SendPAS(ILuaInterface lua) {
		if (!g_Started)
			return 0;

		if (GetPlayerCount() <= 0) {
			DevWarning($"Warning! Trying to net.SendPAS a message '{g_Write.DebugName}' with no players on server!\n");
			g_Started = false;
			return 0;
		}

		if (g_Write.Overflowed)
			lua.ErrorFromLua("Trying to send an overflowed net message!");

		RecipientFilter filter = new();
		filter.AddRecipientsByPAS(LuaVector.Get_Vector(1));
		if (g_Reliable)
			filter.MakeReliable();

		SendFilter(filter);
		g_Started = false;
		return 0;
	}
#endif

	static void ValidateDataSize(ILuaInterface lua, int bits) {
		if (g_NetIncoming == null)
			return;

		if (Game.Client.GarrysMod.GarrysMod.lua_strict.GetInt() != 0 && bits > g_NetIncoming.BitsLeft)
			lua.ErrorFromLua($"Trying to read more data ({bits} bit) than the net message has left ({g_NetIncoming.BitsLeft} bits)!\n");
	}

	[LuaFunction]
	static int ReadData(ILuaInterface lua) {
		if (g_NetIncoming == null)
			return 0;

		int length = (int)g_NetIncoming.BytesLeft;
		int requested = LuaHelper.cvttsd2si(g_Lua!.CheckNumber(1));
		if (requested <= length)
			length = requested;

		if (length != 0) {
			if ((uint)(length - 1) > 0xFFFF)
				g_Lua.ErrorFromLua($"net.ReadData: Invalid length {length}!");
			else {
				byte[] data = new byte[length];
				g_NetIncoming.ReadBytes(data);
				g_Lua.PushString(data);
				return 1;
			}
		}

		g_Lua.PushString("");
		return 1;
	}

	[LuaFunction]
	static int ReadHeader(ILuaInterface lua) {
		if (g_NetIncoming == null)
			return 0;

		ValidateDataSize(lua, 16);
		g_Lua!.PushNumber(g_NetIncoming.ReadUBitLong(16));
		return 1;
	}

	[LuaFunction]
	static int ReadBit(ILuaInterface lua) {
		if (g_NetIncoming == null)
			return 0;

		ValidateDataSize(lua, 1);
		g_Lua!.PushNumber(g_NetIncoming.ReadUBitLong(1) != 0 ? 1 : 0);
		return 1;
	}

	[LuaFunction]
	static int ReadFloat(ILuaInterface lua) {
		if (g_NetIncoming == null)
			return 0;

		ValidateDataSize(lua, 32);
		float value = 0;
		g_NetIncoming.ReadBits(MemoryMarshal.AsBytes(new Span<float>(ref value)), 32);
		g_Lua!.PushNumber(value);
		return 1;
	}

	[LuaFunction]
	static int ReadDouble(ILuaInterface lua) {
		if (g_NetIncoming == null)
			return 0;

		ValidateDataSize(lua, 64);
		double value = 0;
		g_NetIncoming.ReadBits(MemoryMarshal.AsBytes(new Span<double>(ref value)), 64);
		g_Lua!.PushNumber(value);
		return 1;
	}

	[LuaFunction]
	static int ReadVector(ILuaInterface lua) {
		if (g_NetIncoming == null)
			return 0;

		ValidateDataSize(lua, 3);
		g_NetIncoming.ReadBitVec3Coord(out Vector3 vec);
		LuaVector.Push_Vector(vec);
		return 1;
	}

	[LuaFunction]
	static int ReadNormal(ILuaInterface lua) {
		if (g_NetIncoming == null)
			return 0;

		ValidateDataSize(lua, 3);
		g_NetIncoming.ReadBitVec3Normal(out Vector3 vec);
		LuaVector.Push_Vector(vec);
		return 1;
	}

	[LuaFunction]
	static int ReadAngle(ILuaInterface lua) {
		if (g_NetIncoming == null)
			return 0;

		ValidateDataSize(lua, 3);
		g_NetIncoming.ReadBitAngles(out QAngle ang);
		LuaAngle.Push_Angle(ang);
		return 1;
	}

	[LuaFunction]
	static int ReadMatrix(ILuaInterface lua) {
		if (g_NetIncoming == null)
			return 0;

		ValidateDataSize(lua, 512);
		Matrix4x4 matrix = default;
		for (int i = 0; i < 4; i++)
			for (int j = 0; j < 4; j++)
				matrix[i, j] = g_NetIncoming.ReadFloat();
		LuaVMatrix.Push_VMatrix(matrix);
		return 1;
	}

	static readonly byte[] strString = new byte[0x10000];

	[LuaFunction]
	static int ReadString(ILuaInterface lua) {
		if (g_NetIncoming == null)
			return 0;

		ValidateDataSize(lua, 8);
		g_NetIncoming.ReadString(strString, false, out _);
		ReadOnlySpan<byte> str = strString;
		int length = str.IndexOf((byte)0);
		g_Lua!.PushString(length < 0 ? str : str[..length]);
		return 1;
	}

	[LuaFunction]
	static int ReadInt(ILuaInterface lua) {
		if (g_NetIncoming == null)
			return 0;

		int bits = LuaHelper.cvttsd2si(g_Lua!.CheckNumber(1));
		if (bits <= 0)
			return 0;

		ValidateDataSize(lua, bits);
		g_Lua.PushNumber(g_NetIncoming.ReadSBitLong(bits));
		return 1;
	}

	[LuaFunction]
	static int ReadUInt(ILuaInterface lua) {
		if (g_NetIncoming == null)
			return 0;

		int bits = LuaHelper.cvttsd2si(g_Lua!.CheckNumber(1));
		if (bits <= 0)
			return 0;

		ValidateDataSize(lua, bits);
		g_Lua.PushNumber(g_NetIncoming.ReadUBitLong(bits));
		return 1;
	}

	[LuaFunction]
	static int ReadUInt64(ILuaInterface lua) {
		if (g_NetIncoming == null)
			return 0;

		ValidateDataSize(lua, 64);
		g_Lua!.PushString(Bootil.String.Format.UInt64((ulong)g_NetIncoming.ReadLongLong()));
		return 1;
	}

	[LuaFunction]
	static int BytesLeft(ILuaInterface lua) {
		if (g_NetIncoming == null)
			return 0;

		g_Lua!.PushNumber((g_NetIncoming.BitsLeft + 7) >> 3);
		g_Lua.PushNumber(g_NetIncoming.BitsLeft);
		return 2;
	}

	[LuaFunction]
	static void Abort() {
		g_Reliable = true;
		g_Started = false;
	}
}
#endif
