using Game.Shared.GarrysMod;

using Source;
using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.GarrysMod.Lua;
using Source.Common.GUI;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.GUI.Controls;

using System.Numerics;

namespace Game.Client.GarrysMod;

public class SpawnIconRenderer
{
	const float DefaultCamYaw = 190.0f;
	const float DefaultCamFOV = 20.0f;
	const float ZNear = 0.1f;
	const float ZFar = 16192.0f;
	const float AmbientLight = 0.3f;
	const float SharpenAmount = 0.25f;
	const int MaxLights = 4;
	const int LightboxSides = 6;
	const int IntRenderParm = 10;
	const int DefaultLightType = 1;
	const float DefaultAngularFalloff = 5.0f;
	const float DefaultConeAngle = 45.0f;
	const float DefaultFiftyPercentDistance = 64.0f;
	const float DefaultZeroPercentDistance = 128.0f;

	static readonly ConVar spawnicon_sharpen = new("spawnicon_sharpen", "4", 0);
	static readonly IMaterialSystemHardwareConfig HardwareConfig = Singleton<IMaterialSystemHardwareConfig>();

	static readonly List<SpawnIconRenderer> Renderers = [];
	static readonly Vector3[] DefaultLightbox = [
		new(1.3f, 1.3f, 1.3f),
		new(0.2f, 0.2f, 0.2f),
		new(0.2f, 0.2f, 0.2f),
		new(0.2f, 0.2f, 0.2f),
		new(2.3f, 2.3f, 2.3f),
		new(0.1f, 0.1f, 0.1f),
	];
	static readonly Vector3 White = new(1, 1, 1);
	static ITexture? Cubemap;

	public string ModelName = "";
	public string BodyGroups = "";
	public string MaterialPath = "";
	public int Skin;
	public int Wide;
	public int Tall;
	public bool Finished;
	public bool Abort;
	public Vector3 CamPos;
	public QAngle CamAng;
	public Vector3 CustomPos;
	public QAngle CustomAng;
	public float CustomFOV;
	public bool CustomCamera;
	public BaseHandle Entity;
	public int LightCount;
	public InlineArray4<LightDesc> Lights;
	public bool UseLightbox;
	public InlineArray6<Vector3> Lightbox;

	public SpawnIconRenderer(string modelName, string spawnIconPath, int skin, string bodyGroups, int wide, int tall, LuaObject? camera) {
		Wide = (int)BitOperations.RoundUpToPowerOf2((uint)wide);
		Tall = (int)BitOperations.RoundUpToPowerOf2((uint)tall);
		ModelName = modelName;
		MaterialPath = "materials\\" + spawnIconPath;
		Skin = skin;
		BodyGroups = bodyGroups;

		Renderers.Add(this);

		CustomCamera = false;
		CamPos = default;
		CamAng = new(0, DefaultCamYaw, 0);
		Entity = new(Constants.INVALID_EHANDLE_INDEX);
		UseLightbox = false;
		LightCount = 0;

		if (camera == null)
			return;

		CustomCamera = true;
		C_BaseEntity? ent = (C_BaseEntity?)camera.GetMemberEntity("ent", null);
		Entity = ent == null ? new(Constants.INVALID_EHANDLE_INDEX) : ent.GetRefEHandle();
		CustomAng = camera.GetMemberAngle("cam_ang", CamAng);
		CustomPos = camera.GetMemberVector("cam_pos", CamPos);
		CustomFOV = camera.GetMemberFloat("cam_fov", DefaultCamFOV);

		C_BaseEntity? handleEnt = GetEntity();
		if (handleEnt != null) {
			CamAng = handleEnt.GetAbsAngles();
			CamPos = handleEnt.GetAbsOrigin();
		}

		LuaObject lights = new();
		camera.GetMember("lights", lights);
		if (lights.isTable()) {
			for (int i = 1; i < MaxLights + 1; i++) {
				LuaObject light = new();
				lights.GetMember(i, light);
				if (!light.isTable()) {
					light.UnReference();
					break;
				}

				ParseLight(ref Lights[i - 1], light);
				LightCount++;
				light.UnReference();
			}
		}

		LuaObject lightbox = new();
		camera.GetMember("lightbox", lightbox);
		if (lightbox.isTable()) {
			UseLightbox = true;
			for (int i = 1; i < LightboxSides + 1; i++) {
				LuaObject side = new();
				lightbox.GetMember(i, side);
				Lightbox[i - 1] = side.isVector() ? side.GetVector() : default;
				side.UnReference();
			}
		}

		lightbox.UnReference();
		lights.UnReference();
	}

