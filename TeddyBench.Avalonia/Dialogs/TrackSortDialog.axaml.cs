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

        // Handle keyboard shortcuts at the Window level
        AddHandler(KeyDownEvent, TrackSortDialog_KeyDown, handledEventsToo: true);

        // Set focus to Encode button when dialog opens so mnemonics work immediately
        Opened += (s, e) =>
        {
            var encodeButton = this.FindControl<Button>("EncodeButton");
            if (encodeButton != null)
            {
                encodeButton.Focus();
            }
        };
    }

    private void TrackSortDialog_KeyDown(object? sender, KeyEventArgs e)
    {
        // ESC to close (this is the only non-mnemonic shortcut)
        if (e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
        }
        // Note: All Alt+key shortcuts are handled by button mnemonics now
        // (Alt+U, Alt+D, Alt+A, Alt+R, Alt+E, Alt+C work automatically)
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
