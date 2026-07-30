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
- Configurable **print width** in dots
- Optional **automatic paper cut** (requires a cutter, e.g. Epson TM‑T88III)
- Optional **page mode (bulk) printing** — buffers each band inside the printer and prints it in one continuous pass, which eliminates intermittent‑print banding on the Epson TM‑T88III
- Automatically rotates landscape images to portrait
- Prints directly to any installed Windows ESC/POS printer

---

## 📖 Usage

```
HotPixels <printerName> <imagePath> [ditherModeIndex] [gamma] [widthDots] [autoCut] [--options]
```

### 📥 Arguments

- **printerName**  
  Name of the installed ESC/POS printer.  
  Use quotes if the name contains spaces.  
  Run the program without arguments to list all installed printers.

- **imagePath**  
  Path to the image file.

- **ditherModeIndex** (optional, 1‑based integer)  
  Selects a dithering algorithm.  
  Run the program without arguments to list all modes.

- **gamma** (optional, float > 0)  
  Adjusts perceived brightness.  
  Lower = brighter output.  
  Default: **0.75**

- **widthDots** (optional, positive multiple of 8)  
  Sets the print width in dots. Must match the printer's resolution *and* the physical paper width —
  note that the Epson TM‑T88x series is **180 dpi**, while most inexpensive thermal printers are 203 dpi:

  | Printer / paper | widthDots |
  |---|---|
  | Generic 203 dpi, 58 mm | **384** (default) |
  | Generic 203 dpi, 80 mm | **576** |
  | Epson TM‑T88III/IV, 58 mm (factory spacer) | **360** (50.8 mm print width) |
  | Epson TM‑T88III/IV, 80 mm | **512** (72.2 mm print width) |

  A value wider than the printable area is silently clipped on the right and distorts the aspect ratio.  
  Default: **384**

- **autoCut** (optional, `0` or `1`)  
  Set to `1` to send an automatic paper-cut command (`ESC d 6` + `GS V 0`) after printing.  
  Requires a printer with a built-in guillotine cutter (e.g. Epson TM‑T88III).  
  Default: **0** (no cut)

### ⚙️ Named options

Options may appear anywhere on the command line and **override** the positional arguments.
The positional form above continues to work unchanged.

| Option | Meaning |
|---|---|
| `--dither=N` | Same as `ditherModeIndex` |
| `--gamma=F` | Same as `gamma` |
| `--width=N` | Same as `widthDots` |
| `--cut[=0\|1]` | Same as `autoCut`; a bare `--cut` means on |
| `--bulk=MODE` | Bulk printing mode: `off` (default), `page`, or `download` — see below |
| `--pagemode` | Alias for `--bulk=page` |
| `--band=N` | Bulk band height in dot rows, `1`…`831`. Default **384**. `download` caps it at `8 × (1536 ÷ bytesPerRow)` — 192 rows at 512 dots |
| `--probe[=N]` | Print one small test pattern through a single command sequence, to find out which bulk mode the printer implements. Never cuts. Omit `N` to list the variants |
| `--dump=PATH` | Write the generated ESC/POS byte stream to a file **instead of** printing |
| `--help`, `-h`, `/?` | Show usage |

### 📦 Bulk modes

Both bulk modes upload a band into printer memory **while the motor is stopped**, then print it from
memory in one continuous pass. That decoupling is what removes intermittent-print banding.

| Mode | Commands | Band limit at 512 dots | Notes |
|---|---|---|---|
| `off` | `GS v 0` | — | One monolithic raster. Printing follows data arrival, so a slow link causes banding |
| `page` | `ESC L` / `ESC W` / `GS v 0` / `FF` | **831** dot rows (~117 mm) | Fewest seams — but **does not work on the TM-T88III** (see below) |
| `download` | `GS *` / `GS /` | **192** dot rows (~27 mm) | Uses the 12 KB download buffer. **This is the mode that works on the TM-T88III** |

