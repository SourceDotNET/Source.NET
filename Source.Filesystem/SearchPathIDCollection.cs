// TODO: Logging calls when things go wrong, ie. try/catches

namespace Source.FileSystem;

public class SearchPathIDCollection : Dictionary<ulong, SearchPathCollection>
{
	readonly List<ulong> pathOrder = [];
	readonly Dictionary<ulong, string> searchPathNames = [];

	/// <summary>
	/// 
	/// </summary>
	/// <param name="pathID"></param>
	/// <param name="collection"></param>
	/// <returns>True if the collection was created, false if it already existed.</returns>
	public bool OpenOrCreateCollection(in ReadOnlySpan<char> pathID, out SearchPathCollection collection) {
		ulong hashID = pathID.Hash();
		if (TryGetValue(hashID, out var c)) {
			collection = c;
			return false;
		}

		collection = new();
		this[hashID] = collection;
		pathOrder.Add(hashID);
		searchPathNames[hashID] = new(pathID);
		return true;
	}

	public bool Remove(in ReadOnlySpan<char> pathID) => Remove(pathID.Hash());

	public new bool Remove(ulong hashID) {
		base.Remove(hashID);
		searchPathNames.Remove(hashID);
		return pathOrder.Remove(hashID);
	}

	public new void Clear() {
		base.Clear();
		pathOrder.Clear();
	}

	public ReadOnlySpan<char> GetName(ulong key) => searchPathNames.TryGetValue(key, out string? v) ? v : "";

	internal SearchPathCollection? At(int idx) {
		if (idx >= pathOrder.Count)
			return null;
		return this[pathOrder[idx]];
	}
}
