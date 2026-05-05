using HotPixels.Imaging;
using HotPixels.Imaging.Dithering;
using HotPixels.Printing;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Text;

class Program {

   public const double DefaultGamma = 0.75;
   public const int DefaultWidthDots = 384;
   public const DitherMode DefaultDitherMode = DitherMode.Jarvis;

   private static double s_gamma = DefaultGamma;
   private static DitherMode s_ditherMode = DefaultDitherMode;
   private static int s_widthDots = DefaultWidthDots;
   private static bool s_autoCut = false;

   private static int BytesPerRow => s_widthDots / 8;

   /// <summary>
   /// The main entry point of the application.
   /// </summary>
   static void Main(string[] args) {
      Bitmap bitmap = null;

      // Printer name as the first argument, image file name as the second argument, optional dither
      // mode as the third argument (as a number starting from 1), optional gamma value as the fourth argument:

      // Too few arguments provided:
      if (args.Length < 2) {
         // Output a note that image path and printer name must be provided
         Console.WriteLine("Please specify the printer name as the first argument and the path to the image as the second argument.");
         Console.WriteLine("Example: HotPixels.exe \"My ESC/POS Printer\" C:\\Images\\testimage.png");

         // List installed printers
         Console.WriteLine("Installed printers:");
         foreach (string printer in PrinterSettings.InstalledPrinters) {
            Console.WriteLine($"  \"{printer}\"");
         }

         // List possible dither modes from enum DitherMode automatically
         Console.WriteLine($"Possible dither modes as optional third argument (numeric value starting from 1, default is {(int) DefaultDitherMode} ({DefaultDitherMode})):");
         string[] ditherNames = Enum.GetNames(typeof(DitherMode));
         for (int i = 0; i < ditherNames.Length; ++i) {
            Console.WriteLine($"  {i + 1}: {ditherNames[i]}");
         }

         // Output note about gamma value
         Console.WriteLine(
            "Optionally, a gamma value (floating point number greater than 0) can be specified as the fourth argument. " +
            $"Default value is {DefaultGamma.ToString(CultureInfo.InvariantCulture)} (lower values make the image brighter)."
         );

         // Output note about width in dots
         Console.WriteLine(
            $"Optionally, the print width in dots can be specified as the fifth argument (must be a multiple of 8). " +
            $"Default value is {DefaultWidthDots}."
         );

         // Output note about auto-cut
         Console.WriteLine(
            "Optionally, specify 1 as the sixth argument to automatically cut the paper after printing (requires a cutter, e.g. Epson TM-T88III). " +
            "Default is 0 (no auto-cut)."
         );
         return;
      }

      // Use first argument as printer name (ESC/POS capable printer, must be in quotes if name contains spaces)
      string printerName = args[0];
      // Rough verification if printer name is empty
      if (string.IsNullOrWhiteSpace(printerName)) {
         Console.WriteLine("Invalid printer name specified.");
         return;
      }

      // Use second argument as image path
      string imagePath = args[1];
      // Get absolute path
      imagePath = Path.GetFullPath(imagePath);

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

      // If a fifth argument is provided, use as width in dots (must be a multiple of 8)
      if (args.Length >= 5 && int.TryParse(args[4], out int widthDots)) {
         if (widthDots <= 0 || widthDots % 8 != 0)
            Console.WriteLine($"Width must be a positive multiple of 8. Default value {DefaultWidthDots} will be used.");
         else {
            s_widthDots = widthDots;
            Console.WriteLine($"Print width set to {s_widthDots} dots.");
         }
      }

      // If a sixth argument is provided, use as auto-cut flag (1 = cut, 0 = no cut)
      if (args.Length >= 6 && int.TryParse(args[5], out int autoCut)) {
         s_autoCut = autoCut != 0;
      }

      // Load image
      try {
         bitmap = new(imagePath);
      }
      catch {
         Console.WriteLine($"Could not load image \"{imagePath}\".");
         Environment.Exit(1);
      }

      // Send bitmap to printer
      using (bitmap) {
         SendBitmapToPrinter(printerName, bitmap);
      }
   }

   /// <summary>
   /// Sends a bitmap image to the specified ESC/POS printer.
   /// </summary>
   /// <param name="printerName">The name of the printer.</param>
   /// <param name="bitmap">The bitmap image to send.</param>
   /// <param name="rotateIfWide">If true, rotates the image by 90° if it is wider than tall.</param>
   private static void SendBitmapToPrinter(string printerName, Bitmap bitmap, bool rotateIfWide = true) {
      // Rotate image by 90° if it is wider than tall
      if (rotateIfWide && (bitmap.Width > bitmap.Height)) {
         bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);
      }

      // Create ESC/POS raster image
      byte[] escposImage = CreateEscPosRasterImage(bitmap);

      // Send to printer
      RawPrinter.SendBytes(printerName, escposImage);

      if (s_autoCut) {
         // Two concatenated ESC/POS commands:
         //   ESC d n  (0x1B 0x64 0x06): "Print and Feed n Lines" — advances the paper by 6 lines
         //            so the printed output clears the cutter blade before cutting.
         //   GS  V 0  (0x1D 0x56 0x00): "Select Cut Mode and Cut Paper" with mode 0 = full cut —
         //            drives the built-in guillotine cutter to sever the paper completely.
         byte[] cutCommand = [0x1B, 0x64, 0x06, 0x1D, 0x56, 0x00];
         RawPrinter.SendBytes(printerName, cutCommand);
      }
      else {
         // Send four line feeds so the output is visible and the paper can be torn off
         byte[] lineFeeds = Encoding.ASCII.GetBytes("\n\n\n\n");
         RawPrinter.SendBytes(printerName, lineFeeds);
      }
   }

   /// <summary>
   /// Creates an ESC/POS raster image in GS v 0 format from a bitmap.
   /// </summary>
   static byte[] CreateEscPosRasterImage(Bitmap input) {
      // Calculate scaling factor from input image width and maximum printer width
      float scaleFactor = ((float) s_widthDots) / input.Width;

      // Calculate height of scaled image
      int scaledHeight = (int) Math.Round(input.Height * scaleFactor);

      // Create scaled copy of input image
      using Bitmap resized = new(input, new Size(s_widthDots, scaledHeight));

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

      // ESC/POS raster image header for GS v 0
      byte[] escposImageHeader = [
         0x1B, 0x40,             // ESC @ (Initialize)

         0x1D, 0x76, 0x30, 0x00, // GS 'v' '0' m  (m = 0: normal)
         (byte) BytesPerRow, 0,	// xL, xH (width in bytes (low byte, high byte))
         (byte)(scaledHeight & 0xFF),        // yL (low byte of height)
         (byte)((scaledHeight >> 8) & 0xFF), // yH (high byte of height)
      ];

      // Prepare ESC/POS data array (header + image data)
      byte[] result = new byte[escposImageHeader.Length + imageData.Length];

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

