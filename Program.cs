using System;
using System.Text;
using System.Drawing;
using HotPixels.Imaging.Dithering;
using HotPixels.Printing;
using System.Globalization;
using System.Drawing.Printing;

class Program {

	private static string s_printerName;
	public const double DefaultGamma = 0.75;
	private const int WidthDots = 384; // Maximum printer width in dots
	private const int BytesPerRow = WidthDots / 8;

	private static double s_gamma = DefaultGamma;
	private static DitherMode s_ditherMode = DitherMode.Jarvis;

	/// <summary>
	/// The main entry point of the application.
	/// </summary>
	static void Main(string[] args) {
		// Image file name is the first argument, optional dither mode as the second argument (as a number starting from 1), optional gamma value as the third argument:

		// Too few arguments provided:
		if (args.Length < 2) {
			// Output a note that image path and printer name must be provided
			Console.WriteLine("Please specify the path to the image as the first argument and the printer name as the second argument.");
			Console.WriteLine("Example: HotPixels.exe C:\\Images\\testimage.png \"My ESC/POS Printer\"");

			// List installed printers
			Console.WriteLine("Installed printers:");
			foreach (string printerName in PrinterSettings.InstalledPrinters) {
				Console.WriteLine($"  \"{printerName}\"");
			}

			// List possible dither modes from enum DitherMode automatically
			string[] ditherNames = Enum.GetNames(typeof(DitherMode));
			Console.WriteLine("Possible dither modes as optional third argument (numeric value starting from 1, default is 2 (Jarvis)):");
			for (int i = 0; i < ditherNames.Length; ++i) {
				Console.WriteLine($"  {i + 1}: {ditherNames[i]}");
			}

			// Output note about gamma value
			Console.WriteLine(
				"Optionally, a gamma value (floating point number greater than 0) can be specified as the fourth argument. " +
				$"Default value is {DefaultGamma.ToString(CultureInfo.InvariantCulture)} (lower values make the image brighter)."
			);
			return;
		}

		// Use first argument as image path
		string imagePath = args[0];
		// Get absolute path
		imagePath = Path.GetFullPath(imagePath);

		// Use second argument as printer name (ESC/POS capable printer, must be in quotes if name contains spaces)
		s_printerName = args[1];
		// Rough verification if printer name is empty
		if (string.IsNullOrWhiteSpace(s_printerName)) {
			Console.WriteLine("Invalid printer name specified.");
			return;
		}

		// Use third argument as dither mode (1-based)
		if (args.Length >= 3 && int.TryParse(args[2], out int ditherModeIndex)) {
			if (ditherModeIndex < 1 || ditherModeIndex > Enum.GetValues<DitherMode>().Length) {
				Console.WriteLine($"Invalid dither mode index. Default value {s_ditherMode} will be used.");
			}
			else {
				s_ditherMode = (DitherMode) ditherModeIndex;
				Console.WriteLine($"Dither mode set to {s_ditherMode}.");
			}
		}

		// If a fourth argument is provided, use as gamma value (parse with invariant culture)
		if (args.Length >= 4 && double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double gamma)) {
			if (gamma <= 0) Console.WriteLine($"Gamma value must be greater than 0. Default value {s_gamma.ToString(CultureInfo.InvariantCulture)} will be used.");
			else s_gamma = gamma;
		}

		// Load image
		using Bitmap bitmap = new(imagePath);

