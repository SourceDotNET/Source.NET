using Source.Common;

using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Source.ShaderAPI.Gl46;

enum ShaderOpcode
{
	Nop = 0,
	Mov,
	Add,
	Sub,
	Mad,
	Mul,
	Rcp,
	Rsq,
	Dp3,
	Dp4,
	Min,
	Max,
	Slt,
	Sge,
	Exp,
	Log,
	Lit,
	Dst,
	Lrp,
	Frc,
	M4x4,
	M4x3,
	M3x4,
	M3x3,
	M3x2,
	Dcl = 31,
	Pow,
	Crs,
	Sgn,
	Abs,
	Nrm,
	SinCos,
	Ifc = 41,
	Else,
	EndIf,
	TexKill = 65,
	Tex,
	Def = 81,
	Cmp = 88,
	Dp2Add = 90,
	Dsx,
	Dsy,
	TexLdd,
	TexLdl = 95,
	Phase = 0xFFFD,
	Comment = 0xFFFE,
	End = 0xFFFF
}

enum RegType
{
	Temp,
	Input,
	Const,
	Texture,
	RastOut,
	AttrOut,
	Output,
	ConstInt,
	ColorOut,
	DepthOut,
	Sampler,
	MiscType = 17
}

enum SrcMod
{
	None,
	Neg,
	Bias,
	BiasNeg,
	Sign,
	SignNeg,
	Comp,
	X2,
	X2Neg,
	Abs = 11,
	AbsNeg
}

enum DeclUsage
{
	Position = 0,
	Normal = 3,
	TexCoord = 5,
	Color = 10
}

enum ShaderComparison
{
	Gt = 1,
	Eq,
	Ge,
	Lt,
	Ne,
	Le
}

enum TexLoadControl
{
	None,
	Project,
	Bias
}

enum SamplerTextureType
{
	Texture2D = 2,
	Cube,
	Volume
}

internal sealed class VcsTranslator
{
	const string Xyzw = "xyzw";
	static readonly string Temps = string.Concat(Enumerable.Range(0, 32).Select(i => $", r{i} = vec4(0.0)"));

