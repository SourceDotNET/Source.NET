using Microsoft.Extensions.DependencyInjection;

using Source.Common.Filesystem;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;
using Source.MaterialSystem;

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Source.ShaderAPI.Gl46;

public interface IShaderSystemInternal : IShaderInit, IShaderSystem;

public readonly record struct ShaderCombo(string Name, int Min, int Range);

internal sealed class ShaderObject(int glType, string name, string defines, string typeName)
{
	public readonly int GLType = glType;
	public readonly string Name = name;
	public readonly string Defines = defines;
	public readonly string TypeName = typeName;
	public uint Handle;
	public ShaderCompileState State;
	public ShaderCompileJob? Job;
}

internal sealed class ShaderProgram
{
	public uint Handle;
	public ShaderCompileState State;
}

internal sealed class ShaderCompileJob
{
	public required ShaderObject Shader;
	public uint ShaderHandle;
	public uint Handle;
	public string? Error;
	public List<string>? SourceFiles;
	public string? CachePath;
	public bool FromCache;
	public bool LinkFailed;
	public bool Taken;
	public bool HighPriority;
}

internal sealed class ShaderSource(byte[] text, int versionLength, List<string> sourceFiles)
{
	public readonly byte[] Text = text;
	public readonly int VersionLength = versionLength;
	public readonly List<string> SourceFiles = sourceFiles;
	public readonly ulong Hash = text.AsSpan().Hash();
}