	static void ParseLight(ref LightDesc desc, LuaObject light) {
		desc.Type = (LightType)Math.Clamp(light.GetMemberInt("type", DefaultLightType), 0, 3);
		desc.Color = light.GetMemberVector("color", vec3_origin);
		desc.Position = light.GetMemberVector("pos", vec3_origin);
		desc.Direction = light.GetMemberVector("dir", vec3_origin);
		desc.Range = light.GetMemberFloat("range", 0);
		desc.Falloff = light.GetMemberFloat("angularFalloff", DefaultAngularFalloff);
		desc.Theta = (float)(light.GetMemberFloat("innerAngle", DefaultConeAngle) * (Math.PI / 180.0));
		desc.Phi = (float)(light.GetMemberFloat("outerAngle", DefaultConeAngle) * (Math.PI / 180.0));

		if (!light.MemberIsNil("fiftyPercentDistance") && !light.MemberIsNil("zeroPercentDistance"))
			desc.SetupNewStyleAttenuation(light.GetMemberFloat("fiftyPercentDistance", DefaultFiftyPercentDistance), light.GetMemberFloat("zeroPercentDistance", DefaultZeroPercentDistance));
		else
			desc.SetupOldStyleAttenuation(light.GetMemberFloat("quadraticFalloff", 0), light.GetMemberFloat("linearFalloff", 0), light.GetMemberFloat("constantFalloff", 1.0f));

		desc.RecalculateDerivedValues();
	}

	public void Remove() => Renderers.Remove(this);

	C_BaseEntity? GetEntity() => (C_BaseEntity?)cl_entitylist.GetClientEntityFromHandle(Entity);

	public static void RenderQueued() {
		SpawnIcon.ResetQueueTime();
		if (Renderers.Count == 0)
			return;

		Cubemap = materials.FindTexture(HardwareConfig.GetHDREnabled() ? "editor/cubemap.hdr" : "editor/cubemap", null, true);

		SpawnIconRenderer[] queued = [.. Renderers];
		if (queued.Length != 0) {
			queued[0].Render();
			Renderers.Remove(queued[0]);
		}

		Cubemap = null;
	}

	C_BaseFlex? CreateEntity() {
		C_BaseFlex ent = new();

		if (LuaUtil.UTIL_GetModelIndex(ModelName) < 1) {
			if (ent.InitializeAsClientEntity(null, RenderGroup.TranslucentEntity)) {
				Model? model = modelinfo.FindOrLoadModel(ModelName);
				ent.SetModelPointer(model);

				StudioHeader? hdr = modelinfo.GetStudiomodel(model);
				if (hdr == null || ((strcmp(hdr.GetName(), "error.mdl") != 0 || ModelName == "models/error.mdl") && hdr.NumBodyParts >= 1))
					return SetupEntity(ent);
			}
		}
		else if (ent.InitializeAsClientEntity(ModelName, RenderGroup.TranslucentEntity))
			return SetupEntity(ent);

		ent.Release();
		return null;
	}

	C_BaseFlex SetupEntity(C_BaseFlex ent) {
		for (int i = 0; i < BodyGroups.Length; i++) {
			char c = BodyGroups[i];
			int value = -1;
			if ((uint)(c - '0') < 10)
				value = c - '0';
			if ((uint)(c - 'a') < 26)
				value = c - 'a' + 10;
			if ((uint)(c - 'A') < 26)
				value = c - 'A' + 10;
			if (value >= 0)
				ent.SetBodygroup(i, value);
		}

		ent.Skin = Skin;
		ent.Spawn();
		return ent;
	}

