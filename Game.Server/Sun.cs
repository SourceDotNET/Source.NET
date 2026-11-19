using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

[LinkEntityToClass("env_sun")]
[NetworkName("CSun", NoBase = true)]
public partial class Sun : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Render)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned, Proxy = typeof(Color32Proxy) },
		new(nameof(Overlay)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned, Proxy = typeof(Color32Proxy) },
		new(nameof(Direction)) { Bits = 0, Flags = PropFlags.Normal },
		new(nameof(On)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(Size)) { Bits = 10, Flags = PropFlags.Unsigned },
		new(nameof(OverlaySize)) { Bits = 10, Flags = PropFlags.Unsigned },
		new(nameof(Material)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(OverlayMaterial)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(HDRColorScale)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale, Low = 0.0f, High = 100.0f },
	];

	public static readonly new DataMap DataDesc = new(typeof(Sun), BaseEntity.DataDesc, [
		DEFINE<Sun>.FIELD(nameof(Direction), FieldType.Vector),

		DEFINE<Sun>.KEYFIELD(nameof(UseAngles), FieldType.Integer, "use_angles"),
		DEFINE<Sun>.KEYFIELD(nameof(Pitch), FieldType.Float, "pitch"),
		DEFINE<Sun>.KEYFIELD(nameof(Yaw), FieldType.Float, "angle"),
		DEFINE<Sun>.KEYFIELD(nameof(Size), FieldType.Integer, "size"),
		DEFINE<Sun>.KEYFIELD(nameof(Overlay), FieldType.Color32, "overlaycolor"),
		DEFINE<Sun>.KEYFIELD(nameof(OverlaySize), FieldType.Integer, "overlaysize"),
		DEFINE<Sun>.KEYFIELD(nameof(StrMaterial), FieldType.String, "material"),
		DEFINE<Sun>.KEYFIELD(nameof(StrOverlayMaterial), FieldType.String, "overlaymaterial"),

		DEFINE<Sun>.FIELD(nameof(On), FieldType.Boolean),

		DEFINE<Sun>.INPUTFUNC(FieldType.Void, "TurnOn", nameof(InputTurnOn), (INPUTFUNCPTR)((self, data) => ((Sun)self).InputTurnOn(data))),
		DEFINE<Sun>.INPUTFUNC(FieldType.Void, "TurnOff", nameof(InputTurnOff), (INPUTFUNCPTR)((self, data) => ((Sun)self).InputTurnOff(data))),
		DEFINE<Sun>.INPUTFUNC(FieldType.Color32, "SetColor", nameof(InputSetColor), (INPUTFUNCPTR)((self, data) => ((Sun)self).InputSetColor(data))),

		DEFINE<Sun>.KEYFIELD(nameof(HDRColorScale), FieldType.Float, "HDRColorScale"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public Sun() {
		Direction = new(0, 0, 1);

		UseAngles = 0;
		Pitch = 0;
		Yaw = 0;
		Size = 16;

		On = true;
		AddEFlags(EFL.ForceCheckTransmit);

		StrMaterial = null;
		StrOverlayMaterial = null;
		OverlaySize = -1;
	}

	public override void Activate() {
		base.Activate();

		if (UseAngles != 0) {
			MapUtils.SetupLightNormalFromProps(GetAbsAngles(), Yaw, Pitch, out Vector3 direction);
			Direction = -direction;
		}
		else {
			BaseEntity? ent = gEntList.FindEntityByName(null, Target);
			if (ent != null) {
				Vector3 direction = GetAbsOrigin() - ent.GetAbsOrigin();
				MathLib.VectorNormalize(ref direction);
				Direction = direction;
			}
		}

		if (OverlaySize == -1)
			OverlaySize = Size;

		if (StrMaterial == null)
			StrMaterial = "sprites/light_glow02_add_noz.vmt";
		else if (StrMaterial.AsSpan().GetFileExtension().IsEmpty)
			StrMaterial += ".vmt";

		if (StrOverlayMaterial == null)
			StrOverlayMaterial = "sprites/light_glow02_add_noz.vmt";
		else if (StrOverlayMaterial.AsSpan().GetFileExtension().IsEmpty)
			StrOverlayMaterial += ".vmt";

		Material = PrecacheModel(StrMaterial);
		OverlayMaterial = PrecacheModel(StrOverlayMaterial);
	}

	void InputTurnOn(InputData inputdata) {
		if (!On)
			On = true;
	}

	void InputTurnOff(InputData inputdata) {
		if (On)
			On = false;
	}

	void InputSetColor(InputData inputdata) => Render = inputdata.Value.Color32();

	public override EdictFlags UpdateTransmitState() => SetTransmitState(EdictFlags.Always);

	public string? StrMaterial;
	public string? StrOverlayMaterial;

	public int UseAngles;
	public float Pitch;
	public float Yaw;

	[NetworkVar("m_clrRender")]
	public partial Color Render { get; set; }
	[NetworkVar("m_clrOverlay")]
	public partial Color Overlay { get; set; }
	[NetworkVar("m_vDirection")]
	public partial Vector3 Direction { get; set; }
	[NetworkVar("m_bOn")]
	public partial bool On { get; set; }
	[NetworkVar("m_nSize")]
	public partial int Size { get; set; }
	[NetworkVar("m_nOverlaySize")]
	public partial int OverlaySize { get; set; }
	[NetworkVar("m_nMaterial")]
	public partial int Material { get; set; }
	[NetworkVar("m_nOverlayMaterial")]
	public partial int OverlayMaterial { get; set; }
	[NetworkVar("HDRColorScale")]
	public partial float HDRColorScale { get; set; }
}
