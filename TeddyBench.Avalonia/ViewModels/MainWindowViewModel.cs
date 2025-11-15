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
using System.Diagnostics;

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

    /// <summary>
    /// Automatically opens the directory picker on startup and attempts to navigate to an SD card if found.
    /// </summary>
    public async Task AutoOpenDirectoryOnStartup()
    {
        try
        {
            // Wait a brief moment for the window to fully initialize
            await Task.Delay(100);

            // Get the storage provider from the window
            var storageProvider = _window.StorageProvider;

            // Try to detect an SD card automatically and use it as suggested location
            IStorageFolder? suggestedStartLocation = null;
            string? sdCardPath = SdCardDetector.FindFirstSdCard();

            if (!string.IsNullOrEmpty(sdCardPath))
            {
                // Found an SD card! Use it as the suggested start location
                StatusText = $"Auto-detected SD card at {sdCardPath}. Opening directory picker...";
                suggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(sdCardPath);
            }
            else
            {
                // No SD card found, try removable storage paths
                StatusText = "Opening directory picker...";

                var removableStoragePaths = SdCardDetector.GetAllRemovableStoragePaths();

                if (removableStoragePaths.Count > 0)
                {
                    // Try to use first removable storage as suggested location
                    suggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(removableStoragePaths[0]);
                }
            }

            if (suggestedStartLocation == null)
            {
                // Fall back to user's home directory
                var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                suggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(homePath);
            }

            await Task.Delay(200); // Brief delay to show the message

            // Configure folder picker options
            var options = new FolderPickerOpenOptions
            {
                Title = "Select Tonie Files Directory",
                AllowMultiple = false,
                SuggestedStartLocation = suggestedStartLocation
            };

            // Show the folder picker dialog (user must click Open to confirm)
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
                    StatusText = "Could not access the selected folder. Ready.";
                }
            }
            else
            {
                StatusText = "No folder selected. Ready.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Startup error: {ex.Message}";
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
    private System.Collections.IList? _selectedItems;

    [ObservableProperty]
    private ObservableCollection<SortOptionItem> _sortOptions = new();

    [ObservableProperty]
    private SortOptionItem? _currentSortOption;

    public bool HasSelectedFile => SelectedFile != null && (SelectedItems?.Count ?? 0) <= 1;
    public bool HasValidDirectory => !string.IsNullOrEmpty(CurrentDirectory);
    public bool HasMultipleSelection => (SelectedItems?.Count ?? 0) > 1;

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
            // Check if we should prompt for Audio ID
            bool shouldPromptForAudioId = _configService.LoadAudioIdPrompt();
            uint? customAudioId = null;

            if (shouldPromptForAudioId)
            {
                // Show Audio ID prompt dialog
                var audioIdDialog = new HexInputDialog();
                var audioIdResult = await audioIdDialog.ShowDialog<bool?>(_window);

                if (audioIdResult == true)
                {
                    customAudioId = audioIdDialog.GetValue();
                    if (customAudioId == null)
                    {
                        StatusText = "Error: Invalid Audio ID";
                        return;
                    }
                }
                // If cancelled, customAudioId remains null and will be auto-generated
            }

            // Load RFID prefix from config file (4 characters in reverse byte order)
            string rfidPrefix = _configService.LoadRfidPrefix();

            // Get RFID UID from user using RfidInputDialog
            var rfidDialog = new RfidInputDialog(rfidPrefix, CurrentDirectory);
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
            uint audioId = customAudioId ?? parseResult.Value.AudioId; // Use custom Audio ID if provided, otherwise use generated one

            // Select audio files
            var storageProvider = _window.StorageProvider;

            // Try to start in the Music directory
            var musicPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            var suggestedLocation = await storageProvider.TryGetFolderFromPathAsync(musicPath);

            var filePickerOptions = new FilePickerOpenOptions
            {
                Title = "Select Audio Files",
                AllowMultiple = true,
                SuggestedStartLocation = suggestedLocation,
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

            // Show progress dialog
            var progressDialog = new ProgressDialog();
            var progressViewModel = new ProgressDialogViewModel(progressDialog, sortedAudioPaths.Length);
            progressDialog.DataContext = progressViewModel;

            // Create encode callback that reports to progress dialog
            var encodeCallback = new Services.AvaloniaEncodeCallback(progressViewModel);

            StatusText = $"Encoding {sortedAudioPaths.Length} file(s)...";
            IsScanning = true;

            string generatedHash = string.Empty;
            string targetFile = string.Empty;

            // Start encoding in background task
            var encodingTask = Task.Run(() =>
            {
                // Create custom Tonie file using the service with progress callback
                (generatedHash, targetFile) = _customTonieService.CreateCustomTonieFile(
                    CurrentDirectory,
                    reversedUid,
                    audioId,
                    sortedAudioPaths,
                    uidInput,
                    encodeCallback);

                // Notify completion
                progressViewModel.Complete();

                StatusText = $"Successfully created custom Tonie: {reversedUid}/500304E0";
            });

            // Show the progress dialog (non-blocking, but waits for encoding to finish)
            await progressDialog.ShowDialog(_window);

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

    public void UpdateSelectionState()
    {
        OnPropertyChanged(nameof(HasSelectedFile));
        OnPropertyChanged(nameof(HasMultipleSelection));
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
            catch
            {
                // Ignore - hash will be null
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
                catch
                {
                    // Ignore directory deletion errors
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
                    // Show friendly name without [LIVE] prefix for clarity
                    var displayName = tonieFile.DisplayName.Replace("[LIVE] ", "");
                    StatusText = $"Removing LIVE flag: {displayName}... ({filesProcessed}/{totalFiles})";
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
                catch
                {
                    errorCount++;
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
        catch
        {
            // Ignore image download errors
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

    [RelayCommand]
    private async Task PlayTonie(TonieFileItem? file)
    {
        if (file == null)
        {
            StatusText = "Please select a file first";
            return;
        }

        try
        {
            var dialog = new Dialogs.PlayerDialog();
            var viewModel = new PlayerDialogViewModel(file.FilePath, file.DisplayName, dialog);
            dialog.DataContext = viewModel;
            await dialog.ShowDialog(_window);
        }
        catch (Exception ex)
        {
            StatusText = $"Error opening player: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AssignNewUid(TonieFileItem? file)
    {
        if (file == null)
        {
            StatusText = "Please select a file first";
            return;
        }

        if (string.IsNullOrEmpty(CurrentDirectory))
        {
            StatusText = "Error: No directory is currently open";
            return;
        }

        try
        {
            // Get the current UID from the file path
            var fileInfo = new FileInfo(file.FilePath);
            var directory = fileInfo.Directory;
            if (directory == null)
            {
                StatusText = "Error: Could not determine current UID";
                return;
            }

            // The directory name is the reversed UID (first 8 chars)
            string currentReversedUid = directory.Name;

            // Reverse it back to get the display UID
            string currentDisplayUid = _tonieFileService.ReverseUidBytes(currentReversedUid);

            // Load RFID prefix from config file (4 characters in reverse byte order)
            string rfidPrefix = _configService.LoadRfidPrefix();

            // Show RFID input dialog with current UID pre-filled
            var rfidDialog = new RfidInputDialog(currentDisplayUid, CurrentDirectory);
            var rfidResult = await rfidDialog.ShowDialog<bool?>(_window);

            if (rfidResult != true)
            {
                StatusText = "Operation cancelled";
                return;
            }

            // Parse new RFID UID
            string newUidInput = rfidDialog.GetRfidUid()?.Replace(" ", "").Replace(":", "").ToUpper() ?? "";

            // Check if the UID has changed
            if (newUidInput == currentDisplayUid)
            {
                StatusText = "UID unchanged";
                return;
            }

            // Validate new RFID UID
            var (isValid, errorMessage) = _customTonieService.ValidateRfidUid(newUidInput);
            if (!isValid)
            {
                StatusText = $"Error: {errorMessage}";
                return;
            }

            // Reverse the new UID for directory naming
            string newReversedUid = _tonieFileService.ReverseUidBytes(newUidInput);

            // Build new paths
            string newDirPath = Path.Combine(CurrentDirectory, newReversedUid);
            string newFilePath = Path.Combine(newDirPath, "500304E0");

            // Check if new location already exists
            if (File.Exists(newFilePath))
            {
                StatusText = $"Error: A tonie with UID '{newUidInput}' already exists";
                return;
            }

            StatusText = $"Moving tonie from {currentDisplayUid} to {newUidInput}...";

            // Read the hash before moving (for updating customTonies.json if needed)
            string? hash = null;
            bool isCustomTonie = file.IsCustomTonie;
            try
            {
                var audio = TonieAudio.FromFile(file.FilePath, false);
                hash = BitConverter.ToString(audio.Header.Hash).Replace("-", "");
            }
            catch
            {
                // Ignore - hash will be null
            }

            // Create new directory
            Directory.CreateDirectory(newDirPath);

            // Move the file
            File.Move(file.FilePath, newFilePath);

            // Try to delete old directory if it's empty
            try
            {
                if (directory.GetFiles().Length == 0 && directory.GetDirectories().Length == 0)
                {
                    directory.Delete();
                }
            }
            catch
            {
                // Ignore errors deleting old directory
            }

            // Update RFID in customTonies.json if this is a custom tonie
            if (isCustomTonie && !string.IsNullOrEmpty(hash))
            {
                // Get the current custom tonie name from customTonies.json
                string? currentCustomName = _metadataService.GetCustomTonieName(hash);

                if (!string.IsNullOrEmpty(currentCustomName))
                {
                    // Extract the title part (without RFID)
                    string titlePart = currentCustomName;
                    var rfidMatch = Regex.Match(currentCustomName, @"^(.*?)\s*\[RFID:\s*[0-9A-F]{8}\]$", RegexOptions.IgnoreCase);
                    if (rfidMatch.Success)
                    {
                        titlePart = rfidMatch.Groups[1].Value.Trim();
                    }

                    // Create new name with updated RFID
                    string newCustomName = $"{titlePart} [RFID: {newUidInput}]";
                    _metadataService.UpdateCustomTonie(hash, newCustomName);
                }
            }

            StatusText = $"Successfully reassigned UID from {currentDisplayUid} to {newUidInput}";

            // Refresh the directory to show the updated tonie
            if (!string.IsNullOrEmpty(CurrentDirectory))
            {
                await ScanDirectory(CurrentDirectory);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error reassigning UID: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteMultipleTonie()
    {
        if (SelectedItems == null || SelectedItems.Count == 0)
        {
            StatusText = "No items selected";
            return;
        }

        try
        {
            // Create a list of items to delete (copy to avoid modification during enumeration)
            var itemsToDelete = SelectedItems.Cast<TonieFileItem>().ToList();
            int count = itemsToDelete.Count;

            // Show confirmation dialog
            var confirmDialog = new ConfirmDeleteDialog($"{count} tonies");
            var result = await confirmDialog.ShowDialog<bool?>(_window);

            if (result != true)
            {
                StatusText = "Delete cancelled";
                return;
            }

            StatusText = $"Deleting {count} tonies...";
            IsScanning = true;

            int successCount = 0;
            int errorCount = 0;

            foreach (var file in itemsToDelete)
            {
                try
                {
                    // Read the hash before deleting (if possible)
                    string? hash = null;
                    try
                    {
                        var audio = TonieAudio.FromFile(file.FilePath, false);
                        hash = BitConverter.ToString(audio.Header.Hash).Replace("-", "");
                    }
                    catch
                    {
                        // Ignore - hash will be null
                    }

                    // Delete the file and directory
                    var fileInfo = new FileInfo(file.FilePath);
                    var directory = fileInfo.Directory;

                    fileInfo.Delete();

                    if (directory != null && directory.Exists)
                    {
                        // Delete the directory if it's empty or force delete
                        try
                        {
                            directory.Delete(true); // true = recursive delete
                        }
                        catch
                        {
                            // Ignore directory deletion errors
                        }
                    }

                    // Remove from customTonies.json if we got the hash
                    if (!string.IsNullOrEmpty(hash))
                    {
                        _metadataService.RemoveCustomTonie(hash);
                    }

                    successCount++;
                }
                catch (Exception ex)
                {
                    StatusText = $"Error deleting {file.DisplayName}: {ex.Message}";
                    errorCount++;
                }
            }

            StatusText = $"Successfully deleted {successCount} tonie(s)" + (errorCount > 0 ? $", {errorCount} error(s)" : "");

            // Refresh the directory to update the view
            if (!string.IsNullOrEmpty(CurrentDirectory))
            {
                await ScanDirectory(CurrentDirectory);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error during bulk delete: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task RemoveMultipleLiveFlags()
    {
        if (SelectedItems == null || SelectedItems.Count == 0)
        {
            StatusText = "No items selected";
            return;
        }

        IsScanning = true;
        int removedCount = 0;
        int errorCount = 0;
        var itemsToProcess = SelectedItems.Cast<TonieFileItem>().ToList();
        int totalFiles = itemsToProcess.Count;

        try
        {
            StatusText = $"Removing LIVE flags from {totalFiles} selected tonie(s)...";
            await Task.Delay(100);

            foreach (var tonieFile in itemsToProcess)
            {
                try
                {
                    // Check if the file has LIVE flag
                    bool hasLiveFlag = _liveFlagService.GetHiddenAttribute(tonieFile.FilePath);

                    if (hasLiveFlag)
                    {
                        // Remove the flag
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
                }
                catch
                {
                    errorCount++;
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
                StatusText = "No LIVE flags found in selected tonies";
            }
        }
        finally
        {
            IsScanning = false;
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ModifyContents(TonieFileItem? file)
    {
        if (file == null)
        {
            StatusText = "Please select a file first";
            return;
        }

        try
        {
            // Read the original file to get its hash and audio ID
            TonieAudio originalAudio;
            try
            {
                originalAudio = TonieAudio.FromFile(file.FilePath, false);
            }
            catch (Exception ex)
            {
                StatusText = $"Error reading tonie file: {ex.Message}";
                return;
            }

            string oldHash = BitConverter.ToString(originalAudio.Header.Hash).Replace("-", "");
            uint audioId = originalAudio.Header.AudioId;

            // Check if this is a non-custom tonie and show warning
            if (!file.IsCustomTonie)
            {
                var confirmDialog = new ConfirmModifyTonieDialog(file.DisplayName);
                var confirmResult = await confirmDialog.ShowDialog<bool?>(_window);

                if (confirmResult != true)
                {
                    StatusText = "Modification cancelled";
                    return;
                }
            }

            // Create temporary directory for decoded files
            string tempDir = Path.Combine(Path.GetTempPath(), $"TeddyBench_Modify_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                StatusText = $"Decoding {file.FileName} to temporary storage...";
                await Task.Delay(100); // Brief delay for UI update

                // Decode the tonie to temp directory
                await Task.Run(() =>
                {
                    var audio = TonieAudio.FromFile(file.FilePath);
                    audio.DumpAudioFiles(tempDir, Path.GetFileName(file.FilePath), false, Array.Empty<string>(), null);
                });

                // Get the decoded files and create placeholder names
                var decodedFiles = Directory.GetFiles(tempDir, "*.ogg")
                    .OrderBy(f => f)
                    .ToArray();

                if (decodedFiles.Length == 0)
                {
                    StatusText = "Error: No audio tracks found in tonie file";
                    Directory.Delete(tempDir, true);
                    return;
                }

                // Store the temp directory path to detect original files later
                string tempDirPath = tempDir;

                StatusText = $"Opening track editor with {decodedFiles.Length} track(s)...";

                // Show track sorting dialog
                var trackSortDialog = new TrackSortDialog(decodedFiles);
                var sortResult = await trackSortDialog.ShowDialog<bool?>(_window);

                if (sortResult != true)
                {
                    StatusText = "Modification cancelled";
                    Directory.Delete(tempDir, true);
                    return;
                }

                // Get the sorted/modified file paths
                string[]? sortedAudioPaths = trackSortDialog.GetSortedFilePaths();

                if (sortedAudioPaths == null || sortedAudioPaths.Length == 0)
                {
                    StatusText = "Error: At least one track is required";
                    Directory.Delete(tempDir, true);
                    return;
                }

                StatusText = $"Encoding {sortedAudioPaths.Length} track(s)...";
                IsScanning = true;

                string newHash = string.Empty;

                await Task.Run(() =>
                {
                    // Build track sources - identify which are original vs new
                    var trackSources = new List<HybridTonieEncodingService.TrackSourceInfo>();

                    for (int i = 0; i < sortedAudioPaths.Length; i++)
                    {
                        string path = sortedAudioPaths[i];
                        bool isOriginal = path.StartsWith(tempDirPath);

                        var trackSource = new HybridTonieEncodingService.TrackSourceInfo
                        {
                            IsOriginal = isOriginal,
                            AudioFilePath = path
                        };

                        // If this is an original track, extract its track index from the filename
                        if (isOriginal)
                        {
                            // Filename format: "500304E0 - Track #01.ogg"
                            var fileName = Path.GetFileName(path);
                            var match = System.Text.RegularExpressions.Regex.Match(fileName, @"Track #(\d+)\.ogg");
                            if (match.Success && int.TryParse(match.Groups[1].Value, out int trackNum))
                            {
                                trackSource.OriginalTrackIndex = trackNum - 1; // Convert to 0-based index
                            }
                        }

                        trackSources.Add(trackSource);
                    }

                    // Use hybrid encoding to avoid re-encoding original tracks
                    var hybridEncoder = new HybridTonieEncodingService();
                    var (fileContent, hash) = hybridEncoder.EncodeHybridTonie(trackSources, audioId, file.FilePath, 96);
                    newHash = hash;

                    // Overwrite the original file
                    File.WriteAllBytes(file.FilePath, fileContent);

                    StatusText = $"Successfully modified {file.FileName}";
                });

                // Update customTonies.json with new hash
                // Determine the title for the metadata
                string metadataTitle = file.DisplayName;

                // If it was an official tonie, extract just the title without [LIVE] prefix
                if (!file.IsCustomTonie)
                {
                    metadataTitle = StringHelper.SanitizeTitle(file.DisplayName);

                    // Try to extract RFID from the directory structure
                    var fileInfo = new FileInfo(file.FilePath);
                    var directory = fileInfo.Directory;
                    if (directory?.Parent != null)
                    {
                        // Directory name is the reversed RFID
                        string reversedRfid = directory.Name;
                        // Reverse it back for display
                        string displayRfid = _tonieFileService.ReverseUidBytes(reversedRfid);
                        metadataTitle = $"{metadataTitle} [RFID: {displayRfid}]";
                    }
                }

                _metadataService.UpdateTonieHash(oldHash, newHash, metadataTitle);

                StatusText = $"Successfully modified {file.DisplayName}";

                // Clean up temp directory
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore temp directory cleanup errors
                }

                // Refresh the directory to show the updated tonie
                if (!string.IsNullOrEmpty(CurrentDirectory))
                {
                    await ScanDirectory(CurrentDirectory);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Error modifying tonie: {ex.Message}";

                // Clean up temp directory on error
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch
                {
                    // Ignore temp directory cleanup errors
                }
            }
            finally
            {
                IsScanning = false;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }
}
