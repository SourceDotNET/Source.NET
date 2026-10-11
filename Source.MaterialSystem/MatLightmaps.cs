using Source.Bitmap;
using Source.Common.Bitmap;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.ShaderAPI;
using Source.MaterialSystem;

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Source.MaterialSystem;


struct LightmapPageInfo
{
	public ushort Width;
	public ushort Height;
	public int Flags;
}

class DynamicLightmap
{
	public const int COUNT_DYNAMIC_LIGHTMAP_PAGES = 1;

	public void Init() {
		LightmapLocked = -1;
		FrameID = 0;
		CurrentDynamicIndex = 0;
		for (int i = 0; i < COUNT_DYNAMIC_LIGHTMAP_PAGES; i++)
			LightmapLockFrame[i] = 0;
	}

	public int LightmapLocked;
	public int FrameID;
	public int CurrentDynamicIndex;
	public readonly int[] LightmapLockFrame = new int[COUNT_DYNAMIC_LIGHTMAP_PAGES];
	public readonly ImagePacker[] ImagePackers = new ImagePacker[COUNT_DYNAMIC_LIGHTMAP_PAGES];
}

public enum LightmapsState
{
	Default,
	Released
}

public class MatLightmaps
{
	readonly List<ImagePacker> ImagePackers = [];
	int NumSortIDs;
	IMaterialInternal? CurrentWhiteLightmapMaterial;
	LightmapPageInfo[]? LightmapPages;
	readonly List<ShaderAPITextureHandle_t> LightmapPageTextureHandles = [];
	int NumLightmapPages;
	int UpdatingLightmapsStackDepth;
	int FirstDynamicLightmap;
	PixelWriterMem LightmapPixelWriter;
	int LockedLightmap;
	readonly DynamicLightmap Dynamic = new();
	LightmapsState LightmapsState;

	readonly MaterialSystem materialSystem;
	public MatLightmaps(MaterialSystem materialSystem) {
		this.materialSystem = materialSystem;
		LightmapPixelWriter = new();

		NumSortIDs = 0;
		CurrentWhiteLightmapMaterial = null;
		LightmapPages = null;
		NumLightmapPages = 0;
		UpdatingLightmapsStackDepth = 0;
		FirstDynamicLightmap = 0;
		LockedLightmap = -1;
		Dynamic.Init();
		LightmapsState = LightmapsState.Default;
	}

	public void Shutdown() {
		CleanupLightmaps();
	}

	public void BeginLightmapAllocation() {
		CleanupLightmaps();

		ImagePackers.Clear();
		int i = ImagePackers.Count; ImagePackers.Add(new(GetMaterialSystem()));
		ImagePackers[i].Reset(0, GetMaxLightmapPageWidth(), GetMaxLightmapPageHeight());

		SetCurrentMaterialInternal(null);
		CurrentWhiteLightmapMaterial = null;
		NumSortIDs = 0;

		ResetMaterialLightmapPageInfo();
		EnumerateMaterials();
	}

	public void EndLightmapAllocation() {
		NumLightmapPages++;
		NumSortIDs++;

		FirstDynamicLightmap = NumLightmapPages;
		Dynamic.Init();

		int nLastIdx = ImagePackers.Count;
		ImagePackers[nLastIdx - 1].GetMinimumDimensions(out int lastLightmapPageWidth, out int lastLightmapPageHeight);
		ImagePackers.Clear();

		LightmapPages = new LightmapPageInfo[GetNumLightmapPages()];

		int i;
		LightmapPageTextureHandles.EnsureCapacity(GetNumLightmapPages());
		for (i = 0; i < GetNumLightmapPages(); i++) {
			bool lastStaticLightmap = (i == (FirstDynamicLightmap - 1));
			LightmapPages[i].Width = (ushort)(lastStaticLightmap ? lastLightmapPageWidth : GetMaxLightmapPageWidth());
			LightmapPages[i].Height = (ushort)(lastStaticLightmap ? lastLightmapPageHeight : GetMaxLightmapPageHeight());
			LightmapPages[i].Flags = 0;

			AllocateLightmapTexture(i);

		}
	}

