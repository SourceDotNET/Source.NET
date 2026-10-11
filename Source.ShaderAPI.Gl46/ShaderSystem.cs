using Microsoft.Extensions.DependencyInjection;

using Source.Common.Filesystem;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;
using Source.MaterialSystem;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Source.ShaderAPI.Gl46;

public interface IShaderSystemInternal : IShaderInit, IShaderSystem;

public readonly record struct ShaderCombo(string Name, int Min, int Range);

public class ShaderSystem : IShaderSystemInternal
{
	List<IShaderDLL> ShaderDLLs = [];
	IShaderShadow? RenderState;
	private IMaterialSystem? _MaterialSystem;
	private ShaderAPIGl46? _ShaderAPI;
	private MaterialSystem_Config? _Config;

	private IMaterialSystem MaterialSystem => _MaterialSystem ??= Singleton<IMaterialSystem>();
	private ShaderAPIGl46 ShaderAPI => _ShaderAPI ??= (ShaderAPIGl46)Singleton<IShaderAPI>();
	private MaterialSystem_Config Config => _Config ??= Singleton<MaterialSystem_Config>();
	readonly ITextureManager TextureSystem = Singleton<ITextureManager>()!;

	public void BindTexture(Sampler sampler, ITexture texture, int frame) {
		if (texture == null) return;

		((ITextureInternal)texture).Bind(sampler, frame);
	}

	public void ResetShaderState() {

	}

	public void DrawElements(IShader shader, IMaterialVar[] parms, IShaderShadow renderState, VertexCompressionType vertexCompression, uint materialVarTimeStamp) {
		ShaderAPI.InvalidateDelayedShaderConstants();
		// Compute modulation...
		ShaderUsingFlags mod = shader.ComputeModulationFlags(parms, ShaderAPI);
		ShadowStateGl46 shadowState = (ShadowStateGl46)renderState;
		if (shadowState.ModulationFlags != mod) {
			shadowState.ModulationFlags = mod;
			InitState(shader, parms, ref renderState);
		}

		int materialVarFlags = parms[(int)ShaderMaterialVars.Flags].GetIntValue();
		if (((materialVarFlags & (int)MaterialVarFlags.Model) != 0) || (IsFlag2Set(parms, MaterialVarFlags2.SupportsHardwareSkinning) && (ShaderAPI.GetCurrentNumBones() > 0))) {
			ShaderAPI.SetSkinningMatrices();
		}

		if ((Config.ShowNormalMap || Config.ShowMipLevels == 2) && (IsFlag2Set(parms, MaterialVarFlags2.LightingBumpedLightmap) || IsFlag2Set(parms, MaterialVarFlags2.DiffuseBumpmappedModel))) {
			DrawNormalMap(shader, parms, vertexCompression);
		}
		else {
			if ((materialVarFlags & (uint)MaterialVarFlags.Flat) > 0)
				ShaderAPI.ShadeMode(ShadeMode.Flat);

			PrepForShaderDraw(shader, parms, renderState);

			ref BasePerMaterialContextData? contextData = ref ((ShadowStateGl46)renderState).ContextData;

			if (contextData != null && contextData.VarChangeID != materialVarTimeStamp) {
				contextData.MaterialVarsChanged = true;
				contextData.VarChangeID = materialVarTimeStamp;
			}

			shader.DrawElements(parms, null, ShaderAPI, vertexCompression, ref contextData);
			DoneWithShaderDraw();
		}
	}

	private void DrawNormalMap(IShader shader, Span<IMaterialVar> parms, VertexCompressionType vertexCompression) {
		throw new NotImplementedException();
	}

	public IShader? FindShader(ReadOnlySpan<char> shaderName) {
		foreach (var shaderDLL in ShaderDLLs) {
			foreach (var shader in shaderDLL.GetShaders()) {
				if (shaderName.Equals(shader.GetName(), StringComparison.OrdinalIgnoreCase))
					return shader;
			}
		}
		return null;
	}

	public IEnumerable<IShader> GetShaders() {
		foreach (var shaderDLL in ShaderDLLs) {
			foreach (var shader in shaderDLL.GetShaders())
				yield return shader;
		}
	}

