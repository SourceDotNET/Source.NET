using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;

using System.Numerics;
using System.Runtime.InteropServices;

namespace Game.Client.GarrysMod;

public static partial class LuaAudioChannel
{
	[LuaClass(typeof(IGModAudioChannel), NullError = "Tried to use a NULL IGModAudioChannel!")]
	public static readonly LuaClass LC_IGModAudioChannel = new("IGModAudioChannel", LuaType.SoundHandle, null, null);

	public static void Push_IGModAudioChannel(IGModAudioChannel channel) => LC_IGModAudioChannel.Push(channel);

	static IGModAudioChannel? Get_IGModAudioChannel(ILuaInterface lua) {
		IGModAudioChannel? channel = (IGModAudioChannel?)LC_IGModAudioChannel.Get(1);
		if (channel == null)
			lua.Error("Tried to use a NULL IGModAudioChannel!");
		return channel;
	}

	[LuaMethod]
	static int IGModAudioChannel__EnableLooping(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		bool loop = lua.GetBool(2);
		if (loop && channel.IsBlockStreamed()) {
			lua.Error("Cannot use EnableLooping on a block streamed audio channel. Use the 'noblock' flag when creating the channel to disable block streaming.");
			return 0;
		}
		channel.SetLooping(loop);
		return 0;
	}

