using Source.Common.GarrysMod.Lua;
using Source.Common.MaterialSystem;

namespace Game.Client.GarrysMod;

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
}