	public bool LoadShaderDLL<T>(T shaderAPI) where T : IShaderDLL {
		ShaderDLLs.Add(shaderAPI);
		return true;
	}

	public ShaderSystem(IServiceProvider services, IFileSystem fileSystem) {
		Services = services;
		FileSystem = fileSystem;
	}
	public IServiceProvider Services;
	public void LoadAllShaderDLLs() {
		foreach (var dll in Services.GetServices<IShaderDLL>()) {
			LoadShaderDLL(dll);
		}
	}

	static string[] shaderStateStrings = [
		"$debug",
		"$no_fullbright",
		"$no_draw",
		"$use_in_fillrate_mode",

		"$vertexcolor",
		"$vertexalpha",
		"$selfillum",
		"$additive",
		"$alphatest",
		"$multipass",
		"$znearer",
		"$model",
		"$flat",
		"$nocull",
		"$nofog",
		"$ignorez",
		"$decal",
		"$envmapsphere",
		"$noalphamod",
		"$envmapcameraspace",
		"$basealphaenvmapmask",
		"$translucent",
		"$normalmapalphaenvmapmask",
		"$softwareskin",
		"$opaquetexture",
		"$envmapmode",
		"$nodecal",
		"$halflambert",
		"$wireframe",
		"$allowalphatocoverage",
		null
	];

	public ReadOnlySpan<char> ShaderStateString(int i) {
		return shaderStateStrings[i];
	}

	public void InitShaderParameters(IShader shader, IMaterialVar[] vars, ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroupName) {
		PrepForShaderDraw(shader, vars, null);
		shader.InitShaderParams(vars, ShaderAPI, materialName);
		DoneWithShaderDraw();

		if (!vars[(int)ShaderMaterialVars.Color].IsDefined())
			vars[(int)ShaderMaterialVars.Color].SetVecValue(1, 1, 1);

		if (!vars[(int)ShaderMaterialVars.Alpha].IsDefined())
			vars[(int)ShaderMaterialVars.Alpha].SetFloatValue(1);

		int i;
		for (i = shader.GetNumParams(); --i >= 0;) {
			if (vars[i].IsDefined())
				continue;
			ShaderParamType type = shader.GetParamType(i);
			switch (type) {
				case ShaderParamType.Texture:
					// Do nothing; we'll be loading in a string later
					break;
				case ShaderParamType.String:
					// Do nothing; we'll be loading in a string later
					break;
				case ShaderParamType.Material:
					vars[i].SetMaterialValue(null);
					break;
				case ShaderParamType.Bool:
				case ShaderParamType.Integer:
					vars[i].SetIntValue(0);
					break;
				case ShaderParamType.Color:
					vars[i].SetVecValue(1.0f, 1.0f, 1.0f);
					break;
				case ShaderParamType.Vec2:
					vars[i].SetVecValue(0.0f, 0.0f);
					break;
				case ShaderParamType.Vec3:
					vars[i].SetVecValue(0.0f, 0.0f, 0.0f);
					break;
				case ShaderParamType.Vec4:
					vars[i].SetVecValue(0.0f, 0.0f, 0.0f, 0.0f);
					break;
				case ShaderParamType.Float:
					vars[i].SetFloatValue(0);
					break;
				case ShaderParamType.FourCC:
					vars[i].SetFourCCValue(0, 0);
					break;
				case ShaderParamType.Matrix: {
						Matrix4x4 identity = Matrix4x4.Identity;
						vars[i].SetMatrixValue(identity);
					}
					break;
				case ShaderParamType.Matrix4x2: {
						Matrix4x4 identity = Matrix4x4.Identity;
						vars[i].SetMatrixValue(identity);
					}
					break;
				default:
					Dbg.Assert(false);
					break;
			}
		}
	}

	private void DoneWithShaderDraw() {
		RenderState = null;
	}

