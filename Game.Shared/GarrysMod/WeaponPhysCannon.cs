using Source;
#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL

#if CLIENT_DLL
using Game.Client;

using Source;

#endif

using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using Steamworks;

using System;
using System.Numerics;
namespace Game.Shared.GarrysMod;


[LinkEntityToClass("weapon_physcannon")]
[PrecacheWeaponRegister("weapon_physcannon")]
[NetworkName("CWeaponPhysCannon")]
public partial class WeaponPhysCannon : BaseHL2MPCombatWeapon
{
	public WeaponPhysCannon() {
		AttachedObject = new();
	}

	public static bool PlayerHasMegaPhysCannon() => false;


	public enum EffectState_t
	{
		None,
		Closed,
		Ready,
		Holding,
		Launch
	}

#if CLIENT_DLL
	public enum EffectType
	{
		Core,

		Blast,

		Glow1,
		Glow2,
		Glow3,
		Glow4,
		Glow5,
		Glow6,

		EndCap1,
		EndCap2,
		EndCap3,

		NumPhyscannonParameters
	}
	public const int NUM_GLOW_SPRITES = (int)(EffectType.Glow6 - EffectType.Glow1) + 1;
	public const int NUM_ENDCAP_SPRITES = (int)(EffectType.EndCap3 - EffectType.EndCap1) + 1;
	public const int NUM_PHYSCANNON_BEAMS = 3;

	public class PhysCannonEffect
	{
		public PhysCannonEffect() {
			Color = new(255, 255, 255);
			Visible = true;
			Attachment = -1;
		}
		readonly InterpolatedValue Alpha = new();
		readonly InterpolatedValue Scale = new();

		public void SetAttachment(int attachment) => Attachment = attachment;
		public int GetAttachment() => Attachment;

		public void SetVisible(bool visible = true) => Visible = visible;
		public bool IsVisible() => Visible;

		public bool SetMaterial(ReadOnlySpan<char> name) {
			Material.Init(name, MaterialDefines.TEXTURE_GROUP_CLIENT_EFFECTS);
			return Material.IsNotNull;
		}

		public void SetColor(in Vector3 color) => Color = color;
		public ref readonly Vector3 GetColor() => ref Color;

		public MaterialReference GetMaterial() => Material;
		public InterpolatedValue GetAlpha() => Alpha;
		public InterpolatedValue GetScale() => Scale;

		Vector3 Color;
		bool Visible;
		int Attachment;
		readonly MaterialReference Material = new();
	}

	public class PhysCannonEffectBeam
	{
		Beam? Beam;

		public void Init(int startAttachment, int endAttachment, BaseEntity? entity, bool firstPerson) {
			if (Beam != null)
				return;

			BeamInfo beamInfo = new();

			beamInfo.StartEnt = entity;
			beamInfo.StartAttachment = startAttachment;
			beamInfo.EndEnt = entity;
			beamInfo.EndAttachment = endAttachment;
			beamInfo.Type = TempEntType.BeamPoints;
			beamInfo.Start = vec3_origin;
			beamInfo.End = vec3_origin;

			beamInfo.ModelName = (firstPerson) ? PHYSCANNON_BEAM_SPRITE_NOZ : PHYSCANNON_BEAM_SPRITE;

			beamInfo.HaloScale = 0.0f;
			beamInfo.Life = 0.0f;

			if (firstPerson) {
				beamInfo.Width = 0.0f;
				beamInfo.EndWidth = 4.0f;
			}
			else {
				beamInfo.Width = 0.5f;
				beamInfo.EndWidth = 2.0f;
			}

			beamInfo.FadeLength = 0.0f;
			beamInfo.Amplitude = 16;
			beamInfo.Brightness = 255.0f;
			beamInfo.Speed = 150.0f;
			beamInfo.StartFrame = 0;
			beamInfo.FrameRate = 30.0;
			beamInfo.Red = 255.0f;
			beamInfo.Green = 255.0f;
			beamInfo.Blue = 255.0f;
			beamInfo.Segments = 8;
			beamInfo.Renderable = true;
			beamInfo.Flags = BeamFlags.Forever;

			Beam = beams.CreateBeamEntPoint(ref beamInfo);
		}
		public void Release() {
			if (Beam != null) {
				Beam.Flags = 0;
				Beam.Die = gpGlobals.CurTime - 1;
				Beam = null;
			}
		}

