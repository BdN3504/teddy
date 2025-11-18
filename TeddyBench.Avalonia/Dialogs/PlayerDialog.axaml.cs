using Avalonia.Controls;
using Avalonia.Input;
using TeddyBench.Avalonia.ViewModels;
using System.ComponentModel;

namespace TeddyBench.Avalonia.Dialogs;

public partial class PlayerDialog : Window
{
    public PlayerDialog()
    {
        InitializeComponent();

        // Add keyboard shortcut handler back
        AddHandler(KeyDownEvent, PlayerDialog_KeyDown, handledEventsToo: true);

        // Set focus to Close button when dialog opens so mnemonics work immediately
        Opened += (s, e) =>
        {
            var closeButton = this.FindControl<Button>("CloseButton");
            if (closeButton != null)
            {
                closeButton.Focus();
                System.Diagnostics.Debug.WriteLine("PlayerDialog opened, focused Close button");
            }
        };

        // Stop playback when window is closing
        Closing += (s, e) =>
        {
            if (DataContext is PlayerDialogViewModel vm)
            {
                vm.Cleanup();
            }
        };

        // Handle property changes to scroll to current track
        DataContextChanged += (s, e) =>
        {
            if (DataContext is PlayerDialogViewModel vm)
            {
                vm.PropertyChanged += ViewModel_PropertyChanged;
            }
        };
    }

    private void PlayerDialog_KeyDown(object? sender, KeyEventArgs e)
    {
        // Note: Alt+C for Close is handled by button mnemonic now
        // No manual keyboard shortcuts needed - mnemonics handle everything
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerDialogViewModel.CurrentTrack))
        {
            if (DataContext is PlayerDialogViewModel vm && vm.CurrentTrack != null)
            {
                // Find the ListBox by name and scroll to the current track
                var listBox = this.FindControl<ListBox>("TrackListBox");
                if (listBox != null)
                {
                    listBox.ScrollIntoView(vm.CurrentTrack);
                }
            }
        }
    }
}
