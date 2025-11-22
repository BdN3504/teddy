using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using TeddyBench.Avalonia.Models;
using TeddyBench.Avalonia.Services;

namespace TeddyBench.Avalonia.Dialogs;

public partial class TrashcanManagerDialogViewModel : ObservableObject
{
    private readonly TrashcanService _trashcanService;
    private readonly TonieMetadataService _metadataService;
    private readonly string _sdCardPath;
    private readonly Window _window;

    [ObservableProperty]
    private ObservableCollection<DeletedTonieItem> _deletedTonies = new();

    [ObservableProperty]
    private DeletedTonieItem? _selectedTonie;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasDeletedTonies;

    public bool DialogResult { get; private set; }

    public TrashcanManagerDialogViewModel(Window window, string sdCardPath, TonieMetadataService metadataService)
    {
        _window = window;
        _sdCardPath = sdCardPath;
        _metadataService = metadataService;
        _trashcanService = new TrashcanService(metadataService);

        // Load deleted tonies
        _ = LoadDeletedToniesAsync();
    }

    private async Task LoadDeletedToniesAsync()
    {
        IsLoading = true;
        StatusText = "Scanning TRASHCAN directory...";

        try
        {
            var deletedTonies = await _trashcanService.ScanTrashcanAsync(_sdCardPath);

            DeletedTonies.Clear();
            foreach (var tonie in deletedTonies.OrderByDescending(t => t.DeletionDate))
            {
                DeletedTonies.Add(tonie);
            }

            HasDeletedTonies = DeletedTonies.Count > 0;
            StatusText = $"Found {DeletedTonies.Count} deleted Tonie(s) in TRASHCAN";
        }
        catch (Exception ex)
        {
            StatusText = $"Error scanning TRASHCAN: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RestoreSelectedAsync()
    {
        if (SelectedTonie == null)
        {
            return;
        }

        // Check if UID is unknown - if so, ask user to provide it
        if (SelectedTonie.Uid == "Unknown")
        {
            StatusText = "UID unknown - please enter the RFID tag UID...";

            // Get CONTENT directory path for validation
            var contentPath = System.IO.Path.Combine(_sdCardPath, "CONTENT");

            // Show RFID input dialog with empty prefix (user will enter full 8 characters)
            var rfidDialog = new RfidInputDialog("", contentPath);
            var result = await rfidDialog.ShowDialog<bool?>(_window);

            if (result != true)
            {
                StatusText = "Restore cancelled";
                return;
            }

            // Get the entered UID
            var enteredUid = rfidDialog.GetRfidUid();
            if (string.IsNullOrEmpty(enteredUid))
            {
                StatusText = "Restore cancelled - no UID entered";
                return;
            }

            // Update the UID in the selected tonie
            SelectedTonie.Uid = enteredUid;
            StatusText = $"Using RFID UID: {enteredUid}";
        }

        // Attempt restore - will handle conflicts
        await AttemptRestoreAsync(false);
    }

    private async Task AttemptRestoreAsync(bool allowOverwrite)
    {
        if (SelectedTonie == null)
        {
            return;
        }

        IsLoading = true;
        StatusText = $"Restoring {SelectedTonie.DisplayName}...";

        try
        {
            var (success, message) = await _trashcanService.RestoreTonieAsync(SelectedTonie, _sdCardPath, allowOverwrite);

            if (success)
            {
                StatusText = message;

                // Remove from list
                DeletedTonies.Remove(SelectedTonie);
                HasDeletedTonies = DeletedTonies.Count > 0;

                // Set dialog result to indicate changes were made
                DialogResult = true;
            }
            else
            {
                // Check if it's a conflict
                if (message.StartsWith("CONFLICT:"))
                {
                    var existingUid = message.Substring("CONFLICT:".Length);
                    await HandleConflictAsync(existingUid);
                }
                else
                {
                    StatusText = $"Failed to restore: {message}";
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error restoring: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task HandleConflictAsync(string existingUid)
    {
        if (SelectedTonie == null)
        {
            return;
        }

        // Show conflict resolution dialog
        var conflictDialog = new ConflictResolutionDialog(SelectedTonie.DisplayName, existingUid);
        var result = await conflictDialog.ShowDialog<ConflictResolutionResult>(_window);

        switch (result)
        {
            case ConflictResolutionResult.Overwrite:
                // User chose to overwrite - retry with overwrite flag
                StatusText = $"Overwriting existing Tonie at {existingUid}/500304E0...";
                await AttemptRestoreAsync(true);
                break;

            case ConflictResolutionResult.NewUid:
                // User chose to enter a new UID
                StatusText = "Please enter a different RFID UID...";

                // Get CONTENT directory path for validation
                var contentPath = System.IO.Path.Combine(_sdCardPath, "CONTENT");

                // Show RFID input dialog with empty prefix (user will enter full 8 characters)
                var rfidDialog = new RfidInputDialog("", contentPath);
                var rfidResult = await rfidDialog.ShowDialog<bool?>(_window);

                if (rfidResult == true)
                {
                    // Get the entered UID
                    var enteredUid = rfidDialog.GetRfidUid();
                    if (!string.IsNullOrEmpty(enteredUid))
                    {
                        // Update the UID in the selected tonie
                        SelectedTonie.Uid = enteredUid;
                        StatusText = $"Using new RFID UID: {enteredUid}";

                        // Retry restore with new UID
                        await AttemptRestoreAsync(false);
                    }
                    else
                    {
                        StatusText = "Restore cancelled - no UID entered";
                    }
                }
                else
                {
                    StatusText = "Restore cancelled";
                }
                break;

            case ConflictResolutionResult.Cancel:
            default:
                StatusText = "Restore cancelled";
                break;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedTonie == null)
        {
            return;
        }

        // Confirm deletion
        var confirmDialog = new ConfirmDeleteDialogViewModel(SelectedTonie.DisplayName);
        var confirmWindow = new ConfirmDeleteDialog
        {
            DataContext = confirmDialog
        };

        await confirmWindow.ShowDialog(_window);

        if (!confirmDialog.DialogResult)
        {
            return;
        }

        IsLoading = true;
        StatusText = $"Permanently deleting {SelectedTonie.DisplayName}...";

        try
        {
            var (success, message) = await _trashcanService.PermanentlyDeleteAsync(SelectedTonie);

            if (success)
            {
                StatusText = message;

                // Remove from list
                DeletedTonies.Remove(SelectedTonie);
                HasDeletedTonies = DeletedTonies.Count > 0;
            }
            else
            {
                StatusText = $"Failed to delete: {message}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error deleting: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDeletedToniesAsync();
    }

    [RelayCommand]
    private void Close()
    {
        _window.Close();
    }
}