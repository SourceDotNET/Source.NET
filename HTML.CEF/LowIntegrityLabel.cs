using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HTML.CEF;

[SupportedOSPlatform("windows")]
static partial class LowIntegrityLabel
{
	const string LowIntegrityInheritedSddl = "S:(ML;OICI;NW;;;LW)";
	const uint SddlRevision1 = 1;
	const int SeFileObject = 1;
	const uint LabelSecurityInformation = 0x00000010;

	[LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", StringMarshalling = StringMarshalling.Utf16)]
	private static partial int ConvertStringSecurityDescriptorToSecurityDescriptor(string stringSecurityDescriptor, uint revision, out nint securityDescriptor, nint securityDescriptorSize);

	[LibraryImport("advapi32.dll", EntryPoint = "ConvertSecurityDescriptorToStringSecurityDescriptorW")]
	private static partial int ConvertSecurityDescriptorToStringSecurityDescriptor(nint securityDescriptor, uint revision, uint securityInformation, out nint stringSecurityDescriptor, nint stringSecurityDescriptorLen);

	[LibraryImport("advapi32.dll")]
	private static partial int GetSecurityDescriptorSacl(nint securityDescriptor, out int saclPresent, out nint sacl, out int saclDefaulted);

	[LibraryImport("advapi32.dll", EntryPoint = "GetNamedSecurityInfoW", StringMarshalling = StringMarshalling.Utf16)]
	private static partial uint GetNamedSecurityInfo(string objectName, int objectType, uint securityInfo, nint owner, nint group, nint dacl, out nint sacl, out nint securityDescriptor);

	[LibraryImport("advapi32.dll", EntryPoint = "SetNamedSecurityInfoW", StringMarshalling = StringMarshalling.Utf16)]
	private static partial uint SetNamedSecurityInfo(string objectName, int objectType, uint securityInfo, nint owner, nint group, nint dacl, nint sacl);

	[LibraryImport("kernel32.dll")]
	private static partial nint LocalFree(nint mem);

	static bool IsApplied(string directory) {
		if (GetNamedSecurityInfo(directory, SeFileObject, LabelSecurityInformation, 0, 0, 0, out _, out nint securityDescriptor) != 0)
			return false;

		try {
			if (ConvertSecurityDescriptorToStringSecurityDescriptor(securityDescriptor, SddlRevision1, LabelSecurityInformation, out nint sddl, 0) == 0)
				return false;

			try {
				return Marshal.PtrToStringUni(sddl) == LowIntegrityInheritedSddl;
			}
			finally {
				LocalFree(sddl);
			}
		}
		finally {
			LocalFree(securityDescriptor);
		}
	}

	public static bool Apply(string directory) {
		Directory.CreateDirectory(directory);

		if (IsApplied(directory))
			return true;

		if (ConvertStringSecurityDescriptorToSecurityDescriptor(LowIntegrityInheritedSddl, SddlRevision1, out nint securityDescriptor, 0) == 0)
			return false;

		try {
			if (GetSecurityDescriptorSacl(securityDescriptor, out int saclPresent, out nint sacl, out _) == 0 || saclPresent == 0)
				return false;

			return SetNamedSecurityInfo(directory, SeFileObject, LabelSecurityInformation, 0, 0, 0, sacl) == 0;
		}
		finally {
			LocalFree(securityDescriptor);
		}
	}
}
