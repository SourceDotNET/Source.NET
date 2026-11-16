using CommunityToolkit.HighPerformance;

using Game.Assets;

using Microsoft.Extensions.DependencyInjection;

using Snappier;

using Source.Common;
using Source.Common.Compression;
using Source.Common.Engine;
using Source.Common.Filesystem;

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

using static Source.Common.FilesystemHelpers;

namespace Source.Engine;

/// <summary>
/// Common functionality
/// </summary>
/// <param name="providers"></param>
public class Common(IServiceProvider providers, Sys Sys)
{
	ILocalize? Localize = providers.GetService<ILocalize>();
	public static string Gamedir { get; private set; } = "";
	const uint SNAPPY_ID = ('P' << 24) | ('A' << 16) | ('N' << 8) | ('S');

	// TODO: make safe. I'm lazy right now
	static unsafe byte* CompressBuffer_Snappy(byte* source, uint sourceLen, uint* compressedLen, uint maxCompressedLen) {
		Assert(source != null);
		Assert(compressedLen != null);

		// Allocate a buffer big enough to hold the worst case.
		uint nMaxCompressedSize = GetIdealDestinationCompressionBufferSize_Snappy(sourceLen);
		byte* pCompressed = (byte*)NativeMemory.Alloc(nMaxCompressedSize);
		if (pCompressed == null)
			return null;

		// Do the compression
		*(uint*)pCompressed = SNAPPY_ID;
		int compressed_length = Snappy.Compress(new Span<byte>(source, (int)sourceLen), new(pCompressed + sizeof(uint), (int)nMaxCompressedSize - sizeof(uint)));
		compressed_length += 4;
		Assert(compressed_length <= nMaxCompressedSize);

		// Check if this result is OK
		if (maxCompressedLen != 0 && compressed_length > maxCompressedLen) {
			NativeMemory.Free(pCompressed);
			return null;
		}

		*compressedLen = (uint)compressed_length;
		return pCompressed;
	}
	public static uint GetIdealDestinationCompressionBufferSize_Snappy(uint uncompressed) => 4 + (uint)Snappy.GetMaxCompressedLength((int)uncompressed);
	static unsafe bool BufferToBufferCompress_Snappy(byte* dest, uint* destLen, byte* source, uint sourceLen) {
		Assert(dest != null);
		Assert(destLen != null);
		Assert(source != null);

		// Check if we need to use a temporary buffer
		uint nMaxCompressedSize = GetIdealDestinationCompressionBufferSize_Snappy(sourceLen);
		uint compressedLen = *destLen;
		if (compressedLen < nMaxCompressedSize) {
			// Yep.  Use the other function to allocate the buffer of the right size and comrpess into it
			byte* temp = CompressBuffer_Snappy(source, sourceLen, &compressedLen, compressedLen);
			if (temp == null)
				return false;

			// Copy over the data
			memcpy(dest, temp, compressedLen);
			*destLen = compressedLen;
			NativeMemory.Free(temp);
			return true;
		}

		// We have room and should be able to compress directly
		*(uint*)dest = SNAPPY_ID;
		int compressed_length = Snappy.Compress(new Span<byte>(source, (int)sourceLen), new(dest + sizeof(uint), (int)(*destLen - sizeof(uint)))); compressed_length += 4;
		Assert(compressed_length <= nMaxCompressedSize);
		*destLen = (uint)compressed_length;
		return true;
	}

	public static unsafe bool BufferToBufferCompress_Snappy(ref Span<byte> destinationBuffer, ReadOnlySpan<byte> sourceBuffer) {
		fixed (byte* dest = destinationBuffer)
		fixed (byte* src = sourceBuffer) {
			uint destLen = (uint)destinationBuffer.Length;
			bool result = BufferToBufferCompress_Snappy(dest, &destLen, src, (uint)sourceBuffer.Length);
			destinationBuffer = new(dest, (int)destLen);
			return result;
		}
	}

