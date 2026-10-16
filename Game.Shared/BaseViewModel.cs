#if CLIENT_DLL || GAME_DLL
#if CLIENT_DLL
global using BaseViewModel = Game.Client.C_BaseViewModel;
namespace Game.Client;
#else
global using BaseViewModel = Game.Server.BaseViewModel;
namespace Game.Server;
#endif

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.Mathematics;

using System;
using System.Numerics;

using DEFINE = Source.DEFINE<BaseViewModel>;
using FIELD = Source.FIELD<BaseViewModel>;

[LinkEntityToClass("viewmodel")]
[NetworkName("CBaseViewModel")]
public partial class
#if CLIENT_DLL
	C_BaseViewModel
#else
	BaseViewModel
#endif

	:

#if CLIENT_DLL
	C_BaseAnimating
#else
	BaseAnimating
#endif
{

	public const int VIEWMODEL_INDEX_BITS = 4;

	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_BaseViewModel = new([
#if CLIENT_DLL
			RecvPropInt(FIELD.OF(nameof(ModelIndex))),
			RecvPropInt(FIELD.OF(nameof(Body))),
			RecvPropInt(FIELD.OF(nameof(Skin))),
			RecvPropInt(FIELD.OF(nameof(Sequence)), 0, RecvProxy_SequenceNum),
			RecvPropInt(FIELD.OF(nameof(_ViewModelIndex))),
			RecvPropFloat(FIELD.OF(nameof(PlaybackRate))),
			RecvPropInt(FIELD.OF(nameof(Effects))),
			RecvPropInt(FIELD.OF(nameof(AnimationParity))),
			RecvPropEHandle(FIELD.OF(nameof(Weapon)), RecvProxy_Weapon),
			RecvPropEHandle(FIELD.OF(nameof(Owner))),

			RecvPropInt(FIELD.OF(nameof(NewSequenceParity))),
			RecvPropInt(FIELD.OF(nameof(ResetEventsParity))),
			RecvPropInt(FIELD.OF(nameof(MuzzleFlashParity))),

			RecvPropFloat(FIELD.OF_ARRAYINDEX(nameof(PoseParameter), 0)),
			RecvPropArray(FIELD.OF_ARRAY(nameof(PoseParameter))),
#else
			SendPropModelIndex(FIELD.OF(nameof(ModelIndex))),
			SendPropInt(FIELD.OF(nameof(Body)), 32),
			SendPropInt(FIELD.OF(nameof(Skin)), 10),
			SendPropInt(FIELD.OF(nameof(Sequence)), 12, PropFlags.Unsigned),
			SendPropInt(FIELD.OF(nameof(_ViewModelIndex)), VIEWMODEL_INDEX_BITS, PropFlags.Unsigned),
			SendPropFloat(FIELD.OF(nameof(PlaybackRate)), 8, PropFlags.RoundUp, -4.0f, 12.0f),
			SendPropInt(FIELD.OF(nameof(Effects)), 10, PropFlags.Unsigned),
			SendPropInt(FIELD.OF(nameof(AnimationParity)), 3, PropFlags.Unsigned),
			SendPropEHandle(FIELD.OF(nameof(Weapon))),
			SendPropEHandle(FIELD.OF(nameof(Owner))),

			SendPropInt(FIELD.OF(nameof(NewSequenceParity)), (int)EntityEffects.ParityBits, PropFlags.Unsigned ),
			SendPropInt(FIELD.OF(nameof(ResetEventsParity)), (int)EntityEffects.ParityBits, PropFlags.Unsigned ),
			SendPropInt(FIELD.OF(nameof(MuzzleFlashParity)), (int)EntityEffects.MuzzleflashBits, PropFlags.Unsigned ),

			SendPropFloat(FIELD.OF_SENDINFO_ARRAY(nameof(PoseParameter)), 8, 0, 0.0f, 1.0f),
			SendPropArray(FIELD.OF_ARRAY(nameof(PoseParameter))),
#endif
		]);

	TimeUnit_t TimeWeaponIdle;
	Activity Activity;
#if CLIENT_DLL

	private static void RecvProxy_Weapon(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		BaseViewModel viewModel = (BaseViewModel)instance;
		BaseCombatWeapon? oldWeapon = viewModel.GetOwningWeapon();

		RecvProxy_IntToEHandle(in data, instance, field);

		BaseCombatWeapon? newWeapon = viewModel.GetOwningWeapon();
		if (newWeapon != oldWeapon) {
			viewModel.SetCycle(0);
			viewModel.AnimTime = gpGlobals.CurTime;
		}
	}

	private static void RecvProxy_SequenceNum(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		BaseViewModel model = (BaseViewModel)instance;
		if (data.Value.Int != model.GetSequence()) {
			model.SetSequence(data.Value.Int);
			model.AnimTime = gpGlobals.CurTime;
			model.SetCycle(0);
		}
	}
	public static readonly new ClientClass ClientClass = new ClientClass(DT_BaseViewModel);
#else
#pragma warning disable CS0109 // Member does not hide an inherited member; new keyword is not required
	public static readonly new ServerClass ServerClass = new ServerClass(DT_BaseViewModel);
#pragma warning restore CS0109 // Member does not hide an inherited member; new keyword is not required
#endif
	[NetworkName("m_nViewModelIndex")]
	public int _ViewModelIndex;
	[NetworkName("m_hOwner")]
	public EHANDLE Owner = new();
	[NetworkName("m_hWeapon")]
	public Handle<BaseCombatWeapon> Weapon = new();
	[NetworkName("m_nAnimationParity")]
#if CLIENT_DLL
	public int AnimationParity;
#else
	[NetworkVar] public partial int AnimationParity { get; set; }
#endif

	public BaseCombatWeapon? GetOwningWeapon() => Weapon.Get();

	public int ViewModelIndex() => _ViewModelIndex;

	public override void Spawn() {
		Precache();
		CollisionProp().SetCollisionBounds(new(-8, -4, -2), new(8, 4, 2));
		SetSolid(SolidType.None);
	}

	public void SetOwner(BaseEntity? entity) => Owner.Set(entity);

	public void SetIndex(int index) {
		_ViewModelIndex = index;
		Assert(_ViewModelIndex < (1 << VIEWMODEL_INDEX_BITS));
	}

	public const int VIEWMODEL_ANIMATION_PARITY_BITS = 3;

	public virtual void SendViewModelMatchingSequence(int sequence) {
		SetSequence(sequence);

		AnimationParity = (AnimationParity + 1) & ((1 << VIEWMODEL_ANIMATION_PARITY_BITS) - 1);
#if CLIENT_DLL
		OldAnimationParity = AnimationParity;

		// Force frame interpolation to start at exactly frame zero
		AnimTime = gpGlobals.CurTime;
#else
		BaseCombatWeapon? weapon = Weapon.Get();
		// TODO: bool showControlPanels = weapon != null && weapon.ShouldShowControlPanels();
		// TODO: SetControlPanelsActive(showControlPanels);
#endif

		// Restart animation at frame 0
		SetCycle(0);
		ResetSequenceInfo();
	}

	public BaseEntity? GetOwner() => Owner.Get();

#if CLIENT_DLL
	public static void FormatViewModelAttachment(ref Vector3 origin, bool inverse) {
		// Presumably, SetUpView has been called so we know our FOV and render origin.
		ref readonly ViewSetup pViewSetup = ref view.GetPlayerViewSetup();

		float worldx = MathF.Tan(MathLib.DEG2RAD(pViewSetup.FOV) * 0.5f);
		float viewx = MathF.Tan(MathLib.DEG2RAD(pViewSetup.FOVViewmodel) * 0.5f);

		// aspect ratio cancels out, so only need one factor
		// the difference between the screen coordinates of the 2 systems is the ratio
		// of the coefficients of the projection matrices (tan (fov/2) is that coefficient)
		// NOTE: viewx was coming in as 0 when folks set their viewmodel_fov to 0 and show their weapon.
		float factorX = viewx != 0 ? (worldx / viewx) : 0.0f;
		float factorY = factorX;

		// Get the coordinates in the viewer's space.
		Vector3 tmp = origin - pViewSetup.Origin;
		Vector3 vTransformed = new(MainViewRight().Dot(tmp), MainViewUp().Dot(tmp), MainViewForward().Dot(tmp));

		// Now squash X and Y.
		if (inverse) {
			if (factorX != 0 && factorY != 0) {
				vTransformed.X /= factorX;
				vTransformed.Y /= factorY;
			}
			else {
				vTransformed.X = 0.0f;
				vTransformed.Y = 0.0f;
			}
		}
		else {
			vTransformed.X *= factorX;
			vTransformed.Y *= factorY;
		}

		// Transform back to world space.
		Vector3 vOut = (MainViewRight() * vTransformed.X) + (MainViewUp() * vTransformed.Y) + (MainViewForward() * vTransformed.Z);
		origin = pViewSetup.Origin + vOut;
	}
#endif

	public void CalcViewModelView(BasePlayer owner, in Vector3 eyePosition, in QAngle eyeAngles) {
		QAngle vmangoriginal = eyeAngles;
		QAngle vmangles = eyeAngles;
		Vector3 vmorigin = eyePosition;

		BaseCombatWeapon? pWeapon = Weapon.Get();
		//Allow weapon lagging
		if (pWeapon != null) {
#if CLIENT_DLL
			if (!prediction.InPrediction())
#endif
			{
				// add weapon-specific bob 
				// TODO: pWeapon.AddViewmodelBob(this, vmorigin, vmangles);
			}
		}
		// Add model-specific bob even if no weapon associated (for head bob for off hand models)
		// todo: AddViewModelBob(owner, vmorigin, vmangles);
		// todo: CalcViewModelLag
#if CLIENT_DLL
#if CLIENT_DLL
		if (!prediction.InPrediction())  // Let the viewmodel shake at about 10% of the amplitude of the player's view
			vieweffects.ApplyShake(ref vmorigin, ref vmangles, 0.1f);
#endif
#endif
		SetLocalOrigin(in vmorigin);
		SetLocalAngles(in vmangles);
	}

	public
#if CLIENT_DLL
	C_BaseViewModel
#else
	BaseViewModel
#endif
		() {
#if CLIENT_DLL
		OldAnimationParity = 0;
		EntClientFlags |= EntClientFlags.AlwaysInterpolate;
#endif
	}

	void SetControlPanelsActive(bool state) { } // todo

	public override void AddEffects(EntityEffects effects) {
		if ((effects & EntityEffects.NoDraw) != 0)
			SetControlPanelsActive(false);
		base.AddEffects(effects);
	}

	public override void RemoveEffects(EntityEffects effects) {
		if ((effects & EntityEffects.NoDraw) != 0)
			SetControlPanelsActive(true);
		base.RemoveEffects(effects);
	}


	public void SetWeaponModel(ReadOnlySpan<char> modelname, BaseCombatWeapon? weapon = null) {
		Weapon.Set(weapon);

#if CLIENT_DLL
		SetModel(modelname);
#else
		string? str = modelname.IsEmpty ? null : new string(modelname);

		if (str != VMName) {
			VMName = str;
			SetModel(VMName);
		}
#endif
	}

#if !CLIENT_DLL
	string? VMName;

	public override EdictFlags UpdateTransmitState() {
		if (IsEffectActive(EntityEffects.NoDraw))
			return SetTransmitState(EdictFlags.DontSend);

		return SetTransmitState(EdictFlags.FullCheck);
	}

	public override EdictFlags ShouldTransmit(CheckTransmitInfo info) {
		BasePlayer? owner = ToBasePlayer(Owner.Get());

		if (owner != null && owner.Edict() == info.ClientEnt)
			return EdictFlags.Always;

		BaseEntity? recipientEntity = Instance(info.ClientEnt);

		if (recipientEntity!.IsPlayer()) {
			BasePlayer player = (BasePlayer)recipientEntity;
			if (player.IsHLTV())
				return EdictFlags.PVSCheck;

			if ((player.GetObserverMode() == Shared.ObserverMode.InEye) && (player.GetObserverTarget() == owner))
				return EdictFlags.Always;
		}

		return EdictFlags.DontSend;
	}
#endif
}
#endif
