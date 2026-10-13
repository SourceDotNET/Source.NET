using CommunityToolkit.HighPerformance;

using Source.Common;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;
using Source.Common.Utilities;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Source.MaterialSystem;

public enum MaterialFlags : ushort
{
	NeedsWhiteLightmap = 0x1,
	IsPrecached = 0x2,
	VarsIsPrecached = 0x4,
	ValidRenderState = 0x8,
	IsManuallyCreated = 0x10,
	UsesUNCFilename = 0x20,
	IsPReloaded = 0x40,
	ArtificalRefCount = 0x80,
}

public class Material : IMaterialInternal
{
	public readonly MaterialSystem materials;
	public MaterialVarFlags GetMaterialVarFlags() {
		if (ShaderParams != null && VarCount > (int)ShaderMaterialVars.Flags) {
			IMaterialVar var = ShaderParams[(int)ShaderMaterialVars.Flags];
			return (MaterialVarFlags)var.GetIntValue();
		}

		return 0;
	}

	public bool IsErrorMaterialInternal() {
		return materials.errorMaterial == this;
	}

	public MaterialVarFlags2 GetMaterialVarFlags2() {
		if (ShaderParams != null && VarCount > (int)ShaderMaterialVars.Flags2) {
			IMaterialVar var = ShaderParams[(int)ShaderMaterialVars.Flags2];
			return (MaterialVarFlags2)var.GetIntValue();
		}

		return 0;
	}

	public bool IsUsingVertexID() {
		return (GetMaterialVarFlags2() & MaterialVarFlags2.UsesVertexID) != 0;
	}

	IShaderAPI ShaderAPI => materials.ShaderAPI;

	public Material(MaterialSystem materials, ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroupName, KeyValues? keyValues) {
		Span<char> temp = stackalloc char[materialName.Length];
		materialName.ToLowerInvariant(temp);
		temp = temp.StripExtension(temp);
		temp = temp.SliceNullTerminatedString();
		this.materials = materials;
		name = new(temp);
		texGroupName = new(textureGroupName);
		this.keyValues = keyValues;
		if (keyValues != null) {
			flags |= MaterialFlags.IsManuallyCreated;
		}
		ShaderParams = null;
		MappingWidth = MappingHeight = 0;
		Reflectivity = new(0.2f, 0.2f, 0.2f);
		if (keyValues != null) {
			flags |= MaterialFlags.IsManuallyCreated;
		}

		ShaderRenderState = ShaderAPI.NewShaderShadow(materialName);
	}

	public int MappingWidth;
	public int MappingHeight;

	public ReadOnlySpan<char> GetName() {
		return name;
	}

	public bool IsManuallyCreated() {
		return (flags & MaterialFlags.IsManuallyCreated) != 0;
	}

	public bool IsPrecached() {
		return (flags & MaterialFlags.IsPrecached) != 0;
	}
	public bool IsPrecachedVars() {
		return (flags & MaterialFlags.VarsIsPrecached) != 0;
	}

	public void Precache() {
		if (IsPrecached())
			return;

		if (!PrecacheVars())
			return;

		flags |= MaterialFlags.IsPrecached;
		if (Shader != null)
			materials.ShaderSystem.InitShaderInstance(Shader, ShaderParams, GetName(), GetTextureGroupName());

		RecomputeStateSnapshots();
		FindRepresentativeTexture();
		PrecacheMappingDimensions();
	}

	public void ColorModulate(float r, float g, float b) {
		Precache();
		if (VarCount > (int)ShaderMaterialVars.Color)
			ShaderParams![(int)ShaderMaterialVars.Color].SetVecValue(r, g, b);
	}

	public void GetColorModulation(out float r, out float g, out float b) {
		Precache();

		Span<float> color = [0.0f, 0.0f, 0.0f];
		if (VarCount > (int)ShaderMaterialVars.Color)
			ShaderParams![(int)ShaderMaterialVars.Color].GetVecValue(color);
		r = color[0];
		g = color[1];
		b = color[2];
	}

	public void AlphaModulate(float alpha) {
		Precache();
		if (VarCount > (int)ShaderMaterialVars.Alpha)
			ShaderParams![(int)ShaderMaterialVars.Alpha].SetFloatValue(alpha);
	}

	public float GetAlphaModulation() {
		Precache();
		if (VarCount > (int)ShaderMaterialVars.Alpha)
			return ShaderParams![(int)ShaderMaterialVars.Alpha].GetFloatValue();
		return 0.0f;
	}

	public void RecomputeStateSnapshots() {
		bool ok = InitializeRenderState();
		if (!ok)
			SetupErrorShader();
	}

	private bool InitializeRenderState() {
		if (IsPrecached()) {
			if (materials.GetCurrentMaterial() == this) {
				ShaderAPI.FlushBufferedPrimitives();
			}

			if (Shader != null && !materials.ShaderSystem.InitRenderState(Shader, ShaderParams, ref ShaderRenderState, GetName())) {
				flags &= ~MaterialFlags.ValidRenderState;
				return false;
			}

			flags |= MaterialFlags.ValidRenderState;
		}

		return true;
	}

	private void SetupErrorShader() {
		throw new NotImplementedException();
	}

	static readonly ConVarRef mat_reduceparticles = new("mat_reduceparticles");

	static int complainCount = 0;
	public IMaterialVar FindVar(ReadOnlySpan<char> varName, out bool found, bool complain = true) {
		PrecacheVars();

		Span<char> lowercased = stackalloc char[varName.Length];
		varName.ToLowerInvariant(lowercased);
		ReadOnlySpan<char> lowercasedROS = lowercased; // Need to make a version of that that works on Span...
		foreach (var shaderParam in Vars) {
			if (shaderParam.GetName().Hash() == lowercasedROS.Hash()) {
				found = true;
				return shaderParam;
			}
		}
		found = false;
		if (complain) {
			if (complainCount < 100) {
				Warning($"No such variable \"{varName}\" for material \"{GetName()}\"\n");
				complainCount++;
			}
		}
		return GetDummyVariable();
	}

	static IMaterialVar? dummyVar;
	private IMaterialVar GetDummyVariable() {
		dummyVar ??= new MaterialVar(null, "$dummyVar", 0);
		return dummyVar;
	}

