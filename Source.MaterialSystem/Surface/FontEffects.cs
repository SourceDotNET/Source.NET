namespace Source.MaterialSystem.Surface;

internal static class FontEffects
{
	internal static void ApplyDropShadowToTexture(int rgbaWide, int rgbaTall, Span<byte> rgba, uint dropShadowOffset) {
		if (dropShadowOffset == 0)
			return;

		for (int y = rgbaTall - 1; y >= dropShadowOffset; y--) {
			for (int x = rgbaWide - 1; x >= dropShadowOffset; x--) {
				Span<byte> dest = rgba[((x + (y * rgbaWide)) * 4)..];
				if (dest[3] == 0) {
					Span<byte> src = rgba[(int)((x - dropShadowOffset + ((y - dropShadowOffset) * rgbaWide)) * 4)..];
					dest[0] = 0;
					dest[1] = 0;
					dest[2] = 0;
					dest[3] = src[3];
				}
			}
		}
	}
	internal static void ApplyGaussianBlurToTexture(int rgbaWide, int rgbaTall, Span<byte> rgba, uint nBlur) {
		if (nBlur == 0)
			return;

		int filterWidth = (int)(nBlur * 2 + 1);
		Span<float> pGaussianDistribution = stackalloc float[filterWidth];
		float sigma = 0.683f * nBlur;
		for (int x = 0; x <= nBlur * 2; x++) {
			int val = x - (int)nBlur;
			pGaussianDistribution[x] = (1.0f / MathF.Sqrt(2 * 3.14f * sigma * sigma)) * MathF.Pow(2.7f, -1.0f * (val * val) / (2 * sigma * sigma));
		}

		int totalBytes = rgbaWide * rgbaTall * 4;
		Span<byte> src = new byte[totalBytes];
		rgba[..totalBytes].CopyTo(src);

		for (int x = 0; x < rgbaWide; x++) {
			int distOffset = 0;
			int nValues = filterWidth;
			int nOffset = 0;
			if (x < nBlur) {
				nOffset += (int)nBlur - x;
				distOffset += nOffset;
				nValues -= nOffset;
			}
			if (x >= rgbaWide - nBlur) {
				nValues = rgbaWide - (x - nOffset);
			}

			for (int y = 0; y < rgbaTall; y++) {
				int readIndex = (y * rgbaWide + x + nOffset - (int)nBlur) * 4 + 3;
				float accum = 0.0f;
				for (int n = 0; n < nValues; n++) {
					accum += pGaussianDistribution[distOffset + n] * src[readIndex + n * 4];
				}
				byte alpha = (byte)accum;

				int dstIndex = (y * rgbaWide + x) * 4;
				rgba[dstIndex + 0] = rgba[dstIndex + 1] = rgba[dstIndex + 2] = alpha > 0 ? (byte)255 : (byte)0;
				rgba[dstIndex + 3] = alpha;
			}
		}

		rgba[..totalBytes].CopyTo(src);

		for (int y = 0; y < rgbaTall; y++) {
			int distOffset = 0;
			int nValues = filterWidth;
			int nOffset = 0;
			if (y < nBlur) {
				nOffset += (int)nBlur - y;
				distOffset += nOffset;
				nValues -= nOffset;
			}
			if (y >= rgbaTall - nBlur) {
				nValues = rgbaTall - (y - nOffset);
			}

			for (int x = 0; x < rgbaWide; x++) {
				int readIndex = ((y + nOffset - (int)nBlur) * rgbaWide + x) * 4 + 3;
				float accum = 0.0f;
				for (int n = 0; n < nValues; n++) {
					accum += pGaussianDistribution[distOffset + n] * src[readIndex + n * 4 * rgbaWide];
				}
				byte alpha = (byte)accum;

				int dstIndex = (y * rgbaWide + x) * 4;
				rgba[dstIndex + 0] = rgba[dstIndex + 1] = rgba[dstIndex + 2] = alpha > 0 ? (byte)255 : (byte)0;
				rgba[dstIndex + 3] = alpha;
			}
		}
	}
	internal static void ApplyScanlineEffectToTexture(int rgbaWide, int rgbaTall, Span<byte> rgba, uint scanLines) {
		if (scanLines < 2)
			return;

		const float scale = 0.7f;

		for (int y = 0; y < rgbaTall; y++) {
			if (y % scanLines == 0)
				continue;

			Span<byte> pBits = rgba[((y * rgbaWide) * 4)..];

			for (int x = 0; x < rgbaWide; x++, pBits = pBits[4..]) {
				pBits[0] = (byte)(pBits[0] * scale);
				pBits[1] = (byte)(pBits[1] * scale);
				pBits[2] = (byte)(pBits[2] * scale);
			}
		}
	}
	internal static void ApplyRotaryEffectToTexture(int rgbaWide, int rgbaTall, Span<byte> rgba, bool rotary) {
		if (!rotary)
			return;

		int y = rgbaTall / 2;

		Span<byte> line = rgba[((y * rgbaWide) * 4)..];

		for (int x = 0; x < rgbaWide; x++, line = line[4..]) {
			line[0] = 127;
			line[1] = 127;
			line[2] = 127;
			line[3] = 255;
		}
	}
	internal static void ApplyOutlineToTexture(int rgbaWide, int rgbaTall, Span<byte> rgba, uint outlineSize) {
		if (outlineSize == 0)
			return;

		int x, y;
		for (y = 0; y < rgbaTall; y++) {
			for (x = 0; x < rgbaWide; x++) {
				Span<byte> src = rgba[((x + (y * rgbaWide)) * 4)..];
				if (src[3] == 0) {
					int shadowX, shadowY;
					for (shadowX = -(int)outlineSize; shadowX <= (int)outlineSize; shadowX++) {
						for (shadowY = -(int)outlineSize; shadowY <= (int)outlineSize; shadowY++) {
							if (shadowX == 0 && shadowY == 0) {
								continue;
							}
							int testX, testY;
							testX = shadowX + x;
							testY = shadowY + y;
							if (testX < 0 || testX >= rgbaWide ||
								testY < 0 || testY >= rgbaTall) {
								continue;
							}
							Span<byte> test = rgba[((testX + (testY * rgbaWide)) * 4)..];
							if (test[0] != 0 && test[1] != 0 && test[2] != 0 && test[3] != 0) {
								src[0] = 0;
								src[1] = 0;
								src[2] = 0;
								src[3] = 255;
							}
						}
					}
				}
			}
		}
	}
}
