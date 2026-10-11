namespace Source.Common;

/// <summary>
/// An opaque entity handle.
/// </summary>
public struct BaseHandle : IEquatable<BaseHandle>, IBaseHandle
{
	public readonly bool Equals(BaseHandle otherHandle) => Index == otherHandle.Index;
	public uint Index;

	public readonly uint Unpack() => Index;


	public BaseHandle() => Index = (uint)Constants.INVALID_EHANDLE_INDEX;
	public BaseHandle(uint value) => Index = value;
	public BaseHandle(in BaseHandle handle) => Index = handle.Index;
	public BaseHandle(int entry, int serial) => Index = (uint)(entry | (serial << Constants.NUM_ENT_ENTRY_BITS));

	public readonly int GetEntryIndex() => (int)(Index & Constants.ENT_ENTRY_MASK);
	public readonly int GetSerialNumber() => (int)(Index >> Constants.NUM_ENT_ENTRY_BITS);

	public static bool operator ==(BaseHandle a, BaseHandle b) => a.Index == b.Index;
	public static bool operator !=(BaseHandle a, BaseHandle b) => a.Index != b.Index;
	public static bool operator <(BaseHandle a, BaseHandle b) => a.Index < b.Index;
	public static bool operator >(BaseHandle a, BaseHandle b) => a.Index > b.Index;
	public override readonly bool Equals(object? obj) {
		return obj switch {
			BaseHandle b => Index == b.Index,
			_ => false
		};
	}
	public override readonly int GetHashCode() => (int)Index;
	public readonly bool IsValid() => Index != Constants.INVALID_EHANDLE_INDEX;
}


/// <summary>
/// A typed entity handle.
/// </summary>
/// <typeparam name="T"></typeparam>
public struct Handle<T> : IEquatable<Handle<T>>, IEquatable<BaseHandle>, IBaseHandle
{
	public readonly uint Unpack() => Index;

	public readonly bool Equals(Handle<T> otherHandle) => Index == otherHandle.Index;
	public readonly bool Equals(BaseHandle otherHandle) => Index == otherHandle.Index;
	public uint Index;

	public Handle() => Index = (uint)Constants.INVALID_EHANDLE_INDEX;
	public Handle(uint value) => Index = value;
	public Handle(in BaseHandle handle) => Index = handle.Index;
	public Handle(int entry, int serial) => Index = (uint)(entry | (serial << Constants.NUM_ENT_ENTRY_BITS));

	public readonly int GetEntryIndex() => (int)(Index & Constants.ENT_ENTRY_MASK);
	public readonly int GetSerialNumber() => (int)(Index >> Constants.NUM_ENT_ENTRY_BITS);

	public static bool operator ==(BaseHandle a, Handle<T> b) => a.Index == b.Index;
	public static bool operator !=(BaseHandle a, Handle<T> b) => a.Index != b.Index;
	public static bool operator <(BaseHandle a, Handle<T> b) => a.Index < b.Index;
	public static bool operator >(BaseHandle a, Handle<T> b) => a.Index > b.Index;

	public static bool operator ==(Handle<T> a, BaseHandle b) => a.Index == b.Index;
	public static bool operator !=(Handle<T> a, BaseHandle b) => a.Index != b.Index;
	public static bool operator <(Handle<T> a, BaseHandle b) => a.Index < b.Index;
	public static bool operator >(Handle<T> a, BaseHandle b) => a.Index > b.Index;

	public static bool operator ==(Handle<T> a, Handle<T> b) => a.Index == b.Index;
	public static bool operator !=(Handle<T> a, Handle<T> b) => a.Index != b.Index;
	public static bool operator <(Handle<T> a, Handle<T> b) => a.Index < b.Index;
	public static bool operator >(Handle<T> a, Handle<T> b) => a.Index > b.Index;

	public override readonly bool Equals(object? obj) {
		return obj switch {
			BaseHandle b => Index == b.Index,
			Handle<T> b => Index == b.Index,
			IBaseHandle b => Index == b.Unpack(),
			_ => false
		};
	}

	public static implicit operator Handle<T>(BaseHandle handle) => new(handle.Index);
	public static implicit operator BaseHandle(Handle<T> handle) => new(handle.Index);

	public override readonly int GetHashCode() => (int)Index;
	public readonly bool IsValid() => Index != Constants.INVALID_EHANDLE_INDEX;
}

public static class BaseHandleExts
{
	public static void Invalidate(ref this BaseHandle handle) => handle.Index = (uint)Constants.INVALID_EHANDLE_INDEX;

	public static void Init(ref this BaseHandle handle, in BaseHandle otherHandle) => handle.Index = otherHandle.Index;
	public static void Init(ref this BaseHandle handle, uint entindex) => handle.Index = entindex;
	public static void Init(ref this BaseHandle handle, ulong entindex) => handle.Index = (uint)entindex;
	public static void Init(ref this BaseHandle handle, int entry, int serial) => handle.Index = (uint)(entry | (serial << Constants.NUM_ENT_ENTRY_BITS));

	public static BaseHandle Set(ref this BaseHandle handle, IHandleEntity? entity) {
		if (entity != null)
			handle.Index = entity.GetRefEHandle().Index;
		else
			handle.Index = Constants.INVALID_EHANDLE_INDEX;
		return handle;
	}

	public static void Invalidate<T>(ref this Handle<T> handle) => handle.Index = (uint)Constants.INVALID_EHANDLE_INDEX;

	public static void Init<T>(ref this Handle<T> handle, in BaseHandle otherHandle) => handle.Index = otherHandle.Index;
	public static void Init<T>(ref this Handle<T> handle, uint entindex) => handle.Index = entindex;
	public static void Init<T>(ref this Handle<T> handle, ulong entindex) => handle.Index = (uint)entindex;
	public static void Init<T>(ref this Handle<T> handle, int entry, int serial) => handle.Index = (uint)(entry | (serial << Constants.NUM_ENT_ENTRY_BITS));

	public static Handle<T> Set<T>(ref this Handle<T> handle, IHandleEntity? entity) {
		if (entity != null)
			handle.Index = entity.GetRefEHandle().Index;
		else
			handle.Index = Constants.INVALID_EHANDLE_INDEX;
		return handle;
	}
}