	// A more C#-style way of doing this
	public bool TryFindVar(ReadOnlySpan<char> varName, [NotNullWhen(true)] out IMaterialVar? found, bool complain = true) {
		bool hasFound;
		found = FindVar(varName, out hasFound, complain);
		return hasFound;
	}

	private void FindRepresentativeTexture() {
		Precache();
		bool found;
		IMaterialVar? textureVar = FindVar("$basetexture", out found, false);
		if (found && textureVar.GetVarType() == MaterialVarType.Texture) {
			ITextureInternal? texture = (ITextureInternal?)textureVar.GetTextureValue();
			if (texture != null)
				texture.GetReflectivity(out Reflectivity);
		}
		if (!found || textureVar.GetVarType() != MaterialVarType.Texture) {
			textureVar = FindVar("$envmapmask", out found, false);
			if (!found || textureVar.GetVarType() != MaterialVarType.Texture) {
				textureVar = FindVar("$bumpmap", out found, false);
				if (!found || textureVar.GetVarType() != MaterialVarType.Texture) {
					textureVar = FindVar("$dudvmap", out found, false);
					if (!found || textureVar.GetVarType() != MaterialVarType.Texture) {
						textureVar = FindVar("$normalmap", out found, false);
						if (!found || textureVar.GetVarType() != MaterialVarType.Texture) {
							representativeTexture = materials.TextureSystem.ErrorTexture();
							return;
						}
					}
				}
			}
		}

		representativeTexture = (ITextureInternal?)textureVar.GetTextureValue();
		if (representativeTexture != null)
			representativeTexture.Precache();
		else {
			representativeTexture = materials.TextureSystem.ErrorTexture();
			Assert(representativeTexture);
		}
	}

	private void PrecacheMappingDimensions() {
		if (representativeTexture == null) {
			MappingWidth = 64;
			MappingHeight = 64;
		}
		else {
			MappingWidth = representativeTexture.GetMappingWidth();
			MappingHeight = representativeTexture.GetMappingHeight();
		}
	}

	readonly List<FileNameHandle_t> VMTIncludes = [];

	private string GetTextureGroupName() {
		return "";
	}

	public static bool LoadVMTFile(IFileSystem fileSystem, ref KeyValues keyValues, KeyValues patchKeyValues, ReadOnlySpan<char> materialName, bool absolutePath, List<FileNameHandle_t>? includes = null) {
		Span<char> fileName = stackalloc char[MAX_PATH];
		ReadOnlySpan<char> pathID = "GAME";
		if (!absolutePath) {
			sprintf(fileName, "materials/%s.vmt").S(materialName);
		}
		else {
			sprintf(fileName, "%s.vmt").S(materialName);
			if (materialName[0] == '/' && materialName[1] == '/' && materialName[2] != '/') {
				pathID = null;
			}
		}

		if (!keyValues.LoadFromFile(fileSystem, fileName.SliceNullTerminatedString(), pathID)) {
			return false;
		}
		ExpandPatchFile(ref keyValues, patchKeyValues, pathID, includes);

		return true;
	}

	private static void ExpandPatchFile(ref KeyValues keyValues, KeyValues patchKeyValues, ReadOnlySpan<char> pathID, List<FileNameHandle_t>? includes) {
		KeyValues? nonPatchKeyValues = null;
		if (!patchKeyValues.IsEmpty())
			nonPatchKeyValues = keyValues.MakeCopy();
		else {
			bool success = AccumulateRecursiveVmtPatches(patchKeyValues, ref nonPatchKeyValues, keyValues, pathID, includes);
			if (!success) {
				return;
			}
		}

		if (nonPatchKeyValues != null) {
			ApplyPatchKeyValues(nonPatchKeyValues, patchKeyValues);
			keyValues = nonPatchKeyValues;
		}
	}

	private static void ApplyPatchKeyValues(KeyValues keyvalues, KeyValues patchKeyValues) {
		KeyValues? insertSection = patchKeyValues.FindKey("insert");
		KeyValues? replaceSection = patchKeyValues.FindKey("replace");

		if (insertSection != null)
			InsertKeyValues(keyvalues, insertSection, false);

		if (replaceSection != null)
			InsertKeyValues(keyvalues, replaceSection, true);
	}

	private static void InsertKeyValues(KeyValues dst, KeyValues src, bool checkForExistence, bool recursive = false) {
		KeyValues? srcVar = src.GetFirstSubKey();
		while (srcVar != null) {
			if (!checkForExistence || dst.FindKey(srcVar.Name) != null) {
				switch (srcVar.Type) {
					case KeyValues.Types.String:
						dst.SetString(srcVar.Name, srcVar.GetString());
						break;
					case KeyValues.Types.Int:
						dst.SetInt(srcVar.Name, srcVar.GetInt());
						break;
					case KeyValues.Types.Double:
						dst.SetFloat(srcVar.Name, srcVar.GetFloat());
						break;
					case KeyValues.Types.Pointer:
						dst.SetPtr(srcVar.Name, srcVar.GetPtr());
						break;
					case KeyValues.Types.None: {
							KeyValues? newDest = dst.FindKey(srcVar.Name, true);
							Assert(newDest != null);
							InsertKeyValues(newDest, srcVar, checkForExistence, true);
						}
						break;
				}
			}
			srcVar = srcVar.GetNextKey();
		}

		if (recursive && dst.GetFirstSubKey() == null)
			dst.SetInt("__vmtpatchdummy", 1);

		if (checkForExistence) {
			for (KeyValues? scan = dst.GetFirstTrueSubKey(); scan != null; scan = scan.GetNextTrueSubKey()) {
				KeyValues? tmp = src.FindKey(scan.Name);
				if (tmp == null)
					continue;

				if (tmp.Type != KeyValues.Types.None)
					continue;

				InsertKeyValues(scan, tmp, checkForExistence);
			}
		}
	}

	static IFileSystem? _fileSystem;
	static IFileSystem g_fileSystem => _fileSystem ??= Singleton<IFileSystem>();

