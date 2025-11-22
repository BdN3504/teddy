using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TeddyBench.Avalonia.Models;
using TonieFile;

namespace TeddyBench.Avalonia.Services
{
    public class TrashcanService
    {
        private readonly TonieMetadataService _metadataService;

        public TrashcanService(TonieMetadataService metadataService)
        {
            _metadataService = metadataService;
        }

        /// <summary>
        /// Scans the TRASHCAN directory for deleted Tonie files.
        /// </summary>
        /// <param name="sdCardPath">Path to the SD card root</param>
        /// <returns>List of deleted Tonie items</returns>
        public async Task<List<DeletedTonieItem>> ScanTrashcanAsync(string sdCardPath)
        {
            var deletedTonies = new List<DeletedTonieItem>();

            try
            {
                var trashcanPath = Path.Combine(sdCardPath, "TRASHCAN");

                if (!Directory.Exists(trashcanPath))
                {
                    return deletedTonies;
                }

                // Get all subdirectories in TRASHCAN
                var subdirs = Directory.GetDirectories(trashcanPath);

                foreach (var subdir in subdirs)
                {
                    var dirName = Path.GetFileName(subdir);

                    // Get all .043 files in this directory
                    var files = Directory.GetFiles(subdir, "*.043");

                    foreach (var file in files)
                    {
                        try
                        {
                            var deletedTonie = await ParseDeletedTonieAsync(file, dirName);
                            if (deletedTonie != null)
                            {
                                deletedTonies.Add(deletedTonie);
                            }
                        }
                        catch
                        {
                            // Skip files that can't be parsed
                        }
                    }
                }
            }
            catch
            {
                // Return empty list on error
            }

            return deletedTonies;
        }

        /// <summary>
        /// Parses a single deleted Tonie file from TRASHCAN.
        /// </summary>
        private async Task<DeletedTonieItem?> ParseDeletedTonieAsync(string filePath, string trashcanDirectory)
        {
            try
            {
                // Parse the Tonie file
                var tonie = await Task.Run(() => TonieAudio.FromFile(filePath, false));

                // Get deletion date from file modification time
                var fileInfo = new FileInfo(filePath);
                var deletionDate = fileInfo.LastWriteTime;

                // Get metadata
                var hash = BitConverter.ToString(tonie.Header.Hash).Replace("-", "");
                var (title, imagePath, isCustom) = _metadataService.GetTonieInfo(hash);

                // Extract UID from customTonies.json title if it's a custom tonie
                // The Toniebox uses an internal UID for TRASHCAN that's different from the RFID tag UID
                // However, custom tonies store the RFID in their title: "Title [RFID: 0EED33EA]"
                // We extract this RFID to enable restoration to the correct CONTENT location
                string uid = "Unknown";
                if (isCustom && !string.IsNullOrEmpty(title))
                {
                    // Try to extract RFID from title using regex pattern
                    var rfidMatch = System.Text.RegularExpressions.Regex.Match(
                        title,
                        @"\[RFID:\s*([0-9A-F]{8})\]",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    );
                    if (rfidMatch.Success)
                    {
                        uid = rfidMatch.Groups[1].Value.ToUpperInvariant();
                    }
                }

                // Calculate duration using CalculateStatistics
                tonie.CalculateStatistics(out _, out _, out _, out _, out _, out _, out ulong highestGranule);
                var totalSeconds = highestGranule / 48000.0;
                var duration = FormatDuration(totalSeconds);

                var deletedTonie = new DeletedTonieItem
                {
                    FilePath = filePath,
                    DisplayName = title,
                    Uid = uid,
                    DeletionDate = deletionDate,
                    Hash = hash,
                    ImagePath = imagePath,
                    AudioId = $"0x{tonie.Header.AudioId:X8}",
                    Duration = duration,
                    TrackCount = tonie.Header.AudioChapters?.Length ?? 0,
                    IsCustomTonie = isCustom,
                    TrashcanDirectory = trashcanDirectory
                };

                return deletedTonie;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Formats duration in seconds to a human-readable string.
        /// </summary>
        private string FormatDuration(double totalSeconds)
        {
            var ts = TimeSpan.FromSeconds(totalSeconds);

            if (ts.TotalHours >= 1)
            {
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            }
            else
            {
                return $"{ts.Minutes}:{ts.Seconds:D2}";
            }
        }

        /// <summary>
        /// Restores a deleted Tonie from TRASHCAN back to CONTENT.
        /// </summary>
        /// <param name="deletedTonie">The deleted Tonie to restore</param>
        /// <param name="sdCardPath">Path to the SD card root</param>
        /// <returns>True if restore was successful</returns>
        public async Task<(bool success, string message)> RestoreTonieAsync(DeletedTonieItem deletedTonie, string sdCardPath)
        {
            try
            {
                // Calculate the reversed UID for the directory name
                var reversedUid = ReverseByteOrder(deletedTonie.Uid);

                // Create target directory path
                var contentPath = Path.Combine(sdCardPath, "CONTENT", reversedUid);
                var targetFilePath = Path.Combine(contentPath, "500304E0");

                // Check if target already exists
                if (File.Exists(targetFilePath))
                {
                    return (false, $"Target file already exists: {reversedUid}/500304E0");
                }

                // Create directory if it doesn't exist
                if (!Directory.Exists(contentPath))
                {
                    Directory.CreateDirectory(contentPath);
                }

                // Copy the file (don't delete from TRASHCAN yet, safer to keep backup)
                await Task.Run(() => File.Copy(deletedTonie.FilePath, targetFilePath, false));

                // Update customTonies.json if it's a custom tonie
                if (deletedTonie.IsCustomTonie)
                {
                    _metadataService.AddCustomTonie(deletedTonie.Hash, deletedTonie.DisplayName);
                }

                return (true, $"Successfully restored to {reversedUid}/500304E0");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to restore: {ex.Message}");
            }
        }

        /// <summary>
        /// Permanently deletes a file from TRASHCAN.
        /// </summary>
        public async Task<(bool success, string message)> PermanentlyDeleteAsync(DeletedTonieItem deletedTonie)
        {
            try
            {
                await Task.Run(() => File.Delete(deletedTonie.FilePath));

                // Try to remove empty directory
                var directory = Path.GetDirectoryName(deletedTonie.FilePath);
                if (directory != null && Directory.Exists(directory))
                {
                    var remainingFiles = Directory.GetFiles(directory);
                    if (remainingFiles.Length == 0)
                    {
                        Directory.Delete(directory);
                    }
                }

                return (true, "File permanently deleted from TRASHCAN");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to delete: {ex.Message}");
            }
        }

        /// <summary>
        /// Reverses byte order of a hex string.
        /// Example: "04AE797906E4" -> "E4067979AE04"
        /// </summary>
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
    }
}