using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TeddyBench.Avalonia.Models;
using TeddyBench.Avalonia.Services;
using TonieFile;

namespace TeddyBench.Avalonia.ViewModels;

public partial class PlayerDialogViewModel : ViewModelBase
{
    private readonly AudioPlayerService _audioPlayer;
    private readonly string _tonieFilePath;
    private readonly Window? _window;
    private TimeSpan? _pendingSeekPosition = null;

    public PlayerDialogViewModel(string tonieFilePath, string displayName, Window? window = null)
    {
        _tonieFilePath = tonieFilePath;
        _window = window;
        Title = $"Player for: {displayName}";

        _audioPlayer = new AudioPlayerService();

        // Hook up audio player events
        _audioPlayer.PlaybackStateChanged += (s, state) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                PlaybackState = state;
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(IsStopped));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(PlayPauseButtonText));

                // If we have a pending seek and playback just started, apply it now
                if (state == PlaybackState.Playing && _pendingSeekPosition.HasValue)
                {
                    Console.WriteLine($"[PlayerDialog] Applying pending seek to {_pendingSeekPosition.Value}");
                    _audioPlayer.Seek(_pendingSeekPosition.Value);
                    _pendingSeekPosition = null;
                }
            });
        };

        _audioPlayer.PositionChanged += (s, position) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                CurrentPosition = position;
                PositionSeconds = position.TotalSeconds;
                OnPropertyChanged(nameof(CurrentPositionText));
            });
        };

        _audioPlayer.PlaybackFinished += (s, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                // Reset to beginning
                CurrentPosition = TimeSpan.Zero;
                PositionSeconds = 0;
            });
        };

        // Load track information
        LoadTracks();
    }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private PlaybackState _playbackState = PlaybackState.Stopped;

    [ObservableProperty]
    private TimeSpan _currentPosition = TimeSpan.Zero;

    [ObservableProperty]
    private double _positionSeconds;

    [ObservableProperty]
    private double _durationSeconds;

    [ObservableProperty]
    private ObservableCollection<TrackInfo> _tracks = new();

    public bool IsPlaying => PlaybackState == PlaybackState.Playing;
    public bool IsPaused => PlaybackState == PlaybackState.Paused;
    public bool IsStopped => PlaybackState == PlaybackState.Stopped;
    public bool CanStop => IsPlaying || IsPaused;
    public string PlayPauseButtonText => IsPlaying ? "Pause" : "Play";
    public string CurrentPositionText => FormatTime(CurrentPosition);
    public string DurationText => FormatTime(_audioPlayer?.Duration ?? TimeSpan.Zero);

    private void LoadTracks()
    {
        try
        {
            var tonie = TonieAudio.FromFile(_tonieFilePath, readAudio: false);
            var positions = tonie.ParsePositions();

            // Get total duration from CalculateStatistics which reads the highest granule
            TimeSpan totalDuration = TimeSpan.Zero;
            try
            {
                tonie.CalculateStatistics(out _, out _, out _, out _, out _, out _, out ulong highestGranule);
                totalDuration = TimeSpan.FromSeconds((double)highestGranule / 48000.0);
                Console.WriteLine($"[PlayerDialog] Total duration from highest granule: {totalDuration} (granule: {highestGranule})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PlayerDialog] Error calculating statistics: {ex.Message}");
            }

            // Convert granules to TimeSpan and deduplicate
            var distinctPositions = new List<TimeSpan>();
            var seenPositions = new HashSet<ulong>();

            for (int i = 0; i < positions.Length; i++)
            {
                // Skip duplicate positions
                if (!seenPositions.Add(positions[i]))
                    continue;

                // Convert granule to TimeSpan (48000 Hz sample rate)
                var totalSeconds = (double)positions[i] / 48000.0;
                var timeSpan = TimeSpan.FromSeconds(totalSeconds);
                distinctPositions.Add(timeSpan);
            }

            Console.WriteLine($"[PlayerDialog] Distinct positions count: {distinctPositions.Count}, Total duration: {totalDuration}");
            for (int i = 0; i < distinctPositions.Count; i++)
            {
                Console.WriteLine($"[PlayerDialog] Distinct position {i}: {distinctPositions[i]}");
            }

            // Add tracks based on distinct positions with calculated durations
            // All tracks except the last one - calculate duration from next track start
            for (int i = 0; i < distinctPositions.Count - 1; i++)
            {
                var trackStart = distinctPositions[i];
                var trackEnd = distinctPositions[i + 1];
                var trackDuration = trackEnd - trackStart;

                Console.WriteLine($"[PlayerDialog] Adding track {i + 1}: start={trackStart}, end={trackEnd}, duration={trackDuration}");

                Tracks.Add(new TrackInfo
                {
                    TrackNumber = i + 1,
                    Position = trackStart,
                    DisplayText = $"Track {i + 1} ({FormatTime(trackDuration)})"
                });
            }

            // Add the last track
            if (distinctPositions.Count > 0)
            {
                var lastTrackStart = distinctPositions[distinctPositions.Count - 1];

                // If we have a valid total duration, calculate the last track duration
                // Otherwise, just show the track without duration
                string displayText;
                if (totalDuration > TimeSpan.Zero)
                {
                    var lastTrackDuration = totalDuration - lastTrackStart;
                    displayText = $"Track {distinctPositions.Count} ({FormatTime(lastTrackDuration)})";
                    Console.WriteLine($"[PlayerDialog] Adding last track {distinctPositions.Count}: start={lastTrackStart}, duration={lastTrackDuration}");
                }
                else
                {
                    displayText = $"Track {distinctPositions.Count}";
                    Console.WriteLine($"[PlayerDialog] Adding last track {distinctPositions.Count} without duration: start={lastTrackStart}");
                }

                Tracks.Add(new TrackInfo
                {
                    TrackNumber = distinctPositions.Count,
                    Position = lastTrackStart,
                    DisplayText = displayText
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PlayerDialog] Error loading tracks: {ex.Message}");
            // If we can't load tracks, just show one entry
            Tracks.Add(new TrackInfo
            {
                TrackNumber = 1,
                Position = TimeSpan.Zero,
                DisplayText = "Track 1"
            });
        }
    }

    [RelayCommand]
    private void PlayPause()
    {
        try
        {
            if (IsPlaying)
            {
                _audioPlayer.Pause();
            }
            else if (IsPaused)
            {
                _audioPlayer.Resume();
            }
            else
            {
                // Start playback
                _audioPlayer.Play(_tonieFilePath);

                // Update duration
                var duration = _audioPlayer.Duration;
                DurationSeconds = duration.TotalSeconds;
                OnPropertyChanged(nameof(DurationText));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PlayerDialog] Playback error: {ex}");
        }
    }

    [RelayCommand]
    private void Stop()
    {
        try
        {
            _audioPlayer.Stop();
            CurrentPosition = TimeSpan.Zero;
            PositionSeconds = 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PlayerDialog] Stop error: {ex}");
        }
    }

    [RelayCommand]
    private void SeekTo(double seconds)
    {
        try
        {
            _audioPlayer.Seek(TimeSpan.FromSeconds(seconds));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PlayerDialog] Seek error: {ex}");
        }
    }

    [RelayCommand]
    private void SeekToTrack(TimeSpan position)
    {
        try
        {
            // If stopped, start playback first and queue the seek
            if (IsStopped)
            {
                Console.WriteLine($"[PlayerDialog] Starting playback with pending seek to {position}");
                _pendingSeekPosition = position;
                _audioPlayer.Play(_tonieFilePath);

                // Update duration
                var duration = _audioPlayer.Duration;
                DurationSeconds = duration.TotalSeconds;
                OnPropertyChanged(nameof(DurationText));
            }
            else
            {
                // Already playing, seek immediately
                Console.WriteLine($"[PlayerDialog] Seeking immediately to {position}");
                _audioPlayer.Seek(position);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PlayerDialog] Seek to track error: {ex}");
        }
    }

    [RelayCommand]
    private void Close()
    {
        Cleanup();
        _window?.Close();
    }

    public void Cleanup()
    {
        // Stop playback and dispose
        _audioPlayer.Stop();
        _audioPlayer.Dispose();
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time.TotalHours >= 1)
        {
            return time.ToString(@"h\:mm\:ss");
        }
        else
        {
            return time.ToString(@"m\:ss");
        }
    }
}

public class TrackInfo
{
    public int TrackNumber { get; set; }
    public TimeSpan Position { get; set; }
    public string DisplayText { get; set; } = string.Empty;
}
