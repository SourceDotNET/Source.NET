using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Source.CodeAnalysis.Lua
{
	[DiagnosticAnalyzer(LanguageNames.CSharp)]
	public sealed class LuaReferenceAnalyzer : DiagnosticAnalyzer
	{
		public const string DiagnosticId = "SRCLUA001";

		static readonly DiagnosticDescriptor Rule = new(
			DiagnosticId,
			"LuaObject is not unreferenced",
			"'{0}' may reach the end of the method without UnReference() being called",
			"Lua",
			DiagnosticSeverity.Warning,
			isEnabledByDefault: true);

		static readonly DiagnosticDescriptor OverwriteRule = new(
			"SRCLUA002",
			"LuaObject overwritten while still referenced",
			"A LuaObject is assigned over a local that may still hold a reference; UnReference() it first",
			"Lua",
			DiagnosticSeverity.Warning,
			isEnabledByDefault: true);

		public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule, OverwriteRule);

		public override void Initialize(AnalysisContext context) {
			context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
			context.EnableConcurrentExecution();
			context.RegisterCompilationStartAction(start => {
				Ownership ownership = new(start.Compilation);
				start.RegisterOperationBlockAction(block => ownership.AnalyzeBlock(block));
			});
		}

		sealed class FlowResult
		{
			public readonly HashSet<ISymbol> Leaked = new(SymbolEqualityComparer.Default);
			public readonly HashSet<Location> Overwrites = new();
			public readonly Dictionary<ISymbol, Location> Created = new(SymbolEqualityComparer.Default);
			public bool ReturnsOwned;
		}

		sealed class Ownership
		{
			readonly Compilation compilation;
			readonly ConcurrentDictionary<IMethodSymbol, bool> returnsCache = new(SymbolEqualityComparer.Default);
			readonly ConcurrentDictionary<IParameterSymbol, bool> takesCache = new(SymbolEqualityComparer.Default);
			[ThreadStatic] static HashSet<ISymbol>? inProgress;

			public Ownership(Compilation compilation) => this.compilation = compilation;

			public void AnalyzeBlock(OperationBlockAnalysisContext context) {
				HashSet<IOperation> roots = new();
				foreach (IOperation block in context.OperationBlocks) {
					IOperation root = block;
					while (root.Parent != null)
						root = root.Parent;
					if (!roots.Add(root))
						continue;
					ControlFlowGraph? cfg = CreateGraph(root);
					if (cfg != null)
						AnalyzeGraph(context.ReportDiagnostic, cfg);
				}
			}

			static ControlFlowGraph? CreateGraph(IOperation root) => root switch {
				IMethodBodyOperation body => ControlFlowGraph.Create(body),
				IConstructorBodyOperation ctor => ControlFlowGraph.Create(ctor),
				IBlockOperation statements => ControlFlowGraph.Create(statements),
				_ => null,
			};

			void AnalyzeGraph(Action<Diagnostic> report, ControlFlowGraph cfg) {
				FlowResult result = Run(cfg, null);
				foreach (ISymbol local in result.Leaked)
					if (result.Created.TryGetValue(local, out Location? location))
						report(Diagnostic.Create(Rule, location, local.Name));
				foreach (Location location in result.Overwrites)
					report(Diagnostic.Create(OverwriteRule, location));

				foreach (IMethodSymbol fn in cfg.LocalFunctions)
					AnalyzeGraph(report, cfg.GetLocalFunctionControlFlowGraph(fn));
				foreach (BasicBlock block in cfg.Blocks)
					foreach (IOperation op in BlockOperations(block))
						foreach (IOperation d in op.DescendantsAndSelf())
							if (d is IFlowAnonymousFunctionOperation lambda)
								AnalyzeGraph(report, cfg.GetAnonymousFunctionControlFlowGraph(lambda));
			}

			static bool IsLuaObject(ITypeSymbol? type) {
				for (ITypeSymbol? t = type; t != null; t = t.BaseType)
					if (t.Name == "LuaObject" && t.ContainingNamespace?.Name == "GarrysMod")
						return true;
				return false;
			}

			static bool IsLuaObjectOrInterface(ITypeSymbol? type) =>
				type != null && (IsLuaObject(type) || (type.Name == "ILuaObject" && type.ContainingNamespace?.ToDisplayString() == "Source.Common.GarrysMod.Lua"));

			static bool IsInterfaceMember(IMethodSymbol method, string interfaceName, string methodName) {
				if (method.Name != methodName)
					return false;
				if (method.ContainingType.Name == interfaceName && method.ContainingType.TypeKind == TypeKind.Interface)
					return true;
				foreach (INamedTypeSymbol iface in method.ContainingType.AllInterfaces)
					if (iface.Name == interfaceName)
						foreach (ISymbol member in iface.GetMembers(methodName))
							if (SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(member), method))
								return true;
				return false;
			}

			static IOperation? Unwrap(IOperation? op) {
				while (op is IConversionOperation conv)
					op = conv.Operand;
				return op;
			}

			static ISymbol? AsTracked(IOperation? op) => Unwrap(op) switch {
				ILocalReferenceOperation local => local.Local,
				IParameterReferenceOperation parameter => parameter.Parameter,
				_ => null,
			};

			bool IsOwnedValue(IOperation? op) => Unwrap(op) switch {
				IObjectCreationOperation creation => IsLuaObject(creation.Type),
				IInvocationOperation invocation => ReturnsOwnership(invocation.TargetMethod),
				_ => false,
			};

			ControlFlowGraph? GraphFor(ISymbol symbol) {
				foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences) {
					SyntaxNode node = reference.GetSyntax();
					if (node is not BaseMethodDeclarationSyntax && node is not LocalFunctionStatementSyntax)
						continue;
					if (!compilation.ContainsSyntaxTree(node.SyntaxTree))
						continue;
					SemanticModel model = compilation.GetSemanticModel(node.SyntaxTree);
					IOperation? op = model.GetOperation(node);
					if (op != null && CreateGraph(op) is ControlFlowGraph cfg)
						return cfg;
				}
				return null;
			}

			bool Guarded(ISymbol symbol, Func<bool> compute) {
				inProgress ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
				if (!inProgress.Add(symbol))
					return false;
				try {
					return compute();
				}
				finally {
					inProgress.Remove(symbol);
				}
			}

			bool ReturnsOwnership(IMethodSymbol method) {
				method = method.OriginalDefinition;
				if (IsInterfaceMember(method, "ILuaInterface", "CreateObject") || IsInterfaceMember(method, "ILuaGameCallback", "CreateLuaObject"))
					return true;
				if (!IsLuaObjectOrInterface(method.ReturnType))
					return false;
				if (returnsCache.TryGetValue(method, out bool cached))
					return cached;
				bool result = Guarded(method, () => GraphFor(method) is ControlFlowGraph cfg && Run(cfg, null).ReturnsOwned);
				returnsCache[method] = result;
				return result;
			}

			bool TakesOwnership(IParameterSymbol? parameter) {
				if (parameter == null)
					return false;
				parameter = parameter.OriginalDefinition;
				if (parameter.ContainingSymbol is not IMethodSymbol method)
					return false;
				if (parameter.Ordinal == 0 && (IsInterfaceMember(method, "ILuaInterface", "DestroyObject") || IsInterfaceMember(method, "ILuaGameCallback", "DestroyLuaObject")))
					return true;
				if (!IsLuaObjectOrInterface(parameter.Type))
					return false;
				if (takesCache.TryGetValue(parameter, out bool cached))
					return cached;
				bool result = Guarded(parameter, () => {
					if (GraphFor(method) is not ControlFlowGraph cfg)
						return false;
					return !Run(cfg, parameter).Leaked.Contains(parameter);
				});
				takesCache[parameter] = result;
				return result;
			}

			FlowResult Run(ControlFlowGraph cfg, IParameterSymbol? parameter) {
				FlowResult result = new();
				foreach (BasicBlock block in cfg.Blocks)
					foreach (IOperation op in BlockOperations(block))
						foreach (IOperation d in op.DescendantsAndSelf())
							if (d is ISimpleAssignmentOperation assign && Unwrap(assign.Target) is ILocalReferenceOperation local && !result.Created.ContainsKey(local.Local) && IsOwnedValue(assign.Value))
								result.Created[local.Local] = assign.Value.Syntax.GetLocation();

				if (parameter != null)
					result.Created[parameter] = Location.None;

				if (result.Created.Count == 0) {
					foreach (BasicBlock block in cfg.Blocks)
						if (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return && IsOwnedValue(block.BranchValue))
							result.ReturnsOwned = true;
					return result;
				}

				HashSet<ISymbol>?[] input = new HashSet<ISymbol>?[cfg.Blocks.Length];
				input[0] = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
				if (parameter != null)
					input[0]!.Add(parameter);
				Queue<int> work = new();
				work.Enqueue(0);

				while (work.Count > 0) {
					int ordinal = work.Dequeue();
					BasicBlock block = cfg.Blocks[ordinal];
					HashSet<ISymbol> state = new(input[ordinal]!, SymbolEqualityComparer.Default);

					foreach (IOperation op in BlockOperations(block))
						foreach (IOperation d in op.DescendantsAndSelf())
							Transfer(d, state, result);

					if (block.Kind == BasicBlockKind.Exit) {
						result.Leaked.UnionWith(state);
						continue;
					}

					if (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return) {
						if (AsTracked(block.BranchValue) is ISymbol returned && result.Created.ContainsKey(returned)) {
							if (state.Remove(returned))
								result.ReturnsOwned = true;
						}
						else if (IsOwnedValue(block.BranchValue))
							result.ReturnsOwned = true;
					}

					Propagate(cfg, result, block.FallThroughSuccessor, state, input, work);
					Propagate(cfg, result, block.ConditionalSuccessor, state, input, work);
				}
				return result;
			}

			static IEnumerable<IOperation> BlockOperations(BasicBlock block) {
				foreach (IOperation op in block.Operations)
					yield return op;
				if (block.BranchValue != null)
					yield return block.BranchValue;
			}

			void Propagate(ControlFlowGraph cfg, FlowResult result, ControlFlowBranch? branch, HashSet<ISymbol> state, HashSet<ISymbol>?[] input, Queue<int> work) {
				if (branch?.Destination == null || branch.Semantics == ControlFlowBranchSemantics.Throw || branch.Semantics == ControlFlowBranchSemantics.Rethrow)
					return;
				if (!branch.FinallyRegions.IsEmpty) {
					state = new HashSet<ISymbol>(state, SymbolEqualityComparer.Default);
					foreach (ControlFlowRegion region in branch.FinallyRegions)
						for (int i = region.FirstBlockOrdinal; i <= region.LastBlockOrdinal; i++)
							foreach (IOperation op in BlockOperations(cfg.Blocks[i]))
								foreach (IOperation d in op.DescendantsAndSelf())
									Transfer(d, state, result);
				}
				int dest = branch.Destination.Ordinal;
				if (input[dest] == null) {
					input[dest] = new HashSet<ISymbol>(state, SymbolEqualityComparer.Default);
					work.Enqueue(dest);
					return;
				}
				int before = input[dest]!.Count;
				input[dest]!.UnionWith(state);
				if (input[dest]!.Count != before)
					work.Enqueue(dest);
			}

			void ReleaseArguments(ImmutableArray<IArgumentOperation> arguments, HashSet<ISymbol> state) {
				foreach (IArgumentOperation arg in arguments)
					if (AsTracked(arg.Value) is ISymbol owned && state.Contains(owned) && TakesOwnership(arg.Parameter))
						state.Remove(owned);
			}

			void Transfer(IOperation op, HashSet<ISymbol> state, FlowResult result) {
				switch (op) {
					case ISimpleAssignmentOperation assign: {
						if (AsTracked(assign.Target) is ISymbol target && result.Created.ContainsKey(target) && IsOwnedValue(assign.Value)) {
							if (!state.Add(target))
								result.Overwrites.Add(assign.Value.Syntax.GetLocation());
							break;
						}
						if (AsTracked(assign.Value) is ISymbol value && result.Created.ContainsKey(value))
							state.Remove(value);
						break;
					}
					case ITupleOperation tuple: {
						foreach (IOperation element in tuple.Elements)
							if (AsTracked(element) is ISymbol tracked)
								state.Remove(tracked);
						break;
					}
					case IInvocationOperation invocation: {
						if (invocation.TargetMethod.GetAttributes().Any(a => a.AttributeClass?.Name == "DoesNotReturnAttribute")) {
							state.Clear();
							break;
						}
						if ((invocation.TargetMethod.Name == "UnReference" || (invocation.TargetMethod.Name == "Dispose" && invocation.TargetMethod.ContainingType.Name == "LuaDisposable")) && AsTracked(invocation.Instance) is ISymbol instance)
							state.Remove(instance);
						ReleaseArguments(invocation.Arguments, state);
						break;
					}
					case IObjectCreationOperation ctor: {
						ReleaseArguments(ctor.Arguments, state);
						break;
					}
					case IFlowAnonymousFunctionOperation lambda: {
						HashSet<string> names = new(lambda.Syntax.DescendantNodes().OfType<IdentifierNameSyntax>().Select(id => id.Identifier.ValueText));
						state.RemoveWhere(tracked => names.Contains(tracked.Name));
						break;
					}
				}
			}
		}
	}
}
