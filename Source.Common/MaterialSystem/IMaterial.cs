using Source.Common.Formats.Keyvalues;

using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Source.Common.MaterialSystem;

/*
The OpenGL shader layout should look roughly like this, if you used everything:

layout(location = 0) in vec3 v_Position;
layout(location = 1) in vec3 v_Normal;
layout(location = 2) in vec4 v_Color;
layout(location = 3) in vec4 v_Specular;
layout(location = 4) in vec4 v_TangentS;
layout(location = 5) in vec4 v_TangentT;
layout(location = 6) in vec4 v_Wrinkle;
layout(location = 7) in vec4 v_BoneIndex;

// Size here depends on your weights. But for now, only 2 is used anyway
layout(location = 8) in vec2 v_BoneWeights;

// Size here depends on your userdata.
layout(location = 9) in vec2 v_UserData;

// Sizes here depend on your texcoords
layout(location = 10) in vec2 v_TexCoord0;
layout(location = 11) in vec2 v_TexCoord1;
layout(location = 12) in vec2 v_TexCoord2;
layout(location = 13) in vec2 v_TexCoord3;
layout(location = 14) in vec2 v_TexCoord4;
layout(location = 15) in vec2 v_TexCoord5;
// TODO: HOW TO HANDLE TEXCOORDS 6/7, WE OVERFLOW THE MINIMUM OPENGL GIVES US
// (Or are we just going to switch to something else some day, like a potential Veldrid impl.)



 */
public enum VertexElement : int
{
	None = -1,

	Position = 0,
	Normal = 1,
	Color = 2,
	Specular = 3,
	TangentS = 4,
	TangentT = 5,
	Wrinkle = 6,
	BoneIndex = 7,

	BoneWeights1 = 8,
	BoneWeights2 = 9,
	BoneWeights3 = 10,
	BoneWeights4 = 11,

	UserData1 = 12,
	UserData2 = 13,
	UserData3 = 14,
	UserData4 = 15,

	TexCoord1D_0 = 16,
	TexCoord1D_1 = 17,
	TexCoord1D_2 = 18,
	TexCoord1D_3 = 19,
	TexCoord1D_4 = 20,
	TexCoord1D_5 = 21,
	TexCoord1D_6 = 22,
	TexCoord1D_7 = 23,

	TexCoord2D_0 = 24,
	TexCoord2D_1 = 25,
	TexCoord2D_2 = 26,
	TexCoord2D_3 = 27,
	TexCoord2D_4 = 28,
	TexCoord2D_5 = 29,
	TexCoord2D_6 = 30,
	TexCoord2D_7 = 31,

	TexCoord3D_0 = 32,
	TexCoord3D_1 = 33,
	TexCoord3D_2 = 34,
	TexCoord3D_3 = 35,
	TexCoord3D_4 = 36,
	TexCoord3D_5 = 37,
	TexCoord3D_6 = 38,
	TexCoord3D_7 = 39,

	TexCoord4D_0 = 40,
	TexCoord4D_1 = 41,
	TexCoord4D_2 = 42,
	TexCoord4D_3 = 43,
	TexCoord4D_4 = 44,
	TexCoord4D_5 = 45,
	TexCoord4D_6 = 46,
	TexCoord4D_7 = 47,

	Count
}

public enum VertexAttributeType
{
	Byte = 0x1400,
	UnsignedByte = 0x1401,
	Short = 0x1402,
	UnsignedShort = 0x1403,
	Int = 0x1404,
	UnsignedInt = 0x1405,
	Float = 0x1406
}

public static class VertexExts
{
	public const int VERTEX_LAST_BIT = 10;

	public const int VERTEX_BONE_WEIGHT_BIT = (VERTEX_LAST_BIT + 1);
	public const int USER_DATA_SIZE_BIT = VERTEX_LAST_BIT + 1;
	public const int TEX_COORD_SIZE_BIT = VERTEX_LAST_BIT + 4;
	public const int VERTEX_BONE_WEIGHT_MASK = VERTEX_LAST_BIT + 7;
	public const int USER_DATA_SIZE_MASK = 0x7 << USER_DATA_SIZE_BIT;
	public const int VERTEX_FORMAT_FIELD_MASK = 0x0FF;

