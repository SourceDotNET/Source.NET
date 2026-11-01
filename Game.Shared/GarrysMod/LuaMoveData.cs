#if CLIENT_DLL || GAME_DLL
using Game.Shared;

using Source;
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaMoveData
{
	[LuaClass(typeof(MoveData), NullError = "Tried to use a NULL CMoveData!")]
	public static readonly LuaClass LC_MoveData = new("CMoveData", LuaType.MoveData, null, null);

	public static void Push_MoveData(MoveData? data) => LC_MoveData.Push(data);

	public static MoveData Get_MoveData(int stackPos) {
		MoveData? data = LC_MoveData.Get(stackPos) as MoveData;
		if (data == null)
			g_Lua!.Error("Tried to use a NULL CMoveData!");
		return data!;
	}

	[LuaMethod]
	static int CMoveData____tostring(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		Span<char> str = stackalloc char[128];
		sprintf(str, "CMoveData [%i]").I(data.PlayerHandle.GetEntryIndex());
		lua.PushString(((ReadOnlySpan<char>)str).SliceNullTerminatedString());
		return 1;
	}

	[LuaMethod]
	static int CMoveData__IsValid(ILuaInterface lua) {
		lua.PushBool(LC_MoveData.Get(1) is MoveData);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__GetImpulseCommand(ILuaInterface lua) {
		lua.PushNumber(Get_MoveData(1).ImpulseCommand);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetImpulseCommand(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.ImpulseCommand = (int)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetMoveAngles(ILuaInterface lua) {
		LuaAngle.Push_Angle(Get_MoveData(1).ViewAngles);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetMoveAngles(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.ViewAngles = LuaAngle.Get_Angle(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetAbsMoveAngles(ILuaInterface lua) {
		LuaAngle.Push_Angle(Get_MoveData(1).AbsViewAngles);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetAbsMoveAngles(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.AbsViewAngles = LuaAngle.Get_Angle(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetButtons(ILuaInterface lua) {
		lua.PushNumber((int)Get_MoveData(1).Buttons);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetButtons(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.Buttons = (InButtons)(int)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__AddKey(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.Buttons |= (InButtons)(int)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__RemoveKey(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.Buttons &= ~(InButtons)(int)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__KeyDown(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		int key = (int)lua.CheckNumber(2);
		lua.PushBool(((int)data.Buttons & key) != 0);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__KeyWasDown(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		int key = (int)lua.CheckNumber(2);
		lua.PushBool(((int)data.OldButtons & key) != 0);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__KeyPressed(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		int key = (int)lua.CheckNumber(2);
		lua.PushBool(((int)data.Buttons & key) != 0 && ((int)data.OldButtons & key) == 0);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__KeyReleased(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		int key = (int)lua.CheckNumber(2);
		lua.PushBool(((int)data.Buttons & key) == 0 && ((int)data.OldButtons & key) != 0);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__GetOldButtons(ILuaInterface lua) {
		lua.PushNumber((int)Get_MoveData(1).OldButtons);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetOldButtons(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.OldButtons = (InButtons)(int)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetForwardSpeed(ILuaInterface lua) {
		lua.PushNumber(Get_MoveData(1).ForwardMove);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetForwardSpeed(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.ForwardMove = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetSideSpeed(ILuaInterface lua) {
		lua.PushNumber(Get_MoveData(1).SideMove);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetSideSpeed(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.SideMove = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetUpSpeed(ILuaInterface lua) {
		lua.PushNumber(Get_MoveData(1).UpMove);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetUpSpeed(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.UpMove = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetMaxSpeed(ILuaInterface lua) {
		lua.PushNumber(Get_MoveData(1).MaxSpeed);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetMaxSpeed(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.MaxSpeed = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetMaxClientSpeed(ILuaInterface lua) {
		lua.PushNumber(Get_MoveData(1).ClientMaxSpeed);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetMaxClientSpeed(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.ClientMaxSpeed = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetVelocity(ILuaInterface lua) {
		LuaVector.Push_Vector(Get_MoveData(1).Velocity);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetVelocity(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.Velocity = LuaVector.Get_Vector(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetAngles(ILuaInterface lua) {
		LuaAngle.Push_Angle(Get_MoveData(1).Angles);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetAngles(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.Angles = LuaAngle.Get_Angle(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetOldAngles(ILuaInterface lua) {
		LuaAngle.Push_Angle(Get_MoveData(1).OldAngles);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetOldAngles(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.OldAngles = LuaAngle.Get_Angle(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetFinalStepHeight(ILuaInterface lua) {
		lua.PushNumber(Get_MoveData(1).StepHeight);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetFinalStepHeight(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.StepHeight = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetFinalIdealVelocity(ILuaInterface lua) {
		LuaVector.Push_Vector(Get_MoveData(1).WishVel);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetFinalIdealVelocity(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.WishVel = LuaVector.Get_Vector(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetFinalJumpVelocity(ILuaInterface lua) {
		LuaVector.Push_Vector(Get_MoveData(1).JumpVel);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetFinalJumpVelocity(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.JumpVel = LuaVector.Get_Vector(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetConstraintCenter(ILuaInterface lua) {
		LuaVector.Push_Vector(Get_MoveData(1).ConstraintCenter);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetConstraintCenter(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.ConstraintCenter = LuaVector.Get_Vector(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetConstraintRadius(ILuaInterface lua) {
		lua.PushNumber(Get_MoveData(1).ConstraintRadius);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetConstraintRadius(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.ConstraintRadius = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetConstraintWidth(ILuaInterface lua) {
		lua.PushNumber(Get_MoveData(1).ConstraintWidth);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetConstraintWidth(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.ConstraintWidth = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetConstraintSpeedScale(ILuaInterface lua) {
		lua.PushNumber(Get_MoveData(1).ConstraintSpeedFactor);
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetConstraintSpeedScale(ILuaInterface lua) {
		MoveData data = Get_MoveData(1);
		data.ConstraintSpeedFactor = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CMoveData__GetOrigin(ILuaInterface lua) {
		LuaVector.Push_Vector(Get_MoveData(1).GetAbsOrigin());
		return 1;
	}

	[LuaMethod]
	static int CMoveData__SetOrigin(ILuaInterface lua) {
		Get_MoveData(1).SetAbsOrigin(LuaVector.Get_Vector(2));
		return 0;
	}
}
#endif