	public void InitFilesystem(ReadOnlySpan<char> fullModPath) {
		CFSSearchPathsInit initInfo = new();
		IEngineAPI engineAPI = providers.GetRequiredService<IEngineAPI>();
		Host Host = providers.GetRequiredService<Host>();
		FileSystem FileSystem = providers.GetRequiredService<FileSystem>();

		initInfo.FileSystem = engineAPI.GetRequiredService<IFileSystem>();
		initInfo.DirectoryName = fullModPath.IsEmpty ? "" : Path.GetFullPath(new string(fullModPath), AppContext.BaseDirectory);
		if (initInfo.DirectoryName == null)
			initInfo.DirectoryName = Host.GetCurrentGame();

		Host.CheckGore();

		initInfo.LowViolence = Host.LowViolence;
		initInfo.MountHDContent = false; // Study this further

		AssetLinker.CheckRequired();

		FileSystem.LoadSearchPaths(ref initInfo);
		Common.Gamedir = Path.Combine(AppContext.BaseDirectory, initInfo.ModPath ?? throw new Exception("Mod path null"));

		SetupLogDir(null);

	}

	public bool Initialized { get; private set; }
	public void Init() {
		Initialized = true;
	}

	const int COM_TOKEN_MAX_LENGTH = 1024;
	static readonly byte[] com_token = new byte[COM_TOKEN_MAX_LENGTH];

	public static ReadOnlySpan<byte> ParseFile(ReadOnlySpan<byte> data, Span<char> token) {
		ReadOnlySpan<byte> returnData = Parse(data);
		ReadOnlySpan<byte> nullTermToken = com_token.AsSpan()[..System.MemoryExtensions.IndexOf(com_token, (byte)0)];
		token.Clear(); // todo: only set one char
		Encoding.ASCII.GetChars(nullTermToken, token);

		return returnData;
	}

	static ReadOnlySpan<byte> Parse(ReadOnlySpan<byte> data) {
		byte c;
		int len;
		CharacterSet breaks;

		breaks = BreakSetIncludingColons;
		if (com_ignorecolons)
			breaks = BreakSet;

		len = 0;
		com_token[0] = 0;

		if (data.IsEmpty)
			return null;

	skipwhite:
		while ((c = data[0]) <= ' ') {
			if (c == 0)
				return null;
			data = data[1..];
			if (data.IsEmpty)
				return null;
		}

		if (c == '/' && data[1] == '/') {
			while (!data.IsEmpty && data[0] != '\0' && data[0] != '\n')
				data = data[1..];
			goto skipwhite;
		}

		if (c == '\"') {
			data = data[1..];
			while (true) {
				c = data[0];
				data = data[1..];
				if (c == '\"' || c == '\0') {
					com_token[len] = 0;
					return data;
				}
				com_token[len] = c;
				len++;
			}
		}

		if (breaks.Contains((char)c)) {
			com_token[len] = c;
			len++;
			com_token[len] = 0;
			return data[1..];
		}

		do {
			com_token[len] = c;
			data = data[1..];
			len++;
			c = data[0];
			if (breaks.Contains((char)c))
				break;
		} while (c > 32);

		com_token[len] = 0;
		return data;
	}

	const int MAX_LOG_DIRECTORIES = 10000;
	static bool logPathSetup;

	public static void SetupLogDir(ReadOnlySpan<char> mapname) {
		g_pFileSystem.RemoveSearchPaths("LOGDIR");

		if (!mapname.IsEmpty && Singleton<Source.Common.Commands.ICommandLine>().CheckParm("-uselogdir")) {
			int i;
			string relativeLogDir = "";
			for (i = 0; i < MAX_LOG_DIRECTORIES; i++) {
				relativeLogDir = $"logs/{mapname}/{i:D4}";
				if (!g_pFileSystem.IsDirectory(relativeLogDir, "GAME"))
					break;
			}

			if (i == MAX_LOG_DIRECTORIES)
				relativeLogDir = $"logs/{mapname}/{0:D4}";

			g_pFileSystem.CreateDirHierarchy(relativeLogDir, "GAME");

			if (!logPathSetup) {
				logPathSetup = true;
				g_pFileSystem.AddSearchPath($"{Gamedir}/{relativeLogDir}", "LOGDIR");
			}
		}
		else
			g_pFileSystem.AddSearchPath(Gamedir, "LOGDIR");

		g_pFileSystem.MarkPathIDByRequestOnly("LOGDIR", true);
	}

	public static void CreatePath(ReadOnlySpan<char> path) {
		Span<char> tempPath = stackalloc char[1024];
		strcpy(tempPath, path);
		StrTools.StripFilename(tempPath);
		g_pFileSystem.CreateDirHierarchy(((ReadOnlySpan<char>)tempPath).SliceNullTerminatedString(), "DEFAULT_WRITE_PATH");
	}

