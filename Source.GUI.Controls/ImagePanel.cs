using Source.Common;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;

namespace Source.GUI.Controls;

public class ImagePanel : Panel
{
	public static Panel Create_ImagePanel() => new ImagePanel(null, null);

	IImage? Image;
	string? ImageName;
	string? FillColorName;
	string? DrawColorName;
	bool PositionImage;
	bool CenterImage;
	bool ScaleImage;
	bool TileImage;
	bool TileHorizontally;
	bool TileVertically;
	float ScaleAmount;
	Color FillColor;
	Color DrawColor;
	IImageRotation Rotation;
	public ImagePanel(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		Image = null;
		ImageName = null;
		FillColorName = null;
		DrawColorName = null;
		PositionImage = true;
		CenterImage = false;
		ScaleImage = false;
		TileImage = false;
		TileHorizontally = false;
		TileVertically = false;
		ScaleAmount = 0.0f;
		FillColor = new(0, 0, 0, 0);
		DrawColor = new(255, 255, 255, 255);
		Rotation = IImageRotation.Unrotated;

		SetImage(Image);

		RegisterColorAsOverridable(FillColor, "fillcolor_override");
		RegisterColorAsOverridable(DrawColor, "drawcolor_override");
	}

	public void SetImage(IImage? image) {
		Image = image;
		Repaint();
	}

	public void SetImage(ReadOnlySpan<char> imageName) {
		if (!imageName.IsEmpty && ImageName != null && streq(imageName, ImageName))
			return;

		ImageName = new(imageName);
		InvalidateLayout(false, true);
	}

	public IImage? GetImage() => Image;
	public Color GetDrawColor() => DrawColor;
	public void SetDrawColor(Color drawColor) => DrawColor = drawColor;

	public override void PaintBackground() {
		if (FillColor[3] > 0) {
			GetSize(out int wide, out int tall);
			Surface.DrawSetColor(FillColor);
			Surface.DrawFilledRect(0, 0, wide, tall);
		}

		if (Image != null) {
			Image.SetColor(GetDrawColor());
			// Image.SetRotation(Rotation);

			if (PositionImage) {
				if (CenterImage) {
					GetSize(out int wide, out int tall);
					Image.GetSize(out int imgWide, out int imgTall);

					if (ScaleImage && ScaleAmount > 0.0f) {
						imgWide = (int)(imgWide * ScaleAmount);
						imgTall = (int)(imgTall * ScaleAmount);
					}

					Image.SetPos((wide - imgWide) / 2, (tall - imgTall) / 2);
				}
				else
					Image.SetPos(0, 0);
			}

			if (ScaleImage) {
				Image.GetSize(out int imgWide, out int imgTall);
				if (ScaleAmount > 0.0f) {
					imgWide = (int)(imgWide * ScaleAmount);
					imgTall = (int)(imgTall * ScaleAmount);
					Image.SetSize(imgWide, imgTall);
				}

				Image.Paint();
				Image.SetSize(imgWide, imgTall);
			}
			else if (TileImage || TileHorizontally || TileVertically) {
				GetSize(out int wide, out int tall);
				Image.GetSize(out int imgWide, out int imgTall);

				int y = 0;
				while (y < tall) {
					int x = 0;
					while (x < wide) {
						Image.SetPos(x, y);
						Image.Paint();
						x += imgWide;

						if (!TileVertically)
							break;
					}

					y += imgTall;

					if (!TileHorizontally)
						break;
				}

				if (PositionImage)
					Image.SetPos(0, 0);
			}
			else {
				Image.SetColor(GetDrawColor());
				Image.Paint();
			}
		}
	}

	public override void GetSettings(KeyValues outResourceData) {
		base.GetSettings(outResourceData);

		if (ImageName != null)
			outResourceData.SetString("image", ImageName);

		if (FillColorName != null)
			outResourceData.SetString("fillcolor", FillColorName);

		if (DrawColorName != null)
			outResourceData.SetString("drawcolor", DrawColorName);

		if (GetBorder() != null)
			outResourceData.SetString("border", GetBorder()!.GetName());

		outResourceData.SetInt("positionImage", PositionImage ? 1 : 0);
		outResourceData.SetInt("scaleImage", ScaleImage ? 1 : 0);
		outResourceData.SetFloat("scaleAmount", ScaleAmount);
		outResourceData.SetInt("tileImage", TileImage ? 1 : 0);
		outResourceData.SetInt("tileHorizontally", TileHorizontally ? 1 : 0);
		outResourceData.SetInt("tileVertically", TileVertically ? 1 : 0);
	}

	public override void ApplySettings(KeyValues resourceData) {
		ImageName = null;
		FillColorName = null;
		DrawColorName = null;

		PositionImage = resourceData.GetInt("positionImage", 1) != 0;
		ScaleImage = resourceData.GetInt("scaleImage", 0) != 0;
		ScaleAmount = resourceData.GetFloat("scaleAmount", 0.0f);
		TileImage = resourceData.GetInt("tileImage", 0) != 0;
		TileHorizontally = resourceData.GetInt("tileHorizontally", TileImage ? 1 : 0) != 0;
		TileVertically = resourceData.GetInt("tileVertically", TileImage ? 1 : 0) != 0;
		Rotation = (IImageRotation)resourceData.GetInt("rotation", (int)IImageRotation.Unrotated);

		ReadOnlySpan<char> imageName = resourceData.GetString("image", "");
		if (imageName.Length > 0)
			SetImage(imageName);

		ReadOnlySpan<char> fillColor = resourceData.GetString("fillcolor", "");
		if (fillColor.Length > 0) {
			int r = 0, g = 0, b = 0, a = 255;
			FillColorName = new(fillColor);
			if (new ScanF(fillColor, "%d %d %d %d").ReadOrKeep(ref r).ReadOrKeep(ref g).ReadOrKeep(ref b).ReadOrKeep(ref a).ReadArguments >= 3)
				FillColor = new(r, g, b, a);
			else
				FillColor = GetScheme()!.GetColor(fillColor, new(0, 0, 0, 0));
		}

		ReadOnlySpan<char> drawColor = resourceData.GetString("drawcolor", "");
		if (drawColor.Length > 0) {
			int r = 255, g = 255, b = 255, a = 255;
			DrawColorName = new(drawColor);
			if (new ScanF(drawColor, "%d %d %d %d").ReadOrKeep(ref r).ReadOrKeep(ref g).ReadOrKeep(ref b).ReadOrKeep(ref a).ReadArguments >= 3)
				DrawColor = new(r, g, b, a);
			else
				DrawColor = GetScheme()!.GetColor(drawColor, new(255, 255, 255, 255));
		}

		ReadOnlySpan<char> border = resourceData.GetString("border", "");
		if (border.Length > 0)
			SetBorder(GetScheme()!.GetBorder(border));

		base.ApplySettings(resourceData);
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		if (ImageName != null && ImageName.Length > 0)
			SetImage(SchemeManager.GetImage(ImageName, ScaleImage));
	}

	public void SetShouldScaleImage(bool scale) => ScaleImage = scale;
	public bool GetShouldScaleImage() => ScaleImage;

	public void SetScaleAmount(float scale) => ScaleAmount = scale;
	public float GetScaleAmount() => ScaleAmount;

	public void SetFillColor(Color color) => FillColor = color;
	public Color GetFillColor() => FillColor;

	public ReadOnlySpan<char> GetImageName() => ImageName;

	public bool EvictImage() {
		return false; // todo
	}

	public int GetNumFrames() {
		return 0; // todo
	}

	public void SetFrame(int frame) {

	}
}