	void PositionSpawnIcon(C_BaseEntity ent, ref ViewSetup view) {
		C_BasePlayer.GetLocalPlayer()!.EyeVectors(out _);
		ent.SetAbsAngles(CamAng);
		ent.SetAbsOrigin(CamPos);

		if (!CustomCamera) {
			if (g_Lua != null && g_Lua.Global() != null) {
				LuaObject func = new();
				g_Lua.Global().GetMember("PositionSpawnIcon", func);
				if (func.isFunction()) {
					func.Push();
					LuaEntity.Push_Entity(ent);
					g_Lua.PushVector(CamPos);

					LuaObject ret = new();
					if (g_Lua.CallInternalGet(2, ret) && ret.isTable()) {
						view.FOV = ret.GetMemberFloat("fov", view.FOV);
						view.Origin = ret.GetMemberVector("origin", view.Origin);
						view.Angles = ret.GetMemberAngle("angles", view.Angles);
					}
					ret.UnReference();
				}
				func.UnReference();
			}

			if (!CustomCamera)
				return;
		}

		view.Origin = CustomPos;
		view.Angles = CustomAng;
		view.FOV = CustomFOV;
	}

	void Render() {
		C_BaseEntity? ent = GetEntity();
		if (ent == null) {
			C_BaseFlex? flex = CreateEntity();
			if (flex == null) {
				Finished = true;
				return;
			}

			flex.AddEffects(EntityEffects.NoDraw | EntityEffects.NoInterp);

			C_BaseAnimating? animating = flex.GetBaseAnimating();
			if (animating != null) {
				int sequence = animating.LookupSequence("WalkUnarmed_all");
				if (sequence > 0 || (sequence = animating.LookupSequence("walk_all_moderate")) > 0)
					animating.SetSequence(sequence);

				animating.InvalidateBoneCache();
				animating.FrameAdvance(0);
				animating.StudioFrameAdvance();

				if (!stristr(ModelName, "\\spy.mdl").IsEmpty && Skin >= 4)
					animating.SetBodygroup(1, 1);
			}

			ent = flex;
		}
		else {
			StudioHeader? hdr = modelinfo.GetStudiomodel(ent.GetModel());
			if (hdr != null && strcmp(hdr.GetName(), "error.mdl") == 0 && ModelName != "models/error.mdl") {
				Finished = true;
				return;
			}
		}

		ViewSetup viewSetup = view.GetViewSetup();
		PositionSpawnIcon(ent, ref viewSetup);

		viewSetup.X = 0;
		viewSetup.Y = 0;
		viewSetup.Width = Math.Min(Wide * 2, ScreenWidth());
		viewSetup.Height = Math.Min(Tall * 2, ScreenHeight());
		viewSetup.RenderToSubrectOfLargerScreen = true;
		viewSetup.OffCenter = false;
		viewSetup.Ortho = false;
		viewSetup.DoBloomAndToneMapping = false;
		viewSetup.CacheFullSceneState = false;
		viewSetup.AspectRatio = (float)viewSetup.Width / viewSetup.Height;
		viewSetup.ZNear = ZNear;
		viewSetup.ZFar = ZFar;

		C_BaseAnimating? entAnimating = ent.GetBaseAnimating();
		if (entAnimating != null)
			entAnimating.OverrideViewTarget = viewSetup.Origin;

		Frustum frustum = new();
		using MatRenderContextPtr renderContext = new(materials);

		render.Push3DView(viewSetup, 0, null, frustum, null);
		modelrender.SuppressEngineLighting(false);
		renderContext.BindLocalCubemap(Cubemap);
		renderContext.SetLightingOrigin(vec3_origin);
		renderContext.SetAmbientLight(AmbientLight, AmbientLight, AmbientLight);
		modelrender.SuppressEngineLighting(true);
		render.SetColorModulation(White);
		render.SetBlend(1.0f);

		Span<Vector3> lightbox = stackalloc Vector3[LightboxSides];
		if (UseLightbox)
			((ReadOnlySpan<Vector3>)Lightbox).CopyTo(lightbox);
		else
			DefaultLightbox.CopyTo(lightbox);
		studiorender.SetAmbientLightColors(lightbox);

		if (LightCount < 1)
			studiorender.SetLocalLights(0, null);
		else
			studiorender.SetLocalLights(LightCount, ((ReadOnlySpan<LightDesc>)Lights)[..LightCount]);

		renderContext.SetIntRenderingParameter(IntRenderParm, 0);
		renderContext.DepthRange(0.0f, 1.0f);

		int size = viewSetup.Width * viewSetup.Height * 4;
		byte[] red = new byte[size];
		byte[] green = new byte[size];
		byte[] blue = new byte[size];

		renderContext.ClearColor4ub(255, 0, 0, 0);
		renderContext.ClearBuffers(true, true);
		RenderPass(red, ent, renderContext, viewSetup.Width, viewSetup.Height);

		renderContext.ClearColor4ub(0, 255, 0, 0);
		renderContext.ClearBuffers(true, true);
		RenderPass(green, ent, renderContext, viewSetup.Width, viewSetup.Height);

		renderContext.ClearColor4ub(0, 0, 255, 0);
		renderContext.ClearBuffers(true, true);
		RenderPass(blue, ent, renderContext, viewSetup.Width, viewSetup.Height);

		CombinePasses(red, green, blue, viewSetup.Width, viewSetup.Height);

		string dir = MaterialPath;
		Bootil.String.File.StripFilename(ref dir);
		filesystem.CreateDirHierarchy(dir, "DEFAULT_WRITE_PATH");

		get.Resources()!.SavePNG(viewSetup.Width, viewSetup.Height, red, MaterialPath, Wide, Tall);

		modelrender.SuppressEngineLighting(false);
		renderContext.BindLocalCubemap(null);
		render.PopView(frustum);

		if (ent != GetEntity())
			ent.Release();

		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.SpawniconGenerated)) {
			g_Lua!.PushString(ModelName);
			g_Lua.PushString(MaterialPath);
			g_Lua.PushNumber(Renderers.Count);
			gGM.CallNoReturns(3);
		}

		Finished = true;
		if (Abort)
			Remove();
	}

	void RenderPass(Span<byte> pixels, C_BaseEntity ent, IMatRenderContext renderContext, int width, int height) {
		C_BaseEntity? handleEnt = GetEntity();
		if (handleEnt != null)
			ent = handleEnt;

		if (ent.UsesPowerOfTwoFrameBufferTexture() || ent.UsesFullFrameBufferTexture())
			UpdateRefractTexture(0, 0, width, height, true);

		ent.DrawModel(StudioFlags.Render);
		renderContext.ReadPixels(0, 0, width, height, pixels, ImageFormat.BGRA8888);
	}

	static int Clamp255(int value) => value < 0 ? 0 : value > 255 ? 255 : value;

	static void CombinePasses(Span<byte> red, Span<byte> green, Span<byte> blue, int width, int height) {
		const float Inv255 = 1.0f / 255.0f;
		const float Inv510 = 1.0f / 510.0f;
		const float Half = 127.5f;

		for (int i = 0; i < width * height * 4; i += 4) {
			int rB = red[i], rG = red[i + 1], rR = red[i + 2];
			int gB = green[i], gG = green[i + 1], gR = green[i + 2];
			int bB = blue[i], bG = blue[i + 1], bR = blue[i + 2];

			float alphaR = Math.Clamp(rR * Inv255 - (bR + gR) * Inv510, 0.0f, 1.0f);
			float alphaG = Math.Clamp(gG * Inv255 - (bG + rG) * Inv510, 0.0f, 1.0f);
			float alphaB = Math.Clamp(bB * Inv255 - (rB + gB) * Inv510, 0.0f, 1.0f);
			float background = alphaG * alphaR * alphaB;
			float add = background * Half;

			byte r = (byte)Clamp255((int)((gR + bR) / 2 + add));
			byte g = (byte)Clamp255((int)((bG + rG) / 2 + add));
			byte b = (byte)Clamp255((int)(((rB + gB) >> 1) + add));
			byte a = (byte)Clamp255((int)((1.0f - background) * 255.0f));

			red[i] = green[i] = b;
			red[i + 1] = green[i + 1] = g;
			red[i + 2] = green[i + 2] = r;
			red[i + 3] = green[i + 3] = a;
		}

		float sharpen = spawnicon_sharpen.GetFloat();
		if (sharpen == 0.0f)
			return;

		int pitch = width * 4;
		for (int y = 0; y < height; y++) {
			for (int x = 0; x < width; x++) {
				int i = y * pitch + x * 4;
				if (x != 0 && y != 0 && x < width - 1 && y < height - 1) {
					for (int c = 0; c < 4; c++)
						red[i + c] = (byte)Sharpen(green, pitch, x, y, c, red[i + c], sharpen);
				}
			}
		}
	}

	static int Sharpen(ReadOnlySpan<byte> src, int pitch, int x, int y, int channel, int value, float amount) {
		int left = src[y * pitch + (x - 1) * 4 + channel];
		int right = src[y * pitch + (x + 1) * 4 + channel];
		int up = src[(y - 1) * pitch + x * 4 + channel];
		int down = src[(y + 1) * pitch + x * 4 + channel];

		int sum = (int)((int)((int)((up - value) * amount) + (int)((int)((left - value) * amount) - (int)((right - value) * amount))) - (int)((down - value) * amount));
		return Clamp255((int)(sum * SharpenAmount + value));
	}
}

