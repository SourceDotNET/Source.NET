using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Source.CodeAnalysis.NetworkVars
{
	[Generator(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
	public sealed partial class NetworkVarGenerator : IIncrementalGenerator
	{
		private const string AttributeMetadataName = "Source.Common.NetworkVarAttribute";

		private static readonly DiagnosticDescriptor NotPartialProperty = new DiagnosticDescriptor(
			id: "NETVAR001",
			title: "[NetworkVar] requires a partial property",
			messageFormat: "'{0}' is marked [NetworkVar] but is not a partial property; the generator cannot emit its backing storage",
			category: "NetworkVars",
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		private static readonly DiagnosticDescriptor NotPartialType = new DiagnosticDescriptor(
			id: "NETVAR002",
			title: "[NetworkVar] requires a partial containing class",
			messageFormat: "'{0}' contains [NetworkVar] properties but the containing type '{1}' is not partial",
			category: "NetworkVars",
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		private static readonly DiagnosticDescriptor UnsupportedType = new DiagnosticDescriptor(
			id: "NETVAR003",
			title: "[NetworkVar] only supports top-level non-generic classes",
			messageFormat: "'{0}' contains [NetworkVar] properties but the containing type '{1}' is nested or generic, which is not supported yet",
			category: "NetworkVars",
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		private static readonly DiagnosticDescriptor InaccessibleFieldType = new DiagnosticDescriptor(
			id: "NETVAR004",
			title: "Field accessor cannot reference an inaccessible type",
			messageFormat: "Field '{0}' on '{1}' involves type '{2}', which is not accessible outside its declaring type; generated field accessors cannot reference it",
			category: "NetworkVars",
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		private static readonly DiagnosticDescriptor ServerField = new DiagnosticDescriptor(
			id: "NETVAR005",
			title: "Server [NetworkVar] members must be partial properties",
			messageFormat: "'{0}' is a [NetworkVar] field in server code; make it a partial property so changes are tracked",
			category: "NetworkVars",
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		public void Initialize(IncrementalGeneratorInitializationContext context) {
			IncrementalValuesProvider<PropertyModel?> properties = context.SyntaxProvider
				.ForAttributeWithMetadataName(
					AttributeMetadataName,
					predicate: static (node, _) => node is PropertyDeclarationSyntax || node is VariableDeclaratorSyntax,
					transform: static (ctx, _) => GetModel(ctx))
				.Where(static m => m != null);

			IncrementalValueProvider<ImmutableArray<PropertyModel?>> collected = properties.Collect();

			context.RegisterSourceOutput(collected, static (spc, models) => Emit(spc, models));

			InitializeFieldAccessors(context, collected, InitializeTables(context));
		}

		private static bool HasStateTracking(INamedTypeSymbol? type) {
			for (INamedTypeSymbol? t = type; t != null; t = t.BaseType) {
				foreach (ISymbol member in t.GetMembers("NetworkStateChanged")) {
					if (member is IMethodSymbol method && method.Parameters.Length == 1 && method.Parameters[0].Type.Name == "IFieldAccessor")
						return true;
				}
			}
			return false;
		}

		private static bool IsNetworkArray(ITypeSymbol type)
			=> type is INamedTypeSymbol named && named.IsGenericType && named.Name == "NetworkArray" && named.ContainingNamespace.ToDisplayString() == "Source.Common";

		private static PropertyModel? GetModel(GeneratorAttributeSyntaxContext ctx) {
			bool isClient = ctx.TargetNode.SyntaxTree.Options.PreprocessorSymbolNames.Contains("CLIENT_DLL");

			if (ctx.TargetSymbol is IFieldSymbol field) {
				if (isClient || field.IsReadOnly || !HasStateTracking(field.ContainingType) || IsNetworkArray(field.Type))
					return null;
				return PropertyModel.ServerFieldError(field.Name, field.ContainingType.Name, field.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), ctx.TargetNode.GetLocation());
			}

			if (!(ctx.TargetSymbol is IPropertySymbol prop))
				return null;

			INamedTypeSymbol containingType = prop.ContainingType;
			var propSyntax = (PropertyDeclarationSyntax)ctx.TargetNode;

			bool propertyIsPartial = propSyntax.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword));

			bool typeIsPartial = containingType.DeclaringSyntaxReferences
				.Select(r => r.GetSyntax())
				.OfType<TypeDeclarationSyntax>()
				.Any(t => t.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)));

			bool unsupported = containingType.ContainingType != null || containingType.IsGenericType;

			List<string> conversions = new List<string>();
			AddConversions(ctx.SemanticModel.Compilation, prop.Type, conversions);

			string? inlineArrayElement = GetContainer(ctx.SemanticModel.Compilation, prop.Type, out ITypeSymbol? element, out _) == ContainerKind.InlineArray && element != null
				? element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
				: null;

			return new PropertyModel(
				propertyName: prop.Name,
				networkName: GetNetworkNameAttribute(prop) ?? prop.Name,
				accessorBase: ScalarAccessorBase(ctx.SemanticModel.Compilation, prop.Type),
				conversions: string.Join("\n", conversions),
				typeDisplay: prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
				inlineArrayElement: inlineArrayElement,
				@namespace: containingType.ContainingNamespace.IsGlobalNamespace
					? null
					: containingType.ContainingNamespace.ToDisplayString(),
				typeName: containingType.Name,
				typeFullyQualified: containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
				propertyIsPartial: propertyIsPartial,
				typeIsPartial: typeIsPartial,
				unsupported: unsupported,
				isClient: isClient,
				location: propSyntax.Identifier.GetLocation()) {
				Accessibility = string.Join(" ", propSyntax.Modifiers
					.Where(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword) || m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ProtectedKeyword) || m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.InternalKeyword) || m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PrivateKeyword) || m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NewKeyword))
					.Select(m => m.Text)),
				RefReadonly = prop.ReturnsByRefReadonly,
				BaseHasFields = BaseHasNetworkVarFields(containingType.BaseType)
			};
		}

		private static bool BaseHasNetworkVarFields(INamedTypeSymbol? type) {
			for (INamedTypeSymbol? t = type; t != null; t = t.BaseType) {
				if (t.ContainingType != null || t.IsGenericType)
					continue;
				foreach (IPropertySymbol property in t.GetMembers().OfType<IPropertySymbol>()) {
					if (!property.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == AttributeMetadataName))
						continue;
					if (property.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is PropertyDeclarationSyntax syntax && syntax.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword))))
						return true;
				}
			}
			return false;
		}

		private static void Emit(SourceProductionContext spc, ImmutableArray<PropertyModel?> models) {
			foreach (var group in models
				.Where(m => m != null)
				.Select(m => m!)
				.GroupBy(m => m.TypeFullyQualified)) {

				List<PropertyModel> valid = new List<PropertyModel>();
				PropertyModel first = group.First();

				foreach (PropertyModel model in group) {
					if (model.IsServerField) {
						spc.ReportDiagnostic(Diagnostic.Create(ServerField, model.Location, model.PropertyName));
						continue;
					}
					if (!model.PropertyIsPartial) {
						spc.ReportDiagnostic(Diagnostic.Create(NotPartialProperty, model.Location, model.PropertyName));
						continue;
					}
					if (model.Unsupported) {
						spc.ReportDiagnostic(Diagnostic.Create(UnsupportedType, model.Location, model.PropertyName, model.TypeName));
						continue;
					}
					if (!model.TypeIsPartial) {
						spc.ReportDiagnostic(Diagnostic.Create(NotPartialType, model.Location, model.PropertyName, model.TypeName));
						continue;
					}
					valid.Add(model);
				}

				if (valid.Count == 0)
					continue;

				string source = BuildSource(first, valid);
				string hintName = (first.Namespace == null ? "" : first.Namespace + ".") + first.TypeName + ".NetworkVars.g.cs";
				spc.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
			}
		}

		private static string BuildSource(PropertyModel type, List<PropertyModel> props) {
			var sb = new StringBuilder();
			sb.AppendLine("// <auto-generated/>");
			sb.AppendLine("#nullable enable");
			sb.AppendLine();

			bool hasNamespace = type.Namespace != null;
			string indent = hasNamespace ? "\t\t" : "\t";

			if (hasNamespace) {
				sb.Append("namespace ").AppendLine(type.Namespace);
				sb.AppendLine("{");
			}

			string classIndent = hasNamespace ? "\t" : "";
			sb.Append(classIndent).Append("partial class ").AppendLine(type.TypeName);
			sb.Append(classIndent).AppendLine("{");

			foreach (PropertyModel p in props) {
				string t = p.TypeDisplay;
				string name = p.PropertyName;
				string backing = "__nv_" + name;

				sb.Append(indent).Append("private ").Append(t).Append(' ').Append(backing).AppendLine(";");
				string accessibility = p.Accessibility.Length == 0 ? "" : p.Accessibility + " ";
				if (p.RefReadonly) {
					sb.Append(indent).Append(accessibility).Append("partial ref readonly ").Append(t).Append(' ').Append(name).Append(" { get => ref ").Append(backing).AppendLine("; }");
					if (p.IsClient)
						sb.Append(indent).Append("public ref ").Append(t).Append(' ').Append(name).Append("ForModify() => ref ").Append(backing).AppendLine(";");
					else {
						sb.Append(indent).Append("public ref ").Append(t).Append(' ').Append(name).AppendLine("ForModify() {");
						sb.Append(indent).Append("\tNetworkStateChanged(NetworkVarFields.").Append(name).AppendLine(");");
						sb.Append(indent).Append("\treturn ref ").Append(backing).AppendLine(";");
						sb.Append(indent).AppendLine("}");
					}
					sb.AppendLine();
					continue;
				}
				sb.Append(indent).Append(accessibility).Append("partial ").Append(t).Append(' ').Append(name).AppendLine(" {");
				sb.Append(indent).Append("\tget => ").Append(backing).AppendLine(";");
				if (p.IsClient) {
					sb.Append(indent).Append("\tset => ").Append(backing).AppendLine(" = value;");
					sb.Append(indent).AppendLine("}");
					sb.Append(indent).Append("public ref ").Append(t).Append(' ').Append(name).Append("ForModify() => ref ").Append(backing).AppendLine(";");
					sb.AppendLine();
					continue;
				}
				sb.Append(indent).AppendLine("\tset {");
				if (p.InlineArrayElement == null)
					sb.Append(indent).Append("\t\tif (!global::System.Collections.Generic.EqualityComparer<").Append(t).Append(">.Default.Equals(").Append(backing).AppendLine(", value)) {");
				else {
					string e = p.InlineArrayElement;
					sb.Append(indent).Append("\t\tif (!global::System.MemoryExtensions.SequenceEqual((global::System.ReadOnlySpan<").Append(e).Append(">)").Append(backing)
						.Append(", (global::System.ReadOnlySpan<").Append(e).Append(">)value, global::System.Collections.Generic.EqualityComparer<").Append(e).AppendLine(">.Default)) {");
				}
				sb.Append(indent).Append("\t\t\t").Append(backing).AppendLine(" = value;");
				sb.Append(indent).Append("\t\t\tNetworkStateChanged(NetworkVarFields.").Append(name).AppendLine(");");
				sb.Append(indent).AppendLine("\t\t}");
				sb.Append(indent).AppendLine("\t}");
				sb.Append(indent).AppendLine("}");
				sb.Append(indent).Append("public ref ").Append(t).Append(' ').Append(name).AppendLine("ForModify() {");
				sb.Append(indent).Append("\tNetworkStateChanged(NetworkVarFields.").Append(name).AppendLine(");");
				sb.Append(indent).Append("\treturn ref ").Append(backing).AppendLine(";");
				sb.Append(indent).AppendLine("}");
				sb.AppendLine();
			}

			sb.Append(indent).AppendLine(props.Any(p => p.BaseHasFields) ? "internal static new class NetworkVarFields" : "internal static class NetworkVarFields");
			sb.Append(indent).AppendLine("{");
			foreach (PropertyModel p in props) {
				sb.Append(indent).Append("\tpublic static readonly global::Source.Common.IFieldAccessor ").Append(p.PropertyName)
					.Append(" = new __").Append(p.PropertyName).AppendLine("();");
			}
			foreach (PropertyModel p in props) {
				sb.Append(indent).Append("\tprivate sealed class __").Append(p.PropertyName).Append(" : ").AppendLine(p.AccessorBase);
				sb.Append(indent).AppendLine("\t{");
				sb.Append(indent).Append("\t\tpublic __").Append(p.PropertyName).Append("() : base(typeof(").Append(type.TypeFullyQualified).Append("), ")
					.Append(Literal(p.PropertyName)).Append(", ").Append(Literal(p.NetworkName)).AppendLine(") { }");
				sb.Append(indent).Append("\t\tpublic override ref ").Append(p.TypeDisplay).Append(" Ref(object o) => ref ((").Append(type.TypeFullyQualified).Append(")o).__nv_").Append(p.PropertyName).AppendLine(";");
				sb.Append(indent).Append("\t\tpublic override string FieldKey => ").Append(Literal("S|" + type.TypeFullyQualified + "|" + p.PropertyName)).AppendLine(";");
				sb.Append(indent).AppendLine("\t}");
			}
			sb.Append(indent).AppendLine("}");

			sb.Append(classIndent).AppendLine("}");

			if (hasNamespace)
				sb.AppendLine("}");

			return sb.ToString();
		}

		private sealed class PropertyModel
		{
			public PropertyModel(string propertyName, string networkName, string accessorBase, string conversions, string typeDisplay, string? inlineArrayElement, string? @namespace, string typeName,
				string typeFullyQualified, bool propertyIsPartial, bool typeIsPartial, bool unsupported, bool isClient, Location location) {
				PropertyName = propertyName;
				NetworkName = networkName;
				AccessorBase = accessorBase;
				Conversions = conversions;
				TypeDisplay = typeDisplay;
				InlineArrayElement = inlineArrayElement;
				Namespace = @namespace;
				TypeName = typeName;
				TypeFullyQualified = typeFullyQualified;
				PropertyIsPartial = propertyIsPartial;
				TypeIsPartial = typeIsPartial;
				Unsupported = unsupported;
				IsClient = isClient;
				Location = location;
			}

			public static PropertyModel ServerFieldError(string fieldName, string typeName, string typeFullyQualified, Location location) {
				PropertyModel model = new PropertyModel(fieldName, fieldName, "", "", "", null, null, typeName, typeFullyQualified, true, true, false, false, location);
				model.IsServerField = true;
				return model;
			}

			public string PropertyName { get; }
			public string NetworkName { get; }
			public string AccessorBase { get; }
			public string Conversions { get; }
			public string TypeDisplay { get; }
			public string? InlineArrayElement { get; }
			public string? Namespace { get; }
			public string TypeName { get; }
			public string TypeFullyQualified { get; }
			public bool PropertyIsPartial { get; }
			public bool TypeIsPartial { get; }
			public bool Unsupported { get; }
			public bool IsClient { get; }
			public bool IsServerField { get; private set; }
			public string Accessibility { get; set; } = "";
			public bool RefReadonly { get; set; }
			public bool BaseHasFields { get; set; }
			public Location Location { get; }
		}
	}
}
