using System.Collections.Specialized;
using System.Numerics;

namespace Source.Common.Physics;

/// <summary>
/// Analog of solid_t
/// </summary>
public struct Solid
{
	public int Index;
	public InlineArray512<char> Name;
	public InlineArray512<char> Parent;
	public InlineArray512<char> SurfaceProp;
	public Vector3 MassCenterOverride;
	public ObjectParams Params;
}

/// <summary>
/// Analog of fluid_t
/// </summary>
public struct Fluid
{
	public int Index;
	public InlineArray512<char> SurfaceProp;
	public FluidParams Params;
}

public interface IVPhysicsKeyHandler
{
	void ParseKeyValue<T>(ref T data, ReadOnlySpan<char> pKey, ReadOnlySpan<char> pValue);
	void SetDefaults<T>(ref T data);
}

public interface IVPhysicsKeyParser
{
	ReadOnlySpan<char> GetCurrentBlockName();
	bool Finished();
	void ParseSolid(ref Solid solid, IVPhysicsKeyHandler? unknownKeyHandler);
	void ParseFluid(ref Fluid fluid, IVPhysicsKeyHandler? unknownKeyHandler);
	void ParseRagdollConstraint(ref ConstraintRagdollParams constraint, IVPhysicsKeyHandler? unknownKeyHandler);
	void ParseSurfaceTable(Span<nint> table, IVPhysicsKeyHandler? unknownKeyHandler);
	void ParseCustom(ref object? custom, IVPhysicsKeyHandler? unknownKeyHandler);
	void ParseVehicle(ref VehicleParams vehicle, IVPhysicsKeyHandler? unknownKeyHandler);
	void SkipBlock();
}
