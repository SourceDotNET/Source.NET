global using static Game.Client.ViewScene;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Engine;

using System.Drawing.Drawing2D;

using System.Numerics;
using System.Runtime.CompilerServices;
namespace Game.Client;

[EngineComponent]
public static class ViewScene
{
	public static readonly ConVar r_updaterefracttexture = new("r_updaterefracttexture", "1", FCvar.Cheat);
	public static readonly ConVar r_depthoverlay = new("r_depthoverlay", "0", FCvar.Cheat, "Replaces opaque objects with their grayscaled depth values. r_showz_power scales the output.");

	public static long g_viewscene_refractUpdateFrame = 0;
	public static bool g_bAllowMultipleRefractUpdatesPerScenePerFrame = false;

	public static void UpdateRefractTexture(int x, int y, int w, int h, bool forceUpdate = false) {
		Assert(!DrawingShadowDepthView());

		if (!r_updaterefracttexture.GetBool())
			return;

		using MatRenderContextPtr renderContext = new(materials);
		ITexture? texture = RenderTexture.GetPowerOfTwoFrameBufferTexture();
		// forced or only once per frame 
		System.Drawing.Rectangle rect = new(x, y, w, h);
		renderContext.CopyRenderTargetToTextureEx(texture!, 0, ref rect, ref Unsafe.NullRef<System.Drawing.Rectangle>());

		g_viewscene_refractUpdateFrame = gpGlobals.FrameCount;
		renderContext.SetFrameBufferCopyTexture(texture);
	}

	public static void UpdateRefractTexture(bool forceUpdate = false) {
		Assert(!DrawingShadowDepthView());

		using MatRenderContextPtr renderContext = new(materials);

		renderContext.GetViewport(out int x, out int y, out int w, out int h);
		UpdateRefractTexture(x, y, w, h, forceUpdate);
	}

	public static void UpdateScreenEffectTexture(int textureIndex, int x, int y, int w, int h, bool destFullScreen = false)
		=> UpdateScreenEffectTexture(textureIndex, x, y, w, h, destFullScreen, out _);

	public static void UpdateScreenEffectTexture(int textureIndex, int x, int y, int w, int h, bool destFullScreen, out System.Drawing.Rectangle actualRect) {
		System.Drawing.Rectangle srcRect = new(x, y, w, h);

		using MatRenderContextPtr renderContext = new(materials);
		ITexture texture = RenderTexture.GetFullFrameFrameBufferTexture(textureIndex)!;
		renderContext.GetRenderTargetDimensions(out int srcWidth, out int srcHeight);
		int destWidth = texture.GetActualWidth();
		int destHeight = texture.GetActualHeight();

		System.Drawing.Rectangle destRect = srcRect;
		if (!destFullScreen && (srcWidth > destWidth || srcHeight > destHeight)) {
			// the source and target sizes aren't necessarily the same (specifically in dx7 where 
			// nonpow2 rendertargets aren't supported), so lets figure it out here.
			float scaleX = (float)destWidth / (float)srcWidth;
			float scaleY = (float)destHeight / (float)srcHeight;
			destRect.X = (int)(srcRect.X * scaleX);
			destRect.Y = (int)(srcRect.Y * scaleY);
			destRect.Width = (int)(srcRect.Width * scaleX);
			destRect.Height = (int)(srcRect.Height * scaleY);
			destRect.X = Math.Clamp(destRect.X, 0, destWidth);
			destRect.Y = Math.Clamp(destRect.Y, 0, destHeight);
			destRect.Width = Math.Clamp(destRect.Width, 0, destWidth - destRect.X);
			destRect.Height = Math.Clamp(destRect.Height, 0, destHeight - destRect.Y);
		}

		renderContext.CopyRenderTargetToTextureEx(texture, 0, ref srcRect, ref destFullScreen ? ref Unsafe.NullRef<System.Drawing.Rectangle>() : ref destRect);
		renderContext.SetFrameBufferCopyTexture(texture, textureIndex);

		actualRect = destRect;
	}

