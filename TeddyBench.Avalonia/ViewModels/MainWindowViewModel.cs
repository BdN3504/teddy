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
            string rfidPrefix = "0EED"; // Default value (ED0E reversed)
            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (File.Exists(configPath))
                {
                    var configJson = File.ReadAllText(configPath);
                    var config = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(configJson);
                    if (config != null && config.ContainsKey("RfidPrefix"))
                    {
                        rfidPrefix = config["RfidPrefix"];
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not load RFID prefix from config, using default '0EED': {ex.Message}");
            }

            // Step 1: Get RFID UID from user (8 characters, pre-filled with prefix but editable)
            var audioIdDialog = new Window
            {
                Title = "Enter RFID Tag UID",
                Width = 550,
                Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            // Pre-fill the text box with the prefix, cursor positioned at the end
            var audioIdInput = new TextBox
            {
                Text = rfidPrefix,
                Watermark = "e.g., 0EED5104",
                Margin = new global::Avalonia.Thickness(10)
            };

            // Automatically convert input to uppercase as user types
            audioIdInput.TextChanged += (s, e) =>
            {
                if (audioIdInput.Text != null)
                {
                    var currentPos = audioIdInput.CaretIndex;
                    var upperText = audioIdInput.Text.ToUpper();
                    if (audioIdInput.Text != upperText)
                    {
                        audioIdInput.Text = upperText;
                        audioIdInput.CaretIndex = currentPos;
                    }
                }
            };

            // Set cursor to end of text when dialog opens
            audioIdInput.AttachedToVisualTree += (s, e) =>
            {
                audioIdInput.CaretIndex = audioIdInput.Text?.Length ?? 0;
                audioIdInput.Focus();
            };

            var okButton = new Button { Content = "OK", Width = 80, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center };
            var cancelButton = new Button { Content = "Cancel", Width = 80, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center, Margin = new global::Avalonia.Thickness(10, 0, 0, 0) };

            bool? dialogResult = null;
            okButton.Click += (s, e) => { dialogResult = true; audioIdDialog.Close(); };
            cancelButton.Click += (s, e) => { dialogResult = false; audioIdDialog.Close(); };

            var buttonPanel = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center, Margin = new global::Avalonia.Thickness(10) };
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            var mainPanel = new StackPanel { Margin = new global::Avalonia.Thickness(10) };
            mainPanel.Children.Add(new TextBlock
            {
                Text = "Enter the RFID UID from your Tonie figurine:",
                Margin = new global::Avalonia.Thickness(0, 0, 0, 5),
                FontWeight = global::Avalonia.Media.FontWeight.Bold
            });
            mainPanel.Children.Add(new TextBlock
            {
                Text = $"• The prefix '{rfidPrefix}' (in reverse byte order) is pre-filled\n• Complete the UID by adding the last 4 characters (e.g., 5104)\n• You can edit the entire 8-character string if needed\n• You can read the UID from the RFID tag using an NFC reader\n• The UID must match the physical figurine for the Toniebox to recognize it",
                Margin = new global::Avalonia.Thickness(0, 0, 0, 10),
                TextWrapping = global::Avalonia.Media.TextWrapping.Wrap
            });
            mainPanel.Children.Add(audioIdInput);
            mainPanel.Children.Add(buttonPanel);

            audioIdDialog.Content = mainPanel;
            await audioIdDialog.ShowDialog(_window);

            if (dialogResult != true)
            {
                StatusText = "Operation cancelled";
                return;
            }

            // Parse RFID UID
            string uidInput = audioIdInput.Text?.Replace(" ", "").Replace(":", "").ToUpper() ?? "";

            if (string.IsNullOrWhiteSpace(uidInput))
            {
                StatusText = "Error: RFID UID is required (must match RFID tag on figurine)";
                return;
            }

            if (uidInput.Length != 8 || !System.Text.RegularExpressions.Regex.IsMatch(uidInput, "^[0-9A-F]{8}$"))
            {
                StatusText = "Error: RFID UID must be exactly 8 hexadecimal characters";
                return;
            }

            // User entered the full 8-character UID (prefix already included)
            // Example: uidInput="0EED5104" (user added "5104" to pre-filled "0EED")
            // Reverse bytes: "0EED5104" -> "04 51 ED 0E" -> "0451ED0E"
            string fullUid = uidInput; // "0EED5104"

            // Reverse byte order (every 2 characters)
            string reversedUid = "";
            for (int i = fullUid.Length - 2; i >= 0; i -= 2)
            {
                reversedUid += fullUid.Substring(i, 2);
            }
            // reversedUid is now "0451ED0E"

            // Parse as audio ID for TonieAudio constructor (last 4 bytes = last 8 hex chars of full RFID)
            uint audioId;
            string fullRfidWithSuffix = reversedUid + "500304E0";
            if (!uint.TryParse(fullRfidWithSuffix.Substring(8, 8), System.Globalization.NumberStyles.HexNumber, null, out audioId))
            {
                StatusText = "Error: Invalid RFID UID format";
                return;
            }

            // Step 2: Select audio files
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

            // Step 3: Show track sorting dialog
            var trackList = new ObservableCollection<AudioTrackItem>();

            // Sort by filename initially
            var sortedPaths = audioPaths.OrderBy(p => Path.GetFileName(p)).ToArray();
            for (int i = 0; i < sortedPaths.Length; i++)
            {
                trackList.Add(new AudioTrackItem
                {
                    TrackNumber = i + 1,
                    FileName = Path.GetFileName(sortedPaths[i]),
                    FilePath = sortedPaths[i]
                });
            }

            var sortDialog = new Window
            {
                Title = "Sort Your Tracks",
                Width = 700,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = true
            };

            var trackListBox = new ListBox
            {
                ItemsSource = trackList,
                SelectionMode = SelectionMode.Multiple,
                Margin = new global::Avalonia.Thickness(10)
            };

            // Create template for ListBox items - show track number and filename
            var itemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<AudioTrackItem>((track, _) =>
            {
                // Null check to prevent crashes during ObservableCollection updates
                if (track == null)
                    return new TextBlock { Text = "" };

                var panel = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal };
                panel.Children.Add(new TextBlock
                {
                    Text = $"{track.TrackNumber}. ",
                    Width = 40,
                    FontWeight = global::Avalonia.Media.FontWeight.Bold
                });
                panel.Children.Add(new TextBlock
                {
                    Text = track.FileName,
                    TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis
                });
                return panel;
            });
            trackListBox.ItemTemplate = itemTemplate;

            bool canMoveUp = false;
            bool canMoveDown = false;

            var btnMoveUp = new Button
            {
                Content = "Move Up",
                Width = 100,
                Margin = new global::Avalonia.Thickness(5),
                IsEnabled = false
            };

            var btnMoveDown = new Button
            {
                Content = "Move Down",
                Width = 100,
                Margin = new global::Avalonia.Thickness(5),
                IsEnabled = false
            };

            // Update button states when selection changes
            trackListBox.SelectionChanged += (s, e) =>
            {
                var selectedIndices = trackListBox.SelectedItems.Cast<AudioTrackItem>()
                    .Select(t => trackList.IndexOf(t))
                    .OrderBy(i => i)
                    .ToList();

                canMoveUp = selectedIndices.Count > 0 && selectedIndices[0] > 0;
                canMoveDown = selectedIndices.Count > 0 && selectedIndices[selectedIndices.Count - 1] < trackList.Count - 1;

                btnMoveUp.IsEnabled = canMoveUp;
                btnMoveDown.IsEnabled = canMoveDown;
            };

            // Move Up button click handler
            btnMoveUp.Click += (s, e) =>
            {
                var selectedIndices = trackListBox.SelectedItems.Cast<AudioTrackItem>()
                    .Select(t => trackList.IndexOf(t))
                    .OrderBy(i => i)
                    .ToList();

                if (selectedIndices.Count == 0 || selectedIndices[0] == 0)
                    return;

                var selectedItems = selectedIndices.Select(i => trackList[i]).ToList();

                for (int i = 0; i < selectedIndices.Count; i++)
                {
                    int index = selectedIndices[i];
                    if (index > 0)
                    {
                        var item = trackList[index];
                        trackList.RemoveAt(index);
                        trackList.Insert(index - 1, item);
                    }
                }

                // Update track numbers
                for (int i = 0; i < trackList.Count; i++)
                {
                    trackList[i].TrackNumber = i + 1;
                }

                // Restore selection
                trackListBox.SelectedItems.Clear();
                foreach (var item in selectedItems)
                {
                    trackListBox.SelectedItems.Add(item);
                }
            };

            // Move Down button click handler
            btnMoveDown.Click += (s, e) =>
            {
                var selectedIndices = trackListBox.SelectedItems.Cast<AudioTrackItem>()
                    .Select(t => trackList.IndexOf(t))
                    .OrderByDescending(i => i)
                    .ToList();

                if (selectedIndices.Count == 0 || selectedIndices[0] == trackList.Count - 1)
                    return;

                var selectedItems = selectedIndices.Select(i => trackList[i]).ToList();

                for (int i = 0; i < selectedIndices.Count; i++)
                {
                    int index = selectedIndices[i];
                    if (index < trackList.Count - 1)
                    {
                        var item = trackList[index];
                        trackList.RemoveAt(index);
                        trackList.Insert(index + 1, item);
                    }
                }

                // Update track numbers
                for (int i = 0; i < trackList.Count; i++)
                {
                    trackList[i].TrackNumber = i + 1;
                }

                // Restore selection
                trackListBox.SelectedItems.Clear();
                foreach (var item in selectedItems)
                {
                    trackListBox.SelectedItems.Add(item);
                }
            };

            var btnOk = new Button
            {
                Content = "Encode",
                Width = 100,
                Margin = new global::Avalonia.Thickness(5),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right
            };

            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 100,
                Margin = new global::Avalonia.Thickness(5),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right
            };

            bool? sortDialogResult = null;
            btnOk.Click += (s, e) => { sortDialogResult = true; sortDialog.Close(); };
            btnCancel.Click += (s, e) => { sortDialogResult = false; sortDialog.Close(); };

            // Layout
            var moveButtonPanel = new StackPanel
            {
                Orientation = global::Avalonia.Layout.Orientation.Vertical,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
                Margin = new global::Avalonia.Thickness(10)
            };
            moveButtonPanel.Children.Add(btnMoveUp);
            moveButtonPanel.Children.Add(btnMoveDown);

            var bottomPanel = new StackPanel
            {
                Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
                Margin = new global::Avalonia.Thickness(10)
            };
            bottomPanel.Children.Add(btnOk);
            bottomPanel.Children.Add(btnCancel);

            var contentGrid = new Grid
            {
                RowDefinitions = new global::Avalonia.Controls.RowDefinitions("Auto,*,Auto")
            };

            var headerText = new TextBlock
            {
                Text = "Reorder tracks using the Up/Down buttons. Multi-select with Ctrl or Shift.",
                Margin = new global::Avalonia.Thickness(10),
                FontWeight = global::Avalonia.Media.FontWeight.Bold
            };
            Grid.SetRow(headerText, 0);

            var middleGrid = new Grid
            {
                ColumnDefinitions = new global::Avalonia.Controls.ColumnDefinitions("*,Auto")
            };
            Grid.SetRow(middleGrid, 1);
            Grid.SetColumn(trackListBox, 0);
            Grid.SetColumn(moveButtonPanel, 1);
            middleGrid.Children.Add(trackListBox);
            middleGrid.Children.Add(moveButtonPanel);

            Grid.SetRow(bottomPanel, 2);

            contentGrid.Children.Add(headerText);
            contentGrid.Children.Add(middleGrid);
            contentGrid.Children.Add(bottomPanel);

            sortDialog.Content = contentGrid;
            await sortDialog.ShowDialog(_window);

            if (sortDialogResult != true)
            {
                StatusText = "Operation cancelled";
                return;
            }

            // Get the sorted file paths
            string[] sortedAudioPaths = trackList.Select(t => t.FilePath).ToArray();

            StatusText = $"Encoding {sortedAudioPaths.Length} file(s)...";
            IsScanning = true;

            await Task.Run(() =>
            {
                // Step 4: Encode using TonieAudio with sorted paths
                int bitRate = 96; // Default bitrate
                bool useVbr = false;

                TonieAudio generated = new TonieAudio(sortedAudioPaths, audioId, bitRate * 1000, useVbr, null);

                // Step 5: Create directory structure and save
                // User input: 5104 + prefix 0EED = 0EED5104 -> reversed: 0451ED0E
                // Directory: 0451ED0E, File: 500304E0 (constant suffix)
                string dirName = reversedUid;
                string fileName = "500304E0";

                string targetDir = Path.Combine(CurrentDirectory, dirName);
                Directory.CreateDirectory(targetDir);

                string targetFile = Path.Combine(targetDir, fileName);
                File.WriteAllBytes(targetFile, generated.FileContent);

                StatusText = $"Successfully created custom Tonie: {dirName}/{fileName}";
            });

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
                    bool isKnownTonie = false;

                    try
                    {
                        var audio = TonieAudio.FromFile(file.FullName, false);
                        var hash = BitConverter.ToString(audio.Header.Hash).Replace("-", "");
                        // Pass the RFID folder name (subDir.Name) so custom tonies show the RFID instead of hash
                        var (metaTitle, metaImage) = _metadataService.GetTonieInfo(hash, subDir.Name);

                        if (!string.IsNullOrEmpty(metaTitle) && metaTitle != "Unknown Tonie")
                        {
                            title = metaTitle;
                            imagePath = metaImage;
                            isKnownTonie = true; // This is an official Tonie from the database

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

                    // Only check LIVE flag for custom/unknown tonies (not in database)
                    // Official tonies from the database never have the LIVE flag
                    if (!isKnownTonie)
                    {
                        StatusText = $"Checking LIVE flag for {file.Name}... ({filesProcessed}/{totalDirs})";
                        await Task.Delay(1); // Brief delay to ensure UI updates
                        isLive = GetHiddenAttribute(file.FullName);
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

public partial class AudioTrackItem : ObservableObject
{
    [ObservableProperty]
    private int _trackNumber;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}