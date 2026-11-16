using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.MaterialSystem;

using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Client;

public static class DebugViewRender
{
	public static readonly ConVar cl_drawshadowtexture = new("cl_drawshadowtexture", "0", FCvar.Cheat);
	public static readonly ConVar cl_shadowtextureoverlaysize = new("cl_shadowtextureoverlaysize", "256", FCvar.Cheat);

	[ConCommand(helpText: "Draw specified material as an overlay", flags: FCvar.Cheat | FCvar.ServerCanExecute)]
	static void r_screenoverlay(in TokenizedCommand args) {
		if (args.ArgC() == 2) {
			if (stricmp("off", args[1]) == 0 || stricmp("0", args[1]) == 0)
				view.SetScreenOverlayMaterial(null);
			else {
				IMaterial? material = materials.FindMaterial(args[1], MaterialDefines.TEXTURE_GROUP_OTHER, false);
				if (!material.IsErrorMaterial())
					view.SetScreenOverlayMaterial(material);
				else
					view.SetScreenOverlayMaterial(null);
			}
		}
		else {
			IMaterial? material = view.GetScreenOverlayMaterial();
			Warning("r_screenoverlay: %s\n", material != null ? material.GetName().ToString() : "off");
		}
	}

	public static void Draw3DDebuggingInfo(in ViewSetup view) {
		render.Draw3DDebugOverlays();
	}
	public static void Draw2DDebuggingInfo(in ViewSetup view) {

	}
	public static void GenerateOverdrawForTesting() {

	}
}
