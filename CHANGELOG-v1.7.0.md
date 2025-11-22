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
  - **Keyboard Navigation**
    - Arrow keys for navigating between Tonies
    - Space bar to play/pause audio
    - Context menu accessible via keyboard (Menu key or Shift+F10)
    - Alt-key shortcuts for all menu items (e.g., Alt+H for Help)
    - Automatic focus on first Tonie after directory load
  - **Help System**
    - Built-in help dialog with hotkey reference
    - Complete list of keyboard shortcuts
    - Accessible via Help button or F1 key
  - **Search Functionality**
    - Real-time search with 250ms debounce
    - Filters by display name, Audio ID (hex), folder name, or UID
    - Supports reverse RFID lookup (search by actual RFID to find reversed folder)
    - Clear search with ESC key
    - Search field automatically appears when typing

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

- **TRASHCAN Recovery System** ⭐ NEW
  - Recover Tonies deleted by online Toniebox from quarantine directory
  - View all quarantined files with metadata (UID, hash, deletion date, duration)
  - Restore deleted Tonies back to CONTENT folder with original directory structure
  - Permanently delete files from TRASHCAN
  - **Byte-for-byte restoration**: restored files are **100% identical** to originals (preserves Audio ID)
  - **Advanced customTonies.json format**: now stores Directory field for reliable restoration without RFID parsing ambiguity
  - **Conflict resolution**: detects and handles conflicts when restoring to occupied directory
  - **UID prompt**: interactive dialog for unknown Tonies during restoration
  - Accessible via "TRASHCAN Recovery" toolbar button

- **File Operations**
  - Decode Tonie files to audio
  - Display detailed file information
  - Delete Tonies with confirmation dialog
  - Re-assign UID to existing Tonies
  - **Enhanced File Details Display** in Selected File Details panel
    - File size with human-readable formatting (KB/MB/GB)
    - Last modified date and time
    - Directory location information

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
- Platform-specific LibVLC bundling (Windows/macOS bundled, Linux system-installed)
- Optimized release binaries without debug symbols
- .NET 8.0 compatibility (replaced deprecated APIs)
- **BREAKING CHANGE**: customTonies.json format changed from JObject (dictionary) to JArray (array of objects)
  - Added "Directory" field to TonieMetadata model for reliable TRASHCAN restoration
  - Backward compatibility maintained via RFID regex fallback for old entries
  - New format: `[{"No": "0", "Hash": "...", "Title": "...", "Directory": "EA33ED0E", "AudioId": [...], "Tracks": [...]}]`
- **TonieAudio.UpdateFileContent()**: New method to update file content when header is modified (enables Audio ID restoration)

### Bug Fixes
- **Fixed critical bug: Audio ID was incorrectly affecting file hash** ⚠️ IMPORTANT
  - Audio ID (timestamp) was being used as Ogg logical stream ID, causing identical audio to produce different hashes
  - Hardware evidence: Toniebox updates Audio ID but keeps hash unchanged when deleting files
  - Now uses constant stream ID (1) instead of audioId in OpusOggWriteStream
  - Same audio source now always produces same hash, matching hardware behavior
  - Fixes hash collision issues in customTonies.json and enables proper TRASHCAN recovery
- **Fixed hash validation error** when clicking "Show Info" on Tonie files
- **Fixed extended Info display** - improved layout and formatting of detailed file information
- **Fixed sorting dropdown alignment** - now properly right-aligned in UI
- **Fixed audio-id generation bug** that could cause duplicate IDs across different custom Tonies
  - Improved audio ID generation algorithm to ensure uniqueness
  - Added comprehensive test suite to verify audio ID and hash generation specifications
- **Fixed space bar audio playback** issue where pressing and holding space caused buggy behavior
- **Fixed modified Tonie playback** to ensure proper re-encoding for compatibility
- **Fixed encoding progress dialog** now properly displays during Tonie modification workflow
- **Fixed TRASHCAN restoration to preserve original Audio ID**
  - Restored Tonies now have identical Audio ID to the original file
  - Added comprehensive test suite to verify byte-for-byte restoration (TrashcanRestorationTests.cs)
  - Updated all test fixtures to work with new JArray-based customTonies.json format
- Replaced deprecated `Thread.Abort()` for .NET 8.0 compatibility
- Replaced deprecated `WebRequest` with `HttpClient` to eliminate SYSLIB0014 warnings

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
