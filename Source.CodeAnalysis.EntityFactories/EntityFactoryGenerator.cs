using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Source.CodeAnalysis.EntityFactories
{
	[Generator(LanguageNames.CSharp)]
	public sealed class EntityFactoryGenerator : IIncrementalGenerator
	{
		const string LinkAttribute = "Game.Shared.LinkEntityToClassAttribute";
		const string ClientClassMap = "Game.Client.ClassMap";
		const string ClientBaseEntity = "Game.Client.C_BaseEntity";
		const string ServerFactory = "Game.CEntityFactory`1";
		const string G = "global::";

		static readonly DiagnosticDescriptor Skipped = new DiagnosticDescriptor(
			id: "ENTFACT001",
			title: "Linked entity class cannot get a generated factory",
			messageFormat: "'{0}' is linked to '{1}' but {2}, so no factory is registered",
			category: "EntityFactories",
			defaultSeverity: DiagnosticSeverity.Warning,
			isEnabledByDefault: true);

		enum Side { None, Client, Server }
		enum Ctor { Missing, Public, Internal, Hidden }

		sealed class Entry : IEquatable<Entry>
		{
			public string TypeName = "";
			public string Name = "";
			public string[] LocalNames = Array.Empty<string>();
			public string[] Bases = Array.Empty<string>();
			public bool IsGeneric;
			public bool IsAbstract;
			public bool IsAccessible;
			public Ctor Ctor;
			public Location? Location;

			public bool Equals(Entry? other) =>
				other != null && TypeName == other.TypeName && Name == other.Name && IsGeneric == other.IsGeneric && IsAbstract == other.IsAbstract
				&& IsAccessible == other.IsAccessible && Ctor == other.Ctor && LocalNames.SequenceEqual(other.LocalNames)
				&& Bases.SequenceEqual(other.Bases) && Equals(Location, other.Location);
			public override bool Equals(object? obj) => Equals(obj as Entry);
			public override int GetHashCode() => TypeName.GetHashCode();
		}

		public void Initialize(IncrementalGeneratorInitializationContext context) {
			IncrementalValuesProvider<Entry> entries = context.SyntaxProvider.ForAttributeWithMetadataName(
				LinkAttribute,
				static (node, _) => true,
				static (ctx, _) => Transform(ctx))
				.Where(static e => e != null)
				.Select(static (e, _) => e!);

			IncrementalValueProvider<(Side, string)> side = context.CompilationProvider.Select(static (compilation, _) => DetectSide(compilation));

			context.RegisterSourceOutput(entries.Collect().Combine(side), static (spc, input) => Execute(spc, input.Left, input.Right.Item1, input.Right.Item2));
		}

		static (Side, string) DetectSide(Compilation compilation) {
			if (compilation.GetTypeByMetadataName(ClientClassMap) != null) {
				INamedTypeSymbol? baseEntity = compilation.GetTypeByMetadataName(ClientBaseEntity);
				return baseEntity == null ? (Side.None, "") : (Side.Client, baseEntity.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
			}

			INamedTypeSymbol? factory = compilation.GetTypeByMetadataName(ServerFactory);
			if (factory != null) {
				ITypeSymbol? constraint = factory.TypeParameters[0].ConstraintTypes.FirstOrDefault();
				return (Side.Server, constraint?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "");
			}

			return (Side.None, "");
		}

		static Entry? Transform(GeneratorAttributeSyntaxContext ctx) {
			if (!(ctx.TargetSymbol is INamedTypeSymbol type) || type.TypeKind != TypeKind.Class)
				return null;

			string[] names = ctx.Attributes
				.Where(a => a.ConstructorArguments.Length == 1 && a.ConstructorArguments[0].Value is string)
				.Select(a => (string)a.ConstructorArguments[0].Value!)
				.ToArray();
			if (names.Length == 0)
				return null;

			List<string> bases = new List<string>();
			for (INamedTypeSymbol? t = type.BaseType; t != null; t = t.BaseType)
				bases.Add(t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

			return new Entry {
				TypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
				Name = type.Name,
				LocalNames = names,
				Bases = bases.ToArray(),
				IsGeneric = IsGeneric(type),
				IsAbstract = type.IsAbstract,
				IsAccessible = IsAccessible(type),
				Ctor = ParameterlessCtor(type),
				Location = ctx.TargetNode.GetLocation(),
			};
		}

		static bool IsGeneric(INamedTypeSymbol type) {
			for (INamedTypeSymbol? t = type; t != null; t = t.ContainingType)
				if (t.IsGenericType)
					return true;
			return false;
		}

		static bool IsAccessible(INamedTypeSymbol type) {
			for (INamedTypeSymbol? t = type; t != null; t = t.ContainingType) {
				Accessibility a = t.DeclaredAccessibility;
				if (a != Accessibility.Public && a != Accessibility.Internal && a != Accessibility.ProtectedOrInternal)
					return false;
			}
			return true;
		}

		static Ctor ParameterlessCtor(INamedTypeSymbol type) {
			IMethodSymbol? ctor = type.InstanceConstructors.FirstOrDefault(c => c.Parameters.Length == 0);
			if (ctor == null)
				return Ctor.Missing;
			switch (ctor.DeclaredAccessibility) {
				case Accessibility.Public: return Ctor.Public;
				case Accessibility.Internal:
				case Accessibility.ProtectedOrInternal: return Ctor.Internal;
				default: return Ctor.Hidden;
			}
		}

		static string? Problem(Entry e, Side side, string baseEntity) {
			if (e.IsGeneric) return "is generic";
			if (e.IsAbstract) return "is abstract";
			if (!e.IsAccessible) return "is not accessible from generated code";
			if (baseEntity.Length != 0 && e.TypeName != baseEntity && !e.Bases.Contains(baseEntity)) return "does not derive from " + baseEntity;
			if (side == Side.Server && e.Ctor != Ctor.Public) return "has no public parameterless constructor";
			if (e.Ctor == Ctor.Missing) return "has no parameterless constructor";
			return null;
		}

		static void Execute(SourceProductionContext spc, ImmutableArray<Entry> entries, Side side, string baseEntity) {
			if (side == Side.None)
				return;

			StringBuilder accessors = new StringBuilder();
			StringBuilder body = new StringBuilder();
			int accessorId = 0;

			foreach (Entry e in entries) {
				string? problem = Problem(e, side, baseEntity);
				if (problem != null) {
					spc.ReportDiagnostic(Diagnostic.Create(Skipped, e.Location, e.TypeName, e.LocalNames[0], problem));
					continue;
				}

				if (side == Side.Server) {
					foreach (string name in e.LocalNames)
						body.AppendLine($"\t\tnew {G}Game.CEntityFactory<{e.TypeName}>({Literal(name)});");
					continue;
				}

				string factory;
				if (e.Ctor == Ctor.Hidden) {
					string accessor = "__Create" + accessorId++;
					accessors.AppendLine("\t[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Constructor)]");
					accessors.AppendLine($"\tstatic extern {e.TypeName} {accessor}();");
					accessors.AppendLine();
					factory = $"static () => {accessor}()";
				}
				else
					factory = $"static () => new {e.TypeName}()";

				foreach (string name in e.LocalNames)
					body.AppendLine($"\t\t{G}Game.Client.ClassMap.GetClassMap().Add(typeof({e.TypeName}), {Literal(name)}, {Literal(e.Name)}, {factory});");
			}

			StringBuilder sb = new StringBuilder();
			sb.AppendLine("// <auto-generated/>");
			sb.AppendLine("#nullable enable");
			sb.AppendLine("#pragma warning disable CA2255");
			sb.AppendLine();
			sb.AppendLine("namespace Source.Generated;");
			sb.AppendLine();
			sb.AppendLine("internal static class EntityFactoryRegistration");
			sb.AppendLine("{");
			sb.Append(accessors);
			sb.AppendLine("\t[global::System.Runtime.CompilerServices.ModuleInitializer]");
			sb.AppendLine("\tinternal static void Register() {");
			sb.Append(body);
			sb.AppendLine("\t}");
			sb.AppendLine("}");

			spc.AddSource("EntityFactoryRegistration.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
		}

		static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);
	}
}