	private void PrepForShaderDraw(IShader shader, Span<IMaterialVar> vars, IShaderShadow renderState) {
		Assert(RenderState == null);
		// LATER; plug into spew?
		RenderState = renderState;
		renderState?.Activate(); // Activate the render state, this flushes out UBO's etc
	}

	public void InitShaderInstance(IShader shader, IMaterialVar[]? shaderParams, ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroupName) {
		PrepForShaderDraw(shader, shaderParams, null);
		shader.InitShaderInstance(shaderParams, ShaderAPI, this, materialName, textureGroupName);
		DoneWithShaderDraw();
	}

	public void LoadCubeMap(IMaterialVar[] parms, IMaterialVar textureVar, int additionalCreationFlags = 0) {
		if (!HardwareConfig.SupportsCubeMaps())
			return;

		if (textureVar.GetVarType() != MaterialVarType.String) {
			if (textureVar.GetVarType() != MaterialVarType.Texture)
				textureVar.SetTextureValue(TextureSystem.ErrorTexture());
			return;
		}

		if (stricmp(textureVar.GetStringValue(), "env_cubemap") == 0) {
			textureVar.SetTextureValue(ITextureInternal.EnvCubemap);
			SetFlags2(parms, MaterialVarFlags2.UsesEnvCubemap);
			return;
		}

		string textureName = textureVar.GetStringValue();
		if (HardwareConfig.GetHDRType() != HDRType.None)
			textureName += ".hdr";

		ITexture texture = MaterialSystem.FindTexture(textureName, TEXTURE_GROUP_CUBE_MAP, false, (CreateTextureFlags)additionalCreationFlags)
			?? TextureSystem.ErrorTexture();

		textureVar.SetTextureValue(texture);
	}

	public void LoadTexture(IMaterialVar textureVar, ReadOnlySpan<char> textureGroupName, int additionalCreationFlags = 0) {
		if (textureVar.GetVarType() != MaterialVarType.String) {
			if (textureVar.GetVarType() != MaterialVarType.Texture)
				textureVar.SetTextureValue(TextureSystem.ErrorTexture());
			return;
		}

		ReadOnlySpan<char> name = textureVar.GetStringValue();
		if (name[0] == Path.PathSeparator || name[1] == Path.PathSeparator)
			name = name[1..];

		ITexture texture = MaterialSystem.FindTexture(name, textureGroupName, false, (CreateTextureFlags)additionalCreationFlags);

		if (texture == null) {
			if (!ShaderDevice.IsUsingGraphics())
				Warning($"Shader_t::LoadTexture: texture \"{name}.vtf\" doesn't exist\n");
			texture = TextureSystem.ErrorTexture();
		}

		textureVar.SetTextureValue(texture);
	}

	public bool InitRenderState(IShader shader, IMaterialVar[] shaderParams, ref IShaderShadow renderState, ReadOnlySpan<char> materialName) {
		Assert(RenderState == null);
		InitRenderStateFlags(ref renderState, shaderParams);
		((ShadowStateGl46)renderState).ModulationFlags = shader.ComputeModulationFlags(shaderParams, ShaderAPI);
		InitState(shader, shaderParams, ref renderState);
		ComputeRenderStateFlagsFromSnapshot(renderState);
		return true;
	}

	private void ComputeRenderStateFlagsFromSnapshot(IShaderShadow renderState) {
		if (ShaderAPI.IsTranslucent(renderState))
			renderState.SetFlags(renderState.GetFlags() | ShaderFlags.OpacityTranslucent);
		else {
			if (ShaderAPI.IsAlphaTested(renderState))
				renderState.SetFlags(renderState.GetFlags() | ShaderFlags.OpacityAlphaTest);
			else
				renderState.SetFlags(renderState.GetFlags() | ShaderFlags.OpacityOpaque);
		}
	}

	private void InitState(IShader shader, IMaterialVar[] shaderParams, ref IShaderShadow renderState) {
		PrepForShaderDraw(shader, shaderParams, renderState);
		shader.DrawElements(shaderParams, renderState, null, VertexCompressionType.None, ref ((ShadowStateGl46)renderState).ContextData);
		DoneWithShaderDraw();
	}

