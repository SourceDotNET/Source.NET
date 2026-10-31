using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.Mathematics;

namespace Game.Client;

#if GMOD_DLL
using Game.Client.GarrysMod;
#endif

public partial class C_BaseViewModel
{
	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		new(nameof(ModelIndex)) { Type = FieldType.Short, Flags = FieldTypeDescFlags.InSendTable | FieldTypeDescFlags.ModelIndex },
		new(nameof(Skin)) { Type = FieldType.Integer, Flags = FieldTypeDescFlags.InSendTable },
		new(nameof(Body)) { Type = FieldType.Integer, Flags = FieldTypeDescFlags.InSendTable },
		new(nameof(Sequence)) { Type = FieldType.Integer, Flags = FieldTypeDescFlags.InSendTable },
		new(nameof(_ViewModelIndex)) { Type = FieldType.Integer, Flags = FieldTypeDescFlags.InSendTable },
		new(nameof(PlaybackRate)) { Type = FieldType.Float, Flags = FieldTypeDescFlags.InSendTable, Tolerance = 0.125f },
		new(nameof(Effects)) { Type = FieldType.Integer, Flags = FieldTypeDescFlags.InSendTable | FieldTypeDescFlags.Override },
		new(nameof(AnimationParity)) { Type = FieldType.Integer, Flags = FieldTypeDescFlags.InSendTable },
		new(nameof(Weapon)) { Flags = FieldTypeDescFlags.InSendTable },
		new(nameof(AnimTime)) { Type = FieldType.Float, Flags = 0 },
		new(nameof(Owner)) { Flags = FieldTypeDescFlags.Save },
		new(nameof(TimeWeaponIdle)) { Type = FieldType.Float },
		new(nameof(Activity)) { Type = FieldType.Integer },
		new(nameof(Cycle)) { Type = FieldType.Float, Flags = FieldTypeDescFlags.Private | FieldTypeDescFlags.Override | FieldTypeDescFlags.NoErrorCheck },
	];

	int OldAnimationParity;
#if GMOD_DLL
	public override int DrawModel(StudioFlags flags) {
		if (!ReadyToDraw)
			return 0;

		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (GetOwningWeapon() == null) {
			if (GetModelPtr() == null)
				return 0;
			if (stricmp(modelinfo.GetModelName(GetModel()), "weapons/v_hands.mdl") != 0)
				return 0;
		}

		if ((flags & StudioFlags.Render) == 0)
			GetOwningWeapon();
		else {
			float blend = (float)(GetFxBlend() / 255.0f);
			if (blend <= 0.0f)
				return 0;

			render.SetBlend(blend);
			Span<float> color = stackalloc float[3];
			GetColorModulation(color);
			render.SetColorModulation(color);

			C_BaseCombatWeapon? preWeapon = GetOwningWeapon();
			if (preWeapon != null && player != null && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PreDrawViewModel)) {
				LuaEntity.Push_Entity(this);
				LuaEntity.Push_Entity(player);
				LuaEntity.Push_Entity(preWeapon);
				g_Lua!.PushNumber((int)flags);
				if (gGM.CallFinish(4))
					return 0;
			}
		}

		C_BaseCombatWeapon? weapon = GetOwningWeapon();
		int ret;
		if (player != null && player.IsOverridingViewmodel())
			ret = player.DrawOverriddenViewmodel(this, flags);
		else if (weapon != null && weapon.IsOverridingViewmodel())
			ret = weapon.DrawOverriddenViewmodel(this, flags);
		else
			ret = base.DrawModel(flags);

		StudioFlags drawnFlags = flags;
		if ((flags & StudioFlags.Render) != 0 && weapon != null) {
			drawnFlags = flags | StudioFlags.Transparency;
			if ((flags & (StudioFlags.ShadowDepthTexture | StudioFlags.SSAODepthTexture)) != 0)
				drawnFlags = flags & ~StudioFlags.Transparency;
			weapon.ViewModelDrawn(this);
		}

		if ((drawnFlags & StudioFlags.Render) != 0 && weapon != null && player != null && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PostDrawViewModel)) {
			LuaEntity.Push_Entity(this);
			LuaEntity.Push_Entity(player);
			LuaEntity.Push_Entity(weapon);
			g_Lua!.PushNumber((int)drawnFlags);
			gGM.CallNoReturns(4);
		}

		return ret;
	}
