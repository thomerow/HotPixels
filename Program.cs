using System;
using System.Text;
using System.Drawing;

class Program
{
    private const double DefaultGamma = 0.75;
    private static double s_gamma = DefaultGamma;

    static void Main(string[] args)
    {
        string printerName = "EM5820"; // oder der Name, den du vergeben hast

        // Beispiel bild dateiname ist im ersten Argument
        if (args.Length < 1)
        {
            Console.WriteLine("Bitte den Pfad zum Bild als Argument angeben.");
            return;
        }

        string imagePath = args[0];
        // Absoluter Pfad zum Bild
        imagePath = System.IO.Path.GetFullPath(imagePath);

        // Wenn ein zweites Argument übergeben wurde, als Gamma-Wert nutzen (parsen mit neutraler Kultur)
        if (args.Length >= 2 && double.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double gamma))
        {
            if (gamma <= 0) Console.WriteLine("Der Gamma-Wert muss größer als 0 sein. Standardwert wird genutzt.");
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
    static byte[] CreateEscPosRasterImage(Bitmap input)
    {
        // Skalierungsfaktor aus Breite des eingabe-Bildes und der maximalen Druckerbreite (384 Dots) berechnen
        float scaleFactor = 384.0f / input.Width;

        // Höhe des skalierten Bildes berechnen
        int scaledHeight = (int)Math.Round(input.Height * scaleFactor);

        // Skalierte Kopie des Eingabebildes erstellen
        using Bitmap resized = new(input, new Size(384, scaledHeight));

        int widthBytes = 384 / 8;
        int dataLen = widthBytes * scaledHeight;
        byte[] imageData = new byte[dataLen];
        
        // Bilddaten vorbereiten und per Floyd Steinberg dithering in 1 bit Bilddaten umwandeln

        float[,] grayData = new float[resized.Width, resized.Height];
        for (int y = 0; y < resized.Height; ++y)
        {
            for (int x = 0; x < resized.Width; ++x)
            {
                grayData[x, y] = GetPercievedBrightness(resized.GetPixel(x, y), s_gamma);
            }
        }

        int index = 0;
        for (int y = 0; y < resized.Height; ++y)
        {
            for (int bx = 0; bx < widthBytes; ++bx)
            {   
                byte b = 0;             
                for (int bit = 0; bit < 8; ++bit)
                {
                    int x = bx * 8 + bit;
                    float oldValue = grayData[x, y];
                    float newValue = (oldValue < 128) ? 0 : 255;

                    if (newValue == 0) b |= (byte)(0x80 >> bit);

                    float quantError = oldValue - newValue;

                    // Fehlerverteilung nach Floyd-Steinberg
                    if (x + 1 < resized.Width) grayData[x + 1, y] += quantError * 7 / 16;
                    if (x - 1 >= 0 && y + 1 < resized.Height) grayData[x - 1, y + 1] += quantError * 3 / 16;
                    if (y + 1 < resized.Height) grayData[x, y + 1] += quantError * 5 / 16;
                    if (x + 1 < resized.Width && y + 1 < resized.Height) grayData[x + 1, y + 1] += quantError * 1 / 16;
                }
                imageData[index++] = b;
            }
        }

        // ESC/POS datenarray vorbereiten
        byte[] result = new byte[10 + dataLen];
        // Header
        byte[] escposImageHeader = [
            0x1B, 0x40,             // ESC @ (Initialisieren)

            0x1D, 0x76, 0x30, 0x00, // GS 'v' '0' m  (m = 0: normal)
            (byte)widthBytes, 0,    // xL, xH (Breite in Bytes)
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
    /// Berechnet die wahrgenommene Helligkeit einer Farbe.
    /// </summary>
    /// <param name="color">Die Farbe.</param>
    /// <param name="gamma">Der Gamma-Korrekturfaktor. Standard ist 0.8. Höhere Werte machen das Bild dunkler.</param>
    /// <returns>Die wahrgenommene Helligkeit als Byte-Wert (0-255).</returns>
    static byte GetPercievedBrightness(Color color, double gamma = DefaultGamma)
    {
        double brightness = color.R * 0.299 + color.G * 0.587 + color.B * 0.114;
        brightness = Math.Pow(brightness / 255.0, gamma) * 255.0;
        return (byte)brightness;
    }
}