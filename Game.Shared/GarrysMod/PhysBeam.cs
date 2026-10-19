#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
using Source.Common.Formats.BSP;
#if CLIENT_DLL
using Game.Client.GarrysMod;
using Source.Common.Client;
using Source.Common.Engine;
using Source.Common.GarrysMod.Lua;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

#endif
using Game.Shared;
using Source;
using System.Numerics;

#if CLIENT_DLL
namespace Game.Client;
using FIELD = Source.FIELD<C_PhysBeam>;
#else
namespace Game.Server;
using FIELD = Source.FIELD<PhysBeam>;
#endif

using Table =
#if CLIENT_DLL
	RecvTable;
#else
	SendTable;
#endif

using Class =
#if CLIENT_DLL
	ClientClass;
#else
	ServerClass;
#endif

[LinkEntityToClass("physgun_beam")]
[NetworkName("CPhysBeam")]
public partial class
#if CLIENT_DLL
    C_PhysBeam 
#else
	PhysBeam
#endif
	: BaseEntity
{
	public static readonly Table DT_PhysBeam = new(DT_BaseEntity, [
#if CLIENT_DLL
		RecvPropEHandle(FIELD.OF(nameof(TargetEnt))),
		RecvPropVector(FIELD.OF(nameof(HoldPos))),
		RecvPropBool(FIELD.OF(nameof(IsOn))),
		RecvPropInt(FIELD.OF(nameof(PhysBone))),
#elif GAME_DLL
		SendPropEHandle(FIELD.OF(nameof(TargetEnt))),
		SendPropVector(FIELD.OF(nameof(HoldPos)), 0, PropFlags.NoScale),
		SendPropBool(FIELD.OF(nameof(IsOn))),
		SendPropInt(FIELD.OF(nameof(PhysBone)), 12, 0),
#endif
	]);

	public static readonly new Class
#if CLIENT_DLL
		ClientClass
#else
		ServerClass
#endif
		= new Class(DT_PhysBeam);

	[NetworkName("m_hTargetEnt")]
	public EHANDLE TargetEnt = new();
	[NetworkName("m_HoldPos")]
	public Vector3 HoldPos;
	[NetworkName("m_bIsOn")]
	public bool IsOn;
	[NetworkName("m_iPhysBone")]
	public int PhysBone;

	public override EntityCapabilities ObjectCaps() => (base.ObjectCaps() & (EntityCapabilities)0x7ffffffd) | EntityCapabilities.DontSave;

#if CLIENT_DLL
	readonly MaterialReference Glow1 = new();
	readonly MaterialReference Glow2 = new();
	Model? BeamModel;
	Model? BeamAModel;

	public override bool ShouldDraw() => !IsEffectActive(EntityEffects.NoDraw);
	public override bool IsTransparent() => true;
	public override RenderGroup GetRenderGroup() => RenderGroup.TranslucentEntity;

	public override void OnDataChanged(DataUpdateType updateType) {
		base.OnDataChanged(updateType);
		if (updateType != DataUpdateType.Created)
			return;

		Glow1.Init("sprites/physg_glow1", MaterialDefines.TEXTURE_GROUP_CLIENT_EFFECTS, true);
		Glow2.Init("sprites/physg_glow2", MaterialDefines.TEXTURE_GROUP_CLIENT_EFFECTS, true);
		BeamModel = engine.LoadModel("sprites/physbeam.vmt", false);
		BeamAModel = engine.LoadModel("sprites/physbeama.vmt", false);
	}

	public void UpdateBeam(BaseEntity? target, in Vector3 holdPos, int bone) {
		if (HoldPos != holdPos)
			HoldPos = holdPos;
		if (TargetEnt.Get() != target)
			TargetEnt.Set(target);
		if (PhysBone != bone)
			PhysBone = bone;
	}

	public void SetBeamEnabled(bool on) {
		IsOn = on;
		if (!on)
			UpdateBeam(null, default, 0);
	}

	static bool IsVisibleForBeam(BaseEntity entity) {
		if (!entity.ShouldDraw())
			return false;
		Source.RenderMode mode = entity.GetRenderMode();
		if (mode == Source.RenderMode.None)
			return false;
		if (mode != Source.RenderMode.Normal && entity.GetRenderColor().A == 0)
			return false;
		return true;
	}

	static void NormalizeInPlace(ref Vector3 v) {
		float radius = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
		float iradius = 1.0f / (radius + 1.1920929e-07f);
		v *= iradius;
	}

	static Color PlayerColorToColor(BaseEntity? owner) {
		Color color = new(255, 255, 255, 255);
		if (owner is C_GMOD_Player gmod) {
			Vector3 pc = gmod.GetWeaponColor();
			NormalizeInPlace(ref pc);
			color = new((byte)(int)(pc.X * 255), (byte)(int)(pc.Y * 255), (byte)(int)(pc.Z * 255), 255);
		}
		return color;
	}

	static bool BindSprite(Model? model, IMatRenderContext renderContext) {
		if (model == null || modelinfo.GetModelType(model) != ModelType.Sprite)
			return false;

		EngineSprite? sprite = (EngineSprite?)modelinfo.GetModelExtraData(model);
		IMaterial? material = sprite?.GetMaterial(Source.RenderMode.TransAdd, 0);
		if (material == null)
			return false;

		renderContext.Bind(material);
		return true;
	}

	void DrawGlow(in Vector3 origin, bool hasTarget, in QAngle angles) {
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.Bind(Glow2.Get()!, this);

		MathLib.AngleVectors(angles, out Vector3 forward);
		Color color = PlayerColorToColor(GetOwnerEntity());

		Vector3 pos = origin;
		if (!IsOn) {
			for (int i = 0; i < 3; i++) {
				color.A = (byte)random.RandomInt(120, 255);
				float size = (float)(Math.Sin(gpGlobals.CurTime * 5.0 + i * 15.0) * 8.0 + 48.0);
				BeamDraw.DrawSprite(pos, size, size, color);
				pos += forward * 5.0f;
			}
		}
		else {
			int count = hasTarget ? 7 : 2;
			for (int i = 0; i < count; i++) {
				color.A = (byte)random.RandomInt(100, 255);
				float size = random.RandomFloat(16.0f, 64.0f);
				BeamDraw.DrawSprite(pos, size, size, color);
				pos += forward * 2.0f;
			}
		}
	}

	static void DrawBezierBeam(IMatRenderContext renderContext, in Vector3 p0, in Vector3 p1, in Vector3 p2, float width, in Vector3 color, float scroll) {
		float length = (p0 - p2).Length();
		float scrollBase = (float)(scroll - Math.Floor(scroll));

		BeamSegDraw segDraw = new();
		segDraw.Start(renderContext, 17, null);
		for (int i = 0; i <= 16; i++) {
			float t = i * 0.0625f;
			float omt = 1.0f - t;
			BeamSeg seg = default;
			seg.Pos = p0 * (omt * omt) + p1 * (2 * t * omt) + p2 * (t * t);
			seg.TexCoord = scrollBase - t * length * 0.02f;
			seg.Width = width;
			seg.Alpha = 1.0f;
			seg.Color = (i == 0 || i == 16) ? default : color;
			segDraw.NextSeg(ref seg);
		}
		segDraw.End();
	}

	public override int DrawModel(StudioFlags flags) {
		if (GetOwnerEntity() is not C_BasePlayer player)
			return 0;

		BaseCombatWeapon? weapon = player.GetActiveWeapon();
		if (weapon == null || !weapon.GetClassname().Equals("weapon_physgun", StringComparison.Ordinal))
			return 0;

		bool firstPerson = weapon.ShouldDrawUsingViewModel();
		BaseViewModel? viewModel = null;
		if (!firstPerson) {
			if (!IsVisibleForBeam(weapon) || !IsVisibleForBeam(player))
				return 0;
		}
		else {
			if (CurrentViewID() == ViewID.Sky3D)
				return 0;
			viewModel = player.GetViewModel(0, true);
			if (viewModel == null || !IsVisibleForBeam(viewModel))
				return 0;
		}

		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.DrawPhysgunBeam)) {
			LuaEntity.Push_Entity(player);
			LuaEntity.Push_Entity(weapon);
			g_Lua!.PushBool(IsOn);
			LuaEntity.Push_Entity(TargetEnt.Get());
			g_Lua.PushNumber(PhysBone);
			LuaVector.Push_Vector(HoldPos);
			if (!gGM.CallFinish(6))
				return 0;
		}

		using MatRenderContextPtr renderContext = new(materials);

		weapon.GetAttachment(1, out Vector3 attachOrigin, out QAngle attachAngles);
		if (firstPerson)
			viewModel!.GetAttachment(1, out attachOrigin, out attachAngles);

		if (!IsOn) {
			if (firstPerson)
				renderContext.DepthRange(0, 0.01f);
			DrawGlow(attachOrigin, false, attachAngles);
			if (firstPerson)
				renderContext.DepthRange(0, 1);
			return 1;
		}

		if (!C_WeaponPhysGun.physgun_drawbeams.GetBool())
			return 0;

		Vector3 forward = C_WeaponPhysGun.GetAimForward(player);

		Vector3 end;
		Vector3 mid;
		bool hasTarget = false;
		BaseEntity? target = TargetEnt.Get();
		if (target != null) {
			QAngle targetAngles = target.GetAbsAngles();
			if (targetAngles.X == 0 && targetAngles.Y == 0 && targetAngles.Z == 0)
				end = target.GetAbsOrigin() + HoldPos;
			else
				MathLib.VectorTransform(HoldPos, target.EntityToWorldTransform(), out end);

			if (target.GetSolid() == SolidType.VPhysics) {
				Matrix3x4 boneMatrix = C_WeaponPhysGun.GetPhysBoneMatrix(target, PhysBone);
				MathLib.VectorTransform(HoldPos, boneMatrix, out end);
			}

			float dist = (end - attachOrigin).Length();
			mid = player.Weapon_ShootPosition() + forward * dist * 0.6f;
			hasTarget = true;
		}
		else {
			float range = C_WeaponPhysGun.physgun_maxrange.GetFloat();
			Vector3 shoot = player.Weapon_ShootPosition();
			Vector3 traceEnd = shoot + forward * range * 0.5f;
			Util.TraceLine(shoot, traceEnd, (Mask)0x600400b, player, Source.CollisionGroup.None, out Trace tr);
			end = tr.EndPos;
			mid = tr.EndPos;
		}

		if (firstPerson)
			renderContext.DepthRange(0, 0.01f);

		double timeScale = hasTarget ? 10.0 : -2.0;
		BindSprite(hasTarget ? BeamAModel : BeamModel, renderContext.Context);

		Vector3 beamColor = default;
		if (player is C_GMOD_Player gmodPlayer) {
			beamColor = gmodPlayer.GetWeaponColor();
			NormalizeInPlace(ref beamColor);
		}

		double now = gpGlobals.CurTime;
		DrawBezierBeam(renderContext.Context, attachOrigin, mid, end, 2.0f, beamColor, (float)(timeScale * now));
		DrawBezierBeam(renderContext.Context, end, mid, attachOrigin, random.RandomFloat(2.0f, 5.0f), beamColor, (float)(timeScale * now * 1.1));
		DrawBezierBeam(renderContext.Context, end, mid, attachOrigin, random.RandomFloat(2.0f, 6.0f), beamColor, (float)(timeScale * now * -0.9));

		DrawGlow(attachOrigin, hasTarget, attachAngles);

		renderContext.Bind(Glow1.Get()!, this);
		Color color = PlayerColorToColor(player);
		int endCount = hasTarget ? 7 : 2;
		for (int i = 0; i < endCount; i++) {
			color.A = (byte)random.RandomInt(100, 255);
			float size = random.RandomFloat(1.0f, 16.0f);
			BeamDraw.DrawSprite(end, size, size, color);
		}

		if (firstPerson)
			renderContext.DepthRange(0, 1);

		return 1;
	}
