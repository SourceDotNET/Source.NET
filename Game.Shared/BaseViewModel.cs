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

	[NetworkTable(NoBase = true)]
	public static readonly new NetworkProp[] Props = [
		new(nameof(ModelIndex)) { Encoding = NetEncoding.ModelIndex },
		new(nameof(Body)) { Bits = 32, Side = NetworkSide.Server },
		new(nameof(Skin)) { Bits = 10 },
		new(nameof(Body)) { Side = NetworkSide.Client },
		new(nameof(Sequence)) { Bits = 12, Flags = PropFlags.Unsigned, Proxy = typeof(ViewModelSequenceProxy) },
		new(nameof(_ViewModelIndex)) { Bits = VIEWMODEL_INDEX_BITS, Flags = PropFlags.Unsigned },
		new(nameof(PlaybackRate)) { Encoding = NetEncoding.Float, Bits = 8, Flags = PropFlags.RoundUp, Low = -4.0f, High = 12.0f },
		new(nameof(Effects)) { Bits = 10, Flags = PropFlags.Unsigned },
		new(nameof(AnimationParity)) { Bits = 3, Flags = PropFlags.Unsigned },
		new(nameof(Weapon)) { Proxy = typeof(ViewModelWeaponProxy) },
		nameof(Owner),

		new(nameof(NewSequenceParity)) { Bits = (int)EntityEffects.ParityBits, Flags = PropFlags.Unsigned },
		new(nameof(ResetEventsParity)) { Bits = (int)EntityEffects.ParityBits, Flags = PropFlags.Unsigned },
		new(nameof(MuzzleFlashParity)) { Bits = (int)EntityEffects.MuzzleflashBits, Flags = PropFlags.Unsigned },

		new(nameof(PoseParameter)) { Encoding = NetEncoding.Array, Bits = 8, Flags = 0, Low = 0.0f, High = 1.0f },
	];

	public static class ViewModelSequenceProxy
	{
#if CLIENT_DLL
		public static void Recv(ref readonly RecvProxyData data, object instance, IFieldAccessor field) => RecvProxy_SequenceNum(in data, instance, field);
#endif
	}

	public static class ViewModelWeaponProxy
	{
#if CLIENT_DLL
		public static void Recv(ref readonly RecvProxyData data, object instance, IFieldAccessor field) => RecvProxy_Weapon(in data, instance, field);
#endif
	}

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
#endif
	[NetworkVar("m_nViewModelIndex")]
	public partial int _ViewModelIndex { get; set; }
	[NetworkVar("m_hOwner")]
	public partial EHANDLE Owner { get; set; }
	[NetworkVar("m_hWeapon")]
	public partial Handle<BaseCombatWeapon> Weapon { get; set; }
	[NetworkVar("m_nAnimationParity")] public partial int AnimationParity { get; set; }

	public BaseCombatWeapon? GetOwningWeapon() => Weapon.Get();

	public int ViewModelIndex() => _ViewModelIndex;

	public override void Spawn() {
		Precache();
		CollisionProp().SetCollisionBounds(new(-8, -4, -2), new(8, 4, 2));
		SetSolid(SolidType.None);
	}

	public void SetOwner(BaseEntity? entity) => Owner = EHANDLE.FromEntity(entity);

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
		Owner = new();
		Weapon = new();
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
		Weapon = Handle<BaseCombatWeapon>.FromEntity(weapon);

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
