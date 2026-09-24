using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

namespace Prompter.Models
{
    public class LoraModelInfo : INotifyPropertyChanged
    {
        private double _strength = 1.0;
        private bool _isSelected;

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string Folder { get; set; } = "Root";
        public string Category { get; set; } = "General";
        public string SubCategory { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public string? PreviewImageUrl { get; set; }
        public string? TriggerPhrase { get; set; }
        public List<string> Tags { get; set; } = new();
        public long SizeBytes { get; set; }

        public double Strength
        {
            get => _strength;
            set
            {
                var clamped = Math.Round(Math.Clamp(value, -2.0, 2.0), 2);
                if (Math.Abs(_strength - clamped) > 0.001)
                {
                    _strength = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StrengthFormatted));
                    OnPropertyChanged(nameof(DisplayNameWithWeight));
                }
            }
        }

        public string StrengthFormatted
        {
            get => _strength.ToString("0.00", CultureInfo.InvariantCulture);
            set
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    Strength = parsed;
                }
                else if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsedCurrent))
                {
                    Strength = parsedCurrent;
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsFlux =>
            Architecture.Contains("flux", StringComparison.OrdinalIgnoreCase) ||
            Name.Contains("flux", StringComparison.OrdinalIgnoreCase) ||
            Title.Contains("flux", StringComparison.OrdinalIgnoreCase) ||
            RelativePath.Contains("flux", StringComparison.OrdinalIgnoreCase);

        public bool IsSdxl =>
            !IsFlux && (
                Architecture.Contains("xl", StringComparison.OrdinalIgnoreCase) ||
                Architecture.Contains("sdxl", StringComparison.OrdinalIgnoreCase) ||
                Architecture.Contains("pony", StringComparison.OrdinalIgnoreCase) ||
                Name.Contains("xl", StringComparison.OrdinalIgnoreCase) ||
                Name.Contains("sdxl", StringComparison.OrdinalIgnoreCase) ||
                Name.Contains("pony", StringComparison.OrdinalIgnoreCase) ||
                Title.Contains("xl", StringComparison.OrdinalIgnoreCase) ||
                Title.Contains("pony", StringComparison.OrdinalIgnoreCase));

        public bool IsSd15 =>
            !IsFlux && !IsSdxl && (
                Architecture.Contains("1.5", StringComparison.OrdinalIgnoreCase) ||
                Architecture.Contains("v1", StringComparison.OrdinalIgnoreCase) ||
                Name.Contains("1.5", StringComparison.OrdinalIgnoreCase) ||
                Name.Contains("v1-5", StringComparison.OrdinalIgnoreCase));

        public bool IsNsfw =>
            Folder.StartsWith("NSFW", StringComparison.OrdinalIgnoreCase) ||
            Category.Equals("NSFW", StringComparison.OrdinalIgnoreCase) ||
            RelativePath.StartsWith("NSFW", StringComparison.OrdinalIgnoreCase) ||
            Name.Contains("nsfw", StringComparison.OrdinalIgnoreCase);

        public string ArchitectureBadge
        {
            get
            {
                if (IsFlux) return "Flux";
                if (IsSdxl) return "SDXL";
                if (IsSd15) return "SD 1.5";
                if (!string.IsNullOrEmpty(Architecture)) return Architecture;
                return "LoRA";
            }
        }

        public bool IsCompatibleWith(LocalModelInfo? model)
        {
            if (model == null || !model.IsImageModel) return true;
            if (model.IsFlux) return IsFlux;
            if (model.IsSdxl) return IsSdxl || (!IsFlux && !IsSd15);
            if (model.IsSd15) return IsSd15 || (!IsFlux && !IsSdxl);
            return true;
        }

        public string IncompatibleWarning(LocalModelInfo? model)
        {
            if (model == null || !model.IsImageModel || IsCompatibleWith(model))
                return string.Empty;

            var loraArch = ArchitectureBadge;
            var modelArch = model.FamilyName;
            return $"⚠️ Architecture Mismatch: {loraArch} LoRA cannot be applied to {modelArch} model in SwarmUI/ComfyUI.";
        }

        private bool _isCompatibleWithActiveModel = true;
        public bool IsCompatibleWithActiveModel
        {
            get => _isCompatibleWithActiveModel;
            set
            {
                if (_isCompatibleWithActiveModel != value)
                {
                    _isCompatibleWithActiveModel = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _compatibilityWarningText = string.Empty;
        public string CompatibilityWarningText
        {
            get => _compatibilityWarningText;
            set
            {
                if (_compatibilityWarningText != value)
                {
                    _compatibilityWarningText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SizeFormatted
        {
            get
            {
                if (SizeBytes <= 0) return string.Empty;
                double mb = SizeBytes / (1024.0 * 1024.0);
                if (mb >= 1024.0)
                {
                    return $"{mb / 1024.0:0.1} GB";
                }
                return $"{mb:0} MB";
            }
        }

        public string FileName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Name)) return Path.GetFileName(Name);
                if (!string.IsNullOrWhiteSpace(RelativePath)) return Path.GetFileName(RelativePath);
                if (!string.IsNullOrWhiteSpace(FilePath)) return Path.GetFileName(FilePath);
                return string.Empty;
            }
        }

        public string FileNameWithoutExtension => !string.IsNullOrWhiteSpace(FileName)
            ? Path.GetFileNameWithoutExtension(FileName)
            : string.Empty;

        public string DisplayTitle
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Title)) return Title;
                if (!string.IsNullOrWhiteSpace(Name)) return Path.GetFileNameWithoutExtension(Name);
                if (!string.IsNullOrWhiteSpace(RelativePath)) return Path.GetFileNameWithoutExtension(RelativePath);
                if (!string.IsNullOrWhiteSpace(FilePath)) return Path.GetFileNameWithoutExtension(FilePath);
                return "Unknown LoRA";
            }
        }

        public bool HasDistinctFileName =>
            !string.IsNullOrWhiteSpace(FileName) &&
            !string.Equals(DisplayTitle, FileName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(DisplayTitle, FileNameWithoutExtension, StringComparison.OrdinalIgnoreCase);

        public string DisplayNameWithWeight => $"{DisplayTitle} ({StrengthFormatted})";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() => DisplayNameWithWeight;
    }
}
