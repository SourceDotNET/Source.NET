using Source.Common.Formats.Keyvalues;
using Source.Common.Input;
using Source.Common.Launcher;
using Source.Common.Commands;
using SDL;
using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.HighPerformance;

namespace Source.SDLManager;

public unsafe partial class SDL3_System(ICommandLine commandLine) : ISystem
{
	public bool CommandLineParamExists(ReadOnlySpan<char> paramName) {
		return commandLine.FindParm(paramName) != 0;
	}

	public bool CreateShortcut(ReadOnlySpan<char> linkFileName, ReadOnlySpan<char> targetPath, ReadOnlySpan<char> arguments, ReadOnlySpan<char> workingDirectory, ReadOnlySpan<char> iconFile) {
		throw new NotImplementedException();
	}

	public bool DeleteRegistryKey(ReadOnlySpan<char> keyName) {
		throw new NotImplementedException();
	}

	public int GetAvailableDrives(Span<char> buf) {
		return 0;
	}

	public unsafe nuint GetClipboardText(nint offset, Span<char> buf) {
		if (!SDL3.SDL_HasClipboardText())
			return 0;

		byte* clipboard = SDL3.Unsafe_SDL_GetClipboardText();
		nuint len = SDL3.SDL_strlen(clipboard);
		nuint clipboardSize = (nuint)Encoding.UTF8.GetCharCount(clipboard, (int)len) * sizeof(char);
		char* clipboardCast = (char*)NativeMemory.Alloc(clipboardSize);
		Encoding.UTF8.GetChars(clipboard, (int)len, clipboardCast, (int)clipboardSize);
		new Span<char>(clipboardCast, (int)clipboardSize)[..Math.Min(buf.Length, (int)clipboardSize)].CopyTo(buf);
		NativeMemory.Free(clipboardCast);
		return len;
	}

	public nuint GetClipboardTextCount() {
		if (!SDL3.SDL_HasClipboardText())
			return 0;

		byte* clipboard = SDL3.Unsafe_SDL_GetClipboardText();
		nuint len = SDL3.SDL_strlen(clipboard);
		return len;
	}

	public bool GetCommandLineParamValue(ReadOnlySpan<char> paramName, Span<char> value) {
		throw new NotImplementedException();
	}

	public double GetCurrentTime() {
		return Platform.Time;
	}

