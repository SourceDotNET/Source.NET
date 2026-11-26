using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Source.Common.Utilities;

public static class UtlSymbolGlobals {
	public const UtlSymId_t UTL_INVAL_SYMBOL = unchecked((UtlSymId_t)~0);
}

[DebuggerDisplay("UtlSymbol {String()}")]
public struct UtlSymbol
{
	public static readonly UtlSymId_t UTL_INVAL_SYMBOL = UtlSymId_t.MaxValue;
	// Instance fields
	private static UtlSymbolTableMT? SymbolTable;
	private readonly UtlSymId_t id;
	private readonly bool ValidId; // Trick because Id may equal 0 when uninitialized.

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public readonly bool Equals(ReadOnlySpan<char> other, StringComparison comparison) => String().Equals(other, comparison);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public readonly bool Equals(UtlSymbol other, StringComparison comparison) => Equals(other.String(), comparison);

	// Instance methods
	public UtlSymbol() {
		id = UTL_INVAL_SYMBOL;
		ValidId = false;
	}
	public UtlSymbol(ReadOnlySpan<char> str) {
		id = CurrTable().AddString(str);
		ValidId = id != UTL_INVAL_SYMBOL;
	}
	public UtlSymbol(string str) {
		id = CurrTable().AddString(str);
		ValidId = id != UTL_INVAL_SYMBOL;
	}
	public UtlSymbol(UtlSymId_t str) {
		id = str;
		ValidId = id != UTL_INVAL_SYMBOL;
	}
	public UtlSymbol(in UtlSymbol symbol) {
		id = symbol.id;
		ValidId = id != UTL_INVAL_SYMBOL;
	}
	public readonly string? String() => CurrTable().String(id);
	public readonly char this[int i] {
		get {
			string? str = String();
			if (str != null && i < str.Length)
				return str[i];
			return '\0';
		}
	}
	public readonly bool IsValid() => ValidId && id != UTL_INVAL_SYMBOL;
	public readonly UtlSymId_t Id => ValidId ? id : UTL_INVAL_SYMBOL;

	// Static members
	static bool symbolsInitialized = false;

	// Static methods
	[MemberNotNull(nameof(SymbolTable))]
#pragma warning disable CS8774 // Member must have a non-null value when exiting.
	static void Initialize() {
		if (!symbolsInitialized) {
			SymbolTable = new UtlSymbolTableMT();
			symbolsInitialized = true;
		}
	}
#pragma warning restore CS8774 // Member must have a non-null value when exiting.

	public static UtlSymbolTableMT CurrTable() {
		Initialize();
		return SymbolTable;
	}

	// Operators
	public static bool operator ==(UtlSymbol symbol, ReadOnlySpan<char> str) {
		if (!symbol.IsValid())
			return false;
		return str.SliceNullTerminatedString().SequenceEqual(symbol.String());
	}
	public static bool operator !=(UtlSymbol symbol, ReadOnlySpan<char> str) => !(symbol == str);
	public static implicit operator UtlSymId_t(UtlSymbol symbol) => symbol.id;
	public static implicit operator UtlSymbol(ReadOnlySpan<char> txt) => new(txt);
	public static implicit operator ReadOnlySpan<char>(UtlSymbol symbol) => symbol.String();

	public override readonly bool Equals(object? obj) => obj is UtlSymbol sym && sym.id == id;
	public override readonly int GetHashCode() => id.GetHashCode();
}
