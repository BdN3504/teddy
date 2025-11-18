using System;
using System.IO;
using Xunit;
using TonieFile;

namespace TonieAudio.Tests
{
    public class HybridEncodingTests
    {
        private readonly string _testDataDir;

        public HybridEncodingTests()
        {
            _testDataDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData");
        }

        [Fact]
        public void CreateTonie_ThenAppendTrack_ShouldProduceValidFile()
        {
            // Arrange
            string track1 = Path.Combine(_testDataDir, "track1.mp3");
            string track2 = Path.Combine(_testDataDir, "track2.mp3");
            string track3 = Path.Combine(_testDataDir, "track3.mp3");

            Assert.True(File.Exists(track1), $"Track1 not found: {track1}");
            Assert.True(File.Exists(track2), $"Track2 not found: {track2}");
            Assert.True(File.Exists(track3), $"Track3 not found: {track3}");

            string initialTonieFile = Path.Combine(Path.GetTempPath(), $"test_initial_{Guid.NewGuid()}.bin");
            string modifiedTonieFile = Path.Combine(Path.GetTempPath(), $"test_modified_{Guid.NewGuid()}.bin");

            try
            {
                // Act 1: Create initial tonie with 2 tracks
                Console.WriteLine("=== STEP 1: Creating initial tonie with 2 tracks ===");
                TonieFile.TonieAudio initialTonie = new TonieFile.TonieAudio(
                    new[] { track1, track2 },
                    audioId: 12345,
                    bitRate: 96000,
                    useVbr: false,
                    prefixLocation: null,
                    cbr: null
                );

                File.WriteAllBytes(initialTonieFile, initialTonie.FileContent);
                Console.WriteLine($"Initial tonie created: {initialTonieFile}");
                Console.WriteLine($"Initial tonie size: {initialTonie.FileContent.Length} bytes");
                Console.WriteLine($"Initial tonie has {initialTonie.Header.AudioChapters.Length} chapters");

                // Assert 1: Initial tonie should be valid
                Assert.Equal(2, initialTonie.Header.AudioChapters.Length);
                Assert.True(initialTonie.FileContent.Length > 0x1000);

                // Act 2: Read back and validate
                Console.WriteLine("\n=== STEP 2: Reading back initial tonie ===");
                TonieFile.TonieAudio readBack = TonieFile.TonieAudio.FromFile(initialTonieFile, readAudio: true);
                Console.WriteLine($"Read back tonie: {readBack.Header.AudioChapters.Length} chapters");
                Console.WriteLine($"Hash correct: {readBack.HashCorrect}");

                // Assert 2: Read back should match
                Assert.True(readBack.HashCorrect, "Initial tonie hash should be correct");
                Assert.Equal(2, readBack.Header.AudioChapters.Length);

                // Act 3: Extract raw chapters
                Console.WriteLine("\n=== STEP 3: Extracting raw chapters ===");
                var rawChapters = readBack.ExtractRawChapterData();
                Console.WriteLine($"Extracted {rawChapters.Count} raw chapters");
                foreach (var i in Enumerable.Range(0, rawChapters.Count))
                {
                    Console.WriteLine($"  Chapter {i}: {rawChapters[i].Length} bytes");
                }

                // Assert 3: Should have extracted 2 chapters
                Assert.Equal(2, rawChapters.Count);

                // Act 4: Create modified tonie with original 2 tracks + new track
                Console.WriteLine("\n=== STEP 4: Creating modified tonie with 3 tracks (2 original + 1 new) ===");
                var trackSources = new[]
                {
                    new TonieFile.TonieAudio.TrackSource(rawChapters[0]), // Original track 1
                    new TonieFile.TonieAudio.TrackSource(rawChapters[1]), // Original track 2
                    new TonieFile.TonieAudio.TrackSource(track3)          // New track 3
                };

                TonieFile.TonieAudio modifiedTonie = new TonieFile.TonieAudio(
                    trackSources,
                    originalAudioData: readBack.Audio,
                    audioId: 12345,
                    bitRate: 96000,
                    useVbr: false,
                    prefixLocation: null,
                    cbr: null
                );

                File.WriteAllBytes(modifiedTonieFile, modifiedTonie.FileContent);
                Console.WriteLine($"Modified tonie created: {modifiedTonieFile}");
                Console.WriteLine($"Modified tonie size: {modifiedTonie.FileContent.Length} bytes");
                Console.WriteLine($"Modified tonie has {modifiedTonie.Header.AudioChapters.Length} chapters");

                // Assert 4: Modified tonie should have 3 chapters
                Assert.Equal(3, modifiedTonie.Header.AudioChapters.Length);

                // Act 5: Read back modified tonie and validate
                Console.WriteLine("\n=== STEP 5: Reading back modified tonie ===");
                TonieFile.TonieAudio modifiedReadBack = TonieFile.TonieAudio.FromFile(modifiedTonieFile, readAudio: true);
                Console.WriteLine($"Read back modified tonie: {modifiedReadBack.Header.AudioChapters.Length} chapters");
                Console.WriteLine($"Hash correct: {modifiedReadBack.HashCorrect}");

                // Try to calculate statistics (this will fail if pages aren't aligned correctly)
                Console.WriteLine("\n=== STEP 6: Validating Ogg page structure ===");
                try
                {
                    modifiedReadBack.CalculateStatistics(
                        out long totalSegments,
                        out long segLength,
                        out int minSegs,
                        out int maxSegs,
                        out ulong minGranule,
                        out ulong maxGranule,
                        out ulong highestGranule
                    );
                    Console.WriteLine("✓ Ogg page structure is valid");
                    Console.WriteLine($"  Total segments: {totalSegments}");
                    Console.WriteLine($"  Highest granule: {highestGranule}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ Ogg page structure validation FAILED: {ex.Message}");
                    throw;
                }

                // Assert 5: Modified tonie should be valid
                Assert.True(modifiedReadBack.HashCorrect, "Modified tonie hash should be correct");
                Assert.Equal(3, modifiedReadBack.Header.AudioChapters.Length);
            }
            finally
            {
                // Cleanup
                if (File.Exists(initialTonieFile)) File.Delete(initialTonieFile);
                if (File.Exists(modifiedTonieFile)) File.Delete(modifiedTonieFile);
            }
        }

