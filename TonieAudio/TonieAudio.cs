/* Copyright (c) 2020 g3gg0.de

   Redistribution and use in source and binary forms, with or without
   modification, are permitted provided that the following conditions
   are met:

   - Redistributions of source code must retain the above copyright
   notice, this list of conditions and the following disclaimer.

   - Redistributions in binary form must reproduce the above copyright
   notice, this list of conditions and the following disclaimer in the
   documentation and/or other materials provided with the distribution.

   - Neither the name of Internet Society, IETF or IETF Trust, nor the
   names of specific contributors, may be used to endorse or promote
   products derived from this software without specific prior written
   permission.

   THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
   ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
   LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
   A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER
   OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
   EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
   PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
   PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
   LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
   NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
   SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;
using Id3;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using static TonieFile.ProtoCoder;

namespace TonieFile
{
    public class TonieAudio
    {
        /// <summary>
        /// Represents a source for a track - either a file path to encode, or pre-encoded Ogg data.
        /// </summary>
        public class TrackSource
        {
            /// <summary>
            /// Path to audio file to encode (for new tracks)
            /// </summary>
            public string FilePath { get; set; }

            /// <summary>
            /// Pre-encoded Ogg data (for original tracks to avoid re-encoding)
            /// </summary>
            public byte[] PreEncodedOggData { get; set; }

            /// <summary>
            /// True if this is pre-encoded data, false if it's a file path
            /// </summary>
            public bool IsPreEncoded => PreEncodedOggData != null && PreEncodedOggData.Length > 0;

            public TrackSource(string filePath)
            {
                FilePath = filePath;
                PreEncodedOggData = null;
            }

            public TrackSource(byte[] preEncodedData)
            {
                PreEncodedOggData = preEncodedData;
                FilePath = null;
            }
        }

        public class FileHeader
        {
            public byte[] Hash;
            public int AudioLength;
            public uint AudioId;
            public uint[] AudioChapters;
            public byte[] Padding;
            [SkipEncode]
            /* in sfx.bin this is set to zero, all other files miss this field */
            public bool Usable = true;
        }

        public class EncodingException : Exception
        {
            public EncodingException(string message) : base(message)
            {
            }
        }

        private Dictionary<uint, ulong> PageGranuleMap = new Dictionary<uint, ulong>();
        public FileHeader Header = new FileHeader();
        public byte[] Audio = new byte[0];
        public byte[] FileContent = new byte[0];
        public List<string> FileList = new List<string>();
        public long HeaderLength { get; private set; }
        public bool HashCorrect = false;
        public string Filename { get; set; }
        public string FilenameShort => new FileInfo(Filename).Name;


        public static string FormatGranule(ulong granule)
        {
            ulong time = 100 * granule / 48000;
            ulong frames = time % 100;
            ulong seconds = (time / 100) % 60;
            ulong minutes = (time / 100 / 60) % 60;
            ulong hours = (time / 100 / 60 / 60);

            return hours.ToString("00") + ":" + minutes.ToString("00") + ":" + seconds.ToString("00") + "." + frames.ToString("00");
        }

        public ulong GetGranuleByPage(uint page)
        {
            if (PageGranuleMap.ContainsKey(page))
            {
                return PageGranuleMap[page];
            }
            return ulong.MaxValue;
        }

        public uint GetHighestPage()
        {
            return PageGranuleMap.Keys.Last();
        }


        public void CalculateStatistics(out long totalSegments, out long segLength, out int minSegs, out int maxSegs, out ulong minGranule, out ulong maxGranule, out ulong highestGranule)
        {
            totalSegments = 0;
            segLength = 0;
            minSegs = 0xff;
            maxSegs = 0;
            minGranule = long.MaxValue;
            maxGranule = 0;
            highestGranule = 0;
            var file = File.Open(Filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long lastPos = 0x1000;
            long curPos = 0x1000;
            ulong lastGranule = 0;
            byte[] oggPageBuf = new byte[27];

            while (curPos < file.Length)
            {
                lastPos = curPos;

                file.Seek(curPos, SeekOrigin.Begin);
                file.Read(oggPageBuf, 0, oggPageBuf.Length);

                if (oggPageBuf[0] != 'O' || oggPageBuf[1] != 'g' || oggPageBuf[2] != 'g')
                {
                    Console.WriteLine("[ERROR] Not an Ogg page header at 0x" + curPos.ToString("X8"));
                    break;
                }

                ulong granule = BitConverter.ToUInt64(oggPageBuf, 6);
                uint pageNum = BitConverter.ToUInt32(oggPageBuf, 0x12);
                byte segmentsCount = oggPageBuf[26];
                byte[] segmentLengths = new byte[segmentsCount];

                if (!PageGranuleMap.ContainsKey(pageNum))
                {
                    PageGranuleMap.Add(pageNum, granule);
                }

                file.Read(segmentLengths, 0, segmentLengths.Length);

                curPos += 27;
                curPos += segmentLengths.Length;

                foreach (var len in segmentLengths)
                {
                    totalSegments++;
                    segLength += len;
                    curPos += len;
                }

                long lastOffset = lastPos % 0x1000;
                long curOffset = curPos % 0x1000;
                if (lastOffset >= curOffset && curOffset != 0)
                {
                    Console.WriteLine("[ERROR] Ogg page ends in next block at 0x" + curPos.ToString("X8"));
                    break;
                }

                if (lastPos >= 0x2000 && curPos < file.Length)
                {
                    if(lastGranule > granule)
                    {
                        Console.WriteLine("[ERROR] granule at 0x" + curPos.ToString("X8") + " is lower than last.");
                        break;
                    }
                    ulong granuleDelta = granule - lastGranule;

                    minSegs = Math.Min(minSegs, segmentsCount);
                    maxSegs = Math.Max(maxSegs, segmentsCount);
                    minGranule = Math.Min(minGranule, granuleDelta);
                    maxGranule = Math.Max(maxGranule, granuleDelta);
                }

                highestGranule = Math.Max(highestGranule, granule);
                lastGranule = granule;
            }
            file.Close();
        }

        public ulong[] ParsePositions()
        {
            List<ulong> positions = new List<ulong>();

            // Always start with position 0
            positions.Add(0);

            if (Header.AudioChapters.Length == 0)
            {
                // No chapters, just return start position
                return positions.ToArray();
            }

            // Parse Ogg stream to find chapter positions
            int offset = 0;
            int curChapter = 0;
            ulong lastGranule = 0;

            while (offset < Audio.Length - 27)
            {
                // Check for Ogg page signature
                if (Audio[offset] != 'O' || Audio[offset + 1] != 'g' ||
                    Audio[offset + 2] != 'g' || Audio[offset + 3] != 'S')
                {
                    offset++;
                    continue;
                }

                // Read page header
                ulong granule = BitConverter.ToUInt64(Audio, offset + 6);
                uint pageNum = BitConverter.ToUInt32(Audio, offset + 18);
                byte segmentCount = Audio[offset + 26];

                // Track the last valid granule for the final position
                if (granule != 0 && granule != ulong.MaxValue)
                {
                    lastGranule = granule;
                }

                // Check if this page starts a new chapter
                if (curChapter < Header.AudioChapters.Length && pageNum >= Header.AudioChapters[curChapter])
                {
                    // Always add the granule position for this chapter marker
                    // The player will deduplicate if needed (e.g., when chapter[0] = 0)
                    positions.Add(granule);
                    curChapter++;
                }

                // Calculate page size to advance offset
                int dataSize = 0;
                for (int s = 0; s < segmentCount && (offset + 27 + s) < Audio.Length; s++)
                {
                    dataSize += Audio[offset + 27 + s];
                }

                offset += 27 + segmentCount + dataSize;
            }

            // Add final position if we haven't added it yet
            if (positions.Count == 1 || positions[positions.Count - 1] != lastGranule)
            {
                positions.Add(lastGranule);
            }

            return positions.ToArray();
        }

        public class EncodeCallback
        {
            protected string ShortName;
            protected string DisplayName;

            public virtual void Progress(decimal pct)
            {
                int lastPct = (int)(pct * 20);
                if (lastPct % 5 == 0)
                {
                    if (lastPct != 20)
                    {
                        Console.Write("" + (lastPct * 5) + "%");
                    }
                }
                else
                {
                    Console.Write(".");
                }
            }

            public virtual void FileStart(int track, string sourceFile)
            {
                ParseName(track, sourceFile);
                Console.Write(" Track " + track.ToString().PadLeft(3) + " - " + ShortName + "  [");
            }

            protected void ParseName(int track, string sourceFile)
            {
                int snipLen = 15;
                DisplayName = new FileInfo(sourceFile).Name;
                try
                {
                    var tag = new Mp3(sourceFile, Mp3Permissions.Read).GetAllTags().FirstOrDefault();
                    if (tag != null && tag.Title.IsAssigned)
                    {
                        DisplayName = tag.Title.Value;
                    }
                }
                catch (Exception ex)
                {

                }

                ShortName = DisplayName.PadRight(snipLen).Substring(0, snipLen);
            }

            public virtual void FileDone()
            {
               Console.WriteLine("]");
            }
            public virtual void FileFailed(string message)
            {
                Console.WriteLine("]");
                Console.WriteLine("File Failed: " + message);
            }

            public virtual void Failed(string message)
            {
                Console.WriteLine("]");
                Console.WriteLine("Failed: " + message);
            }

            public virtual void Warning(string message)
            {
                Console.WriteLine("");
                Console.WriteLine("Warning: " + message);
            }

            public virtual void PostProcessing(string message)
            {
                Console.WriteLine("");
                Console.WriteLine(message);
            }
        }

        public TonieAudio()
        {
        }

        public TonieAudio(string[] sources, uint audioId, int bitRate = 48000, bool useVbr = false, string prefixLocation = null, EncodeCallback cbr = null)
        {
            BuildFileList(sources);
            BuildFromFiles(FileList, audioId, bitRate, useVbr, prefixLocation, cbr);
        }

        /// <summary>
        /// Constructor that supports mixing pre-encoded tracks with new tracks.
        /// Pre-encoded tracks are copied directly without quality loss.
        /// </summary>
        public TonieAudio(TrackSource[] trackSources, uint audioId, int bitRate = 48000, bool useVbr = false, string prefixLocation = null, EncodeCallback cbr = null)
        {
            BuildFromTrackSources(trackSources, null, audioId, bitRate, useVbr, prefixLocation, cbr);
        }

        /// <summary>
        /// Constructor that supports mixing pre-encoded tracks with new tracks.
        /// Pre-encoded tracks are copied directly without quality loss.
        /// Accepts original audio data for extracting proper headers.
        /// </summary>
        public TonieAudio(TrackSource[] trackSources, byte[] originalAudioData, uint audioId, int bitRate = 48000, bool useVbr = false, string prefixLocation = null, EncodeCallback cbr = null)
        {
            BuildFromTrackSources(trackSources, originalAudioData, audioId, bitRate, useVbr, prefixLocation, cbr);
        }

        public static TonieAudio FromFile(string file, bool readAudio = true)
        {
            TonieAudio audio = new TonieAudio();
            audio.ReadFile(file, readAudio);

            return audio;
        }

        private void BuildFileList(string[] sources)
        {
            foreach (var source in sources)
            {
                // Remove quotes and only trim trailing directory separators (not leading ones)
                string item = source.Trim('"').TrimEnd(Path.DirectorySeparatorChar);

                if (Directory.Exists(item))
                {
                    // Support common audio formats: MP3, OGG, FLAC, WAV, M4A, AAC, WMA
                    var filesInDir = Directory.GetFiles(item, "*.mp3")
                        .Concat(Directory.GetFiles(item, "*.ogg"))
                        .Concat(Directory.GetFiles(item, "*.flac"))
                        .Concat(Directory.GetFiles(item, "*.wav"))
                        .Concat(Directory.GetFiles(item, "*.m4a"))
                        .Concat(Directory.GetFiles(item, "*.aac"))
                        .Concat(Directory.GetFiles(item, "*.wma"))
                        .OrderBy(n => n).ToArray();
                    string[] sourceFiles = filesInDir;

                    try
                    {
                        var fileTuples = filesInDir.Select(f => new Tuple<string, Id3Tag>(f, new Mp3(f, Mp3Permissions.Read).GetAllTags().FirstOrDefault()));

                        sourceFiles = fileTuples.Where(t => t.Item2 != null).OrderBy(m => m.Item2.Track.Value).Select(t => t.Item1).ToArray();

                        if (sourceFiles.Length < filesInDir.Length)
                        {
                            sourceFiles = filesInDir;
                        }
                    }
                    catch (Exception)
                    {
                        // Failed to sort by MP3 tags - already sorted by filename
                    }

                    FileList.AddRange(sourceFiles);
                }
                else if (File.Exists(item))
                {
                    string lower = item.ToLower();
                    bool isSupported = lower.EndsWith(".mp3") || lower.EndsWith(".ogg") ||
                                      lower.EndsWith(".flac") || lower.EndsWith(".wav") ||
                                      lower.EndsWith(".m4a") || lower.EndsWith(".aac") ||
                                      lower.EndsWith(".wma");

                    if (!isSupported)
                    {
                        throw new InvalidDataException("Specified item '" + item + "' is not a supported audio format");
                    }
                    FileList.Add(item);
                }
                else
                {
                    throw new FileNotFoundException("Specified item '" + item + "' not found or supported");
                }
            }
        }

        public void ReadFile(string fileName, bool readAudio = true)
        {
            Filename = fileName;
            var file = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (file.Length < 0x2000)
            {
                throw new InvalidDataException();
            }

            long len = file.Length;
            if (!readAudio)
            {
                len = 4096;
            }
            byte[] buffer = new byte[len];

            if (file.Read(buffer, 0, buffer.Length) != buffer.Length)
            {
                throw new InvalidDataException();
            }
            FileContent = buffer;

            file.Close();
            ParseBuffer();
            CalculateStatistics(out _, out _, out _, out _, out _, out _, out _);
        }

        private void BuildFromFiles(List<string> sourceFiles, uint audioId, int bitRate, bool useVbr, string prefixLocation, EncodeCallback cbr)
        {
            GenerateAudio(sourceFiles, audioId, bitRate, useVbr, prefixLocation, cbr);
            FileContent = new byte[Audio.Length + 0x1000];
            Array.Copy(Audio, 0, FileContent, 0x1000, Audio.Length);
            WriteHeader();
        }

        private void BuildFromTrackSources(TrackSource[] trackSources, byte[] originalAudioData, uint audioId, int bitRate, bool useVbr, string prefixLocation, EncodeCallback cbr)
        {
            // Check if we have any pre-encoded tracks
            bool hasPreEncoded = trackSources.Any(t => t.IsPreEncoded);

            if (hasPreEncoded)
            {
                // Use manual Ogg stream building when we have pre-encoded tracks
                GenerateAudioFromTrackSourcesManual(trackSources, originalAudioData, audioId, bitRate, useVbr, prefixLocation, cbr);
            }
            else
            {
                // All tracks are file paths - use the normal OpusOggWriteStream approach
                var filePaths = trackSources.Select(t => t.FilePath).ToList();
                GenerateAudio(filePaths, audioId, bitRate, useVbr, prefixLocation, cbr);
            }

            FileContent = new byte[Audio.Length + 0x1000];
            Array.Copy(Audio, 0, FileContent, 0x1000, Audio.Length);
            WriteHeader();
        }

        private void WriteHeader()
        {
            int expectedSize = 0x1000 - 4;

            /* set protobuf header size */
            FileContent[0] = (byte)(expectedSize >> 24);
            FileContent[1] = (byte)(expectedSize >> 16);
            FileContent[2] = (byte)(expectedSize >> 8);
            FileContent[3] = (byte)(expectedSize >> 0);

            /* first use one byte padding */
            Header.Padding = new byte[1];

            var coder = new ProtoCoder();
            byte[] dataPre = coder.Serialize(Header);

            /* then determine how many extra bytes to fill */
            long padding = expectedSize - dataPre.Length;
            Header.Padding = new byte[padding];

            byte[] data = coder.Serialize(Header);

            Array.Copy(data, 0, FileContent, 4, data.Length);
        }

        /// <summary>
        /// Generates audio from track sources by building the Ogg stream manually.
        /// This is used when we have pre-encoded tracks to avoid quality loss.
        /// New tracks are encoded to temp files first, then their pages are extracted.
        /// </summary>
        private void GenerateAudioFromTrackSourcesManual(TrackSource[] trackSources, byte[] originalAudioData, uint audioId, int bitRate, bool useVbr, string prefixLocation = null, EncodeCallback cbr = null)
        {
            if(cbr == null)
            {
                cbr = new EncodeCallback();
            }

            if (audioId == 0)
            {
                audioId = (uint)((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
            }

            // First pass: encode any non-pre-encoded tracks to temp TonieAudio files
            // This way we can treat everything uniformly as pre-encoded data
            List<byte[]> allTrackData = new List<byte[]>();
            int track = 0;

            foreach (var trackSource in trackSources)
            {
                track++;
                cbr.FileStart(track, trackSource.IsPreEncoded ? $"[Pre-encoded track {track}]" : trackSource.FilePath);

                if (trackSource.IsPreEncoded)
                {
                    // Already have the data
                    allTrackData.Add(trackSource.PreEncodedOggData);
                }
                else
                {
                    // Encode this single track to a temporary TonieAudio
                    var tempTonie = new TonieAudio(new[] { trackSource.FilePath }, audioId, bitRate, useVbr, prefixLocation, null);

                    // Extract data pages (skip headers OpusHead and OpusTags)
                    int headerOffset = 0;
                    tempTonie.GetOggHeaders(ref headerOffset);

                    // Data pages start after headers
                    int dataLength = tempTonie.Audio.Length - headerOffset;
                    byte[] dataPages = new byte[dataLength];
                    Array.Copy(tempTonie.Audio, headerOffset, dataPages, 0, dataLength);
                    allTrackData.Add(dataPages);
                }

                cbr.FileDone();
            }

            // Now build the combined stream from all track data
            string tempName = Path.GetTempFileName();
            List<uint> chapters = new List<uint>();

            using (FileStream outputData = new FileStream(tempName, FileMode.Create, FileAccess.ReadWrite))
            {
                // Get headers from the original audio data or first track
                // If we have originalAudioData, use it (contains proper OpusHead/OpusTags headers)
                // Otherwise use allTrackData[0] (for all-new-tracks scenario where it has complete structure)
                int hdrOffset = 0;
                var tempAudio = new TonieAudio();
                if (originalAudioData != null && originalAudioData.Length > 0)
                {
                    // Use original audio data which has proper headers
                    tempAudio.Audio = originalAudioData;
                }
                else
                {
                    // Fall back to first track data (all-new-tracks case)
                    tempAudio.Audio = allTrackData[0];
                }
                OggPage[] headers = tempAudio.GetOggHeaders(ref hdrOffset);

                // Write all headers back-to-back
                foreach (var header in headers)
                {
                    header.Write(outputData);
                }

                // MUST pad to exactly 0x200 (512 bytes) for Tonie format
                // First data page MUST start at 0x200 (file offset 0x1200)
                long posAfterHeaders = outputData.Position;
                if (posAfterHeaders != 0x200)
                {
                    if (posAfterHeaders < 0x200)
                    {
                        // Pad if headers are smaller than 512 bytes
                        byte[] headerPadding = new byte[0x200 - posAfterHeaders];
                        outputData.Write(headerPadding, 0, headerPadding.Length);
                    }
                    else
                    {
                        // Error: headers are too large!
                        Console.WriteLine($"[ERROR] Headers are {posAfterHeaders} bytes, expected 512 or less");
                        // Truncate to 0x200 - this is bad but might work
                        outputData.SetLength(0x200);
                        outputData.Seek(0x200, SeekOrigin.Begin);
                    }
                }

                // Track page numbering and granules
                uint pageSeq = 2; // Start after headers
                ulong cumulativeGranule = 0;

                // Write each track's data
                for (int i = 0; i < allTrackData.Count; i++)
                {
                    // Add chapter marker BEFORE writing track data
                    // For first track, use 0 (convention); for others use current page sequence
                    chapters.Add(i == 0 ? 0u : pageSeq);

                    var (newPageSeq, newGranule) = CopyPreEncodedOggData(
                        allTrackData[i],
                        null, // Don't need OpusOggWriteStream anymore
                        outputData,
                        pageSeq,
                        cumulativeGranule,
                        audioId,
                        cbr);

                    pageSeq = newPageSeq;
                    cumulativeGranule = newGranule;
                }

                // Mark the last page with EOS (end-of-stream) flag
                // This is CRITICAL for VLC to know when audio ends
                SetEosOnLastPage(outputData);
            }

            cbr.PostProcessing("Finalizing audio stream...");
            Audio = File.ReadAllBytes(tempName);

            cbr.PostProcessing("Computing file hash...");
            using var prov = SHA1.Create();
            Header.Hash = prov.ComputeHash(Audio);
            Header.AudioChapters = chapters.ToArray();
            Header.AudioLength = Audio.Length;
            Header.AudioId = audioId;
            Header.Padding = new byte[0];

            File.Delete(tempName);
        }

        /// <summary>
        /// Sets the EOS (end-of-stream) flag on the last Ogg page in the stream.
        /// This tells decoders like VLC that the stream has ended.
        /// </summary>
        private void SetEosOnLastPage(Stream outputData)
        {
            // Find the last Ogg page by scanning backwards
            long streamLength = outputData.Length;
            if (streamLength < 0x200 + 27) // Headers + minimum page size
            {
                return; // Stream too short
            }

            // Read the last part of the file into a buffer (last 64KB should be enough)
            int bufferSize = (int)Math.Min(65536, streamLength - 0x200);
            byte[] buffer = new byte[bufferSize];
            outputData.Seek(streamLength - bufferSize, SeekOrigin.Begin);
            outputData.Read(buffer, 0, bufferSize);

            // Scan backwards for Ogg page signatures
            long lastPageOffset = -1;
            for (int i = bufferSize - 27; i >= 0; i--)
            {
                if (buffer[i] == 'O' && buffer[i + 1] == 'g' &&
                    buffer[i + 2] == 'g' && buffer[i + 3] == 'S')
                {
                    // Found an Ogg page
                    lastPageOffset = (streamLength - bufferSize) + i;
                    break;
                }
            }

            if (lastPageOffset < 0)
            {
                Console.WriteLine("[WARNING] Could not find last Ogg page to set EOS flag");
                return;
            }

            // Seek to the last page and parse it
            outputData.Seek(lastPageOffset, SeekOrigin.Begin);
            byte[] pageHeader = new byte[27];
            outputData.Read(pageHeader, 0, 27);

            OggPageHeader header = new OggPageHeader();
            header.Header = new byte[] { pageHeader[0], pageHeader[1], pageHeader[2], pageHeader[3] };
            header.Version = pageHeader[4];
            header.Type = pageHeader[5];
            header.GranulePosition = BitConverter.ToUInt64(pageHeader, 6);
            header.BitstreamSerialNumber = BitConverter.ToUInt32(pageHeader, 14);
            header.PageSequenceNumber = BitConverter.ToUInt32(pageHeader, 18);
            header.Checksum = BitConverter.ToUInt32(pageHeader, 22);
            header.PageSegments = pageHeader[26];

            // Read segment table and data
            byte[] segmentTable = new byte[header.PageSegments];
            outputData.Read(segmentTable, 0, header.PageSegments);

            int totalDataSize = 0;
            for (int i = 0; i < header.PageSegments; i++)
            {
                totalDataSize += segmentTable[i];
            }

            // Parse segments
            List<byte[]> segments = new List<byte[]>();
            int segIdx = 0;
            while (segIdx < header.PageSegments)
            {
                int segLen = 0;
                do
                {
                    segLen += segmentTable[segIdx];
                    segIdx++;
                } while (segIdx < header.PageSegments && segmentTable[segIdx - 1] == 0xFF);

                byte[] segment = new byte[segLen];
                outputData.Read(segment, 0, segLen);
                segments.Add(segment);
            }

            // Create OggPage and set EOS flag
            OggPage page = new OggPage
            {
                Header = header,
                Segments = segments.ToArray()
            };

            // Set EOS flag (Type = 4)
            page.Header.Type = 4;

            // Write the modified page back
            outputData.Seek(lastPageOffset, SeekOrigin.Begin);
            page.Write(outputData);
        }

        /// <summary>
        /// Copies pre-encoded Ogg data by parsing pages and renumbering them.
        /// This avoids re-encoding while maintaining proper stream continuity.
        /// Pads pages to 4k boundaries as required by Tonie format.
        /// Returns the new page index and the ending granule position.
        /// </summary>
        private (uint pageIndex, ulong endGranule) CopyPreEncodedOggData(byte[] preEncodedData, OpusOggWriteStream oggOut, Stream outputData, uint currentPageSeq, ulong cumulativeGranule, uint streamSerial, EncodeCallback cbr)
        {
            // Parse Ogg pages from the pre-encoded data
            List<OggPage> pages = ParseOggPagesFromBytes(preEncodedData);

            if (pages.Count == 0)
            {
                cbr.Warning("No Ogg pages found in pre-encoded data");
                return (currentPageSeq, cumulativeGranule);
            }

            // Find the first and last valid granule positions in the pre-encoded data
            // firstGranule = the minimum granule (this is the track's starting position)
            // lastGranule = the maximum granule (this is the track's ending position)
            ulong firstGranule = ulong.MaxValue;
            ulong lastGranule = 0;

            for (int i = 0; i < pages.Count; i++)
            {
                // Skip header pages and continuation pages
                // Granule = ulong.MaxValue means continuation page without timestamp
                // Granule = 0 is VALID (means first audio sample) and should be included
                if (pages[i].Header.PageSequenceNumber >= 2 &&
                    pages[i].Header.GranulePosition != ulong.MaxValue)
                {
                    ulong granule = pages[i].Header.GranulePosition;

                    // Track minimum and maximum granules
                    if (granule < firstGranule)
                    {
                        firstGranule = granule;
                    }
                    if (granule > lastGranule)
                    {
                        lastGranule = granule;
                    }
                }
            }

            // Calculate the duration of this track in granules (relative duration)
            ulong trackDurationGranules = 0;
            if (firstGranule != ulong.MaxValue && lastGranule >= firstGranule)
            {
                trackDurationGranules = lastGranule - firstGranule;
            }

            // Write each page with renumbered sequence and updated stream ID
            int pagesWritten = 0;
            int pagesSkipped = 0;
            foreach (var page in pages)
            {
                // Skip header pages (OpusHead, OpusTags) - they're already in the stream
                if (page.Header.PageSequenceNumber < 2)
                {
                    pagesSkipped++;
                    continue;
                }

                long posBeforeWrite = outputData.Position;

                // Update the page header
                page.Header.BitstreamSerialNumber = streamSerial;
                page.Header.PageSequenceNumber = currentPageSeq++;

                // Clear BOS and EOS flags from copied pages
                // We'll set EOS only on the final page of the entire stream
                // Preserve bit 0 (continuation flag), clear bits 1 (BOS) and 2 (EOS)
                page.Header.Type = (byte)(page.Header.Type & 1);

                // Adjust granule position for continuity
                // Subtract the track's first granule (make relative to 0), then add cumulative offset
                // Only skip continuation pages (granule = ulong.MaxValue)
                // Granule = 0 is VALID (first audio sample) and should be adjusted
                if (page.Header.GranulePosition != ulong.MaxValue && firstGranule != ulong.MaxValue)
                {
                    if (page.Header.GranulePosition >= firstGranule)
                    {
                        page.Header.GranulePosition = (page.Header.GranulePosition - firstGranule) + cumulativeGranule;
                    }
                }

                // Calculate page size before writing
                int pageSize;
                using (var tempStream = new MemoryStream())
                {
                    page.Write(tempStream);
                    pageSize = (int)tempStream.Length;
                }

                // Tonie format requirement: pages must END at 4k boundaries
                // Calculate how much padding is needed WITHIN the page to reach the boundary
                long currentPos = posBeforeWrite;
                long posAfterPage = currentPos + pageSize;
                long nextBoundary = ((posAfterPage + 0xFFF) / 0x1000) * 0x1000;
                long spaceToFill = nextBoundary - posAfterPage;

                if (spaceToFill > 0)
                {
                    // Account for segment table entries
                    // IMPORTANT: Use 254 bytes per segment to avoid the 255-byte special case
                    // (255-byte segments need 2 table entries: 0xFF + 0x00)
                    // Each 254-byte segment needs exactly 1 byte in the segment table
                    // We need to solve: paddingData + ceil(paddingData / 254) = spaceToFill
                    long paddingData = (spaceToFill * 254) / 255;
                    int segmentEntries = (int)((paddingData + 253) / 254);  // ceil(paddingData / 254)

                    // Adjust if our approximation is off
                    while (paddingData + segmentEntries < spaceToFill)
                    {
                        paddingData++;
                        segmentEntries = (int)((paddingData + 253) / 254);
                    }
                    while (paddingData + segmentEntries > spaceToFill)
                    {
                        paddingData--;
                        segmentEntries = (int)((paddingData + 253) / 254);
                    }

                    if (paddingData > 0)
                    {
                        // Create new segments array with padding (max 254 bytes per segment)
                        byte[][] newSegments = new byte[page.Segments.Length + segmentEntries][];
                        Array.Copy(page.Segments, newSegments, page.Segments.Length);

                        int segIdx = page.Segments.Length;
                        long remaining = paddingData;
                        while (remaining > 0)
                        {
                            int segSize = (int)Math.Min(254, remaining);  // Max 254 to avoid 255 special case
                            newSegments[segIdx++] = new byte[segSize];
                            remaining -= segSize;
                        }
                        page.Segments = newSegments;
                    }
                }

                // Write the page (now with padding included)
                page.Write(outputData);
                pagesWritten++;
            }

            // Return the new page index and the ending cumulative granule position
            ulong newCumulativeGranule = cumulativeGranule + trackDurationGranules;
            return (currentPageSeq, newCumulativeGranule);
        }

        /// <summary>
        /// Parses Ogg pages from a byte array.
        /// </summary>
        private List<OggPage> ParseOggPagesFromBytes(byte[] data)
        {
            List<OggPage> pages = new List<OggPage>();
            int offset = 0;

            while (offset < data.Length - 27) // Minimum Ogg page header size
            {
                // Check for "OggS" signature
                if (data[offset] != 'O' || data[offset + 1] != 'g' ||
                    data[offset + 2] != 'g' || data[offset + 3] != 'S')
                {
                    offset++;
                    continue;
                }

                try
                {
                    // Parse header
                    OggPageHeader header = new OggPageHeader();
                    header.Header = new byte[] { data[offset], data[offset + 1], data[offset + 2], data[offset + 3] };
                    header.Version = data[offset + 4];
                    header.Type = data[offset + 5];
                    header.GranulePosition = BitConverter.ToUInt64(data, offset + 6);
                    header.BitstreamSerialNumber = BitConverter.ToUInt32(data, offset + 14);
                    header.PageSequenceNumber = BitConverter.ToUInt32(data, offset + 18);
                    header.Checksum = BitConverter.ToUInt32(data, offset + 22);
                    header.PageSegments = data[offset + 26];

                    int headerSize = 27;
                    int segmentTableOffset = offset + headerSize;

                    if (segmentTableOffset + header.PageSegments > data.Length)
                    {
                        break;
                    }

                    // Parse segment table
                    byte[] segmentTable = new byte[header.PageSegments];
                    Array.Copy(data, segmentTableOffset, segmentTable, 0, header.PageSegments);

                    // Calculate total data size
                    int totalDataSize = 0;
                    for (int i = 0; i < header.PageSegments; i++)
                    {
                        totalDataSize += segmentTable[i];
                    }

                    int dataOffset = segmentTableOffset + header.PageSegments;
                    if (dataOffset + totalDataSize > data.Length)
                    {
                        break;
                    }

                    // Parse segments by interpreting segment table
                    List<byte[]> segments = new List<byte[]>();
                    int dataPos = dataOffset;
                    int segIdx = 0;

                    while (segIdx < header.PageSegments)
                    {
                        // Combine segments that span multiple entries
                        int segLen = 0;
                        do
                        {
                            segLen += segmentTable[segIdx];
                            segIdx++;
                        } while (segIdx < header.PageSegments && segmentTable[segIdx - 1] == 0xFF);

                        if (segLen > 0 && dataPos + segLen <= data.Length)
                        {
                            byte[] segment = new byte[segLen];
                            Array.Copy(data, dataPos, segment, 0, segLen);
                            segments.Add(segment);
                            dataPos += segLen;
                        }
                    }

                    // Create OggPage
                    OggPage page = new OggPage
                    {
                        Header = header,
                        Segments = segments.ToArray()
                    };

                    pages.Add(page);
                    offset = dataPos;
                }
                catch (Exception)
                {
                    offset++;
                }
            }

            return pages;
        }

        /// <summary>
        /// Encodes a single track from a file (original encoding logic extracted).
        /// </summary>
        private uint EncodeTrackFromFile(string sourceFile, OpusOggWriteStream oggOut, byte[] buffer, int channels, WaveFormat outFormat, string prefixLocation, int track, uint lastIndex, long maxSize, Stream outputData, EncodeCallback cbr)
        {
            int bytesReturned = 1;
            int lastPct = 0;

            /* prepend a audio file for e.g. chapter number */
            if (prefixLocation != null)
            {
                string prefixFile = Path.Combine(prefixLocation, track.ToString("0000") + ".mp3");

                if (!File.Exists(prefixFile))
                {
                    throw new FileNotFoundException("Missing prefix file '" + prefixFile + "'");
                }

                try
                {
                    var prefixStream = new CrossPlatformAudioReader(prefixFile);
                    var prefixResampled = new CrossPlatformResampler(prefixStream, outFormat);

                    while (true)
                    {
                        bytesReturned = prefixResampled.Read(buffer, 0, buffer.Length);

                        if (bytesReturned <= 0)
                        {
                            break;
                        }

                        bool isEmpty = (buffer.Where(v => v != 0).Count() == 0);
                        if (!isEmpty)
                        {
                            float[] sampleBuffer = ConvertToFloat(buffer, bytesReturned, channels);

                            if ((outputData.Length + 0x1000 + sampleBuffer.Length) >= maxSize)
                            {
                                break;
                            }
                            oggOut.WriteSamples(sampleBuffer, 0, sampleBuffer.Length);
                        }
                        lastIndex = (uint)oggOut.PageCounter;
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Failed processing prefix file '" + prefixFile + "'");
                }
            }

            /* then the real audio file */
            string type = sourceFile.Split('.').Last().ToLower();
            WaveStream stream = null;

            switch (type)
            {
                case "ogg":
                    // Use OpusWaveStream for Ogg files (more efficient for Opus-encoded files)
                    stream = new OpusWaveStream(File.Open(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), 48000, channels);
                    break;

                case "mp3":
                case "flac":
                case "wav":
                case "m4a":
                case "aac":
                case "wma":
                    // Use CrossPlatformAudioReader for all other formats
                    stream = new CrossPlatformAudioReader(sourceFile);
                    break;

                default:
                    cbr.FileFailed("Unsupported file type: " + type);
                    return lastIndex;
            }

            if(stream == null)
            {
                cbr.FileFailed("Unknown file type");
                return lastIndex;
            }

            var streamResampled = new CrossPlatformResampler(stream, outFormat);

            while (true)
            {
                bytesReturned = streamResampled.Read(buffer, 0, buffer.Length);

                if (bytesReturned <= 0)
                {
                    break;
                }

                decimal progress = (decimal)stream.Position / stream.Length;

                if ((int)(progress * 20) != lastPct)
                {
                    lastPct = (int)(progress * 20);
                    cbr.Progress(progress);
                }

                bool isEmpty = (buffer.Where(v => v != 0).Count() == 0);
                if (!isEmpty)
                {
                    float[] sampleBuffer = ConvertToFloat(buffer, bytesReturned, channels);

                    oggOut.WriteSamples(sampleBuffer, 0, sampleBuffer.Length);
                }
                lastIndex = (uint)oggOut.PageCounter;
            }
            stream.Close();

            return lastIndex;
        }

        private void GenerateAudio(List<string> sourceFiles, uint audioId, int bitRate, bool useVbr, string prefixLocation = null, EncodeCallback cbr = null)
        {
            int channels = 2;
            int samplingRate = 48000;
            List<uint> chapters = new List<uint>();

            var outFormat = new WaveFormat(samplingRate, 2);

            if(cbr == null)
            {
                cbr = new EncodeCallback();
            }

            OpusEncoder encoder = OpusEncoder.Create(48000, 2, OpusApplication.OPUS_APPLICATION_AUDIO);
            encoder.Bitrate = bitRate;
            encoder.UseVBR = useVbr;

            if (audioId == 0)
            {
                audioId = (uint)((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
            }

            string tempName = Path.GetTempFileName();

            using (Stream outputData = new FileStream(tempName, FileMode.OpenOrCreate))
            {
                byte[] buffer = new byte[2880 * channels * 2];
                OpusTags tags = new OpusTags();
                tags.Comment = "Lavf56.40.101";
                tags.Fields["encoder"] = "opusenc from opus-tools 0.1.9";
                tags.Fields["encoder_options"] = "--quiet --bitrate 96 --vbr";
                tags.Fields["pad"] = new string('0', 0x138);

                OpusOggWriteStream oggOut = new OpusOggWriteStream(encoder, outputData, tags, samplingRate, (int)audioId);

                uint lastIndex = 0;
                int track = 0;
                bool warned = false;
                long maxSize = 0x77359400;

                foreach (var sourceFile in sourceFiles)
                {
                    if ((outputData.Length + 0x1000) >= maxSize)
                    {
                        cbr.Warning("Close to 2 GiB, stopping");
                        break;
                    }

                    try
                    {
                        int bytesReturned = 1;
                        int totalBytesRead = 0;

                        track++;
                        chapters.Add(lastIndex);

                        int lastPct = 0;
                        cbr.FileStart(track, sourceFile);


                        /* prepend a audio file for e.g. chapter number */
                        if (prefixLocation != null)
                        {
                            string prefixFile = Path.Combine(prefixLocation, track.ToString("0000") + ".mp3");

                            if (!File.Exists(prefixFile))
                            {
                                throw new FileNotFoundException("Missing prefix file '" + prefixFile + "'");
                            }

                            try
                            {
                                var prefixStream = new CrossPlatformAudioReader(prefixFile);
                                var prefixResampled = new CrossPlatformResampler(prefixStream, outFormat);

                                while (true)
                                {
                                    bytesReturned = prefixResampled.Read(buffer, 0, buffer.Length);

                                    if (bytesReturned <= 0)
                                    {
                                        break;
                                    }

                                    bool isEmpty = (buffer.Where(v => v != 0).Count() == 0);
                                    if (!isEmpty)
                                    {
                                        float[] sampleBuffer = ConvertToFloat(buffer, bytesReturned, channels);

                                        if ((outputData.Length + 0x1000 + sampleBuffer.Length) >= maxSize)
                                        {
                                            break;
                                        }
                                        oggOut.WriteSamples(sampleBuffer, 0, sampleBuffer.Length);
                                    }
                                    lastIndex = (uint)oggOut.PageCounter;
                                }
                            }
                            catch (Exception ex)
                            {
                                throw new Exception("Failed processing prefix file '" + prefixFile + "'");
                            }
                        }

                        /* then the real audio file */
                        string type = sourceFile.Split('.').Last().ToLower();
                        WaveStream stream = null;

                        switch (type)
                        {
                            case "ogg":
                                // Use OpusWaveStream for Ogg files (more efficient for Opus-encoded files)
                                stream = new OpusWaveStream(File.Open(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), samplingRate, channels);
                                break;

                            case "mp3":
                            case "flac":
                            case "wav":
                            case "m4a":
                            case "aac":
                            case "wma":
                                // Use CrossPlatformAudioReader for all other formats
                                stream = new CrossPlatformAudioReader(sourceFile);
                                break;

                            default:
                                cbr.FileFailed("Unsupported file type: " + type);
                                continue;
                        }

                        if(stream == null)
                        {
                            cbr.FileFailed("Unknown file type");
                            continue;
                        }

                        var streamResampled = new CrossPlatformResampler(stream, outFormat);

                        while (true)
                        {
                            bytesReturned = streamResampled.Read(buffer, 0, buffer.Length);

                            if (bytesReturned <= 0)
                            {
                                break;
                            }
                            totalBytesRead += bytesReturned;

                            decimal progress = (decimal)stream.Position / stream.Length;

                            if ((int)(progress * 20) != lastPct)
                            {
                                lastPct = (int)(progress * 20);
                                cbr.Progress(progress);
                            }

                            bool isEmpty = (buffer.Where(v => v != 0).Count() == 0);
                            if (!isEmpty)
                            {
                                float[] sampleBuffer = ConvertToFloat(buffer, bytesReturned, channels);

                                oggOut.WriteSamples(sampleBuffer, 0, sampleBuffer.Length);
                            }
                            lastIndex = (uint)oggOut.PageCounter;
                        }
                        stream.Close();

                        cbr.FileDone();
                    }
                    catch (OpusOggWriteStream.PaddingException e)
                    {
                        string msg = "Failed to pad opus data properly. Please try CBR with bitrates a multiple of 24 kbps";
                        cbr.Failed(msg);
                        throw new EncodingException(msg);
                    }
                    catch (FileNotFoundException e)
                    {
                        cbr.Failed(e.Message);
                        throw new FileNotFoundException(e.Message);
                    }
                    catch (InvalidDataException e)
                    {
                        string msg = "Failed processing " + sourceFile;
                        cbr.Failed(msg);
                        throw new Exception(msg);
                    }
                    catch (Exception e)
                    {
                        string msg = "Failed processing " + sourceFile + ": " + e.Message;
                        cbr.Failed(msg);
                        throw new Exception(msg, e);
                    }

                    if (!warned && outputData.Length >= maxSize / 2)
                    {
                        cbr.Warning("Approaching 2 GiB, please reduce the bitrate");
                        warned = true;
                    }
                }

                oggOut.Finish();
                Header.AudioId = (uint) oggOut.LogicalStreamId;
            }

            cbr.PostProcessing("Finalizing audio stream...");
            Audio = File.ReadAllBytes(tempName);

            cbr.PostProcessing("Computing file hash...");
            using var prov = SHA1.Create();
            Header.Hash = prov.ComputeHash(Audio);
            Header.AudioChapters = chapters.ToArray();
            Header.AudioLength = Audio.Length;
            Header.Padding = new byte[0];

            File.Delete(tempName);
        }

        private static float ShortToSample(short pcmValue)
        {
            return pcmValue / 32768f;
        }

        private float[] ConvertToFloat(byte[] pcmBuffer, int bytes, int channels)
        {
            int bytesPerSample = 2 * channels;
            float[] samples = new float[channels * (pcmBuffer.Length / bytesPerSample)];

            for (int sample = 0; sample < bytes / bytesPerSample; sample++)
            {
                for (int chan = 0; chan < channels; chan++)
                {
                    samples[channels * sample + chan] = ShortToSample((short)(pcmBuffer[bytesPerSample * sample + 1 + chan * 2] << 8 | pcmBuffer[bytesPerSample * sample + 0 + chan * 2]));
                }
            }

            return samples;
        }

        private void ParseBuffer()
        {
            int protoBufLength = (FileContent[0] << 24) | (FileContent[1] << 16) | (FileContent[2] << 8) | FileContent[3];

            if (protoBufLength > 0x10000)
            {
                throw new InvalidDataException();
            }
            byte[] protoBuf = new byte[protoBufLength];
            int payloadStart = protoBufLength + 4;
            int payloadLength = FileContent.Length - payloadStart;
            byte[] payload = new byte[payloadLength];

            Array.Copy(FileContent, 4, protoBuf, 0, protoBufLength);
            Array.Copy(FileContent, protoBufLength + 4, payload, 0, payloadLength);

            var coder = new ProtoCoder();
            FileHeader header = coder.Deserialize<FileHeader>(protoBuf);

            using var prov = SHA1.Create();
            var hash = prov.ComputeHash(payload);

            HashCorrect = true;
            if (!hash.SequenceEqual(header.Hash))
            {
                HashCorrect = false;
            }

            byte[] data = coder.Serialize(header);

            HeaderLength = data.Length;
            Audio = payload;
            Header = header;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct OggPageHeader
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] Header;
            public byte Version;
            public byte Type;
            public ulong GranulePosition;
            public uint BitstreamSerialNumber;
            public uint PageSequenceNumber;
            public uint Checksum;
            public byte PageSegments;
        }

        private class OggPage
        {
            public OggPageHeader Header;
            public byte SegmentTableLength => (byte)SegmentTable.Length;
            public int TotalSegmentLengths => Segments.Sum(s => s.Length);
            public byte[] SegmentTable
            {
                get
                {
                    int lengthBytes = Segments.Length + Segments.Sum(s => s.Length / 0xFF);
                    byte[] table = new byte[lengthBytes];

                    int tableIndex = 0;
                    for (int pos = 0; pos < Segments.Length; pos++)
                    {
                        int len = Segments[pos].Length;

                        while (len >= 0xFF)
                        {
                            table[tableIndex++] = 0xFF;
                            len -= 0xFF;
                        }
                        table[tableIndex++] = (byte)len;
                    }
                    return table;
                }
            }
            public byte[][] Segments;
            public int Size;

            public class Crc
            {
                const uint CRC32_POLY = 0x04c11db7;
                static uint[] crcTable = new uint[256];

                static Crc()
                {
                    for (uint i = 0; i < 256; i++)
                    {
                        uint s = i << 24;
                        for (int j = 0; j < 8; ++j)
                        {
                            s = (s << 1) ^ (s >= (1U << 31) ? CRC32_POLY : 0);
                        }
                        crcTable[i] = s;
                    }
                }

                uint _crc;

                public Crc()
                {
                    Reset();
                }

                public void Reset()
                {
                    _crc = 0U;
                }

                public void Update(byte nextVal)
                {
                    _crc = (_crc << 8) ^ crcTable[nextVal ^ (_crc >> 24)];
                }

                public void Update(byte[] buf)
                {
                    foreach (byte val in buf)
                    {
                        Update(val);
                    }
                }

                public bool Test(uint checkCrc)
                {
                    return _crc == checkCrc;
                }

                public uint Value
                {
                    get
                    {
                        return _crc;
                    }
                }
            }

            public OggPage()
            {
            }

            public OggPage(OggPage src)
            {
                Header = src.Header;
                Size = src.Size;
                Segments = new byte[src.Segments.Length][];
                for (int pos = 0; pos < Segments.Length; pos++)
                {
                    Segments[pos] = new byte[src.Segments[pos].Length];
                    Array.Copy(src.Segments[pos], Segments[pos], Segments[pos].Length);
                }
            }

            public void Write(Stream outFile)
            {
                UpdateHeader();
                WriteInternal(outFile);
            }

            private void UpdateHeader()
            {
                MemoryStream memStream = new MemoryStream();

                Header.PageSegments = SegmentTableLength;
                Header.Checksum = 0;
                WriteInternal(memStream);

                OggPage.Crc crc = new OggPage.Crc();
                crc.Update(memStream.ToArray());

                Header.Checksum = crc.Value;
                Size = (int)memStream.Length;
            }

            private void WriteInternal(Stream stream)
            {
                byte[] data = StructureToByteArray(Header);

                stream.Write(data, 0, data.Length);
                stream.Write(SegmentTable, 0, SegmentTable.Length);

                foreach (var seg in Segments)
                {
                    stream.Write(seg, 0, seg.Length);
                }
            }
        }

        /// <summary>
        /// Extracts raw Ogg audio data for each chapter/track without decoding.
        /// Returns byte arrays containing the raw Ogg pages for each track.
        /// This allows reusing encoded data without quality loss.
        /// </summary>
        public List<byte[]> ExtractRawChapterData()
        {
            List<byte[]> chapters = new List<byte[]>();

            if (Header.AudioChapters.Length == 0)
            {
                // No chapters, return entire audio
                chapters.Add(Audio);
                return chapters;
            }

            // Parse Ogg stream to find actual byte positions of chapter markers
            // AudioChapters contains page sequence numbers, NOT byte offsets
            List<int> chapterOffsets = new List<int>();
            int offset = 0;

            while (offset < Audio.Length - 27)
            {
                // Check for Ogg page signature
                if (Audio[offset] != 'O' || Audio[offset + 1] != 'g' ||
                    Audio[offset + 2] != 'g' || Audio[offset + 3] != 'S')
                {
                    offset++;
                    continue;
                }

                // Read page sequence number
                uint pageSeq = BitConverter.ToUInt32(Audio, offset + 18);
                byte segmentCount = Audio[offset + 26];

                // Check if this is a chapter marker
                for (int i = 0; i < Header.AudioChapters.Length; i++)
                {
                    if (pageSeq == Header.AudioChapters[i] && chapterOffsets.Count == i)
                    {
                        chapterOffsets.Add(offset);
                        break;
                    }
                }

                // Calculate page size to advance offset
                int segmentTableSize = segmentCount;
                int dataSize = 0;
                for (int s = 0; s < segmentCount; s++)
                {
                    dataSize += Audio[offset + 27 + s];
                }

                offset += 27 + segmentTableSize + dataSize;

                // Stop if we found all chapters
                if (chapterOffsets.Count == Header.AudioChapters.Length)
                {
                    break;
                }
            }

            // Extract chapter data based on found offsets
            for (int chapter = 0; chapter < Header.AudioChapters.Length; chapter++)
            {
                int startOffset = chapterOffsets[chapter];
                int endOffset = Audio.Length;

                if (chapter + 1 < chapterOffsets.Count)
                {
                    endOffset = chapterOffsets[chapter + 1];
                }

                int length = endOffset - startOffset;
                if (length > 0 && startOffset < Audio.Length)
                {
                    byte[] chapterData = new byte[length];
                    Array.Copy(Audio, startOffset, chapterData, 0, length);
                    chapters.Add(chapterData);
                }
            }

            return chapters;
        }

        /// <summary>
        /// Extracts tracks to temporary Ogg files by splitting at exact chapter boundaries.
        /// This is simpler and more reliable than manual Ogg stream manipulation.
        /// Uses ffmpeg to split the Ogg stream at precise timestamps derived from granule positions.
        /// Returns paths to temporary Ogg files (caller is responsible for cleanup).
        /// </summary>
        public List<string> ExtractTracksToTempFiles(string tempDirectory = null)
        {
            if (tempDirectory == null)
            {
                tempDirectory = Path.GetTempPath();
            }

            List<string> trackFiles = new List<string>();

            // If no chapters, extract entire audio as single track
            if (Header.AudioChapters.Length == 0)
            {
                string singleTrackFile = Path.Combine(tempDirectory, $"track_0_{Guid.NewGuid()}.ogg");
                File.WriteAllBytes(singleTrackFile, Audio);
                trackFiles.Add(singleTrackFile);
                return trackFiles;
            }

            // Get precise timestamps for each chapter
            ulong[] granulePositions = ParsePositions();

            // Convert granules to seconds (48000 granules per second)
            double[] timestamps = granulePositions.Select(g => g / 48000.0).ToArray();

            // Write full Ogg to temp file (this is the "dd bs=4096 skip=1" equivalent)
            string fullOggFile = Path.Combine(tempDirectory, $"full_audio_{Guid.NewGuid()}.ogg");
            File.WriteAllBytes(fullOggFile, Audio);

            try
            {
                // Split the Ogg at each chapter boundary using ffmpeg
                // ParsePositions returns: [start, chapter1_start, chapter2_start, ..., end]
                // For N chapters, we expect N+1 or N+2 positions (start, chapters, potentially end)

                // Determine actual chapter count based on header
                int chapterCount = Header.AudioChapters.Length;

                for (int i = 0; i < chapterCount; i++)
                {
                    string trackFile = Path.Combine(tempDirectory, $"track_{i}_{Guid.NewGuid()}.ogg");

                    // Start time is the chapter's position
                    // For first chapter (i=0), if AudioChapters[0]=0, then timestamps might have duplicates
                    // We need to find the actual start of this chapter in the timestamps array

                    // The timestamps array from ParsePositions includes:
                    // - Position 0 (always 0)
                    // - One position for each AudioChapters entry
                    // - Final position (end of audio)

                    // So for chapter i, we want:
                    // - Start: timestamps[i+1] (because timestamps[0] is always 0, then AudioChapters positions)
                    // - End: timestamps[i+2] or end of file

                    double startTime = timestamps[i + 1];
                    double endTime = (i + 2 < timestamps.Length) ? timestamps[i + 2] : timestamps[timestamps.Length - 1];

                    // Use ffmpeg to extract this segment
                    // -ss: start time, -to: end time
                    // -c copy: copy codec (no re-encoding at this stage, just container manipulation)
                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-i \"{fullOggFile}\" -ss {startTime:F6} -to {endTime:F6} -c copy -y \"{trackFile}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (var process = System.Diagnostics.Process.Start(processInfo))
                    {
                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            string error = process.StandardError.ReadToEnd();
                            throw new Exception($"ffmpeg failed to extract track {i}: {error}");
                        }
                    }

                    if (File.Exists(trackFile) && new FileInfo(trackFile).Length > 0)
                    {
                        trackFiles.Add(trackFile);
                    }
                    else
                    {
                        throw new Exception($"Failed to extract track {i}: output file is empty or doesn't exist");
                    }
                }
            }
            finally
            {
                // Clean up the full Ogg temp file
                if (File.Exists(fullOggFile))
                {
                    File.Delete(fullOggFile);
                }
            }

            return trackFiles;
        }

        /// <summary>
        /// Writes a raw chapter data to a proper Ogg file with correct headers.
        /// This creates a valid standalone Ogg file from raw chapter pages.
        /// </summary>
        public void WriteChapterToFile(byte[] chapterData, string outputPath, int chapterIndex)
        {
            int hdrOffset = 0;
            OggPage[] metaPages = GetOggHeaders(ref hdrOffset);

            using (FileStream outFile = File.Open(outputPath, FileMode.Create, FileAccess.Write))
            {
                // Write Ogg headers (OpusHead and OpusTags)
                foreach (OggPage page in metaPages)
                {
                    page.Write(outFile);
                }

                // Write the chapter data
                outFile.Write(chapterData, 0, chapterData.Length);
            }
        }

        public void DumpAudioFiles(string outDirectory, string outFileName, bool singleOgg, string[] tags, string[] titles)
        {
            int hdrOffset = 0;
            OggPage[] metaPages = GetOggHeaders(ref hdrOffset);
            AddTags(metaPages, tags);

            if (singleOgg)
            {
                string outFile = Path.Combine(outDirectory, outFileName);

                File.WriteAllBytes(outFile + ".ogg", Audio);
                //File.WriteAllText(outFile + ".cue", BuildCueSheet(tonie), Encoding.UTF8);
            }
            else
            {
                for (int chapter = 0; chapter < Header.AudioChapters.Length; chapter++)
                {
                    int trackNum = chapter + 1;
                    string fileName = Path.Combine(outDirectory, outFileName + " - Track #" + trackNum.ToString("00") + ".ogg");
                    FileStream outFile = File.Open(fileName, FileMode.Create, FileAccess.Write);
                    OggPage[] metaPagesTrack = metaPages.Select(p => new OggPage(p)).ToArray();

                    if (titles != null && chapter < titles.Length)
                    {
                        string[] trackTags = new[] { "TITLE=" + titles[chapter] };
                        AddTags(metaPagesTrack, trackTags);
                    }

                    foreach (OggPage page in metaPagesTrack)
                    {
                        page.Write(outFile);
                    }

                    int offset = Math.Max(0, (int)(0x1000 * (Header.AudioChapters[chapter] - 2)));
                    int endOffset = int.MaxValue;

                    if (chapter + 1 < Header.AudioChapters.Length)
                    {
                        endOffset = Math.Max(0, (int)(0x1000 * (Header.AudioChapters[chapter + 1] - 2)));
                    }

                    bool done = false;
                    ulong granuleStart = ulong.MaxValue;
                    uint pageStart = uint.MaxValue;
                    while (!done)
                    {
                        OggPage page = GetOggPage(ref offset);

                        if (page == null)
                        {
                            break;
                        }

                        /* reached the end of this chapter? */
                        if (offset >= endOffset || offset >= Audio.Length)
                        {
                            /* set EOS flag */
                            page.Header.Type = 4;
                            done = true;
                        }

                        /* do not write meta headers again. only applies to first chapter */
                        if (!IsMeta(page))
                        {
                            /* set granule position relative to chapter start */
                            if (granuleStart == ulong.MaxValue)
                            {
                                granuleStart = page.Header.GranulePosition;
                                pageStart = page.Header.PageSequenceNumber;
                            }

                            page.Header.GranulePosition -= granuleStart;
                            page.Header.PageSequenceNumber -= pageStart;
                            page.Header.PageSequenceNumber += 2;


                            page.Write(outFile);
                        }
                    }

                    outFile.Close();
                }
            }
        }


        private static byte[] StructureToByteArray(object obj)
        {
            int len = Marshal.SizeOf(obj);
            byte[] arr = new byte[len];
            IntPtr ptr = Marshal.AllocHGlobal(len);
            Marshal.StructureToPtr(obj, ptr, true);
            Marshal.Copy(ptr, arr, 0, len);
            Marshal.FreeHGlobal(ptr);

            return arr;
        }

        private static void ByteArrayToStructure<T>(byte[] bytearray, int offset, ref T obj)
        {
            int len = Marshal.SizeOf(obj);
            IntPtr i = Marshal.AllocHGlobal(len);
            Marshal.Copy(bytearray, offset, i, len);
            obj = (T)Marshal.PtrToStructure(i, typeof(T));
            Marshal.FreeHGlobal(i);
        }

        private void AddTags(OggPage[] pages, string[] tags)
        {
            foreach(OggPage header in pages)
            {
                if (IsMetaType(header, "OpusTags"))
                {
                    uint entryPos = 8 + 4 + GetUint(header.Segments[0], 8);
                    foreach (string tag in tags)
                    {
                        byte[] tagBytes = Encoding.UTF8.GetBytes(tag);
                        byte[] append = new byte[4 + tagBytes.Length];

                        WriteUint(append, 0, (uint)tagBytes.Length);

                        Array.Copy(tagBytes, 0, append, 4, tagBytes.Length);
                        Array.Resize(ref header.Segments[0], header.Segments[0].Length + append.Length);

                        Array.Copy(append, 0, header.Segments[0], header.Segments[0].Length - append.Length, append.Length);

                        WriteUint(header.Segments[0], entryPos, GetUint(header.Segments[0], entryPos) + 1);
                    }
                }
            }
        }

        private OggPage[] GetOggHeaders(ref int offset)
        {
            List<OggPage> headers = new List<OggPage>();
            bool done = false;

            while (!done)
            {
                int curOffset = offset;
                OggPage header = GetOggPage(ref curOffset);

                if (header.Segments.Length < 1)
                {
                    done = true;
                }

                if (IsMeta(header))
                {
                    headers.Add(header);
                    offset = curOffset;
                }
                else
                {
                    done = true;
                }
            }

            return headers.ToArray();
        }

        private static void WriteUint(byte[] buf, uint pos, uint value)
        {
            buf[pos + 0] = (byte)value;
            buf[pos + 1] = (byte)(value >> 8);
            buf[pos + 2] = (byte)(value >> 16);
            buf[pos + 3] = (byte)(value >> 24);
        }

        private static uint GetUint(byte[] buf, uint pos)
        {
            return (uint)buf[pos] | ((uint)buf[pos + 1] << 8) | ((uint)buf[pos + 2] << 16) | ((uint)buf[pos + 3] << 24);
        }

        private static bool IsMetaType(OggPage header, string type)
        {
            return Encoding.UTF8.GetString(header.Segments[0], 0, 8) == type;
        }

        private static bool IsMeta(OggPage header)
        {
            switch (Encoding.UTF8.GetString(header.Segments[0], 0, 8))
            {
                case "OpusHead":
                case "OpusTags":
                    return true;
                default:
                    return false;
            }
        }

        private OggPage GetOggPage(ref int offset)
        {
            OggPageHeader hdr = new OggPageHeader();
            OggPage page = new OggPage();
            int pageSize = 0;
            ByteArrayToStructure(Audio, offset, ref hdr);

            if (hdr.Header[0] != 'O' || hdr.Header[1] != 'g' || hdr.Header[2] != 'g' || hdr.Header[3] != 'S')
            {
                return null;
            }

            page.Header = hdr;
            pageSize += Marshal.SizeOf(hdr);

            page.Segments = new byte[0][];

            /* where will the segment data start */
            int segmentDataPos = pageSize + hdr.PageSegments;
            /* position in page segment table */
            int pageSegTablePos = 0;
            /* logical number of the segment */
            int pageSegNum = 0;
            while (pageSegTablePos < hdr.PageSegments)
            {
                int lenEntry = Audio[offset + pageSize];
                int len = lenEntry;
                pageSize++;
                pageSegTablePos++;

                while (lenEntry == 0xFF)
                {
                    lenEntry = Audio[offset + pageSize];
                    len += lenEntry;
                    pageSize++;
                    pageSegTablePos++;
                }
                Array.Resize(ref page.Segments, pageSegNum + 1);
                page.Segments[pageSegNum] = new byte[len];
                Array.Copy(Audio, offset + segmentDataPos, page.Segments[pageSegNum], 0, len);
                segmentDataPos += page.Segments[pageSegNum].Length;
                pageSegNum++;
            }

            page.Size = segmentDataPos;
            offset += segmentDataPos;

            return page;
        }

    }
}
