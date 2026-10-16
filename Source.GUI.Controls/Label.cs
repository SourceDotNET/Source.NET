using CommunityToolkit.HighPerformance;

using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod.Lua;
using Source.Common.GUI;

namespace Source.GUI.Controls;

public class Label : Panel
{
	public static Panel Create_Label() => new Label(null, null, "Label");

	protected Alignment ContentAlignment;
	protected ColorState TextColorState;
	protected string? FontOverrideName;
	protected bool Wrap;
	protected bool CenterWrap;
	protected bool AutoWideToContents;
	protected bool UseProportionalInsets;
	protected bool AutoWideDirty;

	protected TextImage? TextImage;

	public Alignment GetContentAlignment() => ContentAlignment;

	public Color ExpensiveShadowColor;
	public int ExpensiveShadowDistance;

	public Label(Panel? parent, ReadOnlySpan<char> panelName, ReadOnlySpan<char> text) : base(parent, panelName) {
		Init();

		TextImage = new TextImage(text);
		TextImage.SetColor(new(0, 0, 0, 0));
		SetText(text);
		TextImageIndex = AddImage(TextImage, 0);

		RegisterColorAsOverridable(DisabledFgColor2, "disabledfgcolor2_override");
	}

	public override bool RequestInfo(KeyValues outputData) {
		if (0 == stricmp(outputData.Name, "GetText")) {
			Span<char> buf = stackalloc char[256];
			TextImage!.GetText(buf);
			outputData.SetString("text", buf);
			return true;
		}

		return base.RequestInfo(outputData);
	}

	public nint AddImage(IImage? textImage, int offset) {
		nint newImage = Images.Count;
		Images.Add(new() {
			Image = textImage,
			Offset = (short)offset,
			XPos = -1,
			Width = -1
		});
		InvalidateLayout();
		return newImage;
	}

	public override void PerformLayout() {
		GetSize(out int wide, out _);
		wide -= TextInsetX;

		int twide, ttall;
		if (Images.Count == 1 && TextImageIndex == 0) {
			if (Wrap || CenterWrap) {
				TextImage!.GetContentSize(out twide, out ttall);
				TextImage!.SetSize(wide, ttall);
			}
			else {
				TextImage!.GetContentSize(out twide, out ttall);

				if (wide < twide)
					TextImage!.SetSize(wide, ttall);
				else
					TextImage!.SetSize(twide, ttall);
			}

			HandleAutoSizing();
			HandleAutoSizing();

			return;
		}

		if (TextImageIndex < 0)
			return;

		int widthOfImages = 0;
		Span<ImageInfo> images = Images.AsSpan();
		for (int i = 0; i < images.Length; i++) {
			ref ImageInfo imageInfo = ref images[i];
			IImage? image = imageInfo.Image;
			if (image == null)
				continue;

			if (i == TextImageIndex)
				continue;

			image.GetSize(out int iWide, out int iTall);
			widthOfImages += iWide;
		}

		int spaceAvail = wide - widthOfImages;

		if (spaceAvail < 0)
			return;

		TextImage!.GetSize(out _, out ttall);
		TextImage!.SetSize(spaceAvail, ttall);

		HandleAutoSizing();
	}

	private void HandleAutoSizing() {
		if (AutoWideDirty) {
			AutoWideDirty = false;

			GetContentSize(out int wide, out int tall);
			SetSize(wide, GetTall());
		}
	}

	void Init() {
		ContentAlignment = Alignment.West;
		TextColorState = ColorState.Normal;
		TextInsetX = 0;
		TextInsetY = 0;
		FontOverrideName = null;
		Wrap = false;
		CenterWrap = false;
		AutoWideToContents = false;
		UseProportionalInsets = false;
		AutoWideDirty = false;
	}

	Color DisabledFgColor1;
	Color DisabledFgColor2;

	public const int Content = 8;
	int TextInsetX, TextInsetY;

