using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace TeddyBench.Avalonia.Dialogs;

public partial class RestoreAsNewCustomTonieDialogViewModel : ObservableObject
{
    private readonly Window _window;

    [ObservableProperty]
    private string _rfidUid = string.Empty;

    [ObservableProperty]
    private string _customTitle = string.Empty;

    [ObservableProperty]
    private string _audioIdHex = string.Empty;

    [ObservableProperty]
    private bool _isValid;

    public bool DialogResult { get; private set; }

    public RestoreAsNewCustomTonieDialogViewModel(Window window, string defaultTitle)
    {
        _window = window;
        _customTitle = defaultTitle;
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

    partial void OnAudioIdHexChanged(string value)
    {
        // Auto-uppercase and validate
        if (!string.IsNullOrEmpty(value))
        {
            var upper = value.ToUpperInvariant();
            if (upper != value)
            {
                AudioIdHex = upper;
                return;
            }
        }

        ValidateInput();
    }

    private void ValidateInput()
    {
        // RFID must be exactly 8 hex characters
        bool rfidValid = RfidUid.Length == 8 &&
                        System.Text.RegularExpressions.Regex.IsMatch(RfidUid, "^[0-9A-F]{8}$");

        // Audio ID is optional, but if provided must be valid hex
        bool audioIdValid = string.IsNullOrEmpty(AudioIdHex) ||
                           (AudioIdHex.Length <= 8 &&
                            System.Text.RegularExpressions.Regex.IsMatch(AudioIdHex, "^[0-9A-F]+$"));

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
        if (string.IsNullOrEmpty(AudioIdHex))
        {
            return null;
        }

        if (uint.TryParse(AudioIdHex, System.Globalization.NumberStyles.HexNumber, null, out uint audioId))
        {
            return audioId;
        }

        return null;
    }
}