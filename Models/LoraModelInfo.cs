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

        public bool IsSdxl =>
            Architecture.Contains("xl", StringComparison.OrdinalIgnoreCase) ||
            Architecture.Contains("sdxl", StringComparison.OrdinalIgnoreCase) ||
            Architecture.Contains("pony", StringComparison.OrdinalIgnoreCase) ||
            Name.Contains("xl", StringComparison.OrdinalIgnoreCase) ||
            Name.Contains("sdxl", StringComparison.OrdinalIgnoreCase) ||
            Name.Contains("pony", StringComparison.OrdinalIgnoreCase) ||
            Title.Contains("xl", StringComparison.OrdinalIgnoreCase);

        public bool IsSd15 =>
            Architecture.Contains("1.5", StringComparison.OrdinalIgnoreCase) ||
            Architecture.Contains("v1", StringComparison.OrdinalIgnoreCase) ||
            Name.Contains("1.5", StringComparison.OrdinalIgnoreCase) ||
            Name.Contains("v1-5", StringComparison.OrdinalIgnoreCase);

        public string ArchitectureBadge
        {
            get
            {
                if (IsSdxl) return "SDXL";
                if (IsSd15) return "SD 1.5";
                if (!string.IsNullOrEmpty(Architecture)) return Architecture;
                return "LoRA";
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

        public string DisplayTitle => !string.IsNullOrWhiteSpace(Title) ? Title : Path.GetFileNameWithoutExtension(Name);

        public string DisplayNameWithWeight => $"{DisplayTitle} ({StrengthFormatted})";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() => DisplayNameWithWeight;
    }
}
