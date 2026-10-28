using Source.Common;
using Source.Common.Commands;
using Source.Common.Networking;
using Source.Engine.Client;
using Source.Engine.Server;

namespace Source.Engine;

public class CvarUtilities(ICvar cvar, Host Host, Cmd cmd)
{
	public static void ConVarNetworkChangeCallback(IConVar conVar, in ConVarChangeContext ctx) {
		ConVar var = (ConVar)conVar;
		if (strcmp(var.GetString(), ctx.Old) == 0)
			return;

		if (var.IsFlagSet(FCvar.UserInfo)) {
			if (cl.IsConnected()) {
				NET_SetConVar convar = new();
				convar.AddCVar(var.GetName(), var.GetString());
				cl.NetChannel!.SendNetMsg(convar);
			}
		}

		if (var.IsFlagSet(FCvar.Notify)) {
			IGameEvent? ev = g_GameEventManager.CreateEvent("server_cvar");

			if (ev != null) {
				ev.SetString("cvarname", var.GetName());

				if (var.IsFlagSet(FCvar.Protected))
					ev.SetString("cvarvalue", "***PROTECTED***");
				else
					ev.SetString("cvarvalue", var.GetString());

				g_GameEventManager.FireEvent(ev);
			}
		}

		// if (var.IsFlagSet(FCvar.Replicated) && sv.IsActive()) // TODO
		// 	SV_ReplicateConVarChange((ConVar)conVar, var.GetString());
	}

	public unsafe bool IsCommand(in TokenizedCommand args) {
		int c = args.ArgC();
		if (c == 0)
			return false;

		ConVar? v = cvar.FindVar(args[0]);
		if (v == null)
			return false;

		// development only?

		if (c == 1) {
			ConVar.PrintDescription(v);
			return true;
		}

		if (v.IsFlagSet(FCvar.SingleplayerOnly)) {
#if !SWDS
			if (cl.IsConnected()) {
				if (cl.MaxClients > 1) {
					Dbg.ConMsg($"Can't set {v.GetName()} in multiplayer\n");
					return true;
				}
			}
#endif
		}

		if (v.IsFlagSet(FCvar.NotConnected)) {
#if !SWDS
			if (cl.IsConnected()) {
				Dbg.ConMsg($"Can't set {v.GetName()} when connected\n");
				return true;
			}
#endif
		}

		if (v.IsFlagSet(FCvar.Cheat)) {
			if (!Host.IsSinglePlayerGame() && !Host.CanCheat()
#if !SWDS
				&& !cl.IsHLTV
#endif
				) {
				Dbg.ConMsg($"Can't use cheat cvar {v.GetName()} in multiplayer, unless the server has sv_cheats set to 1.\n");
				return true;
			}
		}

		if (v.IsFlagSet(FCvar.Replicated)) {
			if (!sv.IsActive() && !sv.IsLoading() && (cmd.Source == CommandSource.Command) && cl.IsConnected()) {
				Dbg.ConMsg($"Can't change replicated ConVar {v.GetName()} from console of client, only server operator can change its value\n");
				return true;
			}

			Dbg.Assert(cmd.Source != CommandSource.Client);
		}

		const int LEN_REMAINING = 1024;
		char* remaining = stackalloc char[LEN_REMAINING];
		ReadOnlySpan<char> argS = args.ArgS();
		fixed (char* pArgS = argS) {
			nint len = argS.Length;
			bool isQuoted = argS[0] == '\"';

			if (!isQuoted)
				args.ArgS().CopyTo(new(remaining, LEN_REMAINING));
			else {
				--len;
				// wow! this sucks!
				new Span<char>(pArgS + 1, argS.Length - 1).CopyTo(new(remaining, LEN_REMAINING));
			}

			char* p = remaining + len - 1;
			while (p >= remaining) {
				if (*p > ' ')
					break;
				*p-- = '\0';
			}

			if (isQuoted && p >= remaining) {
				if (*p == '\"')
					*p = '\0';
			}

			SetDirect(v, new(remaining, LEN_REMAINING));
			return true;
		}
	}

#if !SWDS
	readonly ILocalize Localize = Singleton<ILocalize>();
#endif

	private void SetDirect(ConVar var, Span<char> value) {
		// RaphaelIT7: Let's remove everything after the first NULL terminator
		//     This is because from the Command buffer we now have a 1024 char array and
		//     we really don't want all that to be stored inside the ConVar
		// March: Do this with ReadOnlySpan's instead
		ReadOnlySpan<char> strValue = value.SliceNullTerminatedString();

		if (var.IsFlagSet(FCvar.UserInfo)) {
			if (sv.IsDedicated()) return;
		}

		if (var.IsFlagSet(FCvar.PrintableOnly)) {
#if !SWDS
			if (!sv.IsDedicated()) {
				ReadOnlySpan<char> localized = Localize.Find(strValue);
				if (!localized.IsEmpty && localized.Length > 0)
					strValue = localized;
			}
#else

#endif
		}

		if (var.IsFlagSet(FCvar.NeverAsString))
			var.SetValue(double.TryParse(strValue, out double n) ? n : 0);
		else
			var.SetValue(strValue);
	}
}
