using Game.Shared;

using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Client.GarrysMod;

public static partial class LuaBfRead
{
	[LuaClass(typeof(bf_read))]
	public static readonly LuaClass LC_bf_read = new("bf_read", LuaType.UserMsg, null, null);

	public static void Push_bf_read(bf_read? buf) => LC_bf_read.Push(buf);

	[LuaMethod]
	static int bf_read__ReadAngle(ILuaInterface lua) {
		bf_read buf = (bf_read)LC_bf_read.Get(1)!;
		buf.ReadBitAngles(out QAngle angles);
		LuaAngle.Push_Angle(angles);
		return 1;
	}

	[LuaMethod]
	static int bf_read__ReadBool(ILuaInterface lua) {
		bf_read buf = (bf_read)LC_bf_read.Get(1)!;
		lua.PushBool(buf.ReadOneBit() != 0);
		return 1;
	}

	[LuaMethod]
	static int bf_read__ReadChar(ILuaInterface lua) {
		bf_read buf = (bf_read)LC_bf_read.Get(1)!;
		lua.PushNumber(buf.ReadChar());
		return 1;
	}

	[LuaMethod]
	static int bf_read__ReadEntity(ILuaInterface lua) {
		bf_read buf = (bf_read)LC_bf_read.Get(1)!;
		uint handle = LuaNetworkedVars.ReadEHandle(buf);
		if (handle == 0xFFFFFFFF)
			return 0;
		if (handle == 0) {
			LuaEntity.Push_Entity(null);
			return 1;
		}
		LuaEntity.Push_Entity(new EHANDLE(handle).Get());
		return 1;
	}

	[LuaMethod]
	static int bf_read__ReadFloat(ILuaInterface lua) {
		bf_read buf = (bf_read)LC_bf_read.Get(1)!;
		lua.PushNumber(buf.ReadFloat());
		return 1;
	}

	[LuaMethod]
	static int bf_read__ReadLong(ILuaInterface lua) {
		bf_read buf = (bf_read)LC_bf_read.Get(1)!;
		lua.PushNumber(buf.ReadLong());
		return 1;
	}

	[LuaMethod]
	static int bf_read__ReadShort(ILuaInterface lua) {
		bf_read buf = (bf_read)LC_bf_read.Get(1)!;
		lua.PushNumber(buf.ReadShort());
		return 1;
	}

	[LuaMethod]
	static int bf_read__ReadString(ILuaInterface lua) {
		bf_read buf = (bf_read)LC_bf_read.Get(1)!;
		Span<byte> str = stackalloc byte[128];
		buf.ReadString(str, false, out int numChars);
		lua.PushString(str[..numChars]);
		return 1;
	}

	[LuaMethod]
	static int bf_read__ReadVector(ILuaInterface lua) {
		bf_read buf = (bf_read)LC_bf_read.Get(1)!;
		buf.ReadBitVec3Coord(out Vector3 vec);
		LuaVector.Push_Vector(vec);
		return 1;
	}

	[LuaMethod]
	static int bf_read__ReadVectorNormal(ILuaInterface lua) {
		bf_read buf = (bf_read)LC_bf_read.Get(1)!;
		buf.ReadBitVec3Normal(out Vector3 vec);
		LuaVector.Push_Vector(vec);
		return 1;
	}

	[LuaMethod]
	static int bf_read__Reset(ILuaInterface lua) {
		bf_read buf = (bf_read)LC_bf_read.Get(1)!;
		buf.Seek(0);
		return 0;
	}
}