	public int AllocateLightmap(int width, int height, Span<int> offsetIntoLightmapPage, IMaterial? material) {
		IMaterialInternal? materialInternal = (IMaterialInternal?)(material);
		if (materialInternal == null) {
			Warning("Programming error: MatRenderContext.AllocateLightmap: NULL material\n");
			return NumSortIDs;
		}
		materialInternal = materialInternal.GetRealTimeVersion();

		int i;
		int packCount = ImagePackers.Count;
		if (GetCurrentMaterialInternal() != materialInternal) {
			for (i = packCount - 1; --i >= 0;) {
				ImagePackers.RemoveAt(i);
				--packCount;
			}

			if (GetCurrentMaterialInternal() != null) {
				ImagePackers[0].IncrementSortId();
				++NumSortIDs;
			}

			SetCurrentMaterialInternal(materialInternal);

			Assert(materialInternal.GetMinLightmapPageID() > materialInternal.GetMaxLightmapPageID());
			Assert(GetCurrentMaterialInternal());

			GetCurrentMaterialInternal()!.SetMinLightmapPageID(GetNumLightmapPages());
			GetCurrentMaterialInternal()!.SetMaxLightmapPageID(GetNumLightmapPages());
		}

		bool added = false;
		for (i = 0; i < packCount; ++i) {
			added = ImagePackers[i].AddBlock(width, height, ref offsetIntoLightmapPage[0], ref offsetIntoLightmapPage[1]);
			if (added)
				break;
		}

		if (!added) {
			++NumSortIDs;
			i = ImagePackers.Count; ImagePackers.Add(new(materialSystem));
			ImagePackers[i].Reset(NumSortIDs, GetMaxLightmapPageWidth(), GetMaxLightmapPageHeight());
			++NumLightmapPages;
			if (!ImagePackers[i].AddBlock(width, height, ref offsetIntoLightmapPage[0], ref offsetIntoLightmapPage[1])) {
				Error("MatLightmaps.AllocateLightmap: lightmap (%dx%d) too big to fit in page (%dx%d)\n",
					width, height, GetMaxLightmapPageWidth(), GetMaxLightmapPageHeight());
			}

			GetCurrentMaterialInternal()!.SetMaxLightmapPageID(GetNumLightmapPages());
		}

		return ImagePackers[i].GetSortId();
	}

	public int AllocateWhiteLightmap(IMaterial? material) {
		IMaterialInternal? materialInternal = (IMaterialInternal?)material;
		if (materialInternal == null) {
			Warning("Programming error: MatRenderContext.AllocateWhiteLightmap: NULL material\n");
			return NumSortIDs;
		}
		materialInternal = materialInternal.GetRealTimeVersion();
		if (CurrentWhiteLightmapMaterial == null || (CurrentWhiteLightmapMaterial != materialInternal)) {
			if (GetCurrentMaterialInternal() == null && CurrentWhiteLightmapMaterial == null) { }
			else {
				NumSortIDs++;
			}
			CurrentWhiteLightmapMaterial = materialInternal;
			materialInternal.SetNeedsWhiteLightmap(true);
		}

		return NumSortIDs;
	}

	/// <summary>
	/// NOTE: This returns a lightmap page ID, not a sortID like AllocateLightmap!!!
	/// </summary>
	public int AllocateDynamicLightmap(Span<int> lightmapSize, Span<int> outOffsetIntoPage, int frameID) {
		for (int i = 0; i < DynamicLightmap.COUNT_DYNAMIC_LIGHTMAP_PAGES; i++) //-V1008
		{
			int dynamicIndex = (Dynamic.CurrentDynamicIndex + i) % DynamicLightmap.COUNT_DYNAMIC_LIGHTMAP_PAGES; //-V1063
			int lightmapPageIndex = FirstDynamicLightmap + dynamicIndex;
			if (Dynamic.LightmapLockFrame[dynamicIndex] != frameID) {
				Dynamic.LightmapLockFrame[dynamicIndex] = frameID;
				Dynamic.ImagePackers[dynamicIndex].Reset(0, LightmapPages![lightmapPageIndex].Width, LightmapPages[lightmapPageIndex].Height);
			}

			if (Dynamic.ImagePackers[dynamicIndex].AddBlock(lightmapSize[0], lightmapSize[1], ref outOffsetIntoPage[0], ref outOffsetIntoPage[1]))
				return lightmapPageIndex;
		}

		return -1;
	}

