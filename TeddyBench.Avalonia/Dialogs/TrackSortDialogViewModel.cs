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
using TeddyBench.Avalonia.Models;

namespace TeddyBench.Avalonia.Dialogs;

public partial class TrackSortDialogViewModel : ObservableObject
{
    private readonly Window _parentWindow;

    [ObservableProperty]
    private ObservableCollection<AudioTrackItem> _tracks = new();

    [ObservableProperty]
    private ObservableCollection<AudioTrackItem> _selectedTracks = new();

    [ObservableProperty]
    private bool _canMoveUp = false;

    [ObservableProperty]
    private bool _canMoveDown = false;

    [ObservableProperty]
    private bool _canRemove = false;

    public bool DialogResult { get; private set; }

    public TrackSortDialogViewModel(Window parentWindow, string[] audioPaths)
    {
        _parentWindow = parentWindow;

        // Sort by folder grouping - files from the same folder stay together
        var sortedPaths = SortByFolderThenFilename(audioPaths);
        for (int i = 0; i < sortedPaths.Length; i++)
        {
            Tracks.Add(new AudioTrackItem
            {
                TrackNumber = i + 1,
                FileName = Path.GetFileName(sortedPaths[i]),
                FilePath = sortedPaths[i]
            });
        }
    }

    partial void OnSelectedTracksChanged(ObservableCollection<AudioTrackItem> value)
    {
        UpdateButtonStates();
    }

    public void UpdateButtonStates()
    {
        var selectedIndices = SelectedTracks
            .Select(t => Tracks.IndexOf(t))
            .OrderBy(i => i)
            .ToList();

        CanMoveUp = selectedIndices.Count > 0 && selectedIndices[0] > 0;
        CanMoveDown = selectedIndices.Count > 0 && selectedIndices[selectedIndices.Count - 1] < Tracks.Count - 1;
        CanRemove = selectedIndices.Count > 0;
    }

    [RelayCommand]
    private void MoveUp()
    {
        var selectedIndices = SelectedTracks
            .Select(t => Tracks.IndexOf(t))
            .OrderBy(i => i)
            .ToList();

        if (selectedIndices.Count == 0 || selectedIndices[0] == 0)
            return;

        var selectedItems = selectedIndices.Select(i => Tracks[i]).ToList();

        for (int i = 0; i < selectedIndices.Count; i++)
        {
            int index = selectedIndices[i];
            if (index > 0)
            {
                var item = Tracks[index];
                Tracks.RemoveAt(index);
                Tracks.Insert(index - 1, item);
            }
        }

        // Update track numbers
        for (int i = 0; i < Tracks.Count; i++)
        {
            Tracks[i].TrackNumber = i + 1;
        }

        // Restore selection
        SelectedTracks.Clear();
        foreach (var item in selectedItems)
        {
            SelectedTracks.Add(item);
        }

        UpdateButtonStates();
    }

    [RelayCommand]
    private void MoveDown()
    {
        var selectedIndices = SelectedTracks
            .Select(t => Tracks.IndexOf(t))
            .OrderByDescending(i => i)
            .ToList();

        if (selectedIndices.Count == 0 || selectedIndices[0] == Tracks.Count - 1)
            return;

        var selectedItems = selectedIndices.Select(i => Tracks[i]).ToList();

        for (int i = 0; i < selectedIndices.Count; i++)
        {
            int index = selectedIndices[i];
            if (index < Tracks.Count - 1)
            {
                var item = Tracks[index];
                Tracks.RemoveAt(index);
                Tracks.Insert(index + 1, item);
            }
        }

        // Update track numbers
        for (int i = 0; i < Tracks.Count; i++)
        {
            Tracks[i].TrackNumber = i + 1;
        }

        // Restore selection
        SelectedTracks.Clear();
        foreach (var item in selectedItems)
        {
            SelectedTracks.Add(item);
        }

        UpdateButtonStates();
    }

    [RelayCommand]
    private async Task AddFiles()
    {
        try
        {
            var storageProvider = _parentWindow.StorageProvider;
            var filePickerOptions = new FilePickerOpenOptions
            {
                Title = "Add Audio Files",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Audio Files") { Patterns = new[] { "*.mp3", "*.ogg", "*.flac", "*.wav", "*.m4a", "*.aac", "*.wma" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            };

            var selectedFiles = await storageProvider.OpenFilePickerAsync(filePickerOptions);

            if (selectedFiles.Count > 0)
            {
                // Fix file paths - ensure they start with '/' on Linux
                var newAudioPaths = selectedFiles
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

                // Sort new files by folder grouping before adding
                var sortedNewPaths = SortByFolderThenFilename(newAudioPaths);

                // Add sorted files to the end of the list
                foreach (var path in sortedNewPaths)
                {
                    Tracks.Add(new AudioTrackItem
                    {
                        TrackNumber = Tracks.Count + 1,
                        FileName = Path.GetFileName(path),
                        FilePath = path
                    });
                }

                // Update track numbers
                for (int i = 0; i < Tracks.Count; i++)
                {
                    Tracks[i].TrackNumber = i + 1;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error adding files: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Remove()
    {
        if (SelectedTracks.Count == 0)
            return;

        // Prevent removing all tracks - need at least one
        if (SelectedTracks.Count >= Tracks.Count)
        {
            Console.WriteLine("Cannot remove all tracks - at least one track is required");
            return;
        }

        var itemsToRemove = SelectedTracks.ToList();

        // Remove selected items
        foreach (var item in itemsToRemove)
        {
            Tracks.Remove(item);
        }

        // Update track numbers
        for (int i = 0; i < Tracks.Count; i++)
        {
            Tracks[i].TrackNumber = i + 1;
        }

        // Clear selection
        SelectedTracks.Clear();
        UpdateButtonStates();
    }

    [RelayCommand]
    private void Encode()
    {
        DialogResult = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
    }

    public string[] GetSortedFilePaths()
    {
        return Tracks.Select(t => t.FilePath).ToArray();
    }

    /// <summary>
    /// Sorts files by folder first (maintaining folder order), then alphabetically by filename within each folder.
    /// This ensures files from the same folder stay grouped together.
    /// </summary>
    private string[] SortByFolderThenFilename(string[] paths)
    {
        // Group files by their parent directory
        var groupedByFolder = paths
            .GroupBy(p => Path.GetDirectoryName(p) ?? "")
            .Select((group, index) => new
            {
                FolderPath = group.Key,
                OriginalOrder = index, // Preserve the order folders appear in the input
                Files = group.OrderBy(p => Path.GetFileName(p)).ToList() // Sort files within folder
            })
            .OrderBy(g => g.OriginalOrder) // Maintain folder order
            .SelectMany(g => g.Files) // Flatten back to a single list
            .ToArray();

        return groupedByFolder;
    }
}
