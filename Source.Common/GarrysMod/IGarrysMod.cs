namespace Source.Common.GarrysMod;

public interface IGarrysMod : IGameEventListener2
{
	void Update();
	void MD5String(Span<byte> outMD5, ReadOnlySpan<byte> unk1, ReadOnlySpan<byte> unk2, ReadOnlySpan<byte> unk3);
	void PlaySound(ReadOnlySpan<char> sound);
	ReadOnlySpan<char> GetMapName();
	void RunConsoleCommand(ReadOnlySpan<char> cmd);
	void StartVideoScale(int unk1, int unk2);
	void EndVideoScale(int unk1, int unk2);
}
