using CommunityToolkit.Mvvm.ComponentModel;

namespace TeddyBench.Avalonia.Models;

public partial class AudioTrackItem : ObservableObject
{
    [ObservableProperty]
    private int _trackNumber;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}
