using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace TeddyBench.Avalonia.Dialogs;

public partial class RestoreAsNewTonieDialogViewModel : ObservableObject
{
    private readonly Window _window;

    [ObservableProperty]
    private string _rfidUid = string.Empty;

    [ObservableProperty]
    private string _customTitle = string.Empty;

    [ObservableProperty]
    private string _audioIdTimestamp = string.Empty;

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private bool _isCustomTonie = false;

    private readonly uint _originalAudioId;
    private uint _lastGeneratedTimestamp;

    public bool DialogResult { get; private set; }

    public RestoreAsNewTonieDialogViewModel(Window window, string defaultTitle, uint currentAudioId)
    {
        _window = window;
        _customTitle = defaultTitle;

        // Store the original Audio ID
        _originalAudioId = currentAudioId;

        // Pre-fill with the current Audio ID (convert to timestamp by adding the offset back)
        // If currentAudioId is already in the custom range, it needs the offset added back
        // If it's in the official range (>= 0x50000000), use it as-is
        uint displayTimestamp = currentAudioId >= 0x50000000 ? currentAudioId : currentAudioId + 0x50000000;
        AudioIdTimestamp = displayTimestamp.ToString();
        _lastGeneratedTimestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ValidateInput();
    }

    partial void OnRfidUidChanged(string value)
    {
        // Auto-uppercase and validate
        if (!string.IsNullOrEmpty(value))
        {
            var upper = value.ToUpperInvariant();
            if (upper != value)
            {
                RfidUid = upper;
                return;
            }
        }

        ValidateInput();
    }

    partial void OnAudioIdTimestampChanged(string value)
    {
        ValidateInput();
    }

    partial void OnIsCustomTonieChanged(bool value)
    {
        // When toggle changes, update the input field
        if (value)
        {
            // Switching to Custom Tonie mode: use current timestamp
            _lastGeneratedTimestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            AudioIdTimestamp = _lastGeneratedTimestamp.ToString();
        }
        else
        {
            // Switching back to original mode: restore original Audio ID
            uint displayTimestamp = _originalAudioId >= 0x50000000 ? _originalAudioId : _originalAudioId + 0x50000000;
            AudioIdTimestamp = displayTimestamp.ToString();
        }

        // Re-validate after changing the input
        ValidateInput();
    }

    private void ValidateInput()
    {
        // RFID must be exactly 8 hex characters
        bool rfidValid = RfidUid.Length == 8 &&
                        System.Text.RegularExpressions.Regex.IsMatch(RfidUid, "^[0-9A-F]{8}$");

        // Audio ID is optional, but if provided must be a valid decimal timestamp
        bool audioIdValid = string.IsNullOrEmpty(AudioIdTimestamp) ||
                           (uint.TryParse(AudioIdTimestamp, out uint _) && AudioIdTimestamp.Length <= 10);

        IsValid = rfidValid && audioIdValid;
    }

    [RelayCommand]
    private void Restore()
    {
        DialogResult = true;
        _window.Close();
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        _window.Close();
    }

    public uint? GetAudioId()
    {
        if (string.IsNullOrEmpty(AudioIdTimestamp))
        {
            return null;
        }

        // Parse the decimal timestamp
        if (uint.TryParse(AudioIdTimestamp, out uint timestamp))
        {
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
            return audioId;
        }

        return null;
    }
}