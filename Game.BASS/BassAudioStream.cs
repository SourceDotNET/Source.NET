using ManagedBass;

using Source.Common.Audio;
using Source.Common.GarrysMod;

using System.Runtime.InteropServices;

namespace Game.BASS;

public class BassAudioStream : IBassAudioStream, IDisposable
{
	int Handle;
	IAudioStreamEvent? Event;
	uint Offset;
	GCHandle Self;

	static readonly FileProcedures StreamProcs = new() {
		Close = static user => { },
		Length = static user => 0,
		Read = StreamRead,
		Seek = StreamSeek,
	};

	static BassAudioStream Get(nint user) => (BassAudioStream)GCHandle.FromIntPtr(user).Target!;

	static unsafe int StreamRead(nint buffer, int length, nint user) {
		BassAudioStream stream = Get(user);
		int read = stream.Event!.StreamRequestData(new Span<byte>((void*)buffer, length), length, (int)stream.Offset);
		stream.Offset += (uint)read;
		return read;
	}

	static bool StreamSeek(long offset, nint user) {
		if ((ulong)offset > 0x4000000)
			return false;
		Get(user).Offset = (uint)offset;
		return true;
	}

	public bool Init(IAudioStreamEvent? eventHandler) {
		Event = eventHandler;
		Self = GCHandle.Alloc(this);
		Handle = Bass.CreateStream(StreamSystem.NoBuffer, BassFlags.Decode | BassFlags.StreamDownloadBlocks | BassFlags.RestrictDownloadRate, StreamProcs, GCHandle.ToIntPtr(Self));
		if (Handle == 0) {
			Warning($"Couldn't create BASS audio stream ({GMod_Audio.BassErrorDescription(Bass.LastError)})\n");
			return false;
		}
		return true;
	}

	public void Dispose() {
		if (Handle != 0)
			Bass.StreamFree(Handle);
		if (Self.IsAllocated)
			Self.Free();
	}

	public unsafe int Decode(Span<byte> buffer) {
		if (Handle == 0)
			return 0;
		int ret;
		fixed (byte* ptr = buffer)
			ret = Bass.ChannelGetData(Handle, (nint)ptr, buffer.Length);
		return ret < 0 ? 0 : ret;
	}

	public int GetOutputBits() {
		Error("Not used");
		return 0;
	}

	public int GetOutputRate() {
		Bass.ChannelGetInfo(Handle, out ChannelInfo info);
		return info.Frequency == 0 ? 44100 : info.Frequency;
	}

	public int GetOutputChannels() {
		Bass.ChannelGetInfo(Handle, out ChannelInfo info);
		return info.Channels == 0 ? 2 : info.Channels;
	}

	public uint GetPosition() {
		if (Handle == 0)
			return 0;
		return (uint)Bass.StreamGetFilePosition(Handle, FileStreamPosition.Current);
	}

	public void SetPosition(uint distance) { }

	public uint GetHandle() => (uint)Handle;
}
