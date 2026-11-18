using System;
using System.Collections.Generic;
using System.Text;

namespace Source.Common.Utilities;

public delegate void OnEnterFn(ReadOnlySpan<char> text);

public class UserConsoleInput
{
	// note: 256 by default in GMod, raised to the longest command the command buffer accepts
	const int MAX_CONSOLE_TEXTLEN = 512;
	const int MAX_BUFFER_LINES = 30;

	readonly Lock consoleLock = new();

	readonly char[] consoleText = new char[MAX_CONSOLE_TEXTLEN];
	int consoleTextLen;
	int cursorPosition;

	readonly char[] savedConsoleText = new char[MAX_CONSOLE_TEXTLEN];
	int savedConsoleTextLen;

	readonly string?[] lineBuffer = new string?[MAX_BUFFER_LINES];
	int inputLine;
	int browseLine;
	int totalLines;

	public event OnEnterFn? OnEnter;

	public void RunFrame() {
		using Lock.Scope scope = consoleLock.EnterScope();
		while (!Console.IsInputRedirected && Console.KeyAvailable) {
			var key = Console.ReadKey(true);
			switch (key.Key) {
				case ConsoleKey.UpArrow:
					ReceiveUpArrow();
					break;
				case ConsoleKey.DownArrow:
					ReceiveDownArrow();
					break;
				case ConsoleKey.LeftArrow:
					ReceiveLeftArrow();
					break;
				case ConsoleKey.RightArrow:
					ReceiveRightArrow();
					break;
				case ConsoleKey.Enter:
					ReadOnlySpan<char> line = ReceiveNewLine();
					if (line.Length > 0)
						OnEnter?.Invoke(line);
					break;
				case ConsoleKey.Backspace:
					ReceiveBackspace();
					break;
				case ConsoleKey.Tab:
					ReceiveTab();
					break;
				default:
					char ch = key.KeyChar;
					if (ch >= ' ' && ch <= '~')
						ReceiveStandardChar(ch);
					break;
			}
		}
	}

	public void Print(ReadOnlySpan<char> msg) {
		using Lock.Scope scope = consoleLock.EnterScope();
		for (int len = consoleTextLen; len > 0; len--)
			Console.Out.Write("\b \b");

		PrintRaw(msg);

		if (consoleTextLen > 0)
			Console.Out.Write(consoleText, 0, consoleTextLen);
	}

	static void PrintRaw(ReadOnlySpan<char> msg) {
#if WIN32
		if (msg.Contains('\a')) {
			char[] temp = new char[msg.Length];
			msg.Replace(temp, '\a', '.');
			Console.Out.Write(temp);
			return;
		}
#endif
		Console.Out.Write(msg);
	}

	string GetBufferedLine(int line) => (uint)line < MAX_BUFFER_LINES ? lineBuffer[line] ?? "" : "";

	void ShowBufferedLine() {
		string line = GetBufferedLine(browseLine);
		Console.Out.Write(line);
		line.CopyTo(consoleText);
		consoleTextLen = line.Length;
	}

	private void ReceiveUpArrow() {
		int lastCommandInHistory = inputLine + 1;
		if (lastCommandInHistory > totalLines)
			lastCommandInHistory = 0;

		if (browseLine == lastCommandInHistory)
			return;

		if (browseLine == inputLine) {
			if (consoleTextLen > 0)
				consoleText.AsSpan(0, consoleTextLen).CopyTo(savedConsoleText);

			savedConsoleTextLen = consoleTextLen;
		}

		browseLine--;
		if (browseLine < 0)
			browseLine = totalLines - 1;

		while (consoleTextLen-- > 0)
			Console.Out.Write("\b \b");

		ShowBufferedLine();
		cursorPosition = consoleTextLen;
	}

	private void ReceiveDownArrow() {
		if (browseLine == inputLine)
			return;

		browseLine++;
		if (browseLine > totalLines)
			browseLine = 0;

		while (consoleTextLen-- > 0)
			Console.Out.Write("\b \b");

		if (browseLine == inputLine) {
			if (savedConsoleTextLen > 0) {
				savedConsoleText.AsSpan(0, savedConsoleTextLen).CopyTo(consoleText);
				Console.Out.Write(consoleText, 0, savedConsoleTextLen);
			}

			consoleTextLen = savedConsoleTextLen;
		}
		else
			ShowBufferedLine();

		cursorPosition = consoleTextLen;
	}

	private void ReceiveLeftArrow() {
		if (cursorPosition <= 0)
			return;
		Console.Write('\b');
		cursorPosition--;
	}

	private void ReceiveRightArrow() {
		if (cursorPosition >= consoleTextLen)
			return;
		Console.Write(consoleText[cursorPosition]);
		cursorPosition++;
	}

	private void ReceiveTab() {

	}

	private void ReceiveBackspace() {
		int count;
		if (cursorPosition <= 0)
			return;
		consoleTextLen--;
		cursorPosition--;

		Console.Write('\b');
		for (count = cursorPosition; count < consoleTextLen; count++) {
			consoleText[count] = consoleText[count + 1];
			Console.Write(consoleText[count]);
		}

		Console.Write(' ');
		count = consoleTextLen;
		while (count >= cursorPosition) {
			Console.Write('\b');
			count--;
		}

		browseLine = inputLine;
	}

	private ReadOnlySpan<char> ReceiveNewLine() {
		Console.WriteLine();
		int len = 0;
		if (consoleTextLen > 0) {
			len = consoleTextLen;
			consoleTextLen = 0;
			cursorPosition = 0;

			ReadOnlySpan<char> text = consoleText.AsSpan(0, len);
			if (inputLine == 0 || !text.SequenceEqual(GetBufferedLine(inputLine - 1))) {
				lineBuffer[inputLine] = new(text);
				inputLine++;

				if (inputLine > totalLines)
					totalLines = inputLine;

				if (inputLine >= MAX_BUFFER_LINES)
					inputLine = 0;
			}

			browseLine = inputLine;
			return text;
		}
		else
			return null;
	}

	private void ReceiveStandardChar(char ch) {
		int count;
		if (consoleTextLen >= (consoleText.Length - 2))
			return;

		count = consoleTextLen;
		while (count > cursorPosition) {
			consoleText[count] = consoleText[count - 1];
			count--;
		}

		consoleText[cursorPosition] = ch;

		Console.Out.Write(consoleText, cursorPosition, consoleTextLen - cursorPosition + 1);
		consoleTextLen++;
		cursorPosition++;
		count = consoleTextLen;
		while (count > cursorPosition) {
			Console.Write('\b');
			count--;
		}

		browseLine = inputLine;
	}
}
