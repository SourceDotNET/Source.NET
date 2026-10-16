#if CLIENT_DLL || GAME_DLL || MENU_DLL
using Source;
using Source.Common.GarrysMod.Lua;
using Source.Common.MaterialSystem;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaTexture
{
	[LuaClass(typeof(ITexture), NullError = "Tried to use a NULL ITexture!")]
	public static readonly LuaClass LC_ITexture = new("ITexture", LuaType.Texture, null, null);

	public static void Push(ITexture? texture) {
		texture?.IncrementReferenceCount();
		LC_ITexture.Push(texture);
	}

	[LuaMethod]
	static int ITexture__Width(ITexture texture) => texture.GetActualWidth();

	[LuaMethod]
	static int ITexture__Height(ITexture texture) => texture.GetActualHeight();

	[LuaMethod]
	static string ITexture__GetName(ITexture texture) => new(texture.GetName());

	[LuaMethod]
	static int ITexture____gc(ILuaInterface lua) {
		ITexture? texture = (ITexture?)LC_ITexture.Get(1);
		if (texture == null)
			return 0;
		texture.DecrementReferenceCount();
		lua.SetUserType(1, 0);
		return 0;
	}

	[LuaMethod]
	static int ITexture____tostring(ILuaInterface lua) {
		ITexture? texture = (ITexture?)LC_ITexture.Get(1);
		if (texture == null)
			lua.PushString("Texture [NULL]");
		else
			lua.PushString($"Texture [{texture.GetName()}]");
		return 1;
	}

	[LuaMethod]
	static int ITexture__GetMappingWidth(ITexture texture) => texture.GetMappingWidth();

	[LuaMethod]
	static int ITexture__GetMappingHeight(ITexture texture) => texture.GetMappingHeight();

	[LuaMethod]
	static bool ITexture__IsError(ITexture texture) => texture.IsError();

	[LuaMethod]
	static int ITexture__GetNumAnimationFrames(ITexture texture) => texture.GetNumAnimationFrames();

	[LuaMethod]
	static void ITexture__Download(ITexture texture) => texture.Download();

	[LuaMethod]
	static bool ITexture__IsErrorTexture(ITexture texture) {
		TextureReference error = new();
		error.Init("error", "Other textures", true);
		bool isError = error.Get() == texture;
		error.Shutdown();
		return isError;
	}

	[LuaMethod]
	static int ITexture__GetColor(ILuaInterface lua) {
		ITexture? texture = (ITexture?)LC_ITexture.Get(1);
		if (texture == null) {
			lua.Error("Tried to use a NULL ITexture!");
			return 0;
		}

		Color color = default;
		if (get.Resources() != null)
			color = get.Resources()!.GetTextureColour(texture, (int)lua.CheckNumber(2), (int)lua.CheckNumber(3));
		lua.PushColor(color);
		return 1;
	}
}
#endif
