using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Text.RegularExpressions;

namespace TeddyBench.Avalonia.Dialogs;

public partial class ConfirmModifyTonieDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _audioIdInput = string.Empty;

    [ObservableProperty]
    private string _audioIdError = string.Empty;

    [ObservableProperty]
    private bool _isAudioIdValid;

    public bool DialogResult { get; private set; }
    public uint? NewAudioId { get; private set; }

    public ConfirmModifyTonieDialogViewModel(string tonieName, uint currentAudioId)
    {
        Message = $"You are about to modify \"{tonieName}\", which is an official Tonie from the database.\n\n" +
                  $"Modifying this tonie will convert it to a custom tonie. It will still work with the same figurine, " +
                  $"but you will lose the original metadata and image icon.\n\n" +
                  $"To enable playback on the Toniebox hardware, the Audio ID must be changed to a custom tonie ID.";

        // Pre-fill with current timestamp using custom tonie calculation
        uint defaultAudioId = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 0x50000000;
        AudioIdInput = defaultAudioId.ToString("X8");
        ValidateAudioId();
    }

    partial void OnAudioIdInputChanged(string value)
    {
        ValidateAudioId();
    }

    private void ValidateAudioId()
    {
        AudioIdError = string.Empty;
        IsAudioIdValid = false;

        if (string.IsNullOrWhiteSpace(AudioIdInput))
        {
            AudioIdError = "Audio ID is required";
            return;
        }

        // Remove any spaces or 0x prefix
        string cleanInput = AudioIdInput.Trim().Replace(" ", "").ToUpper();
        if (cleanInput.StartsWith("0X"))
        {
            cleanInput = cleanInput.Substring(2);
        }

        // Validate hex format (8 characters)
        if (!Regex.IsMatch(cleanInput, "^[0-9A-F]{1,8}$"))
        {
            AudioIdError = "Audio ID must be a valid hexadecimal value (up to 8 characters)";
            return;
        }

        // Parse the value
        if (!uint.TryParse(cleanInput, System.Globalization.NumberStyles.HexNumber, null, out uint audioId))
        {
            AudioIdError = "Invalid hexadecimal value";
            return;
        }

        // Warn if the audio ID looks like an official tonie (< 0x50000000)
        if (audioId < 0x50000000)
        {
            AudioIdError = "Warning: This looks like an official tonie Audio ID. Custom tonies should use values in the custom range.";
            // Still allow it, but show warning
        }

        IsAudioIdValid = true;
        NewAudioId = audioId;
    }

    [RelayCommand]
    private void Yes()
    {
        if (!IsAudioIdValid)
        {
            AudioIdError = "Please enter a valid Audio ID before continuing";
            return;
        }

        DialogResult = true;
    }

    [RelayCommand]
    private void No()
    {
        DialogResult = false;
    }
}
