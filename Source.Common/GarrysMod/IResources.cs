using Source.Common.MaterialSystem;

using System.Drawing;

namespace Source.Common.GarrysMod;

public interface IImage
{
	void RegenerateTextureBits(ITexture unk1, IVTFTexture unk2, Rectangle unk3);
	void Release();
}

public interface IVideoWriter
{
	void Start(ReadOnlySpan<char> unk1, ReadOnlySpan<char> unk2, ReadOnlySpan<char> unk3, int unk4, int unk5, int unk6, ReadOnlySpan<char> unk7, int unk8, float unk9, bool unk10);
	void AddFrame(int unk1, int unk2, float unk3);
	void Finish();
	void Delete();

	void AddAudio(ReadOnlySpan<byte> unk1, uint unk2, byte unk3, byte unk4);
	void StartMovie();
	void EncodeRGB(ReadOnlySpan<byte> unk1, float unk2);
	void EndMovie();

	bool ManualFiling();

	ReadOnlySpan<char> FileExtension();
}

public interface IVideoHolly
{
	void AddAudio(ReadOnlySpan<byte> unk1, uint unk2, byte unk3, byte unk4);
	void StartMovie();
	void EncodeRGB(ReadOnlySpan<byte> unk1, float unk2);
	void EndMovie();
	bool ManualFiling();
	ReadOnlySpan<char> FileExtension();
}

public interface IResources
{
	int Init(IServiceProvider services);
	void Shutdown();
	IVideoHolly? CreateMovie();
	IMaterial? FindMaterial(ReadOnlySpan<char> unk1, ReadOnlySpan<char> unk2, bool unk3, bool unk4, bool unk5);
	Color GetTextureColour(ITexture unk1, int unk2, int unk3);
	void SavePNG(int unk1, int unk2, Span<byte> unk3, ReadOnlySpan<char> unk4, int unk5, int unk6);
	void SaveJPG(int unk1, int unk2, int unk3, Span<byte> unk4, ReadOnlySpan<char> unk5, int unk6, int unk7, Stream unk8);
	bool ShouldRecordSound();
	void AudioSamples(Span<byte> unk1, uint unk2 /* probably size of unk1*/, byte unk3, byte unk4);
	void SavePNGToBuffer(int unk1, int unk2, Span<byte> unk3, Stream unk4, int unk5, int unk6);
	void SaveJPGToBuffer(int unk1, int unk2, Span<byte> unk3, Stream unk4, int unk5, int unk6, int unk7);
	void SetImage(ITexture unk1, ReadOnlySpan<char> unk2);
}
