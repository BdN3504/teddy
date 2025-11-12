using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TeddyBench.Avalonia.Dialogs;

public partial class RfidInputDialog : Window
{
    public RfidInputDialog()
    {
        InitializeComponent();
    }

    public RfidInputDialog(string rfidPrefix) : this()
    {
        DataContext = new RfidInputDialogViewModel(rfidPrefix);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        // Focus and position cursor at end when dialog opens
        this.Opened += (s, e) =>
        {
            var rfidInput = this.FindControl<TextBox>("RfidInput");
            if (rfidInput != null)
            {
                rfidInput.CaretIndex = rfidInput.Text?.Length ?? 0;
                rfidInput.Focus();
            }
        };
    }

    private void OnOkClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }

    public string? GetRfidUid()
    {
        return (DataContext as RfidInputDialogViewModel)?.RfidUid;
    }
}
