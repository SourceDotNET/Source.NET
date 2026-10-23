using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Physics;
using Source.GUI.Controls;

namespace Game.Client;

[NetworkName("CBaseCombatCharacter")]
public partial class C_BaseCombatCharacter : C_BaseFlex
{
	[NetworkTable("DT_BCCLocalPlayerExclusive")]
	public static readonly NetworkProp[] BCCLocalPlayerExclusiveProps = [
		nameof(NextAttack),
	];

	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(BCCLocalPlayerExclusiveProps)) { Name = "bcc_localdata" },
		nameof(ActiveWeapon),
		nameof(MyWeapons),
		nameof(BloodColor),
	];

	public override bool IsBaseCombatCharacter() => true;

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		new(nameof(Ammo)) { Flags = FieldTypeDescFlags.InSendTable },
		nameof(NextAttack),
		nameof(ActiveWeapon),
		nameof(MyWeapons),
	];

	public C_BaseCombatWeapon? GetWeapon(int i) => MyWeapons[i].Get();

	public TimeUnit_t GetNextAttack() => NextAttack;
	public void SetNextAttack(TimeUnit_t wait) => NextAttack = wait;

	[NetworkVar("m_flNextAttack")]
	public TimeUnit_t NextAttack;
	[NetworkVar("m_hLastWeapon")]
	public Handle<C_BaseCombatWeapon> LastWeapon = new();
	[NetworkVar("m_hActiveWeapon")]
	public Handle<C_BaseCombatWeapon> ActiveWeapon = new();
	[NetworkVar("m_hMyWeapons")]
	public InlineArrayNewMaxWeapons<Handle<C_BaseCombatWeapon>> MyWeapons = new();

	[NetworkVar("m_iAmmo")]
	public partial ref readonly InlineArray256<int> Ammo { get; }

	[NetworkVar("m_bloodColor")]
	public BloodColor BloodColor;

	public int WeaponCount() => MAX_WEAPONS;

	public override void DoMuzzleFlash() {
		C_BaseCombatWeapon? weapon = GetActiveWeapon();
		if (weapon != null)
			weapon.DoMuzzleFlash();
		else
			base.DoMuzzleFlash();
	}
}
