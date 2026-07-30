using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace HotPixels.Tests;

/// <summary>
/// A scratch directory plus the deterministic test image the tests print.
/// </summary>
/// <remarks>
/// The image is generated rather than committed, so the repository stays free of binaries and the input
/// is guaranteed to be identical on every machine. PNG is lossless, so the pixels the tool sees do not
/// depend on the encoder version.
/// </remarks>
[SetUpFixture]
public sealed class TestWorkspace {

   /// <summary>Size of the generated test image. Portrait, so the tool does not rotate it.</summary>
   public const int ImageWidth = 300;
   public const int ImageHeight = 1000;

   private static string s_directory = null!;

   /// <summary>Path of the generated test image.</summary>
   public static string TestImage { get; private set; } = null!;

   /// <summary>Returns a path inside the scratch directory, unique to the calling test.</summary>
   public static string OutputPath(string name) => Path.Combine(s_directory, name);

   [OneTimeSetUp]
   public void CreateWorkspace() {
      s_directory = Path.Combine(Path.GetTempPath(), "HotPixels.Tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(s_directory);

      TestImage = Path.Combine(s_directory, "test.png");
      CreateTestImage(TestImage);
   }

   [OneTimeTearDown]
   public void RemoveWorkspace() {
      try {
         Directory.Delete(s_directory, recursive: true);
      }
      catch (IOException) {
         // A leftover temp directory is not worth failing a test run over
      }
   }

   /// <summary>
   /// Writes a test image with a vertical gradient, a solid black block and one-dot horizontal lines.
   /// The mix matters: the gradient exercises dithering, the solid block exercises full-black packing,
   /// and the thin lines make any vertical scaling error obvious.
   /// </summary>
   private static void CreateTestImage(string path) {
      using var image = new Image<Rgb24>(ImageWidth, ImageHeight);

      image.ProcessPixelRows(accessor => {
         for (int y = 0; y < accessor.Height; ++y) {
            Span<Rgb24> row = accessor.GetRowSpan(y);

            byte value;
            if (y < 500) value = (byte) (255 * y / 500);       // gradient, black to white
            else if (y < 700) value = 255;                     // white gap
            else value = (byte) ((y % 10 == 0) ? 0 : 255);     // one-dot lines every 10 rows

            for (int x = 0; x < row.Length; ++x) row[x] = new Rgb24(value, value, value);
         }

         // Solid black block in the white gap
         for (int y = 520; y < 680; ++y) {
            Span<Rgb24> row = accessor.GetRowSpan(y);
            for (int x = 20; x < ImageWidth - 20; ++x) row[x] = new Rgb24(0, 0, 0);
         }
      });

      image.SaveAsPng(path);
   }
}