		public void SetVisible(bool state = true) {
			if (Beam == null)
				return;
			Beam.Brightness = state ? 255f : 0f;
		}
	}

	readonly PhysCannonEffect[] Parameters = new PhysCannonEffect[(int)EffectType.NumPhyscannonParameters].InstantiateArray();
	readonly PhysCannonEffectBeam[] Beams = new PhysCannonEffectBeam[NUM_PHYSCANNON_BEAMS].InstantiateArray();

#endif

	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Active),
		nameof(AttachedObject),
		new(nameof(AttachedPositionObjectSpace)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(AttachedAnglesPlayerSpace)) { Encoding = NetEncoding.VectorElements, Bits = 11, Flags = PropFlags.RoundDown },
		new(nameof(EffectState)) { Bits = 4 },
		nameof(Open),
		nameof(PhyscannonState),
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(EffectState),
		nameof(Open),
	];

	[NetworkVar("m_bActive")]
	public partial bool Active { get; set; }
	[NetworkVar("m_hAttachedObject")]
	public partial EHANDLE AttachedObject { get; set; }
	[NetworkVar("m_attachedPositionObjectSpace")]
	public partial Vector3 AttachedPositionObjectSpace { get; set; }
	[NetworkVar("m_attachedAnglesPlayerSpace")]
	public partial QAngle AttachedAnglesPlayerSpace { get; set; }
	[NetworkVar("m_EffectState")]
	public partial int EffectState { get; set; }
	[NetworkVar("m_bOpen")]
	public partial bool Open { get; set; }
	[NetworkVar("m_bPhyscannonState")]
	public partial bool PhyscannonState { get; set; }
#if CLIENT_DLL
	public bool OldOpen;
	public int OldEffectState;
	public readonly InterpolatedValue ElementParameter = new();
