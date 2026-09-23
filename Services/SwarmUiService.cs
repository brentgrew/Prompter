using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Prompter.Models;

namespace Prompter.Services
{
    public class GenerateImageResult
    {
        public bool Success { get; set; }
        public string? ImagePath { get; set; }
        public string? ImageUrl { get; set; }
        public string? ErrorMessage { get; set; }
        public int Width { get; set; } = 1024;
        public int Height { get; set; } = 1024;
        public int Steps { get; set; } = 50;
        public double Cfg { get; set; } = 7.0;
        public string? ModelName { get; set; }
    }

    public class SwarmUiService
    {
        private static SwarmUiService? _instance;
        public static SwarmUiService Instance => _instance ??= new SwarmUiService();

        private readonly HttpClient _httpClient;
        private string? _cachedSessionId;
        private readonly SemaphoreSlim _sessionLock = new SemaphoreSlim(1, 1);

        public string BaseUrl { get; set; } = "http://localhost:7801";
        public string SwarmRootPath { get; set; } = @"X:\SwarmUI";
        public string SwarmModelsPath { get; set; } = @"X:\SwarmUI\Models\Stable-Diffusion";
        public string SwarmOutputPath { get; set; } = @"X:\SwarmUI\Output";
        public string? SwarmExecutablePath { get; set; }

