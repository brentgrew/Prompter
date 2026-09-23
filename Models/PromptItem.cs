using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Prompter.Models
{
    public class PromptItem : INotifyPropertyChanged
    {
        private string _id;
        private string _title = string.Empty;
        private string _content = string.Empty;
        private DateTime _createdAt;
        private DateTime _updatedAt;

        public PromptItem()
        {
            _id = Guid.NewGuid().ToString("N");
            _createdAt = DateTime.Now;
            _updatedAt = DateTime.Now;
        }

        public PromptItem(string title, string content) : this()
        {
            _title = title;
            _content = content;
        }

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayTitle));
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
                    OnPropertyChanged(nameof(PreviewSnippet));
                    OnPropertyChanged(nameof(WordCount));
                    OnPropertyChanged(nameof(CharacterCount));
                }
            }
        }

        public DateTime CreatedAt
        {
            get => _createdAt;
            set { _createdAt = value; OnPropertyChanged(); }
        }

        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set { _updatedAt = value; OnPropertyChanged(); }
        }

        public string DisplayTitle => string.IsNullOrWhiteSpace(_title) ? "(Untitled Prompt)" : _title;

        public string PreviewSnippet
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_content)) return "(No content)";
                var singleLine = _content.Replace("\r", " ").Replace("\n", " ").Trim();
                return singleLine.Length > 80 ? singleLine[..77] + "..." : singleLine;
            }
        }

        public int CharacterCount => _content?.Length ?? 0;

        public int WordCount
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_content)) return 0;
                return _content.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
