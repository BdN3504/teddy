using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TeddyBench.Avalonia.Dialogs;

public partial class RfidInputDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _rfidUid = string.Empty;

    public bool DialogResult { get; private set; }

    public RfidInputDialogViewModel(string rfidPrefix)
    {
        RfidUid = rfidPrefix;
    }

    partial void OnRfidUidChanged(string value)
    {
        // Automatically convert to uppercase
        if (!string.IsNullOrEmpty(value) && value != value.ToUpper())
        {
            RfidUid = value.ToUpper();
        }
    }

    [RelayCommand]
    private void Ok()
    {
        DialogResult = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
    }
}
