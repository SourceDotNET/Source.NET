global using static Source.Engine.Server.PureGlobals;

global using PureServerPublicKey_t = System.Collections.Generic.List<byte>;

using DStruct.BinaryTrees;

using Source.Common;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.Server;
using Source.Common.Utilities;

using System.Runtime.InteropServices;
using System.Text;

namespace Source.Engine.Server;

public static class PureGlobals
{
	public static readonly PureFileTracker g_PureFileTracker = new();
	public static PureServerWhitelist CreatePureServerWhitelist(IFileSystem fileSystem) => PureServerWhitelist.Create(fileSystem);
}

public class PureServerWhitelist : IPureServerWhitelist
{
	const ushort kLoadOrder_HardcodedOverride = 0xffff;

#if WIN32
	const char PATH_SEPARATOR = '\\';
#else
	const char PATH_SEPARATOR = '/';
#endif

	sealed class Command
	{
		public PureServerFileClass FileClass;
		public ushort LoadOrder;
	}

	sealed class CaselessStringLessThan : IComparer<string>
	{
		public int Compare(string? lhs, string? rhs) => stricmp(lhs, rhs);
	}

	ushort LoadCounter;

	readonly SortedDictionary<string, Command> FileCommands = new(new CaselessStringLessThan());
	readonly SortedDictionary<string, Command> RecursiveDirCommands = new(new CaselessStringLessThan());
	readonly SortedDictionary<string, Command> NonRecursiveDirCommands = new(new CaselessStringLessThan());

	IFileSystem? FileSystem;

	readonly List<PureServerPublicKey_t> TrustedKeys = [];

	public static PureServerWhitelist Create(IFileSystem fileSystem) {
		PureServerWhitelist ret = new();
		ret.Init(fileSystem);
		return ret;
	}

	void Init(IFileSystem fileSystem) {
		Term();
		FileSystem = fileSystem;
	}

	public void Load(int pureMode) {
		Term();

		if (pureMode < 0)
			return;

		{
			KeyValues kv = new("");
			if (kv.LoadFromFile(g_pFileSystem, "cfg/trusted_keys_base.txt", "game"))
				LoadTrustedKeysFromKeyValues(kv);
			else
				Warning("Error loading cfg/trusted_keys_base.txt\n");
		}

		if (pureMode == 0) {
			KeyValues kv = new("");
			if (kv.LoadFromFile(g_pFileSystem, "cfg/pure_server_minimal.txt", "game"))
				LoadCommandsFromKeyValues(kv);
			else
				Warning("Error loading cfg/pure_server_minimal.txt\n");

			return;
		}

		{
			KeyValues kv = new("");
			if (kv.LoadFromFile(g_pFileSystem, "cfg/pure_server_full.txt", "game"))
				LoadCommandsFromKeyValues(kv);
			else
				Warning("Error loading cfg/pure_server_full.txt\n");
		}

		if (pureMode == 1) {
			{
				KeyValues kv = new("");
				bool loaded = kv.LoadFromFile(g_pFileSystem, "cfg/pure_server_whitelist.txt", "game");
				if (!loaded)
					loaded = kv.LoadFromFile(g_pFileSystem, "pure_server_whitelist.txt", "game");

				if (loaded)
					LoadCommandsFromKeyValues(kv);
				else
					Msg("pure_server_whitelist.txt not present; pure server using only base file rules\n");
			}

			KeyValues keys = new("");
			if (keys.LoadFromFile(g_pFileSystem, "cfg/trusted_keys.txt", "game"))
				LoadTrustedKeysFromKeyValues(keys);
			else
				Msg("trusted_keys.txt not present; pure server using only base trusted key list\n");
		}

		AddHardcodedFileCommands();
	}

	void Term() {
		FileCommands.Clear();
		RecursiveDirCommands.Clear();
		NonRecursiveDirCommands.Clear();
		TrustedKeys.Clear();
		LoadCounter = 0;
	}

