using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prompter.Models;

namespace Prompter.Services
{
    public class DetectedInstallation
    {
        public string Name { get; set; } = string.Empty;
        public bool IsInstalled { get; set; }
        public string? RootDirectory { get; set; }
        public string? ExecutablePath { get; set; }
        public string? ModelsDirectory { get; set; }
        public string? OutputDirectory { get; set; }
        public string? Description { get; set; }
        public List<string> FoundModels { get; set; } = new();
    }

    public class SystemEnvironmentInfo
    {
        public DetectedInstallation SwarmUi { get; set; } = new() { Name = "SwarmUI" };
        public DetectedInstallation Ollama { get; set; } = new() { Name = "Ollama" };
        public DetectedInstallation Qwen { get; set; } = new() { Name = "Qwen" };
    }

    public class SystemEnvironmentDetector
    {
        private static SystemEnvironmentDetector? _instance;
        public static SystemEnvironmentDetector Instance => _instance ??= new SystemEnvironmentDetector();

        public SystemEnvironmentInfo LastDetection { get; private set; } = new();

        public SystemEnvironmentInfo DetectAll()
        {
            var info = new SystemEnvironmentInfo
            {
                SwarmUi = DetectSwarmUi(),
                Ollama = DetectOllama(),
                Qwen = DetectQwen()
            };

            LastDetection = info;
            return info;
        }

        public DetectedInstallation DetectSwarmUi()
        {
            var result = new DetectedInstallation
            {
                Name = "SwarmUI",
                IsInstalled = false
            };

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Candidate roots in order of likelihood
            var candidates = new List<string>
            {
                @"X:\SwarmUI",
                @"D:\AI\SwarmUI",
                @"C:\SwarmUI",
                @"D:\SwarmUI",
                @"E:\SwarmUI",
                Path.Combine(userProfile, "SwarmUI"),
                Path.Combine(userProfile, "AI", "SwarmUI")
            };

            // Also check all drive letters for \SwarmUI
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var driveRoot = Path.Combine(drive.RootDirectory.FullName, "SwarmUI");
                if (!candidates.Contains(driveRoot, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(driveRoot);
                }
            }

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    // Check for launcher batch file or executable
                    string? launcherPath = null;
                    var winBat = Path.Combine(candidate, "launch-windows.bat");
                    var plainBat = Path.Combine(candidate, "launch.bat");
                    var exe = Path.Combine(candidate, "SwarmUI.exe");

                    if (File.Exists(winBat)) launcherPath = winBat;
                    else if (File.Exists(plainBat)) launcherPath = plainBat;
                    else if (File.Exists(exe)) launcherPath = exe;

                    var modelsDir = Path.Combine(candidate, "Models", "Stable-Diffusion");
                    if (!Directory.Exists(modelsDir))
                    {
                        var generalModels = Path.Combine(candidate, "Models");
                        if (Directory.Exists(generalModels)) modelsDir = generalModels;
                    }

                    var outputDir = Path.Combine(candidate, "Output");

                    result.IsInstalled = true;
                    result.RootDirectory = candidate;
                    result.ExecutablePath = launcherPath;
                    result.ModelsDirectory = modelsDir;
                    result.OutputDirectory = outputDir;
                    result.Description = launcherPath != null
                        ? $"Found at {candidate} (Launcher: {Path.GetFileName(launcherPath)})"
                        : $"Found at {candidate}";

                    // Collect existing model file names
                    if (Directory.Exists(modelsDir))
                    {
                        try
                        {
                            var files = Directory.GetFiles(modelsDir, "*.safetensors", SearchOption.TopDirectoryOnly)
                                .Concat(Directory.GetFiles(modelsDir, "*.ckpt", SearchOption.TopDirectoryOnly))
                                .Concat(Directory.GetFiles(modelsDir, "*.gguf", SearchOption.TopDirectoryOnly))
                                .Select(Path.GetFileName)
                                .Where(f => !string.IsNullOrEmpty(f) && !SwarmUiService.IsVideoModelName(f!))
                                .ToList();
                            result.FoundModels.AddRange(files!);
                        }
                        catch { }
                    }

                    break;
                }
            }

            return result;
        }

        public DetectedInstallation DetectOllama()
        {
            var result = new DetectedInstallation
            {
                Name = "Ollama",
                IsInstalled = false
            };

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Candidate executable locations
            var candidates = new List<string>
            {
                Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe"),
                Path.Combine(programFiles, "Ollama", "ollama.exe")
            };

            // Also search PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var p in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var full = Path.Combine(p.Trim(), "ollama.exe");
                    if (File.Exists(full) && !candidates.Contains(full, StringComparer.OrdinalIgnoreCase))
                    {
                        candidates.Add(full);
                    }
                }
                catch { }
            }

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    result.IsInstalled = true;
                    result.ExecutablePath = candidate;
                    result.RootDirectory = Path.GetDirectoryName(candidate);
                    break;
                }
            }

            // Models directory
            var envModels = Environment.GetEnvironmentVariable("OLLAMA_MODELS");
            var defaultModelsDir = Path.Combine(userProfile, ".ollama", "models");
            if (!string.IsNullOrEmpty(envModels) && Directory.Exists(envModels))
            {
                result.ModelsDirectory = envModels;
            }
            else if (Directory.Exists(defaultModelsDir))
            {
                result.ModelsDirectory = defaultModelsDir;
            }

            if (result.IsInstalled)
            {
                result.Description = $"Found at {result.ExecutablePath}";
            }

            return result;
        }

        public DetectedInstallation DetectQwen()
        {
            var result = new DetectedInstallation
            {
                Name = "Qwen",
                IsInstalled = false
            };

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // 1. Check Ollama manifests for Qwen
            var manifestsRoot = Path.Combine(userProfile, ".ollama", "models", "manifests");
            if (Directory.Exists(manifestsRoot))
            {
                try
                {
                    var opt = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true };
                    var tagFiles = Directory.EnumerateFiles(manifestsRoot, "*", opt);
                    foreach (var tagFile in tagFiles)
                    {
                        var modelDir = Path.GetDirectoryName(tagFile);
                        if (string.IsNullOrEmpty(modelDir)) continue;
                        var modelBaseName = Path.GetFileName(modelDir);
                        var tag = Path.GetFileName(tagFile);
                        if (modelBaseName.Contains("qwen", StringComparison.OrdinalIgnoreCase) ||
                            modelBaseName.Contains("qwythos", StringComparison.OrdinalIgnoreCase))
                        {
                            result.IsInstalled = true;
                            result.FoundModels.Add($"{modelBaseName}:{tag}");
                            result.ModelsDirectory ??= manifestsRoot;
                        }
                    }
                }
                catch { }
            }

            // 2. Check dedicated .qwen or models folders
            var qwenFolder = Path.Combine(userProfile, ".qwen");
            if (Directory.Exists(qwenFolder))
            {
                result.IsInstalled = true;
                result.RootDirectory = qwenFolder;
                result.ModelsDirectory ??= qwenFolder;
            }

            if (result.IsInstalled)
            {
                result.Description = result.FoundModels.Count > 0
                    ? $"{result.FoundModels.Count} Qwen model(s) detected ({string.Join(", ", result.FoundModels)})"
                    : "Qwen configuration directory found";
            }

            return result;
        }
    }
}
