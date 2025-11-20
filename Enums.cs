using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyEscPosTest.Enums;

/// <summary>
/// Dithering-Modi für die Bildverarbeitung
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