#if MENU_DLL
using Game.MenuSystem.GarrysMod;

using Source.Common.GarrysMod;

using System.Globalization;

namespace Game.MenuSystem;

public class AddonDownloadNotifications : IAddonDownloadNotification
{
	static bool PushHookCall() {
		LuaObject hook = new();
		g_Lua!.Global().GetMember("hook", hook);
		if (!hook.isTable()) {
			hook.UnReference();
			return false;
		}

		LuaObject call = new();
		hook.GetMember("Call", call);
		if (!call.isFunction()) {
			call.UnReference();
			hook.UnReference();
			return false;
		}

		call.Push();
		call.UnReference();
		hook.UnReference();
		return true;
	}

	static bool Begin(ReadOnlySpan<char> name) {
		if (g_Lua == null || g_Lua.Global() == null || !PushHookCall())
			return false;
		g_Lua.PushString(name);
		return true;
	}

	static void PushID(ulong id) => g_Lua!.PushString(id.ToString(CultureInfo.InvariantCulture));

	public void Start() {
		if (!Begin("WorkshopStart"))
			return;
		g_Lua!.CallInternalNoReturns(1);
	}

	public void StartDownload(ulong wsid, ulong imgid, ReadOnlySpan<char> title, ulong size) {
		if (!Begin("WorkshopDownloadFile"))
			return;
		g_Lua!.PushNil();
		PushID(wsid);
		PushID(imgid);
		g_Lua.PushString(title);
		g_Lua.PushNumber((long)size);
		g_Lua.CallInternalNoReturns(6);
	}

	public void FinishDownload(ulong wsid) {
		if (!Begin("WorkshopDownloadedFile"))
			return;
		g_Lua!.PushNil();
		PushID(wsid);
		g_Lua.CallInternalNoReturns(3);
	}

	public void Finish() {
		if (!Begin("WorkshopEnd"))
			return;
		g_Lua!.CallInternalNoReturns(1);
	}

	public void DownloadProgress(ulong wsid, ulong imgid, ReadOnlySpan<char> title, uint unk1, uint unk2) {
		if (!Begin("WorkshopDownloadProgress"))
			return;
		g_Lua!.PushNil();
		PushID(wsid);
		PushID(imgid);
		g_Lua.PushString(title);
		g_Lua.PushNumber(unk1);
		g_Lua.PushNumber(unk2);
		g_Lua.CallInternalNoReturns(7);
	}

	public void ExtractProgress(ulong wsid, ulong imgid, ReadOnlySpan<char> title, uint percent) {
		if (!Begin("WorkshopExtractProgress"))
			return;
		g_Lua!.PushNil();
		PushID(wsid);
		PushID(imgid);
		g_Lua.PushString(title);
		g_Lua.PushNumber(percent);
		g_Lua.CallInternalNoReturns(6);
	}

	public void DownloadTotals(int num, int max) {
		if (!Begin("WorkshopDownloadTotals"))
			return;
		g_Lua!.PushNil();
		g_Lua.PushNumber(num);
		g_Lua.PushNumber(max);
		g_Lua.CallInternalNoReturns(4);
	}

	public void SubscriptionsProgress(int num, int max) {
		if (!Begin("WorkshopSubscriptionsProgress"))
			return;
		g_Lua!.PushNil();
		g_Lua.PushNumber(num);
		g_Lua.PushNumber(max);
		g_Lua.CallInternalNoReturns(4);
	}

	public void SendMessage(ReadOnlySpan<char> message) {
		if (!Begin("WorkshopSubscriptionsMessage"))
			return;
		g_Lua!.PushNil();
		g_Lua.PushString(message);
		g_Lua.CallInternalNoReturns(3);
	}

	public void NotifySubscriptionChanges() {
		if (!Begin("WorkshopSubscriptionsChanged"))
			return;
		g_Lua!.PushNil();
		g_Lua.CallInternalNoReturns(2);
	}

	public void NotifyAddonConflict(ulong wsid1, ulong wsid2, ReadOnlySpan<char> filename) {
		if (!Begin("OnNotifyAddonConflict"))
			return;
		g_Lua!.PushNil();
		PushID(wsid1);
		PushID(wsid2);
		g_Lua.PushString(filename);
		g_Lua.CallInternalNoReturns(5);
	}
}
#endif
