using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.GarrysMod.Lua;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Client.GarrysMod;

public class ProjectedTexture
{
	public Color Color;
	public float Brightness;
	public FlashlightState State;
	public ClientShadowHandle_t Handle;
	public bool LightWorld;
	public EHANDLE Target = new();

	public ProjectedTexture() {
		State = new() {
			SpotlightTextureFrame = 0,
			OrthoRight = 600.0f,
			OrthoBottom = 800.0f,
		};
		State.EnableShadows = LuaProjectedTexture.r_flashlightdepthtexture.GetInt() != 0;
		State.ShadowMapResolution = LuaProjectedTexture.r_flashlightdepthres.GetInt();
		State.ShadowFilterSize = LuaProjectedTexture.r_projectedtexture_filter.GetFloat();
		Color = new(255, 255, 255, 255);
		State.LightOrigin = default;
		State.Orientation = default;
		State.ShadowQuality = 1;
		State.NearZ = 1.0f;
		State.FarZ = 2048.0f;
		State.HorizontalFOVDegrees = 90.0f;
		State.VerticalFOVDegrees = 90.0f;
		State.QuadraticAtten = 0.0f;
		State.LinearAtten = 100.0f;
		State.ConstantAtten = 0.0f;
		State.ShadowSlopeScaleDepthBias = 2.0f;
		State.ShadowDepthBias = 0.0001f;
		LightWorld = true;
		Brightness = 1.0f;
		Target.Set(null);
		State.Color[0] = 1.0f;
		State.Color[1] = 1.0f;
		State.Color[2] = 1.0f;
		State.Color[3] = 1.0f;
		Handle = ClientShadowMgrGlobals.g_ClientShadowMgr.CreateFlashlight(in State);
		LuaProjectedTexture.ProjectedTextures.Add(this);
	}

	public void Remove() {
		if (Handle != CLIENTSHADOW_INVALID_HANDLE)
			ClientShadowMgrGlobals.g_ClientShadowMgr.DestroyFlashlight(Handle);
		Handle = CLIENTSHADOW_INVALID_HANDLE;
		if (State.SpotlightTexture != null) {
			State.SpotlightTexture.DecrementReferenceCount();
			State.SpotlightTexture = null;
		}
	}
}

public static partial class LuaProjectedTexture
{
	public static readonly ConVar r_projectedtexture_filter = new("r_projectedtexture_filter", "1", 0);
	public static ConVar r_flashlightdepthtexture => ClientShadowMgrGlobals.r_flashlightdepthtexture;
	public static ConVar r_flashlightdepthres => ClientShadowMgrGlobals.r_flashlightdepthres;

	public static readonly List<ProjectedTexture> ProjectedTextures = [];

	[LuaClass(typeof(ProjectedTexture), NullError = "Tried to use a NULL ProjectedTexture!")]
	public static readonly LuaClass LC_ProjectedTexture = new("ProjectedTexture", LuaType.ProjectedTexture, null, null);

	public static void RemoveAll() {
		for (int i = 0; i < ProjectedTextures.Count; i++)
			ProjectedTextures[i].Remove();
	}

	static ProjectedTexture? Get_ProjectedTexture(ILuaInterface lua) {
		ProjectedTexture? pt = (ProjectedTexture?)LC_ProjectedTexture.Get(1);
		if (pt == null)
			lua.Error("Tried to use a NULL ProjectedTexture!");
		return pt;
	}

