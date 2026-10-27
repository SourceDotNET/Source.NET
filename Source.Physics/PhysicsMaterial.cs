using Microsoft.VisualBasic;

using Source.Common;
using Source.Common.GUI;
using Source.Common.Physics;
using Source.Common.Utilities;

using System;
using System.Collections.Generic;
using System.Text;

namespace Source.Physics;

public class PhysicsSurfaceProps : IPhysicsSurfaceProps
{
	public void GetPhysicsParameters(nint surfaceDataIndex, out SurfacePhysicsParams paramsOut) {
		throw new NotImplementedException();
	}

	public void GetPhysicsProperties(nint materialIndex, out float density, out float thickness, out float friction, out float elasticity) {
		Surface? surface = GetInternalSurface(materialIndex);
		if (surface == null) {
			surface = GetInternalSurface(GetSurfaceIndex("default"));
			Assert(surface != null);
		}

		if (surface != null) {
			friction = (float)surface.Data.Physics.Friction;
			elasticity = (float)surface.Data.Physics.Elasticity;
			density = surface.Data.Physics.Density;
			thickness = surface.Data.Physics.Thickness;
		}
		else {
			friction = default;
			elasticity = default;
			density = default;
			thickness = default;
		}
	}

	public ReadOnlySpan<char> GetPropName(nint surfaceDataIndex) {
		Surface? surface = GetInternalSurface(surfaceDataIndex);
		if (surface != null)
			return GetNameString(surface.Name);
		return null;
	}

	public ReadOnlySpan<char> GetString(UtlSymId_t stringTableIndex) {
		return Strings.String(stringTableIndex);
	}

	public bool IsReservedMaterialIndex(nint materialIndex) {
		return (materialIndex > 127) ? true : false;
	}

	public nint GetReservedFallBack(nint materialIndex) {
		switch (materialIndex) {
			case MATERIAL_INDEX_SHADOW:
				return ShadowFallback;

		}
		return 0;
	}

	public Surface? GetInternalSurface(nint materialIndex) {
		if (IsReservedMaterialIndex(materialIndex))
			materialIndex = GetReservedFallBack(materialIndex);

		if (materialIndex < 0 || materialIndex > Props.Count - 1)
			return null;

		return Props[(int)materialIndex];
	}

	public SurfaceData_ptr? GetSurfaceData(nint materialIndex) {
		Surface? surface = GetInternalSurface(materialIndex);
		surface ??= GetInternalSurface(0); // Zero is always the "default" property

		Assert(surface != null);
		return surface?.Data;
	}

	public const int MATERIAL_INDEX_SHADOW = 0xF000;

	public nint GetReservedSurfaceIndex(ReadOnlySpan<char> propName) {
		if (stricmp(propName, "$MATERIAL_INDEX_SHADOW") == 0)
			return MATERIAL_INDEX_SHADOW;

		return -1;
	}

	public nint GetSurfaceIndex(ReadOnlySpan<char> surfacePropName) {
		if (surfacePropName.IsStringEmpty)
			return -1;

		if (surfacePropName[0] == '$') {
			nint index = GetReservedSurfaceIndex(surfacePropName);
			if (index >= 0)
				return index;
		}

		UtlSymId_t id = Strings.Find(surfacePropName);
		if (id != 0)
			for (int i = 0; i < Props.Count; i++)
				if (Props[i].Name == id)
					return i;

		return -1;
	}

	public ReadOnlySpan<char> GetNameString(UtlSymbol name) => Strings.String(name);
	private bool AddFileToDatabase(ReadOnlySpan<char> filename) {
		UtlSymId_t id = Strings.AddString(filename);

		foreach (var f in FileList)
			if (f == id)
				return false;

		FileList.Add(id);
		return true;
	}

	public const int MAX_KEYVALUE = 1024;

