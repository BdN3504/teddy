using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        var totalSw = Stopwatch.StartNew();
        Console.WriteLine($"[ENCODING TIMING] Starting hybrid encoding with {tracks.Count} tracks");

        // Check if we have any original tracks
        var hasOriginalTracks = tracks.Any(t => t.IsOriginal);
        var originalCount = tracks.Count(t => t.IsOriginal);
        var newCount = tracks.Count - originalCount;
        Console.WriteLine($"[ENCODING TIMING] {originalCount} original tracks (copied), {newCount} new tracks (encoded)");

        // If all tracks are new, just use regular encoding
        if (!hasOriginalTracks)
        {
            Console.WriteLine("[ENCODING TIMING] All tracks are new, using regular encoding");
            var sw = Stopwatch.StartNew();
            var audioPaths = tracks.Select(t => t.AudioFilePath!).ToArray();
            TonieAudio generatedAudio = new TonieAudio(audioPaths, audioId, bitRate * 1000, false, null);
            Console.WriteLine($"[ENCODING TIMING] Regular encoding took {sw.ElapsedMilliseconds}ms");
            string resultHash = BitConverter.ToString(generatedAudio.Header.Hash).Replace("-", "");
            Console.WriteLine($"[ENCODING TIMING] Total encoding time: {totalSw.ElapsedMilliseconds}ms");
            return (generatedAudio.FileContent, resultHash);
        }

        // Extract raw chapter data from original tonie
        var sw1 = Stopwatch.StartNew();
        Console.WriteLine("[ENCODING TIMING] Reading original tonie file...");
        var originalAudio = TonieAudio.FromFile(originalTonieFilePath);
        Console.WriteLine($"[ENCODING TIMING] Read original tonie: {sw1.ElapsedMilliseconds}ms");

        sw1.Restart();
        Console.WriteLine("[ENCODING TIMING] Extracting raw chapter data...");
        var rawChapters = originalAudio.ExtractRawChapterData();
        Console.WriteLine($"[ENCODING TIMING] Extract chapters: {sw1.ElapsedMilliseconds}ms");

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
        sw1.Restart();
        Console.WriteLine("[ENCODING TIMING] Creating hybrid tonie with mixed sources...");
        TonieAudio hybridAudio = new TonieAudio(trackSources.ToArray(), originalAudio.Audio, audioId, bitRate * 1000, false, null);
        Console.WriteLine($"[ENCODING TIMING] Create hybrid tonie: {sw1.ElapsedMilliseconds}ms");

        string hybridHash = BitConverter.ToString(hybridAudio.Header.Hash).Replace("-", "");
        Console.WriteLine($"[ENCODING TIMING] Total encoding time: {totalSw.ElapsedMilliseconds}ms");
        return (hybridAudio.FileContent, hybridHash);
    }
}
