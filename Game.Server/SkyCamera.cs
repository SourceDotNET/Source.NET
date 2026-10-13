using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Mathematics;

namespace Game.Server;

using DEFINE = Source.DEFINE<SkyCamera>;

[LinkEntityToClass("sky_camera")]
public class SkyCamera : LogicalEntity
{
	public Sky3DParams SkyboxData = new();
	public bool UseAngles;
	public SkyCamera? Next;

	static SkyCamera? ClassList;

	public static SkyCamera? GetCurrentSkyCamera() => ClassList;
	public static SkyCamera? GetSkyCameraList() => ClassList;

	public static readonly new DataMap DataDesc = new(typeof(SkyCamera), BaseEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(UseAngles), FieldType.Boolean, "use_angles"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	static readonly string[] BogusFogMaps = [
		"d1_canals_01",
		"d1_canals_01a",
		"d1_canals_02",
		"d1_canals_03",
		"d1_canals_09",
		"d1_canals_10",
		"d1_canals_11",
		"d1_canals_12",
		"d1_canals_13",
		"d1_eli_01",
		"d1_trainstation_01",
		"d1_trainstation_03",
		"d1_trainstation_04",
		"d1_trainstation_05",
		"d1_trainstation_06",
		"d3_c17_04",
		"d3_c17_11",
		"d3_c17_12",
		"d3_citadel_01",
	];

	public SkyCamera() {
		Next = ClassList;
		ClassList = this;
		SkyboxData.Fog.MaxDensity = 1.0f;
	}

	public override void UpdateOnRemove() {
		if (ClassList == this)
			ClassList = Next;
		else {
			for (SkyCamera? prev = ClassList; prev != null; prev = prev.Next) {
				if (prev.Next == this) {
					prev.Next = Next;
					break;
				}
			}
		}
		Next = null;

		base.UpdateOnRemove();
	}

	public override bool KeyValue(ReadOnlySpan<char> keyName, ReadOnlySpan<char> value) {
		if (FStrEq(keyName, "scale"))
			SkyboxData.Scale = atoi(value);
		else if (FStrEq(keyName, "fogenable"))
			SkyboxData.Fog.Enable = atoi(value) != 0;
		else if (FStrEq(keyName, "fogblend"))
			SkyboxData.Fog.Blend = atoi(value) != 0;
		else if (FStrEq(keyName, "fogdir")) {
			Span<float> dir = stackalloc float[3];
			UTIL_StringToVector(dir, value);
			SkyboxData.Fog.DirPrimary = new(dir[0], dir[1], dir[2]);
		}
		else if (FStrEq(keyName, "fogcolor"))
			Util.StringToColor32(out SkyboxData.Fog.ColorPrimary, value);
		else if (FStrEq(keyName, "fogcolor2"))
			Util.StringToColor32(out SkyboxData.Fog.ColorSecondary, value);
		else if (FStrEq(keyName, "fogstart"))
			SkyboxData.Fog.Start = strtof(value, out _);
		else if (FStrEq(keyName, "fogend"))
			SkyboxData.Fog.End = strtof(value, out _);
		else if (FStrEq(keyName, "fogmaxdensity"))
			SkyboxData.Fog.MaxDensity = strtof(value, out _);
		else
			return base.KeyValue(keyName, value);

		return true;
	}

	public override void Spawn() {
		SkyboxData.Origin = GetLocalOrigin();
		SkyboxData.Area = engine.GetArea(SkyboxData.Origin);

		Precache();
	}

	public override void Activate() {
		base.Activate();

		if (UseAngles) {
			MathLib.AngleVectors(GetAbsAngles(), out SkyboxData.Fog.DirPrimary);
			SkyboxData.Fog.DirPrimary *= -1.0f;
		}

#if HL2_DLL
		if (SkyboxData.Fog.Blend) {
			for (int i = 0; i < BogusFogMaps.Length; ++i) {
				if (string.Equals(BogusFogMaps[i], gpGlobals.MapName, StringComparison.OrdinalIgnoreCase)) {
					SkyboxData.Fog.ColorPrimary.R = (byte)((SkyboxData.Fog.ColorPrimary.R + SkyboxData.Fog.ColorSecondary.R) * 0.5f);
					SkyboxData.Fog.ColorPrimary.G = (byte)((SkyboxData.Fog.ColorPrimary.G + SkyboxData.Fog.ColorSecondary.G) * 0.5f);
					SkyboxData.Fog.ColorPrimary.B = (byte)((SkyboxData.Fog.ColorPrimary.B + SkyboxData.Fog.ColorSecondary.B) * 0.5f);
					SkyboxData.Fog.ColorPrimary.A = (byte)((SkyboxData.Fog.ColorPrimary.A + SkyboxData.Fog.ColorSecondary.A) * 0.5f);
					SkyboxData.Fog.ColorSecondary = SkyboxData.Fog.ColorPrimary;
				}
			}
		}
#endif
	}
}
