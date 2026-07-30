using HotPixels.Imaging;
using HotPixels.Imaging.Dithering;
using HotPixels.Printing;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Text;

/// <summary>
/// Ways of getting raster data into the printer. Both bulk modes upload a band while the motor is
/// stopped and then print it from printer memory in one continuous pass, which is what avoids the
/// intermittent-print banding a plain Standard mode raster suffers from on a slow link.
/// </summary>
enum BulkMode {
   /// <summary>One monolithic GS v 0 raster in Standard mode. Printing follows data arrival.</summary>
   Off,
   /// <summary>ESC L page mode: GS v 0 buffers into the page buffer, FF prints it.</summary>
   Page,
   /// <summary>GS * defines a downloaded bit image, GS / prints it. Works without page mode.</summary>
   Download,
}

class Program {

   public const double DefaultGamma = 0.75;
   public const int DefaultWidthDots = 384;
   public const DitherMode DefaultDitherMode = DitherMode.Jarvis;

   // Bulk printing limits. The Epson TM-T88III predates the GS ( L high-speed graphics commands of the
   // TM-T88IV, so the only ways to buffer a band inside the printer are page mode and GS * / GS /.
   public const int DefaultBandRows = 384;      // ~54 mm at 180 dpi, comfortably below the row cap
   public const int MaxBandRows = 831;          // TM-T88III page area: 512 x 831 dots (TM-T88IV: 1662 rows)
   public const int MaxPageModeWidthDots = 512; // TM-T88III page area: 512 dots horizontally
   public const int MaxDownloadBytes = 1536;    // GS *: x * y <= 1536 bytes, i.e. the 12 KB download buffer

   // Probe mode: one command-sequence variant per run, so a variant that desyncs the printer's command
   // parser cannot corrupt the results of the others.
   public const int ProbeRows = 64;             // small enough that each probe costs ~1 cm of paper
   private static readonly string[] s_probeVariants = [
      "STD          plain GS v 0 in Standard mode (control - this must work)",
      "PAGE         ESC L / ESC W / GS v 0 / FF",
      "PAGE+T       ESC L / ESC T 0 / ESC W / GS v 0 / FF  (the sequence that failed)",
      "PAGE+ESCFF   ESC L / ESC W / GS v 0 / ESC FF / ESC S",
      "DOWNLOAD     GS * / GS /  (downloaded bit image, no page mode)",
   ];

   private static double s_gamma = DefaultGamma;
   private static DitherMode s_ditherMode = DefaultDitherMode;
   private static int s_widthDots = DefaultWidthDots;
   private static bool s_autoCut = false;
   private static BulkMode s_bulkMode = BulkMode.Off;
   private static int s_bandRows = DefaultBandRows;
   private static string s_dumpPath = null;
   private static int s_probe = 0;               // 0 = off, otherwise the 1-based variant number

   private static int BytesPerRow => s_widthDots / 8;

