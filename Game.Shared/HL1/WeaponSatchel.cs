#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
[LinkEntityToClass("weapon_satchel")]
[NetworkName("CWeaponSatchel")]
public partial class WeaponSatchel : BaseHL1MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(RadioViewIndex)) { Bits = 14 },
		new(nameof(RadioWorldIndex)) { Encoding = NetEncoding.Int, Bits = 14 },
		new(nameof(SatchelViewIndex)) { Encoding = NetEncoding.Int, Bits = 14 },
		new(nameof(SatchelWorldIndex)) { Encoding = NetEncoding.Int, Bits = 14 },
		new(nameof(ChargeReady)) { Encoding = NetEncoding.Int, Bits = 3, Flags = PropFlags.Unsigned },
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		new(nameof(RadioViewIndex)) { Flags = FieldTypeDescFlags.InSendTable | FieldTypeDescFlags.ModelIndex },
		new(nameof(RadioWorldIndex)) { Flags = FieldTypeDescFlags.InSendTable | FieldTypeDescFlags.ModelIndex },
		new(nameof(SatchelViewIndex)) { Flags = FieldTypeDescFlags.InSendTable | FieldTypeDescFlags.ModelIndex },
		new(nameof(SatchelWorldIndex)) { Flags = FieldTypeDescFlags.InSendTable | FieldTypeDescFlags.ModelIndex },
		nameof(ChargeReady),
	];

	[NetworkVar("m_iRadioViewIndex")]
	public partial int RadioViewIndex { get; set; }
	[NetworkVar("m_iRadioWorldIndex")]
	public partial float RadioWorldIndex { get; set; }
	[NetworkVar("m_iSatchelViewIndex")]
	public partial float SatchelViewIndex { get; set; }
	[NetworkVar("m_iSatchelWorldIndex")]
	public partial float SatchelWorldIndex { get; set; }
	[NetworkVar("m_iChargeReady")]
	public partial float ChargeReady { get; set; }
}
#endif
