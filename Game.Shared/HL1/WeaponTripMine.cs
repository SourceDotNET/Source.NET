#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
using FIELD = Source.FIELD<WeaponTripMine>;
[LinkEntityToClass("weapon_tripmine")]
[NetworkName("CWeaponTripMine")]
public class WeaponTripMine : BaseHL1MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponTripMine = new(DT_BaseHL1MPCombatWeapon, [
#if CLIENT_DLL
			RecvPropFloat(FIELD.OF(nameof(GroundIndex))),
			RecvPropFloat(FIELD.OF(nameof(PickedUpIndex))),
#else
			SendPropFloat(FIELD.OF(nameof(GroundIndex)), 0, PropFlags.NoScale),
			SendPropFloat(FIELD.OF(nameof(PickedUpIndex)), 0, PropFlags.NoScale),
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WeaponTripMine);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponTripMine);
#endif
	[NetworkName("m_iGroundIndex")]
	public float GroundIndex;
	[NetworkName("m_iPickedUpIndex")]
	public float PickedUpIndex;
}
#endif
