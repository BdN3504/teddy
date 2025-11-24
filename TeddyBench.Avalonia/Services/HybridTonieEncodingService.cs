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
    /// Uses the NEW LOSSLESS approach: extracts original tracks without re-encoding (perfect quality preservation),
    /// encodes only new tracks, then combines using proper Ogg page manipulation.
    /// This approach guarantees deterministic hashes and no generation loss for original tracks.
    /// </summary>
    public (byte[] FileContent, string Hash) EncodeHybridTonie(
        List<TrackSourceInfo> tracks,
        uint audioId,
        string originalTonieFilePath,
        int bitRate = 96,
        TonieFile.TonieAudio.EncodeCallback? callback = null)
    {
        // Check if we have any original tracks
        var hasOriginalTracks = tracks.Any(t => t.IsOriginal);

        // If all tracks are new, just use regular encoding
        if (!hasOriginalTracks)
        {
            var audioPaths = tracks.Select(t => t.AudioFilePath!).ToArray();
            TonieAudio generatedAudio = new TonieAudio(audioPaths, audioId, bitRate * 1000, false, null, callback);
            string resultHash = BitConverter.ToString(generatedAudio.Header.Hash).Replace("-", "");
            return (generatedAudio.FileContent, resultHash);
        }

        // LOSSLESS APPROACH: Extract raw Ogg data without re-encoding
        var originalAudio = TonieAudio.FromFile(originalTonieFilePath, readAudio: true);
        List<byte[]> rawChapterData = originalAudio.ExtractRawChapterData();

        try
        {
            // Build list of all track Ogg data in correct order
            var allTrackOggData = new List<byte[]>();

            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];

                if (track.IsOriginal && track.OriginalTrackIndex >= 0 && track.OriginalTrackIndex < rawChapterData.Count)
                {
                    // Use raw chapter data (already encoded, no quality loss!)
                    // Update stream serial number to match our audio ID
                    // Note: resetGranulePositions = true because extracted chapters have cumulative granules from original file
                    var tempAudio = new TonieAudio();
                    tempAudio.Audio = rawChapterData[track.OriginalTrackIndex];
                    byte[] updatedOggData = tempAudio.UpdateStreamSerialNumber(audioId, resetGranulePositions: true);
                    allTrackOggData.Add(updatedOggData);
                }
                else
                {
                    // Encode new track with same audio ID
                    TonieAudio newTrackAudio = new TonieAudio(new[] { track.AudioFilePath! }, audioId, bitRate * 1000, false, null, callback);
                    // Extract just the audio data (Ogg stream)
                    allTrackOggData.Add(newTrackAudio.Audio);
                }
            }

            // Combine all tracks losslessly using proper Ogg page manipulation
            var (audioData, hash, chapterMarkers) = TonieAudio.CombineOggTracksLossless(allTrackOggData, audioId);

            // Build final Tonie file with header
            byte[] fileContent = new byte[audioData.Length + 0x1000];
            Array.Copy(audioData, 0, fileContent, 0x1000, audioData.Length);

            // Create and write header
            var tonieAudio = new TonieAudio();
            tonieAudio.FileContent = fileContent;
            tonieAudio.Audio = audioData;
            tonieAudio.Header.Hash = hash;
            tonieAudio.Header.AudioLength = audioData.Length;
            tonieAudio.Header.AudioId = audioId;
            tonieAudio.Header.AudioChapters = chapterMarkers;
            tonieAudio.Header.Padding = new byte[0];
            tonieAudio.UpdateFileContent();

            string resultHash = BitConverter.ToString(hash).Replace("-", "");
            return (tonieAudio.FileContent, resultHash);
        }
        catch (Exception ex)
        {
            // If lossless approach fails, provide helpful error message
            throw new Exception($"Failed to encode hybrid tonie: {ex.Message}", ex);
        }
    }
}