	public static nint SizeOf(this VertexAttributeType type) => type switch {
		VertexAttributeType.Byte => sizeof(sbyte),
		VertexAttributeType.UnsignedByte => sizeof(byte),
		VertexAttributeType.Short => sizeof(short),
		VertexAttributeType.UnsignedShort => sizeof(ushort),
		VertexAttributeType.Int => sizeof(int),
		VertexAttributeType.UnsignedInt => sizeof(uint),
		VertexAttributeType.Float => sizeof(float),
		_ => throw new NotSupportedException()
	};

	public static VertexFormat GetBoneWeight(int boneWeights) {
		switch (boneWeights) {
			case 0: return 0;
			case 1: return VertexFormat.BoneWeights1;
			case 2: return VertexFormat.BoneWeights2;
			case 3: return VertexFormat.BoneWeights3;
			case 4: return VertexFormat.BoneWeights4;
			default:
				throw new NotSupportedException();
		}
	}

	public const int VERTEX_MAX_TEXTURE_COORDINATES = 8;

	public static VertexFormat GetTexCoordSize(int index, int dimension) {
		return dimension switch {
			0 => 0,
			1 => (VertexFormat)(1ul << ((int)VertexElement.TexCoord1D_0 + index)),
			2 => (VertexFormat)(1ul << ((int)VertexElement.TexCoord2D_0 + index)),
			3 => (VertexFormat)(1ul << ((int)VertexElement.TexCoord3D_0 + index)),
			4 => (VertexFormat)(1ul << ((int)VertexElement.TexCoord4D_0 + index)),
			_ => throw new NotSupportedException(),
		};
	}

	public static VertexFormat GetUserDataSize(int userDatas) {
		switch (userDatas) {
			case 0: return 0;
			case 1: return VertexFormat.UserData1;
			case 2: return VertexFormat.UserData2;
			case 3: return VertexFormat.UserData3;
			case 4: return VertexFormat.UserData4;
			default:
				throw new NotSupportedException();
		}
	}


	public static int GetBoneWeightsSize(this VertexFormat format) {
		if ((format & VertexFormat.BoneWeights4) != 0) return 4;
		if ((format & VertexFormat.BoneWeights3) != 0) return 3;
		if ((format & VertexFormat.BoneWeights2) != 0) return 2;
		if ((format & VertexFormat.BoneWeights1) != 0) return 1;
		return 0;
	}

	public static int GetUserDataSize(this VertexFormat format) {
		if ((format & VertexFormat.UserData4) != 0) return 4;
		if ((format & VertexFormat.UserData3) != 0) return 3;
		if ((format & VertexFormat.UserData2) != 0) return 2;
		if ((format & VertexFormat.UserData1) != 0) return 1;
		return 0;
	}

	public static int GetTexCoordDimensionSize(this VertexFormat format, int index) {
		const int TEXCOORD1D_BASE = (int)VertexElement.TexCoord1D_0;
		const int TEXCOORD2D_BASE = (int)VertexElement.TexCoord2D_0;
		const int TEXCOORD3D_BASE = (int)VertexElement.TexCoord3D_0;
		const int TEXCOORD4D_BASE = (int)VertexElement.TexCoord4D_0;

		ulong bit1D = 1ul << (TEXCOORD1D_BASE + index);
		ulong bit2D = 1ul << (TEXCOORD2D_BASE + index);
		ulong bit3D = 1ul << (TEXCOORD3D_BASE + index);
		ulong bit4D = 1ul << (TEXCOORD4D_BASE + index);

		ulong fmt = (ulong)format;
		if ((fmt & bit4D) != 0) return 4;
		if ((fmt & bit3D) != 0) return 3;
		if ((fmt & bit2D) != 0) return 2;
		if ((fmt & bit1D) != 0) return 1;
		return 0;
	}

	public static nint GetSize(this VertexElement element, VertexCompressionType compression = VertexCompressionType.None) {
		element.GetInformation(out int count, out VertexAttributeType type);
		return count * type.SizeOf();
	}

