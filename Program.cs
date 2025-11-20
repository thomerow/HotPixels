using System;
using System.Text;
using System.Drawing;
using MyEscPosTest.Enums;
using System.Globalization;

class Program {

	private const double DefaultGamma = 0.75;
	private const int WidthDots = 384; // Maximale Druckerbreite in Dots
	private const int BytesPerRow = WidthDots / 8;

	private static double s_gamma = DefaultGamma;
	private static DitherMode s_ditherMode = DitherMode.Jarvis;

	static void Main(string[] args) {
		string printerName = "EM5820"; // oder der Name, den du vergeben hast

		// Beispiel bild dateiname ist im ersten Argument, optionaler Dither-Modus im zweiten Argument (als Zahl von 1 beginnend), optionaler Gamma-Wert im dritten Argument:

		// Keine Argumente übergeben:
		if (args.Length < 1) {
			Console.WriteLine("Bitte den Pfad zum Bild als erstes Argument angeben.");
			// Mögliche Dither-Modi aus enum DitherMode automatisch auflisten
			string[] ditherNames = Enum.GetNames(typeof(DitherMode));
			Console.WriteLine("Mögliche Dither-Modi als zweites Argument (Standard ist FloydSteinberg):");
			for (int i = 0; i < ditherNames.Length; ++i) {
				Console.WriteLine($"  {i + 1}: {ditherNames[i]}");
			}
			Console.WriteLine(
				"Optional kann als drittes Argument ein Gamma-Wert (Fließkommazahl größer 0) angegeben werden. " +
				$"Standardwert ist {DefaultGamma.ToString(CultureInfo.InvariantCulture)} (niedrigerer Werte machen das Bild heller)."
			);
			return;
		}

		string imagePath = args[0];
		// Absoluter Pfad zum Bild
		imagePath = System.IO.Path.GetFullPath(imagePath);

		// Zweiten Argument als Dither-Modus nutzen (1-basiert)
		if (args.Length >= 2 && int.TryParse(args[1], out int ditherModeIndex)) {
			if (ditherModeIndex < 1 || ditherModeIndex > Enum.GetValues<DitherMode>().Length) {
				Console.WriteLine($"Ungültiger Dither-Modus Index. Standardwert {s_ditherMode} wird genutzt.");
			}
			else {
				s_ditherMode = (DitherMode) ditherModeIndex;
				Console.WriteLine($"Dither-Modus gesetzt auf {s_ditherMode}.");
			}
		}

		// Wenn ein drittes Argument übergeben wurde, als Gamma-Wert nutzen (parsen mit neutraler Kultur)
		if (args.Length >= 3 && double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double gamma)) {
			if (gamma <= 0) Console.WriteLine($"Der Gamma-Wert muss größer als 0 sein. Standardwert {s_gamma.ToString(CultureInfo.InvariantCulture)} wird genutzt.");
			else s_gamma = gamma;
		}

		using Bitmap bitmap = new(imagePath);

		// ESC/POS Rasterbild erstellen
		byte[] escposImage = CreateEscPosRasterImage(bitmap);

		// An Drucker senden
		RawPrinter.SendBytes(printerName, escposImage);

