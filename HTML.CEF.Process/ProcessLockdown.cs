using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HTML.CEF.Process;

[SupportedOSPlatform("windows")]
static unsafe partial class ProcessLockdown
{
	const string LowIntegritySid = "S-1-16-4096";

	const uint TokenQuery = 0x0008;
	const uint TokenAdjustPrivileges = 0x0020;
	const uint TokenAdjustDefault = 0x0080;
	const int TokenIntegrityLevel = 25;
	const uint SeGroupIntegrity = 0x00000020;

	const int JobObjectBasicUIRestrictions = 4;
	const int JobObjectExtendedLimitInformation = 9;
	const uint JobObjectLimitActiveProcess = 0x00000008;
	const uint JobObjectUILimitAll = 0x000000FF;

	const int ProcessStrictHandleCheckPolicy = 3;
	const int ProcessExtensionPointDisablePolicy = 6;
	const int ProcessFontDisablePolicy = 9;
	const int ProcessImageLoadPolicy = 10;

	const uint StrictHandleCheckRaiseAndPermanent = 0x3;
	const uint ExtensionPointsDisabled = 0x1;
	const uint NonSystemFontsDisabled = 0x1;
	const uint NoRemoteAndLowLabelImages = 0x3;

	[StructLayout(LayoutKind.Sequential)]
	struct JobObjectBasicLimitInformation
	{
		public long PerProcessUserTimeLimit;
		public long PerJobUserTimeLimit;
		public uint LimitFlags;
		public nuint MinimumWorkingSetSize;
		public nuint MaximumWorkingSetSize;
		public uint ActiveProcessLimit;
		public nuint Affinity;
		public uint PriorityClass;
		public uint SchedulingClass;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct IoCounters
	{
		public ulong ReadOperationCount;
		public ulong WriteOperationCount;
		public ulong OtherOperationCount;
		public ulong ReadTransferCount;
		public ulong WriteTransferCount;
		public ulong OtherTransferCount;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct JobObjectExtendedLimitInformationData
	{
		public JobObjectBasicLimitInformation BasicLimitInformation;
		public IoCounters IoInfo;
		public nuint ProcessMemoryLimit;
		public nuint JobMemoryLimit;
		public nuint PeakProcessMemoryUsed;
		public nuint PeakJobMemoryUsed;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct TokenMandatoryLabel
	{
		public nint Sid;
		public uint Attributes;
	}

	[LibraryImport("kernel32.dll")]
	private static partial nint GetCurrentProcess();

	[LibraryImport("kernel32.dll")]
	private static partial int CloseHandle(nint handle);

	[LibraryImport("kernel32.dll")]
	private static partial nint LocalFree(nint mem);

	[LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW")]
	private static partial nint CreateJobObject(nint jobAttributes, nint name);

	[LibraryImport("kernel32.dll")]
	private static partial int SetInformationJobObject(nint job, int infoClass, void* info, uint infoLength);

	[LibraryImport("kernel32.dll")]
	private static partial int AssignProcessToJobObject(nint job, nint process);

	[LibraryImport("kernel32.dll")]
	private static partial int SetProcessMitigationPolicy(int policy, void* buffer, nuint length);

	[LibraryImport("advapi32.dll")]
	private static partial int OpenProcessToken(nint process, uint desiredAccess, out nint token);

	[LibraryImport("advapi32.dll")]
	private static partial int AdjustTokenPrivileges(nint token, int disableAllPrivileges, nint newState, uint bufferLength, nint previousState, nint returnLength);

	[LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSidToSidW", StringMarshalling = StringMarshalling.Utf16)]
	private static partial int ConvertStringSidToSid(string stringSid, out nint sid);

	[LibraryImport("advapi32.dll")]
	private static partial uint GetLengthSid(nint sid);

	[LibraryImport("advapi32.dll")]
	private static partial int SetTokenInformation(nint token, int infoClass, void* info, uint infoLength);

	public static bool Apply(ReadOnlySpan<char> processType) {
		bool isRenderer = processType.SequenceEqual("renderer");

		if (!SetMitigation(ProcessExtensionPointDisablePolicy, ExtensionPointsDisabled))
			return false;
		if (!SetMitigation(ProcessImageLoadPolicy, NoRemoteAndLowLabelImages))
			return false;
		if (!RestrictJob(isRenderer))
			return false;

		if (isRenderer) {
			if (!SetMitigation(ProcessStrictHandleCheckPolicy, StrictHandleCheckRaiseAndPermanent))
				return false;
			if (!SetMitigation(ProcessFontDisablePolicy, NonSystemFontsDisabled))
				return false;
		}

		return LowerToken();
	}

	static bool SetMitigation(int policy, uint flags) => SetProcessMitigationPolicy(policy, &flags, sizeof(uint)) != 0;

	static bool RestrictJob(bool restrictUI) {
		nint job = CreateJobObject(0, 0);
		if (job == 0)
			return false;

		JobObjectExtendedLimitInformationData limits = default;
		limits.BasicLimitInformation.LimitFlags = JobObjectLimitActiveProcess;
		limits.BasicLimitInformation.ActiveProcessLimit = 1;
		if (SetInformationJobObject(job, JobObjectExtendedLimitInformation, &limits, (uint)sizeof(JobObjectExtendedLimitInformationData)) == 0)
			return false;

		if (restrictUI) {
			uint uiRestrictions = JobObjectUILimitAll;
			if (SetInformationJobObject(job, JobObjectBasicUIRestrictions, &uiRestrictions, sizeof(uint)) == 0)
				return false;
		}

		return AssignProcessToJobObject(job, GetCurrentProcess()) != 0;
	}

	static bool LowerToken() {
		if (OpenProcessToken(GetCurrentProcess(), TokenQuery | TokenAdjustPrivileges | TokenAdjustDefault, out nint token) == 0)
			return false;

		try {
			if (AdjustTokenPrivileges(token, 1, 0, 0, 0, 0) == 0)
				return false;

			if (ConvertStringSidToSid(LowIntegritySid, out nint sid) == 0)
				return false;

			try {
				TokenMandatoryLabel label = new() { Sid = sid, Attributes = SeGroupIntegrity };
				return SetTokenInformation(token, TokenIntegrityLevel, &label, (uint)sizeof(TokenMandatoryLabel) + GetLengthSid(sid)) != 0;
			}
			finally {
				LocalFree(sid);
			}
		}
		finally {
			CloseHandle(token);
		}
	}
}
