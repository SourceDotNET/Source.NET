global using static Game.Client.CDLL_Util;
global using static Game.Util_Globals;

using CommunityToolkit.HighPerformance;

using Game.Client;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Client
{
	public static class CDLL_Util
	{

		public static float XRES(float x) => ((x) * ((float)ScreenWidth() / 640.0f));
		public static float YRES(float y) => ((y) * ((float)ScreenHeight() / 480.0f));

		public static int ScreenWidth() {
			GetHudSize(out int w, out _);
			return w;
		}

		public static int ScreenHeight() {
			GetHudSize(out _, out int h);
			return h;
		}
	}

	public enum LightIndex
	{
		TEDynamic = 0x10000000,
		PlayerBright = 0x20000000,
		MuzzleFlash = 0x30000000
	}
}

namespace Game
{
	public static partial class Util
	{
		public static void PrecacheOther(ReadOnlySpan<char> classname) {
			C_BaseEntity? entity = C_BaseEntity.CreateEntityByName(classname);
			if (entity == null) {
				Warning("NULL Ent in UTIL_PrecacheOther\n");
				return;
			}

			entity.Precache();
			// Bye bye
			entity.Release();
		}

		public static void Remove(C_BaseEntity? entity) {
			if (entity != null && !entity.IsMarkedForDeletion())
				ClientThinkList().AddToDeleteList(entity.GetClientHandle());
		}

	}
	public static partial class Util_Globals
	{
		public static bool FStrEq(ReadOnlySpan<char> sz1, ReadOnlySpan<char> sz2)
			=> Unsafe.AreSame(in sz1.DangerousGetReference(), in sz2.DangerousGetReference()) || stricmp(sz1, sz2) == 0;
	}
}
