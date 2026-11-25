#if WIN32
using Microsoft.Win32;
#endif

namespace Source.Common;

public interface IRegistry
{
	bool Init(ReadOnlySpan<char> platformName);
	void Shutdown();

	int ReadInt(ReadOnlySpan<char> key, int defaultValue = 0);
	void WriteInt(ReadOnlySpan<char> key, int value);

	ReadOnlySpan<char> ReadString(scoped ReadOnlySpan<char> key, ReadOnlySpan<char> defaultValue = default);
	void WriteString(ReadOnlySpan<char> key, ReadOnlySpan<char> value);

	int ReadInt(ReadOnlySpan<char> keyBase, ReadOnlySpan<char> key, int defaultValue = 0);
	void WriteInt(ReadOnlySpan<char> keyBase, ReadOnlySpan<char> key, int value);
	ReadOnlySpan<char> ReadString(ReadOnlySpan<char> keyBase, ReadOnlySpan<char> key, ReadOnlySpan<char> defaultValue);
	void WriteString(ReadOnlySpan<char> keyBase, ReadOnlySpan<char> key, ReadOnlySpan<char> value);

	long ReadInt64(ReadOnlySpan<char> key, long defaultValue = 0);
	void WriteInt64(ReadOnlySpan<char> key, long value);
}

public partial class Registry : IRegistry
{
	private bool Valid;
#if WIN32
	private RegistryKey? Key;
#endif

	public static IRegistry InstanceRegistry(ReadOnlySpan<char> subDirectoryUnderValve) {
		var instance = new Registry();
		instance.DirectInit(subDirectoryUnderValve);
		return instance;
	}

	public int ReadInt(ReadOnlySpan<char> keyBase, ReadOnlySpan<char> key, int defaultValue = 0) {
		nint len = strlen(keyBase);
		nint keyLen = strlen(key);
		Span<char> fullKey = stackalloc char[(int)(len + keyLen + 2)];
		int n = sprintf(fullKey, "%s\\%s").S(keyBase).S(key);
		return ReadInt(fullKey[..n], defaultValue);
	}

	public void WriteInt(ReadOnlySpan<char> keyBase, ReadOnlySpan<char> key, int value) {
		nint len = strlen(keyBase);
		nint keyLen = strlen(key);
		Span<char> fullKey = stackalloc char[(int)(len + keyLen + 2)];
		int n = sprintf(fullKey, "%s\\%s").S(keyBase).S(key);
		WriteInt(fullKey[..n], value);
	}

	public ReadOnlySpan<char> ReadString(ReadOnlySpan<char> keyBase, ReadOnlySpan<char> key, ReadOnlySpan<char> defaultValue) {
		nint len = strlen(keyBase);
		nint keyLen = strlen(key);
		Span<char> fullKey = stackalloc char[(int)(len + keyLen + 2)];
		int n = sprintf(fullKey, "%s\\%s").S(keyBase).S(key);
		return ReadString(fullKey[..n], defaultValue);
	}

	public void WriteString(ReadOnlySpan<char> keyBase, ReadOnlySpan<char> key, ReadOnlySpan<char> value) {
		nint len = strlen(keyBase);
		nint keyLen = strlen(key);
		Span<char> fullKey = stackalloc char[(int)(len + keyLen + 2)];
		int n = sprintf(fullKey, "%s\\%s").S(keyBase).S(key);
		WriteString(fullKey[..n], value);
	}
}

#if WIN32
#pragma warning disable CA1416 // This call site is reachable on all platforms.
public partial class Registry
{
	public Registry() {
		Valid = false;
		Key = null;
	}

	public int ReadInt(ReadOnlySpan<char> key, int defaultValue = 0) {
		if (!Valid)
			return defaultValue;

		string name = key.SliceNullTerminatedString().ToString();
		object? value = Key!.GetValue(name);

		if (value == null)
			return defaultValue;

		if (Key.GetValueKind(name) != RegistryValueKind.DWord)
			return defaultValue;

		return Convert.ToInt32(value);
	}

	public void WriteInt(ReadOnlySpan<char> key, int value) {
		if (!Valid)
			return;

		Key!.SetValue(key.SliceNullTerminatedString().ToString(), value, RegistryValueKind.DWord);
	}

	public ReadOnlySpan<char> ReadString(scoped ReadOnlySpan<char> key, ReadOnlySpan<char> defaultValue = default) {
		if (!Valid)
			return defaultValue;

		string name = key.SliceNullTerminatedString().ToString();
		object? value = Key!.GetValue(name);

		if (value == null)
			return defaultValue;

		if (Key.GetValueKind(name) != RegistryValueKind.String)
			return defaultValue;

		return (string)value;
	}

	public void WriteString(ReadOnlySpan<char> key, ReadOnlySpan<char> value) {
		if (!Valid)
			return;

		Key!.SetValue(key.SliceNullTerminatedString().ToString(), value.SliceNullTerminatedString().ToString(), RegistryValueKind.String);
	}

	public bool DirectInit(ReadOnlySpan<char> subDirectoryUnderValve) {
		Span<char> modelKey = stackalloc char[1024];
		int n = sprintf(modelKey, "Software\\Valve\\%s").S(subDirectoryUnderValve);

		try {
			Key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
				modelKey[..n].ToString(),
				RegistryKeyPermissionCheck.ReadWriteSubTree);
		}
		catch (Exception e) {
			Error($"Failed to open registry key: {e.HResult & 0xFFFF} - {e.Message}");
			return false;
		}

		if (Key == null) {
			Valid = false;
			return false;
		}

		Valid = true;
		return true;
	}

	public bool Init(ReadOnlySpan<char> platformName) {
		Span<char> subDir = stackalloc char[512];
		int n = sprintf(subDir, "%s\\Settings").S(platformName);
		return DirectInit(subDir.Slice(0, n));
	}

	public void Shutdown() {
		if (!Valid)
			return;

		Valid = false;
		Key?.Close();
	}

	public long ReadInt64(ReadOnlySpan<char> key, long defaultValue = 0) {
		if (!Valid)
			return defaultValue;

		string name = key.SliceNullTerminatedString().ToString();
		object? value = Key!.GetValue(name);

		if (value == null)
			return defaultValue;

		if (Key.GetValueKind(name) != RegistryValueKind.QWord)
			return defaultValue;

		return Convert.ToInt64(value);
	}

	public void WriteInt64(ReadOnlySpan<char> key, long value) {
		if (!Valid)
			return;

		Key!.SetValue(key.SliceNullTerminatedString().ToString(), value, RegistryValueKind.QWord);
	}
}
#pragma warning restore CA1416
#else
public partial class Registry
{
	public Registry() => Valid = false;

	public int ReadInt(ReadOnlySpan<char> key, int defaultValue = 0) => 0;

	public void WriteInt(ReadOnlySpan<char> key, int value) { }

	public long ReadInt64(ReadOnlySpan<char> key, long defaultValue = 0) => 0;

	public void WriteInt64(ReadOnlySpan<char> key, long value) { }

	public ReadOnlySpan<char> ReadString(scoped ReadOnlySpan<char> key, ReadOnlySpan<char> defaultValue = default) => default;

	public void WriteString(ReadOnlySpan<char> key, ReadOnlySpan<char> value) { }

	public bool DirectInit(ReadOnlySpan<char> subDirectoryUnderValve) => true;

	public bool Init(ReadOnlySpan<char> platformName) {
		Span<char> subDir = stackalloc char[512];
		int n = sprintf(subDir, "%s\\Settings").S(platformName);
		return DirectInit(subDir.Slice(0, n));
	}

	public void Shutdown() => Valid = false;
}
#endif
