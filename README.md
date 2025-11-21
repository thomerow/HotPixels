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

## 🖨️ Setting Up an ESC/POS Thermal Printer on Windows

Before using **HotPixels**, your ESC/POS printer must be installed in Windows.  
Most inexpensive 58 mm / 80 mm thermal printers identify as a USB serial device with a vendor-specific driver, but you can safely install them as a **Generic / Text Only** printer.

Follow these steps:

### 1. Connect the printer via USB

- Plug the printer into your Windows machine.
- Windows will usually show it as a USB printing device (often with names like *USB Printing Support*, *GXMcu Micro-Printer*, *POS Printer*, etc.).

### 2. Open the Windows printer management dialog

- Open **Control Panel**
- Go to **Devices and Printers**
- Click **Add Printer**

### 3. Add the printer manually

- Choose **The printer that I want isn’t listed**
- Select **Add a local printer or network printer with manual settings**
- For the port, choose the automatically created **USB00X** port (e.g. USB001, USB002)

### 4. Select the driver

- In the manufacturer list, select **Generic**
- In the model list, choose **Generic / Text Only**
- Finish the installation

### 5. Rename the printer (optional but recommended)

Give it a meaningful name like:

- `EM5820`
- `Thermal Receipt Printer`
- `ESC POS USB`

You will use this exact name in the HotPixels command line:

```powershell
HotPixels image.png "ESC POS USB"
```

### Why "Generic / Text Only"?

ESC/POS is a **raw byte protocol**, not a page-layout language.  
Windows GDI drivers do not understand ESC/POS image commands, so you *must* use a raw-printing queue. The **Generic / Text Only** driver creates exactly that.

---

## 📄 License

This project is provided under the MIT License. See [LICENSE.md](./LICENSE.md) for details.