public class SpawnIcon : Panel
{
	const double QueueBudget = 0.1;

	static readonly ConVar spawnicon_queue = new("spawnicon_queue", "0", 0, "Enables experimental spawnicon loading queue, which prevents the game from freezing when opening large spawnlists.");

	static TextureID GeneratingTexture = -1;
	static TextureID BrokenTexture = -1;
	static TextureID MaterialTexture = -1;
	static double QueueTime;

	string SpawnIconPath = "";
	string BodyGroups = "";
	string ModelName = "";
	int Skin;
	SpawnIconRenderer? Renderer;
	bool Failed;
	IMaterial? Material;

	public static void ResetQueueTime() => QueueTime = 0;

	public SpawnIcon(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		SetSize(64, 64);

		if (GeneratingTexture == -1) {
			GeneratingTexture = surface.DrawGetTextureId("vgui/spawnmenu/generating");
			if (GeneratingTexture < 0) {
				GeneratingTexture = surface.CreateNewTextureID(true);
				surface.DrawSetTextureFile(GeneratingTexture, "vgui/spawnmenu/generating", 0, false);
			}
		}

		if (BrokenTexture == -1) {
			BrokenTexture = surface.DrawGetTextureId("vgui/spawnmenu/broken");
			if (BrokenTexture < 0) {
				BrokenTexture = surface.CreateNewTextureID(true);
				surface.DrawSetTextureFile(BrokenTexture, "vgui/spawnmenu/broken", 0, false);
			}
		}

		SetPaintBorderEnabled(false);
	}

