namespace Source.ShaderAPI.Gl46;

public unsafe class PersistentBufferRing : IDisposable
{
	const int MEMORY_BUDGET = 48 * 1024 * 1024;
	const int MIN_SLOTS = 8;
	const int MAX_SLOTS = 256;
	const ulong WAIT_TIMEOUT_NS = 1_000_000_000;

	sealed class Slot
	{
		public uint Buffer;
		public byte* Memory;
		public void* Fence;
	}

	readonly List<Slot> Slots = [];
	readonly int Size;
	readonly int MaxSlots;
	int Current;

	public PersistentBufferRing(int size) {
		Size = size;
		MaxSlots = Math.Clamp(MEMORY_BUDGET / Math.Max(size, 1), MIN_SLOTS, MAX_SLOTS);
		Slots.Add(CreateSlot());
	}

	public uint Buffer => Slots[Current].Buffer;
	public byte* Memory => Slots[Current].Memory;

	Slot CreateSlot() {
		Slot slot = new();
		slot.Buffer = glCreateBuffer();
		glNamedBufferStorage(slot.Buffer, Size, null, GL_MAP_WRITE_BIT | GL_MAP_PERSISTENT_BIT | GL_MAP_COHERENT_BIT);
		slot.Memory = (byte*)glMapNamedBufferRange(slot.Buffer, 0, Size, GL_MAP_WRITE_BIT | GL_MAP_PERSISTENT_BIT | GL_MAP_COHERENT_BIT);
		return slot;
	}

	static bool IsSignaled(Slot slot, uint flags, ulong timeout) {
		if (slot.Fence == null)
			return true;

		if (glClientWaitSync(slot.Fence, flags, timeout) == GL_TIMEOUT_EXPIRED)
			return false;

		glDeleteSync(slot.Fence);
		slot.Fence = null;
		return true;
	}

	public void Advance() {
		Slot slot = Slots[Current];
		if (slot.Fence != null)
			glDeleteSync(slot.Fence);
		slot.Fence = glFenceSync(GL_SYNC_GPU_COMMANDS_COMPLETE, 0);

		int next = (Current + 1) % Slots.Count;
		if (next != Current && IsSignaled(Slots[next], 0, 0)) {
			Current = next;
			return;
		}

		if (Slots.Count < MaxSlots) {
			Slots.Insert(Current + 1, CreateSlot());
			Current++;
			return;
		}

		while (!IsSignaled(Slots[next], GL_SYNC_FLUSH_COMMANDS_BIT, WAIT_TIMEOUT_NS)) ;
		Current = next;
	}

	public void Dispose() {
		foreach (Slot slot in Slots) {
			if (slot.Fence != null)
				glDeleteSync(slot.Fence);
			glUnmapNamedBuffer(slot.Buffer);
			uint buffer = slot.Buffer;
			glDeleteBuffers(1, &buffer);
		}
		Slots.Clear();
		Current = 0;
	}
}
