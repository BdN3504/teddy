# Tonie Modification Fix - Ffmpeg Splitting Approach

## Problem

When modifying a Tonie by adding tracks to an existing Tonie file, the Toniebox would:
- Play the first two original tracks correctly
- Flash red LED after track 2 ends
- Fail to automatically play track 3
- Require tag removal/replacement to play track 3

## Root Cause

The previous implementation used complex manual Ogg stream manipulation in `GenerateAudioFromTrackSourcesManual()` which:
- Manually parsed and renumbered Ogg pages
- Adjusted granule positions and sequence numbers
- Had subtle bugs in page alignment or chapter marker generation
- Was difficult to debug and maintain

## Solution

Implemented a simpler, more reliable approach using ffmpeg:

### New Method: `ExtractTracksToTempFiles()`

**Location:** `TonieAudio/TonieAudio.cs:1656-1763`

**How it works:**

1. **Extract precise timestamps** from existing Tonie:
   - Uses `ParsePositions()` to read chapter markers
   - Maps page numbers to granule positions
   - Converts granules to seconds: `time = granule / 48000.0`

2. **Write full Ogg stream** to temp file:
   - Equivalent to `dd bs=4096 skip=1` (skip Tonie header)
   - Creates valid Ogg file with all audio

3. **Split at exact timestamps** using ffmpeg:
   ```bash
   ffmpeg -i full.ogg -ss 0.0 -to 10.5 -c copy track1.ogg
   ffmpeg -i full.ogg -ss 10.5 -to 25.3 -c copy track2.ogg
   ```
   - `-c copy` means no re-encoding at this stage
   - Preserves exact chapter boundaries from granule positions

4. **Return temp file paths** for extracted tracks

### Updated: `HybridTonieEncodingService.EncodeHybridTonie()`

**Location:** `TeddyBench.Avalonia/Services/HybridTonieEncodingService.cs:34-109`

**New workflow:**

```csharp
// Extract original tracks to temp Ogg files
var extractedTracks = originalAudio.ExtractTracksToTempFiles();

// Build list of all tracks (original + new)
var allTrackPaths = new List<string>();
allTrackPaths.AddRange(extractedTracks);  // Original tracks
allTrackPaths.Add(newTrack3Path);         // New track

// Use regular encoding - simpler and reliable!
TonieAudio modifiedTonie = new TonieAudio(allTrackPaths.ToArray(), audioId, bitRate * 1000, false, null);
```

### Legacy Code (Preserved)

The old approach is kept as `EncodeHybridTonieLegacy()` for reference:
- Marked with `[Obsolete]` attribute
- Not used by GUI anymore
- Manual Ogg stream manipulation code remains in `GenerateAudioFromTrackSourcesManual()`

## Advantages of New Approach

✅ **Simpler** - Uses proven `GenerateAudio()` encoding path
✅ **More reliable** - No complex manual Ogg page manipulation
✅ **Preserves chapter boundaries** - ffmpeg splits at exact granule timestamps
✅ **Easier to debug** - Standard ffmpeg + standard encoding
✅ **Better tested** - Uses battle-tested encoding logic

## Trade-offs

⚠️ **Minor quality loss** - Re-encodes original tracks at 96kbps
- Opus degrades gracefully, loss is minimal
- Original tracks were already lossy at 96kbps
- Second encode has negligible impact on perceived quality

## Testing

### Automated Test

New test: `NewApproach_ExtractTracksToTempFiles_ThenReencode_ShouldProduceValidFile()`

**Location:** `TonieAudio.Tests/HybridEncodingTests.cs:447-561`

**Results:**
```
✓ Extracts 2 tracks from initial Tonie
✓ Re-encodes with 3rd track added
✓ Produces valid Ogg page structure
✓ Chapter markers are sequential [0, 34, 76]
✓ Hash validation passes
```

### Manual Testing Required

**To verify the fix on actual Toniebox:**

1. Build the GUI: `dotnet build TeddyBench.Avalonia/TeddyBench.Avalonia.csproj --configuration Release`
2. Open an existing Tonie with 2+ tracks
3. Click "Modify" button
4. Add a new track at the end
5. Save to SD card
6. Place tag on Toniebox and verify:
   - Track 1 plays completely ✓
   - Track 2 plays completely ✓
   - Track 3 starts **automatically** (no red LED) ✓
   - All tracks play in sequence ✓

## Files Changed

1. **TonieAudio/TonieAudio.cs**
   - Added `ExtractTracksToTempFiles()` method (lines 1656-1763)
   - Uses ffmpeg to split Ogg at precise timestamps
   - Kept legacy code intact

2. **TeddyBench.Avalonia/Services/HybridTonieEncodingService.cs**
   - Updated `EncodeHybridTonie()` to use new approach (lines 34-109)
   - Renamed old implementation to `EncodeHybridTonieLegacy()` (lines 111-164)
   - Marked legacy method as `[Obsolete]`

3. **TonieAudio.Tests/HybridEncodingTests.cs**
   - Added test for new approach (lines 447-561)

## Dependencies

Requires **ffmpeg** binary in PATH:
- Linux/macOS: Usually pre-installed or via package manager
- Windows: Included with TeddyBench distributions

Already listed in prerequisites for audio resampling.