	private void InitRenderStateFlags(ref IShaderShadow renderState, IMaterialVar[] shaderParams) {

	}

	public void Draw(bool makeActualDrawCall = true) {
		Assert(RenderState);

		if (makeActualDrawCall)
			ShaderAPI.RenderPass();

		ShaderAPI.InvalidateDelayedShaderConstants();
	}

	internal void BindVertexShader(in VertexShaderHandle vertexShader) {

	}

	internal void BindPixelShader(in PixelShaderHandle pixelShader) {

	}

	internal void SetVertexShaderState(int index) {

	}

	internal void SetPixelShaderState(int index) {

	}

	IFileSystem FileSystem;
	IShaderDevice? _ShaderDevice;
	IShaderDevice? ShaderDevice => _ShaderDevice ??= Singleton<IShaderDevice>();
	IMaterialSystemHardwareConfig? _HardwareConfig;
	IMaterialSystemHardwareConfig HardwareConfig => _HardwareConfig ??= Singleton<IMaterialSystemHardwareConfig>();

	public void Init() {

	}

	Dictionary<ulong, VertexShaderHandle> vshs = [];
	Dictionary<ulong, PixelShaderHandle> pshs = [];

	internal static unsafe bool IsValidShader(uint shader, [NotNullWhen(false)] out string? error) {
		int status = 0;
		glGetShaderiv(shader, GL_COMPILE_STATUS, &status);
		if (status != GL_TRUE) {
			int logLength = 0;
			glGetShaderiv(shader, GL_INFO_LOG_LENGTH, &logLength);
			if (logLength > 0) {
				byte[] infoLog = new byte[logLength];
				fixed (byte* infoPtr = infoLog) {
					glGetShaderInfoLog(shader, logLength, null, infoPtr);
				}
				error = Encoding.ASCII.GetString(infoLog);
			}
			else
				error = "UNKNOWN FAILURE!!!";

			glDeleteShader(shader);
			return false;
		}

		error = null;
		return true;
	}

	internal static unsafe bool IsValidProgram(uint program, [NotNullWhen(false)] out string? error) {
		int status = 0;
		glGetProgramiv(program, GL_LINK_STATUS, &status);
		if (status != GL_TRUE) {
			int logLength = 0;
			glGetProgramiv(program, GL_INFO_LOG_LENGTH, &logLength);
			if (logLength > 0) {
				byte[] infoLog = new byte[logLength];
				fixed (byte* infoPtr = infoLog) {
					glGetProgramInfoLog(program, logLength, null, infoPtr);
				}
				error = Encoding.ASCII.GetString(infoLog);
			}
			else
				error = "UNKNOWN FAILURE";

			glDeleteProgram(program);
			return false;
		}

		error = null;
		return true;
	}

	private byte[]? BuildShaderSource(ReadOnlySpan<byte> source, ReadOnlySpan<char> defines, string mainName, List<string> sourceFiles) {
		string src = Encoding.ASCII.GetString(source);
		int versionEnd = src.IndexOf('\n');
		if (versionEnd < 0)
			return null;

		sourceFiles.Add(mainName);

		StringBuilder sb = new();
		sb.Append(src.AsSpan(0, versionEnd + 1));

		ReadOnlySpan<char> rest = defines;
		while (!rest.IsEmpty) {
			int sep = rest.IndexOf(';');
			ReadOnlySpan<char> define = (sep < 0 ? rest : rest[..sep]).Trim();
			rest = sep < 0 ? default : rest[(sep + 1)..];
			if (define.IsEmpty)
				continue;
			sb.Append("#define ");
			sb.Append(define);
			sb.Append('\n');
		}

		sb.Append("#line 2 0\n");

		ProcessSource(sb, src[(versionEnd + 1)..], 0, 2, sourceFiles, 0);

		return Encoding.ASCII.GetBytes(sb.ToString());
	}