	const int ShaderVcsVersion = 6;
	const int MaxShaderUnpackedBlockSize = 1 << 17;
	const uint BlockCompressionMask = 0xC0000000;
	const uint BlockSizeMask = 0x3FFFFFFF;
	const uint BlockUncompressed = 0x80000000;
	const uint BlockLzma = 0x40000000;
	const uint EndOfBlocks = uint.MaxValue;

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	struct ShaderHeader
	{
		public int Version;
		public int TotalCombos;
		public int DynamicCombos;
		public uint Flags;
		public uint CentroidMask;
		public uint NumStaticCombos;
		public uint SourceCRC32;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	struct StaticComboRecord
	{
		public uint StaticComboID;
		public uint FileOffset;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	struct StaticComboAliasRecord
	{
		public uint StaticComboID;
		public uint SourceStaticCombo;
	}

	static uint NextULONG(ReadOnlySpan<byte> data, ref int offset) {
		uint value = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
		offset += sizeof(uint);
		return value;
	}

	public static string? Load(ReadOnlySpan<byte> vcs) {
		if (vcs.Length < Unsafe.SizeOf<ShaderHeader>())
			return null;
		ShaderHeader header = MemoryMarshal.Read<ShaderHeader>(vcs);
		if (header.Version != ShaderVcsVersion)
			return null;

		int offset = Unsafe.SizeOf<ShaderHeader>();
		ReadOnlySpan<StaticComboRecord> records = MemoryMarshal.Cast<byte, StaticComboRecord>(vcs.Slice(offset, (int)header.NumStaticCombos * Unsafe.SizeOf<StaticComboRecord>()));
		offset += records.Length * Unsafe.SizeOf<StaticComboRecord>();

		int numDups = (int)NextULONG(vcs, ref offset);
		ReadOnlySpan<StaticComboAliasRecord> dups = MemoryMarshal.Cast<byte, StaticComboAliasRecord>(vcs.Slice(offset, numDups * Unsafe.SizeOf<StaticComboAliasRecord>()));

		uint staticCombo = 0;
		foreach (ref readonly StaticComboAliasRecord dup in dups)
			if (dup.StaticComboID == 0)
				staticCombo = dup.SourceStaticCombo;

		offset = -1;
		foreach (ref readonly StaticComboRecord record in records)
			if (record.StaticComboID == staticCombo) {
				offset = (int)record.FileOffset;
				break;
			}
		if (offset < 0)
			return null;

		byte[] unpack = ArrayPool<byte>.Shared.Rent(MaxShaderUnpackedBlockSize);
		try {
			for (uint blockSize; (blockSize = NextULONG(vcs, ref offset)) != EndOfBlocks;) {
				int packedSize = (int)(blockSize & BlockSizeMask);
				ReadOnlySpan<byte> block;
				switch (blockSize & BlockCompressionMask) {
					case BlockUncompressed:
						block = vcs.Slice(offset, packedSize);
						break;
					case BlockLzma:
						block = unpack.AsSpan(0, (int)LZMA.Uncompress(vcs[offset..], unpack));
						break;
					default:
						return null;
				}
				offset += packedSize;

				for (int readPtr = 0; readPtr < block.Length;) {
					uint comboID = NextULONG(block, ref readPtr);
					int shaderSize = (int)NextULONG(block, ref readPtr);
					if (comboID == 0)
						return new VcsTranslator().Translate(MemoryMarshal.Cast<byte, uint>(block.Slice(readPtr, shaderSize)));
					readPtr += shaderSize;
				}
			}
			return null;
		}
		finally {
			ArrayPool<byte>.Shared.Return(unpack);
		}
	}

	bool pixel;
	InlineArray256<bool> defs;
	InlineArray16<SamplerTextureType> samplers;
	InlineArray16<string?> inNames;
	InlineArray16<string?> outNames;
	readonly SortedSet<string> io = [];

	const uint PixelShaderVersionType = 0xFFFF;
	const uint VertexShaderVersionType = 0xFFFE;
	const uint OpcodeMask = 0xFFFF;
	const uint RegNumMask = 0x7FF;

	static RegType GetRegType(uint t) => (RegType)(((t >> 28) & 7) | ((t >> 8) & 0x18));
	static int RegNum(uint t) => (int)(t & RegNumMask);
	static int Swizzle(uint t, int component) => (int)((t >> (16 + component * 2)) & 3);
	static SrcMod GetSrcMod(uint t) => (SrcMod)((t >> 24) & 0xF);
	static uint WriteMask(uint t) => (t >> 16) & 0xF;
	static bool Saturate(uint t) => ((t >> 20) & 1) != 0;
	static uint OpcodeControl(uint tok) => (tok >> 16) & 0xFF;
	static int InstructionLength(uint tok) => (int)((tok >> 24) & 0xF);
	static int CommentLength(uint tok) => (int)((tok >> 16) & 0x7FFF);
	static DeclUsage GetDeclUsage(uint t) => (DeclUsage)(t & 0x1F);
	static int UsageIndex(uint t) => (int)((t >> 16) & 0xF);
	static SamplerTextureType GetSamplerType(uint t) => (SamplerTextureType)((t >> 27) & 0xF);

	string Use(string decl, string name) {
		io.Add($"{decl} vec4 {name};");
		return name;
	}

	string? Reg(uint t) {
		int n = RegNum(t);
		switch (GetRegType(t)) {
			case RegType.Temp: return $"r{n}";
			case RegType.Input: return inNames[n & 15] ?? (pixel ? Use("in", $"vs_Color{n}") : $"a{n}");
			case RegType.Const:
				if (defs[n])
					return $"c{n}";
				if (pixel)
					return $"ps_const[{n}]";
				return n switch {
					0 => "vec4(0.0, 1.0, 2.0, 0.5)",
					1 => "vec4(1.0 / 2.2, 2.2, 0.0, 0.0)",
					>= 4 and < 8 => $"mvpT[{n - 4}]",
					>= 8 and < 12 => $"vpT[{n - 8}]",
					12 => "mvpT[2]",
					13 => "vpT[2]",
					_ => $"vs_const[{n}]"
				};
			case RegType.Texture: return pixel ? Use("in", $"vs_TexCoord{n}") : null;
			case RegType.RastOut: return n == 0 ? "oPos" : n == 1 ? "oFog" : "oPts";
			case RegType.AttrOut: return Use("out", $"vs_Color{n}");
			case RegType.Output: return outNames[n & 15] ?? Use("out", $"vs_TexCoord{n}");
			case RegType.ColorOut: return $"oC{n}";
			case RegType.DepthOut: return "gl_FragDepth";
			case RegType.Sampler: return $"s{n}";
			case RegType.MiscType: return n == 0 ? "gl_FragCoord" : "vFace";
			default: return null;
		}
	}

	string Src(uint t) {
		string r = $"{Reg(t)}.{Xyzw[Swizzle(t, 0)]}{Xyzw[Swizzle(t, 1)]}{Xyzw[Swizzle(t, 2)]}{Xyzw[Swizzle(t, 3)]}";
		return GetSrcMod(t) switch {
			SrcMod.Neg => $"(-{r})",
			SrcMod.Bias => $"({r} - 0.5)",
			SrcMod.BiasNeg => $"(0.5 - {r})",
			SrcMod.Sign => $"({r} * 2.0 - 1.0)",
			SrcMod.SignNeg => $"(1.0 - {r} * 2.0)",
			SrcMod.Comp => $"(1.0 - {r})",
			SrcMod.X2 => $"({r} * 2.0)",
			SrcMod.X2Neg => $"({r} * -2.0)",
			SrcMod.Abs => $"abs({r})",
			SrcMod.AbsNeg => $"(-abs({r}))",
			_ => r
		};
	}

	string Rows(uint vec, uint matrix, int rows, bool dot3) {
		StringBuilder sb = new("vec4(");
		string v = Src(vec);
		for (int i = 0; i < 4; i++) {
			if (i > 0)
				sb.Append(", ");
			if (i >= rows)
				sb.Append("0.0");
			else if (dot3)
				sb.Append($"dot({v}.xyz, {Src(matrix + (uint)i)}.xyz)");
			else
				sb.Append($"dot({v}, {Src(matrix + (uint)i)})");
		}
		return sb.Append(')').ToString();
	}

	string? Translate(ReadOnlySpan<uint> code) {
		if (code.IsEmpty)
			return null;
		uint shaderType = code[0] >> 16;
		if (shaderType != PixelShaderVersionType && shaderType != VertexShaderVersionType)
			return null;
		pixel = shaderType == PixelShaderVersionType;

		StringBuilder head = new("#version 460\n");
		if (pixel)
			head.Append("layout(std140, binding = 6) uniform source_ps_constants { vec4 ps_const[256]; };\nlayout(location = 0) out vec4 oC0;\n");
		else
			head.Append("layout(std140, binding = 0) uniform source_matrices { mat4 viewMatrix; mat4 projectionMatrix; mat4 modelMatrix; };\nlayout(std140, binding = 5) uniform source_vs_constants { vec4 vs_const[256]; };\n");

		StringBuilder body = new("void main() {\n");
		body.Append(pixel
			? $"vec4 oC1, oC2, oC3, vFace = vec4(gl_FrontFacing ? 1.0 : -1.0){Temps};\n"
			: $"mat4 mvpT = transpose(projectionMatrix * viewMatrix * modelMatrix), vpT = transpose(projectionMatrix * viewMatrix);\nvec4 oPos = vec4(0.0), oFog, oPts{Temps};\n");

		for (int i = 1; i < code.Length;) {
			uint tok = code[i++];
			ShaderOpcode op = (ShaderOpcode)(tok & OpcodeMask);
			if (op == ShaderOpcode.End)
				break;
			int len = op == ShaderOpcode.Comment ? CommentLength(tok) : InstructionLength(tok);
			ReadOnlySpan<uint> a = code.Slice(i, len);
			i += len;

			if (op is ShaderOpcode.Nop or ShaderOpcode.Comment or ShaderOpcode.Phase)
				continue;

			if (op == ShaderOpcode.Dcl) {
				int n = RegNum(a[1]);
				RegType type = GetRegType(a[1]);
				DeclUsage usage = GetDeclUsage(a[0]);
				int index = UsageIndex(a[0]);
				if (type == RegType.Sampler) {
					SamplerTextureType samplerType = GetSamplerType(a[0]);
					samplers[n & 15] = samplerType;
					head.Append($"layout(binding = {n}) uniform {samplerType switch { SamplerTextureType.Cube => "samplerCube", SamplerTextureType.Volume => "sampler3D", _ => "sampler2D" }} s{n};\n");
				}
				else if (type == RegType.Input && pixel)
					inNames[n & 15] = Use("in", usage == DeclUsage.Color ? $"vs_Color{index}" : $"vs_TexCoord{index}");
				else if (type == RegType.Input) {
					int location = usage switch { DeclUsage.Position => 0, DeclUsage.Normal => 1, DeclUsage.Color => 2, DeclUsage.TexCoord => 10 + index, _ => -1 };
					if (location < 0)
						body.Append($"vec4 a{n} = vec4(0.0);\n");
					else
						head.Append($"layout(location = {location}) in vec4 a{n};\n");
				}
				else if (type == RegType.Output && !pixel)
					outNames[n & 15] = usage switch {
						DeclUsage.Position => "oPos",
						DeclUsage.TexCoord => Use("out", $"vs_TexCoord{index}"),
						DeclUsage.Color => Use("out", $"vs_Color{index}"),
						_ => "oFog"
					};
				else if (type == RegType.Texture && pixel)
					Use("in", $"vs_TexCoord{n}");
				continue;
			}

			if (op == ShaderOpcode.Def) {
				int n = RegNum(a[0]);
				defs[n] = true;
				head.Append(CultureInfo.InvariantCulture, $"const vec4 c{n} = vec4({BitConverter.UInt32BitsToSingle(a[1]):R}, {BitConverter.UInt32BitsToSingle(a[2]):R}, {BitConverter.UInt32BitsToSingle(a[3]):R}, {BitConverter.UInt32BitsToSingle(a[4]):R});\n");
				continue;
			}

			if (op == ShaderOpcode.Ifc) {
				string cmp = (ShaderComparison)OpcodeControl(tok) switch {
					ShaderComparison.Gt => ">",
					ShaderComparison.Eq => "==",
					ShaderComparison.Ge => ">=",
					ShaderComparison.Lt => "<",
					ShaderComparison.Ne => "!=",
					_ => "<="
				};
				body.Append($"if ({Src(a[0])}.x {cmp} {Src(a[1])}.x) {{\n");
				continue;
			}
			if (op == ShaderOpcode.Else) {
				body.Append("} else {\n");
				continue;
			}
			if (op == ShaderOpcode.EndIf) {
				body.Append("}\n");
				continue;
			}

			string? dst = Reg(a[0]);
			if (dst == null)
				return null;

			if (op == ShaderOpcode.TexKill) {
				body.Append($"if (any(lessThan({dst}.xyz, vec3(0.0)))) discard;\n");
				continue;
			}

			string s1 = len > 1 ? Src(a[1]) : "", s2 = len > 2 ? Src(a[2]) : "", s3 = len > 3 ? Src(a[3]) : "", s4 = len > 4 ? Src(a[4]) : "";
			string smp = len > 2 ? $"s{RegNum(a[2])}" : "";
			string tc = len > 2 && samplers[RegNum(a[2]) & 15] is SamplerTextureType.Cube or SamplerTextureType.Volume ? ".xyz" : ".xy";

			string? expr = op switch {
				ShaderOpcode.Mov => s1,
				ShaderOpcode.Add => $"{s1} + {s2}",
				ShaderOpcode.Sub => $"{s1} - {s2}",
				ShaderOpcode.Mad => $"{s1} * {s2} + {s3}",
				ShaderOpcode.Mul => $"{s1} * {s2}",
				ShaderOpcode.Rcp => $"vec4(1.0 / {s1}.x)",
				ShaderOpcode.Rsq => $"vec4(inversesqrt(abs({s1}.x)))",
				ShaderOpcode.Dp3 => $"vec4(dot({s1}.xyz, {s2}.xyz))",
				ShaderOpcode.Dp4 => $"vec4(dot({s1}, {s2}))",
				ShaderOpcode.Min => $"min({s1}, {s2})",
				ShaderOpcode.Max => $"max({s1}, {s2})",
				ShaderOpcode.Slt => $"vec4(lessThan({s1}, {s2}))",
				ShaderOpcode.Sge => $"vec4(greaterThanEqual({s1}, {s2}))",
				ShaderOpcode.Exp => $"vec4(exp2({s1}.x))",
				ShaderOpcode.Log => $"vec4(log2(abs({s1}.x)))",
				ShaderOpcode.Lrp => $"mix({s3}, {s2}, {s1})",
				ShaderOpcode.Frc => $"fract({s1})",
				ShaderOpcode.M4x4 => Rows(a[1], a[2], 4, false),
				ShaderOpcode.M4x3 => Rows(a[1], a[2], 3, false),
				ShaderOpcode.M3x4 => Rows(a[1], a[2], 4, true),
				ShaderOpcode.M3x3 => Rows(a[1], a[2], 3, true),
				ShaderOpcode.M3x2 => Rows(a[1], a[2], 2, true),
				ShaderOpcode.Pow => $"vec4(pow(abs({s1}.x), {s2}.x))",
				ShaderOpcode.Crs => $"vec4(cross({s1}.xyz, {s2}.xyz), 0.0)",
				ShaderOpcode.Sgn => $"sign({s1})",
				ShaderOpcode.Abs => $"abs({s1})",
				ShaderOpcode.Nrm => $"{s1} * inversesqrt(dot({s1}.xyz, {s1}.xyz))",
				ShaderOpcode.SinCos => $"vec4(cos({s1}.x), sin({s1}.x), 0.0, 0.0)",
				ShaderOpcode.Tex => (TexLoadControl)OpcodeControl(tok) switch {
					TexLoadControl.Project => $"textureProj({smp}, {s1})",
					TexLoadControl.Bias => $"texture({smp}, {s1}{tc}, {s1}.w)",
					_ => $"texture({smp}, {s1}{tc})"
				},
				ShaderOpcode.Cmp => $"mix({s3}, {s2}, greaterThanEqual({s1}, vec4(0.0)))",
				ShaderOpcode.Dp2Add => $"vec4(dot({s1}.xy, {s2}.xy) + {s3}.x)",
				ShaderOpcode.Dsx => $"dFdx({s1})",
				ShaderOpcode.Dsy => $"dFdy({s1})",
				ShaderOpcode.TexLdd => $"textureGrad({smp}, {s1}{tc}, {s3}{tc}, {s4}{tc})",
				ShaderOpcode.TexLdl => $"textureLod({smp}, {s1}{tc}, {s1}.w)",
				_ => null
			};
			if (expr == null)
				return null;

			if (Saturate(a[0]))
				expr = $"clamp({expr}, 0.0, 1.0)";

			if (GetRegType(a[0]) == RegType.DepthOut) {
				body.Append($"gl_FragDepth = ({expr}).x;\n");
				continue;
			}

			uint mask = WriteMask(a[0]);
			string m = ".";
			for (int c = 0; c < 4; c++)
				if ((mask & (1u << c)) != 0)
					m += Xyzw[c];
			body.Append($"{dst}{m} = ({expr}){m};\n");
		}

		if (!pixel)
			body.Append("gl_Position = oPos;\n");
		foreach (string decl in io)
			head.Append(decl).Append('\n');
		return head.Append(body).Append("}\n").ToString();
	}
}
