#if CLIENT_DLL || MENU_DLL
using Source.Common.Commands;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.MenuSystem.GarrysMod;
#endif

public static partial class LuaVideoWriter
{
	[LuaClass(typeof(IVideoWriter), NullError = "Tried to use a NULL IVideoWriter!")]
	public static readonly LuaClass LC_IVideoWriter = new("IVideoWriter", LuaType.Video, null, null);

	public static float FpsMaxRestore = -1.0f;

	[LuaMethod]
	static int IVideoWriter____gc(ILuaInterface lua) {
		IVideoWriter? writer = (IVideoWriter?)LC_IVideoWriter.Get(1);
		if (writer != null) {
			writer.Finish();
			writer.Delete();
		}
		lua.SetUserType(1, 0);
		return 0;
	}

	[LuaMethod]
	static void IVideoWriter__Finish(IVideoWriter writer) {
		writer.Finish();
		if (FpsMaxRestore > -1.0f) {
			ConVarRef fpsMax = new("fps_max");
			fpsMax.SetValue(FpsMaxRestore);
		}
	}

	[LuaMethod]
	static int IVideoWriter__AddFrame(ILuaInterface lua) {
		IVideoWriter? writer = (IVideoWriter?)LC_IVideoWriter.Get(1);
		if (writer == null) {
			lua.Error("Tried to use a NULL IVideoWriter!");
			return 0;
		}

		float frameTime = (float)lua.CheckNumber(2);
		if (lua.GetBool(3)) {
			garrysmod.StartVideoScale(writer.Width, writer.Height);
			writer.AddFrame(0, 0, frameTime);
			garrysmod.EndVideoScale(writer.Width, writer.Height);
		}
		else
			writer.AddFrame(0, 0, frameTime);
		return 0;
	}

	[LuaMethod]
	static int IVideoWriter__Width(IVideoWriter writer) => writer.Width;

	[LuaMethod]
	static int IVideoWriter__Height(IVideoWriter writer) => writer.Height;

	[LuaMethod]
	static int IVideoWriter__SetRecordSound(ILuaInterface lua) {
		IVideoWriter? writer = (IVideoWriter?)LC_IVideoWriter.Get(1);
		if (writer == null) {
			lua.Error("Tried to use a NULL IVideoWriter!");
			return 0;
		}

		writer.RecordSound = lua.GetBool(2);
		return 1;
	}
}
#endif