        public SwarmUiService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10) // Generations can take several minutes on complex models
            };

            // Detect SwarmUI installation on user's system and set default directories
            var detected = SystemEnvironmentDetector.Instance.DetectSwarmUi();
            if (detected.IsInstalled && !string.IsNullOrEmpty(detected.RootDirectory))
            {
                SwarmRootPath = detected.RootDirectory;
                SwarmModelsPath = detected.ModelsDirectory ?? Path.Combine(detected.RootDirectory, "Models", "Stable-Diffusion");
                SwarmOutputPath = detected.OutputDirectory ?? Path.Combine(detected.RootDirectory, "Output");
                SwarmExecutablePath = detected.ExecutablePath;
            }
        }

        /// <summary>
        /// Explicitly starts the SwarmUI server process when the user requests it.
        /// NOTE: Prompter will NEVER automatically call this method to prevent unwanted GPU/system load.
        /// </summary>
        public bool StartSwarmServer()
        {
            if (!string.IsNullOrEmpty(SwarmExecutablePath) && File.Exists(SwarmExecutablePath))
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = SwarmExecutablePath,
                        WorkingDirectory = SwarmRootPath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    return true;
                }
                catch { }
            }
            return false;
        }

        public async Task<bool> IsSwarmRunningAsync(int timeoutMs = 1500)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/API/GetNewSession")
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string?> GetSessionIdAsync(bool forceNew = false)
        {
            await _sessionLock.WaitAsync();
            try
            {
                if (!forceNew && !string.IsNullOrEmpty(_cachedSessionId))
                {
                    return _cachedSessionId;
                }

                using var cts = new CancellationTokenSource(3000);
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/API/GetNewSession")
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };

                using var response = await _httpClient.SendAsync(request, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("session_id", out var sidProp))
                {
                    _cachedSessionId = sidProp.GetString();
                    return _cachedSessionId;
                }

                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                _sessionLock.Release();
            }
        }

        public async Task<List<LocalModelInfo>> ListModelsAsync()
        {
            var models = new Dictionary<string, LocalModelInfo>(StringComparer.OrdinalIgnoreCase);

            // 1. Scan SwarmUI API if running
            try
            {
                var sessionId = await GetSessionIdAsync();
                if (!string.IsNullOrEmpty(sessionId))
                {
                    var payload = new
                    {
                        session_id = sessionId,
                        path = "",
                        depth = 5,
                        subtype = "Stable-Diffusion"
                    };

                    using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/API/ListModels")
                    {
                        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                    };

                    using var cts = new CancellationTokenSource(4000);
                    using var response = await _httpClient.SendAsync(request, cts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(cts.Token);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("files", out var filesArr) && filesArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in filesArr.EnumerateArray())
                            {
                                var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                                if (string.IsNullOrEmpty(name) || IsVideoModelName(name)) continue;

                                int width = 1024;
                                int height = 1024;

                                if (item.TryGetProperty("standard_width", out var w) && w.GetInt32() > 0)
                                    width = w.GetInt32();
                                else if (IsSd15Name(name))
                                    width = 512;

                                if (item.TryGetProperty("standard_height", out var h) && h.GetInt32() > 0)
                                    height = h.GetInt32();
                                else
                                    height = width;

                                var info = new LocalModelInfo
                                {
                                    Name = name,
                                    ModelId = name,
                                    IsImageModel = true,
                                    StandardWidth = width,
                                    StandardHeight = height,
                                    Source = "SwarmUI",
                                    FilePath = Path.Combine(SwarmModelsPath, name)
                                };

                                models[name] = info;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fall through to filesystem scanning
            }

            // 2. Scan X:\SwarmUI\Models\Stable-Diffusion on disk
            try
            {
                if (Directory.Exists(SwarmModelsPath))
                {
                    var opt = new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        MaxRecursionDepth = 3,
                        IgnoreInaccessible = true
                    };

                    var files = Directory.EnumerateFiles(SwarmModelsPath, "*.*", opt)
                        .Where(f => f.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".ckpt", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase));

                    foreach (var file in files)
                    {
                        var relativeName = Path.GetRelativePath(SwarmModelsPath, file).Replace('\\', '/');
                        var fileName = Path.GetFileName(file);

                        // Skip video models (Wan, animate, etc.)
                        if (IsVideoModelName(fileName) || IsVideoModelName(relativeName))
                            continue;

                        // If not already detected via API
                        if (!models.ContainsKey(relativeName) && !models.ContainsKey(fileName))
                        {
                            var fi = new FileInfo(file);
                            int defaultDim = IsSd15Name(fileName) ? 512 : 1024;

                            var info = new LocalModelInfo
                            {
                                Name = relativeName,
                                ModelId = relativeName,
                                IsImageModel = true,
                                StandardWidth = defaultDim,
                                StandardHeight = defaultDim,
                                Source = "SwarmUI Disk",
                                FilePath = file,
                                SizeBytes = fi.Exists ? fi.Length : 0
                            };

                            models[relativeName] = info;
                        }
                    }
                }
            }
            catch
            {
                // Non-fatal
            }

            // Sort: SDXL / Pony / DreamShaper first, then by Name (excluding any video models)
            return models.Values
                .Where(m => !IsVideoModelName(m.Name) && !IsVideoModelName(m.ModelId))
                .OrderByDescending(m => m.IsSdxl ? 3 : (m.IsSd15 ? 2 : 1))
                .ThenBy(m => m.Name)
                .ToList();
        }

        public static bool IsVideoModelName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var lower = name.ToLowerInvariant();
            return lower.Contains("wan") ||
                   lower.Contains("t2v") ||
                   lower.Contains("i2v") ||
                   lower.Contains("vfi") ||
                   lower.Contains("svd") ||
                   lower.Contains("video") ||
                   lower.Contains("animate") ||
                   lower.Contains("cogvideox");
        }

        private static bool IsSd15Name(string name)
        {
            return name.Contains("1.5", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("dreamshaper", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("v1-5", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("v1.5", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<GenerateImageResult> GenerateImageAsync(
            string prompt,
            LocalModelInfo model,
            CancellationToken cancellationToken)
        {
            var result = new GenerateImageResult
            {
                ModelName = model.Name,
                Steps = 50,
                Cfg = 7.0,
                Width = model.StandardWidth > 0 ? model.StandardWidth : 1024,
                Height = model.StandardHeight > 0 ? model.StandardHeight : 1024
            };

            var sessionId = await GetSessionIdAsync();
            if (string.IsNullOrEmpty(sessionId))
            {
                result.Success = false;
                result.ErrorMessage = "Could not obtain a session from SwarmUI. Please verify that SwarmUI is running on http://localhost:7801.";
                return result;
            }

            try
            {
                // Parameters strictly enforce:
                // steps = 50
                // cfgscale = 7
                // width & height = model default
                var requestPayload = new
                {
                    session_id = sessionId,
                    images = 1,
                    prompt = prompt,
                    model = model.ModelId,
                    steps = 50,
                    cfgscale = 7,
                    width = result.Width,
                    height = result.Height
                };

                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(requestPayload),
                    Encoding.UTF8,
                    "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/API/GenerateText2Image")
                {
                    Content = jsonContent
                };

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    result.Success = false;
                    result.ErrorMessage = $"SwarmUI generation error (HTTP {(int)response.StatusCode}): {errorBody}";
                    return result;
                }

                var respJson = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(respJson);

                if (doc.RootElement.TryGetProperty("error_id", out var errIdProp) && !string.IsNullOrEmpty(errIdProp.GetString()))
                {
                    result.Success = false;
                    result.ErrorMessage = doc.RootElement.TryGetProperty("error", out var errP) ? errP.GetString() : "SwarmUI returned an error";
                    return result;
                }

                if (doc.RootElement.TryGetProperty("images", out var imagesArr) &&
                    imagesArr.ValueKind == JsonValueKind.Array &&
                    imagesArr.GetArrayLength() > 0)
                {
                    var firstImg = imagesArr[0].GetString();
                    if (!string.IsNullOrEmpty(firstImg))
                    {
                        var cleanRel = firstImg.Replace('\\', '/').TrimStart('/');
                        result.ImageUrl = $"{BaseUrl}/{cleanRel}";

                        // Resolve local disk file path in X:\SwarmUI\Output
                        string? localDiskPath = null;
                        if (cleanRel.StartsWith("View/", StringComparison.OrdinalIgnoreCase))
                        {
                            var subPath = cleanRel.Substring(5).Replace('/', '\\');
                            localDiskPath = Path.Combine(SwarmOutputPath, subPath);
                        }
                        else
                        {
                            localDiskPath = Path.Combine(SwarmOutputPath, cleanRel.Replace('/', '\\'));
                        }

                        // Check if file is written to disk (wait briefly if needed)
                        for (int i = 0; i < 6; i++)
                        {
                            if (File.Exists(localDiskPath))
                            {
                                result.ImagePath = localDiskPath;
                                break;
                            }
                            await Task.Delay(250, cancellationToken);
                        }

                        // If not found in standard Output subpath, fallback to ImageUrl or try search
                        if (string.IsNullOrEmpty(result.ImagePath) && File.Exists(localDiskPath))
                        {
                            result.ImagePath = localDiskPath;
                        }

                        result.Success = true;
                        return result;
                    }
                }

                result.Success = false;
                result.ErrorMessage = "SwarmUI did not return any image paths in the response.";
                return result;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Image generation was canceled.";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Failed to generate image: {ex.Message}";
                return result;
            }
        }
    }
}
