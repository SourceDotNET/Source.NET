using CommunityToolkit.HighPerformance;

using Microsoft.Win32;

namespace Source.SDLManager;

public unsafe partial class SDL3_System
{
#pragma warning disable CA1416 // Validate platform compatibility (WIN32 ifdef catches this instead)
	static string[] possibleKeys = [@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Fonts"];

	struct Entry
	{
		public string Name;
		public int Index;
		public int Weight;
		public bool Italic;
		public string AbsPath;
	}
	List<Entry>? Entries;

	public ReadOnlySpan<char> GetEntryPath(ReadOnlySpan<char> name, int weight, bool italic) {
		if (Entries == null) {
			Entries = [];
			string windowsDirectory = Environment.GetEnvironmentVariable("windir")!;
			string fontsPath = Path.Combine(windowsDirectory, "Fonts");
			foreach (var keyPath in possibleKeys) {
				using var baseKey = Registry.LocalMachine.OpenSubKey(keyPath, false);
				if (baseKey is null)
					continue;

				foreach (var valueName in baseKey.GetValueNames()) {
					var value = baseKey.GetValue(valueName);

					string absPath = Path.Combine(fontsPath, (string)value!);
					int i = 0;
					foreach (var fontfile in GetFontConditions(absPath)) {
						Entries.Add(new() {
							AbsPath = absPath,
							Index = i++,
							Italic = fontfile.Italic,
							Weight = fontfile.Weight,
							Name = fontfile.Name
						});
					}
				}
			}
		}

		Span<Entry> entries = Entries.AsSpan();

		string? consideration = null;
		int closestDistance = int.MaxValue;

		for (int i = 0; i < entries.Length; i++) {
			ref Entry entry = ref entries[i];

			if (entry.Italic == italic && name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)) {
				int distance = Math.Abs(entry.Weight - weight);
				if (distance < closestDistance) {
					consideration = entry.AbsPath;
					closestDistance = distance;
				}
			}
		}

		return consideration;
	}

	public ReadOnlySpan<char> GetSystemFontPath(ReadOnlySpan<char> fontName, int weight = 0, bool italic = false) {
		return GetEntryPath(fontName, weight, italic);
	}
#pragma warning restore CA1416 // Validate platform compatibility
}
