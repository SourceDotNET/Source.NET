namespace Game.Server;

public class AI_Hint : ServerOnlyEntity
{
	public short HintType() => throw new NotImplementedException();
	public string? GetGroup() => throw new NotImplementedException();
	public void Unlock(float delay) => throw new NotImplementedException();
}

[Flags]
public enum AI_HintNodeFlags {
	None = 0x00000000,
	Visible = 0x00000001,
	Nearest = 0x00000002,
	Random = 0x00000004
}

public static class AI_HintManager
{
	public static AI_HintNodeFlags GetFlags(ReadOnlySpan<char> token) {
		if (token.Length <= 0)
			return AI_HintNodeFlags.None;

		string lowercase = new string(token).ToLowerInvariant();

		if ("none".Contains(lowercase))
			return AI_HintNodeFlags.None;

		AI_HintNodeFlags bits = 0;

		if ("visible".Contains(lowercase))
			bits |= AI_HintNodeFlags.Visible;

		if ("nearest".Contains(lowercase))
			bits |= AI_HintNodeFlags.Nearest;

		if ("random".Contains(lowercase))
			bits |= AI_HintNodeFlags.Random;

		if ((bits & AI_HintNodeFlags.Nearest) != 0 &&
			 (bits & AI_HintNodeFlags.Random) != 0) {
			bits &= ~AI_HintNodeFlags.Random;

			DevMsg($"HINTFLAGS:{token}, inconsistent, the nearest node is never a random hint node, treating as nearest request!\n");
		}

		return bits;
	}
}
