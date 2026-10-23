using Source.Common;
using Source.Common.Formats.BSP;
using Source;

using Game.Shared;

namespace Game.Client;

[NetworkName("CAI_BaseNPC")]
public partial class C_AI_BaseNPC : C_BaseCombatCharacter
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(LifeState),
		nameof(PerformAvoidance),
		nameof(IsMoving),
		nameof(FadeCorpse),
		nameof(DeathPose),
		nameof(DeathFrame),
		nameof(ImportantRagdoll),
	];

	[NetworkVar("m_bPerformAvoidance")]
	public bool PerformAvoidance;
	[NetworkVar("m_bIsMoving")]
	public bool IsMoving;
	[NetworkVar("m_bFadeCorpse")]
	public bool FadeCorpse;
	[NetworkVar("m_iDeathPose")]
	public int DeathPose;
	[NetworkVar("m_iDeathFrame")]
	public int DeathFrame;
	public bool SpeedModActive;
	public int SpeedModRadius;
	public int SpeedModSpeed;
	[NetworkVar("m_bImportanRagdoll")]
	public bool ImportantRagdoll;
	public float TimePingEffect;

	public override bool IsNPC() => true;

	public override Mask PhysicsSolidMaskForEntity() {
		if (!IsRagdoll())
			return Mask.NPCSolid;

		return Mask.Solid;
	}
}
