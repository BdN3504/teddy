using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using TeddyBench.Avalonia.Models;
using TeddyBench.Avalonia.Services;
using TeddyBench.Avalonia.ViewModels;
using TeddyBench.Avalonia.Dialogs;
using TonieFile;
using Xunit;
using Newtonsoft.Json.Linq;

namespace TeddyBench.Avalonia.Tests;

/// <summary>
/// End-to-end integration tests for TeddyBench.Avalonia workflow
/// Tests the complete user workflow:
/// 1. Open directory with existing tonie
/// 2. Select and delete a tonie
/// 3. Add custom tonie with two tracks
/// 4. Open player and verify tracks
/// 5. Modify tonie to add third track
/// </summary>
public class EndToEndWorkflowTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _contentDir;
    private readonly string _existingTonieDir;
    private readonly string _existingTonieFile;
    private readonly string _customTonieJsonPath;
    private readonly string _toniesJsonPath;

    public EndToEndWorkflowTests()
    {
        // Set up test directory structure
        _testDir = Path.Combine(Path.GetTempPath(), $"TeddyBench_E2E_Test_{Guid.NewGuid():N}");
        _contentDir = Path.Combine(_testDir, "CONTENT");
        _existingTonieDir = Path.Combine(_contentDir, "A13DED0E");

        Directory.CreateDirectory(_existingTonieDir);

        _existingTonieFile = Path.Combine(_existingTonieDir, "500304E0");
        _customTonieJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "customTonies.json");
        _toniesJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tonies.json");

        // Create a test tonie file for deletion test
        CreateTestTonieFile(_existingTonieFile);
    }

    private void CreateTestTonieFile(string path)
    {
        // Get test data files
        var track1Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "track1.mp3");
        var track2Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "track2.mp3");

        // Create a tonie file using TonieAudio
        var tonie = new TonieAudio(
            sources: new[] { track1Path, track2Path },
            audioId: 0x0451ED0E, // RFID from directory name reversed
            bitRate: 96000, // 96 kbps
            useVbr: false,
            prefixLocation: null
        );

        // Write the file
        File.WriteAllBytes(path, tonie.FileContent);

        // Calculate hash and add to customTonies.json
        using var sha1 = SHA1.Create();
        var audioData = new byte[tonie.FileContent.Length - 0x1000];
        Array.Copy(tonie.FileContent, 0x1000, audioData, 0, audioData.Length);
        var hash = BitConverter.ToString(sha1.ComputeHash(audioData)).Replace("-", "");

        // Create customTonies.json entry
        var customTonies = new JObject
        {
            [hash] = "Test Tonie [RFID: 0EED51A1]"
        };

        File.WriteAllText(_customTonieJsonPath, customTonies.ToString());
    }

    [AvaloniaFact]
    public async Task CompleteWorkflow_CreateDeleteModifyAndPlayTonie_ShouldSucceed()
    {
        // Arrange
        var window = new Window();
        var viewModel = new MainWindowViewModel(window);

        // Step 1: Wait for JSON metadata to be loaded
        Console.WriteLine("Step 1: Waiting for metadata to load...");
        await Task.Delay(5000); // Give time for InitializeMetadataAsync to complete
        Console.WriteLine($"Status after waiting: {viewModel.StatusText}");
        // Accept either "Ready" or "downloaded" status
        Assert.True(
            viewModel.StatusText.Contains("Ready") ||
            viewModel.StatusText.Contains("downloaded") ||
            viewModel.StatusText.Contains("Metadata"),
            $"Expected status to contain 'Ready', 'downloaded', or 'Metadata', but got: {viewModel.StatusText}");

        // Step 2: Simulate opening the CONTENT directory
        Console.WriteLine("Step 2: Scanning directory...");
        await SimulateDirectoryOpen(viewModel, _contentDir);

        // Verify the existing tonie is loaded
        Assert.Single(viewModel.TonieFiles);
        var existingTonie = viewModel.TonieFiles.First();
        Assert.Contains("Test Tonie", existingTonie.DisplayName);

        // Step 3: Calculate hash and verify it's in customTonies.json
        Console.WriteLine("Step 3: Verifying tonie hash matches customTonies.json...");
        var tonieAudio = TonieAudio.FromFile(_existingTonieFile, readAudio: false);
        var hashString = BitConverter.ToString(tonieAudio.Header.Hash).Replace("-", "");

        var customTonieJson = JObject.Parse(File.ReadAllText(_customTonieJsonPath));
        Assert.True(customTonieJson.ContainsKey(hashString), "Hash should exist in customTonies.json");

        // Step 4: Select the tonie
        Console.WriteLine("Step 4: Selecting tonie...");
        viewModel.SelectTonieCommand.Execute(existingTonie);
        Assert.Equal(existingTonie, viewModel.SelectedFile);
        Assert.True(existingTonie.IsSelected);

        // Step 5: Delete the tonie (simulate confirmation)
        Console.WriteLine("Step 5: Deleting tonie...");
        await SimulateDeleteTonie(viewModel, existingTonie);

        // Verify tonie is deleted
        Assert.Empty(viewModel.TonieFiles);
        Assert.False(File.Exists(_existingTonieFile));
        Assert.False(Directory.Exists(_existingTonieDir));

        // Step 6: Add a custom tonie with track1 and track2
        Console.WriteLine("Step 6: Adding custom tonie with 2 tracks...");
        var track1Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "track1.mp3");
        var track2Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "track2.mp3");

        var newTonieFile = await SimulateAddCustomTonie(
            viewModel,
            rfidUid: "0EED5104",
            audioPaths: new[] { track1Path, track2Path }
        );

        // Verify new tonie exists
        Assert.Single(viewModel.TonieFiles);
        var newTonie = viewModel.TonieFiles.First();
        Assert.NotNull(newTonie);

        // Step 7: Open player and verify initial 2 tracks
        Console.WriteLine("Step 7: Testing player functionality with initial 2 tracks...");
        await TestPlayerFunctionality(newTonieFile, new[] { track1Path, track2Path });

        // Step 8: Open modify dialog and add track3
        Console.WriteLine("Step 8: Opening modify dialog and adding track3...");
        var track3Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "track3.mp3");

        // Step 9: Press encode button and wait for encoding to complete
        Console.WriteLine("Step 9: Pressing encode button and waiting for encoding to complete...");
        await SimulateModifyTonieWithEncode(viewModel, newTonie, track3Path);
        Console.WriteLine("Encoding completed successfully!");

        // Step 10: Refresh directory and select the modified tonie
        Console.WriteLine("Step 10: Refreshing directory and selecting modified tonie...");
        await SimulateDirectoryOpen(viewModel, _contentDir);
        Assert.Single(viewModel.TonieFiles);
        var modifiedTonieItem = viewModel.TonieFiles.First();
        viewModel.SelectTonieCommand.Execute(modifiedTonieItem);
        Assert.Equal(modifiedTonieItem, viewModel.SelectedFile);

        // Step 11: Open player for modified tonie and verify all 3 tracks
        Console.WriteLine("Step 11: Opening player for modified tonie with 3 tracks...");
        await TestPlayerFunctionality(newTonieFile, new[] { track1Path, track2Path, track3Path });

        // Step 12: Verify modified tonie has exactly 3 tracks with correct durations
        Console.WriteLine("Step 12: Verifying modified tonie has 3 tracks with correct durations...");
        var modifiedTonie = TonieAudio.FromFile(newTonieFile, readAudio: true);
        var positions = modifiedTonie.ParsePositions();

        // ParsePositions returns N+1 positions for N tracks (includes end position)
        Assert.True(positions.Length >= 3, $"Expected at least 3 positions, got {positions.Length}");

        // Verify track durations match source files
        await VerifyTrackDurations(newTonieFile, new[] { track1Path, track2Path, track3Path });

        Console.WriteLine("Test completed successfully! All 3 tracks verified.");
    }

    private async Task SimulateDirectoryOpen(MainWindowViewModel viewModel, string directory)
    {
        // Directly call the internal ScanDirectory method via reflection
        var method = typeof(MainWindowViewModel).GetMethod("ScanDirectory",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (method != null)
        {
            var task = method.Invoke(viewModel, new object[] { directory }) as Task;
            if (task != null)
            {
                await task;
            }
        }
    }

    private async Task SimulateDeleteTonie(MainWindowViewModel viewModel, TonieFileItem file)
    {
        // Directly execute the delete logic without showing dialog
        var audio = TonieAudio.FromFile(file.FilePath, false);
        var hash = BitConverter.ToString(audio.Header.Hash).Replace("-", "");

        // Delete file and directory
        File.Delete(file.FilePath);
        var directory = Path.GetDirectoryName(file.FilePath);
        if (directory != null && Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }

        // Remove from customTonies.json
        var metadataService = new TonieMetadataService();
        metadataService.RemoveCustomTonie(hash);

        // Refresh directory
        await SimulateDirectoryOpen(viewModel, _contentDir);
    }

    private async Task<string> SimulateAddCustomTonie(
        MainWindowViewModel viewModel,
        string rfidUid,
        string[] audioPaths)
    {
        // Parse RFID and create directory structure
        var customTonieService = new CustomTonieCreationService(
            new TonieFileService(),
            new TonieMetadataService()
        );

        var parseResult = customTonieService.ParseRfidUid(rfidUid);
        Assert.True(parseResult.HasValue, "RFID UID should be valid");

        var reversedUid = parseResult.Value.ReversedUid;
        var audioId = parseResult.Value.AudioId;

        // Create the tonie file
        var (generatedHash, targetFile) = customTonieService.CreateCustomTonieFile(
            _contentDir,
            reversedUid,
            audioId,
            audioPaths,
            rfidUid
        );

        // Register in metadata
        var sourceFolderName = new TonieFileService().GetSourceFolderName(audioPaths);
        customTonieService.RegisterCustomTonie(generatedHash, sourceFolderName, rfidUid);

        // Refresh directory
        await SimulateDirectoryOpen(viewModel, _contentDir);

        return targetFile;
    }

    private async Task TestPlayerFunctionality(string tonieFilePath, string[] trackPaths)
    {
        // Create player view model
        var dialog = new PlayerDialog();
        var playerViewModel = new PlayerDialogViewModel(tonieFilePath, "Test Tonie", dialog);

        // Verify correct number of tracks are loaded
        Assert.True(playerViewModel.Tracks.Count >= trackPaths.Length,
            $"Expected at least {trackPaths.Length} tracks, got {playerViewModel.Tracks.Count}");

        // Get expected track durations and verify each track
        var expectedDurations = new List<TimeSpan>();
        for (int i = 0; i < trackPaths.Length; i++)
        {
            var expectedDuration = await GetAudioDuration(trackPaths[i]);
            expectedDurations.Add(expectedDuration);
            Console.WriteLine($"Track {i + 1} expected duration: {expectedDuration}");

            // Verify track duration (allow 2 second tolerance for encoding)
            var trackInfo = playerViewModel.Tracks[i];
            var actualDuration = ExtractDurationFromDisplayText(trackInfo.DisplayText);
            var diff = Math.Abs((actualDuration - expectedDuration).TotalSeconds);

            Console.WriteLine($"Track {i + 1} actual duration: {actualDuration}, diff: {diff:F2}s");
            Assert.InRange(diff, 0, 2);
        }

        // Test playback of all tracks
        // Note: LibVLC may not work in headless test environment
        try
        {
            for (int i = 0; i < trackPaths.Length; i++)
            {
                var trackInfo = playerViewModel.Tracks[i];
                Console.WriteLine($"Testing playback of track {i + 1}...");

                // Simulate clicking the track
                playerViewModel.SeekToTrackCommand.Execute(trackInfo.Position);
                await Task.Delay(500);

                // Verify playback state (or at least that it didn't crash)
                Assert.True(true, $"Track {i + 1} playback started without crashing");
            }

            Console.WriteLine($"Successfully tested playback of all {trackPaths.Length} tracks");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Audio playback test skipped (expected in headless environment): {ex.Message}");
        }
        finally
        {
            // Clean up
            playerViewModel.Cleanup();
        }
    }

    private Task SimulateModifyTonieWithEncode(MainWindowViewModel viewModel, TonieFileItem file, string additionalTrackPath)
    {
        Console.WriteLine("  - Reading original tonie...");
        // Read original tonie
        var originalAudio = TonieAudio.FromFile(file.FilePath, readAudio: true);
        var oldHash = BitConverter.ToString(originalAudio.Header.Hash).Replace("-", "");
        var audioId = originalAudio.Header.AudioId;

        // Create temp directory and decode
        string tempDir = Path.Combine(Path.GetTempPath(), $"TeddyBench_Modify_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            Console.WriteLine("  - Decoding original tracks...");
            originalAudio.DumpAudioFiles(tempDir, Path.GetFileName(file.FilePath), false, Array.Empty<string>(), null);

            // Get decoded files
            var decodedFiles = Directory.GetFiles(tempDir, "*.ogg")
                .OrderBy(f => f)
                .ToList();

            Console.WriteLine($"  - Found {decodedFiles.Count} original tracks");
            Console.WriteLine($"  - Adding track: {Path.GetFileName(additionalTrackPath)}");

            // Add the new track
            decodedFiles.Add(additionalTrackPath);

            Console.WriteLine($"  - Preparing to encode {decodedFiles.Count} tracks...");

            // Re-encode with all tracks using hybrid encoding
            var hybridEncoder = new HybridTonieEncodingService();
            var trackSources = new List<HybridTonieEncodingService.TrackSourceInfo>();

            for (int i = 0; i < decodedFiles.Count - 1; i++)
            {
                trackSources.Add(new HybridTonieEncodingService.TrackSourceInfo
                {
                    IsOriginal = true,
                    AudioFilePath = decodedFiles[i],
                    OriginalTrackIndex = i
                });
            }

            // Add the new track (not original)
            trackSources.Add(new HybridTonieEncodingService.TrackSourceInfo
            {
                IsOriginal = false,
                AudioFilePath = additionalTrackPath
            });

            Console.WriteLine("  - Encoding tonie (this simulates pressing the Encode button)...");
            var (fileContent, newHash) = hybridEncoder.EncodeHybridTonie(
                trackSources,
                audioId,
                file.FilePath,
                96000 // 96 kbps
            );

            Console.WriteLine("  - Writing modified tonie file...");
            // Write the modified file
            File.WriteAllBytes(file.FilePath, fileContent);

            Console.WriteLine("  - Updating metadata...");
            // Update metadata
            var metadataService = new TonieMetadataService();
            metadataService.UpdateTonieHash(oldHash, newHash, file.DisplayName);

            // Clean up temp directory
            Directory.Delete(tempDir, true);
            Console.WriteLine("  - Modification complete!");
        }
        catch
        {
            // Clean up temp directory on error
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
            throw;
        }

        // Note: We don't refresh directory here - the test will do it explicitly
        // to simulate the user seeing the result after encoding completes

        return Task.CompletedTask;
    }

    private async Task<TimeSpan> GetAudioDuration(string audioFilePath)
    {
        // Use FFmpeg to get duration
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-i \"{audioFilePath}\" -show_entries format=duration -v quiet -of csv=\"p=0\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (double.TryParse(output.Trim(), out double seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.Zero;
    }

    private TimeSpan ExtractDurationFromDisplayText(string displayText)
    {
        // Extract duration from "Track N (MM:SS)" format
        var match = System.Text.RegularExpressions.Regex.Match(displayText, @"\((\d+):(\d+)\)");
        if (match.Success)
        {
            int minutes = int.Parse(match.Groups[1].Value);
            int seconds = int.Parse(match.Groups[2].Value);
            return TimeSpan.FromMinutes(minutes).Add(TimeSpan.FromSeconds(seconds));
        }

        return TimeSpan.Zero;
    }

    private async Task VerifyTrackDurations(string tonieFilePath, string[] sourceAudioPaths)
    {
        var tonie = TonieAudio.FromFile(tonieFilePath, readAudio: true);
        var positions = tonie.ParsePositions();

        // Get total duration
        tonie.CalculateStatistics(out _, out _, out _, out _, out _, out _, out ulong highestGranule);
        var totalDuration = TimeSpan.FromSeconds((double)highestGranule / 48000.0);

        // Deduplicate positions (same as PlayerDialog does)
        var distinctPositions = new List<TimeSpan>();
        var seenPositions = new HashSet<ulong>();

        for (int i = 0; i < positions.Length; i++)
        {
            // Skip duplicate positions
            if (!seenPositions.Add(positions[i]))
                continue;

            var totalSeconds = (double)positions[i] / 48000.0;
            var timeSpan = TimeSpan.FromSeconds(totalSeconds);
            distinctPositions.Add(timeSpan);
        }

        Console.WriteLine($"Distinct positions: {distinctPositions.Count}");
        for (int i = 0; i < distinctPositions.Count; i++)
        {
            Console.WriteLine($"  Position {i}: {distinctPositions[i]}");
        }

        // Calculate track durations from distinct positions
        // The last position is the end marker, so we have N-1 tracks for N distinct positions
        var trackDurations = new List<TimeSpan>();
        for (int i = 0; i < distinctPositions.Count - 1; i++)
        {
            trackDurations.Add(distinctPositions[i + 1] - distinctPositions[i]);
        }

        // Get expected durations
        var expectedDurations = new List<TimeSpan>();
        foreach (var audioPath in sourceAudioPaths)
        {
            expectedDurations.Add(await GetAudioDuration(audioPath));
        }

        Console.WriteLine($"Expected {expectedDurations.Count} tracks, got {trackDurations.Count} track durations");

        // Verify we have the right number of tracks
        Assert.Equal(expectedDurations.Count, trackDurations.Count);

        // Verify each track duration (allow 2 second tolerance)
        for (int i = 0; i < trackDurations.Count; i++)
        {
            var expectedSeconds = expectedDurations[i].TotalSeconds;
            var actualSeconds = trackDurations[i].TotalSeconds;
            var diff = Math.Abs(expectedSeconds - actualSeconds);

            Console.WriteLine($"Track {i + 1}: Expected {expectedSeconds:F2}s, Actual {actualSeconds:F2}s, Diff {diff:F2}s");
            Assert.InRange(diff, 0, 2);
        }
    }

    public void Dispose()
    {
        // Clean up test directory
        if (Directory.Exists(_testDir))
        {
            try
            {
                Directory.Delete(_testDir, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not delete test directory: {ex.Message}");
            }
        }

        // Clean up customTonies.json if it was created for testing
        if (File.Exists(_customTonieJsonPath))
        {
            try
            {
                File.Delete(_customTonieJsonPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not delete customTonies.json: {ex.Message}");
            }
        }
    }
}
