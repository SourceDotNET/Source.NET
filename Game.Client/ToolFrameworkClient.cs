global using static Game.Client.ToolFrameworkClient;

using Source.Common.MaterialSystem;

namespace Game.Client;

public class ToolFrameworkClient
{
	public static bool ToolsEnabled() => false;

	public static bool ToolFramework_IsThirdPersonCamera() => false;

	public static void ToolFramework_RecordMaterialParams(IMaterial material) {
		// todo
	}
}