Neither mode is enabled by default: inexpensive generic printers frequently implement neither, and an
unsupported command sequence makes the printer print the raster as **text characters**. Use `--probe`
to find out what a given printer supports before relying on a mode.

> **Measured on a TM-T88III (parallel, 80 mm):** probe variants 1 (`GS v 0`) and 5 (`GS *` / `GS /`)
> print correctly; variants 2, 3 and 4 all fail. That firmware does not accept `GS v 0` inside page
> mode, with or without `ESC T` — so `--bulk=download` is the working bulk mode on this model.
> The `page` mode is kept for other printers that do implement it.

---

## 📚 Examples

### 🖨️ Print using default settings
```powershell
HotPixels "EM5820" image.png
```

### 🖨️ Print with a specific dither mode
```powershell
HotPixels "My Thermal Printer" photo.jpg 3
```

### 🖨️ Print with dither mode + custom gamma
```powershell
HotPixels "ESC POS USB" logo.bmp 2 0.6
```

### 🖨️ Print with custom width (512 dots) and auto-cut enabled
```powershell
HotPixels "Epson TM-T88III" receipt.png 3 0.75 512 1
```

### 🖨️ Print on an Epson TM-T88III without banding
```powershell
HotPixels "Epson TM-T88III" photo.jpg --width=512 --pagemode --cut
```

### 🔍 Inspect the generated ESC/POS bytes without printing
```powershell
HotPixels "Epson TM-T88III" photo.jpg --width=512 --pagemode --dump=job.bin
Format-Hex job.bin -Count 32
```

---

## 🩹 Fixing banding / stripes on an Epson TM-T88III

If the printer "stutters" — the motor pauses briefly at regular intervals, leaving faint horizontal
white stripes — the cause is almost always that **the image data cannot reach the print head fast enough**.

In the default (standard mode) path, `GS v 0` couples data arrival to paper motion: the motor starts as
soon as the first raster rows arrive and must then keep consuming rows at motor speed. When the receive
buffer drains, the motor stops. Epson documents exactly this in the TM-T88IV Technical Reference Guide:

> Depending on print conditions such as print duty, print head temperature, or data transmission speed,
> print speed is automatically adjusted which may cause white lines due to intermittent print
> (the motor sometimes stops).

At 512 dots and 180 dpi, a TM-T88III running at its full 150 mm/s needs roughly **68 KB/s sustained**
(≈1063 dot rows per second × 64 bytes), and its receive buffer is only 4 KB — about 9 mm of paper.

### Step 1: find out which bulk mode your printer speaks

Not every printer implements every ESC/POS bulk-graphics command, and an unsupported sequence fails
badly: the printer prints the raster as text characters, and stray `GS V` byte pairs inside the image
data can even trigger the cutter at random. So probe first — each run prints one small pattern
(~1 cm of paper) and **never cuts**:

```powershell
HotPixels "Epson TM-T88III" --probe          # list the variants
HotPixels "Epson TM-T88III" --width=512 --probe=1   # ... then 2, 3, 4, 5
```

The pattern is a solid black bar, a one-dot border, a diagonal and vertical stripes — anything
truncated, shifted or garbled is immediately obvious.

| Variant | Sequence | If it prints correctly | On a TM-T88III |
|---|---|---|---|
| 1 | `GS v 0` in Standard mode | Control — **must** work. If it doesn't, the problem is the cable/handshake, not the command set | ✅ works |
| 2 | `ESC L` / `ESC W` / `GS v 0` / `FF` | Use `--bulk=page` | ❌ fails |
| 3 | as 2, plus `ESC T 0` | `ESC T` is supported too (informational) | ❌ fails |
| 4 | as 2, but `ESC FF` / `ESC S` instead of `FF` | Page mode works, but needs the alternative print trigger | ❌ fails |
| 5 | `GS *` / `GS /` | Use `--bulk=download` | ✅ works |

### Step 2: enable the mode that worked

