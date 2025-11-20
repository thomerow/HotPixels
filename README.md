# HotPixels

**HotPixels** is a simple cross-platform command-line tool that converts images into ESC/POS raster graphics for thermal printers.  
It supports multiple dithering algorithms and optional gamma correction to fine‑tune brightness and contrast.

---

## Features

- Converts input images to ESC/POS **raster format** (`GS v 0`)
- Supports many **dither modes** (error diffusion & ordered dithering)
- Optional **gamma correction** for printer-specific brightness control
- Automatically rotates landscape images to portrait so they fit better on receipt paper

---

## Usage

```
HotPixels <imagePath> [ditherModeIndex] [gamma]
```

### Arguments

- **imagePath**  
  The path to the image you want to print.

- **ditherModeIndex** (optional, 1‑based integer)  
  Selects a dither algorithm.  
  Run `HotPixels` with no arguments to list all modes.

- **gamma** (optional, float > 0)  
  Adjusts perceived brightness.  
  Lower values → brighter output.

---

## Examples

### Print using default settings
```
HotPixels input.png
```

### Print using a specific dithering mode
```
HotPixels input.jpg 3
```

### Print with dithering mode + custom gamma
```
HotPixels image.bmp 2 0.75
```

---

## Requirements

- .NET 8 or newer  
- An ESC/POS‑compatible thermal printer

---

## License

MIT 
