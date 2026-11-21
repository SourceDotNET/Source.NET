using Source.Common.Networking;

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace Source.Engine;

public partial class SV
{
	static RedirectType Redirected;
	static NetAddress? RedirectTo;
	static readonly char[] RedirectBuffer = new char[4096];
		static bool bInFlush = false; // recursion guard

	public static void RedirectFlush() {
		Assert(bInFlush == false);

		bInFlush = true;
		switch(Redirected){
			case RedirectType.Packet:
				Net.OutOfBandPrint(sv.Socket, RedirectTo, $"{(char)A2A.Print}{RedirectBuffer.SliceNullTerminatedString()}");
				break;
			case RedirectType.Client:
				Host.Client!.ClientPrintf(RedirectBuffer.SliceNullTerminatedString());
				break;
			case RedirectType.Socket:
			// todo: rconserver
				break;
		}
		// clear it
		RedirectBuffer[0] = '\0';
		bInFlush = false;
	}

	public static void RedirectStart(RedirectType rd, NetAddress? addr) {
		Redirected = rd;
		RedirectTo = addr;
		RedirectBuffer[0] = '\0';
	}

	public static void RedirectEnd() {
		RedirectFlush();
		Redirected = RedirectType.None;
	}

	public static void RedirectCheckFlush(int len) {
		if (len + strlen(RedirectBuffer) > RedirectBuffer.Length - 1)
			RedirectFlush();
	}

	public static bool RedirectActive() => Redirected != RedirectType.None;
	public static void RedirectAddText(ReadOnlySpan<char> text) {
		text = text.SliceNullTerminatedString();
		RedirectCheckFlush(text.Length);
		strcat(RedirectBuffer, text);
	}
}