	public override void Dispose() {
		if (Renderer != null) {
			Renderer.Remove();
			Renderer = null;
		}

		if (Material != null) {
			Material.DecrementReferenceCount();
			Material = null;
		}

		base.Dispose();
	}

	public override void OnSizeChanged(int newWide, int newTall) {
		base.OnSizeChanged(newWide, newTall);
		if (ModelName.Length != 0 && !Failed)
			SetModel(ModelName, Skin, BodyGroups);
	}

	public override void Paint() {
		if (!Failed && Material != null && Renderer == null) {
			if (MaterialTexture == -1)
				MaterialTexture = surface.CreateNewTextureID(false);
			surface.DrawSetTextureMaterial(MaterialTexture, Material);
		}
		else
			surface.DrawSetTexture(Failed ? BrokenTexture : GeneratingTexture);

		surface.DrawSetColor(255, 255, 255, 255);
		surface.DrawTexturedRect(0, 0, GetWide(), GetTall());
	}

	public override void OnThink() {
		base.OnThink();

		if ((Material != null && Renderer == null) || Failed)
			return;

		if (Material == null && Renderer == null && SpawnIconPath.Length != 0) {
			if (spawnicon_queue.GetInt() == 0)
				StartRender();
			else if (gpGlobals.AbsoluteFrameTime + QueueTime < QueueBudget || QueueTime == 0.0) {
				double start = Platform.Time;
				StartRender();
				QueueTime += Platform.Time - start;
			}
		}

		if (Renderer == null || !Renderer.Finished)
			return;

		Renderer.Remove();
		Renderer = null;

		if (Material == null) {
			Material = get.Resources()!.FindMaterial(SpawnIconPath, "", true, false, false);
			if (Material != null)
				Material.IncrementReferenceCount();
			else
				Failed = true;
			return;
		}

		string path = SpawnIconPath;
		Bootil.String.File.ExtractFilename(ref path);
		Bootil.String.File.StripExtension(ref path);
		string cmd = "mat_reloadmaterial " + path + "\n";
		if (!cmd.Contains(';'))
			engine.ClientCmd(cmd);
	}

