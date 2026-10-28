using Source.Common.Formats.Keyvalues;
using Source.Common.Utilities;

using System.Diagnostics.CodeAnalysis;

namespace Source.Common.MaterialSystem;

[EngineComponent]
public static class MaterialReferenceGlobals {
	public static IMaterialSystem? _materials;
	 public static IMaterialSystem materials => _materials ??= Singleton<IMaterialSystem>();
}

public class MaterialReference : Reference<IMaterial>
{
	public void Init(ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroupName, bool complain = true) {
		IMaterial? material = MaterialReferenceGlobals.materials.FindMaterial(materialName, textureGroupName, complain);
		Init(material);
	}
	public void Init(ReadOnlySpan<char> materialName, KeyValues keyValues) => throw new NotImplementedException();
	public void Init(MaterialReference reference) => this.reference = reference.reference;
	public void Init(ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroupName, KeyValues keyValues) {
		IMaterial? mat = MaterialReferenceGlobals.materials.FindProceduralMaterial(materialName, textureGroupName, keyValues);
		Assert(mat != null);
		Init(mat);
	}

	public void Init(IMaterial material) {
		if (reference != material) {
			Shutdown();
			reference = material;
		}
	}

	public void Shutdown(bool deleteIfUnreferenced = false) {
		if (reference != null) 
			reference = null;
	}
}
