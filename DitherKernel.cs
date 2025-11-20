using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HotPixels.Imaging.Dithering;

/// <summary>
/// Fehlerdiffusions-Kernel für verschiedene Dithering-Methoden
/// </summary>
sealed class DitherKernel {

	/// <summary>
	/// Floyd-Steinberg Dithering Kernel
	/// </summary>
	/// <param name="grayData">Das Graustufenbild als 2D-Array.</param>
	/// <param name="w">Die Breite des Bildes.</param>
	/// <param name="h">Die Höhe des Bildes.</param>
	/// <param name="y">Die aktuelle y-Position.</param>
	/// <param name="x">Die aktuelle x-Position.</param>
	/// <param name="err">Der Quantisierungsfehler.</param>
	public static void FloydSteinberg(float[,] grayData, int w, int h, int y, int x, float err) {
		if (x + 1 < w) grayData[x + 1, y] += err * 7 / 16;
		if (x - 1 >= 0 && y + 1 < h) grayData[x - 1, y + 1] += err * 3 / 16;
		if (y + 1 < h) grayData[x, y + 1] += err * 5 / 16;
		if (x + 1 < w && y + 1 < h) grayData[x + 1, y + 1] += err * 1 / 16;
	}

	/// <summary>
	/// Jarvis, Judice, and Ninke Dithering Kernel (weit, weich)
	/// </summary>
	public static void Jarvis(float[,] grayData, int w, int h, int y, int x, float err) {
		// Zeile y
		if (x + 1 < w) grayData[x + 1, y] += err * 7f / 48f;
		if (x + 2 < w) grayData[x + 2, y] += err * 5f / 48f;

		// Zeile y+1
		if (y + 1 < h) {
			if (x - 2 >= 0) grayData[x - 2, y + 1] += err * 3f / 48f;
			if (x - 1 >= 0) grayData[x - 1, y + 1] += err * 5f / 48f;

			grayData[x, y + 1] += err * 7f / 48f;

			if (x + 1 < w) grayData[x + 1, y + 1] += err * 5f / 48f;
			if (x + 2 < w) grayData[x + 2, y + 1] += err * 3f / 48f;
		}

		// Zeile y+2
		if (y + 2 < h) {
			if (x - 2 >= 0) grayData[x - 2, y + 2] += err * 1f / 48f;
			if (x - 1 >= 0) grayData[x - 1, y + 2] += err * 3f / 48f;

			grayData[x, y + 2] += err * 5f / 48f;

			if (x + 1 < w) grayData[x + 1, y + 2] += err * 3f / 48f;
			if (x + 2 < w) grayData[x + 2, y + 2] += err * 1f / 48f;
		}
	}

	/// <summary>
	/// Stucki Dithering Kernel
	/// </summary>
	public static void Stucki(float[,] grayData, int w, int h, int y, int x, float err) {
		// y
		if (x + 1 < w) grayData[x + 1, y] += err * 8f / 42f;
		if (x + 2 < w) grayData[x + 2, y] += err * 4f / 42f;

		// y + 1
		if (y + 1 < h) {
			if (x - 2 >= 0) grayData[x - 2, y + 1] += err * 2f / 42f;
			if (x - 1 >= 0) grayData[x - 1, y + 1] += err * 4f / 42f;

			grayData[x, y + 1] += err * 8f / 42f;

			if (x + 1 < w) grayData[x + 1, y + 1] += err * 4f / 42f;
			if (x + 2 < w) grayData[x + 2, y + 1] += err * 2f / 42f;
		}

		// y + 2
		if (y + 2 < h) {
			if (x - 2 >= 0) grayData[x - 2, y + 2] += err * 1f / 42f;
			if (x - 1 >= 0) grayData[x - 1, y + 2] += err * 2f / 42f;

			grayData[x, y + 2] += err * 4f / 42f;

			if (x + 1 < w) grayData[x + 1, y + 2] += err * 2f / 42f;
			if (x + 2 < w) grayData[x + 2, y + 2] += err * 1f / 42f;
		}
	}

	/// <summary>
	/// Burkes Dithering Kernel
	/// </summary>
	public static void Burkes(float[,] grayData, int w, int h, int y, int x, float err) {
		// y
		if (x + 1 < w) grayData[x + 1, y] += err * 8f / 32f;
		if (x + 2 < w) grayData[x + 2, y] += err * 4f / 32f;

		// y + 1
		if (y + 1 < h) {
			if (x - 2 >= 0) grayData[x - 2, y + 1] += err * 2f / 32f;
			if (x - 1 >= 0) grayData[x - 1, y + 1] += err * 4f / 32f;
			grayData[x, y + 1] += err * 8f / 32f;
			if (x + 1 < w) grayData[x + 1, y + 1] += err * 4f / 32f;
			if (x + 2 < w) grayData[x + 2, y + 1] += err * 2f / 32f;
		}
	}

	/// <summary>
	/// Sierra Lite Dithering Kernel
	/// </summary>
	public static void SierraLite(float[,] grayData, int w, int h, int y, int x, float err) {
		// y
		if (x + 1 < w) grayData[x + 1, y] += err * 2f / 4f;

		// y+1
		if (y + 1 < h) {
			if (x - 1 >= 0) grayData[x - 1, y + 1] += err * 1f / 4f;
			grayData[x, y + 1] += err * 1f / 4f;
		}
	}

	/// <summary>
	/// Atkinson Dithering Kernel
	/// </summary>
	public static void Atkinson(float[,] grayData, int w, int h, int y, int x, float err) {
		// y
		if (x + 1 < w) grayData[x + 1, y] += err / 8f;
		if (x + 2 < w) grayData[x + 2, y] += err / 8f;

		// y+1
		if (y + 1 < h) {
			if (x - 1 >= 0) grayData[x - 1, y + 1] += err / 8f;
			grayData[x, y + 1] += err / 8f;
			if (x + 1 < w) grayData[x + 1, y + 1] += err / 8f;
		}

		// y+2
		if (y + 2 < h) {
			grayData[x, y + 2] += err / 8f;
		}
	}
}

