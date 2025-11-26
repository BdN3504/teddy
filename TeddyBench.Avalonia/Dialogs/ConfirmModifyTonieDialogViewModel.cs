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

    [ObservableProperty]
    private bool _isCustomTonie = false;

    private readonly uint _originalAudioId;
    private uint _lastGeneratedTimestamp;

    public bool DialogResult { get; private set; }
    public uint? NewAudioId { get; private set; }

    public ConfirmModifyTonieDialogViewModel(string tonieName, uint currentAudioId)
    {
        Message = $"You are about to modify \"{tonieName}\", which is an official Tonie from the database.\n\n" +
                  $"Modifying this tonie will convert it to a custom tonie. It will still work with the same figurine, " +
                  $"but you will lose the original metadata and image icon.\n\n" +
                  $"To enable playback on the Toniebox hardware, the Audio ID must be changed to a custom tonie ID.";

        // Store the original Audio ID
        _originalAudioId = currentAudioId;

        // Pre-fill with the current Audio ID (convert to timestamp by adding the offset back)
        // If currentAudioId is already in the custom range, it needs the offset added back
        // If it's in the official range (>= 0x50000000), use it as-is
        uint displayTimestamp = currentAudioId >= 0x50000000 ? currentAudioId : currentAudioId + 0x50000000;
        AudioIdInput = displayTimestamp.ToString();
        _lastGeneratedTimestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ValidateAudioId();
    }

    partial void OnAudioIdInputChanged(string value)
    {
        ValidateAudioId();
    }

    partial void OnIsCustomTonieChanged(bool value)
    {
        // When toggle changes, update the input field
        if (value)
        {
            // Switching to Custom Tonie mode: use current timestamp
            _lastGeneratedTimestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            AudioIdInput = _lastGeneratedTimestamp.ToString();
        }
        else
        {
            // Switching back to original mode: restore original Audio ID
            uint displayTimestamp = _originalAudioId >= 0x50000000 ? _originalAudioId : _originalAudioId + 0x50000000;
            AudioIdInput = displayTimestamp.ToString();
        }

        // Re-validate after changing the input
        ValidateAudioId();
    }

    private void ValidateAudioId()
    {
        AudioIdError = string.Empty;
        IsAudioIdValid = false;

        if (string.IsNullOrWhiteSpace(AudioIdInput))
        {
            AudioIdError = "Audio ID timestamp is required";
            return;
        }

        // Remove any spaces
        string cleanInput = AudioIdInput.Trim().Replace(" ", "");

        // Validate decimal format (up to 10 digits for Unix timestamp)
        if (!Regex.IsMatch(cleanInput, "^[0-9]{1,10}$"))
        {
            AudioIdError = "Audio ID must be a valid Unix timestamp (decimal number, up to 10 digits)";
            return;
        }

        // Parse the timestamp value
        if (!uint.TryParse(cleanInput, out uint timestamp))
        {
            AudioIdError = "Invalid timestamp value";
            return;
        }

        // Apply custom tonie offset if the switch is enabled
        uint audioId;
        if (IsCustomTonie)
        {
            // Custom tonie: timestamp - 0x50000000
            audioId = timestamp - 0x50000000;
        }
        else
        {
            // Use timestamp as-is (for official tonie Audio IDs)
            audioId = timestamp;
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
