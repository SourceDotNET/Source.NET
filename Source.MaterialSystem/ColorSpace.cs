using CommunityToolkit.HighPerformance;

using Source.Common.Mathematics;

using System.Numerics;

namespace Source.MaterialSystem;

public static class ColorSpace
{
	static float[] textureToLinear = new float[256];  // texture (0..255) to linear (0..1)
	static int[] linearToTexture = new int[1024];   // linear (0..1) to texture (0..255)
	static int[] linearToScreen = new int[1024];    // linear (0..1) to gamma corrected vertex light (0..255)
	static float[] g_LinearToVertex = new float[4096];   // linear (0..4) to screen corrected vertex space (0..1?)
	static int[] linearToLightmap = new int[4096];  // linear (0..4) to screen corrected texture value (0..255)

	public static void LinearToLightmap(Span<byte> pDstRGB, ReadOnlySpan<float> pSrcRGB) {
		Vector3 tmpVect = default;
		int i, j;
		for (j = 0; j < 3; j++) {
			i = MathLib.RoundFloatToInt(pSrcRGB[j] * 1024); // assume 0..4 range
			if (i < 0) {
				i = 0;
			}
			if (i > 4091)
				i = 4091;
			tmpVect[j] = g_LinearToVertex[i];
		}

		MathLib.ColorClamp(ref tmpVect);

		pDstRGB[0] = MathLib.RoundFloatToByte(tmpVect[0] * 255.0f);
		pDstRGB[1] = MathLib.RoundFloatToByte(tmpVect[1] * 255.0f);
		pDstRGB[2] = MathLib.RoundFloatToByte(tmpVect[2] * 255.0f);
	}

