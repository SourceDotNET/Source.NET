#if CLIENT_DLL || GAME_DLL
using Source;
using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Mathematics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Game.Shared;

public partial class EffectData
{
	[NetworkTable("DT_EffectData")]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Encoding = NetEncoding.VectorElements, Bits = (int)CoordSize.COORD_INTEGER_BITS + SUBINCH_PRECISION, Low = WorldSize.MIN_COORD_INTEGER, High = WorldSize.MAX_COORD_INTEGER },
		new(nameof(Start)) { Encoding = NetEncoding.VectorElements, Bits = (int)CoordSize.COORD_INTEGER_BITS + SUBINCH_PRECISION, Low = WorldSize.MIN_COORD_INTEGER, High = WorldSize.MAX_COORD_INTEGER },
		new(nameof(Angles)) { Bits = 7, Flags = PropFlags.RoundDown },
		new(nameof(Normal)) { Bits = 0, Flags = PropFlags.Normal | PropFlags.VarInt },
		new(nameof(Flags)) { Bits = MAX_EFFECT_FLAG_BITS, Flags = PropFlags.Unsigned },
		new(nameof(Magnitude)) { Bits = 12, Flags = PropFlags.RoundDown, Low = 0.0f, High = 1023.0f },
		new(nameof(Scale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(AttachmentIndex)) { Bits = 5, Flags = PropFlags.Unsigned },
		new(nameof(SurfaceProp)) { Encoding = NetEncoding.IntWithMinusOneFlag, Bits = 8, Proxy = typeof(SurfacePropProxy) },
		new(nameof(EffectName)) { Bits = MAX_EFFECT_DISPATCH_STRING_BITS, Flags = PropFlags.Unsigned },
		new(nameof(Material)) { Bits = 13, Flags = PropFlags.Unsigned },
		new(nameof(DamageType)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(HitBox)) { Bits = 12, Flags = PropFlags.Unsigned },
		new(nameof(EntIndex)) { Bits = Constants.MAX_EDICT_BITS, Flags = PropFlags.Unsigned },
		new(nameof(Color)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Radius)) { Bits = 10, Flags = PropFlags.RoundDown, Low = 0.0f, High = 1023.0f },
		nameof(HasCustomColors),
		new(nameof(CustomColors.Color1)) { Bits = 8, Low = 0, High = 1 },
		new(nameof(CustomColors.Color2)) { Bits = 8, Low = 0, High = 1 },
		nameof(HasControlPoint1),
		new(nameof(ControlPoint1.ParticleAttachment)) { Bits = 5, Flags = PropFlags.Unsigned },
		new(nameof(ControlPoint1.Offset)) { Encoding = NetEncoding.VectorElements, Bits = 0, Flags = PropFlags.Coord | PropFlags.NoScale },
		nameof(AllowOverride),
	];

	public static class SurfacePropProxy
	{
#if GAME_DLL
		public static void Send(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) => Game.Server.SendProxy.SendProxy_ShortAddOne(prop, instance, field, ref outData, element, objectID);
#endif
	}

	public const int SUBINCH_PRECISION = 3;
	public const int MAX_EFFECT_FLAG_BITS = 8;
	public const int CUSTOM_COLOR_CP1 = 9;
	public const int CUSTOM_COLOR_CP2 = 10;
	public const int MAX_EFFECT_DISPATCH_STRING_BITS = 10;
	public const int MAX_EFFECT_DISPATCH_STRINGS = 1 << MAX_EFFECT_DISPATCH_STRING_BITS;

	[NetworkVar("m_vOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_vStart")]
	public Vector3 Start;
	[NetworkVar("m_vAngles")]
	public QAngle Angles;
	[NetworkVar("m_vNormal")]
	public Vector3 Normal;
	[NetworkVar("m_fFlags")]
	public int Flags;
	[NetworkVar("m_flMagnitude")]
	public float Magnitude;
	[NetworkVar("m_flScale")]
	public float Scale;
	[NetworkVar("m_nAttachmentIndex")]
	public int AttachmentIndex;
	[NetworkVar("m_nSurfaceProp")]
	public short SurfaceProp;
	[NetworkVar("m_iEffectName")]
	public int EffectName;
	[NetworkVar("m_nMaterial")]
	public int Material;
	[NetworkVar("m_nDamageType")]
	public int DamageType;
	[NetworkVar("m_nHitBox")]
	public int HitBox;
#if CLIENT_DLL
	public BaseHandle Entity = new();
#else
#endif
	[NetworkVar("entindex")]
	public int EntIndex;
	[NetworkVar("m_nColor")]
	public byte Color;

	[NetworkVar("m_flRadius")]
	public float Radius;

	[NetworkVar("m_bCustomColors")]
	public bool HasCustomColors;
	[NetworkVar("m_CustomColors")]
	public ParticleEffectsColors CustomColors;
	[NetworkVar("m_bControlPoint1")]
	public bool HasControlPoint1;
	[NetworkVar("m_ControlPoint1")]
	public ParticleEffectsControlPoint ControlPoint1;
	[NetworkVar("m_bAllowOverride")]
	public bool AllowOverride;

	public EffectData Copy() => (EffectData)MemberwiseClone();
}
#endif
