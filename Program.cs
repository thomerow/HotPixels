using HotPixels.Imaging;
using HotPixels.Imaging.Dithering;
using HotPixels.Printing;
using System.Globalization;

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
      string target, imagePath;

      // Target and image path are positional; everything else is a named option
      try {
         if (!ParseArguments(args, out target, out imagePath)) {
            PrintUsage();
            return;
         }
      }
      catch (ArgumentError ex) {
         Console.Error.WriteLine($"Error: {ex.Message}");
         Console.Error.WriteLine("Run HotPixels without arguments for usage information.");
         Environment.Exit(1);
         return;
      }

      try {
         SendImageToPrinter(target, imagePath);
      }
      catch (ImageLoadException) {
         Console.WriteLine($"Could not load image \"{imagePath}\".");
         Environment.Exit(1);
      }
   }

   /// <summary>
   /// Thrown when the command line cannot be parsed. The message is shown to the user.
   /// </summary>
   private sealed class ArgumentError(string message) : Exception(message);

   /// <summary>
   /// Prints usage information, the available print targets and the available dither modes.
   /// </summary>
   private static void PrintUsage() {
      Console.WriteLine("Usage: HotPixels <target> <imagePath> [options]");
      Console.WriteLine("Example: HotPixels \"My ESC/POS Printer\" testimage.png --dither=Atkinson");
      Console.WriteLine();

      Console.WriteLine("Positional arguments:");
      Console.WriteLine("  target         Where to print. The transport follows from its shape:");
      Console.WriteLine("                   host:port    a network printer, e.g. 192.168.1.50:9100");
      Console.WriteLine("                   a full path  a device or file, e.g. /dev/usb/lp0 or C:\\out.bin");
      Console.WriteLine("                   anything else  a print queue name (quote it if it has spaces);");
      Console.WriteLine("                                  the Windows spooler, or CUPS via \"lp -o raw\".");
      Console.WriteLine("  imagePath      Path to the image file.");
      Console.WriteLine();

      // Options may be written as --name=value or --name value
      Console.WriteLine("Options (both --name=value and --name value are accepted):");
      Console.WriteLine($"  --dither=MODE  Dithering algorithm, by name or number. Default is {(int) DefaultDitherMode} ({DefaultDitherMode}).");
      Console.WriteLine($"  --gamma=F      Gamma correction, a number greater than 0. Default is {DefaultGamma.ToString(CultureInfo.InvariantCulture)}");
      Console.WriteLine("                 (lower values make the image brighter).");
      Console.WriteLine($"  --width=N      Print width in dots, a positive multiple of 8. Default is {DefaultWidthDots}.");
      Console.WriteLine("                 Must match the paper width the printer is configured for");
      Console.WriteLine("                 (e.g. 384 for 58 mm, 512 for 80 mm).");
      Console.WriteLine("  --cut[=0|1]    Cut the paper after printing (requires a cutter, e.g. Epson TM-T88III).");
      Console.WriteLine("                 A bare --cut means on; use --cut=0 or --cut=1 to set it explicitly.");
      Console.WriteLine("  --help, -h, /? Show this help.");
      Console.WriteLine();

      // List the print targets this machine offers: spooler queues on Windows, CUPS queues and
      // printer device nodes on Unix-like systems
      Console.WriteLine("Available print targets:");
      bool any = false;
      foreach (string printTarget in RawPrinter.ListTargets()) {
         Console.WriteLine($"  \"{printTarget}\"");
         any = true;
      }
      if (!any) Console.WriteLine("  (none found)");
      Console.WriteLine();

      // List possible dither modes from enum DitherMode automatically. Either spelling works, so
      // --dither=Jarvis and --dither=2 are equivalent.
      Console.WriteLine("Dither modes (--dither accepts either the name or the number):");
      foreach (DitherMode mode in Enum.GetValues<DitherMode>()) {
         Console.WriteLine($"  {(int) mode}: {mode}");
      }
   }

   /// <summary>
   /// Parses the command line into the print target, the image path and the global settings.
   /// </summary>
   /// <returns>True if the arguments are usable, false if the usage information should be shown.</returns>
   /// <exception cref="ArgumentError">The command line is malformed.</exception>
   private static bool ParseArguments(string[] args, out string target, out string imagePath) {
      target = null;
      imagePath = null;

      if (args.Length == 0) return false;

      List<string> positional = [];

      for (int i = 0; i < args.Length; ++i) {
         string arg = args[i];

         if (arg is "-h" or "/?") return false;

         if (!arg.StartsWith("--", StringComparison.Ordinal)) {
            positional.Add(arg);
            continue;
         }

         // Split "--name=value" into its two halves; without "=" the value comes from the next argument
         int eq = arg.IndexOf('=');
         string name = (eq < 0 ? arg[2..] : arg[2..eq]).ToLowerInvariant();
         string inlineValue = eq < 0 ? null : arg[(eq + 1)..];

         if (name == "help") return false;

         // --cut is a flag: a bare --cut means on, and it never consumes the following argument,
         // which would otherwise be ambiguous with the positional arguments.
         if (name == "cut") {
            s_autoCut = inlineValue is null || ParseCut(inlineValue);
            continue;
         }

         if (name is not ("dither" or "gamma" or "width")) {
            throw new ArgumentError($"unknown option \"--{name}\".");
         }

         string value = inlineValue;
         if (value is null) {
            // Take the next argument as the value, unless it is an option itself or missing entirely
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) {
               throw new ArgumentError($"--{name} requires a value.");
            }
            value = args[++i];
         }

         switch (name) {
            case "dither": s_ditherMode = ParseDitherMode(value); break;
            case "gamma": s_gamma = ParseGamma(value); break;
            case "width": s_widthDots = ParseWidthDots(value); break;
         }
      }

      if (positional.Count > 2) {
         throw new ArgumentError(
            $"unexpected argument \"{positional[2]}\". Only the target and the image path are " +
            "positional; every setting is a named option such as --dither, --gamma, --width or --cut."
         );
      }
      if (positional.Count < 2) {
         throw new ArgumentError("both a target and an image path are required.");
      }

      // Print target: a queue name, a device path or host:port (quote it if it contains spaces)
      target = positional[0];
      if (string.IsNullOrWhiteSpace(target)) throw new ArgumentError("the target is empty.");

      // Image path, resolved to an absolute path
      imagePath = Path.GetFullPath(positional[1]);

      // Confirm the settings actually in effect
      Console.WriteLine($"Dither mode: {s_ditherMode}");
      Console.WriteLine($"Gamma: {s_gamma.ToString(CultureInfo.InvariantCulture)}");
      Console.WriteLine($"Print width: {s_widthDots} dots");
      Console.WriteLine($"Auto-cut: {(s_autoCut ? "on" : "off")}");

      return true;
   }

   /// <summary>
   /// Parses a dither mode given either by name (case-insensitive) or by its 1-based number.
   /// </summary>
   private static DitherMode ParseDitherMode(string value) {
      // DitherMode is numbered from 1, so its underlying values are exactly the numbers users type,
      // and Enum.TryParse accepts both spellings. IsDefined rejects out-of-range numbers.
      if (Enum.TryParse(value, ignoreCase: true, out DitherMode mode) && Enum.IsDefined(mode)) return mode;

      throw new ArgumentError(
         $"invalid value \"{value}\" for --dither; expected a mode name or a number from 1 to " +
         $"{Enum.GetValues<DitherMode>().Length}. Run HotPixels without arguments to list the modes."
      );
   }

   /// <summary>
   /// Parses the gamma value, which must be greater than 0. Always uses the invariant culture, so the
   /// decimal separator is a point regardless of the system's regional settings.
   /// </summary>
   private static double ParseGamma(string value) {
      if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double gamma) || gamma <= 0) {
         throw new ArgumentError($"invalid value \"{value}\" for --gamma; expected a number greater than 0.");
      }

      return gamma;
   }

   /// <summary>
   /// Parses the print width in dots, which must be a positive multiple of 8.
   /// </summary>
   private static int ParseWidthDots(string value) {
      if (!int.TryParse(value, out int widthDots) || widthDots <= 0 || widthDots % 8 != 0) {
         throw new ArgumentError($"invalid value \"{value}\" for --width; expected a positive multiple of 8.");
      }

      return widthDots;
   }

   /// <summary>
   /// Parses the explicit value of the --cut flag.
   /// </summary>
   private static bool ParseCut(string value) {
      if (!int.TryParse(value, out int cut)) {
         throw new ArgumentError($"invalid value \"{value}\" for --cut; expected 0 or 1.");
      }

      return cut != 0;
   }

   /// <summary>
   /// Loads an image and sends it to the specified ESC/POS printer.
   /// </summary>
   /// <param name="target">The printer target (queue name, device path or host:port).</param>
   /// <param name="imagePath">Path to the image file.</param>
   /// <param name="rotateIfWide">If true, rotates the image by 90° if it is wider than tall.</param>
   private static void SendImageToPrinter(string target, string imagePath, bool rotateIfWide = true) {
      // Load, rotate, scale and convert to perceived brightness
      float[,] grayData = ImageLoader.LoadGrayscale(imagePath, s_widthDots, s_gamma, rotateIfWide, out int rows);

      // Create ESC/POS raster image
      byte[] escposImage = CreateEscPosRasterImage(grayData, rows);

      byte[] trailer;
      if (s_autoCut) {
         // Two concatenated ESC/POS commands:
         //   ESC d n  (0x1B 0x64 0x06): "Print and Feed n Lines" — advances the paper by 6 lines
         //            so the printed output clears the cutter blade before cutting.
         //   GS  V 0  (0x1D 0x56 0x00): "Select Cut Mode and Cut Paper" with mode 0 = full cut —
         //            drives the built-in guillotine cutter to sever the paper completely.
         trailer = [0x1B, 0x64, 0x06, 0x1D, 0x56, 0x00];
      }
      else {
         // Four line feeds so the output is visible and the paper can be torn off
         trailer = [0x0A, 0x0A, 0x0A, 0x0A];
      }

      // Image and trailer go out as ONE write. Sending them separately would make each transport
      // reopen the target, which for a file target means starting again at offset 0 and overwriting
      // the image, and for a print queue means a second job the spooler is free to reorder or delay.
      byte[] job = new byte[escposImage.Length + trailer.Length];
      Buffer.BlockCopy(escposImage, 0, job, 0, escposImage.Length);
      Buffer.BlockCopy(trailer, 0, job, escposImage.Length, trailer.Length);

      RawPrinter.SendBytes(target, job);
   }

   /// <summary>
   /// Creates an ESC/POS raster image in GS v 0 format from the grayscale image data.
   /// </summary>
   /// <param name="grayData">The scaled grayscale image, indexed as [x, y].</param>
   /// <param name="scaledHeight">The height of the image in dot rows.</param>
   static byte[] CreateEscPosRasterImage(float[,] grayData, int scaledHeight) {
      int dataLen = BytesPerRow * scaledHeight;
      byte[] imageData = new byte[dataLen];

      // Prepare image data and convert to 1-bit using selected dithering
      Dither(grayData, s_widthDots, scaledHeight, imageData);

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
   /// <param name="width">The width of the image in dots.</param>
   /// <param name="height">The height of the image in dot rows.</param>
   /// <param name="imageData">The array to which the 1-bit image data is written.</param>
   private static void Dither(float[,] grayData, int width, int height, byte[] imageData) {
      int index = 0, w = width, h = height;
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

