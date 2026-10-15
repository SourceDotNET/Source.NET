using Source.Engine;

using System.Runtime.InteropServices;

namespace Game.Shared;

public class EntityMapData
{
	public const int MAPKEY_MAXLENGTH = 2048;
	ReadOnlyMemory<byte> EntData;
	int EntDataSize;
	ReadOnlyMemory<byte> CurrentKey;

	public EntityMapData(ReadOnlyMemory<byte> entBlock, int entBlockSize = -1) {
		EntData = entBlock;
		EntDataSize = entBlockSize;
		CurrentKey = entBlock;
	}

	public bool ExtractValue(ReadOnlySpan<byte> keyName, Span<byte> value) => MapEntity.ExtractValue(EntData.Span, keyName, value);

	public bool GetFirstKey(Span<byte> keyName, Span<byte> value) {
		CurrentKey = EntData; // reset the status pointer
		return GetNextKey(keyName, value);
	}

	public ReadOnlyMemory<byte> CurrentBufferPosition() => CurrentKey;

	public bool GetNextKey(Span<byte> keyName, Span<byte> value) {
		Span<byte> token = stackalloc byte[MAPKEY_MAXLENGTH];

		// parse key
		ReadOnlyMemory<byte> prevKey = CurrentKey;
		ReadOnlySpan<byte> rest = MapEntity.ParseToken(CurrentKey.Span, token);
		if (token[0] == '}') {
			// step back
			CurrentKey = prevKey;
			return false;
		}

		if (rest.IsEmpty) {
			Warning("EntityMapData::GetNextKey: EOF without closing brace\n");
			Assert(false);
			return false;
		}
		CurrentKey = CurrentKey[(CurrentKey.Length - rest.Length)..];

		MapEntity.CopyToken(token, keyName);

		// fix up keynames with trailing spaces
		int n = MapEntity.StrLen(keyName);
		while (n > 0 && keyName[n - 1] == ' ') {
			keyName[n - 1] = 0;
			n--;
		}

		// parse value
		rest = MapEntity.ParseToken(CurrentKey.Span, token);
		if (rest.IsEmpty) {
			Warning("EntityMapData::GetNextKey: EOF without closing brace\n");
			Assert(false);
			return false;
		}
		CurrentKey = CurrentKey[(CurrentKey.Length - rest.Length)..];

		if (token[0] == '}') {
			Warning("EntityMapData::GetNextKey: closing brace without data\n");
			Assert(false);
			return false;
		}

		// value successfully found
		MapEntity.CopyToken(token, value);
		return true;
	}

	// find the nth keyName in the entdata and change its value to the specified one.
	public bool SetValue(ReadOnlySpan<byte> keyName, ReadOnlySpan<byte> newValue, int nKeyInstance = 0) {
		Assert(EntDataSize != -1);
		if (EntDataSize == -1)
			return false;

		Span<byte> token = stackalloc byte[MAPKEY_MAXLENGTH];
		Span<byte> newValueBuf = stackalloc byte[1024];
		Span<byte> entData = MemoryMarshal.AsMemory(EntData).Span;
		ReadOnlySpan<byte> key = keyName[..MapEntity.StrLen(keyName)];
		ReadOnlySpan<byte> inputData = entData;
		int currKeyInstance = 0;

		while (!inputData.IsEmpty) {
			inputData = MapEntity.ParseToken(inputData, token);
			if (token[0] == '}')
				break;

			if (token[..MapEntity.StrLen(token)].SequenceEqual(key)) {
				++currKeyInstance;
				if (currKeyInstance > nKeyInstance) {
					int prevOffset = entData.Length - inputData.Length;
					inputData = MapEntity.ParseToken(inputData, token);
					int postOffset = entData.Length - inputData.Length;
					byte[] postData = entData.Slice(postOffset, MapEntity.StrLen(entData[postOffset..])).ToArray();

					ReadOnlySpan<byte> value = newValue[..MapEntity.StrLen(newValue)];
					int newValueLen;
					if (value.IsEmpty || value[0] != '"') {
						newValueLen = Math.Min(value.Length, newValueBuf.Length - 3);
						newValueBuf[0] = (byte)'"';
						value[..newValueLen].CopyTo(newValueBuf[1..]);
						newValueBuf[newValueLen + 1] = (byte)'"';
						newValueLen += 2;
					}
					else {
						newValueLen = Math.Min(value.Length, newValueBuf.Length - 1);
						value[..newValueLen].CopyTo(newValueBuf);
					}

					int padding = newValueLen - MapEntity.StrLen(token) - 2;

					int writeOffset = prevOffset + 1;
					int destEnd = Math.Min(EntDataSize, entData.Length);
					int copyLen = Math.Min(newValueLen, destEnd - writeOffset - 1);
					newValueBuf[..copyLen].CopyTo(entData[writeOffset..]);
					writeOffset += copyLen;

					int postLen = Math.Min(postData.Length, destEnd - writeOffset - 1);
					postData.AsSpan(0, postLen).CopyTo(entData[writeOffset..]);
					entData[writeOffset + postLen] = 0;

					CurrentKey = EntData[(EntData.Length - CurrentKey.Length + padding)..];
					return true;
				}
			}

			inputData = MapEntity.ParseToken(inputData, token);
		}

		return false;
	}
}

