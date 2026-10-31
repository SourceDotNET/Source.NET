using Source;

using System;
using System.Collections.Generic;
using System.Text;

#if CLIENT_DLL
using static Game.Client.GarrysMod.GarrysMod;
#elif GAME_DLL
using static Game.Server.GarrysMod.GarrysMod;
#endif

namespace Game.Shared;


public delegate void PrecacheFn(object? userdata);

public class PrecacheRegisterAttribute(string classname) : Attribute{
	public string ClassName => classname;
}
public class PrecacheWeaponRegisterAttribute(string classname) : PrecacheRegisterAttribute(classname);

public class PrecacheRegister
{
	public static PrecacheRegister? PrecacheRegisters;

	static PrecacheRegister(){
		foreach(var type in ReflectionUtils.GetLoadedTypesWithAttribute<PrecacheRegisterAttribute>())
			new PrecacheRegister(PrecacheFn_Other, type.Value.ClassName);
	}

	public PrecacheRegister(PrecacheFn fn, object? userdata) {
		Fn = fn;
		User = userdata;

		Next = PrecacheRegisters;
		PrecacheRegisters = this;
	}

	public PrecacheFn Fn;
	public object? User;
	public PrecacheRegister? Next;

	public static void Precache() {
#if GAME_DLL || CLIENT_DLL
		if (++g_GModPrecaching > 0x20)
			Error("Something seems to have gone wrong");
#endif
		for (PrecacheRegister? cur = PrecacheRegisters; cur != null; cur = cur.Next)
			cur.Fn(cur.User);
#if GAME_DLL || CLIENT_DLL
		g_GModPrecaching--;
#endif
	}

	public static void PrecacheFn_Other(object? userdata) {
#if GAME_DLL || CLIENT_DLL
		Util.PrecacheOther((string?)userdata);
#endif
	}
}
