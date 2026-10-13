using Source.Common.MaterialSystem;

namespace Source.StdShader.Gl46;

public class Water_DX9_HDR : Water_DX90
{
	public static new string HelpString = "Help for Water_DX9_HDR";

	public override string? GetFallbackShader(IMaterialVar[] vars) {
		if (HardwareConfig.GetHDRType() == HDRType.None)
			return "WATER_DX90";
		return null;
	}
}
