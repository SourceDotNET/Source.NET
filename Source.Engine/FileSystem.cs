using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;

namespace Source.Engine;

public struct CFSSearchPathsInit
{
	public string? DirectoryName;
	public string Language;
	public IFileSystem FileSystem;
	public bool MountHDContent;
	public bool LowViolence;
	public string ModPath;
}
public enum FSReturnCode
{
	OK,
	MissingGameInfoFile,
	InvalidGameInfoFile,
	InvalidParameters,
	UnableToInit,
	MissingSteamDLL,
}
/// <summary>
/// Internal engine filesystem initializer.
/// </summary>
public class FileSystem(IFileSystem fileSystem, IServiceProvider services)
{
	FSReturnCode SetupFileSystemError(bool run, FSReturnCode ret, ReadOnlySpan<char> msg) {
		Dbg.Error($"{msg}\n");
		return ret;
	}
	public FSReturnCode LoadSearchPaths(ref CFSSearchPathsInit initInfo) {
		if (initInfo.FileSystem == null || initInfo.DirectoryName == null)
			return SetupFileSystemError(false, FSReturnCode.InvalidParameters, "FileSystem.LoadSearchPaths: Invalid parameters specified.");

		KeyValues? mainFile, fileSystemInfo, searchPaths;
		FSReturnCode retVal = LoadGameInfoFile(initInfo.DirectoryName, out mainFile, out fileSystemInfo, out searchPaths);
		if (retVal != FSReturnCode.OK)
			return retVal;

		string baseDir;
		if (!GetBaseDir(out baseDir))
			return SetupFileSystemError(false, FSReturnCode.InvalidParameters, "FileSystem.GetBaseDir: failed.");

		initInfo.ModPath = initInfo.DirectoryName;

#if !GMOD_DLL
		const string GAMEINFOPATH_TOKEN = "|gameinfo_path|";
		const string BASESOURCEPATHS_TOKEN = "|all_source_engine_paths|";

		// todo: extraSearchPath.
		bool lowViolence = initInfo.LowViolence;
		// bool firstGamePath = true;
		foreach (KeyValues cur in searchPaths!) {
			ReadOnlySpan<char> location = cur.GetString();
			string lBaseDir = baseDir;
			if (location.Contains(GAMEINFOPATH_TOKEN, StringComparison.OrdinalIgnoreCase)) {
				location = location[GAMEINFOPATH_TOKEN.Length..];
				lBaseDir = initInfo.DirectoryName;
			}
			else if (location.Contains(BASESOURCEPATHS_TOKEN, StringComparison.OrdinalIgnoreCase)) {
				location = location[BASESOURCEPATHS_TOKEN.Length..];
			}

			ReadOnlySpan<char> absSearchPath;
			if (Path.IsPathFullyQualified(location))
				absSearchPath = location;
			else
				absSearchPath = Path.GetFullPath(Path.Combine(lBaseDir, new string(location)));
			// TODO; theres a lot of weird logic here I don't fully understand yet.
			// So just do what we can here
			string[] pathIDs = cur.Name.Split('+');
			for (int i = 0; i < pathIDs.Length; i++) {
				pathIDs[i] = pathIDs[i].Trim();
				initInfo.FileSystem.AddSearchPath(absSearchPath, pathIDs[i]);
			}
		}
#else
		initInfo.FileSystem.GMOD_SetupDefaultPaths(baseDir, initInfo.DirectoryName);
#endif

		initInfo.FileSystem.MarkPathIDByRequestOnly("executable_path", true);
		initInfo.FileSystem.MarkPathIDByRequestOnly("gamebin", true);
		initInfo.FileSystem.MarkPathIDByRequestOnly("download", true);
		initInfo.FileSystem.MarkPathIDByRequestOnly("mod", true);
		initInfo.FileSystem.MarkPathIDByRequestOnly("game_write", true);
		initInfo.FileSystem.MarkPathIDByRequestOnly("mod_write", true);

#if GMOD_DLL
		initInfo.FileSystem.DoFilesystemRefresh();
#endif

		return FSReturnCode.OK;
	}

	private bool GetBaseDir(out string baseDir) {
		baseDir = AppContext.BaseDirectory;
		return true;
	}

	public const string GAMEINFO_FILENAME = "gameinfo.txt";

	public FSReturnCode LoadGameInfoFile(string directoryName, out KeyValues? mainFile, out KeyValues? fileSystemInfo, out KeyValues? searchPaths) {
		mainFile = null;
		fileSystemInfo = null;
		searchPaths = null;

		string gameInfoFilename = Path.Combine(directoryName, GAMEINFO_FILENAME);
		mainFile = ReadKeyValuesFile(gameInfoFilename);
		if (mainFile == null)
			return SetupFileSystemError(true, FSReturnCode.MissingGameInfoFile, $"{gameInfoFilename} is missing.");

		// Load ModInfo for other things so everything else doesn't need to parse gameinfo.txt.
		services.GetRequiredService<ModInfo>().LoadGameInfoFromKeyValues(mainFile);

		fileSystemInfo = mainFile.FindKey("FileSystem");
		if (fileSystemInfo == null)
			return SetupFileSystemError(true, FSReturnCode.InvalidGameInfoFile, $"{gameInfoFilename} is not a valid format (missing FileSystem).");

		searchPaths = fileSystemInfo.FindKey("SearchPaths");
		if (searchPaths == null)
			return SetupFileSystemError(true, FSReturnCode.InvalidGameInfoFile, $"{gameInfoFilename} is not a valid format (missing SearchPaths).");

		return FSReturnCode.OK;
	}

	private KeyValues? ReadKeyValuesFile(string filename) {
		KeyValues kv = new("");
		if (!kv.LoadFromFile(filename)) {
			return null;
		}

		return kv;
	}


	[ConCommand]
	void whereis(in TokenizedCommand args) {
		ReadOnlySpan<char> where = fileSystem.WhereIsFile(args.ArgS(1), "GAME");
		if (where.IsEmpty)
			ConWarning($"File '{args.ArgS(1)}' not found\n");
		else
			ConMsg($"{where}\n");
	}
	[ConCommand]
	void dir(in TokenizedCommand args) {
		ReadOnlySpan<char> input = args.Arg(1);

		ReadOnlySpan<char> filename = fileSystem.FindFirstEx(input, args.Arg(2), out FileFindHandle_t findHandle);
		while (!filename.IsEmpty) {
			Msg(filename);
			Msg("\n");
			filename = fileSystem.FindNext(findHandle);
		}
		fileSystem.FindClose(findHandle);
	}
	[ConCommand]
	void path(in TokenizedCommand args) {
		fileSystem.PrintSearchPaths();
	}

}