	public static unsafe void ColorClampBumped(ref Vector3 color1, ref Vector3 color2, ref Vector3 color3) {
		fixed (Vector3* pColor1 = &color1)
		fixed (Vector3* pColor2 = &color2)
		fixed (Vector3* pColor3 = &color3) {
			Vector3 maxs;
			Vector3** colors = stackalloc Vector3*[3];
			colors[0] = pColor1;
			colors[1] = pColor2;
			colors[2] = pColor3;
			maxs.X = MathLib.VectorMaximum(color1);
			maxs.Y = MathLib.VectorMaximum(color2);
			maxs.Z = MathLib.VectorMaximum(color3);

			Span<int> order = stackalloc int[3];
			if (maxs[0] >= maxs[1] && maxs[1] >= maxs[2]) {
				order[0] = 0;
				order[1] = 1;
				order[2] = 2;
			}
			if (maxs[0] >= maxs[2] && maxs[2] >= maxs[1]) {
				order[0] = 0;
				order[1] = 2;
				order[2] = 1;
			}
			if (maxs[1] >= maxs[0] && maxs[0] >= maxs[2]) {
				order[0] = 1;
				order[1] = 0;
				order[2] = 2;
			}
			if (maxs[1] >= maxs[2] && maxs[2] >= maxs[0]) {
				order[0] = 1;
				order[1] = 2;
				order[2] = 0;
			}
			if (maxs[2] >= maxs[0] && maxs[0] >= maxs[1]) {
				order[0] = 2;
				order[1] = 0;
				order[2] = 1;
			}
			if (maxs[2] >= maxs[1] && maxs[1] >= maxs[0]) {
				order[0] = 2;
				order[1] = 1;
				order[2] = 0;
			}

			int i;
			for (i = 0; i < 3; i++) {
				float max = MathLib.VectorMaximum(*colors[order[i]]);
				if (max <= 1.0f) {
					continue;
				}
				// This channel is too bright. . take half of the amount that we are over and 
				// add it to the other two channel.
				float factorToRedist = (max - 1.0f) / max;
				Vector3 colorToRedist = factorToRedist * *colors[order[i]];
				*colors[order[i]] -= colorToRedist;
				colorToRedist *= 0.5f;
				*colors[order[(i + 1) % 3]] += colorToRedist;
				*colors[order[(i + 2) % 3]] += colorToRedist;
			}

			MathLib.ColorClamp(ref color1);
			MathLib.ColorClamp(ref color2);
			MathLib.ColorClamp(ref color3);

			if (color1.X < 0f) color1.X = 0f;
			if (color1.Y < 0f) color1.Y = 0f;
			if (color1.Z < 0f) color1.Z = 0f;
			if (color2.X < 0f) color2.X = 0f;
			if (color2.Y < 0f) color2.Y = 0f;
			if (color2.Z < 0f) color2.Z = 0f;
			if (color3.X < 0f) color3.X = 0f;
			if (color3.Y < 0f) color3.Y = 0f;
			if (color3.Z < 0f) color3.Z = 0f;
		}
	}
	public static void LinearToBumpedLightmap(
		ReadOnlySpan<float> linearColor, ReadOnlySpan<float> linearBumpColor1,
		ReadOnlySpan<float> linearBumpColor2, ReadOnlySpan<float> linearBumpColor3,
		Span<byte> ret, Span<byte> retBump1,
		Span<byte> retBump2, Span<byte> retBump3) {

		ref readonly Vector3 linearBump1 = ref linearBumpColor1[..3].Cast<float, Vector3>()[0];
		ref readonly Vector3 linearBump2 = ref linearBumpColor2[..3].Cast<float, Vector3>()[0];
		ref readonly Vector3 linearBump3 = ref linearBumpColor3[..3].Cast<float, Vector3>()[0];

		Vector3 gammaGoal;
		// gammaGoal is premultiplied by 1/overbright, which we want
		gammaGoal.X = MathLib.LinearToVertexLight(linearColor[0]);
		gammaGoal.Y = MathLib.LinearToVertexLight(linearColor[1]);
		gammaGoal.Z = MathLib.LinearToVertexLight(linearColor[2]);
		Vector3 bumpAverage = linearBump1;
		bumpAverage += linearBump2;
		bumpAverage += linearBump3;
		bumpAverage *= (1.0f / 3.0f);

		Vector3 correctionScale = default;
		if (const_reinterpret<float, int>(in bumpAverage.X) != 0 && const_reinterpret<float, int>(in bumpAverage.Y) != 0 && const_reinterpret<float, int>(in bumpAverage.Z) != 0)
			MathLib.VectorDivide(gammaGoal, bumpAverage, out correctionScale);
		else {
			correctionScale.Init(0.0f, 0.0f, 0.0f);
			if (bumpAverage[0] != 0.0f) {
				correctionScale[0] = gammaGoal[0] / bumpAverage[0];
			}
			if (bumpAverage[1] != 0.0f) {
				correctionScale[1] = gammaGoal[1] / bumpAverage[1];
			}
			if (bumpAverage[2] != 0.0f) {
				correctionScale[2] = gammaGoal[2] / bumpAverage[2];
			}
		}
		Vector3 correctedBumpColor1;
		Vector3 correctedBumpColor2;
		Vector3 correctedBumpColor3;
		MathLib.VectorMultiply(linearBump1, correctionScale, out correctedBumpColor1);
		MathLib.VectorMultiply(linearBump2, correctionScale, out correctedBumpColor2);
		MathLib.VectorMultiply(linearBump3, correctionScale, out correctedBumpColor3);

		Vector3 check = (correctedBumpColor1 + correctedBumpColor2 + correctedBumpColor3) / 3.0f;

		ColorClampBumped(ref correctedBumpColor1, ref correctedBumpColor2, ref correctedBumpColor3);

		ret[0] = MathLib.RoundFloatToByte(gammaGoal[0] * 255.0f);
		ret[1] = MathLib.RoundFloatToByte(gammaGoal[1] * 255.0f);
		ret[2] = MathLib.RoundFloatToByte(gammaGoal[2] * 255.0f);
		retBump1[0] = MathLib.RoundFloatToByte(correctedBumpColor1[0] * 255.0f);
		retBump1[1] = MathLib.RoundFloatToByte(correctedBumpColor1[1] * 255.0f);
		retBump1[2] = MathLib.RoundFloatToByte(correctedBumpColor1[2] * 255.0f);
		retBump2[0] = MathLib.RoundFloatToByte(correctedBumpColor2[0] * 255.0f);
		retBump2[1] = MathLib.RoundFloatToByte(correctedBumpColor2[1] * 255.0f);
		retBump2[2] = MathLib.RoundFloatToByte(correctedBumpColor2[2] * 255.0f);
		retBump3[0] = MathLib.RoundFloatToByte(correctedBumpColor3[0] * 255.0f);
		retBump3[1] = MathLib.RoundFloatToByte(correctedBumpColor3[1] * 255.0f);
		retBump3[2] = MathLib.RoundFloatToByte(correctedBumpColor3[2] * 255.0f);
	}

	public static ushort LinearFloatToCorrectedShort(float input) {
		input = MathF.Min(input * 4096.0f, 65535.0f);
		return (ushort)MathF.Max(input, 0.0f);
	}

	public static ushort LinearToUnsignedShort(float input, int fractionalBits) {
		input = input * (1 << fractionalBits);
		input = MathF.Min(input, 65535);
		return (ushort)MathF.Max(input, 0.0f);
	}

	public static void ClampToHDR(in Vector3 input, Span<ushort> output) {
		output[0] = LinearFloatToCorrectedShort(input.X);
		output[1] = LinearFloatToCorrectedShort(input.Y);
		output[2] = LinearFloatToCorrectedShort(input.Z);
	}