	public virtual void GetTextInset(out int xInset, out int yInset) {
		xInset = TextInsetX;
		yInset = TextInsetY;
	}
	public virtual void SetTextInset(int xInset, int yInset) {
		TextInsetX = xInset;
		TextInsetY = yInset;

		GetSize(out int wide, out int tall);
		TextImage!.SetDrawWidth(wide - TextInsetX);
	}
	public virtual Color GetDisabledFgColor1() => DisabledFgColor1;
	public virtual Color GetDisabledFgColor2() => DisabledFgColor2;
	public virtual void SetDisabledFgColor1(in Color c) => DisabledFgColor1 = c;
	public virtual void SetDisabledFgColor2(in Color c) => DisabledFgColor2 = c;
	ColorState ColorState;
	public virtual void SetTextColorState(ColorState state) => ColorState = state;
	struct ImageInfo
	{
		public IImage? Image;
		public short Offset;
		public short XPos;
		public short Width;
	}

	public virtual void SetFont(IFont? font) {
		TextImage!.SetFont(font);
		Repaint();
	}
	public virtual IFont? GetFont() => TextImage!.GetFont();


	public void SetContentAlignment(Alignment alignment) {
		ContentAlignment = alignment;
	}

	public virtual void SizeToContents() {
		GetContentSize(out int wide, out int tall);
		SetSize(wide, tall);
	}