	public bool LoadCommandsFromKeyValues(KeyValues kv) {
		Span<char> pathName = stackalloc char[MAX_PATH];

		for (KeyValues? curItem = kv.GetFirstValue(); curItem != null; curItem = curItem.GetNextValue()) {
			string keyValue = curItem.Name;
			ReadOnlySpan<char> modifiers = curItem.GetString();

			strcpy(pathName, keyValue);
			StrTools.FixSlashes(pathName, PATH_SEPARATOR);
			ReadOnlySpan<char> value = pathName.SliceNullTerminatedString();

			bool fromTrustedSource = false, allowFromDisk = false, checkCRC = false, any = false;

			foreach (string mod in modifiers.ToString().Split('+', StringSplitOptions.RemoveEmptyEntries)) {
				if (stricmp(mod, "from_steam") == 0 || stricmp(mod, "trusted_source") == 0)
					fromTrustedSource = true;
				else if (stricmp(mod, "allow_from_disk") == 0)
					allowFromDisk = true;
				else if (stricmp(mod, "check_crc") == 0 || stricmp(mod, "check_hash") == 0)
					checkCRC = true;
				else if (stricmp(mod, "any") == 0)
					any = true;
				else
					Warning("Unknown modifier in whitelist file: %s.\n", mod);
			}

			if ((fromTrustedSource && (allowFromDisk || checkCRC || any)) || (any && checkCRC)) {
				Warning("Whitelist: incompatible flags used on %s.\n", value.ToString());
				continue;
			}

			PureServerFileClass fileClass;
			if (checkCRC)
				fileClass = PureServerFileClass.CheckHash;
			else if (fromTrustedSource)
				fileClass = PureServerFileClass.AnyTrusted;
			else
				fileClass = PureServerFileClass.Any;

			AddFileCommand(value, fileClass, LoadCounter++);
		}

		return true;
	}

	void AddHardcodedFileCommands() {
		AddFileCommand("materials/vgui/replay/thumbnails/...", PureServerFileClass.Any, kLoadOrder_HardcodedOverride);
		AddFileCommand("sound/ui/hitsound.wav", PureServerFileClass.Any, kLoadOrder_HardcodedOverride);
		AddFileCommand("materials/vgui/logos/...", PureServerFileClass.Any, kLoadOrder_HardcodedOverride);
	}

	static bool ExtractFilePath(ReadOnlySpan<char> path, Span<char> dest) {
		int src = path.Length != 0 ? path.Length - 1 : 0;
		while (src != 0 && !path[src - 1].IsPathSeparator())
			src--;

		int copysize = Math.Min(src, dest.Length - 1);
		path[..copysize].CopyTo(dest);
		dest[copysize] = '\0';

		return copysize != 0;
	}

	void AddFileCommand(ReadOnlySpan<char> commandPath, PureServerFileClass fileClass, ushort loadOrder) {
		Command command = new() {
			LoadOrder = loadOrder,
			FileClass = fileClass
		};

		SortedDictionary<string, Command> list;
		ReadOnlySpan<char> endPart = commandPath.UnqualifiedFileName();
		if (stricmp(endPart, "...") == 0)
			list = RecursiveDirCommands;
		else if (stricmp(endPart, "*.*") == 0)
			list = NonRecursiveDirCommands;
		else
			list = FileCommands;

		Span<char> filePath = stackalloc char[MAX_PATH];
		if (list == RecursiveDirCommands || list == NonRecursiveDirCommands)
			ExtractFilePath(commandPath, filePath);
		else
			strcpy(filePath, commandPath);

		StrTools.FixSlashes(filePath, PATH_SEPARATOR);

		string key = new(filePath.SliceNullTerminatedString());
		list.Remove(key);
		list.Add(key, command);
	}

	static int Nibble(char c) {
		if (c >= '0' && c <= '9')
			return c - '0';

		if (c >= 'A' && c <= 'F')
			return 0x0a + (c - 'A');

		if (c >= 'a' && c <= 'f')
			return 0x0a + (c - 'a');

		return '0';
	}

