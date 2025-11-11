using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using TeddyBench.Avalonia.Models;

namespace TeddyBench.Avalonia.Services
{
    public class TonieMetadataService
    {
        private List<TonieMetadata> _toniesDb = new();
        private Dictionary<string, string> _customTonies = new();
        private readonly string _basePath;
        private readonly string _cachePath;
        private readonly string _customToniesPath;
        private readonly HttpClient _httpClient;
        private const string TonieJsonUrl = "https://api.revvox.de/tonies.json?source=TeddyBench.Avalonia&version=1.0";
        private bool _customToniesModified = false;

        public TonieMetadataService()
        {
            // Assume files are in the app directory
            _basePath = AppDomain.CurrentDomain.BaseDirectory;
            _cachePath = Path.Combine(_basePath, "cache");
            _customToniesPath = Path.Combine(_basePath, "customTonies.json");
            _httpClient = new HttpClient();

            // Create cache directory if it doesn't exist
            if (!Directory.Exists(_cachePath))
            {
                Directory.CreateDirectory(_cachePath);
            }

            LoadMetadata();
        }

        private void LoadMetadata()
        {
            try
            {
                // Load tonies.json
                var toniesPath = FindFile("tonies.json");
                if (!string.IsNullOrEmpty(toniesPath) && File.Exists(toniesPath))
                {
                    var json = File.ReadAllText(toniesPath);
                    _toniesDb = JsonConvert.DeserializeObject<List<TonieMetadata>>(json) ?? new();
                    Console.WriteLine($"Loaded {_toniesDb.Count} Tonies from database");
                }

                // Load customTonies.json
                var customPath = FindFile("customTonies.json");
                if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
                {
                    var json = File.ReadAllText(customPath);
                    _customTonies = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new();
                    Console.WriteLine($"Loaded {_customTonies.Count} custom Tonies");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading metadata: {ex.Message}");
            }
        }

        private string? FindFile(string filename)
        {
            // Try multiple locations
            var locations = new[]
            {
                Path.Combine(_basePath, filename),
                Path.Combine(Directory.GetCurrentDirectory(), filename),
                Path.Combine(Directory.GetCurrentDirectory(), "..", filename),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "..", filename),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", filename)
            };

            return locations.FirstOrDefault(File.Exists);
        }

        public (string title, string? imagePath) GetTonieInfo(string hash, string? rfidFolder = null)
        {
            hash = hash.ToUpperInvariant();

            // Try custom tonies first
            if (_customTonies.ContainsKey(hash))
            {
                return (_customTonies[hash], GetCachedImage(hash));
            }

            // Try tonies database
            var tonie = _toniesDb.FirstOrDefault(t =>
                t.Hash != null && t.Hash.Any(h => h.Equals(hash, StringComparison.OrdinalIgnoreCase)));

            if (tonie != null)
            {
                var title = !string.IsNullOrEmpty(tonie.Title) ? tonie.Title : tonie.Series;
                return (title, GetCachedImage(hash));
            }

            // Not found in either database - create a custom entry
            // Use RFID folder name if provided, otherwise fall back to hash
            var customTitle = !string.IsNullOrEmpty(rfidFolder)
                ? $"Custom Tonie [RFID: {rfidFolder}]"
                : $"Custom Tonie [{hash.Substring(0, 8)}]";
            AddCustomTonie(hash, customTitle);
            return (customTitle, null);
        }

        public void AddCustomTonie(string hash, string title)
        {
            hash = hash.ToUpperInvariant();

            if (!_customTonies.ContainsKey(hash))
            {
                _customTonies[hash] = title;
                _customToniesModified = true;
                Console.WriteLine($"Added custom Tonie: {hash} = {title}");

                // Save immediately
                SaveCustomTonies();
            }
        }

        private void SaveCustomTonies()
        {
            if (!_customToniesModified)
            {
                return;
            }

            try
            {
                var json = JsonConvert.SerializeObject(_customTonies, Formatting.Indented);
                File.WriteAllText(_customToniesPath, json);
                Console.WriteLine($"Saved {_customTonies.Count} custom Tonies to {_customToniesPath}");
                _customToniesModified = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving customTonies.json: {ex.Message}");
            }
        }

        public (string title, string? imagePath) GetTonieInfoByAudioId(uint audioId)
        {
            var audioIdStr = audioId.ToString();

            // Try tonies database by audio_id
            var tonie = _toniesDb.FirstOrDefault(t =>
                t.AudioId != null && t.AudioId.Any(id => id.Equals(audioIdStr, StringComparison.OrdinalIgnoreCase)));

            if (tonie != null)
            {
                var title = !string.IsNullOrEmpty(tonie.Title) ? tonie.Title : tonie.Series;
                var hash = tonie.Hash?.FirstOrDefault();
                return (title, hash != null ? GetCachedImage(hash) : null);
            }

            return ($"Audio ID: 0x{audioId:X8}", null);
        }

        private string? GetCachedImage(string hash)
        {
            hash = hash.ToUpperInvariant();

            // Try multiple cache locations
            var cacheLocations = new[]
            {
                Path.Combine(_basePath, "cache"),
                Path.Combine(Directory.GetCurrentDirectory(), "cache"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "cache"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "cache"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "cache")
            };

            foreach (var cacheDir in cacheLocations)
            {
                if (Directory.Exists(cacheDir))
                {
                    var imagePath = Path.Combine(cacheDir, $"{hash}.png");
                    if (File.Exists(imagePath))
                    {
                        return imagePath;
                    }
                }
            }

            return null;
        }

        public async Task<bool> DownloadTonieJsonAsync()
        {
            try
            {
                Console.WriteLine($"Downloading tonies.json from {TonieJsonUrl}...");
                var jsonContent = await _httpClient.GetStringAsync(TonieJsonUrl);

                var toniesPath = Path.Combine(_basePath, "tonies.json");
                await File.WriteAllTextAsync(toniesPath, jsonContent);

                // Reload metadata after download
                _toniesDb = JsonConvert.DeserializeObject<List<TonieMetadata>>(jsonContent) ?? new();
                Console.WriteLine($"Downloaded and loaded {_toniesDb.Count} Tonies from database");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading tonies.json: {ex.Message}");
                return false;
            }
        }

        public async Task<string?> DownloadAndCacheImageAsync(string hash, string? picUrl)
        {
            if (string.IsNullOrEmpty(picUrl))
            {
                return null;
            }

            hash = hash.ToUpperInvariant();
            var cacheFileName = Path.Combine(_cachePath, $"{hash}.png");

            // If already cached, return the cached path
            if (File.Exists(cacheFileName))
            {
                return cacheFileName;
            }

            try
            {
                Console.WriteLine($"Downloading image for {hash}...");
                var imageBytes = await _httpClient.GetByteArrayAsync(picUrl);
                await File.WriteAllBytesAsync(cacheFileName, imageBytes);
                Console.WriteLine($"Cached image to {cacheFileName}");
                return cacheFileName;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading image for {hash}: {ex.Message}");
                return null;
            }
        }

        public string? GetPicUrl(string hash)
        {
            hash = hash.ToUpperInvariant();

            // Try custom tonies first
            if (_customTonies.ContainsKey(hash))
            {
                return null; // Custom tonies don't have pic URLs
            }

            // Try tonies database
            var tonie = _toniesDb.FirstOrDefault(t =>
                t.Hash != null && t.Hash.Any(h => h.Equals(hash, StringComparison.OrdinalIgnoreCase)));

            return tonie?.Pic;
        }
    }
}