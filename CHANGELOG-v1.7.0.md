# Teddy v1.7.0

Enhanced cross-platform fork with .NET 8.0 and improved compatibility.

## What's New

### Core Features
- **Cross-platform CLI tool** (Teddy) for encoding/decoding Tonie files
  - Supports multiple audio formats: MP3, OGG, FLAC, WAV, M4A, AAC, WMA
  - Extract audio from Tonie files to OGG format
  - Create custom Tonie files from audio sources
  - Display file metadata (text, CSV, JSON output)

- **Cross-platform GUI application** (TeddyBench.Avalonia)
  - Modern Avalonia UI with MVVM architecture
  - Icon-based file browser with automatic metadata and image downloads
  - Auto-detects Toniebox SD cards and navigates to CONTENT folder
  - Audio playback with play/pause/stop controls and progress bar
  - Multi-selection support with Shift/Ctrl modifiers

- **Custom Tonie Creation**
  - Multi-format audio file picker
  - Track sorting dialog with drag-and-drop
  - Configurable RFID prefix (default: 0EED)
  - Automatic title generation from source folder name
  - Custom metadata management via customTonies.json

- **LIVE Flag Management**
  - Visual [LIVE] indicator for flagged files
  - Toggle LIVE flag on individual files (context menu)
  - Bulk remove LIVE flags from all tonies (button)
  - Cross-platform implementation (Windows, Linux, macOS)

- **File Operations**
  - Decode Tonie files to audio
  - Display detailed file information
  - Delete Tonies with confirmation dialog
  - Re-assign UID to existing Tonies

### Platform Support
- **Windows**: Full support for CLI and GUI (including legacy TeddyBench)
- **Linux**: Full support for CLI and GUI (requires `ffmpeg` and `fatattr` packages)
- **macOS**: Full support for CLI and GUI (requires `ffmpeg` and `fatattr` via Homebrew)

### Technical Improvements
- Self-contained single-file executables for all platforms
- FFmpeg-based audio resampling for Linux/macOS
- NAudio-based audio processing for Windows
- LibVLC integration for cross-platform audio playback
- Opus encoding with configurable bitrate and VBR support
- Proper Ogg page alignment (4096-byte boundaries)

### Legal & Licensing
- MIT License for Teddy codebase
- Full LGPL 2.1 compliance for LibVLC usage
- Comprehensive third-party notices document (THIRD-PARTY-NOTICES.md)
- Clear attribution for all open-source dependencies

## Installation

Download the appropriate binary for your platform:
- **Windows**: `teddy-v1.7.0-win-x64.zip` or `teddybench-v1.7.0-win-x64.zip`
- **Linux**: `teddy-v1.7.0-linux-x64.tar.gz` or `teddybench-v1.7.0-linux-x64.tar.gz`
- **macOS (Intel)**: `teddy-v1.7.0-osx-x64.tar.gz` or `teddybench-v1.7.0-osx-x64.tar.gz`
- **macOS (Apple Silicon)**: `teddy-v1.7.0-osx-arm64.tar.gz` or `teddybench-v1.7.0-osx-arm64.tar.gz`

### Linux/macOS Prerequisites
```bash
# Ubuntu/Debian (VLC required for audio playback in GUI)
sudo apt install ffmpeg fatattr vlc libvlc-dev

# macOS (Homebrew - only FFmpeg needed, LibVLC is bundled)
brew install ffmpeg
```

## Usage

### CLI Examples
```bash
# Get information about a Tonie file
./Teddy -m info <toniefile>

# Decode Tonie file to audio
./Teddy -m decode -o <output_dir> <toniefile>

# Encode audio files to Tonie format
./Teddy -m encode -o <output_file> -i <audio_id> <folder_or_files>
```

### GUI
Simply launch TeddyBench.Avalonia and navigate to your Toniebox SD card's CONTENT folder.

## Known Issues
- None at this time

## Credits
Developed by the Teddy community. Special thanks to all contributors and testers.

---

**Full Changelog**: https://github.com/BdN3504/teddy/commits/v1.7.0
