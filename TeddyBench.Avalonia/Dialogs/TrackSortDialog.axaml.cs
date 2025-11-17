using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System.Linq;

namespace TeddyBench.Avalonia.Dialogs;

public partial class TrackSortDialog : Window
{
    private TrackSortDialogViewModel? _viewModel;

    public TrackSortDialog()
    {
        InitializeComponent();
    }

    public TrackSortDialog(string[] audioPaths) : this()
    {
        _viewModel = new TrackSortDialogViewModel(this, audioPaths);
        DataContext = _viewModel;

        // Wire up selection changed event
        var trackListBox = this.FindControl<ListBox>("TrackListBox");
        if (trackListBox != null)
        {
            trackListBox.SelectionChanged += (s, e) =>
            {
                if (_viewModel != null)
                {
                    // Update the ViewModel's selected tracks collection
                    _viewModel.SelectedTracks.Clear();
                    foreach (var item in trackListBox.SelectedItems?.OfType<Models.AudioTrackItem>() ?? Enumerable.Empty<Models.AudioTrackItem>())
                    {
                        _viewModel.SelectedTracks.Add(item);
                    }
                    _viewModel.UpdateButtonStates();
                }
            };
        }

        // Handle ESC key at the Window level to ensure it works even when ListBox has focus
        KeyDown += TrackSortDialog_KeyDown;
    }

    private void TrackSortDialog_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnEncodeClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }

    public string[]? GetSortedFilePaths()
    {
        return _viewModel?.GetSortedFilePaths();
    }
}
