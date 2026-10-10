using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.GarrysMod.Lua;
using Source.Common.GUI;
using Source.Common.Mathematics;
using Source.Common.MaterialSystem;
using Source.Engine;

using System.Numerics;

namespace Game.Client.GarrysMod;

public static partial class LuaCam
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_cam = new("cam");

	static int Start2DCount;
	static int Start3DCount;
	static int OrthoViewCount;

	static ViewSetup StartView;

	public static bool InSurface3D;
	public static void SetInSurface3D(bool value) => InSurface3D = value;

	public static bool FrameStateChecked;
	public static void ResetFrameStateChecked() => FrameStateChecked = false;

	[LuaFunction]
	static int Start(ILuaInterface lua) {
		LuaTable data = new(null, 0);
		data.SetFromStack(1);
		if (!data.isTable()) {
			lua.TypeError("table", 1);
			data.UnReference();
			return 0;
		}

		ResetFrameStateChecked();

		if (data.GetMemberStr("type", "3D")![0] == '2') {
			using MatRenderContextPtr renderContext = new(materials);
			ClientModeShared.SetupVGuiMatrices(true, renderContext);
			((ClientModeShared)clientMode).SetupGModSurface(true);
			renderContext.OverrideAlphaWriteEnable(true, true);
			Start2DCount++;
			data.UnReference();
			return 0;
		}

		StartView = view.GetViewSetup();
		StartView.Origin = data.GetMemberVector("origin", StartView.Origin);
		StartView.Angles = data.GetMemberAngle("angles", StartView.Angles);
		StartView.FOV = data.GetMemberFloat("fov", StartView.FOV);
		StartView.X = (int)data.GetMemberFloat("x", StartView.X);
		StartView.Y = (int)data.GetMemberFloat("y", StartView.Y);
		StartView.Width = (int)data.GetMemberFloat("w", StartView.Width);
		StartView.Height = (int)data.GetMemberFloat("h", StartView.Height);
		StartView.AspectRatio = data.GetMemberFloat("aspect", StartView.AspectRatio);
		StartView.ZNear = data.GetMemberFloat("znear", StartView.ZNear);
		StartView.ZFar = data.GetMemberFloat("zfar", StartView.ZFar);

		bool subrect = StartView.X != 0 || StartView.Y != 0 || StartView.Width != ScreenWidth() || StartView.Height != ScreenHeight();
		StartView.RenderToSubrectOfLargerScreen = subrect;
		StartView.RenderToSubrectOfLargerScreen = data.GetMemberBool("subrect", subrect);

		LuaObject offCenter = new();
		data.GetMember("offcenter", offCenter);
		if (offCenter.isTable()) {
			StartView.OffCenter = true;
			float invWidth = 1.0f / StartView.Width;
			float invHeight = 1.0f / StartView.Height;
			StartView.OffCenterLeft = offCenter.GetMemberFloat("left", StartView.OffCenterLeft) * invWidth;
			StartView.OffCenterRight = offCenter.GetMemberFloat("right", StartView.OffCenterRight) * invWidth;
			StartView.OffCenterTop = offCenter.GetMemberFloat("bottom", StartView.OffCenterTop) * invHeight;
			StartView.OffCenterBottom = offCenter.GetMemberFloat("top", StartView.OffCenterBottom) * invHeight;
		}
		else
			StartView.OffCenter = false;

		LuaObject ortho = new();
		data.GetMember("ortho", ortho);
		if (ortho.isTable()) {
			StartView.Ortho = true;
			StartView.OrthoLeft = ortho.GetMemberFloat("left", StartView.OrthoLeft);
			StartView.OrthoRight = ortho.GetMemberFloat("right", StartView.OrthoRight);
			StartView.OrthoTop = ortho.GetMemberFloat("top", StartView.OrthoTop);
			StartView.OrthoBottom = ortho.GetMemberFloat("bottom", StartView.OrthoBottom);
		}
		else
			StartView.Ortho = false;

		StartView.DoBloomAndToneMapping = data.GetMemberBool("bloomtone", false);

		// todo: poster

		render.Push3DView(in StartView, 0, null, view.GetFrustum(), null);
		Start3DCount++;

		ortho.UnReference();
		offCenter.UnReference();
		data.UnReference();
		return 0;
	}

	[LuaFunction]
	static int End(ILuaInterface lua) {
		if (Start3DCount < 1) {
			lua.ErrorFromLua("cam.End underflow\n");
			return 0;
		}

		Start3DCount--;
		render.PopView(view.GetFrustum());
		return 0;
	}

	[LuaFunction]
	static int End3D(ILuaInterface lua) {
		if (Start3DCount < 1) {
			lua.ErrorFromLua("cam.End3D underflow\n");
			return 0;
		}

		Start3DCount--;
		render.PopView(view.GetFrustum());
		return 0;
	}

	[LuaFunction]
	static int End2D(ILuaInterface lua) {
		if (Start2DCount < 1) {
			lua.ErrorFromLua("cam.End3D underflow\n");
			return 0;
		}

		Start2DCount--;
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.OverrideAlphaWriteEnable(false, false);
		((ClientModeShared)clientMode).SetupGModSurface(false);
		ClientModeShared.SetupVGuiMatrices(false, renderContext);
		return 0;
	}

	[LuaFunction]
	static int StartOrthoView(ILuaInterface lua) {
		float left = (float)lua.CheckNumber(1);
		float top = (float)lua.CheckNumber(2);
		float right = (float)lua.CheckNumber(3);
		float bottom = (float)lua.CheckNumber(4);

		using MatRenderContextPtr renderContext = new(materials);
		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();
		renderContext.Scale(1, -1, 1);
		renderContext.Ortho(left, top, right, bottom, -99999.0, 99999.0);
		OrthoViewCount++;
		return 0;
	}

	[LuaFunction]
	static int EndOrthoView(ILuaInterface lua) {
		if (OrthoViewCount > 0) {
			OrthoViewCount--;
			using MatRenderContextPtr renderContext = new(materials);
			renderContext.MatrixMode(MaterialMatrixMode.Projection);
			renderContext.PopMatrix();
			return 0;
		}

		lua.ErrorFromLua("cam.EndOrthoView underflow\n");
		return 0;
	}

	[LuaFunction]
	static int IgnoreZ(ILuaInterface lua) {
		using MatRenderContextPtr renderContext = new(materials);
		if (lua.GetBool(1))
			renderContext.DepthRange(0, 0.01f);
		else
			renderContext.DepthRange(0, 1);
		return 0;
	}

	static int Start3D2DCount;
	static int ModelMatrixCount;

	[LuaFunction]
	static int Start3D2D(ILuaInterface lua) {
		ref QAngle angles = ref LuaAngle.Get_Angle(2);
		ref Vector3 origin = ref LuaVector.Get_Vector(1);

		Matrix4x4 matrix = default;
		matrix.SetupMatrixOrgAngles(origin, angles);
		float scale = (float)lua.CheckNumber(3);
		MathLib.MatrixBuildScale(out Matrix4x4 scaleMatrix, scale, -scale, 1.0f);
		MathLib.MatrixMultiply(matrix, scaleMatrix, out Matrix4x4 result);

		using MatRenderContextPtr renderContext = new(materials);
		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		renderContext.LoadMatrix(result);
		renderContext.OverrideDepthEnable(true, false);
		renderContext.GMOD_ForceFilterMode(true, 3);
		renderContext.GMOD_ForceFilterMode(false, 3);
		Start3D2DCount++;
		return 0;
	}

	[LuaFunction]
	static int End3D2D(ILuaInterface lua) {
		if (Start3D2DCount < 1) {
			lua.ErrorFromLua("cam.End3D2D underflow\n");
			return 0;
		}

		Start3D2DCount--;
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PopMatrix();
		renderContext.OverrideDepthEnable(false, true);
		renderContext.GMOD_ForceFilterMode(true, 0);
		renderContext.GMOD_ForceFilterMode(false, 0);
		return 0;
	}

	[LuaFunction]
	static int PushModelMatrix(ILuaInterface lua) {
		ref Matrix4x4 matrix = ref LuaVMatrix.Get_VMatrix(1);

		using MatRenderContextPtr renderContext = new(materials);
		Matrix4x4 result;
		if (lua.GetType(2) != LuaType.Nil && lua.GetBool(2)) {
			renderContext.GetMatrix(MaterialMatrixMode.Model, out Matrix4x4 current);
			MathLib.MatrixMultiply(current, matrix, out result);
		}
		else
			result = matrix;

		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		renderContext.LoadMatrix(result);
		ModelMatrixCount++;
		return 0;
	}

	[LuaFunction]
	static int PopModelMatrix(ILuaInterface lua) {
		if (ModelMatrixCount > 0) {
			using MatRenderContextPtr renderContext = new(materials);
			renderContext.MatrixMode(MaterialMatrixMode.Model);
			renderContext.PopMatrix();
			ModelMatrixCount--;
			return 0;
		}

		Warning("cam.PopModelMatrix: Matrix stack is empty!\n");
		return 0;
	}

	[LuaFunction]
	static int GetModelMatrix(ILuaInterface lua) {
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.GetMatrix(MaterialMatrixMode.Model, out Matrix4x4 matrix);
		LuaVMatrix.Push_VMatrix(matrix);
		return 1;
	}

	[LuaFunction]
	static int GetProjectionMatrix(ILuaInterface lua) {
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.GetMatrix(MaterialMatrixMode.Projection, out Matrix4x4 matrix);
		LuaVMatrix.Push_VMatrix(matrix);
		return 1;
	}

	[LuaFunction]
	static int GetViewMatrix(ILuaInterface lua) {
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.GetMatrix(MaterialMatrixMode.View, out Matrix4x4 matrix);
		LuaVMatrix.Push_VMatrix(matrix);
		return 1;
	}

	[LuaFunction]
	static int ApplyShake(ILuaInterface lua) {
		Vector3 origin = LuaVector.Get_Vector(1);
		QAngle angles = LuaAngle.Get_Angle(2);
		float factor = (float)lua.CheckNumber(3);
		vieweffects.CalcShake();
		vieweffects.ApplyShake(ref origin, ref angles, factor);
		LuaVector.Push_Vector(origin);
		LuaAngle.Push_Angle(angles);
		return 2;
	}
}
