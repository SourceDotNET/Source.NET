using Source.Common.Bitmap;

using System.Numerics;

namespace Source.Common.Engine;


public struct ViewRect
{
	public int X, Y, Width, Height;
}

/// <summary>
/// General merging of things, differentiates from Source's garbage when managing video modes. Allows arbitrary user data for the video mode
/// </summary>
public struct UserVideoMode
{
	public int Width;
	public int Height;
	public int RefreshRate;
	public bool Windowed;
	public bool Borderless;

	public UserVideoMode(int width, int height) {
		Width = width;
		Height = height;
	}
	public UserVideoMode(int width, int height, bool windowed) {
		Width = width;
		Height = height;
		Windowed = windowed;
	}
	public UserVideoMode(int width, int height, bool windowed, bool borderless) {
		Width = width;
		Height = height;
		Windowed = windowed;
		Borderless = borderless;
	}
	private static int GCD(int a, int b) {
		while (b != 0) {
			int t = b;
			b = a % b;
			a = t;
		}
		return a;
	}
	public static void ProduceAvailableResolutionList(int numerator, int denominator, int monitorWidth, int monitorHeight, List<Vector2> modes) {
		int g = GCD(numerator, denominator);
		int baseW = numerator / g;
		int baseH = denominator / g;

		int maxK = Math.Min(monitorWidth / baseW, monitorHeight / baseH);

		for (int k = 1; k <= maxK; k++) {
			int w = baseW * k;
			int h = baseH * k;

			modes.Add(new(w, h));
		}
	}
}

public interface IVideoMode
{
	bool Init();
	void DrawStartupGraphic();
	bool CreateGameWindow(in UserVideoMode videomode);
	void SetGameWindow(nint window);
	bool SetMode(in UserVideoMode videomode);
	ViewRects GetClientViewRect();
	bool IsWindowedMode();
	bool IsBorderlessMode();
	void TakeSnapshotTGA(ReadOnlySpan<char> filename);
}
