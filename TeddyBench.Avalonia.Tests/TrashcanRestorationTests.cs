using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeddyBench.Avalonia.Services;
using TonieFile;
using Xunit;

namespace TeddyBench.Avalonia.Tests;

/// <summary>
/// End-to-end tests for TRASHCAN deletion and restoration process.
/// Verifies that:
/// 1. Files can be "deleted" to TRASHCAN with changed Audio ID
/// 2. Files can be restored to correct CONTENT location
/// 3. Restored files are byte-for-byte identical to originals (Audio ID restored)
/// </summary>
public class TrashcanRestorationTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _contentDir;
    private readonly string _trashcanDir;
    private readonly string _customTonieJsonPath;

    public TrashcanRestorationTests()
    {
        // Set up test directory structure
        _testDir = Path.Combine(Path.GetTempPath(), $"TeddyBench_Trashcan_Test_{Guid.NewGuid():N}");
        _contentDir = Path.Combine(_testDir, "CONTENT");
        _trashcanDir = Path.Combine(_testDir, "TRASHCAN");

        Directory.CreateDirectory(_contentDir);
        Directory.CreateDirectory(_trashcanDir);

        _customTonieJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "customTonies.json");
    }

    [AvaloniaFact]
    public async Task TrashcanRestore_ShouldProduceByteForByteIdenticalFile()
    {
        Console.WriteLine("=== TRASHCAN Restoration End-to-End Test ===");
        Console.WriteLine();

        // Step 1: Create a custom tonie with known parameters
        Console.WriteLine("Step 1: Creating original tonie file...");
        var rfidUid = "0EED33EA";
        var reversedUid = ReverseByteOrder(rfidUid); // EA33ED0E
        var originalDir = Path.Combine(_contentDir, reversedUid);
        Directory.CreateDirectory(originalDir);
        var originalFilePath = Path.Combine(originalDir, "500304E0");

        var track1Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "track1.mp3");
        var track2Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "track2.mp3");

        Assert.True(File.Exists(track1Path), $"Test file not found: {track1Path}");
        Assert.True(File.Exists(track2Path), $"Test file not found: {track2Path}");

        // Create tonie with known Audio ID
        uint originalAudioId = 0x6921F586;
        var originalTonie = new TonieAudio(
            sources: new[] { track1Path, track2Path },
            audioId: originalAudioId,
            bitRate: 96000,
            useVbr: false,
            prefixLocation: null
        );

        File.WriteAllBytes(originalFilePath, originalTonie.FileContent);
        Console.WriteLine($"  ✓ Created tonie at: {reversedUid}/500304E0");
        Console.WriteLine($"  ✓ Original Audio ID: 0x{originalAudioId:X8}");

        // Step 2: Calculate hash of original file (entire file, not just audio data)
        Console.WriteLine();
        Console.WriteLine("Step 2: Calculating hash of original file...");
        string originalFileHash;
        using (var sha1 = SHA1.Create())
        {
            var fileBytes = File.ReadAllBytes(originalFilePath);
            var hashBytes = sha1.ComputeHash(fileBytes);
            originalFileHash = BitConverter.ToString(hashBytes).Replace("-", "");
        }
        Console.WriteLine($"  ✓ Original file hash: {originalFileHash}");

        // Also get the content hash (for customTonies.json)
        var contentHash = BitConverter.ToString(originalTonie.Header.Hash).Replace("-", "");
        Console.WriteLine($"  ✓ Content hash (protobuf): {contentHash}");

        // Step 3: Store metadata in customTonies.json
        Console.WriteLine();
        Console.WriteLine("Step 3: Storing metadata in customTonies.json...");
        var customTonies = new JArray
        {
            new JObject
            {
                ["no"] = "0",
                ["hash"] = new JArray { contentHash },
                ["title"] = $"Test Album [RFID: {rfidUid}]",
                ["directory"] = reversedUid,
                ["audio_id"] = new JArray { originalAudioId.ToString("X8") },
                ["tracks"] = new JArray { "Track 1", "Track 2" }
            }
        };
        File.WriteAllText(_customTonieJsonPath, customTonies.ToString(Formatting.Indented));
        Console.WriteLine($"  ✓ Metadata stored");
        Console.WriteLine($"  ✓ Directory field: {reversedUid}");
        Console.WriteLine($"  ✓ AudioId field: {originalAudioId:X8}");

        // Step 4: Simulate deletion process
        Console.WriteLine();
        Console.WriteLine("Step 4: Simulating deletion (Toniebox behavior)...");

        // Record deletion timestamp
        var deletionTimestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Console.WriteLine($"  - Deletion timestamp: 0x{deletionTimestamp:X8} ({deletionTimestamp})");

        // Move to TRASHCAN directory (use subdirectory like Toniebox does)
        var trashcanSubDir = Path.Combine(_trashcanDir, "7F0");
        Directory.CreateDirectory(trashcanSubDir);
        var trashcanFilePath = Path.Combine(trashcanSubDir, $"{deletionTimestamp:X8}.043");

        // Copy file to TRASHCAN
        File.Copy(originalFilePath, trashcanFilePath, true);
        Console.WriteLine($"  ✓ Moved to TRASHCAN: 7F0/{deletionTimestamp:X8}.043");

        // Change Audio ID to deletion timestamp (this is what the Toniebox does)
        var deletedTonie = TonieAudio.FromFile(trashcanFilePath, readAudio: true);
        deletedTonie.Header.AudioId = deletionTimestamp;
        deletedTonie.UpdateFileContent();
        File.WriteAllBytes(trashcanFilePath, deletedTonie.FileContent);
        Console.WriteLine($"  ✓ Changed Audio ID to deletion timestamp: 0x{deletionTimestamp:X8}");

        // Verify Audio ID was changed
        var verifyDeleted = TonieAudio.FromFile(trashcanFilePath, readAudio: false);
        Assert.Equal(deletionTimestamp, verifyDeleted.Header.AudioId);
        Console.WriteLine($"  ✓ Verified Audio ID in TRASHCAN: 0x{verifyDeleted.Header.AudioId:X8}");

        // Calculate hash of deleted file (should be different due to Audio ID change)
        string deletedFileHash;
        using (var sha1 = SHA1.Create())
        {
            var fileBytes = File.ReadAllBytes(trashcanFilePath);
            var hashBytes = sha1.ComputeHash(fileBytes);
            deletedFileHash = BitConverter.ToString(hashBytes).Replace("-", "");
        }
        Console.WriteLine($"  ✓ Deleted file hash: {deletedFileHash}");
        Assert.NotEqual(originalFileHash, deletedFileHash);
        Console.WriteLine($"  ✓ File hashes are different (Audio ID changed)");

        // Delete from original CONTENT location
        File.Delete(originalFilePath);
        Directory.Delete(originalDir, true);
        Console.WriteLine($"  ✓ Deleted from CONTENT/{reversedUid}/");

        // Step 5: Scan TRASHCAN
        Console.WriteLine();
        Console.WriteLine("Step 5: Scanning TRASHCAN...");
        var metadataService = new TonieMetadataService();
        var trashcanService = new TrashcanService(metadataService);

        var deletedTonies = await trashcanService.ScanTrashcanAsync(_testDir);
        Assert.Single(deletedTonies);
        var scannedTonie = deletedTonies.First();

        Console.WriteLine($"  ✓ Found 1 deleted tonie");
        Console.WriteLine($"  ✓ Display Name: {scannedTonie.DisplayName}");
        Console.WriteLine($"  ✓ UID: {scannedTonie.Uid}");
        Console.WriteLine($"  ✓ Audio ID (deleted): {scannedTonie.AudioId}");
        Console.WriteLine($"  ✓ Is Custom Tonie: {scannedTonie.IsCustomTonie}");

        Assert.Equal(rfidUid, scannedTonie.Uid);
        Assert.True(scannedTonie.IsCustomTonie);

        // Step 6: Restore the file
        Console.WriteLine();
        Console.WriteLine("Step 6: Restoring file from TRASHCAN...");
        var (success, message) = await trashcanService.RestoreTonieAsync(scannedTonie, _testDir);

        Assert.True(success, $"Restore should succeed. Message: {message}");
        Console.WriteLine($"  ✓ Restore succeeded: {message}");

        // Step 7: Verify restored file
        Console.WriteLine();
        Console.WriteLine("Step 7: Verifying restored file...");

        var restoredFilePath = Path.Combine(_contentDir, reversedUid, "500304E0");
        Assert.True(File.Exists(restoredFilePath), $"Restored file should exist at: {restoredFilePath}");
        Console.WriteLine($"  ✓ File exists at: CONTENT/{reversedUid}/500304E0");

        // Read restored file and check Audio ID
        var restoredTonie = TonieAudio.FromFile(restoredFilePath, readAudio: false);
        Console.WriteLine($"  ✓ Restored Audio ID: 0x{restoredTonie.Header.AudioId:X8}");
        Assert.Equal(originalAudioId, restoredTonie.Header.AudioId);
        Console.WriteLine($"  ✓ Audio ID matches original: 0x{originalAudioId:X8}");

        // Step 8: Calculate hash of restored file
        Console.WriteLine();
        Console.WriteLine("Step 8: Comparing file hashes...");
        string restoredFileHash;
        using (var sha1 = SHA1.Create())
        {
            var fileBytes = File.ReadAllBytes(restoredFilePath);
            var hashBytes = sha1.ComputeHash(fileBytes);
            restoredFileHash = BitConverter.ToString(hashBytes).Replace("-", "");
        }

        Console.WriteLine($"  Original file hash:  {originalFileHash}");
        Console.WriteLine($"  Restored file hash:  {restoredFileHash}");
        Assert.Equal(originalFileHash, restoredFileHash);
        Console.WriteLine($"  ✓ HASHES MATCH! File is byte-for-byte identical!");

        // Step 9: Verify content hash is still correct
        Console.WriteLine();
        Console.WriteLine("Step 9: Verifying content hash...");
        var restoredContentHash = BitConverter.ToString(restoredTonie.Header.Hash).Replace("-", "");
        Console.WriteLine($"  Original content hash: {contentHash}");
        Console.WriteLine($"  Restored content hash: {restoredContentHash}");
        Assert.Equal(contentHash, restoredContentHash);
        Console.WriteLine($"  ✓ Content hashes match!");

        Console.WriteLine();
        Console.WriteLine("=== TEST PASSED ===");
        Console.WriteLine("✓ Deletion process correctly changed Audio ID to timestamp");
        Console.WriteLine("✓ Restoration process correctly restored original Audio ID");
        Console.WriteLine("✓ Restored file is byte-for-byte identical to original");
    }

    private string ReverseByteOrder(string hexString)
    {
        if (string.IsNullOrEmpty(hexString) || hexString.Length % 2 != 0)
        {
            return hexString;
        }

        string reversed = "";
        for (int i = hexString.Length - 2; i >= 0; i -= 2)
        {
            reversed += hexString.Substring(i, 2);
        }
        return reversed;
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

        // Clean up customTonies.json
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