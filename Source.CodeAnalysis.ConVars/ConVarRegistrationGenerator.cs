using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Source.CodeAnalysis.ConVars
{
	/// <summary>
	/// Emits Source.Generated.CvarRegistration for every [SourceDll] assembly. The engine uses it in place of the
	/// reflection sweep in EngineBuilder.ConVar_Register: one registrar per type that declares ConVar fields/properties
	/// or [ConCommand] methods. Private members are reached with [UnsafeAccessor], so the declaring code needs no changes.
	/// Anything the generator cannot express exactly is listed for the engine to handle through reflection instead.
	/// </summary>
	[Generator(LanguageNames.CSharp)]
	public sealed class ConVarRegistrationGenerator : IIncrementalGenerator
	{
		const string Commands = "Source.Common.Commands.";
		const string G = "global::";
		const string CompilerServices = "global::System.Runtime.CompilerServices.";
		const string RegistrarType = "global::System.Action<int, global::System.Type, global::System.Type, global::Source.Common.Commands.ICvar, global::System.Func<global::System.Type, bool, string, object>>";

		public void Initialize(IncrementalGeneratorInitializationContext context) {
			context.RegisterSourceOutput(context.CompilationProvider, static (spc, compilation) => Execute(spc, compilation));
		}

		sealed class Symbols
		{
			public Compilation Compilation = null!;
			public INamedTypeSymbol ConVar = null!;
			public INamedTypeSymbol? ConVarRef;
			public INamedTypeSymbol ConCommandAttribute = null!;
			public INamedTypeSymbol? CvarIgnoreAttribute;
			public INamedTypeSymbol TokenizedCommand = null!;
			public INamedTypeSymbol CommandSource = null!;
			public INamedTypeSymbol? EnumerableOfString;
			public bool HasUnsafeAccessorType;
			public string AssemblyName = "";
		}

		enum MemberKind { Property, Field, Method }

		sealed class Completion
		{
			public string Name = "";
			public bool Direct;
			public string ReturnType = "";
		}

		sealed class Member
		{
			public MemberKind Kind;
			public string Name = "";
			public bool IsStatic;
			public bool Inheritable;
			public bool Direct;
			public string ValueType = "";
			public int Callback;
			public string CommandName = "";
			public string HelpText = "null";
			public string Flags = "0";
			public Completion? Completion;
			public int Id;
		}

		sealed class TypeModel
		{
			public string MetadataName = "";
			public string FullyQualified = "";
			public readonly List<Member> Members = new List<Member>();
		}

		static void Execute(SourceProductionContext spc, Compilation compilation) {
			if (!compilation.Assembly.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Source.Common.SourceDllAttribute"))
				return;

			INamedTypeSymbol? conVar = compilation.GetTypeByMetadataName(Commands + "ConVar");
			INamedTypeSymbol? conCommandAttribute = compilation.GetTypeByMetadataName(Commands + "ConCommandAttribute");
			INamedTypeSymbol? tokenizedCommand = compilation.GetTypeByMetadataName(Commands + "TokenizedCommand");
			INamedTypeSymbol? commandSource = compilation.GetTypeByMetadataName(Commands + "CommandSource");
			if (conVar == null || conCommandAttribute == null || tokenizedCommand == null || commandSource == null || compilation.GetTypeByMetadataName(Commands + "ICvar") == null)
				return;

			Symbols s = new Symbols {
				Compilation = compilation,
				ConVar = conVar,
				ConVarRef = compilation.GetTypeByMetadataName(Commands + "ConVarRef"),
				ConCommandAttribute = conCommandAttribute,
				CvarIgnoreAttribute = compilation.GetTypeByMetadataName(Commands + "CvarIgnoreAttribute"),
				TokenizedCommand = tokenizedCommand,
				CommandSource = commandSource,
				EnumerableOfString = compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1")?.Construct(compilation.GetSpecialType(SpecialType.System_String)),
				HasUnsafeAccessorType = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.UnsafeAccessorTypeAttribute") != null,
				AssemblyName = compilation.AssemblyName ?? "",
			};

			List<TypeModel> models = new List<TypeModel>();
			List<string> reflected = new List<string>();
			int nextId = 0;
			foreach (INamedTypeSymbol type in AllTypes(compilation.Assembly.GlobalNamespace)) {
				spc.CancellationToken.ThrowIfCancellationRequested();
				Analyze(s, type, models, reflected, ref nextId);
			}

			spc.AddSource("CvarRegistration.g.cs", SourceText.From(Emit(s, models, reflected), Encoding.UTF8));
		}

		static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns) {
			foreach (INamedTypeSymbol type in ns.GetTypeMembers())
				foreach (INamedTypeSymbol t in SelfAndNested(type))
					yield return t;

			foreach (INamespaceSymbol child in ns.GetNamespaceMembers())
				foreach (INamedTypeSymbol t in AllTypes(child))
					yield return t;
		}

		static IEnumerable<INamedTypeSymbol> SelfAndNested(INamedTypeSymbol type) {
			yield return type;
			foreach (INamedTypeSymbol nested in type.GetTypeMembers())
				foreach (INamedTypeSymbol t in SelfAndNested(nested))
					yield return t;
		}

		static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol target) {
			for (ITypeSymbol? t = type; t != null; t = t.BaseType)
				if (SymbolEqualityComparer.Default.Equals(t, target))
					return true;
			return false;
		}

		static bool IsConVarType(Symbols s, ITypeSymbol type) {
			if (type is ITypeParameterSymbol tp)
				return tp.ConstraintTypes.Any(c => IsConVarType(s, c));
			return DerivesFrom(type, s.ConVar);
		}

		static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attribute)
			=> attribute != null && symbol.GetAttributes().Any(a => DerivesFrom(a.AttributeClass, attribute));

		static AttributeData? FindAttribute(ISymbol symbol, INamedTypeSymbol attribute)
			=> symbol.GetAttributes().FirstOrDefault(a => DerivesFrom(a.AttributeClass, attribute));

		static string MetadataName(INamedTypeSymbol type) {
			if (type.ContainingType != null)
				return MetadataName(type.ContainingType) + "+" + type.MetadataName;
			if (type.ContainingNamespace == null || type.ContainingNamespace.IsGlobalNamespace)
				return type.MetadataName;
			return type.ContainingNamespace.ToDisplayString() + "." + type.MetadataName;
		}

		static string Fq(ITypeSymbol type) => type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);

		static bool IsGeneric(INamedTypeSymbol type) {
			for (INamedTypeSymbol? t = type; t != null; t = t.ContainingType)
				if (t.Arity > 0)
					return true;
			return false;
		}

		static bool IsNameable(Symbols s, ITypeSymbol type) {
			switch (type) {
				case IArrayTypeSymbol array:
					return IsNameable(s, array.ElementType);
				case INamedTypeSymbol named:
					for (INamedTypeSymbol? t = named; t != null; t = t.ContainingType)
						if (t.IsFileLocal || t.IsAnonymousType)
							return false;
					if (!s.Compilation.IsSymbolAccessibleWithin(named, s.Compilation.Assembly))
						return false;
					return named.TypeArguments.All(a => IsNameable(s, a));
				default:
					return false;
			}
		}

		static bool CapturesConVarInPrimaryConstructor(Symbols s, INamedTypeSymbol type) {
			foreach (IMethodSymbol ctor in type.InstanceConstructors) {
				if (!ctor.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is TypeDeclarationSyntax))
					continue;
				if (ctor.Parameters.Any(p => IsConVarType(s, p.Type)))
					return true;
			}
			return false;
		}

		static void Analyze(Symbols s, INamedTypeSymbol type, List<TypeModel> models, List<string> reflected, ref int nextId) {
			if (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct && type.TypeKind != TypeKind.Interface)
				return;
			if (DerivesFrom(type, s.ConVar) || (s.ConVarRef != null && SymbolEqualityComparer.Default.Equals(type, s.ConVarRef)))
				return;

			bool nameable = IsNameable(s, type);
			bool isStruct = type.TypeKind == TypeKind.Struct;
			bool isInterface = type.TypeKind == TypeKind.Interface;
			bool candidates = false;
			bool reflect = false;
			TypeModel model = new TypeModel { MetadataName = MetadataName(type), FullyQualified = Fq(type) };

			if (type.TypeKind != TypeKind.Interface && CapturesConVarInPrimaryConstructor(s, type)) {
				candidates = true;
				reflect = true;
			}

			foreach (ISymbol symbol in type.GetMembers()) {
				switch (symbol) {
					case IPropertySymbol property: {
							if (property.IsIndexer || !IsConVarType(s, property.Type) || HasAttribute(property, s.CvarIgnoreAttribute))
								break;
							if (property.GetMethod == null || property.GetMethod.DeclaredAccessibility != Accessibility.Public)
								break;

							candidates = true;
							if (isInterface ? property.IsStatic : (property.IsVirtual || property.IsAbstract || property.IsOverride))
								reflect = true;
							if (isStruct && !property.IsStatic)
								reflect = true;
							if (!nameable && !CanUseAccessor(s, type, property.Type))
								reflect = true;

							model.Members.Add(new Member {
								Kind = MemberKind.Property,
								Name = property.Name,
								IsStatic = property.IsStatic,
								Inheritable = !property.IsStatic && !isInterface,
								Direct = nameable,
								ValueType = Fq(property.Type),
								Id = nextId++,
							});
							break;
						}
					case IFieldSymbol field: {
							if (!IsConVarType(s, field.Type) || HasAttribute(field, s.CvarIgnoreAttribute))
								break;

							candidates = true;
							if (isStruct && !field.IsStatic)
								reflect = true;

							bool direct = nameable && field.AssociatedSymbol == null && field.CanBeReferencedByName && s.Compilation.IsSymbolAccessibleWithin(field, s.Compilation.Assembly);
							if (!direct && !CanUseAccessor(s, type, field.Type))
								reflect = true;

							model.Members.Add(new Member {
								Kind = MemberKind.Field,
								Name = field.Name,
								IsStatic = field.IsStatic,
								Inheritable = !field.IsStatic && field.DeclaredAccessibility != Accessibility.Private,
								Direct = direct,
								ValueType = Fq(field.Type),
								Id = nextId++,
							});
							break;
						}
					case IMethodSymbol method: {
							AttributeData? attribute = FindAttribute(method, s.ConCommandAttribute);
							if (attribute == null) {
								for (IMethodSymbol? overridden = method.OverriddenMethod; overridden != null; overridden = overridden.OverriddenMethod) {
									if (FindAttribute(overridden, s.ConCommandAttribute) != null) {
										candidates = true;
										reflect = true;
										break;
									}
								}
								break;
							}

							candidates = true;
							Member? member = AnalyzeCommand(s, type, method, attribute, nameable, isStruct);
							if (member == null)
								reflect = true;
							else {
								member.Id = nextId++;
								model.Members.Add(member);
							}
							break;
						}
				}
			}

			if (!candidates)
				return;

			if (reflect || IsGeneric(type))
				reflected.Add(model.MetadataName);
			else
				models.Add(model);
		}

		static bool CanUseAccessor(Symbols s, INamedTypeSymbol owner, ITypeSymbol valueType)
			=> s.HasUnsafeAccessorType && owner.TypeKind == TypeKind.Class && !IsGeneric(owner) && IsNameable(s, valueType);

		static Member? AnalyzeCommand(Symbols s, INamedTypeSymbol type, IMethodSymbol method, AttributeData attribute, bool nameable, bool isStruct) {
			if (method.MethodKind != MethodKind.Ordinary || method.IsGenericMethod || !method.ReturnsVoid)
				return null;
			if (method.IsVirtual || method.IsAbstract || method.IsOverride || type.TypeKind == TypeKind.Interface)
				return null;
			if (isStruct && !method.IsStatic)
				return null;
			if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, s.ConCommandAttribute) || attribute.ConstructorArguments.Length != 4 || attribute.NamedArguments.Length != 0)
				return null;

			int callback;
			var parameters = method.Parameters;
			if (parameters.Length == 0)
				callback = 0;
			else if (parameters.Length == 1 && IsCommandParameter(s, parameters[0]))
				callback = 1;
			else if (parameters.Length == 3 && IsCommandParameter(s, parameters[0])
				&& parameters[1].RefKind == RefKind.None && SymbolEqualityComparer.Default.Equals(parameters[1].Type, s.CommandSource)
				&& parameters[2].RefKind == RefKind.None && parameters[2].Type.SpecialType == SpecialType.System_Int32)
				callback = 2;
			else
				return null;

			TypedConstant name = attribute.ConstructorArguments[0];
			TypedConstant help = attribute.ConstructorArguments[1];
			TypedConstant flags = attribute.ConstructorArguments[2];
			TypedConstant complete = attribute.ConstructorArguments[3];
			if (name.Kind == TypedConstantKind.Error || help.Kind == TypedConstantKind.Error || flags.Kind == TypedConstantKind.Error || complete.Kind == TypedConstantKind.Error || flags.Type == null)
				return null;

			bool direct = nameable && s.Compilation.IsSymbolAccessibleWithin(method, s.Compilation.Assembly);
			if (!direct && !CanUseAccessor(s, type, s.TokenizedCommand))
				return null;

			Member member = new Member {
				Kind = MemberKind.Method,
				Name = method.Name,
				IsStatic = method.IsStatic,
				Inheritable = !method.IsStatic && method.DeclaredAccessibility != Accessibility.Private,
				Direct = direct,
				Callback = callback,
				CommandName = Literal(name.Value as string ?? method.Name),
				HelpText = help.Value is string helpText ? Literal(helpText) : "null",
				Flags = "unchecked((" + Fq(flags.Type) + ")(" + (flags.Value is ulong u ? u.ToString() + "UL" : Convert.ToInt64(flags.Value ?? 0).ToString() + "L") + "))",
			};

			if (complete.Value is string completeName) {
				IMethodSymbol[] found = type.GetMembers(completeName).OfType<IMethodSymbol>().ToArray();
				if (found.Length != 1)
					return null;

				IMethodSymbol completion = found[0];
				if (completion.MethodKind != MethodKind.Ordinary || completion.IsGenericMethod)
					return null;

				bool bindable = completion.IsStatic == method.IsStatic && s.EnumerableOfString != null && !completion.ReturnsVoid
					&& completion.Parameters.Length == 1 && completion.Parameters[0].RefKind == RefKind.None;
				if (bindable && completion.Parameters[0].Type.SpecialType != SpecialType.System_String)
					return null;

				if (bindable) {
					Conversion conversion = ((CSharpCompilation)s.Compilation).ClassifyConversion(completion.ReturnType, s.EnumerableOfString!);
					if (!conversion.IsIdentity && !(conversion.IsImplicit && conversion.IsReference))
						bindable = false;
				}

				if (bindable) {
					bool completionDirect = nameable && s.Compilation.IsSymbolAccessibleWithin(completion, s.Compilation.Assembly);
					if (!completionDirect && !CanUseAccessor(s, type, completion.ReturnType))
						return null;

					member.Completion = new Completion { Name = completion.Name, Direct = completionDirect, ReturnType = Fq(completion.ReturnType) };
				}
			}

			return member;
		}

		static bool IsCommandParameter(Symbols s, IParameterSymbol parameter)
			=> parameter.RefKind == RefKind.In && SymbolEqualityComparer.Default.Equals(parameter.Type, s.TokenizedCommand);

		static string Emit(Symbols s, List<TypeModel> models, List<string> reflected) {
			StringBuilder sb = new StringBuilder();
			StringBuilder accessors = new StringBuilder();
			sb.Append("// <auto-generated/>\n#nullable disable\n#pragma warning disable\n\nnamespace Source.Generated\n{\n");
			sb.Append("\tinternal static class CvarRegistration\n\t{\n");
			sb.Append("\t\tpublic static void Populate(global::System.Collections.Generic.Dictionary<string, ").Append(RegistrarType).Append("> declared, global::System.Collections.Generic.Dictionary<string, ").Append(RegistrarType).Append("> inherited, global::System.Collections.Generic.List<string> reflected) {\n");
			for (int i = 0; i < models.Count; i++) {
				sb.Append("\t\t\tdeclared.Add(").Append(Literal(models[i].MetadataName)).Append(", D").Append(i).Append(");\n");
				if (models[i].Members.Any(m => m.Inheritable))
					sb.Append("\t\t\tinherited.Add(").Append(Literal(models[i].MetadataName)).Append(", I").Append(i).Append(");\n");
			}
			foreach (string name in reflected)
				sb.Append("\t\t\treflected.Add(").Append(Literal(name)).Append(");\n");
			sb.Append("\t\t}\n\n");

			sb.Append("\t\tstatic object Inst(global::System.Func<global::System.Type, bool, string, object> resolve, global::System.Type declaring, global::System.Type type, bool concommand, string name) {\n");
			sb.Append("\t\t\tobject instance = resolve(type, concommand, name);\n");
			sb.Append("\t\t\tif (!declaring.IsInstanceOfType(instance))\n");
			sb.Append("\t\t\t\tthrow new global::System.InvalidCastException($\"The instance resolved for '{type}' is not a '{declaring}', which declares '{name}'.\");\n");
			sb.Append("\t\t\treturn instance;\n\t\t}\n\n");
			sb.Append("\t\tstatic void Reg(global::Source.Common.Commands.ICvar cvar, global::Source.Common.Commands.ConVar cv, string name) {\n");
			sb.Append("\t\t\tif (cv == null)\n\t\t\t\treturn;\n");
			sb.Append("\t\t\tif (cv.GetName() == null)\n\t\t\t\tcv.SetName(name);\n");
			sb.Append("\t\t\tcvar.RegisterConCommand(cv);\n\t\t}\n");

			for (int i = 0; i < models.Count; i++) {
				EmitRegistrar(s, sb, accessors, models[i], "D" + i, inheritedOnly: false);
				if (models[i].Members.Any(m => m.Inheritable))
					EmitRegistrar(s, sb, accessors, models[i], "I" + i, inheritedOnly: true);
			}

			sb.Append(accessors);
			sb.Append("\t}\n}\n");
			return sb.ToString();
		}

		static void EmitRegistrar(Symbols s, StringBuilder sb, StringBuilder accessors, TypeModel model, string name, bool inheritedOnly) {
			sb.Append("\n\t\t// ").Append(model.MetadataName).Append(inheritedOnly ? " (members inherited by derived types)" : "").Append('\n');
			sb.Append("\t\tstatic void ").Append(name).Append("(int phase, global::System.Type declaring, global::System.Type type, global::Source.Common.Commands.ICvar cvar, global::System.Func<global::System.Type, bool, string, object> resolve) {\n");
			sb.Append("\t\t\tswitch (phase) {\n");
			foreach (MemberKind kind in new[] { MemberKind.Property, MemberKind.Field, MemberKind.Method }) {
				// The runtime lists a type's instance fields before its static fields; keep that order so that
				// duplicate names resolve to the same parent ConVar as they do through reflection.
				Member[] members = model.Members.Where(m => m.Kind == kind && (!inheritedOnly || m.Inheritable))
					.OrderBy(m => kind == MemberKind.Field && m.IsStatic ? 1 : 0).ToArray();
				if (members.Length == 0)
					continue;

				sb.Append("\t\t\t\tcase ").Append((int)kind).Append(":\n");
				foreach (Member member in members) {
					if (!inheritedOnly)
						EmitAccessors(s, accessors, model, member);
					if (kind == MemberKind.Method)
						EmitCommand(sb, model, member);
					else
						EmitConVar(sb, model, member);
				}
				sb.Append("\t\t\t\t\tbreak;\n");
			}
			sb.Append("\t\t\t}\n\t\t}\n");
		}

		static string Id(string name) => "@" + name;

		static void EmitConVar(StringBuilder sb, TypeModel model, Member member) {
			string nameLiteral = Literal(member.Name);
			string value;
			if (member.Direct)
				value = (member.IsStatic ? model.FullyQualified : "((" + model.FullyQualified + ")i)") + "." + Id(member.Name);
			else
				value = "A" + member.Id + "(" + (member.IsStatic ? "null" : "i") + ")";

			if (member.IsStatic)
				sb.Append("\t\t\t\t\tReg(cvar, ").Append(value).Append(", ").Append(nameLiteral).Append(");\n");
			else
				sb.Append("\t\t\t\t\t{ object i = Inst(resolve, declaring, type, false, ").Append(nameLiteral).Append("); Reg(cvar, ").Append(value).Append(", ").Append(nameLiteral).Append("); }\n");
		}

		static void EmitCommand(StringBuilder sb, TypeModel model, Member member) {
			string target = member.IsStatic ? model.FullyQualified : "((" + model.FullyQualified + ")i)";
			string arg = member.IsStatic ? "null" : "i";
			string callbackType = G + Commands + (member.Callback == 0 ? "FnCommandCallbackVoid" : member.Callback == 1 ? "FnCommandCallback" : "FnCommandCallbackSourced");
			string tokenized = G + Commands + "TokenizedCommand";
			string source = G + Commands + "CommandSource";

			string callback;
			if (member.Direct)
				callback = target + "." + Id(member.Name);
			else if (member.Callback == 0)
				callback = "() => A" + member.Id + "(" + arg + ")";
			else if (member.Callback == 1)
				callback = "(in " + tokenized + " a) => A" + member.Id + "(" + arg + ", in a)";
			else
				callback = "(in " + tokenized + " a, " + source + " b, int c) => A" + member.Id + "(" + arg + ", in a, b, c)";

			string completion = "null";
			if (member.Completion != null) {
				completion = "(" + G + Commands + "FnCommandCompletionCallback)(" + (member.Completion.Direct
					? target + "." + Id(member.Completion.Name)
					: "(string a) => C" + member.Id + "(" + arg + ", a)") + ")";
			}

			sb.Append("\t\t\t\t\t{ ");
			if (!member.IsStatic)
				sb.Append("object i = Inst(resolve, declaring, type, true, ").Append(Literal(member.Name)).Append("); ");
			sb.Append("cvar.RegisterConCommand(new ").Append(G).Append(Commands).Append("ConCommand(").Append(member.CommandName)
				.Append(", (").Append(callbackType).Append(")(").Append(callback).Append("), ")
				.Append(member.HelpText).Append(", ").Append(member.Flags).Append(", ").Append(completion).Append(")); }\n");
		}

		static void EmitAccessors(Symbols s, StringBuilder accessors, TypeModel model, Member member) {
			string owner = "[" + CompilerServices + "UnsafeAccessorType(" + Literal(model.MetadataName + ", " + s.AssemblyName) + ")] object _";
			string tokenized = G + Commands + "TokenizedCommand";

			if (!member.Direct) {
				accessors.Append('\n');
				switch (member.Kind) {
					case MemberKind.Field:
						AppendAccessorAttribute(accessors, member.IsStatic ? "StaticField" : "Field", member.Name);
						accessors.Append("\t\tstatic extern ref ").Append(member.ValueType).Append(" A").Append(member.Id).Append('(').Append(owner).Append(");\n");
						break;
					case MemberKind.Property:
						AppendAccessorAttribute(accessors, member.IsStatic ? "StaticMethod" : "Method", "get_" + member.Name);
						accessors.Append("\t\tstatic extern ").Append(member.ValueType).Append(" A").Append(member.Id).Append('(').Append(owner).Append(");\n");
						break;
					case MemberKind.Method:
						AppendAccessorAttribute(accessors, member.IsStatic ? "StaticMethod" : "Method", member.Name);
						accessors.Append("\t\tstatic extern void A").Append(member.Id).Append('(').Append(owner);
						if (member.Callback >= 1)
							accessors.Append(", in ").Append(tokenized).Append(" a");
						if (member.Callback == 2)
							accessors.Append(", ").Append(G).Append(Commands).Append("CommandSource b, int c");
						accessors.Append(");\n");
						break;
				}
			}

			if (member.Completion != null && !member.Completion.Direct) {
				accessors.Append('\n');
				AppendAccessorAttribute(accessors, member.IsStatic ? "StaticMethod" : "Method", member.Completion.Name);
				accessors.Append("\t\tstatic extern ").Append(member.Completion.ReturnType).Append(" C").Append(member.Id).Append('(').Append(owner).Append(", string a);\n");
			}
		}

		static void AppendAccessorAttribute(StringBuilder accessors, string kind, string name) {
			accessors.Append("\t\t[").Append(CompilerServices).Append("UnsafeAccessor(").Append(CompilerServices).Append("UnsafeAccessorKind.").Append(kind)
				.Append(", Name = ").Append(Literal(name)).Append(")]\n");
		}
	}
}
