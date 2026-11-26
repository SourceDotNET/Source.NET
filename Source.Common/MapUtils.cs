using Source.Common.Mathematics;

using System.Numerics;

using static Source.Common.Formats.BSP.BSPFileCommon;
using static Source.Common.Mathematics.MathLib;
using static Source.Common.Mathematics.MathLibConsts;

namespace Source.Common;

public static class MapUtils
{
	public static void SetupLightNormalFromProps(in QAngle angles, float angle, float pitch, out Vector3 output) {
		output = default;
		if (angle == ANGLE_UP) {
			output.X = output.Y = 0;
			output.Z = 1;
		}
		else if (angle == ANGLE_DOWN) {
			output.X = output.Y = 0;
			output.Z = -1;
		}
		else {
			if (angle == 0)
				angle = angles[YAW];

			output.Z = 0;

			SinCos(DEG2RAD(angle), out output.Y, out output.X);
		}

		if (pitch == 0)
			pitch = angles[PITCH];

		SinCos(DEG2RAD(pitch), out float sin, out float cos);

		output.X *= cos;
		output.Y *= cos;
		output.Z = sin;
	}
}
