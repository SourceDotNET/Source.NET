#if CLIENT_DLL || GAME_DLL
using Game.Shared;

using Source;
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaUserCmd
{
	[LuaClass(typeof(AnonymousSafeFieldPointer<UserCmd>), NullError = "Tried to use a NULL CUserCmd!")]
	public static readonly LuaClass LC_UserCmd = new("CUserCmd", LuaType.UserCmd, null, null);

	class UserCmdRef
	{
		public UserCmd Cmd;
		public AnonymousSafeFieldPointer<UserCmd> Ptr => new(this, static o => ref ((UserCmdRef)o).Cmd);
	}

	static readonly UserCmdRef CurrentCommandCopy = new();

	public static void Push_UserCmd(AnonymousSafeFieldPointer<UserCmd> cmd) => LC_UserCmd.Push(cmd);

	public static ref UserCmd Get_UserCmd(int stackPos) {
		if (LC_UserCmd.Get(stackPos) is not AnonymousSafeFieldPointer<UserCmd> cmd || cmd.IsNull) {
			g_Lua!.Error("Tried to use a NULL CUserCmd!");
			return ref UserCmd.NULL;
		}
		return ref cmd.Get();
	}

	public static void Push_CurrentCommand(in UserCmd cmd) {
		CurrentCommandCopy.Cmd = cmd;
		Push_UserCmd(CurrentCommandCopy.Ptr);
	}

	[LuaMethod]
	static int CUserCmd__AddKey(ILuaInterface lua) {
		ref UserCmd cmd = ref Get_UserCmd(1);
		cmd.Buttons |= (InButtons)(int)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CUserCmd__RemoveKey(ILuaInterface lua) {
		ref UserCmd cmd = ref Get_UserCmd(1);
		cmd.Buttons &= ~(InButtons)(int)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CUserCmd__KeyDown(ILuaInterface lua) {
		ref UserCmd cmd = ref Get_UserCmd(1);
		lua.PushBool((cmd.Buttons & (InButtons)(int)lua.CheckNumber(2)) != 0);
		return 1;
	}

	[LuaMethod]
	static int CUserCmd__ClearButtons(ILuaInterface lua) {
		Get_UserCmd(1).Buttons = 0;
		return 0;
	}

	[LuaMethod]
	static int CUserCmd__ClearMovement(ILuaInterface lua) {
		ref UserCmd cmd = ref Get_UserCmd(1);
		cmd.ForwardMove = 0;
		cmd.SideMove = 0;
		cmd.UpMove = 0;
		return 0;
	}

	[LuaMethod]
	static int CUserCmd__CommandNumber(ILuaInterface lua) {
		lua.PushNumber(Get_UserCmd(1).CommandNumber);
		return 1;
	}

	[LuaMethod]
	static int CUserCmd__TickCount(ILuaInterface lua) {
		lua.PushNumber(Get_UserCmd(1).TickCount);
		return 1;
	}

	[LuaMethod]
	static int CUserCmd__GetButtons(ILuaInterface lua) {
		lua.PushNumber((int)Get_UserCmd(1).Buttons);
		return 1;
	}

	[LuaMethod]
	static int CUserCmd__SetButtons(ILuaInterface lua) {
		ref UserCmd cmd = ref Get_UserCmd(1);
		cmd.Buttons = (InButtons)(int)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CUserCmd__GetForwardMove(ILuaInterface lua) {
		lua.PushNumber(Get_UserCmd(1).ForwardMove);
		return 1;
	}

	[LuaMethod]
	static int CUserCmd__SetForwardMove(ILuaInterface lua) {
		ref UserCmd cmd = ref Get_UserCmd(1);
		cmd.ForwardMove = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CUserCmd__GetSideMove(ILuaInterface lua) {
		lua.PushNumber(Get_UserCmd(1).SideMove);
		return 1;
	}

	[LuaMethod]
	static int CUserCmd__SetSideMove(ILuaInterface lua) {
		ref UserCmd cmd = ref Get_UserCmd(1);
		cmd.SideMove = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CUserCmd__GetUpMove(ILuaInterface lua) {
		lua.PushNumber(Get_UserCmd(1).UpMove);
		return 1;
	}

	[LuaMethod]
	static int CUserCmd__SetUpMove(ILuaInterface lua) {
		ref UserCmd cmd = ref Get_UserCmd(1);
		cmd.UpMove = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CUserCmd__GetImpulse(ILuaInterface lua) {
		lua.PushNumber(Get_UserCmd(1).Impulse);
		return 1;
	}

	[LuaMethod]
	static int CUserCmd__SetImpulse(ILuaInterface lua) {
		ref UserCmd cmd = ref Get_UserCmd(1);
		cmd.Impulse = (byte)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CUserCmd__GetMouseWheel(ILuaInterface lua) {
		lua.PushNumber(Get_UserCmd(1).ScrollWheelSpeed);
		return 1;
	}

	[LuaMethod]
	static int CUserCmd__SetMouseWheel(ILuaInterface lua) {
		ref UserCmd cmd = ref Get_UserCmd(1);
		cmd.ScrollWheelSpeed = (sbyte)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CUserCmd__GetMouseX(ILuaInterface lua) {
		lua.PushNumber(Get_UserCmd(1).MouseDeltaX);
		return 1;
	}

	[LuaMethod]
	static int CUserCmd__SetMouseX(ILuaInterface lua) {
		ref UserCmd cmd = ref Get_UserCmd(1);
		cmd.MouseDeltaX = (short)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CUserCmd__GetMouseY(ILuaInterface lua) {
		lua.PushNumber(Get_UserCmd(1).MouseDeltaY);
		return 1;
	}

	[LuaMethod]
	static int CUserCmd__SetMouseY(ILuaInterface lua) {
		ref UserCmd cmd = ref Get_UserCmd(1);
		cmd.MouseDeltaY = (short)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CUserCmd__GetViewAngles(ILuaInterface lua) {
		LuaAngle.Push_Angle(Get_UserCmd(1).ViewAngles);
		return 1;
	}

	[LuaMethod]
	static int CUserCmd__SetViewAngles(ILuaInterface lua) {
		ref UserCmd cmd = ref Get_UserCmd(1);
		cmd.ViewAngles = LuaAngle.Get_Angle(2);
		return 0;
	}

	[LuaMethod]
	static int CUserCmd__IsForced(ILuaInterface lua) {
		lua.PushBool(Get_UserCmd(1).Forced);
		return 1;
	}

	[LuaMethod]
	static int CUserCmd__SelectWeapon(ILuaInterface lua) {
		ref UserCmd cmd = ref Get_UserCmd(1);
		BaseCombatWeapon weapon = (BaseCombatWeapon)LuaEntity.Get_Entity(2, false)!;
		cmd.WeaponSelect = weapon.EntIndex();
		cmd.WeaponSubtype = weapon.GetSubType();
		return 0;
	}

	[LuaMethod]
	static int CUserCmd__IsValid(ILuaInterface lua) {
		lua.PushBool(LC_UserCmd.Get(1) is AnonymousSafeFieldPointer<UserCmd> cmd && !cmd.IsNull);
		return 1;
	}

	[LuaMethod]
	static int CUserCmd____tostring(ILuaInterface lua) {
		if (LC_UserCmd.Get(1) is not AnonymousSafeFieldPointer<UserCmd> cmd || cmd.IsNull) {
			lua.PushString("CUserCmd [NULL]");
			return 1;
		}
		Span<char> str = stackalloc char[256];
		sprintf(str, "CUserCmd [%i]").I(cmd.Get().CommandNumber);
		lua.PushString(str.SliceNullTerminatedString());
		return 1;
	}
}
#endif