	public int GetNumSortIDs() => NumSortIDs;

	public void GetSortInfo(Span<MaterialSystem_SortInfo> sortInfoArray) {
		int sortId = 0;
		ComputeSortInfo(sortInfoArray, ref sortId, false);
		ComputeWhiteLightmappedSortInfo(sortInfoArray, ref sortId, false);
		Assert(NumSortIDs == sortId);
	}

	readonly IMaterialSystemHardwareConfig HardwareConfig = Singleton<IMaterialSystemHardwareConfig>();
	readonly IShaderAPI ShaderAPI = Singleton<IShaderAPI>();

	public void UpdateLightmap(int lightmapPageID, Span<int> lightmapSize, Span<int> offsetIntoLightmapPage, Span<float> floatImage, Span<float> floatImageBump1, Span<float> floatImageBump2, Span<float> floatImageBump3) {
		bool hasBump = false;
		int uSize = 1;
		if (!floatImageBump1.IsEmpty && !floatImageBump2.IsEmpty && !floatImageBump3.IsEmpty) {
			hasBump = true;
			uSize = 4;
		}

		if (lightmapPageID >= GetNumLightmapPages() || lightmapPageID < 0) {
			Error("MatLightmaps.UpdateLightmap lightmapPageID=%d out of range\n", lightmapPageID);
			return;
		}
		bool bDynamic = IsDynamicLightmap(lightmapPageID);

		if (bDynamic) {
			int dynamicIndex = lightmapPageID - FirstDynamicLightmap;
			Assert(dynamicIndex < DynamicLightmap.COUNT_DYNAMIC_LIGHTMAP_PAGES);
			Dynamic.CurrentDynamicIndex = (dynamicIndex + 1) % DynamicLightmap.COUNT_DYNAMIC_LIGHTMAP_PAGES; //-V1063
		}

		bool lockSubRect;
		{
			lockSubRect = UpdatingLightmapsStackDepth <= 0 && !bDynamic;
			if (lockSubRect) {
				ShaderAPI.ModifyTexture(LightmapPageTextureHandles[lightmapPageID]);
				if (!ShaderAPI.TexLock(0, 0, offsetIntoLightmapPage[0], offsetIntoLightmapPage[1],
					lightmapSize[0] * uSize, lightmapSize[1], ref LightmapPixelWriter)) {
					return;
				}
			}
			else if (lightmapPageID != LockedLightmap) {
				if (!LockLightmap(lightmapPageID)) {
					// todo: warn
					return;
				}
			}
		}

		Span<int> subRectOffset = stackalloc int[2];
		{
			if (hasBump) {
				switch (HardwareConfig.GetHDRType()) {
					case HDRType.None:
						BumpedLightmapBitsToPixelWriter_LDR(floatImage, floatImageBump1, floatImageBump2, floatImageBump3, lightmapSize, lockSubRect ? subRectOffset : offsetIntoLightmapPage);
						break;
					case HDRType.Integer:
						BumpedLightmapBitsToPixelWriter_HDRI(floatImage, floatImageBump1, floatImageBump2, floatImageBump3, lightmapSize, lockSubRect ? subRectOffset : offsetIntoLightmapPage);
						break;
					case HDRType.Float:
						BumpedLightmapBitsToPixelWriter_HDRF(floatImage, floatImageBump1, floatImageBump2, floatImageBump3, lightmapSize, lockSubRect ? subRectOffset : offsetIntoLightmapPage);
						break;
				}
			}
			else {
				switch (HardwareConfig.GetHDRType()) {
					case HDRType.None:
						LightmapBitsToPixelWriter_LDR(floatImage, lightmapSize, lockSubRect ? subRectOffset : offsetIntoLightmapPage);
						break;

					case HDRType.Integer:
						LightmapBitsToPixelWriter_HDRI(floatImage, lightmapSize, lockSubRect ? subRectOffset : offsetIntoLightmapPage);
						break;

					case HDRType.Float:
						LightmapBitsToPixelWriter_HDRF(floatImage, lightmapSize, lockSubRect ? subRectOffset : offsetIntoLightmapPage);
						break;

					default:
						Assert(false);
						break;
				}
			}
		}

		if (lockSubRect) {
			ShaderAPI.TexUnlock();
		}
	}