```powershell
HotPixels "Epson TM-T88III" photo.jpg --width=512 --bulk=download   # TM-T88III: this one
HotPixels "Epson TM-T88III" photo.jpg --width=512 --bulk=page       # only if variant 2 worked
```

In ESC/POS **page mode**, `GS v 0` only *stores* the raster in the print buffer — the motor does not move
at all — and `FF` then prints the whole band in a single continuous pass. The **download** mode achieves
the same thing with `GS *` (upload into the 12 KB download buffer) followed by `GS /` (print it from
there), using only commands that exist on pre-2004 printers. Either way, data starvation becomes
impossible *within* a band, so the stripes disappear.

This does not make printing *faster*: transfer and printing now strictly alternate. It makes it **smooth**,
and it moves the remaining motor stops to a few predictable band boundaries (one every ~54 mm at the
default band height) instead of one every ~9 mm.

Tune the band height with `--band=N` if seams are still visible:

- `--band=192` for very dark, high-duty images
- `--band=831` for sparse line art (831 dot rows is the TM-T88III page area limit)

> **Note:** bulk modes are **opt-in** because inexpensive generic printers often implement neither
> `ESC L` / `ESC W` nor `GS *`, and will print the raster bytes as garbage characters instead.
> Avoid combining a bulk mode with `--cut` until you have confirmed the mode works via `--probe`.

### Why not a "send the whole image, then print" command?

The TM-T88III predates the modern `GS ( L` / `GS 8 L` graphics commands (Function 112 = store graphics,
Function 50 = print graphics). Epson's TM-T88IV Technical Reference Guide lists *"High-Speed Graphic
Printing"* under **Additional Functions and Functional Improvements** — i.e. it arrived with the T88IV.
For the same reason `GS ( K` Function 50 (print speed) and Function 97 (head energizing) are unavailable:
on the TM-T88III head energizing is fixed to two-part and density is set by DIP switches only.
Page mode is the only bulk-graphics path this model has.

### Hardware checks

If stripes persist even with `--pagemode`, the remaining causes are power and heat, not software:

1. **Power supply.** Use a genuine PS-180-class 24 V unit with headroom. A weak brick sags under the
   peak current of a dense dithered image and the printer's own protection halts the motor — a symptom
   nearly indistinguishable from data starvation.
2. **DIP SW 2-3 / 2-4 — print density.** `OFF/OFF` = level 1 (regular), `ON/OFF` = level 2,
   `OFF/ON` = level 3 (heavy), **`ON/ON` = low-power mode at a constant 70 mm/s**. Low-power mode halves
   the required data rate and is this model's only "keep the print speed constant by setting it lower" lever.
   Setting it is also a good way to *confirm* the diagnosis: if the stripes vanish, the bottleneck was data rate.
3. **DIP SW 1-2 = OFF** for the 4 KB receive buffer (`ON` gives only 45 bytes). To read the current
   settings, run a self-test: open the roll paper cover, hold **FEED**, and switch the printer on.
4. **Parallel models:** the forward channel is Centronics *compatibility mode* only — no ECP/EPP. If you
   are using a USB→parallel adapter, suspect it first: those bridges deliver data in bursts with idle gaps.
   On a real LPT port, setting the port to ECP/EPP in the BIOS can still help, because the chipset then
   drives the STROBE/BUSY handshake in hardware with a FIFO instead of the CPU toggling lines.
5. **Serial models:** use the highest baud rate both ends support. Epson's own note — *"a slow baud rate
   is a probable cause of intermittent printing"* — describes this bug directly.
6. **Windows queue:** in the printer's Advanced properties, prefer *"Print directly to the printer"*.

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
HotPixels "ESC POS USB" image.png
```

### Why "Generic / Text Only"?

ESC/POS is a **raw byte protocol**, not a page-layout language.  
Windows GDI drivers do not understand ESC/POS image commands, so you *must* use a raw-printing queue. The **Generic / Text Only** driver creates exactly that.

---

## 📄 License

This project is provided under the MIT License. See [LICENSE.md](./LICENSE.md) for details.
