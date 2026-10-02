using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public class LocalAiService
    {
        private static LocalAiService? _instance;
        public static LocalAiService Instance => _instance ??= new LocalAiService();

        private readonly HttpClient _httpClient;
        public string BaseUrl { get; set; } = "http://localhost:11434";
        public string? OllamaExecutablePath { get; set; }
        public string? OllamaModelsPath { get; set; }

        public LocalAiService()
        {
            _httpClient = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            // Detect Ollama on user's system and set default directories
            var detected = SystemEnvironmentDetector.Instance.DetectOllama();
            if (detected.IsInstalled)
            {
                OllamaExecutablePath = detected.ExecutablePath;
                OllamaModelsPath = detected.ModelsDirectory;
            }
        }

        public async Task<bool> IsOllamaRunningAsync(int timeoutMs = 1500)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/tags");
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> EnsureOllamaRunningAsync()
        {
            if (await IsOllamaRunningAsync(1000))
            {
                return true;
            }

            // Try starting Ollama serve using detected executable or standard path
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var standardOllamaPath = Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe");
                var exePath = !string.IsNullOrEmpty(OllamaExecutablePath) && File.Exists(OllamaExecutablePath)
                    ? OllamaExecutablePath
                    : (File.Exists(standardOllamaPath) ? standardOllamaPath : "ollama");

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "serve",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(startInfo);

                // Wait up to 6 seconds for Ollama server to become ready
                for (int i = 0; i < 12; i++)
                {
                    await Task.Delay(500);
                    if (await IsOllamaRunningAsync(800))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Fall through if cannot start process
            }

            return false;
        }

        public async Task<List<LocalModelInfo>> ScanModelsAsync()
        {
            var discovered = new Dictionary<string, LocalModelInfo>(StringComparer.OrdinalIgnoreCase);

            // 1. Scan filesystem for Ollama manifests
            ScanOllamaManifestsOnDisk(discovered);

            // 2. Scan filesystem for standalone GGUF files in .ollama, .gemma, .qwen, llama.cpp, etc.
            ScanGgufFilesOnDisk(discovered);

            // 3. Query Ollama API if online to get active models & detailed metadata
            await ScanOllamaApiModelsAsync(discovered);

            // 4. Query Ollama Cloud catalog or fallback to known cloud models
            await ScanOllamaCloudModelsAsync(discovered);

            // Sort models:
            // 1. Local models first, then Cloud models
            // 2. Qwen and Gemma models first among local
            // 3. Models from Ollama library
            // 4. Then by DisplayName
            return discovered.Values
                .OrderBy(m => m.IsCloudModel ? 1 : 0)
                .ThenByDescending(m => m.IsQwen ? 2 : (m.IsGemma ? 1 : 0))
                .ThenByDescending(m => m.Source == "Ollama")
                .ThenBy(m => m.DisplayName)
                .ToList();
        }

        private void ScanOllamaManifestsOnDisk(Dictionary<string, LocalModelInfo> models)
        {
            try
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var manifestsRoot = Path.Combine(userProfile, ".ollama", "models", "manifests");

                if (!Directory.Exists(manifestsRoot)) return;

                var opt = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 5,
                    IgnoreInaccessible = true
                };

                var tagFiles = Directory.EnumerateFiles(manifestsRoot, "*", opt);
                foreach (var tagFile in tagFiles)
                {
                    var tag = Path.GetFileName(tagFile);
                    var modelDir = Path.GetDirectoryName(tagFile);
                    if (string.IsNullOrEmpty(modelDir)) continue;

                    var modelBaseName = Path.GetFileName(modelDir);
                    var fullName = $"{modelBaseName}:{tag}";

                    if (!models.ContainsKey(fullName))
                    {
                        var info = new LocalModelInfo
                        {
                            Name = fullName,
                            ModelId = fullName,
                            Source = "Ollama Library",
                            FilePath = tagFile
                        };

                        DetectFamilyFromName(info, modelBaseName);
                        models[fullName] = info;
                    }
                }
            }
            catch
            {
                // Non-fatal if folder permissions or directory missing
            }
        }

        private void ScanGgufFilesOnDisk(Dictionary<string, LocalModelInfo> models)
        {
            try
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var searchFolders = new List<string>
                {
                    Path.Combine(userProfile, ".ollama", "models"),
                    Path.Combine(userProfile, ".qwen"),
                    Path.Combine(userProfile, ".gemma"),
                    Path.Combine(userProfile, "gemma"),
                    Path.Combine(userProfile, ".llama"),
                    Path.Combine(userProfile, "llama"),
                    Path.Combine(userProfile, "llama.cpp"),
                    Path.Combine(userProfile, "llama.cpp", "models"),
                    Path.Combine(userProfile, "models"),
                    Path.Combine(userProfile, ".cache", "lm-studio", "models"),
                    Path.Combine(userProfile, ".cache", "huggingface", "hub")
                };

                if (Directory.Exists(@"C:\models")) searchFolders.Add(@"C:\models");
                if (Directory.Exists(@"D:\models")) searchFolders.Add(@"D:\models");

                foreach (var folder in searchFolders)
                {
                    if (!Directory.Exists(folder)) continue;

                    IEnumerable<string> ggufFiles;
                    try
                    {
                        var opt = new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            MaxRecursionDepth = 2,
                            IgnoreInaccessible = true
                        };
                        ggufFiles = Directory.EnumerateFiles(folder, "*.gguf", opt);
                    }
                    catch
                    {
                        try { ggufFiles = Directory.GetFiles(folder, "*.gguf", SearchOption.TopDirectoryOnly); }
                        catch { continue; }
                    }

                    foreach (var file in ggufFiles)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            // Filter out small vocabulary / token files (< 50MB)
                            if (fileInfo.Length < 50L * 1024 * 1024) continue;

                            var filename = Path.GetFileNameWithoutExtension(file);
                            if (!models.ContainsKey(filename) && !models.ContainsKey($"{filename}:latest"))
                            {
                                var sizeGb = fileInfo.Length / (1024.0 * 1024.0 * 1024.0);

                                var info = new LocalModelInfo
                                {
                                    Name = filename,
                                    ModelId = filename,
                                    Source = "Local GGUF",
                                    FilePath = file,
                                    SizeBytes = fileInfo.Length,
                                    ParameterSize = $"{sizeGb:0.1} GB"
                                };

                                DetectFamilyFromName(info, filename);
                                models[filename] = info;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch
            {
                // Non-fatal
            }
        }

        private static void DetectFamilyFromName(LocalModelInfo info, string name)
        {
            if (name.Contains("gemma", StringComparison.OrdinalIgnoreCase))
                info.Family = "gemma";
            else if (name.Contains("qwen", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("qwythos", StringComparison.OrdinalIgnoreCase))
                info.Family = "qwen";
            else if (name.Contains("llama", StringComparison.OrdinalIgnoreCase))
                info.Family = "llama";
            else if (name.Contains("deepseek", StringComparison.OrdinalIgnoreCase))
                info.Family = "deepseek";
            else if (name.Contains("mistral", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("mixtral", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("codestral", StringComparison.OrdinalIgnoreCase))
                info.Family = "mistral";
            else if (name.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("gptoss", StringComparison.OrdinalIgnoreCase))
                info.Family = "gpt-oss";
            else if (name.Contains("nemotron", StringComparison.OrdinalIgnoreCase))
                info.Family = "nemotron";
            else if (name.Contains("kimi", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("moonshot", StringComparison.OrdinalIgnoreCase))
                info.Family = "kimi";
            else if (name.Contains("minimax", StringComparison.OrdinalIgnoreCase))
                info.Family = "minimax";
            else if (name.Contains("phi", StringComparison.OrdinalIgnoreCase))
                info.Family = "phi";
            else if (name.Contains("glm", StringComparison.OrdinalIgnoreCase))
                info.Family = "glm";
        }

        private async Task ScanOllamaApiModelsAsync(Dictionary<string, LocalModelInfo> models)
        {
            try
            {
                using var cts = new CancellationTokenSource(2500);
                using var response = await _httpClient.GetAsync($"{BaseUrl}/api/tags", cts.Token);
                if (!response.IsSuccessStatusCode) return;

                var json = await response.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("models", out var modelsArray) &&
                    modelsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in modelsArray.EnumerateArray())
                    {
                        var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (string.IsNullOrEmpty(name)) continue;

                        if (!models.TryGetValue(name, out var info))
                        {
                            info = new LocalModelInfo
                            {
                                Name = name,
                                ModelId = name,
                                Source = "Ollama"
                            };
                            models[name] = info;
                        }

                        if (item.TryGetProperty("remote_host", out var rh) && rh.GetString() is string remoteHost && !string.IsNullOrEmpty(remoteHost))
                        {
                            info.RemoteHost = remoteHost;
                            info.Source = "Ollama Cloud";
                            info.IsCloudModel = true;
                        }

                        if (item.TryGetProperty("remote_model", out var rm) && rm.GetString() is string remoteModel && !string.IsNullOrEmpty(remoteModel))
                        {
                            info.RemoteModel = remoteModel;
                        }

                        if (name.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase))
                        {
                            info.IsCloudModel = true;
                            if (string.IsNullOrEmpty(info.Source) || info.Source == "Ollama")
                            {
                                info.Source = "Ollama Cloud";
                            }
                        }

                        if (item.TryGetProperty("size", out var s) && s.TryGetInt64(out var sizeBytes))
                        {
                            info.SizeBytes = sizeBytes;
                        }

                        if (item.TryGetProperty("details", out var details) &&
                            details.ValueKind == JsonValueKind.Object)
                        {
                            if (details.TryGetProperty("parameter_size", out var param) && param.GetString() is string ps)
                            {
                                info.ParameterSize = ps;
                            }
                            if (details.TryGetProperty("quantization_level", out var quant) && quant.GetString() is string q)
                            {
                                info.Quantization = q;
                            }
                            if (details.TryGetProperty("family", out var fam) && fam.GetString() is string f)
                            {
                                info.Family = f;
                            }
                        }

                        if (string.IsNullOrEmpty(info.Family))
                        {
                            DetectFamilyFromName(info, name);
                        }
                    }
                }
            }
            catch
            {
                // Ollama API might be currently offline
            }
        }

        public static readonly (string Name, string Family, string ParamSize)[] KnownOllamaCloudModels = new[]
        {
            ("gpt-oss:20b", "gpt-oss", "20.9B"),
            ("gpt-oss:120b", "gpt-oss", "120B"),
            ("nemotron-3-nano:30b", "nemotron", "30B"),
            ("nemotron-3-super", "nemotron", "Super"),
            ("nemotron-3-ultra", "nemotron", "Ultra"),
            ("gemma4:31b", "gemma", "31B"),
            ("glm-5.3-flash", "glm", "321B"),
            ("glm-5.3", "glm", "755B"),
            ("glm-5.2", "glm", "5.2"),
            ("deepseek-v4-pro:0813", "deepseek", "V4 Pro"),
            ("deepseek-v4.1-flash", "deepseek", "V4.1 Flash"),
            ("mistral-large-3:675b", "mistral", "675B"),
            ("kimi-k3", "kimi", "K3"),
            ("kimi-k2.7-code", "kimi", "K2.7 Code"),
            ("kimi-k2.6", "kimi", "K2.6"),
            ("minimax-m3", "minimax", "M3"),
            ("minimax-m2.7", "minimax", "M2.7")
        };

        private async Task ScanOllamaCloudModelsAsync(Dictionary<string, LocalModelInfo> models)
        {
            try
            {
                using var cts = new CancellationTokenSource(3500);
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://ollama.com/api/tags");
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cts.Token);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("models", out var modelsArray) &&
                        modelsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in modelsArray.EnumerateArray())
                        {
                            var rawName = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            if (string.IsNullOrWhiteSpace(rawName)) continue;

                            var cloudTag = rawName.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase) || rawName.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase)
                                ? rawName
                                : $"{rawName}:cloud";

                            bool alreadyExists = models.ContainsKey(cloudTag) ||
                                models.ContainsKey(rawName) ||
                                models.Values.Any(m => string.Equals(m.RemoteModel, rawName, StringComparison.OrdinalIgnoreCase) ||
                                                       string.Equals(m.Name, $"{rawName}-cloud", StringComparison.OrdinalIgnoreCase) ||
                                                       string.Equals(m.Name, $"{rawName}:cloud", StringComparison.OrdinalIgnoreCase));

                            if (!alreadyExists)
                            {
                                var info = new LocalModelInfo
                                {
                                    Name = cloudTag,
                                    ModelId = cloudTag,
                                    Source = "Ollama Cloud",
                                    IsCloudModel = true,
                                    RemoteHost = "https://ollama.com",
                                    RemoteModel = rawName
                                };

                                if (item.TryGetProperty("size", out var s) && s.TryGetInt64(out var sizeBytes) && sizeBytes > 0)
                                {
                                    info.SizeBytes = sizeBytes;
                                }

                                if (item.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object)
                                {
                                    if (details.TryGetProperty("parameter_size", out var ps) && ps.GetString() is string param && !string.IsNullOrWhiteSpace(param))
                                    {
                                        info.ParameterSize = param;
                                    }
                                    if (details.TryGetProperty("family", out var f) && f.GetString() is string fam && !string.IsNullOrWhiteSpace(fam))
                                    {
                                        info.Family = fam;
                                    }
                                }

                                if (string.IsNullOrEmpty(info.Family))
                                {
                                    DetectFamilyFromName(info, rawName);
                                }

                                if (string.IsNullOrEmpty(info.ParameterSize))
                                {
                                    var known = KnownOllamaCloudModels.FirstOrDefault(k => string.Equals(k.Name, rawName, StringComparison.OrdinalIgnoreCase));
                                    if (!string.IsNullOrEmpty(known.ParamSize))
                                    {
                                        info.ParameterSize = known.ParamSize;
                                    }
                                }

                                models[cloudTag] = info;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Network error or timeout: fall through to fallback catalog
            }

            // Fallback catalog: ensure all known cloud models are available
            foreach (var (knownName, knownFamily, knownParam) in KnownOllamaCloudModels)
            {
                var cloudTag = $"{knownName}:cloud";
                bool alreadyExists = models.ContainsKey(cloudTag) ||
                    models.ContainsKey(knownName) ||
                    models.Values.Any(m => string.Equals(m.RemoteModel, knownName, StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(m.Name, $"{knownName}-cloud", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(m.Name, $"{knownName}:cloud", StringComparison.OrdinalIgnoreCase));

                if (!alreadyExists)
                {
                    var info = new LocalModelInfo
                    {
                        Name = cloudTag,
                        ModelId = cloudTag,
                        Family = knownFamily,
                        ParameterSize = knownParam,
                        Source = "Ollama Cloud",
                        IsCloudModel = true,
                        RemoteHost = "https://ollama.com",
                        RemoteModel = knownName
                    };
                    DetectFamilyFromName(info, knownName);
                    models[cloudTag] = info;
                }
            }
        }

        public Task StreamChatAsync(
            string modelName,
            IEnumerable<ChatMessage> history,
            Action<string> onChunkReceived,
            CancellationToken cancellationToken)
        {
            return StreamChatAsync(
                modelName,
                history,
                (chunk, isThinking) =>
                {
                    if (!isThinking) onChunkReceived(chunk);
                },
                cancellationToken);
        }

        public async Task StreamChatAsync(
            string modelName,
            IEnumerable<ChatMessage> history,
            Action<string, bool> onChunkReceived,
            CancellationToken cancellationToken)
        {
            var messagesPayload = history
                .Where(m => !m.IsError && !string.IsNullOrWhiteSpace(m.Content))
                .Select(m => new
                {
                    role = m.Role.ToLowerInvariant(),
                    content = m.Content
                })
                .ToList();

            var requestBody = new
            {
                model = modelName,
                messages = messagesPayload,
                stream = true
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/chat")
            {
                Content = jsonContent
            };

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string? parsedErrorMessage = null;
                try
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (!string.IsNullOrWhiteSpace(errorBody))
                    {
                        using var errorDoc = JsonDocument.Parse(errorBody);
                        if (errorDoc.RootElement.TryGetProperty("error", out var errProp) &&
                            errProp.GetString() is string msg && !string.IsNullOrWhiteSpace(msg))
                        {
                            parsedErrorMessage = msg;
                        }
                        else
                        {
                            parsedErrorMessage = errorBody;
                        }
                    }
                }
                catch
                {
                    // Fall back
                }

                if (!string.IsNullOrWhiteSpace(parsedErrorMessage))
                {
                    throw new HttpRequestException(parsedErrorMessage, null, response.StatusCode);
                }

                response.EnsureSuccessStatusCode();
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? line;
            while (!cancellationToken.IsCancellationRequested &&
                   (line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("message", out var msgElement))
                    {
                        // Check for thinking chunks (Gemma 4, DeepSeek-R1, reasoning models)
                        if (msgElement.TryGetProperty("thinking", out var thinkingElement))
                        {
                            var thinkingChunk = thinkingElement.GetString();
                            if (!string.IsNullOrEmpty(thinkingChunk))
                            {
                                onChunkReceived(thinkingChunk, true);
                            }
                        }

                        // Check for standard content chunks
                        if (msgElement.TryGetProperty("content", out var contentElement))
                        {
                            var chunk = contentElement.GetString();
                            if (!string.IsNullOrEmpty(chunk))
                            {
                                onChunkReceived(chunk, false);
                            }
                        }
                    }

                    if (doc.RootElement.TryGetProperty("done", out var doneElement) &&
                        doneElement.GetBoolean())
                    {
                        break;
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed chunk
                }
            }
        }

        public async Task<bool> PullModelAsync(string modelName, Action<string> onStatus, CancellationToken cancellationToken)
        {
            try
            {
                var payload = new { model = modelName, stream = true };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/pull")
                {
                    Content = content
                };

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    onStatus($"Failed to pull model: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                    return false;
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                string? line;
                while (!cancellationToken.IsCancellationRequested && (line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        if (doc.RootElement.TryGetProperty("status", out var statusProp))
                        {
                            var status = statusProp.GetString() ?? "";
                            if (doc.RootElement.TryGetProperty("completed", out var comp) &&
                                doc.RootElement.TryGetProperty("total", out var tot))
                            {
                                long c = comp.GetInt64();
                                long t = tot.GetInt64();
                                if (t > 0)
                                {
                                    var pct = (c * 100) / t;
                                    onStatus($"{status}: {pct}% ({c / (1024 * 1024)}MB / {t / (1024 * 1024)}MB)");
                                    continue;
                                }
                            }
                            onStatus(status);
                        }
                    }
                    catch { }
                }

                return true;
            }
            catch (Exception ex)
            {
                onStatus($"Error pulling model: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RegisterGgufModelAsync(string modelName, string ggufFilePath)
        {
            try
            {
                var cleanPath = ggufFilePath.Replace('\\', '/');
                var payload = new
                {
                    model = modelName,
                    modelfile = $"FROM \"{cleanPath}\""
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await _httpClient.PostAsync($"{BaseUrl}/api/create", content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