	public void GetLightmapPageSize(int lightmapPageID, out int width, out int height) {
		switch (lightmapPageID) {
			default:
				Assert(lightmapPageID >= 0 && lightmapPageID < GetNumLightmapPages());
				width = LightmapPages![lightmapPageID].Width;
				height = LightmapPages![lightmapPageID].Height;
				break;
			case StandardLightmap.UserDefined:
				width = height = 1;
				break;
			case StandardLightmap.White:
			case StandardLightmap.WhiteBump:
				width = height = 1;
				break;
		}
	}

	public void ResetMaterialLightmapPageInfo() {
		foreach (var material in GetMaterialDict()) {
			material.SetMinLightmapPageID(9999);
			material.SetMaxLightmapPageID(-9999);
			material.SetNeedsWhiteLightmap(false);
		}
	}

	public int GetLightmapWidth(int lightmapPageID) {
		switch (lightmapPageID) {
			default:
				Assert(lightmapPageID >= 0 && lightmapPageID < GetNumLightmapPages());
				return LightmapPages![lightmapPageID].Width;
			case StandardLightmap.UserDefined:
				return 1;
			case StandardLightmap.White:
			case StandardLightmap.WhiteBump:
				return 1;
		}
	}

	public int GetLightmapHeight(int lightmapPageID) {
		switch (lightmapPageID) {
			default:
				Assert(lightmapPageID >= 0 && lightmapPageID < GetNumLightmapPages());
				return LightmapPages![lightmapPageID].Height;
			case StandardLightmap.UserDefined:
				return 1;
			case StandardLightmap.White:
			case StandardLightmap.WhiteBump:
				return 1;
		}
	}

	public void ReleaseLightmapPages() {
		switch (LightmapsState) {
			case LightmapsState.Default:
				break;
			default:
				Warning($"ReleaseLightmapPages is expected in LightmapsState.Default, current state = {LightmapsState}, discarded.\n");
				AssertMsg(false, "ReleaseLightmapPages is expected in LightmapsState.Default");

				return;
		}

		for (int i = 0; i < GetNumLightmapPages(); i++)
			ShaderAPI.DeleteTexture(LightmapPageTextureHandles[i]);

		LightmapsState = LightmapsState.Released;
	}

	public void RestoreLightmapPages() {
		switch (LightmapsState) {
			case LightmapsState.Released:
				break;

			default:
				Warning($"RestoreLightmapPages is expected in LightmapsState.Released, current state = {LightmapsState}, discarded.\n");
				AssertMsg(false, "RestoreLightmapPages is expected in LightmapsState.Released");
				return;
		}

		LightmapsState = LightmapsState.Default;

		for (int i = 0; i < GetNumLightmapPages(); i++)
			AllocateLightmapTexture(i);
	}

	public void EnableLightmapFiltering(bool enabled) {
		int i;
		for (i = 0; i < GetNumLightmapPages(); i++) {
			ShaderAPI.ModifyTexture(LightmapPageTextureHandles[i]);
			if (enabled) {
				ShaderAPI.TexMinFilter(TexFilterMode.Linear);
				ShaderAPI.TexMagFilter(TexFilterMode.Linear);
			}
			else {
				ShaderAPI.TexMinFilter(TexFilterMode.Nearest);
				ShaderAPI.TexMagFilter(TexFilterMode.Nearest);
			}
		}
	}

	public int GetNumLightmapPages() => NumLightmapPages;
	public ShaderAPITextureHandle_t GetLightmapPageTextureHandle(int lightmap) => LightmapPageTextureHandles[lightmap];
	public bool IsDynamicLightmap(int lightmap) => (lightmap >= FirstDynamicLightmap) ? true : false;

	public MaterialSystem GetMaterialSystem() => materialSystem;

	public void BeginUpdateLightmaps() {
		UpdatingLightmapsStackDepth++;
	}

	public void EndUpdateLightmaps() {
		UpdatingLightmapsStackDepth--;
		Assert(UpdatingLightmapsStackDepth >= 0);
		if (UpdatingLightmapsStackDepth <= 0 && LockedLightmap != -1) {
			ShaderAPI.TexUnlock();
			LockedLightmap = -1;
		}
	}

