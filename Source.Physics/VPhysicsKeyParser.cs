using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Formats.Keyvalues;
using Source.Common.Physics;

using System.Globalization;
using System.Numerics;
using System.Text;

namespace Source.Physics;

internal class VPhysicsKeyParser : IVPhysicsKeyParser
{
	const string DummyParserKeyValues = """
		"PhysProps_Fallback"
		{
			"solid"
			{
				"dummy" "1"
			}
			"vehicle"
			{
				"dummy" "1"
			}
			"vehicle_sounds"
			{
				"dummy" "1"
			}
			"vehicle_view"
			{
				"dummy" "1"
			}
			"ragdollconstraint"
			{
				"dummy" "1"
			}
			"collisionrules"
			{
				"dummy" "1"
			}
		}
		""";

	readonly KeyValues KV;
	KeyValues? CurrentBlock;

	public VPhysicsKeyParser(ReadOnlySpan<byte> keyData) {
		int length = keyData.IndexOf((byte)0);
		string text = Encoding.UTF8.GetString(length >= 0 ? keyData[..length] : keyData);

		KeyValues? kv = HeaderlessKVBufferToKeyValues(text, "VPhysicsKeyParse");
		if (kv == null) {
			Warning("CreateVPhysicsKeyParser: Encountered invalid KV data. Falling back to a dummy KV. You may notice a broken prop/vehicle.\n");
			kv = new KeyValues("VPhysicsKeyParse_Fallback");
			kv.LoadFromBuffer("VPhysicsKeyParse_Fallback", DummyParserKeyValues);
		}

		KV = kv;
		CurrentBlock = KV.GetFirstSubKey();
	}

	static KeyValues? HeaderlessKVBufferToKeyValues(string buffer, string setName) {
		KeyValues kv = new(setName);
		if (!kv.LoadFromBuffer(setName, "\"PhysProps\"\r\n{" + buffer + "\r\n}"))
			return null;
		return kv;
	}

	void NextBlock() => CurrentBlock = CurrentBlock?.GetNextKey();

	public ReadOnlySpan<char> GetCurrentBlockName() => CurrentBlock != null ? CurrentBlock.Name : "";

	public bool Finished() => CurrentBlock == null;

	public void SkipBlock() => NextBlock();

