using Source.Common.MaterialSystem;

namespace Source.StdShader.Gl46;

public class Water : BaseVSShader
{
	public static string HelpString = "Help for Water";
	public static int Flags = 0;

	public override string? GetFallbackShader(IMaterialVar[] vars) => "Water_DX9_HDR";
}
