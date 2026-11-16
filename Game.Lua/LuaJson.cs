using Source.Common;
using Source.Common.GarrysMod.Lua;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

using static Game.Lua.LuaApi;

namespace Game.Lua;

// note: implementation of https://github.com/Facepunch/garrysmod-requests/issues/2822
internal static unsafe class LuaJson
{
	enum Walk : byte
	{
		Start,
		ArrayNext,
		ArrayIndex,
		ObjectNext
	}

	struct Frame
	{
		public nint Table;
		public int Index;
		public int Position;
		public int Total;
		public int Open;
		public int Count;
		public Walk Mode;
		public bool InArray;
		public bool Named;
		public bool HasMetaTable;
	}

	const int MAX_DEPTH = 3001;
	const int VISITED_THRESHOLD = 64;
	const string CyclicReference = "attempt to serialize structure with cyclic reference";
	const string DepthTooHigh = "JSON Depth is too high! > 3000";

	public static bool TableToJSON(LuaInterfaceImpl lua, nint L, int index, bool pretty, [NotNullWhen(false)] out string? error) {
		int top = lua_gettop(L);
		RapidJson.Writer writer = new(pretty);
		try {
			lua_pushvalue(L, index);
			error = WalkTable(lua, L, top + 1, ref writer);
			lua_settop(L, top);
			if (error != null)
				return false;

			fixed (byte* json = writer.WrittenSpan)
				lua_pushlstring(L, json, (nuint)writer.Length);
			return true;
		}
		finally {
			writer.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static bool Next(LuaInterfaceImpl lua, nint L, int index, bool protect) => protect ? lua.Next(index) : lua_next_fast(L, index) != 0;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static bool IsIndex(nint L, int index, int expected) => lua_type(L, index) == TNUMBER && lua_tonumber(L, index) == expected;

	static bool HasMetaTable(nint L, int index) {
		if (lua_getmetatable(L, index) == 0)
			return false;

		lua_settop(L, -2);
		return true;
	}

	static bool IsVisited(ReadOnlySpan<Frame> frames, int top, HashSet<nint>? visited, nint table) {
		if (visited != null)
			return visited.Contains(table);

		for (int i = 0; i <= top; i++) {
			if (frames[i].Table == table)
				return true;
		}

		return false;
	}

	static int FormatNumberKey(double key, Span<byte> target) {
		if (key >= int.MinValue && key <= int.MaxValue && key == (int)key) {
			((int)key).TryFormat(target, out int written, default, CultureInfo.InvariantCulture);
			return written;
		}

		if (double.IsInfinity(key))
			return Encoding.ASCII.GetBytes(key > 0 ? "Infinity" : "-inf", target);

		return Encoding.ASCII.GetBytes(CFormatting.FormatG(key, 14), target);
	}

	static float* GetVectorOrAngle(nint L, int index, out bool angle) {
		angle = false;
		LuaInterfaceImpl.UserData* ud = (LuaInterfaceImpl.UserData*)lua_touserdata(L, index);
		if (ud == null || ud->Data == 0)
			return null;

		if (ud->Type == (byte)LuaType.Angle)
			angle = true;
		else if (ud->Type != (byte)LuaType.Vector)
			return null;

		return (float*)ud->Data;
	}

	static bool HasNamedChild(LuaInterfaceImpl lua, nint L, int table, bool protect) {
		int top = lua_gettop(L);
		lua_pushnil(L);
		while (Next(lua, L, table, protect)) {
			bool named;
			switch (lua_type(L, top + 1)) {
				case TSTRING:
					nuint length;
					lua_tolstring_fast(L, top + 1, &length);
					named = length != 0;
					break;
				case TNUMBER:
				case TBOOLEAN:
					named = true;
					break;
				default:
					named = false;
					break;
			}

			if (named) {
				switch (lua_type(L, top + 2)) {
					case TBOOLEAN:
					case TNUMBER:
					case TSTRING:
					case TTABLE:
						break;
					case TUSERDATA:
						named = GetVectorOrAngle(L, top + 2, out _) != null;
						break;
					default:
						named = false;
						break;
				}
			}

			if (named) {
				lua_settop(L, top);
				return true;
			}

			lua_settop(L, top + 1);
		}

		return false;
	}

	static void Decide(LuaInterfaceImpl lua, nint L, ref Frame frame, ref bool protect) {
		int count = 0;
		lua_pushnil(L);
		while (Next(lua, L, frame.Index, protect)) {
			count++;
			lua_settop(L, frame.Index + 1);
		}

		int run = 0;
		while (true) {
			if (frame.HasMetaTable) {
				protect = true;
				lua.PushLong(run + 1);
				lua.GetTable(frame.Index);
			}
			else
				lua_rawgeti(L, frame.Index, run + 1);

			bool nil = lua_type(L, frame.Index + 1) == TNIL;
			lua_settop(L, frame.Index);
			if (nil || ++run > count)
				break;
		}

		if (run == count) {
			frame.Mode = Walk.ArrayIndex;
			frame.Position = 0;
			frame.Total = count;
			frame.InArray = true;
		}
		else {
			frame.Mode = Walk.ObjectNext;
			frame.InArray = false;
			lua_pushnil(L);
		}
	}

	static void Restart(LuaInterfaceImpl lua, nint L, ref Frame frame, ref RapidJson.Writer writer, ref bool protect) {
		bool numbered = lua_type(L, frame.Index + 1) == TNUMBER;
		lua_settop(L, frame.Index);
		writer.Length = frame.Open + 1;
		frame.Count = 0;
		if (numbered)
			Decide(lua, L, ref frame, ref protect);
		else {
			frame.Mode = Walk.ObjectNext;
			frame.InArray = false;
			lua_pushnil(L);
		}
	}

	static string? WalkTable(LuaInterfaceImpl lua, nint L, int root, ref RapidJson.Writer writer) {
		Span<Frame> frames = stackalloc Frame[32];
		Span<byte> scratch = stackalloc byte[192];
		HashSet<nint>? visited = null;
		bool protect = false;

		int top = 0;
		frames[0] = new() { Table = lua_topointer(L, root), Index = root, HasMetaTable = HasMetaTable(L, root) };
		writer.Put((byte)'[');

		while (top >= 0) {
			ref Frame frame = ref frames[top];
			int level = top + 1;
			bool pair = false;

			if (frame.Mode == Walk.Start) {
				if (frame.HasMetaTable) {
					Decide(lua, L, ref frame, ref protect);
					continue;
				}

				lua_pushnil(L);
				if (!Next(lua, L, frame.Index, protect))
					frame.Mode = Walk.ArrayIndex;
				else if (IsIndex(L, frame.Index + 1, 1)) {
					frame.Mode = Walk.ArrayNext;
					frame.Position = 1;
					frame.InArray = true;
					pair = true;
				}
				else {
					lua_rawgeti(L, frame.Index, 1);
					if (lua_type(L, frame.Index + 3) != TNIL) {
						lua_settop(L, frame.Index);
						Decide(lua, L, ref frame, ref protect);
						continue;
					}

					lua_settop(L, frame.Index + 2);
					frame.Mode = Walk.ObjectNext;
					pair = true;
				}
			}

			int value;
			switch (frame.Mode) {
				case Walk.ArrayNext:
					if (!pair) {
						if (!Next(lua, L, frame.Index, protect)) {
							value = 0;
							break;
						}

						if (!IsIndex(L, frame.Index + 1, frame.Position + 1)) {
							Restart(lua, L, ref frame, ref writer, ref protect);
							continue;
						}

						frame.Position++;
					}

					value = frame.Index + 2;
					break;
				case Walk.ArrayIndex:
					if (frame.Position == frame.Total) {
						value = 0;
						break;
					}

					lua_rawgeti(L, frame.Index, ++frame.Position);
					value = frame.Index + 1;
					break;
				default:
					value = pair || Next(lua, L, frame.Index, protect) ? frame.Index + 2 : 0;
					break;
			}

			if (value == 0) {
				bool array = frame.InArray || frame.Count == 0;
				if (!array)
					writer[frame.Open] = (byte)'{';
				writer.End(array ? (byte)']' : (byte)'}', frame.Count, top);
				visited?.Remove(frame.Table);
				if (top > 0)
					lua_settop(L, frame.Index - 1);
				top--;
				continue;
			}

			int type = lua_type(L, value);
			if (type == TTABLE && frame.Mode == Walk.ArrayNext) {
				int count = frame.Position;
				bool sequential = true;
				lua_settop(L, frame.Index + 1);
				while (Next(lua, L, frame.Index, protect)) {
					if (!IsIndex(L, frame.Index + 1, count + 1)) {
						sequential = false;
						break;
					}

					count++;
					lua_settop(L, frame.Index + 1);
				}

				if (!sequential) {
					Restart(lua, L, ref frame, ref writer, ref protect);
					continue;
				}

				lua_settop(L, frame.Index);
				frame.Mode = Walk.ArrayIndex;
				frame.Total = count;
				frame.Position--;
				continue;
			}

			scoped ReadOnlySpan<byte> name = default;
			if (frame.Mode == Walk.ObjectNext) {
				int key = frame.Index + 1;
				switch (lua_type(L, key)) {
					case TSTRING:
						nuint length;
						byte* text = lua_tolstring_fast(L, key, &length);
						name = new(text, (int)length);
						break;
					case TNUMBER:
						name = scratch[..FormatNumberKey(lua_tonumber(L, key), scratch)];
						break;
					case TBOOLEAN:
						name = lua_toboolean(L, key) != 0 ? "true"u8 : "false"u8;
						break;
					default:
						lua_settop(L, key);
						continue;
				}
			}

			nint child = 0;
			float* vector = null;
			bool angle = false;
			switch (type) {
				case TBOOLEAN:
				case TNUMBER:
				case TSTRING:
					break;
				case TTABLE:
					if (level >= MAX_DEPTH)
						return DepthTooHigh;

					child = lua_topointer(L, value);
					if (IsVisited(frames, top, visited, child))
						return CyclicReference;
					break;
				case TUSERDATA:
					vector = GetVectorOrAngle(L, value, out angle);
					if (vector == null) {
						lua_settop(L, value - 1);
						continue;
					}

					break;
				default:
					lua_settop(L, value - 1);
					continue;
			}

			if (frame.Mode == Walk.ObjectNext) {
				if (!name.IsEmpty)
					frame.Named = true;
				else if (frame.Named || HasNamedChild(lua, L, frame.Index, protect)) {
					frame.Named = true;
					name = "_1_"u8;
				}
				else
					frame.InArray = true;

				if (!frame.InArray) {
					writer.Prefix(ref frame.Count, false, level);
					writer.String(name);
				}
			}

			writer.Prefix(ref frame.Count, frame.InArray, level);
			switch (type) {
				case TBOOLEAN:
					writer.Bool(lua_toboolean(L, value) != 0);
					break;
				case TNUMBER:
					double number = lua_tonumber(L, value);
					if (number >= int.MinValue && number <= int.MaxValue && number == (int)number)
						writer.Int((int)number);
					else {
						number = Bootil.String.Format.NiceDouble(number);
						if (double.IsInfinity(number))
							writer.Null();
						else
							writer.Double(number);
					}

					break;
				case TSTRING:
					nuint size;
					byte* bytes = lua_tolstring_fast(L, value, &size);
					writer.String(new ReadOnlySpan<byte>(bytes, (int)size));
					break;
				case TTABLE:
					if (level == frames.Length) {
						Span<Frame> larger = new Frame[frames.Length * 4];
						frames.CopyTo(larger);
						frames = larger;
					}

					if (visited == null && level >= VISITED_THRESHOLD) {
						visited = [];
						for (int i = 0; i <= top; i++)
							visited.Add(frames[i].Table);
					}

					visited?.Add(child);
					top++;
					frames[top] = new() { Table = child, Index = value, Open = writer.Length, HasMetaTable = HasMetaTable(L, value) };
					writer.Put((byte)'[');
					continue;
				default:
					int written = 0;
					scratch[written++] = angle ? (byte)'{' : (byte)'[';
					written += Bootil.String.Format.NiceFloat(vector[0], scratch[written..]);
					scratch[written++] = (byte)' ';
					written += Bootil.String.Format.NiceFloat(vector[1], scratch[written..]);
					scratch[written++] = (byte)' ';
					written += Bootil.String.Format.NiceFloat(vector[2], scratch[written..]);
					scratch[written++] = angle ? (byte)'}' : (byte)']';
					writer.String(scratch[..written]);
					break;
			}

			lua_settop(L, value - 1);
		}

		return null;
	}
}
