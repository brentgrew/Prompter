using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.ViewModels
{
    public class LoraManagerViewModel : INotifyPropertyChanged
    {
        private readonly SwarmUiService _swarmUiService;
        private string _searchText = string.Empty;
        private string _selectedFolder = "All Folders";
        private LoraModelInfo? _selectedAvailableLora;
        private LoraModelInfo? _selectedActiveLora;
        private bool _isLoading;
        private string _statusMessage = string.Empty;

        public ObservableCollection<LoraModelInfo> AvailableLoras { get; } = new();
        public ObservableCollection<LoraModelInfo> ActiveLoras { get; } = new();
        public ObservableCollection<string> Folders { get; } = new();

        public ICollectionView AvailableLorasView { get; }

        public RelayCommand<LoraModelInfo> AddLoraCommand { get; }
        public RelayCommand<LoraModelInfo> RemoveLoraCommand { get; }
        public RelayCommand ClearAllActiveCommand { get; }
        public RelayCommand RefreshCommand { get; }

        public LoraManagerViewModel(IEnumerable<LoraModelInfo>? currentActiveLoras = null, SwarmUiService? swarmUiService = null)
        {
            _swarmUiService = swarmUiService ?? SwarmUiService.Instance;

            AvailableLorasView = CollectionViewSource.GetDefaultView(AvailableLoras);
            AvailableLorasView.Filter = FilterAvailableLora;

            // Group by Folder for folder organization
            AvailableLorasView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LoraModelInfo.Folder)));

            // Commands
            AddLoraCommand = new RelayCommand<LoraModelInfo>(
                lora => AddLora(lora ?? SelectedAvailableLora),
                _ => (SelectedAvailableLora != null || AvailableLoras.Count > 0));

            RemoveLoraCommand = new RelayCommand<LoraModelInfo>(
                lora => RemoveLora(lora ?? SelectedActiveLora),
                _ => (SelectedActiveLora != null || ActiveLoras.Count > 0));

            ClearAllActiveCommand = new RelayCommand(ClearAllActive, () => ActiveLoras.Count > 0);
            RefreshCommand = new RelayCommand(async () => await LoadLorasAsync());

            ActiveLoras.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(ActiveCount));
                OnPropertyChanged(nameof(HasActiveLoras));
                OnPropertyChanged(nameof(ActiveCountSummary));
                ClearAllActiveCommand.RaiseCanExecuteChanged();
                RemoveLoraCommand.RaiseCanExecuteChanged();
            };

            // Copy initial active LoRAs
            if (currentActiveLoras != null)
            {
                foreach (var item in currentActiveLoras)
                {
                    ActiveLoras.Add(new LoraModelInfo
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Title = item.Title,
                        FilePath = item.FilePath,
                        RelativePath = item.RelativePath,
                        Folder = item.Folder,
                        Category = item.Category,
                        SubCategory = item.SubCategory,
                        Architecture = item.Architecture,
                        Strength = item.Strength,
                        SizeBytes = item.SizeBytes,
                        TriggerPhrase = item.TriggerPhrase,
                        PreviewImageUrl = item.PreviewImageUrl,
                        Tags = new List<string>(item.Tags)
                    });
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    AvailableLorasView.Refresh();
                    OnPropertyChanged(nameof(FilteredAvailableCount));
                }
            }
        }

        public string SelectedFolder
        {
            get => _selectedFolder;
            set
            {
                if (_selectedFolder != value)
                {
                    _selectedFolder = value;
                    OnPropertyChanged();
                    AvailableLorasView.Refresh();
                    OnPropertyChanged(nameof(FilteredAvailableCount));
                }
            }
        }

        public LoraModelInfo? SelectedAvailableLora
        {
            get => _selectedAvailableLora;
            set
            {
                if (_selectedAvailableLora != value)
                {
                    _selectedAvailableLora = value;
                    OnPropertyChanged();
                    AddLoraCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public LoraModelInfo? SelectedActiveLora
        {
            get => _selectedActiveLora;
            set
            {
                if (_selectedActiveLora != value)
                {
                    _selectedActiveLora = value;
                    OnPropertyChanged();
                    RemoveLoraCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public int TotalAvailableCount => AvailableLoras.Count;
        public int FilteredAvailableCount => AvailableLorasView.Cast<object>().Count();
        public int ActiveCount => ActiveLoras.Count;
        public bool HasActiveLoras => ActiveLoras.Count > 0;
        public string ActiveCountSummary => $"{ActiveLoras.Count} Active LoRA{(ActiveLoras.Count == 1 ? "" : "s")}";

        public async Task LoadLorasAsync()
        {
            IsLoading = true;
            StatusMessage = "Scanning LoRA models from SwarmUI...";
            try
            {
                var loras = await _swarmUiService.ListLorasAsync();

                AvailableLoras.Clear();
                foreach (var lora in loras)
                {
                    AvailableLoras.Add(lora);
                }

                // Populate Folders list
                var folderSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in loras)
                {
                    if (!string.IsNullOrEmpty(l.Folder) && !l.Folder.Equals("Root", StringComparison.OrdinalIgnoreCase))
                    {
                        folderSet.Add(l.Folder);
                    }
                }

                Folders.Clear();
                Folders.Add("All Folders");
                foreach (var f in folderSet.OrderBy(x => x))
                {
                    Folders.Add(f);
                }

                SelectedFolder = "All Folders";
                AvailableLorasView.Refresh();

                StatusMessage = $"Found {AvailableLoras.Count} LoRA models across {folderSet.Count} folders.";
                OnPropertyChanged(nameof(TotalAvailableCount));
                OnPropertyChanged(nameof(FilteredAvailableCount));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load LoRAs: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void AddLora(LoraModelInfo? lora)
        {
            if (lora == null) return;

            // Check if already in ActiveLoras
            var existing = ActiveLoras.FirstOrDefault(l =>
                l.RelativePath.Equals(lora.RelativePath, StringComparison.OrdinalIgnoreCase) ||
                l.Id.Equals(lora.Id, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                SelectedActiveLora = existing;
                return;
            }

            var activeCopy = new LoraModelInfo
            {
                Id = lora.Id,
                Name = lora.Name,
                Title = lora.Title,
                FilePath = lora.FilePath,
                RelativePath = lora.RelativePath,
                Folder = lora.Folder,
                Category = lora.Category,
                SubCategory = lora.SubCategory,
                Architecture = lora.Architecture,
                Strength = 1.0, // Default 1.0 as requested
                SizeBytes = lora.SizeBytes,
                TriggerPhrase = lora.TriggerPhrase,
                PreviewImageUrl = lora.PreviewImageUrl,
                Tags = new List<string>(lora.Tags)
            };

            ActiveLoras.Add(activeCopy);
            SelectedActiveLora = activeCopy;
        }

        public void RemoveLora(LoraModelInfo? lora)
        {
            if (lora == null) return;
            ActiveLoras.Remove(lora);
            if (SelectedActiveLora == lora)
            {
                SelectedActiveLora = ActiveLoras.LastOrDefault();
            }
        }

        public void ClearAllActive()
        {
            ActiveLoras.Clear();
            SelectedActiveLora = null;
        }

        private bool FilterAvailableLora(object obj)
        {
            if (obj is not LoraModelInfo lora) return false;

            // Folder filter
            if (!string.IsNullOrEmpty(SelectedFolder) &&
                !SelectedFolder.Equals("All Folders", StringComparison.OrdinalIgnoreCase))
            {
                if (!lora.Folder.Equals(SelectedFolder, StringComparison.OrdinalIgnoreCase) &&
                    !lora.Folder.StartsWith(SelectedFolder + "/", StringComparison.OrdinalIgnoreCase) &&
                    !lora.Category.Equals(SelectedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // Search query filter
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var query = SearchText.Trim();
                var nameMatch = lora.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
                var titleMatch = lora.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
                var folderMatch = lora.Folder.Contains(query, StringComparison.OrdinalIgnoreCase);
                var triggerMatch = lora.TriggerPhrase != null && lora.TriggerPhrase.Contains(query, StringComparison.OrdinalIgnoreCase);
                var archMatch = lora.Architecture.Contains(query, StringComparison.OrdinalIgnoreCase);

                if (!nameMatch && !titleMatch && !folderMatch && !triggerMatch && !archMatch)
                {
                    return false;
                }
            }

            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
