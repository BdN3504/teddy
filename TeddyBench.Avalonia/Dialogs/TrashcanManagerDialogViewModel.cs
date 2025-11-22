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

        IsLoading = true;
        StatusText = $"Restoring {SelectedTonie.DisplayName}...";

        try
        {
            var (success, message) = await _trashcanService.RestoreTonieAsync(SelectedTonie, _sdCardPath);

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
                StatusText = $"Failed to restore: {message}";
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