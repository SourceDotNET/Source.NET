using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;

using System.Numerics;

namespace Source.GUI;

public class ImageBorder : Border
{
	PaintBackgroundType BackgroundType;
	TextureID TextureID;
	bool Tiled;
	string? ImageName;
	bool ShouldPaintFirst;

	public ImageBorder() {
		BackgroundType = PaintBackgroundType.Textured;
		TextureID = Surface.CreateNewTextureID();
	}

	public override bool PaintFirst() => ShouldPaintFirst;

	public virtual void SetImage(ReadOnlySpan<char> imageName) {
		if (!imageName.IsEmpty && imageName.Length > 0) {
			ImageName = "vgui/" + new string(imageName);
			Surface.DrawSetTextureFile(TextureID, ImageName, 1, false);
		}
	}

	public override void Paint(int x, int y, int wide, int tall, Sides breakSide, int breakStart, int breakStop) {
		if (string.IsNullOrEmpty(ImageName))
			return;

		Surface.DrawSetColor(255, 255, 255, 255);
		Surface.DrawSetTexture(TextureID);

		float uvx = 0;
		float uvy = 0;
		float uvw = 1;
		float uvh = 1;
		Vector2 uv11 = new(uvx, uvy);
		Vector2 uv21 = new(uvx + uvw, uvy);
		Vector2 uv22 = new(uvx + uvw, uvy + uvh);
		Vector2 uv12 = new(uvx, uvy + uvh);

		Span<SurfaceVertex> verts = stackalloc SurfaceVertex[4];
		if (Tiled) {
			Surface.DrawGetTextureSize(TextureID, out int imageWide, out int imageTall);

			int y2 = 0;
			while (y2 < tall) {
				int x2 = 0;
				while (x2 < wide) {
					verts[0] = new(new(x, y), uv11);
					verts[1] = new(new(x + imageWide, y), uv21);
					verts[2] = new(new(x + imageWide, y + imageTall), uv22);
					verts[3] = new(new(x, y + imageTall), uv12);

					Surface.DrawTexturedPolygon(verts);

					x2 += imageWide;
				}

				y2 += imageTall;
			}
		}
		else {
			verts[0] = new(new(x, y), uv11);
			verts[1] = new(new(x + wide, y), uv21);
			verts[2] = new(new(x + wide, y + tall), uv22);
			verts[3] = new(new(x, y + tall), uv12);

			Surface.DrawTexturedPolygon(verts);
		}

		Surface.DrawSetTexture(0);
	}

	public override void ApplySchemeSettings(IScheme? scheme, KeyValues inResourceData) {
		BackgroundType = (PaintBackgroundType)inResourceData.GetInt("backgroundtype");
		Tiled = inResourceData.GetInt("tiled") != 0;

		ReadOnlySpan<char> imageName = inResourceData.GetString("image", "");
		SetImage(imageName);

		ShouldPaintFirst = inResourceData.GetInt("paintfirst", 1) != 0;
	}
}
