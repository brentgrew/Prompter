using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Prompter.Models
{
    public class PromptFolder : INotifyPropertyChanged
    {
        private string _id;
        private string _name = string.Empty;
        private bool _isPasswordProtected;
        private string? _passwordSalt;
        private string? _passwordHash;
        private string? _encryptedPayload;
        private int _cachedPromptCount;
        private bool _isUnlocked = true;
        private bool _isSelected;

        public PromptFolder()
        {
            _id = Guid.NewGuid().ToString("N");
            Prompts = new ObservableCollection<PromptItem>();
            Prompts.CollectionChanged += OnPromptsCollectionChanged;
        }

        public PromptFolder(string name, bool isPasswordProtected = false) : this()
        {
            _name = name;
            _isPasswordProtected = isPasswordProtected;
            _isUnlocked = !isPasswordProtected;
        }

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string DisplayName => string.IsNullOrWhiteSpace(_name) ? "(Untitled Folder)" : _name;

        public bool IsPasswordProtected
        {
            get => _isPasswordProtected;
            set
            {
                if (_isPasswordProtected != value)
                {
                    _isPasswordProtected = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusIcon));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(CanAccessPrompts));
                }
            }
        }

        public string? PasswordSalt
        {
            get => _passwordSalt;
            set { _passwordSalt = value; OnPropertyChanged(); }
        }

        public string? PasswordHash
        {
            get => _passwordHash;
            set { _passwordHash = value; OnPropertyChanged(); }
        }

        public string? EncryptedPayload
        {
            get => _encryptedPayload;
            set { _encryptedPayload = value; OnPropertyChanged(); }
        }

        public int CachedPromptCount
        {
            get => _cachedPromptCount;
            set
            {
                if (_cachedPromptCount != value)
                {
                    _cachedPromptCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PromptCount));
                }
            }
        }

        public bool IsUnlocked
        {
            get => _isUnlocked;
            set
            {
                if (_isUnlocked != value)
                {
                    _isUnlocked = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusIcon));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(CanAccessPrompts));
                    OnPropertyChanged(nameof(PromptCount));
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// In-memory password used during the unlocked session to re-encrypt on save.
        /// Not serialized to disk.
        /// </summary>
        public string? SessionPassword { get; set; }

        public ObservableCollection<PromptItem> Prompts { get; }

        public bool CanAccessPrompts => !IsPasswordProtected || IsUnlocked;

        public int PromptCount => (!IsUnlocked && IsPasswordProtected) ? _cachedPromptCount : Prompts.Count;

        public string StatusIcon
        {
            get
            {
                if (!IsPasswordProtected) return "📁";
                return IsUnlocked ? "🔓" : "🔒";
            }
        }

        public string StatusText
        {
            get
            {
                if (!IsPasswordProtected) return "Normal";
                return IsUnlocked ? "Unlocked" : "Locked";
            }
        }

        private void OnPromptsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (IsUnlocked)
            {
                _cachedPromptCount = Prompts.Count;
            }
            OnPropertyChanged(nameof(PromptCount));
        }

        public void Lock()
        {
            if (!IsPasswordProtected) return;
            _cachedPromptCount = Prompts.Count;
            IsUnlocked = false;
            SessionPassword = null;
            Prompts.Clear();
            OnPropertyChanged(nameof(PromptCount));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