	private void ProcessSource(StringBuilder sb, string source, int fileIndex, int startLine, List<string> sourceFiles, int depth) {
		if (depth > 32) {
			sb.Append(source);
			return;
		}

		int lineNo = startLine;
		foreach (string line in source.Split('\n')) {
			ReadOnlySpan<char> trimmed = line.AsSpan().TrimStart();
			if (trimmed.StartsWith("#include")) {
				int q1 = line.IndexOf('"');
				int q2 = q1 >= 0 ? line.IndexOf('"', q1 + 1) : -1;
				if (q1 >= 0 && q2 > q1) {
					string includeName = line[(q1 + 1)..q2];
					using IFileHandle? includeHandle = FileSystem.Open($"shaders/{includeName}", FileOpenOptions.Read, "game");
					if (includeHandle != null) {
						byte[] bytes = new byte[includeHandle.Stream.Length];
						includeHandle.Stream.ReadExactly(bytes);

						int includeIndex = sourceFiles.Count;
						sourceFiles.Add(includeName);

						sb.Append("#line 1 ").Append(includeIndex).Append('\n');
						ProcessSource(sb, Encoding.ASCII.GetString(bytes), includeIndex, 1, sourceFiles, depth + 1);
						sb.Append("#line ").Append(lineNo + 1).Append(' ').Append(fileIndex).Append('\n');
						++lineNo;
						continue;
					}

					Warning($"Shader include not found: {includeName}\n");
				}
			}

			sb.Append(line);
			sb.Append('\n');
			++lineNo;
		}
	}

	private byte[]? LoadVcs(ReadOnlySpan<char> name) {
		int ext = name.LastIndexOf("_gl");
		if (ext < 0)
			return null;

		using IFileHandle? handle = FileSystem.Open($"shaders/fxc/{name[..ext]}.vcs", FileOpenOptions.Read, "game");
		if (handle == null)
			return null;

		byte[] vcs = new byte[handle.Stream.Length];
		handle.Stream.ReadExactly(vcs);
		string? source = VcsTranslator.Load(vcs);
		if (source == null) {
			Warning($"WARNING: Failed to translate shader shaders/fxc/{name[..ext]}.vcs\n");
			return null;
		}
		return Encoding.ASCII.GetBytes(source);
	}

	private unsafe uint CompileShader(int glType, ReadOnlySpan<char> name, ReadOnlySpan<char> defines, string typeName) {
		using IFileHandle? handle = FileSystem.Open($"shaders/{name}", FileOpenOptions.Read, "game");
		List<string> sourceFiles = [];
		byte[]? built;
		if (handle == null) {
			if ((built = LoadVcs(name)) == null)
				return 0;
			sourceFiles.Add(new(name));
		}
		else {
			Span<byte> source = stackalloc byte[(int)handle.Stream.Length];
			handle.Stream.Read(source);

			built = BuildShaderSource(source, defines, new string(name), sourceFiles);
			if (built == null)
				return 0;
		}

		uint pShader = glCreateShader(glType);
		int len = built.Length;
		fixed (byte* pSrc = built)
			glShaderSource(pShader, 1, &pSrc, &len);
		glCompileShader(pShader);

		if (!IsValidShader(pShader, out string? error)) {
			Warning($"WARNING: {typeName} shader compilation error in {name}.\n");
			Warning(error);
			Warning("\n");
			for (int i = 0; i < sourceFiles.Count; i++)
				Warning($"  [source string {i}] = {sourceFiles[i]}\n");
			Warning("\n");
			return 0;
		}
		else {
			ReadOnlySpan<char> combos = defines.SliceNullTerminatedString();
			if (combos.IsEmpty)
				Msg($"Compiled shader: {name} ({typeName})\n");
			else
				Msg($"Compiled shader: {name} ({typeName}) [{combos}]\n");
		}

		return pShader;
	}

	Dictionary<string, (List<ShaderCombo> Static, List<ShaderCombo> Dynamic)> comboCache = [];