	public bool LoadTrustedKeysFromKeyValues(KeyValues kv) {
		for (KeyValues? curItem = kv.GetFirstTrueSubKey(); curItem != null; curItem = curItem.GetNextTrueSubKey()) {
			if (stricmp(curItem.Name, "public_key") != 0) {
				Warning("Trusted key list has unexpected block '%s'; expected only 'public_key' blocks\n", curItem.Name);
				continue;
			}

			ReadOnlySpan<char> type = curItem.GetString("type", "(none)");
			if (stricmp(type, "rsa") != 0) {
				Warning("Trusted key type '%s' not supported.\n", type.ToString());
				continue;
			}

			ReadOnlySpan<char> keyData = curItem.GetString("rsa_public_key", "");
			if (keyData.IsEmpty) {
				Warning("trusted key is missing 'rsa_public_key' data; ignored\n");
				continue;
			}

			int numchars = keyData.Length & ~0x1;
			byte[] bits = new byte[keyData.Length / 2];
			for (int i = 0, o = 0; i < numchars && o < bits.Length; i += 2, o++)
				bits[o] = (byte)((Nibble(keyData[i]) << 4) | Nibble(keyData[i + 1]));

			TrustedKeys.Add([.. bits]);
		}

		return true;
	}

	static void UpdateCommandStats(SortedDictionary<string, Command> commands, ref int highest, ref int longestPathName) {
		foreach (KeyValuePair<string, Command> command in commands) {
			highest = Math.Max(highest, command.Value.LoadOrder);
			longestPathName = Math.Max(longestPathName, command.Key.Length);
		}
	}

	static void PrintCommand(string fileSpec, string? ext, int maxPathnameLen, Command command) {
		ReadOnlySpan<char> tempFileSpec = fileSpec;
		if (tempFileSpec.Length > 0 && (tempFileSpec[^1] == '/' || tempFileSpec[^1] == '\\'))
			tempFileSpec = tempFileSpec[..^1];

		StringBuilder buf = new();
		if (ext != null)
			buf.Append(tempFileSpec).Append(PATH_SEPARATOR).Append(ext);
		else
			buf.Append(tempFileSpec);

		for (int i = fileSpec.Length; i < maxPathnameLen + 6; i++)
			buf.Append(' ');

		buf.Append('\t');
		switch (command.FileClass) {
			default:
				buf.Append($"(bogus value {(int)command.FileClass})");
				Assert(false);
				break;
			case PureServerFileClass.Any:
				buf.Append("any");
				break;
			case PureServerFileClass.AnyTrusted:
				buf.Append("trusted_source");
				break;
			case PureServerFileClass.CheckHash:
				buf.Append("check_crc");
				break;
		}

		Msg("%s\n", buf.ToString());
	}

	static string? FindCommandByLoadOrder(SortedDictionary<string, Command> commands, int loadOrder) {
		foreach (KeyValuePair<string, Command> command in commands) {
			if (command.Value.LoadOrder == loadOrder)
				return command.Key;
		}

		return null;
	}

	public void PrintWhitelistContents() {
		int highestLoadOrder = 0, longestPathName = 0;
		UpdateCommandStats(FileCommands, ref highestLoadOrder, ref longestPathName);
		UpdateCommandStats(RecursiveDirCommands, ref highestLoadOrder, ref longestPathName);
		UpdateCommandStats(NonRecursiveDirCommands, ref highestLoadOrder, ref longestPathName);

		for (int loadOrder = 0; loadOrder <= highestLoadOrder; loadOrder++) {
			string? command = FindCommandByLoadOrder(FileCommands, loadOrder);
			if (command != null)
				PrintCommand(command, null, longestPathName, FileCommands[command]);
			else {
				command = FindCommandByLoadOrder(RecursiveDirCommands, loadOrder);
				if (command != null)
					PrintCommand(command, "...", longestPathName, RecursiveDirCommands[command]);
				else {
					command = FindCommandByLoadOrder(NonRecursiveDirCommands, loadOrder);
					if (command != null)
						PrintCommand(command, "*.*", longestPathName, NonRecursiveDirCommands[command]);
				}
			}
		}
	}

	public void Encode(UtlBuffer buf) {
		buf.PutUnsignedInt(0xffff);

		EncodeCommandList(FileCommands, buf);
		EncodeCommandList(RecursiveDirCommands, buf);
		EncodeCommandList(NonRecursiveDirCommands, buf);

		buf.PutUnsignedInt((uint)TrustedKeys.Count);
		for (int i = 0; i < TrustedKeys.Count; i++) {
			buf.PutUnsignedInt((uint)TrustedKeys[i].Count);
			buf.Put(CollectionsMarshal.AsSpan(TrustedKeys[i]));
		}
	}