#else
	public override int DrawModel(StudioFlags flags) {
		if (!ReadyToDraw)
			return 0;

		if ((flags & StudioFlags.Render) != 0) {
			// Determine blending amount and tell engine
			float blend = (float)(GetFxBlend() / 255.0f);

			// Totally gone
			if (blend <= 0.0f)
				return 0;

			// Tell engine
			render.SetBlend(blend);

			Span<float> color = stackalloc float[3];
			GetColorModulation(color);
			render.SetColorModulation(color);
		}

		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		C_BaseCombatWeapon? weapon = GetOwningWeapon();
		int ret;
		if (player != null && player.IsOverridingViewmodel())
			ret = player.DrawOverriddenViewmodel(this, flags);
		else if (weapon != null && weapon.IsOverridingViewmodel())
			ret = weapon.DrawOverriddenViewmodel(this, flags);
		else
			ret = base.DrawModel(flags);

		if ((flags & StudioFlags.Render) != 0) {
			if (OldAnimationParity != AnimationParity)
				OldAnimationParity = AnimationParity;

			// Tell the weapon itself that we've rendered, in case it wants to do something
			if (weapon != null)
				weapon.ViewModelDrawn(this);
		}

		return ret;
	}
#endif
	public void UpdateAnimationParity() {
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (OldAnimationParity != AnimationParity && !GetPredictable()) {
			double curtime = player != null && IsIntermediateDataAllocated() ? player.GetFinalPredictedTime() : gpGlobals.CurTime;
			SetCycle(0);
			AnimTime = curtime;
		}
	}

	public override void OnDataChanged(DataUpdateType updateType) {
		SetPredictionEligible(true);
		base.OnDataChanged(updateType);
	}

	public override void PostDataUpdate(DataUpdateType updateType) {
		base.PostDataUpdate(updateType);
		OnLatchInterpolatedVariables(LatchFlags.LatchAnimationVar);
#if GMOD_DLL
		UpdateAnimationParity();
		OldAnimationParity = AnimationParity;
#endif
	}

	public override bool Interpolate(TimeUnit_t currentTime) {
		StudioHdr? studioHdr = GetModelPtr();
#if !GMOD_DLL
		UpdateAnimationParity();
#endif
		bool bret = base.Interpolate(currentTime);

		// Hack to extrapolate cycle counter for view model
		TimeUnit_t elapsed_time = currentTime - AnimTime;
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();

		// Predicted viewmodels have fixed up interval
		if (GetPredictable() || IsClientCreated()) {
			Assert(player != null);
			TimeUnit_t curtime = player != null ? player.GetFinalPredictedTime() : gpGlobals.CurTime;
			elapsed_time = curtime - AnimTime;
			if (!engine.IsPaused())
				elapsed_time += (gpGlobals.InterpolationAmount * TICK_INTERVAL);
		}

		// Prediction errors?	
		if (elapsed_time < 0)
			elapsed_time = 0;

		TimeUnit_t dt = elapsed_time * GetSequenceCycleRate(studioHdr, GetSequence()) * GetPlaybackRate();
		if (dt >= 1.0f) {
			if (!IsSequenceLooping(GetSequence()))
				dt = 0.999;
			else
				dt = MathLib.Fmodf(dt, 1.0);
		}
#if GMOD_DLL
		else if (dt < -1.0) {
			if (!IsSequenceLooping(GetSequence()))
				dt = 0.0;
			else
				dt = -MathLib.Fmodf(-dt, 1.0);
		}
		if (dt < 0.0)
			dt = 1.0 - Math.Abs(dt);
#endif
		SetCycle(dt);
		return bret;
	}
	public override bool IsViewModel() => true;
	public override RenderGroup GetRenderGroup() {
		return RenderGroup.ViewModelOpaque;
	}
}