	public static void GetInformation(this VertexElement element, out int count, out VertexAttributeType type, VertexCompressionType compression = VertexCompressionType.None) {
		switch (element) {
			case VertexElement.Position: count = 3; type = VertexAttributeType.Float; return;
			case VertexElement.Normal: count = 3; type = VertexAttributeType.Float; return;
			case VertexElement.Color: count = 4; type = VertexAttributeType.UnsignedByte; return;
			case VertexElement.Specular: count = 4; type = VertexAttributeType.UnsignedByte; return;
			case VertexElement.TangentS: count = 3; type = VertexAttributeType.Float; return;
			case VertexElement.TangentT: count = 3; type = VertexAttributeType.Float; return;
			case VertexElement.Wrinkle: count = 1; type = VertexAttributeType.Float; return;
			case VertexElement.BoneIndex: count = 4; type = VertexAttributeType.UnsignedByte; return;

			case VertexElement.BoneWeights1: count = 1; type = VertexAttributeType.Float; return;
			case VertexElement.BoneWeights2: count = 2; type = VertexAttributeType.Float; return;
			case VertexElement.BoneWeights3: count = 3; type = VertexAttributeType.Float; return;
			case VertexElement.BoneWeights4: count = 4; type = VertexAttributeType.Float; return;

			case VertexElement.UserData1: count = 1; type = VertexAttributeType.Float; return;
			case VertexElement.UserData2: count = 2; type = VertexAttributeType.Float; return;
			case VertexElement.UserData3: count = 3; type = VertexAttributeType.Float; return;
			case VertexElement.UserData4: count = 4; type = VertexAttributeType.Float; return;

			case VertexElement.TexCoord1D_0: count = 1; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord1D_1: count = 1; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord1D_2: count = 1; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord1D_3: count = 1; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord1D_4: count = 1; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord1D_5: count = 1; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord1D_6: count = 1; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord1D_7: count = 1; type = VertexAttributeType.Float; return;

			case VertexElement.TexCoord2D_0: count = 2; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord2D_1: count = 2; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord2D_2: count = 2; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord2D_3: count = 2; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord2D_4: count = 2; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord2D_5: count = 2; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord2D_6: count = 2; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord2D_7: count = 2; type = VertexAttributeType.Float; return;

			case VertexElement.TexCoord3D_0: count = 3; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord3D_1: count = 3; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord3D_2: count = 3; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord3D_3: count = 3; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord3D_4: count = 3; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord3D_5: count = 3; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord3D_6: count = 3; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord3D_7: count = 3; type = VertexAttributeType.Float; return;

			case VertexElement.TexCoord4D_0: count = 4; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord4D_1: count = 4; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord4D_2: count = 4; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord4D_3: count = 4; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord4D_4: count = 4; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord4D_5: count = 4; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord4D_6: count = 4; type = VertexAttributeType.Float; return;
			case VertexElement.TexCoord4D_7: count = 4; type = VertexAttributeType.Float; return;
		}
		AssertMsg(false, "No size definition");
		count = 0;
		type = VertexAttributeType.Byte;
	}
}

[Flags]
public enum VertexFormat : ulong
{
	Position = 1ul << VertexElement.Position,
	Normal = 1ul << VertexElement.Normal,
	Color = 1ul << VertexElement.Color,
	Specular = 1ul << VertexElement.Specular,
	TangentS = 1ul << VertexElement.TangentS,
	TangentT = 1ul << VertexElement.TangentT,
	TangentSpace = TangentS | TangentT,
	Wrinkle = 1ul << VertexElement.Wrinkle,
	BoneIndex = 1ul << VertexElement.BoneIndex,
	BoneWeights1 = 1ul << VertexElement.BoneWeights1,
	BoneWeights2 = 1ul << VertexElement.BoneWeights2,
	BoneWeights3 = 1ul << VertexElement.BoneWeights3,
	BoneWeights4 = 1ul << VertexElement.BoneWeights4,
	UserData1 = 1ul << VertexElement.UserData1,
	UserData2 = 1ul << VertexElement.UserData2,
	UserData3 = 1ul << VertexElement.UserData3,
	UserData4 = 1ul << VertexElement.UserData4,

	TexCoord1D_0 = 1ul << VertexElement.TexCoord1D_0,
	TexCoord1D_1 = 1ul << VertexElement.TexCoord1D_1,
	TexCoord1D_2 = 1ul << VertexElement.TexCoord1D_2,
	TexCoord1D_3 = 1ul << VertexElement.TexCoord1D_3,
	TexCoord1D_4 = 1ul << VertexElement.TexCoord1D_4,
	TexCoord1D_5 = 1ul << VertexElement.TexCoord1D_5,
	TexCoord1D_6 = 1ul << VertexElement.TexCoord1D_6,
	TexCoord1D_7 = 1ul << VertexElement.TexCoord1D_7,

