using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using TeddyBench.Avalonia.ViewModels;
using TeddyBench.Avalonia.Models;
using System.ComponentModel;

namespace TeddyBench.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly HashSet<Key> _pressedKeys = new HashSet<Key>();

    public MainWindow()
    {
        InitializeComponent();

        // Set up the ViewModel with a reference to this window for dialogs
        var viewModel = new MainWindowViewModel(this);
        DataContext = viewModel;

        // Hook into window closing event to save configuration
        Closing += MainWindow_Closing;

        // Hook into window opened event to auto-open directory picker
        Opened += MainWindow_Opened;

        // Hook into key up event for debouncing
        KeyUp += MainWindow_KeyUp;

        // Hook into key down event for keyboard shortcuts
        KeyDown += MainWindow_KeyDown;

        // Monitor IsAnyDialogOpen to clear pressed keys when dialogs close
        // This prevents stale key states if keys were released while dialog was open
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsAnyDialogOpen))
            {
                if (!viewModel.IsAnyDialogOpen && _pressedKeys.Count > 0)
                {
                    _pressedKeys.Clear();
                }
            }
        };
    }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        // Auto-open directory picker on startup
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.AutoOpenDirectoryOnStartup();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        // Save configuration before closing
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SaveConfigurationOnExit();
        }
    }

    private void Border_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Border border &&
            border.DataContext is TonieFileItem item &&
            DataContext is MainWindowViewModel viewModel)
        {
            // Get the ListBox
            var listBox = this.FindControl<ListBox>("TonieListBox");
            if (listBox == null) return;

            // Check how many items are currently selected
            int selectionCount = listBox.SelectedItems?.Count ?? 0;

            if (selectionCount <= 1)
            {
                // Single selection or no selection - use single-item context menu
                // The border already has the single-selection context menu attached
                // Just ensure the item is selected
                listBox.SelectedItem = item;
            }
            else
            {
                // Multiple items selected
                // Check if the right-clicked item is part of the selection
                bool isItemInSelection = listBox.SelectedItems?.Contains(item) ?? false;

                if (!isItemInSelection)
                {
                    // If right-clicked item is not in selection, select only this item
                    listBox.SelectedItem = item;
                }
                else
                {
                    // Right-clicked on a selected item - show multi-selection context menu
                    e.Handled = true;

                    // Capture the currently selected items before showing the menu
                    var selectedItemsCopy = listBox.SelectedItems?.Cast<TonieFileItem>().ToList();
                    if (selectedItemsCopy == null || selectedItemsCopy.Count == 0)
                    {
                        return;
                    }

                    var multiContextMenu = new ContextMenu
                    {
                        Background = Brushes.White
                    };

                    // Add style for black text
                    var style = new Style(x => x.OfType<MenuItem>());
                    style.Setters.Add(new Setter(MenuItem.ForegroundProperty, Brushes.Black));
                    multiContextMenu.Styles.Add(style);

                    var deleteMenuItem = new MenuItem
                    {
                        Header = $"Delete Selected ({selectionCount} items)"
                    };
                    deleteMenuItem.Click += async (s, args) =>
                    {
                        // Use the captured selection
                        viewModel.SelectedItems = new System.Collections.ArrayList(selectedItemsCopy);
                        await viewModel.DeleteMultipleTonieCommand.ExecuteAsync(null);
                    };

                    var removeLiveFlagMenuItem = new MenuItem
                    {
                        Header = $"Remove LIVE Flag ({selectionCount} items)"
                    };
                    removeLiveFlagMenuItem.Click += async (s, args) =>
                    {
                        // Use the captured selection
                        viewModel.SelectedItems = new System.Collections.ArrayList(selectedItemsCopy);
                        await viewModel.RemoveMultipleLiveFlagsCommand.ExecuteAsync(null);
                    };

                    multiContextMenu.Items.Add(removeLiveFlagMenuItem);
                    multiContextMenu.Items.Add(deleteMenuItem);

                    multiContextMenu.Open(border);
                }
            }
        }
    }

    private async void Border_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Border border &&
            border.DataContext is TonieFileItem item &&
            DataContext is MainWindowViewModel viewModel)
        {
            // Open the player on double-click
            await viewModel.PlayTonieCommand.ExecuteAsync(item);
        }
    }

    private void TonieListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.UpdateSelectionState();
        }
    }

    private void MainWindow_KeyUp(object? sender, KeyEventArgs e)
    {
        // Ignore KeyUp when dialog is open - prevents spurious releases when focus changes
        // This ensures held keys stay "pressed" until dialog closes AND user releases them
        if (DataContext is MainWindowViewModel viewModel && viewModel.IsAnyDialogOpen)
        {
            return;
        }

        // Key released - allow it to be processed again
        _pressedKeys.Remove(e.Key);
    }

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        // Key debouncing: ignore repeated KeyDown events from holding a key
        if (_pressedKeys.Contains(e.Key))
        {
            e.Handled = true;
            return;
        }

        // Mark this key as pressed
        _pressedKeys.Add(e.Key);

        // Disable main window shortcuts when any dialog is open
        if (viewModel.IsAnyDialogOpen)
        {
            return;
        }

        // Get the ListBox
        var listBox = this.FindControl<ListBox>("TonieListBox");
        if (listBox == null) return;

        // Check if exactly one item is selected
        int selectionCount = listBox.SelectedItems?.Count ?? 0;
        TonieFileItem? selectedItem = listBox.SelectedItem as TonieFileItem;

        // F2 key - Rename (only for custom tonies)
        if (e.Key == Key.F2 && selectionCount == 1 && selectedItem != null)
        {
            if (selectedItem.IsCustomTonie)
            {
                await viewModel.RenameSelectedTonieCommand.ExecuteAsync(selectedItem);
                e.Handled = true;
            }
        }
        // Space key - Open player
        else if (e.Key == Key.Space && selectionCount == 1 && selectedItem != null)
        {
            await viewModel.PlayTonieCommand.ExecuteAsync(selectedItem);
            e.Handled = true;
        }
        // Enter key - Modify contents
        else if (e.Key == Key.Enter && selectionCount == 1 && selectedItem != null)
        {
            await viewModel.ModifyContentsCommand.ExecuteAsync(selectedItem);
            e.Handled = true;
        }
        // Delete key - Delete tonie
        else if (e.Key == Key.Delete && selectionCount >= 1)
        {
            if (selectionCount == 1 && selectedItem != null)
            {
                // Single item deletion
                await viewModel.DeleteSelectedTonieCommand.ExecuteAsync(selectedItem);
                e.Handled = true;
            }
            else if (selectionCount > 1)
            {
                // Multiple item deletion
                await viewModel.DeleteMultipleTonieCommand.ExecuteAsync(null);
                e.Handled = true;
            }
        }
    }
}