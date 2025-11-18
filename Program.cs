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

// 24x24 schwarzer Testblock im ESC * Format (24-dot double density)
/*
byte[] escposImage =
{
    0x1B, 0x40,                   // ESC @ (Initialisieren)
    
    // ---- Zeile 1 (24 Pixel hoch, 24 Pixel breit) ----
    0x1B, 0x2A, 0x21,             // ESC * 33 (24-dot double density)
    24, 0,                        // nL=24, nH=0   (24 Pixel breit)
    
    // 24px Breite -> 24 / 8 = 3 Bytes pro Zeile-Gruppe
    // 24 Pixel Höhe -> 3 Graustufenbänder à 8 Pixel -> pro Band ebenfalls 3 Bytes
    //
    // Wir erzeugen einen vollen schwarzen Block => jedes Bit = 1 => 0xFF

    // 3 Bytes für Band 1 (obere 8 Pixel)
    0xFF, 0xFF, 0xFF,

    // 3 Bytes für Band 2 (mittlere 8)
    0xFF, 0xFF, 0xFF,

    // 3 Bytes für Band 3 (untere 8)
    0xFF, 0xFF, 0xFF,

    // ESC J 24 -> eine Zeile vertikal vorschieben
    0x1B, 0x4A, 24,

    // ---- Zeile 2 (wieder derselbe Block) ----
    0x1B, 0x2A, 0x21,
    24, 0,
    0xFF, 0xFF, 0xFF,
    0xFF, 0xFF, 0xFF,
    0xFF, 0xFF, 0xFF,

    0x1B, 0x4A, 24,

    // ---- Zeile 3 ----
    0x1B, 0x2A, 0x21,
    24, 0,
    0xFF, 0xFF, 0xFF,
    0xFF, 0xFF, 0xFF,
    0xFF, 0xFF, 0xFF,

    0x1B, 0x4A, 24,

    // Abschließendes Feed
    0x1B, 0x64, 0x04,             // ESC d 4 (4 Zeilen Vorschub)
    0x1D, 0x56, 0x00              // GS V 0 (volle Schnittkante) - falls unterstützt
};
RawPrinter.SendBytes(printerName, escposImage);
//*/

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

// 384 dots / line test pattern (ESC * command)

byte[] escPosTestPattern = new byte[8 + 48 * 48]; // 8 Byte Header + 2304 Byte Bilddaten

// Header
byte[] escPosTestPatternHeader = [
    0x1B, 0x40,                   // ESC @ (Initialisieren)

    0x1D, 0x76, 0x30, 0x00, // GS 'v' '0' m  (m = 0: normal)
    48, 0,                  // xL, xH: 48 Bytes Breite (48 * 8 = 384 Pixel)
    48, 0,                  // yL, yH: 48 Pixel Höhe
];
Buffer.BlockCopy(escPosTestPatternHeader, 0, escPosTestPattern, 0, escPosTestPatternHeader.Length);

// Bilddaten: 48 Zeilen * 48 Bytes pro Zeile = 2304 Bytes
// Jedes Byte enthält ein alternierendes Muster von 10101010 bzw. 01010101. Jede zweite Zeile ist invertiert.
for (int i = 0; i < 48; ++i) { // i = Zeilenindex
    byte patternByte = (i % 2 == 0) ? (byte)0xAA : (byte)0x55;
    for (int j = 0; j < 48; j++) { // j = Byte-Index innerhalb der Zeile
        escPosTestPattern[8 + i * 48 + j] = patternByte;
    }
}

RawPrinter.SendBytes(printerName, escPosTestPattern);