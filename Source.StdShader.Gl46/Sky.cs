using Source.Common.MaterialSystem;

namespace Source.StdShader.Gl46;

public class Sky : BaseVSShader
{
	public static string HelpString = "Help for Sky";
	public static int Flags = 0;

	public override string? GetFallbackShader(IMaterialVar[] vars) => "Sky_HDR_DX9";
}
