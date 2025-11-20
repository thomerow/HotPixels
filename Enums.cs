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
}