using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using TeddyBench.Avalonia.Models;
using TonieFile;

namespace TeddyBench.Avalonia.Services;

/// <summary>
/// Cross-platform audio player service using LibVLC for reliable playback
/// </summary>
public class AudioPlayerService : IDisposable
{
    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private Timer? _positionTimer;
    private bool _isDisposed;

    /// <summary>
    /// Raised when playback state changes
    /// </summary>
    public event EventHandler<PlaybackState>? PlaybackStateChanged;

    /// <summary>
    /// Raised periodically during playback with current position
    /// </summary>
    public event EventHandler<TimeSpan>? PositionChanged;

    /// <summary>
    /// Raised when playback finishes naturally (reaches end)
    /// </summary>
    public event EventHandler? PlaybackFinished;

    /// <summary>
    /// Raised when an error occurs during playback
    /// </summary>
    public event EventHandler<string>? PlaybackError;

    /// <summary>
    /// Current playback state
    /// </summary>
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;

    /// <summary>
    /// Current playback position
    /// </summary>
    public TimeSpan CurrentPosition
    {
        get
        {
            if (_mediaPlayer != null && _mediaPlayer.IsPlaying)
            {
                return TimeSpan.FromMilliseconds(_mediaPlayer.Time);
            }
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Total duration of loaded audio
    /// </summary>
    public TimeSpan Duration
    {
        get
        {
            if (_mediaPlayer?.Media != null)
            {
                return TimeSpan.FromMilliseconds(_mediaPlayer.Length);
            }
            return TimeSpan.Zero;
        }
    }

    public AudioPlayerService()
    {
        Core.Initialize();
        _libVLC = new LibVLC();
    }

    /// <summary>
    /// Load and play a Tonie audio file
    /// </summary>
    /// <param name="tonieFilePath">Path to the Tonie file</param>
    public void Play(string tonieFilePath)
    {
        try
        {
            Console.WriteLine($"[AudioPlayer] Starting playback of: {tonieFilePath}");

            if (_libVLC == null)
            {
                OnPlaybackError("LibVLC not initialized");
                return;
            }

            // Stop any existing playback
            Stop();

            // Load the Tonie file
            Console.WriteLine("[AudioPlayer] Loading Tonie file...");
            var tonie = TonieAudio.FromFile(tonieFilePath, readAudio: true);
            if (tonie.Audio == null || tonie.Audio.Length == 0)
            {
                OnPlaybackError("No audio data in Tonie file");
                return;
            }

            Console.WriteLine($"[AudioPlayer] Audio data loaded: {tonie.Audio.Length} bytes");

            // Create a temporary file for the audio (LibVLC works best with files)
            var tempFile = Path.GetTempFileName();
            File.WriteAllBytes(tempFile, tonie.Audio);

            Console.WriteLine($"[AudioPlayer] Created temp file: {tempFile}");

            // Create media and player
            var media = new Media(_libVLC, tempFile, FromType.FromPath);
            _mediaPlayer = new MediaPlayer(media);

            // Hook up events
            _mediaPlayer.Playing += (s, e) =>
            {
                SetState(PlaybackState.Playing);
                StartPositionTimer();
            };

            _mediaPlayer.Paused += (s, e) => SetState(PlaybackState.Paused);

            _mediaPlayer.Stopped += (s, e) =>
            {
                SetState(PlaybackState.Stopped);
                StopPositionTimer();
            };

            _mediaPlayer.EndReached += (s, e) =>
            {
                StopPositionTimer();
                OnPlaybackFinished();
            };

            _mediaPlayer.EncounteredError += (s, e) =>
            {
                OnPlaybackError("VLC encountered an error during playback");
            };

            // Start playback
            _mediaPlayer.Play();
            Console.WriteLine("[AudioPlayer] Playback started");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioPlayer] ERROR: {ex}");
            OnPlaybackError($"Failed to start playback: {ex.Message}");
            Stop();
        }
    }

    /// <summary>
    /// Pause playback
    /// </summary>
    public void Pause()
    {
        Console.WriteLine($"[AudioPlayer] Pause called. Current state: {State}");
        if (_mediaPlayer != null && State == PlaybackState.Playing)
        {
            _mediaPlayer.Pause();
            StopPositionTimer();
            Console.WriteLine("[AudioPlayer] Paused");
        }
    }

    /// <summary>
    /// Resume playback
    /// </summary>
    public void Resume()
    {
        Console.WriteLine($"[AudioPlayer] Resume called. Current state: {State}");
        if (_mediaPlayer != null && State == PlaybackState.Paused)
        {
            _mediaPlayer.Play();
            StartPositionTimer();
            Console.WriteLine("[AudioPlayer] Resumed");
        }
    }

    /// <summary>
    /// Stop playback and release resources
    /// </summary>
    public void Stop()
    {
        Console.WriteLine($"[AudioPlayer] Stop called. Current state: {State}");

        StopPositionTimer();

        if (_mediaPlayer != null)
        {
            _mediaPlayer.Stop();
            _mediaPlayer.Media?.Dispose();
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
        }

        SetState(PlaybackState.Stopped);
        Console.WriteLine("[AudioPlayer] Stopped");
    }

    /// <summary>
    /// Seek to a specific position in the audio
    /// </summary>
    /// <param name="position">Target position</param>
    public void Seek(TimeSpan position)
    {
        if (_mediaPlayer == null || State == PlaybackState.Stopped)
        {
            Console.WriteLine($"[AudioPlayer] Seek ignored - player: {_mediaPlayer != null}, state: {State}");
            return;
        }

        try
        {
            Console.WriteLine($"[AudioPlayer] Seeking to {position}");
            _mediaPlayer.Time = (long)position.TotalMilliseconds;
            Console.WriteLine($"[AudioPlayer] Seek complete");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioPlayer] Seek error: {ex}");
            OnPlaybackError($"Failed to seek: {ex.Message}");
        }
    }

    private void StartPositionTimer()
    {
        StopPositionTimer();
        _positionTimer = new Timer(_ =>
        {
            OnPositionChanged(CurrentPosition);
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
    }

    private void StopPositionTimer()
    {
        _positionTimer?.Dispose();
        _positionTimer = null;
    }

    private void SetState(PlaybackState newState)
    {
        if (State == newState)
            return;

        State = newState;
        PlaybackStateChanged?.Invoke(this, newState);
    }

    private void OnPositionChanged(TimeSpan position)
    {
        PositionChanged?.Invoke(this, position);
    }

    private void OnPlaybackFinished()
    {
        PlaybackFinished?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlaybackError(string message)
    {
        PlaybackError?.Invoke(this, message);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        Stop();
        _libVLC?.Dispose();
        _libVLC = null;
        _isDisposed = true;
    }
}