	/// <summary>
	/// Draws the screen effect
	/// </summary>
	public static void DrawScreenEffectMaterial(IMaterial material, int x, int y, int w, int h) {
		UpdateScreenEffectTexture(0, x, y, w, h, false, out System.Drawing.Rectangle actualRect);
		ITexture texture = RenderTexture.GetFullFrameFrameBufferTexture(0)!;

		Singleton<RenderUtils>().DrawScreenSpaceRectangle(material, x, y, w, h,
			actualRect.X, actualRect.Y, actualRect.X + actualRect.Width - 1, actualRect.Y + actualRect.Height - 1,
			texture.GetActualWidth(), texture.GetActualHeight(), null, 1, 1, 0);
	}

	/// <summary>
	/// intended for use by dynamic meshes to naively update front buffer textures needed by a material
	/// </summary>
	public static void UpdateFrontBufferTexturesForMaterial(IMaterial material, bool force = false) {
		Assert(!DrawingShadowDepthView());

		if (material.NeedsPowerOfTwoFrameBufferTexture(true))
			UpdateRefractTexture(force);
		else if (material.NeedsFullFrameBufferTexture(true)) {
			ref ViewSetup viewSetup = ref view.GetViewSetup();
			UpdateScreenEffectTexture(0, viewSetup.X, viewSetup.Y, viewSetup.Width, viewSetup.Height);
		}
	}

	public static void UpdateScreenEffectTexture() {
		Assert(!DrawingShadowDepthView());

		ref ViewSetup viewSetup = ref view.GetViewSetup();
		UpdateScreenEffectTexture(0, viewSetup.X, viewSetup.Y, viewSetup.Width, viewSetup.Height);
	}

	public static void ViewTransform(in Vector3 worldSpace, out Vector3 viewSpace) {
		ref readonly Matrix4x4 viewMatrix = ref engine.WorldToViewMatrix();
		MathLib.Vector3DMultiplyPosition(in viewMatrix, in worldSpace, out viewSpace);
	}
	public static bool FrustumTransform(in Matrix4x4 worldToSurface, in Vector3 point, out Vector3 screen) {
		// UNDONE: Clean this up some, handle off-screen vertices
		float w;

		screen.X = worldToSurface[0][0] * point[0] + worldToSurface[0][1] * point[1] + worldToSurface[0][2] * point[2] + worldToSurface[0][3];
		screen.Y = worldToSurface[1][0] * point[0] + worldToSurface[1][1] * point[1] + worldToSurface[1][2] * point[2] + worldToSurface[1][3];
		//	z		 = worldToSurface[2][0] * point[0] + worldToSurface[2][1] * point[1] + worldToSurface[2][2] * point[2] + worldToSurface[2][3];
		w = worldToSurface[3][0] * point[0] + worldToSurface[3][1] * point[1] + worldToSurface[3][2] * point[2] + worldToSurface[3][3];

		// Just so we have something valid here
		screen.Z = 0.0f;

		bool behind;
		if (w < 0.001f) {
			behind = true;
			screen.X *= 100000;
			screen.Y *= 100000;
		}
		else {
			behind = false;
			float invw = 1.0f / w;
			screen.X *= invw;
			screen.Y *= invw;
		}

		return behind;
	}

	public static bool ScreenTransform(in Vector3 point, out Vector3 screen) {
		// UNDONE: Clean this up some, handle off-screen vertices
		return FrustumTransform(engine.WorldToScreenMatrix(), point, out screen);
	}

	public static bool HudTransform(in Vector3 point, out Vector3 screen) {
		if (/*UseVR()*/ false) {
			throw new NotImplementedException(); // todo vr	
												 //return FrustumTransform(g_ClientVirtualReality.GetHudProjectionFromWorld(), point, screen);
		}
		else {
			return FrustumTransform(engine.WorldToScreenMatrix(), point, out screen);
		}
	}

	public static void UpdateFullScreenDepthTexture() {
		// todo
	}
}
