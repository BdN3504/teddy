using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TeddyBench.Avalonia.Dialogs;

public partial class RfidInputDialog : Window
{
    private TextBox? _rfidInput;

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
            _rfidInput = this.FindControl<TextBox>("RfidInput");
            if (_rfidInput != null)
            {
                _rfidInput.CaretIndex = _rfidInput.Text?.Length ?? 0;
                _rfidInput.Focus();

                // Handle text input to convert to uppercase
                _rfidInput.PropertyChanged += OnRfidInputPropertyChanged;
            }
        };
    }

    private void OnRfidInputPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == nameof(TextBox.Text) && _rfidInput != null)
        {
            var text = _rfidInput.Text;
            if (!string.IsNullOrEmpty(text))
            {
                var upperText = text.ToUpper();
                if (text != upperText)
                {
                    var caretIndex = _rfidInput.CaretIndex;
                    _rfidInput.Text = upperText;
                    _rfidInput.CaretIndex = caretIndex;

                    // Update the ViewModel
                    if (DataContext is RfidInputDialogViewModel vm)
                    {
                        vm.RfidUid = upperText;
                    }
                }
            }
        }
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
