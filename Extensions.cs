using HotPixels.Imaging.Dithering;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HotPixels.Imaging;

/// <summary>
/// Useful extension methods
/// </summary>
internal static class Extensions {

   /// <summary>
   /// Returns whether the dither mode is an ordered dither mode.
   /// </summary>
   public static bool IsOrderedDither(this DitherMode mode) => mode >= DitherMode.Bayer2x2;

   /// <summary>
   /// Calculates the perceived brightness of a color.
   /// </summary>
   /// <param name="color">The color.</param>
   /// <param name="gamma">The gamma correction factor. Default is 0.75. Higher values make the image darker.</param>
   /// <returns>The perceived brightness as a byte value (0-255).</returns>
   public static byte GetPerceivedBrightness(this Color color, double gamma = Program.DefaultGamma) {
      double brightness = color.R * 0.299 + color.G * 0.587 + color.B * 0.114;
      brightness = Math.Pow(brightness / 255.0, gamma) * 255.0;
      return (byte) brightness;
   }
}