	public static bool CopyFile(ReadOnlySpan<char> sourcePath, ReadOnlySpan<char> destPath) {
		const int bufferSize = 4096 * 32;

		using IFileHandle? input = g_pFileSystem.Open(sourcePath, FileOpenOptions.Read | FileOpenOptions.Binary);
		AssertMsg(input != null, $"COM_CopyFile(): Input file '{sourcePath}' failed to open");
		if (input == null)
			return false;

		CreatePath(destPath);

		using IFileHandle? output = g_pFileSystem.Open(destPath, FileOpenOptions.Write | FileOpenOptions.Binary);
		AssertMsg(output != null, $"COM_CopyFile(): Output file '{destPath}' failed to open");
		if (output == null)
			return false;

		byte[] buf = new byte[bufferSize];
		long remaining = input.Stream.Length;
		while (remaining > 0) {
			int count = remaining < bufferSize ? (int)remaining : bufferSize;
			int read = input.Stream.Read(buf, 0, count);
			output.Stream.Write(buf, 0, read);
			remaining -= read;
		}

		return true;
	}

	public static bool IsValidPath(ReadOnlySpan<char> filename) {
		if (filename.IsEmpty)
			return false;

		if (filename.Length == 0
			|| filename.Contains("\\\\", StringComparison.OrdinalIgnoreCase) // To protect network paths
			|| filename.Contains(":", StringComparison.OrdinalIgnoreCase) // To protect absolute paths
			|| filename.Contains("..", StringComparison.OrdinalIgnoreCase) // To protect relative paths
			|| filename.Contains("\n", StringComparison.OrdinalIgnoreCase)
			|| filename.Contains("\r", StringComparison.OrdinalIgnoreCase)
		)
			return false;

		return true;
	}

	static readonly char[] tempDisconnectMsgBuffer = new char[512];
	public void ExplainDisconnection(bool print, ReadOnlySpan<char> disconnectReason) {
		ReadOnlySpan<char> message;
		if (print) {
			tempDisconnectMsgBuffer[0] = '\0';
			ReadOnlySpan<char> localized = g_Localize != null ? g_Localize.Find(disconnectReason) : null;
			if (!localized.IsEmpty) {
				int count = strcpy(tempDisconnectMsgBuffer.AsSpan(), localized);
				message = tempDisconnectMsgBuffer.AsSpan()[..count];
			}
			else
				message = disconnectReason;
		}
		else {
			message = disconnectReason;
		}

		if (print && !disconnectReason.IsEmpty) {
			if (disconnectReason.Length > 0 && disconnectReason[0] == '#')
				disconnectReason = Localize == null ? disconnectReason : Localize.Find(disconnectReason);

			ConMsg($"{message}\n");
		}
		Sys.DisconnectReason = new(message);
		Sys.ExtendedError = true;
	}

	internal static void TimestampedLog(ReadOnlySpan<char> msg) {
		string time = DateTime.Now.ToString("G");
		Span<char> finalMsg = stackalloc char[msg.Length + 5 + time.Length];
		finalMsg[0] = '[';
		time.CopyTo(finalMsg[1..]);
		"]: ".CopyTo(finalMsg[(1 + time.Length)..]);
		msg.CopyTo(finalMsg[(1 + time.Length + 3)..]);
		finalMsg[^1] = '\n';
		Msg(finalMsg);
	}

	public void Shutdown() {

	}

	public static ReadOnlySpan<char> FormatSeconds(double seconds) {
		int hours = 0;
		int minutes = (int)(seconds / 60);

		if (minutes > 0) {
			seconds -= minutes * 60;
			hours = minutes / 60;

			if (hours > 0)
				minutes -= hours * 60;
		}

		if (hours > 0)
			return $"{hours}:{minutes:00}:{(int)seconds:00}";
		else
			return $"{minutes}:{(int)seconds:00}";
	}

	internal static unsafe bool BufferToBufferCompress_LZSS(ref Span<byte> compressedData, ReadOnlySpan<byte> sourceBuffer) {
		CLZSSProcessor s = new();
		int compressedLen = 0;
		fixed (byte* source = sourceBuffer)
		fixed (byte* dest = compressedData)
			if (null == s.CompressNoAlloc(source, sourceBuffer.Length, dest, ref compressedLen))
				return false;
		compressedData = compressedData[..compressedLen];
		return true;
	}

