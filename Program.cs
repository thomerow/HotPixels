using System;
using System.Text;

string printerName = "EM5820"; // oder der Name, den du vergeben hast

byte[] escpos = [ 0x1B, 0x40, ]; // ESC @  -> Initialisieren

var text = "Hallo EM5820!\n";
byte[] textBytes = Encoding.ASCII.GetBytes(text); // Text in Windows-1252 kodieren

byte[] feed = [ 0x1B, 0x64, 0x03 ]; // ESC d 3  -> 3 Zeilen vorwärts
byte[] cut  = [ 0x1D, 0x56, 0x00 ]; // GS V 0  -> Vollschnitt (falls unterstützt)

// alles zusammenbauen
byte[] payload = new byte[escpos.Length + textBytes.Length + feed.Length + cut.Length];
Buffer.BlockCopy(escpos,    0, payload, 0, escpos.Length);
Buffer.BlockCopy(textBytes, 0, payload, escpos.Length, textBytes.Length);
Buffer.BlockCopy(feed,      0, payload, escpos.Length + textBytes.Length, feed.Length);
Buffer.BlockCopy(cut,       0, payload, escpos.Length + textBytes.Length + feed.Length, cut.Length);

// RawPrinter.SendBytes(printerName, payload);

// 24×24 schwarzer Block im "neuen" Rasterformat (GS v 0)
// Druckt ein gefülltes Quadrat, wenn der Drucker GS v 0 unterstützt.
byte[] escposImageNew =
{
    0x1B, 0x40,             // ESC @  -> Initialisieren

    0x1D, 0x76, 0x30, 0x00, // GS 'v' '0' m  (m = 0: normal)
    0x03, 0x00,             // xL, xH: 3 Bytes Breite (3 * 8 = 24 Pixel)
    0x18, 0x00,             // yL, yH: 24 Pixel Höhe (0x18 = 24)

    // Bilddaten: 24 Zeilen * 3 Bytes pro Zeile = 72 Bytes
    // Vollschwarz -> alle Bits = 1 -> 0xFF
    0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,
    0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,
    0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,
    0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,
    0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,
    0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,  0xFF,0xFF,0xFF,

    0x1B, 0x64, 0x04,       // ESC d 4 -> 4 Zeilen Vorschub
    0x1D, 0x56, 0x00        // GS V 0 -> Vollschnitt (falls unterstützt)
};
// RawPrinter.SendBytes(printerName, escposImageNew);

// 384 dots / line test pattern (GS v 0 format):

const int imageWidthDots = 384;
const int imageWidthBytes = imageWidthDots / 8;
const int imageHeightDots = 128;

byte[] escPosTestPattern = new byte[8 + imageWidthBytes * imageHeightDots]; // 8 Byte Header + Bilddaten

// Header
byte[] escPosTestPatternHeader = [
    0x1B, 0x40,                   // ESC @ (Initialisieren)

    0x1D, 0x76, 0x30, 0x00, // GS 'v' '0' m  (m = 0: normal)
    imageWidthBytes, 0,     // xL, xH (Breite in Bytes)
    imageHeightDots, 0,     // yL, yH (Höhe in Dots)
];

// Header kopieren
Buffer.BlockCopy(escPosTestPatternHeader, 0, escPosTestPattern, 0, escPosTestPatternHeader.Length);

// Bilddaten. Jedes Byte enthält ein alternierendes Muster von 10101010 (bzw. 01010101 für jede zweite Zeile).
for (int i = 0; i < imageHeightDots; ++i) { // i = Zeilenindex
    byte patternByte = (byte)((i % 2 == 0) ? 0xAA : 0x55); // 0xAA = 10101010, 0x55 = 01010101
    for (int j = 0; j < imageWidthBytes; j++) { // j = Byte-Index innerhalb der Zeile
        escPosTestPattern[8 + i * imageWidthBytes + j] = patternByte;
    }
}

RawPrinter.SendBytes(printerName, escPosTestPattern);
