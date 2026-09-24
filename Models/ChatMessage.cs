using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Prompter.Models
{
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _content = string.Empty;
        private string _thinkingContent = string.Empty;
        private bool _isGenerating;
        private bool _isThinking;
        private string? _modelName;
        private bool _isImageMessage;
        private string? _imagePath;
        private string? _imageUrl;
        private string? _imagePrompt;
        private bool _isImageLoading;
        private string? _imageDimensions;
        private int _imageSteps = 50;
        private double _imageCfg = 7.0;

        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Role { get; set; } = "User"; // "User", "Assistant", "System", "Error"
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public bool IsImageMessage
        {
            get => _isImageMessage;
            set
            {
                if (_isImageMessage != value)
                {
                    _isImageMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? ImagePath
        {
            get => _imagePath;
            set
            {
                if (_imagePath != value)
                {
                    _imagePath = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasImageFile));
                }
            }
        }

        public bool HasImageFile => !string.IsNullOrEmpty(_imagePath) && System.IO.File.Exists(_imagePath);

        public string? ImageUrl
        {
            get => _imageUrl;
            set
            {
                if (_imageUrl != value)
                {
                    _imageUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? ImagePrompt
        {
            get => _imagePrompt;
            set
            {
                if (_imagePrompt != value)
                {
                    _imagePrompt = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsImageLoading
        {
            get => _isImageLoading;
            set
            {
                if (_isImageLoading != value)
                {
                    _isImageLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? ImageDimensions
        {
            get => _imageDimensions;
            set
            {
                if (_imageDimensions != value)
                {
                    _imageDimensions = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ImageSteps
        {
            get => _imageSteps;
            set
            {
                if (_imageSteps != value)
                {
                    _imageSteps = value;
                    OnPropertyChanged();
                }
            }
        }

        public double ImageCfg
        {
            get => _imageCfg;
            set
            {
                if (_imageCfg != value)
                {
                    _imageCfg = value;
                    OnPropertyChanged();
                }
            }
        }

        private string? _lorasSummary;
        public string? LorasSummary
        {
            get => _lorasSummary;
            set
            {
                if (_lorasSummary != value)
                {
                    _lorasSummary = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasLoras));
                }
            }
        }

        public bool HasLoras => !string.IsNullOrWhiteSpace(_lorasSummary);

        private long? _imageSeed;
        public long? ImageSeed
        {
            get => _imageSeed;
            set
            {
                if (_imageSeed != value)
                {
                    _imageSeed = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSeed));
                }
            }
        }

        public bool HasSeed => _imageSeed.HasValue && _imageSeed.Value >= 0;

        public string ThinkingContent
        {
            get => _thinkingContent;
            set
            {
                if (_thinkingContent != value)
                {
                    _thinkingContent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasThinking));
                }
            }
        }

        public bool HasThinking => !string.IsNullOrWhiteSpace(_thinkingContent);

        public bool IsThinking
        {
            get => _isThinking;
            set
            {
                if (_isThinking != value)
                {
                    _isThinking = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Content
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (_isGenerating != value)
                {
                    _isGenerating = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? ModelName
        {
            get => _modelName;
            set
            {
                if (_modelName != value)
                {
                    _modelName = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isEditing;
        private string _editBuffer = string.Empty;

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    OnPropertyChanged();
                }
            }
        }

        public string EditBuffer
        {
            get => _editBuffer;
            set
            {
                if (_editBuffer != value)
                {
                    _editBuffer = value;
                    OnPropertyChanged();
                }
            }
        }

        public void BeginEdit()
        {
            EditBuffer = Content;
            IsEditing = true;
        }

        public void CancelEdit()
        {
            IsEditing = false;
            EditBuffer = string.Empty;
        }

        public void CommitEdit()
        {
            Content = EditBuffer;
            IsEditing = false;
            EditBuffer = string.Empty;
        }

        public bool IsUser => string.Equals(Role, "User", StringComparison.OrdinalIgnoreCase);
        public bool IsAssistant => string.Equals(Role, "Assistant", StringComparison.OrdinalIgnoreCase);
        public bool IsError => string.Equals(Role, "Error", StringComparison.OrdinalIgnoreCase);

        public string TimeFormatted => Timestamp.ToString("HH:mm:ss");

        public ChatMessage() { }

        public ChatMessage(string role, string content, string? modelName = null)
        {
            Role = role;
            Content = content;
            ModelName = modelName;
            Timestamp = DateTime.Now;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
