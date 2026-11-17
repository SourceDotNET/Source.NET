namespace Source.Common;

public delegate void NetworkVarChanged<T>(ref T newValue);

public struct NetworkVarBase<Type>
{
	public Type Value;
	public NetworkVarChanged<Type>? VarChanged;
}

// TODO: Can we make this a source generator of some kind. It is ridiculous I have to double define the count.
[AttributeUsage(AttributeTargets.Field)]
public class NetworkArraySizeAttribute(int size) : Attribute{
	public int Size => size;
}

public struct NetworkArray<Type>(int count) where Type : unmanaged
{
	public readonly Type[] Value = new Type[count];
	public NetworkVarChanged<Type>? VarChanged;

	public static implicit operator Type[](NetworkArray<Type> netArray) => netArray.Value;

	public readonly ref readonly Type this[int i] => ref Get(i);

	public readonly ref readonly Type Get(int i) {
		Assert(i >= 0 && i < count);
		return ref Value[i];
	}

	public readonly ref Type GetForModify(int i) {
		Assert(i >= 0 && i < count);
		NetworkStateChanged(i);
		return ref Value[i];
	}

	public readonly void Set(int i, in Type val) {
		Assert(i >= 0 && i < count);
		if (memcmp(in Value[i], in val) != 0) {
			NetworkStateChanged(i);
			Value[i] = val;
		}
	}

	public readonly Type[] Base => Value;
	public readonly int Count() => count;

	public void Hook(NetworkVarChanged<Type> fn) => VarChanged = fn;

	private readonly void NetworkStateChanged(int changeIndex) {
		if (VarChanged == null)
			return;
		VarChanged(ref Value[changeIndex]);
	}
}
