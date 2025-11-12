using Avalonia.Controls;
using Avalonia.Interactivity;
using TeddyBench.Avalonia.ViewModels;
using System.ComponentModel;

namespace TeddyBench.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Set up the ViewModel with a reference to this window for dialogs
        var viewModel = new MainWindowViewModel(this);
        DataContext = viewModel;

        // Hook into window closing event to save configuration
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        // Save configuration before closing
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SaveConfigurationOnExit();
        }
    }

    private void ContextMenu_Opened(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is Button button &&
            button.Tag is TonieFileItem item &&
            DataContext is MainWindowViewModel viewModel)
        {
            // Select the item when context menu opens
            viewModel.SelectedFile = item;
        }
    }
}