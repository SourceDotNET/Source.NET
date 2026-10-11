namespace Source.Common;

/// <summary>
/// An opaque entity handle.
/// </summary>
public struct BaseHandle : IEquatable<BaseHandle>, IBaseHandle
{
	public readonly bool Equals(BaseHandle otherHandle) => Index == otherHandle.Index;
	public uint Index;

	public readonly uint Unpack() => Index;


	public void Invalidate() => Index = (uint)Constants.INVALID_EHANDLE_INDEX;
	public BaseHandle() => Invalidate();
	public BaseHandle(uint value) => Index = value;
	public BaseHandle(in BaseHandle handle) => Index = handle.Index;
	public BaseHandle(int entry, int serial) => Init(entry, serial);

	public void Init(in BaseHandle otherHandle) => Index = otherHandle.Index;
	public void Init(uint entindex) => Index = entindex;
	public void Init(ulong entindex) => Index = (uint)entindex;
	public void Init(int entry, int serial) => Index = (uint)(entry | (serial << Constants.NUM_ENT_ENTRY_BITS));

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

	public BaseHandle Set(IHandleEntity? entity) {
		if (entity != null)
			this.Index = entity.GetRefEHandle().Index;
		else
			Index = Constants.INVALID_EHANDLE_INDEX;
		return this;
	}
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

	public void Invalidate() => Index = (uint)Constants.INVALID_EHANDLE_INDEX;
	public Handle() => Invalidate();
	public Handle(uint value) => Index = value;
	public Handle(in BaseHandle handle) => Index = handle.Index;
	public Handle(int entry, int serial) => Init(entry, serial);

	public void Init(in BaseHandle otherHandle) => Index = otherHandle.Index;
	public void Init(uint entindex) => Index = entindex;
	public void Init(ulong entindex) => Index = (uint)entindex;
	public void Init(int entry, int serial) => Index = (uint)(entry | (serial << Constants.NUM_ENT_ENTRY_BITS));

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

	public Handle<T> Set(IHandleEntity? entity) {
		if (entity != null)
			this.Index = entity.GetRefEHandle().Index;
		else
			Index = Constants.INVALID_EHANDLE_INDEX;
		return this;
	}
}
