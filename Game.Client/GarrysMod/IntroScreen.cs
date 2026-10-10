using Source;
using Source.Common.GarrysMod;
using Source.Common.GUI;
using Source.Common.MaterialSystem;

namespace Game.Client.GarrysMod;

public class IntroScreen : IIntroScreen
{
	static int Steps;

	IMaterial? Logo;
	IMaterial? Background;
	TextureID TextureID;

	public void Start() {
		Logo = get.Resources()!.FindMaterial("introscreen/main.png", "", true, false, false); // todo, a source.net logo!
		if (Logo != null) {
			Logo.IncrementReferenceCount();
			Logo.GetMappingHeight();
		}

		Background = materials.FindMaterial("console/background01", "VGUI textures", true);
		if (Background != null) {
			Background.IncrementReferenceCount();
			Background.GetMappingHeight();
		}

		TextureID = surface.CreateNewTextureID(false);
	}

	public void End() {
		Logo?.DecrementReferenceCount();
		Logo = null;

		Background?.DecrementReferenceCount();
		Background = null;

		surface.DestroyTextureID(TextureID);
		TextureID = TextureID.INVALID;
	}

	public void Update(ReadOnlySpan<char> status, bool step) {
		if (Logo == null || Background == null)
			return;

		float steps = Steps;
		if (step)
			Steps++;

		float progress = steps * (1.0f / 60.0f);
		if (progress > 1.0f) {
			Msg($"WARNING: STARTUP DELTA IS TOO LOW {Steps}\n");
			progress = 1.0f;
		}

		materials.BeginFrame(0);

		MatRenderContextPtr renderContext = new(materials);
		renderContext.ClearBuffers(true, true, false);
		renderContext.GetViewport(out _, out _, out int width, out int height);

		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();
		renderContext.Scale(1, -1, 1);
		renderContext.Ortho(0.5, 0.5, width + 0.5, height + 0.5, -1.0, 1.0);

		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		DoDraw(ref renderContext, status, width, height, progress);

		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PopMatrix();
		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PopMatrix();
		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PopMatrix();

		renderContext.Dispose();

		materials.EndFrame();
		materials.SwapBuffers();
	}

	public void DoDraw(ref MatRenderContextPtr renderContext, ReadOnlySpan<char> status, int width, int height, float progress) {
		int x = (int)((width - 512) * 0.5f);
		int y = (int)((height - 512) * 0.5f);

		if (Background != null) {
			surface.DrawSetTextureMaterial(TextureID, Background);
			surface.DrawSetTexture(TextureID);
			surface.DrawSetColor(new Color(255, 255, 255, 255));
			surface.DrawTexturedRect(0, 0, width, height);
		}

		if (Logo != null) {
			surface.DrawSetTextureMaterial(TextureID, Logo);
			surface.DrawSetTexture(TextureID);
			surface.DrawSetColor(new Color(255, 255, 255, 255));
			surface.DrawTexturedRect(x, y, x + 512, y + 512);
		}

		surface.DrawSetColor(new Color(50, 200, 60, 255));
		surface.DrawFilledRect(x + 19, y + 491, (int)((x + 20) + progress * 475.0f), y + 501);

		IScheme? scheme = vguiSchemeManager.GetDefaultScheme();
		if (scheme == null)
			return;

		surface.DrawSetTextFont(scheme.GetFont("DefaultSmall", false));
		surface.GetTextSize(scheme.GetFont("DefaultSmall", false), status, out int wide, out _);
		x -= wide / 2;

		surface.DrawSetTextPos(x + 257, y + 443);
		surface.DrawSetTextColor(0, 0, 0, 150);
		surface.DrawPrintText(status);

		surface.DrawSetTextPos(x + 256, y + 442);
		surface.DrawSetTextColor(255, 255, 255, 255);
		surface.DrawPrintText(status);
	}
}