	private static bool AccumulateRecursiveVmtPatches(KeyValues patchKeyValuesOut, ref KeyValues? baseKeyValuesOut, KeyValues keyValues, ReadOnlySpan<char> pathID, List<ulong>? includes) {
		includes?.Clear();

		patchKeyValuesOut.Clear();

		if (!keyValues.Name.Equals("patch", StringComparison.OrdinalIgnoreCase)) {
			if (baseKeyValuesOut != null)
				baseKeyValuesOut = null;

			return true;
		}

		KeyValues currentKeyValues = keyValues.MakeCopy();

		int nCount = 0;
		while (nCount < 10 && currentKeyValues.Name.Equals("patch", StringComparison.OrdinalIgnoreCase)) {
			// Accumulate the new patch keys from this file
			AccumulatePatchKeyValues(currentKeyValues, patchKeyValuesOut);

			// Load the included file
			ReadOnlySpan<char> includeFileName = currentKeyValues.GetString("include");

			if (includeFileName.IsEmpty) {
				Warning("VMT patch file has no include key - invalid!\n");
				Assert(!includeFileName.IsEmpty);
				break;
			}

			currentKeyValues.Clear();
			bool bSuccess = currentKeyValues.LoadFromFile(g_fileSystem, includeFileName, pathID);
			if (bSuccess)
				includes?.Add(g_fileSystem.FindOrAddFileName(includeFileName));
			else {
				currentKeyValues = null!;
#if !SWDS
				Warning($"Failed to load $include VMT file {includeFileName}\n");
#endif
				AssertMsg(false, $"Failed to load $include VMT file ({includeFileName})");
				return false;
			}

			nCount++;
		}

		baseKeyValuesOut = currentKeyValues;

		if (nCount >= 10)
			Warning("Infinite recursion in patch file?\n");

		return true;
	}

	private static void AccumulatePatchKeyValues(KeyValues srcKeyValues, KeyValues patchKeyValues) {
		KeyValues? destInsertSection = patchKeyValues.FindKey("insert");
		if (destInsertSection == null) {
			destInsertSection = new KeyValues("insert");
			patchKeyValues.AddSubKey(destInsertSection);
		}

		KeyValues? destReplaceSection = patchKeyValues.FindKey("replace");
		if (destReplaceSection == null) {
			destReplaceSection = new KeyValues("replace");
			patchKeyValues.AddSubKey(destReplaceSection);
		}

		KeyValues? srcInsertSection = srcKeyValues.FindKey("insert");
		if (srcInsertSection != null)
			MergeKeyValues(srcInsertSection, destInsertSection);

		KeyValues? srcReplaceSection = srcKeyValues.FindKey("replace");
		if (srcReplaceSection != null)
			MergeKeyValues(srcReplaceSection, destReplaceSection);
	}

	private static void MergeKeyValues(KeyValues srcKeys, KeyValues destKeys) {
		for (KeyValues? kv = srcKeys.GetFirstValue(); kv != null; kv = kv.GetNextValue()) {
			switch (kv.Type) {
				case KeyValues.Types.String:
					destKeys.SetString(kv.Name, kv.GetString());
					break;
				case KeyValues.Types.Int:
					destKeys.SetInt(kv.Name, kv.GetInt());
					break;
				case KeyValues.Types.Double:
					destKeys.SetFloat(kv.Name, kv.GetFloat());
					break;
				case KeyValues.Types.Pointer:
					destKeys.SetPtr(kv.Name, kv.GetPtr());
					break;
			}
		}
		for (KeyValues? kv = srcKeys.GetFirstTrueSubKey(); kv != null; kv = kv.GetNextTrueSubKey()) {
			KeyValues destKV = destKeys.FindKey(kv.Name, true)!;
			MergeKeyValues(kv, destKV);
		}
	}

	public bool PrecacheVars(KeyValues? inVmtKeyValues = null, KeyValues? inPatchKeyValues = null, List<FileNameHandle_t>? includes = null, MaterialFindContext findContext = 0) {
		if (IsPrecachedVars())
			return true;

		// How should we load VMT includes?
		// How should we allow async?

		bool ok = false;
		bool error = false;
		KeyValues? vmtKeyValues = null;
		KeyValues? patchKeyValues = null;

		if (keyValues != null) {
			vmtKeyValues = keyValues;
			patchKeyValues = new("vmt_patches");
		}
		else if (inVmtKeyValues != null) {
			vmtKeyValues = inVmtKeyValues;
			patchKeyValues = inPatchKeyValues;
		}
		else {
			VMTIncludes.Clear();

			vmtKeyValues = new KeyValues("vmt");
			patchKeyValues = new KeyValues("vmt_patches");
			if (!LoadVMTFile(materials.FileSystem, ref vmtKeyValues, patchKeyValues, GetName(), false, VMTIncludes)) {
				Warning($"CMaterial::PrecacheVars: error loading vmt file for {GetName()}\n");
				error = true;
			}
		}

		if (!error) {
			flags |= MaterialFlags.VarsIsPrecached;
			KeyValues? fallbackKeyValues = InitializeShader(vmtKeyValues!, patchKeyValues, findContext);
			if (fallbackKeyValues != null) {
				InitializeMaterialProxy(fallbackKeyValues);
				ok = true;
			}
		}

		return ok;
	}

	IMaterialProxy? ReplacementProxy;
	readonly List<IMaterialProxy> ProxyInfo = [];

	void DetermineProxyReplacements(KeyValues fallbackKeyValues) {
		ReplacementProxy = materials.DetermineProxyReplacements(this, fallbackKeyValues);
	}

	private void InitializeMaterialProxy(KeyValues fallbackKeyValues) {
		IMaterialProxyFactory materialProxyFactory = materials.GetMaterialProxyFactory();
		if (materialProxyFactory == null)
			return;

		DetermineProxyReplacements(fallbackKeyValues);

		if (ReplacementProxy != null)
			ProxyInfo.Add(ReplacementProxy);

		KeyValues? proxySection = fallbackKeyValues.FindKey("Proxies");
		if (proxySection != null) {
			KeyValues? proxyKey = proxySection.GetFirstSubKey();

			for (; proxyKey != null; proxyKey = proxyKey.GetNextKey()) {
				// Each of the proxies should themselves be databases
				IMaterialProxy? proxy = materialProxyFactory.CreateProxy(proxyKey.Name);
				if (proxy == null) {
					Warning($"Error: Material \"{GetName()}\" : proxy \"{proxyKey.Name}\" not found!\n");
					continue;
				}

				if (!proxy.Init(this, proxyKey)) {
					materialProxyFactory.DeleteProxy(proxy);
					Warning($"Error: Material \"{GetName()}\" : proxy \"{proxyKey.Name}\" unable to initialize!\n");
				}
				else {
					ProxyInfo.Add(proxy);
				}
			}
		}
	}