	public static void LinearToBumpedLightmap(
		ReadOnlySpan<float> linearColor, ReadOnlySpan<float> linearBumpColor1,
		ReadOnlySpan<float> linearBumpColor2, ReadOnlySpan<float> linearBumpColor3,
		out Vector3 ret, out Vector3 retBump1,
		out Vector3 retBump2, out Vector3 retBump3) {

		Vector3 linearUnbumped = linearColor[..3].Cast<float, Vector3>()[0];
		Vector3 linearBump1 = linearBumpColor1[..3].Cast<float, Vector3>()[0];
		Vector3 linearBump2 = linearBumpColor2[..3].Cast<float, Vector3>()[0];
		Vector3 linearBump3 = linearBumpColor3[..3].Cast<float, Vector3>()[0];

		Vector3 bumpAverage = linearBump1;
		bumpAverage += linearBump2;
		bumpAverage += linearBump3;
		bumpAverage *= (1.0f / 3.0f);

		Vector3 correctionScale = default;
		if (const_reinterpret<float, int>(in bumpAverage.X) != 0 && const_reinterpret<float, int>(in bumpAverage.Y) != 0 && const_reinterpret<float, int>(in bumpAverage.Z) != 0)
			MathLib.VectorDivide(linearUnbumped, bumpAverage, out correctionScale);
		else {
			correctionScale.Init(0.0f, 0.0f, 0.0f);
			if (bumpAverage[0] != 0.0f)
				correctionScale[0] = linearUnbumped[0] / bumpAverage[0];
			if (bumpAverage[1] != 0.0f)
				correctionScale[1] = linearUnbumped[1] / bumpAverage[1];
			if (bumpAverage[2] != 0.0f)
				correctionScale[2] = linearUnbumped[2] / bumpAverage[2];
		}
		linearBump1 *= correctionScale;
		linearBump2 *= correctionScale;
		linearBump3 *= correctionScale;

		ret = linearUnbumped;
		retBump1 = linearBump1;
		retBump2 = linearBump2;
		retBump3 = linearBump3;
	}

	public static void LinearToBumpedLightmap(
		ReadOnlySpan<float> linearColor, ReadOnlySpan<float> linearBumpColor1,
		ReadOnlySpan<float> linearBumpColor2, ReadOnlySpan<float> linearBumpColor3,
		Span<ushort> ret, Span<ushort> retBump1,
		Span<ushort> retBump2, Span<ushort> retBump3) {
		LinearToBumpedLightmap(linearColor, linearBumpColor1, linearBumpColor2, linearBumpColor3, out Vector3 linearUnbumped, out Vector3 linearBump1, out Vector3 linearBump2, out Vector3 linearBump3);

		ClampToHDR(in linearUnbumped, ret);
		ClampToHDR(in linearBump1, retBump1);
		ClampToHDR(in linearBump2, retBump2);
		ClampToHDR(in linearBump3, retBump3);
	}

	internal static void SetGamma(float screenGamma, float texGamma, float overbright, bool allowCheats, bool linearFrameBuffer) {
		int i, inf;
		float g1, g3;
		float g;
		float brightness = 0.0f; // This used to be configurable. . hardcode to 0.0

		if (linearFrameBuffer) {
			screenGamma = 1.0f;
		}

		g = screenGamma;

		// clamp values to prevent cheating in multiplayer
		if (!allowCheats) {
			if (brightness > 2.0f)
				brightness = 2.0f;

			if (g < 1.8f)
				g = 1.8f;
		}

		if (g > 3.0f)
			g = 3.0f;

		g = 1.0f / g;
		g1 = texGamma * g;

		// pow( textureColor, g1 ) converts from on-disk texture space to framebuffer space

		if (brightness <= 0.0f) {
			g3 = 0.125f;
		}
		else if (brightness > 1.0f) {
			g3 = 0.05f;
		}
		else {
			g3 = 0.125f - (brightness * brightness) * 0.075f;
		}

		for (i = 0; i < 1024; i++) {
			float f1;

			f1 = i / 1023.0f;

			if (brightness > 1.0f)
				f1 *= brightness;

			if (f1 <= g3)
				f1 = (f1 / g3) * 0.125f;
			else
				f1 = 0.125f + ((f1 - g3) / (1.0f - g3)) * 0.875f;

			inf = (int)(255 * MathF.Pow(f1, g));

			if (inf < 0)
				inf = 0;
			if (inf > 255)
				inf = 255;
			linearToScreen[i] = inf;
		}

		for (i = 0; i < 256; i++)
			textureToLinear[i] = MathF.Pow(i / 255.0f, texGamma);

		for (i = 0; i < 1024; i++)
			linearToTexture[i] = (int)(MathF.Pow(i / 1023.0f, 1.0f / texGamma) * 255);

		float f, overbrightFactor;

		if (overbright == 2.0F)
			overbrightFactor = 0.5F;
		else if (overbright == 4.0F)
			overbrightFactor = 0.25F;
		else
			overbrightFactor = 1.0F;

		for (i = 0; i < 4096; i++) {
			f = MathF.Pow(i / 1024.0f, 1.0f / screenGamma);

			g_LinearToVertex[i] = f * overbrightFactor;
			if (g_LinearToVertex[i] > 1)
				g_LinearToVertex[i] = 1;

			linearToLightmap[i] = (int)(f * 255 * overbrightFactor);
			if (linearToLightmap[i] > 255)
				linearToLightmap[i] = 255;
		}
	}
}