internal sealed record ShaderCompileBatch(ShaderCompileJob[] Jobs, nint Fence);

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
			renderState.SetFlags(renderState.GetFlags() | ShaderRenderStateFlags.OpacityTranslucent);
		else {
			if (ShaderAPI.IsAlphaTested(renderState))
				renderState.SetFlags(renderState.GetFlags() | ShaderRenderStateFlags.OpacityAlphaTest);
			else
				renderState.SetFlags(renderState.GetFlags() | ShaderRenderStateFlags.OpacityOpaque);
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

	private ShaderSource? BuildShaderSource(ReadOnlySpan<byte> source, string mainName) {
		string src = Encoding.ASCII.GetString(source);
		int versionEnd = src.IndexOf('\n');
		if (versionEnd < 0)
			return null;

		List<string> sourceFiles = [mainName];

		StringBuilder sb = new();
		sb.Append(src.AsSpan(0, versionEnd + 1));
		sb.Append("#line 2 0\n");

		ProcessSource(sb, src.AsSpan(versionEnd + 1), 0, 2, sourceFiles, 0);

		return new(Encoding.ASCII.GetBytes(sb.ToString()), versionEnd + 1, sourceFiles);
	}

	private static byte[] BuildDefines(ReadOnlySpan<char> defines) {
		int length = 0;
		foreach (Range range in MemoryExtensions.Split(defines, ';')) {
			ReadOnlySpan<char> define = defines[range].Trim();
			if (!define.IsEmpty)
				length += "#define "u8.Length + define.Length + 1;
		}

		byte[] bytes = new byte[length];
		Span<byte> dest = bytes;
		foreach (Range range in MemoryExtensions.Split(defines, ';')) {
			ReadOnlySpan<char> define = defines[range].Trim();
			if (define.IsEmpty)
				continue;
			"#define "u8.CopyTo(dest);
			dest = dest["#define "u8.Length..];
			dest = dest[Encoding.ASCII.GetBytes(define, dest)..];
			dest[0] = (byte)'\n';
			dest = dest[1..];
		}
		return bytes;
	}

	private void ProcessSource(StringBuilder sb, ReadOnlySpan<char> source, int fileIndex, int startLine, List<string> sourceFiles, int depth) {
		if (depth > 32) {
			sb.Append(source);
			return;
		}

		int lineNo = startLine;
		foreach (Range range in MemoryExtensions.Split(source, '\n')) {
			ReadOnlySpan<char> line = source[range];
			ReadOnlySpan<char> trimmed = line.TrimStart();
			if (trimmed.StartsWith("#include")) {
				int q1 = line.IndexOf('"');
				int q2 = q1 >= 0 ? line[(q1 + 1)..].IndexOf('"') : -1;
				if (q2 >= 0) {
					string includeName = new(line.Slice(q1 + 1, q2));
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

	private ShaderSource? LoadShaderSource(string name) {
		using IFileHandle? handle = FileSystem.Open($"shaders/{name}", FileOpenOptions.Read, "game");
		if (handle == null) {
			byte[]? vcs = LoadVcs(name);
			return vcs == null ? null : new(vcs, 0, [name]);
		}

		byte[] source = new byte[handle.Stream.Length];
		handle.Stream.ReadExactly(source);
		return BuildShaderSource(source, name);
	}

	private ShaderSource? GetShaderSource(string name) {
		if (!ShaderSources.TryGetValue(name, out ShaderSource? source)) {
			source = LoadShaderSource(name);
			ShaderSources[name] = source;
		}
		return source;
	}

	private string? GetCachePath(ShaderObject shader) {
		if (ProgramCacheDir == null)
			return null;

		ShaderSource? source = GetShaderSource(shader.Name);
		if (source == null)
			return null;

		ulong key = $"{source.Hash:x16};{shader.Defines}".AsSpan().Hash(false);
		return $"{ProgramCacheDir}/{shader.Name}_{key:x16}.bin";
	}

	private unsafe bool LoadCachedProgram(ShaderCompileJob job) {
		using IFileHandle? handle = FileSystem.Open(job.CachePath, FileOpenOptions.Read | FileOpenOptions.Binary, "DEFAULT_WRITE_PATH");
		if (handle == null || handle.Stream.Length <= sizeof(int))
			return false;

		byte[] data = new byte[handle.Stream.Length];
		handle.Stream.ReadExactly(data);

		job.Handle = glCreateProgram();
		glProgramParameteri(job.Handle, GL_PROGRAM_SEPARABLE, GL_TRUE);
		fixed (byte* pData = data)
			glProgramBinary(job.Handle, BinaryPrimitives.ReadInt32LittleEndian(data), pData + sizeof(int), data.Length - sizeof(int));
		job.FromCache = true;
		return true;
	}

	private unsafe void SaveCachedProgram(ShaderCompileJob job) {
		int length = 0;
		glGetProgramiv(job.Handle, GL_PROGRAM_BINARY_LENGTH, &length);
		if (length <= 0)
			return;

		byte[] data = new byte[sizeof(int) + length];
		int format = 0;
		fixed (byte* pData = data)
			glGetProgramBinary(job.Handle, length, &length, &format, pData + sizeof(int));
		BinaryPrimitives.WriteInt32LittleEndian(data, format);
		FileSystem.WriteFile(job.CachePath, "DEFAULT_WRITE_PATH", data.AsSpan(0, sizeof(int) + length));
	}

	private unsafe void CompileProgram(ShaderCompileJob job) {
		ShaderObject shader = job.Shader;
		ShaderSource? source = GetShaderSource(shader.Name);
		if (source == null)
			return;

		job.SourceFiles = source.SourceFiles;
		byte[] defines = source.VersionLength > 0 ? BuildDefines(shader.Defines) : [];

		job.ShaderHandle = glCreateShader(shader.GLType);
		fixed (byte* pText = source.Text)
		fixed (byte* pDefines = defines) {
			if (defines.Length == 0) {
				int length = source.Text.Length;
				glShaderSource(job.ShaderHandle, 1, &pText, &length);
			}
			else {
				byte** strings = stackalloc byte*[3];
				int* lengths = stackalloc int[3];
				strings[0] = pText;
				lengths[0] = source.VersionLength;
				strings[1] = pDefines;
				lengths[1] = defines.Length;
				strings[2] = pText + source.VersionLength;
				lengths[2] = source.Text.Length - source.VersionLength;
				glShaderSource(job.ShaderHandle, 3, strings, lengths);
			}
		}
		glCompileShader(job.ShaderHandle);

		job.Handle = glCreateProgram();
		glProgramParameteri(job.Handle, GL_PROGRAM_SEPARABLE, GL_TRUE);
		if (job.CachePath != null)
			glProgramParameteri(job.Handle, GL_PROGRAM_BINARY_RETRIEVABLE_HINT, GL_TRUE);
		glAttachShader(job.Handle, job.ShaderHandle);
		glLinkProgram(job.Handle);
	}

	private void SubmitCompileJob(ShaderCompileJob job) {
		job.CachePath = GetCachePath(job.Shader);
		if (job.CachePath != null && LoadCachedProgram(job))
			return;

		CompileProgram(job);
	}

	private unsafe void FinishCompileJob(ShaderCompileJob job) {
		if (job.Handle == 0)
			return;

		if (job.FromCache) {
			int status = 0;
			glGetProgramiv(job.Handle, GL_LINK_STATUS, &status);
			if (status == GL_TRUE)
				return;

			glDeleteProgram(job.Handle);
			job.Handle = 0;
			job.FromCache = false;
			CompileProgram(job);
			if (job.Handle == 0)
				return;
		}

		if (!IsValidShader(job.ShaderHandle, out job.Error)) {
			glDeleteProgram(job.Handle);
			job.Handle = 0;
			return;
		}

		glDetachShader(job.Handle, job.ShaderHandle);
		glDeleteShader(job.ShaderHandle);

		if (!IsValidProgram(job.Handle, out job.Error)) {
			job.LinkFailed = true;
			job.Handle = 0;
			return;
		}

		if (job.CachePath != null)
			SaveCachedProgram(job);
	}

	private static void ApplyCompileJob(ShaderCompileJob job) {
		ShaderObject shader = job.Shader;
		shader.Handle = job.Handle;
		shader.State = job.Handle != 0 ? ShaderCompileState.Ready : ShaderCompileState.Failed;
		shader.Job = null;

		if (job.LinkFailed) {
			Warning("WARNING: Shader combobulation linker error.\n");
			Warning(job.Error!);
			Warning("\n");
		}
		else if (job.Error != null) {
			Warning($"WARNING: {shader.TypeName} shader compilation error in {shader.Name}.\n");
			Warning(job.Error);
			Warning("\n");
			for (int i = 0; i < job.SourceFiles!.Count; i++)
				Warning($"  [source string {i}] = {job.SourceFiles[i]}\n");
			Warning("\n");
		}
		else if (job.Handle != 0 && !job.FromCache) {
			ReadOnlySpan<char> combos = shader.Defines.AsSpan().SliceNullTerminatedString();
			if (combos.IsEmpty)
				Msg($"Compiled shader: {shader.Name} ({shader.TypeName})\n");
			else
				Msg($"Compiled shader: {shader.Name} ({shader.TypeName}) [{combos}]\n");
		}
	}

	const int MaxCompileJobsPerBatch = 32;

	readonly List<ShaderObject> ShaderObjects = [];
	readonly Dictionary<ulong, ShaderProgram> ShaderPrograms = [];
	readonly Dictionary<string, ShaderSource?> ShaderSources = [];
	readonly HashSet<(ShaderType Type, string File, int StaticIndex)> StaticCombos = [];
	readonly List<(ShaderType Type, string File, int StaticIndex)> NewStaticCombos = [];
	string? ProgramCacheDir;
	readonly Queue<ShaderCompileJob> PendingCompileJobs = [];
	readonly Queue<ShaderCompileJob> HighPriorityCompileJobs = [];
	readonly ConcurrentQueue<ShaderCompileBatch> FinishedCompileBatches = [];
	readonly List<ShaderCompileBatch> FencedCompileBatches = [];
	IGraphicsContext? CompileContext;
	Thread? CompileThread;
	bool CompileWorkerStarted;
	int OutstandingCompileJobs;
	bool ParallelShaderCompile;

	private static unsafe bool HasGLExtension(ReadOnlySpan<byte> extension) {
		int count;
		glGetIntegerv(GL_NUM_EXTENSIONS, &count);
		for (uint i = 0; i < count; i++) {
			if (MemoryMarshal.CreateReadOnlySpanFromNullTerminated(glGetStringi(GL_EXTENSIONS, i)).SequenceEqual(extension))
				return true;
		}
		return false;
	}

	private static unsafe string GetGLString(int name) => Encoding.ASCII.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(glGetString(name)));

	private unsafe void StartCompileWorker() {
		CompileWorkerStarted = true;

		int binaryFormats = 0;
		glGetIntegerv(GL_NUM_PROGRAM_BINARY_FORMATS, &binaryFormats);
		if (binaryFormats > 0) {
			string driver = $"{GetGLString(GL_VENDOR)};{GetGLString(GL_RENDERER)};{GetGLString(GL_VERSION)}";
			ProgramCacheDir = $"cache/shaders/{driver.AsSpan().Hash(false):x16}";
			FileSystem.CreateDirHierarchy(ProgramCacheDir, "DEFAULT_WRITE_PATH");
		}
		else
			Warning("Program binaries unsupported, shader cache disabled\n");

		CompileContext = ShaderAPI.Device?.CreateSharedContext();
		if (CompileContext == null) {
			Warning("Shader compile worker unavailable, compiling shaders on the main thread\n");
			return;
		}

		ParallelShaderCompile = glMaxShaderCompilerThreadsKHRLoaded && (HasGLExtension("GL_KHR_parallel_shader_compile"u8) || HasGLExtension("GL_ARB_parallel_shader_compile"u8));
		CompileThread = new Thread(CompileWorker) { Name = "ShaderCompile", IsBackground = true };
		CompileThread.Start();
	}

	private unsafe void CompileWorker() {
		CompileContext!.MakeCurrent();
		if (ParallelShaderCompile)
			glMaxShaderCompilerThreadsKHR(2);

		List<ShaderCompileJob> jobs = [];
		while (true) {
			lock (PendingCompileJobs) {
				while (HighPriorityCompileJobs.Count == 0 && PendingCompileJobs.Count == 0)
					Monitor.Wait(PendingCompileJobs);
				TakeCompileJobs(HighPriorityCompileJobs, jobs);
				TakeCompileJobs(PendingCompileJobs, jobs);
			}

			if (jobs.Count == 0)
				continue;

			foreach (ShaderCompileJob job in jobs)
				SubmitCompileJob(job);
			foreach (ShaderCompileJob job in jobs)
				FinishCompileJob(job);

			ShaderCompileBatch batch = new([.. jobs], (nint)glFenceSync(GL_SYNC_GPU_COMMANDS_COMPLETE, 0));
			glFlush();
			FinishedCompileBatches.Enqueue(batch);
			jobs.Clear();
		}
	}

	private static void TakeCompileJobs(Queue<ShaderCompileJob> queue, List<ShaderCompileJob> jobs) {
		while (queue.Count > 0 && jobs.Count < MaxCompileJobsPerBatch) {
			ShaderCompileJob job = queue.Dequeue();
			if (job.Taken)
				continue;
			job.Taken = true;
			jobs.Add(job);
		}
	}

	private void QueueCompileJob(ShaderCompileJob job, bool highPriority) {
		if (!CompileWorkerStarted)
			StartCompileWorker();

		if (CompileContext == null) {
			SubmitCompileJob(job);
			FinishCompileJob(job);
			ApplyCompileJob(job);
			return;
		}

		++OutstandingCompileJobs;
		lock (PendingCompileJobs) {
			job.HighPriority = highPriority;
			(highPriority ? HighPriorityCompileJobs : PendingCompileJobs).Enqueue(job);
			Monitor.Pulse(PendingCompileJobs);
		}
	}

	internal void PrioritizeShader(nint handle) {
		if (handle < 0 || handle >= ShaderObjects.Count)
			return;

		ShaderObject shader = ShaderObjects[(int)handle];
		ShaderCompileJob? job = shader.Job;
		if (shader.State != ShaderCompileState.Pending || job == null || job.HighPriority)
			return;

		lock (PendingCompileJobs) {
			if (job.Taken)
				return;
			job.HighPriority = true;
			HighPriorityCompileJobs.Enqueue(job);
			Monitor.Pulse(PendingCompileJobs);
		}
	}

	public void BeginFrame() => ProcessFinishedCompileJobs();

	public void WaitForPendingCompiles(Action<int, int>? progress) {
		int total = OutstandingCompileJobs;
		while (OutstandingCompileJobs > 0) {
			ProcessFinishedCompileJobs();
			progress?.Invoke(total - OutstandingCompileJobs, total);
			if (OutstandingCompileJobs > 0)
				Thread.Sleep(1);
		}
	}

	private unsafe void ProcessFinishedCompileJobs() {
		while (FinishedCompileBatches.TryDequeue(out ShaderCompileBatch? batch))
			FencedCompileBatches.Add(batch);

		for (int i = 0; i < FencedCompileBatches.Count; i++) {
			ShaderCompileBatch batch = FencedCompileBatches[i];
			if (glClientWaitSync((void*)batch.Fence, 0, 0) == GL_TIMEOUT_EXPIRED)
				continue;

			glDeleteSync((void*)batch.Fence);
			foreach (ShaderCompileJob job in batch.Jobs)
				ApplyCompileJob(job);
			OutstandingCompileJobs -= batch.Jobs.Length;
			FencedCompileBatches.RemoveAt(i--);
		}
	}

	private nint QueueShader(int glType, ReadOnlySpan<char> name, ReadOnlySpan<char> defines, string typeName) {
		ShaderObject shader = new(glType, new(name), new(defines), typeName);
		int index = ShaderObjects.Count;
		ShaderObjects.Add(shader);
		shader.Job = new() { Shader = shader };
		QueueCompileJob(shader.Job, false);
		return index;
	}

	internal uint GetShaderProgram(nint handle) {
		if (handle < 0 || handle >= ShaderObjects.Count)
			return 0;
		return ShaderObjects[(int)handle].Handle;
	}

	internal unsafe uint GetProgram(VertexShaderHandle vertexShader, PixelShaderHandle pixelShader) {
		if (!vertexShader.IsValid() || !pixelShader.IsValid())
			return 0;

		ulong key = ((ulong)(uint)vertexShader.Handle << 32) | (uint)pixelShader.Handle;
		if (!ShaderPrograms.TryGetValue(key, out ShaderProgram? program)) {
			// Even invalid program states should be hashed... for now.
			// Maybe a time based thing for invalid programs, to try allowing for the shader developer to recover, etc...
			program = new();
			ShaderPrograms[key] = program;
		}

		if (program.State == ShaderCompileState.Pending) {
			ShaderObject vertex = ShaderObjects[(int)vertexShader.Handle];
			ShaderObject pixel = ShaderObjects[(int)pixelShader.Handle];
			if (vertex.State == ShaderCompileState.Failed || pixel.State == ShaderCompileState.Failed)
				program.State = ShaderCompileState.Failed;
			else if (vertex.State == ShaderCompileState.Ready && pixel.State == ShaderCompileState.Ready) {
				uint pipeline;
				glCreateProgramPipelines(1, &pipeline);
				glUseProgramStages(pipeline, (uint)GL_VERTEX_SHADER_BIT, vertex.Handle);
				glUseProgramStages(pipeline, (uint)GL_FRAGMENT_SHADER_BIT, pixel.Handle);
				program.Handle = pipeline;
				program.State = ShaderCompileState.Ready;
			}
			else {
				PrioritizeShader(vertexShader.Handle);
				PrioritizeShader(pixelShader.Handle);
			}
		}

		return program.Handle;
	}

	internal void RegisterStaticCombo(ShaderType type, string file, int staticIndex) {
		if (StaticCombos.Add((type, file, staticIndex)))
			NewStaticCombos.Add((type, file, staticIndex));
	}

	public void QueueAllShaderCombos() {
		foreach (var (type, file, staticIndex) in NewStaticCombos) {
			ShaderComboState combos = new(this, type);
			combos.Configure(file, staticIndex);
			bool vertex = type == ShaderType.Vertex;
			for (int i = 0; i < combos.DynamicComboCount; i++) {
				string defines = combos.GetVariantDefines(i);
				if (vertex)
					LoadVertexShader(file, defines);
				else
					LoadPixelShader(file, defines);
			}
		}

		NewStaticCombos.Clear();
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

			foreach (Range lineRange in MemoryExtensions.Split(source, '\n')) {
				ReadOnlySpan<char> line = source.AsSpan(lineRange).Trim();

				if (!line.StartsWith("//"))
					continue;
				ReadOnlySpan<char> body = line[2..].TrimStart();

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
				ReadOnlySpan<char> rest = line[(q1 + 1)..];
				int q2 = rest.IndexOf('"');
				if (q2 < 0)
					continue;
				string comboName = new(rest[..q2]);
				rest = rest[(q2 + 1)..];

				int min = 0, max = 1;
				int q3 = rest.IndexOf('"');
				if (q3 >= 0) {
					rest = rest[(q3 + 1)..];
					int q4 = rest.IndexOf('"');
					if (q4 >= 0) {
						ReadOnlySpan<char> range = rest[..q4];
						int dots = range.IndexOf("..");
						if (dots >= 0) {
							min = int.Parse(range[..dots]);
							max = int.Parse(range[(dots + 2)..]);
						}
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

		VertexShaderHandle vsh = new(QueueShader(GL_VERTEX_SHADER, name, defines, "Vertex"));
		vshs[symbol] = vsh;
		return vsh;
	}

	public PixelShaderHandle LoadPixelShader(ReadOnlySpan<char> name, ReadOnlySpan<char> defines = default) {
		ulong symbol = ComboSymbol(name, defines);
		if (pshs.TryGetValue(symbol, out PixelShaderHandle value))
			return value;

		PixelShaderHandle psh = new(QueueShader(GL_FRAGMENT_SHADER, name, defines, "Pixel"));
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