        [Fact]
        public void CreateTonieFromThreeNewTracks_ShouldProduceValidFile()
        {
            // Arrange
            string track1 = Path.Combine(_testDataDir, "track1.mp3");
            string track2 = Path.Combine(_testDataDir, "track2.mp3");
            string track3 = Path.Combine(_testDataDir, "track3.mp3");

            string tonieFile = Path.Combine(Path.GetTempPath(), $"test_three_tracks_{Guid.NewGuid()}.bin");

            try
            {
                // Act: Create tonie with 3 new tracks
                Console.WriteLine("=== Creating tonie with 3 new tracks ===");
                TonieFile.TonieAudio tonie = new TonieFile.TonieAudio(
                    new[] { track1, track2, track3 },
                    audioId: 54321,
                    bitRate: 96000,
                    useVbr: false,
                    prefixLocation: null,
                    cbr: null
                );

                File.WriteAllBytes(tonieFile, tonie.FileContent);
                Console.WriteLine($"Tonie created: {tonieFile}");
                Console.WriteLine($"Tonie size: {tonie.FileContent.Length} bytes");

                // Assert: Should have 3 chapters
                Assert.Equal(3, tonie.Header.AudioChapters.Length);

                // Read back and validate
                TonieFile.TonieAudio readBack = TonieFile.TonieAudio.FromFile(tonieFile, readAudio: true);
                Assert.True(readBack.HashCorrect, "Tonie hash should be correct");
                Assert.Equal(3, readBack.Header.AudioChapters.Length);

                // Validate structure
                readBack.CalculateStatistics(
                    out long totalSegments,
                    out long segLength,
                    out int minSegs,
                    out int maxSegs,
                    out ulong minGranule,
                    out ulong maxGranule,
                    out ulong highestGranule
                );
                Console.WriteLine("✓ Ogg page structure is valid");
            }
            finally
            {
                if (File.Exists(tonieFile)) File.Delete(tonieFile);
            }
        }

