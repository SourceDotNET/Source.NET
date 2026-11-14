namespace Source.Common;

public class UtlMultiList<T> where T : class, new()
{
	struct ListElem
	{
		public T Element;
		public ushort Previous;
		public ushort Next;
	}

	struct List
	{
		public ushort Head;
		public ushort Tail;
		public ushort Count;
	}

	ListElem[] Memory = [];
	readonly List<List> Lists = [];
	ushort FirstFree;
	ushort TotalElements;
	int MaxElementIndex;

	public UtlMultiList() => ConstructList();

	void ConstructList() {
		FirstFree = InvalidIndex();
		TotalElements = 0;
		MaxElementIndex = 0;
	}

	public T Element(ushort i) => Memory[i].Element;
	public T this[ushort i] => Memory[i].Element;

	public void EnsureCapacity(int num) => throw new NotImplementedException();

	public void Purge() {
		RemoveAll();
		Lists.Clear();
		Memory = [];
		FirstFree = InvalidIndex();
		TotalElements = 0;
		MaxElementIndex = 0;
	}

	public ushort CreateList() {
		Lists.Add(new List { Head = InvalidIndex(), Tail = InvalidIndex(), Count = 0 });
		return (ushort)(Lists.Count - 1);
	}

	public void DestroyList(ushort list) => throw new NotImplementedException();
	public bool IsValidList(ushort list) => list < Lists.Count;

	public ushort InsertBefore(ushort list, ushort before) => throw new NotImplementedException();
	public ushort InsertAfter(ushort list, ushort after) => throw new NotImplementedException();
	public ushort AddToHead(ushort list) => throw new NotImplementedException();
	public ushort AddToTail(ushort list) => throw new NotImplementedException();

	public void Remove(ushort list, ushort elem) => throw new NotImplementedException();
	public void RemoveAll(ushort list) => throw new NotImplementedException();

	public void RemoveAll() {
		if (MaxElementIndex == 0)
			return;

		for (int i = 0; i < MaxElementIndex; i++)
			Memory[i].Element = null!;

		for (int i = 0; i < Lists.Count; i++)
			Lists[i] = new List { Head = InvalidIndex(), Tail = InvalidIndex(), Count = 0 };

		FirstFree = InvalidIndex();
		TotalElements = 0;
		MaxElementIndex = 0;
	}

	public ushort Alloc() {
		ushort elem;
		if (FirstFree == InvalidIndex()) {
			if (!IndexInRange(MaxElementIndex))
				Error("CUtlMultiList overflow! (exhausted index range)\n");

			if (MaxElementIndex == Memory.Length)
				Array.Resize(ref Memory, Memory.Length == 0 ? 1 : Memory.Length * 2);

			elem = (ushort)MaxElementIndex;
			++MaxElementIndex;
		}
		else {
			elem = FirstFree;
			FirstFree = Memory[FirstFree].Next;
		}

		Memory[elem].Next = Memory[elem].Previous = elem;

		++TotalElements;

		Memory[elem].Element = new();

		return elem;
	}

	public void Free(ushort elem) => throw new NotImplementedException();

	public void LinkBefore(ushort list, ushort before, ushort elem) => throw new NotImplementedException();

	public void LinkAfter(ushort list, ushort after, ushort elem) {
		Unlink(list, elem);

		ref ListElem newElem = ref Memory[elem];
		List l = Lists[list];

		newElem.Previous = after;
		if (after == InvalidIndex()) {
			newElem.Next = l.Head;
			l.Head = elem;
		}
		else {
			ref ListElem afterElem = ref Memory[after];
			newElem.Next = afterElem.Next;
			afterElem.Next = elem;
		}

		if (newElem.Next == InvalidIndex())
			l.Tail = elem;
		else
			Memory[newElem.Next].Previous = elem;

		++l.Count;
		Lists[list] = l;
	}

	public void Unlink(ushort list, ushort elem) {
		if (IsInList(elem)) {
			ref ListElem oldElem = ref Memory[elem];
			List l = Lists[list];

			if (oldElem.Previous != InvalidIndex())
				Memory[oldElem.Previous].Next = oldElem.Next;
			else
				l.Head = oldElem.Next;

			if (oldElem.Next != InvalidIndex())
				Memory[oldElem.Next].Previous = oldElem.Previous;
			else
				l.Tail = oldElem.Previous;

			oldElem.Previous = oldElem.Next = elem;

			--l.Count;
			Lists[list] = l;
		}
	}

	public void LinkToHead(ushort list, ushort elem) => LinkAfter(list, InvalidIndex(), elem);
	public void LinkToTail(ushort list, ushort elem) => throw new NotImplementedException();

	public static ushort InvalidIndex() => ushort.MaxValue;
	public static bool IndexInRange(int index) => (ushort)index == index && (ushort)index != InvalidIndex();

	public ushort Count(ushort list) => Lists[list].Count;
	public ushort TotalCount() => TotalElements;
	public int GetMaxElementIndex() => MaxElementIndex;

	public ushort Head(ushort list) => Lists[list].Head;
	public ushort Tail(ushort list) => Lists[list].Tail;
	public ushort Previous(ushort i) => Memory[i].Previous;
	public ushort Next(ushort i) => Memory[i].Next;

	public bool IsValidIndex(ushort i) {
		if (i < MaxElementIndex) {
			ref ListElem value = ref Memory[i];
			return value.Previous != i || value.Next == i;
		}
		return false;
	}

	public bool IsInList(ushort i) => i < MaxElementIndex && Previous(i) != i;
}
