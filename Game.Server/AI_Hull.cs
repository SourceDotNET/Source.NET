using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Server;

[InlineArray((int)AI_HullType.NumHulls)] public struct InlineArrayNumHulls<T> { public T item; }

// analog of Hull_t
public enum AI_HullType
{
	Human,
	SmallCentered,
	WideHuman,
	Tiny,
	WideShort,
	Medium,
	TinyCentered,
	Large,
	LargeCentered,
	MediumTall,
	NumHulls,
	None
}

// analog of Hull_Bits_t
[Flags]
public enum AI_HullBits
{
	Human = 1 << AI_HullType.Human,
	SmallCentered = 1 << AI_HullType.SmallCentered,
	WideHuman = 1 << AI_HullType.WideHuman,
	Tiny = 1 << AI_HullType.Tiny,
	WideShort = 1 << AI_HullType.WideShort,
	Medium = 1 << AI_HullType.Medium,
	TinyCentered = 1 << AI_HullType.TinyCentered,
	Large = 1 << AI_HullType.Large,
	LargeCentered = 1 << AI_HullType.LargeCentered,
	MediumTall = 1 << AI_HullType.MediumTall,
	HullBitsMask = 0x000002ff,
}

public class AI_Hull(AI_HullBits bit, string name, Vector3 mins, Vector3 maxs, Vector3 smallMins, Vector3 smallMaxs)
{
	public readonly AI_HullBits HullBit = bit;
	public readonly string Name = name;

	public readonly Vector3 Mins = mins;
	public readonly Vector3 Maxs = maxs;

	public readonly Vector3 SmallMins = smallMins;
	public readonly Vector3 SmallMaxs = smallMaxs;
}

public static class NAI_Hull
{
	static readonly AI_Hull Human_Hull = new(AI_HullBits.Human, "HUMAN_HULL", new(-13, -13, 0), new(13, 13, 72), new(-8, -8, 0), new(8, 8, 72));
	static readonly AI_Hull Small_Centered_Hull = new(AI_HullBits.SmallCentered, "SMALL_CENTERED_HULL", new(-20, -20, -20), new(20, 20, 20), new(-12, -12, -12), new(12, 12, 12));
	static readonly AI_Hull Wide_Human_Hull = new(AI_HullBits.WideHuman, "WIDE_HUMAN_HULL", new(-15, -15, 0), new(15, 15, 72), new(-10, -10, 0), new(10, 10, 72));
	static readonly AI_Hull Tiny_Hull = new(AI_HullBits.Tiny, "TINY_HULL", new(-12, -12, 0), new(12, 12, 24), new(-12, -12, 0), new(12, 12, 24));
	static readonly AI_Hull Wide_Short_Hull = new(AI_HullBits.WideShort, "WIDE_SHORT_HULL", new(-35, -35, 0), new(35, 35, 32), new(-20, -20, 0), new(20, 20, 32));
	static readonly AI_Hull Medium_Hull = new(AI_HullBits.Medium, "MEDIUM_HULL", new(-16, -16, 0), new(16, 16, 64), new(-8, -8, 0), new(8, 8, 64));
	static readonly AI_Hull Tiny_Centered_Hull = new(AI_HullBits.TinyCentered, "TINY_CENTERED_HULL", new(-8, -8, -4), new(8, 8, 4), new(-8, -8, -4), new(8, 8, 4));
	static readonly AI_Hull Large_Hull = new(AI_HullBits.Large, "LARGE_HULL", new(-40, -40, 0), new(40, 40, 100), new(-40, -40, 0), new(40, 40, 100));
	static readonly AI_Hull Large_Centered_Hull = new(AI_HullBits.LargeCentered, "LARGE_CENTERED_HULL", new(-38, -38, -38), new(38, 38, 38), new(-30, -30, -30), new(30, 30, 30));
	static readonly AI_Hull Medium_Tall_Hull = new(AI_HullBits.MediumTall, "MEDIUM_TALL_HULL", new(-18, -18, 0), new(18, 18, 100), new(-12, -12, 0), new(12, 12, 100));

	static readonly AI_Hull[] hull = [
		Human_Hull,
		Small_Centered_Hull,
		Wide_Human_Hull,
		Tiny_Hull,
		Wide_Short_Hull,
		Medium_Hull,
		Tiny_Centered_Hull,
		Large_Hull,
		Large_Centered_Hull,
		Medium_Tall_Hull,
	];

	public static int HullToBit(AI_HullType hull) => 1 << (int)hull;

	public static ref readonly Vector3 Mins(AI_HullType id) => ref hull[(int)id].Mins;
	public static ref readonly Vector3 Maxs(AI_HullType id) => ref hull[(int)id].Maxs;

	public static ref readonly Vector3 SmallMins(AI_HullType id) => ref hull[(int)id].SmallMins;
	public static ref readonly Vector3 SmallMaxs(AI_HullType id) => ref hull[(int)id].SmallMaxs;

	public static float Length(AI_HullType id) => hull[(int)id].Maxs.X - hull[(int)id].Mins.X;
	public static float Width(AI_HullType id) => hull[(int)id].Maxs.Y - hull[(int)id].Mins.Y;
	public static float Height(AI_HullType id) => hull[(int)id].Maxs.Z - hull[(int)id].Mins.Z;

	public static AI_HullBits Bits(AI_HullType id) => hull[(int)id].HullBit;

	public static string Name(AI_HullType id) => hull[(int)id].Name;

	public static AI_HullType LookupId(ReadOnlySpan<char> name) {
		int i;
		if (name.IsEmpty)
			return AI_HullType.Human;

		for (i = 0; i < (int)AI_HullType.NumHulls; i++) {
			if (stricmp(name, Name((AI_HullType)i)) == 0)
				return (AI_HullType)i;
		}
		return AI_HullType.Human;
	}
}
