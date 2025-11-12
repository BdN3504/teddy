using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TonieFile;
using System.Text.RegularExpressions;
using TeddyBench.Avalonia.Services;
using TeddyBench.Avalonia.Models;
using TeddyBench.Avalonia.Dialogs;
using TeddyBench.Avalonia.Utilities;

namespace TeddyBench.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly TonieMetadataService _metadataService;
    private readonly LiveFlagService _liveFlagService;
    private readonly ConfigurationService _configService;
    private readonly TonieFileService _tonieFileService;
    private readonly DirectoryScanService _scanService;
    private readonly TonieSortService _sortService;
    private readonly CustomTonieCreationService _customTonieService;

    public MainWindowViewModel(Window window)
    {
        _window = window;
        _metadataService = new TonieMetadataService();
        _liveFlagService = new LiveFlagService();
        _configService = new ConfigurationService();
        _tonieFileService = new TonieFileService();
        _scanService = new DirectoryScanService(_metadataService, _liveFlagService);
        _sortService = new TonieSortService();
        _customTonieService = new CustomTonieCreationService(_tonieFileService, _metadataService);

        // Hook up scan service events
        _scanService.ProgressUpdate += (s, message) => StatusText = message;
        _scanService.RequestImageDownload += (s, data) => _ = DownloadImageForTonieAsync(data.Hash, data.PicUrl, data.FilePath);

        // Initialize sort options from provider
        SortOptions = new ObservableCollection<SortOptionItem>(SortOptionsProvider.GetAllSortOptions());

        // Load sort option from configuration
        var savedSortOption = _configService.LoadSortOption();
        CurrentSortOption = SortOptions.FirstOrDefault(s => s.Option == savedSortOption) ?? SortOptions[0];

        // Try to download tonies.json on startup if it doesn't exist
        _ = InitializeMetadataAsync();
    }

    private async Task InitializeMetadataAsync()
    {
        try
        {
            // Check if tonies.json exists, if not download it
            var toniesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tonies.json");
            if (!File.Exists(toniesPath))
            {
                StatusText = "Downloading Tonie metadata database...";
                var success = await _metadataService.DownloadTonieJsonAsync();
                if (success)
                {
                    StatusText = "Metadata downloaded successfully. Ready.";
                }
                else
                {
                    StatusText = "Failed to download metadata. Using local cache if available.";
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error initializing metadata: {ex.Message}";
        }
    }

    [ObservableProperty]
    private string? _currentDirectory;

    [ObservableProperty]
    private string _statusText = "Ready. Open a directory to get started.";

    [ObservableProperty]
    private bool _isScanning = false;

    [ObservableProperty]
    private ObservableCollection<TonieFileItem> _tonieFiles = new();

    [ObservableProperty]
    private TonieFileItem? _selectedFile;

    [ObservableProperty]
    private string _selectedFileDetails = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SortOptionItem> _sortOptions = new();

    [ObservableProperty]
    private SortOptionItem? _currentSortOption;

    public bool HasSelectedFile => SelectedFile != null;
    public bool HasValidDirectory => !string.IsNullOrEmpty(CurrentDirectory);

    partial void OnSelectedFileChanged(TonieFileItem? value)
    {
        if (value != null)
        {
            UpdateSelectedFileDetails(value);
        }
        OnPropertyChanged(nameof(HasSelectedFile));
    }

    partial void OnCurrentDirectoryChanged(string? value)
    {
        OnPropertyChanged(nameof(HasValidDirectory));
    }

    partial void OnCurrentSortOptionChanged(SortOptionItem? value)
    {
        if (value != null)
        {
            // Save the sort option to config file
            _configService.SaveSortOption(value.Option);

            if (TonieFiles.Count > 0)
            {
                ApplySorting();
            }
        }
    }

    [RelayCommand]
    private async Task OpenDirectory()
    {
        try
        {
            StatusText = "Opening directory picker...";

            // Get the storage provider from the window
            var storageProvider = _window.StorageProvider;

            // Configure folder picker options
            var options = new FolderPickerOpenOptions
            {
                Title = "Select Tonie Files Directory",
                AllowMultiple = false
            };

            // Show the folder picker dialog
            var result = await storageProvider.OpenFolderPickerAsync(options);

            if (result.Count > 0)
            {
                // Get the selected folder path
                var selectedFolder = result[0];
                var folderPath = selectedFolder.TryGetLocalPath();

                if (!string.IsNullOrEmpty(folderPath))
                {
                    await ScanDirectory(folderPath);
                }
                else
                {
                    StatusText = "Could not access the selected folder";
                }
            }
            else
            {
                StatusText = "No folder selected";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddCustomTonie()
    {
        if (string.IsNullOrEmpty(CurrentDirectory))
        {
            StatusText = "Please open a directory first (must be Toniebox SD card CONTENT folder)";
            return;
        }

        try
        {
            // Load RFID prefix from config file (4 characters in reverse byte order)
            string rfidPrefix = _configService.LoadRfidPrefix();

            // Get RFID UID from user using RfidInputDialog
            var rfidDialog = new RfidInputDialog(rfidPrefix);
            var rfidResult = await rfidDialog.ShowDialog<bool?>(_window);

            if (rfidResult != true)
            {
                StatusText = "Operation cancelled";
                return;
            }

            // Parse RFID UID
            string uidInput = rfidDialog.GetRfidUid()?.Replace(" ", "").Replace(":", "").ToUpper() ?? "";

            // Validate RFID UID
            var (isValid, errorMessage) = _customTonieService.ValidateRfidUid(uidInput);
            if (!isValid)
            {
                StatusText = $"Error: {errorMessage}";
                return;
            }

            // Parse RFID UID and extract audio ID
            var parseResult = _customTonieService.ParseRfidUid(uidInput);
            if (!parseResult.HasValue)
            {
                StatusText = "Error: Invalid RFID UID format";
                return;
            }

            string reversedUid = parseResult.Value.ReversedUid;
            uint audioId = parseResult.Value.AudioId;

            // Select audio files
            var storageProvider = _window.StorageProvider;
            var filePickerOptions = new FilePickerOpenOptions
            {
                Title = "Select Audio Files",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Audio Files") { Patterns = new[] { "*.mp3", "*.ogg", "*.flac", "*.wav", "*.m4a", "*.aac", "*.wma" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            };

            var selectedFiles = await storageProvider.OpenFilePickerAsync(filePickerOptions);

            if (selectedFiles.Count == 0)
            {
                StatusText = "No audio files selected";
                return;
            }

            // Fix file paths - ensure they start with '/' on Linux
            string[] audioPaths = selectedFiles
                .Select(f => f.TryGetLocalPath())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p =>
                {
                    // On Linux, TryGetLocalPath() sometimes returns paths without leading '/'
                    if (OperatingSystem.IsLinux() && !p!.StartsWith("/"))
                    {
                        return "/" + p;
                    }
                    return p!;
                })
                .ToArray();

            // Show track sorting dialog
            var trackSortDialog = new TrackSortDialog(audioPaths);
            var sortResult = await trackSortDialog.ShowDialog<bool?>(_window);

            if (sortResult != true)
            {
                StatusText = "Operation cancelled";
                return;
            }

            // Get the sorted file paths
            string[] sortedAudioPaths = trackSortDialog.GetSortedFilePaths() ?? audioPaths;

            // Determine the folder name from source files
            string sourceFolderName = _tonieFileService.GetSourceFolderName(sortedAudioPaths);

            StatusText = $"Encoding {sortedAudioPaths.Length} file(s)...";
            IsScanning = true;

            string generatedHash = string.Empty;
            string targetFile = string.Empty;

            await Task.Run(() =>
            {
                // Create custom Tonie file using the service
                (generatedHash, targetFile) = _customTonieService.CreateCustomTonieFile(
                    CurrentDirectory,
                    reversedUid,
                    audioId,
                    sortedAudioPaths,
                    uidInput);

                StatusText = $"Successfully created custom Tonie: {reversedUid}/500304E0";
            });

            // Register custom tonie in metadata
            if (!string.IsNullOrEmpty(generatedHash))
            {
                _customTonieService.RegisterCustomTonie(generatedHash, sourceFolderName, uidInput);
            }

            // Refresh the directory to show the new Tonie
            await ScanDirectory(CurrentDirectory);
        }
        catch (Exception ex)
        {
            StatusText = $"Error creating custom Tonie: {ex.Message}";
            Console.WriteLine($"Error: {ex}");
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task DecodeSelected()
    {
        if (SelectedFile == null)
        {
            StatusText = "Please select a file first";
            return;
        }

        try
        {
            // Show folder picker dialog with default to home folder
            var storageProvider = _window.StorageProvider;

            var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var options = new FolderPickerOpenOptions
            {
                Title = "Select Destination Folder for Decoded Files",
                AllowMultiple = false,
                SuggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(homePath)
            };

            var result = await storageProvider.OpenFolderPickerAsync(options);

            if (result.Count == 0)
            {
                StatusText = "Decode cancelled - no folder selected";
                return;
            }

            var selectedFolder = result[0];
            var baseFolderPath = selectedFolder.TryGetLocalPath();

            if (string.IsNullOrEmpty(baseFolderPath))
            {
                StatusText = "Error: Could not access the selected folder";
                return;
            }

            // Sanitize the title - remove all bracketed content
            string sanitizedTitle = StringHelper.SanitizeTitle(SelectedFile.DisplayName);

            // Create subfolder with sanitized title
            var outputDir = Path.Combine(baseFolderPath, sanitizedTitle);
            Directory.CreateDirectory(outputDir);

            StatusText = $"Decoding {SelectedFile.FileName}...";

            var audio = TonieAudio.FromFile(SelectedFile.FilePath);
            audio.DumpAudioFiles(outputDir, Path.GetFileName(SelectedFile.FilePath), false, Array.Empty<string>(), null);

            StatusText = $"Successfully decoded to {outputDir}";
        }
        catch (Exception ex)
        {
            StatusText = $"Decode error: {ex.Message}";
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ShowInfo()
    {
        if (SelectedFile == null)
        {
            StatusText = "Please select a file first";
            return;
        }

        try
        {
            var audio = TonieAudio.FromFile(SelectedFile.FilePath, false);

            SelectedFileDetails = $"File: {SelectedFile.FileName}\n" +
                                $"Audio ID: 0x{audio.Header.AudioId:X8}\n" +
                                $"Audio Length: {audio.Header.AudioLength} bytes\n" +
                                $"Chapters: {audio.Header.AudioChapters.Length}\n" +
                                $"Hash: {BitConverter.ToString(audio.Header.Hash).Replace("-", "")}\n" +
                                $"Hash Valid: {audio.HashCorrect}";

            StatusText = "File information loaded";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading info: {ex.Message}";
            SelectedFileDetails = string.Empty;
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        if (!string.IsNullOrEmpty(CurrentDirectory))
        {
            await ScanDirectory(CurrentDirectory);
        }
    }

    [RelayCommand]
    private void Exit()
    {
        // Save configuration before exiting
        if (CurrentSortOption != null)
        {
            _configService.SaveSortOption(CurrentSortOption.Option);
        }

        Environment.Exit(0);
    }

    [RelayCommand]
    private void About()
    {
        StatusText = "TeddyBench Avalonia - Cross-platform Tonie File Manager";
    }

    [RelayCommand]
    private void SelectTonie(TonieFileItem item)
    {
        // Deselect all items
        foreach (var tonieFile in TonieFiles)
        {
            tonieFile.IsSelected = false;
        }

        // Select the clicked item
        item.IsSelected = true;
        SelectedFile = item;
    }

    [RelayCommand]
    private void ToggleLiveFlag(TonieFileItem? file)
    {
        if (file == null)
        {
            StatusText = "Please select a file first";
            return;
        }

        try
        {
            StatusText = $"Checking LIVE flag for {file.FileName}...";

            // Check current state and toggle
            bool currentlyHidden = _liveFlagService.GetHiddenAttribute(file.FilePath);
            bool newState = !currentlyHidden;

            StatusText = newState ? $"Adding LIVE flag to {file.FileName}..." : $"Removing LIVE flag from {file.FileName}...";
            bool success = _liveFlagService.SetHiddenAttribute(file.FilePath, newState);

            if (success)
            {
                file.IsLive = newState;
                StatusText = newState ? $"Added LIVE flag to {file.FileName}" : $"Removed LIVE flag from {file.FileName}";

                // Update DisplayName
                var titleWithoutLive = file.DisplayName.Replace("[LIVE] ", "");
                file.DisplayName = file.IsLive ? $"[LIVE] {titleWithoutLive}" : titleWithoutLive;
            }
            else
            {
                StatusText = $"Failed to toggle LIVE flag for {file.FileName}";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error toggling LIVE flag: {ex.Message}");
            StatusText = $"Error toggling LIVE flag: {ex.Message}";
        }
    }


    [RelayCommand]
    private async Task DecodeTonie(TonieFileItem? file)
    {
        if (file == null)
        {
            StatusText = "Please select a file first";
            return;
        }

        try
        {
            // Show folder picker dialog with default to home folder
            var storageProvider = _window.StorageProvider;

            var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var options = new FolderPickerOpenOptions
            {
                Title = "Select Destination Folder for Decoded Files",
                AllowMultiple = false,
                SuggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(homePath)
            };

            var result = await storageProvider.OpenFolderPickerAsync(options);

            if (result.Count == 0)
            {
                StatusText = "Decode cancelled - no folder selected";
                return;
            }

            var selectedFolder = result[0];
            var baseFolderPath = selectedFolder.TryGetLocalPath();

            if (string.IsNullOrEmpty(baseFolderPath))
            {
                StatusText = "Error: Could not access the selected folder";
                return;
            }

            // Sanitize the title - remove all bracketed content
            string sanitizedTitle = StringHelper.SanitizeTitle(file.DisplayName);

            // Create subfolder with sanitized title
            var outputDir = Path.Combine(baseFolderPath, sanitizedTitle);
            Directory.CreateDirectory(outputDir);

            StatusText = $"Decoding {file.FileName}...";

            var audio = TonieAudio.FromFile(file.FilePath);
            audio.DumpAudioFiles(outputDir, Path.GetFileName(file.FilePath), false, Array.Empty<string>(), null);

            StatusText = $"Successfully decoded to {outputDir}";
        }
        catch (Exception ex)
        {
            StatusText = $"Decode error: {ex.Message}";
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ShowTonieInfo(TonieFileItem? file)
    {
        if (file == null)
        {
            StatusText = "Please select a file first";
            return;
        }

        try
        {
            // Set as selected file so details show
            SelectedFile = file;

            var audio = TonieAudio.FromFile(file.FilePath, false);

            SelectedFileDetails = $"File: {file.FileName}\n" +
                                $"Audio ID: 0x{audio.Header.AudioId:X8}\n" +
                                $"Audio Length: {audio.Header.AudioLength} bytes\n" +
                                $"Chapters: {audio.Header.AudioChapters.Length}\n" +
                                $"Hash: {BitConverter.ToString(audio.Header.Hash).Replace("-", "")}\n" +
                                $"Hash Valid: {audio.HashCorrect}";

            StatusText = "File information loaded";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading info: {ex.Message}";
            SelectedFileDetails = string.Empty;
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DeleteSelectedTonie(TonieFileItem? file)
    {
        if (file == null)
        {
            StatusText = "Please select a file first";
            return;
        }

        try
        {
            // Show confirmation dialog using ConfirmDeleteDialog
            var confirmDialog = new ConfirmDeleteDialog(file.DisplayName);
            var result = await confirmDialog.ShowDialog<bool?>(_window);

            if (result != true)
            {
                StatusText = "Delete cancelled";
                return;
            }

            // Read the hash before deleting (if possible)
            string? hash = null;
            try
            {
                var audio = TonieAudio.FromFile(file.FilePath, false);
                hash = BitConverter.ToString(audio.Header.Hash).Replace("-", "");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not read hash from file before deleting: {ex.Message}");
            }

            // Delete the file and directory
            var fileInfo = new FileInfo(file.FilePath);
            var directory = fileInfo.Directory;

            StatusText = $"Deleting {file.FileName}...";

            fileInfo.Delete();

            if (directory != null && directory.Exists)
            {
                // Delete the directory if it's empty or force delete
                try
                {
                    directory.Delete(true); // true = recursive delete
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not delete directory {directory.FullName}: {ex.Message}");
                }
            }

            // Remove from customTonies.json if we got the hash
            if (!string.IsNullOrEmpty(hash))
            {
                _metadataService.RemoveCustomTonie(hash);
            }

            StatusText = $"Successfully deleted {file.DisplayName}";

            // Refresh the directory to update the view
            if (!string.IsNullOrEmpty(CurrentDirectory))
            {
                await ScanDirectory(CurrentDirectory);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error deleting file: {ex.Message}";
            Console.WriteLine($"Error: {ex}");
        }
    }

    [RelayCommand]
    private async Task RenameSelectedTonie(TonieFileItem? file)
    {
        if (file == null)
        {
            StatusText = "Please select a file first";
            return;
        }

        // Only allow renaming custom tonies
        if (!file.IsCustomTonie)
        {
            StatusText = "Only custom tonies can be renamed. Official tonies from the database cannot be renamed.";
            return;
        }

        try
        {
            // Get the hash first
            string? hash = null;
            try
            {
                var audio = TonieAudio.FromFile(file.FilePath, false);
                hash = BitConverter.ToString(audio.Header.Hash).Replace("-", "");
            }
            catch (Exception ex)
            {
                StatusText = $"Error reading file: {ex.Message}";
                Console.WriteLine($"Error reading file: {ex}");
                return;
            }

            if (string.IsNullOrEmpty(hash))
            {
                StatusText = "Error: Could not read file hash";
                return;
            }

            // Extract the current title without [LIVE] and [RFID: ...] prefixes
            string currentTitle = StringHelper.SanitizeTitle(file.DisplayName);

            // Show rename dialog using RenameTonieDialog
            var renameDialog = new RenameTonieDialog(currentTitle);
            var result = await renameDialog.ShowDialog<bool?>(_window);

            if (result != true)
            {
                StatusText = "Rename cancelled";
                return;
            }

            string newTitle = renameDialog.GetNewTitle()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(newTitle))
            {
                StatusText = "Error: Title cannot be empty";
                return;
            }

            // Extract RFID from current title if present (to preserve it)
            string? rfidPart = null;
            var rfidMatch = Regex.Match(file.DisplayName, @"\[RFID:\s*([0-9A-F]{8})\]", RegexOptions.IgnoreCase);
            if (rfidMatch.Success)
            {
                rfidPart = $" [RFID: {rfidMatch.Groups[1].Value}]";
            }

            // Construct the full new title with RFID (if it existed)
            string fullNewTitle = newTitle + (rfidPart ?? "");

            // Update in customTonies.json
            _metadataService.UpdateCustomTonie(hash, fullNewTitle);

            // Update the DisplayName in the UI
            bool isLive = file.IsLive;
            file.DisplayName = isLive ? $"[LIVE] {fullNewTitle}" : fullNewTitle;

            StatusText = $"Successfully renamed tonie to '{newTitle}'";
        }
        catch (Exception ex)
        {
            StatusText = $"Error renaming tonie: {ex.Message}";
            Console.WriteLine($"Error: {ex}");
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RemoveAllLiveFlags()
    {
        IsScanning = true;
        int removedCount = 0;
        int errorCount = 0;
        int totalFiles = TonieFiles.Count;
        int filesProcessed = 0;

        try
        {
            StatusText = "Scanning for LIVE flags...";
            await Task.Delay(100);

            foreach (var tonieFile in TonieFiles)
            {
                filesProcessed++;

                // Skip files that aren't marked as LIVE - they can't have the flag
                // (Official tonies from database are never LIVE, only custom files)
                if (!tonieFile.IsLive)
                {
                    continue;
                }

                try
                {
                    // This file is marked as LIVE, so remove the flag
                    StatusText = $"Removing LIVE flag from {tonieFile.FileName}... ({filesProcessed}/{totalFiles})";
                    await Task.Delay(1);

                    bool success = _liveFlagService.SetHiddenAttribute(tonieFile.FilePath, false);
                    if (success)
                    {
                        tonieFile.IsLive = false;

                        // Update DisplayName to remove [LIVE] prefix
                        var titleWithoutLive = tonieFile.DisplayName.Replace("[LIVE] ", "");
                        tonieFile.DisplayName = titleWithoutLive;

                        removedCount++;
                    }
                    else
                    {
                        errorCount++;
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    Console.WriteLine($"Error removing LIVE flag from {tonieFile.FileName}: {ex.Message}");
                }
            }

            if (errorCount > 0)
            {
                StatusText = $"Removed LIVE flag from {removedCount} tonie(s), {errorCount} error(s)";
            }
            else if (removedCount > 0)
            {
                StatusText = $"Successfully removed LIVE flag from {removedCount} tonie(s)";
            }
            else
            {
                StatusText = "No LIVE flags found to remove";
            }
        }
        finally
        {
            IsScanning = false;
        }

        await Task.CompletedTask;
    }

    private async Task ScanDirectory(string directory)
    {
        IsScanning = true;
        TonieFiles.Clear();
        StatusText = $"Directory selected: {directory}";

        await Task.Delay(100); // Brief delay to ensure UI updates

        try
        {
            // Check if this looks like a Toniebox SD card root and navigate to CONTENT folder
            directory = _scanService.CheckForContentDirectory(directory);
            if (directory.EndsWith("CONTENT"))
            {
                StatusText = $"Detected Toniebox SD card, navigating to CONTENT folder...";
                await Task.Delay(100);
            }

            CurrentDirectory = directory;

            // Use DirectoryScanService to scan the directory
            var scannedFiles = await _scanService.ScanDirectoryAsync(directory);

            // Add all scanned files to the collection
            foreach (var file in scannedFiles)
            {
                TonieFiles.Add(file);
            }

            // Apply sorting after loading files
            ApplySorting();
        }
        catch (Exception ex)
        {
            StatusText = $"Error scanning directory: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void ApplySorting()
    {
        if (CurrentSortOption == null || TonieFiles.Count == 0)
            return;

        // Use TonieSortService to sort the files
        var sortedList = _sortService.SortTonieFiles(TonieFiles, CurrentSortOption.Option);

        // Replace TonieFiles collection with sorted list
        TonieFiles.Clear();
        foreach (var item in sortedList)
        {
            TonieFiles.Add(item);
        }
    }

    private void UpdateSelectedFileDetails(TonieFileItem file)
    {
        try
        {
            var fileInfo = new FileInfo(file.FilePath);
            SelectedFileDetails = $"File: {file.FileName}\n" +
                                $"Size: {fileInfo.Length / 1024} KB\n" +
                                $"Modified: {fileInfo.LastWriteTime}\n" +
                                $"Path: {file.FilePath}";
        }
        catch (Exception ex)
        {
            SelectedFileDetails = $"Error: {ex.Message}";
        }
    }

    private async Task DownloadImageForTonieAsync(string hash, string picUrl, string filePath)
    {
        try
        {
            var imagePath = await _metadataService.DownloadAndCacheImageAsync(hash, picUrl);

            if (!string.IsNullOrEmpty(imagePath))
            {
                // Find the corresponding TonieFileItem and update its ImagePath
                var item = TonieFiles.FirstOrDefault(t => t.FilePath == filePath);
                if (item != null)
                {
                    item.ImagePath = imagePath;
                    // Trigger property change notification
                    OnPropertyChanged(nameof(TonieFiles));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error downloading image for {hash}: {ex.Message}");
        }
    }

    public void SaveConfigurationOnExit()
    {
        // Save current sort option before exiting
        if (CurrentSortOption != null)
        {
            _configService.SaveSortOption(CurrentSortOption.Option);
        }
    }
}
