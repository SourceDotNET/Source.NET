#define DBGFLAG_HIDE_ASSERTS_FROM_DEBUGGING_STACK
using System.Diagnostics;
using System.Runtime.InteropServices;


namespace Source.Common;

public static class AssertDialog
{
	// Allows things to plug into the assert dialog systems.
	// (more than likely, the SDL launcher system)
	// These do not need to be provided
	public delegate void MainThreadDialogFactory(ref AssertDialogResult result);
	public delegate void SepThreadDialogFactory(ref AssertDialogResult result);

	public static event MainThreadDialogFactory? OnSepThreadAssert;
	public static event SepThreadDialogFactory? OnMainThreadAssert;

#if DBGFLAG_ASSERT
	public class AssertDisable
	{
		public ulong FileSymbol;
		public int LineMin;
		public int LineMax;
		public int IgnoreTimes;
	}

	public static bool ShouldUseNewAssertDialog() => true;
	static bool AssertsEnabled = true;
	static readonly LinkedList<AssertDisable> DisabledAsserts = [];

	public static bool AreAssertsDisabled() => Platform.GetCommandLine().Contains("-noassert", StringComparison.OrdinalIgnoreCase);

	static ulong HashFileLine(ReadOnlySpan<char> fileName, int line) {
		// Allocate a temporary stack buffer for hashing
		//Span<byte> hashTarget = stackalloc byte[(sizeof(char) * fileName.Length) + sizeof(int)];
		// Get bytes for the filename
		//ReadOnlySpan<byte> incomingText = MemoryMarshal.Cast<char, byte>(fileName);
		// Get bytes for the line. Need to reinterpret the integer reference as a byte span
		//ReadOnlySpan<byte> incomingLine = MemoryMarshal.Cast<int, byte>(new(ref line));
		// Copy the bytes of the line and text into one single contiguous buffer
		//incomingLine.CopyTo(hashTarget);
		//incomingText.CopyTo(hashTarget[sizeof(int)..]);
		// And then hash that buffer
		ulong hash = fileName.Hash();
		return hash;
	}

	public static bool AreAssertsEnabledInFileLine(ReadOnlySpan<char> fileName, int line) {
		ulong hash = HashFileLine(fileName, line);
		LinkedListNode<AssertDisable>? prev = DisabledAsserts.First;
		LinkedListNode<AssertDisable>? next = null;
		for (LinkedListNode<AssertDisable>? cur = DisabledAsserts.First; cur != null; cur = cur.Next) {
			next = cur.Next;

			AssertDisable assert = cur.Value;

			if (hash == assert.FileSymbol) {
				bool assertsEnabled = true;
				if (assert.LineMin == -1 && assert.LineMax == -1)
					assertsEnabled = false;

				if (line >= assert.LineMin && line <= assert.LineMax)
					assertsEnabled = false;

				if (!assertsEnabled) {
					if (assert.IgnoreTimes > 0) {
						--assert.IgnoreTimes;
						if (assert.IgnoreTimes == 0) {
							DisabledAsserts.Remove(cur);
							continue;
						}
					}

					return false;
				}
			}

			prev = cur.Next;
		}

		return true;
	}

	static AssertDisable CreateNewAssertDisable(ReadOnlySpan<char> file) {
		AssertDisable disable = new AssertDisable();
		DisabledAsserts.AddFirst(disable);

		disable.LineMin = disable.LineMax = -1;
		disable.IgnoreTimes = -1;
		disable.FileSymbol = file.Hash();

		return disable;
	}

	private static void IgnoreAssertsInCurrentFile(string filename) {
		CreateNewAssertDisable(filename);
	}

	private static AssertDisable IgnoreAssertsNearby(string filename, int line, int range) {
		AssertDisable disable = CreateNewAssertDisable(filename);
		disable.LineMin = line - range;
		disable.LineMax = line + range;
		return disable;
	}

	static AssertDialogOutgoing LastUserInput;
	static object LastUserInputLock = new();

#if DBGFLAG_HIDE_ASSERTS_FROM_DEBUGGING_STACK
	[DebuggerHidden]
#endif
	public static unsafe bool DoNewAssertDialog(ReadOnlySpan<char> file, int line, ReadOnlySpan<char> expression) {
		if (AreAssertsDisabled())
			return false;

		if (!AssertsEnabled)
			return false;

		if (!AreAssertsEnabledInFileLine(file, line))
			return false;

		AssertDialogResult result = new();
		result.AssertInfo.FileName = new(file);
		result.AssertInfo.Expression = new(expression);
		result.AssertInfo.Line = line;

		if (!ThreadInMainThread()) 
			OnSepThreadAssert?.Invoke(ref result);
		else 
			OnMainThreadAssert?.Invoke(ref result);

		// The above call will be blocking until something handles it,
		// and afterward, this lock is needed for the callbacks and 
		// last data settings calls

		bool breaking = false;
		lock (LastUserInputLock) {
			LastUserInput = result.UserInfo;

			// Evaluate what the user wants to do
			switch (result.UserInfo.Type) {
				case AssertDialogResultType.IgnoreFile:
					IgnoreAssertsInCurrentFile(result.AssertInfo.FileName);
					break;
				case AssertDialogResultType.Ignore:
					if (result.UserInfo.IgnoreNumTimes > 1) {
						AssertDisable pDisable = IgnoreAssertsNearby(result.AssertInfo.FileName, result.AssertInfo.Line, 0);
						pDisable.IgnoreTimes = result.UserInfo.IgnoreNumTimes - 1;
					}
					break;
				case AssertDialogResultType.IgnoreAlways:
					IgnoreAssertsNearby(result.AssertInfo.FileName, result.AssertInfo.Line, 0);
					break;
				case AssertDialogResultType.IgnoreNearby:
					if (result.UserInfo.LineRange < 1)
						break;

					IgnoreAssertsNearby(result.AssertInfo.FileName, result.AssertInfo.Line, result.UserInfo.LineRange);
					break;
				case AssertDialogResultType.IgnoreAll:
					AssertsEnabled = false;
					break;
				case AssertDialogResultType.Break:
					breaking = true;
					break;
			}
		}

		return breaking;
	}

	private static bool ShowMessageBox(string caption, string title) {

		return true;
	}

#else
	public static bool AreAssertsDisabled() => true;
	public static bool ShouldUseNewAssertDialog() => false;
	public static bool AreAssertsEnabledInFileLine(ReadOnlySpan<char> fileName, int line) => false;
	public static bool DoNewAssertDialog(ReadOnlySpan<char> file, int line, ReadOnlySpan<char> msg) => false;
#endif
	public enum AssertDialogResultType
	{
		Ignore = 247,
		IgnoreFile,
		IgnoreAlways,
		IgnoreNearby,
		IgnoreAll,
		Break
	}

	public struct AssertDialogIncoming
	{
		public string FileName;
		public string Expression;
		public int Line;
	}
	public struct AssertDialogOutgoing
	{
		public AssertDialogResultType Type;
		public int LineRange;
		public int IgnoreNumTimes;
		public AssertDialogOutgoing() {
			LineRange = 5;
			IgnoreNumTimes = -1;
		}
	}

	public struct AssertDialogResult
	{
		public AssertDialogIncoming AssertInfo;
		public AssertDialogOutgoing UserInfo;
		public AssertDialogResult() {
			AssertInfo = new();
			UserInfo = new();
		}
	}
}
