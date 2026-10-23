using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

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
		private const string NetworkNameAttributeName = "Source.Common.NetworkNameAttribute";
		private const string NetworkArraySizeAttributeName = "Source.Common.NetworkArraySizeAttribute";
		private const string InlineArrayAttributeName = "System.Runtime.CompilerServices.InlineArrayAttribute";
		private const string UnsafeName = "global::System.Runtime.CompilerServices.Unsafe";
		private const string ClassPlaceholder = "__CLASS__";

		private static readonly HashSet<string> FieldMethods = new HashSet<string> {
			"OF", "OF_NAMED", "OF_ARRAY", "OF_ARRAYINDEX", "OF_SENDINFO_ARRAY", "OF_VECTORELEM", "OF_LIST",
			"FIELD", "KEYFIELD", "KEYFIELD_NOT_SAVED", "ARRAY", "GLOBAL_FIELD", "GLOBAL_KEYFIELD", "AUTO_ARRAY_KEYFIELD", "INPUT", "OUTPUT",
			"AUTO_ARRAY", "PRED_FIELD", "PRED_ARRAY", "PRED_FIELD_TOL", "PRED_ARRAY_TOL", "PRED_TYPEDESCRIPTION", "EMBEDDED", "UTLVECTOR"
		};
		private static readonly HashSet<string> ValueMethods = new HashSet<string> { "GetValue", "SetValue", "CopyTo", "CopyFrom", "GetElement", "SetElement" };

		private enum ContainerKind
		{
			None,
			InlineArray,
			Vector,
			NetworkArray,
			List
		}

		private static void InitializeFieldAccessors(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<ImmutableArray<PropertyModel?>> properties, IncrementalValueProvider<ImmutableArray<TableModel?>> tables) {
			IncrementalValueProvider<ImmutableArray<FieldAccessorModel?>> accessors = context.SyntaxProvider
				.CreateSyntaxProvider(
					predicate: static (node, _) => IsInvocationNamed(node, FieldMethods),
					transform: static (ctx, ct) => GetFieldAccessorModel(ctx, ct))
				.Where(static m => m != null)
				.Collect();

			IncrementalValueProvider<ImmutableArray<string?>> conversions = context.SyntaxProvider
				.CreateSyntaxProvider(
					predicate: static (node, _) => IsInvocationNamed(node, ValueMethods),
					transform: static (ctx, ct) => GetValueConversions(ctx, ct))
				.Where(static c => c != null)
				.Collect();

			IncrementalValueProvider<ImmutableArray<PredictionMapModel?>> predictionMaps = context.SyntaxProvider
				.CreateSyntaxProvider(
					predicate: static (node, _) => IsDataMapCreationCandidate(node),
					transform: static (ctx, ct) => GetPredictionMapModel(ctx, ct))
				.Where(static m => m != null)
				.Collect();

			context.RegisterSourceOutput(accessors.Combine(conversions).Combine(properties).Combine(predictionMaps).Combine(tables),
				static (spc, data) => EmitFieldAccessors(spc, data.Left.Left.Left.Left, data.Left.Left.Left.Right, data.Left.Left.Right, data.Left.Right, data.Right));
		}

		private static bool IsInvocationNamed(SyntaxNode node, HashSet<string> names) {
			if (!(node is InvocationExpressionSyntax invocation))
				return false;

			SimpleNameSyntax? name = invocation.Expression switch {
				MemberAccessExpressionSyntax member => member.Name,
				SimpleNameSyntax simple => simple,
				_ => null
			};
			return name != null && names.Contains(name.Identifier.ValueText);
		}

		private static string? GetValueConversions(GeneratorSyntaxContext ctx, CancellationToken ct) {
			if (!(ctx.SemanticModel.GetSymbolInfo(ctx.Node, ct).Symbol is IMethodSymbol method) || method.TypeArguments.Length != 1)
				return null;

			List<string> conversions = new List<string>();
			AddConversions(ctx.SemanticModel.Compilation, method.TypeArguments[0], conversions);
			return conversions.Count == 0 ? null : string.Join("\n", conversions);
		}

		private static FieldAccessorModel? GetFieldAccessorModel(GeneratorSyntaxContext ctx, CancellationToken ct) {
			var invocation = (InvocationExpressionSyntax)ctx.Node;
			if (!(ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol method))
				return null;

			INamedTypeSymbol container = method.ContainingType;
			if (container == null || (container.Name != "FIELD" && container.Name != "DEFINE") || container.TypeArguments.Length != 1 || container.ContainingNamespace.ToDisplayString() != "Source")
				return null;

			if (!(container.TypeArguments[0] is INamedTypeSymbol owner) || owner.TypeKind == TypeKind.Error)
				return null;

			SeparatedSyntaxList<ArgumentSyntax> args = invocation.ArgumentList.Arguments;
			if (args.Count == 0 || !(ctx.SemanticModel.GetConstantValue(args[0].Expression, ct).Value is string expression))
				return null;

			Compilation compilation = ctx.SemanticModel.Compilation;
			var failure = new AccessFailure();
			FieldAccessorModel? model;
			if (container.Name == "DEFINE") {
				if (method.Parameters.Length == 0 || method.Parameters[0].Name != "name")
					return null;
				INamedTypeSymbol? declaring = FindDataMapField(owner, expression)?.ContainingType ?? FindNetworkVarProperty(owner, expression)?.ContainingType;
				model = declaring == null ? null : BuildScalar(compilation, declaring, expression, null, failure);
			}
			else {
				switch (method.Name) {
					case "OF":
						model = BuildScalar(compilation, owner, expression, null, failure);
						break;
					case "OF_NAMED":
						if (args.Count < 2 || !(ctx.SemanticModel.GetConstantValue(args[1].Expression, ct).Value is string name))
							return null;
						model = BuildScalar(compilation, owner, expression, name, failure);
						break;
					case "OF_LIST":
						model = BuildArray(compilation, owner, expression, true, failure);
						break;
					default:
						model = BuildArray(compilation, owner, expression, false, failure);
						break;
				}
			}

			if (model == null && failure.Type != null)
				return FieldAccessorModel.Inaccessible(args[0].Expression.GetLocation(), expression, owner.ToDisplayString(), failure.Type.ToDisplayString());

			return model;
		}

		private static FieldAccessorModel? BuildScalar(Compilation compilation, INamedTypeSymbol owner, string expression, string? name, AccessFailure failure) {
			ResolvedPath? path = Resolve(compilation, owner, expression, failure);
			if (path == null)
				return null;

			string accessorName = name ?? expression;
			string networkName = name ?? BuildNetworkName(path.Parts, accessorName, false);
			string ownerDisplay = Display(owner);
			string field = Display(path.Type);

			var body = new StringBuilder();
			body.Append("\t\tprivate sealed class ").Append(ClassPlaceholder).Append(" : ").AppendLine(ScalarAccessorBase(compilation, path.Type));
			body.AppendLine("\t\t{");
			body.Append("\t\t\tpublic ").Append(ClassPlaceholder).Append("() : base(typeof(").Append(ownerDisplay).Append("), ")
				.Append(Literal(accessorName)).Append(", ").Append(Literal(networkName)).AppendLine(") { }");
			body.Append("\t\t\tpublic static ref ").Append(field).Append(" RefOf(object o) => ref ").Append(path.Expression).AppendLine(";");
			body.Append("\t\t\tpublic override ref ").Append(field).AppendLine(" Ref(object o) => ref RefOf(o);");
			body.Append("\t\t\tpublic override string FieldKey => ").Append(Literal(FieldKey("S", path, owner, expression))).AppendLine(";");
			foreach (string accessor in path.Accessors)
				body.Append("\t\t\t").AppendLine(accessor);
			body.AppendLine("\t\t}");

			string registration = "global::Source.Common.FieldAccessorRegistry.Register(typeof(" + ownerDisplay + "), " + Literal(expression) + ", "
				+ (name == null ? "null" : Literal(name)) + ", static () => new " + ClassPlaceholder + "());";

			List<string> conversions = new List<string>();
			AddConversions(compilation, path.Type, conversions);

			return new FieldAccessorModel("S|" + ownerDisplay + "|" + expression + "|" + (name ?? "\0"), registration, body.ToString(), string.Join("\n", conversions));
		}

		private static FieldAccessorModel? BuildArray(Compilation compilation, INamedTypeSymbol owner, string expression, bool isList, AccessFailure failure) {
			ResolvedPath? path = Resolve(compilation, owner, expression, failure);
			if (path == null)
				return null;

			ContainerKind kind = GetContainer(compilation, path.Type, out ITypeSymbol? element, out int length);
			if (kind == ContainerKind.None || element == null)
				return null;
			if (!IsAccessible(compilation, element))
				return failure.Fail<FieldAccessorModel>(element);

			bool listFlag = false;
			if (kind != ContainerKind.InlineArray) {
				if (isList) {
					if (kind != ContainerKind.List)
						return null;
					listFlag = true;
				}
				else if (kind == ContainerKind.NetworkArray) {
					if (!(path.LastMember is IFieldSymbol field) || !TryGetNetworkArraySize(field, out length))
						return null;
				}
				else if (kind != ContainerKind.Vector)
					return null;
			}

			string ownerDisplay = Display(owner);
			string storing = Display(path.Type);
			string elementDisplay = Display(element);
			string networkName = BuildNetworkName(path.Parts, expression, false);

			List<NamePart> elementParts = new List<NamePart>(path.Parts) { NamePart.SymbolicIndex() };
			string elementFormat = BuildNetworkName(elementParts, EscapeFormat(expression) + "[{0}]", true);

			var body = new StringBuilder();
			body.Append("\t\tprivate sealed class ").Append(ClassPlaceholder).Append(" : global::Source.Common.ArrayFieldAccessor<").Append(storing).Append(", ").Append(elementDisplay).AppendLine(">");
			body.AppendLine("\t\t{");
			body.Append("\t\t\tpublic ").Append(ClassPlaceholder).Append("(int listMax) : base(typeof(").Append(ownerDisplay).Append("), ")
				.Append(Literal(expression)).Append(", ").Append(Literal(networkName)).Append(", ").Append(Literal(elementFormat)).Append(", ")
				.Append(listFlag ? "listMax" : length.ToString(CultureInfo.InvariantCulture)).Append(", ").Append(listFlag ? "true" : "false").AppendLine(") { }");
			body.Append("\t\t\tpublic override ref ").Append(storing).Append(" Ref(object o) => ref ").Append(path.Expression).AppendLine(";");
			body.Append("\t\t\tpublic override string FieldKey => ").Append(Literal(FieldKey("A", path, owner, expression))).AppendLine(";");
			body.Append("\t\t\tpublic override ref ").Append(elementDisplay).Append(" ElementRef(object o, int index) => ref ")
				.Append(IndexExpression(kind, path.Type, element, path.Expression, "index")).AppendLine(";");
			foreach (string accessor in path.Accessors)
				body.Append("\t\t\t").AppendLine(accessor);
			body.AppendLine("\t\t}");

			string registration = "global::Source.Common.FieldAccessorRegistry.RegisterArray(typeof(" + ownerDisplay + "), " + Literal(expression)
				+ ", static listMax => new " + ClassPlaceholder + "(listMax));";

			List<string> conversions = new List<string>();
			AddConversions(compilation, path.Type, conversions);
			AddConversions(compilation, element, conversions);

			return new FieldAccessorModel("A|" + ownerDisplay + "|" + expression, registration, body.ToString(), string.Join("\n", conversions));
		}

		private static ResolvedPath? Resolve(Compilation compilation, INamedTypeSymbol owner, string expression, AccessFailure failure) {
			if (!IsAccessible(compilation, owner))
				return failure.Fail<ResolvedPath>(owner);

			List<Segment>? segments = Tokenize(expression);
			if (segments == null || segments.Count == 0)
				return null;

			var path = new ResolvedPath {
				Expression = owner.IsValueType
					? UnsafeName + ".Unbox<" + Display(owner) + ">(o)"
					: "((" + Display(owner) + ")o)"
			};

			ITypeSymbol current = owner;
			for (int i = 0; i < segments.Count; i++) {
				Segment segment = segments[i];
				if (segment.Name == null) {
					ContainerKind kind = GetContainer(compilation, current, out ITypeSymbol? element, out _);
					if (kind == ContainerKind.None || element == null)
						return null;
					if (!IsAccessible(compilation, element))
						return failure.Fail<ResolvedPath>(element);

					path.Expression = IndexExpression(kind, current, element, path.Expression, segment.Index.ToString(CultureInfo.InvariantCulture));
					path.Parts.Add(NamePart.ConstantIndex(segment.Index));
					path.LastMember = null;
					current = element;
					continue;
				}

				ITypeSymbol? next = ApplyMember(compilation, path, current, segment.Name, i == segments.Count - 1, failure);
				if (next == null)
					return null;
				current = next;
			}

			path.Type = current;
			return path;
		}

		private static ITypeSymbol? ApplyMember(Compilation compilation, ResolvedPath path, ITypeSymbol current, string name, bool last, AccessFailure failure) {
			ISymbol? member = FindMember(current, name) ?? FindNetworkVarProperty(current, name);
			string fieldName;
			ITypeSymbol memberType;
			INamedTypeSymbol declaring;
			bool direct;

			if (member is IFieldSymbol field) {
				if (field.IsStatic)
					return null;
				fieldName = field.Name;
				memberType = field.Type;
				declaring = field.ContainingType;
				direct = compilation.IsSymbolAccessibleWithin(field, compilation.Assembly) && !(field.IsReadOnly && (last || memberType.IsValueType));
			}
			else if (member is IPropertySymbol prop && !prop.IsStatic && HasAttribute(prop, AttributeMetadataName)) {
				fieldName = "__nv_" + prop.Name;
				memberType = prop.Type;
				declaring = prop.ContainingType;
				direct = false;
			}
			else
				return null;

			if (!IsAccessible(compilation, memberType))
				return failure.Fail<ITypeSymbol>(memberType);

			if (direct)
				path.Expression = path.Expression + "." + Identifier(fieldName);
			else {
				if (declaring.IsGenericType)
					return null;
				if (!IsAccessible(compilation, declaring))
					return failure.Fail<ITypeSymbol>(declaring);

				string accessorName = "__F" + path.Accessors.Count.ToString(CultureInfo.InvariantCulture);
				string byRef = declaring.IsValueType ? "ref " : "";
				path.Accessors.Add("[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "
					+ Literal(fieldName) + ")] private static extern ref " + Display(memberType) + " " + accessorName + "(" + byRef + Display(declaring) + " o);");
				path.Expression = accessorName + "(" + byRef + path.Expression + ")";
			}

			string? networkName = GetNetworkNameAttribute(member);
			if (path.RootDeclaring == null)
				path.RootDeclaring = declaring;
			path.Parts.Add(NamePart.Member(networkName ?? member.Name, networkName != null));
			path.LastMember = member;
			return memberType;
		}

		private static ISymbol? FindMember(ITypeSymbol type, string name) {
			for (ITypeSymbol? t = type; t != null; t = t.BaseType) {
				bool declaredHere = SymbolEqualityComparer.Default.Equals(t, type);
				foreach (ISymbol member in t.GetMembers(name)) {
					if (!(member is IFieldSymbol) && !(member is IPropertySymbol))
						continue;
					if (!declaredHere && member.DeclaredAccessibility == Accessibility.Private)
						continue;
					return member;
				}
			}
			return null;
		}

		private static IPropertySymbol? FindNetworkVarProperty(ITypeSymbol type, string backingName) {
			string name = backingName.StartsWith("__nv_", StringComparison.Ordinal) ? backingName.Substring(5) : backingName;
			return FindMember(type, name) is IPropertySymbol prop && HasAttribute(prop, AttributeMetadataName) ? prop : null;
		}

		private static IFieldSymbol? FindDataMapField(ITypeSymbol type, string name) {
			for (ITypeSymbol? t = type; t != null; t = t.BaseType) {
				foreach (ISymbol member in t.GetMembers(name)) {
					if (member is IFieldSymbol field && !field.IsStatic)
						return field;
				}
			}
			return null;
		}

		private static ContainerKind GetContainer(Compilation compilation, ITypeSymbol type, out ITypeSymbol? element, out int length) {
			element = null;
			length = 0;

			if (!(type is INamedTypeSymbol named))
				return ContainerKind.None;

			AttributeData? inlineArray = named.OriginalDefinition.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == InlineArrayAttributeName);
			if (inlineArray != null) {
				if (inlineArray.ConstructorArguments.Length != 1 || !(inlineArray.ConstructorArguments[0].Value is int inlineLength))
					return ContainerKind.None;
				element = named.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => !f.IsStatic)?.Type;
				length = inlineLength;
				return ContainerKind.InlineArray;
			}

			if (named.IsGenericType) {
				string definition = named.ConstructedFrom.ToDisplayString();
				element = named.TypeArguments[0];
				if (definition == "Source.Common.NetworkArray<Type>")
					return ContainerKind.NetworkArray;
				if (definition == "System.Collections.Generic.List<T>")
					return ContainerKind.List;
				element = null;
				return ContainerKind.None;
			}

			string display = named.ToDisplayString();
			if (display == "System.Numerics.Vector3" || display == "Source.Common.Mathematics.QAngle") {
				element = compilation.GetSpecialType(SpecialType.System_Single);
				length = 3;
				return ContainerKind.Vector;
			}

			return ContainerKind.None;
		}

		private static string ScalarAccessorBase(Compilation compilation, ITypeSymbol type) {
			ContainerKind kind = GetContainer(compilation, type, out ITypeSymbol? element, out _);
			if (element != null && IsAccessible(compilation, element)) {
				if (kind == ContainerKind.InlineArray)
					return "global::Source.Common.InlineArrayFieldAccessor<" + Display(type) + ", " + Display(element) + ">";
				if (kind == ContainerKind.NetworkArray)
					return "global::Source.Common.NetworkArrayFieldAccessor<" + Display(element) + ">";
			}
			return "global::Source.Common.FieldAccessor<" + Display(type) + ">";
		}

		private static string IndexExpression(ContainerKind kind, ITypeSymbol container, ITypeSymbol element, string expression, string index) {
			switch (kind) {
				case ContainerKind.InlineArray:
				case ContainerKind.Vector:
					return UnsafeName + ".Add(ref " + UnsafeName + ".As<" + Display(container) + ", " + Display(element) + ">(ref " + expression + "), " + index + ")";
				case ContainerKind.NetworkArray:
					return expression + ".Value[" + index + "]";
				case ContainerKind.List:
					return "global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(" + expression + ")[" + index + "]";
				default:
					throw new InvalidOperationException();
			}
		}

		private static bool TryGetNetworkArraySize(IFieldSymbol field, out int size) {
			size = 0;
			AttributeData? attribute = field.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == NetworkArraySizeAttributeName);
			if (attribute == null || attribute.ConstructorArguments.Length != 1 || !(attribute.ConstructorArguments[0].Value is int value))
				return false;
			size = value;
			return true;
		}

		private static List<Segment>? Tokenize(string expression) {
			var segments = new List<Segment>();
			int last = 0;
			for (int i = 0; i <= expression.Length; i++) {
				if (i == expression.Length) {
					if (i > last)
						segments.Add(Segment.Member(expression.Substring(last, i - last)));
					break;
				}

				char c = expression[i];
				if (c == '.') {
					if (i > last)
						segments.Add(Segment.Member(expression.Substring(last, i - last)));
					last = i + 1;
				}
				else if (c == '[') {
					if (i > last)
						segments.Add(Segment.Member(expression.Substring(last, i - last)));
					int close = expression.IndexOf(']', i + 1);
					if (close < 0 || !int.TryParse(expression.Substring(i + 1, close - i - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
						return null;
					segments.Add(Segment.Indexer(index));
					i = close;
					last = i + 1;
				}
			}
			return segments;
		}

		private static string BuildNetworkName(List<NamePart> parts, string fallback, bool asFormat) {
			if (!parts.Any(p => !p.IsIndex && p.Named))
				return fallback;

			var sb = new StringBuilder();
			for (int i = 0; i < parts.Count; i++) {
				NamePart part = parts[i];
				if (part.IsIndex) {
					sb.Append('[').Append(part.Symbolic ? "{0}" : part.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
					continue;
				}

				string memberName = part.Name!;
				if (i + 1 < parts.Count && parts[i + 1].IsIndex && memberName.Contains("{0}")) {
					NamePart next = parts[i + 1];
					if (next.Symbolic)
						memberName = EscapeFormat(memberName.Replace("{0}", "\u0001")).Replace("\u0001", "{0}");
					else {
						memberName = memberName.Replace("{0}", next.Index.ToString(CultureInfo.InvariantCulture));
						if (asFormat)
							memberName = EscapeFormat(memberName);
					}
					i++;
				}
				else if (asFormat)
					memberName = EscapeFormat(memberName);

				if (memberName.Length == 0)
					continue;
				if (sb.Length > 0)
					sb.Append('.');
				sb.Append(memberName);
			}
			return sb.ToString();
		}

		private static void AddConversions(Compilation compilation, ITypeSymbol type, List<string> conversions) {
			if (!(type is INamedTypeSymbol named) || named.TypeKind == TypeKind.Error || !IsAccessible(compilation, named))
				return;

			foreach (IMethodSymbol op in named.GetMembers("op_Implicit").OfType<IMethodSymbol>()) {
				if (!op.IsStatic || op.Parameters.Length != 1 || !compilation.IsSymbolAccessibleWithin(op, compilation.Assembly))
					continue;

				IParameterSymbol parameter = op.Parameters[0];
				if (parameter.RefKind != RefKind.None && parameter.RefKind != RefKind.In)
					continue;

				ITypeSymbol from = parameter.Type, to = op.ReturnType;
				if (!IsUsableTypeArgument(compilation, from) || !IsUsableTypeArgument(compilation, to) || SymbolEqualityComparer.Default.Equals(from, to))
					continue;

				string f = Display(from), t = Display(to);
				conversions.Add("global::Source.Common.FieldConvert<" + f + ", " + t + ">.Register(static (in " + f + " v) => v);");
			}

			if (GetContainer(compilation, named, out ITypeSymbol? element, out _) == ContainerKind.InlineArray && element?.SpecialType == SpecialType.System_Char) {
				string d = Display(named);
				conversions.Add("global::Source.Common.FieldConvert<" + d + ", string>.Register(static (in " + d + " v) => string.Intern(new string(global::Source.UnmanagedUtils.SliceNullTerminatedString(v))));");
			}
		}

		private static bool IsUsableTypeArgument(Compilation compilation, ITypeSymbol type) {
			if (type.IsRefLikeType || type.SpecialType == SpecialType.System_Void)
				return false;
			return IsAccessible(compilation, type);
		}

		private static bool IsAccessible(Compilation compilation, ITypeSymbol type) {
			switch (type) {
				case IArrayTypeSymbol array:
					return IsAccessible(compilation, array.ElementType);
				case INamedTypeSymbol named:
					if (named.TypeKind == TypeKind.Error || !compilation.IsSymbolAccessibleWithin(named, compilation.Assembly))
						return false;
					foreach (ITypeSymbol argument in named.TypeArguments)
						if (!IsAccessible(compilation, argument))
							return false;
					return true;
				default:
					return false;
			}
		}

		private static bool HasAttribute(ISymbol symbol, string attributeName)
			=> symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attributeName);

		private static string? GetNetworkNameAttribute(ISymbol symbol) {
			AttributeData? networkVar = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == AttributeMetadataName);
			if (networkVar != null && networkVar.ConstructorArguments.Length == 1 && networkVar.ConstructorArguments[0].Value is string networkVarName)
				return networkVarName;

			AttributeData? attribute = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == NetworkNameAttributeName);
			if (attribute == null || attribute.ConstructorArguments.Length != 1)
				return null;
			return attribute.ConstructorArguments[0].Value as string;
		}

		private static string Display(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

		private static string FieldKey(string kind, ResolvedPath path, INamedTypeSymbol owner, string expression)
			=> kind + "|" + Display(path.RootDeclaring ?? owner) + "|" + expression;

		private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);

		private static string Identifier(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

		private static string EscapeFormat(string value) => value.Replace("{", "{{").Replace("}", "}}");

		private static void EmitFieldAccessors(SourceProductionContext spc, ImmutableArray<FieldAccessorModel?> models, ImmutableArray<string?> valueConversions, ImmutableArray<PropertyModel?> properties, ImmutableArray<PredictionMapModel?> predictionMaps, ImmutableArray<TableModel?> tables) {
			foreach (TableModel? table in tables) {
				if (table == null)
					continue;
				models = models.AddRange(table.Accessors);
				if (table.Map != null)
					predictionMaps = predictionMaps.Add(table.Map);
			}

			var unique = new SortedDictionary<string, FieldAccessorModel>(StringComparer.Ordinal);
			var conversions = new SortedSet<string>(StringComparer.Ordinal);

			foreach (FieldAccessorModel? model in models) {
				if (model == null)
					continue;
				if (model.ErrorLocation != null) {
					spc.ReportDiagnostic(Diagnostic.Create(InaccessibleFieldType, model.ErrorLocation, model.ErrorArgs));
					continue;
				}
				if (!unique.ContainsKey(model.Key))
					unique.Add(model.Key, model);
				AddLines(conversions, model.Conversions);
			}
			foreach (string? lines in valueConversions)
				AddLines(conversions, lines);
			foreach (PropertyModel? property in properties)
				AddLines(conversions, property?.Conversions);

			List<PredictionMapModel> maps = SelectPredictionMaps(predictionMaps);

			if (unique.Count == 0 && conversions.Count == 0 && maps.Count == 0)
				return;

			var classes = new Dictionary<string, string>(StringComparer.Ordinal);
			int classIndex = 0;
			foreach (string key in unique.Keys)
				classes[key] = "Accessor" + (classIndex++).ToString(CultureInfo.InvariantCulture);

			var sb = new StringBuilder();
			sb.AppendLine("// <auto-generated/>");
			sb.AppendLine("#nullable disable");
			sb.AppendLine("#pragma warning disable CA2255, CS0168, CS0219");
			sb.AppendLine();
			sb.AppendLine("namespace Source.Generated");
			sb.AppendLine("{");
			sb.AppendLine("\tinternal static class FieldAccessors");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\t[global::System.Runtime.CompilerServices.ModuleInitializer]");
			sb.AppendLine("\t\tinternal static void Register() {");

			int index = 0;
			foreach (FieldAccessorModel model in unique.Values)
				sb.Append("\t\t\t").AppendLine(model.Registration.Replace(ClassPlaceholder, "Accessor" + (index++).ToString(CultureInfo.InvariantCulture)));
			foreach (string conversion in conversions)
				sb.Append("\t\t\t").AppendLine(conversion);
			for (int i = 0; i < maps.Count; i++)
				sb.Append("\t\t\t").Append(PredictionCopyImplName).Append(".RegisterCopyFields(typeof(").Append(maps[i].Owner).Append("), CopyFields").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(");");

			sb.AppendLine("\t\t}");

			index = 0;
			foreach (FieldAccessorModel model in unique.Values) {
				sb.AppendLine();
				sb.Append(model.Body.Replace(ClassPlaceholder, "Accessor" + (index++).ToString(CultureInfo.InvariantCulture)));
			}

			for (int i = 0; i < maps.Count; i++) {
				sb.AppendLine();
				sb.Append(BuildPredictionCopy(maps[i], i, classes));
			}

			sb.AppendLine("\t}");
			sb.AppendLine("}");

			spc.AddSource("FieldAccessors.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
		}

		private static void AddLines(ISet<string> set, string? lines) {
			if (string.IsNullOrEmpty(lines))
				return;
			foreach (string line in lines!.Split('\n'))
				set.Add(line);
		}

		private sealed class Segment
		{
			public string? Name { get; private set; }
			public int Index { get; private set; }

			public static Segment Member(string name) => new Segment { Name = name };
			public static Segment Indexer(int index) => new Segment { Index = index };
		}

		private sealed class NamePart
		{
			public string? Name { get; private set; }
			public bool Named { get; private set; }
			public bool IsIndex { get; private set; }
			public int Index { get; private set; }
			public bool Symbolic { get; private set; }

			public static NamePart Member(string name, bool named) => new NamePart { Name = name, Named = named };
			public static NamePart ConstantIndex(int index) => new NamePart { IsIndex = true, Index = index };
			public static NamePart SymbolicIndex() => new NamePart { IsIndex = true, Symbolic = true };
		}

		private sealed class AccessFailure
		{
			public ITypeSymbol? Type;

			public T? Fail<T>(ITypeSymbol type) where T : class {
				if (Type == null)
					Type = type;
				return null;
			}
		}

		private sealed class ResolvedPath
		{
			public string Expression = "";
			public ITypeSymbol Type = null!;
			public ISymbol? LastMember;
			public INamedTypeSymbol? RootDeclaring;
			public readonly List<NamePart> Parts = new List<NamePart>();
			public readonly List<string> Accessors = new List<string>();
		}

		private sealed class FieldAccessorModel : IEquatable<FieldAccessorModel>
		{
			public FieldAccessorModel(string key, string registration, string body, string conversions) {
				Key = key;
				Registration = registration;
				Body = body;
				Conversions = conversions;
			}

			public static FieldAccessorModel Inaccessible(Location location, string member, string owner, string type)
				=> new FieldAccessorModel("E|" + owner + "|" + member, "", "", "") { ErrorLocation = location, ErrorArgs = new object[] { member, owner, type } };

			public string Key { get; }
			public string Registration { get; }
			public string Body { get; }
			public string Conversions { get; }
			public Location? ErrorLocation { get; private set; }
			public object[]? ErrorArgs { get; private set; }

			public bool Equals(FieldAccessorModel? other)
				=> other != null && Key == other.Key && Registration == other.Registration && Body == other.Body && Conversions == other.Conversions && Equals(ErrorLocation, other.ErrorLocation);

			public override bool Equals(object? obj) => Equals(obj as FieldAccessorModel);
			public override int GetHashCode() => Key.GetHashCode();
		}
	}
}
