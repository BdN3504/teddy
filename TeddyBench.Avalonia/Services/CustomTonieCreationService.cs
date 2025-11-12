using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace TeddyBench.Avalonia.Services;

/// <summary>
/// Service for creating custom Tonie files.
/// Handles RFID validation, encoding, and file system operations.
/// </summary>
public class CustomTonieCreationService
{
    private readonly TonieFileService _tonieFileService;
    private readonly TonieMetadataService _metadataService;

    public CustomTonieCreationService(TonieFileService tonieFileService, TonieMetadataService metadataService)
    {
        _tonieFileService = tonieFileService;
        _metadataService = metadataService;
    }

    /// <summary>
    /// Validates an RFID UID input string.
    /// </summary>
    public (bool IsValid, string ErrorMessage) ValidateRfidUid(string uidInput)
    {
        if (string.IsNullOrWhiteSpace(uidInput))
        {
            return (false, "RFID UID is required (must match RFID tag on figurine)");
        }

        if (uidInput.Length != 8 || !Regex.IsMatch(uidInput, "^[0-9A-F]{8}$"))
        {
            return (false, "RFID UID must be exactly 8 hexadecimal characters");
        }

        return (true, string.Empty);
    }

    /// <summary>
    /// Parses an RFID UID and extracts the audio ID.
    /// </summary>
    /// <returns>The reversed UID and parsed audio ID, or null if parsing failed.</returns>
    public (string ReversedUid, uint AudioId)? ParseRfidUid(string uidInput)
    {
        // Reverse byte order
        string reversedUid = _tonieFileService.ReverseUidBytes(uidInput);

        // Parse as audio ID (last 4 bytes = last 8 hex chars of full RFID)
        string fullRfidWithSuffix = reversedUid + "500304E0";
        if (uint.TryParse(fullRfidWithSuffix.Substring(8, 8), NumberStyles.HexNumber, null, out uint audioId))
        {
            return (reversedUid, audioId);
        }

        return null;
    }

    /// <summary>
    /// Creates a custom Tonie file and saves it to the specified directory.
    /// </summary>
    /// <param name="targetDirectory">Base directory (typically CONTENT folder)</param>
    /// <param name="reversedUid">The reversed RFID UID for the directory name</param>
    /// <param name="audioId">The parsed audio ID</param>
    /// <param name="sortedAudioPaths">Sorted audio file paths</param>
    /// <param name="originalUid">The original UID for metadata (user-entered format)</param>
    /// <returns>The generated hash and target file path</returns>
    public (string Hash, string FilePath) CreateCustomTonieFile(
        string targetDirectory,
        string reversedUid,
        uint audioId,
        string[] sortedAudioPaths,
        string originalUid)
    {
        // Encode the audio files
        var (fileContent, hash) = _tonieFileService.EncodeCustomTonie(sortedAudioPaths, audioId, 96);

        // Create directory structure
        // Directory: reversedUid (e.g., "0451ED0E"), File: "500304E0" (constant suffix)
        string dirName = reversedUid;
        string fileName = "500304E0";

        string targetDir = Path.Combine(targetDirectory, dirName);
        Directory.CreateDirectory(targetDir);

        string targetFile = Path.Combine(targetDir, fileName);
        File.WriteAllBytes(targetFile, fileContent);

        return (hash, targetFile);
    }

    /// <summary>
    /// Registers a custom Tonie in the metadata database.
    /// </summary>
    public void RegisterCustomTonie(string hash, string sourceFolderName, string originalUid)
    {
        string customTitle = $"{sourceFolderName} [RFID: {originalUid}]";
        _metadataService.AddCustomTonie(hash, customTitle);
    }
}