	[LuaGlobal("ProjectedTexture")]
	static int CreateProjectedTexture(ILuaInterface lua) {
		LC_ProjectedTexture.Push(new ProjectedTexture());
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture____gc(ILuaInterface lua) {
		ProjectedTexture? pt = (ProjectedTexture?)LC_ProjectedTexture.Get(1);
		if (pt != null) {
			pt.Remove();
			ProjectedTextures.Remove(pt);
		}
		lua.SetUserType(1, 0);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture____tostring(ILuaInterface lua) {
		lua.PushString("ProjectedTexture");
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__IsValid(ILuaInterface lua) {
		ProjectedTexture? pt = (ProjectedTexture?)LC_ProjectedTexture.Get(1);
		lua.PushBool(pt != null && pt.Handle != CLIENTSHADOW_INVALID_HANDLE);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__Remove(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		pt.Remove();
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__Update(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		if (pt.Handle != CLIENTSHADOW_INVALID_HANDLE) {
			ClientShadowMgrGlobals.g_ClientShadowMgr.UpdateFlashlightState(pt.Handle, in pt.State);
			ClientShadowMgrGlobals.g_ClientShadowMgr.SetFlashlightTarget(pt.Handle, pt.Target);
			ClientShadowMgrGlobals.g_ClientShadowMgr.SetFlashlightLightWorld(pt.Handle, pt.LightWorld);
			ClientShadowMgrGlobals.g_ClientShadowMgr.UpdateProjectedTexture(pt.Handle, true);
		}
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetAngles(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		MathLib.QuaternionAngles(in pt.State.Orientation, out QAngle angles);
		LuaAngle.Push_Angle(angles);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetAngles(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		QAngle angles = LuaAngle.Get_Angle(2);
		MathLib.AngleQuaternion(in angles, out pt.State.Orientation);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetPos(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		LuaVector.Push_Vector(pt.State.LightOrigin);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetPos(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		pt.State.LightOrigin = LuaVector.Get_Vector(2);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetBrightness(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushNumber(pt.Brightness);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetBrightness(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		float brightness = (float)lua.CheckNumber(2);
		pt.Brightness = brightness;
		pt.State.Color[0] = pt.Color.R * 0.003921569f * brightness;
		pt.State.Color[1] = pt.Color.G * 0.003921569f * brightness;
		pt.State.Color[2] = pt.Color.B * 0.003921569f * brightness;
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetColor(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushColor(pt.Color);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetColor(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		Color color = lua.GetColor(2);
		float brightness = pt.Brightness;
		pt.Color = color;
		pt.State.Color[0] = color.R * 0.003921569f * brightness;
		pt.State.Color[1] = color.G * 0.003921569f * brightness;
		pt.State.Color[2] = color.B * 0.003921569f * brightness;
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetConstantAttenuation(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushNumber(pt.State.ConstantAtten);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetConstantAttenuation(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		pt.State.ConstantAtten = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetLinearAttenuation(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushNumber(pt.State.LinearAtten);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetLinearAttenuation(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		pt.State.LinearAtten = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetQuadraticAttenuation(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushNumber(pt.State.QuadraticAtten);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetQuadraticAttenuation(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		pt.State.QuadraticAtten = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetEnableShadows(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushBool(pt.State.EnableShadows);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetEnableShadows(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		if (lua.GetBool(2) && r_flashlightdepthtexture.GetInt() != 0) {
			pt.State.EnableShadows = true;
			return 0;
		}
		pt.State.EnableShadows = false;
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetNearZ(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushNumber(pt.State.NearZ);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetNearZ(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		pt.State.NearZ = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetFarZ(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushNumber(pt.State.FarZ);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetFarZ(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		pt.State.FarZ = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__SetFOV(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		float fov = (float)lua.CheckNumber(2);
		pt.State.HorizontalFOVDegrees = fov;
		pt.State.VerticalFOVDegrees = fov;
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetHorizontalFOV(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushNumber(pt.State.HorizontalFOVDegrees);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetHorizontalFOV(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		pt.State.HorizontalFOVDegrees = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetVerticalFOV(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushNumber(pt.State.VerticalFOVDegrees);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetVerticalFOV(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		pt.State.VerticalFOVDegrees = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetLightWorld(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushBool(pt.LightWorld);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetLightWorld(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		pt.LightWorld = lua.GetBool(2);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetNoCull(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushBool(pt.State.NoCull);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetNoCull(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		pt.State.NoCull = lua.GetBool(2);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetSkipShadowUpdates(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushBool(pt.State.SkipShadowUpdates);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetSkipShadowUpdates(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		pt.State.SkipShadowUpdates = lua.GetBool(2);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetOrthographic(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		if (!pt.State.Ortho) {
			lua.PushBool(false);
			return 1;
		}
		float left = pt.State.OrthoLeft;
		float top = pt.State.OrthoTop;
		float right = pt.State.OrthoRight;
		float bottom = pt.State.OrthoBottom;
		lua.PushBool(true);
		lua.PushNumber(MathF.Abs(left));
		lua.PushNumber(MathF.Abs(top));
		lua.PushNumber(right);
		lua.PushNumber(bottom);
		return 5;
	}

	[LuaMethod]
	static int ProjectedTexture__SetOrthographic(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		if (lua.Top() > 2) {
			double bottom = lua.CheckNumber(6);
			double right = lua.CheckNumber(5);
			double top = lua.CheckNumber(4);
			double left = lua.CheckNumber(3);
			pt.State.Ortho = lua.GetBool(2);
			if (pt.State.Ortho) {
				pt.State.OrthoLeft = -MathF.Abs((float)left);
				pt.State.OrthoTop = -MathF.Abs((float)top);
				pt.State.OrthoRight = MathF.Abs((float)right);
				pt.State.OrthoBottom = MathF.Abs((float)bottom);
			}
			return 0;
		}
		pt.State.Ortho = lua.GetBool(2);
		if (pt.State.Ortho) {
			pt.State.OrthoLeft = -0.0f;
			pt.State.OrthoRight = 0.0f;
			pt.State.OrthoBottom = 0.0f;
			pt.State.OrthoTop = -0.0f;
		}
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetShadowDepthBias(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushNumber(pt.State.ShadowDepthBias);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetShadowDepthBias(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		pt.State.ShadowDepthBias = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetShadowFilter(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushNumber(pt.State.ShadowFilterSize);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetShadowFilter(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		pt.State.ShadowFilterSize = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetShadowSlopeScaleDepthBias(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushNumber(pt.State.ShadowSlopeScaleDepthBias);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetShadowSlopeScaleDepthBias(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		pt.State.ShadowSlopeScaleDepthBias = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetTargetEntity(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		LuaEntity.Push_Entity(pt.Target.Get());
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetTargetEntity(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		BaseEntity? ent = LuaEntity.Get_Entity(2, true);
		if (ent == null) {
			pt.Target.Set(null);
			return 0;
		}
		pt.Target.Set(ent);
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetTexture(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		if (pt.State.SpotlightTexture == null) {
			lua.PushNil();
			return 1;
		}
		LuaTexture.Push(pt.State.SpotlightTexture);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetTexture(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		ITexture? texture;
		if (lua.GetType(2) == LuaType.String)
			texture = materials.FindTexture(lua.CheckString(2), "Other textures", false, 0);
		else
			texture = (ITexture?)LuaTexture.LC_ITexture.Get(2);

		pt.State.SpotlightTexture?.DecrementReferenceCount();
		pt.State.SpotlightTexture = texture;
		texture?.IncrementReferenceCount();

		pt.State.SpotlightTextureFrame = 0;
		if (pt.State.SpotlightTexture != null && pt.State.SpotlightTexture.IsRenderTarget())
			pt.State.SpotlightTextureFrame = 0;
		return 0;
	}

	[LuaMethod]
	static int ProjectedTexture__GetTextureFrame(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		lua.PushNumber(pt.State.SpotlightTextureFrame);
		return 1;
	}

	[LuaMethod]
	static int ProjectedTexture__SetTextureFrame(ILuaInterface lua) {
		ProjectedTexture? pt = Get_ProjectedTexture(lua);
		if (pt == null)
			return 0;
		pt.State.SpotlightTextureFrame = (int)lua.CheckNumber(2);
		if (pt.State.SpotlightTexture != null && pt.State.SpotlightTexture.IsRenderTarget())
			pt.State.SpotlightTextureFrame = 0;
		return 0;
	}
}
