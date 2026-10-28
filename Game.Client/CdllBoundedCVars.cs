global using static Game.Client.CdllBoundedCVars;
using Source.Common.Commands;
using Source.Common.Networking;

namespace Game.Client;

public class BoundedCvar_Predict()
	: ConVar_ServerBounded("cl_predict", "1.0", FCvar.UserInfo, "Perform client side prediction.")
{
	public static bool ForceCLPredictOff = false;
	ConVar? clientPredict;
	public override float GetFloat() {
		if (ForceCLPredictOff)
			return 0;

		clientPredict ??= cvar.FindVar("sv_client_predict");

		if (clientPredict != null && clientPredict.GetInt() != -1 && gpGlobals.MaxClients > 1)
			return clientPredict.GetFloat();
		else
			return GetBaseFloatValue();
	}
}

public class BoundedCvar_InterpRatio()
	: ConVar_ServerBounded("cl_interp_ratio", "2.0", FCvar.UserInfo | FCvar.NotConnected | FCvar.Archive, "Sets the interpolation amount (final amount is cl_interp_ratio / cl_updaterate).")
{
	ConVar? min;
	ConVar? max;
	public override float GetFloat() {
		min ??= cvar.FindVar("sv_client_min_interp_ratio");
		max ??= cvar.FindVar("sv_client_max_interp_ratio");

		if (min != null && max != null && min.GetFloat() != -1) 
			return Math.Clamp(GetBaseFloatValue(), min.GetFloat(), max.GetFloat());
		else 
			return GetBaseFloatValue();
	}
}

public class BoundedCvar_Interp()
	: ConVar_ServerBounded("cl_interp", "0.1", FCvar.UserInfo | FCvar.NotConnected | FCvar.Archive, "Sets the interpolation amount (bounded on low side by server interp ratio settings).", 0, 0.5f)
{
	ConVar? updateRate;
	ConVar? min;
	public override float GetFloat() {
		updateRate ??= cvar.FindVar("cl_updaterate");
		min ??= cvar.FindVar("sv_client_min_interp_ratio");

		if (updateRate != null && min != null && min.GetFloat() != -1) 
			return Math.Max(GetBaseFloatValue(), min.GetFloat() / updateRate.GetFloat());
		else 
			return GetBaseFloatValue();
	}
}


internal static class CdllBoundedCVars
{
	public static readonly BoundedCvar_Predict cl_predict = new();
	public static readonly BoundedCvar_InterpRatio cl_interp_ratio = new();
	public static readonly BoundedCvar_Interp cl_interp = new();

	static ConVarRef cl_updaterate;
	static ConVar_ServerBounded? s_cl_updaterate;

	public static double GetClientInterpAmount() {
		cl_updaterate.Init("cl_updaterate");
		s_cl_updaterate ??= (ConVar_ServerBounded?)cl_updaterate.GetLinkedConVar();

		return Math.Max(cl_interp.GetFloat(), cl_interp_ratio.GetFloat() / s_cl_updaterate!.GetFloat());
	}
}
