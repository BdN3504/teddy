using Avalonia.Controls;
using Avalonia.Interactivity;
using TeddyBench.Avalonia.ViewModels;
using TeddyBench.Avalonia.Models;
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

    private void Button_ContextRequested(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.Tag is TonieFileItem item &&
            DataContext is MainWindowViewModel viewModel)
        {
            // Select the item before context menu opens
            // Deselect all items first
            foreach (var tonieFile in viewModel.TonieFiles)
            {
                tonieFile.IsSelected = false;
            }

            // Select the right-clicked item
            item.IsSelected = true;
            viewModel.SelectedFile = item;
        }
    }
}