	private KeyValues? CheckConditionalFakeShaderName(ReadOnlySpan<char> shaderName, ReadOnlySpan<char> suffixName, KeyValues keyValues) {
		KeyValues? fallbackSection = keyValues.FindKey(suffixName);
		if (fallbackSection != null)
			return fallbackSection;

		Span<char> nameBuf = stackalloc char[256];
		int written = sprintf(nameBuf, "%s_%s").S(shaderName).S(suffixName);
		return keyValues.FindKey(nameBuf[..written]);
	}

	private KeyValues? FindBuiltinFallbackBlock(ReadOnlySpan<char> shaderName, KeyValues keyValues) {
		int dxSupportLevel = materials.HardwareConfig.GetDXSupportLevel();

		if (dxSupportLevel < 90)
			if (CheckConditionalFakeShaderName(shaderName, "<DX90", keyValues) is KeyValues ret)
				return ret;

		if (dxSupportLevel < 95)
			if (CheckConditionalFakeShaderName(shaderName, "<DX95", keyValues) is KeyValues ret)
				return ret;

		if (dxSupportLevel < 90 || !materials.HardwareConfig.SupportsPixelShaders_2_b())
			if (CheckConditionalFakeShaderName(shaderName, "<DX90_20b", keyValues) is KeyValues ret)
				return ret;

		if (dxSupportLevel >= 90 && materials.HardwareConfig.SupportsPixelShaders_2_b())
			if (CheckConditionalFakeShaderName(shaderName, ">=DX90_20b", keyValues) is KeyValues ret)
				return ret;

		if (dxSupportLevel <= 90)
			if (CheckConditionalFakeShaderName(shaderName, "<=DX90", keyValues) is KeyValues ret)
				return ret;

		if (dxSupportLevel >= 90)
			if (CheckConditionalFakeShaderName(shaderName, ">=DX90", keyValues) is KeyValues ret)
				return ret;

		if (dxSupportLevel > 90)
			if (CheckConditionalFakeShaderName(shaderName, ">DX90", keyValues) is KeyValues ret)
				return ret;

		if (materials.HardwareConfig.GetHDRType() != HDRType.None) {
			if (CheckConditionalFakeShaderName(shaderName, "hdr_dx9", keyValues) is KeyValues ret)
				return ret;
			if (CheckConditionalFakeShaderName(shaderName, "hdr", keyValues) is KeyValues ret2)
				return ret2;
		}
		else {
			if (CheckConditionalFakeShaderName(shaderName, "ldr", keyValues) is KeyValues ret)
				return ret;
		}

		if (materials.HardwareConfig.UsesSRGBCorrectBlending())
			if (CheckConditionalFakeShaderName(shaderName, "srgb", keyValues) is KeyValues ret)
				return ret;

		if (dxSupportLevel >= 90)
			if (CheckConditionalFakeShaderName(shaderName, "dx9", keyValues) is KeyValues ret)
				return ret;

		return null;
	}

	private KeyValues? InitializeShader(KeyValues keyValues, KeyValues? patchKeyValues, MaterialFindContext findContext) {
		KeyValues currentFallback = keyValues;
		KeyValues? fallbackSection = null;

		string? shaderName = currentFallback.Name;
		if (shaderName == null) {
			Warning($"Shader not specified in material {GetName()}\nUsing wireframe instead...\n");
			Assert(false);
			shaderName = MissingShaderName();
		}

		IShader? shader;
		IMaterialVar[] vars = new IMaterialVar[256];
		string fallbackShaderName = "";
		string fallbackMaterialName = "";
		bool modelDefault = false;
		int varCount;

		while (true) {
			shader = materials.ShaderSystem.FindShader(shaderName);
			if (shader == null) {
#if DEBUG
				DevWarning($"Error: Material \"{GetName()}\" uses unknown shader \"{shaderName}\"\n");
#else
				Warning($"Error: Material \"{GetName()}\" uses unknown shader \"{shaderName}\"\n");
#endif

				shaderName = MissingShaderName();
				shader = materials.ShaderSystem.FindShader(shaderName);
				if (shader == null)
					return null;
			}

			bool hasBuiltinFallbackBlock = false;
			if (fallbackSection == null) {
				fallbackSection = FindBuiltinFallbackBlock(shaderName, keyValues);
				if (fallbackSection != null) {
					hasBuiltinFallbackBlock = true;
					fallbackSection.ChainKeyValue(keyValues);
					currentFallback = fallbackSection;
				}
			}

			varCount = ParseMaterialVars(shader, keyValues, fallbackSection, modelDefault, vars, findContext);
			if (shader == null)
				break;

			materials.ShaderSystem.InitShaderParameters(shader, vars, GetName(), null);

			shaderName = shader.GetFallbackShader(vars);
			if (shaderName == null)
				break;

			if (true) { // Yeah, we support vertex and pixel shaders... do we even really need a flag for that....
				modelDefault = (vars[(int)ShaderMaterialVars.Flags].GetIntValue() & (int)MaterialVarFlags.Model) != 0;
			}

			if (!hasBuiltinFallbackBlock) {
				fallbackSection = keyValues.FindKey(shaderName);
				if (fallbackSection != null) {
					fallbackSection.ChainKeyValue(keyValues);
					currentFallback = fallbackSection;
				}
			}

			for (int i = 0; i < varCount; i++) {
				vars[i] = null;
			}
		}

		this.Shader = shader;
		this.ShaderParams = vars;
		this.VarCount = varCount;

		return currentFallback;
	}