	public virtual void GetContentSize(out int wide, out int tall) {
		if (GetFont() == null) {
			IScheme? scheme = GetScheme();
			if (scheme != null) {
				SetFont(scheme.GetFont("Default", IsProportional()));
			}
		}

		ComputeAlignment(out int tx0, out int ty0, out int tx1, out int ty1);

		wide = (tx1 - tx0) + TextInsetX;

		TextImage!.GetSize(out int iWide, out _);
		wide -= iWide;
		TextImage.GetContentSize(out iWide, out int iTall);
		wide += iWide;

		foreach (var i in Images)
			wide += i.Offset;

		tall = Math.Max((ty1 - ty0) + TextInsetY, iTall);
	}
	public virtual void GetText(Span<char> textOut) => TextImage!.GetText(textOut);

#if GMOD_DLL
	public override void SetText(ReadOnlySpan<char> text) {
#else
	public virtual void SetText(ReadOnlySpan<char> text) {
#endif
		if (text.IsEmpty)
			text = "";

		TextImage!.SetText(text);
		AutoWideDirty = AutoWideToContents;
		InvalidateLayout();
		Repaint();
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		if (FontOverrideName != null)
			SetFont(scheme.GetFont(FontOverrideName, IsProportional()));

		if (GetFont() == null)
			SetFont(scheme.GetFont("Default", IsProportional()));

		int wide, tall;
		if (Wrap || CenterWrap) {
			GetSize(out wide, out tall);
			wide -= TextInsetX;
			TextImage!.SetSize(wide, tall);

			TextImage!.RecalculateNewLinePositions();
		}
		else {
			TextImage!.GetContentSize(out wide, out tall);
			TextImage!.SetSize(wide, tall);
		}

		if (AutoWideToContents) {
			AutoWideDirty = true;
			HandleAutoSizing();
		}

		// clear out any the images, since they will have been invalidated
		Span<ImageInfo> images = Images.AsSpan();
		for (int i = 0; i < images.Length; i++) {
			IImage? image = images[i].Image;
			if (image == null)
				continue; // skip over null images

			if (i == TextImageIndex)
				continue;

			ref ImageInfo imageInfo = ref images[i];
			imageInfo.Image = null;
		}

		SetDisabledFgColor1(GetSchemeColor("Label.DisabledFgColor1", scheme));
		SetDisabledFgColor2(GetSchemeColor("Label.DisabledFgColor2", scheme));
		SetBgColor(GetSchemeColor("Label.BgColor", scheme));

		switch (TextColorState) {
			case ColorState.Dull:
				SetFgColor(GetSchemeColor("Label.TextDullColor", scheme));
				break;
			case ColorState.Bright:
				SetFgColor(GetSchemeColor("Label.TextBrightColor", scheme));
				break;
			case ColorState.Normal:
			default:
				SetFgColor(GetSchemeColor("Label.TextColor", scheme));
				break;
		}
#if GMOD_DLL
		if (PushLuaHook(LUA_POOLEDSTRING.ApplySchemeSettings)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.CallInternalNoReturns(1);
		}
#endif
	}

	nint TextImageIndex;

	public void ClearImages() {
		Images.Clear();
		TextImageIndex = -1;
	}

	public override void ApplySettings(KeyValues resourceData) {
		base.ApplySettings(resourceData);

		ReadOnlySpan<char> labelText = resourceData.GetString("labelText", null);
		if (!labelText.IsEmpty && labelText.Length > 0) {
			if (labelText[0] == '%' && labelText[labelText.Length - 1] == '%') {
				// TODO: What is this exactly
			}
			else
				SetText(labelText);
		}


		ReadOnlySpan<char> alignmentString = resourceData.GetString("textAlignment", "");
		Alignment align = (Alignment)(-1);

		if (alignmentString.Equals("north-west", StringComparison.OrdinalIgnoreCase))
			align = Alignment.Northwest;
		else if (alignmentString.Equals("north", StringComparison.OrdinalIgnoreCase))
			align = Alignment.North;
		else if (alignmentString.Equals("north-east", StringComparison.OrdinalIgnoreCase))
			align = Alignment.Northeast;
		else if (alignmentString.Equals("west", StringComparison.OrdinalIgnoreCase))
			align = Alignment.West;
		else if (alignmentString.Equals("center", StringComparison.OrdinalIgnoreCase))
			align = Alignment.Center;
		else if (alignmentString.Equals("east", StringComparison.OrdinalIgnoreCase))
			align = Alignment.East;
		else if (alignmentString.Equals("south-west", StringComparison.OrdinalIgnoreCase))
			align = Alignment.Southwest;
		else if (alignmentString.Equals("south", StringComparison.OrdinalIgnoreCase))
			align = Alignment.South;
		else if (alignmentString.Equals("south-east", StringComparison.OrdinalIgnoreCase))
			align = Alignment.Southeast;

		if (align != (Alignment)(-1))
			SetContentAlignment(align);


		ReadOnlySpan<char> associateName = resourceData.GetString("associate", "");
		if (associateName.Length > 0) {
			// todo
		}

		if (resourceData.GetInt("dulltext", 0) == 1)
			SetTextColorState(ColorState.Dull);
		else if (resourceData.GetInt("brighttext", 0) == 1)
			SetTextColorState(ColorState.Bright);
		else
			SetTextColorState(ColorState.Normal);


		ReadOnlySpan<char> overrideFont = resourceData.GetString("font", "");
		IScheme scheme = GetScheme()!;

		if (overrideFont.Length > 0) {
			FontOverrideName = new(overrideFont.SliceNullTerminatedString());
			SetFont(scheme.GetFont(FontOverrideName, IsProportional()));
		}
		else if (FontOverrideName != null) {
			FontOverrideName = null;
			SetFont(scheme.GetFont("Default", IsProportional()));
		}

		bool bWrapText = resourceData.GetInt("centerwrap", 0) > 0;
		SetCenterWrap(bWrapText);

		AutoWideToContents = resourceData.GetInt("auto_wide_tocontents", 0) > 0;

		bWrapText = resourceData.GetInt("wrap", 0) > 0;
		SetWrap(bWrapText);

		int inset_x = resourceData.GetInt("textinsetx", TextInsetX);
		int inset_y = resourceData.GetInt("textinsety", TextInsetY);

		UseProportionalInsets = resourceData.GetInt("use_proportional_insets", 0) > 0;
		if (UseProportionalInsets)
			inset_x = SchemeManager.GetProportionalScaledValueEx(GetScheme()!, inset_x);

		SetTextInset(inset_x, inset_y);

		bool bAllCaps = resourceData.GetInt("allcaps", 0) > 0;
		SetAllCaps(bAllCaps);

		InvalidateLayout(true);
	}
	bool AllCaps;
	public void SetWrap(bool wrap) {
		Wrap = wrap;
		TextImage!.SetWrap(Wrap);
		InvalidateLayout();
	}
	public void SetCenterWrap(bool wrap) {
		CenterWrap = wrap;
		TextImage!.SetCenterWrap(CenterWrap);
		InvalidateLayout();
	}
	public void SetAllCaps(bool allcaps) {
		AllCaps = allcaps;
		TextImage!.SetAllCaps(AllCaps);
		InvalidateLayout();
	}
	public override void Paint() {
		ComputeAlignment(out int tx0, out int ty0, out int tx1, out int ty1);

		// TODO: Research what associates are in vgui

		GetSize(out int labelWide, out int labelTall);
		int x = tx0, y = TextInsetY + ty0;
		int imageYPos = 0;

		Span<ImageInfo> images = Images.AsSpan();
		for (int i = 0; i < images.Length; i++) {
			ref ImageInfo imageInfo = ref images[i];
			IImage? image = imageInfo.Image;
			if (image == null)
				continue;

			x += imageInfo.Offset;

			if (i == TextImageIndex) {
				switch (ContentAlignment) {
					case Alignment.Northwest:
					case Alignment.West:
					case Alignment.Southwest:
						x += TextInsetX;
						break;

					case Alignment.Northeast:
					case Alignment.East:
					case Alignment.Southeast:
						x -= TextInsetX;
						break;
				}
			}

			if (imageInfo.XPos >= 0) {
				x = imageInfo.XPos;
			}

			imageYPos = y;
			image.SetPos(x, y);

			if (ContentAlignment == Alignment.West || ContentAlignment == Alignment.Center || ContentAlignment == Alignment.East) {
				image.GetSize(out int iw, out int it);
				if (it < (ty1 - ty0)) {
					imageYPos = ((ty1 - ty0) - it) / 2 + y;
					image.SetPos(x, ((ty1 - ty0) - it) / 2 + y);
				}
			}

			if (imageInfo.Width >= 0) {
				image.GetSize(out int w, out int t);
				if (w > imageInfo.Width)
					image.SetSize(imageInfo.Width, t);
			}

			if (image == TextImage) {
				if (IsEnabled()) {
					// TODO: Associates...
					TextImage.SetColor(GetFgColor());
					TextImage.Paint();
				}
				else {
					TextImage.SetPos(x + 1, imageYPos + 1);
					TextImage.SetColor(DisabledFgColor1);
					TextImage.Paint();

					Surface.DrawFlushText();
					TextImage.SetPos(x, imageYPos);
					TextImage.SetColor(DisabledFgColor2);
					TextImage.Paint();
				}
			}
			else
				image.Paint();

			image.GetSize(out int wide, out int tall);
			x += wide;
		}
	}

	public void DrawDashedLine(int x0, int y0, int x1, int y1, int dashLen, int gapLen) {
		if (x1 - x0 > y1 - y0) {
			while (true) {
				if (x0 + dashLen > x1)
					Surface.DrawFilledRect(x0, y0, x1, y1);
				else
					Surface.DrawFilledRect(x0, y0, x0 + dashLen, y1);

				x0 += dashLen;

				if (x0 + gapLen >= x1)
					break;

				x0 += gapLen;
			}
		}
		else {
			while (true) {
				if (y0 + dashLen > y1)
					Surface.DrawFilledRect(x0, y0, x1, y1);
				else
					Surface.DrawFilledRect(x0, y0, x1, y0 + dashLen);

				y0 += dashLen;

				if (y0 + gapLen >= y1)
					break;

				y0 += gapLen;
			}
		}
	}

	List<ImageInfo> Images = [];

	private void ComputeAlignment(out int tx0, out int ty0, out int tx1, out int ty1) {
		GetPaintSize(out int wide, out int tall);
		int tWide, tTall;

		tx0 = 0;
		ty0 = 0;

		int maxX = 0, maxY = 0;

		Alignment actualXAlignment = ContentAlignment;
		Span<ImageInfo> images = Images.AsSpan();
		for (int i = 0; i < images.Length; i++) {
			ref ImageInfo imageInfo = ref images[i];
			IImage? image = imageInfo.Image;
			if (image == null)
				continue;

			image.GetSize(out int iWide, out int iTall);
			if (iWide > wide)
				actualXAlignment = Alignment.West;

			maxY = Math.Max(maxY, iTall);
			maxX += iWide;

			maxX += imageInfo.Offset;
		}

		tWide = maxX;
		tTall = maxY;

		switch (actualXAlignment) {
			case Alignment.Northwest:
			case Alignment.West:
			case Alignment.Southwest:
				tx0 = 0;
				break;
			case Alignment.North:
			case Alignment.Center:
			case Alignment.South:
				tx0 = (wide - tWide) / 2;
				break;

			case Alignment.Northeast:
			case Alignment.East:
			case Alignment.Southeast:
				tx0 = wide - tWide;
				break;
		}

		switch (ContentAlignment) {
			case Alignment.Northwest:
			case Alignment.North:
			case Alignment.Northeast:
				ty0 = 0;
				break;

			case Alignment.West:
			case Alignment.Center:
			case Alignment.East:
				ty0 = (tall - tTall) / 2;
				break;

			case Alignment.Southwest:
			case Alignment.South:
			case Alignment.Southeast:
				ty0 = tall - tTall;
				break;
		}

		tx1 = tx0 + tWide;
		ty1 = ty0 + tTall;
	}

	public TextImage? GetTextImage() {
		return TextImage;
	}

	public void ResetToSimpleTextImage() {
		ClearImages();
		TextImageIndex = AddImage(TextImage!, 0);
	}

	public void SetImageAtIndex(int index, IImage? image, int offset) {
		EnsureImageCapacity(index);

		if (Images[index].Image != image || Images[index].Offset != offset) {
			var info = Images[index];
			info.Image = image;
			info.Offset = (short)offset;
			Images[index] = info;
			InvalidateLayout();
		}
	}

	public IImage? GetImageAtIndex(int index) {
		if (index < 0 || index >= Images.Count)
			return null;

		return Images[index].Image;
	}

	public int GetImageCount() => Images.Count;

	public nint SetTextImageIndex(int newIndex) {
		if (newIndex == TextImageIndex)
			return TextImageIndex;

		EnsureImageCapacity(newIndex);

		nint oldIndex = TextImageIndex;

		if (TextImageIndex >= 0) {
			var info = Images[(int)TextImageIndex];
			info.Image = null;
			Images[(int)TextImageIndex] = info;
		}

		if (newIndex >= 0) {
			var info = Images[newIndex];
			info.Image = TextImage;
			Images[newIndex] = info;
		}

		TextImageIndex = newIndex;
		return oldIndex;
	}


	public void EnsureImageCapacity(int maxIndex) {
		while (Images.Count <= maxIndex)
			AddImage(null, 0);
	}

	public void SetImagePreOffset(int index, int offset) {
		if (index < 0 || index >= Images.Count)
			return;

		var info = Images[index];
		info.Offset = (short)offset;
		Images[index] = info;
		InvalidateLayout();
	}

	public void SetImageBounds(nint index, int xPos, int width) {
		var info = Images[(int)index];
		info.XPos = (short)xPos;
		info.Width = (short)width;
		Images[(int)index] = info;
	}
}