	public bool GetCurrentTimeAndDate(out int year, out int month, out int dayOfWeek, out int day, out int hour, out int minute, out int second) {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GetDesktopFolderPath() {
		throw new NotImplementedException();
	}

	public double GetFrameTime() {
		return FrameTime;
	}

	public double GetFreeDiskSpace(ReadOnlySpan<char> path) {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GetFullCommandLine() {
		throw new NotImplementedException();
	}

	public bool GetRegistryInteger(ReadOnlySpan<char> key, out int value) {
		throw new NotImplementedException();
	}

	public bool GetShortcutTarget(ReadOnlySpan<char> linkFileName, Span<char> targetPath, Span<char> arguments) {
		throw new NotImplementedException();
	}

	public long GetTimeMillis() {
		return (long)(GetCurrentTime() * 1000);
	}

	public double GetTimeSinceLastUse() {
		throw new NotImplementedException();
	}

	public KeyValues? GetUserConfigFileData(ReadOnlySpan<char> dialogName, int dialogID) {
		throw new NotImplementedException();
	}

	public ButtonCode KeyCode_VirtualKeyToVGUI(int keyCode) {
		throw new NotImplementedException();
	}

	public bool ModifyShortcutTarget(ReadOnlySpan<char> linkFileName, ReadOnlySpan<char> targetPath, ReadOnlySpan<char> arguments, ReadOnlySpan<char> workingDirectory) {
		throw new NotImplementedException();
	}

	double FrameTime;

	public void RunFrame() {
		FrameTime = GetCurrentTime();
	}

	public void SaveUserConfigFile() {
		throw new NotImplementedException();
	}

	public void SetClipboardImage(IWindow wnd, int x1, int y1, int x2, int y2) {
		throw new NotImplementedException();
	}

	public unsafe void SetClipboardText(ReadOnlySpan<char> text) {
		if (text.IsEmpty)
			SDL3.SDL_SetClipboardText("");

		if (text.Length == 0)
			SDL3.SDL_SetClipboardText("");

		nuint bytes = (nuint)Encoding.UTF8.GetByteCount(text);
		byte* rawData = (byte*)NativeMemory.Alloc(bytes);
		Encoding.UTF8.GetBytes(text, new Span<byte>(rawData, (int)bytes));
		SDL3.SDL_SetClipboardText(rawData);
		NativeMemory.Free(rawData);
	}

	public bool SetRegistryInteger(ReadOnlySpan<char> key, int value) {
		throw new NotImplementedException();
	}

	public void SetUserConfigFile(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathName) {
		throw new NotImplementedException();
	}

	public bool SetWatchForComputerUse(bool state) {
		throw new NotImplementedException();
	}

	public void ShellExecute(ReadOnlySpan<char> command, ReadOnlySpan<char> file) {
		throw new NotImplementedException();
	}

	public void ShellExecuteEx(ReadOnlySpan<char> command, ReadOnlySpan<char> file, ReadOnlySpan<char> pParams) {
		throw new NotImplementedException();
	}

	private static ushort SwapUInt16(ushort val) {
		return (ushort)((val >> 8) | (val << 8));
	}

	private static uint SwapUInt32(uint val) {
		return (val >> 24) |
			   ((val >> 8) & 0x0000FF00) |
			   ((val << 8) & 0x00FF0000) |
			   (val << 24);
	}
	// TODO: Make these soft warnings
	struct FontConditions
	{
		public string Name;
		public int Weight;
		public bool Italic;
	}
	private FontConditions[] GetFontConditions(string absPath) {
		ReadOnlySpan<char> cExt = absPath.AsSpan().GetFileExtension();
		Span<char> ext = stackalloc char[cExt.Length];
		cExt.ToLowerInvariant(ext);
		switch (ext) {
			case "ttc": {
					using var stream = new FileStream(absPath, FileMode.Open, FileAccess.Read);
					using var reader = new BinaryReader(stream);

					string tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
					if (tag != "ttcf") {
						Warning("Not a valid TTC file.\n");
						return [];
					}

					reader.BaseStream.Seek(4, SeekOrigin.Current); // skip version
					uint numFonts = SwapUInt32(reader.ReadUInt32());

					int[] offsets = new int[numFonts];
					for (int i = 0; i < numFonts; i++) {
						offsets[i] = (int)SwapUInt32(reader.ReadUInt32());
					}

					FontConditions[] conditions = new FontConditions[numFonts];
					for (int i = 0; i < numFonts; i++)
						conditions[i] = ParseTtfAtOffset(reader, offsets[i]);
					return conditions;
				}
			case "ttf": {
					using (var stream = new FileStream(absPath, FileMode.Open, FileAccess.Read))
					using (var reader = new BinaryReader(stream)) {
						return [ParseTtfAtOffset(reader, 0)];
					}
				}
			default: return [];
		}
	}

	private FontConditions ParseTtfAtOffset(BinaryReader reader, int offset) {
		reader.BaseStream.Seek(offset, SeekOrigin.Begin);

		// Read TTF header
		reader.BaseStream.Seek(4, SeekOrigin.Current); // skip scaler type
		ushort numTables = SwapUInt16(reader.ReadUInt16());
		reader.BaseStream.Seek(6, SeekOrigin.Current); // skip searchRange, entrySelector, rangeShift

		uint os2Offset = 0;
		uint nameOffset = 0;

		// Locate OS/2 and name tables
		for (int i = 0; i < numTables; i++) {
			string tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
			reader.BaseStream.Seek(4, SeekOrigin.Current); // skip checksum
			uint tableOffset = SwapUInt32(reader.ReadUInt32());
			reader.BaseStream.Seek(4, SeekOrigin.Current); // skip length

			if (tag == "OS/2") os2Offset = tableOffset;
			if (tag == "name") nameOffset = tableOffset;
		}

		if (os2Offset == 0)
			throw new Exception("OS/2 table not found.");
		if (nameOffset == 0)
			throw new Exception("name table not found.");

		// Read font weight
		reader.BaseStream.Seek(os2Offset + 4, SeekOrigin.Begin); // usWeightClass at offset 4
		ushort usWeightClass = SwapUInt16(reader.ReadUInt16());

		// Read fsSelection to detect Italic (offset 62 from start of OS/2 table)
		reader.BaseStream.Seek(os2Offset + 62, SeekOrigin.Begin);
		ushort fsSelection = SwapUInt16(reader.ReadUInt16());
		bool italic = (fsSelection & 0x01) != 0;

		// Read name table
		reader.BaseStream.Seek(nameOffset, SeekOrigin.Begin);
		SwapUInt16(reader.ReadUInt16()); // format
		ushort count = SwapUInt16(reader.ReadUInt16());
		ushort stringOffset = SwapUInt16(reader.ReadUInt16());

		string? baseFamily = null;
		string? subFamily = null;
		string? fullName = null;

		for (int i = 0; i < count; i++) {
			ushort platformID = SwapUInt16(reader.ReadUInt16());
			ushort encodingID = SwapUInt16(reader.ReadUInt16());
			ushort languageID = SwapUInt16(reader.ReadUInt16());
			ushort nameID = SwapUInt16(reader.ReadUInt16());
			ushort length = SwapUInt16(reader.ReadUInt16());
			ushort offsetInTable = SwapUInt16(reader.ReadUInt16());

			long currentPos = reader.BaseStream.Position;

			// Decode string
			reader.BaseStream.Seek(nameOffset + stringOffset + offsetInTable, SeekOrigin.Begin);
			byte[] nameData = reader.ReadBytes(length);

			string decodedName = (platformID == 0 || platformID == 3)
				? Encoding.BigEndianUnicode.GetString(nameData)
				: Encoding.ASCII.GetString(nameData);

			// Store values based on nameID
			switch (nameID) {
				case 1: baseFamily = decodedName; break;   // Family
				case 2: subFamily = decodedName; break;    // Subfamily (Bold, Italic)
				case 4: fullName = decodedName; break;     // Full font name
			}

			reader.BaseStream.Seek(currentPos, SeekOrigin.Begin);
		}

		// Determine the best family name
		string familyName;
		if (!string.IsNullOrEmpty(fullName)) {
			// Remove style suffix if it matches subFamily
			familyName = fullName;
			if (!string.IsNullOrEmpty(subFamily) &&
				familyName.EndsWith(subFamily, StringComparison.OrdinalIgnoreCase)) {
				familyName = familyName.Substring(0, familyName.Length - subFamily.Length).TrimEnd();
			}
		}
		else if (!string.IsNullOrEmpty(baseFamily)) {
			familyName = baseFamily;
		}
		else {
			familyName = "Unknown";
		}

		return new FontConditions {
			Name = familyName,
			Weight = usWeightClass,
			Italic = italic
		};
	}

	ConVarRef cl_language;
	public void GetUILanguage(Span<char> destination) {
		cl_language.Init("cl_language", true);
		if (cl_language.IsValid())
			cl_language.GetString().ClampedCopyTo(destination);
		else
			"english".AsSpan().ClampedCopyTo(destination);
	}
	public void SetUILanguage(ReadOnlySpan<char> incoming) {
		cl_language.Init("cl_language", true);
		if (cl_language.IsValid())
			cl_language.SetValue(incoming);
	}
}