	private int ParseMaterialVars(IShader shader, KeyValues keyValues, KeyValues? overrideKeyValues, bool modelDefault, IMaterialVar[] vars, MaterialFindContext findContext) {
		IMaterialVar? newVar;
		Span<bool> overrides = stackalloc bool[256];
		Span<bool> conditional = stackalloc bool[256];
		int overrideMask = 0;
		int flagMask = 0;

		int modelFlag = modelDefault ? (int)MaterialVarFlags.Model : 0;
		vars[(int)ShaderMaterialVars.Flags] = new MaterialVar(this, "$flags", modelFlag);
		vars[(int)ShaderMaterialVars.FlagsDefined] = new MaterialVar(this, "$flags_defined", modelFlag);
		vars[(int)ShaderMaterialVars.Flags2] = new MaterialVar(this, "flags2", 0);
		vars[(int)ShaderMaterialVars.FlagsDefined2] = new MaterialVar(this, "$flags_defined2", 0);

		int numParams = shader == null ? 0 : shader.GetNumParams();
		int varCount = numParams;

		bool parsingOverrides = overrideKeyValues != null;
		var var = overrideKeyValues != null ? overrideKeyValues.GetFirstSubKey() : keyValues.GetFirstSubKey();

		ReadOnlySpan<char> matName = var != null ? var.GetString() : "Unknown";
		while (var != null) {
			bool processThisOne = true;
			bool isConditionalVar;
			ReadOnlySpan<char> varName = GetVarName(var);

			if (findContext == MaterialFindContext.IsOnAModel && !varName.IsEmpty && varName.Length > 0) {
				if (varName.Contains("$ignorez", StringComparison.OrdinalIgnoreCase)) {
					Warning($"Ignoring material flag '{varName}' on material '{matName}'.\n");
					goto nextVar;
				}
			}

			if (
				ShouldSkipVar(var, out isConditionalVar) ||
				(var.Name[0] == '%') ||
				ParseMaterialFlag(var, vars[(int)ShaderMaterialVars.Flags], vars[(int)ShaderMaterialVars.FlagsDefined], parsingOverrides, ref flagMask, ref overrideMask) ||
				ParseMaterialFlag(var, vars[(int)ShaderMaterialVars.Flags2], vars[(int)ShaderMaterialVars.FlagsDefined2], parsingOverrides, ref flagMask, ref overrideMask)
				)
				processThisOne = false;

			if (processThisOne) {
				int varIdx = FindMaterialVar(shader, varName);
				if (varIdx >= 0) {
					if (vars[varIdx] != null && (!isConditionalVar)) {
						if (!overrides[varIdx] || parsingOverrides) {
							Warning($"Error! Variable \"{var.Name}\" is multiply defined in material \"{GetName()}\"!\n");
						}
						goto nextVar;
					}
				}
				else {
					int i;
					for (i = numParams; i < varCount; ++i) {
						Assert(vars[i] != null);
						if (vars[i].GetName().Equals(var.Name, StringComparison.OrdinalIgnoreCase))
							break;
					}

					if (i != varCount) {
						if (!overrides[i] || parsingOverrides) {
							Warning($"Error! Variable \"{var.Name}\" is multiply defined in material \"{GetName()}\"!\n");
						}
						goto nextVar;
					}
				}

				newVar = CreateMaterialVarFromKeyValue(this, var);
				if (newVar == null)
					goto nextVar;

				if (varIdx < 0)
					varIdx = varCount++;

				vars[varIdx] = newVar;
				if (parsingOverrides)
					overrides[varIdx] = true;

				conditional[varIdx] = isConditionalVar;
			}

		nextVar:
			var = var.GetNextKey();
			if (var == null && parsingOverrides) {
				var = keyValues.GetFirstSubKey();
				parsingOverrides = false;
			}
		}

		for (int i = 0; i < numParams; i++) {
			if (vars[i] == null)
				vars[i] = new MaterialVar(this, shader!.GetParamName(i));
		}

		return varCount;
	}

	private static IMaterialVar? CreateMaterialVarFromKeyValue(Material material, KeyValues keyValue) {
		ReadOnlySpan<char> name = GetVarName(keyValue);
		switch (keyValue.Type) {
			case KeyValues.Types.Int: return new MaterialVar(material, name, keyValue.GetInt());
			case KeyValues.Types.Double: return new MaterialVar(material, name, keyValue.GetFloat());
			case KeyValues.Types.String:
				ReadOnlySpan<char> str = keyValue.GetString();
				if (str.IsEmpty || str.Length == 0)
					return null;

				IMaterialVar? matrixVar = CreateMatrixVarFromKeyValue(material, keyValue);
				if (matrixVar != null) return matrixVar;

				if (!IsVector(str)) {
					// FIXME KeyValues is meant to handle this
					{
						if (int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i32))
							return new MaterialVar(material, name, i32);

						if (float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out float f32))
							return new MaterialVar(material, name, f32);
					}
					return new MaterialVar(material, name, str);
				}

