using System.Runtime.InteropServices;

namespace Source.MaterialSystem;

public sealed unsafe class MatMemoryStack(int blockSize, int alignment) : IDisposable
{
	struct Block
	{
		public byte* Base;
		public int Size;
		public int Used;
	}

	readonly List<Block> Blocks = [];
	int CurrentBlock = -1;
	int TotalUsed;

	public int GetUsed() => TotalUsed;

	static int AlignValue(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);

	public byte* Alloc(int size) {
		if (size <= 0)
			return null;

		int alignedSize = AlignValue(size, alignment);

		while (CurrentBlock >= 0 && CurrentBlock < Blocks.Count) {
			Block block = Blocks[CurrentBlock];
			if (block.Size - block.Used >= alignedSize) {
				byte* result = block.Base + block.Used;
				block.Used += alignedSize;
				Blocks[CurrentBlock] = block;
				TotalUsed += alignedSize;
				return result;
			}

			if (CurrentBlock + 1 >= Blocks.Count)
				break;
			CurrentBlock++;
		}

		int newSize = Math.Max(blockSize, alignedSize);
		Block newBlock = new() {
			Base = (byte*)NativeMemory.AlignedAlloc((nuint)newSize, (nuint)alignment),
			Size = newSize,
			Used = alignedSize
		};
		Blocks.Add(newBlock);
		CurrentBlock = Blocks.Count - 1;
		TotalUsed += alignedSize;
		return newBlock.Base;
	}

	public void ShrinkLast(byte* ptr, int oldSize, int newSize) {
		if (ptr == null || CurrentBlock < 0)
			return;

		Block block = Blocks[CurrentBlock];
		int oldAligned = AlignValue(oldSize, alignment);
		if (block.Base + block.Used - oldAligned != ptr)
			return;

		int newAligned = AlignValue(newSize, alignment);
		block.Used -= oldAligned - newAligned;
		TotalUsed -= oldAligned - newAligned;
		Blocks[CurrentBlock] = block;
	}

	public void FreeAll() {
		for (int i = 0; i < Blocks.Count; i++) {
			Block block = Blocks[i];
			block.Used = 0;
			Blocks[i] = block;
		}
		CurrentBlock = Blocks.Count > 0 ? 0 : -1;
		TotalUsed = 0;
	}

	public void Term() {
		for (int i = 0; i < Blocks.Count; i++)
			NativeMemory.AlignedFree(Blocks[i].Base);
		Blocks.Clear();
		CurrentBlock = -1;
		TotalUsed = 0;
	}

	public void Dispose() => Term();
}