#endif

	public void DoEffect(EffectState_t effectType, Vector3 pos = default) {
		EffectState = (int)effectType;
#if CLIENT_DLL
		OldEffectState = EffectState;
#endif
		// Msg($"got new effect state: {(EffectState_t)EffectState}\n");
		switch (effectType) {
			case EffectState_t.Closed:
				DoEffectClosed();
				break;
			case EffectState_t.Ready:
				DoEffectReady();
				break;
			case EffectState_t.Holding:
				DoEffectHolding();
				break;
			case EffectState_t.Launch:
				DoEffectLaunch(pos);
				break;
			default:
			case EffectState_t.None:
				DoEffectNone();
				break;
		}
	}

	public void OpenElements() {
		if (Open)
			return;
		WeaponSound(Shared.WeaponSound.Special1);

		BasePlayer? owner = ToBasePlayer(GetOwner());
		if (owner == null)
			return;

		SendWeaponAnim(Activity.ACT_VM_IDLE);
		Open = true;
		DoEffect(EffectState_t.Ready);
	}
	public void CloseElements() {
		if (!Open)
			return;
		WeaponSound(Shared.WeaponSound.MeleeHit);

		BasePlayer? owner = ToBasePlayer(GetOwner());
		if (owner == null)
			return;

		SendWeaponAnim(Activity.ACT_VM_IDLE);
		Open = false;
		DoEffect(EffectState_t.Closed);
	}

	public const float SPRITE_SCALE = 128f;

	static readonly string[] attachNamesGlowThirdPerson = [
		"fork1m",
		"fork1t",
		"fork2m",
		"fork2t",
		"fork3m",
		"fork3t",
	];

	static readonly string[] attachNamesGlow = [
		"fork1b",
		"fork1m",
		"fork1t",
		"fork2b",
		"fork2m",
		"fork2t"
	];
	static readonly string[] attachNamesEndCap = [
		"fork1t",
		"fork2t",
		"fork3t"
	];
	public const string PHYSCANNON_BEAM_SPRITE = "sprites/orangelight1.vmt";
	public const string PHYSCANNON_BEAM_SPRITE_NOZ = "sprites/orangelight1_noz.vmt";
	public const string PHYSCANNON_GLOW_SPRITE = "sprites/glow04_noz";
	public const string PHYSCANNON_ENDCAP_SPRITE = "sprites/orangeflare1";
	public const string PHYSCANNON_CENTER_GLOW = "sprites/orangecore1";
	public const string PHYSCANNON_BLAST_SPRITE = "sprites/orangecore2";


	public void StopEffects() {
		DoEffect(EffectState_t.None);
	}

	public void DestroyEffects() {
#if CLIENT_DLL
		Beams[0].Release();
		Beams[1].Release();
		Beams[2].Release();
#endif
		StopEffects();
	}
	public void StartEffects() {
#if CLIENT_DLL
		if (Parameters[(int)EffectType.Core].GetMaterial().IsNull) {
			Parameters[(int)EffectType.Core].GetScale().Init(0.0f, 1.0f, 0.1f);
			Parameters[(int)EffectType.Core].GetAlpha().Init(255.0f, 255.0f, 0.1f);
			Parameters[(int)EffectType.Core].SetAttachment(1);

			if (Parameters[(int)EffectType.Core].SetMaterial(PHYSCANNON_CENTER_GLOW) == false) {
				// This means the texture was not found
				Assert(false);
			}
		}

		// ------------------------------------------
		// Blast
		// ------------------------------------------

		if (Parameters[(int)EffectType.Blast].GetMaterial().IsNull) {
			Parameters[(int)EffectType.Blast].GetScale().Init(0.0f, 1.0f, 0.1f);
			Parameters[(int)EffectType.Blast].GetAlpha().Init(255.0f, 255.0f, 0.1f);
			Parameters[(int)EffectType.Blast].SetAttachment(1);
			Parameters[(int)EffectType.Blast].SetVisible(false);

			if (Parameters[(int)EffectType.Blast].SetMaterial(PHYSCANNON_BLAST_SPRITE) == false) {
				Assert(false);
			}
		}

		// ------------------------------------------
		// Glows
		// ------------------------------------------

		//Create the glow sprites
		for (int i = (int)EffectType.Glow1; i < ((int)EffectType.Glow1 + NUM_GLOW_SPRITES); i++) {
			if (Parameters[i].GetMaterial().IsNotNull)
				continue;

			Parameters[i].GetScale().SetAbsolute(0.05f * SPRITE_SCALE);
			Parameters[i].GetAlpha().SetAbsolute(64.0f);

			// Different for different views
			if (ShouldDrawUsingViewModel())
				Parameters[i].SetAttachment(LookupAttachment(attachNamesGlow[i - (int)EffectType.Glow1]));
			else
				Parameters[i].SetAttachment(LookupAttachment(attachNamesGlowThirdPerson[i - (int)EffectType.Glow1]));
			Parameters[i].SetColor(new(255, 128, 0));

			if (Parameters[i].SetMaterial(PHYSCANNON_GLOW_SPRITE) == false) {
				// This means the texture was not found
				Assert(false);
			}
		}

		// ------------------------------------------
		// End caps
		// ------------------------------------------


		//Create the glow sprites
		for (int i = (int)EffectType.EndCap1; i < ((int)EffectType.EndCap1 + NUM_ENDCAP_SPRITES); i++) {
			if (Parameters[i].GetMaterial().IsNotNull)
				continue;

			Parameters[i].GetScale().SetAbsolute(0.05f * SPRITE_SCALE);
			Parameters[i].GetAlpha().SetAbsolute(255.0f);
			Parameters[i].SetAttachment(LookupAttachment(attachNamesEndCap[i - (int)EffectType.EndCap1]));
			Parameters[i].SetVisible(false);

			if (Parameters[i].SetMaterial(PHYSCANNON_ENDCAP_SPRITE) == false) {
				// This means the texture was not found
				Assert(false);
			}
		}
#endif
	}
	public void DoEffectIdle() {
#if CLIENT_DLL
		StartEffects();
		for (EffectType i = EffectType.Glow1; i < (EffectType.Glow1 + NUM_GLOW_SPRITES); i++) {
			Parameters[(int)i].GetScale().SetAbsolute(random.RandomFloat(0.075f, 0.05f) * SPRITE_SCALE);
			Parameters[(int)i].GetAlpha().SetAbsolute(random.RandomInt(24, 32));
		}
		for (EffectType i = EffectType.EndCap1; i < (EffectType.EndCap1 + NUM_ENDCAP_SPRITES); i++) {
			Parameters[(int)i].GetScale().SetAbsolute(random.RandomFloat(3, 5));
			Parameters[(int)i].GetAlpha().SetAbsolute(random.RandomInt(200, 255));
		}
		if (EffectState != (int)EffectState_t.Holding) {
			Beams[0].SetVisible(false);
			Beams[1].SetVisible(false);
			Beams[2].SetVisible(false);
		}
#endif
	}
	public void DoEffectHolding() {
#if CLIENT_DLL
		if (ShouldDrawUsingViewModel()) {
			// Scale up the center sprite
			Parameters[(int)EffectType.Core].GetScale().InitFromCurrent(16.0f, 0.2f);
			Parameters[(int)EffectType.Core].GetAlpha().InitFromCurrent(255.0f, 0.1f);
			Parameters[(int)EffectType.Core].SetVisible();

			// Prepare for scale up
			Parameters[(int)EffectType.Blast].SetVisible(false);

			// Turn on the glow sprites
			for (EffectType i = EffectType.Glow1; i < (EffectType.Glow1 + NUM_ENDCAP_SPRITES); i++) {
				Parameters[(int)i].GetScale().InitFromCurrent(0.5f * SPRITE_SCALE, 0.2f);
				Parameters[(int)i].GetAlpha().InitFromCurrent(64.0f, 0.2f);
				Parameters[(int)i].SetVisible();
			}

			// Turn on the glow sprites
			// NOTE: The last glow is left off for first-person
			for (EffectType i = EffectType.EndCap1; i < (EffectType.EndCap1 + NUM_ENDCAP_SPRITES); i++)
				Parameters[(int)i].SetVisible();

			// Create our beams
			BasePlayer pOwner = ToBasePlayer(GetOwner())!;
			BaseEntity? pBeamEnt = pOwner.GetViewModel();

			Beams[0].Init(LookupAttachment("fork1t"), 1, pBeamEnt, true);
			Beams[1].Init(LookupAttachment("fork2t"), 1, pBeamEnt, true);

			Beams[0].SetVisible();
			Beams[1].SetVisible();
		}
		else {
			// Scale up the center sprite
			Parameters[(int)EffectType.Core].GetScale().InitFromCurrent(14.0f, 0.2f);
			Parameters[(int)EffectType.Core].GetAlpha().InitFromCurrent(255.0f, 0.1f);
			Parameters[(int)EffectType.Core].SetVisible();

			// Prepare for scale up
			Parameters[(int)EffectType.Blast].SetVisible(false);

			// Turn on the glow sprites
			for (EffectType i = EffectType.Glow1; i < (EffectType.Glow1 + NUM_GLOW_SPRITES); i++) {
				Parameters[(int)i].GetScale().InitFromCurrent(0.5f * SPRITE_SCALE, 0.2f);
				Parameters[(int)i].GetAlpha().InitFromCurrent(64.0f, 0.2f);
				Parameters[(int)i].SetVisible();
			}

			// Turn on the glow sprites
			for (EffectType i = EffectType.EndCap1; i < (EffectType.EndCap1 + NUM_ENDCAP_SPRITES); i++)
				Parameters[(int)i].SetVisible();

			// Setup the beams
			Beams[0].Init(LookupAttachment("fork1t"), 1, this, false);
			Beams[1].Init(LookupAttachment("fork2t"), 1, this, false);
			Beams[2].Init(LookupAttachment("fork3t"), 1, this, false);

			// Set them visible
			Beams[0].SetVisible();
			Beams[1].SetVisible();
			Beams[2].SetVisible();
		}
#endif
	}
	public void DoEffectClosed() {
#if CLIENT_DLL
		for (int i = (int)EffectType.EndCap1; i < ((int)EffectType.EndCap1 + NUM_ENDCAP_SPRITES); i++)
			Parameters[i].SetVisible(false);
#endif
	}
	public void DoEffectNone() {
#if CLIENT_DLL
		Parameters[(int)EffectType.Core].SetVisible(false);
		Parameters[(int)EffectType.Blast].SetVisible(false);

		for (int i = (int)EffectType.Glow1; i < ((int)EffectType.Glow1 + NUM_GLOW_SPRITES); i++) {
			Parameters[i].SetVisible(false);
		}

		// Turn on the glow sprites
		for (int i = (int)EffectType.EndCap1; i < ((int)EffectType.EndCap1 + NUM_ENDCAP_SPRITES); i++) {
			Parameters[i].SetVisible(false);
		}

		Beams[0].SetVisible(false);
		Beams[1].SetVisible(false);
		Beams[2].SetVisible(false);
#endif
	}
	public void DoEffectLaunch(Vector3 pos) {
#if CLIENT_DLL
		//Turn on the blast sprite and scale
		Parameters[(int)EffectType.Blast].GetScale().Init(8.0f, 64.0f, 0.1f);
		Parameters[(int)EffectType.Blast].GetAlpha().Init(255.0f, 0.0f, 0.2f);
		Parameters[(int)EffectType.Blast].SetVisible();
#endif
	}
	public void DoEffectReady() {
#if CLIENT_DLL
		// Special POV case
		if (ShouldDrawUsingViewModel()) {
			//Turn on the center sprite
			Parameters[(int)EffectType.Core].GetScale().InitFromCurrent(14.0f, 0.2f);
			Parameters[(int)EffectType.Core].GetAlpha().InitFromCurrent(128.0f, 0.2f);
			Parameters[(int)EffectType.Core].SetVisible();
		}
		else {
			//Turn off the center sprite
			Parameters[(int)EffectType.Core].GetScale().InitFromCurrent(8.0f, 0.2f);
			Parameters[(int)EffectType.Core].GetAlpha().InitFromCurrent(0.0f, 0.2f);
			Parameters[(int)EffectType.Core].SetVisible();
		}

		// Turn on the glow sprites
		for (int i = (int)EffectType.Glow1; i < ((int)EffectType.Glow1 + NUM_GLOW_SPRITES); i++) {
			Parameters[i].GetScale().InitFromCurrent(0.4f * SPRITE_SCALE, 0.2f);
			Parameters[i].GetAlpha().InitFromCurrent(64.0f, 0.2f);
			Parameters[i].SetVisible();
		}

		// Turn on the glow sprites
		for (int i = (int)EffectType.EndCap1; i < ((int)EffectType.EndCap1 + NUM_ENDCAP_SPRITES); i++) {
			Parameters[i].SetVisible(false);
		}
#endif
	}

	public void DetachObject(bool playSound = true, bool wasLaunched = false) {
		// todo
	}

	public override bool Deploy() {
		CloseElements();
		DoEffect(EffectState_t.Ready);

		bool @return = base.Deploy();

		NextSecondaryAttack = NextPrimaryAttack = gpGlobals.CurTime;
		BasePlayer? owner = ToBasePlayer(GetOwner());
		if (owner != null)
			owner.SetNextAttack(gpGlobals.CurTime);

		return @return;
	}

	public void ForceDrop() {
		CloseElements();
		DetachObject();
		StopEffects();
	}

	public override bool CanHolster() {
		if (Active)
			return false;

		return base.CanHolster();
	}

	public override bool Holster(BaseCombatWeapon switchingTo) {
		if (Active)
			return false;

		ForceDrop();
		DestroyEffects();
		return base.Holster(switchingTo);
	}

	public bool ResetOwnerEntity;
	public TimeUnit_t ElementDebounce;
	public TimeUnit_t CheckSuppressTime;
	public TimeUnit_t RepuntObjectTime;
	public bool LastDenySoundPlayed;
	public InButtons Attack2Debounce;

	public void CheckForTarget() {

	}

	public enum ElementState
	{
		None,
		Open,
		Closed
	}

	public ElementState ChangeState;

	public override void ItemPostFrame() {
		BasePlayer? owner = ToBasePlayer(GetOwner());
		if (owner == null) {
			// We found an object. Debounce the button
			Attack2Debounce = 0;
			return;
		}

		//Check for object in pickup range
		if (Active == false) {
			CheckForTarget();

			if ((ElementDebounce < gpGlobals.CurTime) && (ChangeState != ElementState.None)) {
				if (ChangeState == ElementState.Open)
					OpenElements();
				else if (ChangeState == ElementState.Closed)
					CloseElements();

				ChangeState = ElementState.None;
			}
		}

		// NOTE: Attack2 will be considered to be pressed until the first item is picked up.
		InButtons nAttack2Mask = owner.Buttons & (~Attack2Debounce);
		if ((nAttack2Mask & InButtons.Attack2) != 0)
			SecondaryAttack();
		else {
			// Reset our debouncer
			LastDenySoundPlayed = false;

			if (Active == false)
				DoEffect(EffectState_t.Ready);
		}

		if ((owner.Buttons & InButtons.Attack2) == 0)
			Attack2Debounce = 0;

		if ((owner.Buttons & InButtons.Attack) != 0)
			PrimaryAttack();
		else
			WeaponIdle();
	}
	public void PrimaryFireEffect() {
		BasePlayer? owner = ToBasePlayer(GetOwner());

		if (owner == null)
			return;

		owner.ViewPunch(new QAngle(-6, SharedRandomInt("physcannonfire", -2, 2), 0));

#if !CLIENT_DLL
		Color white = new(245, 245, 255, 32);
		Util.ScreenFade(owner, white, 0.1f, 0.0f, FadeFlags.In);
#endif

		WeaponSound(Shared.WeaponSound.Single);
	}

	public override void PrimaryAttack() {
		if (NextPrimaryAttack > gpGlobals.CurTime)
			return;

		BasePlayer? owner = ToBasePlayer(GetOwner());

		if (owner == null)
			return;

		if (Active) {
			// Punch the object being held!!
			Vector3 forward;
			owner.EyeVectors(out forward);

			// Validate the item is within punt range
			// BaseEntity? held = m_grabController.GetAttached();
			// Assert(held != NULL);

			// if (held != null) {
			// 	float heldDist = (held.WorldSpaceCenter() - owner.WorldSpaceCenter()).Length();
			// 
			// 	if (heldDist > physcannon_tracelength.GetFloat()) {
			// 		// We can't punt this yet
			// 		DryFire();
			// 		return;
			// 	}
			// }

			// LaunchObject(forward, physcannon_maxforce.GetFloat());

			PrimaryFireEffect();
			SendWeaponAnim(Activity.ACT_VM_SECONDARYATTACK);
			return;
		}

		NextPrimaryAttack = gpGlobals.CurTime + 0.5;
		DryFire();
	}

	public void DryFire(){
		SendWeaponAnim(Activity.ACT_VM_PRIMARYATTACK);

		WeaponSound(Shared.WeaponSound.Empty);
	}

	public override void SecondaryAttack() {
#if !CLIENT_DLL
			// todo
#endif
	}


