global using static Game.Client.CDLL_Util;
global using static Game.Util_Globals;

using CommunityToolkit.HighPerformance;

using Game.Client;

using Source;
using Source.Common;

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

		public static int EntitiesInSphere(Span<C_BaseEntity?> list, in Vector3 center, float radius, EntityFlags flagMask, PartitionListMask partitionMask) {
			FlaggedEntitiesEnum sphereEnum = new(list, flagMask);
			partition.EnumerateElementsInSphere((SpatialPartitionListMask_t)partitionMask, center, radius, false, ref sphereEnum);

			return sphereEnum.GetCount();
		}

		public static int EntitiesInBox(Span<C_BaseEntity?> list, in Vector3 mins, in Vector3 maxs, EntityFlags flagMask, PartitionListMask partitionMask) {
			FlaggedEntitiesEnum boxEnum = new(list, flagMask);
			partition.EnumerateElementsInBox((SpatialPartitionListMask_t)partitionMask, mins, maxs, false, ref boxEnum);

			return boxEnum.GetCount();
		}

	}
	public static partial class Util_Globals
	{
		public static bool FStrEq(ReadOnlySpan<char> sz1, ReadOnlySpan<char> sz2)
			=> Unsafe.AreSame(in sz1.DangerousGetReference(), in sz2.DangerousGetReference()) || stricmp(sz1, sz2) == 0;
	}

	public ref struct FlaggedEntitiesEnum : IPartitionEnumerator
	{
		public FlaggedEntitiesEnum(Span<C_BaseEntity?> list, EntityFlags flagMask) {
			List = list;
			FlagMask = flagMask;
			Count = 0;
		}

		public int GetCount() => Count;

		public bool AddToList(C_BaseEntity entity) {
			if (Count >= List.Length)
				return false;
			List[Count] = entity;
			Count++;
			return true;
		}

		public IterationRetval EnumElement(IHandleEntity? handleEntity) {
			IClientEntity? clientEntity = cl_entitylist.GetClientEntityFromHandle(handleEntity!.GetRefEHandle());
			C_BaseEntity? entity = clientEntity?.GetBaseEntity();
			if (entity != null) {
				if (FlagMask != 0 && (entity.GetFlags() & FlagMask) == 0)
					return IterationRetval.Continue;

				if (!AddToList(entity))
					return IterationRetval.Stop;
			}

			return IterationRetval.Continue;
		}

		Span<C_BaseEntity?> List;
		EntityFlags FlagMask;
		int Count;
	}

	public struct EntitySphereQuery
	{
		public const int MAX_SPHERE_QUERY = 256;

		public EntitySphereQuery(in Vector3 center, float radius, EntityFlags flagMask = 0, PartitionListMask partitionMask = PartitionListMask.ClientNonStaticEdicts) {
			ListIndex = 0;
			ListCount = Util.EntitiesInSphere(List, center, radius, flagMask, partitionMask);
		}

		public C_BaseEntity? GetCurrentEntity() {
			if (ListIndex < ListCount)
				return List[ListIndex];
			return null;
		}

		public void NextEntity() => ListIndex++;

		[InlineArray(MAX_SPHERE_QUERY)] struct InlineArrayMaxSphereQuery<T> { public T first; }
		int ListIndex;
		int ListCount;
		InlineArrayMaxSphereQuery<C_BaseEntity?> List;
	}
}
