using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod.Lua;
using Source.Common.MaterialSystem;

namespace Game.Client.GarrysMod;

public class MaterialProxy : IMaterialProxy
{
	public static readonly ConVar lua_matproxy = new("lua_matproxy", "1", 0, "Whether to enable Lua material proxies or not.");

	readonly MaterialReference Material = new();
	readonly string Name;
	string UniqueName = "";
	readonly bool HasBind;

	public MaterialProxy(ReadOnlySpan<char> name, bool hasBind) {
		Name = new(name);
		HasBind = hasBind;
	}

	public bool Init(IMaterial material, KeyValues keyValues) {
		UniqueName = $"{Name}{material.GetName()}";
		Material.Init(material);

		if (g_Lua != null && ThreadInMainThread()) {
			LuaTable values = new(null, 0);
			LuaHelper.KeyValuesToTable(keyValues, values, false);
			if (LuaHelper.PushLibraryFunction(g_Lua, "matproxy", "Init")) {
				g_Lua.PushString(Name);
				g_Lua.PushString(UniqueName);
				LuaMaterial.Push(Material.Get());
				values.Push();
				g_Lua.CallInternalNoReturns(4);
			}
			values.UnReference();
		}
		return true;
	}

	public void OnBind(object? o) {
		C_BaseEntity? entity = null;
		if (o is IClientRenderable renderable)
			entity = renderable.GetIClientUnknown().GetBaseEntity();

		CallBind(entity);

		if (ToolsEnabled())
			ToolFramework_RecordMaterialParams(GetMaterial());
	}

	void CallBind(C_BaseEntity? entity) {
		if (!HasBind)
			return;

		if (lua_matproxy.GetInt() != 0 && g_Lua != null && LuaHelper.PushLibraryFunction(g_Lua, "matproxy", "Call")) {
			g_Lua.PushString(UniqueName);
			g_Lua.PushNil();
			LuaEntity.Push_Entity(entity);
			g_Lua.CallInternalNoReturns(3);
		}
	}

	public void Release() => Material.Shutdown();

	public IMaterial GetMaterial() => Material.Get()!;
}

public class MaterialProxyFactory : IMaterialProxyFactory
{
	public static IMaterialProxyFactory? OldFactory;
	public static MaterialProxyFactory? Factory;

	public static void Init() {
		OldFactory = materials.GetMaterialProxyFactory();
		if (OldFactory == null)
			Error("GetMaterialProxyFactory returned NULL!");
		if (Factory != null)
			Error("MaterialProxyFactory isn't NULL!");

		Factory = new();
		materials.SetMaterialProxyFactory(Factory);
	}

	public static void Shutdown() {
		materials.SetMaterialProxyFactory(OldFactory);
		Factory = null;
		OldFactory = null;
	}

	public IMaterialProxy? CreateProxy(ReadOnlySpan<char> proxyName) {
		if (OldFactory == null)
			return null;
		if (!ThreadInMainThread())
			return null;

		if (g_Lua != null && LuaHelper.PushLibraryFunction(g_Lua, "matproxy", "ShouldOverrideProxy")) {
			g_Lua.PushString(proxyName);
			if (g_Lua.CallInternal(1, 2)) {
				ILuaObject shouldOverride = g_Lua.GetReturn(1);
				ILuaObject noBind = g_Lua.GetReturn(0);
				if (shouldOverride.isBool() && shouldOverride.GetBool())
					return new MaterialProxy(proxyName, !noBind.isBool() || !noBind.GetBool());
			}
		}

		return OldFactory.CreateProxy(proxyName);
	}

	public void DeleteProxy(IMaterialProxy proxy) => OldFactory?.DeleteProxy(proxy);
}
