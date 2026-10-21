using Microsoft.CodeAnalysis;
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
		private const string FactoryAttributeName = "Source.Common.ImplementClientClassFactoryAttribute";
		private const string TableAttributeName = "Source.Common.NetworkTableAttribute";
		private const string PredictionTableAttributeName = "Source.Common.PredictionTableAttribute";

		private static readonly DiagnosticDescriptor MissingNetworkVar = new DiagnosticDescriptor(
			id: "NETVAR013",
			title: "Table references a variable without [NetworkVar]",
			messageFormat: "'{0}' is referenced by a network table but has no [NetworkVar(\"m_name\")]",
			category: "NetworkVars",
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		private static readonly DiagnosticDescriptor InvalidListEntry = new DiagnosticDescriptor(
			id: "NETVAR014",
			title: "Invalid table entry",
			messageFormat: "Entry '{0}' in '{1}' does not name a member, table or table list",
			category: "NetworkVars",
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		private static readonly DiagnosticDescriptor ProxyNotSupported = new DiagnosticDescriptor(
			id: "NETVAR011",
			title: "Encoding does not take a proxy",
			messageFormat: "'{0}' uses encoding '{1}', which does not accept a custom proxy",
			category: "NetworkVars",
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		private static readonly DiagnosticDescriptor UnsupportedEncoding = new DiagnosticDescriptor(
			id: "NETVAR006",
			title: "Cannot infer the network encoding",
			messageFormat: "'{0}' has type '{1}', which has no default network encoding; set Encoding on its table entry",
			category: "NetworkVars",
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		private static readonly DiagnosticDescriptor UnsupportedPredicted = new DiagnosticDescriptor(
			id: "NETVAR007",
			title: "Cannot infer the prediction field type",
			messageFormat: "'{0}' has type '{1}', which has no default prediction field type; set Type on its prediction entry",
			category: "NetworkVars",
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		private static readonly DiagnosticDescriptor MissingBaseTable = new DiagnosticDescriptor(
			id: "NETVAR008",
			title: "Network class has no networked base class",
			messageFormat: "'{0}' has no base class with [NetworkName], so its table has no parent",
			category: "NetworkVars",
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		private static IncrementalValueProvider<ImmutableArray<TableModel?>> InitializeTables(IncrementalGeneratorInitializationContext context) {
			IncrementalValueProvider<ImmutableArray<TableModel?>> entityTables = context.SyntaxProvider
				.ForAttributeWithMetadataName(
					NetworkNameAttributeName,
					predicate: static (node, _) => node is ClassDeclarationSyntax,
					transform: static (ctx, ct) => GetTableModel(ctx, ct))
				.Where(static m => m != null)
				.Collect();
			IncrementalValueProvider<ImmutableArray<TableModel?>> plainTables = context.SyntaxProvider
				.ForAttributeWithMetadataName(
					TableAttributeName,
					predicate: static (node, _) => node is VariableDeclaratorSyntax,
					transform: static (ctx, ct) => GetListTableModel(ctx, ct))
				.Where(static m => m != null)
				.Collect();
			IncrementalValueProvider<ImmutableArray<TableModel?>> tables = entityTables.Combine(plainTables).Select(static (pair, _) => pair.Left.AddRange(pair.Right));

			context.RegisterSourceOutput(tables, static (spc, models) => EmitTables(spc, models));
			return tables;
		}

		private static void EmitTables(SourceProductionContext spc, ImmutableArray<TableModel?> models) {
			foreach (TableModel? model in models) {
				if (model == null)
					continue;
				foreach (TableDiagnostic diagnostic in model.Diagnostics)
					spc.ReportDiagnostic(Diagnostic.Create(diagnostic.Descriptor, diagnostic.Location, diagnostic.Args));
				if (model.Source.Length != 0)
					spc.AddSource(model.HintName, SourceText.From(model.Source, Encoding.UTF8));
			}
		}

		private static TableModel? GetTableModel(GeneratorAttributeSyntaxContext ctx, CancellationToken ct) {
			if (!(ctx.TargetSymbol is INamedTypeSymbol type) || type.ContainingType != null || type.IsGenericType)
				return null;
			if (ctx.Attributes.Length == 0 || ctx.Attributes[0].ConstructorArguments.Length != 1 || !(ctx.Attributes[0].ConstructorArguments[0].Value is string networkName) || networkName.Length < 2)
				return null;

			bool isClient = ctx.TargetNode.SyntaxTree.Options.PreprocessorSymbolNames.Contains("CLIENT_DLL");
			bool isEntity = type.AllInterfaces.Any(i => i.Name == (isClient ? "IClientEntity" : "IServerEntity") && i.ContainingNamespace.ToDisplayString() == "Source.Common");
			bool isTempEntity = !isEntity && InheritsFrom(type, isClient ? "Game.Client.C_BaseTempEntity" : "Game.Server.BaseTempEntity");
			if (!isEntity && !isTempEntity)
				return null;

			string className = isClient ? "ClientClass" : "ServerClass";
			if (type.GetMembers(className).Any(m => !m.IsImplicitlyDeclared))
				return null;

			string tableName = NamedValue(ctx.Attributes[0], "Table") as string ?? "DT_" + networkName.Substring(1);
			if (type.GetMembers(tableName).Any(m => !m.IsImplicitlyDeclared))
				return null;

			return BuildTable(ctx.SemanticModel.Compilation, type, isClient, networkName, tableName, isEntity, isTempEntity, false,
				NamedValue(ctx.Attributes[0], "NoBase") is bool nb && nb, ct);
		}

		private static TableModel? GetListTableModel(GeneratorAttributeSyntaxContext ctx, CancellationToken ct) {
			if (!(ctx.TargetSymbol is IFieldSymbol field) || !(field.ContainingType is INamedTypeSymbol type) || type.ContainingType != null || type.IsGenericType)
				return null;
			if (GetClassTableName(type) != null)
				return null;
			List<IFieldSymbol> lists = ListFields(type, TableAttributeName);
			if (lists.Count == 0 || !SymbolEqualityComparer.Default.Equals(lists[0], field))
				return null;
			bool isClient = ctx.TargetNode.SyntaxTree.Options.PreprocessorSymbolNames.Contains("CLIENT_DLL");
			return BuildTable(ctx.SemanticModel.Compilation, type, isClient, null, ListTableName(field, field.Name), false, false, true, true, ct);
		}

		private static TableModel? BuildTable(Compilation compilation, INamedTypeSymbol type, bool isClient, string? networkName, string tableName,
			bool isEntity, bool isTempEntity, bool plain, bool noBase, CancellationToken ct) {
			string typeDisplay = Display(type);
			var diagnostics = new List<TableDiagnostic>();
			var accessors = new List<FieldAccessorModel>();

			List<IFieldSymbol> tableLists = ListFields(type, TableAttributeName);
			List<IFieldSymbol> predictionLists = ListFields(type, PredictionTableAttributeName);
			foreach (IFieldSymbol listField in tableLists) {
				AttributeData? listAttr = listField.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == TableAttributeName);
				if (listAttr != null && SideMatches(listAttr, isClient) && ListTableName(listField, tableName) == tableName && NamedValue(listAttr, "NoBase") is bool listNoBase && listNoBase)
					noBase = true;
			}
			INamedTypeSymbol? baseType = plain ? null : type.BaseType;
			string? baseTableName = null;
			for (; baseType != null; baseType = baseType.BaseType) {
				baseTableName = GetClassTableName(baseType);
				if (baseTableName != null)
					break;
			}
			if (!noBase && (baseType == null || baseTableName == null)) {
				diagnostics.Add(new TableDiagnostic(MissingBaseTable, type.Locations.FirstOrDefault(), type.Name));
				return new TableModel("", "", accessors, null, diagnostics);
			}
			string baseDisplay = baseType == null ? "" : Display(baseType);

			string indentProps = (type.ContainingNamespace.IsGlobalNamespace ? "" : "\t") + "\t\t";
			var subTables = new List<string>();
			var props = new Dictionary<string, List<string>>(StringComparer.Ordinal) { [""] = new List<string>() };
			var predicted = new List<string>();
			var entries = new List<string>();
			var embeddedMaps = new List<string>();

			string? BuildNamedProp(string name, PropSettings settings) {
				string? encoding = SettingEnum(settings, "Encoding");
				string? proxy = ProxyMethod(SettingValue(settings, "Proxy") as ITypeSymbol, isClient);
				if (encoding == "Array2" && SettingValue(settings, "Count") is int arrayCount)
					return (isClient ? "global::Source.Common.RecvPropHelpers.RecvPropArray2(" : "global::Source.Common.SendPropHelpers.SendPropArray2(")
						+ (proxy ?? "null") + ", " + arrayCount.ToString(CultureInfo.InvariantCulture) + ", " + Literal(name) + ")";
				if (encoding != "Int")
					return null;
				if (isClient)
					return "global::Source.Common.RecvPropHelpers.RecvPropInt(" + Literal(name) + (proxy == null ? "" : ", proxyFn: " + proxy) + ")";
				string? flags = SettingFlags(settings, "Flags", "global::Source.Common.PropFlags");
				return "global::Source.Common.SendPropHelpers.SendPropInt(" + Literal(name)
					+ (SettingValue(settings, "Bits") is int bits ? ", bits: " + bits.ToString(CultureInfo.InvariantCulture) : "")
					+ (flags == null ? "" : ", flags: " + flags)
					+ (proxy == null ? "" : ", proxyFn: " + proxy) + ", sizeOfVar: 4)";
			}

			string? BuildMemberProp(string memberPath, ITypeSymbol memberType, PropSettings settings, Location location) {
				string? encoding = SettingEnum(settings, "Encoding");
				ContainerKind container = GetContainer(compilation, memberType, out ITypeSymbol? element, out _);
				string fieldExpression = SettingValue(settings, "Name") is string propNameOverride
					? "global::Source.FIELD<" + typeDisplay + ">.OF_NAMED(" + Literal(memberPath) + ", " + Literal(propNameOverride) + ")"
					: "global::Source.FIELD<" + typeDisplay + ">.OF(" + Literal(memberPath) + ")";

				if (encoding == "DataTable") {
					FieldAccessorModel? dtAccessor = BuildScalar(compilation, type, memberPath, SettingValue(settings, "Name") as string, new AccessFailure());
					if (dtAccessor != null)
						accessors.Add(dtAccessor);
					string? tableMember = SettingValue(settings, "DataTable") as string;
					string netName = SettingValue(settings, "Name") as string ?? GetNetworkNameAttribute(LastMember(type, memberPath) ?? type) ?? memberPath;
					if (tableMember == null) {
						diagnostics.Add(new TableDiagnostic(UnsupportedEncoding, location, memberPath, memberType.ToDisplayString()));
						return null;
					}
					string table = Display(memberType) + "." + tableMember;
					string? dtProxy = ProxyMethod(SettingValue(settings, "Proxy") as ITypeSymbol, isClient);
					if (isClient)
						return "global::Source.Common.RecvPropHelpers.RecvPropDataTable(" + Literal(netName) + ", " + fieldExpression + ", " + table
							+ ", proxyFn: " + (dtProxy ?? "global::Source.Common.RecvPropHelpers.DataTableRecvProxy_PointerDataTable") + ")";
					return "global::Source.Common.SendPropHelpers.SendPropDataTable(" + Literal(netName) + ", " + fieldExpression + ", " + table + (dtProxy == null ? "" : ", proxyFn: " + dtProxy) + ")";
				}

				if (encoding == "List") {
					GetContainer(compilation, memberType, out ITypeSymbol? listElement, out _);
					object? countValue = SettingValue(settings, "Count");
					if (listElement == null || !(countValue is int listCount)) {
						diagnostics.Add(new TableDiagnostic(UnsupportedEncoding, location, memberPath, memberType.ToDisplayString()));
						return null;
					}
					string listNetName = GetNetworkNameAttribute(LastMember(type, memberPath) ?? type) ?? memberPath;
					string? elementTable = SettingValue(settings, "DataTable") as string;
					string elementProp;
					if (elementTable != null)
						elementProp = (isClient ? "global::Source.Common.RecvPropHelpers.RecvPropDataTable(null!, " : "global::Source.Common.SendPropHelpers.SendPropDataTable(null, ")
							+ Display(listElement) + "." + elementTable + ")";
					else {
						string? listElementProp = BuildProp(isClient, settings, listElement, "null!", out _, out _, InferEncoding(listElement));
						if (listElementProp == null) {
							diagnostics.Add(new TableDiagnostic(UnsupportedEncoding, location, memberPath, listElement.ToDisplayString()));
							return null;
						}
						elementProp = listElementProp;
					}
					string count = listCount.ToString(CultureInfo.InvariantCulture);
					string elementDisplay = Display(listElement);
					if (!isClient) {
						FieldAccessorModel? listScalar = BuildScalar(compilation, type, memberPath, SettingValue(settings, "Name") as string, new AccessFailure());
						if (listScalar != null)
							accessors.Add(listScalar);
						return "global::Source.Common.SendPropHelpers.SendPropList<" + elementDisplay + ">(" + fieldExpression + ", " + count + ", " + elementProp + ")";
					}
					FieldAccessorModel? listArray = BuildArray(compilation, type, memberPath, true, new AccessFailure());
					if (listArray != null)
						accessors.Add(listArray);
					string? resize = SettingValue(settings, "Proxy") is ITypeSymbol listProxy && listProxy.GetMembers("Resize").Any(m => m is IMethodSymbol { IsStatic: true })
						? Display(listProxy) + ".Resize" : null;
					if (resize == null) {
						diagnostics.Add(new TableDiagnostic(UnsupportedEncoding, location, memberPath, memberType.ToDisplayString()));
						return null;
					}
					return "global::Source.Common.RecvPropHelpers.RecvPropList<" + elementDisplay + ">(global::Source.FIELD<" + typeDisplay + ">.OF_LIST(" + Literal(memberPath) + ", " + count + "), "
						+ resize + ", " + elementProp + ")";
				}

				if (encoding == "PredictableId" || encoding == "GModTable") {
					FieldAccessorModel? specialAccessor = BuildScalar(compilation, type, memberPath, SettingValue(settings, "Name") as string, new AccessFailure());
					if (specialAccessor != null)
						accessors.Add(specialAccessor);
					if (encoding == "PredictableId")
						return isClient ? "global::Game.Client.RecvProxy.RecvPropPredictableId(" + fieldExpression + ")" : "global::Game.Server.SendProxy.SendPropPredictableId(" + fieldExpression + ")";
					return isClient ? "global::Source.Common.RecvPropHelpers.RecvPropGModTable(" + fieldExpression + ")" : "global::Source.Common.SendPropHelpers.SendPropGModTable(" + fieldExpression + ")";
				}

				if (encoding == "VectorElements" || encoding == "Array2" || encoding == "Elements" || encoding == "Array") {
					FieldAccessorModel? elementsAccessor = BuildArray(compilation, type, memberPath, false, new AccessFailure());
					if (elementsAccessor != null)
						accessors.Add(elementsAccessor);
					GetContainer(compilation, memberType, out ITypeSymbol? elementType, out int length);
					if (elementType == null) {
						diagnostics.Add(new TableDiagnostic(UnsupportedEncoding, location, memberPath, memberType.ToDisplayString()));
						return null;
					}
					string? elementEncoding = encoding == "VectorElements" && memberType.OriginalDefinition.ToDisplayString() == "Source.Common.Mathematics.QAngle" ? "Angle" : InferEncoding(elementType);
					var parts = new List<string>();
					int count = encoding == "VectorElements" ? (SettingValue(settings, "Count") is int vectorCount && vectorCount > 0 ? vectorCount : 3) : encoding == "Elements" ? (SettingValue(settings, "Count") is int elementCount && elementCount > 0 ? elementCount : length) : 1;
					for (int i = 0; i < count; i++) {
						string elementField = encoding == "VectorElements"
							? "global::Source.FIELD<" + typeDisplay + ">.OF_VECTORELEM(" + Literal(memberPath) + ", " + i.ToString(CultureInfo.InvariantCulture) + ")"
							: encoding == "Elements"
								? "global::Source.FIELD<" + typeDisplay + ">.OF_ARRAYINDEX(" + Literal(memberPath) + ", " + i.ToString(CultureInfo.InvariantCulture) + ")"
							: isClient
								? "global::Source.FIELD<" + typeDisplay + ">.OF_ARRAYINDEX(" + Literal(memberPath) + ", 0)"
								: "global::Source.FIELD<" + typeDisplay + ">.OF_SENDINFO_ARRAY(" + Literal(memberPath) + ")";
						string? part = BuildProp(isClient, settings, elementType, elementField, out bool partProxyUnsupported, out string partEncoding, elementEncoding);
						if (partProxyUnsupported) {
							diagnostics.Add(new TableDiagnostic(ProxyNotSupported, location, memberPath, partEncoding));
							return null;
						}
						if (part == null) {
							diagnostics.Add(new TableDiagnostic(UnsupportedEncoding, location, memberPath, elementType.ToDisplayString()));
							return null;
						}
						parts.Add(part);
					}
					if (encoding == "Array")
						parts.Add((isClient ? "global::Source.Common.RecvPropHelpers.RecvPropArray(" : "global::Source.Common.SendPropHelpers.SendPropArray(")
							+ "global::Source.FIELD<" + typeDisplay + ">.OF_ARRAY(" + Literal(memberPath) + "))");
					if (encoding == "Array2") {
						string arrayName = GetNetworkNameAttribute(LastMember(type, memberPath) ?? type) ?? memberPath;
						parts.Add((isClient ? "global::Source.Common.RecvPropHelpers.RecvPropArray2(" : "global::Source.Common.SendPropHelpers.SendPropArray2(")
							+ "null, " + length.ToString(CultureInfo.InvariantCulture) + ", " + Literal(arrayName) + ")");
					}
					return string.Join(",\n" + indentProps, parts);
				}

				bool isArray = (container == ContainerKind.InlineArray || container == ContainerKind.NetworkArray) && element != null && (encoding ?? InferEncoding(memberType)) != "String";
				if (isArray) {
					FieldAccessorModel? arrayAccessor = BuildArray(compilation, type, memberPath, false, new AccessFailure());
					if (arrayAccessor != null)
						accessors.Add(arrayAccessor);
					string arrayExpression = "global::Source.FIELD<" + typeDisplay + ">.OF_ARRAY(" + Literal(memberPath) + ")";
					string elementExpression = "global::Source.FIELD<" + typeDisplay + ">.OF_ARRAYINDEX(" + Literal(memberPath) + ", 0)";
					string? elementProp = BuildProp(isClient, settings, element!, elementExpression, out bool elementProxyUnsupported, out string elementEncoding);
					if (elementProxyUnsupported) {
						diagnostics.Add(new TableDiagnostic(ProxyNotSupported, location, memberPath, elementEncoding));
						return null;
					}
					if (elementProp == null) {
						diagnostics.Add(new TableDiagnostic(UnsupportedEncoding, location, memberPath, element!.ToDisplayString()));
						return null;
					}
					return isClient
						? "global::Source.Common.RecvPropHelpers.RecvPropArray3(" + arrayExpression + ", " + elementProp + ")"
						: "global::Source.Common.SendPropHelpers.SendPropArray3(" + arrayExpression + ", " + elementProp + ")";
				}

				FieldAccessorModel? accessor = BuildScalar(compilation, type, memberPath, SettingValue(settings, "Name") as string, new AccessFailure());
				if (accessor != null)
					accessors.Add(accessor);
				string? prop = BuildProp(isClient, settings, memberType, fieldExpression, out bool proxyUnsupported, out string encodingName);
				if (proxyUnsupported)
					diagnostics.Add(new TableDiagnostic(ProxyNotSupported, location, memberPath, encodingName));
				else if (prop == null)
					diagnostics.Add(new TableDiagnostic(UnsupportedEncoding, location, memberPath, memberType.ToDisplayString()));
				return prop;
			}

			foreach (IFieldSymbol listField in tableLists) {
				AttributeData? listAttr = listField.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == TableAttributeName);
				if (listAttr == null || !SideMatches(listAttr, isClient))
					continue;
				string listTable = ListTableName(listField, tableName);
				if (listTable == tableName)
					listTable = "";
				if (!props.TryGetValue(listTable, out List<string>? target)) {
					target = new List<string>();
					props[listTable] = target;
					subTables.Add(listTable);
				}

				foreach (ListEntry entry in ParseList(compilation, type, listField, ct)) {
					if (!EntrySideMatches(entry.Settings, isClient))
						continue;

					if (SettingValue(entry.Settings, "Exclude") is bool exclude && exclude) {
						string? excludeTable = SettingValue(entry.Settings, "DataTable") as string ?? (entry.Symbol == null ? null : GetClassTableName(entry.Symbol.ContainingType));
						string? excludeProp = SettingValue(entry.Settings, "Name") as string ?? (entry.Symbol == null ? entry.Path : GetNetworkNameAttribute(entry.Symbol));
						if (excludeTable == null || excludeProp == null)
							diagnostics.Add(new TableDiagnostic(InvalidListEntry, entry.Location, entry.Path, listField.Name));
						else if (!isClient)
							target.Add("global::Source.Common.SendPropHelpers.SendPropExclude(" + Literal(excludeTable) + ", " + Literal(excludeProp) + ")");
						continue;
					}

					if (entry.Symbol == null) {
						string? namedProp = BuildNamedProp(entry.Path, entry.Settings);
						if (namedProp == null)
							diagnostics.Add(new TableDiagnostic(InvalidListEntry, entry.Location, entry.Path, listField.Name));
						else
							target.Add(namedProp);
						continue;
					}

					if (entry.Symbol is IFieldSymbol tableField && tableField.IsStatic && IsTableField(tableField)) {
						string refTable = HasAttribute(tableField, TableAttributeName)
							? ListTableName(tableField, GetClassTableName(tableField.ContainingType) ?? tableField.Name)
							: tableField.Name;
						string tableExpression = SymbolEqualityComparer.Default.Equals(tableField.ContainingType, type) ? refTable : Display(tableField.ContainingType) + "." + refTable;
						string propName = SettingValue(entry.Settings, "Name") as string ?? (refTable.StartsWith("DT_", StringComparison.Ordinal) ? refTable.Substring(3) : refTable);
						string? dtProxy = ProxyMethod(SettingValue(entry.Settings, "Proxy") as ITypeSymbol, isClient);
						target.Add((isClient ? "global::Source.Common.RecvPropHelpers.RecvPropDataTable(" : "global::Source.Common.SendPropHelpers.SendPropDataTable(")
							+ Literal(propName) + (isClient ? ", " : ", 0, ") + tableExpression + (dtProxy == null ? "" : ", proxyFn: " + dtProxy) + ")");
						continue;
					}

					string? memberNetworkName = GetNetworkNameAttribute(entry.Symbol);
					if (!HasAttribute(entry.Symbol, AttributeMetadataName) || memberNetworkName == null) {
						diagnostics.Add(new TableDiagnostic(MissingNetworkVar, entry.Location, entry.Path));
						continue;
					}

					ITypeSymbol entryType = entry.Symbol is IFieldSymbol ef ? ef.Type : ((IPropertySymbol)entry.Symbol).Type;
					string? code = BuildMemberProp(entry.Path, entryType, entry.Settings, entry.Location);
					if (code != null)
						target.Add(code);
				}
			}

			if (isClient) {
				var clientNetworked = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
				if (predictionLists.Count > 0) {
					for (INamedTypeSymbol? chain = type; chain != null; chain = chain.BaseType) {
						foreach (IFieldSymbol chainList in ListFields(chain, TableAttributeName)) {
							AttributeData? chainAttr = chainList.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == TableAttributeName);
							if (chainAttr == null || !SideMatches(chainAttr, true))
								continue;
							foreach (ListEntry chainEntry in ParseList(compilation, chain, chainList, ct)) {
								if (!EntrySideMatches(chainEntry.Settings, true) || (SettingValue(chainEntry.Settings, "Exclude") is bool chainExclude && chainExclude))
									continue;
								if (chainEntry.Symbol is IPropertySymbol || (chainEntry.Symbol is IFieldSymbol chainField && !chainField.IsStatic))
									clientNetworked.Add(chainEntry.Symbol.OriginalDefinition);
							}
						}
						if (HasNoBaseTable(chain))
							break;
					}
				}

				foreach (var (listField, entry) in predictionLists.SelectMany(list => ParseList(compilation, type, list, ct).Select(entry => (list, entry)))) {
					if (!(entry.Symbol is IFieldSymbol) && !(entry.Symbol is IPropertySymbol)) {
						diagnostics.Add(new TableDiagnostic(InvalidListEntry, entry.Location, entry.Path, listField.Name));
						continue;
					}
					ISymbol member = entry.Symbol!;
					ITypeSymbol memberType = member is IFieldSymbol pf ? pf.Type : ((IPropertySymbol)member).Type;
					if (SettingValue(entry.Settings, "Embedded") is bool embedded && embedded) {
						FieldAccessorModel? embeddedAccessor = BuildScalar(compilation, member.ContainingType, member.Name, null, new AccessFailure());
						if (embeddedAccessor != null)
							accessors.Add(embeddedAccessor);
						predicted.Add(PredictionDescription("Embedded", member, "1", "global::Source.Common.FieldTypeDescFlags.Save", Display(memberType) + ".PredMap", "0"));
						entries.Add("E");
						embeddedMaps.Add(memberType.ToDisplayString() + ".PredMap");
						continue;
					}

					ContainerKind predContainer = GetContainer(compilation, memberType, out ITypeSymbol? predElement, out int predLength);
					bool predArray = (predContainer == ContainerKind.InlineArray || predContainer == ContainerKind.NetworkArray)
						&& predElement != null && predElement.SpecialType != SpecialType.System_Char && predLength > 0;
					string? fieldType = SettingEnum(entry.Settings, "Type") ?? InferFieldType(predArray ? predElement! : memberType);
					if (fieldType == null) {
						diagnostics.Add(new TableDiagnostic(UnsupportedPredicted, entry.Location, entry.Path, memberType.ToDisplayString()));
						continue;
					}

					INamedTypeSymbol declaring = member.ContainingType;
					FieldAccessorModel? predAccessor = BuildScalar(compilation, declaring, member.Name, null, new AccessFailure());
					if (predAccessor != null)
						accessors.Add(predAccessor);

					bool hasFlags = entry.Settings.Values.ContainsKey("Flags");
					object? tolerance = SettingValue(entry.Settings, "Tolerance");
					string? flagsText = SettingFlags(entry.Settings, "Flags", "global::Source.Common.FieldTypeDescFlags");
					if (!hasFlags && clientNetworked.Contains(member.OriginalDefinition)) {
						hasFlags = true;
						flagsText = "(global::Source.Common.FieldTypeDescFlags)256";
					}
					string flags = flagsText ?? (predArray || tolerance is float ? "0" : "global::Source.Common.FieldTypeDescFlags.Save");
					predicted.Add(PredictionDescription(fieldType, member, (predArray ? predLength : 1).ToString(CultureInfo.InvariantCulture), flags, "null",
						tolerance is float tol ? tol.ToString("R", CultureInfo.InvariantCulture) + "f" : "0"));

					string entryType = fieldType == "Byte" ? "Character" : fieldType;
					ResolvedPath? path = Resolve(compilation, declaring, member.Name, new AccessFailure());
					if (IsNoOpFieldType(entryType))
						entries.Add("N");
					else if (predArray && path != null && PredictionElementType(entryType) != null)
						entries.Add("F|" + entryType + "|S|" + Display(declaring) + "|" + member.Name + "|\0|" + Display(path.Type)
							+ (predContainer == ContainerKind.NetworkArray ? "|1|" + Display(predElement!) : "|0|"));
					else if (path == null || PredictionElementType(entryType) == null || GetContainer(compilation, path.Type, out _, out _) != ContainerKind.None)
						entries.Add("X");
					else
						entries.Add("F|" + entryType + "|S|" + Display(declaring) + "|" + member.Name + "|\0|" + Display(path.Type) + "|0|");
				}
			}

			var sb = new StringBuilder();
			sb.AppendLine("// <auto-generated/>");
			sb.AppendLine("#nullable enable");
			sb.AppendLine();
			bool hasNamespace = !type.ContainingNamespace.IsGlobalNamespace;
			string indent = hasNamespace ? "\t" : "";
			if (hasNamespace) {
				sb.Append("namespace ").AppendLine(type.ContainingNamespace.ToDisplayString());
				sb.AppendLine("{");
			}
			sb.Append(indent).Append(type.IsRecord ? (type.TypeKind == TypeKind.Struct ? "partial record struct " : "partial record ") : type.TypeKind == TypeKind.Struct ? "partial struct " : "partial class ").AppendLine(type.Name);
			sb.Append(indent).AppendLine("{");

			string tableType = isClient ? "global::Source.Common.RecvTable" : "global::Source.Common.SendTable";
			string propType = isClient ? "global::Source.Common.RecvProp" : "global::Source.Common.SendProp";
			foreach (string sub in subTables) {
				sb.Append(indent).Append("\tpublic static readonly ").Append(tableType).Append(' ').Append(sub).Append(" = new(").Append(Literal(sub))
					.Append(", new ").Append(propType).AppendLine("[] {");
				foreach (string prop in props[sub])
					sb.Append(indent).Append("\t\t").Append(prop).AppendLine(",");
				sb.Append(indent).AppendLine("\t});");
			}

			sb.Append(indent).Append("\tpublic static readonly ").Append(tableType).Append(' ').Append(tableName).Append(" = new(")
				.Append(plain ? Literal(tableName) + ", " : noBase ? "" : baseDisplay + "." + baseTableName + ", ").Append("new ").Append(propType).AppendLine("[] {");
			foreach (string prop in props[""])
				sb.Append(indent).Append("\t\t").Append(prop).AppendLine(",");
			sb.Append(indent).AppendLine("\t});");

			bool ownPredMap = !type.GetMembers("PredMap").Any(m => !m.IsImplicitlyDeclared);
			if (plain) {
				if (isClient && ownPredMap && predicted.Count > 0) {
					sb.Append(indent).AppendLine("\tpublic static readonly global::Source.Common.DataMap PredMap = new(new global::Source.Common.TypeDescription[] {");
					foreach (string entry in predicted)
						sb.Append(indent).Append("\t\t").Append(entry).AppendLine(",");
					sb.Append(indent).Append("\t}, typeof(").Append(typeDisplay).AppendLine("), null);");
				}
			}
			else if (isClient) {
				sb.Append(indent).Append(baseType == null ? "\tpublic static readonly global::Source.Common.ClientClass ClientClass = new global::Source.Common.ClientClass(" : "\tpublic static readonly new global::Source.Common.ClientClass ClientClass = new global::Source.Common.ClientClass(").Append(Literal(networkName!)).Append(", ")
					.Append(tableName).Append(", ").Append(Literal(tableName)).Append(", ").Append(isTempEntity ? TempEntityFactory(type) : ClientFactory(type)).AppendLine(");");
				sb.Append(indent).Append(baseType == null ? "\tpublic virtual" : "\tpublic override").AppendLine(" global::Source.Common.ClientClass GetClientClass() => ClientClass;");

				if (isEntity && !type.GetMembers("PredMap").Any(m => !m.IsImplicitlyDeclared)) {
					sb.Append(indent).AppendLine(baseType == null ? "\tpublic static readonly global::Source.Common.DataMap PredMap = new(new global::Source.Common.TypeDescription[] {" : "\tpublic static readonly new global::Source.Common.DataMap PredMap = new(new global::Source.Common.TypeDescription[] {");
					foreach (string entry in predicted)
						sb.Append(indent).Append("\t\t").Append(entry).AppendLine(",");
					sb.Append(indent).Append("\t}, typeof(").Append(typeDisplay).Append("), ").Append(baseType == null ? "null" : baseDisplay + ".PredMap").AppendLine(");");
					sb.Append(indent).AppendLine(baseType == null ? "\tpublic virtual global::Source.Common.DataMap? GetPredDescMap() => PredMap;" : "\tpublic override global::Source.Common.DataMap? GetPredDescMap() => PredMap;");
				}
			}
			else {
				sb.Append(indent).Append(baseType == null ? "\tpublic static readonly global::Source.Common.ServerClass ServerClass = new(" : "\tpublic static readonly new global::Source.Common.ServerClass ServerClass = new(").Append(Literal(networkName!)).Append(", ")
					.Append(tableName).Append(", ").Append(Literal(tableName)).AppendLine(");");
				sb.Append(indent).Append(baseType == null ? "\tpublic virtual" : "\tpublic override").AppendLine(" global::Source.Common.ServerClass GetServerClass() => ServerClass;");
			}

			sb.Append(indent).AppendLine("}");
			if (hasNamespace)
				sb.AppendLine("}");

			PredictionMapModel? map = isClient && ownPredMap && (isEntity || (plain && predicted.Count > 0))
				? new PredictionMapModel(type.ToDisplayString() + ".PredMap", "PredMap", typeDisplay, string.Join("\n", entries), string.Join("\n", embeddedMaps))
				: null;

			string hint = (hasNamespace ? type.ContainingNamespace.ToDisplayString() + "." : "") + type.Name + ".NetworkTable.g.cs";
			return new TableModel(hint, sb.ToString(), accessors, map, diagnostics);
		}

		private static object? SettingValue(PropSettings settings, string name)
			=> settings.Values.TryGetValue(name, out var entry) ? entry.Value : null;

		private static string? SettingEnum(PropSettings settings, string name) {
			if (!settings.Values.TryGetValue(name, out var entry) || entry.Value == null || !(entry.Type is INamedTypeSymbol enumType) || enumType.TypeKind != TypeKind.Enum)
				return null;
			foreach (IFieldSymbol field in enumType.GetMembers().OfType<IFieldSymbol>()) {
				if (field.HasConstantValue && Equals(field.ConstantValue, entry.Value))
					return field.Name == "Auto" ? null : field.Name;
			}
			return null;
		}

		private static string? SettingFlags(PropSettings settings, string name, string enumDisplay)
			=> settings.Values.TryGetValue(name, out var entry) && entry.Value != null ? "(" + enumDisplay + ")" + FormatConstant(entry.Value) : null;

		private static List<IFieldSymbol> ListFields(INamedTypeSymbol type, string attributeName)
			=> type.GetMembers().OfType<IFieldSymbol>()
				.Where(f => f.IsStatic && HasAttribute(f, attributeName) && f.Locations.Length > 0 && f.Locations[0].IsInSource)
				.OrderBy(f => f.Locations[0].SourceTree?.FilePath ?? "", StringComparer.Ordinal)
				.ThenBy(f => f.Locations[0].SourceSpan.Start)
				.ToList();

		private static bool HasNoBaseTable(INamedTypeSymbol type) {
			string? tableName = GetClassTableName(type);
			if (tableName == null)
				return true;
			AttributeData? networkName = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == NetworkNameAttributeName);
			if (networkName != null && NamedValue(networkName, "NoBase") is bool noBase && noBase)
				return true;
			foreach (IFieldSymbol list in ListFields(type, TableAttributeName)) {
				AttributeData? attribute = list.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == TableAttributeName);
				if (attribute != null && SideMatches(attribute, true) && ListTableName(list, tableName) == tableName && NamedValue(attribute, "NoBase") is bool listNoBase && listNoBase)
					return true;
			}
			return false;
		}

		private static bool IsTableField(IFieldSymbol field)
			=> HasAttribute(field, TableAttributeName) || field.Type.Name == "SendTable" || field.Type.Name == "RecvTable";

		private static string ListTableName(IFieldSymbol field, string fallback) {
			AttributeData? attribute = field.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == TableAttributeName);
			return attribute != null && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string name ? name : fallback;
		}

		private static bool SideMatches(AttributeData attribute, bool isClient) {
			object? side = NamedValue(attribute, "Side");
			return side == null || (int)side == 0 || (int)side == (isClient ? 2 : 1);
		}

		private static bool EntrySideMatches(PropSettings settings, bool isClient) {
			object? side = SettingValue(settings, "Side");
			return side == null || (int)side == 0 || (int)side == (isClient ? 2 : 1);
		}

		private sealed class ListEntry
		{
			public ListEntry(string path, ISymbol? symbol, PropSettings settings, Location location) {
				Path = path;
				Symbol = symbol;
				Settings = settings;
				Location = location;
			}

			public string Path { get; }
			public ISymbol? Symbol { get; }
			public PropSettings Settings { get; }
			public Location Location { get; }
		}

		private static List<ListEntry> ParseList(Compilation compilation, INamedTypeSymbol type, IFieldSymbol listField, CancellationToken ct) {
			var result = new List<ListEntry>();
			foreach (SyntaxReference reference in listField.DeclaringSyntaxReferences) {
				if (!(reference.GetSyntax(ct) is VariableDeclaratorSyntax declarator) || declarator.Initializer == null)
					continue;
				SemanticModel model = compilation.GetSemanticModel(declarator.SyntaxTree);
				IEnumerable<ExpressionSyntax> elements = declarator.Initializer.Value switch {
					CollectionExpressionSyntax collection => collection.Elements.OfType<ExpressionElementSyntax>().Select(e => e.Expression),
					ArrayCreationExpressionSyntax array when array.Initializer != null => array.Initializer.Expressions,
					ImplicitArrayCreationExpressionSyntax implicitArray => implicitArray.Initializer.Expressions,
					InitializerExpressionSyntax initializer => initializer.Expressions,
					_ => Enumerable.Empty<ExpressionSyntax>()
				};
				foreach (ExpressionSyntax element in elements) {
					ExpressionSyntax? reference2 = element;
					InitializerExpressionSyntax? initializer = null;
					if (element is BaseObjectCreationExpressionSyntax creation) {
						reference2 = creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
						initializer = creation.Initializer;
					}
					if (reference2 == null) {
						result.Add(new ListEntry(element.ToString(), null, new PropSettings(), element.GetLocation()));
						continue;
					}
					(string path, ISymbol? symbol) = ResolveReference(model, type, reference2, ct);
					result.Add(new ListEntry(path, symbol, PropSettings.From(model, initializer, ct), element.GetLocation()));
				}
			}
			return result;
		}

		private static (string Path, ISymbol? Symbol) ResolveReference(SemanticModel model, INamedTypeSymbol type, ExpressionSyntax expression, CancellationToken ct) {
			if (expression is InvocationExpressionSyntax invocation && invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" } && invocation.ArgumentList.Arguments.Count == 1) {
				ExpressionSyntax target = invocation.ArgumentList.Arguments[0].Expression;
				var parts = new List<string>();
				ExpressionSyntax current = target;
				while (current is MemberAccessExpressionSyntax access) {
					ISymbol? left = model.GetSymbolInfo(access.Expression, ct).Symbol ?? model.GetSymbolInfo(access.Expression, ct).CandidateSymbols.FirstOrDefault();
					parts.Insert(0, access.Name.Identifier.ValueText);
					if (left is ITypeSymbol || left is INamespaceSymbol || left == null)
						break;
					current = access.Expression;
					if (!(current is MemberAccessExpressionSyntax) && current is IdentifierNameSyntax identifier)
						parts.Insert(0, identifier.Identifier.ValueText);
				}
				if (parts.Count == 0 && target is IdentifierNameSyntax simple)
					parts.Add(simple.Identifier.ValueText);
				ISymbol? symbol = model.GetSymbolInfo(target, ct).Symbol ?? model.GetSymbolInfo(target, ct).CandidateSymbols.FirstOrDefault();
				string path = string.Join(".", parts);
				if (parts.Count > 1)
					symbol = LastMember(type, path) ?? symbol;
				return (path, symbol);
			}
			Optional<object?> constant = model.GetConstantValue(expression, ct);
			if (constant.HasValue && constant.Value is string text)
				return (text, LastMember(type, text));
			return (expression.ToString(), null);
		}

		private sealed class PropSettings
		{
			public readonly Dictionary<string, (object? Value, ITypeSymbol? Type)> Values = new Dictionary<string, (object? Value, ITypeSymbol? Type)>(StringComparer.Ordinal);

			public static PropSettings From(SemanticModel model, InitializerExpressionSyntax? initializer, CancellationToken ct) {
				var settings = new PropSettings();
				if (initializer == null)
					return settings;
				foreach (ExpressionSyntax expression in initializer.Expressions) {
					if (!(expression is AssignmentExpressionSyntax assignment) || !(assignment.Left is IdentifierNameSyntax left))
						continue;
					string key = left.Identifier.ValueText;
					if (assignment.Right is TypeOfExpressionSyntax typeOf) {
						settings.Values[key] = (model.GetTypeInfo(typeOf.Type, ct).Type, null);
						continue;
					}
					Optional<object?> constant = model.GetConstantValue(assignment.Right, ct);
					TypeInfo info = model.GetTypeInfo(assignment.Right, ct);
					object? value = constant.HasValue ? constant.Value : null;
					if (value != null && info.ConvertedType?.SpecialType == SpecialType.System_Single && !(value is float))
						value = Convert.ToSingle(value, CultureInfo.InvariantCulture);
					else if (value != null && info.ConvertedType?.SpecialType == SpecialType.System_Int32 && !(value is int))
						value = Convert.ToInt32(value, CultureInfo.InvariantCulture);
					settings.Values[key] = (value, info.ConvertedType ?? info.Type);
				}
				return settings;
			}
		}

		private static ISymbol? LastMember(INamedTypeSymbol type, string path) {
			ITypeSymbol? current = type;
			ISymbol? member = null;
			foreach (string segment in path.Split('.')) {
				if (current == null)
					return null;
				member = FindMember(current, segment);
				current = member switch {
					IFieldSymbol field => field.Type,
					IPropertySymbol property => property.Type,
					_ => null
				};
			}
			return member;
		}

		private static bool InheritsFrom(INamedTypeSymbol type, string fullName) {
			for (INamedTypeSymbol? t = type; t != null; t = t.BaseType) {
				if (t.ToDisplayString() == fullName)
					return true;
			}
			return false;
		}

		private static string TempEntityFactory(INamedTypeSymbol type) {
			string factory = "static (entNum, serialNum) => throw new global::System.NotImplementedException(" + Literal("Temp entity '" + type.Name + "' is created as an event") + ")";
			if (type.IsAbstract || !type.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0))
				return factory;
			return factory + ").AsEvent<" + Display(type) + ">(";
		}

		private static string? GetClassTableName(INamedTypeSymbol type) {
			foreach (AttributeData attribute in type.GetAttributes()) {
				if (attribute.AttributeClass?.ToDisplayString() == NetworkNameAttributeName && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string name && name.Length > 1)
					return NamedValue(attribute, "Table") as string ?? "DT_" + name.Substring(1);
			}
			return null;
		}

		private static string? ProxyMethod(ITypeSymbol? proxyType, bool isClient) {
			if (proxyType == null)
				return null;
			string name = isClient ? "Recv" : "Send";
			if (!proxyType.GetMembers(name).Any(m => m is IMethodSymbol method && method.IsStatic))
				return null;
			return Display(proxyType) + "." + name;
		}

		private static string? BuildProp(bool isClient, PropSettings attribute, ITypeSymbol type, string field, out bool proxyUnsupported, out string encodingName, string? forcedEncoding = null) {
			bool[] unsupported = { false };
			string? result = BuildPropCore(isClient, attribute, type, field, unsupported, out encodingName, forcedEncoding);
			proxyUnsupported = unsupported[0];
			return result;
		}

		private static string? BuildPropCore(bool isClient, PropSettings attribute, ITypeSymbol type, string field, bool[] proxyUnsupported, out string encodingName, string? forcedEncoding) {
			string? encoding = forcedEncoding ?? SettingEnum(attribute, "Encoding");
			if (encoding == null || encoding == "Auto")
				encoding = InferEncoding(type);
			encodingName = encoding ?? "";
			if (encoding == null)
				return null;

			var args = new List<string> { field };
			object? bits = SettingValue(attribute, "Bits");
			string? flags = SettingFlags(attribute, "Flags", "global::Source.Common.PropFlags");
			object? low = SettingValue(attribute, "Low");
			object? high = SettingValue(attribute, "High");
			string? proxy = ProxyMethod(SettingValue(attribute, "Proxy") as ITypeSymbol, isClient);
			void Add(string name, object? value, string? text = null) {
				if (value != null)
					args.Add(name + ": " + (text ?? FormatConstant(value)));
			}
			string Call(string method) {
				Add("proxyFn", proxy, proxy);
				return method + "(" + string.Join(", ", args) + ")";
			}
			string? NoProxy(string call) {
				if (proxy == null)
					return call;
				proxyUnsupported[0] = true;
				return null;
			}

			if (isClient) {
				string recv = "global::Source.Common.RecvPropHelpers.";
				string recvProxy = "global::Game.Client.RecvProxy.";
				switch (encoding) {
					case "Int": case "ModelIndex": return Call(recv + "RecvPropInt");
					case "Bool": return proxy == null ? recvProxy + "RecvPropBool(" + field + ")" : Call(recv + "RecvPropInt");
					case "Float": case "Angle": return Call(recv + "RecvPropFloat");
					case "Time": return NoProxy(recvProxy + "RecvPropTime(" + field + ")");
					case "Time64": return NoProxy(recvProxy + "RecvPropTime64(" + field + ")");
					case "Vector": return Call(recv + "RecvPropVector");
					case "VectorXY": return Call(recv + "RecvPropVectorXY");
					case "QAngles": return Call(recv + "RecvPropQAngles");
					case "EHandle": return Call(recv + "RecvPropEHandle");
					case "String":
						Add("bufferSize", StringBufferSize(type));
						return Call(recv + "RecvPropString");
					case "IntWithMinusOneFlag": return Call(recvProxy + "RecvPropIntWithMinusOneFlag");
					default: return null;
				}
			}

			string send = "global::Source.Common.SendPropHelpers.";
			string sendProxy = "global::Game.Server.SendProxy.";
			switch (encoding) {
				case "Int":
					Add("bits", bits); Add("flags", flags, flags);
					return Call(send + "SendPropInt");
				case "Bool":
					if (proxy == null)
						return sendProxy + "SendPropBool(" + field + ")";
					args.Add("bits: 1");
					args.Add("flags: global::Source.Common.PropFlags.Unsigned");
					return Call(send + "SendPropInt");
				case "Float": case "Vector": case "VectorXY": case "Angle":
					Add("bits", bits); Add("flags", flags, flags); Add("lowValue", low); Add("highValue", high);
					return Call(send + "SendProp" + encoding);
				case "QAngles":
					Add("bits", bits); Add("flags", flags, flags);
					return Call(send + "SendPropQAngles");
				case "Time": return NoProxy(sendProxy + "SendPropTime(" + field + ")");
				case "Time64": return NoProxy(sendProxy + "SendPropTime64(" + field + ")");
				case "EHandle":
					Add("flags", flags, flags);
					return Call(sendProxy + "SendPropEHandle");
				case "ModelIndex": return NoProxy(send + "SendPropModelIndex(" + field + ")");
				case "String":
					Add("bufferSize", StringBufferSize(type)); Add("flags", flags, flags);
					return Call(send + "SendPropString");
				case "IntWithMinusOneFlag":
					args.Add("bits: " + FormatConstant(bits ?? 32));
					return Call(sendProxy + "SendPropIntWithMinusOneFlag");
				default: return null;
			}
		}

		private static string PredictionDescription(string fieldType, ISymbol member, string size, string flags, string embeddedMap, string tolerance)
			=> "new global::Source.Common.TypeDescription(global::Source.Common.FieldType." + fieldType + ", " + Literal(member.Name)
				+ ", global::Source.FIELD<" + Display(member.ContainingType) + ">.OF(" + Literal(member.Name) + "), " + size
				+ ", (global::Source.Common.FieldTypeDescFlags)(" + flags + "), " + embeddedMap + ", " + tolerance + ")";

		private static object? StringBufferSize(ITypeSymbol type) {
			if (!(type is INamedTypeSymbol named))
				return null;
			AttributeData? inlineArray = named.OriginalDefinition.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == InlineArrayAttributeName);
			return inlineArray != null && inlineArray.ConstructorArguments.Length == 1 && inlineArray.ConstructorArguments[0].Value is int length ? length : (object?)null;
		}

		private static string? InferEncoding(ITypeSymbol type) {
			if (type is INamedTypeSymbol named && named.OriginalDefinition.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == InlineArrayAttributeName)
				&& named.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => !f.IsStatic)?.Type.SpecialType == SpecialType.System_Char)
				return "String";
			if (type.TypeKind == TypeKind.Enum)
				return "Int";
			switch (type.SpecialType) {
				case SpecialType.System_Boolean: return "Bool";
				case SpecialType.System_Byte: case SpecialType.System_SByte: case SpecialType.System_Int16: case SpecialType.System_UInt16:
				case SpecialType.System_Int32: case SpecialType.System_UInt32: case SpecialType.System_Int64: case SpecialType.System_UInt64:
					return "Int";
				case SpecialType.System_Single: return "Float";
				case SpecialType.System_Double: return "Time64";
			}
			string display = type.OriginalDefinition.ToDisplayString();
			switch (display) {
				case "System.Numerics.Vector3": return "Vector";
				case "Source.Common.Mathematics.QAngle": return "QAngles";
				case "Source.Common.Handle<T>": case "Source.Common.BaseHandle": return "EHandle";
			}
			return null;
		}

		private static string? InferFieldType(ITypeSymbol type) {
			if (type.TypeKind == TypeKind.Enum)
				return "Integer";
			switch (type.SpecialType) {
				case SpecialType.System_Boolean: return "Boolean";
				case SpecialType.System_Byte: case SpecialType.System_SByte: return "Character";
				case SpecialType.System_Int16: case SpecialType.System_UInt16: return "Short";
				case SpecialType.System_Int32: case SpecialType.System_UInt32: return "Integer";
				case SpecialType.System_Single: return "Float";
				case SpecialType.System_Double: return "Double";
			}
			switch (type.OriginalDefinition.ToDisplayString()) {
				case "System.Numerics.Vector3": case "Source.Common.Mathematics.QAngle": return "Vector";
				case "Source.Common.Handle<T>": case "Source.Common.BaseHandle": return "EHandle";
				case "Source.Color": return "Color32";
			}
			return null;
		}

		private static object? NamedValue(AttributeData attribute, string name) {
			foreach (KeyValuePair<string, TypedConstant> arg in attribute.NamedArguments) {
				if (arg.Key == name)
					return arg.Value.Value;
			}
			return null;
		}

		private static string FormatConstant(object value) => value switch {
			float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
			double d => d.ToString("R", CultureInfo.InvariantCulture),
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => value.ToString() ?? ""
		};

		private static string ClientFactory(INamedTypeSymbol type) {
			string fullyQualified = Display(type);
			foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>()) {
				if (method.IsStatic && method.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == FactoryAttributeName))
					return fullyQualified + "." + method.Name;
			}
			if (type.IsAbstract || !type.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0))
				return "static (entNum, serialNum) => throw new global::System.NotImplementedException(" + Literal("ClientClass did not define how to create type '" + type.Name + "'") + ")";
			return "static (entNum, serialNum) => { " + fullyQualified + " entity = new " + fullyQualified + "(); return entity.Init(entNum, serialNum) ? entity : null!; }";
		}

		private sealed class TableDiagnostic : IEquatable<TableDiagnostic>
		{
			public TableDiagnostic(DiagnosticDescriptor descriptor, Location? location, params object[] args) {
				Descriptor = descriptor;
				Location = location;
				Args = args;
			}

			public DiagnosticDescriptor Descriptor { get; }
			public Location? Location { get; }
			public object[] Args { get; }

			public bool Equals(TableDiagnostic? other) => other != null && Descriptor.Id == other.Descriptor.Id && Equals(Location, other.Location) && Args.SequenceEqual(other.Args);
			public override bool Equals(object? obj) => Equals(obj as TableDiagnostic);
			public override int GetHashCode() => Descriptor.Id.GetHashCode();
		}

		private sealed class TableModel : IEquatable<TableModel>
		{
			public TableModel(string hintName, string source, List<FieldAccessorModel> accessors, PredictionMapModel? map, List<TableDiagnostic> diagnostics) {
				HintName = hintName;
				Source = source;
				Accessors = accessors;
				Map = map;
				Diagnostics = diagnostics;
			}

			public string HintName { get; }
			public string Source { get; }
			public List<FieldAccessorModel> Accessors { get; }
			public PredictionMapModel? Map { get; }
			public List<TableDiagnostic> Diagnostics { get; }

			public bool Equals(TableModel? other)
				=> other != null && HintName == other.HintName && Source == other.Source && Equals(Map, other.Map)
					&& Accessors.SequenceEqual(other.Accessors) && Diagnostics.SequenceEqual(other.Diagnostics);

			public override bool Equals(object? obj) => Equals(obj as TableModel);
			public override int GetHashCode() => HintName.GetHashCode() ^ Source.GetHashCode();
		}
	}
}
