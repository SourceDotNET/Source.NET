using Source.Common.Audio;
using Source.Common.Engine;

namespace Source.Engine;

public enum Res : byte
{
	FatalIfMissing = 1 << 0,
	Preload = 1 << 1
}

public struct PrecacheUserData
{
	public Res Flags;
}

public class PrecacheItem
{
	public const string MODEL_PRECACHE_TABLENAME = "modelprecache";
	public const string GENERIC_PRECACHE_TABLENAME = "genericprecache";
	public const string SOUND_PRECACHE_TABLENAME = "soundprecache";
	public const string DECAL_PRECACHE_TABLENAME = "decalprecache";

	public const int PRECACHE_USER_DATA_NUMBITS = 2;

	public const int MAX_MODEL_INDEX_BITS = 13;
	public const int MAX_MODELS = (1 << MAX_MODEL_INDEX_BITS);

	public const int MAX_GENERIC_INDEX_BITS = 9;
	public const int MAX_GENERIC = (1 << MAX_GENERIC_INDEX_BITS);

	public const int MAX_DECAL_INDEX_BITS = 9;
	public const int MAX_BASE_DECAL = (1 << MAX_DECAL_INDEX_BITS);

	public const int MAX_SOUND_INDEX_BITS = 14;
	public const int MAX_SOUNDS = (1 << MAX_SOUND_INDEX_BITS);

	private enum ItemType
	{
		Unk = 0,
		Model,
		Sound,
		Generic,
		Decal
	}

	private ItemType Type = ItemType.Unk;
	private uint RefCount = 0;

	private object? Item = null;

	public PrecacheItem() {
		ResetStats();
		Type = ItemType.Unk;
		Item = null;
	}

	// Accessors

	public Model? GetModel() => Type == ItemType.Model ? (Model?)Item : null;
	public string? GetGeneric() => Type == ItemType.Generic ? (string?)Item : null;
	public SfxTable? GetSound() => Type == ItemType.Sound ? (SfxTable?)Item : null;
	public string? GetName() => Type == ItemType.Unk ? null : (Type == ItemType.Generic ? (string?)Item : null);
	public string? GetDecal() => Type == ItemType.Decal ? (string?)Item : null;

	// Mutators

	public void SetModel(Model? model) {
		Init(ItemType.Model, model);
	}

	public void SetGeneric(string generic) {
		Init(ItemType.Generic, generic);
	}

	public void SetSound(SfxTable sound) {
		Init(ItemType.Sound, sound);
	}

	public void SetName(string name) {
		Init(ItemType.Generic, name); // Assuming "Name" is stored similarly to generic string
	}

	public void SetDecal(string decal) {
		Init(ItemType.Decal, decal);
	}

	public float GetFirstReference() {
#if DEBUG_PRECACHE
		return _firstReference;
#else
		return 0f;
#endif
	}

	public float GetMostRecentReference() {
#if DEBUG_PRECACHE
		return _mostRecentReference;
#else
		return 0f;
#endif
	}

	public uint GetReferenceCount() => RefCount;

	// Private methods

	private void Init(ItemType type, object? item) {
		Type = type;
		Item = item;
		ResetStats();
	}

	private void ResetStats() {
		RefCount = 0;
#if DEBUG_PRECACHE
		_firstReference = 0f;
		_mostRecentReference = 0f;
#endif
	}

	private void Reference() {
		if (RefCount == 0) {
#if DEBUG_PRECACHE
			_firstReference = GetCurrentTime();
#endif
		}
#if DEBUG_PRECACHE
		_mostRecentReference = GetCurrentTime();
#endif
		RefCount++;
	}

	private float GetCurrentTime() {
		// Implement your own time retrieval, e.g.:
		return (float)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
	}
}