	void StartRender() {
		Material = get.Resources()!.FindMaterial(SpawnIconPath, "", true, false, false);
		if (Material != null) {
			Material.IncrementReferenceCount();
			return;
		}

		RebuildSpawnIcon();
		if (Renderer == null)
			Failed = true;
	}

	public virtual void SetModel(string modelName, int skin, string bodyGroups) {
		Skin = skin;
		ModelName = modelName;
		Bootil.String.Lower(ref ModelName);
		Bootil.String.File.FixSlashes(ref ModelName, "\\", "/");

		BodyGroups = bodyGroups;
		string bodyGroupSuffix = "";
		if (BodyGroups.Length == 9) {
			for (int i = 0; i < BodyGroups.Length; i++) {
				if (BodyGroups[i] != '0') {
					bodyGroupSuffix = "_" + BodyGroups;
					break;
				}
			}
		}

		string sizeSuffix = "";
		if (GetWide() != 64 || GetTall() != 64) {
			int wide = 32;
			for (int i = 5; wide < GetWide();) {
				if (--i == 0)
					break;
				wide <<= 1;
			}

			int tall = 32;
			for (int i = 5; tall < GetTall();) {
				if (--i == 0)
					break;
				tall *= 2;
			}

			if (tall != 64 || wide != 64) {
				if (wide == tall)
					sizeSuffix = $"_{wide}";
				else
					sizeSuffix = $"_{wide}x{tall}";
			}
		}

		SpawnIconPath = ModelName;
		Bootil.String.File.StripExtension(ref SpawnIconPath);

		if (Skin < 1)
			SpawnIconPath = "spawnicons\\" + SpawnIconPath + bodyGroupSuffix + sizeSuffix + ".png";
		else
			SpawnIconPath = "spawnicons\\" + SpawnIconPath + "_skin" + Skin.ToString() + bodyGroupSuffix + sizeSuffix + ".png";

		Failed = false;
		if (Renderer != null) {
			Renderer.Abort = true;
			Renderer = null;
		}

		if (Material != null) {
			Material.DecrementReferenceCount();
			Material = null;
		}

		Material = get.Resources()!.FindMaterial(SpawnIconPath, "", true, false, false);
		Material?.IncrementReferenceCount();
	}

	public virtual void SetSpawnIcon(string path) {
		Skin = 0;
		ModelName = "";
		SpawnIconPath = path;
		Bootil.String.Lower(ref SpawnIconPath);
		Bootil.String.File.FixSlashes(ref SpawnIconPath, "\\", "/");

		if (Bootil.String.Test.EndsWith(SpawnIconPath, ".png")) {
			Material = get.Resources()!.FindMaterial(SpawnIconPath, "", true, false, false);
			if (Material != null) {
				Material.IncrementReferenceCount();
				return;
			}
		}

		SpawnIconPath = "";
	}

	public virtual void RebuildSpawnIcon() {
		if (Renderer == null && ModelName.Length != 0) {
			Renderer = new SpawnIconRenderer(ModelName, SpawnIconPath, Skin, BodyGroups, GetWide(), GetTall(), null);
			Failed = false;
		}
	}

	public virtual void RebuildSpawnIconEx(LuaObject camera) {
		if (Renderer == null && ModelName.Length != 0) {
			Renderer = new SpawnIconRenderer(ModelName, SpawnIconPath, Skin, BodyGroups, GetWide(), GetTall(), camera);
			Failed = false;
		}
	}
}
