using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HotPixels.Imaging.Dithering;

/// <summary>
/// Dithering-Modi for image processing.
/// </summary>
enum DitherMode {
   FloydSteinberg = 1,
   Jarvis,
   Stucki,
   Burkes,
   SierraLite,
   Atkinson,
   Bayer2x2,
   Bayer4x4,
   Bayer8x8,
   Halftone4x4,
}