	internal (List<ShaderCombo> Static, List<ShaderCombo> Dynamic) GetShaderCombos(ReadOnlySpan<char> name) {
		string key = new(name);
		if (comboCache.TryGetValue(key, out var cached))
			return cached;

		List<ShaderCombo> statics = [];
		List<ShaderCombo> dynamics = [];

		using IFileHandle? handle = FileSystem.Open($"shaders/{key}", FileOpenOptions.Read, "game");
		if (handle != null) {
			byte[] bytes = new byte[handle.Stream.Length];
			handle.Stream.ReadExactly(bytes);
			string source = Encoding.ASCII.GetString(bytes);

			foreach (string rawLine in source.Split('\n')) {
				string line = rawLine.Trim();

				if (!line.StartsWith("//"))
					continue;
				ReadOnlySpan<char> body = line.AsSpan(2).TrimStart();

				List<ShaderCombo>? list = null;
				if (body.StartsWith("STATIC:"))
					list = statics;
				else if (body.StartsWith("DYNAMIC:"))
					list = dynamics;
				if (list == null)
					continue;

				int q1 = line.IndexOf('"');
				if (q1 < 0)
					continue;
				int q2 = line.IndexOf('"', q1 + 1);
				if (q2 < 0)
					continue;
				string comboName = line[(q1 + 1)..q2];

				int min = 0, max = 1;
				int q3 = line.IndexOf('"', q2 + 1);
				int q4 = q3 < 0 ? -1 : line.IndexOf('"', q3 + 1);
				if (q4 >= 0) {
					ReadOnlySpan<char> range = line[(q3 + 1)..q4];
					int dots = range.IndexOf("..");
					if (dots >= 0) {
						min = int.Parse(range[..dots]);
						max = int.Parse(range[(dots + 2)..]);
					}
				}

				list.Add(new(comboName, min, max - min + 1));
			}
		}

		var result = (statics, dynamics);
		comboCache[key] = result;
		return result;
	}

	private static ulong ComboSymbol(ReadOnlySpan<char> name, ReadOnlySpan<char> defines) {
		ulong symbol = name.Hash();
		if (!defines.IsEmpty)
			symbol ^= defines.Hash();
		return symbol;
	}

	public VertexShaderHandle LoadVertexShader(ReadOnlySpan<char> name, ReadOnlySpan<char> defines = default) {
		ulong symbol = ComboSymbol(name, defines);
		if (vshs.TryGetValue(symbol, out VertexShaderHandle value))
			return value;

		uint pShader = CompileShader(GL_VERTEX_SHADER, name, defines, "Vertex");
		if (pShader == 0)
			return VertexShaderHandle.INVALID;

		VertexShaderHandle vsh = new((nint)pShader);
		vshs[symbol] = vsh;
		return vsh;
	}

	public PixelShaderHandle LoadPixelShader(ReadOnlySpan<char> name, ReadOnlySpan<char> defines = default) {
		ulong symbol = ComboSymbol(name, defines);
		if (pshs.TryGetValue(symbol, out PixelShaderHandle value))
			return value;

		uint pShader = CompileShader(GL_FRAGMENT_SHADER, name, defines, "Pixel");
		if (pShader == 0)
			return PixelShaderHandle.INVALID;

		PixelShaderHandle psh = new((nint)pShader);
		pshs[symbol] = psh;
		return psh;
	}

	public int GetShaderAPITextureBindHandle(ITexture? texture, int frame, int textureChannel) {
		Assert(!ITextureInternal.IsTextureInternalEnvCubemap((ITextureInternal?)texture));
		if (texture != null) {
			ITextureInternal tex = (ITextureInternal)texture;
			// RequestAllMipmaps todo

			return tex.GetTextureHandle(frame, textureChannel);
		}
		else
			return INVALID_SHADERAPI_TEXTURE_HANDLE;
	}

	public void LoadBumpMap(IMaterialVar textureVar, string? textureGroupName) {
		Assert(textureVar);

		if (textureVar.GetVarType() != MaterialVarType.String) {
			if (textureVar.GetVarType() != MaterialVarType.Texture)
				textureVar.SetTextureValue(TextureSystem.ErrorTexture());
			return;
		}

		ITexture? text = MaterialSystem.FindTexture(textureVar.GetStringValue(), textureGroupName, false, 0);
		text ??= TextureSystem.ErrorTexture();

		textureVar.SetTextureValue(text);
	}
}
