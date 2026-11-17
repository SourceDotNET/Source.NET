namespace Source.MaterialSystem.Surface;

public class TexturePacker
{
	public struct Rect
	{
		public int X;
		public int Y;
		public int Width;
		public int Height;
	}

	public struct TreeEntry
	{
		public Rect Rc;
		public bool InUse;
	}

	struct Node
	{
		public TreeEntry Entry;
		public int FirstChild;
		public int NextSibling;
	}

	const int InvalidIndex = -1;

	readonly List<Node> Tree = [];
	int Root = InvalidIndex;
	readonly int PageWidth;
	readonly int PageHeight;
	readonly int PixelGap;

	public TexturePacker(int texWidth, int texHeight, int pixelGap = 0) {
		PageWidth = texWidth;
		PageHeight = texHeight;
		PixelGap = pixelGap;

		Clear();
	}

	public void Clear() {
		Tree.Clear();

		Node newEntry = default;
		newEntry.Entry.Rc.X = 0;
		newEntry.Entry.Rc.Y = 0;
		newEntry.Entry.Rc.Width = PageWidth;
		newEntry.Entry.Rc.Height = PageHeight;
		newEntry.Entry.InUse = false;
		newEntry.FirstChild = InvalidIndex;
		newEntry.NextSibling = InvalidIndex;

		Root = Tree.Count;
		Tree.Add(newEntry);
	}

	public int MaxElementIndex => Tree.Count;

	public bool IsValidIndex(int nodeIndex) => nodeIndex >= 0 && nodeIndex < Tree.Count;

	public TreeEntry GetEntry(int nodeIndex) => Tree[nodeIndex].Entry;

	bool IsLeaf(int nodeIndex) {
		int leftChildIndex = Tree[nodeIndex].FirstChild;
		if (leftChildIndex == InvalidIndex)
			return true;
		else if (Tree[leftChildIndex].NextSibling == InvalidIndex)
			return true;

		return false;
	}

	int InsertChildAfter(int parent, int after, in Rect rc) {
		Node node = default;
		node.Entry.Rc = rc;
		node.Entry.InUse = false;
		node.FirstChild = InvalidIndex;

		int index = Tree.Count;
		if (after == InvalidIndex) {
			node.NextSibling = Tree[parent].FirstChild;
			Tree.Add(node);
			Node parentNode = Tree[parent];
			parentNode.FirstChild = index;
			Tree[parent] = parentNode;
		}
		else {
			node.NextSibling = Tree[after].NextSibling;
			Tree.Add(node);
			Node afterNode = Tree[after];
			afterNode.NextSibling = index;
			Tree[after] = afterNode;
		}

		return index;
	}

	public int InsertRect(in Rect texRect, int nodeIndex = InvalidIndex) {
		if (nodeIndex == InvalidIndex)
			nodeIndex = Root;

		while (true) {
			// if we are not a leaf
			if (!IsLeaf(nodeIndex)) {
				int leftChildIndex = Tree[nodeIndex].FirstChild;
				// try inserting under left child
				int newIndex = InsertRect(texRect, leftChildIndex);
				if (IsValidIndex(newIndex))
					return newIndex;

				// no room, insert under right child
				nodeIndex = Tree[leftChildIndex].NextSibling;
				continue;
			}

			// we are a leaf
			Node node = Tree[nodeIndex];
			if (node.Entry.InUse)
				return InvalidIndex;

			int availableWidth = node.Entry.Rc.Width;
			int availableHeight = node.Entry.Rc.Height;

			if (texRect.Width > availableWidth || texRect.Height > availableHeight)
				return InvalidIndex;

			if (texRect.Width == availableWidth && texRect.Height == availableHeight) {
				node.Entry.InUse = true;
				Tree[nodeIndex] = node;
				return nodeIndex;
			}

			// otherwise, gotta split this node and create some kids
			// decide which way to split
			int dw = availableWidth - texRect.Width;
			int dh = availableHeight - texRect.Height;

			int leftChild;
			if (dw > dh) { // split vertically
				Rect left = new() { X = node.Entry.Rc.X, Y = node.Entry.Rc.Y, Width = texRect.Width, Height = availableHeight };
				leftChild = InsertChildAfter(nodeIndex, InvalidIndex, in left);

				Rect right = new() { X = node.Entry.Rc.X + texRect.Width + PixelGap, Y = node.Entry.Rc.Y, Width = dw - PixelGap, Height = availableHeight };
				InsertChildAfter(nodeIndex, leftChild, in right);
			}
			else { // split horizontally
				Rect left = new() { X = node.Entry.Rc.X, Y = node.Entry.Rc.Y, Width = availableWidth, Height = texRect.Height };
				leftChild = InsertChildAfter(nodeIndex, InvalidIndex, in left);

				Rect right = new() { X = node.Entry.Rc.X, Y = node.Entry.Rc.Y + texRect.Height + PixelGap, Width = availableWidth, Height = dh - PixelGap };
				InsertChildAfter(nodeIndex, leftChild, in right);
			}

			// insert into first child we created
			nodeIndex = leftChild;
		}
	}
}