	public nint ParseSurfaceData(ReadOnlySpan<char> fileName, ReadOnlySpan<char> textfile) {
		if (!AddFileToDatabase(fileName)) {
			return 0;
		}

		ReadOnlySpan<char> text = textfile;

		Span<char> key = stackalloc char[MAX_KEYVALUE], value = stackalloc char[MAX_KEYVALUE];
		do {
			text = ParseKeyvalue(text, key, value);
			if (0 == strcmp(value, "{")) {
				Surface prop = new();
				memreset(ref prop.Data.Struct);
				prop.Name = new(Strings.AddString(key));
				nint baseMaterial = GetSurfaceIndex(key);
				if (baseMaterial < 0)
					baseMaterial = GetSurfaceIndex("default");

				CopyPhysicsProperties(prop, baseMaterial);

				do {
					text = ParseKeyvalue(text, key, value);
					if (0 == strcmpi(key, "}")) {
						// already in the database, don't add again, override values instead
						ReadOnlySpan<char> pOverride = Strings.String(prop.Name);
						nint propIndex = GetSurfaceIndex(pOverride);
						if (propIndex >= 0) {
							Surface pSurface = GetInternalSurface(propIndex)!;
							pSurface.Data.Struct = prop.Data.Struct;
							break;
						}

						Props.Add(prop);
						break;
					}
					else if (0 == strcmpi(key, "base")) {
						baseMaterial = GetSurfaceIndex(value);
						CopyPhysicsProperties(prop, baseMaterial);
					}
					else if (0 == strcmpi(key, "thickness"))
						prop.Data.Physics.Thickness = strtof(value, out _);
					else if (0 == strcmpi(key, "density"))
						prop.Data.Physics.Density = strtof(value, out _);
					else if (0 == strcmpi(key, "elasticity"))
						prop.Data.Physics.Elasticity = strtof(value, out _);
					else if (0 == strcmpi(key, "friction"))
						prop.Data.Physics.Friction = strtof(value, out _);
					else if (0 == strcmpi(key, "maxspeedfactor"))
						prop.Data.Game.MaxSpeedFactor = strtof(value, out _);
					else if (0 == strcmpi(key, "jumpfactor"))
						prop.Data.Game.JumpFactor = strtof(value, out _);
					else if (0 == strcmpi(key, "climbable"))
						byte.TryParse(value, out prop.Data.Game.Climbable);
					// audio parameters
					else if (0 == strcmpi(key, "audioReflectivity"))
						prop.Data.Audio.Reflectivity = strtof(value, out _);
					else if (0 == strcmpi(key, "audioHardnessFactor"))
						prop.Data.Audio.HardnessFactor = strtof(value, out _);
					else if (0 == strcmpi(key, "audioHardMinVelocity"))
						prop.Data.Audio.HardVelocityThreshold = strtof(value, out _);
					else if (0 == strcmpi(key, "audioRoughnessFactor"))
						prop.Data.Audio.RoughnessFactor = strtof(value, out _);
					else if (0 == strcmpi(key, "scrapeRoughThreshold"))
						prop.Data.Audio.RoughThreshold = strtof(value, out _);
					else if (0 == strcmpi(key, "impactHardThreshold"))
						prop.Data.Audio.HardThreshold = strtof(value, out _);
					// sound names
					else if (0 == strcmpi(key, "stepleft"))
						prop.Data.Sounds.StepLeft = Strings.AddString(value);
					else if (0 == strcmpi(key, "stepright"))
						prop.Data.Sounds.StepRight = Strings.AddString(value);
					else if (0 == strcmpi(key, "impactsoft"))
						prop.Data.Sounds.ImpactSoft = Strings.AddString(value);
					else if (0 == strcmpi(key, "impacthard"))
						prop.Data.Sounds.ImpactHard = Strings.AddString(value);
					else if (0 == strcmpi(key, "scrapesmooth"))
						prop.Data.Sounds.ScrapeSmooth = Strings.AddString(value);
					else if (0 == strcmpi(key, "scraperough"))
						prop.Data.Sounds.ScrapeRough = Strings.AddString(value);
					else if (0 == strcmpi(key, "bulletimpact"))
						prop.Data.Sounds.BulletImpact = Strings.AddString(value);
					else if (0 == strcmpi(key, "break"))
						prop.Data.Sounds.BreakSound = Strings.AddString(value);
					else if (0 == strcmpi(key, "strain"))
						prop.Data.Sounds.StrainSound = Strings.AddString(value);
					else if (0 == strcmpi(key, "rolling"))
						prop.Data.Sounds.Rolling = Strings.AddString(value);
					else if (0 == strcmpi(key, "gamematerial")) {
						if (strlen(value) == 1 && !char.IsDigit(value[0]))
							prop.Data.Game.Material = char.ToUpper(value[0]);
						else
							if (int.TryParse(value, out var ul))
								prop.Data.Game.Material = (ulong)ul;
					}
					else if (0 == strcmpi(key, "dampening"))
						prop.Data.Physics.Dampening = strtof(value, out _);
					else {
						// force a breakpoint
						AssertMsg(false, $"Bad surfaceprop key {key.SliceNullTerminatedString()} ({value.SliceNullTerminatedString()})\n");
					}
				} while (!text.IsStringEmpty);
			}
		} while (!text.IsStringEmpty);

		if (!Init) {
			Init = true;
			Surface prop = new();

			nint baseMaterial = GetSurfaceIndex("default");
			memreset(ref prop.Data.Struct);
			prop.Name = new(Strings.AddString(GetReservedMaterialName(MATERIAL_INDEX_SHADOW)));
			CopyPhysicsProperties(prop, baseMaterial);
			prop.Data.Physics.Elasticity = 1e-3f;
			prop.Data.Physics.Friction = 0.8f;
			ShadowFallback = Props.Count; Props.Add(prop);
		}
		return Props.Count;
	}
	bool Init;
	public void CopyPhysicsProperties(Surface surfaceOut, nint baseIndex) {
		Surface? surface = GetInternalSurface(baseIndex);
		if (surface != null)
			surfaceOut.Data.Struct = surface.Data.Struct;
	}
	public static ReadOnlySpan<char> GetReservedMaterialName(int materialIndex) {
		switch (materialIndex) {
			case MATERIAL_INDEX_SHADOW: return "$MATERIAL_INDEX_SHADOW";
		}
		return null;
	}
	private ReadOnlySpan<char> ParseKeyvalue(ReadOnlySpan<char> buffer, scoped Span<char> key, scoped Span<char> value) {
		// Make sure value is always null-terminated.
		value[0] = '\0';

		buffer = FilesystemHelpers.ParseFile(buffer, key, out _);

		// no value on a close brace
		if (key[0] == '}' && key[1] == 0) {
			value[0] = '\0';
			return buffer;
		}

		strlower(key);

		buffer = FilesystemHelpers.ParseFile(buffer, value, out _);
		strlower(value);

		return buffer;
	}

	readonly nint[] WorldMaterialMap = new nint[128];

	public void SetWorldMaterialIndexTable(Span<nint> mapArray) {
		int size = Math.Min(mapArray.Length, WorldMaterialMap.Length);
		mapArray[..size].CopyTo(WorldMaterialMap);
	}

	internal nint RemapWorldMaterial(int index) => WorldMaterialMap[index];

	public nint SurfacePropCount() {
		return Props.Count;
	}

	readonly UtlSymbolTableMT Strings = new();
	readonly List<Surface> Props = new();
	readonly List<UtlSymId_t> FileList = new();
	public nint ShadowFallback;
}


public class Surface
{
	public UtlSymbol Name;
	public ushort Pad;
	public readonly SurfaceData_ptr Data = new();
}