        [Fact]
        public void NormalEncoding_ParsePositions_ShouldReturnCorrectPositions()
        {
            // This tests that ParsePositions works correctly for normal encoding
            string track1 = Path.Combine(_testDataDir, "track1.mp3");
            string track2 = Path.Combine(_testDataDir, "track2.mp3");
            string tonieFile = Path.Combine(Path.GetTempPath(), $"test_parse_positions_{Guid.NewGuid()}.bin");

            try
            {
                // Create a normal 2-track tonie
                Console.WriteLine("=== Creating normal 2-track tonie ===");
                TonieFile.TonieAudio tonie = new TonieFile.TonieAudio(
                    new[] { track1, track2 },
                    audioId: 12345,
                    bitRate: 96000,
                    useVbr: false,
                    prefixLocation: null,
                    cbr: null
                );

                File.WriteAllBytes(tonieFile, tonie.FileContent);
                Console.WriteLine($"Created tonie with {tonie.Header.AudioChapters.Length} chapters");
                Console.WriteLine($"Chapter markers: [{string.Join(", ", tonie.Header.AudioChapters)}]");

                // Read back and call ParsePositions
                Console.WriteLine("\n=== Reading back and calling ParsePositions ===");
                TonieFile.TonieAudio readBack = TonieFile.TonieAudio.FromFile(tonieFile, readAudio: true);
                ulong[] positions = readBack.ParsePositions();

                Console.WriteLine($"ParsePositions returned {positions.Length} positions:");
                for (int i = 0; i < positions.Length; i++)
                {
                    double timeSeconds = positions[i] / 48000.0;
                    Console.WriteLine($"  Position {i}: {positions[i]} granules = {timeSeconds:F2}s");
                }

                // For a 2-track file with chapter markers [0, 32], we expect:
                // - Position 0 (implicit start)
                // - Position 0 (from chapter marker 0)
                // - Position at second track (from chapter marker 32)
                // - Final position
                // Total: 4 positions, but player deduplicates to get 2 tracks
                Assert.Equal(4, positions.Length);
                Assert.Equal(0UL, positions[0]); // Always starts at 0
                Assert.Equal(0UL, positions[1]); // Duplicate from chapter marker 0 (will be deduplicated by player)
            }
            finally
            {
                if (File.Exists(tonieFile)) File.Delete(tonieFile);
            }
        }

