using Source.Common.Commands;

namespace Game.Client;

public static class HLTVCameraGlobals
{
	public static readonly ConVar spec_autodirector = new("spec_autodirector", "1", FCvar.ClientDLL | FCvar.ClientCmdCanExecute, "Auto-director chooses best view modes while spectating");

	// todo: C_HLTVCamera
}