		// Vier Zeilenumbrüche senden, damit man die Ausgabe sieht und das Papier abreißen kann
		byte[] lineFeeds = Encoding.ASCII.GetBytes("\n\n\n\n");
		RawPrinter.SendBytes(printerName, lineFeeds);
	}

	/// <summary>
	/// Erstellt ein ESC/POS Rasterbild im GS v 0 Format aus einem Bitmap. Fürs erste wird nur ein Schwellenwert von 128 genutzt.
	/// </summary>
	static byte[] CreateEscPosRasterImage(Bitmap input) {
		// Skalierungsfaktor aus Breite des eingabe-Bildes und der maximalen Druckerbreite (384 Dots) berechnen
		float scaleFactor = 384.0f / input.Width;

		// Höhe des skalierten Bildes berechnen
		int scaledHeight = (int) Math.Round(input.Height * scaleFactor);

		// Skalierte Kopie des Eingabebildes erstellen
		using Bitmap resized = new(input, new Size(384, scaledHeight));

		int dataLen = BytesPerRow * scaledHeight;
		byte[] imageData = new byte[dataLen];

		// Graustufenwerte des skalierten Bildes berechnen
		float[,] grayData = new float[resized.Width, resized.Height];
		for (int y = 0; y < resized.Height; ++y) {
			for (int x = 0; x < resized.Width; ++x) {
				grayData[x, y] = GetPercievedBrightness(resized.GetPixel(x, y), s_gamma);
			}
		}

		// Bilddaten vorbereiten und per ausgewähltem Dithering in 1-Bit umwandeln
		Dither(grayData, resized.Size, imageData);

		// ESC/POS datenarray vorbereiten
		byte[] result = new byte[10 + dataLen];
		// Header
		byte[] escposImageHeader = [
			0x1B, 0x40,             // ESC @ (Initialisieren)

			0x1D, 0x76, 0x30, 0x00, // GS 'v' '0' m  (m = 0: normal)
			BytesPerRow, 0,			// xL, xH (Breite in Bytes)
			(byte)(scaledHeight & 0xFF),        // yL
			(byte)((scaledHeight >> 8) & 0xFF), // yH
		];
		// Header kopieren
		Buffer.BlockCopy(escposImageHeader, 0, result, 0, escposImageHeader.Length);
		// Bilddaten kopieren
		Buffer.BlockCopy(imageData, 0, result, escposImageHeader.Length, imageData.Length);

		return result;
	}

	/// <summary>
	/// Wendet Dithering auf die Graustufenbilddaten an und schreibt die 1-Bit Bilddaten in das imageData Array.
	/// </summary>
	/// <param name="grayData">Das Graustufenbild als 2D-Array.</param>
	/// <param name="size">Die Größe des Bildes.</param>
	/// <param name="imageData">Das Array, in das die 1-Bit Bilddaten geschrieben werden.</param>
	private static void Dither(float[,] grayData, Size size, byte[] imageData) {
		int index = 0, w = size.Width, h = size.Height;

		Action<float[,], int, int, int, int, float> ditherKernel = s_ditherMode switch {
			DitherMode.FloydSteinberg => Kernel_FloydSteinberg,
			DitherMode.Jarvis => Kernel_Jarvis,
			DitherMode.Stucki => Kernel_Stucki,
			DitherMode.Burkes => Kernel_Burkes,
			DitherMode.SierraLite => Kernel_SierraLite,
			DitherMode.Atkinson => Kernel_Atkinson,
			_ => throw new NotImplementedException($"Dither-Modus {s_ditherMode} ist nicht implementiert."),
		};

		for (int y = 0; y < h; ++y) {
			for (int bx = 0; bx < BytesPerRow; ++bx) {
				byte b = 0;
				for (int bit = 0; bit < 8; ++bit) {
					int x = bx * 8 + bit;
					float oldValue = grayData[x, y];
					float newValue = (oldValue < 128) ? 0 : 255;

					if (newValue == 0) b |= (byte) (0x80 >> bit); // Bit setzen

					float err = oldValue - newValue; // Quantisierungsfehler

					ditherKernel.Invoke(grayData, w, h, y, x, err);
				}
				imageData[index++] = b;
			}
		}
	}

	#region Dithering Kernels

	private static void Kernel_FloydSteinberg(float[,] grayData, int w, int h, int y, int x, float err) {
		if (x + 1 < w) grayData[x + 1, y] += err * 7 / 16;
		if (x - 1 >= 0 && y + 1 < h) grayData[x - 1, y + 1] += err * 3 / 16;
		if (y + 1 < h) grayData[x, y + 1] += err * 5 / 16;
		if (x + 1 < w && y + 1 < h) grayData[x + 1, y + 1] += err * 1 / 16;
	}

	private static void Kernel_Jarvis(float[,] grayData, int w, int h, int y, int x, float err) {
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

	private static void Kernel_Stucki(float[,] grayData, int w, int h, int y, int x, float err) {
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

	private static void Kernel_Burkes(float[,] grayData, int w, int h, int y, int x, float err) {
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

	private static void Kernel_SierraLite(float[,] grayData, int w, int h, int y, int x, float err) {
		// y
		if (x + 1 < w) grayData[x + 1, y] += err * 2f / 4f;

		// y+1
		if (y + 1 < h) {
			if (x - 1 >= 0) grayData[x - 1, y + 1] += err * 1f / 4f;
			grayData[x, y + 1] += err * 1f / 4f;
		}
	}

	private static void Kernel_Atkinson(float[,] grayData, int w, int h, int y, int x, float err) {
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

	#endregion


	#region Hilfsmethoden

	/// <summary>
	/// Berechnet die wahrgenommene Helligkeit einer Farbe.
	/// </summary>
	/// <param name="color">Die Farbe.</param>
	/// <param name="gamma">Der Gamma-Korrekturfaktor. Standard ist 0.8. Höhere Werte machen das Bild dunkler.</param>
	/// <returns>Die wahrgenommene Helligkeit als Byte-Wert (0-255).</returns>
	static byte GetPercievedBrightness(Color color, double gamma = DefaultGamma) {
		double brightness = color.R * 0.299 + color.G * 0.587 + color.B * 0.114;
		brightness = Math.Pow(brightness / 255.0, gamma) * 255.0;
		return (byte) brightness;
	}

	#endregion
}