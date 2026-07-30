using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace HotPixels.Imaging;

/// <summary>
/// Thrown when the image file cannot be read or decoded.
/// </summary>
internal sealed class ImageLoadException(Exception inner) : Exception(inner.Message, inner);

/// <summary>
/// Loads an image and turns it into the grayscale matrix the dithering works on.
/// </summary>
/// <remarks>
/// This is the only place in the project that touches an imaging library, which is what keeps the rest
/// of the code platform independent. ImageSharp is used rather than System.Drawing because the latter
/// throws <see cref="PlatformNotSupportedException"/> on anything but Windows since .NET 7.
/// </remarks>
internal static class ImageLoader {

   /// <summary>
   /// Loads an image, rotates it to portrait if needed, scales it to the print width and converts it to
   /// perceived-brightness values.
   /// </summary>
   /// <param name="path">Path to the image file.</param>
   /// <param name="widthDots">Target width in dots; the height follows from the aspect ratio.</param>
   /// <param name="gamma">Gamma correction factor. Lower values make the image brighter.</param>
   /// <param name="rotateIfWide">If true, rotates the image by 90° if it is wider than tall.</param>
   /// <param name="rows">Receives the height of the scaled image in dot rows.</param>
   /// <returns>The brightness of every pixel (0-255), indexed as [x, y].</returns>
   public static float[,] LoadGrayscale(string path, int widthDots, double gamma, bool rotateIfWide, out int rows) {
      // Rgb24 keeps the three bytes GetPerceivedBrightness needs and drops any alpha channel, which
      // matches the previous behaviour: a transparent pixel is judged by the color underneath it.
      Image<Rgb24> loaded;
      try {
         loaded = Image.Load<Rgb24>(path);
      }
      catch (Exception ex) {
         throw new ImageLoadException(ex);
      }

      using Image<Rgb24> image = loaded;

      // No AutoOrient() call on purpose: GDI+ ignored the EXIF orientation tag as well, so honoring it
      // here would silently start rotating photos that used to print upright.

      // Rotate image by 90° if it is wider than tall
      if (rotateIfWide && (image.Width > image.Height)) {
         image.Mutate(x => x.Rotate(RotateMode.Rotate90));
      }

      // Calculate height of scaled image from the scaling factor. A local is needed because an out
      // parameter cannot be captured by the lambda below.
      int scaledHeight = (int) Math.Round(image.Height * (((float) widthDots) / image.Width));
      rows = scaledHeight;

      // Scale to exactly the print width. The sampler is stated explicitly rather than left to the
      // default so that a change to it is visible in the diff, since it noticeably affects dithering.
      image.Mutate(x => x.Resize(new ResizeOptions {
         Size = new Size(widthDots, scaledHeight),
         Sampler = KnownResamplers.Bicubic,
         Mode = ResizeMode.Stretch,
      }));

      float[,] grayData = new float[image.Width, image.Height];

      // Read row by row rather than pixel by pixel; a per-pixel accessor is an order of magnitude slower
      image.ProcessPixelRows(accessor => {
         for (int y = 0; y < accessor.Height; ++y) {
            Span<Rgb24> row = accessor.GetRowSpan(y);
            for (int x = 0; x < row.Length; ++x) {
               grayData[x, y] = row[x].GetPerceivedBrightness(gamma);
            }
         }
      });

      return grayData;
   }
}