public static class MapEntity
{
	public static ReadOnlySpan<byte> SkipToNextEntity(ReadOnlySpan<byte> mapData, scoped Span<byte> workBuffer) {
		if (mapData.IsEmpty)
			return null;

		int openBraceCount = 1;
		while (!mapData.IsEmpty) {
			mapData = ParseToken(mapData, workBuffer);

			if (workBuffer.Length > 0 && workBuffer[0] == '{')
				openBraceCount++;
			else if (workBuffer.Length > 0 && workBuffer[0] == '}') {
				openBraceCount--;
				if (openBraceCount == 0)
					return mapData;
			}
		}

		return null;
	}

	static readonly char[] s_BraceChars = "{}()\'".ToCharArray();
	static readonly bool[] s_BraceCharacters = new bool[256];
	static bool s_BuildReverseMap = true;

	public static ReadOnlySpan<byte> ParseToken(ReadOnlySpan<byte> data, scoped Span<byte> newToken) {
		int len = 0;
		newToken[0] = 0;

		if (data == default || data.IsEmpty)
			return null;

		if (s_BuildReverseMap) {
			s_BuildReverseMap = false;
			Array.Clear(s_BraceCharacters, 0, s_BraceCharacters.Length);
			foreach (var chh in s_BraceChars)
				s_BraceCharacters[(byte)chh] = true;
		}

	skipwhite:
		while (true) {
			if (data.IsEmpty)
				return null;

			int c = data[0];
			if (c > ' ')
				break;

			if (c == 0)
				return null;

			data = data[1..];
		}

		int ch = data[0];

		if (ch == '/' && data.Length > 1 && data[1] == '/') {
			while (!data.IsEmpty && data[0] != '\n')
				data = data[1..];
			goto skipwhite;
		}

		if (ch == '"') {
			data = data[1..];

			while (len < EntityMapData.MAPKEY_MAXLENGTH) {
				if (data.IsEmpty)
					break;

				ch = data[0];
				data = data[1..];

				if (ch == '"' || ch == 0) {
					newToken[len] = 0;
					return data;
				}

				newToken[len++] = (byte)ch;
			}

			if (len >= EntityMapData.MAPKEY_MAXLENGTH) {
				len--;
				newToken[len] = 0;
			}

			newToken[len] = 0;
			return data;
		}

		if (ch < 256 && s_BraceCharacters[ch]) {
			newToken[len++] = (byte)ch;
			newToken[len] = 0;
			return data[1..];
		}

		do {
			newToken[len++] = (byte)ch;
			data = data[1..];

			if (data.IsEmpty)
				break;

			ch = data[0];

			if (ch < 256 && s_BraceCharacters[ch])
				break;

			if (len >= EntityMapData.MAPKEY_MAXLENGTH) {
				len--;
				newToken[len] = 0;
			}

		} while (ch > 32);

		newToken[len] = 0;
		return data;
	}

	public static bool ExtractValue(ReadOnlySpan<byte> entData, ReadOnlySpan<byte> keyName, Span<byte> value) {
		Span<byte> token = stackalloc byte[EntityMapData.MAPKEY_MAXLENGTH];
		ReadOnlySpan<byte> inputData = entData;

		while (!inputData.IsEmpty) {
			var remainder = ParseToken(inputData, token);
			if (remainder.IsEmpty)
				break;

			inputData = remainder;

			if (token[0] == '}')
				break;

			if (SequenceEquals(token, keyName)) {
				remainder = ParseToken(inputData, token);
				if (remainder.IsEmpty)
					return false;

				inputData = remainder;
				int tokenLen = token.IndexOf((byte)0);
				if (tokenLen < 0) tokenLen = token.Length;
				value.Clear();
				token[..tokenLen].CopyTo(value);
				return true;
			}

			remainder = ParseToken(inputData, token);
			if (remainder.IsEmpty)
				break;

			inputData = remainder;
		}

		return false;
	}

	static bool SequenceEquals(Span<byte> token, ReadOnlySpan<byte> key) {
		int len = token.IndexOf((byte)0);
		if (len < 0) len = token.Length;
		return token[..len].SequenceEqual(key);
	}

	public static int GetNumKeysInEntity(ReadOnlySpan<byte> entData) {
		Span<byte> token = stackalloc byte[EntityMapData.MAPKEY_MAXLENGTH];
		ReadOnlySpan<byte> inputData = entData;
		int numKeys = 0;

		while (!inputData.IsEmpty) {
			var remainder = ParseToken(inputData, token);	// get keyname
			if (remainder.IsEmpty)
				break;
			inputData = remainder;

			if (token[0] == '}')							// end of entity?
				break;										// must not have seen the classname

			numKeys++;

			remainder = ParseToken(inputData, token);		// skip over value
			if (remainder.IsEmpty)
				break;
			inputData = remainder;
		}

		return numKeys;
	}

	// length of a null-terminated token buffer
	public static int StrLen(ReadOnlySpan<byte> token) {
		int len = token.IndexOf((byte)0);
		return len < 0 ? token.Length : len;
	}

	// Q_strncpy equivalent: copy a null-terminated token into dest and null-terminate.
	public static void CopyToken(ReadOnlySpan<byte> token, Span<byte> dest) {
		int len = StrLen(token);
		if (len > dest.Length - 1)
			len = dest.Length - 1;
		token[..len].CopyTo(dest);
		dest[len] = 0;
	}
}
