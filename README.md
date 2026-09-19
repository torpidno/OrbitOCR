# OrbitOCR 🪐🔍

> Lightweight, high-performance, open-source offline screen OCR & visual search utility inspired by Google Pixel's **"Circle to Search"** for Windows 10 & 11.

---

## Overview

**OrbitOCR** runs as an ultra-compact background utility in the Windows system tray. With a single global hotkey (`Ctrl + Shift + S` by default), it freezes your desktop across all connected monitors, dims the screen with a subtle translucent tint, and lets you circle or drag-select any text, diagram, or UI element.

Using Windows' native offline OCR engine (`Windows.Media.Ocr.OcrEngine`), OrbitOCR extracts text instantly without internet access or external binaries, and presents a sleek floating action pill menu right next to your selection.

---

## ✨ Features

- **⚡ System Tray & Background Lifecycle**
  - Stays quietly in the notification tray (<30 MB RAM idle).
  - Context menu with `Trigger Snip`, `Settings...`, `About OrbitOCR`, and `Exit`.
  - Single-instance protection using a named session mutex.
- **⌨️ Global Win32 Hotkey Hook**
  - Listens globally via native Win32 `RegisterHotKey` API without polling or background loops.
  - Fully configurable combinations (e.g. `Ctrl + Shift + S`, `Alt + S`, `PrintScreen`).
- **🖥️ Multi-Monitor Virtual Desktop Capture**
  - Instantly captures all displays spanning negative and positive virtual screen coordinates.
  - Seamless PerMonitorV2 DPI awareness prevents blur and coordinate drift.
- **⭕ "Circle to Search" & Rectangle Selection**
  - **Rectangle Drag-Select**: Clean border with live pixel dimensions and 4 corner resize handles.
  - **Pixel-Style Lasso / Circling**: Smooth freehand drawing with a glowing neon trail that calculates the tight bounding box upon release.
  - Interactive dimming mask cut-out keeps your selected area 100% bright and clear.
  - Press `Esc` at any moment to cancel immediately with zero residual memory footprint.
- **🔒 100% Local Offline OCR**
  - Powered by native Windows 10/11 `Windows.Media.Ocr.OcrEngine`.
  - No cloud calls, no telemetry, no external runtimes like Tesseract or Python.
  - Automatic language detection based on user profile and system language packs.
- **💊 Sleek Floating Action Pill Menu**
  - **📋 Copy Text**: Copies recognized text to clipboard with subtle harmonic audio chime and toast notification.
  - **🌐 Search Google**: Opens the default browser with the escaped query (`https://www.google.com/search?q=...`).
  - **📷 Search Lens / Image**: Saves the cropped snippet to temporary PNG, copies image to clipboard, and launches Google Lens.
  - **💾 Save Image**: Prompts to save the high-resolution snippet as PNG/JPEG.
  - Gracefully falls back to visual image search when no text is present in the selection.

---

## 🏗️ Architecture & Project Structure

```
OrbitOCR/
├── app.manifest                    # PerMonitorV2 DPI awareness & Windows 10/11 compatibility
├── OrbitOCR.csproj                 # net8.0-windows10.0.19041.0 target & single-file publish spec
├── App.xaml & App.xaml.cs          # Tray lifecycle, single-instance mutex, memory trimming
├── Models/
│   ├── AppSettings.cs              # User settings (hotkey, OCR language, mode, sound)
│   └── OcrExtractedResult.cs       # Extracted OCR text lines, word counts, angle
├── Services/
│   ├── HotkeyService.cs            # Win32 RegisterHotKey & HwndSource message pump hook
│   ├── ScreenCaptureService.cs     # Virtual desktop multi-monitor capture & GDI+ bitmap crop
│   ├── OcrService.cs               # Offline native Windows.Media.Ocr.OcrEngine wrapper
│   ├── TrayIconService.cs          # Win32 Shell_NotifyIcon with Fluent dark context menu
│   ├── SoundService.cs             # Synthesized harmonic chime audio feedback
│   └── SettingsService.cs          # Configuration persistence in %APPDATA%\OrbitOCR\settings.json
├── UI/
│   ├── ActionMenu.xaml (.cs)       # Floating pill menu (Copy, Search Google, Search Lens, Save)
│   ├── OverlayWindow.xaml (.cs)    # TopMost virtual screen canvas, mask cutout & lasso drawing
│   └── SettingsWindow.xaml (.cs)   # Hotkey configurator & OCR language selector
├── Utils/
│   └── IconHelper.cs               # Multi-resolution ICO generator (16, 32, 48, 64px)
├── Assets/
│   └── app.ico                     # Application icon
└── tests/
    ├── OrbitOCR.Tests.csproj       # MSTest test project
    └── UnitTest1.cs                # Unit & integration tests (bounds, cropping, end-to-end OCR)
```

---

## 🚀 Quick Start

### Prerequisites
- Windows 10 (Build 1903+) or Windows 11 (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer

### 1. Build and Run in Debug Mode
```bash
# Clone or navigate to the repository
cd OrbitOCR

# Build the solution
dotnet build

# Run the utility
dotnet run
```
Once launched, OrbitOCR will appear in your system notification tray with a welcome notification. Press **`Ctrl + Shift + S`** to trigger your first snip.

---

### 2. Run Automated Unit & OCR Integration Tests
```bash
dotnet test tests/OrbitOCR.Tests.csproj
```
All 9 automated unit and OCR recognition tests will run, testing virtual screen bounds, boundary clamping, serialization, and end-to-end WinRT OCR text extraction.

---

### 3. Publish as a Self-Contained Single-File Executable
To create a standalone `OrbitOCR.exe` with bundled runtime and zero prerequisites:

```bash
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o ./publish
```

The output executable is created at `./publish/OrbitOCR.exe`. You can copy this single `.exe` to any folder, USB drive, or startup directory.

---

## 🎯 Usage Guide

| Action | Shortcut / Trigger | Description |
|---|---|---|
| **Trigger Snip** | `Ctrl + Shift + S` (or tray click) | Freezes virtual desktop, dims background, opens canvas |
| **Rectangle Mode** | Press `R` or click top pill | Select an exact rectangular area with corner handles |
| **Circle / Lasso Mode** | Press `C` or click top pill | Draw a freehand circle or curve around target content |
| **Resize Selection** | Drag corner handles | Adjust selection bounds before extracting |
| **Copy Detected Text** | `Ctrl + C` or click `Copy Text` | Copies OCR text to clipboard and closes canvas |
| **Search Google** | Click `Search Google` | Opens Google search for extracted text in default browser |
| **Search Lens** | Click `Search Lens` | Copies image to clipboard & opens Google Lens |
| **Cancel Snip** | `Esc` | Immediately dismisses overlay and reclaims memory |
| **Open Settings** | Right-click tray icon -> `Settings...` | Change hotkey, default mode, sound, OCR language |

---

## ⚡ Performance & Memory Optimization

OrbitOCR is engineered specifically for background desktop use:
- **Zero-allocation Idle State**: When the overlay is dismissed, all screenshot bitmaps and WPF visual handles are explicitly disposed.
- **Aggressive Working Set Trimming**: Upon overlay closure, OrbitOCR triggers garbage collection and calls Win32 `SetProcessWorkingSetSize(proc, -1, -1)`, reducing memory usage to **<30 MB RAM** while idle in the tray.
- **Asynchronous Pipeline**: Screen capture and OCR execution are decoupled so the UI remains butter-smooth at 60+ FPS.

---

## 📄 License
MIT License - Open Source and free for personal and commercial use.
