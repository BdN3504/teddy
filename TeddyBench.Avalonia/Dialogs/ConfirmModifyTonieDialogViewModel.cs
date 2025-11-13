using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TeddyBench.Avalonia.Dialogs;

public partial class ConfirmModifyTonieDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _message = string.Empty;

    public bool DialogResult { get; private set; }

    public ConfirmModifyTonieDialogViewModel(string tonieName)
    {
        Message = $"You are about to modify \"{tonieName}\", which is an official Tonie from the database.\n\n" +
                  $"Modifying this tonie will convert it to a custom tonie. It will still work with the same figurine, " +
                  $"but you will lose the original metadata and image icon.";
    }

    [RelayCommand]
    private void Yes()
    {
        DialogResult = true;
    }

    [RelayCommand]
    private void No()
    {
        DialogResult = false;
    }
}