	int GetMaxLightmapPageWidth() {
		int nWidth = 512;
		if (nWidth > HardwareConfig.MaxTextureWidth())
			nWidth = HardwareConfig.MaxTextureWidth();

		return nWidth;
	}

	int GetMaxLightmapPageHeight() {
		int nHeight = 256;

		if (nHeight > HardwareConfig.MaxTextureHeight())
			nHeight = HardwareConfig.MaxTextureHeight();

		return nHeight;
	}

	void CleanupLightmaps() {
		// delete old lightmap pages
		if (LightmapPages != null) {
			int i;
			for (i = 0; i < GetNumLightmapPages(); i++)
				ShaderAPI.DeleteTexture(LightmapPageTextureHandles[i]);

			LightmapPages = null;
		}

		NumLightmapPages = 0;
	}

	// Allocate lightmap textures in D3D
	void AllocateLightmapTexture(int lightmap) {
		bool bUseDynamicTextures = HardwareConfig.PreferDynamicTextures();

		CreateTextureFlags flags = bUseDynamicTextures ? CreateTextureFlags.Dynamic : CreateTextureFlags.Managed;

		LightmapPageTextureHandles.EnsureCount(lightmap + 1);

		Span<char> debugName = stackalloc char[256];
		sprintf(debugName, "[lightmap %d]").D(lightmap);

		ImageFormat imageFormat;
		switch (HardwareConfig.GetHDRType()) {
			default:
				Assert(false);
				goto case HDRType.None;
			case HDRType.None:
				imageFormat = ImageFormat.RGBA8888;
				flags |= CreateTextureFlags.SRGB;
				break;
			case HDRType.Integer:
				imageFormat = ImageFormat.RGBA16161616;
				break;
			case HDRType.Float:
				imageFormat = ImageFormat.RGBA16161616F;
				break;
		}

		switch (LightmapsState) {
			case LightmapsState.Default: {
					LightmapPageTextureHandles[lightmap] = ShaderAPI.CreateTexture(
						GetLightmapWidth(lightmap), GetLightmapHeight(lightmap), 1,
						imageFormat,
						1, 1, (int)flags, debugName, TEXTURE_GROUP_LIGHTMAP);

					ShaderAPI.ModifyTexture(LightmapPageTextureHandles[lightmap]);
					ShaderAPI.TexMinFilter(TexFilterMode.Linear);
					ShaderAPI.TexMagFilter(TexFilterMode.Linear);

					//  if (!bUseDynamicTextures) 
					//  	ShaderAPI.TexSetPriority(1); << TODO in shaderapi
					InitLightmapBits(lightmap);
				}
				break;

			case LightmapsState.Released:
				DevMsg($"AllocateLightmapTexture({lightmap}) in released lightmap state (LightmapsState.Released), delayed till \"Restore\".\n");
				return;
			default:
				Warning($"AllocateLightmapTexture({lightmap}) in unknown lightmap state ({LightmapsState}), skipped.\n");
				AssertMsg(false, "AllocateLightmapTexture(?) in unknown lightmap state (?)");
				return;
		}
	}

	// Initializes lightmap bits
	void InitLightmapBits(int lightmap) {
		int width = GetLightmapWidth(lightmap);
		int height = GetLightmapHeight(lightmap);

		PixelWriter writer = new();

		ShaderAPI.ModifyTexture(LightmapPageTextureHandles[lightmap]);
		if (!ShaderAPI.TexLock(0, 0, 0, 0, width, height, ref writer))
			return;

		if (writer.IsUsingFloatFormat()) {
			for (int j = 0; j < height; ++j) {
				writer.Seek(0, j);
				for (int k = 0; k < width; ++k) {
#if !DEBUG
					writer.WritePixel(1, 1, 1);
#else
					if (((j + k) & 1) != 0)
						writer.WritePixelF(0.0f, 1.0f, 0.0f);
					else
						writer.WritePixelF(0.0f, 0.0f, 0.0f);
#endif
				}
			}
		}
		else {
			for (int j = 0; j < height; ++j) {
				writer.Seek(0, j);
				for (int k = 0; k < width; ++k) {
#if !DEBUG
					writer.WritePixel(0, 0, 0);
#else
					if (((j + k) & 1) != 0)
						writer.WritePixel(0, 255, 0);
					else
						writer.WritePixel(0, 0, 0);
#endif
				}
			}
		}

		ShaderAPI.TexUnlock();
	}