				return CreateVectorMaterialVarFromKeyValue(material, keyValue);
		}

		return null;
	}

	private static IMaterialVar? CreateVectorMaterialVarFromKeyValue(Material material, KeyValues keyValue) {
		ReadOnlySpan<char> name = GetVarName(keyValue);
		Span<float> vecVal = stackalloc float[4];
		int dimensions = ParseVectorFromKeyValueString(keyValue, name, vecVal);
		if (dimensions == 0)
			return null;

		return new MaterialVar(material, name, vecVal[..dimensions]);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static bool IsEndline(char c) => c == '\n' || c == '\0';

	private static int ParseVectorFromKeyValueString(KeyValues keyValue, ReadOnlySpan<char> materialName, Span<float> vecVal) {
		ReadOnlySpan<char> scan = keyValue.GetString();
		bool divideBy255 = false;

		while (char.IsWhiteSpace(scan[0]))
			scan = scan[1..];

		if (scan[0] == '{')
			divideBy255 = true;
		else
			Assert(scan[0] == '[');

		scan = scan[1..];
		int i;
		for (i = 0; i < 4; i++) {
			// skip whitespace
			while (char.IsWhiteSpace(scan[0]))
				scan = scan[1..];

			if (IsEndline(scan[0]) || scan[0] == ']' || scan[0] == '}') {
				if (scan[0] != ']' && scan[0] != '}')
					Warning($"Warning in .VMT file ({materialName}): no ']' or '}}' found in vector key \"{keyValue.Name}\".\nDid you forget to surround the vector with \"s?\n");

				vecVal[i] = 0.0f;
				break;
			}
			vecVal[i] = strtof(scan, out ReadOnlySpan<char> end);

			if (scan == end) {
				Warning($"Error in .VMT file: error parsing vector element \"{keyValue.Name}\" in \"{materialName}\"\n");
				return 0;
			}

			scan = end;
		}

		if (divideBy255) {
			const float div = 1.0f / 255.0f;
			vecVal[0] *= div;
			vecVal[1] *= div;
			vecVal[2] *= div;
			vecVal[3] *= div;
		}

		return i;
	}

	private static IMaterialVar? CreateMatrixVarFromKeyValue(Material material, KeyValues keyValue) {
		ReadOnlySpan<char> scan = keyValue.GetString();
		ReadOnlySpan<char> name = GetVarName(keyValue);
		return null; // TODO: implement
	}

	private static bool IsVector(ReadOnlySpan<char> str) {
		while (char.IsWhiteSpace(str[0])) {
			str = str[1..];
			if (str.Length == 0 || str[0] == '\n')
				return false;
		}

		return str[0] == '[' || str[0] == '{';
	}

	private int FindMaterialVar(IShader? shader, ReadOnlySpan<char> varName) {
		if (shader == null)
			return -1;

		varName = varName.Trim();
		for (int i = shader.GetNumParams(); --i >= 0;) {
			ReadOnlySpan<char> paramName = shader.GetParamName(i);
			int foundIndex = varName.IndexOf(paramName, StringComparison.OrdinalIgnoreCase);
			if (foundIndex != 0)
				continue;

			int len = paramName.Length;
			int pFound = len;

			while (true) {
				if (pFound >= varName.Length)
					return i;

				if (!char.IsWhiteSpace(varName[pFound]))
					break;

				pFound++;
			}
		}

		return -1;
	}

	private bool ParseMaterialFlag(KeyValues parseValue, IMaterialVar flagVar, IMaterialVar flagDefinedVar, bool parsingOverrides, ref int flagMask, ref int overrideMask) {
		int flagbit = (int)FindMaterialVarFlag(GetVarName(parseValue));
		if (flagbit == 0)
			return false;

		MaterialVarFlags dbg = (MaterialVarFlags)flagbit;
		int testMask = parsingOverrides ? overrideMask : flagMask;
		if ((testMask & flagbit) != 0) {
			Warning($"Error! Flag \"{parseValue.Name}\" is multiply defined in material \"{GetName()}\"!\n");
			return true;
		}

		if ((overrideMask & flagbit) != 0)
			return true;

		if (parsingOverrides)
			overrideMask |= flagbit;
		else
			flagMask |= flagbit;

		if (parseValue.GetInt() != 0)
			flagVar.SetIntValue(flagVar.GetIntValue() | flagbit);
		else
			flagVar.SetIntValue(flagVar.GetIntValue() & (~flagbit));

		flagDefinedVar.SetIntValue(flagDefinedVar.GetIntValue() | flagbit);
		return true;
	}

	private MaterialVarFlags FindMaterialVarFlag(ReadOnlySpan<char> flagName) {
		flagName = flagName.Trim();

		for (int i = 0; !materials.ShaderSystem.ShaderStateString(i).IsEmpty; ++i) {
			ReadOnlySpan<char> stateString = materials.ShaderSystem.ShaderStateString(i);
			int foundIndex = flagName.IndexOf(stateString, StringComparison.OrdinalIgnoreCase);

			if (foundIndex != 0)
				continue;

			int nLen = stateString.Length;
			int pFound = nLen;

			while (true) {
				if (pFound >= flagName.Length)
					return (MaterialVarFlags)(1 << i);

				if (!char.IsWhiteSpace(flagName[pFound]))
					break;

				pFound++;
			}
		}

		return 0;
	}

	private bool ShouldSkipVar(KeyValues var, out bool isConditional) {
		ReadOnlySpan<char> varName = var.Name;
		int qPos = varName.IndexOf('?');
		ReadOnlySpan<char> question = qPos == -1 ? null : varName[(qPos + 1)..];
		if (question.IsEmpty || (question == varName)) {
			isConditional = false;                           // unconditional var
			return false;
		}
		else {
			bool shouldSkip = true;
			isConditional = true;

			// parse the conditional part
			ReadOnlySpan<char> cond = varName[..qPos];
			bool toggle = false;
			if (cond[0] == '!') {
				cond = cond[1..];
				toggle = true;
			}

			if (0 == stricmp(cond, "lowfill"))
				shouldSkip = !mat_reduceparticles.GetBool();
			else if (0 == stricmp(cond, "hdr"))
				shouldSkip = materials.HardwareConfig.GetHDRType() == HDRType.None;
			else if (0 == stricmp(cond, "srgb"))
				shouldSkip = !materials.HardwareConfig.UsesSRGBCorrectBlending();
			else if (0 == stricmp(cond, "ldr"))
				shouldSkip = materials.HardwareConfig.GetHDRType() != HDRType.None;
			else if (0 == stricmp(cond, "360"))
				shouldSkip = !IsX360();
			else if (0 == stricmp(cond, "gameconsole"))
				shouldSkip = !IsConsole();
			else
				Warning($"unrecognized conditional test {varName} in {GetName()}\n");

			return shouldSkip ^ toggle;
		}
	}

	public static ReadOnlySpan<char> GetVarName(KeyValues value) {
		ReadOnlySpan<char> name = value.Name;
		int qIndex = name.IndexOf('?');
		if (qIndex == -1)
			return name;

		return name[(qIndex + 1)..];
	}

	private string MissingShaderName() {
		return "Wireframe";
	}

	MaterialFlags flags;
	UtlSymbol name;
	string texGroupName;
	IShader? Shader;
	KeyValues? keyValues;
	IMaterialVar[]? ShaderParams;
	private int VarCount;
	ITextureInternal? representativeTexture;
	Vector3 Reflectivity;
	uint ChangeID;

	public Span<IMaterialVar> Vars => new Span<IMaterialVar>(ShaderParams)[..VarCount];

	// IMaterialProxy
	IShaderShadow ShaderRenderState;
	static uint DebugVarsSignature = 0;

	public void ReportVarChanged(IMaterialVar? var) => ChangeID++;

	public uint GetChangeID() => ChangeID;

	public void DrawMesh(VertexCompressionType vertexCompression) {
		if (Shader != null) {
			if ((GetMaterialVarFlags() & MaterialVarFlags.Debug) == 0) {
#pragma warning disable CS0168
				int x; // Debugging breakpoint.
#pragma warning restore CS0168
			}

			if ((GetMaterialVarFlags() & MaterialVarFlags.NoDraw) == 0) {
				ReadOnlySpan<char> name = Shader.GetName();
				materials.ShaderSystem.DrawElements(Shader, ShaderParams, ShaderRenderState, vertexCompression, ChangeID ^ DebugVarsSignature);
			}
		}
		else {
			Warning("Material.DrawMesh: No bound shader\n");
		}
	}
	public IShader? GetShader() => Shader;
	public string? GetShaderName() => Shader?.GetName();
	public void SetShader(ReadOnlySpan<char> shaderName) {

	}

	public bool IsRealTimeVersion() {
		return true;
	}

	public VertexFormat GetVertexFormat() {
		Precache();
		return ShaderRenderState.GetVertexFormat();
	}

	// This is not well understood. It relates to multithreading, and that's all I know right now.
	public IMaterialInternal GetRealTimeVersion() {
		return this;
	}

	public bool InMaterialPage() => false;
	public void GetMaterialOffset(Span<float> offset) {
		offset[0] = 0.0f;
		offset[1] = 0.0f;
	}
	public void GetMaterialScale(Span<float> scale) {
		scale[0] = 1.0f;
		scale[1] = 1.0f;
	}
	public IMaterial GetMaterialPage() => null;

	public float GetMappingWidth() {
		Precache();
		return MappingWidth;
	}

	public float GetMappingHeight() {
		Precache();
		return MappingHeight;
	}

	public void GetReflectivity(out Vector3 reflect) {
		Precache();
		reflect = Reflectivity;
	}

	public void Refresh() {
		if (materials.ShaderDevice.IsUsingGraphics()) {
			Uncache();
			Precache();
		}
	}

	public void ReloadTextures() {
		Precache();
		int paramCount = ShaderParamCount();
		IMaterialVar[]? vars = GetShaderParams();
		for (int i = 0; i < paramCount; i++) {
			if (vars![i] != null && vars[i].IsTexture()) {
				ITexture? texture = vars[i].GetTextureValue();
				if (!ITexture.IsError(texture))
					texture.Download();
			}
		}
	}

	public void RefreshPreservingMaterialVars() {
		if (materials.ShaderDevice.IsUsingGraphics()) {
			Uncache(true);
			Precache();
		}
	}

	public void Uncache(bool preserveVars = false) {
		if (IsPrecached()) {
			CleanUpStateSnapshots();
			flags &= ~MaterialFlags.ValidRenderState;
			flags &= ~MaterialFlags.IsPrecached;
		}

		if (!preserveVars) {
			if (IsPrecachedVars()) {
				CleanUpShaderParams();
				Shader = null;

				CleanUpMaterialProxy();

				flags &= ~MaterialFlags.VarsIsPrecached;
			}
		}
	}

	private void CleanUpMaterialProxy() {

	}

	private void CleanUpShaderParams() {

	}

	private void CleanUpStateSnapshots() {

	}

	int EnumerationID;
	public int GetEnumerationID() => EnumerationID;
	public void SetEnumerationID(int id) => EnumerationID = id;
	public bool GetPropertyFlag(MaterialPropertyTypes type) {
		Precache();

		if (!IsValidRenderState())
			return false;

		switch (type) {
			case MaterialPropertyTypes.NeedsLightmap:
				return IsUsingLightmap();

			case MaterialPropertyTypes.NeedsBumpedLightmaps:
				return IsUsingDiffuseBumpedLighting();
		}

		return false;
	}

	private bool IsUsingDiffuseBumpedLighting() {
		return (GetMaterialVarFlags2() & MaterialVarFlags2.LightingBumpedLightmap) != 0;
	}

	private bool IsUsingLightmap() {
		return (GetMaterialVarFlags2() & MaterialVarFlags2.LightingLightmap) != 0;
	}

	int MinLightmapPageID;
	int MaxLightmapPageID;

	public int GetMinLightmapPageID() => MinLightmapPageID;
	public int GetMaxLightmapPageID() => MaxLightmapPageID;
	public void SetMinLightmapPageID(int value) => MinLightmapPageID = value;
	public void SetMaxLightmapPageID(int value) => MaxLightmapPageID = value;
	public bool GetNeedsWhiteLightmap() => (flags & MaterialFlags.NeedsWhiteLightmap) != 0;
	public void SetNeedsWhiteLightmap(bool value) {
		if (value)
			flags |= MaterialFlags.NeedsWhiteLightmap;
		else
			flags &= ~MaterialFlags.NeedsWhiteLightmap;
	}

	public IMaterialVar[]? GetShaderParams() => ShaderParams;
	public int ShaderParamCount() => VarCount;

	int RefCount;

	public int GetReferenceCount() => RefCount;

	public void IncrementReferenceCount() => ++RefCount;

	public void DecrementReferenceCount() => --RefCount;

	public void DeleteIfUnreferenced() {
		if (RefCount > 0)
			return;
		// todo: this has more, but i'm not sure if its needed?
		materials.MaterialDict.RemoveMaterial(this);
	}

	public IMaterialVar? FindVarFast(ReadOnlySpan<char> varName, ref TokenCache token) {
		PrecacheVars();

		if (token.Cached) {
			if (token.VarIndex < VarCount && ShaderParams![token.VarIndex].GetNameAsSymbol() == token.Symbol)
				return ShaderParams![token.VarIndex];

			// FIXME: Could look for flags here too...
			if (!IMaterialVar.SymbolMatches(varName, token.Symbol))
				token.Symbol = IMaterialVar.FindSymbol(varName);
		}
		else {
			token.Cached = true;
			token.Symbol = IMaterialVar.FindSymbol(varName);
		}

		if (token.Symbol != UTL_INVAL_SYMBOL) {
			for (int i = VarCount; --i >= 0;) {
				if (ShaderParams![i].GetNameAsSymbol() == token.Symbol) {
					token.VarIndex = i;
					return ShaderParams[i];
				}
			}
		}

		return null;
	}

	public bool IsTranslucent() {
		Precache();
		if (VarCount > (int)ShaderMaterialVars.Alpha)
			return IsTranslucentInternal(ShaderParams != null ? ShaderParams[(int)ShaderMaterialVars.Alpha].GetFloatValue() : 0f);
		return false;
	}

	public bool IsVertexLit() {
		Precache();
		if (IsValidRenderState())
			return (GetMaterialVarFlags2() & MaterialVarFlags2.LightingVertexLit) != 0;
		return false;
	}

	public bool NeedsSoftwareSkinning() {
		Precache();
		Assert(Shader != null);
		if (Shader == null)
			return false;
		Assert(ShaderParams != null);
		return (GetMaterialVarFlags() & MaterialVarFlags.NeedsSoftwareSkinning) != 0;
	}

	public bool NeedsSoftwareLighting() {
		Precache();
		Assert(Shader != null);
		if (Shader == null)
			return false;
		Assert(ShaderParams != null);
		return (GetMaterialVarFlags2() & MaterialVarFlags2.NeedsSoftwareLighting) != 0;
	}

	public void SetMaterialVarFlags2(MaterialVarFlags2 flags, bool on) {
		if (ShaderParams == null) {
			Assert(false);
			return;
		}

		MaterialVarFlags2 val = on ? (GetMaterialVarFlags2() | flags) : (GetMaterialVarFlags2() & (~flags));
		ShaderParams[(int)ShaderMaterialVars.Flags2].SetIntValue((int)val);
		ShaderParams[(int)ShaderMaterialVars.FlagsDefined2].SetIntValue(ShaderParams[(int)ShaderMaterialVars.FlagsDefined2].GetIntValue() | (int)flags);
	}

	public void SetUseFixedFunctionBakedLighting(bool enable) {
		SetMaterialVarFlags2(MaterialVarFlags2.UseFixedFunctionBakedLighting, enable);
	}

	public int GetNumAnimationFrames() {
		Precache();
		if (representativeTexture != null) {
			return representativeTexture.GetNumAnimationFrames();
		}
		else {
			Warning($"Material.GetNumAnimationFrames: no representative texture for material {GetName()}\n");
			return 1;
		}
	}



	private bool IsTranslucentInternal(float alphaModulation) {
		if (Shader != null && IsValidRenderState()) {
			return IShaderSystem.IsTranslucent(ShaderRenderState) || (alphaModulation < 1.0f) || Shader.IsTranslucent(ShaderParams);
		}
		return false;
	}

	private bool IsValidRenderState() {
		return (flags & MaterialFlags.ValidRenderState) != 0;
	}

	public bool HasProxy() {
		PrecacheVars();
		return ProxyInfo.Count > 0;
	}

	public void CallBindProxy(object? proxydata) {
		if (ProxyInfo.Count != 0) {
			foreach (var proxy in ProxyInfo) {
				proxy.OnBind(proxydata);
			}
		}
	}

	public void SetMaterialVarFlags(MaterialVarFlags flags, bool on) {
		if (ShaderParams == null) {
			Assert(false);
			return;
		}

		MaterialVarFlags val = on ? (GetMaterialVarFlags() | flags) : (GetMaterialVarFlags() & (~flags));
		ShaderParams[(int)ShaderMaterialVars.Flags].SetIntValue((int)val);
		ShaderParams[(int)ShaderMaterialVars.FlagsDefined].SetIntValue(ShaderParams[(int)ShaderMaterialVars.FlagsDefined].GetIntValue() | (int)flags);
	}

	public void SetMaterialVarFlag(MaterialVarFlags flag, bool on) {
		bool oldOn = (GetMaterialVarFlags() & flag) != 0;
		if (oldOn != on) {
			SetMaterialVarFlags(flag, on);
			RecomputeStateSnapshots();
		}
	}

	public bool GetMaterialVarFlag(MaterialVarFlags flag) => (GetMaterialVarFlags() & flag) != 0;

	public bool IsTwoSided() {
		PrecacheVars();
		return GetMaterialVarFlag(MaterialVarFlags.NoCull);
	}

	public bool IsAlphaTested() {
		Precache();
		if (Shader != null && IsValidRenderState())
			return (ShaderRenderState.GetFlags() & ShaderFlags.OpacityAlphaTest) != 0 || GetMaterialVarFlag(MaterialVarFlags.AlphaTest);

		return false;
	}

	public bool UsesEnvCubemap() {
		Precache();

		if (Shader == null) {
			AssertMsg(false, $"Shader==NULL. Shader: {GetName()}");
			return false;
		}

		Assert(ShaderParams != null);
		return IsFlag2Set(ShaderParams, MaterialVarFlags2.UsesEnvCubemap);
	}

	public bool NeedsTangentSpace() {
		Precache();

		if (Shader == null) {
			AssertMsg(false, $"Shader==NULL. Shader: {GetName()}");
			return false;
		}

		Assert(ShaderParams != null);
		return IsFlag2Set(ShaderParams, MaterialVarFlags2.NeedsTangentSpaces);
	}

	public bool NeedsPowerOfTwoFrameBufferTexture(bool checkSpecificToThisFrame) {
		Precache();

		if (Shader == null) {
			AssertMsg(false, $"Shader==NULL. Shader: {GetName()}");
			return false;
		}

		return Shader.NeedsPowerOfTwoFrameBufferTexture(ShaderParams, checkSpecificToThisFrame);
	}

	public bool NeedsFullFrameBufferTexture(bool checkSpecificToThisFrame) {
		Precache();

		if (Shader == null) {
			AssertMsg(false, $"Shader==NULL. Shader: {GetName()}");
			return false;
		}

		return Shader.NeedsFullFrameBufferTexture(ShaderParams, checkSpecificToThisFrame);
	}

	public bool NeedsLightmapBlendAlpha() {
		Precache();
		return (GetMaterialVarFlags2() & MaterialVarFlags2.BlendWithLightmapAlpha) != 0;
	}
}