		// Rotate image by 90° if it is wider than tall
		if (bitmap.Width > bitmap.Height) {
			bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);
		}

		// Create ESC/POS raster image
		byte[] escposImage = CreateEscPosRasterImage(bitmap);

		// Send to printer
		RawPrinter.SendBytes(s_printerName, escposImage);

		// Send four line feeds so the output is visible and the paper can be torn off
		byte[] lineFeeds = Encoding.ASCII.GetBytes("\n\n\n\n");
		RawPrinter.SendBytes(s_printerName, lineFeeds);
	}

	/// <summary>
	/// Creates an ESC/POS raster image in GS v 0 format from a bitmap.
	/// </summary>
	static byte[] CreateEscPosRasterImage(Bitmap input) {
		// Calculate scaling factor from input image width and maximum printer width (384 dots)
		float scaleFactor = 384.0f / input.Width;

		// Calculate height of scaled image
		int scaledHeight = (int) Math.Round(input.Height * scaleFactor);

		// Create scaled copy of input image
		using Bitmap resized = new(input, new Size(384, scaledHeight));

		int dataLen = BytesPerRow * scaledHeight;
		byte[] imageData = new byte[dataLen];

		// Calculate grayscale values of scaled image
		float[,] grayData = new float[resized.Width, resized.Height];
		for (int y = 0; y < resized.Height; ++y) {
			for (int x = 0; x < resized.Width; ++x) {
				grayData[x, y] = resized.GetPixel(x, y).GetPerceivedBrightness(s_gamma);
			}
		}

		// Prepare image data and convert to 1-bit using selected dithering
		Dither(grayData, resized.Size, imageData);

		// Prepare ESC/POS data array (10 byte header + image data)
		byte[] result = new byte[10 + dataLen];

		// Header
		byte[] escposImageHeader = [
			0x1B, 0x40,             // ESC @ (Initialize)

			0x1D, 0x76, 0x30, 0x00, // GS 'v' '0' m  (m = 0: normal)
			BytesPerRow, 0,			// xL, xH (width in bytes)
			(byte)(scaledHeight & 0xFF),        // yL
			(byte)((scaledHeight >> 8) & 0xFF), // yH
		];

		// Copy header
		Buffer.BlockCopy(escposImageHeader, 0, result, 0, escposImageHeader.Length);

		// Copy image data
		Buffer.BlockCopy(imageData, 0, result, escposImageHeader.Length, imageData.Length);

		return result;
	}

	/// <summary>
	/// Applies dithering to the grayscale image data and writes the 1-bit image data to the provided array.
	/// </summary>
	/// <param name="grayData">The grayscale image as a 2D array.</param>
	/// <param name="size">The size of the image.</param>
	/// <param name="imageData">The array to which the 1-bit image data is written.</param>
	private static void Dither(float[,] grayData, Size size, byte[] imageData) {
		int index = 0, w = size.Width, h = size.Height;
		float oldValue, threshold, newValue = 0, err;
		Action<float[,], int, int, int, int, float> ditherKernel = null;
		Func<int, int, float> getThreshold = null;

		if (!s_ditherMode.IsOrderedDither()) {
			ditherKernel = s_ditherMode switch {
				DitherMode.FloydSteinberg => DitherKernel.FloydSteinberg,
				DitherMode.Jarvis => DitherKernel.Jarvis,
				DitherMode.Stucki => DitherKernel.Stucki,
				DitherMode.Burkes => DitherKernel.Burkes,
				DitherMode.SierraLite => DitherKernel.SierraLite,
				DitherMode.Atkinson => DitherKernel.Atkinson,
				_ => throw new NotImplementedException($"Dither mode {s_ditherMode} is not implemented."),
			};
		}
		else {
			getThreshold = s_ditherMode switch {
				DitherMode.Bayer2x2 => HalftoneKernel.GetBayer2x2Threshold,
				DitherMode.Bayer4x4 => HalftoneKernel.GetBayer4x4Threshold,
				DitherMode.Bayer8x8 => HalftoneKernel.GetBayer8x8Threshold,
				DitherMode.Halftone4x4 => HalftoneKernel.GetHalftone4x4Threshold,
				_ => throw new NotImplementedException($"Dither mode {s_ditherMode} is not implemented."),
			};
		}

		for (int y = 0; y < h; ++y) {
			for (int bx = 0; bx < BytesPerRow; ++bx) {
				byte b = 0;
				for (int bit = 0; bit < 8; ++bit) {
					int x = bx * 8 + bit;
					oldValue = grayData[x, y]; // Read current grayscale value

					if (ditherKernel != null) {
						// Apply error distribution kernel
						newValue = (oldValue < 128) ? 0 : 255;
						err = oldValue - newValue; // Quantization error
						ditherKernel(grayData, w, h, y, x, err);
					}
					else {
						// No error distribution for ordered dithering
						threshold = getThreshold(x, y);
						newValue = (oldValue < threshold) ? 0 : 255;
					}

					// Set bit if pixel is black
					if (newValue == 0) b |= (byte) (0x80 >> bit);
				}
				imageData[index++] = b; // Write byte to image data array
			}
		}
	}
}

/// <summary>
/// Useful extension methods
/// </summary>
internal static class Extensions {

	/// <summary>
	/// Returns whether the dither mode is an ordered dither mode.
	/// </summary>
	public static bool IsOrderedDither(this DitherMode mode) => mode >= DitherMode.Bayer2x2;

	/// <summary>
	/// Calculates the perceived brightness of a color.
	/// </summary>
	/// <param name="color">The color.</param>
	/// <param name="gamma">The gamma correction factor. Default is 0.75. Higher values make the image darker.</param>
	/// <returns>The perceived brightness as a byte value (0-255).</returns>
	public static byte GetPerceivedBrightness(this Color color, double gamma = Program.DefaultGamma) {
		double brightness = color.R * 0.299 + color.G * 0.587 + color.B * 0.114;
		brightness = Math.Pow(brightness / 255.0, gamma) * 255.0;
		return (byte) brightness;
	}
}