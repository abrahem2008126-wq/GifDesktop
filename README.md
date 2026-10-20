# GifDesktop

Animated GIFs and stickers that sit on your Windows desktop: anywhere you put them, any size you want.

صور GIF وستيكرات متحركة على سطح المكتب بويندوز، بالمكان والحجم اللي تريده.

---

## Features

- Up to **10 GIFs** and **10 stickers** on screen at once, each with its own position and size
- Transparent, borderless windows that can stay **on top of other windows**
- **Automatic background removal** for images with a solid-color background
- Stickers in **WebP, PNG, JPG, GIF and SVG** formats, plus **Telegram animated stickers** (`.tgs` and `.webm`)
- Built-in **image → SVG converter**. Pixel art is converted exactly, so it stays sharp at any size
- Sharp on high-DPI screens (125%, 150%…)
- **Arabic and English** interface
- Remembers everything between runs, with an option to start with Windows

## Requirements

- Windows 10 or 11
- Nothing to install: it runs on the PowerShell and .NET Framework that come with Windows
- WebP stickers need the *WebP Image Extensions* from the Microsoft Store (usually preinstalled)
- Telegram stickers use the Microsoft Edge WebView2 Runtime (built into Windows 11; on Windows 10 get it from Microsoft if it is missing)

## How to run

1. Download the files (green **Code** button → **Download ZIP**) and extract them to a folder.
2. Double-click **`GifDesktop.vbs`**.
3. Choose one or more GIFs.

To create a desktop shortcut: right-click `GifDesktop.vbs` → *Send to* → *Desktop (create shortcut)*. You can set its icon to `GifDesktop.ico`.

## Controls

| Action | How |
|---|---|
| Move | Drag with the left mouse button |
| Resize | Mouse wheel (hold **Ctrl** for small steps) |
| Menu | Right-click a GIF or sticker |

The right-click menu has: add GIF / sticker, convert image to SVG, change image, exact position and size, original size, always on top, lock, remove background, start with Windows, language, remove, and close all.

## Files

| File | What it does |
|---|---|
| `GifDesktop.vbs` | Starts the program without a console window |
| `GifDesktop.ps1` | The program: windows, menu, settings |
| `GifBox.cs` | Plays GIFs and removes backgrounds |
| `SvgTools.cs` | Converts images to SVG and draws SVG files |
| `TelegramStickers.cs` | Opens Telegram `.tgs` / `.webm` stickers through WebView2 |
| `lib/` | WebView2 SDK files (Microsoft) and lottie-web, used for Telegram stickers |
| `GifDesktop.ico` | Icon |

Your settings are saved in `settings.json` next to the program (it is not uploaded to GitHub).

## Limitations

- Animated WebP stickers show only their first frame.
- The first time a Telegram sticker is opened it takes a few seconds to prepare; after that it opens instantly (frames are cached in `%LOCALAPPDATA%\GifDesktop\cache`).
- The SVG viewer is meant for SVGs made by the built-in converter. Complex SVGs from elsewhere may not display.
- Converting photos to SVG gives a "painted", fewer-colors look. SVG works best for pixel art, cartoons and logos.