	TexCoord2D_0 = 1ul << VertexElement.TexCoord2D_0,
	TexCoord2D_1 = 1ul << VertexElement.TexCoord2D_1,
	TexCoord2D_2 = 1ul << VertexElement.TexCoord2D_2,
	TexCoord2D_3 = 1ul << VertexElement.TexCoord2D_3,
	TexCoord2D_4 = 1ul << VertexElement.TexCoord2D_4,
	TexCoord2D_5 = 1ul << VertexElement.TexCoord2D_5,
	TexCoord2D_6 = 1ul << VertexElement.TexCoord2D_6,
	TexCoord2D_7 = 1ul << VertexElement.TexCoord2D_7,

	TexCoord3D_0 = 1ul << VertexElement.TexCoord3D_0,
	TexCoord3D_1 = 1ul << VertexElement.TexCoord3D_1,
	TexCoord3D_2 = 1ul << VertexElement.TexCoord3D_2,
	TexCoord3D_3 = 1ul << VertexElement.TexCoord3D_3,
	TexCoord3D_4 = 1ul << VertexElement.TexCoord3D_4,
	TexCoord3D_5 = 1ul << VertexElement.TexCoord3D_5,
	TexCoord3D_6 = 1ul << VertexElement.TexCoord3D_6,
	TexCoord3D_7 = 1ul << VertexElement.TexCoord3D_7,

	TexCoord4D_0 = 1ul << VertexElement.TexCoord4D_0,
	TexCoord4D_1 = 1ul << VertexElement.TexCoord4D_1,
	TexCoord4D_2 = 1ul << VertexElement.TexCoord4D_2,
	TexCoord4D_3 = 1ul << VertexElement.TexCoord4D_3,
	TexCoord4D_4 = 1ul << VertexElement.TexCoord4D_4,
	TexCoord4D_5 = 1ul << VertexElement.TexCoord4D_5,
	TexCoord4D_6 = 1ul << VertexElement.TexCoord4D_6,
	TexCoord4D_7 = 1ul << VertexElement.TexCoord4D_7,

	Invalid = 0xFFFFFFFFFFFFFFFFul
}

public enum StencilOperation
{
	Keep = 1,
	Zero = 2,
	Replace = 3,
	IncrSat = 4,
	DecrSat = 5,
	Invert = 6,
	Incr = 7,
	Decr = 8,
}

public enum StencilComparisonFunction
{
	Never = 1,
	Less = 2,
	Equal = 3,
	LessEqual = 4,
	Greater = 5,
	NotEqual = 6,
	GreaterEqual = 7,
	Always = 8,
}


[Flags]
public enum MaterialVarFlags
{
	Debug = (1 << 0),
	NoDebugOverride = (1 << 1),
	NoDraw = (1 << 2),
	UseInFillrateMode = (1 << 3),

	VertexColor = (1 << 4),
	VertexAlpha = (1 << 5),
	SelfIllum = (1 << 6),
	Additive = (1 << 7),
	AlphaTest = (1 << 8),
	Multipass = (1 << 9),
	ZNearer = (1 << 10),
	Model = (1 << 11),
	Flat = (1 << 12),
	NoCull = (1 << 13),
	NoFog = (1 << 14),
	IgnoreZ = (1 << 15),
	Decal = (1 << 16),
	EnvMapSphere = (1 << 17),
	NoAlphaMod = (1 << 18),
	EnvMapCameraSpace = (1 << 19),
	BaseAlphaEnvMapMask = (1 << 20),
	Translucent = (1 << 21),
	NormalMapAlphaEnvMapMask = (1 << 22),
	NeedsSoftwareSkinning = (1 << 23),
	OpaqueTexture = (1 << 24),
	EnvMapMode = (1 << 25),
	SuppressDecals = (1 << 26),
	HalfLambert = (1 << 27),
	Wireframe = (1 << 28),
	AllowAlphaToCoverage = (1 << 29),
	IgnoreAlphaModulation = (1 << 30)
}
[Flags]
public enum MaterialVarFlags2
{
	// NOTE: These are for $flags2!!!!!
	//	UNUSED											= (1 << 0),

	LightingUnlit = 0,
	LightingVertexLit = (1 << 1),
	LightingLightmap = (1 << 2),
	LightingBumpedLightmap = (1 << 3),
	LightingMask =
		(LightingVertexLit |
			LightingLightmap |
			LightingBumpedLightmap),

