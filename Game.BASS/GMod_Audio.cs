using ManagedBass;

using Microsoft.Extensions.DependencyInjection;

using Source.Common.Audio;
using Source.Common.Filesystem;
using Source.Common.GarrysMod;

using System.Numerics;
using System.Runtime.InteropServices;

namespace Game.BASS;

public class GMod_Audio : IGMod_Audio
{
	public static readonly GMod_Audio g_GModAudio = new();

	internal static IFileSystem? filesystem;

	static readonly string[] ErrorStrings = [
		"BASS_OK", "BASS_ERROR_MEM", "BASS_ERROR_FILEOPEN", "BASS_ERROR_DRIVER", "BASS_ERROR_BUFLOST", "BASS_ERROR_HANDLE", "BASS_ERROR_FORMAT", "BASS_ERROR_POSITION",
		"BASS_ERROR_INIT", "BASS_ERROR_START", "BASS_ERROR_SSL", "BASS_ERROR_", "BASS_ERROR_", "BASS_ERROR_", "BASS_ERROR_ALREADY", "BASS_ERROR_NOTAUDIO",
		"BASS_ERROR_", "BASS_ERROR_", "BASS_ERROR_NOCHAN", "BASS_ERROR_ILLTYPE", "BASS_ERROR_ILLPARAM", "BASS_ERROR_NO3D", "BASS_ERROR_NOEAX", "BASS_ERROR_DEVICE",
		"BASS_ERROR_NOPLAY", "BASS_ERROR_FREQ", "BASS_ERROR_", "BASS_ERROR_NOTFILE", "BASS_ERROR_", "BASS_ERROR_NOHW", "BASS_ERROR_", "BASS_ERROR_EMPTY",
		"BASS_ERROR_NONET", "BASS_ERROR_CREATE", "BASS_ERROR_NOFX", "BASS_ERROR_", "BASS_ERROR_", "BASS_ERROR_NOTAVAIL", "BASS_ERROR_DECODE", "BASS_ERROR_DX",
		"BASS_ERROR_TIMEOUT", "BASS_ERROR_FILEFORM", "BASS_ERROR_SPEAKER", "BASS_ERROR_VERSION", "BASS_ERROR_CODEC", "BASS_ERROR_ENDED", "BASS_ERROR_BUSY", "BASS_ERROR_UNSTREAMABLE",
	];

	internal static string BassErrorDescription(Errors error) => (int)error switch {
		0 => "success",
		1 => "out of memory",
		2 => "can't open file",
		3 => "can't find free/valid driver",
		4 => "sample buffer lost",
		5 => "invalid handle",
		6 => "unsupported sample format",
		7 => "invalid position",
		8 => "BASS_Init has not been successfully called",
		9 => "BASS_Start has not been successfully called",
		10 => "SSL/HTTPS support not available",
		14 => "already initialized/paused/whatever",
		17 => "not audio",
		18 => "no free channels available",
		19 => "illegal type specified",
		20 => "illegal parameter specified",
		21 => "no 3D support",
		22 => "no EAX support",
		23 => "illegal device number",
		24 => "not playing",
		25 => "illegal sample rate",
		27 => "invalid file stream",
		29 => "no hardware voices available",
		31 => "MOD music has no sequence data",
		32 => "no internet connection could be opened",
		33 => "couldn't create file",
		34 => "effects are not available",
		37 => "requested data is not available",
		38 => "this is not a decoding channel",
		39 => "sufficient DirectX version not installed",
		40 => "connection timed out",
		41 => "unsupported file format",
		42 => "unavailable speaker",
		43 => "invalid BASS version",
		44 => "codec not available/supported",
		45 => "channel/file has already ended",
		46 => "device is busy",
		47 => "unstreamable",
		_ => "unknown error",
	};

	internal static Vector3D ToBass(in Vector3 vec) => new(vec.X, -vec.Y, vec.Z);
	internal static Vector3 FromBass(Vector3D vec) => new(vec.X, -vec.Y, vec.Z);

	static readonly (int Device, DeviceInitFlags Flags)[] InitAttempts = [
		(-1, DeviceInitFlags.Device3D),
		(1, DeviceInitFlags.Device3D),
		(-1, DeviceInitFlags.Device3D | DeviceInitFlags.Mono),
		(1, DeviceInitFlags.Device3D | DeviceInitFlags.Mono),
		(-1, 0),
		(1, 0)
	];

	public bool Init(IServiceProvider services) {
		filesystem = services.GetService<IFileSystem>();

		Shutdown();

		Bass.Configure(Configuration.IncludeDefaultDevice, 1);
		bool initialized = false;
		for (int i = 0; i < InitAttempts.Length; i++) {
			if (Bass.Init(InitAttempts[i].Device, 44100, InitAttempts[i].Flags, 0, 0)) {
				initialized = true;
				break;
			}
			Warning($"BASS_Init failed ({GetErrorString((int)Bass.LastError)})! Attempt {i + 1}.\n");
		}

		if (!initialized && !Bass.Init(0, 44100, 0, 0, 0) && Bass.LastError != Errors.Already)
			Error($"Couldn't Init Bass ({GetErrorString((int)Bass.LastError)})!");

		Bass.Configure((Configuration)60, 1);
		Bass.Configure(Configuration.NetPlaylist, 1);
		Bass.Configure(Configuration.FileVerificationBytes, 0x100000);
		Bass.Set3DFactors(29.527559f, 7.0f, 0.02f);
		return true;
	}

	public void Shutdown() {
		Bass.Free();
		Bass.PluginFree(0);
	}

	public void Update(uint time) => Bass.Update((int)time);

	public IBassAudioStream? CreateAudioStream(IAudioStreamEvent? eventHandler) {
		BassAudioStream stream = new();
		if (stream.Init(eventHandler))
			return stream;
		stream.Dispose();
		return null;
	}

