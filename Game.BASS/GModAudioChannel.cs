using ManagedBass;

using Source.Common.GarrysMod;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Game.BASS;

public class GModAudioChannel(int handle, bool isFile, int type, string? fileName) : IGModAudioChannel
{
	int Handle = handle;
	readonly bool IsFile = isFile;
	readonly int Type = type;
	readonly string? FileName = fileName;

	public void Destroy() {
		if (Bass.ChannelIsActive(Handle) == PlaybackState.Playing && !IsLooping() && Bass.ChannelGetLength(Handle, PositionFlags.Bytes) >= 0) {
			Bass.ChannelFlags(Handle, BassFlags.AutoFree, BassFlags.AutoFree);
			Handle = 0;
			return;
		}

		if (Type == 0)
			Bass.StreamFree(Handle);
		else if (Type == 1)
			Bass.MusicFree(Handle);

		Handle = 0;
	}

	public void Stop() => Bass.ChannelStop(Handle);
	public void Pause() => Bass.ChannelPause(Handle);
	public void Play() => Bass.ChannelPlay(Handle, false);

	public void SetVolume(float volume) => Bass.ChannelSetAttribute(Handle, ChannelAttribute.Volume, volume);

	public float GetVolume() {
		Bass.ChannelGetAttribute(Handle, ChannelAttribute.Volume, out float volume);
		return volume;
	}

	public void SetPlaybackRate(float rate) => Bass.ChannelSetAttribute(Handle, ChannelAttribute.Frequency, rate * GetSamplingRate());

	public float GetPlaybackRate() {
		Bass.ChannelGetAttribute(Handle, ChannelAttribute.Frequency, out float frequency);
		if (frequency <= 0.0f)
			return 1.0f;
		return frequency / GetSamplingRate();
	}

	public void SetPos(in Vector3 position, in Vector3 direction, in Vector3 velocity) {
		Bass.ChannelSet3DPosition(Handle, GMod_Audio.ToBass(position), GMod_Audio.ToBass(direction), GMod_Audio.ToBass(velocity));
		Bass.Apply3D();
	}

	public void GetPos(out Vector3 position, out Vector3 direction, out Vector3 velocity) {
		Vector3D pos = new(), dir = new(), vel = new();
		Bass.ChannelGet3DPosition(Handle, ref pos, ref dir, ref vel);
		position = GMod_Audio.FromBass(pos);
		direction = GMod_Audio.FromBass(dir);
		velocity = GMod_Audio.FromBass(vel);
	}

	public void SetTime(TimeUnit_t time, bool dontDecode) {
		long target = Bass.ChannelSeconds2Bytes(Handle, time);
		long current = Bass.ChannelGetPosition(Handle, PositionFlags.Bytes);
		PositionFlags flags = PositionFlags.Bytes;
		if (current < target && !dontDecode)
			flags = PositionFlags.DecodeTo;
		Bass.ChannelSetPosition(Handle, target, flags);
	}

	public TimeUnit_t GetTime() => Bass.ChannelBytes2Seconds(Handle, Bass.ChannelGetPosition(Handle, PositionFlags.Bytes));

	public TimeUnit_t GetBufferedTime() {
		if (IsFile)
			return GetLength();

		long length = Bass.ChannelGetLength(Handle, PositionFlags.Bytes);
		long download = Bass.StreamGetFilePosition(Handle, FileStreamPosition.Download);
		long start = Bass.StreamGetFilePosition(Handle, FileStreamPosition.Start);
		long size = Bass.StreamGetFilePosition(Handle, (FileStreamPosition)8);
		double buffered = (double)(ulong)download / (double)(ulong)(size - start) * (double)(ulong)length;
		return Bass.ChannelBytes2Seconds(Handle, (long)(ulong)buffered);
	}

	public void Set3DFadeDistance(float min, float max) {
		Bass.ChannelSet3DAttributes(Handle, Mode3D.LeaveCurrent, min, max, -1, -1, -1.0f);
		Bass.Apply3D();
	}

	public void Get3DFadeDistance(out float min, out float max) {
		Mode3D mode = 0;
		int iangle = 0, oangle = 0;
		float outvol = 0;
		min = 0;
		max = 0;
		Bass.ChannelGet3DAttributes(Handle, ref mode, ref min, ref max, ref iangle, ref oangle, ref outvol);
	}

	public void Set3DCone(int innerAngle, int outerAngle, float outerVolume) {
		if (innerAngle < 0 || outerAngle < 0) {
			innerAngle = -1;
			outerAngle = -1;
		}
		Bass.ChannelSet3DAttributes(Handle, Mode3D.LeaveCurrent, -1.0f, -1.0f, innerAngle, outerAngle, outerVolume);
		Bass.Apply3D();
	}

	public void Get3DCone(out int innerAngle, out int outerAngle, out float outerVolume) {
		Mode3D mode = 0;
		float min = 0, max = 0;
		innerAngle = 0;
		outerAngle = 0;
		outerVolume = 0;
		Bass.ChannelGet3DAttributes(Handle, ref mode, ref min, ref max, ref innerAngle, ref outerAngle, ref outerVolume);
	}

