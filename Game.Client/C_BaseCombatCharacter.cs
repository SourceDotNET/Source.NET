using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Physics;
using Source.GUI.Controls;

using DEFINE = Source.DEFINE<Game.Client.C_BaseCombatCharacter>;
using FIELD = Source.FIELD<Game.Client.C_BaseCombatCharacter>;

namespace Game.Client;

[NetworkName("CBaseCombatCharacter")]
public partial class C_BaseCombatCharacter : C_BaseFlex
{
	public static readonly RecvTable DT_BCCLocalPlayerExclusive = new(nameof(DT_BCCLocalPlayerExclusive), [
		RecvPropTime64(FIELD.OF(nameof(NextAttack))),
	]);
	public override bool IsBaseCombatCharacter() => true;

	public static readonly new DataMap PredMap = new(typeof(C_BaseCombatCharacter), C_BaseFlex.PredMap, [
		DEFINE.PRED_ARRAY( nameof(Ammo), FieldType.Integer, MAX_AMMO_TYPES, FieldTypeDescFlags.InSendTable ),
		DEFINE.PRED_FIELD( nameof(NextAttack), FieldType.Double, FieldTypeDescFlags.InSendTable ),
		DEFINE.PRED_FIELD( nameof(ActiveWeapon), FieldType.EHandle, FieldTypeDescFlags.InSendTable ),
		DEFINE.PRED_ARRAY( nameof(MyWeapons), FieldType.EHandle, MAX_WEAPONS, FieldTypeDescFlags.InSendTable ),
	]); public override DataMap? GetPredDescMap() => PredMap;

	public static readonly RecvTable DT_BaseCombatCharacter = new(DT_BaseFlex, [
		RecvPropDataTable( "bcc_localdata", DT_BCCLocalPlayerExclusive ),
		RecvPropEHandle(FIELD.OF(nameof(ActiveWeapon))),
		RecvPropArray3(FIELD.OF_ARRAY(nameof(MyWeapons)), RecvPropEHandle( FIELD.OF_ARRAY(nameof(MyWeapons)))),
		RecvPropInt(FIELD.OF(nameof(BloodColor)))
	]);
	public static readonly new ClientClass ClientClass = new ClientClass(DT_BaseCombatCharacter);

	public C_BaseCombatWeapon? GetWeapon(int i) => MyWeapons[i].Get();

	public TimeUnit_t GetNextAttack() => NextAttack;
	public void SetNextAttack(TimeUnit_t wait) => NextAttack = wait;

	[NetworkName("m_flNextAttack")]
	public TimeUnit_t NextAttack;
	[NetworkName("m_hLastWeapon")]
	public Handle<C_BaseCombatWeapon> LastWeapon = new();
	[NetworkName("m_hActiveWeapon")]
	public Handle<C_BaseCombatWeapon> ActiveWeapon = new();
	[NetworkName("m_hMyWeapons")]
	public InlineArrayNewMaxWeapons<Handle<C_BaseCombatWeapon>> MyWeapons = new();

	[NetworkName("m_iAmmo")]
	[NetworkArraySize(MAX_AMMO_TYPES)] public readonly NetworkArray<int> Ammo = new(MAX_AMMO_TYPES);

	[NetworkName("m_bloodColor")]
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