        [Fact]
        public void ModifiedTonie_AllTracksPlayable_ShouldHaveCorrectDurations()
        {
            // Arrange
            string track1 = Path.Combine(_testDataDir, "track1.mp3");
            string track2 = Path.Combine(_testDataDir, "track2.mp3");
            string track3 = Path.Combine(_testDataDir, "track3.mp3");

            string initialTonieFile = Path.Combine(Path.GetTempPath(), $"test_playback_initial_{Guid.NewGuid()}.bin");
            string modifiedTonieFile = Path.Combine(Path.GetTempPath(), $"test_playback_modified_{Guid.NewGuid()}.bin");

            try
            {
                // Act 1: Create initial tonie with 2 tracks
                Console.WriteLine("=== Creating initial tonie ===");
                TonieFile.TonieAudio initialTonie = new TonieFile.TonieAudio(
                    new[] { track1, track2 },
                    audioId: 12345,
                    bitRate: 96000,
                    useVbr: false,
                    prefixLocation: null,
                    cbr: null
                );

                File.WriteAllBytes(initialTonieFile, initialTonie.FileContent);

                // Validate initial tonie tracks
                Console.WriteLine("Validating initial tonie tracks:");
                var initialReadBack = TonieFile.TonieAudio.FromFile(initialTonieFile, readAudio: true);
                var initialChapters = initialReadBack.ExtractRawChapterData();
                for (int i = 0; i < initialChapters.Count; i++)
                {
                    var pages = 0;
                    ulong lastGranule = 0;
                    int offset = 0;
                    byte[] data = initialChapters[i];

                    while (offset < data.Length - 27)
                    {
                        if (data[offset] == 'O' && data[offset + 1] == 'g' && data[offset + 2] == 'g' && data[offset + 3] == 'S')
                        {
                            ulong granule = BitConverter.ToUInt64(data, offset + 6);
                            byte segCount = data[offset + 26];
                            int dataSize = 0;
                            for (int s = 0; s < segCount; s++) dataSize += data[offset + 27 + s];

                            if (granule != 0 && granule != ulong.MaxValue) lastGranule = granule;
                            pages++;
                            offset += 27 + segCount + dataSize;
                        }
                        else offset++;
                    }

                    Console.WriteLine($"  Initial track {i+1}: {pages} pages, {lastGranule} granules, {lastGranule/48000.0:F2}s");
                }

                // Act 2: Read and create modified tonie
                Console.WriteLine("\n=== Creating modified tonie with 3 tracks ===");
                TonieFile.TonieAudio readBack = TonieFile.TonieAudio.FromFile(initialTonieFile, readAudio: true);
                var rawChapters = readBack.ExtractRawChapterData();

                var trackSources = new[]
                {
                    new TonieFile.TonieAudio.TrackSource(rawChapters[0]),
                    new TonieFile.TonieAudio.TrackSource(rawChapters[1]),
                    new TonieFile.TonieAudio.TrackSource(track3)
                };

                TonieFile.TonieAudio modifiedTonie = new TonieFile.TonieAudio(
                    trackSources,
                    originalAudioData: readBack.Audio,
                    audioId: 12345,
                    bitRate: 96000,
                    useVbr: false,
                    prefixLocation: null,
                    cbr: null
                );

                File.WriteAllBytes(modifiedTonieFile, modifiedTonie.FileContent);

                // Act 3: Read back modified tonie
                Console.WriteLine("\n=== Reading back modified tonie ===");
                TonieFile.TonieAudio modifiedReadBack = TonieFile.TonieAudio.FromFile(modifiedTonieFile, readAudio: true);

                // Act 4: Dump each track to individual Ogg files and validate
                Console.WriteLine("\n=== Extracting and validating individual tracks ===");
                string outputDir = Path.GetTempPath();
                string baseFileName = $"test_playback_{Guid.NewGuid()}";

                // Extract each track
                var extractedChapters = modifiedReadBack.ExtractRawChapterData();
                Assert.Equal(3, extractedChapters.Count);

                for (int i = 0; i < extractedChapters.Count; i++)
                {
                    string trackFile = Path.Combine(outputDir, $"{baseFileName}_track{i + 1}.ogg");
                    Console.WriteLine($"\n--- Track {i + 1} ---");
                    Console.WriteLine($"  Raw data size: {extractedChapters[i].Length} bytes");

                    // Write chapter to file with proper headers
                    modifiedReadBack.WriteChapterToFile(extractedChapters[i], trackFile, i);
                    Console.WriteLine($"  Written to: {trackFile}");

                    // Verify file exists and has content
                    Assert.True(File.Exists(trackFile), $"Track {i + 1} file should exist");
                    FileInfo fi = new FileInfo(trackFile);
                    Assert.True(fi.Length > 0, $"Track {i + 1} file should have content");
                    Console.WriteLine($"  File size: {fi.Length} bytes");

                    // Read as Ogg file and check structure
                    byte[] oggData = File.ReadAllBytes(trackFile);
                    Assert.True(oggData[0] == 'O' && oggData[1] == 'g' && oggData[2] == 'g' && oggData[3] == 'S',
                        $"Track {i + 1} should start with Ogg signature");

                    // Parse pages and check for valid structure
                    int pageCount = 0;
                    ulong firstGranule = ulong.MaxValue;
                    ulong lastGranule = 0;
                    int offset = 0;

                    while (offset < oggData.Length - 27)
                    {
                        if (oggData[offset] == 'O' && oggData[offset + 1] == 'g' &&
                            oggData[offset + 2] == 'g' && oggData[offset + 3] == 'S')
                        {
                            ulong granule = BitConverter.ToUInt64(oggData, offset + 6);
                            byte segmentCount = oggData[offset + 26];

                            int dataSize = 0;
                            for (int s = 0; s < segmentCount; s++)
                            {
                                dataSize += oggData[offset + 27 + s];
                            }

                            pageCount++;

                            // Track first and last valid granules
                            if (granule != 0 && granule != ulong.MaxValue)
                            {
                                if (firstGranule == ulong.MaxValue) firstGranule = granule;

                                if (lastGranule > 0)
                                {
                                    Assert.True(granule >= lastGranule,
                                        $"Track {i + 1} page {pageCount}: granule should increase (was {lastGranule}, now {granule})");
                                }
                                lastGranule = granule;
                            }

                            offset += 27 + segmentCount + dataSize;
                        }
                        else
                        {
                            offset++;
                        }
                    }

                    // Calculate duration from relative granules (48000 granules per second)
                    ulong relativeDuration = (firstGranule == ulong.MaxValue) ? lastGranule : (lastGranule - firstGranule);
                    double durationSeconds = relativeDuration / 48000.0;
                    Console.WriteLine($"  Pages: {pageCount}");
                    Console.WriteLine($"  First granule: {firstGranule}, Last granule: {lastGranule}");
                    Console.WriteLine($"  Relative duration: {relativeDuration} granules = {durationSeconds:F2} seconds");

                    // Validate durations are reasonable
                    if (i == 0)
                    {
                        // Track 1 should be ~10 seconds
                        Assert.True(durationSeconds >= 9.5 && durationSeconds <= 10.5,
                            $"Track 1 duration should be ~10s, got {durationSeconds:F2}s");
                    }
                    else if (i == 1)
                    {
                        // Track 2 should be ~15 seconds
                        Assert.True(durationSeconds >= 14.5 && durationSeconds <= 15.5,
                            $"Track 2 duration should be ~15s, got {durationSeconds:F2}s");
                    }
                    else if (i == 2)
                    {
                        // Track 3 should be ~8 seconds
                        Assert.True(durationSeconds >= 7.5 && durationSeconds <= 8.5,
                            $"Track 3 duration should be ~8s, got {durationSeconds:F2}s");
                    }

                    // Cleanup
                    File.Delete(trackFile);
                }

                Console.WriteLine("\n✓ All tracks validated successfully");
            }
            finally
            {
                if (File.Exists(initialTonieFile)) File.Delete(initialTonieFile);
                if (File.Exists(modifiedTonieFile)) File.Delete(modifiedTonieFile);
            }
        }