	[LuaMethod]
	static int IGModAudioChannel__FFT(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;

		LuaObject table = new();
		table.SetFromStack(2);
		if (!table.isTable()) {
			lua.TypeError("table", 2);
			table.UnReference();
			return 0;
		}

		int size = Math.Clamp((int)lua.CheckNumberOpt(3, 2), 0, 7);
		float[] data = new float[16384];
		int count = channel.FFT(data, (GModChannelFFT)size);
		for (int i = 0; i < count; i++)
			table.SetMember((float)(i + 1), data[i]);
		lua.PushNumber(count);
		table.UnReference();
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__Get3DCone(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		channel.Get3DCone(out int innerAngle, out int outerAngle, out float outerVolume);
		lua.PushNumber(innerAngle);
		lua.PushNumber(outerAngle);
		lua.PushNumber(outerVolume);
		return 3;
	}

	[LuaMethod]
	static int IGModAudioChannel__Get3DEnabled(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		lua.PushBool(channel.Get3DEnabled());
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__Get3DFadeDistance(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		channel.Get3DFadeDistance(out float min, out float max);
		lua.PushNumber(min);
		lua.PushNumber(max);
		return 2;
	}

	[LuaMethod]
	static int IGModAudioChannel__GetAverageBitRate(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		lua.PushNumber(channel.GetAverageBitRate());
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__GetBitsPerSample(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		lua.PushNumber(channel.GetBitsPerSample());
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__GetBufferedTime(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		lua.PushNumber(channel.GetBufferedTime());
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__GetFileName(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		lua.PushString(channel.GetFileName());
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__GetLength(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		lua.PushNumber(channel.GetLength());
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__GetLevel(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		channel.GetLevel(out float left, out float right);
		lua.PushNumber(left);
		lua.PushNumber(right);
		return 2;
	}

	[LuaMethod]
	static int IGModAudioChannel__GetPan(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		lua.PushNumber(channel.GetChannelPan());
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__GetPlaybackRate(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		lua.PushNumber(channel.GetPlaybackRate());
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__GetPos(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		channel.GetPos(out Vector3 position, out Vector3 direction, out _);
		LuaVector.Push_Vector(position);
		LuaVector.Push_Vector(direction);
		return 2;
	}

	[LuaMethod]
	static int IGModAudioChannel__GetSamplingRate(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		lua.PushNumber(channel.GetSamplingRate());
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__GetState(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		lua.PushNumber(channel.GetState());
		return 1;
	}

	static unsafe int PushTagList(ILuaInterface lua, int type) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;

		byte* tags = (byte*)channel.GetTags(type);
		if (tags == null)
			return 0;

		LuaTable table = new();
		int index = 1;
		while (*tags != 0) {
			ReadOnlySpan<byte> tag = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(tags);
			lua.PushString(tag);
			LuaObject value = new(-1, LuaType.None);
			table.SetMember((float)index++, value);
			value.UnReference();
			lua.Pop(1);
			tags += tag.Length + 1;
		}
		table.Push();
		table.UnReference();
		return 1;
	}

	static unsafe int PushTagString(ILuaInterface lua, int type) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;

		byte* tags = (byte*)channel.GetTags(type);
		if (tags == null)
			return 0;

		lua.PushString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(tags));
		return 1;
	}

	[LuaMethod] static int IGModAudioChannel__GetTagsHTTP(ILuaInterface lua) => PushTagList(lua, 3);
	[LuaMethod] static int IGModAudioChannel__GetTagsMP4(ILuaInterface lua) => PushTagList(lua, 7);
	[LuaMethod] static int IGModAudioChannel__GetTagsOGG(ILuaInterface lua) => PushTagList(lua, 2);
	[LuaMethod] static int IGModAudioChannel__GetTagsWMA(ILuaInterface lua) => PushTagList(lua, 8);
	[LuaMethod] static int IGModAudioChannel__GetTagsMeta(ILuaInterface lua) => PushTagString(lua, 5);
	[LuaMethod] static int IGModAudioChannel__GetTagsVendor(ILuaInterface lua) => PushTagString(lua, 9);

	static unsafe string ID3Field(byte* src, int size) {
		int length = 0;
		while (length < size - 1 && src[length] != 0)
			length++;
		return System.Text.Encoding.Latin1.GetString(src, length);
	}

	[LuaMethod]
	static unsafe int IGModAudioChannel__GetTagsID3(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;

		byte* tag = (byte*)channel.GetTags(0);
		if (tag == null)
			return 0;

		LuaTable table = new();
		table.SetMember("id", ID3Field(tag, 4));
		table.SetMember("title", ID3Field(tag + 3, 31));
		table.SetMember("artist", ID3Field(tag + 33, 31));
		table.SetMember("album", ID3Field(tag + 63, 31));
		table.SetMember("year", ID3Field(tag + 93, 5));
		table.SetMember("comment", ID3Field(tag + 97, 31));
		table.SetMember("genre", (float)tag[127]);
		table.Push();
		table.UnReference();
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__GetTime(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		lua.PushNumber(channel.GetTime());
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__GetVolume(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		lua.PushNumber(channel.GetVolume());
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__Is3D(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		lua.PushBool(channel.Is3D());
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__IsBlockStreamed(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		lua.PushBool(channel.IsBlockStreamed());
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__IsLooping(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		lua.PushBool(channel.IsLooping());
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__IsOnline(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		lua.PushBool(channel.IsOnline());
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__IsValid(ILuaInterface lua) {
		IGModAudioChannel? channel = (IGModAudioChannel?)LC_IGModAudioChannel.Get(1);
		lua.PushBool(channel != null && channel.IsValid());
		return 1;
	}

	[LuaMethod]
	static int IGModAudioChannel__Pause(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel != null && channel.IsValid())
			channel.Pause();
		return 0;
	}

	[LuaMethod]
	static int IGModAudioChannel__Play(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel != null && channel.IsValid())
			channel.Play();
		return 0;
	}

	[LuaMethod]
	static int IGModAudioChannel__Set3DCone(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		float outerVolume = (float)lua.CheckNumber(4);
		int outerAngle = (int)lua.CheckNumber(3);
		int innerAngle = (int)lua.CheckNumber(2);
		channel.Set3DCone(innerAngle, outerAngle, outerVolume);
		return 0;
	}

	[LuaMethod]
	static int IGModAudioChannel__Set3DEnabled(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel != null && channel.IsValid())
			channel.Set3DEnabled(lua.GetBool(2));
		return 0;
	}

	[LuaMethod]
	static int IGModAudioChannel__Set3DFadeDistance(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		float max = (float)lua.CheckNumber(3);
		float min = (float)lua.CheckNumber(2);
		channel.Set3DFadeDistance(min, max);
		return 0;
	}

	[LuaMethod]
	static int IGModAudioChannel__SetPan(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel != null && channel.IsValid())
			channel.SetChannelPan((float)lua.CheckNumber(2));
		return 0;
	}

	[LuaMethod]
	static int IGModAudioChannel__SetPlaybackRate(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel != null && channel.IsValid())
			channel.SetPlaybackRate((float)lua.CheckNumber(2));
		return 0;
	}

	[LuaMethod]
	static int IGModAudioChannel__SetPos(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		Vector3 position = LuaVector.Get_Vector(2);
		Vector3 direction = default;
		if (lua.GetType(3) == LuaType.Vector)
			direction = LuaVector.Get_Vector(3);
		channel.SetPos(position, direction, default);
		return 0;
	}

	[LuaMethod]
	static int IGModAudioChannel__SetTime(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		if (channel.IsBlockStreamed()) {
			lua.Error("Cannot use SetTime on a block streamed audio channel. Use the 'noblock' flag when creating the channel to disable block streaming.");
			return 0;
		}
		bool dontDecode = lua.GetType(3) != LuaType.Nil && lua.GetBool(3);
		channel.SetTime((float)lua.CheckNumber(2), dontDecode);
		return 0;
	}

	[LuaMethod]
	static int IGModAudioChannel__SetVolume(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel != null && channel.IsValid())
			channel.SetVolume((float)lua.CheckNumber(2));
		return 0;
	}

	[LuaMethod]
	static int IGModAudioChannel__Stop(ILuaInterface lua) {
		IGModAudioChannel? channel = Get_IGModAudioChannel(lua);
		if (channel == null || !channel.IsValid())
			return 0;
		channel.Stop();
		channel.Destroy();
		lua.SetUserType(1, 0);
		return 0;
	}

	[LuaMethod]
	static int IGModAudioChannel____tostring(ILuaInterface lua) {
		IGModAudioChannel? channel = (IGModAudioChannel?)LC_IGModAudioChannel.Get(1);
		if (channel != null && channel.IsValid()) {
			lua.PushString($"IGModAudioChannel [{channel.GetFileName()}]");
			return 1;
		}
		lua.PushString("IGModAudioChannel [NULL]");
		return 1;
	}
}