	static void EncodeCommandList(SortedDictionary<string, Command> theList, UtlBuffer buf) {
		int count = 0;
		foreach (KeyValuePair<string, Command> command in theList) {
			if (command.Value.LoadOrder != kLoadOrder_HardcodedOverride)
				++count;
		}

		buf.PutInt(count);

		foreach (KeyValuePair<string, Command> command in theList) {
			if (command.Value.LoadOrder == kLoadOrder_HardcodedOverride)
				continue;

			buf.PutUnsignedChar((byte)command.Value.FileClass);
			buf.PutUnsignedShort(command.Value.LoadOrder);
			buf.PutString(command.Key);
		}
	}

	public void Decode(UtlBuffer buf) {
		Term();

		uint versionTag = buf.TellMaxPut() - buf.TellGet() >= sizeof(uint) ? BitConverter.ToUInt32(buf.Base(), buf.TellGet()) : 0;
		uint formatVersion = 0;
		if (versionTag == 0xffff) {
			buf.GetUnsignedInt();
			formatVersion = 1;
		}
		else
			Load(2);

		DecodeCommandList(FileCommands, buf, formatVersion);
		DecodeCommandList(RecursiveDirCommands, buf, formatVersion);
		DecodeCommandList(NonRecursiveDirCommands, buf, formatVersion);

		AddHardcodedFileCommands();

		if (formatVersion >= 1) {
			uint keyCount = buf.GetUnsignedInt();
			for (uint i = 0; i < keyCount && buf.IsValid(); i++) { // note: GMod trusts the count
				uint keySize = buf.GetUnsignedInt();
				if (!buf.IsValid() || keySize > buf.TellMaxPut() - buf.TellGet())
					break;

				byte[] key = new byte[keySize];
				buf.Get(key);
				TrustedKeys.Add([.. key]);
			}
		}
	}

	void DecodeCommandList(SortedDictionary<string, Command> theList, UtlBuffer buf, uint formatVersion) {
		Span<char> str = stackalloc char[MAX_PATH];

		int commands = buf.GetInt();
		for (int i = 0; i < commands && buf.IsValid(); i++) { // note: GMod trusts the count
			Command command = new();

			byte val = buf.GetUnsignedChar();
			ushort loadOrder = buf.GetUnsignedShort();

			if (formatVersion == 0) {
				command.FileClass = (val & 1) != 0 ? PureServerFileClass.Any : PureServerFileClass.AnyTrusted;
				command.LoadOrder = (ushort)(loadOrder + LoadCounter);
			}
			else {
				command.FileClass = (PureServerFileClass)val;
				command.LoadOrder = loadOrder;
			}

			buf.GetString(str);
			StrTools.FixSlashes(str, PATH_SEPARATOR);

			theList[new(str.SliceNullTerminatedString())] = command;
		}
	}

	Command? GetBestEntry(ReadOnlySpan<char> filename) {
		Span<char> relativeFilename = stackalloc char[MAX_PATH];
		strcpy(relativeFilename, filename);

		if (!StrTools.IsAbsolutePath(relativeFilename.SliceNullTerminatedString()) || FileSystem!.FullPathToRelativePath(filename, relativeFilename)) {
			StrTools.FixSlashes(relativeFilename, PATH_SEPARATOR);

			Span<char> relativeDir = stackalloc char[MAX_PATH];
			if (!ExtractFilePath(relativeFilename.SliceNullTerminatedString(), relativeDir))
				relativeDir[0] = '\0';

			Command? bestEntry = null;

			bestEntry = CheckEntry(FileCommands, relativeFilename.SliceNullTerminatedString(), bestEntry);
			if (relativeDir[0] != '\0') {
				bestEntry = CheckEntry(NonRecursiveDirCommands, relativeDir.SliceNullTerminatedString(), bestEntry);

				while (relativeDir[0] != '\0') {
					bestEntry = CheckEntry(RecursiveDirCommands, relativeDir.SliceNullTerminatedString(), bestEntry);
					if (!StrTools.StripLastDir(relativeDir))
						break;

					StrTools.FixSlashes(relativeDir, PATH_SEPARATOR);
				}
			}

			return bestEntry;
		}

		return null;
	}

	static Command? CheckEntry(SortedDictionary<string, Command> dict, ReadOnlySpan<char> entryName, Command? bestEntry) {
		if (dict.TryGetValue(new(entryName), out Command? command) && (bestEntry == null || command.LoadOrder > bestEntry.LoadOrder))
			bestEntry = command;

		return bestEntry;
	}