	void BumpedLightmapBitsToPixelWriter_LDR(Span<float> floatImage, Span<float> floatImageBump1, Span<float> floatImageBump2, Span<float> floatImageBump3, Span<int> lightmapSize, Span<int> offsetIntoLightmapPage) {
		int lightmapSize0 = lightmapSize[0];
		int lightmap0WriterSizeBytes = lightmapSize0 * LightmapPixelWriter.GetPixelSize();
		int rewindToNextPixel = -((lightmap0WriterSizeBytes * 3) - LightmapPixelWriter.GetPixelSize());

		Span<byte> color0 = stackalloc byte[3];
		Span<byte> color1 = stackalloc byte[3];
		Span<byte> color2 = stackalloc byte[3];
		Span<byte> color3 = stackalloc byte[3];

		for (int t = 0; t < lightmapSize[1]; t++) {
			int srcTexelOffset = 4 * (0 + t * lightmapSize0);
			LightmapPixelWriter.Seek(offsetIntoLightmapPage[0], offsetIntoLightmapPage[1] + t);

			for (int s = 0; s < lightmapSize0;
				s++, LightmapPixelWriter.SkipBytes(rewindToNextPixel), srcTexelOffset += 4) {
				ColorSpace.LinearToBumpedLightmap(floatImage[srcTexelOffset..],
					floatImageBump1[srcTexelOffset..], floatImageBump2[srcTexelOffset..],
					floatImageBump3[srcTexelOffset..],
					color0, color1, color2, color3);

				byte alpha = MathLib.RoundFloatToByte(floatImage[srcTexelOffset + 3] * 255.0f);
				LightmapPixelWriter.WritePixelNoAdvance(color0[0], color0[1], color0[2], alpha);

				LightmapPixelWriter.SkipBytes(lightmap0WriterSizeBytes);
				LightmapPixelWriter.WritePixelNoAdvance(color1[0], color1[1], color1[2], alpha);

				LightmapPixelWriter.SkipBytes(lightmap0WriterSizeBytes);
				LightmapPixelWriter.WritePixelNoAdvance(color2[0], color2[1], color2[2], alpha);

				LightmapPixelWriter.SkipBytes(lightmap0WriterSizeBytes);
				LightmapPixelWriter.WritePixelNoAdvance(color3[0], color3[1], color3[2], alpha);
			}
		}
	}

	void BumpedLightmapBitsToPixelWriter_HDRF(Span<float> floatImage, Span<float> floatImageBump1, Span<float> floatImageBump2, Span<float> floatImageBump3, Span<int> lightmapSize, Span<int> offsetIntoLightmapPage) {
		throw new NotImplementedException();
	}