   /// <summary>
   /// The main entry point of the application.
   /// </summary>
   static void Main(string[] args) {
      Bitmap bitmap = null;

      if (!ParseArguments(args, out string printerName, out string imagePath)) {
         PrintUsage();
         return;
      }

      // Probe mode prints its own built-in test pattern, so no image file is involved
      if (s_probe > 0) {
         RunProbe(printerName);
         return;
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
   /// Prints the usage information, the installed printers and the available dither modes.
   /// </summary>
   private static void PrintUsage() {
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

      // List the named options, which may be mixed freely with the positional arguments above
      Console.WriteLine("Named options (may appear anywhere; they override the positional arguments):");
      Console.WriteLine("  --dither=N     Same as the third positional argument.");
      Console.WriteLine("  --gamma=F      Same as the fourth positional argument.");
      Console.WriteLine("  --width=N      Same as the fifth positional argument.");
      Console.WriteLine("  --cut[=0|1]    Same as the sixth positional argument.");
      Console.WriteLine("  --bulk=MODE    Buffer each band inside the printer and print it in one continuous pass,");
      Console.WriteLine("                 which avoids intermittent-print banding on the Epson TM-T88III:");
      Console.WriteLine("                   off       One monolithic GS v 0 raster (default).");
      Console.WriteLine("                   page      ESC L page mode; FF prints each band.");
      Console.WriteLine("                   download  GS * / GS /; works without page mode, smaller bands.");
      Console.WriteLine("                 Default is off, because generic printers often implement neither.");
      Console.WriteLine("  --pagemode     Alias for --bulk=page.");
      Console.WriteLine($"  --band=N       Bulk band height in dot rows (1..{MaxBandRows}). Default is {DefaultBandRows}.");
      Console.WriteLine("                 Download mode caps this at 8 * (1536 / bytesPerRow) - 192 rows at 512 dots.");
      Console.WriteLine("  --probe[=N]    Print one small test pattern through a single command sequence, to find out");
      Console.WriteLine("                 which bulk mode this printer implements. Never cuts. Omit N to list variants.");
      Console.WriteLine("  --dump=PATH    Write the generated ESC/POS byte stream to a file instead of printing.");
      Console.WriteLine();
      Console.WriteLine("Print width in dots per printer and paper width:");
      Console.WriteLine("  Generic 203 dpi, 58 mm             384 (default)");
      Console.WriteLine("  Generic 203 dpi, 80 mm             576");
      Console.WriteLine("  Epson TM-T88III/IV, 58 mm          360  (180 dpi, 50.8 mm print width)");
      Console.WriteLine("  Epson TM-T88III/IV, 80 mm          512  (180 dpi, 72.2 mm print width)");
   }

   /// <summary>
   /// Parses the command line into the printer name, the image path and the global settings.
   /// </summary>
   /// <returns>True if the arguments are usable, false if the usage information should be shown.</returns>
   private static bool ParseArguments(string[] args, out string printerName, out string imagePath) {
      printerName = null;
      imagePath = null;

      // Split the command line into "--name[=value]" options and bare positional arguments. This keeps
      // the documented positional form working unchanged while allowing options anywhere on the line.
      List<string> positional = [];
      Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);

      foreach (string arg in args) {
         if (arg is "-h" or "/?") return false;

         if (arg.StartsWith("--", StringComparison.Ordinal)) {
            int eq = arg.IndexOf('=');
            options[eq < 0 ? arg[2..] : arg[2..eq]] = eq < 0 ? "" : arg[(eq + 1)..];
         }
         else positional.Add(arg);
      }

      if (options.ContainsKey("help")) return false;

      // A bare "--probe" only lists the variants; it neither prints nor needs an image
      if (options.TryGetValue("probe", out string probeValue) && probeValue.Length == 0) {
         PrintProbeVariants();
         return false;
      }

      // Probe mode uses a built-in test pattern, so only the printer name is required there
      int required = options.ContainsKey("probe") ? 1 : 2;
      if (positional.Count < required) return false;

      // Use first argument as printer name (ESC/POS capable printer, must be in quotes if name contains spaces)
      printerName = positional[0];
      // Rough verification if printer name is empty
      if (string.IsNullOrWhiteSpace(printerName)) {
         Console.WriteLine("Invalid printer name specified.");
         return false;
      }

      // Use second argument as image path, resolved to an absolute path
      if (positional.Count >= 2) imagePath = Path.GetFullPath(positional[1]);

      // Positional arguments three to six, then the equivalent named options so an option always wins
      if (positional.Count >= 3) ApplyDitherMode(positional[2]);
      if (positional.Count >= 4) ApplyGamma(positional[3]);
      if (positional.Count >= 5) ApplyWidthDots(positional[4]);
      if (positional.Count >= 6) ApplyAutoCut(positional[5]);

      foreach ((string name, string value) in options) {
         switch (name.ToLowerInvariant()) {
            case "dither": ApplyDitherMode(value); break;
            case "gamma": ApplyGamma(value); break;
            case "width": ApplyWidthDots(value); break;
            case "cut": s_autoCut = IsFlagSet(value, "cut"); break;
            case "pagemode": if (IsFlagSet(value, "pagemode")) s_bulkMode = BulkMode.Page; break;
            case "bulk": if (!ApplyBulkMode(value)) return false; break;
            case "band": ApplyBandRows(value); break;
            case "dump": ApplyDumpPath(value); break;
            case "probe": if (!ApplyProbe(value)) return false; break;
            // A typo in an option should not cost a print run, so warn and carry on
            default: Console.WriteLine($"Unknown option \"--{name}\" ignored."); break;
         }
      }

      if (s_probe > 0) {
         // A probe must never cut: if a variant desyncs the command parser, stray GS V pairs in the
         // raster can already trigger cuts on their own, and an intentional one only adds confusion.
         if (s_autoCut) Console.WriteLine("Auto-cut is ignored in probe mode.");
         s_autoCut = false;
         s_bulkMode = BulkMode.Off;
         Console.WriteLine($"Probe variant {s_probe}: {s_probeVariants[s_probe - 1]}");
         return true;
      }

      if (s_bulkMode == BulkMode.Page) {
         // The page area cannot exceed 512 dots on a TM-T88III; anything wider would be silently dropped
         if (s_widthDots > MaxPageModeWidthDots) {
            Console.WriteLine(
               $"Page mode supports at most {MaxPageModeWidthDots} dots of print width, but {s_widthDots} were requested. " +
               "Reduce the width or print without page mode."
            );
            return false;
         }

         Console.WriteLine($"Page mode enabled (band height {s_bandRows} dot rows).");
         Console.WriteLine(
            "Note: page mode is confirmed NOT to work on the Epson TM-T88III - that firmware does not " +
            "accept GS v 0 inside page mode and prints the raster as text. Use --bulk=download there. " +
            "On other printers, verify with --probe=2 first."
         );
      }
      else if (s_bulkMode == BulkMode.Download) {
         Console.WriteLine($"Download bit image mode enabled (band height {DownloadBandRows()} dot rows).");
      }

      if (s_bulkMode != BulkMode.Off) {
         // 360 and 512 are the only widths a TM-T88III can actually print; flag anything else, since a
         // mismatched width clips the right edge and distorts the aspect ratio.
         if (s_widthDots is not (360 or 512)) {
            Console.WriteLine(
               $"Note: {s_widthDots} dots is not a TM-T88III print width. Use 512 for 80 mm paper or 360 for 58 mm paper."
            );
         }

         // A bulk mode that the printer does not implement leaves the raster to be printed as text, and
         // dithered data statistically contains a stray GS V (1D 56) cut sequence every ~65 KB.
         if (s_autoCut) {
            Console.WriteLine(
               "Warning: combining a bulk mode with auto-cut is risky until the mode is confirmed working on " +
               "your printer. If the printer does not understand the commands, the raster is printed as text " +
               "and random cut sequences inside the image data can trigger the cutter. Verify with --probe first."
            );
         }
      }

      return true;
   }

   /// <summary>
   /// Lists the probe variants without printing anything.
   /// </summary>
   private static void PrintProbeVariants() {
      Console.WriteLine("Probe variants (run one at a time, e.g. --probe=1). Each prints a small test");
      Console.WriteLine("pattern and never cuts the paper:");
      for (int i = 0; i < s_probeVariants.Length; ++i) {
         Console.WriteLine($"  --probe={i + 1}   {s_probeVariants[i]}");
      }
   }

   /// <summary>
   /// Applies the bulk output mode.
   /// </summary>
   private static bool ApplyBulkMode(string value) {
      switch (value.ToLowerInvariant()) {
         case "off" or "none" or "standard": s_bulkMode = BulkMode.Off; return true;
         case "page" or "pagemode": s_bulkMode = BulkMode.Page; return true;
         case "download" or "dl": s_bulkMode = BulkMode.Download; return true;
         default:
            Console.WriteLine($"Invalid value \"{value}\" for --bulk; expected off, page or download.");
            return false;
      }
   }

   /// <summary>
   /// Applies the probe variant number.
   /// </summary>
   private static bool ApplyProbe(string value) {
      if (!int.TryParse(value, out int variant) || variant < 1 || variant > s_probeVariants.Length) {
         Console.WriteLine($"Invalid probe variant \"{value}\". Use --probe to list the available variants.");
         return false;
      }

      s_probe = variant;
      return true;
   }

   /// <summary>
   /// Interprets an option value as a boolean flag. A bare option (no "=value") means "on".
   /// </summary>
   private static bool IsFlagSet(string value, string name) {
      if (value.Length == 0) return true;
      if (int.TryParse(value, out int number)) return number != 0;

      Console.WriteLine($"Invalid value \"{value}\" for --{name}; expected 0 or 1. Treating it as enabled.");
      return true;
   }

   /// <summary>
   /// Applies the dither mode from its 1-based index.
   /// </summary>
   private static void ApplyDitherMode(string value) {
      if (!int.TryParse(value, out int ditherModeIndex)) return;

      if (ditherModeIndex < 1 || ditherModeIndex > Enum.GetValues<DitherMode>().Length) {
         Console.WriteLine($"Invalid dither mode index. Default value {s_ditherMode} will be used.");
      }
      else {
         s_ditherMode = (DitherMode) ditherModeIndex;
         Console.WriteLine($"Dither mode set to {s_ditherMode}.");
      }
   }

   /// <summary>
   /// Applies the gamma value (parsed with invariant culture).
   /// </summary>
   private static void ApplyGamma(string value) {
      if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double gamma)) return;