	internal static int GetIdealDestinationCompressionBufferSize_LZSS(uint numBytes) {
		return (int)numBytes;
	}
}

public ref struct CLZSSProcessor
{
	unsafe struct lzss_node_t
	{
		public byte* pData;
		public lzss_node_t* pPrev;
		public lzss_node_t* pNext;
		public InlineArray4<byte> empty;
	}
	unsafe struct lzss_list_t
	{
		public lzss_node_t* pStart;
		public lzss_node_t* pEnd;
	}
	unsafe void BuildHash(byte* pData) {

	}
	unsafe lzss_list_t* m_pHashTable;
	unsafe lzss_node_t* m_pHashTarget;
	int m_nWindowSize;

	internal unsafe byte* CompressNoAlloc(byte* pInput, int inputLength, byte* pOutputBuf, ref int pOutputSize) {
		if (inputLength <= (int)(sizeof(lzss_header_t)) + 8) {
			return null;
		}

		// create the compression work buffers, small enough (~64K) for stack
		lzss_list_t* hashTable = stackalloc lzss_list_t[256];
		lzss_node_t* hashTarget = stackalloc lzss_node_t[m_nWindowSize];

		m_pHashTable = hashTable;
		m_pHashTarget = hashTarget;

		// allocate the output buffer, compressed buffer is expected to be less, caller will free
		byte* pStart = pOutputBuf;
		// prevent compression failure (inflation), leave enough to allow dribble eof bytes
		byte* pEnd = pStart + inputLength - sizeof(lzss_header_t) - 8;

		// set the header
		lzss_header_t* pHeader = (lzss_header_t*)pStart;
		pHeader->id = CLZSS.LZSS_ID;
		pHeader->actualSize = (uint)inputLength;

		byte* pOutput = pStart + sizeof(lzss_header_t);
		byte* pLookAhead = pInput;
		byte* pWindow = pInput;
		byte* pEncodedPosition = null;
		byte* pCmdByte = null;
		int putCmdByte = 0;

		while (inputLength > 0) {
			pWindow = pLookAhead - m_nWindowSize;
			if (pWindow < pInput) {
				pWindow = pInput;
			}

			if (0 == putCmdByte) {
				pCmdByte = pOutput++;
				*pCmdByte = 0;
			}
			putCmdByte = (putCmdByte + 1) & 0x07;

			int encodedLength = 0;
			int lookAheadLength = inputLength < CLZSS.LZSS_LOOKAHEAD ? inputLength : CLZSS.LZSS_LOOKAHEAD;

			lzss_node_t* pHash = m_pHashTable[pLookAhead[0]].pStart;
			while (pHash != null) {
				int matchLength = 0;
				int length = lookAheadLength;
				while (length-- != 0 && pHash->pData[matchLength] == pLookAhead[matchLength])
					matchLength++;

				if (matchLength > encodedLength) {
					encodedLength = matchLength;
					pEncodedPosition = pHash->pData;
				}
				if (matchLength == lookAheadLength) {
					break;
				}
				pHash = pHash->pNext;
			}

			if (encodedLength >= 3) {
				*pCmdByte = unchecked((byte)((*pCmdByte >> 1) | 0x80));
				*pOutput++ = unchecked((byte)(((pLookAhead - pEncodedPosition - 1) >> CLZSS.LZSS_LOOKSHIFT)));
				*pOutput++ = unchecked((byte)(((pLookAhead - pEncodedPosition - 1) << CLZSS.LZSS_LOOKSHIFT) | (encodedLength - 1)));
			}
			else {
				encodedLength = 1;
				*pCmdByte = unchecked((byte)((*pCmdByte >> 1)));
				*pOutput++ = *pLookAhead;
			}

			for (int i = 0; i < encodedLength; i++)
				BuildHash(pLookAhead++);

			inputLength -= encodedLength;

			if (pOutput >= pEnd) {
				// compression is worse, abandon
				return null;
			}
		}

		if (inputLength != 0) {
			// unexpected failure
			Assert(false);
			return null;
		}

		if (0 == putCmdByte) {
			pCmdByte = pOutput++;
			*pCmdByte = 0x01;
		}
		else {
			*pCmdByte = unchecked((byte)(((*pCmdByte >> 1) | 0x80) >> (7 - putCmdByte)));
		}

		*pOutput++ = 0;
		*pOutput++ = 0;

		pOutputSize = (int)(pOutput - pStart);

		return pStart;
	}
}
