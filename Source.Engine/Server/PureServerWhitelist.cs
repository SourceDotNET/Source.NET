global using static Source.Engine.Server.PureGlobals;

global using PureServerPublicKey_t = System.Collections.Generic.List<byte>;

using DStruct.BinaryTrees;

using Source.Common.Filesystem;
using Source.Common.Server;

namespace Source.Engine.Server;

public static class PureGlobals
{
	public static readonly PureFileTracker g_PureFileTracker = new();
	public static PureServerWhitelist CreatePureServerWhitelist(IFileSystem fileSystem) {
		throw new NotImplementedException();
	}
}

public class PureServerWhitelist : IPureServerWhitelist
{
	public PureServerFileClass GetFileClass(ReadOnlySpan<char> filename) {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<byte> GetTrustedKey(int keyIndex) {
		throw new NotImplementedException();
	}

	public int GetTrustedKeyCount() {
		throw new NotImplementedException();
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
