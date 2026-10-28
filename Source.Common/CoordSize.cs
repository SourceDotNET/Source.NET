using System;
using System.Collections.Generic;
using System.Text;

namespace Source.Common;

public static class CoordSize
{
	// OVERALL Coordinate Size Limits used in COMMON.C MSG_*BitCoord() Routines (and someday the HUD)
	public const uint COORD_INTEGER_BITS = 14;
	public const uint COORD_FRACTIONAL_BITS = 5;
	public const int COORD_DENOMINATOR = 1 << (int)COORD_FRACTIONAL_BITS;
	public const float COORD_RESOLUTION = 1.0f / COORD_DENOMINATOR;

	// Special threshold for networking multiplayer origins
	public const uint COORD_INTEGER_BITS_MP = 11;
	public const uint COORD_FRACTIONAL_BITS_MP_LOWPRECISION = 3;
	public const int COORD_DENOMINATOR_LOWPRECISION = 1 << (int)COORD_FRACTIONAL_BITS_MP_LOWPRECISION;
	public const float COORD_RESOLUTION_LOWPRECISION = 1.0f / COORD_DENOMINATOR_LOWPRECISION;

	public const int NORMAL_FRACTIONAL_BITS = 11;
	public const int NORMAL_DENOMINATOR = (1 << NORMAL_FRACTIONAL_BITS) - 1;
	public const double NORMAL_RESOLUTION = 1.0 / NORMAL_DENOMINATOR;

	/// <summary>
	/// This is limited by the network fractional bits used for coords, because net coords will be only be accurate to 5 bits fractional.
	/// <br/>
	/// Standard collision test epsilon - 1/32nd inch collision epsilon
	/// </summary>
	public const float DIST_EPSILON = 0.03125f;
}