      if (gamma <= 0) Console.WriteLine($"Gamma value must be greater than 0. Default value {s_gamma.ToString(CultureInfo.InvariantCulture)} will be used.");
      else s_gamma = gamma;
   }

   /// <summary>
   /// Applies the print width in dots (must be a positive multiple of 8).
   /// </summary>
   private static void ApplyWidthDots(string value) {
      if (!int.TryParse(value, out int widthDots)) return;

      if (widthDots <= 0 || widthDots % 8 != 0)
         Console.WriteLine($"Width must be a positive multiple of 8. Default value {DefaultWidthDots} will be used.");
      else {
         s_widthDots = widthDots;
         Console.WriteLine($"Print width set to {s_widthDots} dots.");
      }
   }

   /// <summary>
   /// Applies the auto-cut flag (1 = cut, 0 = no cut).
   /// </summary>
   private static void ApplyAutoCut(string value) {
      if (int.TryParse(value, out int autoCut)) s_autoCut = autoCut != 0;
   }

   /// <summary>
   /// Applies the page mode band height in dot rows, clamped to the printer's page area limit.
   /// </summary>
   private static void ApplyBandRows(string value) {
      if (!int.TryParse(value, out int bandRows) || bandRows < 1 || bandRows > MaxBandRows) {
         Console.WriteLine($"Band height must be between 1 and {MaxBandRows} dot rows. Default value {DefaultBandRows} will be used.");
         return;
      }

      s_bandRows = bandRows;
   }

   /// <summary>
   /// Applies the dump target path, which diverts the byte stream to a file instead of the printer.
   /// </summary>
   private static void ApplyDumpPath(string value) {
      if (string.IsNullOrWhiteSpace(value)) {
         Console.WriteLine("--dump requires a file path, e.g. --dump=job.bin. It will be ignored.");
         return;
      }

      s_dumpPath = Path.GetFullPath(value);
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

      // Dither and pack the whole image once; the bulk paths below only slice this buffer
      byte[] imageData = PackBitmap(bitmap, out int rows);

      // Assemble the complete job — image plus trailer — so it can go out as ONE spool job. Two
      // separate SendBytes calls would be two Windows print jobs, which lets the spooler insert an
      // unbounded gap right after the last raster row (motor stops with paper under the head = a
      // visible stripe), and leaves the cut free to race the image.
      using MemoryStream job = new(imageData.Length + 4096);

      // ESC @ (Initialize): clears the buffer, selects Standard mode and restores the default motion
      // units (1/180" on the TM-T88III), so one motion unit equals one dot — which ESC W relies on.
      // Emitted exactly once: inside page mode ESC @ would cancel page mode.
      job.Write([0x1B, 0x40]);

      switch (s_bulkMode) {
         case BulkMode.Page: WritePageModeImage(job, imageData, rows); break;
         case BulkMode.Download: WriteDownloadModeImage(job, imageData, rows); break;
         default: WriteStandardModeImage(job, imageData, rows); break;
      }

      // Re-initialize before the trailer when a bulk mode was used. After the last band the printer is
      // back in Standard mode with an empty buffer, so this is a no-op — but if the printer did not
      // understand the bulk commands and its parser has desynced, it stops the cut command below from
      // being executed out of a broken state.
      if (s_bulkMode != BulkMode.Off) job.Write([0x1B, 0x40]);

      WriteTrailer(job);

      Emit(printerName, job);
   }

   /// <summary>
   /// Sends the assembled job to the printer, or writes it to the dump file if --dump was given.
   /// </summary>
   private static void Emit(string printerName, MemoryStream job) {
      byte[] escposJob = job.ToArray();

      // Divert to a file instead of the printer when --dump was given, so the byte stream can be
      // inspected without consuming paper
      if (s_dumpPath != null) {
         File.WriteAllBytes(s_dumpPath, escposJob);
         Console.WriteLine($"Wrote {escposJob.Length} bytes to \"{s_dumpPath}\" instead of printing.");
         return;
      }

      RawPrinter.SendBytes(printerName, escposJob);
   }

   /// <summary>
   /// Prints a single probe variant: a small built-in test pattern emitted through one specific ESC/POS
   /// command sequence. Used to find out which bulk-graphics sequence a given printer actually implements,
   /// one run at a time so a variant that desyncs the command parser cannot affect the other results.
   /// </summary>
   private static void RunProbe(string printerName) {
      byte[] pattern = BuildProbePattern(ProbeRows);

      using MemoryStream job = new(pattern.Length + 1024);

      // ESC @ (Initialize), then a plain text label so the printout says which variant produced it
      job.Write([0x1B, 0x40]);
      job.Write(Encoding.ASCII.GetBytes($"PROBE {s_probe}: {s_probeVariants[s_probe - 1].Split("  ")[0]}\n"));

      switch (s_probe) {
         case 1: // Control: plain GS v 0 in Standard mode
            WriteStandardModeImage(job, pattern, ProbeRows);
            break;

         case 2: // Page mode without ESC T
            job.Write([0x1B, 0x4C]);                  // ESC L  (select page mode)
            job.Write(PageModeAreaHeader(ProbeRows)); // ESC W  (set print area)
            job.Write(RasterHeader(ProbeRows));       // GS v 0 (buffers only in page mode)
            job.Write(pattern, 0, pattern.Length);
            job.WriteByte(0x0C);                      // FF     (print the page, back to Standard mode)
            break;

         case 3: // Page mode with ESC T — the sequence that failed on the TM-T88III
            job.Write([0x1B, 0x4C]);                  // ESC L
            job.Write([0x1B, 0x54, 0x00]);            // ESC T 0 (print direction; the prime suspect)
            job.Write(PageModeAreaHeader(ProbeRows)); // ESC W
            job.Write(RasterHeader(ProbeRows));       // GS v 0
            job.Write(pattern, 0, pattern.Length);
            job.WriteByte(0x0C);                      // FF
            break;

         case 4: // Page mode with the alternative print trigger
            job.Write([0x1B, 0x4C]);                  // ESC L
            job.Write(PageModeAreaHeader(ProbeRows)); // ESC W
            job.Write(RasterHeader(ProbeRows));       // GS v 0
            job.Write(pattern, 0, pattern.Length);
            job.Write([0x1B, 0x0C]);                  // ESC FF (print, but keep the page data)
            job.Write([0x1B, 0x53]);                  // ESC S  (back to Standard mode, clears the page)
            break;

         case 5: // Downloaded bit image — no page mode involved at all
            WriteColumnFormatBand(job, pattern, ProbeRows, 0, ProbeRows);
            break;
      }

      // Separate the pattern from whatever comes next; never a cut in probe mode
      job.Write([0x0A, 0x0A, 0x0A]);

      Emit(printerName, job);
   }

   /// <summary>
   /// Builds a packed test pattern in the same row-major layout <see cref="PackBitmap"/> produces.
   /// A solid bar, a full border, a diagonal and vertical stripes make any shifted, truncated or
   /// garbled output immediately obvious.
   /// </summary>
   private static byte[] BuildProbePattern(int rows) {
      byte[] data = new byte[BytesPerRow * rows];

      void Set(int x, int y) {
         if (x < 0 || x >= s_widthDots || y < 0 || y >= rows) return;
         data[y * BytesPerRow + (x >> 3)] |= (byte) (0x80 >> (x & 7));
      }

      // Solid black bar across the top quarter
      for (int y = 0; y < rows / 4; ++y)
         for (int x = 0; x < s_widthDots; ++x) Set(x, y);

      // One-dot border around the whole pattern
      for (int x = 0; x < s_widthDots; ++x) { Set(x, 0); Set(x, rows - 1); }
      for (int y = 0; y < rows; ++y) { Set(0, y); Set(s_widthDots - 1, y); }

      // Diagonal from just below the bar down to the bottom right
      for (int x = 0; x < s_widthDots; ++x)
         Set(x, rows / 4 + 2 + (x * (rows - rows / 4 - 4)) / s_widthDots);

      // Vertical stripes every 32 dots across the lower half
      for (int x = 0; x < s_widthDots; x += 32)
         for (int y = rows / 2; y < rows; ++y) Set(x, y);

      return data;
   }

   /// <summary>
   /// Scales the bitmap to the print width, converts it to grayscale and dithers it to 1 bit per pixel.
   /// </summary>
   /// <param name="input">The bitmap to convert.</param>
   /// <param name="rows">Receives the height of the packed image in dot rows.</param>
   /// <returns>The packed image data, row-major, <see cref="BytesPerRow"/> bytes per row.</returns>
   private static byte[] PackBitmap(Bitmap input, out int rows) {
      // Calculate scaling factor from input image width and maximum printer width
      float scaleFactor = ((float) s_widthDots) / input.Width;

      // Calculate height of scaled image
      rows = (int) Math.Round(input.Height * scaleFactor);

      // Create scaled copy of input image
      using Bitmap resized = new(input, new Size(s_widthDots, rows));

      int dataLen = BytesPerRow * rows;
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

      return imageData;
   }

   /// <summary>
   /// Builds the 8-byte GS v 0 raster header for the given number of dot rows.
   /// </summary>
   private static byte[] RasterHeader(int rows) => [
      0x1D, 0x76, 0x30, 0x00,             // GS 'v' '0' m  (m = 0: normal, no scaling)
      (byte) (BytesPerRow & 0xFF),        // xL (width in bytes, low byte)
      (byte) ((BytesPerRow >> 8) & 0xFF), // xH (width in bytes, high byte)
      (byte) (rows & 0xFF),               // yL (height in dot rows, low byte)
      (byte) ((rows >> 8) & 0xFF),        // yH (height in dot rows, high byte)
   ];

   /// <summary>
   /// Writes the image as a single GS v 0 raster command in Standard mode. The printer starts the
   /// motor as soon as the first rows arrive and must then be fed at motor speed, so on a slow link
   /// this is what produces intermittent-print banding — see <see cref="WritePageModeImage"/>.
   /// </summary>
   private static void WriteStandardModeImage(Stream job, byte[] imageData, int rows) {
      job.Write(RasterHeader(rows));
      job.Write(imageData, 0, imageData.Length);
   }

   /// <summary>
   /// Writes the image as a sequence of page mode bands. In page mode GS v 0 only stores the raster
   /// into the print buffer — the motor does not move — and FF then prints the whole band in a single
   /// continuous pass. This is the only bulk-graphics path the Epson TM-T88III has (the GS ( L /
   /// GS 8 L high-speed graphics commands were introduced with the TM-T88IV), and it is what removes
   /// the stripes caused by feeding a Standard mode raster through the small receive buffer.
   /// </summary>
   private static void WritePageModeImage(Stream job, byte[] imageData, int rows) {
      for (int bandStart = 0; bandStart < rows; bandStart += s_bandRows) {
         // The final band is short; its actual height must be used below, because GS v 0 data beyond
         // the print area is discarded and an area taller than the data feeds blank paper.
         int bandRows = Math.Min(s_bandRows, rows - bandStart);

         // ESC L (select page mode). No ESC T is sent: it is not needed, because the ESC W origin of
         // 0,0 already positions the band, and an unimplemented ESC T would leave its parameter byte to
         // be printed as text — which puts data in the print buffer and makes GS v 0 fall back to
         // "processes m and the following data as normal data", i.e. the whole raster as characters.
         job.Write([0x1B, 0x4C]);
         job.Write(PageModeAreaHeader(bandRows));

         // GS v 0 in page mode only fills the print buffer; nothing is printed and the motor stays put
         job.Write(RasterHeader(bandRows));

         // Dither() writes imageData strictly row-major, so a band is a plain contiguous slice. The
         // whole image is dithered in one pass, so error diffusion carries across band boundaries and
         // no tonal seam appears at the joins.
         job.Write(imageData, BytesPerRow * bandStart, BytesPerRow * bandRows);

         // FF (in page mode): prints the buffered area collectively in one continuous pass, deletes
         // the page data, resets the ESC W print area and returns to Standard mode. Note that ESC FF
         // (0x1B 0x0C) would instead *retain* the buffer and reprint this band on the next FF, so it
         // would need a following ESC S (0x1B 0x53).
         job.WriteByte(0x0C);
      }
   }

   /// <summary>
   /// Builds the 10-byte ESC W command that sets the page mode print area to the full print width and
   /// the given band height, with the logical origin at the top left of the printable area.
   /// </summary>
   /// <remarks>
   /// The parameters are motion units, not dots. They coincide only because ESC @ restored the default
   /// GS P (1/180" on the TM-T88III), which equals the dot pitch — so never emit GS P in this tool.
   /// </remarks>
   private static byte[] PageModeAreaHeader(int bandRows) => [
      0x1B, 0x57,                         // ESC W      (set print area in page mode)
      0x00, 0x00,                         //   xL, xH   (horizontal logical origin = 0)
      0x00, 0x00,                         //   yL, yH   (vertical logical origin = 0)
      (byte) (s_widthDots & 0xFF),        //   dxL      (area width = print width in dots)
      (byte) ((s_widthDots >> 8) & 0xFF), //   dxH
      (byte) (bandRows & 0xFF),           //   dyL      (area height = dot rows in this band)
      (byte) ((bandRows >> 8) & 0xFF),    //   dyH
   ];

   /// <summary>
   /// Returns the band height in dot rows for the downloaded bit image path, capped by the download
   /// buffer and rounded down to a whole number of bytes.
   /// </summary>
   private static int DownloadBandRows() {
      // GS * takes x bytes horizontally and y bytes (= y * 8 dot rows) vertically, with x * y <= 1536
      int maxRows = 8 * (MaxDownloadBytes / BytesPerRow);
      int bandRows = Math.Min(s_bandRows, maxRows);

      bandRows -= bandRows % 8; // y counts whole bytes, so the height must be a multiple of 8
      return Math.Max(8, bandRows);
   }

   /// <summary>
   /// Writes the image as a sequence of downloaded bit images. GS * uploads a band into the printer's
   /// 12 KB download buffer with the motor stopped, and GS / then prints it straight out of that buffer.
   /// This gives the same decoupling of data transfer from paper motion as page mode, but uses only
   /// commands that exist on pre-2004 printers such as the Epson TM-T88III.
   /// </summary>
   private static void WriteDownloadModeImage(Stream job, byte[] imageData, int rows) {
      int bandRows = DownloadBandRows();

      for (int bandStart = 0; bandStart < rows; bandStart += bandRows) {
         // The band height must stay a multiple of 8, so a short final band is padded with blank rows
         // rather than shortened. WriteColumnFormatBand treats rows past the image as white.
         int remaining = rows - bandStart;
         WriteColumnFormatBand(job, imageData, rows, bandStart, Math.Min(bandRows, (remaining + 7) / 8 * 8));
      }
   }

   /// <summary>
   /// Defines one band as a downloaded bit image and prints it.
   /// </summary>
   /// <remarks>
   /// GS * expects <b>column format</b>: for each dot column from left to right, y bytes from top to
   /// bottom, with the MSB of each byte being the topmost of its eight dots. That is transposed from the
   /// row-major layout <see cref="Dither"/> produces, so the bits are re-gathered here. Never emit ESC @
   /// or ESC &amp; between GS * and GS /: both clear the definition.
   /// </remarks>
   private static void WriteColumnFormatBand(Stream job, byte[] imageData, int rows, int bandStart, int bandRows) {
      int verticalBytes = bandRows / 8;

      job.Write([
         0x1D, 0x2A,             // GS '*'  (define downloaded bit image)
         (byte) BytesPerRow,     //   x     (horizontal size in bytes)
         (byte) verticalBytes,   //   y     (vertical size in bytes, i.e. y * 8 dot rows)
      ]);

      for (int col = 0; col < s_widthDots; ++col) {
         int columnByte = col >> 3, columnMask = 0x80 >> (col & 7);

         for (int vb = 0; vb < verticalBytes; ++vb) {
            byte b = 0;
            for (int bit = 0; bit < 8; ++bit) {
               int row = bandStart + vb * 8 + bit;
               // Rows past the end of the image stay white, which pads the final band
               if (row < rows && (imageData[row * BytesPerRow + columnByte] & columnMask) != 0) {
                  b |= (byte) (0x80 >> bit);
               }
            }
            job.WriteByte(b);
         }
      }

      // GS / m (m = 0: normal size). Feeds exactly as much paper as the image needs, regardless of the
      // line spacing, so consecutive bands butt together without any line feed in between.
      job.Write([0x1D, 0x2F, 0x00]);
   }

   /// <summary>
   /// Writes the trailing paper handling commands: either an automatic cut or a few line feeds.
   /// </summary>
   private static void WriteTrailer(Stream job) {
      if (s_autoCut) {
         // Two concatenated ESC/POS commands:
         //   ESC d n  (0x1B 0x64 0x06): "Print and Feed n Lines" — advances the paper by 6 lines
         //            so the printed output clears the cutter blade before cutting.
         //   GS  V 0  (0x1D 0x56 0x00): "Select Cut Mode and Cut Paper" with mode 0 = full cut —
         //            drives the built-in guillotine cutter to sever the paper completely.
         job.Write([0x1B, 0x64, 0x06, 0x1D, 0x56, 0x00]);
      }
      else {
         // Four line feeds so the output is visible and the paper can be torn off
         job.Write([0x0A, 0x0A, 0x0A, 0x0A]);
      }
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

