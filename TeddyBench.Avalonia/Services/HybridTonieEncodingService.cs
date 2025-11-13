using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TonieFile;

namespace TeddyBench.Avalonia.Services;

/// <summary>
/// Service for encoding tonies with a mix of original (already-encoded) and new (need encoding) tracks.
/// This avoids quality loss from re-encoding already-encoded Opus files.
/// </summary>
public class HybridTonieEncodingService
{
    public class TrackSourceInfo
    {
        /// <summary>
        /// Track index in the original tonie (if IsOriginal is true)
        /// </summary>
        public int OriginalTrackIndex { get; set; } = -1;

        /// <summary>
        /// If true, this is an original track from the tonie.
        /// If false, this is a new track (use AudioFilePath).
        /// </summary>
        public bool IsOriginal { get; set; }

        /// <summary>
        /// For new tracks: the path to the audio file to encode
        /// </summary>
        public string? AudioFilePath { get; set; }
    }

    /// <summary>
    /// Encodes a custom tonie from a mix of original and new tracks.
    /// Original tracks are copied directly (no re-encoding), new tracks are encoded.
    /// </summary>
    public (byte[] FileContent, string Hash) EncodeHybridTonie(
        List<TrackSourceInfo> tracks,
        uint audioId,
        string originalTonieFilePath,
        int bitRate = 96)
    {
        // Check if we have any original tracks
        var hasOriginalTracks = tracks.Any(t => t.IsOriginal);

        // If all tracks are new, just use regular encoding
        if (!hasOriginalTracks)
        {
            var audioPaths = tracks.Select(t => t.AudioFilePath!).ToArray();
            TonieAudio generatedAudio = new TonieAudio(audioPaths, audioId, bitRate * 1000, false, null);
            string resultHash = BitConverter.ToString(generatedAudio.Header.Hash).Replace("-", "");
            return (generatedAudio.FileContent, resultHash);
        }

        // Extract raw chapter data from original tonie
        var originalAudio = TonieAudio.FromFile(originalTonieFilePath);
        var rawChapters = originalAudio.ExtractRawChapterData();

        // Build array of TrackSource objects for TonieAudio
        var trackSources = new List<TonieAudio.TrackSource>();

        for (int i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];

            if (track.IsOriginal && track.OriginalTrackIndex >= 0 && track.OriginalTrackIndex < rawChapters.Count)
            {
                // Use pre-encoded data
                trackSources.Add(new TonieAudio.TrackSource(rawChapters[track.OriginalTrackIndex]));
            }
            else
            {
                // Use file path for new tracks
                trackSources.Add(new TonieAudio.TrackSource(track.AudioFilePath!));
            }
        }

        // Use the new TonieAudio constructor that supports mixed sources
        // Pass the original audio data so headers can be extracted correctly
        TonieAudio hybridAudio = new TonieAudio(trackSources.ToArray(), originalAudio.Audio, audioId, bitRate * 1000, false, null);

        string hybridHash = BitConverter.ToString(hybridAudio.Header.Hash).Replace("-", "");
        return (hybridAudio.FileContent, hybridHash);
    }
}
