using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TonieFile;
using System.Text.RegularExpressions;
using TeddyBench.Avalonia.Services;

namespace TeddyBench.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly TonieMetadataService _metadataService;

    public MainWindowViewModel(Window window)
    {
        _window = window;
        _metadataService = new TonieMetadataService();

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

    public bool HasSelectedFile => SelectedFile != null;

    partial void OnSelectedFileChanged(TonieFileItem? value)
    {
        if (value != null)
        {
            UpdateSelectedFileDetails(value);
        }
        OnPropertyChanged(nameof(HasSelectedFile));
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
    private async Task EncodeFiles()
    {
        StatusText = "Encode feature - use CLI for now: dotnet Teddy.dll -m encode";
        // This would open a file picker and encode dialog
        // Implementation would use TonieAudio class
        await Task.CompletedTask;
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
            StatusText = $"Decoding {SelectedFile.FileName}...";

            var audio = TonieAudio.FromFile(SelectedFile.FilePath);
            var outputDir = Path.Combine(Path.GetDirectoryName(SelectedFile.FilePath)!, "decoded");
            Directory.CreateDirectory(outputDir);

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
            bool currentlyHidden = GetHiddenAttribute(file.FilePath);
            bool newState = !currentlyHidden;

            StatusText = newState ? $"Adding LIVE flag to {file.FileName}..." : $"Removing LIVE flag from {file.FileName}...";
            bool success = SetHiddenAttribute(file.FilePath, newState);

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

    private bool GetHiddenAttribute(string filePath)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                // On Linux, use fatattr to check DOS hidden attribute
                var result = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "fatattr",
                    Arguments = $"\"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (result != null)
                {
                    result.WaitForExit();
                    string output = result.StandardOutput.ReadToEnd();
                    // fatattr output format: "h" for hidden, "-" for not hidden
                    return output.Contains("h");
                }
            }
            else
            {
                // On Windows, use FileInfo.Attributes
                var fileInfo = new FileInfo(filePath);
                return fileInfo.Attributes.HasFlag(FileAttributes.Hidden);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking hidden attribute: {ex.Message}");
        }

        return false;
    }

    private bool SetHiddenAttribute(string filePath, bool hidden)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                // On Linux, use fatattr to set DOS hidden attribute
                string arguments = hidden ? $"+h \"{filePath}\"" : $"-h \"{filePath}\"";

                var result = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "fatattr",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (result != null)
                {
                    result.WaitForExit();
                    string error = result.StandardError.ReadToEnd();

                    if (result.ExitCode != 0 && !string.IsNullOrEmpty(error))
                    {
                        Console.WriteLine($"fatattr error: {error}");
                    }

                    return result.ExitCode == 0;
                }
            }
            else
            {
                // On Windows, use FileInfo.Attributes
                var fileInfo = new FileInfo(filePath);
                if (hidden)
                {
                    fileInfo.Attributes |= FileAttributes.Hidden;
                }
                else
                {
                    fileInfo.Attributes &= ~FileAttributes.Hidden;
                }

                // Verify it was set
                fileInfo.Refresh();
                return fileInfo.Attributes.HasFlag(FileAttributes.Hidden) == hidden;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting hidden attribute: {ex.Message}");
            StatusText = $"Error: {ex.Message}. Make sure 'fatattr' is installed (sudo apt install fatattr)";
        }

        return false;
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
            StatusText = $"Decoding {file.FileName}...";

            var audio = TonieAudio.FromFile(file.FilePath);
            var outputDir = Path.Combine(Path.GetDirectoryName(file.FilePath)!, "decoded");
            Directory.CreateDirectory(outputDir);

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

                try
                {
                    // Update status for current file
                    StatusText = $"Checking {tonieFile.FileName}... ({filesProcessed}/{totalFiles})";
                    await Task.Delay(1); // Brief delay to ensure UI updates

                    // Only process files that have the Hidden attribute
                    if (GetHiddenAttribute(tonieFile.FilePath))
                    {
                        StatusText = $"Removing LIVE flag from {tonieFile.FileName}... ({filesProcessed}/{totalFiles})";
                        await Task.Delay(1);

                        bool success = SetHiddenAttribute(tonieFile.FilePath, false);
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
            var dirInfo = new DirectoryInfo(directory);

            // Check if this looks like a Toniebox SD card root (has CONTENT directory)
            var contentDir = Path.Combine(directory, "CONTENT");
            if (Directory.Exists(contentDir))
            {
                StatusText = $"Detected Toniebox SD card, navigating to CONTENT folder...";
                await Task.Delay(100);
                directory = contentDir;
                dirInfo = new DirectoryInfo(directory);
            }

            CurrentDirectory = directory;
            StatusText = $"Scanning for Tonie files...";
            await Task.Delay(100);

            int filesProcessed = 0;
            int totalDirs = dirInfo.GetDirectories().Length;

            // Scan for Tonie files in subdirectories (format: XXXXXXXX/YYYYYYY0304E0)
            foreach (var subDir in dirInfo.GetDirectories())
            {
                filesProcessed++;
                StatusText = $"Reading Tonie files... ({filesProcessed}/{totalDirs} directories)";

                var tonieFiles = subDir.GetFiles("*0304E0");
                foreach (var file in tonieFiles)
                {
                    // Try to get metadata for this Tonie
                    string title = $"{subDir.Name}/{file.Name}";
                    string? imagePath = null;
                    bool isLive = false;

                    // Check if file has Hidden attribute (LIVE flag) using cross-platform method
                    StatusText = $"Checking LIVE flag for {file.Name}... ({filesProcessed}/{totalDirs})";
                    await Task.Delay(1); // Brief delay to ensure UI updates
                    isLive = GetHiddenAttribute(file.FullName);

                    try
                    {
                        var audio = TonieAudio.FromFile(file.FullName, false);
                        var hash = BitConverter.ToString(audio.Header.Hash).Replace("-", "");
                        var (metaTitle, metaImage) = _metadataService.GetTonieInfo(hash);

                        if (!string.IsNullOrEmpty(metaTitle) && metaTitle != "Unknown Tonie")
                        {
                            title = metaTitle;
                            imagePath = metaImage;

                            // If image is not cached, try to download it asynchronously
                            if (imagePath == null)
                            {
                                var picUrl = _metadataService.GetPicUrl(hash);
                                if (!string.IsNullOrEmpty(picUrl))
                                {
                                    // Download in background and update UI when done
                                    _ = DownloadImageForTonieAsync(hash, picUrl, file.FullName);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // If we can't read the file, just use the filename
                    }

                    // Add [LIVE] prefix if file has Hidden attribute
                    var displayTitle = isLive ? $"[LIVE] {title}" : title;

                    TonieFiles.Add(new TonieFileItem
                    {
                        FileName = file.Name,
                        FilePath = file.FullName,
                        DisplayName = displayTitle,
                        InfoText = $"Size: {file.Length / 1024} KB",
                        ImagePath = imagePath,
                        IsLive = isLive
                    });
                }
            }

            // Also scan for renamed files in current directory
            var renamedFiles = dirInfo.GetFiles().Where(f =>
            {
                var match = Regex.Match(f.Name, @"(?<prod>[0-9]{8}|[0-9]{2}-[0-9]{4}) - [0-9A-F]{8} - (?<name>.*)");
                return match.Success;
            });

            foreach (var file in renamedFiles)
            {
                TonieFiles.Add(new TonieFileItem
                {
                    FileName = file.Name,
                    FilePath = file.FullName,
                    DisplayName = file.Name,
                    InfoText = $"Size: {file.Length / 1024} KB"
                });
            }

            StatusText = $"Scan complete: Found {TonieFiles.Count} Tonie file(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Error scanning directory: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }

        await Task.CompletedTask;
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
}

public partial class TonieFileItem : ObservableObject
{
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _infoText = string.Empty;

    [ObservableProperty]
    private string? _imagePath;

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private bool _isSelected;
}