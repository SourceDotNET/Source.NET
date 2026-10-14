using Source.Common;
using Source.Common.Formats.BSP;
using Source;

using Game.Shared;

namespace Game.Client;

using FIELD = FIELD<C_AI_BaseNPC>;

[NetworkName("CAI_BaseNPC")]
public class C_AI_BaseNPC : C_BaseCombatCharacter
{
	public static readonly RecvTable DT_AI_BaseNPC = new(DT_BaseCombatCharacter, [
		RecvPropInt(FIELD.OF(nameof(LifeState))),
		RecvPropBool(FIELD.OF(nameof(PerformAvoidance))),
		RecvPropBool(FIELD.OF(nameof(IsMoving))),
		RecvPropBool(FIELD.OF(nameof(FadeCorpse))),
		RecvPropInt(FIELD.OF(nameof(DeathPose))),
		RecvPropInt(FIELD.OF(nameof(DeathFrame))),
		RecvPropBool(FIELD.OF(nameof(ImportantRagdoll))),
	]);
	public static readonly new ClientClass ClientClass = new ClientClass(DT_AI_BaseNPC);

	[NetworkName("m_bPerformAvoidance")]
	public bool PerformAvoidance;
	[NetworkName("m_bIsMoving")]
	public bool IsMoving;
	[NetworkName("m_bFadeCorpse")]
	public bool FadeCorpse;
	[NetworkName("m_iDeathPose")]
	public int DeathPose;
	[NetworkName("m_iDeathFrame")]
	public int DeathFrame;
	public bool SpeedModActive;
	public int SpeedModRadius;
	public int SpeedModSpeed;
	[NetworkName("m_bImportanRagdoll")]
	public bool ImportantRagdoll;
	public float TimePingEffect;

	public override Mask PhysicsSolidMaskForEntity() {
		if (!IsRagdoll())
			return Mask.NPCSolid;

		return Mask.Solid;
	}
}
