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
	public sealed class LuaStackAnalyzer : DiagnosticAnalyzer
	{
		static readonly DiagnosticDescriptor ImbalanceRule = new(
			"SRCLUA003",
			"Lua stack is left unbalanced",
			"Paths through '{0}' leave the Lua stack at different depths ({1})",
			"Lua",
			DiagnosticSeverity.Warning,
			isEnabledByDefault: true);

		static readonly DiagnosticDescriptor ReturnCountRule = new(
			"SRCLUA004",
			"Lua binding returns more values than it pushed",
			"Binding returns {0} value(s) but only {1} were pushed on this path",
			"Lua",
			DiagnosticSeverity.Warning,
			isEnabledByDefault: true);

		static readonly DiagnosticDescriptor UseAfterFreeRule = new(
			"SRCLUA005",
			"LuaObject used after UnReference()",
			"'{0}' is used after UnReference() was called on it",
			"Lua",
			DiagnosticSeverity.Warning,
			isEnabledByDefault: true);

		public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(ImbalanceRule, ReturnCountRule, UseAfterFreeRule);

		public override void Initialize(AnalysisContext context) {
			context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
			context.EnableConcurrentExecution();
			context.RegisterCompilationStartAction(start => {
				Stack stack = new(start.Compilation);
				start.RegisterOperationBlockAction(block => stack.AnalyzeBlock(block));
			});
		}

		const int Unknown = int.MinValue;

		static readonly Dictionary<string, int> FixedEffects = new() {
			["Push"] = 1, ["PushNil"] = 1, ["PushString"] = 1, ["PushNumber"] = 1, ["PushBool"] = 1, ["PushCFunction"] = 1,
			["PushUserdata"] = 1, ["ReferencePush"] = 1, ["PushSpecial"] = 1, ["PushAngle"] = 1, ["PushVector"] = 1,
			["PushUserType"] = 1, ["PushValueUserType"] = 1, ["PushObjectUserType"] = 1, ["PushColor"] = 1, ["PushLong"] = 1,
			["PushLuaObject"] = 1, ["PushLuaFunction"] = 1, ["PushPooledString"] = 1, ["CreateTable"] = 1, ["PreCreateTable"] = 1,
			["NewUserdata"] = 1, ["GetField"] = 1,
			["GetTable"] = 0, ["RawGet"] = 0, ["Insert"] = 0, ["ArgError"] = 0, ["CheckNumber"] = 0, ["CheckString"] = 0,
			["CheckType"] = 0, ["Equal"] = 0, ["RawEqual"] = 0, ["GetAngle"] = 0, ["GetBool"] = 0, ["GetCFunction"] = 0,
			["GetNumber"] = 0, ["GetString"] = 0, ["GetStringBytes"] = 0, ["GetType"] = 0, ["GetTypeName"] = 0,
			["GetUserdata"] = 0, ["GetVector"] = 0, ["IsType"] = 0, ["ObjLen"] = 0, ["Top"] = 0, ["ReferenceFree"] = 0,
			["SetUserType"] = 0, ["ReleaseUserTypeObject"] = 0, ["GetValueUserType"] = 0, ["GetObjectUserType"] = 0,
			["CheckNumberOpt"] = 0, ["CheckStringOpt"] = 0, ["GetColor"] = 0, ["GetStringOrError"] = 0, ["isUserData"] = 0,
			["IsServer"] = 0, ["IsClient"] = 0, ["IsMenu"] = 0, ["Msg"] = 0, ["MsgColour"] = 0, ["ErrorNoHalt"] = 0,
			["GetObject"] = 0, ["GetReturn"] = 0, ["Global"] = 0, ["CreateObject"] = 0, ["DestroyObject"] = 0,
			["GetNewTable"] = 0, ["NewTemporaryObject"] = 0, ["SetType"] = 0, ["GetActualTypeName"] = 0, ["SetMember"] = 0,
			["GetMetaTableObject"] = 0, ["NewGlobalTable"] = 0, ["GetPooledString"] = 0,
			["SetField"] = -1, ["SetMetaTable"] = -1, ["Remove"] = -1, ["ReferenceCreate"] = -1,
			["SetTable"] = -2, ["RawSet"] = -2,
		};

		static readonly HashSet<string> ObjectPushers = new() { "Push", "PushMemberFast" };

		static readonly HashSet<string> Rereferencers = new() { "UnReference", "SetFromStack", "SetReference", "Set", "SetFromGlobal", "Init" };

		sealed class Stack
		{
			readonly Compilation compilation;
			readonly ConcurrentDictionary<IMethodSymbol, int> summaries = new(SymbolEqualityComparer.Default);
			[ThreadStatic] static HashSet<ISymbol>? inProgress;

			public Stack(Compilation compilation) => this.compilation = compilation;

			public void AnalyzeBlock(OperationBlockAnalysisContext context) {
				if (context.OwningSymbol is not IMethodSymbol method)
					return;
				HashSet<IOperation> roots = new();
				foreach (IOperation block in context.OperationBlocks) {
					IOperation root = block;
					while (root.Parent != null)
						root = root.Parent;
					if (!roots.Add(root) || CreateGraph(root) is not ControlFlowGraph cfg)
						continue;
					RunStack(cfg, method, context.ReportDiagnostic);
					RunUseAfterFree(cfg, context.ReportDiagnostic);
				}
			}

			static ControlFlowGraph? CreateGraph(IOperation root) => root switch {
				IMethodBodyOperation body => ControlFlowGraph.Create(body),
				IConstructorBodyOperation ctor => ControlFlowGraph.Create(ctor),
				IBlockOperation statements => ControlFlowGraph.Create(statements),
				_ => null,
			};

			static IEnumerable<IOperation> BlockOperations(BasicBlock block) {
				foreach (IOperation op in block.Operations)
					yield return op;
				if (block.BranchValue != null)
					yield return block.BranchValue;
			}

			static IOperation? Unwrap(IOperation? op) {
				while (op is IConversionOperation conv)
					op = conv.Operand;
				return op;
			}

			static bool IsBinding(IMethodSymbol method) =>
				method.ReturnType.SpecialType == SpecialType.System_Int32 &&
				method.Parameters.Any(p => IsLuaApi(p.Type));

			static bool IsLuaApi(ITypeSymbol? type) {
				if (type == null)
					return false;
				if (type.Name is "ILuaBase" or "ILuaInterface" && type.ContainingNamespace?.ToDisplayString() == "Source.Common.GarrysMod.Lua")
					return true;
				return type.AllInterfaces.Any(i => i.Name is "ILuaBase" or "ILuaInterface");
			}

			static bool IsLuaObject(ITypeSymbol? type) {
				if (type == null)
					return false;
				if (type.Name == "ILuaObject")
					return true;
				return type.AllInterfaces.Any(i => i.Name == "ILuaObject");
			}

			static bool DoesNotReturn(IMethodSymbol method) => method.GetAttributes().Any(a => a.AttributeClass?.Name == "DoesNotReturnAttribute");

			static int? Constant(IOperation? op) {
				op = Unwrap(op);
				if (op?.ConstantValue.HasValue == true && op.ConstantValue.Value is int i)
					return i;
				return null;
			}

			static int? Argument(IInvocationOperation invocation, int ordinal) {
				foreach (IArgumentOperation arg in invocation.Arguments)
					if (arg.Parameter?.Ordinal == ordinal)
						return arg.ArgumentKind == ArgumentKind.DefaultValue && arg.Parameter.HasExplicitDefaultValue && arg.Parameter.ExplicitDefaultValue is int d ? d : Constant(arg.Value);
				return null;
			}

			int Effect(IInvocationOperation invocation) {
				IMethodSymbol target = invocation.TargetMethod;
				string name = target.Name;

				if (invocation.Instance != null && IsLuaApi(invocation.Instance.Type)) {
					switch (name) {
						case "Pop":
							return Argument(invocation, 0) is int popped ? -popped : Unknown;
						case "Call":
							return Argument(invocation, 0) is int args && Argument(invocation, 1) is int results ? -(args + 1) + results : Unknown;
						case "PushCClosure":
							return Argument(invocation, 1) is int upvalues ? 1 - upvalues : Unknown;
						case "SetMember":
							return invocation.Arguments.Length == 2 ? -1 : 0;
					}
					return FixedEffects.TryGetValue(name, out int effect) ? effect : Unknown;
				}

				int summary = Summary(target);
				if (summary != NoSource)
					return summary;
				if (invocation.Instance != null && IsLuaObject(invocation.Instance.Type))
					return ObjectPushers.Contains(name) ? 1 : 0;
				if (target.Parameters.Any(p => IsLuaApi(p.Type) || IsLuaObject(p.Type)) || name.StartsWith("Push", StringComparison.Ordinal) || name.StartsWith("Pop", StringComparison.Ordinal))
					return Unknown;
				return 0;
			}

			const int NoSource = int.MaxValue;

			int Summary(IMethodSymbol method) {
				method = method.OriginalDefinition;
				if (summaries.TryGetValue(method, out int cached))
					return cached;
				inProgress ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
				if (!inProgress.Add(method))
					return Unknown;
				int result = NoSource;
				try {
					foreach (SyntaxReference reference in method.DeclaringSyntaxReferences) {
						SyntaxNode node = reference.GetSyntax();
						if (node is not BaseMethodDeclarationSyntax && node is not LocalFunctionStatementSyntax && node is not AccessorDeclarationSyntax && node is not ArrowExpressionClauseSyntax)
							continue;
						if (!compilation.ContainsSyntaxTree(node.SyntaxTree))
							continue;
						IOperation? op = compilation.GetSemanticModel(node.SyntaxTree).GetOperation(node);
						if (op != null && CreateGraph(op) is ControlFlowGraph cfg)
							result = RunStack(cfg, null, null);
						break;
					}
				}
				finally {
					inProgress.Remove(method);
				}
				summaries[method] = result;
				return result;
			}

			int Apply(IOperation op, int depth, out bool ended) {
				ended = false;
				foreach (IOperation d in op.DescendantsAndSelf().Reverse()) {
					if (d is not IInvocationOperation invocation)
						continue;
					if (DoesNotReturn(invocation.TargetMethod)) {
						ended = true;
						return depth;
					}
					int effect = Effect(invocation);
					if (effect == Unknown)
						return Unknown;
					depth += effect;
				}
				return depth;
			}

			int RunStack(ControlFlowGraph cfg, IMethodSymbol? method, Action<Diagnostic>? report) {
				bool binding = method != null && IsBinding(method);
				int?[] input = new int?[cfg.Blocks.Length];
				input[0] = 0;
				Queue<int> work = new();
				work.Enqueue(0);
				HashSet<int> exitDepths = new();
				bool unknown = false;
				bool returnsMatchDepth = method?.ReturnType.SpecialType == SpecialType.System_Int32;

				while (work.Count > 0 && !unknown) {
					int ordinal = work.Dequeue();
					BasicBlock block = cfg.Blocks[ordinal];
					int depth = input[ordinal]!.Value;
					bool ended = false;

					foreach (IOperation op in BlockOperations(block)) {
						depth = Apply(op, depth, out ended);
						if (depth == Unknown || ended)
							break;
					}
					if (depth == Unknown) {
						unknown = true;
						break;
					}
					if (ended)
						continue;

					if (block.Kind == BasicBlockKind.Exit) {
						exitDepths.Add(depth);
						continue;
					}

					if (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return) {
						int? returned = Constant(block.BranchValue);
						if (returned != depth)
							returnsMatchDepth = false;
						if (binding && returned is int count && count > 0 && count > depth)
							report?.Invoke(Diagnostic.Create(ReturnCountRule, block.BranchValue!.Syntax.GetLocation(), count, depth));
					}

					foreach (ControlFlowBranch? branch in new[] { block.FallThroughSuccessor, block.ConditionalSuccessor }) {
						if (branch?.Destination == null || branch.Semantics is ControlFlowBranchSemantics.Throw or ControlFlowBranchSemantics.Rethrow)
							continue;
						int dest = branch.Destination.Ordinal;
						if (branch.Destination.Kind == BasicBlockKind.Exit) {
							exitDepths.Add(depth);
							continue;
						}
						if (input[dest] == null) {
							input[dest] = depth;
							work.Enqueue(dest);
						}
						else if (input[dest] != depth) {
							if (dest <= ordinal || binding) {
								unknown = true;
								break;
							}
							if (method != null && report != null && method.ReturnType.SpecialType != SpecialType.System_Boolean)
								report(Diagnostic.Create(ImbalanceRule, method.Locations.FirstOrDefault(), method.Name, $"{input[dest]} vs {depth}"));
							unknown = true;
							break;
						}
					}
				}

				if (unknown)
					return Unknown;
				if (exitDepths.Count > 1) {
					if (!binding && !returnsMatchDepth && method != null && report != null && method.ReturnType.SpecialType != SpecialType.System_Boolean)
						report(Diagnostic.Create(ImbalanceRule, method.Locations.FirstOrDefault(), method.Name, string.Join(" vs ", exitDepths)));
					return Unknown;
				}
				return exitDepths.Count == 1 ? exitDepths.First() : 0;
			}

			static ILocalSymbol? AsLocal(IOperation? op) => (Unwrap(op) as ILocalReferenceOperation)?.Local;

			static void FreeTransfer(BasicBlock block, HashSet<ILocalSymbol> released, List<Diagnostic>? report) {
				foreach (IOperation op in BlockOperations(block))
					foreach (IOperation d in op.DescendantsAndSelf().Reverse()) {
						switch (d) {
							case ISimpleAssignmentOperation assign when AsLocal(assign.Target) is ILocalSymbol target:
								released.Remove(target);
								break;
							case IInvocationOperation invocation when AsLocal(invocation.Instance) is ILocalSymbol local && IsLuaObject(local.Type): {
								if (invocation.TargetMethod.Name == "UnReference")
									released.Add(local);
								else if (Rereferencers.Contains(invocation.TargetMethod.Name))
									released.Remove(local);
								else if (released.Contains(local))
									report?.Add(Diagnostic.Create(UseAfterFreeRule, invocation.Syntax.GetLocation(), local.Name));
								break;
							}
						}
					}
			}

			void RunUseAfterFree(ControlFlowGraph cfg, Action<Diagnostic> report) {
				HashSet<ILocalSymbol>?[] input = new HashSet<ILocalSymbol>?[cfg.Blocks.Length];
				input[0] = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
				Queue<int> work = new();
				work.Enqueue(0);

				while (work.Count > 0) {
					int ordinal = work.Dequeue();
					BasicBlock block = cfg.Blocks[ordinal];
					HashSet<ILocalSymbol> released = new(input[ordinal]!, SymbolEqualityComparer.Default);
					FreeTransfer(block, released, null);

					foreach (ControlFlowBranch? branch in new[] { block.FallThroughSuccessor, block.ConditionalSuccessor }) {
						if (branch?.Destination == null)
							continue;
						int dest = branch.Destination.Ordinal;
						if (input[dest] == null) {
							input[dest] = new HashSet<ILocalSymbol>(released, SymbolEqualityComparer.Default);
							work.Enqueue(dest);
						}
						else {
							int before = input[dest]!.Count;
							input[dest]!.IntersectWith(released);
							if (input[dest]!.Count != before)
								work.Enqueue(dest);
						}
					}
				}

				List<Diagnostic> found = new();
				for (int i = 0; i < cfg.Blocks.Length; i++)
					if (input[i] != null)
						FreeTransfer(cfg.Blocks[i], new HashSet<ILocalSymbol>(input[i]!, SymbolEqualityComparer.Default), found);
				foreach (Diagnostic diagnostic in found)
					report(diagnostic);
			}
		}
	}
}
