using Avalonia.Controls;
using TeddyBench.Avalonia.ViewModels;

namespace TeddyBench.Avalonia.Dialogs;

public partial class PlayerDialog : Window
{
    public PlayerDialog()
    {
        InitializeComponent();

        // Stop playback when window is closing
        Closing += (s, e) =>
        {
            if (DataContext is PlayerDialogViewModel vm)
            {
                vm.Cleanup();
            }
        };
    }
}
