using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Client;


[NetworkName("CDynamicLight")]
public partial class C_DynamicLight : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Flags)) { Bits = 4, Flags = PropFlags.Unsigned },
		new(nameof(LightStyle)) { Bits = 4, Flags = PropFlags.Unsigned },
		new(nameof(Radius)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Exponent)) { Bits = 8 },
		new(nameof(InnerAngle)) { Bits = 8, Low = 0.0f, High = 360.0f },
		new(nameof(OuterAngle)) { Bits = 8, Low = 0.0f, High = 360.0f },
		new(nameof(SpotRadius)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_Flags")]
	public int Flags;
	[NetworkVar("m_LightStyle")]
	public int LightStyle;
	[NetworkVar("m_Radius")]
	public float Radius;
	[NetworkVar("m_Exponent")]
	public int Exponent;
	[NetworkVar("m_InnerAngle")]
	public float InnerAngle;
	[NetworkVar("m_OuterAngle")]
	public float OuterAngle;
	[NetworkVar("m_SpotRadius")]
	public float SpotRadius;

	DLight? DynamicLight;
	DLight? SpotlightEnd;

	bool ShouldBeElight() => (Flags & (int)DLightFlags.NoWorldIllumination) != 0;

	public override void OnDataChanged(DataUpdateType updateType) {
		if (updateType == DataUpdateType.Created)
			SetNextClientThink(gpGlobals.CurTime + 0.05f);

		base.OnDataChanged(updateType);
	}

	public override bool ShouldDraw() => false;

	public override void Release() {
		DynamicLight?.Die = (float)gpGlobals.CurTime;
		DynamicLight = null;

		SpotlightEnd?.Die = (float)gpGlobals.CurTime;
		SpotlightEnd = null;

		base.Release();
	}

	public override void ClientThink() {
		MathLib.AngleVectors(GetAbsAngles(), out Vector3 forward);

		if ((Flags & (int)DLightFlags.NoModelIllumination) == 0) {
			if (DynamicLight == null || DynamicLight.Key != EntIndex()) {
				DynamicLight = effects.AllocDlight(EntIndex());
				Assert(DynamicLight != null);
				DynamicLight.MinLight = 0;
			}

			DynamicLight.Style = LightStyle;
			DynamicLight.Radius = Radius;
			DynamicLight.Flags = (DLightFlags)Flags;
			if (OuterAngle > 0)
				DynamicLight.Flags |= DLightFlags.NoWorldIllumination;
			DynamicLight.Color.R = GetRenderColor().R;
			DynamicLight.Color.G = GetRenderColor().G;
			DynamicLight.Color.B = GetRenderColor().B;
			DynamicLight.Color.Exponent = (sbyte)Exponent;
			DynamicLight.Origin = GetAbsOrigin();
			DynamicLight.InnerAngle = InnerAngle;
			DynamicLight.OuterAngle = OuterAngle;
			DynamicLight.Die = (float)gpGlobals.CurTime + 1e6f;
			DynamicLight.Direction = forward;
		}
		else {
			DynamicLight?.Die = (float)gpGlobals.CurTime;
			DynamicLight = null;
		}

		if (OuterAngle > 0 && (Flags & (int)DLightFlags.NoWorldIllumination) == 0) {
			if (SpotlightEnd == null || SpotlightEnd.Key != -EntIndex()) {
				SpotlightEnd = effects.AllocDlight(-EntIndex());
				Assert(SpotlightEnd != null);
			}

			MathLib.VectorMA(GetAbsOrigin(), Radius, forward, out Vector3 end);

			PushEnableAbsRecomputations(false);
			Util.TraceLine(GetAbsOrigin(), end, Mask.NPCWorldStatic, null, Source.CollisionGroup.None, out Trace pm);
			PopEnableAbsRecomputations();
			MathLib.VectorCopy(pm.EndPos, out SpotlightEnd.Origin);

			if (pm.Fraction == 1.0f) {
				SpotlightEnd.Die = (float)gpGlobals.CurTime;
				SpotlightEnd = null;
			}
			else {
				float falloff = 1.0f - pm.Fraction;
				falloff *= falloff;

				SpotlightEnd.Style = LightStyle;
				SpotlightEnd.Flags = DLightFlags.NoModelIllumination | ((DLightFlags)Flags & DLightFlags.DisplacementMask);
				SpotlightEnd.Radius = SpotRadius;
				SpotlightEnd.Die = (float)gpGlobals.CurTime + 1e6f;
				SpotlightEnd.Color.R = (byte)(GetRenderColor().R * falloff);
				SpotlightEnd.Color.G = (byte)(GetRenderColor().G * falloff);
				SpotlightEnd.Color.B = (byte)(GetRenderColor().B * falloff);
				SpotlightEnd.Color.Exponent = (sbyte)Exponent;

				SpotlightEnd.Direction = forward;

				render.TouchLight(SpotlightEnd);
			}
		}
		else {
			SpotlightEnd?.Die = (float)gpGlobals.CurTime;
			SpotlightEnd = null;
		}

		SetNextClientThink(gpGlobals.CurTime + 0.001f);
	}
}