	void BumpedLightmapBitsToPixelWriter_HDRI(Span<float> floatImage, Span<float> floatImageBump1, Span<float> floatImageBump2, Span<float> floatImageBump3, Span<int> lightmapSize, Span<int> offsetIntoLightmapPage) {
		int lightmapSize0 = lightmapSize[0];
		int lightmap0WriterSizeBytes = lightmapSize0 * LightmapPixelWriter.GetPixelSize();
		int rewindToNextPixel = -((lightmap0WriterSizeBytes * 3) - LightmapPixelWriter.GetPixelSize());

		Span<ushort> color0 = stackalloc ushort[4];
		Span<ushort> color1 = stackalloc ushort[4];
		Span<ushort> color2 = stackalloc ushort[4];
		Span<ushort> color3 = stackalloc ushort[4];

		if (LightmapPixelWriter.IsUsingFloatFormat()) {
			for (int t = 0; t < lightmapSize[1]; t++) {
				int srcTexelOffset = 4 * (0 + t * lightmapSize0);
				LightmapPixelWriter.Seek(offsetIntoLightmapPage[0], offsetIntoLightmapPage[1] + t);

				for (int s = 0; s < lightmapSize0;
					s++, LightmapPixelWriter.SkipBytes(rewindToNextPixel), srcTexelOffset += 4) {
					ColorSpace.LinearToBumpedLightmap(floatImage[srcTexelOffset..],
						floatImageBump1[srcTexelOffset..], floatImageBump2[srcTexelOffset..],
						floatImageBump3[srcTexelOffset..],
						color0, color1, color2, color3);
					float alpha = floatImage[srcTexelOffset + 3];
					Assert(alpha >= 0.0f && alpha <= 1.0f);
					color0[3] = color1[3] = color2[3] = color3[3] = (ushort)alpha;

					float toFloat = 1.0f / (float)(1 << 16);

					LightmapPixelWriter.WritePixelNoAdvanceF(toFloat * color0[0], toFloat * color0[1], toFloat * color0[2], toFloat * color0[3]);

					LightmapPixelWriter.SkipBytes(lightmap0WriterSizeBytes);
					LightmapPixelWriter.WritePixelNoAdvanceF(toFloat * color1[0], toFloat * color1[1], toFloat * color1[2], toFloat * color1[3]);

					LightmapPixelWriter.SkipBytes(lightmap0WriterSizeBytes);
					LightmapPixelWriter.WritePixelNoAdvanceF(toFloat * color2[0], toFloat * color2[1], toFloat * color2[2], toFloat * color2[3]);

					LightmapPixelWriter.SkipBytes(lightmap0WriterSizeBytes);
					LightmapPixelWriter.WritePixelNoAdvanceF(toFloat * color3[0], toFloat * color3[1], toFloat * color3[2], toFloat * color3[3]);
				}
			}
		}
		else {
			for (int t = 0; t < lightmapSize[1]; t++) {
				int srcTexelOffset = 4 * (0 + t * lightmapSize0);
				LightmapPixelWriter.Seek(offsetIntoLightmapPage[0], offsetIntoLightmapPage[1] + t);

				for (int s = 0; s < lightmapSize0;
					s++, LightmapPixelWriter.SkipBytes(rewindToNextPixel), srcTexelOffset += 4) {
					ColorSpace.LinearToBumpedLightmap(floatImage[srcTexelOffset..],
						floatImageBump1[srcTexelOffset..], floatImageBump2[srcTexelOffset..],
						floatImageBump3[srcTexelOffset..],
						color0, color1, color2, color3);
					ushort alpha = ColorSpace.LinearToUnsignedShort(floatImage[srcTexelOffset + 3], 16);
					color0[3] = color1[3] = color2[3] = color3[3] = alpha;

					LightmapPixelWriter.WritePixelNoAdvance(color0[0], color0[1], color0[2], color0[3]);

					LightmapPixelWriter.SkipBytes(lightmap0WriterSizeBytes);
					LightmapPixelWriter.WritePixelNoAdvance(color1[0], color1[1], color1[2], color1[3]);

					LightmapPixelWriter.SkipBytes(lightmap0WriterSizeBytes);
					LightmapPixelWriter.WritePixelNoAdvance(color2[0], color2[1], color2[2], color2[3]);

					LightmapPixelWriter.SkipBytes(lightmap0WriterSizeBytes);
					LightmapPixelWriter.WritePixelNoAdvance(color3[0], color3[1], color3[2], color3[3]);
				}
			}
		}
	}

	void LightmapBitsToPixelWriter_LDR(Span<float> floatImage, Span<int> lightmapSize, Span<int> offsetIntoLightmapPage) {
		Span<float> src = floatImage;
		Span<byte> color = stackalloc byte[4];
		for (int t = 0; t < lightmapSize[1]; ++t) {
			LightmapPixelWriter.Seek(offsetIntoLightmapPage[0], offsetIntoLightmapPage[1] + t);
			for (int s = 0; s < lightmapSize[0]; ++s, src = src[4..]) {
				memreset(color);
				ColorSpace.LinearToLightmap(color, src);
				color[3] = MathLib.RoundFloatToByte(src[3] * 255.0f);
				LightmapPixelWriter.WritePixel(color[0], color[1], color[2], color[3]);
			}
		}
	}

	void LightmapBitsToPixelWriter_HDRF(Span<float> floatImage, Span<int> lightmapSize, Span<int> offsetIntoLightmapPage) {
		throw new NotImplementedException();
	}

