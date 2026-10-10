using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.MaterialSystem;

namespace Source.MaterialSystem;

public class ReplacementProxy(IMaterialSystem materials) : IMaterialProxy
{
	public const string REPLACEMENT_NAME = "_replacement";

	IMaterial? ReplaceMaterial;

	public bool Init(IMaterial material, KeyValues keyValues) {
		ReadOnlySpan<char> fileName = material.GetName();
		string newName = $"{fileName}{REPLACEMENT_NAME}";

		ReplaceMaterial = materials.CreateMaterial(newName, keyValues);
		ReplaceMaterial.IncrementReferenceCount();

		return true;
	}

	public void OnBind(object? o) { }

	public void Release() => ReplaceMaterial!.DecrementReferenceCount();

	static readonly ConVarRef localplayer_visionflags = new("localplayer_visionflags");

	public IMaterial GetMaterial() {
		bool visionOverride = localplayer_visionflags.IsValid() && (localplayer_visionflags.GetInt() & 0x01) != 0;

		if (visionOverride)
			return ReplaceMaterial!;

		return null!;
	}
}
