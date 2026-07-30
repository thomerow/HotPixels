<p align="center">
  <img src="logo.svg" alt="HotPixels logo" width="320">
</p>

# HotPixels

**HotPixels** is a small command‑line tool that converts images into ESC/POS raster graphics and prints them on a thermal printer.  
It runs on **Windows and Linux** and supports multiple dithering algorithms and optional gamma correction to fine‑tune brightness and contrast.

---

## ✨ Features

- Converts images to ESC/POS **raster format** (`GS v 0`)
- Multiple **dithering modes** (error‑diffusion + ordered dithers)
- Optional **gamma correction**
- Configurable **print width** in dots
- Optional **automatic paper cut** (requires a cutter, e.g. Epson TM‑T88III)
- Automatically rotates landscape images to portrait
- Prints to a **print queue**, a **device node** or a **network printer**, on Windows and Linux alike

---

## 📖 Usage

```
HotPixels <target> <imagePath> [options]
```

### 📥 Positional arguments

- **target**  
  Where to print. The transport follows from the shape of the value:

  | Shape | Transport | Examples |
  |---|---|---|
  | `host:port` | TCP socket (network printers listen on port 9100) | `192.168.1.50:9100` |
  | An absolute path | Written to directly | `/dev/usb/lp0`, `/dev/lp0`, `C:\out.bin` |
  | Anything else | A print queue — the Windows spooler, or CUPS via `lp -o raw` | `EPSON TM-T88III`, `\\server\printer` |

  Use quotes if the value contains spaces.  
  Run the program without arguments to list the targets available on your machine.

  Writing to an absolute path is also the easiest way to inspect the generated ESC/POS bytes without
  using paper. IPv6 literals are not supported as network targets.

- **imagePath**  
  Path to the image file.

### ⚙️ Options

Options may appear anywhere on the command line, and both `--name=value` and `--name value` are accepted.

| Option | Value | Default |
|---|---|---|
| `--dither=MODE` | Dithering algorithm, either by **name** (`Jarvis`, case-insensitive) or by its **number**. Run the program without arguments to list all modes | `2` (`Jarvis`) |
| `--gamma=F` | Gamma correction, a number greater than 0. Lower = brighter output | `0.75` |
| `--width=N` | Print width in dots, a positive multiple of 8. Must match the physical paper width your printer is configured for (e.g. 384 for 58 mm, 512 for 80 mm) | `384` |
| `--cut[=0\|1]` | Send an automatic paper-cut command (`ESC d 6` + `GS V 0`) after printing. Requires a printer with a built-in guillotine cutter (e.g. Epson TM‑T88III) | off |
| `--help`, `-h`, `/?` | Show usage | — |

A bare `--cut` means on. To set it explicitly, use the inline form `--cut=0` or `--cut=1` — `--cut` never
consumes the following argument, because that would be ambiguous with the positional arguments.

An invalid value aborts with an error and exit code **1** without printing anything, so a typo cannot
cost you a misprint.

---

## 📚 Examples

### 🖨️ Print using default settings
```powershell
HotPixels "EM5820" image.png
```

### 🖨️ Print with a specific dither mode
```powershell
HotPixels "My Thermal Printer" photo.jpg --dither=Stucki
```

### 🖨️ Print with dither mode + custom gamma
```powershell
HotPixels "ESC POS USB" logo.bmp --dither=Jarvis --gamma=0.6
```

### 🖨️ Print with custom width (512 dots) and auto-cut enabled
```powershell
HotPixels "Epson TM-T88III" receipt.png --dither=Stucki --width=512 --cut
```

### 🐧 Print to a USB printer on Linux
```bash
HotPixels /dev/usb/lp0 photo.jpg --dither=Atkinson
```

### 🌐 Print to a network printer
```bash
HotPixels 192.168.1.50:9100 photo.jpg --width=512
```

### 🔍 Inspect the generated ESC/POS bytes without using paper
```bash
HotPixels /tmp/out.bin photo.jpg --width=512
xxd /tmp/out.bin | head
```

---

## 🛠️ Requirements

- Windows or Linux  
- .NET 10 or newer  
- An ESC/POS‑compatible thermal printer

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

## 🐧 Setting Up an ESC/POS Thermal Printer on Linux

You have two options. The device node is the simpler one and needs no configuration at all.

### Option A: write to the device node

A USB printer handled by the `usblp` kernel driver shows up as `/dev/usb/lp0`, and a parallel port as
`/dev/lp0`. Check what is there:

```bash
ls -l /dev/usb/lp* /dev/lp* 2>/dev/null
```

Those nodes usually belong to group `lp`, so add yourself to it and log in again:

```bash
sudo usermod -aG lp $USER
```

Then print straight to it:

```bash
HotPixels /dev/usb/lp0 image.png
```

### Option B: a raw CUPS queue

Use this for network printers, or when you want the printer shared. The queue must be **raw** so that
CUPS passes the bytes through untouched — this is the exact counterpart of the Windows
"Generic / Text Only" advice above:

```bash
# find the device URI
lpinfo -v

# create a raw queue (no driver, no filtering)
sudo lpadmin -p TM-T88III -E -v usb://EPSON/TM-T88III -m raw
```

Then use the queue name as the target:

```bash
HotPixels TM-T88III image.png
```

HotPixels invokes `lp -d <queue> -o raw` for queue targets. The `-o raw` is what stops CUPS from
running the data through its filter chain and mangling the ESC/POS control codes.

---

## 🧪 Tests

```bash
dotnet test
```

The suite drives the built executable as a child process and covers the command line, the target
resolution rules and the generated ESC/POS bytes. No printer is involved — everything goes to a
temporary file or a deliberately unreachable target.

See [tests/README.md](./tests/README.md) for what each fixture covers and how to update a golden hash.

---

## 📄 License

This project is provided under the MIT License. See [LICENSE.md](./LICENSE.md) for details.