	static float ParseFloat(ReadOnlySpan<char> s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0.0f;

	static void ParseFloats(ReadOnlySpan<char> s, Span<float> output) {
		int index = 0;
		foreach (Range range in MemoryExtensions.Split(s, ' ')) {
			ReadOnlySpan<char> part = s[range].Trim();
			if (part.IsEmpty)
				continue;
			if (index >= output.Length)
				break;
			output[index++] = ParseFloat(part);
		}
	}

	static Vector3 ParseVector(ReadOnlySpan<char> s) {
		Span<float> values = stackalloc float[3];
		ParseFloats(s, values);
		return new Vector3(values[0], values[1], values[2]);
	}

	static Vector4 ParseVector4(ReadOnlySpan<char> s) {
		Span<float> values = stackalloc float[4];
		ParseFloats(s, values);
		return new Vector4(values[0], values[1], values[2], values[3]);
	}

	static void CopyString(Span<char> dest, ReadOnlySpan<char> src) {
		int n = Math.Min(src.Length, dest.Length - 1);
		src[..n].CopyTo(dest);
		dest[n] = '\0';
	}

	public void ParseSolid(ref Solid solid, IVPhysicsKeyHandler? unknownKeyHandler) {
		unknownKeyHandler?.SetDefaults(ref solid);

		for (KeyValues? prop = CurrentBlock?.GetFirstSubKey(); prop != null; prop = prop.GetNextKey()) {
			ReadOnlySpan<char> value = prop.GetString();
			switch (prop.Name.ToLowerInvariant()) {
				case "index": solid.Index = prop.GetInt(); break;
				case "name": CopyString(solid.Name, value); break;
				case "parent": CopyString(solid.Parent, value); break;
				case "mass": solid.Params.Mass = prop.GetFloat(); break;
				case "surfaceprop": CopyString(solid.SurfaceProp, value); break;
				case "masscenteroverride": solid.MassCenterOverride = ParseVector(value); break;
				case "damping": solid.Params.Damping = prop.GetFloat(); break;
				case "rotdamping": solid.Params.RotDamping = prop.GetFloat(); break;
				case "drag": solid.Params.DragCoefficient = prop.GetFloat(); break;
				case "inertia": solid.Params.Inertia = prop.GetFloat(); break;
				case "rotinertialimit": solid.Params.RotInertiaLimit = prop.GetFloat(); break;
				case "volume": solid.Params.Volume = prop.GetFloat(); break;
				default: unknownKeyHandler?.ParseKeyValue(ref solid, prop.Name, value); break;
			}
		}

		NextBlock();
	}

	public void ParseFluid(ref Fluid fluid, IVPhysicsKeyHandler? unknownKeyHandler) {
		if (unknownKeyHandler != null)
			unknownKeyHandler.SetDefaults(ref fluid);
		else {
			fluid = default;
			CopyString(fluid.SurfaceProp, "water");
		}

		for (KeyValues? prop = CurrentBlock?.GetFirstSubKey(); prop != null; prop = prop.GetNextKey()) {
			ReadOnlySpan<char> value = prop.GetString();
			switch (prop.Name.ToLowerInvariant()) {
				case "index": fluid.Index = prop.GetInt(); break;
				case "surfaceprop": CopyString(fluid.SurfaceProp, value); break;
				case "damping": fluid.Params.Damping = prop.GetFloat(); break;
				case "surfaceplane": fluid.Params.SurfacePlane = ParseVector4(value); break;
				case "currentvelocity": fluid.Params.CurrentVelocity = ParseVector(value); break;
				case "contents": fluid.Params.Contents = (Contents)prop.GetInt(); break;
				default: unknownKeyHandler?.ParseKeyValue(ref fluid, prop.Name, value); break;
			}
		}

		NextBlock();
	}

	public void ParseRagdollConstraint(ref ConstraintRagdollParams constraint, IVPhysicsKeyHandler? unknownKeyHandler) {
		if (unknownKeyHandler != null)
			unknownKeyHandler.SetDefaults(ref constraint);
		else {
			constraint = default;
			constraint.ChildIndex = -1;
			constraint.ParentIndex = -1;
		}

		constraint.UseClockwiseRotations = true;

		for (KeyValues? prop = CurrentBlock?.GetFirstSubKey(); prop != null; prop = prop.GetNextKey()) {
			ReadOnlySpan<char> value = prop.GetString();
			switch (prop.Name.ToLowerInvariant()) {
				case "parent": constraint.ParentIndex = prop.GetInt(); break;
				case "child": constraint.ChildIndex = prop.GetInt(); break;
				case "xmin": constraint.Axes[0].MinRotation = prop.GetFloat(); break;
				case "ymin": constraint.Axes[1].MinRotation = prop.GetFloat(); break;
				case "zmin": constraint.Axes[2].MinRotation = prop.GetFloat(); break;
				case "xmax": constraint.Axes[0].MaxRotation = prop.GetFloat(); break;
				case "ymax": constraint.Axes[1].MaxRotation = prop.GetFloat(); break;
				case "zmax": constraint.Axes[2].MaxRotation = prop.GetFloat(); break;
				case "xfriction":
					constraint.Axes[0].Torque = prop.GetFloat();
					constraint.Axes[0].AngularVelocity = 0;
					break;
				case "yfriction":
					constraint.Axes[1].Torque = prop.GetFloat();
					constraint.Axes[1].AngularVelocity = 0;
					break;
				case "zfriction":
					constraint.Axes[2].Torque = prop.GetFloat();
					constraint.Axes[2].AngularVelocity = 0;
					break;
				default: unknownKeyHandler?.ParseKeyValue(ref constraint, prop.Name, value); break;
			}
		}

		NextBlock();
	}

	public void ParseSurfaceTable(Span<nint> table, IVPhysicsKeyHandler? unknownKeyHandler) {
		for (KeyValues? prop = CurrentBlock?.GetFirstSubKey(); prop != null; prop = prop.GetNextKey()) {
			nint propIndex = physprops.GetSurfaceIndex(prop.Name);
			int tableIndex = prop.GetInt();

			if (tableIndex >= 0 && tableIndex < 128 && tableIndex < table.Length)
				table[tableIndex] = propIndex;
		}

		NextBlock();
	}

	static void ParseCustomRecursive(KeyValues kv, ref object? custom, IVPhysicsKeyHandler? unknownKeyHandler) {
		for (KeyValues? prop = kv.GetFirstSubKey(); prop != null; prop = prop.GetNextKey()) {
			unknownKeyHandler?.ParseKeyValue(ref custom, prop.Name, prop.GetString());
			ParseCustomRecursive(prop, ref custom, unknownKeyHandler);
		}
	}

	public void ParseCustom(ref object? custom, IVPhysicsKeyHandler? unknownKeyHandler) {
		unknownKeyHandler?.SetDefaults(ref custom);

		if (CurrentBlock != null)
			ParseCustomRecursive(CurrentBlock, ref custom, unknownKeyHandler);

		NextBlock();
	}

	public void ParseVehicle(ref VehicleParams vehicle, IVPhysicsKeyHandler? unknownKeyHandler) => NextBlock();
}
