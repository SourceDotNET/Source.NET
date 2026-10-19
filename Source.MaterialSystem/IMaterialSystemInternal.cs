using Source.Common;
using Source.Common.Formats.Keyvalues;
using Source.Common.MaterialSystem;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Source.MaterialSystem;


public sealed class MatCallQueue : ICallQueue
{
	abstract class CallBuffer
	{
		public bool Used;
		public abstract void Execute(int index);
		public abstract void Clear();
	}

	sealed class ActionBuffer : CallBuffer
	{
		Action[] Funcs = new Action[64];
		int Count;

		public int Add(Action func) {
			if (Count == Funcs.Length)
				Array.Resize(ref Funcs, Count * 2);
			Funcs[Count] = func;
			return Count++;
		}

		public override void Execute(int index) => Funcs[index]();

		public override void Clear() {
			Array.Clear(Funcs, 0, Count);
			Count = 0;
		}
	}

	sealed class CallBuffer<T> : CallBuffer
	{
		Action<T>[] Funcs = new Action<T>[16];
		T[] Args = new T[16];
		int Count;

		public int Add(Action<T> func, in T args) {
			if (Count == Args.Length) {
				Array.Resize(ref Funcs, Count * 2);
				Array.Resize(ref Args, Count * 2);
			}
			Funcs[Count] = func;
			Args[Count] = args;
			return Count++;
		}

		public override void Execute(int index) => Funcs[index](Args[index]);

		public override void Clear() {
			Array.Clear(Funcs, 0, Count);
			if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
				Array.Clear(Args, 0, Count);
			Count = 0;
		}
	}

	struct Entry
	{
		public CallBuffer Buffer;
		public int Index;
	}

	static int NextTypeID = 1;
	static class TypeID<T>
	{
		public static readonly int Value = Interlocked.Increment(ref NextTypeID) - 1;
	}

	readonly ActionBuffer Actions = new();
	CallBuffer?[] Buffers = new CallBuffer?[64];
	readonly List<CallBuffer> UsedBuffers = [];
	Entry[] Entries = new Entry[4096];
	int EntryCount;

	public int Count() => EntryCount;

	void AddEntry(CallBuffer buffer, int index) {
		if (!buffer.Used) {
			buffer.Used = true;
			UsedBuffers.Add(buffer);
		}
		if (EntryCount == Entries.Length)
			Array.Resize(ref Entries, EntryCount * 2);
		ref Entry entry = ref Entries[EntryCount++];
		entry.Buffer = buffer;
		entry.Index = index;
	}

	public void QueueCall(Action func) => AddEntry(Actions, Actions.Add(func));

	public void QueueCall<T>(Action<T> func, in T args) {
		int id = TypeID<T>.Value;
		if (id >= Buffers.Length)
			Array.Resize(ref Buffers, Math.Max(id + 1, Buffers.Length * 2));
		CallBuffer<T> buffer = (CallBuffer<T>?)Buffers[id] ?? (CallBuffer<T>)(Buffers[id] = new CallBuffer<T>());
		AddEntry(buffer, buffer.Add(func, in args));
	}

	public void CallQueued() {
		for (int i = 0; i < EntryCount; i++) {
			ref Entry entry = ref Entries[i];
			entry.Buffer.Execute(entry.Index);
		}
		Flush();
	}

	public void Flush() {
		Array.Clear(Entries, 0, EntryCount);
		EntryCount = 0;
		for (int i = 0; i < UsedBuffers.Count; i++) {
			UsedBuffers[i].Clear();
			UsedBuffers[i].Used = false;
		}
		UsedBuffers.Clear();
	}

	public void QueueFunctorInternal(ref Functor functor) {
		throw new NotImplementedException();
	}
}


public interface IMaterialSystemInternal : IMaterialSystem
{
	// Returns the current material
	IMaterial? GetCurrentMaterial();

	int GetLightmapPage();

	// Gets the maximum lightmap page size...
	int GetLightmapWidth(int lightmap);
	int GetLightmapHeight(int lightmap);

	ITexture? GetLocalCubemap();

	void ForceDepthFuncEquals(bool enable);
	MaterialHeightClipMode GetHeightClipMode();

	void AddMaterialToMaterialList(IMaterialInternal? material);
	void RemoveMaterial(IMaterialInternal? material);
	void RemoveMaterialSubRect(IMaterialInternal? material);
	bool InFlashlightMode();

	// Can we use editor materials?
	bool CanUseEditorMaterials();
	ReadOnlySpan<char> GetForcedTextureLoadPathID();

	MatCallQueue? GetRenderCallQueue();

	void UnbindMaterial(IMaterial? material);
	uint GetRenderThreadId();

	IMaterialProxy? DetermineProxyReplacements(IMaterial? material, KeyValues? fallbackKeyValues);
}