	void LightmapBitsToPixelWriter_HDRI(Span<float> floatImage, Span<int> lightmapSize, Span<int> offsetIntoLightmapPage) {
		Span<float> src = floatImage;
		if (LightmapPixelWriter.IsUsingFloatFormat()) {
			for (int t = 0; t < lightmapSize[1]; ++t) {
				LightmapPixelWriter.Seek(offsetIntoLightmapPage[0], offsetIntoLightmapPage[1] + t);
				for (int s = 0; s < lightmapSize[0]; ++s, src = src[4..]) {
					int r = ColorSpace.LinearFloatToCorrectedShort(src[0]);
					int g = ColorSpace.LinearFloatToCorrectedShort(src[1]);
					int b = ColorSpace.LinearFloatToCorrectedShort(src[2]);

					float toFloat = 1.0f / (float)(1 << 16);

					Assert(src[3] >= 0.0f && src[3] <= 1.0f);
					LightmapPixelWriter.WritePixelF(r * toFloat, g * toFloat, b * toFloat, src[3]);
				}
			}
		}
		else {
			for (int t = 0; t < lightmapSize[1]; ++t) {
				LightmapPixelWriter.Seek(offsetIntoLightmapPage[0], offsetIntoLightmapPage[1] + t);
				for (int s = 0; s < lightmapSize[0]; ++s, src = src[4..]) {
					int r = ColorSpace.LinearFloatToCorrectedShort(src[0]);
					int g = ColorSpace.LinearFloatToCorrectedShort(src[1]);
					int b = ColorSpace.LinearFloatToCorrectedShort(src[2]);
					int a = ColorSpace.LinearToUnsignedShort(src[3], 16);

					LightmapPixelWriter.WritePixel(r, g, b, a);
				}
			}
		}
	}

	// For computing sort info
	void ComputeSortInfo(Span<MaterialSystem_SortInfo> info, ref int sortId, bool alpha) {
		int lightmapPageID;
		foreach (IMaterialInternal material in GetMaterialDict()) {
			if (material.GetMinLightmapPageID() > material.GetMaxLightmapPageID())
				continue;

			for (lightmapPageID = material.GetMinLightmapPageID();
				 lightmapPageID <= material.GetMaxLightmapPageID(); ++lightmapPageID) {
				info[sortId].Material = material;
				info[sortId].LightmapPageID = lightmapPageID;
				++sortId;
			}
		}
	}

	void ComputeWhiteLightmappedSortInfo(Span<MaterialSystem_SortInfo> info, ref int sortId, bool alpha) {
		foreach (IMaterialInternal material in GetMaterialDict()) {
			if (material.GetNeedsWhiteLightmap()) { // TODO FIXME REFERENCECOUNT!!!!!!!!!!!!!!!!!!
				info[sortId].Material = material;
				if (material.GetPropertyFlag(MaterialPropertyTypes.NeedsBumpedLightmaps))
					info[sortId].LightmapPageID = StandardLightmap.WhiteBump;
				else
					info[sortId].LightmapPageID = StandardLightmap.White;

				sortId++;
			}
		}
	}

	void EnumerateMaterials() {
		int id = 0;
		foreach (IMaterialInternal material in GetMaterialDict()) {
			material.SetEnumerationID(id);
			++id;
		}
	}

	// Lock a lightmap for update.
	bool LockLightmap(int lightmap) {
		if (LockedLightmap != -1)
			ShaderAPI.TexUnlock();

		ShaderAPI.ModifyTexture(LightmapPageTextureHandles[lightmap]);
		int pageWidth = LightmapPages![lightmap].Width;
		int pageHeight = LightmapPages![lightmap].Height;
		if (!ShaderAPI.TexLock(0, 0, 0, 0, pageWidth, pageHeight, ref LightmapPixelWriter)) {
			Assert(false);
			return false;
		}
		LockedLightmap = lightmap;
		return true;
	}


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	IMaterialInternal? GetCurrentMaterialInternal()
		=> GetMaterialSystem().GetRenderContextInternal().GetCurrentMaterialInternal();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void SetCurrentMaterialInternal(IMaterialInternal? currentMaterial)
		=> GetMaterialSystem().GetRenderContextInternal().SetCurrentMaterialInternal(currentMaterial);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	IMatRenderContextInternal GetRenderContextInternal()
		=> GetMaterialSystem().GetRenderContextInternal();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	MaterialDict GetMaterialDict()
		=> materialSystem.MaterialDict;
}