	public int GetState() => Bass.ChannelIsActive(Handle) switch {
		PlaybackState.Playing => 1,
		PlaybackState.Stalled => 3,
		PlaybackState.Paused => 2,
		_ => 0,
	};

	public void SetLooping(bool looping) => Bass.ChannelFlags(Handle, looping ? BassFlags.Loop : BassFlags.Default, BassFlags.Loop);
	public bool IsLooping() => (Bass.ChannelFlags(Handle, BassFlags.Default, BassFlags.Default) & BassFlags.Loop) != 0;
	public bool IsOnline() => !IsFile;
	public bool Is3D() => (Bass.ChannelFlags(Handle, BassFlags.Default, BassFlags.Default) & BassFlags.Bass3D) != 0;
	public bool IsBlockStreamed() => (Bass.ChannelFlags(Handle, BassFlags.Default, BassFlags.Default) & BassFlags.StreamDownloadBlocks) != 0;
	public bool IsValid() => Handle != 0;

	public double GetLength() => Bass.ChannelBytes2Seconds(Handle, Bass.ChannelGetLength(Handle, PositionFlags.Bytes));

	public ReadOnlySpan<char> GetFileName() {
		if (FileName != null)
			return FileName;
		Bass.ChannelGetInfo(Handle, out ChannelInfo info);
		ChannelInfoRaw raw = Unsafe.As<ChannelInfo, ChannelInfoRaw>(ref info);
		if (raw.FileName == 0)
			return "";
		return ((raw.Flags & BassFlags.Unicode) != 0 ? Marshal.PtrToStringUni(raw.FileName) : Marshal.PtrToStringAnsi(raw.FileName)) ?? "";
	}

	[StructLayout(LayoutKind.Sequential)]
	struct ChannelInfoRaw
	{
		public int Frequency;
		public int Channels;
		public BassFlags Flags;
		public ChannelType ChannelType;
		public int OriginalResolution;
		public int Plugin;
		public int Sample;
		public nint FileName;
	}

	public int GetSamplingRate() {
		Bass.ChannelGetInfo(Handle, out ChannelInfo info);
		return info.Frequency;
	}

	public int GetBitsPerSample() {
		Bass.ChannelGetInfo(Handle, out ChannelInfo info);
		return info.OriginalResolution;
	}

	public float GetAverageBitRate() {
		Bass.ChannelGetAttribute(Handle, ChannelAttribute.Bitrate, out float bitrate);
		return bitrate;
	}

	public void GetLevel(out float left, out float right) {
		int level = Bass.ChannelIsActive(Handle) == PlaybackState.Playing ? Bass.ChannelGetLevel(Handle) : 0;
		left = ((uint)level >> 16) * 3.0517578e-05f;
		right = (level & 0xffff) * 3.0517578e-05f;
	}

	public int FFT(float[] data, GModChannelFFT size) {
		if (Bass.ChannelIsActive(Handle) != PlaybackState.Playing)
			return 0;

		(int count, DataFlags flags) = size switch {
			GModChannelFFT.FFT_256 => (128, DataFlags.FFT256),
			GModChannelFFT.FFT_512 => (256, DataFlags.FFT512),
			GModChannelFFT.FFT_2048 => (1024, DataFlags.FFT2048),
			GModChannelFFT.FFT_4096 => (2048, DataFlags.FFT4096),
			GModChannelFFT.FFT_8192 => (4096, DataFlags.FFT8192),
			GModChannelFFT.FFT_16384 => (8192, DataFlags.FFT16384),
			GModChannelFFT.FFT_32768 => (16384, DataFlags.FFT32768),
			_ => (512, DataFlags.FFT1024),
		};
		Bass.ChannelGetData(Handle, data, (int)(flags | DataFlags.Float | DataFlags.FFTRemoveDC));
		return count;
	}

	public void SetChannelPan(float pan) => Bass.ChannelSetAttribute(Handle, ChannelAttribute.Pan, pan);

	public float GetChannelPan() {
		Bass.ChannelGetAttribute(Handle, ChannelAttribute.Pan, out float pan);
		return pan;
	}

	public nint GetTags(int type) => Bass.ChannelGetTags(Handle, (TagType)type);

	public void Set3DEnabled(bool enabled) {
		if (!Is3D())
			return;
		Bass.ChannelSet3DAttributes(Handle, enabled ? Mode3D.Normal : Mode3D.Off, -1.0f, -1.0f, -1, -1, -1.0f);
		Bass.Apply3D();
	}

	public bool Get3DEnabled() {
		if (!Is3D())
			return false;
		Mode3D mode = 0;
		float min = 0, max = 0, outvol = 0;
		int iangle = 0, oangle = 0;
		Bass.ChannelGet3DAttributes(Handle, ref mode, ref min, ref max, ref iangle, ref oangle, ref outvol);
		return mode != Mode3D.Off;
	}

	public void Restart() => Bass.ChannelPlay(Handle, true);
}
