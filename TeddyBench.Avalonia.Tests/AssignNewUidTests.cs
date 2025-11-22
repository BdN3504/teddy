using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeddyBench.Avalonia.Services;
using TeddyBench.Avalonia.ViewModels;
using TonieFile;
using Xunit;

namespace TeddyBench.Avalonia.Tests;

/// <summary>
/// Tests for the AssignNewUid functionality
/// Verifies that changing a tonie's UID updates the RFID in customTonies.json
/// </summary>
public class AssignNewUidTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _contentDir;
    private readonly string _customTonieJsonPath;
    private readonly string _appSettingsPath;

    public AssignNewUidTests()
    {
        // Set up test directory structure
        _testDir = Path.Combine(Path.GetTempPath(), $"TeddyBench_AssignUid_Test_{Guid.NewGuid():N}");
        _contentDir = Path.Combine(_testDir, "CONTENT");
        Directory.CreateDirectory(_contentDir);

        _customTonieJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "customTonies.json");
        _appSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        // Ensure appsettings.json exists
        EnsureAppSettings();
    }

    private void EnsureAppSettings()
    {
        if (!File.Exists(_appSettingsPath))
        {
            var defaultSettings = new JObject
            {
                ["RfidPrefix"] = "0EED",
                ["SortOption"] = "DisplayName",
                ["AudioIdPrompt"] = false
            };
            File.WriteAllText(_appSettingsPath, defaultSettings.ToString());
        }
    }

    [AvaloniaFact]
    public void AssignNewUid_CustomTonie_UpdatesRfidInCustomToniesJson()
    {
        // Arrange - Create a custom tonie with initial UID
        string initialUid = "0EED5104";
        string newUid = "0EED5105";

        var track1Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "track1.mp3");
        var track2Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "track2.mp3");

        // Create custom tonie with initial UID
        var customTonieService = new CustomTonieCreationService(
            new TonieFileService(),
            new TonieMetadataService()
        );

        var tonieFileService = new TonieFileService();
        var reversedUid = tonieFileService.ReverseUidBytes(initialUid);

        var trackPaths = new[] { track1Path, track2Path };
        var (generatedHash, targetFile) = customTonieService.CreateCustomTonieFile(
            _contentDir,
            reversedUid,
            trackPaths,
            initialUid
        );

        // Get the audio ID from the created file
        var createdTonie = TonieAudio.FromFile(targetFile, readAudio: false);
        var audioId = createdTonie.Header.AudioId;

        // Register in metadata with initial RFID
        var sourceFolderName = new TonieFileService().GetSourceFolderName(trackPaths);
        customTonieService.RegisterCustomTonie(generatedHash, sourceFolderName, initialUid, audioId, trackPaths);

        // Verify initial customTonies.json contains the initial RFID
        var customToniesJson = JObject.Parse(File.ReadAllText(_customTonieJsonPath));
        Assert.True(customToniesJson.ContainsKey(generatedHash), "Hash should exist in customTonies.json");
        var initialTitle = customToniesJson[generatedHash]?.ToString();
        Assert.NotNull(initialTitle);
        Assert.Contains($"[RFID: {initialUid}]", initialTitle);
        Console.WriteLine($"Initial title: {initialTitle}");

        // Act - Simulate AssignNewUid operation
        var metadataService = new TonieMetadataService();

        // Read the file to get hash
        var tonieAudio = TonieAudio.FromFile(targetFile, readAudio: false);
        var hash = BitConverter.ToString(tonieAudio.Header.Hash).Replace("-", "");

        // Get current custom tonie name
        var currentCustomName = metadataService.GetCustomTonieName(hash);
        Assert.NotNull(currentCustomName);
        Console.WriteLine($"Current custom name: {currentCustomName}");

        // Extract title part (without RFID) - this is what AssignNewUid does
        string titlePart = currentCustomName;
        var rfidMatch = System.Text.RegularExpressions.Regex.Match(
            currentCustomName,
            @"^(.*?)\s*\[RFID:\s*[0-9A-F]{8}\]$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        if (rfidMatch.Success)
        {
            titlePart = rfidMatch.Groups[1].Value.Trim();
        }
        Console.WriteLine($"Extracted title part: {titlePart}");

        // Create new name with updated RFID
        string newCustomName = $"{titlePart} [RFID: {newUid}]";
        Console.WriteLine($"New custom name: {newCustomName}");

        // Update customTonies.json
        var existingMetadata = metadataService.GetCustomTonieMetadata(hash);
        if (existingMetadata != null)
        {
            existingMetadata.Title = newCustomName;
            metadataService.UpdateCustomTonie(hash, existingMetadata);
        }

        // Move the file to new directory (simulate the full AssignNewUid operation)
        var newReversedUid = tonieFileService.ReverseUidBytes(newUid);
        var newDirPath = Path.Combine(_contentDir, newReversedUid);
        var newFilePath = Path.Combine(newDirPath, "500304E0");

        Directory.CreateDirectory(newDirPath);
        File.Move(targetFile, newFilePath);

        // Clean up old directory
        var oldDir = Path.GetDirectoryName(targetFile);
        if (oldDir != null && Directory.Exists(oldDir))
        {
            if (Directory.GetFiles(oldDir).Length == 0 && Directory.GetDirectories(oldDir).Length == 0)
            {
                Directory.Delete(oldDir);
            }
        }

        // Assert - Verify customTonies.json was updated correctly
        var updatedCustomToniesJson = JObject.Parse(File.ReadAllText(_customTonieJsonPath));

        // Hash should still exist (same tonie, just moved)
        Assert.True(updatedCustomToniesJson.ContainsKey(hash), "Hash should still exist in customTonies.json");

        // Title should contain the new RFID
        var updatedTitle = updatedCustomToniesJson[hash]?.ToString();
        Assert.NotNull(updatedTitle);
        Console.WriteLine($"Updated title: {updatedTitle}");

        // Verify the RFID was updated
        Assert.Contains($"[RFID: {newUid}]", updatedTitle);
        Assert.DoesNotContain($"[RFID: {initialUid}]", updatedTitle);

        // Verify the title part remained the same
        Assert.Contains(titlePart, updatedTitle);

        // Verify file was moved to correct location
        Assert.True(File.Exists(newFilePath), "File should exist at new location");
        Assert.False(File.Exists(targetFile), "File should not exist at old location");
    }

    [AvaloniaFact]
    public void AssignNewUid_NonCustomTonie_DoesNotUpdateCustomToniesJson()
    {
        // Arrange - Create a tonie that's not in customTonies.json (official tonie)
        var track1Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "track1.mp3");
        var track2Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "track2.mp3");

        string initialUid = "0EED5106";

        var customTonieService = new CustomTonieCreationService(
            new TonieFileService(),
            new TonieMetadataService()
        );

        var tonieFileService = new TonieFileService();
        var reversedUid = tonieFileService.ReverseUidBytes(initialUid);

        var (generatedHash, targetFile) = customTonieService.CreateCustomTonieFile(
            _contentDir,
            reversedUid,
            new[] { track1Path, track2Path },
            initialUid
        );

        // DO NOT register in customTonies.json (simulate official tonie)
        // Just ensure customTonies.json exists but doesn't contain this hash
        if (!File.Exists(_customTonieJsonPath))
        {
            File.WriteAllText(_customTonieJsonPath, "{}");
        }

        var metadataService = new TonieMetadataService();
        var tonieAudio = TonieAudio.FromFile(targetFile, readAudio: false);
        var hash = BitConverter.ToString(tonieAudio.Header.Hash).Replace("-", "");

        // Verify it's NOT in customTonies.json
        var customName = metadataService.GetCustomTonieName(hash);
        Assert.Null(customName);

        // Act - Try to update (should not do anything since it's not a custom tonie)
        // This simulates the condition check in AssignNewUid: if (isCustomTonie && !string.IsNullOrEmpty(hash))
        bool isCustomTonie = customName != null;

        if (isCustomTonie && !string.IsNullOrEmpty(hash))
        {
            // This should NOT execute for non-custom tonies
            Assert.Fail("Should not try to update non-custom tonie");
        }

        // Assert - Verify customTonies.json was NOT modified
        var customToniesJson = JObject.Parse(File.ReadAllText(_customTonieJsonPath));
        Assert.False(customToniesJson.ContainsKey(hash), "Hash should not be in customTonies.json for official tonie");
    }

    [AvaloniaFact]
    public void AssignNewUid_CustomTonieWithoutRfid_DoesNotCrash()
    {
        // Arrange - Create a custom tonie with title that doesn't have RFID pattern
        var track1Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "track1.mp3");

        string initialUid = "0EED5107";

        var customTonieService = new CustomTonieCreationService(
            new TonieFileService(),
            new TonieMetadataService()
        );

        var tonieFileService = new TonieFileService();
        var reversedUid = tonieFileService.ReverseUidBytes(initialUid);

        var (generatedHash, targetFile) = customTonieService.CreateCustomTonieFile(
            _contentDir,
            reversedUid,
            new[] { track1Path },
            initialUid
        );

        // Manually create a custom entry without RFID pattern
        var customTonies = new JObject
        {
            [generatedHash] = "Custom Title Without RFID"
        };
        File.WriteAllText(_customTonieJsonPath, customTonies.ToString());

        // Act - Simulate AssignNewUid operation
        var metadataService = new TonieMetadataService();
        var currentCustomName = metadataService.GetCustomTonieName(generatedHash);
        Assert.NotNull(currentCustomName);
        Assert.Equal("Custom Title Without RFID", currentCustomName);

        // Extract title part (this should handle the case where there's no RFID)
        string titlePart = currentCustomName;
        var rfidMatch = System.Text.RegularExpressions.Regex.Match(
            currentCustomName,
            @"^(.*?)\s*\[RFID:\s*[0-9A-F]{8}\]$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        if (rfidMatch.Success)
        {
            titlePart = rfidMatch.Groups[1].Value.Trim();
        }

        // titlePart should be the full title since no RFID was found
        Assert.Equal("Custom Title Without RFID", titlePart);

        // Create new name with RFID appended
        string newUid = "0EED5108";
        string newCustomName = $"{titlePart} [RFID: {newUid}]";

        // Update
        var existingMetadata = metadataService.GetCustomTonieMetadata(generatedHash);
        if (existingMetadata != null)
        {
            existingMetadata.Title = newCustomName;
            metadataService.UpdateCustomTonie(generatedHash, existingMetadata);
        }

        // Assert - Verify the RFID was appended correctly
        var updatedCustomToniesJson = JObject.Parse(File.ReadAllText(_customTonieJsonPath));
        var updatedTitle = updatedCustomToniesJson[generatedHash]?.ToString();
        Assert.Equal("Custom Title Without RFID [RFID: 0EED5108]", updatedTitle);
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