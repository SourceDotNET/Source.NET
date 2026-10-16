using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

namespace Source.CodeAnalysis.NetworkClasses
{
	[Generator(LanguageNames.CSharp)]
	public sealed class NetworkClassRegistrationGenerator : IIncrementalGenerator
	{
		const string ClientClassName = "Source.Common.ClientClass";
		const string ServerClassName = "Source.Common.ServerClass";
		const string NetworkNameAttributeName = "Source.Common.NetworkNameAttribute";
		const string FactoryAttributeName = "Source.Common.ImplementClientClassFactoryAttribute";
		const string ClientEntityName = "Source.Common.IClientEntity";

		static readonly DiagnosticDescriptor DuplicateSite = new DiagnosticDescriptor(
			"SDNNC001", "Duplicate network class site",
			"Network class sites in {0} and {1} share the key {2}:{3}",
			"NetworkClasses", DiagnosticSeverity.Error, true);

		static readonly DiagnosticDescriptor InaccessibleType = new DiagnosticDescriptor(
			"SDNNC002", "Inaccessible network class type",
			"{0} declares a network class but is not accessible from generated code",
			"NetworkClasses", DiagnosticSeverity.Error, true);

		public void Initialize(IncrementalGeneratorInitializationContext context) {
			IncrementalValuesProvider<Site> sites = context.SyntaxProvider.CreateSyntaxProvider(
				static (node, _) => IsCandidate(node),
				static (ctx, ct) => Transform(ctx, ct))
				.Where(static site => site != null)
				.Select(static (site, _) => site!);

			context.RegisterSourceOutput(sites.Collect(), static (spc, items) => Emit(spc, items));
		}

		sealed class Site : System.IEquatable<Site>
		{
			public bool IsClient;
			public string File = "";
			public int Line;
			public string TypeName = "";
			public string FullyQualified = "";
			public string? NetworkName;
			public string? Factory;
			public bool Accessible;

			public bool Equals(Site? other) => other != null && IsClient == other.IsClient && File == other.File && Line == other.Line
				&& TypeName == other.TypeName && FullyQualified == other.FullyQualified && NetworkName == other.NetworkName
				&& Factory == other.Factory && Accessible == other.Accessible;

			public override bool Equals(object? obj) => Equals(obj as Site);

			public override int GetHashCode() => (FullyQualified.GetHashCode() * 397) ^ Line;
		}

		static bool IsCandidate(SyntaxNode node) {
			if (node is ImplicitObjectCreationExpressionSyntax)
				return true;

			if (node is not ObjectCreationExpressionSyntax creation)
				return false;

			string name = creation.Type switch {
				IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
				QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
				AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
				_ => ""
			};

			return IsNetworkClassName(name) || IsNetworkClassAlias(creation, name);
		}

		static bool IsNetworkClassName(string name) => name == "ClientClass" || name == "ServerClass";

		static bool IsNetworkClassAlias(SyntaxNode node, string name) {
			if (name.Length == 0)
				return false;

			foreach (SyntaxNode ancestor in node.Ancestors()) {
				SyntaxList<UsingDirectiveSyntax> usings = ancestor switch {
					CompilationUnitSyntax unit => unit.Usings,
					BaseNamespaceDeclarationSyntax ns => ns.Usings,
					_ => default
				};

				foreach (UsingDirectiveSyntax directive in usings) {
					if (directive.Alias?.Name.Identifier.ValueText != name || directive.Name == null)
						continue;

					string target = directive.Name switch {
						QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
						IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
						AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
						_ => ""
					};
					return IsNetworkClassName(target);
				}
			}
			return false;
		}

		static string FileName(string path) {
			int slash = path.LastIndexOfAny(new[] { '/', '\\' });
			return slash == -1 ? path : path.Substring(slash + 1);
		}

		static Site? Transform(GeneratorSyntaxContext ctx, CancellationToken ct) {
			BaseObjectCreationExpressionSyntax creation = (BaseObjectCreationExpressionSyntax)ctx.Node;
			if (ctx.SemanticModel.GetSymbolInfo(creation, ct).Symbol is not IMethodSymbol constructor)
				return null;

			string kind = constructor.ContainingType.ToDisplayString();
			bool isClient = kind == ClientClassName;
			if (!isClient && kind != ServerClassName)
				return null;

			ISymbol? enclosing = ctx.SemanticModel.GetEnclosingSymbol(creation.SpanStart, ct);
			INamedTypeSymbol? owner = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType;
			if (owner == null)
				return null;

			Compilation compilation = ctx.SemanticModel.Compilation;

			return new Site {
				IsClient = isClient,
				File = FileName(creation.SyntaxTree.FilePath),
				Line = creation.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
				TypeName = owner.Name,
				FullyQualified = owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
				NetworkName = GetNetworkName(owner),
				Factory = isClient ? GetFactory(owner) : null,
				Accessible = compilation.IsSymbolAccessibleWithin(owner, compilation.Assembly),
			};
		}

