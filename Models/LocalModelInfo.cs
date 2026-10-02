using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Prompter.Models
{
    public class LocalModelInfo : INotifyPropertyChanged
    {
        private bool _isDefault;
        private bool _isCloudModel;

        public bool IsDefault
        {
            get => _isDefault;
            set
            {
                if (_isDefault != value)
                {
                    _isDefault = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(DetailsBadge));
                    OnPropertyChanged(nameof(StarIcon));
                }
            }
        }

        public bool IsCloudModel
        {
            get => _isCloudModel ||
                   string.Equals(Source, "Ollama Cloud", StringComparison.OrdinalIgnoreCase) ||
                   Name.Contains(":cloud", StringComparison.OrdinalIgnoreCase) ||
                   Name.Contains("-cloud", StringComparison.OrdinalIgnoreCase) ||
                   ModelId.Contains(":cloud", StringComparison.OrdinalIgnoreCase) ||
                   ModelId.Contains("-cloud", StringComparison.OrdinalIgnoreCase) ||
                   !string.IsNullOrEmpty(RemoteHost);
            set
            {
                if (_isCloudModel != value)
                {
                    _isCloudModel = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(DetailsBadge));
                    OnPropertyChanged(nameof(FamilyPrefix));
                }
            }
        }

        public string StarIcon => IsDefault ? "⭐ " : "";

        public string Name { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string ParameterSize { get; set; } = string.Empty;
        public string Quantization { get; set; } = string.Empty;
        public string Source { get; set; } = "Ollama";
        public string? FilePath { get; set; }
        public long SizeBytes { get; set; }
        public string? RemoteHost { get; set; }
        public string? RemoteModel { get; set; }

        public bool IsImageModel { get; set; }
        public int StandardWidth { get; set; } = 1024;
        public int StandardHeight { get; set; } = 1024;

        public bool IsFlux =>
            IsImageModel && (
                Name.Contains("flux", StringComparison.OrdinalIgnoreCase) ||
                ModelId.Contains("flux", StringComparison.OrdinalIgnoreCase) ||
                Family.Contains("flux", StringComparison.OrdinalIgnoreCase));

        public bool IsSdxl =>
            IsImageModel && !IsFlux && (
                Name.Contains("sdxl", StringComparison.OrdinalIgnoreCase) ||
                Name.Contains("pony", StringComparison.OrdinalIgnoreCase) ||
                Name.Contains("xl", StringComparison.OrdinalIgnoreCase));

        public bool IsSd15 =>
            IsImageModel && !IsFlux && (
                Name.Contains("1.5", StringComparison.OrdinalIgnoreCase) ||
                Name.Contains("dreamshaper", StringComparison.OrdinalIgnoreCase) ||
                Name.Contains("v1", StringComparison.OrdinalIgnoreCase));

        public bool IsGemma =>
            !IsImageModel && (
                Name.Contains("gemma", StringComparison.OrdinalIgnoreCase) ||
                Family.Contains("gemma", StringComparison.OrdinalIgnoreCase));

        public bool IsQwen =>
            !IsImageModel && (
                Name.Contains("qwen", StringComparison.OrdinalIgnoreCase) ||
                Family.Contains("qwen", StringComparison.OrdinalIgnoreCase) ||
                Name.Contains("qwythos", StringComparison.OrdinalIgnoreCase) ||
                Family.Contains("qwythos", StringComparison.OrdinalIgnoreCase));

        public bool IsLlama =>
            !IsImageModel && (
                Name.Contains("llama", StringComparison.OrdinalIgnoreCase) ||
                Family.Contains("llama", StringComparison.OrdinalIgnoreCase));

        public bool IsDeepSeek =>
            !IsImageModel && (
                Name.Contains("deepseek", StringComparison.OrdinalIgnoreCase) ||
                Family.Contains("deepseek", StringComparison.OrdinalIgnoreCase));

        public bool IsMistral =>
            !IsImageModel && (
                Name.Contains("mistral", StringComparison.OrdinalIgnoreCase) ||
                Family.Contains("mistral", StringComparison.OrdinalIgnoreCase) ||
                Name.Contains("mixtral", StringComparison.OrdinalIgnoreCase) ||
                Family.Contains("mixtral", StringComparison.OrdinalIgnoreCase) ||
                Name.Contains("codestral", StringComparison.OrdinalIgnoreCase));

        public bool IsPhi =>
            !IsImageModel && (
                Name.Contains("phi", StringComparison.OrdinalIgnoreCase) ||
                Family.Contains("phi", StringComparison.OrdinalIgnoreCase));

        public bool IsGlm =>
            !IsImageModel && (
                Name.Contains("glm", StringComparison.OrdinalIgnoreCase) ||
                Family.Contains("glm", StringComparison.OrdinalIgnoreCase));

        public bool IsGptOss =>
            !IsImageModel && (
                Name.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase) ||
                Name.Contains("gptoss", StringComparison.OrdinalIgnoreCase) ||
                Family.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase) ||
                Family.Contains("gptoss", StringComparison.OrdinalIgnoreCase));

        public bool IsNemotron =>
            !IsImageModel && (
                Name.Contains("nemotron", StringComparison.OrdinalIgnoreCase) ||
                Family.Contains("nemotron", StringComparison.OrdinalIgnoreCase));

        public bool IsKimi =>
            !IsImageModel && (
                Name.Contains("kimi", StringComparison.OrdinalIgnoreCase) ||
                Name.Contains("moonshot", StringComparison.OrdinalIgnoreCase) ||
                Family.Contains("kimi", StringComparison.OrdinalIgnoreCase));

        public bool IsMiniMax =>
            !IsImageModel && (
                Name.Contains("minimax", StringComparison.OrdinalIgnoreCase) ||
                Family.Contains("minimax", StringComparison.OrdinalIgnoreCase));

        public string FamilyName
        {
            get
            {
                if (IsImageModel)
                {
                    if (IsFlux) return "Flux";
                    if (IsSdxl) return "SDXL";
                    if (IsSd15) return "SD 1.5";
                    return "Stable Diffusion";
                }
                if (IsGemma) return "Gemma";
                if (IsQwen) return "Qwen";
                if (IsLlama) return "Llama";
                if (IsDeepSeek) return "DeepSeek";
                if (IsMistral) return "Mistral";
                if (IsGptOss) return "GPT-OSS";
                if (IsNemotron) return "Nemotron";
                if (IsKimi) return "Kimi";
                if (IsMiniMax) return "MiniMax";
                if (IsGlm) return "GLM";
                if (IsPhi) return "Phi";
                if (!string.IsNullOrEmpty(Family)) return Family;
                return "Local";
            }
        }

        public string FamilyPrefix
        {
            get
            {
                if (IsImageModel)
                {
                    if (IsFlux) return "🎨 [Flux] ";
                    if (IsSdxl) return "🎨 [SDXL] ";
                    if (IsSd15) return "🎨 [SD 1.5] ";
                    return "🎨 [Image] ";
                }
                if (IsCloudModel)
                {
                    return $"☁️ [{FamilyName}] ";
                }
                if (IsGemma) return "💎 [Gemma] ";
                if (IsQwen) return "⭐ [Qwen] ";
                if (IsLlama) return "🦙 [Llama] ";
                if (IsDeepSeek) return "🐋 [DeepSeek] ";
                if (IsMistral) return "🌪️ [Mistral] ";
                if (IsGlm) return "🌐 [GLM] ";
                if (IsPhi) return "🔬 [Phi] ";
                if (IsGptOss) return "🧠 [GPT-OSS] ";
                if (IsNemotron) return "⚡ [Nemotron] ";
                if (IsKimi) return "🌙 [Kimi] ";
                if (IsMiniMax) return "🔮 [MiniMax] ";
                return "🤖 ";
            }
        }

        public bool IsVideoModel =>
            Name.Contains("wan", StringComparison.OrdinalIgnoreCase) ||
            ModelId.Contains("wan", StringComparison.OrdinalIgnoreCase) ||
            Name.Contains("t2v", StringComparison.OrdinalIgnoreCase) ||
            Name.Contains("i2v", StringComparison.OrdinalIgnoreCase) ||
            Name.Contains("vfi", StringComparison.OrdinalIgnoreCase) ||
            Name.Contains("svd", StringComparison.OrdinalIgnoreCase) ||
            Name.Contains("video", StringComparison.OrdinalIgnoreCase) ||
            Name.Contains("animate", StringComparison.OrdinalIgnoreCase) ||
            Name.Contains("cogvideox", StringComparison.OrdinalIgnoreCase);

        public string DisplayName
        {
            get
            {
                var star = IsDefault ? "⭐ " : "";
                if (IsImageModel)
                {
                    return $"{star}{FamilyPrefix}{Name}";
                }
                var details = !string.IsNullOrEmpty(ParameterSize) ? $" ({ParameterSize})" : "";
                return $"{star}{FamilyPrefix}{Name}{details}";
            }
        }

        public string DetailsBadge
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (IsDefault) parts.Add("⭐ Default");
                if (IsCloudModel)
                {
                    parts.Add("☁️ Ollama Cloud");
                    parts.Add(FamilyName);
                    if (!string.IsNullOrEmpty(ParameterSize)) parts.Add(ParameterSize);
                    if (!string.IsNullOrEmpty(Source) && Source != "Ollama" && Source != "Ollama Cloud") parts.Add(Source);
                }
                else
                {
                    parts.Add(FamilyName);
                    if (!IsImageModel)
                    {
                        if (!string.IsNullOrEmpty(ParameterSize)) parts.Add(ParameterSize);
                        if (!string.IsNullOrEmpty(Quantization)) parts.Add(Quantization);
                    }
                    if (!string.IsNullOrEmpty(Source)) parts.Add(Source);
                }
                return string.Join(" • ", parts);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() => DisplayName;
    }
}