#endif

#if GAME_DLL
	Vector3 EndPos;

	public static PhysBeam? Create(BaseEntity? owner, in Vector3 position) {
		PhysBeam? beam = BaseEntity.Create("physgun_beam", position, default, null) as PhysBeam;
		if (beam == null)
			return null;

		beam.SetMoveType(Source.MoveType.None);
		beam.AddSolidFlags(SolidFlags.NotSolid);
		beam.AddEffects(EntityEffects.NoShadow);
		Util.SetSize(beam, new Vector3(-64), new Vector3(64));
		beam.SetOwnerEntity(owner);
		beam.SetParent(owner, -1);
		beam.AddEFlags(EFL.ForceCheckTransmit);
		return beam;
	}

	public void UpdateBeam(BaseEntity? target, in Vector3 holdPos, int bone) {
		if (HoldPos != holdPos) {
			NetworkStateChanged();
			HoldPos = holdPos;
		}
		if (TargetEnt.Get() != target) {
			NetworkStateChanged();
			TargetEnt.Set(target);
		}
		if (PhysBone != bone) {
			NetworkStateChanged();
			PhysBone = bone;
		}
		if (IsOn)
			ComputeEndPos();
	}

	public void SetBeamEnabled(bool on) {
		if (IsOn != on) {
			NetworkStateChanged();
			IsOn = on;
		}
		if (!on)
			UpdateBeam(null, default, 0);
	}

	void ComputeEndPos() {
		BasePlayer? owner = GetOwnerEntity() as BasePlayer;
		if (owner == null || !owner.IsPlayer() || owner.GetActiveWeapon() == null)
			return;

		BaseEntity? target = TargetEnt.Get();
		if (target != null) {
			EndPos = target.GetAbsOrigin();
			return;
		}

		Vector3 forward = WeaponPhysGun.GetAimForward(owner);
		Vector3 start = owner.Weapon_ShootPosition();
		Vector3 end = start + forward * WeaponPhysGun.physgun_maxrange.GetFloat() * 0.5f;

		Ray ray = default;
		ray.Init(start, end);
		TraceFilterSimple filter = new(owner, Source.CollisionGroup.None);
		enginetrace.TraceRay(ray, (Mask)0x600400b, ref filter, out Trace tr);

		if (r_visualizetraces.GetBool())
			Game.Shared.DebugOverlay.DebugDrawLine(tr.StartPos, tr.EndPos, 255, 0, 0, true, -1.0f);

		EndPos = tr.EndPos;
	}
#endif
}
#endif
