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

		public void Initialize(IncrementalGeneratorInitializationContext context) {
			context.RegisterPostInitializationOutput(ctx =>
				ctx.AddSource("NetworkVarAttribute.g.cs", SourceText.From(AttributeSource, Encoding.UTF8)));

			IncrementalValuesProvider<PropertyModel?> properties = context.SyntaxProvider
				.ForAttributeWithMetadataName(
					AttributeMetadataName,
					predicate: static (node, _) => node is PropertyDeclarationSyntax,
					transform: static (ctx, _) => GetModel(ctx))
				.Where(static m => m != null);

			IncrementalValueProvider<ImmutableArray<PropertyModel?>> collected = properties.Collect();

			context.RegisterSourceOutput(collected, static (spc, models) => Emit(spc, models));

			InitializeFieldAccessors(context, collected);
		}

		private static PropertyModel? GetModel(GeneratorAttributeSyntaxContext ctx) {
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
				location: propSyntax.Identifier.GetLocation());
		}

		private static void Emit(SourceProductionContext spc, ImmutableArray<PropertyModel?> models) {
			foreach (var group in models
				.Where(m => m != null)
				.Select(m => m!)
				.GroupBy(m => m.TypeFullyQualified)) {

				List<PropertyModel> valid = new List<PropertyModel>();
				PropertyModel first = group.First();

				foreach (PropertyModel model in group) {
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
				sb.Append(indent).Append("public partial ").Append(t).Append(' ').Append(name).AppendLine(" {");
				sb.Append(indent).Append("\tget => ").Append(backing).AppendLine(";");
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

			sb.Append(indent).AppendLine("internal static class NetworkVarFields");
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

		private const string AttributeSource =
@"// <auto-generated/>
#nullable enable
namespace Source.Common
{
	/// <summary>
	/// Marks a partial property as a networked var. The NetworkVar source generator emits the
	/// backing field, a change-checked setter that calls NetworkStateChanged, and a shared
	/// IFieldAccessor (exposed via the nested NetworkVarFields class) for the send table to use.
	/// </summary>
	[global::System.AttributeUsage(global::System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
	internal sealed class NetworkVarAttribute : global::System.Attribute
	{
	}
}";

		private sealed class PropertyModel
		{
			public PropertyModel(string propertyName, string networkName, string accessorBase, string conversions, string typeDisplay, string? inlineArrayElement, string? @namespace, string typeName,
				string typeFullyQualified, bool propertyIsPartial, bool typeIsPartial, bool unsupported, Location location) {
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
				Location = location;
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
			public Location Location { get; }
		}
	}
}
