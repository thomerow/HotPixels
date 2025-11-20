using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HotPixels.Imaging.Dithering;

/// <summary>
/// Threshold-based kernels for ordered dithering methods.
/// </summary>
sealed class HalftoneKernel {

	private static readonly int[,] Bayer2x2 = { { 0, 2 }, { 3, 1 } };

	private static readonly int[,] Bayer4x4 = {
		{  0,  8,  2, 10 },
		{ 12,  4, 14,  6 },
		{  3, 11,  1,  9 },
		{ 15,  7, 13,  5 }
	};

	private static readonly int[,] Bayer8x8 = {
		{  0, 32,  8, 40,  2, 34, 10, 42 },
		{ 48, 16, 56, 24, 50, 18, 58, 26 },
		{ 12, 44,  4, 36, 14, 46,  6, 38 },
		{ 60, 28, 52, 20, 62, 30, 54, 22 },
		{  3, 35, 11, 43,  1, 33,  9, 41 },
		{ 51, 19, 59, 27, 49, 17, 57, 25 },
		{ 15, 47,  7, 39, 13, 45,  5, 37 },
		{ 63, 31, 55, 23, 61, 29, 53, 21 }
	};

	private static readonly int[,] Halftone4x4 = {
		{  7, 13, 11,  4 },
		{ 12, 16, 14,  8 },
		{ 10, 15,  6,  2 },
		{  5,  9,  3,  1 }
	};

	/// <summary>
	/// Returns the threshold for the Bayer 2×2 ordered dithering pattern.
	/// </summary>
	public static float GetBayer2x2Threshold(int x, int y) {
		int v = Bayer2x2[y & 1, x & 1]; // y % 2, x % 2
		// (v + 0.5) / 4 → 0..1, *255 → 0..255
		return (float) ((v + 0.5) / 4.0 * 255.0);
	}

	/// <summary>
	/// Returns the threshold for the Bayer 4×4 ordered dithering pattern.
	/// </summary>
	public static float GetBayer4x4Threshold(int x, int y) {
		int v = Bayer4x4[y & 3, x & 3]; // y % 4, x % 4
		// (v + 0.5) / 16 → 0..1, *255 → 0..255
		return (float) ((v + 0.5) / 16.0 * 255.0);
	}

	/// <summary>
	/// Returns the threshold for the Bayer 8×8 ordered dithering pattern.
	/// </summary>
	public static float GetBayer8x8Threshold(int x, int y) {
		int v = Bayer8x8[y & 7, x & 7]; // y % 8, x % 8
		// (v + 0.5) / 64 → 0..1, *255 → 0..255
		return (float) ((v + 0.5) / 64.0 * 255.0);
	}

	/// <summary>
	/// Returns the threshold for the clustered-dot halftone 4×4 pattern.
	/// </summary>
	public static float GetHalftone4x4Threshold(int x, int y) {
		int v = Halftone4x4[y & 3, x & 3];
		return (float) ((v + 0.5) / 16.0 * 255.0);
	}
}
