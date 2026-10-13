using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace Source.CodeAnalysis.NetworkVars
{
	public sealed partial class NetworkVarGenerator
	{
		private const string PredictionCopyImplName = "global::Game.Client.PredictionCopyImpl";
		private const string MemoryMarshalName = "global::System.Runtime.InteropServices.MemoryMarshal";

		private static bool IsDataMapCreationCandidate(SyntaxNode node)
			=> node is BaseObjectCreationExpressionSyntax && node.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax };

		private static PredictionMapModel? GetPredictionMapModel(GeneratorSyntaxContext ctx, CancellationToken ct) {
			if (!ctx.Node.SyntaxTree.Options.PreprocessorSymbolNames.Contains("CLIENT_DLL"))
				return null;

			var creation = (BaseObjectCreationExpressionSyntax)ctx.Node;
			if (!(ctx.SemanticModel.GetOperation(creation, ct) is IObjectCreationOperation operation) || operation.Type?.ToDisplayString() != "Source.Common.DataMap")
				return null;

			var declarator = (VariableDeclaratorSyntax)creation.Parent!.Parent!;
			if (!(ctx.SemanticModel.GetDeclaredSymbol(declarator, ct) is IFieldSymbol mapField) || !mapField.IsStatic)
				return null;

			ITypeSymbol? owner = null;
			CollectionExpressionSyntax? entries = null;
			foreach (IArgumentOperation argument in operation.Arguments) {
				if (argument.Parameter?.Name == "dataClassType" && argument.Value is ITypeOfOperation typeOf)
					owner = typeOf.TypeOperand;
				else if (argument.Parameter?.Name == "dataDesc" && argument.Syntax is ArgumentSyntax { Expression: CollectionExpressionSyntax collection })
					entries = collection;
			}

			if (owner == null || entries == null || !(owner is INamedTypeSymbol namedOwner) || !IsAccessible(ctx.SemanticModel.Compilation, namedOwner))
				return null;

			List<string> lines = new List<string>();
			List<string> embedded = new List<string>();
			foreach (CollectionElementSyntax element in entries.Elements) {
				if (!(element is ExpressionElementSyntax expressionElement))
					return null;
				lines.Add(BuildPredictionEntry(ctx, expressionElement.Expression, embedded, ct));
			}

			return new PredictionMapModel(
				mapField.ToDisplayString(),
				mapField.Name,
				Display(owner),
				string.Join("\n", lines),
				string.Join("\n", embedded));
		}

		private static string BuildPredictionEntry(GeneratorSyntaxContext ctx, ExpressionSyntax expression, List<string> embedded, CancellationToken ct) {
			if (!(expression is InvocationExpressionSyntax invocation) || !(ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol method))
				return "X";

			INamedTypeSymbol container = method.ContainingType;
			if (container == null || container.Name != "DEFINE" || container.TypeArguments.Length != 1 || container.ContainingNamespace.ToDisplayString() != "Source")
				return "X";

			SeparatedSyntaxList<ArgumentSyntax> args = invocation.ArgumentList.Arguments;
			if (method.Name == "PRED_TYPEDESCRIPTION") {
				if (args.Count > 1 && ctx.SemanticModel.GetSymbolInfo(args[1].Expression, ct).Symbol is IFieldSymbol embeddedMap)
					embedded.Add(embeddedMap.ToDisplayString());
				return "E";
			}

			if (method.Parameters.Length == 0 || method.Parameters[0].Name != "name" || args.Count == 0)
				return "X";

			if (!(ctx.SemanticModel.GetConstantValue(args[0].Expression, ct).Value is string name))
				return "X";

			int fieldTypeIndex = method.Parameters.IndexOf(method.Parameters.FirstOrDefault(p => p.Name == "fieldType"));
			if (fieldTypeIndex < 0 || fieldTypeIndex >= args.Count)
				return "X";

			if (!(ctx.SemanticModel.GetSymbolInfo(args[fieldTypeIndex].Expression, ct).Symbol is IFieldSymbol fieldTypeMember) || fieldTypeMember.ContainingType?.ToDisplayString() != "Source.Common.FieldType")
				return "X";

			string fieldType = fieldTypeMember.Name == "Byte" ? "Character" : fieldTypeMember.Name;
			if (IsNoOpFieldType(fieldType))
				return "N";

			string? element = PredictionElementType(fieldType);
			if (element == null)
				return "X";

			Compilation compilation = ctx.SemanticModel.Compilation;
			ITypeSymbol owner = container.TypeArguments[0];
			INamedTypeSymbol? declaring = FindDataMapField(owner, name)?.ContainingType ?? FindNetworkVarProperty(owner, name)?.ContainingType;
			if (declaring == null)
				return "X";

			ResolvedPath? path = Resolve(compilation, declaring, name, new AccessFailure());
			if (path == null)
				return "X";

			string key = "S|" + Display(declaring) + "|" + name + "|\0";
			if (GetContainer(compilation, path.Type, out ITypeSymbol? arrayElement, out _) == ContainerKind.NetworkArray && arrayElement != null)
				return "F|" + fieldType + "|" + key + "|" + Display(path.Type) + "|1|" + Display(arrayElement);

			return "F|" + fieldType + "|" + key + "|" + Display(path.Type) + "|0|";
		}

		private static bool IsNoOpFieldType(string fieldType) => fieldType switch {
			"Void" or "Time" or "Tick" or "ModelIndex" or "ModelName" or "SoundName" or "Custom" or "ClassPtr" or "EDict" or "PositionVector" or "Function" => true,
			_ => false
		};

		private static string? PredictionElementType(string fieldType) => fieldType switch {
			"Float" => "float",
			"Double" => "double",
			"Integer" => "int",
			"Short" => "short",
			"Character" => "byte",
			"StringCharacter" => "char",
			"String" => "char",
			"Boolean" => "bool",
			"Vector" => "global::System.Numerics.Vector3",
			"Quaternion" => "global::System.Numerics.Quaternion",
			"Color32" => "global::Source.Color",
			"EHandle" => "global::Source.Common.BaseHandle",
			_ => null
		};

		private static (string Compare, string Copy, string Describe, string Watch) PredictionCalls(string fieldType) {
			switch (fieldType) {
				case "Float": return StandardCalls("Float");
				case "Double": return StandardCalls("Double");
				case "Integer": return StandardCalls("Int");
				case "Short": return StandardCalls("Short");
				case "Character": return StandardCalls("Byte");
				case "Boolean": return StandardCalls("Bool");
				case "Vector": return StandardCalls("Vector");
				case "Quaternion": return StandardCalls("Quaternion");
				case "StringCharacter":
					return ("self.CompareChar(output, input, size)", "self.CopyChar(difftype, output, input, size)", "self.DescribeChar(difftype, output, input, size)", DataCall("WatchData", "2 * size"));
				case "String":
					return ("self.CompareString(output, input)", "self.CopyString(difftype, output, input)", "self.DescribeString(difftype, output, input)", "self.WatchString(difftype, output, input)");
				case "Color32":
					return ("self.CompareColor(output, input, size)", "self.CopyColor(difftype, output, input, size)", DataCall("DescribeData", "4 * size"), DataCall("WatchData", "4 * size"));
				case "EHandle":
					return (PredictionCopyImplName + ".CompareEHandle(ref self, output, input, size)",
						PredictionCopyImplName + ".CopyEHandle(ref self, difftype, output, input, size)",
						PredictionCopyImplName + ".DescribeEHandle(ref self, difftype, output, input, size)",
						PredictionCopyImplName + ".WatchEHandle(ref self, difftype, output, input, size)");
				default:
					throw new InvalidOperationException(fieldType);
			}
		}

		private static (string, string, string, string) StandardCalls(string name)
			=> ("self.Compare" + name + "(output, input, size)", "self.Copy" + name + "(difftype, output, input, size)", "self.Describe" + name + "(difftype, output, input, size)", "self.Watch" + name + "(difftype, output, input, size)");

		private static string DataCall(string method, string size)
			=> "self." + method + "(difftype, " + size + ", " + MemoryMarshalName + ".AsBytes(output), " + MemoryMarshalName + ".AsBytes(input))";

		private static List<PredictionMapModel> SelectPredictionMaps(ImmutableArray<PredictionMapModel?> maps) {
			var embedded = new HashSet<string>(StringComparer.Ordinal);
			foreach (PredictionMapModel? map in maps)
				AddLines(embedded, map?.Embedded);

			var selected = maps
				.Where(m => m != null && (m.FieldName == "PredMap" || embedded.Contains(m.MapSymbol)))
				.Select(m => m!)
				.GroupBy(m => m.MapSymbol)
				.Select(g => g.First())
				.ToList();

			var duplicateOwners = new HashSet<string>(selected.GroupBy(m => m.Owner).Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.Ordinal);
			return selected.Where(m => !duplicateOwners.Contains(m.Owner)).OrderBy(m => m.MapSymbol, StringComparer.Ordinal).ToList();
		}

		private static string BuildPredictionCopy(PredictionMapModel map, int index, Dictionary<string, string> classes) {
			string pc = PredictionCopyImplName;
			string[] entries = map.Entries.Length == 0 ? new string[0] : map.Entries.Split('\n');

			var sb = new StringBuilder();
			sb.Append("\t\tprivate static void CopyFields").Append(index.ToString(CultureInfo.InvariantCulture))
				.AppendLine("(ref global::Source.Common.PredictionCopy self, int chainCount, global::Source.Common.DataMap rootMap, global::Source.Common.TypeDescription[] fields) {");
			sb.Append("\t\t\tif (fields.Length != ").Append(entries.Length.ToString(CultureInfo.InvariantCulture)).AppendLine(") {");
			sb.Append("\t\t\t\t").Append(pc).AppendLine(".CopyFieldsInterpreted(ref self, chainCount, rootMap, fields);");
			sb.AppendLine("\t\t\t\treturn;");
			sb.AppendLine("\t\t\t}");
			sb.AppendLine("\t\t\tself.CurrentMap = rootMap;");
			sb.AppendLine("\t\t\tif (self.CurrentClassName.IsEmpty)");
			sb.AppendLine("\t\t\t\tself.CurrentClassName = rootMap.DataClassName;");
			sb.AppendLine("\t\t\tbool destIsObject = self.Relationship is global::Source.Common.PredictionCopyRelationship.DataFrameToObject or global::Source.Common.PredictionCopyRelationship.ObjectToObject;");
			sb.AppendLine("\t\t\tbool srcIsObject = self.Relationship is global::Source.Common.PredictionCopyRelationship.ObjectToDataFrame or global::Source.Common.PredictionCopyRelationship.ObjectToObject;");
			sb.AppendLine("\t\t\tobject dest = self.Dest_Object, src = self.Src_Object;");
			sb.AppendLine("\t\t\tglobal::System.Span<byte> destFrame = self.Dest_DataFrame, srcFrame = self.Src_DataFrame;");
			sb.AppendLine("\t\t\tglobal::Source.Common.TypeDescription field;");
			sb.AppendLine("\t\t\tbool writeBack;");

			for (int i = 0; i < entries.Length; i++) {
				string[] parts = entries[i].Split('|');
				sb.Append("\t\t\tfield = fields[").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine("];");

				string kind = parts[0];
				string? accessorClass = null;
				if (kind == "F") {
					string key = parts[2] + "|" + parts[3] + "|" + parts[4] + "|" + parts[5];
					if (!classes.TryGetValue(key, out accessorClass))
						kind = "X";
				}

				switch (kind) {
					case "N":
						sb.Append("\t\t\t").Append(pc).AppendLine(".BeginField(ref self, chainCount, field);");
						break;
					case "E":
						sb.Append("\t\t\tif (").Append(pc).AppendLine(".BeginField(ref self, chainCount, field))");
						sb.Append("\t\t\t\t").Append(pc).AppendLine(".CopyEmbedded(ref self, chainCount, rootMap, field, destIsObject, srcIsObject);");
						break;
					case "F":
						AppendTypedPredictionField(sb, parts[1], accessorClass!, parts[6], parts[7] == "1", parts[8]);
						break;
					default:
						sb.Append("\t\t\tif (").Append(pc).AppendLine(".BeginField(ref self, chainCount, field))");
						sb.Append("\t\t\t\t").Append(pc).AppendLine(".CopyField(ref self, chainCount, rootMap, field, destIsObject, srcIsObject);");
						break;
				}
			}

			sb.AppendLine("\t\t\tself.CurrentClassName = default;");
			sb.AppendLine("\t\t}");
			return sb.ToString();
		}

		private static void AppendTypedPredictionField(StringBuilder sb, string fieldType, string accessorClass, string storing, bool networkArray, string arrayElement) {
			string pc = PredictionCopyImplName;
			string element = PredictionElementType(fieldType)!;
			var calls = PredictionCalls(fieldType);

			string output, input;
			if (networkArray) {
				output = pc + ".NetworkArrayOutput<" + arrayElement + ", " + element + ">(in " + accessorClass + ".RefOf(dest), out writeBack)";
				input = pc + ".NetworkArrayInput<" + arrayElement + ", " + element + ">(in " + accessorClass + ".RefOf(src))";
			}
			else {
				output = pc + ".ObjectOutput<" + storing + ", " + element + ">(ref " + accessorClass + ".RefOf(dest), size, out writeBack)";
				input = pc + ".ObjectInput<" + storing + ", " + element + ">(ref " + accessorClass + ".RefOf(src), size)";
			}

			sb.Append("\t\t\tif (").Append(pc).AppendLine(".BeginField(ref self, chainCount, field)) {");
			sb.AppendLine("\t\t\t\tint size = field.FieldSize;");
			sb.AppendLine("\t\t\t\tbool shouldWatch = self.WatchField == field;");
			sb.Append("\t\t\t\tglobal::System.Span<").Append(element).Append("> output = destIsObject ? ").Append(output)
				.Append(" : ").Append(pc).Append(".FrameOutput<").Append(element).AppendLine(">(destFrame, field, size, out writeBack);");
			sb.Append("\t\t\t\tglobal::System.ReadOnlySpan<").Append(element).Append("> input = srcIsObject ? ").Append(input)
				.Append(" : ").Append(pc).Append(".FrameInput<").Append(element).AppendLine(">(srcFrame, field, size);");
			sb.Append("\t\t\t\tglobal::Source.Common.DiffType difftype = ").Append(calls.Compare).AppendLine(";");
			sb.Append("\t\t\t\t").Append(calls.Copy).AppendLine(";");
			if (!networkArray)
				sb.Append("\t\t\t\tif (writeBack) ").Append(pc).Append(".WriteBack<").Append(storing).Append(", ").Append(element).Append(">(ref ").Append(accessorClass).AppendLine(".RefOf(dest));");
			sb.Append("\t\t\t\tif (self.ErrorCheck && self.ShouldDescribe) ").Append(calls.Describe).AppendLine(";");
			sb.Append("\t\t\t\tif (shouldWatch) ").Append(calls.Watch).AppendLine(";");
			sb.AppendLine("\t\t\t}");
		}

		private sealed class PredictionMapModel : IEquatable<PredictionMapModel>
		{
			public PredictionMapModel(string mapSymbol, string fieldName, string owner, string entries, string embedded) {
				MapSymbol = mapSymbol;
				FieldName = fieldName;
				Owner = owner;
				Entries = entries;
				Embedded = embedded;
			}

			public string MapSymbol { get; }
			public string FieldName { get; }
			public string Owner { get; }
			public string Entries { get; }
			public string Embedded { get; }

			public bool Equals(PredictionMapModel? other)
				=> other != null && MapSymbol == other.MapSymbol && FieldName == other.FieldName && Owner == other.Owner && Entries == other.Entries && Embedded == other.Embedded;

			public override bool Equals(object? obj) => Equals(obj as PredictionMapModel);
			public override int GetHashCode() => MapSymbol.GetHashCode();
		}
	}
}