		static string? GetNetworkName(INamedTypeSymbol owner) {
			foreach (AttributeData attribute in owner.GetAttributes()) {
				if (attribute.AttributeClass?.ToDisplayString() == NetworkNameAttributeName && attribute.ConstructorArguments.Length == 1)
					return attribute.ConstructorArguments[0].Value as string;
			}
			return null;
		}

		static string? GetFactory(INamedTypeSymbol owner) {
			string fullyQualified = owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

			foreach (IMethodSymbol method in owner.GetMembers().OfType<IMethodSymbol>()) {
				if (method.IsStatic && method.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == FactoryAttributeName))
					return $"{fullyQualified}.{method.Name}";
			}

			if (owner.IsAbstract || owner.IsGenericType)
				return null;

			if (!owner.AllInterfaces.Any(i => i.ToDisplayString() == ClientEntityName))
				return null;

			if (!owner.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0))
				return null;

			if (!HasInit(owner))
				return null;

			return $"static (entNum, serialNum) => {{ {fullyQualified} entity = new {fullyQualified}(); return entity.Init(entNum, serialNum) ? entity : null!; }}";
		}

		static bool HasInit(INamedTypeSymbol owner) {
			for (INamedTypeSymbol? type = owner; type != null; type = type.BaseType) {
				foreach (IMethodSymbol method in type.GetMembers("Init").OfType<IMethodSymbol>()) {
					if (!method.IsStatic && method.DeclaredAccessibility == Accessibility.Public
						&& method.ReturnType.SpecialType == SpecialType.System_Boolean && method.Parameters.Length == 2
						&& method.Parameters[0].Type.SpecialType == SpecialType.System_Int32
						&& method.Parameters[1].Type.SpecialType == SpecialType.System_Int32)
						return true;
				}
			}
			return false;
		}

		static string Literal(string? value) => value == null ? "null" : SymbolDisplay.FormatLiteral(value, true);

		static void Emit(SourceProductionContext spc, ImmutableArray<Site> items) {
			if (items.IsEmpty)
				return;

			List<Site> sorted = items.OrderBy(s => s.IsClient ? 0 : 1).ThenBy(s => s.File, System.StringComparer.Ordinal).ThenBy(s => s.Line).ToList();

			bool failed = false;
			Dictionary<(bool, string, int), Site> seen = new Dictionary<(bool, string, int), Site>();
			foreach (Site site in sorted) {
				if (!site.Accessible) {
					spc.ReportDiagnostic(Diagnostic.Create(InaccessibleType, Location.None, site.FullyQualified));
					failed = true;
				}

				if (seen.TryGetValue((site.IsClient, site.File, site.Line), out Site? existing)) {
					spc.ReportDiagnostic(Diagnostic.Create(DuplicateSite, Location.None, existing.FullyQualified, site.FullyQualified, site.File, site.Line));
					failed = true;
				}
				else
					seen.Add((site.IsClient, site.File, site.Line), site);
			}

			if (failed)
				return;

			StringBuilder sb = new StringBuilder();
			sb.AppendLine("// <auto-generated/>");
			sb.AppendLine("#nullable enable");
			sb.AppendLine("#pragma warning disable CA2255");
			sb.AppendLine("namespace Source.Generated");
			sb.AppendLine("{");
			sb.AppendLine("\tinternal static class NetworkClassRegistration");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\t[global::System.Runtime.CompilerServices.ModuleInitializer]");
			sb.AppendLine("\t\tinternal static void Register()");
			sb.AppendLine("\t\t{");

			foreach (Site site in sorted) {
				if (site.IsClient)
					sb.Append($"\t\t\tglobal::Source.Common.ClientClass.RegisterSite({Literal(site.File)}, {site.Line}, {Literal(site.TypeName)}, {Literal(site.NetworkName)}, {site.Factory ?? "null"});\n");
				else
					sb.Append($"\t\t\tglobal::Source.Common.ServerClass.RegisterSite({Literal(site.File)}, {site.Line}, {Literal(site.TypeName)}, {Literal(site.NetworkName)});\n");
			}

			sb.AppendLine("\t\t}");
			sb.AppendLine("\t}");
			sb.AppendLine("}");

			spc.AddSource("NetworkClassRegistration.g.cs", sb.ToString());
		}
	}
}