	public void SetEar(ref Vector3 position, ref Vector3 velocity, ref Vector3 forward, ref Vector3 up) {
		Bass.Set3DPosition(ToBass(position), ToBass(velocity), ToBass(forward), ToBass(up));
		Bass.Set3DFactors(29.527559f, 7.0f, 0.02f);
		Bass.Apply3D();
	}

	internal static BassFlags ParseFlags(string flags) {
		BassFlags ret = 0;
		if (flags.Contains("3d"))
			ret = BassFlags.Bass3D | BassFlags.Mono;
		if (flags.Contains("mono"))
			ret |= BassFlags.Mono;
		if (!flags.Contains("noblock"))
			ret |= BassFlags.StreamDownloadBlocks;
		return ret;
	}

	internal static void StartChannel(int handle, string flags) {
		if (!flags.Contains("noplay"))
			Bass.ChannelPlay(handle, false);

		if (flags.Contains("3d")) {
			Bass.ChannelSet3DAttributes(handle, Mode3D.Normal, 200.0f, 0.0f, 360, 360, 0.0f);
			Vector3D zero = new();
			Bass.ChannelSet3DPosition(handle, zero, zero, zero);
			Bass.Apply3D();
		}
	}

	public IGModAudioChannel? PlayURL(ReadOnlySpan<char> url, ReadOnlySpan<char> flags, Span<int> error) {
		string flagStr = flags.ToString().ToLowerInvariant();

		int handle = Bass.CreateStream(url.ToString(), 0, ParseFlags(flagStr), null, 0);
		if (handle == 0) {
			Errors code = Bass.LastError;
			if (error.IsEmpty)
				Warning($"BASS_StreamCreateURL failed ({BassErrorDescription(code)})\n");
			else
				error[0] = (int)code;
			return null;
		}

		StartChannel(handle, flagStr);
		return new GModAudioChannel(handle, false, 0, null);
	}

	static readonly FileProcedures FileProcs = new() {
		Close = FileClose,
		Length = FileLength,
		Read = FileRead,
		Seek = FileSeek,
	};

	static void FileClose(nint user) {
		if (filesystem != null && user != 0) {
			GCHandle handle = GCHandle.FromIntPtr(user);
			((IFileHandle)handle.Target!).Dispose();
			handle.Free();
		}
	}

	static long FileLength(nint user) {
		if (filesystem == null || user == 0)
			return 0;
		return ((IFileHandle)GCHandle.FromIntPtr(user).Target!).Stream.Length;
	}

	static unsafe int FileRead(nint buffer, int length, nint user) {
		if (filesystem != null && user != 0) {
			Stream stream = ((IFileHandle)GCHandle.FromIntPtr(user).Target!).Stream;
			if (stream.Position < stream.Length)
				return stream.Read(new Span<byte>((void*)buffer, length));
		}
		return -1;
	}

	static bool FileSeek(long offset, nint user) {
		if (filesystem == null || user == 0)
			return false;
		Stream stream = ((IFileHandle)GCHandle.FromIntPtr(user).Target!).Stream;
		stream.Seek(offset, SeekOrigin.Begin);
		return stream.Position == offset;
	}

	public IGModAudioChannel? PlayFile(ReadOnlySpan<char> path, ReadOnlySpan<char> flags, Span<int> error) {
		string flagStr = flags.ToString().ToLowerInvariant();
		string pathStr = path.ToString();

		if (pathStr.StartsWith(' ') || pathStr.StartsWith('/') || pathStr.Contains("..") || pathStr.Contains("./") || pathStr.Contains(':')) {
			if (!error.IsEmpty)
				error[0] = 2;
			return null;
		}

		IFileHandle? file = filesystem!.Open(pathStr, FileOpenOptions.Read | FileOpenOptions.Binary, "GAME");
		if (file == null) {
			if (!error.IsEmpty)
				error[0] = 2;
			return null;
		}

		BassFlags bassFlags = ParseFlags(flagStr);
		GCHandle fileHandle = GCHandle.Alloc(file);
		int handle = Bass.CreateStream(StreamSystem.NoBuffer, bassFlags, FileProcs, GCHandle.ToIntPtr(fileHandle));
		int type = 0;
		if (handle == 0) {
			if (Bass.LastError != Errors.FileFormat) {
				if (!error.IsEmpty)
					error[0] = (int)Bass.LastError;
				return null;
			}

			using IFileHandle? musicFile = filesystem.Open(pathStr, FileOpenOptions.Read | FileOpenOptions.Binary, "GAME");
			int size = (int)musicFile!.Stream.Length;
			byte[] data = new byte[size];
			int read = musicFile.Stream.Read(data, 0, size);
			type = 1;
			handle = Bass.MusicLoad(data, 0, read, (bassFlags & ~BassFlags.StreamDownloadBlocks) | BassFlags.Prescan, 0);
			if (handle == 0) {
				if (!error.IsEmpty)
					error[0] = (int)Bass.LastError;
				return null;
			}
		}

		StartChannel(handle, flagStr);
		return new GModAudioChannel(handle, true, type, pathStr);
	}

	public void SetGlobalVolume(float volume) {
		int value = (int)(volume * 10000.0f);
		Bass.Configure(Configuration.GlobalMusicVolume, value);
		Bass.Configure(Configuration.GlobalSampleVolume, value);
		Bass.Configure(Configuration.GlobalStreamVolume, value);
	}

	public void StopAllPlayback() {
		Bass.Stop();
		Bass.Start();
	}

	public ReadOnlySpan<char> GetErrorString(int error) {
		if (error < 0 || error >= 48)
			return "BASS_ERROR_UNKNOWN";
		return ErrorStrings[error];
	}
}