	// FIXME: Should this be a part of the above lighting enums?
	DiffuseBumpmappedModel = (1 << 4),
	UsesEnvCubemap = (1 << 5),
	NeedsTangentSpaces = (1 << 6),
	NeedsSoftwareLighting = (1 << 7),
	// GR - HDR path puts lightmap alpha in separate texture...
	BlendWithLightmapAlpha = (1 << 8),
	NeedsBakedLightingSnapshots = (1 << 9),
	UseFlashlight = (1 << 10),
	UseFixedFunctionBakedLighting = (1 << 11),
	NeedsFixedFunctionFlashlight = (1 << 12),
	UseEditor = (1 << 13),
	NeedsPowerOfTwoFrameBufferTexture = (1 << 14),
	NeedsFullFrameBufferTexture = (1 << 15),
	IsSpritecard = (1 << 16),
	UsesVertexID = (1 << 17),
	SupportsHardwareSkinning = (1 << 18),
	SupportsFlashlight = (1 << 19),
}

public static class IMaterialExts
{
	public static bool IsErrorMaterial([NotNullWhen(false)] this IMaterial? mat) {
		return mat == null || mat.IsErrorMaterialInternal();
	}
}
public interface IMaterial
{
	bool IsRealTimeVersion();
	bool InMaterialPage();
	IMaterial GetMaterialPage();
	void GetMaterialOffset(Span<float> offset);
	void GetMaterialScale(Span<float> scale);
	float GetMappingWidth();
	float GetMappingHeight();
	void GetReflectivity(out Vector3 reflect);
	bool TryFindVar(ReadOnlySpan<char> varName, [NotNullWhen(true)] out IMaterialVar? found, bool complain = true);
	IMaterialVar FindVar(ReadOnlySpan<char> varName, out bool found, bool complain = true);
	void Refresh();
	void RefreshPreservingMaterialVars();
	void ColorModulate(float r, float g, float b);
	void GetColorModulation(out float r, out float g, out float b);
	void AlphaModulate(float alpha);
	float GetAlphaModulation();
	bool IsErrorMaterialInternal();
	VertexFormat GetVertexFormat();
	ReadOnlySpan<char> GetName();
	string? GetShaderName();
	int GetEnumerationID();
	void RecomputeStateSnapshots();
	IMaterialVar[]? GetShaderParams();
	int ShaderParamCount();
	bool GetPropertyFlag(MaterialPropertyTypes needsBumpedLightmaps);
	void IncrementReferenceCount();
	void DecrementReferenceCount();
	void DeleteIfUnreferenced();
	IMaterialVar? FindVarFast(ReadOnlySpan<char> name, ref TokenCache lightmapVarCache);
	bool IsTranslucent();
	bool IsVertexLit();
	int GetNumAnimationFrames();
	bool HasProxy();
	void CallBindProxy(object? clientEntity);

	void SetMaterialVarFlag(MaterialVarFlags flag, bool on);
	bool GetMaterialVarFlag(MaterialVarFlags flag);

	bool IsTwoSided();
	bool IsAlphaTested();
	bool UsesEnvCubemap();
	bool NeedsTangentSpace();
	bool NeedsSoftwareSkinning();
	bool NeedsSoftwareLighting();
	void SetUseFixedFunctionBakedLighting(bool enable);
	bool NeedsPowerOfTwoFrameBufferTexture(bool checkSpecificToThisFrame);
	bool NeedsFullFrameBufferTexture(bool checkSpecificToThisFrame);
	bool NeedsLightmapBlendAlpha();
}

// Intended to only be used by the material system and not other components
// todo: can we code analyze enforce this
public interface IMaterialInternal : IMaterial
{
	void DrawMesh(VertexCompressionType vertexCompressionType);
	int GetMaxLightmapPageID();
	int GetMinLightmapPageID();
	IMaterialInternal GetRealTimeVersion();
	bool IsManuallyCreated();
	bool IsPrecached();
	bool IsPrecachedVars();
	int GetReferenceCount();
	void ReloadTextures();
	bool IsUsingVertexID();
	void Precache();
	bool PrecacheVars(KeyValues? inVmtKeyValues = null, KeyValues? inPatchKeyValues = null, List<FileNameHandle_t>? includes = null, MaterialFindContext findContext = 0);
	void ReportVarChanged(IMaterialVar? var);
	uint GetChangeID();
	void SetEnumerationID(int id);
	void SetMaxLightmapPageID(int value);
	void SetMinLightmapPageID(int value);
	bool GetNeedsWhiteLightmap();
	void SetNeedsWhiteLightmap(bool value);
	void Uncache(bool preserveVars = false);
}