#if CLIENT_DLL
	public bool IsEffectVisible(EffectType effectID) {
		return Parameters[(int)effectID].IsVisible();
	}

	public void DrawEffectSprite(EffectType effectID) {
		if (!IsEffectVisible(effectID))
			return;

		GetEffectParameters(effectID, out Color color, out float scale, out IMaterial material, out Vector3 attachment);

		if (color.A <= 0)
			return;

		using MatRenderContextPtr renderContext = new(materials);
		renderContext.Bind(material, this);
		DrawSprite(attachment, scale, scale, color);
	}

	private void GetEffectParameters(EffectType effectID, out Color color, out float scale, out IMaterial material, out Vector3 attachment) {
		TimeUnit_t dt = gpGlobals.CurTime;
		float alpha = Parameters[(int)effectID].GetAlpha().Interp(dt);
		scale = Parameters[(int)effectID].GetScale().Interp(dt);
		material = Parameters[(int)effectID].GetMaterial().Get()!;
		color.R = (byte)(int)Parameters[(int)effectID].GetColor().X;
		color.G = (byte)(int)Parameters[(int)effectID].GetColor().Y;
		color.B = (byte)(int)Parameters[(int)effectID].GetColor().Z;
		color.A = (byte)(int)alpha;
		int attachmentIdx = Parameters[(int)effectID].GetAttachment();

		// Format for first-person
		if (ShouldDrawUsingViewModel()) {
			BasePlayer? owner = ToBasePlayer(GetOwner());

			if (owner != null) {
				owner.GetViewModel()!.GetAttachment(attachmentIdx, out attachment, out _);
				// BaseViewModel.FormatViewModelAttachment(ref attachment, true); // << this screws things up, fix later
			}
			else {
				attachment = default;
			}
		}
		else {
			GetAttachment(attachmentIdx, out attachment, out _);
		}
	}

	public override void ClientThink() {
		UpdateElementPosition();
		DoEffectIdle();
	}

	public void DrawEffects() {
		DrawEffectSprite(EffectType.Core);
		DrawEffectSprite(EffectType.Blast);
		for (EffectType i = EffectType.Glow1; i < (EffectType.Glow1 + NUM_GLOW_SPRITES); i++)
			DrawEffectSprite(i);
		for (EffectType i = EffectType.EndCap1; i < (EffectType.EndCap1 + NUM_ENDCAP_SPRITES); i++)
			DrawEffectSprite(i);
	}

	public override int DrawModel(StudioFlags flags) {
		if ((flags & StudioFlags.Transparency) != 0) {
			return 1;
		}
		// TODO: MOVE THIS NEXT LINE UP TO ONLY TRANSPARENCY!!!
		// Engine doesnt support this yet
		DrawEffects();
		return base.DrawModel(flags);
	}

	// This was added to test enginetrace, can remove later
	private void DrawTraceTest() {
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player == null) return;

		MathLib.AngleVectors(player.EyeAngles(), out Vector3 forward);

		Vector3 start = player.EyePosition();
		Vector3 end = start + forward * MAX_TRACE_LENGTH;

		Util.TraceLine(start, end, Mask.Shot, player, Source.CollisionGroup.None, out Trace tr);

		GetEffectParameters(EffectType.Core, out Color color, out float scale, out IMaterial material, out Vector3 attachment);

		if (color.A <= 0)
			return;

		using MatRenderContextPtr renderContext = new(materials);
		renderContext.Bind(material, this);

		float size = scale;
		Vector3 origin = tr.EndPos;
		Vector3 normal = tr.Plane.Normal;

		Vector3 right, up;
		if (MathF.Abs(normal.Z) < 0.9f) 
			MathLib.CrossProduct(in normal, new Vector3(0, 0, 1), out right);
		else 
			MathLib.CrossProduct(in normal, new Vector3(1, 0, 0), out right);
		MathLib.VectorNormalize(ref right);
		MathLib.CrossProduct(in normal, in right, out up);
		MathLib.VectorNormalize(ref up);

		MeshBuilder meshBuilder = new();
		Vector3 point = default;

		IMesh pMesh = renderContext.GetDynamicMesh();
		meshBuilder.Begin(pMesh, MaterialPrimitiveType.Quads, 1);

		meshBuilder.Color4ubv(color);
		meshBuilder.TexCoord2f(0, 0, 1);
		MathLib.VectorMA(origin, -size, up, out point);
		MathLib.VectorMA(point, -size, right, out point);
		meshBuilder.Position3fv(point);
		meshBuilder.AdvanceVertex();

		meshBuilder.Color4ubv(color);
		meshBuilder.TexCoord2f(0, 0, 0);
		MathLib.VectorMA(origin, size, up, out point);
		MathLib.VectorMA(point, -size, right, out point);
		meshBuilder.Position3fv(point);
		meshBuilder.AdvanceVertex();

		meshBuilder.Color4ubv(color);
		meshBuilder.TexCoord2f(0, 1, 0);
		MathLib.VectorMA(origin, size, up, out point);
		MathLib.VectorMA(point, size, right, out point);
		meshBuilder.Position3fv(point);
		meshBuilder.AdvanceVertex();

		meshBuilder.Color4ubv(color);
		meshBuilder.TexCoord2f(0, 1, 1);
		MathLib.VectorMA(origin, -size, up, out point);
		MathLib.VectorMA(point, size, right, out point);
		meshBuilder.Position3fv(point);
		meshBuilder.AdvanceVertex();

		meshBuilder.End();
		pMesh.Draw();
	}

	public override void ViewModelDrawn(BaseViewModel viewmodelflags) {
		DrawEffects();
		DrawTraceTest();
		base.ViewModelDrawn(viewmodelflags);
	}
	public void UpdateElementPosition() {
		BasePlayer? owner = ToBasePlayer(GetOwner());
		float elementPosition = ElementParameter.Interp(gpGlobals.CurTime);

		if (ShouldDrawUsingViewModel()) {
			if (owner != null) {
				BaseViewModel? vm = owner.GetViewModel();
				if (vm != null)
					vm.SetPoseParameter("active", elementPosition);
			}
		}
	}

	public override void OnDataChanged(DataUpdateType type) {
		base.OnDataChanged(type);

		if (type == DataUpdateType.Created) {
			SetNextClientThink(CLIENT_THINK_ALWAYS);
			using C_BaseAnimating.AutoAllowBoneAccess boneaccess = new(true, false);
			StartEffects();
		}

		// Update effect state when out of parity with the server
		if (OldEffectState != EffectState) {
			DoEffect((EffectState_t)EffectState);
			OldEffectState = EffectState;
		}

		// Update element state when out of parity
		if (OldOpen != Open) {
			if (Open)
				ElementParameter.InitFromCurrent(1.0f, 0.2f, InterpType.Spline);
			else
				ElementParameter.InitFromCurrent(0.0f, 0.5f, InterpType.Spline);

			OldOpen = (bool)Open;
		}
	}
#endif

}
#endif