	public int GetTrustedKeyCount() => TrustedKeys.Count;

	public ReadOnlySpan<byte> GetTrustedKey(int keyIndex) {
		if (keyIndex < 0 || keyIndex >= TrustedKeys.Count)
			return default;

		return CollectionsMarshal.AsSpan(TrustedKeys[keyIndex]);
	}

	public PureServerFileClass GetFileClass(ReadOnlySpan<char> filename) {
		Command? command = GetBestEntry(filename);
		if (command != null)
			return command.FileClass;

		return PureServerFileClass.Any;
	}
}

public struct UserReportedFileHash
{
	public int IdxFile;
	public USERID UserID;
	public FileHash FileHash;

	public sealed class LessComparer : IComparer<UserReportedFileHash>
	{
		internal LessComparer() { }

		public int Compare(UserReportedFileHash lhs, UserReportedFileHash rhs) {
			int cmp = lhs.IdxFile.CompareTo(rhs.IdxFile);
			if (cmp != 0)
				return cmp;

			return lhs.UserID.SteamID.CompareTo(rhs.UserID.SteamID);
		}
	}
	public static readonly LessComparer Less = new LessComparer();
}

public struct UserReportedFile
{
	public CRC32_t CRCIdentifier;
	public string Filename;
	public string Path;
	public int FileFraction;

	public sealed class LessComparer : IComparer<UserReportedFile>
	{
		internal LessComparer() { }

		public int Compare(UserReportedFile lhs, UserReportedFile rhs) {
			int cmp = lhs.CRCIdentifier.CompareTo(rhs.CRCIdentifier);
			if (cmp != 0)
				return cmp;

			cmp = lhs.FileFraction.CompareTo(rhs.FileFraction);
			if (cmp != 0)
				return cmp;

			cmp = string.CompareOrdinal(lhs.Filename, rhs.Filename);
			if (cmp != 0)
				return cmp;

			return string.CompareOrdinal(lhs.Path, rhs.Path);
		}
	}

	public static readonly LessComparer Less = new LessComparer();
}

public struct MasterFileHash
{
	public int IdxFile;
	public int Matches;
	public FileHash FileHash;

	public sealed class LessComparer : IComparer<MasterFileHash>
	{
		internal LessComparer() { }

		public int Compare(MasterFileHash lhs, MasterFileHash rhs) {
			return lhs.IdxFile.CompareTo(rhs.IdxFile);
		}
	}

	public static readonly LessComparer Less = new LessComparer();
}

public class PureFileTracker
{
	public PureFileTracker() {
		TreeAllReportedFiles = new(UserReportedFile.Less);
		TreeMasterFileHashes = new(MasterFileHash.Less);
		TreeUserReportedFileHash = new(UserReportedFileHash.Less);
		LastFileReceivedTime = 0;
		MatchedFile = 0;
		MatchedMasterFile = 0;
		MatchedMasterFileHash = 0;
		MatchedFileFullHash = 0;
	}

	public void AddUserReportedFileHash(int idxFile, ref FileHash fileHash, USERID userID, bool bAddMasterRecord) {

	}
	public bool DoesFileMatch(ReadOnlySpan<char> pathID, ReadOnlySpan<char> relativeFilename, int fileFraction, ref FileHash fileHash, USERID userID) {
		return true; // note: dead code in GMod
	}
	public int ListUserFiles(bool listAll, ReadOnlySpan<char> filenameFind) {
		return 0; // note: dead code in GMod
	}
	public int ListAllTrackedFiles(bool listAll, ReadOnlySpan<char> filenameFind, int fileFractionMin, int fileFractionMax) {
		return 0; // note: dead code in GMod
	}

	public readonly RedBlackTree<UserReportedFile> TreeAllReportedFiles;
	public readonly RedBlackTree<MasterFileHash> TreeMasterFileHashes;
	public readonly RedBlackTree<UserReportedFileHash> TreeUserReportedFileHash;

	public TimeUnit_t LastFileReceivedTime;
	public int MatchedFile;
	public int MatchedMasterFile;
	public int MatchedMasterFileHash;
	public int MatchedFileFullHash;
}