        [Fact]
        public void NewApproach_ExtractTracksToTempFiles_ThenReencode_ShouldProduceValidFile()
        {
            // This test uses the new simpler approach: extract tracks to temp Ogg files
            // using ffmpeg splitting, then re-encode all tracks together.

            // Arrange
            string track1 = Path.Combine(_testDataDir, "track1.mp3");
            string track2 = Path.Combine(_testDataDir, "track2.mp3");
            string track3 = Path.Combine(_testDataDir, "track3.mp3");

            string initialTonieFile = Path.Combine(Path.GetTempPath(), $"test_new_approach_initial_{Guid.NewGuid()}.bin");
            string modifiedTonieFile = Path.Combine(Path.GetTempPath(), $"test_new_approach_modified_{Guid.NewGuid()}.bin");

            try
            {
                // Act 1: Create initial tonie with 2 tracks
                Console.WriteLine("=== STEP 1: Creating initial tonie with 2 tracks ===");
                TonieFile.TonieAudio initialTonie = new TonieFile.TonieAudio(
                    new[] { track1, track2 },
                    audioId: 12345,
                    bitRate: 96000,
                    useVbr: false,
                    prefixLocation: null,
                    cbr: null
                );

                File.WriteAllBytes(initialTonieFile, initialTonie.FileContent);
                Console.WriteLine($"Initial tonie created: {initialTonieFile}");
                Console.WriteLine($"Initial tonie has {initialTonie.Header.AudioChapters.Length} chapters");

                // Act 2: Read back and extract tracks using new approach
                Console.WriteLine("\n=== STEP 2: Extracting tracks using ExtractTracksToTempFiles() ===");
                TonieFile.TonieAudio readBack = TonieFile.TonieAudio.FromFile(initialTonieFile, readAudio: true);
                List<string> extractedTracks = readBack.ExtractTracksToTempFiles();

                Console.WriteLine($"Extracted {extractedTracks.Count} tracks:");
                for (int i = 0; i < extractedTracks.Count; i++)
                {
                    var fileInfo = new FileInfo(extractedTracks[i]);
                    Console.WriteLine($"  Track {i + 1}: {extractedTracks[i]} ({fileInfo.Length} bytes)");
                }

                // Assert: Should have extracted 2 tracks
                Assert.Equal(2, extractedTracks.Count);

                // Act 3: Create modified tonie with original 2 tracks + new track using simple re-encoding
                Console.WriteLine("\n=== STEP 3: Re-encoding all tracks (2 original + 1 new) ===");
                var allTrackPaths = new List<string>();
                allTrackPaths.AddRange(extractedTracks);  // Original tracks
                allTrackPaths.Add(track3);                 // New track

                TonieFile.TonieAudio modifiedTonie = new TonieFile.TonieAudio(
                    allTrackPaths.ToArray(),
                    audioId: 12345,
                    bitRate: 96000,
                    useVbr: false,
                    prefixLocation: null,
                    cbr: null
                );

                File.WriteAllBytes(modifiedTonieFile, modifiedTonie.FileContent);
                Console.WriteLine($"Modified tonie created: {modifiedTonieFile}");
                Console.WriteLine($"Modified tonie has {modifiedTonie.Header.AudioChapters.Length} chapters");

                // Clean up extracted temp files
                foreach (var tempFile in extractedTracks)
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }

                // Assert: Modified tonie should have 3 chapters
                Assert.Equal(3, modifiedTonie.Header.AudioChapters.Length);

                // Act 4: Read back and validate
                Console.WriteLine("\n=== STEP 4: Validating modified tonie ===");
                TonieFile.TonieAudio modifiedReadBack = TonieFile.TonieAudio.FromFile(modifiedTonieFile, readAudio: true);

                Assert.True(modifiedReadBack.HashCorrect, "Modified tonie hash should be correct");
                Assert.Equal(3, modifiedReadBack.Header.AudioChapters.Length);

                // Validate Ogg page structure
                modifiedReadBack.CalculateStatistics(
                    out long totalSegments,
                    out long segLength,
                    out int minSegs,
                    out int maxSegs,
                    out ulong minGranule,
                    out ulong maxGranule,
                    out ulong highestGranule
                );

                Console.WriteLine($"✓ Ogg page structure is valid");
                Console.WriteLine($"  Total segments: {totalSegments}");
                Console.WriteLine($"  Highest granule: {highestGranule}");

                // Additional validation: Check chapter markers are sequential
                Console.WriteLine($"\n=== STEP 5: Validating chapter markers ===");
                Console.WriteLine($"Chapter markers: [{string.Join(", ", modifiedReadBack.Header.AudioChapters)}]");

                for (int i = 1; i < modifiedReadBack.Header.AudioChapters.Length; i++)
                {
                    Assert.True(modifiedReadBack.Header.AudioChapters[i] > modifiedReadBack.Header.AudioChapters[i - 1],
                        $"Chapter markers should be sequential: Chapter {i} ({modifiedReadBack.Header.AudioChapters[i]}) should be > Chapter {i-1} ({modifiedReadBack.Header.AudioChapters[i - 1]})");
                }

                Console.WriteLine("✓ Chapter markers are sequential");
                Console.WriteLine("\n✓ All validations passed - new approach works correctly!");
            }
            finally
            {
                if (File.Exists(initialTonieFile)) File.Delete(initialTonieFile);
                if (File.Exists(modifiedTonieFile)) File.Delete(modifiedTonieFile);
            }
        }
    }
}
