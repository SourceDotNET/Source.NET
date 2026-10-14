using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;

namespace Game.MenuSystem;

public class MenuSystem : IMenuSystem
{
	public int Init(IServiceProvider services, IGet get, IGarrysMod gmod, GlobalVarsBase vars) {
		throw new NotImplementedException();
	}

	public bool IsServerBlacklisted(ReadOnlySpan<char> address, ReadOnlySpan<char> hostname, ReadOnlySpan<char> description, ReadOnlySpan<char> gamemode, ReadOnlySpan<char> map) {
		throw new NotImplementedException();
	}

	public void OnLuaError(in LuaError err, IAddonSystem.Information? addonInfo) {
		if (addonInfo.HasValue)
			GarrysMod.LuaHelper.CallOnLuaErrorHook(in err, addonInfo.Value.Title ?? "", addonInfo.Value.WorkshopID);
		else
			GarrysMod.LuaHelper.CallOnLuaErrorHook(in err, null, 0);
	}

	public void SendProblemToMenu(ReadOnlySpan<char> id, int severity, ReadOnlySpan<char> parms) {
		// throw new NotImplementedException();
		Msg($"SendProblemToMenu not implemented! ({id.SliceNullTerminatedString()}, {severity}, {parms.SliceNullTerminatedString()})\n");
	}

	public void ServerDetails(ReadOnlySpan<char> unk1, ReadOnlySpan<char> unk2, ReadOnlySpan<char> unk3, int unk4, ReadOnlySpan<char> unk5) {
		throw new NotImplementedException();
	}

	public void SetupNetworkString(INetworkStringTableContainer networkStringTableContainer) {
		throw new NotImplementedException();
	}

	public void Shutdown() {
		throw new NotImplementedException();
	}

	public void StartLua() {
		throw new NotImplementedException();
	}

	public void Think() {
		throw new NotImplementedException();
	}
}
