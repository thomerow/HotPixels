<p align="center">
  <img src="logo.svg" alt="HotPixels logo" width="320">
</p>

# HotPixels

**HotPixels** is a small Windows command‑line tool that converts images into ESC/POS raster graphics and prints them on a thermal printer.  
It supports multiple dithering algorithms and optional gamma correction to fine‑tune brightness and contrast.

---

## ✨ Features

- Converts images to ESC/POS **raster format** (`GS v 0`)
- Multiple **dithering modes** (error‑diffusion + ordered dithers)
- Optional **gamma correction**
- Automatically rotates landscape images to portrait
- Prints directly to any installed Windows ESC/POS printer

---

## 📖 Usage

```
HotPixels <imagePath> <printerName> [ditherModeIndex] [gamma]
```

### 📥 Arguments

- **imagePath**  
  Path to the image file.

- **printerName**  
  Name of the installed ESC/POS printer.  
  Use quotes if the name contains spaces.  
  Run the program without arguments to list all installed printers.

- **ditherModeIndex** (optional, 1‑based integer)  
  Selects a dithering algorithm.  
  Run the program without arguments to list all modes.

- **gamma** (optional, float > 0)  
  Adjusts perceived brightness.  
  Lower = brighter output.  
  Default: **0.75**

---

## 📚 Examples

### 🖨️ Print using default settings
```powershell
HotPixels image.png "EM5820"
```

### 🖨️ Print with a specific dither mode
```powershell
HotPixels photo.jpg "My Thermal Printer" 3
```

### 🖨️ Print with dither mode + custom gamma
```powershell
HotPixels logo.bmp "ESC POS USB" 2 0.6
```

---

## 🛠️ Requirements

- Windows  
- .NET 8 or newer  
- An ESC/POS‑compatible thermal printer installed in the system

---

## 📄 License

This project is provided under the MIT License. See [LICENSE.md](./LICENSE.md) for details.
