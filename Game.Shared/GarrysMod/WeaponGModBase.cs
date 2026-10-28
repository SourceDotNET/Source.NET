#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Mathematics;

// ====================================================================================================== //
// WeaponHL2MPBase
// ====================================================================================================== //
namespace Game
{
	public static partial class Util
	{
		public static void ClipPunchAngleOffset(ref QAngle input, in QAngle punch, in QAngle clip) {
			QAngle final = input + punch;

			//Clip each component
			for (int i = 0; i < 3; i++) {
				if (final[i] > clip[i]) {
					final[i] = clip[i];
				}
				else if (final[i] < -clip[i]) {
					final[i] = -clip[i];
				}

				//Return the result
				input[i] = final[i] - punch[i];
			}
		}
	}
}

#if CLIENT_DLL
namespace Game.Client
#else
namespace Game.Server
#endif

{
	[LinkEntityToClass("weapon_hl2mp_base")]
	[NetworkName("CWeaponHL2MPBase")]
	public partial class
#if CLIENT_DLL
		C_WeaponHL2MPBase
#else
	WeaponHL2MPBase
#endif
		: BaseCombatWeapon
	{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

#if CLIENT_DLL
		public override bool ShouldPredict() {
			if (GetOwner() != null && GetOwner() == C_BasePlayer.GetLocalPlayer())
				return true;
			return base.ShouldPredict();
		}
#endif

		public override bool IsPredicted() {
			return true;
		}

		public
#if CLIENT_DLL
	C_WeaponHL2MPBase
#else
	WeaponHL2MPBase
#endif
		() {
			SetPredictionEligible(true);
		}

		public override void WeaponSound(WeaponSound soundType, TimeUnit_t soundTime = 0.0) {
#if CLIENT_DLL
			ReadOnlySpan<char> shootsound = GetWpnData().ShootSounds[(int)soundType].AsSpan().SliceNullTerminatedString();
			if (shootsound.IsEmpty || shootsound[0] == '\0')
				return;

			BroadcastRecipientFilter filter = new();

			if (!te.CanPredict())
				return;

			EmitSound(filter, GetPlayerOwner()!.EntIndex(), shootsound, in GetPlayerOwner()!.GetAbsOrigin(), soundTime, out _);
#else
			base.WeaponSound(soundType, soundTime);
#endif
		}

		public BasePlayer? GetPlayerOwner() => ToBasePlayer(GetOwner());
	}

	// ====================================================================================================== //
	// BaseHL2MPCombatWeapon
	// ====================================================================================================== //

	[LinkEntityToClass("basehl2mpcombatweapon")]
	[NetworkName("CBaseHL2MPCombatWeapon")]
	public partial class
#if CLIENT_DLL
		C_BaseHL2MPCombatWeapon
#else
	BaseHL2MPCombatWeapon
#endif
		: WeaponHL2MPBase
	{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

		protected bool Lowered;
		protected TimeUnit_t RaiseTime;
		protected TimeUnit_t HolsterTime;

		public override bool Holster(BaseCombatWeapon switchingTo) {
			if (base.Holster(switchingTo)) {
				SetWeaponVisible(false);
				HolsterTime = gpGlobals.CurTime;
				return true;
			}
			return false;
		}

#if CLIENT_DLL
		public override void OnDataChanged(DataUpdateType updateType) {
			base.OnDataChanged(updateType);
			if (GetPredictable() && !ShouldPredict())
				ShutdownPredictable();
		}

		public override bool ShouldPredict() {
			if (GetOwner() != null && GetOwner() == C_BasePlayer.GetLocalPlayer())
				return true;
			return base.ShouldPredict();
		}
#endif
	}
}
#endif
