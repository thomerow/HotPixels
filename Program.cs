using System;
using System.Text;
using System.Drawing;
using HotPixels.Imaging.Dithering;
using System.Globalization;

partial class Program {

	private const string PrinterName = "EM5820";  // TODO: Druckernamen per Kommandzeilenargument übergeben oder alle druckbaren Geräte auflisten und auswählbar machen
	private const double DefaultGamma = 0.75;
	private const int WidthDots = 384; // Maximale Druckerbreite in Dots
	private const int BytesPerRow = WidthDots / 8;

	private static double s_gamma = DefaultGamma;
	private static DitherMode s_ditherMode = DitherMode.Jarvis;

	static void Main(string[] args) {
		// Bild-Dateiname ist im ersten Argument, optionaler Dither-Modus im zweiten Argument (als Zahl von 1 beginnend), optionaler Gamma-Wert im dritten Argument:

		// Keine Argumente übergeben:
		if (args.Length < 1) {
			Console.WriteLine("Bitte den Pfad zum Bild als erstes Argument angeben.");
			// Mögliche Dither-Modi aus enum DitherMode automatisch auflisten
			string[] ditherNames = Enum.GetNames(typeof(DitherMode));
			Console.WriteLine("Mögliche Dither-Modi als optionales, zweites Argument (Zahlenwert von 1 beginnend, Standard ist 2 (Jarvis)):");
			for (int i = 0; i < ditherNames.Length; ++i) {
				Console.WriteLine($"  {i + 1}: {ditherNames[i]}");
			}
			Console.WriteLine(
				"Optional kann als drittes Argument ein Gamma-Wert (Fließkommazahl größer 0) angegeben werden. " +
				$"Standardwert ist {DefaultGamma.ToString(CultureInfo.InvariantCulture)} (niedrigere Werte machen das Bild heller)."
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

		// Bild laden
		using Bitmap bitmap = new(imagePath);

		// Bild um 90° drehen, falls es breiter als hoch ist
		if (bitmap.Width > bitmap.Height) {
			bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);
		}

		// ESC/POS Rasterbild erstellen
		byte[] escposImage = CreateEscPosRasterImage(bitmap);

		// An Drucker senden
		RawPrinter.SendBytes(PrinterName, escposImage);

		// Vier Zeilenumbrüche senden, damit man die Ausgabe sieht und das Papier abreißen kann
		byte[] lineFeeds = Encoding.ASCII.GetBytes("\n\n\n\n");
		RawPrinter.SendBytes(PrinterName, lineFeeds);
	}

	/// <summary>
	/// Erstellt ein ESC/POS Rasterbild im GS v 0 Format aus einem Bitmap. 
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

		// ESC/POS datenarray vorbereiten (10 Byte Header + Bilddaten)
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
	/// Wendet Dithering auf die Graustufenbilddaten an und schreibt die 1-Bit Bilddaten in das übergebene Array.
	/// </summary>
	/// <param name="grayData">Das Graustufenbild als 2D-Array.</param>
	/// <param name="size">Die Größe des Bildes.</param>
	/// <param name="imageData">Das Array, in das die 1-Bit Bilddaten geschrieben werden.</param>
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
				_ => throw new NotImplementedException($"Dither-Modus {s_ditherMode} ist nicht implementiert."),
			};
		}
		else {
			getThreshold = s_ditherMode switch {
				DitherMode.Bayer2x2 => HalftoneKernel.GetBayer2x2Threshold,
				DitherMode.Bayer4x4 => HalftoneKernel.GetBayer4x4Threshold,
				DitherMode.Bayer8x8 => HalftoneKernel.GetBayer8x8Threshold,
				DitherMode.Halftone4x4 => HalftoneKernel.GetHalftone4x4Threshold,
				_ => throw new NotImplementedException($"Dither-Modus {s_ditherMode} ist nicht implementiert."),
			};
		}

		for (int y = 0; y < h; ++y) {
			for (int bx = 0; bx < BytesPerRow; ++bx) {
				byte b = 0;
				for (int bit = 0; bit < 8; ++bit) {
					int x = bx * 8 + bit;
					oldValue = grayData[x, y]; // Aktuellen Graustufenwert lesen

					if (ditherKernel != null) {
						// Fehlerverteilungskernel anwenden
						newValue = (oldValue < 128) ? 0 : 255;
						err = oldValue - newValue; // Quantisierungsfehler
						ditherKernel(grayData, w, h, y, x, err);
					}
					else {
						// Bei geordnetem Dithering wird kein Fehler verteilt
						threshold = getThreshold(x, y);
						newValue = (oldValue < threshold) ? 0 : 255;
					}

					// Bit setzen wenn Pixel schwarz ist
					if (newValue == 0) b |= (byte) (0x80 >> bit);
				}
				imageData[index++] = b; // Byte in das Bilddatenarray schreiben
			}
		}
	}

	#region Hilfsmethoden

	/// <summary>
	/// Berechnet die wahrgenommene Helligkeit einer Farbe.
	/// </summary>
	/// <param name="color">Die Farbe.</param>
	/// <param name="gamma">Der Gamma-Korrekturfaktor. Standard ist 0.75. Höhere Werte machen das Bild dunkler.</param>
	/// <returns>Die wahrgenommene Helligkeit als Byte-Wert (0-255).</returns>
	static byte GetPercievedBrightness(Color color, double gamma = DefaultGamma) {
		double brightness = color.R * 0.299 + color.G * 0.587 + color.B * 0.114;
		brightness = Math.Pow(brightness / 255.0, gamma) * 255.0;
		return (byte) brightness;
	}

	#endregion
}

internal static class Extensions {

	/// <summary>
	/// Gibt zurück, ob der Dither-Modus ein geordneter Dither-Modus ist.
	/// </summary>
	public static bool IsOrderedDither(this DitherMode mode) => mode >= DitherMode.Bayer2x2;
}